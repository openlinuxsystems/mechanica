using System.Numerics;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Motion;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

/// <summary>
/// Runs part behaviour scripts on the host (D2: all part behaviour lives in Lua). Every part whose
/// definition names a <c>script</c> gets an instance: its own Lua environment, a persistent
/// <c>state</c> table, and the hooks the script defines (<see cref="Hooks"/>). The runtime keeps the
/// instances in step with the Creations every <see cref="Update"/>, calls <c>on_update</c> on the live
/// ones, and reloads a script in place when its file changes (<see cref="CheckForChanges"/>), keeping
/// each part's <c>state</c>.
/// <para>
/// Scripts change the world only through the Action API (<see cref="ActionDispatcher"/>, as
/// <see cref="ActionSource.Script"/> callers on the host), and each script can only move its own
/// Creation. Reads (part position and velocity, raycasts) go straight to the world. A script that raises
/// an error or runs past the instruction budget stops; its part waits for a fixed reload.
/// </para>
/// </summary>
public sealed partial class ScriptRuntime
{
    private static readonly ActionCaller Caller = new(Creation.HostPeer, ActionSource.Script);

    private readonly ScriptSettings _settings;
    private readonly ActionDispatcher _dispatcher;
    private readonly IMotionWorld _motion;
    private readonly IScriptPhysics _physics;
    private readonly Func<IEnumerable<Creation>> _creations;
    private readonly Dictionary<string, LuaContext> _contexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileEntry> _files = new(StringComparer.Ordinal);
    private readonly SortedDictionary<(int Creation, int Part), Instance> _instances = [];
    // The step's instances, copied because a hook can add or remove parts; reused so a step allocates no list.
    private readonly List<Instance> _stepping = [];
    private readonly Dictionary<int, int> _revisions = [];
    private readonly Dictionary<string, ScriptFile?> _scriptOfType = new(StringComparer.Ordinal);
    private readonly Queue<ScriptMessage> _messages = new();
    private IScriptSource _source;
    private Instance? _current;
    private double _time;

    private sealed class FileEntry(ScriptFile file, LuaContext context)
    {
        public ScriptFile File { get; } = file;
        public LuaContext Context { get; } = context;
        public DateTime Stamp { get; set; }
        public DynValue? Factory { get; set; }
        public string? Error { get; set; }
        public int Version { get; set; }
    }

    private sealed class Instance(int creation, int part, string partType, FileEntry file)
    {
        public int Creation { get; } = creation;
        public int Part { get; } = part;
        public string PartType { get; } = partType;
        public FileEntry File { get; set; } = file;
        public Table? Env { get; set; }
        public Table? State { get; set; }
        public bool Created { get; set; }
        public string? Fault { get; set; }
        public int Version { get; set; }
        // The part's upgrade level its `properties` were read at.
        public int Level { get; set; }
        public HashSet<string> Warned { get; } = new(StringComparer.Ordinal);

        // Sound loops the part started; they end with the part or when its script stops.
        public HashSet<int> Loops { get; } = [];

        // The part's sounds as it started, for part.sound once the part is gone (it broke and hears on_damage).
        public IReadOnlyDictionary<string, string>? Sounds { get; set; }

        // Held items (chunk 20b): the peer holding the item, the slot it was last used from and who acts.
        public int? Holder { get; init; }
        public int Slot { get; set; }
        public ActionCaller Caller { get; set; }
        public bool IsItem => Holder is not null;

        // A mod script (mod.json "scripts"): no part, the whole world. PartType holds its qualified path.
        public bool IsWorld { get; init; }

        // Event topics the script listens to (events.listen).
        public HashSet<string> Topics { get; } = new(StringComparer.Ordinal);

        // Timers the script started (timer.after, timer.every); a fresh start or a stop drops them.
        public List<Timer> Timers { get; set; } = [];
        public int LastTimer { get; set; }

