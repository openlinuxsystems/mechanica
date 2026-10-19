using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

// Held items (chunk 20b, D2): an item (not a placeable part) whose definition names a script runs it while a player holds and
// uses it. Each player gets their own instance per item type, with its own state, made the first time
// they equip or use one. The host's UseItem and EquipItem actions call the hooks; the item, holder,
// inventory, combat and farm libraries act for the holding player.
public sealed partial class ScriptRuntime
{
    /// <summary>Action name for using the item in a slot (<see cref="UseItemArgs"/>).</summary>
    public const string UseItem = "UseItem";

    /// <summary>Action name for taking an item in hand, or emptying the hand (<see cref="EquipItemArgs"/>).</summary>
    public const string EquipItem = "EquipItem";

    /// <summary>Action name for reloading the item in a slot (<see cref="ReloadItemArgs"/>).</summary>
    public const string ReloadItem = "ReloadItem";

    /// <summary>Action name for starting or stopping aiming the item in hand (<see cref="AimItemArgs"/>).</summary>
    public const string AimItem = "AimItem";

    // Most pellets one combat.pellets call may make.
    private const int MaxPellets = 64;

    // Largest a projectile's look may be drawn (combat.projectile size), in metres.
    private const float MaxProjectileSize = 10f;

    private readonly SortedDictionary<(int Peer, string Item), Instance> _items = [];
    private readonly Dictionary<int, (string Item, int Slot)> _held = [];
    // Saved state for item scripts a player has not started yet this session (ImportItemStates).
    private readonly Dictionary<(int Peer, string Item), JsonObject> _savedItemStates = [];
    // Projectiles scripts launched: the item, part or mod script that hears on_hit, and its data.
    private readonly Dictionary<int, (Instance Owner, DynValue Data)> _flights = [];
    // True while on_hit runs for a landed projectile (see Reaching).
    private bool _landing;
    private readonly HashSet<int> _aiming = [];
    private IPartCatalog? _catalog;
    private SurvivalActions? _survival;
    private InventoryActions? _inventories;
    private ItemPhase _phase;

    /// <summary>
    /// An item script set what its holder's hotbar slot and HUD counter show (<c>item.counter</c>): the holder's peer
    /// and the view. The game sends it to that player.
    /// </summary>
    public event Action<int, ItemCounterView>? ItemCounterChanged;

    // What item hooks may do: read only (can_use, setup) or act (on_use, on_equip, on_unequip, on_hit).
    private enum ItemPhase
    {
        Acting,
        Checking,
    }

    /// <summary>
    /// Runs item scripts for the survival loop: registers <c>UseItem</c> and <c>EquipItem</c> on the
    /// dispatcher and calls <c>on_hit</c> when a projectile an item launched lands. Call once, on the host.
    /// </summary>
    public void EnableItems(IPartCatalog catalog, SurvivalActions survival, InventoryActions inventories)
    {
        if (_survival is not null)
            throw new InvalidOperationException("Items are already enabled.");
        (_catalog, _survival, _inventories) = (catalog, survival, inventories);
        _dispatcher.Register<UseItemArgs>(UseItem, ValidateUse, ApplyUse);
        _dispatcher.Register<EquipItemArgs>(EquipItem, ValidateEquip, ApplyEquip);
        _dispatcher.Register<ReloadItemArgs>(ReloadItem, ValidateReload, ApplyReload);
        _dispatcher.Register<AimItemArgs>(AimItem, ValidateAim, ApplyAim);
        survival.ProjectileLanded += OnProjectileLanded;
        survival.VitalsChanged += peer =>
        {
            if (survival.IsDead(peer))
                Unequip(peer);
        };
    }

    /// <summary>The item each player holds, by peer: its qualified id and slot.</summary>
    public IReadOnlyDictionary<int, (string Item, int Slot)> Held => _held;

    /// <summary>True while <paramref name="peer"/> aims the item in hand (right click held).</summary>
    public bool IsAiming(int peer) => _aiming.Contains(peer);

    /// <summary>Every held-item script instance, by player then item.</summary>
    public IReadOnlyList<ItemScriptInfo> ItemInstances =>
        [.. _items.Values.Select(i => new ItemScriptInfo(i.Holder!.Value, i.PartType, i.File.File.Key, i.Fault, i.Version))];

    // ---- Actions ----

