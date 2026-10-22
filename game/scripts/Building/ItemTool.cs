using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Characters;
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
/// The item in the local player's hand (chunk 20): the hotbar picks one of the items in their inventory (food,
/// weapons, seeds, water), left click uses it and G drops one on the ground. Holding Force Build (F) while left
/// clicking places one where the crosshair points instead, where it stays (UI spec §1, §9: food, buckets, glowsticks).
/// A weapon with an <c>aim</c> section aims while right click is held: the view zooms in a little and the crosshair
/// tightens (<c>AimItem</c>); left click fires, and so does letting go when it fires on release. Reload (R) runs
/// <c>ReloadItem</c>. Any other item's right click picks parts up, as with an empty hand (<see cref="HandTool.PickUp"/>). The rounds loaded and the ammo carried show on its hotbar slot and the HUD (UI spec §10).
/// An item whose <c>held.hold_use</c> is set (the Fire Extinguisher) is held, not clicked: pressing left click sends
/// <c>UseItem</c> with <c>hold</c> and letting go sends <c>ReleaseItem</c>, and while it is held the player's guard flag
/// tells every peer to draw its spray (<see cref="Player.HeldSprays"/>).
/// <list type="bullet">
/// <item>An item with a script (food, weapons, seeds, water; chunk 20b): <c>UseItem</c> where the crosshair
/// points, with the part under it when that is in reach (the soil a seed is planted in). Its Lua decides
/// what that does on the host. While this tool is out, <c>EquipItem</c> tells the host which scripted
/// item is in hand (its <c>on_equip</c> and <c>on_unequip</c>).</item>
/// <item>Aimed at the sea with water in hand or nothing usable: <c>CollectWater</c> fills every empty bucket carried. Aimed at a POI's pool
/// of another liquid, it fills them with that pool's bucket (a chemical pool's Chemical Bucket) or collects its items.</item>
/// </list>
/// The host decides every one; nothing changes here until it answers.
/// </summary>
public partial class ItemTool : HandTool
{
    private string? _chosen;
    private bool _empty;
    // The scripted item the host was last told is in hand: (item, slot), or null for none.
    private (string Item, int Slot)? _equipped;
    private bool _aiming;
    // The held item whose Use is held down (held.hold_use), or null.
    private (string Item, int Slot)? _holding;
    // When a bucket gesture under way lets the next click start (ms).
    private ulong _busyUntil;

    /// <summary>Inventories, for what the player carries. Set before adding to the tree.</summary>
    public InventorySync Inventory { get; init; } = null!;

    /// <summary>Survival state and rules. Set before adding to the tree.</summary>
    public SurvivalSync Survival { get; init; } = null!;

    /// <inheritdoc/>
    public override string ToolName => "Item";

    /// <summary>How items look in hand. Null shows every item as <see cref="ItemPose.Default"/>.</summary>
    public CharacterSettings? Characters { get; init; }

    /// <inheritdoc/>
    public override CharacterProp Prop => PoseInHand?.Prop ?? CharacterProp.None;

    /// <inheritdoc/>
    protected override CharacterGesture GestureFor(InputEvent @event) => CharacterGesture.None; // Click plays its own

    // How the item in hand looks, or null with nothing usable in hand.
    private ItemPose? PoseInHand
    {
        get
        {
            if (InHand is not { } hand)
                return null;
            var definition = Survival.Catalog.FindDefinition(hand.Item);
            var name = definition?.Category is { } c ? System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(c.ToString()) : null;
            var pose = Characters?.PoseFor(hand.Item, name) ?? ItemPose.Default;
            // A drink (its data's drink flag) is held as a bottle and drunk rather than eaten.
            return pose.Prop == CharacterProp.Food && FoodUse.Of(definition).Drink
                ? new ItemPose { Prop = CharacterProp.Drink, Gesture = CharacterGesture.Drink }
                : pose;
        }
    }

    /// <summary>The usable items the player carries, as (slot, item) in slot order, one per item.</summary>
    public IReadOnlyList<SlotView> Usable =>
        [.. (Inventory.Own?.Items ?? []).Where(s => IsUsable(s.Item)).GroupBy(s => s.Item).Select(g => g.First())];

