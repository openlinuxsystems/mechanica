using System.Numerics;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.World;

namespace Mechanica.Core.Survival;

/// <summary>Arguments of <c>Eat</c>: eat one of the food in a slot of the caller's inventory.</summary>
/// <param name="Slot">The slot.</param>
public sealed record EatArgs(int Slot);

/// <summary>Arguments of <c>Fire</c>: shoot (or throw) the weapon in a slot of the caller's inventory.</summary>
/// <param name="Slot">The slot holding the weapon.</param>
/// <param name="Direction">Where the shot goes from the shooter's eyes; the host checks it against where they look.</param>
public sealed record FireArgs(int Slot, Vec3 Direction);

/// <summary>Arguments of the soil actions (<c>Plant</c>, <c>Water</c>, <c>Harvest</c>) and <c>SetSpawn</c>.</summary>
/// <param name="Creation">Creation holding the part.</param>
/// <param name="Part">The soil (or bed) part.</param>
/// <param name="Slot">The caller's slot holding the seed or water; <c>Harvest</c> and <c>SetSpawn</c> ignore it.</param>
public sealed record PartUseArgs(int Creation, int Part, int Slot = 0);

/// <summary>Arguments of <c>CollectWater</c>: fill a bucket where the caller aims at water.</summary>
/// <param name="Point">The water point aimed at, in world metres.</param>
public sealed record CollectWaterArgs(Vec3 Point);

/// <summary>Arguments of <c>DropItems</c>: put items from a slot on the ground in front of the caller.</summary>
/// <param name="Slot">The slot.</param>
/// <param name="Count">How many; null drops the whole stack.</param>
public sealed record DropItemsArgs(int Slot, int? Count = null);

/// <summary>Arguments of <c>PickUp</c>: take a pile of items off the ground.</summary>
/// <param name="Drop">The pile's id.</param>
public sealed record PickUpArgs(int Drop);

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
public sealed record VitalsView(int Peer, float Health, float MaxHealth, bool Dead, float RespawnIn, bool HasBed, GameMode Mode);

/// <summary>What grows on a soil part, as <c>GetSoil</c> returns it and clients are sent it.</summary>
/// <param name="Creation">Creation holding the part.</param>
/// <param name="Part">The soil part.</param>
/// <param name="Seed">Qualified id of the planted seed, or null (left out of the JSON, so it defaults).</param>
/// <param name="Crop">Qualified id of the item it yields, or null.</param>
/// <param name="Stage">Growth stage, 0 to <see cref="SoilState.Stages"/> (ripe).</param>
/// <param name="Water">Seconds of water left.</param>
/// <param name="Capacity">Most seconds of water the soil holds.</param>
public sealed record SoilView(int Creation, int Part, string? Seed = null, string? Crop = null, int Stage = 0, float Water = 0, float Capacity = 0)
{
    /// <summary>True when the crop can be harvested.</summary>
    public bool Ripe => Seed is not null && Stage >= SoilState.Stages;
}

/// <summary>A pile of items lying on the ground. The host owns every pile; clients are sent them.</summary>
/// <param name="Id">The pile's id.</param>
/// <param name="Position">World position, in metres, on the ground.</param>
/// <param name="Items">What it holds (qualified ids).</param>
/// <param name="Expires">Host clock time at which it is gone, in seconds.</param>
public sealed record ItemDrop(int Id, Vec3 Position, IReadOnlyList<ItemStack> Items, double Expires);

/// <summary>Value of a successful <c>Eat</c>.</summary>
/// <param name="Health">Health after eating.</param>
public sealed record EatResult(float Health);

/// <summary>One thing a shot hit.</summary>
/// <param name="Kind"><c>player</c>, <c>part</c>, <c>object</c>, <c>bot</c>, <c>crop</c> (a bot destroyed it) or <c>ground</c>.</param>
/// <param name="Point">Where, in world metres.</param>
/// <param name="Damage">Damage dealt.</param>
/// <param name="Player">The player hit.</param>
/// <param name="Creation">The Creation hit.</param>
/// <param name="Part">The part hit.</param>
/// <param name="Object">The world object hit.</param>
/// <param name="Bot">The bot hit.</param>
public sealed record ShotHitView(string Kind, Vec3 Point, float Damage, int? Player = null, int? Creation = null, int? Part = null, string? Object = null, int? Bot = null);

/// <summary>Value of a successful <c>Fire</c>.</summary>
/// <param name="Hits">What the pellets and the splash hit.</param>
public sealed record FireResult(IReadOnlyList<ShotHitView> Hits);

/// <summary>Value of a successful <c>Harvest</c>.</summary>
/// <param name="Items">What went into the caller's inventory.</param>
public sealed record HarvestResult(IReadOnlyList<ItemAmount> Items);

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

    /// <summary>The first thing a ray hits within <paramref name="range"/> metres, ignoring <paramref name="ignorePlayer"/>; null for nothing.</summary>
    RayHit? Cast(Vector3 from, Vector3 direction, float range, int ignorePlayer);

    /// <summary>The ground point under <paramref name="point"/> (where an item put there comes to rest).</summary>
    Vector3 GroundBelow(Vector3 point);

    /// <summary>True when <paramref name="point"/> is in water.</summary>
    bool IsWater(Vector3 point);

    /// <summary>A world object that still stands, or null.</summary>
    WorldObjectPlacement? FindObject(WorldObjectId id);

    /// <summary>World objects that still stand within <paramref name="radius"/> of <paramref name="point"/>.</summary>
    IEnumerable<WorldObjectPlacement> ObjectsNear(Vector3 point, float radius);

    /// <summary>Adds damage to a world object and returns its total damage.</summary>
    float AddObjectDamage(WorldObjectId id, float damage);

    /// <summary>Destroys a world object for every peer.</summary>
    void DestroyObject(WorldObjectId id);

    /// <summary>Takes health off a part; at 0 it breaks off (<see cref="ToolActions.DamagePart"/>).</summary>
    SledgeResult DamagePart(Creation creation, int partId, float damage);

    /// <summary>Gives a Body an impulse (N·s) at a world point.</summary>
    void Push(Creation creation, int bodyIndex, Vector3 point, Vector3 impulse);
}