    private string? ValidateUse(ActionCaller caller, UseItemArgs args)
    {
        if (CheckItem(caller, args.Slot, out var id, out var definition) is { } bad)
            return bad;
        var eye = _survival!.World.EyeOf(caller.Peer);
        if (eye is null)
            return "You have no player to use it with.";
        Vector3? direction = null;
        if (args.Direction is { } given)
        {
            var d = given.ToNumerics();
            if (!float.IsFinite(d.X) || !float.IsFinite(d.Y) || !float.IsFinite(d.Z) || d.LengthSquared() < 1e-6f)
                return "direction must be a non-zero vector.";
            direction = Vector3.Normalize(d);
            if (caller.Source == ActionSource.Player && _survival.World.LookOf(caller.Peer) is { } look
                && AngleDegrees(look, direction.Value) > _survival.Settings.AimTolerance)
                return "That is not where you are aiming.";
        }
        if ((args.Creation is null) != (args.Part is null))
            return "creation and part go together.";
        if (args.Creation is { } creationId && _survival.World.FindCreation(creationId)?.FindPart(args.Part!.Value) is null)
            return $"There is no part {args.Part} in creation {creationId}.";
        var instance = ItemInstance(caller, id!, args.Slot);
        if (instance?.Fault is { } fault)
            return $"The {definition!.Name ?? id}'s script is stopped: {fault}";
        if (instance is null)
            return $"The {definition!.Name ?? id} has no script to run.";
        var values = CallItem(instance, Hooks.CanUse, ItemPhase.Checking, Target(instance, eye.Value, direction ?? _survival.World.LookOf(caller.Peer), args));
        if (values is null)
            return $"The {definition!.Name ?? id}'s script is stopped: {instance.Fault}";
        return values.Length > 0 && values[0].Type == DataType.String ? values[0].String
            : values.Length > 0 && values[0].Type == DataType.Boolean && !values[0].Boolean ? $"You cannot use the {definition!.Name ?? id} now."
            : null;
    }

    private JsonElement? ApplyUse(ActionCaller caller, UseItemArgs args)
    {
        var id = Identifiers.Qualify(_inventories!.PlayerInventory(caller.Peer)[args.Slot]!.Item);
        Equip(caller, id, args.Slot);
        var instance = ItemInstance(caller, id, args.Slot)!;
        var eye = _survival!.World.EyeOf(caller.Peer)!.Value;
        var direction = args.Direction is { } d ? Vector3.Normalize(d.ToNumerics()) : _survival.World.LookOf(caller.Peer);
        var values = CallItem(instance, Hooks.OnUse, ItemPhase.Acting, Target(instance, eye, direction, args))
            ?? throw new InvalidOperationException($"the {instance.PartType} script failed: {instance.Fault}");
        if (values.Length == 0 || values[0].IsNil())
            return null;
        try
        {
            return LuaValues.ToJson(values[0], "on_use result") is { } node ? JsonSerializer.SerializeToElement(node) : null;
        }
        catch (ArgumentException e)
        {
            Post(ScriptMessageKind.Warning, instance.File.File.Key, null, null, $"on_use returned something that is not plain data: {e.Message}");
            return null;
        }
    }

    private string? ValidateEquip(ActionCaller caller, EquipItemArgs args)
    {
        if (_survival!.World.EyeOf(caller.Peer) is null)
            return "You have no player to hold it.";
        return args.Slot is { } slot ? CheckItem(caller, slot, out _, out _) : null;
    }

    private JsonElement? ApplyEquip(ActionCaller caller, EquipItemArgs args)
    {
        if (args.Slot is { } slot)
            Equip(caller, Identifiers.Qualify(_inventories!.PlayerInventory(caller.Peer)[slot]!.Item), slot);
        else
            Unequip(caller.Peer);
        return null;
    }

    private string? ValidateReload(ActionCaller caller, ReloadItemArgs args)
    {
        if (CheckItem(caller, args.Slot, out var id, out var definition) is { } bad)
            return bad;
        if (_survival!.World.EyeOf(caller.Peer) is null)
            return "You have no player to reload with.";
        var instance = ItemInstance(caller, id!, args.Slot);
        if (instance?.Fault is { } fault)
            return $"The {definition!.Name ?? id}'s script is stopped: {fault}";
        if (instance?.Env?.RawGet(Hooks.OnReloadItem) is not { Type: DataType.Function })
            return $"The {definition!.Name ?? id} does not reload.";
        var values = CallItem(instance, Hooks.CanReloadItem, ItemPhase.Checking);
        if (values is null)
            return $"The {definition!.Name ?? id}'s script is stopped: {instance.Fault}";
        return values.Length > 0 && values[0].Type == DataType.String ? values[0].String
            : values.Length > 0 && values[0].Type == DataType.Boolean && !values[0].Boolean ? $"You cannot reload the {definition!.Name ?? id} now."
            : null;
    }

    private JsonElement? ApplyReload(ActionCaller caller, ReloadItemArgs args)
    {
        var id = Identifiers.Qualify(_inventories!.PlayerInventory(caller.Peer)[args.Slot]!.Item);
        Equip(caller, id, args.Slot);
        var instance = ItemInstance(caller, id, args.Slot)!;
        if (CallItem(instance, Hooks.OnReloadItem, ItemPhase.Acting) is null)
            throw new InvalidOperationException($"the {instance.PartType} script failed: {instance.Fault}");
        return null;
    }

    private string? ValidateAim(ActionCaller caller, AimItemArgs args) =>
        !args.On || _held.ContainsKey(caller.Peer) ? null : "You have nothing in hand to aim.";

    private JsonElement? ApplyAim(ActionCaller caller, AimItemArgs args)
    {
        if (!(args.On ? _aiming.Add(caller.Peer) : _aiming.Remove(caller.Peer)))
            return null;
        if (_held.TryGetValue(caller.Peer, out var held) && _items.TryGetValue((caller.Peer, held.Item), out var instance) && instance.Fault is null)
            CallItem(instance, Hooks.OnAim, ItemPhase.Acting, DynValue.NewBoolean(args.On));
        return null;
    }