    /// <summary>
    /// The slot in hand: the chosen item if the player carries it (usable or not, so it can be dropped), else the
    /// first usable item until one is chosen; null with an empty hand or nothing usable.
    /// </summary>
    public SlotView? InHand
    {
        get
        {
            if (_empty)
                return null;
            if (_chosen is not null && Inventory.Own?.Items.FirstOrDefault(s => s.Item == _chosen) is { } chosen)
                return chosen;
            return _chosen is null ? Usable.FirstOrDefault() : null;
        }
    }

    /// <summary>Puts <paramref name="item"/> (a qualified id) in hand, if the player carries it; null empties the hand.</summary>
    public void Choose(string? item) => (_chosen, _empty) = (item, item is null);

    /// <summary>Uses the item in hand on what the crosshair points at. False when there is nothing to do.</summary>
    public bool Use(Action<ActionResult>? done = null) => Use(false, done);

    // Uses the item in hand; with hold, Use stays held on the host until ReleaseItem.
    private bool Use(bool hold, Action<ActionResult>? done)
    {
        var hand = InHand;
        var item = hand is null ? null : Survival.Catalog.FindDefinition(hand.Item);
        var reach = Survival.Settings!.Reach;
        var hit = Cast(Player, reach);
        var aimed = PartHit(hit);
        if (hand is not null && hand.Item == EmptyBucket && WaterAimed(hit, reach) is { } sea)
        {
            Send(SurvivalActions.CollectWater, new CollectWaterArgs(new Vec3(sea.X, sea.Y, sea.Z), hand.Slot), done);
            return true;
        }
        if (item is null || !IsScripted(item))
            return false;
        var aim = -Player.Camera.GlobalBasis.Z;
        Send(ScriptRuntime.UseItem, new UseItemArgs(hand!.Slot, new Vec3(aim.X, aim.Y, aim.Z), aimed?.Node.Creation.Id, aimed?.Part, hold), done);
        return true;
    }

    /// <summary>True while Use is held down with a held-use item (<c>held.hold_use</c>: the Fire Extinguisher spraying).</summary>
    public bool Holding => _holding is not null;

    /// <summary>
    /// Presses Use and keeps it held with an item whose <c>held.hold_use</c> is set: the host runs its <c>on_use</c> and its
    /// script sees <c>holder.using()</c> until <see cref="LetGo"/>, and every peer sees it spray. False when the item in hand
    /// is not held to use.
    /// </summary>
    public bool Hold(Action<ActionResult>? done = null)
    {
        if (InHand is not { } hand || !HoldsUse(hand.Item))
            return false;
        if (_holding is not null)
            return true;
        var holding = (hand.Item, hand.Slot);
        _holding = holding;
        Player.HeldGuard = true;
        if (!Use(true, result =>
            {
                if (!result.Ok && _holding == holding)
                    LetGo();
                done?.Invoke(result);
            }))
            LetGo();
        return true;
    }

    /// <summary>Lets go of a held Use (<see cref="Hold"/>): <c>ReleaseItem</c>, and the spray stops on every peer.</summary>
    public void LetGo(Action<ActionResult>? done = null)
    {
        if (_holding is not { } holding)
            return;
        _holding = null;
        Player.HeldGuard = false;
        // Not through Send: the answer to the use stays on the HUD.
        Actions.Request(ScriptRuntime.ReleaseItem, new ReleaseItemArgs(holding.Slot), result => done?.Invoke(result));
    }

    // An item held to use rather than clicked (a spray).
    private bool HoldsUse(string item) => Survival.Catalog.FindDefinition(item) is { Held.HoldUse: true } d && IsScripted(d);

