using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

// The logic and power bus (chunk 10): part outputs and inputs, settings, seats and fuel. The runtime is the
// ILogicHost behind LogicActions, so ActivatePart and the seat actions reach the part scripts.
public sealed partial class ScriptRuntime : ILogicHost
{
    /// <summary>The logic and power bus: every part's output, and the inputs <see cref="Update"/> delivers as <c>on_power</c>.</summary>
    public SignalBus Signals { get; } = new();

    /// <summary>How bright each part's light is (chunk 23), set by <c>part.set_light</c>; the game sends it to clients.</summary>
    public LightLevels Lights { get; } = new();

    /// <summary>How far each animated part's model is turned (chunk 23), set by <c>part.animate</c> on the script clock (<see cref="Time"/>).</summary>
    public PartAnimations Animations { get; } = new();

    /// <summary>What each part's script shows (UI spec §12), set by <c>part.display</c>; the game sends it to clients.</summary>
    public PartReadouts Readouts { get; } = new();

    /// <summary>Who sits where, for <c>part.occupant()</c>. Null means no seats.</summary>
    public SeatRegistry? Seats { get; set; }

    /// <summary>A seated player's latest input (the host's copy, already sanitized), for <c>part.occupant_input()</c>.</summary>
    public Func<int, PlayerInput?>? OccupantInput { get; set; }

    /// <summary>True in Creative (D13): <c>world.creative()</c> tells engines to run without fuel.</summary>
    public bool Creative { get; set; }

    /// <inheritdoc/>
    public string? CheckActivate(ActionCaller caller, Creation creation, int part)
    {
        Reconcile();
        if (!_instances.TryGetValue((creation.Id, part), out var instance) || instance.Env is null)
            return $"{creation.DefinitionOf(part).Name ?? creation.FindPart(part)!.Part} has nothing to use.";
        if (instance.Fault is { } fault)
            return $"Its script is stopped: {fault}";
        var hook = instance.Env.RawGet(Hooks.OnActivate);
        if (hook is not { Type: DataType.Function })
            return $"{creation.DefinitionOf(part).Name ?? instance.PartType} has nothing to use.";
        // can_activate(peer) may refuse with a reason; like an item's can_use it only reads.
        var previous = _phase;
        _phase = ItemPhase.Checking;
        DynValue[]? values;
        try
        {
            values = CallHook(instance, Hooks.CanActivate, DynValue.NewNumber(caller.Peer));
        }
        finally
        {
            _phase = previous;
        }
        if (values is null)
            return $"Its script is stopped: {instance.Fault}";
        return values.Length > 0 && values[0].Type == DataType.String ? values[0].String
            : values.Length > 0 && values[0].Type == DataType.Boolean && !values[0].Boolean ? $"You cannot use the {creation.DefinitionOf(part).Name ?? instance.PartType} now."
            : null;
    }

    /// <inheritdoc/>
    public JsonElement? Activate(ActionCaller caller, Creation creation, int part)
    {
        var result = Invoke(creation.Id, part, Hooks.OnActivate, (double)caller.Peer);
        if (!result.Ok)
            throw new InvalidOperationException(result.Error);
        return result.Values.Count > 0 && result.Values[0] is { } value ? JsonSerializer.SerializeToElement(value) : null;
    }

    /// <inheritdoc/>
    public void Release(int peer, Creation creation, int part) =>
        Invoke(creation.Id, part, Hooks.OnRelease, (double)peer);

    /// <inheritdoc/>
    public void Seated(SeatAssignment seat, bool entered) =>
        Invoke(seat.Creation, seat.Part, entered ? Hooks.OnEnter : Hooks.OnLeave, (double)seat.Peer);

    /// <inheritdoc/>
    public (string? Script, string? Fault) ScriptOf(int creation, int part) =>
        _instances.TryGetValue((creation, part), out var instance) ? (instance.File.File.Key, instance.Fault) : (null, null);

    /// <inheritdoc/>
    public string? CheckCommand(Creation creation, int part)
    {
        Reconcile();
        if (!_instances.TryGetValue((creation.Id, part), out var instance) || instance.Env is null)
            return $"{creation.DefinitionOf(part).Name ?? creation.FindPart(part)!.Part} runs no script.";
        if (instance.Fault is { } fault)
            return $"Its script is stopped: {fault}";
        return instance.Env.RawGet(Hooks.OnCommand) is { Type: DataType.Function } ? null : $"{creation.DefinitionOf(part).Name ?? instance.PartType} takes no commands.";
    }

