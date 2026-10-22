using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

// Item transport (Parts map E1): the generic primitives a Vacuum Pump's and a Resource Collector's scripts use. What a
// part's IO ports reach through pipes (part.network), what its intake reaches (part.pickups), taking one of those in
// (part.collect) and throwing items out (part.eject) through the host's validated CollectItems and EjectItems actions.
// A ripe crop a machine collects is harvested by the soil
// part's own script, in its on_collect hook.
public sealed partial class ScriptRuntime
{
    private TransportActions? _transport;

    /// <summary>
    /// Gives part scripts item transport (<c>part.pickups</c>, <c>part.collect</c>, <c>part.eject</c>) through
    /// <paramref name="transport"/>, and lets machines harvest ripe crops through the soil's <c>on_collect</c>. Call once,
    /// on the host, after its actions are registered.
    /// </summary>
    public void EnableTransport(TransportActions transport)
    {
        if (_transport is not null)
            throw new InvalidOperationException("Transport is already enabled.");
        _transport = transport;
        transport.HarvestCrop = Harvest;
    }

    private void AddTransportLibrary(LuaContext context, Table part)
    {
        var script = context.Script;
        DynValue Items(IEnumerable<ItemStack> items)
        {
            var list = new Table(script);
            var i = 1;
            foreach (var stack in items)
            {
                var entry = new Table(script);
                entry.Set("item", DynValue.NewString(stack.Item));
                entry.Set("count", DynValue.NewNumber(stack.Count));
                list.Set(i++, DynValue.NewTable(entry));
            }
            return DynValue.NewTable(list);
        }

        Fn(part, "network", _ =>
        {
            var list = new Table(script);
            if (Here() is not { } h)
                return DynValue.NewTable(list);
            var i = 1;
            foreach (var link in TransportActions.Network(h.Creation, h.Instance.Part))
            {
                var entry = new Table(script);
                entry.Set("port", DynValue.NewString(link.Port));
                entry.Set("part", DynValue.NewNumber(link.Part));
                entry.Set("type", DynValue.NewString(link.Type));
                entry.Set("remote_port", DynValue.NewString(link.RemotePort));
                entry.Set("takes_in", DynValue.NewBoolean(link.TakesIn));
                entry.Set("gives_out", DynValue.NewBoolean(link.GivesOut));
                entry.Set("storage", DynValue.NewBoolean(link.Storage));
                entry.Set("station", DynValue.NewBoolean(link.Station));
                var pipes = new Table(script);
                for (var p = 0; p < link.Pipes.Count; p++)
                    pipes.Set(p + 1, DynValue.NewNumber(link.Pipes[p]));
                entry.Set("pipes", DynValue.NewTable(pipes));
                list.Set(i++, DynValue.NewTable(entry));
            }
            return DynValue.NewTable(list);
        });
        Fn(part, "pickups", _ =>
        {
            var list = new Table(script);
            if (_transport is null || Here() is not { } h)
                return DynValue.NewTable(list);
            var i = 1;
            foreach (var pickup in _transport.Pickups(h.Creation, h.Instance.Part))
            {
                var entry = new Table(script);
                entry.Set("kind", DynValue.NewString(JsonNamingPolicy.SnakeCaseLower.ConvertName(pickup.Kind.ToString())));
                entry.Set("position", LuaValues.Vector(script, context.Vec3Meta, pickup.Position.ToNumerics()));
                entry.Set("distance", DynValue.NewNumber(pickup.Distance));
                entry.Set("items", Items(pickup.Items));
                if (pickup.Drop is { } drop)
                    entry.Set("drop", DynValue.NewNumber(drop));
                if (pickup.Creation is { } c)
                    entry.Set("creation", DynValue.NewNumber(c));
                if (pickup.Part is { } p)
                    entry.Set("part", DynValue.NewNumber(p));
                list.Set(i++, DynValue.NewTable(entry));
            }
            return DynValue.NewTable(list);
        });
        Fn(part, "collect", args =>
        {
            Changing("part.collect");
            var instance = PartInstance();
            var target = args.AsType(0, "part.collect", DataType.Table).Table;
            var kindName = target.Get("kind") is { Type: DataType.String } k ? k.String : null;
            if (kindName is null || !Enum.TryParse<PickupKind>(kindName, ignoreCase: true, out var kind) || kindName.Any(char.IsUpper))
                throw new ScriptRuntimeException("bad argument #1 to 'part.collect' (a pickup from part.pickups() expected: kind is drop, loose, crop or water)");
            int? Id(string key) => target.Get(key) is { } v && !v.IsNil() ? (int)LuaValues.Number(v, $"part.collect ({key})", 1) : null;
            if (_transport is null)
                return Refused(instance, "item transport is off here");
            var result = _dispatcher.Dispatch(Caller, TransportActions.CollectItems,
                new CollectItemsArgs(instance.Creation, instance.Part, kind, Id("drop"), Id("creation"), Id("part")));
            if (!result.Ok)
                return Refused(instance, result.Message ?? result.Code ?? "refused");
            var moved = result.Value!.Value.Deserialize<CollectItemsResult>(ActionJson.Options)!;
            return DynValue.NewTuple(DynValue.True, Items(moved.Items));
        });
        Fn(part, "eject", args =>
        {
            Changing("part.eject");
            var instance = PartInstance();
            var item = args.Count > 0 && !args[0].IsNil() ? ItemId(instance, args.AsType(0, "part.eject", DataType.String).String) : null;
            var count = args.Count > 1 && !args[1].IsNil() ? Count(args[1], "part.eject", 2) : 1;
            if (_transport is null)
                return Refused(instance, "item transport is off here");
            var result = _dispatcher.Dispatch(Caller, TransportActions.EjectItems, new EjectItemsArgs(instance.Creation, instance.Part, item, count));
            if (!result.Ok)
                return Refused(instance, result.Message ?? result.Code ?? "refused");
            var ejected = result.Value!.Value.Deserialize<EjectItemsResult>(ActionJson.Options)!;
            var table = new Table(script);
            table.Set("item", DynValue.NewString(ejected.Item));
            table.Set("count", DynValue.NewNumber(ejected.Count));
            table.Set("drop", DynValue.NewNumber(ejected.Drop));
            return DynValue.NewTuple(DynValue.True, DynValue.NewTable(table));
        });
    }

    // A machine collects a ripe crop: the soil part's on_collect says what the harvest gives (and clears the soil); null
    // when it runs no script, its script is stopped or it gives nothing.
    private IReadOnlyList<ItemStack>? Harvest(Creation creation, int part)
    {
        Reconcile();
        if (!_instances.TryGetValue((creation.Id, part), out var instance) || instance.Fault is not null)
            return null;
        var values = CallHook(instance, Hooks.OnCollect);
        if (values is not { Length: > 0 } || values[0].Type != DataType.Table)
            return null;
        var items = new List<ItemStack>();
        JsonNode? plain;
        try
        {
            plain = LuaValues.ToJson(values[0], "on_collect");
        }
        catch (ArgumentException)
        {
            return null;
        }
        foreach (var entry in plain as JsonArray ?? [])
        {
            if (entry?["item"]?.GetValueKind() != JsonValueKind.String)
                continue;
            var count = entry["count"] is JsonValue c && c.TryGetValue<double>(out var n) ? (int)n : 1;
            var id = ItemId(instance, entry["item"]!.GetValue<string>());
            if (count > 0 && _catalog?.FindDefinition(id) is not null)
                items.Add(new ItemStack(id, count));
        }
        return items;
    }
}