    /// <summary>
    /// Use (E) on an animal: with a food it eats in hand it feeds it (<see cref="BotActions.FeedBot"/>), towards taming it,
    /// and a tame one its breed's treat, so it looks for a partner; with its Lead in hand it takes a tame one on the Lead, or lets it go (<see cref="BotActions.LeadBot"/>).
    /// </summary>
    public override (string Label, Action Run)? Interaction
    {
        get
        {
            if (!Active || InHand is not { } hand || Survival.Settings is not { } settings
                || CastShown(Player, settings.Reach)?.Collider is not Bots.BotBody { View: { Dead: false } view } bot
                || bot.Type.Tame is not { } tame)
                return null;
            var name = Loc.Mod(bot.Type.Name);
            if (view.Kept && tame.Lead == hand.Item)
            {
                var label = view.LedBy == Player.PeerId ? Loc.T("let the {0} go", name) : Loc.T("lead the {0}", name);
                return (label, () => Send(BotActions.LeadBot, new BotArgs(bot.BotId), null));
            }
            var item = hand.Item;
            // a tame one takes its breed's treat (it looks for a partner); a wild one a food it eats
            if (view.Kept ? tame.Breed?.Treat != item || view.InLove : !tame.Foods.Contains(item))
                return null;
            return (Loc.T("feed the {0}", name), () => Send(BotActions.FeedBot, new FeedBotArgs(bot.BotId, item), null));
        }
    }

    /// <inheritdoc/>
    public override bool Aiming => _aiming;

    /// <summary>An item that aims (an <c>aim</c> section: the weapons) keeps right click; any other picks parts up with it.</summary>
    public override bool OwnsRightClick => AimOfHand is not null;

    /// <summary>How the item in hand aims, or null when it does not.</summary>
    public AimDefinition? AimOfHand => InHand is { } hand ? Survival.Catalog.FindDefinition(hand.Item)?.Aim : null;

    /// <summary>What the item in hand's counter shows (rounds loaded and ammo carried), or null when it has none.</summary>
    public (string Text, int? Loaded, int Carried)? Counter => InHand is { } hand ? CounterFor(hand.Item) : null;

    /// <summary>
    /// What <paramref name="item"/>'s counter shows (UI spec §10), from what its script last set: the rounds loaded out of
    /// the magazine, or the ammo carried when it has no magazine; null when it has no counter.
    /// </summary>
    public (string Text, int? Loaded, int Carried)? CounterFor(string item)
    {
        if (Survival.CounterOf(item) is not { } view)
            return null;
        var carried = view.Ammo is { } ammo ? Inventory.Own?.Count(ammo) ?? 0 : 0;
        var text = view.Value is { } loaded
            ? view.Max is { } max ? $"{loaded} / {max}" : $"{loaded}"
            : $"{carried}";
        return (text, view.Value, carried);
    }

    /// <summary>Starts (or stops) aiming the item in hand. False when it does not aim.</summary>
    public bool Aim(bool on, Action<ActionResult>? done = null)
    {
        if (on && AimOfHand is null)
            return false;
        if (_aiming == on)
            return true;
        _aiming = on;
        Player.AimZoom = on ? AimOfHand?.Zoom ?? AimDefinition.DefaultZoom : 1f;
        // Not through Send: the answer to the last shot stays on the HUD.
        Actions.Request(ScriptRuntime.AimItem, new AimItemArgs(on), result => done?.Invoke(result));
        return true;
    }

    /// <summary>Reloads the item in hand. False with nothing in hand.</summary>
    public bool Reload(Action<ActionResult>? done = null)
    {
        if (InHand is not { } hand || Survival.Catalog.FindDefinition(hand.Item) is not { } item || !IsScripted(item))
            return false;
        Send(ScriptRuntime.ReloadItem, new ReloadItemArgs(hand.Slot), done);
        return true;
    }

    /// <summary>
    /// Force Build: places one of the item in hand where the crosshair points, within reach, where it stays. False
    /// with nothing in hand or nothing in reach to put it on.
    /// </summary>
    public bool PlaceOne(Action<ActionResult>? done = null)
    {
        if (InHand is not { } hand || Cast(Player, Survival.Settings!.Reach) is not { } hit)
            return false;
        // Just above the surface, so it sits on it.
        var at = hit.Point + hit.Normal * 0.02f;
        Send(SurvivalActions.DropItems, new DropItemsArgs(hand.Slot, 1, new Vec3(at.X, at.Y, at.Z)), done);
        return true;
    }

