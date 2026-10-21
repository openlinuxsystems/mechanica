using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting.Bots;

/// <summary>Finds the script of a bot type.</summary>
public interface IBotScriptSource
{
    /// <summary>The script of <paramref name="bot"/>, or null when it has none (or it can't be resolved).</summary>
    ScriptFile? FindScript(Bot bot);
}

/// <summary>Resolves bot scripts against loaded mods (D6).</summary>
public sealed class ModBotScriptSource(ModLoadResult mods) : IBotScriptSource
{
    /// <inheritdoc/>
    public ScriptFile? FindScript(Bot bot)
    {
        if (bot.Script is not { } path || !AssetPath.TryParse(path, bot.ModId, AssetKind.Script, out var asset, out _))
            return null;
        var mod = mods.Mods.FirstOrDefault(m => m.Id == asset.ModId);
        return mod is null ? null : new ScriptFile(asset.ToString(), asset.ModId, asset.Resolve(mod.Directory));
    }
}

/// <summary>One running bot brain, as the editor and tests see it.</summary>
/// <param name="Bot">Bot id.</param>
/// <param name="Type">Qualified bot type.</param>
/// <param name="Script">Qualified script path, or null for a bot that only runs its tree.</param>
/// <param name="Fault">Why it is stopped, or null while it runs.</param>
/// <param name="Status">What its tree returned on its last think.</param>
public sealed record BotBrainInfo(int Bot, string Type, string? Script, string? Fault, string? Status);

/// <summary>
/// Runs bot behaviour on the host (chunk 21, D2: behaviour lives in Lua). Every living bot gets its own Lua
/// environment in its mod's state, with a persistent <c>state</c> table, its type's <c>properties</c> and
/// behaviour <c>tree</c>, and the built-in <c>behavior</c> library. Each bot thinks every
/// <see cref="BotSettings.ThinkInterval"/>: it calls <c>on_think(dt)</c>, which by default runs its tree.
/// Other hooks: <c>on_spawn()</c>, <c>on_damaged(amount)</c>, <c>on_death()</c>.
/// <para>
/// Bots change the world only through the Action API, as <see cref="ActionSource.Bot"/> callers: the same
/// validated <c>MoveBot</c> and <c>BotAttack</c> an external agent would use. A bot whose script fails
/// stops (and stands still) until its script is fixed and reloaded.
/// </para>
/// </summary>
public sealed partial class BotRuntime
{
    private static readonly ActionCaller Caller = new(Creation.HostPeer, ActionSource.Bot);

    private readonly ScriptSettings _settings;
    private readonly ActionDispatcher _dispatcher;
    private readonly BotActions _bots;
    private readonly RaidDirector? _raids;
    private readonly IBotWorld _world;
    private readonly Dictionary<string, (LuaContext Context, DynValue Library)> _contexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileEntry> _files = new(StringComparer.Ordinal);
    private readonly SortedDictionary<int, Brain> _brains = [];
    private readonly Queue<ScriptMessage> _messages = new();
    private IBotScriptSource _source;
    private Brain? _current;

    private sealed class FileEntry(ScriptFile file)
    {
        public ScriptFile File { get; } = file;
        public DateTime Stamp { get; set; }
        public DynValue? Factory { get; set; }
        public string? Error { get; set; }
    }

