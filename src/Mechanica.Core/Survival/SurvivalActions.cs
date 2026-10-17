using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.World;

namespace Mechanica.Core.Survival;

/// <summary>
/// The survival loop (chunk 20) in the Action API, validated on the host like every other action:
/// <list type="bullet">
/// <item>Health is the only meter (D12). Damage comes from weapons, the Sledgehammer, falls and splash;
/// at 0 a player dies, drops what they carry and may <c>Respawn</c> after a delay, at their bed or the
/// world spawn. In Creative nothing hurts a player.</item>
/// <item><c>Eat</c> heals with food. <c>Fire</c> shoots a weapon: host-side rays that hurt players, break
/// parts and fell world objects.</item>
/// <item>Farming: <c>Plant</c> a seed in soil, <c>Water</c> it (buckets come from <c>CollectWater</c>),
/// and <c>Harvest</c> it once ripe. Crops grow only while their soil is watered.</item>
/// <item>Items on the ground: <c>DropItems</c>, <c>PickUp</c>. Broken containers, broken world objects and
/// dead players leave piles too.</item>
/// <item><c>SetSpawn</c> picks the bed a player respawns at. <c>GetVitals</c> and <c>GetSoil</c> are queries.</item>
/// </list>
/// It also prices building in Survival (<see cref="IBuildCosts"/>) and lets the Sledgehammer hit players
/// and world objects (<see cref="IStrikeTargets"/>). Dead players can act on nothing but <c>Respawn</c>
/// (<see cref="Gate"/>).
/// </summary>
public sealed class SurvivalActions(IPartCatalog catalog, SurvivalSettings settings, InventoryActions inventories, ISurvivalWorld world)
    : IBuildCosts, IStrikeTargets
{
    /// <summary>Action name for eating.</summary>
    public const string Eat = "Eat";

    /// <summary>Action name for shooting a weapon.</summary>
    public const string Fire = "Fire";

    /// <summary>Action name for planting a seed.</summary>
    public const string Plant = "Plant";

    /// <summary>Action name for watering soil.</summary>
    public const string Water = "Water";

    /// <summary>Action name for harvesting a ripe crop.</summary>
    public const string Harvest = "Harvest";

    /// <summary>Action name for filling a bucket at water.</summary>
    public const string CollectWater = "CollectWater";

    /// <summary>Action name for setting the bed to respawn at.</summary>
    public const string SetSpawn = "SetSpawn";

    /// <summary>Action name for putting items on the ground.</summary>
    public const string DropItems = "DropItems";

    /// <summary>Action name for picking items up.</summary>
    public const string PickUp = "PickUp";

    /// <summary>Action name for respawning (dead players), or going back to the spawn spot (living ones).</summary>
    public const string Respawn = "Respawn";

    /// <summary>Action name for reading the caller's health.</summary>
    public const string GetVitals = "GetVitals";

    /// <summary>Action name for reading what grows on a soil part.</summary>
    public const string GetSoil = "GetSoil";

    // Share of a weapon's interval the host insists on between one player's shots (network jitter).
    private const double IntervalJitter = 0.8;

    private readonly SortedDictionary<int, Vitals> _vitals = [];
    private readonly SortedDictionary<int, ItemDrop> _drops = [];
    private readonly Dictionary<int, double> _lastShot = [];
    private readonly Dictionary<int, double> _lastWater = [];
    private int _nextDrop = 1;

    private sealed class Vitals(float health)
    {
        public float Health = health;
        public bool Dead;
        public double DiedAt;
        public (int Creation, int Part)? Bed;
    }

    /// <summary>Creative or Survival. The host sets it before players join.</summary>
    public GameMode Mode { get; set; } = GameMode.Survival;

    /// <summary>The numbers in use.</summary>
    public SurvivalSettings Settings => settings;

    /// <summary>Piles of items on the ground, by id.</summary>
    public IReadOnlyDictionary<int, ItemDrop> Drops => _drops;

    /// <summary>Players being tracked, by peer.</summary>
    public IEnumerable<int> Players => _vitals.Keys;

    /// <summary>A player's health changed, they died or they respawned.</summary>
    public event Action<int>? VitalsChanged;

    /// <summary>A pile of items appeared or changed.</summary>
    public event Action<ItemDrop>? DropChanged;

    /// <summary>A pile of items is gone (picked up or expired).</summary>
    public event Action<int>? DropRemoved;

    /// <summary>A player took damage: peer, health lost, cause (<c>fall</c>, <c>weapon</c>, <c>sledge</c>, ...).</summary>
    public event Action<int, float, string>? Hurt;

    /// <summary>Registers every survival action.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<EatArgs>(Eat, ValidateEat, ApplyEat);
        dispatcher.Register<FireArgs>(Fire, ValidateFire, ApplyFire);
        dispatcher.Register<PartUseArgs>(Plant, ValidatePlant, ApplyPlant);
        dispatcher.Register<PartUseArgs>(Water, ValidateWater, ApplyWater);
        dispatcher.Register<PartUseArgs>(Harvest, ValidateHarvest, ApplyHarvest);
        dispatcher.Register<CollectWaterArgs>(CollectWater, ValidateCollectWater, ApplyCollectWater);
        dispatcher.Register<PartUseArgs>(SetSpawn, ValidateSetSpawn, ApplySetSpawn);
        dispatcher.Register<DropItemsArgs>(DropItems, ValidateDrop, ApplyDrop);
        dispatcher.Register<PickUpArgs>(PickUp, ValidatePickUp, ApplyPickUp);
        dispatcher.Register<NoArgs>(Respawn, ValidateRespawn, ApplyRespawn);
        dispatcher.Register<NoArgs>(GetVitals, (c, _) => _vitals.ContainsKey(c.Peer) ? null : "You have no player.",
            (c, _) => Value(View(c.Peer)), readOnly: true);
        dispatcher.Register<GetSoilArgs>(GetSoil, (caller, a) => FindSoil(a.Creation, a.Part, out _, out _),
            (_, a) => Value(SoilViewOf(world.FindCreation(a.Creation)!, a.Part)), readOnly: true);
    }

    /// <summary>
    /// For <see cref="ActionDispatcher.Gate"/>: a dead player can only respawn. Other callers and living
    /// players pass.
    /// </summary>
    public string? Gate(ActionCaller caller, string action) =>
        caller.Source == ActionSource.Player && IsDead(caller.Peer) && action != Respawn ? "You are dead. Respawn first." : null;

    // ---- Players ----

    /// <summary>Starts tracking a player who joined, at full health; in Survival they get the starting items.</summary>
    public void Join(int peer)
    {
        if (_vitals.ContainsKey(peer))
            return;
        _vitals[peer] = new Vitals(settings.MaxHealth);
        if (Mode == GameMode.Survival)
        {
            var pockets = inventories.PlayerInventory(peer);
            foreach (var start in settings.StartingItems.Where(s => catalog.FindDefinition(s.Item) is not null))
                pockets.Add(start.Item, start.Count);
        }
        VitalsChanged?.Invoke(peer);
    }

    /// <summary>Forgets a player who left.</summary>
    public void Leave(int peer)
    {
        _vitals.Remove(peer);
        _lastShot.Remove(peer);
        _lastWater.Remove(peer);
    }

    /// <summary>True while <paramref name="peer"/>'s player is dead.</summary>
    public bool IsDead(int peer) => _vitals.TryGetValue(peer, out var v) && v.Dead;

    /// <summary>A player's health as clients see it, or null for an unknown peer.</summary>
    public VitalsView? View(int peer)
    {
        if (!_vitals.TryGetValue(peer, out var v))
            return null;
        var wait = v.Dead ? Math.Max(0, settings.RespawnDelay - (float)(world.Now - v.DiedAt)) : 0f;
        return new VitalsView(peer, v.Health, settings.MaxHealth, v.Dead, wait, BedOf(peer) is not null, Mode);
    }

    /// <summary>
    /// Takes <paramref name="amount"/> health from a player (nothing in Creative or when dead). At 0 they
    /// die. Returns the health left. Bots (chunk 21) hurt players through this.
    /// </summary>
    public float Damage(int peer, float amount, string cause)
    {
        if (!_vitals.TryGetValue(peer, out var v))
            return 0f;
        if (Mode == GameMode.Creative || v.Dead || !(amount > 0) || !float.IsFinite(amount))
            return v.Health;
        var lost = Math.Min(v.Health, amount);
        v.Health -= lost;
        Hurt?.Invoke(peer, lost, cause);
        if (v.Health <= 0)
            Die(peer, v);
        VitalsChanged?.Invoke(peer);
        return v.Health;
    }

    /// <summary>A player's body hit the ground at <paramref name="speed"/> m/s straight down.</summary>
    public void Landed(int peer, float speed)
    {
        if (settings.Fall.DamageAt(speed) is var damage and > 0)
            Damage(peer, damage, "fall");
    }

    private void Die(int peer, Vitals v)
    {
        v.Health = 0;
        v.Dead = true;
        v.DiedAt = world.Now;
        if (!settings.DropInventoryOnDeath || world.FeetOf(peer) is not { } feet)
            return;
        var pockets = inventories.PlayerInventory(peer);
        var items = pockets.Slots.Where(s => s is not null).Select(s => s!).ToList();
        pockets.Clear();
        AddDrop(feet, items);
    }

    // ---- Respawn and beds ----

    private string? ValidateRespawn(ActionCaller caller, NoArgs args)
    {
        if (!_vitals.TryGetValue(caller.Peer, out var v) || world.FeetOf(caller.Peer) is null)
            return "You have no player to respawn.";
        var wait = settings.RespawnDelay - (world.Now - v.DiedAt);
        return v.Dead && wait > 0 ? $"You can respawn in {Math.Ceiling(wait):0} s." : null;
    }

    private JsonElement? ApplyRespawn(ActionCaller caller, NoArgs args)
    {
        var v = _vitals[caller.Peer];
        world.Teleport(caller.Peer, BedOf(caller.Peer) is { } bed ? BedTop(bed.Creation, bed.Part) : world.SpawnOf(caller.Peer));
        if (v.Dead)
        {
            v.Dead = false;
            v.Health = settings.MaxHealth;
            VitalsChanged?.Invoke(caller.Peer);
        }
        return null;
    }

    private string? ValidateSetSpawn(ActionCaller caller, PartUseArgs args)
    {
        if (world.FindCreation(args.Creation) is not { } creation || creation.FindPart(args.Part) is null)
            return $"There is no part {args.Part} in creation {args.Creation}.";
        if (creation.DefinitionOf(args.Part).RespawnPoint != true)
            return $"You cannot respawn at the {NameOf(creation.FindPart(args.Part)!.Part)}.";
        return CheckReach(caller, PartCentre(creation, args.Part));
    }

    private JsonElement? ApplySetSpawn(ActionCaller caller, PartUseArgs args)
    {
        if (_vitals.TryGetValue(caller.Peer, out var v))
            v.Bed = (args.Creation, args.Part);
        VitalsChanged?.Invoke(caller.Peer);
        return null;
    }

    // The bed a player set, while it still exists.
    private (int Creation, int Part)? BedOf(int peer) =>
        _vitals.TryGetValue(peer, out var v) && v.Bed is { } bed && world.FindCreation(bed.Creation) is { } c
            && c.FindPart(bed.Part) is not null && c.DefinitionOf(bed.Part).RespawnPoint == true ? bed : null;

    // A spot just above a bed's top face, in the world.
    private Vector3 BedTop(int creationId, int partId)
    {
        var creation = world.FindCreation(creationId)!;
        var part = creation.FindPart(partId)!;
        var definition = creation.DefinitionOf(partId);
        var top = PartGeometry.CentreMetres(definition, part.Cell, part.Rotation).ToNumerics()
            + new Vector3(0, (definition.Size ?? GridSize.One).Metres.Y / 2 + 0.05f, 0);
        return creation.BodyPose(creation.BodyIndexOf(partId)).Apply(top);
    }

    // ---- Eating ----

    private string? ValidateEat(ActionCaller caller, EatArgs args)
    {
        if (CheckSlot(caller, args.Slot, out var item) is { } bad)
            return bad;
        if (item!.Food is null)
            return $"You cannot eat {item.Name}.";
        if (!_vitals.TryGetValue(caller.Peer, out var v))
            return "You have no player.";
        return v.Health >= settings.MaxHealth ? "You are already at full health." : null;
    }

    private JsonElement? ApplyEat(ActionCaller caller, EatArgs args)
    {
        var pockets = inventories.PlayerInventory(caller.Peer);
        var stack = pockets[args.Slot]!;
        var food = catalog.FindDefinition(stack.Item)!.Food!;
        pockets.SetSlot(args.Slot, stack.Count > 1 ? stack with { Count = stack.Count - 1 } : null);
        var v = _vitals[caller.Peer];
        v.Health = Math.Min(settings.MaxHealth, v.Health + (food.Health ?? 0));
        VitalsChanged?.Invoke(caller.Peer);
        return Value(new EatResult(v.Health));
    }

    // ---- Weapons ----

    private string? ValidateFire(ActionCaller caller, FireArgs args)
    {
        if (CheckSlot(caller, args.Slot, out var item) is { } bad)
            return bad;
        if (item!.Weapon is not { } weapon)
            return $"{item.Name} is not a weapon.";
        var direction = args.Direction.ToNumerics();
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z) || direction.LengthSquared() < 1e-6f)
            return "direction must be a non-zero vector.";
        if (world.EyeOf(caller.Peer) is null)
            return "You have no player to shoot with.";
        if (caller.Source == ActionSource.Player)
        {
            if (world.LookOf(caller.Peer) is { } look && AngleDegrees(look, Vector3.Normalize(direction)) > settings.AimTolerance)
                return "That is not where you are aiming.";
            if (_lastShot.TryGetValue(caller.Peer, out var last) && world.Now - last < (weapon.Interval ?? 0) * IntervalJitter)
                return $"The {item.Name} is not ready yet.";
        }
        if (weapon.Ammo is { } ammo && inventories.PlayerInventory(caller.Peer).Count(ammo) < 1)
            return $"You have no {NameOf(ammo)} to fire.";
        return null;
    }

    private JsonElement? ApplyFire(ActionCaller caller, FireArgs args)
    {
        _lastShot[caller.Peer] = world.Now;
        var pockets = inventories.PlayerInventory(caller.Peer);
        var weapon = catalog.FindDefinition(pockets[args.Slot]!.Item)!.Weapon!;
        if (weapon.Ammo is { } ammo)
            pockets.Remove(ammo, 1);
        var eye = world.EyeOf(caller.Peer)!.Value;
        var range = weapon.Range ?? 0;
        var damage = weapon.Damage ?? 0;
        var hits = new List<ShotHitView>();
        foreach (var direction in Pellets(Vector3.Normalize(args.Direction.ToNumerics()), weapon.Pellets ?? 1, weapon.Spread ?? 0))
        {
            var hit = world.Cast(eye, direction, range, caller.Peer);
            if (weapon.Radius is { } radius)
            {
                // Splash weapons go off where they hit, or at the end of their range.
                Explode(caller, hit?.Point ?? eye + direction * range, radius, damage, weapon.Impulse ?? 0, hits);
            }
            else if (hit is not null)
            {
                hits.Add(HitOne(caller, hit, damage, direction * (weapon.Impulse ?? 0)));
            }
        }
        return Value(new FireResult(hits));
    }

    /// <summary>
    /// The directions of a shot's pellets: the first straight along <paramref name="aim"/>, the rest
    /// evenly round a cone of half-angle <paramref name="spread"/> degrees. No randomness, so every peer
    /// and every replay agrees.
    /// </summary>
    public static IReadOnlyList<Vector3> Pellets(Vector3 aim, int count, float spread)
    {
        var result = new List<Vector3> { aim };
        if (count <= 1 || spread <= 0)
            return [.. Enumerable.Repeat(aim, Math.Max(1, count))];
        var side = Vector3.Normalize(Vector3.Cross(aim, MathF.Abs(aim.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX));
        var up = Vector3.Cross(side, aim);
        var tan = MathF.Tan(spread * MathF.PI / 180f);
        for (var i = 1; i < count; i++)
        {
            var angle = 2 * MathF.PI * (i - 1) / (count - 1);
            result.Add(Vector3.Normalize(aim + (side * MathF.Cos(angle) + up * MathF.Sin(angle)) * tan));
        }
        return result;
    }

    private ShotHitView HitOne(ActionCaller caller, RayHit hit, float damage, Vector3 push)
    {
        var point = hit.Point.ToVec3();
        if (hit.Player is { } peer)
        {
            var dealt = CanHurt(caller, peer) ? damage : 0f;
            if (dealt > 0)
                Damage(peer, dealt, "weapon");
            return new ShotHitView("player", point, dealt, Player: peer);
        }
        if (hit.Creation is { } id && hit.Part is { } part && world.FindCreation(id) is { } creation && creation.FindPart(part) is not null)
        {
            var body = creation.BodyIndexOf(part);
            var result = world.DamagePart(creation, part, damage);
            if (!result.Broken && push != Vector3.Zero)
                world.Push(creation, body, hit.Point, push);
            return new ShotHitView("part", point, damage, Creation: id, Part: part);
        }
        if (hit.Object is { } objectId)
        {
            DamageObject(objectId, damage);
            return new ShotHitView("object", point, damage, Object: objectId.ToString());
        }
        return new ShotHitView("ground", point, 0);
    }

    // Splash: everything within the radius takes damage falling off linearly to 0 at the edge.
    private void Explode(ActionCaller caller, Vector3 at, float radius, float damage, float impulse, List<ShotHitView> hits)
    {
        float Falloff(float distance) => damage * Math.Clamp(1 - distance / radius, 0, 1);
        foreach (var peer in _vitals.Keys.ToList())
        {
            if (PlayerCentre(peer) is not { } centre || Vector3.Distance(centre, at) is var d && d > radius)
                continue;
            // A shooter's own splash hurts them even with PvP off.
            var dealt = peer == caller.Peer || CanHurt(caller, peer) ? Falloff(d) : 0f;
            if (dealt > 0)
                Damage(peer, dealt, "explosion");
            hits.Add(new ShotHitView("player", centre.ToVec3(), dealt, Player: peer));
        }
        foreach (var creation in world.AllCreations.ToList())
        {
            foreach (var part in creation.Parts.ToList())
            {
                if (creation.FindPart(part.Id) is null)
                    continue; // broken off by an earlier part's damage, with the Creation split
                var centre = PartCentre(creation, part.Id);
                if (Vector3.Distance(centre, at) is var d && d > radius)
                    continue;
                var dealt = Falloff(d);
                var body = creation.BodyIndexOf(part.Id);
                var result = world.DamagePart(creation, part.Id, dealt);
                if (!result.Broken && impulse > 0 && centre != at)
                    world.Push(creation, body, centre, Vector3.Normalize(centre - at) * impulse * (dealt / damage));
                hits.Add(new ShotHitView("part", centre.ToVec3(), dealt, Creation: creation.Id, Part: part.Id));
            }
        }
        foreach (var placement in world.ObjectsNear(at, radius).ToList())
        {
            var dealt = Falloff(Vector3.Distance(ObjectCentre(placement), at));
            DamageObject(placement.Id, dealt);
            hits.Add(new ShotHitView("object", ObjectCentre(placement).ToVec3(), dealt, Object: placement.Id.ToString()));
        }
    }

    private bool CanHurt(ActionCaller caller, int peer) =>
        Mode == GameMode.Survival && (settings.Pvp || caller.Source != ActionSource.Player);

    // ---- World objects ----

    /// <summary>
    /// Takes <paramref name="damage"/> health from a world object with a <c>health</c>. At 0 it breaks for
    /// every peer and leaves its <c>drops</c>. Returns the health left (0 when it broke, or for an object
    /// that cannot break).
    /// </summary>
    public float DamageObject(WorldObjectId id, float damage)
    {
        if (world.FindObject(id) is not { Definition.Health: { } full } placement)
            return 0f;
        var total = world.AddObjectDamage(id, damage);
        if (total < full)
            return full - total;
        world.DestroyObject(id);
        var items = (placement.Definition.Drops ?? [])
            .Where(d => d.Item is not null && catalog.FindDefinition(d.Item) is not null)
            .Select(d => new ItemStack(Identifiers.Qualify(d.Item!), d.Count)).ToList();
        AddDrop(placement.Position + new Vector3(0, 0.5f, 0), items);
        return 0f;
    }

    private static Vector3 ObjectCentre(WorldObjectPlacement placement) =>
        placement.Position + new Vector3(0, (placement.Definition.Collider?.At.Y ?? 0) * placement.Scale, 0);

    // ---- IStrikeTargets (the Sledgehammer) ----

    /// <inheritdoc/>
    public Vector3? PlayerCentre(int peer) =>
        !IsDead(peer) && _vitals.ContainsKey(peer) && world.FeetOf(peer) is { } feet && world.EyeOf(peer) is { } eye ? (feet + eye) / 2 : null;

    /// <inheritdoc/>
    public string? CheckStrikePlayer(ActionCaller caller, int peer) =>
        Mode == GameMode.Creative ? "Players cannot be hurt in Creative."
        : !settings.Pvp && caller.Source == ActionSource.Player ? "Players cannot hurt each other in this world."
        : null;

    /// <inheritdoc/>
    public float StrikePlayer(ActionCaller caller, int peer, float damage) => Damage(peer, damage, "sledge");

    /// <inheritdoc/>
    public Vector3? ObjectCentre(string id) =>
        WorldObjectId.TryParse(id, out var parsed) && world.FindObject(parsed) is { Definition.Health: not null } placement ? ObjectCentre(placement) : null;

    /// <inheritdoc/>
    public float StrikeObject(string id, float damage) => WorldObjectId.TryParse(id, out var parsed) ? DamageObject(parsed, damage) : 0f;

    // ---- Impacts ----

    /// <summary>
    /// A Body's velocity changed by <paramref name="deltaVelocity"/> (m/s) in one tick: a crash. Parts on
    /// the side that hit (the leading face against the change, <see cref="ImpactSettings.Depth"/> deep)
    /// take damage for every m/s above their threshold. Survival only. Returns the parts damaged.
    /// </summary>
    public IReadOnlyList<int> Impact(Creation creation, int bodyIndex, Vector3 deltaVelocity)
    {
        var speed = deltaVelocity.Length();
        if (Mode != GameMode.Survival || speed < settings.Impact.MinSpeed || bodyIndex >= creation.Bodies.Count)
            return [];
        // The ground pushed the Body along deltaVelocity, so the parts that hit lead the other way.
        var into = -deltaVelocity / speed;
        var members = creation.Bodies[bodyIndex].PartIds.Select(id => (Id: id, Along: Vector3.Dot(PartCentre(creation, id), into))).ToList();
        var lead = members.Max(m => m.Along);
        var damaged = new List<int>();
        foreach (var (id, along) in members.Where(m => m.Along >= lead - settings.Impact.Depth).ToList())
        {
            if (creation.FindPart(id) is null)
                continue;
            var over = speed - settings.Impact.ThresholdFor(creation.DefinitionOf(id).Durability);
            if (over <= 0)
                continue;
            world.DamagePart(creation, id, over * settings.Impact.DamagePerSpeed);
            damaged.Add(id);
        }
        return damaged;
    }

    // ---- Farming ----

    private string? ValidatePlant(ActionCaller caller, PartUseArgs args)
    {
        if (CheckSoilUse(caller, args, out var soil, out _) is { } bad)
            return bad;
        if (CheckSlot(caller, args.Slot, out var item) is { } noItem)
            return noItem;
        if (item!.Seed is not { } seed || catalog.FindDefinition(seed.Crop ?? "") is null)
            return $"{item.Name} cannot be planted.";
        return soil!.Seed is not null ? "Something is already growing there." : null;
    }

    private JsonElement? ApplyPlant(ActionCaller caller, PartUseArgs args)
    {
        var pockets = inventories.PlayerInventory(caller.Peer);
        var stack = pockets[args.Slot]!;
        pockets.SetSlot(args.Slot, stack.Count > 1 ? stack with { Count = stack.Count - 1 } : null);
        world.FindCreation(args.Creation)!.SoilOf(args.Part)!.Plant(stack.Item);
        return Value(SoilViewOf(world.FindCreation(args.Creation)!, args.Part));
    }

    private string? ValidateWater(ActionCaller caller, PartUseArgs args)
    {
        if (CheckSoilUse(caller, args, out var soil, out var definition) is { } bad)
            return bad;
        if (CheckSlot(caller, args.Slot, out var item) is { } noItem)
            return noItem;
        if (item!.Watering is null)
            return $"{item.Name} does not water anything.";
        return soil!.Water >= (definition!.Soil!.Capacity ?? 0) ? "The soil is already full of water." : null;
    }

    private JsonElement? ApplyWater(ActionCaller caller, PartUseArgs args)
    {
        var pockets = inventories.PlayerInventory(caller.Peer);
        var stack = pockets[args.Slot]!;
        var watering = catalog.FindDefinition(stack.Item)!.Watering!;
        pockets.SetSlot(args.Slot, stack.Count > 1 ? stack with { Count = stack.Count - 1 } : null);
        var creation = world.FindCreation(args.Creation)!;
        creation.SoilOf(args.Part)!.AddWater(watering.Seconds ?? 0, creation.DefinitionOf(args.Part).Soil!.Capacity ?? 0);
        return Value(SoilViewOf(creation, args.Part));
    }

    private string? ValidateHarvest(ActionCaller caller, PartUseArgs args)
    {
        if (CheckSoilUse(caller, args, out var soil, out _) is { } bad)
            return bad;
        if (soil!.Seed is not { } seedId || catalog.FindDefinition(seedId)?.Seed is not { } seed)
            return "Nothing is growing there.";
        if (soil.StageFor(seed.GrowTime ?? 0) < SoilState.Stages)
            return $"The {NameOf(seed.Crop!)} is not ripe yet.";
        return inventories.PlayerInventory(caller.Peer).CanAddAll(HarvestOf(seedId, seed)) ? null : "Make room in your inventory first.";
    }

    private JsonElement? ApplyHarvest(ActionCaller caller, PartUseArgs args)
    {
        var soil = world.FindCreation(args.Creation)!.SoilOf(args.Part)!;
        var items = HarvestOf(soil.Seed!, catalog.FindDefinition(soil.Seed!)!.Seed!);
        inventories.PlayerInventory(caller.Peer).TryAddAll(items);
        soil.Clear();
        return Value(new HarvestResult(items));
    }

    private static List<ItemAmount> HarvestOf(string seedId, SeedDefinition seed)
    {
        var items = new List<ItemAmount> { new(Identifiers.Qualify(seed.Crop!), seed.Yield ?? 1) };
        if (seed.Seeds is > 0)
            items.Add(new ItemAmount(seedId, seed.Seeds.Value));
        return items;
    }

    private string? CheckSoilUse(ActionCaller caller, PartUseArgs args, out SoilState? soil, out PartDefinition? definition)
    {
        if (FindSoil(args.Creation, args.Part, out soil, out definition) is { } bad)
            return bad;
        return CheckReach(caller, PartCentre(world.FindCreation(args.Creation)!, args.Part));
    }

    private string? FindSoil(int creationId, int partId, out SoilState? soil, out PartDefinition? definition)
    {
        soil = null;
        definition = null;
        if (world.FindCreation(creationId) is not { } creation || creation.FindPart(partId) is null)
            return $"There is no part {partId} in creation {creationId}.";
        definition = creation.DefinitionOf(partId);
        soil = creation.SoilOf(partId);
        return soil is null ? $"The {definition.Name ?? "part"} is not soil." : null;
    }

    /// <summary>What grows on a soil part, for <c>GetSoil</c> and client sync.</summary>
    public SoilView SoilViewOf(Creation creation, int partId)
    {
        var soil = creation.SoilOf(partId)!;
        var seed = soil.Seed is { } id ? catalog.FindDefinition(id)?.Seed : null;
        var crop = seed?.Crop is { } c ? Identifiers.Qualify(c) : null;
        return new SoilView(creation.Id, partId, soil.Seed, crop, soil.StageFor(seed?.GrowTime ?? 0), soil.Water,
            creation.DefinitionOf(partId).Soil?.Capacity ?? 0);
    }

    /// <summary>
    /// What every planted crop is worth, summed (each seed's <c>value</c>). Chunk 21 scales raids by it.
    /// </summary>
    public float CropValue() => world.AllCreations.Sum(c => c.ActiveSoil
        .Where(s => s.Soil.Seed is not null)
        .Sum(s => catalog.FindDefinition(s.Soil.Seed!)?.Seed?.Value ?? 1f));

    // ---- Water ----

    private string? ValidateCollectWater(ActionCaller caller, CollectWaterArgs args)
    {
        var point = args.Point.ToNumerics();
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
            return "point must be finite.";
        if (catalog.FindDefinition(settings.WaterItem) is null)
            return $"There is no '{settings.WaterItem}' item to fill.";
        if (CheckReach(caller, point) is { } far)
            return far;
        if (!world.IsWater(point))
            return "There is no water there.";
        if (_lastWater.TryGetValue(caller.Peer, out var last) && world.Now - last < settings.WaterSeconds * IntervalJitter)
            return "You are still filling the last one.";
        return inventories.PlayerInventory(caller.Peer).SpaceFor(settings.WaterItem) > 0 ? null : "Make room in your inventory first.";
    }

    private JsonElement? ApplyCollectWater(ActionCaller caller, CollectWaterArgs args)
    {
        _lastWater[caller.Peer] = world.Now;
        inventories.PlayerInventory(caller.Peer).Add(settings.WaterItem, 1);
        return null;
    }

    // ---- Items on the ground ----

    private string? ValidateDrop(ActionCaller caller, DropItemsArgs args)
    {
        if (CheckSlot(caller, args.Slot, out _) is { } bad)
            return bad;
        var stack = inventories.PlayerInventory(caller.Peer)[args.Slot]!;
        if (args.Count is { } count && (count < 1 || count > stack.Count))
            return $"count must be 1 to {stack.Count} (got {count}).";
        return world.FeetOf(caller.Peer) is null ? "You have no player to drop it from." : null;
    }

    private JsonElement? ApplyDrop(ActionCaller caller, DropItemsArgs args)
    {
        var pockets = inventories.PlayerInventory(caller.Peer);
        var stack = pockets[args.Slot]!;
        var count = args.Count ?? stack.Count;
        pockets.SetSlot(args.Slot, count < stack.Count ? stack with { Count = stack.Count - count } : null);
        var look = world.LookOf(caller.Peer) ?? Vector3.UnitZ;
        var ahead = new Vector3(look.X, 0, look.Z) is { } flat && flat.LengthSquared() > 1e-4f ? Vector3.Normalize(flat) : Vector3.Zero;
        var drop = AddDrop(world.FeetOf(caller.Peer)!.Value + ahead + new Vector3(0, 0.5f, 0), [stack with { Count = count }]);
        return Value(new DropItemsResult(drop?.Id ?? 0));
    }

    private string? ValidatePickUp(ActionCaller caller, PickUpArgs args)
    {
        if (!_drops.TryGetValue(args.Drop, out var drop))
            return $"There is nothing to pick up there (pile {args.Drop}).";
        if (caller.Source == ActionSource.Player)
        {
            if (world.EyeOf(caller.Peer) is not { } eye || world.FeetOf(caller.Peer) is not { } feet)
                return "You have no player to pick it up with.";
            // Measured from the nearer of eyes and feet, so a pile at your feet is in reach.
            var at = drop.Position.ToNumerics();
            var distance = Math.Min(Vector3.Distance(eye, at), Vector3.Distance(feet, at));
            if (distance > settings.PickupReach)
                return $"That is {distance:F1} m away; you can pick up from {settings.PickupReach:F1} m.";
        }
        var pockets = inventories.PlayerInventory(caller.Peer);
        return drop.Items.Any(s => pockets.SpaceFor(s.Item) > 0) ? null : "Make room in your inventory first.";
    }

    private JsonElement? ApplyPickUp(ActionCaller caller, PickUpArgs args)
    {
        var drop = _drops[args.Drop];
        var pockets = inventories.PlayerInventory(caller.Peer);
        var left = new List<ItemStack>();
        var taken = 0;
        foreach (var stack in drop.Items)
        {
            var added = pockets.Add(stack.Item, stack.Count);
            taken += added;
            if (added < stack.Count)
                left.Add(stack with { Count = stack.Count - added });
        }
        if (left.Count == 0)
            RemoveDrop(drop.Id);
        else
        {
            _drops[drop.Id] = drop with { Items = left };
            DropChanged?.Invoke(_drops[drop.Id]);
        }
        return Value(new PickUpResult(taken, left.Sum(s => s.Count)));
    }

    /// <summary>
    /// Puts a pile of items on the ground below <paramref name="at"/>. Returns it, or null when there was
    /// nothing to drop. Past <see cref="SurvivalSettings.MaxDrops"/> piles the oldest goes.
    /// </summary>
    public ItemDrop? AddDrop(Vector3 at, IReadOnlyList<ItemStack> items)
    {
        var kept = items.Where(s => s.Count > 0).ToList();
        if (kept.Count == 0)
            return null;
        while (_drops.Count >= settings.MaxDrops)
            RemoveDrop(_drops.Keys.First());
        var drop = new ItemDrop(_nextDrop++, world.GroundBelow(at).ToVec3(), kept, world.Now + settings.DropLifetime);
        _drops[drop.Id] = drop;
        DropChanged?.Invoke(drop);
        return drop;
    }

    /// <summary>A part with an inventory is breaking: everything in it falls out as a pile where it was.</summary>
    public void Spill(Creation creation, int partId)
    {
        if (creation.InventoryOf(partId) is not { IsEmpty: false } inventory)
            return;
        var items = inventory.Slots.Where(s => s is not null).Select(s => s!).ToList();
        inventory.Clear();
        AddDrop(PartCentre(creation, partId), items);
    }

    private void RemoveDrop(int id)
    {
        if (_drops.Remove(id))
            DropRemoved?.Invoke(id);
    }

    // ---- IBuildCosts ----

    /// <inheritdoc/>
    public string? CheckPlace(ActionCaller caller, string part) =>
        !Pays(caller) || inventories.PlayerInventory(caller.Peer).Count(part) > 0 ? null : $"You have no {NameOf(part)} to place.";

    /// <inheritdoc/>
    public void Placed(ActionCaller caller, string part)
    {
        if (Pays(caller))
            inventories.PlayerInventory(caller.Peer).Remove(part, 1);
    }

    /// <inheritdoc/>
    public void Removed(ActionCaller caller, string part, Vector3 at)
    {
        if (Pays(caller) && inventories.PlayerInventory(caller.Peer).Add(part, 1) == 0)
            AddDrop(at, [new ItemStack(part, 1)]);
    }

    private bool Pays(ActionCaller caller) => Mode == GameMode.Survival && caller.Source == ActionSource.Player;

    // ---- Ticking ----

    /// <summary>Grows every watered crop and clears piles that have lain too long. The host calls it every physics tick.</summary>
    public void Tick(float seconds)
    {
        foreach (var creation in world.AllCreations)
        {
            foreach (var (part, soil) in creation.ActiveSoil.ToList())
            {
                var growTime = soil.Seed is { } seed ? catalog.FindDefinition(seed)?.Seed?.GrowTime ?? 0 : 0;
                soil.Tick(seconds, creation.DefinitionOf(part).Soil?.Growth ?? 1f, growTime);
            }
        }
        foreach (var expired in _drops.Values.Where(d => d.Expires <= world.Now).Select(d => d.Id).ToList())
            RemoveDrop(expired);
    }

    // ---- Shared ----

    private string? CheckSlot(ActionCaller caller, int slot, out PartDefinition? item)
    {
        item = null;
        var pockets = inventories.PlayerInventory(caller.Peer);
        if (slot < 0 || slot >= pockets.SlotCount)
            return $"slot must be 0 to {pockets.SlotCount - 1} (got {slot}).";
        if (pockets[slot] is not { } stack)
            return $"Slot {slot} is empty.";
        item = catalog.FindDefinition(stack.Item);
        return item is null ? $"Unknown item '{stack.Item}'." : null;
    }

    private string? CheckReach(ActionCaller caller, Vector3 point)
    {
        if (caller.Source != ActionSource.Player)
            return null;
        if (world.EyeOf(caller.Peer) is not { } eye)
            return "You have no player to reach it with.";
        var distance = Vector3.Distance(eye, point);
        return distance <= settings.Reach ? null : $"That is {distance:F1} m away; you can reach {settings.Reach:F1} m.";
    }

    private static Vector3 PartCentre(Creation creation, int partId)
    {
        var part = creation.FindPart(partId)!;
        var pose = creation.BodyPose(creation.BodyIndexOf(partId));
        return pose.Apply(PartGeometry.CentreMetres(creation.DefinitionOf(partId), part.Cell, part.Rotation).ToNumerics());
    }

    private static float AngleDegrees(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)) * 180f / MathF.PI;

    private string NameOf(string item) => catalog.FindDefinition(item)?.Name ?? item;

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}
