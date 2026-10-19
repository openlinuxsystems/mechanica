using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Motion;
using Mechanica.Core.Remote;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

// Mod scripts (mod.json "scripts"): behaviour that belongs to no part, the events scripts listen to, actions.call
// and require.
public sealed partial class ScriptRuntime
{
    /// <summary>
    /// The event topics scripts may listen to (<c>events.listen</c>): every endpoint topic except script messages,
    /// which would feed a script its own prints.
    /// </summary>
    public static readonly IReadOnlySet<string> EventTopics =
        RemoteEvents.Topics.Keys.Where(t => t is not ("messages" or "events_dropped")).ToHashSet(StringComparer.Ordinal);

    // Actions that move a Creation: a part moves only its own through part.*, and a mod script moves none.
    private static readonly HashSet<string> MotionActionNames =
        new([MotionActions.ApplyForce, MotionActions.SetBearingTarget, MotionActions.SetPistonTarget], StringComparer.Ordinal);

    private readonly List<Instance> _worlds = [];
    private readonly Queue<(string Topic, JsonNode? Data)> _events = new();
    private IReadOnlyList<ScriptFile>? _worldScripts;

    /// <summary>The running mod scripts (<c>mod.json</c> <c>scripts</c>), in load order, with why each stopped.</summary>
    public IReadOnlyList<(string Script, string? Fault)> ModScripts => [.. _worlds.Select(w => (w.File.File.Key, w.Fault))];

    /// <summary>True when a running script listens to <paramref name="topic"/>.</summary>
    public bool Listens(string topic)
    {
        foreach (var world in _worlds)
            if (world.Fault is null && world.Topics.Contains(topic))
                return true;
        foreach (var instance in _instances.Values)
            if (instance.Fault is null && instance.Topics.Count > 0 && instance.Topics.Contains(topic))
                return true;
        return false;
    }

    /// <summary>
    /// Queues an event for the scripts listening to <paramref name="topic"/>; they hear it as <c>on_event(topic, data)</c>
    /// at the start of the next <see cref="Update"/>. Past <see cref="ScriptSettings.MaxQueuedEvents"/> the oldest is dropped.
    /// </summary>
    public void RaiseEvent(string topic, JsonElement data)
    {
        if (!EventTopics.Contains(topic) || !Listens(topic))
            return;
        _events.Enqueue((topic, JsonNode.Parse(data.GetRawText())));
        while (_events.Count > _settings.MaxQueuedEvents)
            _events.Dequeue();
    }