    private sealed class Brain(BotState bot, LuaContext context, FileEntry? file)
    {
        public BotState Bot { get; } = bot;
        public LuaContext Context { get; } = context;
        public FileEntry? File { get; set; } = file;
        public Table? Env { get; set; }
        public Table? State { get; set; }
        public Table? Memory { get; set; }
        public DynValue Tree { get; set; } = DynValue.Nil;
        public string? Fault { get; set; }
        public string? Status { get; set; }
        public float Since { get; set; }
        public float Health { get; set; } = bot.Health;
        public bool Mourned { get; set; }
        public Random Random { get; } = new(bot.Id * 7919 + 17);
        public HashSet<string> Warned { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Creates a runtime over the host's bots.</summary>
    /// <param name="settings">Lua limits.</param>
    /// <param name="dispatcher">The host's Action API, which every bot state change goes through.</param>
    /// <param name="bots">The bots.</param>
    /// <param name="raids">The raid director, for <c>bot.raid()</c>; null when raids are off.</param>
    /// <param name="world">Line-of-sight rays.</param>
    /// <param name="source">Finds each bot type's script.</param>
    public BotRuntime(ScriptSettings settings, ActionDispatcher dispatcher, BotActions bots, RaidDirector? raids, IBotWorld world, IBotScriptSource source)
    {
        settings.Validate();
        _settings = settings;
        _dispatcher = dispatcher;
        _bots = bots;
        _raids = raids;
        _world = world;
        _source = source;
    }

    /// <summary>Raised for every print, warning and error, as it happens.</summary>
    public event Action<ScriptMessage>? Message;

    /// <summary>A bot noticed a new target (its <c>_alert</c> sound plays).</summary>
    public event Action<int>? Alerted;

    /// <summary>A bot's script played a sound event (<c>bot.sound(id)</c>; a bare id is the bot's mod's, else vanilla's).</summary>
    public event Action<int, string>? Sounded;

    /// <summary>Recent messages, oldest first.</summary>
    public IReadOnlyList<ScriptMessage> Messages => [.. _messages];

    /// <summary>Seconds simulated so far.</summary>
    public double Time { get; private set; }

    /// <summary>Every bot brain, by bot id.</summary>
    public IReadOnlyList<BotBrainInfo> Brains =>
        [.. _brains.Values.Select(b => new BotBrainInfo(b.Bot.Id, b.Bot.Type.QualifiedId, b.File?.File.Key, b.Fault, b.Status))];

    /// <summary>Where bot types' scripts come from. Setting it restarts every bot on its new script.</summary>
    public IBotScriptSource Source
    {
        get => _source;
        set
        {
            _source = value;
            Clear();
        }
    }

    /// <summary>
    /// One physics step: starts brains for new bots, calls <c>on_damaged</c> and <c>on_death</c> where
    /// health changed, and lets every bot whose think is due call <c>on_think</c>. Bots think in id order,
    /// spread over the think interval.
    /// </summary>
    public void Update(float dt)
    {
        Time += dt;
        var interval = _bots.Settings.ThinkInterval;
        foreach (var id in _brains.Keys.Where(id => _bots.Find(id) is null).ToList())
            _brains.Remove(id);
        foreach (var bot in _bots.Bots.Values.ToList())
        {
            if (!_brains.TryGetValue(bot.Id, out var brain))
            {
                brain = _brains[bot.Id] = new Brain(bot, ContextFor(bot.Type.ModId), FileFor(bot.Type));
                // Spread first thinks over the interval so a raid doesn't think all at once.
                brain.Since = interval * (bot.Id % 8) / 8f;
                Start(brain, fresh: true);
            }
            if (bot.Health < brain.Health && brain.Fault is null && !bot.Dead)
                CallHook(brain, "on_damaged", DynValue.NewNumber(brain.Health - bot.Health));
            brain.Health = bot.Health;
            if (bot.Dead)
            {
                if (!brain.Mourned)
                {
                    brain.Mourned = true;
                    CallHook(brain, "on_death");
                }
                continue;
            }
            brain.Since += dt;
            if (brain.Since < interval || brain.Fault is not null)
                continue;
            var since = brain.Since;
            brain.Since = 0;
            Think(brain, since);
        }
    }

    /// <summary>Reloads every bot script whose file changed on disk; bots keep their <c>state</c>.</summary>
    public void CheckForChanges()
    {
        foreach (var entry in _files.Values)
        {
            if (StampOf(entry.File.FullPath) == entry.Stamp)
                continue;
            Compile(entry);
            foreach (var brain in _brains.Values.Where(b => b.File == entry))
                Start(brain, fresh: false);
        }
    }

    /// <summary>Forgets every brain and script; living bots start fresh on the next update.</summary>
    public void Clear()
    {
        _brains.Clear();
        _files.Clear();
        _contexts.Clear();
    }

    private void Think(Brain brain, float dt)
    {
        if (brain.Bot.LedBy is not null)
        {
            Follow(brain);
            return;
        }
        var env = brain.Env!;
        var think = env.RawGet("on_think") ?? DynValue.Nil;
        if (think.Type == DataType.Function)
            brain.Status = Describe(Run(brain, think, "on_think", DynValue.NewNumber(dt)));
        else
            brain.Status = Describe(Run(brain, brain.Context.Script.Globals.Get("behavior").Table.Get("run"), "the behaviour tree", brain.Tree, DynValue.NewNumber(dt)));
    }

    // On a Lead it walks after its player, faster the more the rope pulls, and stands facing them within its slack.
    private void Follow(Brain brain)
    {
        brain.Status = "led";
        if (_bots.LeaderFeet(brain.Bot) is not { } feet)
            return;
        var away = Flat(feet - brain.Bot.Position);
        var slack = _bots.Settings.LeadLength;
        var distance = away.Length();
        if (distance <= slack)
        {
            Move(brain, away.LengthSquared() < 1e-6f ? Vector3.Zero : away, 0f);
            return;
        }
        var pull = Math.Clamp((distance - slack) / 2f, 0.35f, 1f);
        Move(brain, away, brain.Bot.TopSpeed * pull);
    }

    private static string? Describe(DynValue[]? values) =>
        values is { Length: > 0 } && values[0].Type == DataType.String ? values[0].String : null;

    private (LuaContext Context, DynValue Library) ContextEntry(string modId)
    {
        if (_contexts.TryGetValue(modId, out var entry))
            return entry;
        var context = new LuaContext(modId, _settings, Print);
        Bind(context);
        var library = context.Compile(BehaviorSource.Value, "=behavior");
        var outcome = context.Call(library, DynValue.NewTable(context.Script.Globals));
        if (outcome.Status != CallStatus.Ok)
            throw new InvalidOperationException($"The behaviour library failed to load: {outcome.Error}");
        return _contexts[modId] = (context, library);
    }

    private LuaContext ContextFor(string modId) => ContextEntry(modId).Context;

    private FileEntry? FileFor(Bot type)
    {
        if (_source.FindScript(type) is not { } file)
            return null;
        if (_files.TryGetValue(file.Key, out var entry))
            return entry;
        entry = _files[file.Key] = new FileEntry(file);
        Compile(entry);
        return entry;
    }

    private void Compile(FileEntry entry)
    {
        entry.Stamp = StampOf(entry.File.FullPath);
        try
        {
            entry.Factory = ContextFor(entry.File.ModId).Compile(File.ReadAllText(entry.File.FullPath), entry.File.Key);
            entry.Error = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            entry.Error = $"cannot read {entry.File.Key}: {e.Message}";
        }
        catch (InterpreterException e)
        {
            entry.Error = e.DecoratedMessage ?? e.Message;
        }
        if (entry.Error is { } error)
            Post(ScriptMessageKind.Error, entry.File.Key, null, error);
    }

    // A fresh environment (keeping state on a reload), the script's top level, then on_spawn the first time.
    private void Start(Brain brain, bool fresh)
    {
        var context = brain.Context;
        var script = context.Script;
        var env = context.NewEnvironment();
        brain.State ??= new Table(script);
        brain.Memory = new Table(script);
        env.Set("state", DynValue.NewTable(brain.State));
        env.Set("properties", LuaValues.FromJson(script, brain.Bot.Type.Definition.Properties) is { Type: DataType.Table } p ? p : DynValue.NewTable(new Table(script)));
        brain.Tree = LuaValues.FromJson(script, brain.Bot.Type.Behavior);
        env.Set("tree", brain.Tree);
        brain.Env = env;
        brain.Fault = null;
        brain.Warned.Clear();
        if (brain.File is { } file)
        {
            if (file.Factory is null)
            {
                Stop(brain, file.Error ?? "the script did not load");
                return;
            }
            if (Run(brain, file.Factory, "loading", DynValue.NewTable(env)) is null)
                return;
        }
        if (fresh)
            CallHook(brain, "on_spawn");
        else
            CallHook(brain, "on_reload");
    }

    private void CallHook(Brain brain, string hook, params DynValue[] args)
    {
        if (brain.Env is not { } env || brain.Fault is not null)
            return;
        var function = env.RawGet(hook);
        if (function is null || function.IsNil())
            return;
        if (function.Type != DataType.Function)
        {
            Stop(brain, $"{hook} is a {function.Type.ToLuaTypeString()}, not a function");
            return;
        }
        Run(brain, function, hook, args);
    }

    private DynValue[]? Run(Brain brain, DynValue function, string what, params DynValue[] args)
    {
        var previous = _current;
        _current = brain;
        CallOutcome outcome;
        try
        {
            outcome = brain.Context.Call(function, args);
        }
        finally
        {
            _current = previous;
        }
        if (outcome.Status == CallStatus.Ok)
            return outcome.Values;
        Stop(brain, $"{outcome.Error} (in {what})");
        return null;
    }

    private void Stop(Brain brain, string fault)
    {
        brain.Fault = fault;
        if (!brain.Bot.Dead && _bots.Find(brain.Bot.Id) is not null)
            _dispatcher.Dispatch(Caller, BotActions.MoveBot, new MoveBotArgs(brain.Bot.Id));
        Post(ScriptMessageKind.Error, brain.File?.File.Key ?? brain.Bot.Type.QualifiedId, brain.Bot.Id,
            $"{fault}; the bot stands still until its script is reloaded");
    }

    private void Post(ScriptMessageKind kind, string script, int? bot, string text)
    {
        var message = new ScriptMessage(kind, script, null, null, bot is { } id ? $"bot {id}: {text}" : text, (long)(Time * 1000));
        _messages.Enqueue(message);
        while (_messages.Count > _settings.MaxMessages)
            _messages.Dequeue();
        Message?.Invoke(message);
    }

    private void Print(string text)
    {
        if (_current is { } brain)
            Post(ScriptMessageKind.Print, brain.File?.File.Key ?? brain.Bot.Type.QualifiedId, brain.Bot.Id, text);
        else
            Post(ScriptMessageKind.Print, "?", null, text);
    }

    private static DateTime StampOf(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    private static class BehaviorSource
    {
        public static readonly string Value = Load();

        private static string Load()
        {
            using var stream = typeof(BotRuntime).Assembly.GetManifestResourceStream("Mechanica.Scripting.Bots.behavior.lua")
                ?? throw new InvalidOperationException("The behaviour library is missing from the assembly.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
}