        // Placed parts: not items or mod scripts.
        public bool IsPart => !IsItem && !IsWorld;

        // Where the part is this step; bodies only move between steps.
        public (long Tick, int Revision, PartKinematics State)? Kinematics { get; set; }

        // This step's summed push from the part (ApplyForce replaces a part's push, so calls add up here).
        public long PushTick { get; set; } = -1;
        public Vector3 PushForce { get; set; }
        public Vector3 PushTorque { get; set; }
        public Vector3? PushAt { get; set; }
    }

    /// <summary>Hook names the host calls.</summary>
    public static class Hooks
    {
        /// <summary><c>on_create()</c>: once, when the part's script first runs.</summary>
        public const string OnCreate = "on_create";

        /// <summary><c>on_update(dt)</c>: every physics step while the Creation is live.</summary>
        public const string OnUpdate = "on_update";

        /// <summary><c>on_reload()</c>: after a hot reload, instead of <c>on_create</c>.</summary>
        public const string OnReload = "on_reload";

        /// <summary><c>on_destroy()</c>: when the part is removed or its Creation goes away.</summary>
        public const string OnDestroy = "on_destroy";

        /// <summary><c>on_activate(peer)</c>: a player used the part.</summary>
        public const string OnActivate = "on_activate";

        /// <summary><c>on_release(peer)</c>: a player let go of a part they held (a button).</summary>
        public const string OnRelease = "on_release";

        /// <summary><c>can_activate(peer)</c>: may the player use the part now? Nil to allow, or the reason it can't. Changes nothing.</summary>
        public const string CanActivate = "can_activate";

        /// <summary><c>on_power(amount)</c>: the part's power or logic input changed.</summary>
        public const string OnPower = "on_power";

        /// <summary><c>on_collision(other, impulse)</c>: the part's Body hit something.</summary>
        public const string OnCollision = "on_collision";

        /// <summary><c>get_outputs()</c>: the part's output on the logic bus, once per step (instead of <c>part.set_output</c>).</summary>
        public const string GetOutputs = "get_outputs";

        /// <summary><c>on_enter(peer)</c>: a player sat down in the part (a seat).</summary>
        public const string OnEnter = "on_enter";

        /// <summary><c>on_leave(peer)</c>: the player sitting in the part stood up.</summary>
        public const string OnLeave = "on_leave";

        /// <summary><c>on_command(name, args, peer)</c>: a player's menu sent the part a command (a sequence preview).</summary>
        public const string OnCommand = "on_command";

        /// <summary><c>on_equip()</c>: a player took the item in hand.</summary>
        public const string OnEquip = "on_equip";

        /// <summary><c>on_unequip()</c>: the item left the player's hand (another item, another tool, or death).</summary>
        public const string OnUnequip = "on_unequip";

        /// <summary><c>on_aim(on)</c>: the player holding the item started (true) or stopped (false) aiming with right click.</summary>
        public const string OnAim = "on_aim";

        /// <summary><c>can_use(target)</c>: may the item be used now? Nil to allow, or the reason it can't. Changes nothing.</summary>
        public const string CanUse = "can_use";

        /// <summary><c>on_use(target)</c>: the player used the item; what it returns is the action's value.</summary>
        public const string OnUse = "on_use";

        /// <summary><c>on_use_release()</c>: the player let go of Use after a held use (<c>UseItem</c> with <c>hold</c>).</summary>
        public const string OnUseRelease = "on_use_release";

        /// <summary><c>on_hit(landing)</c>: a projectile the script launched hit something or ran out of time.</summary>
        public const string OnHit = "on_hit";

        /// <summary><c>can_reload_item()</c>: may the item be reloaded now? Nil to allow, or the reason it can't. Changes nothing.</summary>
        public const string CanReloadItem = "can_reload_item";

        /// <summary><c>on_reload_item()</c>: the player pressed Reload (R) with the item in hand.</summary>
        public const string OnReloadItem = "on_reload_item";

