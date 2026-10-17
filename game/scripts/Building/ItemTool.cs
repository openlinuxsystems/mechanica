using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Game.Items;
using Mechanica.Game.Survival;
using Mechanica.Game.Ui;
using Mechanica.Scripting;

namespace Mechanica.Game.Building;

/// <summary>
/// The item in the local player's hand (chunk 20): Q and E pick one of the usable items in their
/// inventory (food, weapons, seeds, water), left click uses it and G drops one on the ground.
/// <list type="bullet">
/// <item>An item with a script (food, weapons, seeds, water; chunk 20b): <c>UseItem</c> where the crosshair
/// points, with the part under it when that is in reach (the soil a seed is planted in). Its Lua decides
/// what that does on the host. While this tool is out, <c>EquipItem</c> tells the host which scripted
/// item is in hand (its <c>on_equip</c> and <c>on_unequip</c>).</item>
/// <item>Aimed at the sea with water in hand or nothing usable: <c>CollectWater</c> fills a bucket.</item>
/// </list>
/// The host decides every one; nothing changes here until it answers.
/// </summary>
public partial class ItemTool : HandTool
{
    private string? _chosen;
    // The scripted item the host was last told is in hand: (item, slot), or null for none.
    private (string Item, int Slot)? _equipped;

    /// <summary>Inventories, for what the player carries. Set before adding to the tree.</summary>
    public InventorySync Inventory { get; init; } = null!;

    /// <summary>Survival state and rules. Set before adding to the tree.</summary>
    public SurvivalSync Survival { get; init; } = null!;

    /// <inheritdoc/>
    public override string ToolName => "Item";

    /// <summary>The usable items the player carries, as (slot, item) in slot order, one per item.</summary>
    public IReadOnlyList<SlotView> Usable =>
        [.. (Inventory.Own?.Items ?? []).Where(s => IsUsable(s.Item)).GroupBy(s => s.Item).Select(g => g.First())];

    /// <summary>The slot in hand, or null when the player carries nothing usable.</summary>
    public SlotView? InHand
    {
        get
        {
            var usable = Usable;
            return usable.FirstOrDefault(s => s.Item == _chosen) ?? usable.FirstOrDefault();
        }
    }

    /// <summary>Puts <paramref name="item"/> (a qualified id) in hand, if the player carries it.</summary>
    public void Choose(string item) => _chosen = item;

    /// <summary>Uses the item in hand on what the crosshair points at. False when there is nothing to do.</summary>
    public bool Use(Action<ActionResult>? done = null)
    {
        var hand = InHand;
        var item = hand is null ? null : Survival.Catalog.FindDefinition(hand.Item);
        var reach = Survival.Settings!.Reach;
        var hit = Cast(Player, reach);
        var aimed = PartHit(hit);
        var water = Identifiers.Qualify(Survival.Settings.WaterItem);
        if ((item is null || hand!.Item == water && aimed is null) && WaterAimed(hit, reach) is { } sea)
        {
            Send(SurvivalActions.CollectWater, new CollectWaterArgs(new Vec3(sea.X, sea.Y, sea.Z)), done);
            return true;
        }
        if (item is null || !IsScripted(item))
            return false;
        var aim = -Player.Camera.GlobalBasis.Z;
        Send(ScriptRuntime.UseItem, new UseItemArgs(hand!.Slot, new Vec3(aim.X, aim.Y, aim.Z), aimed?.Node.Creation.Id, aimed?.Part), done);
        return true;
    }

    /// <summary>Drops one of the item in hand on the ground in front of the player.</summary>
    public bool DropOne(Action<ActionResult>? done = null)
    {
        if (InHand is not { } hand)
            return false;
        Send(SurvivalActions.DropItems, new DropItemsArgs(hand.Slot, 1), done);
        return true;
    }

    /// <inheritdoc/>
    public override string HudText()
    {
        var hand = InHand;
        var name = hand is null ? Loc.T("nothing usable") : $"{Survival.Catalog.FindDefinition(hand.Item)?.Name ?? hand.Item} x{hand.Count}";
        return Loc.T("Item: {0}   {1}/{2} choose  {3} use  {4} drop one", name, Key(InputActions.HotbarPrevious), Key(InputActions.HotbarNext), Key(InputActions.Primary), Key(InputActions.Drop)) + Status;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            Use();
        else if (Pressed(@event, InputActions.HotbarPrevious))
            Cycle(-1);
        else if (Pressed(@event, InputActions.HotbarNext))
            Cycle(1);
        else if (Pressed(@event, InputActions.Drop))
            DropOne();
        else
            return false;
        return true;
    }

    private void Cycle(int step)
    {
        var usable = Usable;
        if (usable.Count == 0)
            return;
        var at = Math.Max(0, usable.ToList().FindIndex(s => s.Item == InHand?.Item));
        _chosen = usable[(at + step + usable.Count) % usable.Count].Item;
    }

    public override void _Process(double delta)
    {
        // Tell the host when the scripted item in hand changes (another item, another tool, death).
        (string Item, int Slot)? want = Active && Survival.Own is not { Dead: true } && InHand is { } hand
            && Survival.Catalog.FindDefinition(hand.Item) is { } d && IsScripted(d) ? (hand.Item, hand.Slot) : null;
        if (want == _equipped || Survival.Own is null)
            return;
        _equipped = want;
        Actions.Request(ScriptRuntime.EquipItem, new EquipItemArgs(want?.Slot), _ => { });
    }

    // A held item whose behaviour is its Lua script (a placeable part's script runs once it is placed).
    private static bool IsScripted(PartDefinition d) => d.Script is not null && !d.IsPlaceable;

    private bool IsUsable(string id) =>
        Survival.Catalog.FindDefinition(id) is { } d && IsScripted(d);

    // Where the aim ray meets the sea within reach, when nothing solid is in the way first.
    private Vector3? WaterAimed(RayHit? hit, float reach)
    {
        var generator = Survival.World.Generator;
        var from = Player.Camera.GlobalPosition;
        var direction = -Player.Camera.GlobalBasis.Z;
        if (direction.Y >= -1e-3f)
            return null;
        var t = (generator.SeaLevel - from.Y) / direction.Y;
        if (t < 0 || t > reach || (hit is { } h && h.Point.DistanceTo(from) < t - 0.05f && h.Point.Y > generator.SeaLevel))
            return null;
        var point = from + direction * t;
        return generator.HeightAt(point.X, point.Z) < generator.SeaLevel ? point : null;
    }
}