    // on_update(dt) for the item each living player holds, after the parts' on_update.
    private void UpdateHeldItems(DynValue dt)
    {
        if (_survival is null || _held.Count == 0)
            return;
        _stepping.Clear();
        foreach (var (peer, held) in _held)
        {
            if (!_survival.IsDead(peer) && _items.TryGetValue((peer, held.Item), out var instance) && instance.Fault is null)
                _stepping.Add(instance);
        }
        foreach (var instance in _stepping)
        {
            if (instance.Fault is null && _held.TryGetValue(instance.Holder!.Value, out var held) && held.Item == instance.PartType)
                CallItem(instance, Hooks.OnUpdate, ItemPhase.Acting, dt);
        }
        _stepping.Clear();
    }

    // The slot holds an item that runs a script.
    private string? CheckItem(ActionCaller caller, int slot, out string? id, out PartDefinition? definition)
    {
        id = null;
        definition = null;
        var pockets = _inventories!.PlayerInventory(caller.Peer);
        if (slot < 0 || slot >= pockets.SlotCount)
            return $"slot must be 0 to {pockets.SlotCount - 1} (got {slot}).";
        if (pockets[slot] is not { } stack)
            return $"Slot {slot} is empty.";
        id = Identifiers.Qualify(stack.Item);
        definition = _catalog!.FindDefinition(id);
        if (definition is null)
            return $"Unknown item '{stack.Item}'.";
        // A placeable part's script runs once the part is placed, not in a player's hand.
        return definition.Script is null || definition.IsPlaceable ? $"{definition.Name ?? id} cannot be used." : null;
    }

    // Takes an item in hand: on_unequip for what was held, on_equip for the new one. The same item in
    // the same slot changes nothing.
    private void Equip(ActionCaller caller, string id, int slot)
    {
        if (_held.TryGetValue(caller.Peer, out var held) && held == (id, slot))
            return;
        Unequip(caller.Peer);
        _held[caller.Peer] = (id, slot);
        if (ItemInstance(caller, id, slot) is { } instance)
            CallItem(instance, Hooks.OnEquip, ItemPhase.Acting);
    }

    private void Unequip(int peer)
    {
        _aiming.Remove(peer);
        if (!_held.Remove(peer, out var held))
            return;
        if (_items.TryGetValue((peer, held.Item), out var instance))
            CallItem(instance, Hooks.OnUnequip, ItemPhase.Acting);
    }

    // The holder's instance of an item's script, started on first use; null when the item has none.
    private Instance? ItemInstance(ActionCaller caller, string id, int slot)
    {
        var file = ScriptOf(id);
        if (_items.TryGetValue((caller.Peer, id), out var instance) && (file is null || instance.File.File.Key != file.Key))
        {
            _items.Remove((caller.Peer, id));
            instance = null;
        }
        if (file is null)
            return null;
        if (instance is null)
        {
            var entry = EntryFor(file);
            instance = new Instance(-1, -1, id, entry) { Holder = caller.Peer, Caller = caller, Slot = slot };
            // A returning player's state from the save is there before the script's setup runs.
            if (_savedItemStates.Remove((caller.Peer, id), out var saved))
                instance.State = LuaValues.FromJson(entry.Context.Script, saved).Table;
            _items[(caller.Peer, id)] = instance;
            Start(instance);
        }
        instance.Caller = caller;
        instance.Slot = slot;
        return instance;
    }

    private DynValue[]? CallItem(Instance instance, string hook, ItemPhase phase, params DynValue[] args)
    {
        var previous = _phase;
        _phase = phase;
        try
        {
            return CallHook(instance, hook, args);
        }
        finally
        {
            _phase = previous;
        }
    }

    // What on_use and can_use are aimed at: {from, direction, creation?, part?}.
    private DynValue Target(Instance instance, Vector3 eye, Vector3? direction, UseItemArgs args)
    {
        var script = instance.File.Context.Script;
        var target = new Table(script);
        target.Set("from", LuaValues.Vector(script, instance.File.Context.Vec3Meta, eye));
        target.Set("direction", LuaValues.Vector(script, instance.File.Context.Vec3Meta, direction ?? -Vector3.UnitZ));
        target.Set("aiming", DynValue.NewBoolean(_aiming.Contains(instance.Holder!.Value)));
        if (args is { Creation: { } creation, Part: { } part })
        {
            target.Set("creation", DynValue.NewNumber(creation));
            target.Set("part", DynValue.NewNumber(part));
        }
        return DynValue.NewTable(target);
    }