    /// <inheritdoc/>
    public override float? Progress =>
        InHand is { } hand && Survival.CounterOf(hand.Item) is { Busy: > 0 } view && Survival.BusyFor(hand.Item) is > 0 and var left
            ? 1f - left / view.Busy
            : null;

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
        var line = Loc.T("Item: {0}   {1} use  {2} drop one  hold {3} + {1} place", name, Key(InputActions.Primary), Key(InputActions.Drop), Key(InputActions.ForceBuild));
        line += "  " + (OwnsRightClick ? Loc.T("hold {0} aim", Key(InputActions.Secondary)) : PickUpHint);
        if (hand is not null && Survival.CounterOf(hand.Item) is { Max: not null })
            line += "  " + Loc.T("{0} reload", Key(InputActions.Reload));
        return line + Status;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            Click(Input.IsActionPressed(InputActions.ForceBuild));
        else if (Released(@event, InputActions.Primary) && _holding is not null)
            LetGo();
        else if (Pressed(@event, InputActions.Drop))
            DropOne();
        else if (Pressed(@event, InputActions.Reload))
            Reload();
        else if (Pressed(@event, InputActions.Secondary) && AimOfHand is not null)
            Aim(true);
        else if (Released(@event, InputActions.Secondary) && _aiming)
            Release();
        else
            return false;
        return true;
    }

    /// <summary>
    /// A left click: uses the item in hand, or with <paramref name="place"/> (Force Build held) puts one down, and plays
    /// the arm movement that goes with it. A water bucket's fill, throw or put-down happens at the moment in its clip
    /// where it does (<see cref="BucketSettings.ReleaseSeconds"/>), and no other click starts until the clip is over.
    /// </summary>
    public void Click(bool place)
    {
        if (Time.GetTicksMsec() < _busyUntil)
            return; // the bucket is still swinging from the last one, or the last bite still being eaten
        if (!place && Hold())
            return; // a spray: held until left click is let go
        if (BucketGesture(place) is var bucket and not CharacterGesture.None)
        {
            Player.Gesture(bucket);
            AfterRelease(bucket, place ? () => PlaceOne() : () => Use());
            return;
        }
        if (!place && PoseInHand is { Prop: CharacterProp.Food or CharacterProp.Drink } food && Characters is { } characters)
        {
            // Food is eaten only once the host has taken it (not at full health): then the bites play, and no more is
            // eaten until it is over (the food's eat_seconds, else its clip's length). A drink plays the drink clip's
            // gulps instead, and leaves sprint alone; eating stops the player sprinting meanwhile.
            var use = FoodUse.Of(Survival.Catalog.FindDefinition(InHand!.Item));
            var drink = food.Prop == CharacterProp.Drink;
            var seconds = use.SecondsOr(drink ? characters.Drink.Seconds : characters.Eat.Seconds);
            _busyUntil = Time.GetTicksMsec() + (ulong)(seconds * 1000f);
            if (!drink)
                Player.NoSprintUntil = _busyUntil;
            if (!Use(result =>
                {
                    if (!result.Ok)
                        _busyUntil = Player.NoSprintUntil = 0;
                    else if (IsInsideTree())
                        Player.Gesture(food.Gesture);
                }))
                _busyUntil = Player.NoSprintUntil = 0;
            return;
        }
        Player.Gesture(PoseInHand?.Gesture ?? CharacterGesture.None);
        if (place)
            PlaceOne();
        else
            Use();
    }

    /// <summary>Lets go of right click: stops aiming, and fires first when the item fires on release.</summary>
    public void Release()
    {
        if (_aiming && AimOfHand is { FireOnRelease: true })
            Use();
        Aim(false);
    }

    /// <inheritdoc/>
    protected override void OnActiveChanged()
    {
        if (!Active)
        {
            Aim(false);
            LetGo();
        }
    }

    public override void _Process(double delta)
    {
        // A release the tool never heard (the mouse freed for a window, another item in hand) ends the aim.
        if (_aiming && (!Active || AimOfHand is null || (ReadDeviceInput && Input.MouseMode != Input.MouseModeEnum.Captured)))
            Aim(false);
        // So does a held Use: let go unheard, the item gone from the hand (used up) or the player dead.
        if (_holding is { } holding && (!Active || InHand is not { } held || held.Item != holding.Item || Survival.Own is { Dead: true }
            || (ReadDeviceInput && (Input.MouseMode != Input.MouseModeEnum.Captured || !Input.IsActionPressed(InputActions.Primary)))))
            LetGo();
        // Tell the host when the scripted item in hand changes (another item, another tool, death).
        (string Item, int Slot)? want = Active && Survival.Own is not { Dead: true } && InHand is { } hand
            && Survival.Catalog.FindDefinition(hand.Item) is { } d && IsScripted(d) ? (hand.Item, hand.Slot) : null;
        if (want == _equipped || Survival.Own is null)
            return;
        _equipped = want;
        Actions.Request(ScriptRuntime.EquipItem, new EquipItemArgs(want?.Slot), _ => { });
    }

    // The empty and the full water bucket (qualified ids).
    private string EmptyBucket => Identifiers.Qualify(Survival.Settings!.BucketItem);

    private string FullBucket => Identifiers.Qualify(Survival.Settings!.WaterItem);

    // The bucket gesture a click plays: filling an empty bucket aimed at water, throwing a full one, or setting either
    // down (Force Build); None for anything else.
    private CharacterGesture BucketGesture(bool place)
    {
        if (InHand is not { } hand || Characters is null)
            return CharacterGesture.None;
        // A full bucket of anything (water, or chemical from a pool) throws, or is set down full.
        if (hand.Item == FullBucket || PoseInHand?.Prop is CharacterProp.ChemicalBucketFull)
            return place ? CharacterGesture.BucketPlaceFull : CharacterGesture.BucketThrow;
        if (hand.Item != EmptyBucket)
            return CharacterGesture.None;
        if (place)
            return CharacterGesture.BucketPlace;
        var reach = Survival.Settings!.Reach;
        return WaterAimed(Cast(Player, reach), reach) is null ? CharacterGesture.None : CharacterGesture.BucketFill;
    }

    // Does what a bucket gesture does at the moment in the clip it happens (release_seconds), if the same bucket is
    // still in hand then; no other click starts until the clip is over.
    private void AfterRelease(CharacterGesture gesture, Action act)
    {
        var clip = CharacterRig.GestureClip(gesture)!;
        var bucket = Characters!.Bucket;
        _busyUntil = Time.GetTicksMsec() + (ulong)(BucketRig.Length(bucket, clip) * 1000f);
        var item = InHand?.Item;
        var release = bucket.ReleaseSeconds.GetValueOrDefault(clip);
        if (release <= 0f)
        {
            act();
            return;
        }
        GetTree().CreateTimer(release).Timeout += () =>
        {
            if (IsInsideTree() && Active && InHand?.Item == item)
                act();
        };
    }

    // A held item whose behaviour is its Lua script (a placeable part's script runs once it is placed).
    private static bool IsScripted(PartDefinition d) => d.Script is not null && !d.IsPlaceable;

    private bool IsUsable(string id) =>
        Survival.Catalog.FindDefinition(id) is { } d && IsScripted(d);

    // Where the aim ray meets the sea, or a POI's pool of liquid (a chemical pool), within reach, when nothing solid is in
    // the way first.
    private Vector3? WaterAimed(RayHit? hit, float reach)
    {
        var generator = Survival.World.Generator;
        var from = Player.AimOrigin;
        var direction = -Player.Camera.GlobalBasis.Z;
        if (direction.Y >= -1e-3f)
            return null;
        foreach (var pool in generator.PoolsAt(from.X, from.Z))
        {
            var reached = (pool.Centre.Y - from.Y) / direction.Y;
            if (reached < 0 || reached > reach || (hit is { } blocked && blocked.Point.DistanceTo(from) < reached - 0.05f && blocked.Point.Y > pool.Centre.Y))
                continue;
            // Just under the surface, so the host finds the point in the liquid.
            var surface = from + direction * reached - new Vector3(0, 0.02f, 0);
            if (generator.LiquidAt(surface.ToNumerics()) is not null)
                return surface;
        }
        var t = (generator.SeaLevel - from.Y) / direction.Y;
        if (t < 0 || t > reach || (hit is { } h && h.Point.DistanceTo(from) < t - 0.05f && h.Point.Y > generator.SeaLevel))
            return null;
        var point = from + direction * t;
        return generator.HeightAt(point.X, point.Z) < generator.SeaLevel ? point : null;
    }
}
