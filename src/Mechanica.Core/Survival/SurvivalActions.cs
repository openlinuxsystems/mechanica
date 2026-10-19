using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Building;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.World;

namespace Mechanica.Core.Survival;

/// <summary>
/// The survival loop (chunk 20) in the Action API, validated on the host like every other action:
/// <list type="bullet">
/// <item>Health is the only meter (D12). Damage comes from weapons, the Sledgehammer, falls and splash;
/// at 0 a player dies, drops what they carry (except tools) and may <c>Respawn</c> after a delay, at their bed or the
/// world spawn. In Creative nothing hurts a player.</item>
/// <item>Weapons and food are Lua item scripts (chunk 20b, D2). They hurt and heal through the generic
/// combat primitives here: <see cref="Hitscan"/>, <see cref="Hit"/>, <see cref="Splash"/>, <see cref="Launch"/>
/// (projectiles) and <see cref="Heal"/>.</item>
/// <item>Farming: seeds and the water bucket are Lua item scripts (chunk 20b) that plant and water soil
/// through <see cref="PlantSeed"/> and <see cref="WaterSoil"/>. Buckets come from <c>CollectWater</c>. How
/// crops grow and are harvested is the soil part's own Lua script (D2: growth in <c>on_update</c>, the harvest
/// in <c>on_activate</c> through <c>ActivatePart</c>); C# only keeps the <see cref="SoilState"/> it decides.</item>
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

    /// <summary>Action name for starting to hold Use on a resource node to refine it.</summary>
    public const string StartRefine = "StartRefine";

    /// <summary>Action name for letting go of Use (or moving off) before a refine finished.</summary>
    public const string CancelRefine = "CancelRefine";

    /// <summary>Action name for finishing a refine once Use has been held long enough.</summary>
    public const string FinishRefine = "FinishRefine";

    /// <summary>Action name for gathering a world object by hand with Use (an oil geyser).</summary>
    public const string Harvest = "Harvest";

    // Seconds between the hurts of a liquid that hurts (a chemical pool, LiquidDefinition.Damage) a player stands in.
    private const float SoakSeconds = 0.5f;

    /// <summary>Action name for turning friendly fire on or off (the host only).</summary>
    public const string SetFriendlyFire = "SetFriendlyFire";

    /// <summary>Vanilla sound played where a resource node is refined (the Refine Tool's last stroke).</summary>
    public const string RefineCompleteSound = "refiner_complete";

    /// <summary>Sound key an item plays when it is picked up (<see cref="Sound.PartSounds"/>).</summary>
    public const string PickupSound = "pickup";

    // Share of the filling time the host insists on between one player's buckets (network jitter).
    private const double IntervalJitter = 0.8;

    private readonly SortedDictionary<int, Vitals> _vitals = [];
    private readonly SortedDictionary<int, ItemDrop> _drops = [];
    private readonly SortedDictionary<int, Flight> _projectiles = [];
    private readonly Dictionary<int, double> _lastWater = [];
    // Refines being held, by peer: the node, when Use went down and where the player stood (per player, UI spec §10).
    private readonly Dictionary<int, (string Target, double Started, Vector3 Feet)> _refining = [];
    // Seconds each player has stood in a liquid that hurts since that last hurt them (hurt in steps, not every tick).
    private readonly Dictionary<int, (string Liquid, float Seconds)> _soaking = [];
    // Picks counts of drops that give a range (count to count_max).
    private readonly Random _rolls = new(20261019);
    private int _nextDrop = 1;
    private int _nextProjectile = 1;

    private sealed class Vitals(float health)
    {
        public float Health = health;
        public bool Dead;
        public double DiedAt;
        public (int Creation, int Part)? Bed;
        public float SlowFactor = 1f;
        public double SlowUntil;
    }

    /// <summary>Creative or Survival. The host sets it before players join.</summary>
    public GameMode Mode { get; set; } = GameMode.Survival;

    /// <summary>How hard the world is; it scales bot damage and raids (<see cref="SurvivalSettings.Difficulties"/>).</summary>
    public Difficulty Difficulty { get; set; } = Difficulty.Normal;

    /// <summary>The multipliers of <see cref="Difficulty"/>.</summary>
    public DifficultySettings Rules => settings.For(Difficulty);

    /// <summary>
    /// Whether players can hurt each other (off by default): with it off no player, external AI or part script
    /// (a player's mounted gun or cutter) hurts another player, by any weapon, splash or the Sledgehammer. Bots,
    /// falls and fire still do, and a player's own splash still hurts them. The host picks it in New Game and
    /// may change it later (<c>SetFriendlyFire</c>); every player is told.
    /// </summary>
    public bool FriendlyFire
    {
        get => _friendlyFire;
        set
        {
            if (_friendlyFire == value)
                return;
            _friendlyFire = value;
            foreach (var peer in _vitals.Keys.ToList())
                VitalsChanged?.Invoke(peer);
        }
    }

    private bool _friendlyFire;

    /// <summary>The numbers in use.</summary>
    public SurvivalSettings Settings => settings;

    /// <summary>The bots weapons, splash and the Sledgehammer can hurt (chunk 21); null when there are none.</summary>
    public IBotTargets? Bots { get; set; }

    /// <summary>Item and part definitions.</summary>
    public IPartCatalog Catalog => catalog;

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

    /// <summary>A blow rocked a player who lived through it (<see cref="HitSettings"/>): how hard, and where it came from.</summary>
    public event Action<PlayerHitView>? PlayerHit;

    /// <summary>Registers every survival action.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<CollectWaterArgs>(CollectWater, ValidateCollectWater, ApplyCollectWater);
        dispatcher.Register<PartUseArgs>(SetSpawn, ValidateSetSpawn, ApplySetSpawn);
        dispatcher.Register<DropItemsArgs>(DropItems, ValidateDrop, ApplyDrop);
        dispatcher.Register<PickUpArgs>(PickUp, ValidatePickUp, ApplyPickUp);
        dispatcher.Register<NoArgs>(Respawn, ValidateRespawn, ApplyRespawn);
        dispatcher.Register<RefineArgs>(StartRefine, ValidateStartRefine, ApplyStartRefine);
        dispatcher.Register<NoArgs>(CancelRefine, (_, _) => null, (caller, _) =>
        {
            _refining.Remove(caller.Peer);
            return null;
        });
        dispatcher.Register<RefineArgs>(FinishRefine, ValidateFinishRefine, ApplyFinishRefine);
        dispatcher.Register<HarvestArgs>(Harvest, ValidateHarvest, ApplyHarvest);
        dispatcher.Register<SetFriendlyFireArgs>(SetFriendlyFire,
            (c, _) => c.Peer == Creation.HostPeer && c.Source is ActionSource.Player or ActionSource.External ? null : "Only the host can change friendly fire.",
            (_, a) =>
            {
                FriendlyFire = a.On;
                return null;
            });
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
        _lastWater.Remove(peer);
        _refining.Remove(peer);
        _soaking.Remove(peer);
    }

    /// <summary>True while <paramref name="peer"/>'s player is dead.</summary>
    public bool IsDead(int peer) => _vitals.TryGetValue(peer, out var v) && v.Dead;

    /// <summary>A player's health as clients see it, or null for an unknown peer.</summary>
    public VitalsView? View(int peer)
    {
        if (!_vitals.TryGetValue(peer, out var v))
            return null;
        var wait = v.Dead ? Math.Max(0, settings.RespawnDelay - (float)(world.Now - v.DiedAt)) : 0f;
        var slowFor = (float)Math.Max(0, v.SlowUntil - world.Now);
        return new VitalsView(peer, v.Health, settings.MaxHealth, v.Dead, wait, BedOf(peer) is not null, Mode,
            slowFor > 0 ? v.SlowFactor : 1f, slowFor, Difficulty, FriendlyFire);
    }

    /// <summary>
    /// Slows a player: their movement speed is multiplied by <paramref name="factor"/> (0 to 1) for
    /// <paramref name="seconds"/>. A stronger or longer slow already on them is kept. Survival only, and
    /// not on the dead. Bots' bubble wrap (chunk 21) slows players through this.
    /// </summary>
    public void Slow(int peer, float factor, float seconds)
    {
        if (!_vitals.TryGetValue(peer, out var v) || Mode == GameMode.Creative || v.Dead
            || !(factor >= 0 && factor < 1) || !(seconds > 0) || !float.IsFinite(seconds))
            return;
        var active = v.SlowUntil > world.Now;
        v.SlowFactor = active ? Math.Min(v.SlowFactor, factor) : factor;
        v.SlowUntil = Math.Max(active ? v.SlowUntil : 0, world.Now + seconds);
        VitalsChanged?.Invoke(peer);
    }

    /// <summary>What <paramref name="peer"/>'s movement speed is multiplied by now: 1 unless they are slowed.</summary>
    public float SpeedScale(int peer) =>
        _vitals.TryGetValue(peer, out var v) && !v.Dead && v.SlowUntil > world.Now ? v.SlowFactor : 1f;

    /// <summary>
    /// Takes <paramref name="amount"/> health from a player (nothing in Creative or when dead). At 0 they
    /// die. Returns the health left. Bots (chunk 21) hurt players through this. A blow (<see cref="HitSettings.Causes"/>)
    /// they live through raises <see cref="PlayerHit"/>, with <paramref name="reaction"/> or the one its damage earns, and
    /// <paramref name="from"/>, where it came from, so they flinch away from it.
    /// </summary>
    public float Damage(int peer, float amount, string cause, Vector3? from = null, HitReaction? reaction = null)
    {
        if (!_vitals.TryGetValue(peer, out var v))
            return 0f;
        if (Mode == GameMode.Creative || v.Dead || !(amount > 0) || !float.IsFinite(amount))
            return v.Health;
        if (cause == "bot")
            amount *= Rules.BotDamage;
        if (amount <= 0)
            return v.Health;
        var lost = Math.Min(v.Health, amount);
        v.Health -= lost;
        Hurt?.Invoke(peer, lost, cause);
        if (v.Health <= 0)
            Die(peer, v);
        else if ((reaction ?? settings.Hits.ReactionTo(cause, lost)) is var kind and not HitReaction.None)
            PlayerHit?.Invoke(new PlayerHitView(peer, kind, from?.ToVec3()));
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
        v.SlowUntil = 0;
        v.DiedAt = world.Now;
        if (!settings.DropInventoryOnDeath || world.FeetOf(peer) is not { } feet)
            return;
        // Tools (the crafted Connect Tool) stay in their slots; everything else is dropped.
        var pockets = inventories.PlayerInventory(peer);
        var items = new List<ItemStack>();
        for (var slot = 0; slot < pockets.Slots.Count; slot++)
        {
            if (pockets.Slots[slot] is not { } stack || catalog.FindDefinition(stack.Item)?.Category == PartCategory.Tool)
                continue;
            items.Add(stack);
            pockets.SetSlot(slot, null);
        }
        AddDrop(feet, items, knockedOut: true, owner: peer);
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

    // ---- Combat primitives ----
    // Held-item scripts (chunk 20b: weapons and food are Lua) and bots (chunk 21) hurt and heal through
    // these. They change state directly, so callers run them only inside an action the host accepted.

    /// <summary>
    /// Gives a living player back up to <paramref name="amount"/> health, never past the maximum.
    /// Returns the health they have now (0 for an unknown peer; a dead player is not healed).
    /// </summary>
    public float Heal(int peer, float amount)
    {
        if (!_vitals.TryGetValue(peer, out var v))
            return 0f;
        if (v.Dead || !(amount > 0) || !float.IsFinite(amount))
            return v.Health;
        v.Health = Math.Min(settings.MaxHealth, v.Health + amount);
        VitalsChanged?.Invoke(peer);
        return v.Health;
    }

    /// <summary>
    /// Puts back a returning player's saved health and bed (a loaded save, a rejoin). Health is clamped to
    /// the maximum, and a bed that no longer exists is dropped. Does nothing for an unknown or dead peer.
    /// </summary>
    public void Restore(int peer, float? health, (int Creation, int Part)? bed)
    {
        if (!_vitals.TryGetValue(peer, out var v) || v.Dead)
            return;
        if (health is { } h && float.IsFinite(h) && h > 0)
            v.Health = Math.Min(settings.MaxHealth, h);
        v.Bed = bed;
        v.Bed = BedOf(peer); // null again when that bed is gone

        VitalsChanged?.Invoke(peer);
    }

    /// <summary>The bed a player respawns at, or null when they have none (or it is gone).</summary>
    public (int Creation, int Part)? RespawnBedOf(int peer) => BedOf(peer);

    /// <summary>A player's health, or null for an unknown peer.</summary>
    public float? HealthOf(int peer) => _vitals.TryGetValue(peer, out var v) ? v.Health : null;

    /// <summary>The world the actions read: players' eyes and looks, the clock, ray casts.</summary>
    public ISurvivalWorld World => world;

    /// <summary>The first thing a ray hits within <paramref name="range"/> m, ignoring <paramref name="ignorePeer"/>'s player; null for nothing.</summary>
    public RayHit? Cast(Vector3 from, Vector3 direction, float range, int ignorePeer) =>
        direction.LengthSquared() < 1e-12f || !(range > 0) ? null : world.Cast(from, Vector3.Normalize(direction), range, ignorePeer);

    /// <summary>
    /// A hitscan shot: casts from <paramref name="from"/> and deals <paramref name="damage"/> to the first
    /// thing hit (<see cref="Hit"/>), pushing a Creation with <paramref name="impulse"/> N·s along the shot.
    /// Null when nothing is in range.
    /// </summary>
    public ShotHitView? Hitscan(ActionCaller caller, Vector3 from, Vector3 direction, float range, float damage, float impulse)
    {
        if (Cast(from, direction, range, caller.Peer) is not { } hit)
            return null;
        return Hit(caller, hit, damage, Vector3.Normalize(direction) * impulse);
    }

    /// <summary>
    /// Deals <paramref name="damage"/> to what a ray hit: a player (when <paramref name="caller"/> may hurt
    /// them), a bot, a part (which may break off; otherwise its Body gets <paramref name="push"/>, in N·s) or a
    /// world object, which only takes it if it takes damage of <paramref name="kind"/> (<see cref="DamageKinds"/>: a
    /// rock outcrop takes only a drill's). Returns what was hit and the damage dealt.
    /// </summary>
    public ShotHitView Hit(ActionCaller caller, RayHit hit, float damage, Vector3 push, string kind = DamageKinds.Weapon)
    {
        var point = hit.Point.ToVec3();
        if (!(damage >= 0) || !float.IsFinite(damage))
            damage = 0;
        // A cutting tool's bite shows chips or sparks where it cut (trees show their own, through ObjectHit).
        var cutting = kind is DamageKinds.Saw or DamageKinds.Drill && damage > 0;
        if (hit.Bot is { } bot)
        {
            Bots?.DamageBot(bot, damage, "weapon", melee: false);
            if (cutting)
                world.Impact(hit.Point, "metal");
            return new ShotHitView("bot", point, damage, Bot: bot);
        }
        if (hit.Player is { } peer)
        {
            var dealt = CanHurt(caller, peer) ? damage : 0f;
            if (dealt > 0)
                Damage(peer, dealt, "weapon", PlayerCentre(caller.Peer));
            return new ShotHitView("player", point, dealt, Player: peer);
        }
        if (hit.Creation is { } id && hit.Part is { } part && world.FindCreation(id) is { } creation && creation.FindPart(part) is not null)
        {
            var body = creation.BodyIndexOf(part);
            if (cutting)
                world.Impact(hit.Point, creation.DefinitionOf(part).SoundMaterial ?? "metal");
            var result = world.DamagePart(creation, part, damage);
            if (!result.Broken && push != Vector3.Zero && float.IsFinite(push.LengthSquared()))
                world.Push(creation, body, hit.Point, push);
            return new ShotHitView("part", point, damage, Creation: id, Part: part);
        }
        if (hit.Object is { } objectId)
        {
            var dealt = world.FindObject(objectId) is { } placement && placement.Definition.TakesDamageFrom(kind) ? damage : 0f;
            // The blow comes from behind the hit point along the push, or straight in at it (a saw's bite).
            var from = push != Vector3.Zero && float.IsFinite(push.LengthSquared()) ? hit.Point - Vector3.Normalize(push) : hit.Point;
            DamageObject(objectId, dealt, kind, hit.Point, from);
            return new ShotHitView("object", point, dealt, Object: objectId.ToString());
        }
        return new ShotHitView("ground", point, 0);
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

    /// <summary>
    /// Splash: everything within <paramref name="radius"/> of <paramref name="at"/> takes damage falling off
    /// linearly from <paramref name="damage"/> at the centre to 0 at the edge. Players (the caller's own
    /// player even with friendly fire off), bots, parts (pushed away from the centre with up to <paramref name="impulse"/>
    /// N·s) and world objects. Returns everything it reached.
    /// </summary>
    public IReadOnlyList<ShotHitView> Splash(ActionCaller caller, Vector3 at, float radius, float damage, float impulse)
    {
        var hits = new List<ShotHitView>();
        if (!(radius > 0) || !float.IsFinite(radius) || !(damage > 0) || !float.IsFinite(damage) || !float.IsFinite(at.LengthSquared()))
            return hits;
        if (!(impulse >= 0) || !float.IsFinite(impulse))
            impulse = 0;
        float Falloff(float distance) => damage * Math.Clamp(1 - distance / radius, 0, 1);
        foreach (var peer in _vitals.Keys.ToList())
        {
            if (PlayerCentre(peer) is not { } centre || Vector3.Distance(centre, at) is var d && d > radius)
                continue;
            // A shooter's own splash hurts them even with friendly fire off.
            var dealt = IsOwn(caller, peer) || CanHurt(caller, peer) ? Falloff(d) : 0f;
            if (dealt > 0)
                Damage(peer, dealt, "explosion", at);
            hits.Add(new ShotHitView("player", centre.ToVec3(), dealt, Player: peer));
        }
        foreach (var (bot, centre) in Bots?.BotsNear(at, radius) ?? [])
        {
            var dealt = Falloff(Vector3.Distance(centre, at));
            Bots!.DamageBot(bot, dealt, "explosion", melee: false);
            hits.Add(new ShotHitView("bot", centre.ToVec3(), dealt, Bot: bot));
        }
        // FindCreation brings each Creation's Body poses up to date.
        foreach (var creation in world.AllCreations.Select(c => c.Id).ToList().Select(world.FindCreation).OfType<Creation>())
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
            var dealt = placement.Definition.TakesDamageFrom(DamageKinds.Explosion) ? Falloff(Vector3.Distance(ObjectCentre(placement), at)) : 0f;
            DamageObject(placement.Id, dealt, DamageKinds.Explosion, ObjectCentre(placement), at);
            hits.Add(new ShotHitView("object", ObjectCentre(placement).ToVec3(), dealt, Object: placement.Id.ToString()));
        }
        return hits;
    }

    // ---- Projectiles ----

    /// <summary>
    /// Launches a projectile for <paramref name="owner"/>: it flies from <paramref name="from"/> at
    /// <paramref name="velocity"/> m/s, accelerated by <paramref name="gravity"/> m/s², until it hits
    /// something (never its owner's player) or <paramref name="lifetime"/> seconds pass. Flight is stepped
    /// on the host in <see cref="Tick"/>; <see cref="ProjectileLanded"/> says where it ended, and whoever
    /// launched it decides what that does (a projectile deals no damage by itself). <paramref name="look"/> says how
    /// clients draw it (<see cref="ProjectileView.Look"/>), and <paramref name="size"/> how big (<see cref="ProjectileView.Size"/>).
    /// </summary>
    public ProjectileView Launch(int owner, Vector3 from, Vector3 velocity, Vector3 gravity, float lifetime, string? look = null, float? size = null)
    {
        var flight = new Flight(_nextProjectile++, owner, from, velocity, gravity, lifetime, world.Now, look, size);
        _projectiles[flight.Id] = flight;
        var view = flight.View(world.Now);
        ProjectileLaunched?.Invoke(view);
        return view;
    }

    /// <summary>Projectiles in flight, as clients are sent them (ages as of now).</summary>
    public IEnumerable<ProjectileView> Projectiles => _projectiles.Values.Select(f => f.View(world.Now)).ToList();

    /// <summary>A projectile was launched.</summary>
    public event Action<ProjectileView>? ProjectileLaunched;

    /// <summary>A projectile hit something or ran out of time; it is gone.</summary>
    public event Action<ProjectileLanding>? ProjectileLanded;

    private void StepProjectiles(float seconds)
    {
        foreach (var flight in _projectiles.Values.ToList())
        {
            var step = Math.Min(seconds, Math.Max(0f, flight.Lifetime - flight.Age));
            // Constant acceleration: this step's straight chord is exact at both ends.
            var next = flight.Position + flight.Velocity * step + flight.Gravity * (0.5f * step * step);
            var chord = next - flight.Position;
            var hit = chord.LengthSquared() > 1e-12f ? world.Cast(flight.Position, Vector3.Normalize(chord), chord.Length(), flight.Owner) : null;
            flight.Age += step;
            if (hit is null)
            {
                flight.Position = next;
                flight.Velocity += flight.Gravity * step;
                if (flight.Age < flight.Lifetime - 1e-6f)
                    continue;
            }
            _projectiles.Remove(flight.Id);
            ProjectileLanded?.Invoke(new ProjectileLanding(flight.Id, flight.Owner, hit?.Point ?? flight.Position, hit));
        }
    }

    private sealed class Flight(int id, int owner, Vector3 from, Vector3 velocity, Vector3 gravity, float lifetime, double launched, string? look, float? size)
    {
        public string? Look { get; } = look;
        public float? Size { get; } = size;
        public int Id { get; } = id;
        public int Owner { get; } = owner;
        public Vector3 From { get; } = from;
        public Vector3 LaunchVelocity { get; } = velocity;
        public Vector3 Gravity { get; } = gravity;
        public float Lifetime { get; } = lifetime;
        public double Launched { get; } = launched;
        public Vector3 Position { get; set; } = from;
        public Vector3 Velocity { get; set; } = velocity;
        public float Age { get; set; }

        public ProjectileView View(double now) =>
            new(Id, Owner, From.ToVec3(), LaunchVelocity.ToVec3(), Gravity.ToVec3(), Lifetime, (float)Math.Max(0, now - Launched), Look, Size);
    }

    // With friendly fire off only bots hurt players: players, external AIs and part scripts (players' machines) do not.
    private bool CanHurt(ActionCaller caller, int peer) =>
        Mode == GameMode.Survival && (FriendlyFire || caller.Source == ActionSource.Bot);

    // The caller's own player: a player's or an external AI's, never a script's or bot's (they act as the host's peer).
    private static bool IsOwn(ActionCaller caller, int peer) =>
        peer == caller.Peer && caller.Source is ActionSource.Player or ActionSource.External;

    // ---- World objects ----

    /// <summary>
    /// Takes <paramref name="damage"/> health from a world object with a <c>health</c>, when it takes damage of
    /// <paramref name="kind"/> (<see cref="WorldObjectDefinition.DamageFrom"/>). At 0 it breaks for every peer, leaves
    /// its <c>drops</c> and falls apart into its <c>breaks_into</c> loose parts (a rock cube into chunks), or, with
    /// <c>fell</c>, falls as a tree: the log segments stacked up its trunk tip over away from <paramref name="from"/>, where
    /// the blow came from. <paramref name="at"/> is where it landed (the object's middle when omitted). Returns the
    /// health left (0 when it broke, or for an object that cannot break).
    /// </summary>
    public float DamageObject(WorldObjectId id, float damage, string kind, Vector3? at = null, Vector3? from = null)
    {
        if (world.FindObject(id) is not { Definition.Health: { } full } placement)
            return 0f;
        if (!placement.Definition.TakesDamageFrom(kind))
            return full - world.AddObjectDamage(id, 0f);
        var definition = placement.Definition;
        var centre = ObjectCentre(placement);
        var where = at ?? centre;
        var total = world.AddObjectDamage(id, damage);
        if (total < full)
        {
            if (damage > 0)
            {
                world.ObjectHit(id, where, broke: false);
                if (ObjectSound(definition, ObjectSounds.Hit) is { } hitSound)
                    world.PlaySound(hitSound, Identifiers.VanillaModId, where);
            }
            return full - total;
        }
        world.DestroyObject(id);
        world.ObjectHit(id, where, broke: true);
        AddDrop(placement.Position + new Vector3(0, 0.5f, 0), ObjectDrops(placement));
        if (definition.BreaksInto is { } breaks)
        {
            world.PlaySound(ObjectSound(definition, ObjectSounds.Break) ?? BreakSound, Identifiers.VanillaModId, centre);
            var seed = Noise.Hash(id.ToString());
            if (definition.Fell is { } fell)
            {
                var foot = placement.Position + new Vector3(0, definition.Sink * placement.Scale, 0);
                world.Fell(breaks, foot, fell.Height * placement.Scale, FallDirection(placement, from ?? where, seed), fell.Spin, seed);
            }
            else
            {
                world.Shatter(breaks, centre, placement.Scale * 0.5f, seed);
            }
        }
        else if (ObjectSound(definition, ObjectSounds.Break) is { } breakSound)
        {
            world.PlaySound(breakSound, Identifiers.VanillaModId, centre);
        }
        return 0f;
    }

    /// <summary>The sound event a world object makes falling apart into loose parts (a rock cube cracking).</summary>
    public const string BreakSound = "drill_break";

    /// <summary>A world object's own sound event for <paramref name="key"/> (<see cref="ObjectSounds"/>), or null.</summary>
    public static string? ObjectSound(WorldObjectDefinition definition, string key) =>
        definition.Sounds?.GetValueOrDefault(key) is { Length: > 0 } sound ? sound : null;

    /// <summary>
    /// Which way a felled tree falls: level, away from <paramref name="from"/> (where the blow came from). A blow from
    /// straight above or below picks a way from <paramref name="seed"/>.
    /// </summary>
    public static Vector3 FallDirection(WorldObjectPlacement placement, Vector3 from, uint seed)
    {
        var away = placement.Position - from;
        away.Y = 0;
        if (away.LengthSquared() > 1e-6f)
            return Vector3.Normalize(away);
        var angle = Noise.Unit(Noise.Hash(seed, 7, 7)) * MathF.Tau;
        return new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
    }

    // The items a world object leaves when it breaks or is refined: its drops (one with a chance is rolled on its own),
    // then its loot table picks.
    private List<ItemStack> ObjectDrops(WorldObjectPlacement placement)
    {
        List<ItemStack> items = [.. (placement.Definition.Drops ?? [])
            .Where(d => d.Item is not null && catalog.FindDefinition(d.Item) is not null && (d.Chance is not { } chance || _rolls.NextSingle() < chance))
            .Select(d => new ItemStack(Identifiers.Qualify(d.Item!), d.CountMax is { } most && most > d.Count ? _rolls.Next(d.Count, most + 1) : d.Count))];
        if (placement.Definition.Loot is { Table: { } id } loot && world.FindLootTable(id) is { } table)
        {
            items.AddRange(LootRolls.Roll(loot, table, _rolls)
                .Where(p => catalog.FindDefinition(p.Item) is not null)
                .Select(p => new ItemStack(Identifiers.Qualify(p.Item), p.Count)));
        }
        return items;
    }

    // Plays an item's pickup sound where it was taken, if it binds one.
    private void PlayPickup(string item, Vector3 at)
    {
        if (catalog.FindDefinition(item) is { } definition && Sound.PartSounds.For(definition, PickupSound) is { } sound)
            world.PlaySound(sound, item.Contains(':') ? item.Split(':')[0] : Identifiers.VanillaModId, at);
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
        : !CanHurt(caller, peer) ? "Friendly fire is off in this world: players cannot hurt each other."
        : null;

    /// <inheritdoc/>
    public float StrikePlayer(ActionCaller caller, int peer, float damage) => Damage(peer, damage, "sledge", PlayerCentre(caller.Peer));

    /// <inheritdoc/>
    public Vector3? ObjectCentre(string id) =>
        WorldObjectId.TryParse(id, out var parsed) && world.FindObject(parsed) is { Definition.Health: not null } placement ? ObjectCentre(placement) : null;

    /// <inheritdoc/>
    public float StrikeObject(string id, float damage, string kind, Vector3? at = null, Vector3? from = null) =>
        WorldObjectId.TryParse(id, out var parsed) ? DamageObject(parsed, damage, kind, at, from) : 0f;

    /// <inheritdoc/>
    public string? ObjectSound(string id, string key) =>
        WorldObjectId.TryParse(id, out var parsed) && world.FindObject(parsed) is { } placement ? ObjectSound(placement.Definition, key) : null;

    /// <inheritdoc/>
    public Vector3? BotCentre(int bot) => Bots?.BotCentre(bot);

    /// <inheritdoc/>
    public float StrikeBot(int bot, float damage) => Bots?.DamageBot(bot, damage, "sledge", melee: true) ?? 0f;

    /// <inheritdoc/>
    public float? SledgeDamageOf(string id) =>
        WorldObjectId.TryParse(id, out var parsed) ? world.FindObject(parsed)?.Definition.SledgeDamage : null;

    /// <inheritdoc/>
    public void KnockPlayer(int peer, Vector3 velocity)
    {
        if (!IsDead(peer) && _vitals.ContainsKey(peer))
            world.Knock(peer, velocity);
    }

    /// <inheritdoc/>
    public void KnockBot(int bot, Vector3 velocity) => Bots?.Knock(bot, velocity);

    /// <inheritdoc/>
    public string? CheckBlock(ActionCaller caller) => Mode == GameMode.Creative ? null : "Blocking with the Sledgehammer is only in Creative.";

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

    /// <summary>
    /// Why <paramref name="caller"/> cannot work the soil part (it is not soil, or is out of reach); null when they can.
    /// </summary>
    public string? CheckSoil(ActionCaller caller, int creationId, int partId)
    {
        if (FindSoil(creationId, partId, out _, out _) is { } bad)
            return bad;
        return CheckReach(caller, PartCentre(world.FindCreation(creationId)!, partId));
    }

    /// <summary>Why <paramref name="caller"/> cannot plant <paramref name="seed"/> in the soil part; null when they can.</summary>
    public string? CheckPlant(ActionCaller caller, int creationId, int partId, string seed)
    {
        if (CheckSoil(caller, creationId, partId) is { } bad)
            return bad;
        if (catalog.FindDefinition(seed) is not { } item)
            return $"Unknown item '{seed}'.";
        if (CropOf(Identifiers.Qualify(seed), item.Seed) is not { } crop || catalog.FindDefinition(crop) is null)
            return $"{item.Name ?? seed} cannot be planted.";
        return world.FindCreation(creationId)!.SoilOf(partId)!.Seed is not null ? "Something is already growing there." : null;
    }

    /// <summary>
    /// Plants <paramref name="seed"/> (an item with a <c>seed</c> section) in the soil part. It takes nothing
    /// from an inventory: the seed's item script uses one up. Throws when <see cref="CheckPlant"/> refuses.
    /// </summary>
    public SoilView PlantSeed(ActionCaller caller, int creationId, int partId, string seed)
    {
        if (CheckPlant(caller, creationId, partId, seed) is { } bad)
            throw new InvalidOperationException(bad);
        var creation = world.FindCreation(creationId)!;
        creation.SoilOf(partId)!.Plant(Identifiers.Qualify(seed));
        return SoilViewOf(creation, partId);
    }

    /// <summary>
    /// Soil parts whose centre is within <paramref name="radius"/> m of <paramref name="at"/>, nearest first, as
    /// (creation, part): where a splash of water lands.
    /// </summary>
    public IReadOnlyList<(int Creation, int Part)> SoilNear(Vector3 at, float radius)
    {
        var found = new List<(int Creation, int Part, float Distance)>();
        if (!(radius > 0) || !float.IsFinite(radius) || !float.IsFinite(at.LengthSquared()))
            return [];
        foreach (var creation in world.AllCreations.Select(c => c.Id).ToList().Select(world.FindCreation).OfType<Creation>())
        {
            foreach (var part in creation.Parts)
            {
                if (creation.DefinitionOf(part.Id).Soil is null)
                    continue;
                var d = Vector3.Distance(PartCentre(creation, part.Id), at);
                if (d <= radius)
                    found.Add((creation.Id, part.Id, d));
            }
        }
        return [.. found.OrderBy(f => f.Distance).ThenBy(f => f.Creation).ThenBy(f => f.Part).Select(f => (f.Creation, f.Part))];
    }

    /// <summary>Why <paramref name="caller"/> cannot water the soil part (also when it is full); null when they can.</summary>
    public string? CheckWater(ActionCaller caller, int creationId, int partId)
    {
        if (CheckSoil(caller, creationId, partId) is { } bad)
            return bad;
        var creation = world.FindCreation(creationId)!;
        return creation.SoilOf(partId)!.Water >= (creation.DefinitionOf(partId).Soil!.Capacity ?? 0) ? "The soil is already full of water." : null;
    }

    /// <summary>
    /// Adds <paramref name="seconds"/> of water to the soil part, up to its capacity. The watering item's script
    /// says how much and uses itself up. Throws when <see cref="CheckWater"/> refuses or seconds is not positive.
    /// </summary>
    public SoilView WaterSoil(ActionCaller caller, int creationId, int partId, float seconds)
    {
        if (!(seconds > 0f) || !float.IsFinite(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds), "seconds of water must be more than 0.");
        if (CheckWater(caller, creationId, partId) is { } bad)
            throw new InvalidOperationException(bad);
        var creation = world.FindCreation(creationId)!;
        var soil = creation.DefinitionOf(partId).Soil!;
        creation.SoilOf(partId)!.AddWater(seconds, soil.Capacity ?? 0, SoilState.WetAt(soil));
        return SoilViewOf(creation, partId);
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
        var crop = soil.Seed is { } id ? CropOf(id, catalog.FindDefinition(id)?.Seed) : null;
        var definition = creation.DefinitionOf(partId).Soil;
        return new SoilView(creation.Id, partId, soil.Seed, crop, soil.Stage, soil.Water, definition?.Capacity ?? 0,
            soil.Wetness(SoilState.WetAt(definition)));
    }

    // The qualified item a seed grows into: a bare id is in the seed's own mod.
    private static string? CropOf(string seedId, SeedDefinition? seed) =>
        seed?.Crop is { } crop ? Identifiers.Qualify(crop, seedId.Split(':')[0]) : null;

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
        if (catalog.FindDefinition(settings.WaterItem) is null || catalog.FindDefinition(settings.BucketItem) is null)
            return $"There is no '{settings.BucketItem}' to fill.";
        if (EmptyBucketSlot(caller.Peer, args.Slot) is null)
        {
            var name = catalog.FindDefinition(settings.BucketItem)?.Name ?? settings.BucketItem;
            return args.Slot is null ? $"You need an empty {name} to fill." : $"That slot holds no empty {name}.";
        }
        if (CheckReach(caller, point) is { } far)
            return far;
        if (world.LiquidAt(point) is { } pool)
        {
            if (pool.Liquid.Collect is not { Item: { } item } collect || catalog.FindDefinition(item) is null)
                return "A bucket cannot hold that.";
            if (_lastWater.TryGetValue(caller.Peer, out var lastDip) && world.Now - lastDip < settings.WaterSeconds * IntervalJitter)
                return "You are still filling the last one.";
            return inventories.PlayerInventory(caller.Peer).SpaceFor(item) >= collect.Count ? null : "Make room in your inventory first.";
        }
        if (!world.IsWater(point))
            return "There is no water there.";
        if (_lastWater.TryGetValue(caller.Peer, out var last) && world.Now - last < settings.WaterSeconds * IntervalJitter)
            return "You are still filling the last one.";
        var pockets = inventories.PlayerInventory(caller.Peer);
        return pockets[EmptyBucketSlot(caller.Peer, args.Slot)!.Value]!.Count == 1 || pockets.SpaceFor(settings.WaterItem) > 0
            ? null : "Make room in your inventory first.";
    }

    // Fills the empty bucket in the slot: the last one in it turns full where it is, so it stays in hand.
    // A pool of a liquid that can be collected (a chemical pool) gives its items and the bucket stays empty.
    private JsonElement? ApplyCollectWater(ActionCaller caller, CollectWaterArgs args)
    {
        _lastWater[caller.Peer] = world.Now;
        var pockets = inventories.PlayerInventory(caller.Peer);
        var slot = EmptyBucketSlot(caller.Peer, args.Slot)!.Value;
        if (world.LiquidAt(args.Point.ToNumerics()) is { Liquid.Collect: { } collect })
        {
            pockets.Add(Identifiers.Qualify(collect.Item!), collect.Count);
            PlayPickup(collect.Item!, args.Point.ToNumerics());
            return Value(new CollectWaterResult(slot));
        }
        var stack = pockets[slot]!;
        if (stack.Count == 1)
            pockets.SetSlot(slot, new ItemStack(Identifiers.Qualify(settings.WaterItem), 1));
        else
        {
            pockets.SetSlot(slot, stack with { Count = stack.Count - 1 });
            pockets.Add(Identifiers.Qualify(settings.WaterItem), 1);
        }
        return Value(new CollectWaterResult(slot));
    }

    // The slot holding an empty bucket: the one asked for, or the first; null when there is none.
    private int? EmptyBucketSlot(int peer, int? slot)
    {
        var pockets = inventories.PlayerInventory(peer);
        var bucket = Identifiers.Qualify(settings.BucketItem);
        bool Holds(int s) => s >= 0 && s < pockets.SlotCount && pockets[s] is { } stack && Identifiers.Qualify(stack.Item) == bucket;
        if (slot is { } asked)
            return Holds(asked) ? asked : null;
        for (var s = 0; s < pockets.SlotCount; s++)
        {
            if (Holds(s))
                return s;
        }
        return null;
    }

    // ---- Refining (UI spec §9, §10) ----

    /// <summary>
    /// What a player is refining (<see cref="RefineArgs.Target"/>: a world object id, or <c>part:creation.part</c>) and
    /// when they started, or null.
    /// </summary>
    public (string Target, double Started)? RefiningOf(int peer) =>
        _refining.TryGetValue(peer, out var r) ? (r.Target, r.Started) : null;

    // Something that can be refined and is in reach of the caller: a node that still stands, or a loose part with
    // refine items. Gives what it turns into and where it is.
    private string? CheckRefinable(ActionCaller caller, RefineArgs args, out List<ItemStack> items, out Vector3 at)
    {
        items = [];
        at = default;
        if (args.Object is { } id)
        {
            if (args.Creation is not null || args.Part is not null)
                return "Give either object or creation and part, not both.";
            if (!WorldObjectId.TryParse(id, out var parsed) || world.FindObject(parsed) is not { } found)
                return $"There is nothing to refine there ({id}).";
            if (!found.Definition.Refine)
                return $"The {found.Definition.Id} cannot be refined.";
            items = ObjectDrops(found);
            at = ObjectCentre(found);
            return CheckReach(caller, at);
        }
        if (args.Creation is not { } creationId || args.Part is not { } partId)
            return "Give the object, or the creation and part, to refine.";
        if (world.FindCreation(creationId) is not { } creation || creation.FindPart(partId) is null)
            return $"There is nothing to refine there (creation {creationId}, part {partId}).";
        var definition = creation.DefinitionOf(partId);
        if (definition.Loose != true || definition.Refine is not { Count: > 0 } refine)
            return $"The {definition.Name ?? "part"} cannot be refined.";
        items = [.. refine.Where(r => r.Item is not null && r.Count is > 0 && catalog.FindDefinition(r.Item) is not null)
            .Select(r => new ItemStack(Identifiers.Qualify(r.Item!), r.Count!.Value))];
        at = PartCentre(creation, partId);
        return CheckReach(caller, at);
    }

    private string? ValidateStartRefine(ActionCaller caller, RefineArgs args) =>
        world.FeetOf(caller.Peer) is null ? "You have no player to refine with." : CheckRefinable(caller, args, out _, out _);

    private JsonElement? ApplyStartRefine(ActionCaller caller, RefineArgs args)
    {
        _refining[caller.Peer] = (args.Target, world.Now, world.FeetOf(caller.Peer)!.Value);
        return null;
    }

    private string? ValidateFinishRefine(ActionCaller caller, RefineArgs args)
    {
        if (CheckRefinable(caller, args, out _, out _) is { } bad)
            return bad;
        if (caller.Source != ActionSource.Player)
            return null;
        if (!_refining.TryGetValue(caller.Peer, out var held) || held.Target != args.Target)
            return "Hold Use on it to refine it.";
        if (world.Now - held.Started < settings.RefineSeconds * IntervalJitter)
            return "Keep holding Use to refine it.";
        if (world.FeetOf(caller.Peer) is not { } feet || Vector3.Distance(feet, held.Feet) > settings.RefineCancelDistance)
            return "You moved away, so the refine stopped.";
        return null;
    }

    private JsonElement? ApplyFinishRefine(ActionCaller caller, RefineArgs args)
    {
        _refining.Remove(caller.Peer);
        CheckRefinable(caller, args, out var items, out var at);
        if (args.Object is { } text && WorldObjectId.TryParse(text, out var id))
            world.DestroyObject(id);
        else
            world.RemovePart(world.FindCreation(args.Creation!.Value)!, args.Part!.Value);
        world.PlaySound(RefineCompleteSound, Identifiers.VanillaModId, at);
        var pockets = inventories.PlayerInventory(caller.Peer);
        var left = new List<ItemStack>();
        var taken = 0;
        foreach (var stack in items)
        {
            var added = pockets.Add(stack.Item, stack.Count);
            taken += added;
            if (added < stack.Count)
                left.Add(stack with { Count = stack.Count - added });
        }
        // What does not fit stays on the ground where it was.
        AddDrop(at, left);
        if (taken > 0)
            PlayPickup(items[0].Item, at);
        return Value(new RefineResult(taken, left.Sum(s => s.Count)));
    }

    // ---- Harvesting by hand ----

    private string? ValidateHarvest(ActionCaller caller, HarvestArgs args)
    {
        if (world.FeetOf(caller.Peer) is null)
            return "You have no player to harvest with.";
        if (!WorldObjectId.TryParse(args.Object, out var id) || world.FindObject(id) is not { } found)
            return $"There is nothing to harvest there ({args.Object}).";
        if (!found.Definition.Harvest)
            return $"The {found.Definition.Id} cannot be harvested by hand.";
        return CheckReach(caller, ObjectCentre(found));
    }

    // Takes the object's drops into the inventory (what does not fit is left where it stood) and uses it up until it regrows.
    private JsonElement? ApplyHarvest(ActionCaller caller, HarvestArgs args)
    {
        WorldObjectId.TryParse(args.Object, out var id);
        var found = world.FindObject(id)!;
        var at = ObjectCentre(found);
        var items = ObjectDrops(found);
        world.DestroyObject(id);
        var pockets = inventories.PlayerInventory(caller.Peer);
        var left = new List<ItemStack>();
        var taken = 0;
        foreach (var stack in items)
        {
            var added = pockets.Add(stack.Item, stack.Count);
            taken += added;
            if (added < stack.Count)
                left.Add(stack with { Count = stack.Count - added });
        }
        AddDrop(at, left);
        if (items.Count > 0)
            PlayPickup(items[0].Item, at);
        return Value(new HarvestResult(taken, left.Sum(s => s.Count)));
    }

    // ---- Liquids ----

    // Hurts every living player standing in a liquid that hurts (a chemical pool), every half second they stay in it.
    private void SoakPlayers(float seconds)
    {
        foreach (var peer in _vitals.Keys)
        {
            var feet = world.FeetOf(peer);
            if (_vitals[peer].Dead || feet is null || world.LiquidAt(feet.Value + new Vector3(0, 0.1f, 0)) is not { Liquid: { Damage: > 0 } liquid })
            {
                _soaking.Remove(peer);
                continue;
            }
            var soak = _soaking.TryGetValue(peer, out var was) && was.Liquid == liquid.Id ? was.Seconds + seconds : seconds;
            if (soak < SoakSeconds)
            {
                _soaking[peer] = (liquid.Id!, soak);
                continue;
            }
            // The cause is the liquid's id (chemical).
            _soaking[peer] = (liquid.Id!, 0f);
            Damage(peer, liquid.Damage * soak, liquid.Id!);
        }
    }

    // ---- Items on the ground ----

    private string? ValidateDrop(ActionCaller caller, DropItemsArgs args)
    {
        if (CheckSlot(caller, args.Slot, out _) is { } bad)
            return bad;
        var stack = inventories.PlayerInventory(caller.Peer)[args.Slot]!;
        if (args.Count is { } count && (count < 1 || count > stack.Count))
            return $"count must be 1 to {stack.Count} (got {count}).";
        if (args.At is { } at)
        {
            var point = at.ToNumerics();
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                return "at must be finite.";
            if (CheckReach(caller, point) is { } far)
                return far;
        }
        return world.FeetOf(caller.Peer) is null ? "You have no player to drop it from." : null;
    }

    private JsonElement? ApplyDrop(ActionCaller caller, DropItemsArgs args)
    {
        var pockets = inventories.PlayerInventory(caller.Peer);
        var stack = pockets[args.Slot]!;
        var count = args.Count ?? stack.Count;
        pockets.SetSlot(args.Slot, count < stack.Count ? stack with { Count = stack.Count - count } : null);
        if (args.At is { } at)
            return Value(new DropItemsResult(AddDrop(at.ToNumerics(), [stack with { Count = count }], placed: true)?.Id ?? 0));
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
        if (taken > 0)
            PlayPickup(drop.Items[0].Item, drop.Position.ToNumerics());
        return Value(new PickUpResult(taken, left.Sum(s => s.Count)));
    }

    /// <summary>
    /// Puts a pile of items on the ground below <paramref name="at"/>, or exactly at it when <paramref name="placed"/>
    /// (Force Build: it then never expires). Returns it, or null when there was nothing to drop. Past
    /// <see cref="SurvivalSettings.MaxDrops"/> piles the oldest goes. <paramref name="knockedOut"/> marks what a
    /// knocked-out player dropped (<see cref="ItemDrop.KnockedOut"/>), and <paramref name="owner"/> whose it is.
    /// </summary>
    public ItemDrop? AddDrop(Vector3 at, IReadOnlyList<ItemStack> items, bool placed = false, bool knockedOut = false, int? owner = null)
    {
        var kept = items.Where(s => s.Count > 0).ToList();
        if (kept.Count == 0)
            return null;
        while (_drops.Count >= settings.MaxDrops)
            RemoveDrop(_drops.Keys.First());
        var drop = new ItemDrop(_nextDrop++, (placed ? at : world.GroundBelow(at)).ToVec3(), kept, world.Now + settings.DropLifetime, placed, knockedOut, owner);
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
        !Pays(caller) || PayingItem(inventories.PlayerInventory(caller.Peer), part) is not null ? null
        : $"You have no {NameOf(PartItems.ItemFor(catalog, part))} to place.";

    /// <inheritdoc/>
    public string? CheckPlaceAll(ActionCaller caller, IReadOnlyDictionary<string, int> parts)
    {
        if (!Pays(caller))
            return null;
        var inventory = inventories.PlayerInventory(caller.Peer);
        var items = parts.GroupBy(p => PartItems.ItemFor(catalog, p.Key)).ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
        foreach (var (item, count) in items.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var have = inventory.Count(item);
            if (have < count)
                return $"You need {count} {NameOf(item)} and have {have}.";
        }
        return null;
    }

    /// <inheritdoc/>
    public void Placed(ActionCaller caller, string part)
    {
        if (Pays(caller) && inventories.PlayerInventory(caller.Peer) is var inventory && PayingItem(inventory, part) is { } item)
            inventory.Remove(item, 1);
    }

    /// <inheritdoc/>
    public void Removed(ActionCaller caller, string part, Vector3 at)
    {
        var item = PartItems.ItemFor(catalog, part);
        if (Pays(caller) && inventories.PlayerInventory(caller.Peer).Add(item, 1) == 0)
            AddDrop(at, [new ItemStack(item, 1)]);
    }

    /// <inheritdoc/>
    public void Emptied(ActionCaller caller, IReadOnlyList<ItemStack> items, Vector3 at)
    {
        var left = new List<ItemStack>();
        var pockets = caller.Source == ActionSource.Player ? inventories.PlayerInventory(caller.Peer) : null;
        foreach (var stack in items)
        {
            var added = pockets?.Add(stack.Item, stack.Count) ?? 0;
            if (added < stack.Count)
                left.Add(stack with { Count = stack.Count - added });
        }
        AddDrop(at, left);
    }

    // The item placing part uses up: the one it is placed from (a Soil Bag for a soil mound), or the part itself when a
    // player still carries it from before it had one (soil from an older save). Null when they have neither.
    private string? PayingItem(Inventory inventory, string part)
    {
        var item = PartItems.ItemFor(catalog, part);
        if (inventory.Count(item) > 0)
            return item;
        var own = Identifiers.Qualify(part);
        return own != item && inventory.Count(own) > 0 ? own : null;
    }

    private bool Pays(ActionCaller caller) => Mode == GameMode.Survival && caller.Source == ActionSource.Player;

    // ---- Ticking ----

    /// <summary>
    /// Moves projectiles, hurts players standing in a liquid that hurts and clears piles that have lain too long. The host
    /// calls it every physics tick.
    /// (Crops grow in the soil part's script.)
    /// </summary>
    public void Tick(float seconds)
    {
        StepProjectiles(seconds);
        SoakPlayers(seconds);
        foreach (var expired in _drops.Values.Where(d => !d.Placed && d.Expires <= world.Now).Select(d => d.Id).ToList())
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

    /// <inheritdoc/>
    public string? CheckTool(ActionCaller caller, string tool) =>
        !Pays(caller) || catalog.FindDefinition(tool) is not { Category: PartCategory.Tool } item || inventories.PlayerInventory(caller.Peer).Count(tool) > 0 ? null
        : $"Craft a {item.Name ?? tool} at a Fabricator first.";

    /// <inheritdoc/>
    public string? CheckSpend(ActionCaller caller, string item, int count) =>
        !Pays(caller) || count <= 0 || inventories.PlayerInventory(caller.Peer).Count(item) >= count ? null
        : $"You have no {NameOf(item)}. Craft some at a Fabricator.";

    /// <inheritdoc/>
    public void Spend(ActionCaller caller, string item, int count)
    {
        if (Pays(caller) && count > 0)
            inventories.PlayerInventory(caller.Peer).Remove(item, count);
    }

    private string NameOf(string item) => catalog.FindDefinition(item)?.Name ?? item;

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}