        /// <summary><c>on_event(topic, data)</c>: an event the script listens to (<c>events.listen</c>) happened. Mod and part scripts.</summary>
        public const string OnEvent = "on_event";

        /// <summary>A placed part took damage: <c>on_damage(amount, kind, broken, at)</c> (see <c>docs/lua-api.md</c>).</summary>
        public const string OnDamage = "on_damage";

        /// <summary>
        /// <c>on_collect()</c>: a machine (a Vacuum Pump) collects this ripe crop. Harvest, clear the soil and return the
        /// items as <c>{ {item = id, count = n}, ... }</c>, or nil when there is nothing to take.
        /// </summary>
        public const string OnCollect = "on_collect";
    }

    /// <summary>Creates a runtime over the host's world.</summary>
    /// <param name="settings">Limits.</param>
    /// <param name="dispatcher">The host's Action API, which every script state change goes through.</param>
    /// <param name="motion">The Creations, with poses current (the host's motion world).</param>
    /// <param name="physics">Physics queries.</param>
    /// <param name="source">Finds each part type's script.</param>
    /// <param name="creations">
    /// Every Creation, to keep the instances in step each update. Null uses
    /// <see cref="IMotionWorld.AllCreations"/>; a host whose motion world writes poses back on every read
    /// passes a cheaper list (only the part being scripted needs current poses).
    /// </param>
    public ScriptRuntime(ScriptSettings settings, ActionDispatcher dispatcher, IMotionWorld motion, IScriptPhysics physics, IScriptSource source,
        Func<IEnumerable<Creation>>? creations = null)
    {
        _creations = creations ?? (() => motion.AllCreations);
        settings.Validate();
        _settings = settings;
        _dispatcher = dispatcher;
        _motion = motion;
        _physics = physics;
        _source = source;
    }

    /// <summary>Raised for every print, warning and error, as it happens.</summary>
    public event Action<ScriptMessage>? Message;

    /// <summary>The sound primitives behind the <c>sound</c> table. Set before parts start.</summary>
    public IScriptSound Sound { get; set; } = new NoSound();

    /// <summary>Recent messages, oldest first (at most <see cref="ScriptSettings.MaxMessages"/>).</summary>
    public IReadOnlyList<ScriptMessage> Messages => [.. _messages];

    /// <summary>Steps run so far.</summary>
    public long Tick { get; private set; }