    /// <inheritdoc/>
    public void Command(ActionCaller caller, Creation creation, int part, string command, JsonElement? args)
    {
        var result = Invoke(creation.Id, part, Hooks.OnCommand, command, args is { } a ? System.Text.Json.Nodes.JsonNode.Parse(a.GetRawText()) : null, (double)caller.Peer);
        if (!result.Ok)
            throw new InvalidOperationException(result.Error);
    }

    // Hands each part whose input changed its on_power(amount); the inputs come from last step's outputs.
    private void DeliverSignals()
    {
        foreach (var (creation, part, input) in Signals.Step(_creations()))
        {
            if (_instances.TryGetValue((creation, part), out var instance) && instance.Fault is null && _motion.IsLive(creation))
                CallHook(instance, Hooks.OnPower, DynValue.NewNumber(input));
        }
    }

    // Parts that define get_outputs() set their output from what it returns, once per step.
    private void CollectOutputs()
    {
        // on_update may have added or removed parts.
        _stepping.Clear();
        _stepping.AddRange(_instances.Values);
        foreach (var instance in _stepping)
        {
            if (instance.Fault is not null || instance.Env?.RawGet(Hooks.GetOutputs) is not { Type: DataType.Function } || !_motion.IsLive(instance.Creation))
                continue;
            if (CallHook(instance, Hooks.GetOutputs) is { } values)
                Signals.SetOutput(instance.Creation, instance.Part, values.Length == 0 ? 0f : SignalOf(values[0], Hooks.GetOutputs));
        }
    }

    private static float SignalOf(DynValue value, string function) => value.Type switch
    {
        DataType.Nil or DataType.Void => 0f,
        DataType.Boolean => value.Boolean ? 1f : 0f,
        DataType.Number => (float)value.Number,
        _ => throw new ScriptRuntimeException($"{function}: a signal is a number or a boolean, not a {value.Type.ToLuaTypeString()}"),
    };

