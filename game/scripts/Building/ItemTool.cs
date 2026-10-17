using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Game.Items;
using Mechanica.Game.Survival;

namespace Mechanica.Game.Building;

/// <summary>
/// The item in the local player's hand (chunk 20): Q and E pick one of the usable items in their
/// inventory (food, weapons, seeds, water), left click uses it and G drops one on the ground.
/// <list type="bullet">
/// <item>Food: <c>Eat</c>. A weapon: <c>Fire</c> where the crosshair points.</item>
/// <item>A seed: <c>Plant</c> it in the soil under the crosshair. Water: <c>Water</c> that soil.</item>
/// <item>Aimed at water with water in hand or nothing usable: <c>CollectWater</c> fills a bucket.</item>
/// </list>
/// The host decides every one; nothing changes here until it answers.
/// </summary>
public partial class ItemTool : HandTool
{
    private string? _chosen;
    private double _lastUse = double.NegativeInfinity;

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
        var now = Time.GetTicksMsec() / 1000.0;
        var hand = InHand;
        var item = hand is null ? null : Survival.Catalog.FindDefinition(hand.Item);
        if (item?.Weapon is { } weapon)
        {
            if (now - _lastUse < (weapon.Interval ?? 0))
                return false;
            _lastUse = now;
            var aim = -Player.Camera.GlobalBasis.Z;
            Send(SurvivalActions.Fire, new FireArgs(hand!.Slot, new Vec3(aim.X, aim.Y, aim.Z)), done);
            return true;
        }
        if (item?.Food is not null)
        {
            Send(SurvivalActions.Eat, new EatArgs(hand!.Slot), done);
            return true;
        }
        var reach = Survival.Settings!.Reach;
        var hit = Cast(Player, reach);
        if (PartHit(hit) is { } aimed && aimed.Node.Creation.DefinitionOf(aimed.Part).Soil is not null && hand is not null
            && (item?.Seed is not null || item?.Watering is not null))
        {
            Send(item!.Seed is not null ? SurvivalActions.Plant : SurvivalActions.Water,
                new PartUseArgs(aimed.Node.Creation.Id, aimed.Part, hand.Slot), done);
            return true;
        }
        if ((item is null || item.Watering is not null) && WaterAimed(hit, reach) is { } water)
        {
            Send(SurvivalActions.CollectWater, new CollectWaterArgs(new Vec3(water.X, water.Y, water.Z)), done);
            return true;
        }
        return false;
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
        var name = hand is null ? "nothing usable" : $"{Survival.Catalog.FindDefinition(hand.Item)?.Name ?? hand.Item} x{hand.Count}";
        return $"Item: {name}   [Q/E] choose  [LMB] use  [G] drop one{Status}";
    }

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Clicked(@event, MouseButton.Left))
            Use();
        else if (Pressed(@event, Key.Q))
            Cycle(-1);
        else if (Pressed(@event, Key.E))
            Cycle(1);
        else if (Pressed(@event, Key.G))
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

    private bool IsUsable(string id) =>
        Survival.Catalog.FindDefinition(id) is { } d && (d.Food is not null || d.Weapon is not null || d.Seed is not null || d.Watering is not null);

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
