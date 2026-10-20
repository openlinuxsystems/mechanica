using System.Numerics;
using Mechanica.Core.Building;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.World;

namespace Mechanica.Core.Survival;

/// <summary>Arguments of <c>SetSpawn</c>: one part of a Creation.</summary>
/// <param name="Creation">Creation holding the part.</param>
/// <param name="Part">The bed part.</param>
/// <param name="Slot">Unused; kept so older callers that send it still parse.</param>
public sealed record PartUseArgs(int Creation, int Part, int Slot = 0);

/// <summary>Arguments of <c>CollectWater</c>: fill every empty bucket the caller carries where they aim at water.</summary>
/// <param name="Point">The water point aimed at, in world metres.</param>
/// <param name="Slot">The inventory slot holding the empty bucket in hand, filled first; null starts at the first one.</param>
public sealed record CollectWaterArgs(Vec3 Point, int? Slot = null);

/// <summary>Result of <c>CollectWater</c>.</summary>
/// <param name="Slot">The slot the full bucket in hand is in.</param>
public sealed record CollectWaterResult(int Slot);

/// <summary>Arguments of <c>DropItems</c>: put items from a slot on the ground in front of the caller.</summary>
/// <param name="Slot">The slot.</param>
/// <param name="Count">How many; null drops the whole stack.</param>
/// <param name="At">
/// Force Build (UI spec §1, §9): the world point the caller aims at, within reach, where the items are placed and stay
/// (they never expire). Null drops them in front of the caller.
/// </param>
public sealed record DropItemsArgs(int Slot, int? Count = null, Vec3? At = null);

/// <summary>Arguments of <c>PickUp</c>: take a pile of items off the ground.</summary>
/// <param name="Drop">The pile's id.</param>
public sealed record PickUpArgs(int Drop);

/// <summary>
/// Arguments of <c>StartRefine</c> and <c>FinishRefine</c>: what Use is held on. Either a resource node (a tree) by
/// <paramref name="Object"/>, or a loose part that refines (a Stone Rod) by <paramref name="Creation"/> and <paramref name="Part"/>.
/// </summary>
/// <param name="Object">Id of the world object (<c>"3.10.-4"</c>).</param>
/// <param name="Creation">Id of the loose part's Creation.</param>
/// <param name="Part">Id of the loose part in it.</param>
public sealed record RefineArgs(string? Object = null, int? Creation = null, int? Part = null)
{
    /// <summary>What is refined, as one key: the object id, or <c>part:creation.part</c>.</summary>
    public string Target => Object ?? $"part:{Creation}.{Part}";
}

/// <summary>Arguments of <c>Harvest</c>: gather what a world object gives by hand (an oil geyser's crude oil).</summary>
/// <param name="Object">Id of the world object (<c>"poi.7.2"</c>).</param>
public sealed record HarvestArgs(string Object);

/// <summary>Value of a successful <c>Harvest</c>.</summary>
/// <param name="Taken">Items that went into the inventory.</param>
/// <param name="Left">Items that did not fit and were left on the ground where the object stood.</param>
public sealed record HarvestResult(int Taken, int Left);

/// <summary>Value of a successful <c>FinishRefine</c>.</summary>
/// <param name="Taken">Items that went into the inventory.</param>
/// <param name="Left">Items that did not fit and were left on the ground where the node stood.</param>
public sealed record RefineResult(int Taken, int Left);

/// <summary>Arguments of <c>GetSoil</c>.</summary>
/// <param name="Creation">Creation holding the soil part.</param>
/// <param name="Part">The soil part.</param>
public sealed record GetSoilArgs(int Creation, int Part);

