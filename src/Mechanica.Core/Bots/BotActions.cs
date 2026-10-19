using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;

namespace Mechanica.Core.Bots;

/// <summary>A bot in the world, as the host keeps it.</summary>
public sealed class BotState
{
    internal BotState(int id, Bot type, Vector3 position, float yaw)
    {
        Id = id;
        Type = type;
        Position = position;
        Home = position;
        Yaw = yaw;
        Health = type.Health;
    }

    /// <summary>Its id.</summary>
    public int Id { get; }

    /// <summary>Its type.</summary>
    public Bot Type { get; }

    /// <summary>Its feet, in world metres. The game moves it and reports here.</summary>
    public Vector3 Position { get; internal set; }

    /// <summary>Where it spawned (where it wanders).</summary>
    public Vector3 Home { get; }

    /// <summary>Which way it faces, in degrees about the vertical (0 faces -Z, like players).</summary>
    public float Yaw { get; internal set; }

    /// <summary>The horizontal velocity it asked to walk at, in m/s.</summary>
    public Vector3 Desired { get; internal set; }

    /// <summary>
    /// Velocity a hit gave it on top of its walk, in m/s (a Sledgehammer's knockback). The game moves it by this and
    /// slows it back to zero (<see cref="TakeKnock"/>).
    /// </summary>
    public Vector3 Knock { get; internal set; }

    /// <summary>
    /// The knockback to move by this tick: its vertical part once (the game adds it to the fall), its horizontal part
    /// slowing by <paramref name="drag"/> m/s² over <paramref name="dt"/> seconds.
    /// </summary>
    public Vector3 TakeKnock(float drag, float dt)
    {
        var knock = Knock;
        var flat = new Vector3(knock.X, 0, knock.Z);
        var speed = flat.Length();
        var left = MathF.Max(0, speed - drag * dt);
        Knock = speed > 1e-6f && left > 0 ? flat * (left / speed) : Vector3.Zero;
        return knock;
    }

    /// <summary>Health now.</summary>
    public float Health { get; internal set; }

    /// <summary>True from its death until its body is gone.</summary>
    public bool Dead { get; internal set; }

    /// <summary>Host time it died.</summary>
    public double DiedAt { get; internal set; }

    /// <summary>Whether it stands in water (the game reports it).</summary>
    public bool InWater { get; internal set; }

    /// <summary>The raid it belongs to, or null.</summary>
    public int? Raid { get; internal set; }

    /// <summary>Whether it is fighting (see <see cref="BotActions.Engage"/>).</summary>
    public bool Engaged { get; internal set; }

    /// <summary>Host time its engagement ends.</summary>
    internal double EngagedUntil { get; set; }

    /// <summary>When each attack was last used, by id.</summary>
    internal Dictionary<string, double> LastUse { get; } = new(StringComparer.Ordinal);

    /// <summary>Middle of its body.</summary>
    public Vector3 Centre => Position + new Vector3(0, Type.Height / 2, 0);

    /// <summary>Its eyes.</summary>
    public Vector3 Eye => Position + new Vector3(0, Type.Height * 0.8f, 0);

    /// <summary>Top speed where it stands now.</summary>
    public float TopSpeed => InWater ? Type.WaterSpeed : Type.Speed;

    /// <summary>Seconds until an attack can be used again (0 when it can now).</summary>
    public float Cooldown(BotAttack attack, double now) =>
        LastUse.TryGetValue(attack.Id, out var last) ? (float)Math.Max(0, last + attack.Interval - now) : 0f;

    /// <summary>The view clients are sent.</summary>
    public BotView View => new(Id, Type.QualifiedId, Position.ToVec3(), Yaw, Health, Type.Health, Dead, Raid, Engaged);
}