    /// <summary>
    /// The <c>state</c> tables of <paramref name="peer"/>'s item scripts by qualified item id, for saves: the scripts they
    /// ran this session and the saved state of ones they have not used yet. Empty tables are left out.
    /// </summary>
    /// <exception cref="ArgumentException">A state holds something other than plain data.</exception>
    public IReadOnlyDictionary<string, JsonObject> ExportItemStates(int peer)
    {
        var states = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var ((holder, item), saved) in _savedItemStates)
        {
            if (holder == peer)
                states[item] = saved.DeepClone().AsObject();
        }
        foreach (var ((holder, item), instance) in _items)
        {
            if (holder == peer && instance.State is { } state && LuaValues.ToJson(DynValue.NewTable(state)) is JsonObject json && json.Count > 0)
                states[item] = json;
        }
        return states;
    }

    /// <summary>
    /// Gives <paramref name="peer"/> saved item script state (a returning player): a script they already run takes it now,
    /// the others when they first use the item. Replaces what was waiting for that peer.
    /// </summary>
    public void ImportItemStates(int peer, IReadOnlyDictionary<string, JsonObject> states)
    {
        ForgetItemStates(peer);
        foreach (var (item, state) in states)
        {
            if (_items.TryGetValue((peer, item), out var instance) && instance.Env is not null)
            {
                instance.State = LuaValues.FromJson(instance.File.Context.Script, state).Table;
                instance.Env.Set("state", DynValue.NewTable(instance.State));
            }
            else
            {
                _savedItemStates[(peer, item)] = state.DeepClone().AsObject();
            }
        }
    }

    /// <summary>Drops the saved item script state waiting for <paramref name="peer"/> (they left; the id may come back as someone else).</summary>
    public void ForgetItemStates(int peer)
    {
        foreach (var key in _savedItemStates.Keys.Where(k => k.Peer == peer).ToList())
            _savedItemStates.Remove(key);
    }

    // Players who left lose their item scripts (no hooks run: they are gone).
    private void ReconcileItems()
    {
        if (_survival is null || _items.Count == 0 && _held.Count == 0)
            return;
        var players = _survival.Players.ToHashSet();
        foreach (var key in _items.Keys.Where(k => !players.Contains(k.Peer)).ToList())
            _items.Remove(key);
        foreach (var peer in _held.Keys.Where(p => !players.Contains(p)).ToList())
            _held.Remove(peer);
        _aiming.RemoveWhere(p => !players.Contains(p));
    }

    private void ClearItems()
    {
        _items.Clear();
        _savedItemStates.Clear();
        _held.Clear();
        _aiming.Clear();
        _flights.Clear();
    }

    private void OnProjectileLanded(ProjectileLanding landing)
    {
        if (!_flights.Remove(landing.Id, out var flight) || flight.Owner is not { Fault: null } instance || !IsRunning(instance))
            return;
        var script = instance.File.Context.Script;
        var table = new Table(script);
        table.Set("id", DynValue.NewNumber(landing.Id));
        table.Set("position", LuaValues.Vector(script, instance.File.Context.Vec3Meta, landing.Point));
        if (landing.Hit is { } hit)
            table.Set("hit", HitTable(instance.File.Context, hit, null));
        table.Set("data", flight.Data);
        _landing = true;
        try
        {
            CallItem(instance, Hooks.OnHit, ItemPhase.Acting, DynValue.NewTable(table));
        }
        finally
        {
            _landing = false;
        }
    }

    // Still the script it was: an item its holder still has, a part still placed, a mod script still listed.
    private bool IsRunning(Instance instance) =>
        instance.IsItem ? _items.TryGetValue((instance.Holder!.Value, instance.PartType), out var item) && item == instance
        : instance.IsWorld ? _worlds.Contains(instance)
        : _instances.TryGetValue((instance.Creation, instance.Part), out var part) && part == instance;

    // ---- Libraries ----

    private void AddItemLibraries(LuaContext context)
    {
        var script = context.Script;
        var globals = script.Globals;
        DynValue Vec(Vector3 v) => LuaValues.Vector(script, context.Vec3Meta, v);

        var item = new Table(script);
        Fn(item, "id", _ => DynValue.NewString(ItemNow().PartType));
        Fn(item, "name", args =>
        {
            var instance = ItemNow();
            var id = args.Count > 0 && !args[0].IsNil() ? ItemId(instance, args.AsType(0, "item.name", DataType.String).String) : instance.PartType;
            return _catalog!.FindDefinition(id) is { } d ? DynValue.NewString(d.Name ?? id) : DynValue.Nil;
        });
        Fn(item, "slot", _ => DynValue.NewNumber(ItemNow().Slot));
        Fn(item, "count", _ =>
        {
            var instance = ItemNow();
            var stack = Pockets(instance)[instance.Slot];
            return DynValue.NewNumber(stack is not null && Identifiers.Qualify(stack.Item) == instance.PartType ? stack.Count : 0);
        });
        Fn(item, "consume", args =>
        {
            var instance = Acting("item.consume");
            var count = args.Count > 0 && !args[0].IsNil() ? Count(args[0], "item.consume", 1) : 1;
            var pockets = Pockets(instance);
            if (pockets[instance.Slot] is not { } stack || Identifiers.Qualify(stack.Item) != instance.PartType || stack.Count < count)
                return DynValue.False;
            pockets.SetSlot(instance.Slot, stack.Count > count ? stack with { Count = stack.Count - count } : null);
            return DynValue.True;
        });
        Fn(item, "replace", args =>
        {
            var instance = Acting("item.replace");
            var id = ItemId(instance, args.AsType(0, "item.replace", DataType.String).String);
            if (_catalog!.FindDefinition(id) is null)
                throw new ScriptRuntimeException($"item.replace: unknown item '{id}'");
            var pockets = Pockets(instance);
            if (pockets[instance.Slot] is not { } stack || Identifiers.Qualify(stack.Item) != instance.PartType)
                return DynValue.False;
            // The last one turns into the new item where it is (a bucket stays in its hotbar slot); one of several makes room.
            if (stack.Count == 1)
            {
                pockets.SetSlot(instance.Slot, new ItemStack(id, 1));
                return DynValue.True;
            }
            if (pockets.SpaceFor(id) < 1)
                return DynValue.False;
            pockets.SetSlot(instance.Slot, stack with { Count = stack.Count - 1 });
            pockets.Add(id, 1);
            return DynValue.True;
        });
        Fn(item, "counter", args =>
        {
            var instance = Acting("item.counter");
            var table = args.AsType(0, "item.counter", DataType.Table).Table;
            int? Whole(string key)
            {
                var v = table.Get(key);
                if (v.IsNil())
                    return null;
                var n = LuaValues.Number(v, $"item.counter ({key})", 1);
                if (n < 0 || n != MathF.Floor(n))
                    throw new ScriptRuntimeException($"item.counter: {key} must be a whole number of 0 or more");
                return (int)n;
            }
            var ammo = table.Get("ammo") is { Type: DataType.String } a ? ItemId(instance, a.String) : null;
            var busyValue = table.Get("busy");
            var busy = busyValue.IsNil() ? 0f : LuaValues.Number(busyValue, "item.counter (busy)", 1);
            if (!(busy >= 0f) || !float.IsFinite(busy))
                throw new ScriptRuntimeException("item.counter: busy must be 0 or more seconds");
            ItemCounterChanged?.Invoke(instance.Holder!.Value, new ItemCounterView(instance.PartType, Whole("value"), Whole("max"), ammo, busy));
            return DynValue.Nil;
        });
        Fn(item, "sound", args =>
        {
            var instance = ItemNow();
            var key = args.AsType(0, "item.sound", DataType.String).String;
            return _catalog!.FindDefinition(instance.PartType)?.Sounds?.GetValueOrDefault(key) is { } id ? DynValue.NewString(id) : DynValue.Nil;
        });
        globals.Set("item", DynValue.NewTable(item));

        var holder = new Table(script);
        Fn(holder, "peer", _ => DynValue.NewNumber(ItemNow().Holder!.Value));
        Fn(holder, "eye", _ => _survival!.World.EyeOf(ItemNow().Holder!.Value) is { } v ? Vec(v) : DynValue.Nil);
        Fn(holder, "feet", _ => _survival!.World.FeetOf(ItemNow().Holder!.Value) is { } v ? Vec(v) : DynValue.Nil);
        Fn(holder, "look", _ => _survival!.World.LookOf(ItemNow().Holder!.Value) is { } v ? Vec(v) : DynValue.Nil);
        Fn(holder, "health", _ => _survival!.HealthOf(ItemNow().Holder!.Value) is { } h ? DynValue.NewNumber(h) : DynValue.Nil);
        Fn(holder, "max_health", _ =>
        {
            ItemNow();
            return DynValue.NewNumber(_survival!.Settings.MaxHealth);
        });
        Fn(holder, "heal", args =>
        {
            var instance = Acting("holder.heal");
            return DynValue.NewNumber(_survival!.Heal(instance.Holder!.Value, LuaValues.Number(args[0], "holder.heal", 1)));
        });
        Fn(holder, "aiming", _ => DynValue.NewBoolean(_aiming.Contains(ItemNow().Holder!.Value)));
        globals.Set("holder", DynValue.NewTable(holder));

        var inventory = new Table(script);
        Fn(inventory, "count", args =>
        {
            var instance = ItemNow();
            return DynValue.NewNumber(Pockets(instance).Count(ItemId(instance, args.AsType(0, "inventory.count", DataType.String).String)));
        });
        Fn(inventory, "take", args =>
        {
            var instance = Acting("inventory.take");
            var id = ItemId(instance, args.AsType(0, "inventory.take", DataType.String).String);
            var count = args.Count > 1 && !args[1].IsNil() ? Count(args[1], "inventory.take", 2) : 1;
            var pockets = Pockets(instance);
            if (pockets.Count(id) < count)
                return DynValue.False;
            pockets.Remove(id, count);
            return DynValue.True;
        });
        globals.Set("inventory", DynValue.NewTable(inventory));

        var combat = new Table(script);
        // cast, hit and hitscan also work in part scripts (a saw blade, a drill, a mounted gun): they act as
        // the host's scripts and skip no player.
        Fn(combat, "cast", args =>
        {
            var (_, _, ignore) = Combatant("combat.cast", acting: false);
            var (from, direction, range) = Ray(args, "combat.cast");
            if (_survival!.Cast(from, direction, range, ignore) is not { } hit)
                return DynValue.Nil;
            var table = HitTable(context, hit, from);
            // What it hit: a part's qualified type, or a world object's kind (birch_tree), so a tool can bite each its own way.
            var type = hit.Creation is { } c && hit.Part is { } p && _survival.World.FindCreation(c)?.FindPart(p) is { } instance
                ? Identifiers.Qualify(instance.Part)
                : hit.Object is { } id ? _survival.World.FindObject(id)?.Definition.Id : null;
            if (type is not null)
                table.Table.Set("type", DynValue.NewString(type));
            return table;
        });
        Fn(combat, "hit", args =>
        {
            var (_, caller, _) = Combatant("combat.hit", acting: true);
            var hit = ToRayHit(args.AsType(0, "combat.hit", DataType.Table).Table, "combat.hit");
            var damage = LuaValues.Number(args[1], "combat.hit", 2);
            var push = args.Count > 2 && !args[2].IsNil() ? LuaValues.ToVector(args[2], "combat.hit", 3) : Vector3.Zero;
            var kind = DamageKind(args, 3, "combat.hit");
            return ViewTable(context, _survival!.Hit(caller, hit, damage, push, kind));
        });
        Fn(combat, "hitscan", args =>
        {
            var (_, caller, ignore) = Combatant("combat.hitscan", acting: true);
            var (from, direction, range) = Ray(args, "combat.hitscan");
            var damage = LuaValues.Number(args[3], "combat.hitscan", 4);
            var impulse = LuaValues.OptionalNumber(args[4], "combat.hitscan", 5) ?? 0f;
            if (_survival!.Cast(from, direction, range, ignore) is not { } hit)
                return DynValue.Nil;
            return ViewTable(context, _survival.Hit(caller, hit, damage, Vector3.Normalize(direction) * impulse));
        });
        Fn(combat, "splash", args =>
        {
            var (_, caller, _) = Combatant("combat.splash", acting: true);
            var at = LuaValues.ToVector(args[0], "combat.splash", 1);
            var radius = LuaValues.Number(args[1], "combat.splash", 2);
            if (radius <= 0f || radius > _settings.MaxOverlapRadius)
                throw new ScriptRuntimeException($"combat.splash: radius must be more than 0 and at most {_settings.MaxOverlapRadius:0.#} m");
            var damage = LuaValues.Number(args[2], "combat.splash", 3);
            var impulse = LuaValues.OptionalNumber(args[3], "combat.splash", 4) ?? 0f;
            var list = new Table(script);
            var index = 1;
            foreach (var view in _survival!.Splash(caller, at, radius, damage, impulse))
                list.Set(index++, ViewTable(context, view));
            return DynValue.NewTable(list);
        });
        Fn(combat, "pellets", args =>
        {
            var aim = LuaValues.ToVector(args[0], "combat.pellets", 1);
            if (aim.LengthSquared() < 1e-12f)
                throw new ScriptRuntimeException("bad argument #1 to 'combat.pellets' (non-zero vector expected)");
            var count = (int)LuaValues.Number(args[1], "combat.pellets", 2);
            if (count < 1 || count > MaxPellets)
                throw new ScriptRuntimeException($"combat.pellets: count must be 1 to {MaxPellets}");
            var spread = LuaValues.Number(args[2], "combat.pellets", 3);
            if (!(spread is >= 0f and < 90f))
                throw new ScriptRuntimeException("combat.pellets: spread must be 0 or more and under 90 degrees");
            var list = new Table(script);
            var index = 1;
            foreach (var direction in SurvivalActions.Pellets(Vector3.Normalize(aim), count, spread))
                list.Set(index++, Vec(direction));
            return DynValue.NewTable(list);
        });
        Fn(combat, "projectile", args =>
        {
            var (instance, _, ignore) = Combatant("combat.projectile", acting: true);
            var from = LuaValues.ToVector(args[0], "combat.projectile", 1);
            var velocity = LuaValues.ToVector(args[1], "combat.projectile", 2);
            var options = args.Count > 2 && !args[2].IsNil() ? args.AsType(2, "combat.projectile", DataType.Table).Table : null;
            var gravity = options?.Get("gravity") is { } g && !g.IsNil() ? LuaValues.ToVector(g, "combat.projectile (gravity)", 3) : _physics.Gravity;
            var lifetime = options?.Get("lifetime") is { } l && !l.IsNil() ? LuaValues.Number(l, "combat.projectile (lifetime)", 3) : _settings.MaxProjectileLifetime;
            if (lifetime <= 0f || lifetime > _settings.MaxProjectileLifetime)
                throw new ScriptRuntimeException($"combat.projectile: lifetime must be more than 0 and at most {_settings.MaxProjectileLifetime:0.#} s");
            if (_flights.Count >= _settings.MaxProjectiles)
                return Refused(instance, $"there are already {_settings.MaxProjectiles} projectiles in flight");
            var look = options?.Get("look") is { Type: DataType.String } k ? k.String : null;
            if ((look is null && options?.Get("look") is { } bad && !bad.IsNil()) || (look is not null && !ProjectileView.Looks.Contains(look)))
                throw new ScriptRuntimeException($"combat.projectile: look must be one of {string.Join(", ", ProjectileView.Looks)}");
            float? size = options?.Get("size") is { } z && !z.IsNil() ? LuaValues.Number(z, "combat.projectile (size)", 3) : null;
            if (size is { } sz && !(sz is > 0f and <= MaxProjectileSize))
                throw new ScriptRuntimeException($"combat.projectile: size must be more than 0 and at most {MaxProjectileSize:0} m");
            var data = options?.Get("data") ?? DynValue.Nil;
            var view = _survival!.Launch(ignore, from, velocity, gravity, lifetime, look, size);
            _flights[view.Id] = (instance, data);
            return DynValue.NewNumber(view.Id);
        });
        globals.Set("combat", DynValue.NewTable(combat));

        var farm = new Table(script);
        Fn(farm, "soil", args =>
        {
            var (_, caller, _) = Combatant("farm.soil", acting: false);
            var (creation, part) = SoilArgs(args, "farm.soil");
            return _survival!.CheckSoil(caller, creation, part) is { } bad ? NilBecause(bad)
                : SoilTable(script, _survival.SoilViewOf(_survival.World.FindCreation(creation)!, part));
        });
        Fn(farm, "plant", args =>
        {
            var instance = Acting("farm.plant");
            var (creation, part) = SoilArgs(args, "farm.plant");
            var seed = args.Count > 2 && !args[2].IsNil() ? ItemId(instance, args.AsType(2, "farm.plant", DataType.String).String) : instance.PartType;
            return _survival!.CheckPlant(instance.Caller, creation, part, seed) is { } bad ? NilBecause(bad)
                : SoilTable(script, _survival.PlantSeed(instance.Caller, creation, part, seed));
        });
        Fn(farm, "water", args =>
        {
            var (_, caller, _) = Combatant("farm.water", acting: true);
            caller = Reaching(caller);
            var (creation, part) = SoilArgs(args, "farm.water");
            var seconds = LuaValues.Number(args[2], "farm.water", 3);
            if (!(seconds > 0f) || !float.IsFinite(seconds))
                throw new ScriptRuntimeException("farm.water: seconds must be more than 0");
            return _survival!.CheckWater(caller, creation, part) is { } bad ? NilBecause(bad)
                : SoilTable(script, _survival.WaterSoil(caller, creation, part, seconds));
        });
        Fn(farm, "near", args =>
        {
            Combatant("farm.near", acting: false);
            var at = LuaValues.ToVector(args[0], "farm.near", 1);
            var radius = LuaValues.Number(args[1], "farm.near", 2);
            if (!(radius > 0f) || radius > _settings.MaxOverlapRadius)
                throw new ScriptRuntimeException($"farm.near: radius must be more than 0 and at most {_settings.MaxOverlapRadius:0.#} m");
            var list = new Table(script);
            var index = 1;
            foreach (var (creation, part) in _survival!.SoilNear(at, radius))
            {
                var entry = new Table(script);
                entry.Set("creation", DynValue.NewNumber(creation));
                entry.Set("part", DynValue.NewNumber(part));
                list.Set(index++, DynValue.NewTable(entry));
            }
            return DynValue.NewTable(list);
        });
        globals.Set("farm", DynValue.NewTable(farm));
    }

    private static (int Creation, int Part) SoilArgs(CallbackArguments args, string function) =>
        ((int)LuaValues.Number(args[0], function, 1), (int)LuaValues.Number(args[1], function, 2));

    // nil plus why, the way farm functions refuse.
    private static DynValue NilBecause(string reason) => DynValue.NewTuple(DynValue.Nil, DynValue.NewString(reason));

    // A soil part as scripts see it: {seed?, crop?, stage, ripe, water, capacity}.
    private static DynValue SoilTable(Script script, SoilView view)
    {
        var table = new Table(script);
        if (view.Seed is { } seed)
            table.Set("seed", DynValue.NewString(seed));
        if (view.Crop is { } crop)
            table.Set("crop", DynValue.NewString(crop));
        table.Set("stage", DynValue.NewNumber(view.Stage));
        table.Set("ripe", DynValue.NewBoolean(view.Ripe));
        table.Set("water", DynValue.NewNumber(view.Water));
        table.Set("capacity", DynValue.NewNumber(view.Capacity));
        return DynValue.NewTable(table);
    }

    // Who acts for a combat or farm function that part scripts may call too: an item acts for its holder
    // (and its rays skip them), a part as the host's scripts (skipping no player; 0 is never a peer).
    // Where a projectile lands the host decided, not the player: water from it reaches past the holder's reach.
    private ActionCaller Reaching(ActionCaller caller) => _landing ? caller with { Source = ActionSource.Script } : caller;

    private (Instance Instance, ActionCaller Caller, int Ignore) Combatant(string function, bool acting)
    {
        if (_current is { IsItem: false } part)
        {
            if (_survival is null)
                throw new ScriptRuntimeException($"{function}: combat and farming are not enabled on this host");
            if (acting)
                Changing(function);
            return (part, Caller, 0);
        }
        var instance = acting ? Acting(function) : ItemNow();
        return (instance, instance.Caller, instance.Holder!.Value);
    }

    private Instance ItemNow()
    {
        if (_current is not { IsItem: true } instance)
            throw new ScriptRuntimeException("this function only works inside an item's hook");
        if (_survival is null)
            throw new ScriptRuntimeException("items are not enabled on this host");
        return instance;
    }

    // An item hook that may change the world.
    private Instance Acting(string function)
    {
        var instance = ItemNow();
        if (_phase == ItemPhase.Checking)
            throw new ScriptRuntimeException($"{function} changes the world, which can_use and the script's setup may not do; do it in on_use");
        return instance;
    }

    private Inventory Pockets(Instance instance) => _inventories!.PlayerInventory(instance.Holder!.Value);

    // An item id as a script writes it: bare ids are in the item's own mod.
    private static string ItemId(Instance instance, string id) => Identifiers.Qualify(id, instance.PartType.Split(':')[0]);

    private static int Count(DynValue value, string function, int argument)
    {
        var n = LuaValues.Number(value, function, argument);
        if (n < 1 || n != MathF.Floor(n))
            throw new ScriptRuntimeException($"bad argument #{argument} to '{function}' (a whole number of 1 or more expected)");
        return (int)n;
    }

    private (Vector3 From, Vector3 Direction, float Range) Ray(CallbackArguments args, string function)
    {
        var from = LuaValues.ToVector(args[0], function, 1);
        var direction = LuaValues.ToVector(args[1], function, 2);
        var range = LuaValues.Number(args[2], function, 3);
        if (direction.LengthSquared() < 1e-12f)
            throw new ScriptRuntimeException($"bad argument #2 to '{function}' (non-zero vector expected)");
        if (range <= 0f || range > _settings.MaxRayLength)
            throw new ScriptRuntimeException($"{function}: range must be more than 0 and at most {_settings.MaxRayLength:0.#} m");
        return (from, Vector3.Normalize(direction), range);
    }

    // What a ray hit, as scripts see it: {kind, position, distance?, player?, creation?, part?, object?, bot?}.
    private static DynValue HitTable(LuaContext context, RayHit hit, Vector3? from)
    {
        var table = new Table(context.Script);
        table.Set("kind", DynValue.NewString(hit.Bot is not null ? "bot" : hit.Player is not null ? "player" : hit.Creation is not null && hit.Part is not null ? "part"
            : hit.Object is not null ? "object" : "ground"));
        table.Set("position", LuaValues.Vector(context.Script, context.Vec3Meta, hit.Point));
        if (from is { } start)
            table.Set("distance", DynValue.NewNumber(Vector3.Distance(start, hit.Point)));
        if (hit.Player is { } peer)
            table.Set("player", DynValue.NewNumber(peer));
        if (hit.Creation is { } creation)
            table.Set("creation", DynValue.NewNumber(creation));
        if (hit.Part is { } part)
            table.Set("part", DynValue.NewNumber(part));
        if (hit.Object is { } id)
            table.Set("object", DynValue.NewString(id.ToString()));
        if (hit.Bot is { } bot)
            table.Set("bot", DynValue.NewNumber(bot));
        return DynValue.NewTable(table);
    }

    private static RayHit ToRayHit(Table table, string function)
    {
        var position = LuaValues.ToVector(table.Get("position"), function, 1);
        static int? Int(DynValue v) => v.Type == DataType.Number ? (int)v.Number : null;
        WorldObjectId? id = table.Get("object") is { Type: DataType.String } text && WorldObjectId.TryParse(text.String, out var parsed) ? parsed : null;
        return new RayHit(position, Int(table.Get("player")), Int(table.Get("creation")), Int(table.Get("part")), id, Int(table.Get("bot")));
    }

    // What a hit did: the hit table plus the damage dealt.
    private static DynValue ViewTable(LuaContext context, ShotHitView view)
    {
        var table = new Table(context.Script);
        table.Set("kind", DynValue.NewString(view.Kind));
        table.Set("position", LuaValues.Vector(context.Script, context.Vec3Meta, view.Point.ToNumerics()));
        table.Set("damage", DynValue.NewNumber(view.Damage));
        if (view.Player is { } peer)
            table.Set("player", DynValue.NewNumber(peer));
        if (view.Creation is { } creation)
            table.Set("creation", DynValue.NewNumber(creation));
        if (view.Part is { } part)
            table.Set("part", DynValue.NewNumber(part));
        if (view.Object is { } id)
            table.Set("object", DynValue.NewString(id));
        if (view.Bot is { } bot)
            table.Set("bot", DynValue.NewNumber(bot));
        return DynValue.NewTable(table);
    }

    private static float AngleDegrees(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)) * 180f / MathF.PI;

    // The optional damage kind a hit names (a drill's "drill"): what world objects take it (World.DamageKinds).
    private static string DamageKind(CallbackArguments args, int index, string fn)
    {
        if (args.Count <= index || args[index].IsNil())
            return Mechanica.Core.World.DamageKinds.Weapon;
        if (args[index].Type != DataType.String || !Mechanica.Core.World.DamageKinds.All.Contains(args[index].String))
            throw new ScriptRuntimeException($"{fn}: argument {index + 1} must be a damage kind ({string.Join(", ", Mechanica.Core.World.DamageKinds.All)})");
        return args[index].String;
    }
}