/// <summary>A player's health as <c>GetVitals</c> returns it and clients are sent it.</summary>
/// <param name="Peer">The player.</param>
/// <param name="Health">Health now.</param>
/// <param name="MaxHealth">Full health.</param>
/// <param name="Dead">True while dead.</param>
/// <param name="RespawnIn">Seconds until a dead player may respawn (0 when they may now).</param>
/// <param name="HasBed">True when the player has set a bed to respawn at.</param>
/// <param name="Mode">The world's game mode.</param>
/// <param name="Slow">What the player's movement speed is multiplied by now (1 when not slowed).</param>
/// <param name="SlowFor">Seconds the slow lasts from now (0 when not slowed).</param>
/// <param name="Difficulty">The world's difficulty, so every player can see it (Options, Gameplay).</param>
/// <param name="FriendlyFire">Whether players can hurt each other in this world, so every player can see it.</param>
public sealed record VitalsView(int Peer, float Health, float MaxHealth, bool Dead, float RespawnIn, bool HasBed, GameMode Mode,
    float Slow = 1f, float SlowFor = 0f, Difficulty Difficulty = Difficulty.Normal, bool FriendlyFire = false);

/// <summary>Arguments of <c>SetFriendlyFire</c>: let players hurt each other in this world, or stop them. The host only.</summary>
/// <param name="On">True to let players hurt each other.</param>
public sealed record SetFriendlyFireArgs(bool On);

/// <summary>What grows on a soil part, as <c>GetSoil</c> returns it and clients are sent it.</summary>
/// <param name="Creation">Creation holding the part.</param>
/// <param name="Part">The soil part.</param>
/// <param name="Seed">Qualified id of the planted seed, or null (left out of the JSON, so it defaults).</param>
/// <param name="Crop">Qualified id of the item it yields, or null.</param>
/// <param name="Stage">Growth stage, 0 to <see cref="SoilState.Stages"/> (ripe), as the soil's script last set it.</param>
/// <param name="Water">Seconds of water left.</param>
/// <param name="Capacity">Most seconds of water the soil holds.</param>
/// <param name="Wetness">How wet the soil looks: dry, watered, wet or drying.</param>
public sealed record SoilView(int Creation, int Part, string? Seed = null, string? Crop = null, int Stage = 0, float Water = 0, float Capacity = 0,
    SoilWetness Wetness = SoilWetness.Dry)
{
    /// <summary>True when the crop can be harvested.</summary>
    public bool Ripe => Seed is not null && Stage >= SoilState.Stages;
}

/// <summary>A pile of items lying on the ground. The host owns every pile; clients are sent them.</summary>
/// <param name="Id">The pile's id.</param>
/// <param name="Position">World position, in metres, on the ground.</param>
/// <param name="Items">What it holds (qualified ids).</param>
/// <param name="Expires">Host clock time at which it is gone, in seconds (ignored when <paramref name="Placed"/>).</param>
/// <param name="Placed">True for items put down with Force Build: they stay where they were placed and never expire.</param>
/// <param name="KnockedOut">True for what a knocked-out player dropped: the game shows it as the knockout bag.</param>
/// <param name="Owner">The peer whose knockout bag it is (their compass and maps point to it), or null.</param>
public sealed record ItemDrop(int Id, Vec3 Position, IReadOnlyList<ItemStack> Items, double Expires, bool Placed = false, bool KnockedOut = false, int? Owner = null);

/// <summary>One thing a shot, a splash or a bot's attack hit (<see cref="SurvivalActions.Hit"/>, <see cref="SurvivalActions.Splash"/>).</summary>
/// <param name="Kind"><c>player</c>, <c>part</c>, <c>object</c>, <c>bot</c>, <c>crop</c> (a bot destroyed it) or <c>ground</c>.</param>
/// <param name="Point">Where, in world metres.</param>
/// <param name="Damage">Damage dealt.</param>
/// <param name="Player">The player hit.</param>
/// <param name="Creation">The Creation hit.</param>
/// <param name="Part">The part hit.</param>
/// <param name="Object">The world object hit.</param>
/// <param name="Bot">The bot hit.</param>
public sealed record ShotHitView(string Kind, Vec3 Point, float Damage, int? Player = null, int? Creation = null, int? Part = null, string? Object = null, int? Bot = null);

/// <summary>A blow a player lived through: who, how hard it rocked them, and where it came from (null: nowhere in particular).</summary>
public sealed record PlayerHitView(int Peer, HitReaction Reaction, Vec3? From);