    /// <summary>A mod script's <c>state</c> as JSON (for saves), by script; scripts with an empty state are left out.</summary>
    public IReadOnlyDictionary<string, JsonObject> ExportModStates()
    {
        var states = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var world in _worlds)
        {
            if (world.State is { } state && LuaValues.ToJson(DynValue.NewTable(state)) is JsonObject json && json.Count > 0)
                states[world.File.File.Key] = json;
        }
        return states;
    }

    /// <summary>Replaces a mod script's <c>state</c> with JSON data. False when no mod script <paramref name="script"/> runs.</summary>
    public bool ImportModState(string script, JsonObject state)
    {
        ReconcileWorld();
        if (_worlds.FirstOrDefault(w => w.File.File.Key == script) is not { Env: { } env } world)
            return false;
        world.State = LuaValues.FromJson(world.File.Context.Script, state).Table;
        env.Set("state", DynValue.NewTable(world.State));
        return true;
    }

    // Starts the mod scripts the mods list now and stops the ones they no longer list; the rest keep running.
    private void ReconcileWorld()
    {
        var wanted = _worldScripts ??= _source.WorldScripts;
        if (_worlds.Count == wanted.Count && _worlds.Select(w => w.File.File).SequenceEqual(wanted))
            return;
        foreach (var world in _worlds.Where(w => !wanted.Contains(w.File.File)).ToList())
            DestroyWorld(world);
        var running = _worlds.ToDictionary(w => w.File.File.Key, StringComparer.Ordinal);
        _worlds.Clear();
        foreach (var file in wanted)
        {
            if (running.TryGetValue(file.Key, out var kept))
            {
                if (!_worlds.Contains(kept))
                    _worlds.Add(kept);
                continue;
            }
            var instance = new Instance(-1, -1, file.Key, EntryFor(file)) { IsWorld = true };
            running[file.Key] = instance;
            _worlds.Add(instance);
            Start(instance);
        }
    }

    private void DestroyWorld(Instance world)
    {
        if (world.Fault is null && world.Created)
            CallHook(world, Hooks.OnDestroy);
        _worlds.Remove(world);
    }

    private void ClearWorld()
    {
        foreach (var world in _worlds.ToList())
            DestroyWorld(world);
        _events.Clear();
        _worldScripts = null;
    }

    // Hands this step's events to the scripts listening for them: mod scripts in load order, then parts.
    private void DeliverEvents()
    {
        for (var count = _events.Count; count > 0; count--)
        {
            var (topic, data) = _events.Dequeue();
            _stepping.Clear();
            _stepping.AddRange(_worlds);
            _stepping.AddRange(_instances.Values);
            foreach (var instance in _stepping)
            {
                if (instance.Fault is null && instance.Topics.Contains(topic))
                    CallHook(instance, Hooks.OnEvent, DynValue.NewString(topic), LuaValues.FromJson(instance.File.Context.Script, data));
            }
            _stepping.Clear();
        }
    }

    private void AddModLibraries(LuaContext context)
    {
        var script = context.Script;
        var events = new Table(script);
        Fn(events, "listen", args =>
        {
            var instance = Listener("events.listen");
            var topic = Topic(args, "events.listen");
            instance.Topics.Add(topic);
            return DynValue.True;
        });
        Fn(events, "unlisten", args =>
        {
            var instance = Listener("events.unlisten");
            return DynValue.NewBoolean(instance.Topics.Remove(Topic(args, "events.unlisten")));
        });
        Fn(events, "topics", _ =>
        {
            var list = new Table(script);
            var index = 1;
            foreach (var topic in EventTopics.Order(StringComparer.Ordinal))
                list.Set(index++, DynValue.NewString(topic));
            return DynValue.NewTable(list);
        });
        script.Globals.Set("events", DynValue.NewTable(events));

        var actions = new Table(script);
        Fn(actions, "call", args =>
        {
            var instance = Current();
            var name = args.AsType(0, "actions.call", DataType.String).String;
            var input = args.Count > 1 && !args[1].IsNil() ? args.AsType(1, "actions.call", DataType.Table) : DynValue.NewTable(new Table(script));
            if (!_dispatcher.Has(name))
                throw new ScriptRuntimeException($"actions.call: there is no action '{name}'");
            if (MotionActionNames.Contains(name))
                throw new ScriptRuntimeException($"actions.call: {name} moves a Creation; a part moves its own with part.*");
            if (!_dispatcher.IsReadOnly(name))
            {
                if (!instance.IsWorld)
                    throw new ScriptRuntimeException($"actions.call: {name} changes the world, which only mod scripts may do through actions.call (part and item scripts may call actions that only read)");
                Changing("actions.call");
            }
            string json;
            try
            {
                json = LuaValues.ToJson(input, "args")?.ToJsonString() ?? "{}";
            }
            catch (ArgumentException e)
            {
                throw new ScriptRuntimeException($"actions.call: {e.Message}");
            }
            var result = _dispatcher.Dispatch(Caller, name, json);
            if (!result.Ok)
                return Refused(instance, result.Message ?? result.Code ?? "refused");
            return DynValue.NewTuple(DynValue.True,
                result.Value is { } value ? LuaValues.FromJson(script, JsonNode.Parse(value.GetRawText())) : DynValue.Nil);
        });
        Fn(actions, "list", _ =>
        {
            var list = new Table(script);
            var index = 1;
            foreach (var name in _dispatcher.Names.Where(n => !MotionActionNames.Contains(n)))
                list.Set(index++, DynValue.NewString(name));
            return DynValue.NewTable(list);
        });
        script.Globals.Set("actions", DynValue.NewTable(actions));

        InstallRequire(context);
    }

    // Part and mod scripts listen to events; an item's script lives only while it is used.
    private Instance Listener(string function) =>
        _current is { IsItem: false } instance ? instance : throw new ScriptRuntimeException($"{function} only works in a mod or part script");

    private static string Topic(CallbackArguments args, string function)
    {
        var topic = args.AsType(0, function, DataType.String).String;
        return EventTopics.Contains(topic) ? topic
            : throw new ScriptRuntimeException($"{function}: there is no event topic '{topic}'; topics: {string.Join(", ", EventTopics.Order(StringComparer.Ordinal))}");
    }

    // require(path): runs a script of this mod (or other_mod:path of a dependency) once and returns what it returns.
    // The module runs in its own environment over the mod's libraries and is shared by every script of the mod;
    // a changed file loads again on the next require.
    private const string RequireSource = """
        return function(find, globals, setmetatable, pcall, error)
          local loaded, loading = {}, {}
          return function(path)
            local key, chunk = find(path)
            local cached = loaded[key]
            if cached ~= nil then return cached end
            if loading[key] then error("require: " .. path .. " requires itself (through another module)", 2) end
            loading[key] = true
            local ok, result = pcall(chunk(), setmetatable({}, { __index = globals }))
            loading[key] = nil
            if not ok then error(result, 0) end
            if result == nil then result = true end
            loaded[key] = result
            return result
          end
        end
        """;

    private void InstallRequire(LuaContext context)
    {
        var script = context.Script;
        var chunks = new Dictionary<string, (DateTime Stamp, int Version, DynValue Chunk)>(StringComparer.Ordinal);
        var find = DynValue.NewCallback((_, args) =>
        {
            var path = args.AsType(0, "require", DataType.String).String;
            if (_source.FindModule(context.ModId, path) is not { } file)
                throw new ScriptRuntimeException($"require: '{path}' is not a script in mod '{context.ModId}' or a mod it depends on");
            var stamp = StampOf(file.FullPath);
            if (!chunks.TryGetValue(file.Key, out var known) || known.Stamp != stamp)
            {
                string source;
                try
                {
                    source = File.ReadAllText(file.FullPath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new ScriptRuntimeException($"require: cannot read {file.Key}: {e.Message}");
                }
                DynValue chunk;
                try
                {
                    chunk = script.LoadString($"return function(_ENV) {source}\nend", null, file.Key);
                }
                catch (SyntaxErrorException e)
                {
                    throw new ScriptRuntimeException(e.DecoratedMessage ?? e.Message);
                }
                chunks[file.Key] = known = (stamp, known.Version + 1, chunk);
            }
            return DynValue.NewTuple(DynValue.NewString($"{file.Key}#{known.Version}"), known.Chunk);
        }, "require");
        var factory = script.DoString(RequireSource, null, "=require");
        var g = script.Globals;
        script.Globals.Set("require", script.Call(factory, find, DynValue.NewTable(g), g.Get("setmetatable"), g.Get("pcall"), g.Get("error")));
    }
}
