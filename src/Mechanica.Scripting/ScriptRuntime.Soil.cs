using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

// Generic primitives behind soil and other parts that hand out items (D2): world.item reads any item's data,
// the soil library reads and stores the soil part's own SoilState (how a crop grows is the script's call),
// and the player library gives items to a player's inventory, all or nothing.
public sealed partial class ScriptRuntime
{
    // Each definition's data as part JSON, made once per definition (a mod reload makes new definitions).
    private readonly ConditionalWeakTable<PartDefinition, JsonObject> _itemData = new();

    /// <summary>
    /// Item and part definitions, for <c>world.item</c>. <see cref="EnableItems"/> sets it; set it yourself to
    /// give part scripts item data without held items.
    /// </summary>
    public IPartCatalog? Catalog
    {
        get => _catalog;
        set => _catalog = value;
    }

    private void AddSoilLibraries(Script script, Table world)
    {
        Fn(world, "item", args =>
        {
            var instance = Current();
            var id = ItemId(instance, args.AsType(0, "world.item", DataType.String).String);
            var key = args.Count > 1 && !args[1].IsNil() ? args.AsType(1, "world.item", DataType.String).String : null;
            if (_catalog?.FindDefinition(id) is not { } definition)
                return DynValue.Nil;
            var data = _itemData.GetValue(definition, d => JsonSerializer.SerializeToNode(d, DataJson.WriteOptions)!.AsObject());
            return key is null ? LuaValues.FromJson(script, data) : LuaValues.FromJson(script, data[key]);
        });

        var soil = new Table(script);
        Fn(soil, "get", _ =>
        {
            if (OwnSoil("soil.get", mustExist: false) is not { } own)
                return DynValue.Nil;
            var state = own.Soil;
            var table = new Table(script);
            if (state.Seed is { } seed)
                table.Set("seed", DynValue.NewString(seed));
            table.Set("grown", DynValue.NewNumber(state.Grown));
            table.Set("water", DynValue.NewNumber(state.Water));
            table.Set("stage", DynValue.NewNumber(state.Stage));
            table.Set("stages", DynValue.NewNumber(SoilState.Stages));
            if (state.Updated is { } updated)
                table.Set("updated", DynValue.NewNumber(updated));
            return DynValue.NewTable(table);
        });
        Fn(soil, "set", args =>
        {
            Changing("soil.set");
            var (creation, state) = OwnSoil("soil.set", mustExist: true)!.Value;
            var values = args.AsType(0, "soil.set", DataType.Table).Table;
            float? Field(string name) => values.Get(name) is { } v && !v.IsNil() ? LuaValues.Number(v, $"soil.set ({name})", 1) : null;
            var (grown, water, stage) = (Field("grown"), Field("water"), Field("stage"));
            // A world.time() stamp stays a double: a float would lose its fraction after a few days of play.
            var updated = values.Get("updated") switch
            {
                { Type: DataType.Number } n => n.Number,
                var v when v.IsNil() => (double?)null,
                var v => throw new ScriptRuntimeException($"bad argument #1 to 'soil.set' (updated must be a number, got {LuaValues.Describe(v)})"),
            };
            if (stage is { } s && s != MathF.Floor(s))
                throw new ScriptRuntimeException("soil.set: stage must be a whole number");
            // Never more water than the soil holds (its capacity may have shrunk with a mod update).
            if (water is { } w)
                water = Math.Min(w, creation.DefinitionOf(PartInstance().Part).Soil?.Capacity ?? 0);
            try
            {
                state.Set(grown, water, stage is { } st ? (int)st : null, updated);
            }
            catch (ArgumentOutOfRangeException e)
            {
                throw new ScriptRuntimeException($"soil.set: {e.Message.Split(" (Parameter")[0]}");
            }
            return DynValue.Nil;
        });
        Fn(soil, "clear", _ =>
        {
            Changing("soil.clear");
            OwnSoil("soil.clear", mustExist: true)!.Value.Soil.Clear();
            return DynValue.Nil;
        });
        script.Globals.Set("soil", DynValue.NewTable(soil));

        var player = new Table(script);
        Fn(player, "can_give", args =>
        {
            var (peer, items) = GiveArgs(args, "player.can_give");
            return GiveRefusal(peer, items) is { } why ? DynValue.NewTuple(DynValue.False, DynValue.NewString(why)) : DynValue.True;
        });
        Fn(player, "give", args =>
        {
            Changing("player.give");
            var (peer, items) = GiveArgs(args, "player.give");
            if (GiveRefusal(peer, items) is { } why)
                return DynValue.NewTuple(DynValue.False, DynValue.NewString(why));
            _inventories!.PlayerInventory(peer).TryAddAll(items);
            return DynValue.True;
        });
        script.Globals.Set("player", DynValue.NewTable(player));
    }

    // Functions that change the world may not run in can_use, can_activate or an item's setup.
    private void Changing(string function)
    {
        if (_phase == ItemPhase.Checking)
            throw new ScriptRuntimeException($"{function} changes the world, which can_use, can_activate and an item script's setup may not do");
    }

    // The running part's Creation and soil; null once the part is gone (unless it must exist, which raises).
    private (Creation Creation, SoilState Soil)? OwnSoil(string function, bool mustExist)
    {
        var instance = PartInstance();
        if (_motion.FindCreation(instance.Creation) is not { } creation || creation.FindPart(instance.Part) is null)
            return mustExist ? throw new ScriptRuntimeException($"{function}: the part is gone") : null;
        return creation.SoilOf(instance.Part) is { } soil
            ? (creation, soil)
            : throw new ScriptRuntimeException($"{function}: the {creation.DefinitionOf(instance.Part).Name ?? instance.PartType} is not soil");
    }

    // (peer, {{item = id, count = n}, ...}) with bare ids in the script's own mod.
    private (int Peer, List<ItemAmount> Items) GiveArgs(CallbackArguments args, string function)
    {
        var instance = Current();
        if (_inventories is null || _survival is null)
            throw new ScriptRuntimeException($"{function}: items are not enabled on this host");
        // Peer ids are large (ENet's are up to 2^31): read the double, not a float, which would round them.
        var peerValue = args.AsType(0, function, DataType.Number).Number;
        if (peerValue != Math.Floor(peerValue) || peerValue < int.MinValue || peerValue > int.MaxValue)
            throw new ScriptRuntimeException($"bad argument #1 to '{function}' (a peer id expected)");
        var peer = (int)peerValue;
        var items = new List<ItemAmount>();
        foreach (var pair in args.AsType(1, function, DataType.Table).Table.Pairs)
        {
            if (pair.Value.Type != DataType.Table)
                throw new ScriptRuntimeException($"bad argument #2 to '{function}' (a list of {{item = id, count = n}} expected)");
            var entry = pair.Value.Table;
            if (entry.Get("item") is not { Type: DataType.String } item)
                throw new ScriptRuntimeException($"bad argument #2 to '{function}' (every entry needs an item id)");
            var id = ItemId(instance, item.String);
            if (_catalog?.FindDefinition(id) is null)
                throw new ScriptRuntimeException($"{function}: unknown item '{id}'");
            var count = entry.Get("count") is { } c && !c.IsNil() ? Count(c, function, 2) : 1;
            items.Add(new ItemAmount(id, count));
        }
        return (peer, items);
    }

    private string? GiveRefusal(int peer, List<ItemAmount> items)
    {
        if (!_survival!.Players.Contains(peer))
            return $"there is no player {peer}";
        return _inventories!.PlayerInventory(peer).CanAddAll(items) ? null : "there is no room for it";
    }
}