/// <summary>A projectile in flight, as clients are sent it to draw (<see cref="SurvivalActions.Launch"/>).</summary>
/// <param name="Id">Projectile id.</param>
/// <param name="Owner">Peer whose player launched it.</param>
/// <param name="From">Where it was launched, in world metres.</param>
/// <param name="Velocity">Launch velocity, in m/s.</param>
/// <param name="Gravity">Its constant acceleration, in m/s².</param>
/// <param name="Lifetime">Seconds it flies before it ends where it is.</param>
/// <param name="Age">Seconds it had flown when this view was made. Its position is From + Velocity·t + ½·Gravity·t².</param>
/// <param name="Look">How it is drawn: <see cref="ProjectileView.Water"/> for a splash of water, null for a plain ball.</param>
/// <param name="Size">How big its look is drawn, in metres: for water, the radius its splash spreads over. Null: the look's own size.</param>
public sealed record ProjectileView(int Id, int Owner, Vec3 From, Vec3 Velocity, Vec3 Gravity, float Lifetime, float Age, string? Look = null, float? Size = null)
{
    /// <summary>The <see cref="Look"/> of thrown water: a blob that bursts into a splash where it lands.</summary>
    public const string Water = "water";

    /// <summary>Every <see cref="Look"/> clients know how to draw.</summary>
    public static IReadOnlySet<string> Looks { get; } = new HashSet<string>(StringComparer.Ordinal) { Water };
}

/// <summary>Where a projectile's flight ended.</summary>
/// <param name="Id">Projectile id.</param>
/// <param name="Owner">Peer whose player launched it.</param>
/// <param name="Point">Where it ended, in world metres.</param>
/// <param name="Hit">What it hit, or null when its lifetime ran out.</param>
public sealed record ProjectileLanding(int Id, int Owner, Vector3 Point, RayHit? Hit);

/// <summary>Value of a successful <c>PickUp</c>.</summary>
/// <param name="Taken">How many items the caller took.</param>
/// <param name="Left">How many stayed on the ground (no room).</param>
public sealed record PickUpResult(int Taken, int Left);

/// <summary>Value of a successful <c>DropItems</c>.</summary>
/// <param name="Drop">The new pile's id.</param>
public sealed record DropItemsResult(int Drop);

/// <summary>What a ray cast into the world hit (the host's physics answers it).</summary>
/// <param name="Point">Hit point, in world metres.</param>
/// <param name="Player">Peer of the player hit.</param>
/// <param name="Creation">The Creation hit.</param>
/// <param name="Part">The part hit in it.</param>
/// <param name="Object">The world object hit.</param>
/// <param name="Bot">The bot hit.</param>
public sealed record RayHit(Vector3 Point, int? Player = null, int? Creation = null, int? Part = null, WorldObjectId? Object = null, int? Bot = null);

/// <summary>What the survival actions need from the world. The host's game layer implements it.</summary>
public interface ISurvivalWorld
{
    /// <summary>The host's clock, in seconds.</summary>
    double Now { get; }

    /// <summary>The Creation with this id, with its Body poses current, or null.</summary>
    Creation? FindCreation(int id);

    /// <summary>Every Creation in the world, live or parked. Their Body poses may be stale; <see cref="FindCreation"/> one to read them.</summary>
    IEnumerable<Creation> AllCreations { get; }

    /// <summary>World position of a peer's player's eyes, or null when it has no player.</summary>
    Vector3? EyeOf(int peer);

    /// <summary>World position of a peer's player's feet, or null when it has no player.</summary>
    Vector3? FeetOf(int peer);

    /// <summary>Unit direction a peer's player looks in, as the host last saw it, or null.</summary>
    Vector3? LookOf(int peer);

    /// <summary>Where a player stands when they respawn with no bed.</summary>
    Vector3 SpawnOf(int peer);

    /// <summary>Moves a player's feet to <paramref name="feet"/>, at rest.</summary>
    void Teleport(int peer, Vector3 feet);

    /// <summary>Adds <paramref name="velocity"/> (m/s) to a player's own movement once: a knockback.</summary>
    void Knock(int peer, Vector3 velocity)
    {
    }