    private void AddLogicLibrary(Script script, Table part, Table creation, Table world, Func<System.Numerics.Vector3, DynValue> vec)
    {
        Fn(part, "set_output", args =>
        {
            var instance = PartInstance();
            Signals.SetOutput(instance.Creation, instance.Part, SignalOf(args[0], "part.set_output"));
            return DynValue.Nil;
        });
        Fn(part, "output", _ => DynValue.NewNumber(Signals.Output(PartInstance().Creation, PartInstance().Part)));
        Fn(part, "input", _ => Here() is { } h ? DynValue.NewNumber(Signals.Inputs(h.Creation, h.Instance.Part).Sum(i => i.Value)) : DynValue.NewNumber(0));
        Fn(part, "inputs", _ =>
        {
            var list = new Table(script);
            if (Here() is { } h)
            {
                var index = 1;
                foreach (var (from, value) in Signals.Inputs(h.Creation, h.Instance.Part))
                {
                    var entry = new Table(script);
                    entry.Set("part", DynValue.NewNumber(from));
                    entry.Set("value", DynValue.NewNumber(value));
                    if (h.Creation.FindPart(from) is { } source)
                    {
                        entry.Set("type", DynValue.NewString(Identifiers.Qualify(source.Part)));
                        // A container linked in (a gas container feeding an engine) rather than a control.
                        entry.Set("storage", DynValue.NewBoolean(h.Creation.InventoryOf(from) is not null));
                        // A seat linked in (a driver's seat to an engine), and whether someone sits in it.
                        if (h.Creation.DefinitionOf(from).Seat is not null)
                        {
                            entry.Set("seat", DynValue.True);
                            entry.Set("occupied", DynValue.NewBoolean(Seats?.Occupant(h.Creation.Id, from) is not null));
                        }
                    }
                    list.Set(index++, DynValue.NewTable(entry));
                }
            }
            return DynValue.NewTable(list);
        });
        Fn(part, "links", _ =>
        {
            var list = new Table(script);
            if (Here() is { } h)
            {
                var index = 1;
                foreach (var to in h.Creation.LinksFrom(h.Instance.Part))
                    list.Set(index++, DynValue.NewNumber(to));
            }
            return DynValue.NewTable(list);
        });
        Fn(part, "setting", args =>
        {
            var id = args.AsType(0, "part.setting", DataType.String).String;
            if (Here() is not { } h || PartSettings.Value(h.Creation.DefinitionOf(h.Instance.Part), h.Creation.FindPart(h.Instance.Part)!.Settings, id) is not { } value)
                return DynValue.Nil;
            // Scripts read their settings every step: plain values skip the JSON round trip.
            return value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => DynValue.NewNumber(value.GetDouble()),
                System.Text.Json.JsonValueKind.True => DynValue.True,
                System.Text.Json.JsonValueKind.False => DynValue.False,
                System.Text.Json.JsonValueKind.String => DynValue.NewString(value.GetString()),
                _ => LuaValues.FromJson(script, System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText())),
            };
        });
        Fn(part, "sound", args =>
        {
            var key = args.AsType(0, "part.sound", DataType.String).String;
            return Here() is { } h && h.Creation.DefinitionOf(h.Instance.Part).Sounds?.GetValueOrDefault(key) is { } id
                ? DynValue.NewString(id)
                : DynValue.Nil;
        });
        Fn(part, "set_light", args =>
        {
            var instance = PartInstance();
            var level = SignalOf(args[0], "part.set_light");
            if (Here() is not { } h || h.Creation.DefinitionOf(instance.Part).Light is null)
                return Refused(instance, "the part has no light");
            Lights.Set(instance.Creation, instance.Part, level);
            return DynValue.True;
        });
        Fn(part, "display", args =>
        {
            var instance = PartInstance();
            return Display(instance, instance.Part, args.AsType(0, "part.display", DataType.String).String, args[1], "part.display");
        });
        Fn(part, "displayed", args =>
        {
            var instance = PartInstance();
            var id = args.AsType(0, "part.displayed", DataType.String).String;
            return Readouts.Text(instance.Creation, instance.Part, id) is { } text ? DynValue.NewString(text)
                : Readouts.Number(instance.Creation, instance.Part, id) is { } n ? DynValue.NewNumber(n) : DynValue.Nil;
        });
        // Another part of the same Creation (a seat asking whether the engines run): read only, so any part will do.
        Fn(creation, "displayed", args =>
        {
            var instance = PartInstance();
            var target = (int)LuaValues.Number(args[0], "creation.displayed", 1);
            var id = args.AsType(1, "creation.displayed", DataType.String).String;
            return Readouts.Text(instance.Creation, target, id) is { } text ? DynValue.NewString(text)
                : Readouts.Number(instance.Creation, target, id) is { } n ? DynValue.NewNumber(n) : DynValue.Nil;
        });
        // A part this one is linked to (an engine's wheel bearings): its highlight follows what drives it.
        Fn(creation, "display", args =>
        {
            var instance = PartInstance();
            var target = (int)LuaValues.Number(args[0], "creation.display", 1);
            if (target != instance.Part && (_motion.FindCreation(instance.Creation) is not { } c || !c.LinksFrom(instance.Part).Contains(target)))
                return Refused(instance, $"creation.display: part {target} is not linked from this part");
            return Display(instance, target, args.AsType(1, "creation.display", DataType.String).String, args[2], "creation.display");
        });
        Fn(part, "light", _ => DynValue.NewNumber(Lights.Level(PartInstance().Creation, PartInstance().Part)));
        Fn(part, "crafting", _ => Here() is { } h && h.Creation.CraftingOf(h.Instance.Part) is { } queue ? CraftingTable(script, queue) : DynValue.Nil);
        Fn(part, "animate", args =>
        {
            var instance = PartInstance();
            float? angle = args.Count > 0 && !args[0].IsNil() ? LuaValues.Number(args[0], "part.animate", 1) : null;
            var speed = LuaValues.OptionalNumber(args[1], "part.animate", 2) ?? 0f;
            if (Here() is not { } h || h.Creation.DefinitionOf(instance.Part).Animation is null)
                return Refused(instance, "the part has no animation");
            Animations.Set(instance.Creation, instance.Part, angle, speed, Time);
            return DynValue.True;
        });
        Fn(part, "animation", _ => DynValue.NewNumber(Animations.AngleAt(PartInstance().Creation, PartInstance().Part, Time)));
        Fn(part, "occupant", _ =>
        {
            var instance = PartInstance();
            return Seats?.Occupant(instance.Creation, instance.Part) is { } peer ? DynValue.NewNumber(peer) : DynValue.Nil;
        });
        Fn(part, "occupant_input", _ => OccupantTable(script, PartInstance().Creation, PartInstance().Part));
        // Another seat of the same Creation: a control surface or rotor reads the pilot of the seat linked to it.
        Fn(creation, "occupant_input", args => OccupantTable(script, PartInstance().Creation, (int)LuaValues.Number(args[0], "creation.occupant_input", 1)));


        Fn(creation, "count", args =>
        {
            var instance = PartInstance();
            var item = ItemId(instance, args.AsType(0, "creation.count", DataType.String).String);
            return DynValue.NewNumber(_motion.FindCreation(instance.Creation) is { } c
                ? Containers(c, args, 1, "creation.count").Sum(p => c.InventoryOf(p)?.Count(item) ?? 0)
                : 0);
        });
        Fn(creation, "space", args =>
        {
            var instance = PartInstance();
            var item = ItemId(instance, args.AsType(0, "creation.space", DataType.String).String);
            return DynValue.NewNumber(_motion.FindCreation(instance.Creation) is { } c
                ? Containers(c, args, 1, "creation.space").Sum(p => c.InventoryOf(p)?.SpaceFor(item) ?? 0)
                : 0);
        });
        Fn(creation, "take", args =>
        {
            var instance = PartInstance();
            var item = ItemId(instance, args.AsType(0, "creation.take", DataType.String).String);
            var wanted = args.Count > 1 && !args[1].IsNil() ? Count(args[1], "creation.take", 2) : 1;
            var taken = 0;
            if (_motion.FindCreation(instance.Creation) is { } c)
            {
                // Containers in part order, so which tank empties first is the same every time.
                foreach (var p in Containers(c, args, 2, "creation.take"))
                {
                    if (taken == wanted)
                        break;
                    if (c.InventoryOf(p) is { } inventory && inventory.Count(item) > 0)
                        taken += inventory.Remove(item, wanted - taken);
                }
            }
            return DynValue.NewNumber(taken);
        });

        Fn(world, "creative", _ => DynValue.NewBoolean(Creative));
    }

    // The parts creation.count and creation.take look in, in part order: every part of the Creation, or only those
    // listed in the optional argument at `index` (a list of part ids, such as the gas containers linked to an engine).
    private static IEnumerable<int> Containers(Creation creation, CallbackArguments args, int index, string function)
    {
        if (args.Count <= index || args[index].IsNil())
            return creation.Parts.Select(p => p.Id);
        var listed = new HashSet<int>();
        foreach (var pair in args.AsType(index, function, DataType.Table).Table.Pairs)
            listed.Add((int)LuaValues.Number(pair.Value, function, index + 1));
        return creation.Parts.Select(p => p.Id).Where(listed.Contains);
    }

    // Sets (a number, boolean or string) or clears (nil) one of a part's readouts.
    private DynValue Display(Instance instance, int part, string id, DynValue value, string function)
    {
        string? error;
        switch (value.Type)
        {
            case DataType.Nil or DataType.Void:
                Readouts.Clear(instance.Creation, part, id);
                return DynValue.True;
            case DataType.String:
                error = Readouts.Set(instance.Creation, part, id, value.String);
                break;
            case DataType.Boolean or DataType.Number:
                error = Readouts.Set(instance.Creation, part, id, SignalOf(value, function));
                break;
            default:
                throw new ScriptRuntimeException($"{function}: a value is a number, a boolean, a string or nil, not a {value.Type.ToLuaTypeString()}");
        }
        return error is null ? DynValue.True : Refused(instance, error);
    }

    // A seated player's input as {x, z, jump, sprint, yaw, pitch}, or nil when the seat is empty.
    private static DynValue CraftingTable(Script script, CraftingQueue queue)
    {
        var table = new Table(script);
        table.Set("working", DynValue.NewBoolean(!queue.IsEmpty && !queue.IsBlocked));
        table.Set("blocked", DynValue.NewBoolean(queue.IsBlocked));
        table.Set("crafts", DynValue.NewNumber(queue.Jobs.Sum(j => j.Count)));
        table.Set("finished", DynValue.NewNumber(queue.Finished));
        return DynValue.NewTable(table);
    }

    private DynValue OccupantTable(Script script, int creationId, int partId)
    {
        if (Seats?.Occupant(creationId, partId) is not { } peer || OccupantInput?.Invoke(peer) is not { } input)
            return DynValue.Nil;
        var table = new Table(script);
        table.Set("x", DynValue.NewNumber(input.MoveX));
        table.Set("z", DynValue.NewNumber(input.MoveZ));
        table.Set("jump", DynValue.NewBoolean(input.Jump));
        table.Set("sprint", DynValue.NewBoolean(input.Sprint));
        table.Set("yaw", DynValue.NewNumber(input.Yaw));
        table.Set("pitch", DynValue.NewNumber(input.Pitch));
        return DynValue.NewTable(table);
    }

    // The world direction a joint turns about or slides along, for creation.joint().axis.
    private static System.Numerics.Vector3 JointAxis(Creation creation, JointLink link, JointFrame frame) =>
        System.Numerics.Vector3.Transform(frame.Axis.ToNumerics(), creation.BodyPose(link.BodyA).Rotation);
}