    /// <summary>
    /// Seconds simulated so far (the sum of every step's dt): the scripts' <c>world.time()</c>. A loaded world sets
    /// it to the time it was saved at (<c>SaveGame.Time</c>), so the clock carries on across saves and times
    /// scripts stored (the soil's last growth) stay meaningful.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative or non-finite value.</exception>
    public double Time
    {
        get => _time;
        set => _time = double.IsFinite(value) && value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "time must be 0 or more seconds.");
    }

    /// <summary>Every part script, by creation then part.</summary>
    public IReadOnlyList<ScriptInstanceInfo> Instances =>
        [.. _instances.Values.Select(i => new ScriptInstanceInfo(i.Creation, i.Part, i.PartType, i.File.File.Key, i.Fault, i.Version))];

    /// <summary>
    /// Fills <paramref name="into"/> with the running part scripts that define <paramref name="hook"/> as a function
    /// now, by creation then part (the host only reports collisions for parts that listen for them).
    /// </summary>
    public void Defining(string hook, List<(int Creation, int Part)> into)
    {
        into.Clear();
        foreach (var instance in _instances.Values)
        {
            if (instance.Fault is null && instance.Env?.RawGet(hook) is { Type: DataType.Function })
                into.Add((instance.Creation, instance.Part));
        }
    }

    /// <summary>Qualified paths of the scripts in use.</summary>
    public IReadOnlyList<string> Scripts => [.. _files.Keys.Order(StringComparer.Ordinal)];

    /// <summary>
    /// Where part types' scripts come from. Setting it (after the mods reload) re-checks every part:
    /// a part whose script changed restarts on the new one.
    /// </summary>
    public IScriptSource Source
    {
        get => _source;
        set
        {
            _source = value;
            _scriptOfType.Clear();
            _revisions.Clear();
            _worldScripts = null;
        }
    }

    /// <summary>
    /// One physics step: brings the instances in line with the Creations (creating and destroying as
    /// parts come and go) and the mod scripts in line with the mods, hands out the events scripts listen to
    /// (<c>on_event</c>), hands every part whose logic input changed its <c>on_power(amount)</c>, then
    /// calls <c>on_update(dt)</c> on every mod script in load order and every running part of a live Creation, in
    /// creation and part order, and last collects the outputs of parts that define <c>get_outputs()</c>. An output set this step reaches the
    /// parts it is linked to next step (<see cref="SignalBus"/>).
    /// </summary>
    public void Update(float dt)
    {
        Tick++;
        _time += dt;
        ReconcileWorld();
        Reconcile();
        ReconcileItems();
        DeliverEvents();
        DeliverSignals();
        DynValue[] arg = [DynValue.NewNumber(dt)];
        _stepping.Clear();
        _stepping.AddRange(_worlds);
        foreach (var instance in _stepping)
        {
            FireTimers(instance, dt);
            if (instance.Fault is null)
                CallHook(instance, Hooks.OnUpdate, arg);
        }
        _stepping.Clear();
        _stepping.AddRange(_instances.Values);
        foreach (var instance in _stepping)
        {
            if (instance.Fault is not null || !_motion.IsLive(instance.Creation))
                continue;
            FireTimers(instance, dt);
            if (instance.Fault is null)
                CallHook(instance, Hooks.OnUpdate, arg);
        }
        UpdateHeldItems(arg[0]);
        CollectOutputs();
        _stepping.Clear();
    }

    /// <summary>
    /// Calls a hook of one part (<c>on_activate</c>, <c>on_power</c>, <c>get_outputs</c>, …). Arguments may be
    /// null, booleans, numbers, strings, <see cref="Vector3"/> (a <c>vec3</c>) or JSON (tables).
    /// A part without that hook answers OK with no values.
    /// </summary>
    public ScriptCallResult Invoke(int creation, int part, string hook, params object?[] args)
    {
        Reconcile();
        if (!_instances.TryGetValue((creation, part), out var instance))
            return ScriptCallResult.Fail($"Part {part} of creation {creation} runs no script.");
        if (instance.Fault is { } fault)
            return ScriptCallResult.Fail($"The script is stopped: {fault}");
        var context = instance.File.Context;
        var values = CallHook(instance, hook, [.. args.Select(a => ToLua(context, a))]);
        if (values is null)
            return ScriptCallResult.Fail(instance.Fault ?? "The hook failed.");
        var plain = new List<JsonNode?>();
        foreach (var value in values)
        {
            try
            {
                plain.Add(LuaValues.ToJson(value, "result"));
            }
            catch (ArgumentException)
            {
                plain.Add(null);
            }
        }
        return new ScriptCallResult(true, plain, null);
    }

    /// <summary>
    /// Reloads a script from its file. When it compiles, every part running it gets a fresh environment
    /// with its old <c>state</c> table, runs the new top level, and then <c>on_reload()</c> (or
    /// <c>on_create()</c> if it never started); parts stopped by an error start again. When it doesn't
    /// compile, the old code keeps running and the error is reported.
    /// </summary>
    public ScriptReloadResult Reload(string script)
    {
        if (!_files.TryGetValue(script, out var entry))
            return new ScriptReloadResult(false, script, $"No part uses the script '{script}'.", 0);
        var users = _worlds.Concat(_instances.Values).Concat(_items.Values).Where(i => i.File == entry).ToList();
        if (!Compile(entry, keepOld: entry.Factory is not null))
            return new ScriptReloadResult(false, script, entry.Error, users.Count);
        foreach (var instance in users)
            Start(instance);
        return new ScriptReloadResult(true, script, null, users.Count);
    }

    /// <summary>Reloads every script whose file changed on disk since it was loaded. Returns what was reloaded.</summary>
    public IReadOnlyList<ScriptReloadResult> CheckForChanges()
    {
        var results = new List<ScriptReloadResult>();
        foreach (var entry in _files.Values.ToList())
        {
            if (StampOf(entry.File.FullPath) != entry.Stamp)
                results.Add(Reload(entry.File.Key));
        }
        return results;
    }

    /// <summary>A part's <c>state</c> table as JSON (for saves), or null when the part runs no script.</summary>
    /// <exception cref="ArgumentException">The state holds something other than plain data.</exception>
    public JsonNode? ExportState(int creation, int part) =>
        _instances.TryGetValue((creation, part), out var instance) && instance.State is { } state
            ? LuaValues.ToJson(DynValue.NewTable(state)) ?? new JsonObject()
            : null;

    /// <summary>Replaces a part's <c>state</c> table with JSON data. False when the part runs no script.</summary>
    public bool ImportState(int creation, int part, JsonObject state)
    {
        Reconcile();
        if (!_instances.TryGetValue((creation, part), out var instance) || instance.Env is null)
            return false;
        instance.State = LuaValues.FromJson(instance.File.Context.Script, state).Table;
        instance.Env.Set("state", DynValue.NewTable(instance.State));
        return true;
    }

    /// <summary>Destroys every instance and mod script (calling <c>on_destroy</c>) and forgets every script.</summary>
    public void Clear()
    {
        ClearWorld();
        foreach (var instance in _instances.Values.ToList())
            Destroy(instance);
        ClearItems();
        Signals.Clear();
        Lights.Clear();
        Readouts.Clear();
        Animations.Clear();
        _revisions.Clear();
        _files.Clear();
        _contexts.Clear();
        _scriptOfType.Clear();
    }

    private void Reconcile()
    {
        DeliverDamage();
        var present = new HashSet<int>();
        foreach (var creation in _creations())
        {
            present.Add(creation.Id);
            if (_revisions.TryGetValue(creation.Id, out var revision) && revision == creation.Revision)
                continue;
            _revisions[creation.Id] = creation.Revision;
            // Parts shown by another part's script (creation.display) have no instance of their own.
            Readouts.Prune(creation.Id, id => creation.FindPart(id) is not null);
            var wanted = new Dictionary<int, (string Type, ScriptFile File)>();
            foreach (var part in creation.Parts)
            {
                if (ScriptOf(part.Part) is { } file)
                    wanted[part.Id] = (part.Part, file);
            }
            foreach (var instance in InstancesOf(creation.Id))
            {
                if (!wanted.TryGetValue(instance.Part, out var want) || want.Type != instance.PartType || want.File.Key != instance.File.File.Key)
                    Destroy(instance);
                else if (instance.Env is { } env && creation.LevelOf(instance.Part) != instance.Level)
                    env.Set("properties", Properties(instance, creation));   // upgraded in place: its new level's values
            }
            foreach (var (part, want) in wanted.OrderBy(w => w.Key))
            {
                if (_instances.ContainsKey((creation.Id, part)))
                    continue;
                var instance = new Instance(creation.Id, part, want.Type, EntryFor(want.File));
                _instances[(creation.Id, part)] = instance;
                Start(instance);
            }
        }
        foreach (var gone in _revisions.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _revisions.Remove(gone);
            foreach (var instance in InstancesOf(gone))
                Destroy(instance);
            Readouts.Prune(gone, _ => false);
        }
    }

    private List<Instance> InstancesOf(int creation) =>
        [.. _instances.Values.Where(i => i.Creation == creation)];

    private ScriptFile? ScriptOf(string partType)
    {
        if (!_scriptOfType.TryGetValue(partType, out var file))
            _scriptOfType[partType] = file = _source.FindScript(partType);
        return file;
    }

    private FileEntry EntryFor(ScriptFile file)
    {
        if (_files.TryGetValue(file.Key, out var entry) && entry.File == file)
            return entry;
        if (!_contexts.TryGetValue(file.ModId, out var context))
            _contexts[file.ModId] = context = CreateContext(file.ModId);
        entry = _files[file.Key] = new FileEntry(file, context);
        Compile(entry, keepOld: false);
        return entry;
    }

    // Reads and compiles a file. On failure keepOld leaves the last good factory in place.
    private bool Compile(FileEntry entry, bool keepOld)
    {
        entry.Stamp = StampOf(entry.File.FullPath);
        string source;
        try
        {
            source = File.ReadAllText(entry.File.FullPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return CompileFailed(entry, keepOld, $"cannot read {entry.File.Key}: {e.Message}");
        }
        try
        {
            entry.Factory = entry.Context.Compile(source, entry.File.Key);
        }
        catch (InterpreterException e)
        {
            return CompileFailed(entry, keepOld, e.DecoratedMessage ?? e.Message);
        }
        entry.Error = null;
        entry.Version++;
        return true;
    }

    private bool CompileFailed(FileEntry entry, bool keepOld, string error)
    {
        entry.Error = error;
        if (!keepOld)
            entry.Factory = null;
        Post(ScriptMessageKind.Error, entry.File.Key, null, null,
            keepOld ? $"{error} (the previous version keeps running)" : error);
        return false;
    }

    // The part's (or held item's) properties as a Lua table, at the part's upgrade level, which it remembers.
    private DynValue Properties(Instance instance, Creation? creation)
    {
        var script = instance.File.Context.Script;
        var placed = !instance.IsItem && creation?.FindPart(instance.Part) is not null;
        instance.Level = placed ? creation!.LevelOf(instance.Part) : 0;
        var properties = instance.IsItem ? _catalog?.FindDefinition(instance.PartType)?.Properties
            : placed ? creation!.DefinitionOf(instance.Part).Properties : null;
        return properties is null ? DynValue.NewTable(new Table(script)) : LuaValues.FromJson(script, properties);
    }

    // Builds the instance's environment (keeping its state), runs the script's top level, then
    // on_create or on_reload.
    private void Start(Instance instance)
    {
        var entry = instance.File;
        if (entry.Factory is null)
        {
            instance.Fault = entry.Error ?? "the script did not load";
            return;
        }
        var context = entry.Context;
        var env = context.NewEnvironment();
        instance.State ??= new Table(context.Script);
        env.Set("state", DynValue.NewTable(instance.State));
        env.Set("properties", Properties(instance, instance.IsItem ? null : _motion.FindCreation(instance.Creation)));
        if (instance.IsPart && _motion.FindCreation(instance.Creation) is { } home && home.FindPart(instance.Part) is not null)
            instance.Sounds = home.DefinitionOf(instance.Part).Sounds;
        instance.Env = env;
        instance.Fault = null;
        // A fresh start hears its input again as on_power, even if it has not changed.
        if (instance.IsPart)
            Signals.ResetInput(instance.Creation, instance.Part);
        instance.Warned.Clear();
        // The top level listens again (events.listen), so a reload drops topics the new code no longer wants.
        instance.Topics.Clear();
        instance.Timers = [];
        instance.Version = entry.Version;
        if (Run(instance, entry.Factory, "loading", DynValue.NewTable(env)) is null)
            return;
        var first = !instance.Created;
        instance.Created = true;
        // An item's setup runs outside any accepted action, so it may read but not change the world.
        var phase = _phase;
        _phase = instance.IsItem ? ItemPhase.Checking : phase;
        try
        {
            CallHook(instance, first ? Hooks.OnCreate : Hooks.OnReload);
        }
        finally
        {
            _phase = phase;
        }
    }

    private void Destroy(Instance instance)
    {
        if (instance.Fault is null && instance.Created)
            CallHook(instance, Hooks.OnDestroy);
        StopLoops(instance);
        _instances.Remove((instance.Creation, instance.Part));
        Signals.Forget(instance.Creation, instance.Part);
        Lights.Forget(instance.Creation, instance.Part);
        Readouts.Forget(instance.Creation, instance.Part);
        Animations.Forget(instance.Creation, instance.Part);
    }

    private void StopLoops(Instance instance)
    {
        foreach (var handle in instance.Loops)
            Sound.StopLoop(handle);
        instance.Loops.Clear();
    }

    // Calls a hook if the script defines it. Null when the call failed (the instance is then stopped).
    private DynValue[]? CallHook(Instance instance, string hook, params DynValue[] args)
    {
        if (instance.Env is not { } env || instance.Fault is not null)
            return null;
        var function = env.RawGet(hook);
        if (function is null || function.IsNil())
            return [];
        if (function.Type != DataType.Function)
        {
            Stop(instance, $"{hook} is a {function.Type.ToLuaTypeString()}, not a function");
            return null;
        }
        return Run(instance, function, hook, args);
    }

    private DynValue[]? Run(Instance instance, DynValue function, string what, params DynValue[] args)
    {
        var previous = _current;
        _current = instance;
        CallOutcome outcome;
        try
        {
            outcome = instance.File.Context.Call(function, args);
        }
        finally
        {
            _current = previous;
        }
        if (outcome.Status == CallStatus.Ok)
            return outcome.Values;
        Stop(instance, $"{outcome.Error} (in {what})");
        return null;
    }

    private void Stop(Instance instance, string fault)
    {
        instance.Fault = fault;
        instance.Timers = [];
        StopLoops(instance);
        if (instance.IsPart)
        {
            Signals.SetOutput(instance.Creation, instance.Part, 0f);
            Lights.Set(instance.Creation, instance.Part, 0f);
            Animations.Set(instance.Creation, instance.Part, null, 0f, Time);
        }
        if (instance.IsItem)
            Post(ScriptMessageKind.Error, instance.File.File.Key, null, null, $"{fault}; the {instance.PartType} script of player {instance.Holder} is stopped until it is reloaded");
        else if (instance.IsWorld)
            Post(ScriptMessageKind.Error, instance.File.File.Key, null, null, $"{fault}; the mod script is stopped until it is reloaded");
        else
            Post(ScriptMessageKind.Error, instance.File.File.Key, instance.Creation, instance.Part, $"{fault}; the part's script is stopped until it is reloaded");
    }

    private void Post(ScriptMessageKind kind, string script, int? creation, int? part, string text)
    {
        var message = new ScriptMessage(kind, script, creation, part, text, Tick);
        _messages.Enqueue(message);
        while (_messages.Count > _settings.MaxMessages)
            _messages.Dequeue();
        Message?.Invoke(message);
    }

    private void Print(string text)
    {
        if (_current is { IsPart: false } item)
            Post(ScriptMessageKind.Print, item.File.File.Key, null, null, text);
        else if (_current is { } instance)
            Post(ScriptMessageKind.Print, instance.File.File.Key, instance.Creation, instance.Part, text);
        else
            Post(ScriptMessageKind.Print, "?", null, null, text);
    }

    private DynValue ToLua(LuaContext context, object? value) => value switch
    {
        null => DynValue.Nil,
        Vector3 v => LuaValues.Vector(context.Script, context.Vec3Meta, v),
        JsonNode node => LuaValues.FromJson(context.Script, node),
        DynValue d => d,
        _ => DynValue.FromObject(context.Script, value),
    };

    private static DateTime StampOf(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
}