    /// <summary>The first thing a ray hits within <paramref name="range"/> metres, ignoring <paramref name="ignorePlayer"/>; null for nothing.</summary>
    RayHit? Cast(Vector3 from, Vector3 direction, float range, int ignorePlayer);

    /// <summary>The ground point under <paramref name="point"/> (where an item put there comes to rest).</summary>
    Vector3 GroundBelow(Vector3 point);

    /// <summary>True when <paramref name="point"/> is in water.</summary>
    bool IsWater(Vector3 point);

    /// <summary>The POI pool of liquid (a chemical pool) <paramref name="point"/> is in, or null.</summary>
    PoiPoolPlacement? LiquidAt(Vector3 point) => null;

    /// <summary>A world object that still stands, or null.</summary>
    WorldObjectPlacement? FindObject(WorldObjectId id);

    /// <summary>A loot table of the world's generation rules (<c>loot_tables</c> in worldgen.json), or null.</summary>
    LootTableDefinition? FindLootTable(string id) => null;

    /// <summary>World objects that still stand within <paramref name="radius"/> of <paramref name="point"/>.</summary>
    IEnumerable<WorldObjectPlacement> ObjectsNear(Vector3 point, float radius);

    /// <summary>Adds damage to a world object and returns its total damage.</summary>
    float AddObjectDamage(WorldObjectId id, float damage);

    /// <summary>Destroys a world object for every peer.</summary>
    void DestroyObject(WorldObjectId id);

    /// <summary>
    /// Plays a sound event at a world point for everyone in earshot: a bare id is <paramref name="ownerModId"/>'s
    /// event, else vanilla's. Worlds without sound ignore it.
    /// </summary>
    void PlaySound(string sound, string ownerModId, Vector3 position)
    {
    }

    /// <summary>Takes health off a part; at 0 it breaks off (<see cref="ToolActions.DamagePart"/>).</summary>
    SledgeResult DamagePart(Creation creation, int partId, float damage);

    /// <summary>Gives a Body an impulse (N·s) at a world point.</summary>
    void Push(Creation creation, int bodyIndex, Vector3 point, Vector3 impulse);

    /// <summary>Takes a part out of the world for every peer (a refined rod), splitting or removing its Creation.</summary>
    void RemovePart(Creation creation, int partId);

    /// <summary>
    /// Something <paramref name="radius"/> m across at <paramref name="centre"/> fell apart: spawns its
    /// <paramref name="breaks"/> loose parts there, each its own Creation thrown a little apart
    /// (<see cref="Building.ToolActions.Shatter"/>). <paramref name="seed"/> picks the pieces.
    /// </summary>
    void Shatter(BreakDefinition breaks, Vector3 centre, float radius, uint seed)
    {
    }

    /// <summary>
    /// A tree was felled: spawns its <paramref name="breaks"/> loose parts (log segments) stacked upright from
    /// <paramref name="foot"/> up a trunk <paramref name="height"/> m tall, all tipping over along
    /// <paramref name="direction"/> at <paramref name="spin"/> rad/s (<see cref="Building.ToolActions.Fell"/>).
    /// </summary>
    void Fell(BreakDefinition breaks, Vector3 foot, float height, Vector3 direction, float spin, uint seed)
    {
    }

    /// <summary>
    /// Damage reached a world object at <paramref name="at"/> (cosmetic, for every peer: the tree shakes, chips and leaves
    /// fly). <paramref name="broke"/> is true for the blow that broke or felled it.
    /// </summary>
    void ObjectHit(WorldObjectId id, Vector3 at, bool broke)
    {
    }

    /// <summary>
    /// A cutting tool (a saw blade, a drill) bit into a part or a bot at <paramref name="at"/> (cosmetic, for every peer):
    /// chips fly off wood, sparks off metal. <paramref name="material"/> is what was cut, as a sound material
    /// (<c>wood</c>, <c>metal</c>, ...); a bot is <c>metal</c>.
    /// </summary>
    void Impact(Vector3 at, string material)
    {
    }
}