/// <summary>
/// Bots in the Action API (chunk 21), validated on the host like every other action. Bots use exactly
/// the calls an external agent could: <c>SpawnBot</c>, <c>MoveBot</c> (walk a direction at up to top
/// speed), <c>BotAttack</c> (one of the bot's attacks, checked for reach, line of sight and cooldown),
/// <c>DespawnBot</c>, and the queries <c>GetBots</c> and <c>GetBot</c>. Players cannot drive bots;
/// in Creative they may spawn and remove them.
/// <para>
/// It is also the bots' side of the damage API (<see cref="IBotTargets"/>): weapons, splash and the
/// Sledgehammer hurt bots through it. A bot at 0 health dies, drops what its data rolls, and its body
/// goes after <see cref="BotSettings.CorpseSeconds"/>. Bots hurt players, crops and parts through
/// <see cref="SurvivalActions"/>: a hit on soil with a crop on it destroys the crop.
/// </para>
/// </summary>
public sealed class BotActions(IBotCatalog bots, BotSettings settings, SurvivalActions survival, ISurvivalWorld survivalWorld, IBotWorld world)
    : IBotTargets
{
    /// <summary>Action name for putting a bot in the world.</summary>
    public const string SpawnBot = "SpawnBot";

    /// <summary>Action name for taking a bot out of the world.</summary>
    public const string DespawnBot = "DespawnBot";

    /// <summary>Action name for setting where a bot walks.</summary>
    public const string MoveBot = "MoveBot";

    /// <summary>Action name for a bot's attack.</summary>
    public const string BotAttack = "BotAttack";

    /// <summary>Action name for listing every bot.</summary>
    public const string GetBots = "GetBots";

    /// <summary>Action name for reading one bot.</summary>
    public const string GetBot = "GetBot";

    // Share of an attack's interval the host insists on (think timing jitter).
    private const double IntervalJitter = 0.98;

    private readonly SortedDictionary<int, BotState> _bots = [];
    private readonly List<(int Peer, float PerSecond, double Until)> _lasting = [];
    private (double Now, long Key, List<CropTarget> Crops)? _crops;
    private int _nextId = 1;

    /// <summary>Every bot in the world, dead ones included until their body goes, by id.</summary>
    public IReadOnlyDictionary<int, BotState> Bots => _bots;

    /// <summary>The bot types.</summary>
    public IBotCatalog Types => bots;

    /// <summary>The numbers in use.</summary>
    public BotSettings Settings => settings;

    /// <summary>The host's clock, in seconds.</summary>
    public double Now => survivalWorld.Now;

    /// <summary>Seed for drop rolls (the world seed), so a replay drops the same.</summary>
    public int Seed { get; set; }

    /// <summary>A bot appeared.</summary>
    public event Action<BotState>? Spawned;

    /// <summary>A bot's health, raid or engagement changed, or it died.</summary>
    public event Action<BotState>? Changed;

    /// <summary>A bot is gone (despawned, or its body went after death).</summary>
    public event Action<int>? Removed;

    /// <summary>A bot used an attack.</summary>
    public event Action<BotAttackEvent>? Attacked;

    /// <summary>A crop was destroyed by a bot: Creation, soil part, bot.</summary>
    public event Action<int, int, int>? CropDestroyed;

    /// <summary>Registers every bot action.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<SpawnBotArgs>(SpawnBot, ValidateSpawn, ApplySpawn);
        dispatcher.Register<BotArgs>(DespawnBot, ValidateDespawn, (_, a) =>
        {
            Remove(a.Bot);
            return null;
        });
        dispatcher.Register<MoveBotArgs>(MoveBot, ValidateMove, ApplyMove);
        dispatcher.Register<BotAttackArgs>(BotAttack, ValidateAttack, ApplyAttack);
        dispatcher.Register<NoArgs>(GetBots, (_, _) => null, (_, _) => BotValues.Value(_bots.Values.Select(b => b.View).ToList()), readOnly: true);
        dispatcher.Register<BotArgs>(GetBot, (_, a) => _bots.ContainsKey(a.Bot) ? null : $"There is no bot {a.Bot}.",
            (_, a) => BotValues.Value(_bots[a.Bot].View), readOnly: true);
    }

    /// <summary>A bot by id, or null.</summary>
    public BotState? Find(int id) => _bots.GetValueOrDefault(id);

    // ---- Spawning ----

    /// <summary>Whether a caller may make and remove bots: anything but a player, or anyone in Creative.</summary>
    private string? CheckDebug(ActionCaller caller, string what) =>
        caller.Source == ActionSource.Player && survival.Mode != GameMode.Creative ? $"Players can {what} bots only in Creative." : null;

    private string? ValidateSpawn(ActionCaller caller, SpawnBotArgs args)
    {
        if (CheckDebug(caller, "spawn") is { } refused)
            return refused;
        if (args.Position is not { Length: 3 } p || !p.All(float.IsFinite) || !float.IsFinite(args.Yaw))
            return "position must be [x, y, z] and yaw a finite number.";
        if (MathF.Sqrt(p[0] * p[0] + p[2] * p[2]) > settings.MaxSpawnDistance)
            return $"position is more than {settings.MaxSpawnDistance} m from the world centre.";
        if (bots.FindBot(args.Bot) is null)
            return $"There is no bot type '{args.Bot}'.";
        return _bots.Count >= settings.MaxBots ? $"The world already has {settings.MaxBots} bots." : null;
    }

    private JsonElement? ApplySpawn(ActionCaller caller, SpawnBotArgs args) =>
        BotValues.Value(new SpawnBotResult(Spawn(bots.FindBot(args.Bot)!, new Vector3(args.Position[0], args.Position[1], args.Position[2]), args.Yaw).Id));

    /// <summary>Adds a bot without validation (the raid director has already checked). Returns it.</summary>
    internal BotState Spawn(Bot type, Vector3 feet, float yaw)
    {
        var bot = new BotState(_nextId++, type, feet, yaw);
        _bots[bot.Id] = bot;
        Spawned?.Invoke(bot);
        return bot;
    }

    private string? ValidateDespawn(ActionCaller caller, BotArgs args) =>
        CheckDebug(caller, "remove") ?? (_bots.ContainsKey(args.Bot) ? null : $"There is no bot {args.Bot}.");

    /// <summary>Takes a bot out of the world at once.</summary>
    public void Remove(int id)
    {
        if (_bots.Remove(id))
            Removed?.Invoke(id);
    }

    /// <summary>Puts a bot in a raid (or takes it out with null).</summary>
    public void SetRaid(int id, int? raid)
    {
        if (_bots.TryGetValue(id, out var bot) && bot.Raid != raid)
        {
            bot.Raid = raid;
            Changed?.Invoke(bot);
        }
    }

    // ---- Moving ----

    private string? CheckDriver(ActionCaller caller, int id, out BotState? bot)
    {
        bot = null;
        if (caller.Source == ActionSource.Player)
            return "Players cannot drive bots.";
        if (!_bots.TryGetValue(id, out bot))
            return $"There is no bot {id}.";
        return bot.Dead ? $"Bot {id} is dead." : null;
    }

    private string? ValidateMove(ActionCaller caller, MoveBotArgs args)
    {
        if (CheckDriver(caller, args.Bot, out var bot) is { } bad)
            return bad;
        if (args.Direction is { } d && !(float.IsFinite(d.X) && float.IsFinite(d.Y) && float.IsFinite(d.Z)))
            return "direction must be finite.";
        if (args.Speed is { } speed && !(speed >= 0 && float.IsFinite(speed)))
            return "speed must be 0 or more.";
        if (args.Speed > bot!.TopSpeed * 1.001f)
            return $"{bot.Type.Name} walks at most {bot.TopSpeed:0.##} m/s here (asked {args.Speed:0.##}).";
        return null;
    }

    private JsonElement? ApplyMove(ActionCaller caller, MoveBotArgs args)
    {
        var bot = _bots[args.Bot];
        var flat = args.Direction is { } d ? new Vector3(d.X, 0, d.Z) : Vector3.Zero;
        if (flat.LengthSquared() < 1e-8f)
        {
            bot.Desired = Vector3.Zero;
            return null;
        }
        flat = Vector3.Normalize(flat);
        bot.Desired = flat * (args.Speed ?? bot.TopSpeed);
        bot.Yaw = YawOf(flat);
        return null;
    }

    /// <summary>Where the game moved a bot this tick, and whether it is in water.</summary>
    public void Moved(int id, Vector3 feet, bool inWater)
    {
        if (_bots.TryGetValue(id, out var bot))
            (bot.Position, bot.InWater) = (feet, inWater);
    }

    /// <summary>
    /// Keeps a living bot engaged (fighting: its eye red) for <see cref="BotSettings.EngagedSeconds"/> from now. Spotting a
    /// target (its brain's <c>bot.engage()</c> and <c>bot.alert()</c>), attacking and being hurt all call it.
    /// </summary>
    public void Engage(int id)
    {
        if (!_bots.TryGetValue(id, out var bot) || bot.Dead)
            return;
        bot.EngagedUntil = survivalWorld.Now + settings.EngagedSeconds;
        if (bot.Engaged)
            return;
        bot.Engaged = true;
        Changed?.Invoke(bot);
    }

    // ---- Attacking ----

    private string? ValidateAttack(ActionCaller caller, BotAttackArgs args)
    {
        if (CheckDriver(caller, args.Bot, out var bot) is { } bad)
            return bad;
        if (bot!.Type.Attack(args.Attack) is not { } attack)
            return $"{bot.Type.Name} has no attack '{args.Attack}'.";
        if (bot.LastUse.TryGetValue(attack.Id, out var last) && survivalWorld.Now - last < attack.Interval * IntervalJitter)
            return $"{bot.Type.Name}'s {attack.Id} is not ready.";
        var targets = (args.Player is not null ? 1 : 0) + (args.Creation is not null || args.Part is not null ? 1 : 0) + (args.Target is not null ? 1 : 0)
            + (args.Object is not null ? 1 : 0);
        if (attack.Kind == BotAttackKind.Area)
            return targets == 0 ? null : "An area attack takes no target.";
        if (targets != 1)
            return "Name one target: a player, a creation and part, a bot or a world object.";
        if (args.Object is not null && !(attack.ObjectDamage > 0))
            return $"{bot.Type.Name}'s {attack.Id} cannot break world objects.";
        if (TargetPoint(args, out var radius) is not { } point)
            return "The target is not there.";
        if (args.Target == args.Bot)
            return "A bot cannot attack itself.";
        var reach = Vector3.Distance(bot.Centre, point) - bot.Type.Radius - radius;
        if (reach > attack.Range + settings.RangeTolerance)
            return $"The target is {reach:0.0} m away; {attack.Id} reaches {attack.Range:0.0} m.";
        if (attack.Kind == BotAttackKind.Ranged && !CanSee(bot, args, point))
            return "Something is in the way.";
        return null;
    }

    // Where a single-target attack aims and how big the target is; null when it is gone.
    private Vector3? TargetPoint(BotAttackArgs args, out float radius)
    {
        radius = 0;
        if (args.Player is { } peer)
        {
            radius = settings.PlayerRadius;
            return survival.PlayerCentre(peer);
        }
        if (args.Target is { } other)
        {
            if (!_bots.TryGetValue(other, out var target) || target.Dead)
                return null;
            radius = target.Type.Radius;
            return target.Centre;
        }
        if (args.Object is { } objectId)
        {
            if (!WorldObjectId.TryParse(objectId, out var id) || survivalWorld.FindObject(id) is not { Definition.Health: not null } placement)
                return null;
            radius = ObjectRadius(placement);
            return ObjectCentre(placement);
        }
        if (args.Creation is not { } creationId || args.Part is not { } partId
            || survivalWorld.FindCreation(creationId) is not { } creation || creation.FindPart(partId) is null)
            return null;
        var size = (creation.DefinitionOf(partId).Size ?? GridSize.One).Metres;
        radius = Math.Max(size.X, Math.Max(size.Y, size.Z)) / 2;
        return PartCentre(creation, partId);
    }

    private bool CanSee(BotState bot, BotAttackArgs args, Vector3 point)
    {
        if (world.Cast(bot.Eye, point, bot.Id) is not { } hit)
            return true;
        return (args.Player is { } peer && hit.Player == peer)
            || (args.Creation is { } creation && hit.Creation == creation)
            || (args.Target is { } other && hit.Bot == other)
            || (args.Object is { } id && hit.Object?.ToString() == id);
    }

    private JsonElement? ApplyAttack(ActionCaller caller, BotAttackArgs args)
    {
        var bot = _bots[args.Bot];
        var attack = bot.Type.Attack(args.Attack)!;
        bot.LastUse[attack.Id] = survivalWorld.Now;
        Engage(bot.Id);
        var hits = new List<ShotHitView>();
        if (attack.Kind == BotAttackKind.Area)
            Blast(bot, attack, hits);
        else
        {
            var point = TargetPoint(args, out _)!.Value;
            bot.Yaw = YawOf(point - bot.Position) is var yaw && float.IsFinite(yaw) ? yaw : bot.Yaw;
            if (args.Player is { } peer)
                hits.Add(HurtPlayer(peer, attack, attack.Damage, point));
            else if (args.Target is { } other)
            {
                DamageBot(other, attack.Damage, "bot", attack.Kind == BotAttackKind.Melee);
                hits.Add(new ShotHitView("bot", point.ToVec3(), attack.Damage, Bot: other));
            }
            else if (args.Object is { } id)
            {
                survival.StrikeObject(id, attack.ObjectDamage, DamageKinds.Bot);
                hits.Add(new ShotHitView("object", point.ToVec3(), attack.ObjectDamage, Object: id));
            }
            else if (attack.HitsParts)
                hits.Add(HitPart(bot, attack, survivalWorld.FindCreation(args.Creation!.Value)!, args.Part!.Value, attack.PartDamage));
        }
        Attacked?.Invoke(new BotAttackEvent(bot.Id, attack.Id, attack.Sound, hits));
        if (attack.KillsSelf)
            Kill(bot, "self");
        return BotValues.Value(new BotAttackResult(hits));
    }

    // An area attack: everything within its radius of the bot, falling off to 0 at the edge.
    private void Blast(BotState bot, BotAttack attack, List<ShotHitView> hits)
    {
        var at = bot.Centre;
        float Falloff(float full, float distance) => full * Math.Clamp(1 - distance / attack.Radius, 0, 1);
        foreach (var peer in survival.Players.ToList())
        {
            if (survival.PlayerCentre(peer) is not { } centre)
                continue;
            var distance = Math.Max(0, Vector3.Distance(centre, at) - settings.PlayerRadius);
            if (distance <= attack.Radius)
                hits.Add(HurtPlayer(peer, attack, Falloff(attack.Damage, distance), centre));
        }
        if (attack.ObjectDamage > 0)
        {
            foreach (var placement in survivalWorld.ObjectsNear(at, attack.Radius).Where(o => o.Definition.Health is not null && o.Definition.TakesDamageFrom(DamageKinds.Bot)).ToList())
            {
                var centre = ObjectCentre(placement);
                var damage = Falloff(attack.ObjectDamage, Math.Max(0, Vector3.Distance(centre, at) - ObjectRadius(placement)));
                if (damage <= 0)
                    continue;
                survival.StrikeObject(placement.Id.ToString(), damage, DamageKinds.Bot);
                hits.Add(new ShotHitView("object", centre.ToVec3(), damage, Object: placement.Id.ToString()));
            }
        }
        if (!attack.HitsParts)
            return;
        foreach (var creation in survivalWorld.AllCreations.Select(c => c.Id).ToList().Select(survivalWorld.FindCreation).OfType<Creation>())
        {
            foreach (var part in creation.Parts.ToList())
            {
                if (creation.FindPart(part.Id) is null)
                    continue; // broken off by an earlier hit
                var distance = Vector3.Distance(PartCentre(creation, part.Id), at);
                if (distance <= attack.Radius)
                    hits.Add(HitPart(bot, attack, creation, part.Id, Falloff(attack.PartDamage, distance)));
            }
        }
    }

    private ShotHitView HurtPlayer(int peer, BotAttack attack, float damage, Vector3 point)
    {
        if (damage > 0)
            survival.Damage(peer, damage, "bot");
        if (attack.DamagePerSecond > 0 && survival.Mode == GameMode.Survival)
            _lasting.Add((peer, attack.DamagePerSecond, survivalWorld.Now + attack.DamageSeconds));
        if (attack.Slows)
            survival.Slow(peer, attack.SlowFactor, attack.SlowSeconds);
        return new ShotHitView("player", point.ToVec3(), damage, Player: peer);
    }

    // A hit on soil with a crop destroys the crop; anything else takes part damage.
    private ShotHitView HitPart(BotState bot, BotAttack attack, Creation creation, int partId, float damage)
    {
        var point = PartCentre(creation, partId).ToVec3();
        if (creation.SoilOf(partId) is { Seed: not null } soil)
        {
            soil.Clear();
            _crops = null;
            CropDestroyed?.Invoke(creation.Id, partId, bot.Id);
            return new ShotHitView("crop", point, damage, Creation: creation.Id, Part: partId);
        }
        survivalWorld.DamagePart(creation, partId, damage);
        return new ShotHitView("part", point, damage, Creation: creation.Id, Part: partId);
    }

    // ---- IBotTargets ----

    /// <inheritdoc/>
    public void Knock(int bot, Vector3 velocity)
    {
        if (_bots.TryGetValue(bot, out var b) && !b.Dead && !b.Type.MeleeImmune && float.IsFinite(velocity.LengthSquared()))
            b.Knock += velocity;
    }

    /// <inheritdoc/>
    public Vector3? BotCentre(int bot) => _bots.TryGetValue(bot, out var b) && !b.Dead ? b.Centre : null;

    /// <inheritdoc/>
    public IEnumerable<(int Bot, Vector3 Centre)> BotsNear(Vector3 point, float radius) =>
        _bots.Values.Where(b => !b.Dead && Vector3.Distance(b.Centre, point) <= radius + b.Type.Radius).Select(b => (b.Id, b.Centre)).ToList();

    /// <inheritdoc/>
    public float DamageBot(int id, float damage, string cause, bool melee)
    {
        if (!_bots.TryGetValue(id, out var bot) || bot.Dead)
            return 0f;
        if ((melee && bot.Type.MeleeImmune) || !(damage > 0) || !float.IsFinite(damage))
            return bot.Health;
        bot.Health = Math.Max(0, bot.Health - damage);
        if (bot.Health <= 0)
        {
            Kill(bot, cause);
            return 0f;
        }
        // Being hurt engages it; one Changed carries both the new health and the engagement.
        bot.Engaged = true;
        bot.EngagedUntil = survivalWorld.Now + settings.EngagedSeconds;
        Changed?.Invoke(bot);
        return bot.Health;
    }

    private void Kill(BotState bot, string cause)
    {
        if (bot.Dead)
            return;
        bot.Health = 0;
        bot.Dead = true;
        bot.Engaged = false;
        bot.DiedAt = survivalWorld.Now;
        bot.Desired = Vector3.Zero;
        var random = new Random(unchecked(Seed * 31 + bot.Id * 7919));
        var items = bot.Type.Drops
            .Where(d => random.NextSingle() < d.Chance)
            .Select(d => new ItemStack(d.Item, d.Count))
            .ToList();
        survival.AddDrop(bot.Centre, items);
        Changed?.Invoke(bot);
    }

    // ---- Queries for scripts and raids ----

    /// <summary>
    /// Where a target is (the middle of a player, bot or part) and how big it is (its radius, m); null when
    /// it is gone. <paramref name="target"/> names it as <c>BotAttack</c> would; its bot and attack are ignored.
    /// </summary>
    public Vector3? Locate(BotAttackArgs target, out float radius) => TargetPoint(target, out radius);

    /// <summary>How far a target is from the edge of <paramref name="bot"/>'s body to its own edge, in m; null when it is gone.</summary>
    public float? Reach(BotState bot, BotAttackArgs target) =>
        TargetPoint(target, out var radius) is { } point ? Vector3.Distance(bot.Centre, point) - bot.Type.Radius - radius : null;

    /// <summary>Every living player with the middle of their body.</summary>
    public IEnumerable<(int Peer, Vector3 Centre)> LivingPlayers() =>
        survival.Players.Select(p => (Peer: p, Centre: survival.PlayerCentre(p))).Where(p => p.Centre is not null).Select(p => (p.Peer, p.Centre!.Value)).ToList();

    /// <summary>Every planted crop, with where it is. Worked out once per host tick unless soil changes.</summary>
    public IReadOnlyList<CropTarget> Crops()
    {
        // What is planted where, cheaply: changes when soil is planted, cleared or comes and goes.
        var key = 17L;
        foreach (var creation in survivalWorld.AllCreations)
        {
            foreach (var (part, soil) in creation.ActiveSoil)
                key = unchecked(key * 31 + creation.Id * 7919 + part * 131 + soil.Revision);
        }
        if (_crops is { } cached && cached.Now == survivalWorld.Now && cached.Key == key)
            return cached.Crops;
        var list = new List<CropTarget>();
        foreach (var id in survivalWorld.AllCreations.Where(c => c.ActiveSoil.Any(s => s.Soil.Seed is not null)).Select(c => c.Id).ToList())
        {
            if (survivalWorld.FindCreation(id) is not { } creation)
                continue;
            foreach (var (part, soil) in creation.ActiveSoil)
            {
                if (soil.Seed is null || creation.FindPart(part) is null)
                    continue;
                list.Add(new CropTarget(creation.Id, part, PartCentre(creation, part), SeedOf(soil.Seed)?.Value ?? 1f, soil.Ripe));
            }
        }
        _crops = (survivalWorld.Now, key, list);
        return list;
    }

    private SeedDefinition? SeedOf(string seed) => survival.Catalog.FindDefinition(seed)?.Seed;

    /// <summary>
    /// World objects a bot can break (those with health that bots can damage) within <paramref name="range"/> of
    /// <paramref name="point"/>, measured to their edge, nearest first.
    /// </summary>
    public IReadOnlyList<(string Id, Vector3 Centre, float Distance)> ObjectsNear(Vector3 point, float range) =>
        [.. survivalWorld.ObjectsNear(point, range + MaxObjectRadius)
            .Where(o => o.Definition.Health is not null && o.Definition.TakesDamageFrom(DamageKinds.Bot))
            .Select(o => (Id: o.Id.ToString(), Centre: ObjectCentre(o), Distance: Math.Max(0, Vector3.Distance(ObjectCentre(o), point) - ObjectRadius(o))))
            .Where(o => o.Distance <= range)
            .OrderBy(o => o.Distance).ThenBy(o => o.Id, StringComparer.Ordinal)];

    /// <summary>
    /// Parts spinning on a joint near <paramref name="point"/>: the far side of every unlocked rotational
    /// joint (a bearing's rotor, a wheel, a saw blade) turning faster than <paramref name="minSpeed"/> rad/s
    /// against its base, within <paramref name="range"/>. Nearest first. With <paramref name="partType"/> (part types,
    /// qualified or not, separated by commas: a saw blade, a drill), only parts of those types on the turning side count,
    /// each at its own centre.
    /// </summary>
    public IReadOnlyList<(int Creation, int Part, Vector3 Centre, float Speed, float Distance)> SpinningNear(Vector3 point, float range, float minSpeed,
        string? partType = null)
    {
        var only = partType?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => Identifiers.Qualify(t)).ToHashSet(StringComparer.Ordinal);
        if (only is { Count: 0 })
            only = null;
        var list = new List<(int Creation, int Part, Vector3 Centre, float Speed, float Distance)>();
        foreach (var id in survivalWorld.AllCreations.Where(c => c.Joints.Any(j => !j.IsLocked)).Select(c => c.Id).ToList())
        {
            // Finding it again brings its Bodies' poses and velocities up to date.
            if (survivalWorld.FindCreation(id) is not { } creation)
                continue;
            foreach (var joint in creation.Joints.Where(j => !j.IsLocked))
            {
                var part = joint.Connection.B;
                if (creation.FindPart(part) is null || !creation.TryGetJointFrame(joint.Connection, out var frame) || frame.Type != ConnectionType.Rotational)
                    continue;
                if (only is not null)
                {
                    // The nearest part of those types riding the turning Body.
                    var match = creation.Parts.Where(p => only.Contains(Identifiers.Qualify(p.Part)) && creation.BodyIndexOf(p.Id) == joint.BodyB)
                        .Select(p => p.Id).OrderBy(p => Vector3.Distance(PartCentre(creation, p), point)).ThenBy(p => p).Cast<int?>().FirstOrDefault();
                    if (match is not { } found)
                        continue;
                    part = found;
                }
                var centre = PartCentre(creation, part);
                var distance = Vector3.Distance(centre, point);
                if (distance > range)
                    continue;
                var axis = Vector3.Transform(frame.Axis.ToNumerics(), creation.BodyPose(joint.BodyA).Rotation);
                var speed = MathF.Abs(Vector3.Dot(creation.GetBodyVelocity(joint.BodyB).Angular - creation.GetBodyVelocity(joint.BodyA).Angular, axis));
                if (speed >= minSpeed)
                    list.Add((creation.Id, part, centre, speed, distance));
            }
        }
        return [.. list.OrderBy(s => s.Distance).ThenBy(s => s.Creation).ThenBy(s => s.Part)];
    }

    // Objects are found by their position; this covers the widest one's reach from it.
    private const float MaxObjectRadius = 4f;

    private static Vector3 ObjectCentre(WorldObjectPlacement placement) =>
        placement.Position + new Vector3(0, (placement.Definition.Collider?.At.Y ?? 0) * placement.Scale, 0);

    // A world object's horizontal size: its collider's radius, or half its widest side.
    private static float ObjectRadius(WorldObjectPlacement placement)
    {
        var collider = placement.Definition.Collider;
        var radius = collider?.Radius ?? (collider?.Size is { } size ? Math.Max(size.X, size.Z) / 2 : 0.5f);
        return Math.Min(radius * placement.Scale, MaxObjectRadius);
    }

    // ---- Ticking ----

    /// <summary>Deals lasting damage and clears away dead bots' bodies. The host calls it every physics tick.</summary>
    public void Tick(float seconds)
    {
        var now = survivalWorld.Now;
        for (var i = _lasting.Count - 1; i >= 0; i--)
        {
            var (peer, perSecond, until) = _lasting[i];
            // Only the part of this tick before the effect wore off counts.
            var share = (float)Math.Min(seconds, until - (now - seconds));
            if (share > 0 && !survival.IsDead(peer))
                survival.Damage(peer, perSecond * share, "bot");
            if (now >= until || survival.IsDead(peer))
                _lasting.RemoveAt(i);
        }
        foreach (var gone in _bots.Values.Where(b => b.Dead && now - b.DiedAt >= settings.CorpseSeconds).Select(b => b.Id).ToList())
            Remove(gone);
        foreach (var calm in _bots.Values.Where(b => b.Engaged && now >= b.EngagedUntil).ToList())
        {
            calm.Engaged = false;
            Changed?.Invoke(calm);
        }
    }

    // ---- Shared ----

    /// <summary>Yaw in degrees for facing along <paramref name="direction"/> (0 faces -Z).</summary>
    public static float YawOf(Vector3 direction) => MathF.Atan2(-direction.X, -direction.Z) * 180f / MathF.PI;

    internal static Vector3 PartCentre(Creation creation, int partId)
    {
        var part = creation.FindPart(partId)!;
        var pose = creation.BodyPose(creation.BodyIndexOf(partId));
        return pose.Apply(PartGeometry.CentreMetres(creation.DefinitionOf(partId), part.Cell, part.Rotation).ToNumerics());
    }
}
