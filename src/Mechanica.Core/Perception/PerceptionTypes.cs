using System.Numerics;
using Mechanica.Core.Building;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;

namespace Mechanica.Core.Perception;

/// <summary>Arguments of <c>GetPlayer</c>.</summary>
/// <param name="Peer">The player's peer; null is the caller's own player.</param>
public sealed record GetPlayerArgs(int? Peer = null);

/// <summary>A seat a player sits in.</summary>
/// <param name="Creation">Creation holding the seat.</param>
/// <param name="Part">The seat part.</param>
public sealed record SeatView(int Creation, int Part);

/// <summary>
/// One player as <c>GetPlayers</c> and <c>GetPlayer</c> return it. The game builds it from the host's view of
/// the player's body, seat, held tool, Lift and health.
/// </summary>
/// <param name="Peer">The player's peer id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Character">Qualified id of the character the player is shown as, or null when no character is loaded.</param>
/// <param name="Position">World position of the feet, in m.</param>
/// <param name="Eye">World position of the eyes (where Use and tools aim from), in m.</param>
/// <param name="Look">Unit direction the player looks in.</param>
/// <param name="Velocity">m/s.</param>
/// <param name="OnFloor">Standing on something.</param>
/// <param name="Swimming">In deep water.</param>
/// <param name="Crouched">Crouching.</param>
/// <param name="Seat">The seat the player sits in, or null.</param>
/// <param name="Held">The tool or weapon shown in the player's hands, or null for empty hands.</param>
/// <param name="HeldItem">Qualified id of the scripted item the player holds (<c>EquipItem</c>), or null.</param>
/// <param name="Lift">The player's Lift, or null when they have not placed one.</param>
/// <param name="Health">Health now, or null outside Survival.</param>
/// <param name="MaxHealth">Full health, or null outside Survival.</param>
/// <param name="Dead">True while dead.</param>
public sealed record PlayerView(
    int Peer, string Name, string? Character, Vec3 Position, Vec3 Eye, Vec3 Look, Vec3 Velocity,
    bool OnFloor, bool Swimming, bool Crouched, SeatView? Seat, CharacterProp? Held, string? HeldItem, LiftState? Lift,
    float? Health, float? MaxHealth, bool Dead)
{
    /// <summary>
    /// Heading in radians: 0 looks along −z, and it grows turning left (counter-clockwise seen from above), like the
    /// player's own yaw.
    /// </summary>
    public float Yaw => MathF.Atan2(-Look.X, -Look.Z);

    /// <summary>Radians above the horizon (negative looks down).</summary>
    public float Pitch => MathF.Asin(Math.Clamp(Look.Y, -1f, 1f));
}

/// <summary>Arguments of <c>Raycast</c>. With no arguments it is "what am I looking at".</summary>
/// <param name="From">World start point, in m; null is the caller's eyes.</param>
/// <param name="Direction">Direction (any length but 0); null is where the caller looks.</param>
/// <param name="Range">How far to look, in m, up to <see cref="PerceptionActions.MaxRayRange"/>.</param>
/// <param name="IgnorePlayer">
/// A player whose body the ray passes through; null is the caller's own when the ray starts at their eyes, else none.
/// </param>
public sealed record RaycastArgs(Vec3? From = null, Vec3? Direction = null, float Range = PerceptionActions.DefaultRayRange, int? IgnorePlayer = null);

/// <summary>Value of <c>Raycast</c>.</summary>
/// <param name="Hit">False when the ray hit nothing within range (every other member is then null).</param>
/// <param name="Kind"><c>player</c>, <c>part</c>, <c>object</c>, <c>bot</c> or <c>ground</c> (terrain and POI structures).</param>
/// <param name="Point">Where, in world metres.</param>
/// <param name="Distance">Metres from the start.</param>
/// <param name="Player">Peer of the player hit.</param>
/// <param name="Creation">Creation hit.</param>
/// <param name="Part">Part hit in it.</param>
/// <param name="PartType">Qualified definition id of that part.</param>
/// <param name="Object">World object hit (an id like <c>3.10.-4</c>, as <c>Sledge</c> and <c>StartRefine</c> take it).</param>
/// <param name="ObjectType">That object's type (<c>tree</c>, <c>rock_node</c>, ...).</param>
/// <param name="Bot">Bot hit.</param>
public sealed record RaycastResult(
    bool Hit, string? Kind = null, Vec3? Point = null, float? Distance = null, int? Player = null, int? Creation = null,
    int? Part = null, string? PartType = null, string? Object = null, string? ObjectType = null, int? Bot = null);

/// <summary>
/// Arguments of <c>SampleTerrain</c>: either <paramref name="Points"/>, or a grid from <paramref name="Min"/> to
/// <paramref name="Max"/> every <paramref name="Step"/> metres. At most <see cref="PerceptionActions.MaxSamples"/> samples.
/// </summary>
/// <param name="Points">World <c>[x, z]</c> points.</param>
/// <param name="Min">Grid corner <c>[x, z]</c>.</param>
/// <param name="Max">Opposite grid corner <c>[x, z]</c>.</param>
/// <param name="Step">Grid spacing in m.</param>
public sealed record SampleTerrainArgs(IReadOnlyList<float[]>? Points = null, float[]? Min = null, float[]? Max = null, float Step = 1f);

/// <summary>The ground at one point.</summary>
/// <param name="X">World x.</param>
/// <param name="Z">World z.</param>
/// <param name="Height">Ground height, in m.</param>
/// <param name="Biome">Biome id.</param>
/// <param name="Slope">Steepness, as rise over run.</param>
/// <param name="WaterDepth">Metres of water above the ground (0 on dry land).</param>
/// <param name="Poi">Qualified id of the POI whose footprint holds the point, or null.</param>
public sealed record TerrainSample(float X, float Z, float Height, string Biome, float Slope, float WaterDepth, string? Poi = null);

/// <summary>Value of <c>SampleTerrain</c>.</summary>
/// <param name="SeaLevel">Water surface height, in m.</param>
/// <param name="Samples">One per point, in order (grids row by row, x fastest).</param>
public sealed record TerrainSamples(float SeaLevel, IReadOnlyList<TerrainSample> Samples);

/// <summary>Arguments of <c>GetObjects</c>.</summary>
/// <param name="Center">World point to search around; null is the caller's feet.</param>
/// <param name="Radius">Search radius in m, up to <see cref="PerceptionActions.MaxObjectRadius"/>.</param>
/// <param name="Type">Only objects of this type (<c>tree</c>, <c>rock_node</c>, ...).</param>
/// <param name="Limit">Most objects returned, nearest first.</param>
public sealed record GetObjectsArgs(Vec3? Center = null, float Radius = 32f, string? Type = null, int Limit = 100);

/// <summary>A world object (tree, rock, boulder) that still stands.</summary>
/// <param name="Id">Its id, as <c>Sledge</c>, <c>StartRefine</c> and <c>FinishRefine</c> take it.</param>
/// <param name="Type">Object type id.</param>
/// <param name="Position">World position of its ground point, in m.</param>
/// <param name="Yaw">Turn about +y in radians.</param>
/// <param name="Scale">Uniform scale.</param>
/// <param name="Distance">Metres from the search centre.</param>
/// <param name="Health">Health left, or null when it can't be broken by hits.</param>
/// <param name="MaxHealth">Full health, or null when it can't be broken by hits.</param>
/// <param name="Refine">True for a resource node that holding Use refines (<c>StartRefine</c>).</param>
/// <param name="Drops">What it leaves when broken or refined.</param>
public sealed record WorldObjectView(
    string Id, string Type, Vec3 Position, float Yaw, float Scale, float Distance, float? Health, float? MaxHealth, bool Refine,
    IReadOnlyList<ItemAmount> Drops);

/// <summary>Arguments of <c>GetDrops</c>.</summary>
/// <param name="Center">World point to search around; null is the caller's feet (or every pile with no radius).</param>
/// <param name="Radius">Search radius in m; null is every pile.</param>
public sealed record GetDropsArgs(Vec3? Center = null, float? Radius = null);

/// <summary>A pile of items on the ground.</summary>
/// <param name="Id">Pile id, as <c>PickUp</c> takes it.</param>
/// <param name="Position">World position, in m.</param>
/// <param name="Items">What it holds.</param>
/// <param name="Placed">True for items put down with Force Build; they never expire.</param>
/// <param name="ExpiresIn">Seconds until it is gone, or null when placed.</param>
/// <param name="Distance">Metres from the search centre, or null with no centre.</param>
public sealed record DropView(int Id, Vec3 Position, IReadOnlyList<ItemStack> Items, bool Placed, double? ExpiresIn, float? Distance);

/// <summary>Arguments of <c>GetPois</c>.</summary>
/// <param name="All">Also list POIs the story has not revealed. Players can't; scripts and agents can.</param>
public sealed record GetPoisArgs(bool All = false);

/// <summary>A named point of a POI (a quest marker, a fire spot, a loot spot).</summary>
/// <param name="Type">Marker kind.</param>
/// <param name="Ref">Catalog id it refers to, or null.</param>
/// <param name="Position">World position, in m.</param>
/// <param name="Yaw">Turn about +y in radians.</param>
public sealed record PoiMarkerView(string Type, string? Ref, Vec3 Position, float Yaw);

/// <summary>A placed POI.</summary>
/// <param name="Index">Its place among the world's POIs.</param>
/// <param name="Id">Qualified POI id (what quests and <c>Reveal</c> name).</param>
/// <param name="Name">Display name.</param>
/// <param name="Centre">World position of its centre on its pad, in m.</param>
/// <param name="Radius">Footprint radius, in m.</param>
/// <param name="Yaw">Turn about +y in radians.</param>
/// <param name="Revealed">True when the story has revealed it to the players.</param>
/// <param name="Markers">Its named points.</param>
public sealed record PoiView(int Index, string Id, string Name, Vec3 Centre, float Radius, float Yaw, bool Revealed, IReadOnlyList<PoiMarkerView> Markers);

/// <summary>A player in <c>GetWorld</c>.</summary>
/// <param name="Peer">Peer id (the host is 1).</param>
/// <param name="Name">Display name.</param>
public sealed record PeerView(int Peer, string Name);

/// <summary>Value of <c>GetWorld</c>.</summary>
/// <param name="Seed">World seed.</param>
/// <param name="SeedText">The seed as text, for callers whose numbers can't hold 64 bits exactly (JavaScript).</param>
/// <param name="Mode">Game mode.</param>
/// <param name="Difficulty">Difficulty.</param>
/// <param name="FriendlyFire">Whether players can hurt each other.</param>
/// <param name="Time">The host's clock, in s (what drop expiry and cooldowns count in).</param>
/// <param name="Tick">The host's physics tick.</param>
/// <param name="TickRate">Physics ticks per second.</param>
/// <param name="WorldSize">The world's east–west extent, in m, centred on the origin.</param>
/// <param name="WorldDepth">The world's north–south extent, in m, centred on the origin.</param>
/// <param name="SeaLevel">Water surface height, in m.</param>
/// <param name="Spawn">Where new players appear, or null.</param>
/// <param name="Players">Players in the session.</param>
/// <param name="Creations">How many Creations exist.</param>
/// <param name="Drops">How many item piles lie on the ground.</param>
/// <param name="Pois">How many POIs the world has.</param>
/// <param name="RevealedPois">Qualified ids of the POIs the story has revealed.</param>
public sealed record WorldView(
    long Seed, string SeedText, GameMode Mode, Difficulty Difficulty, bool FriendlyFire, double Time, long Tick, int TickRate, float WorldSize,
    float WorldDepth, float SeaLevel, Vec3? Spawn, IReadOnlyList<PeerView> Players, int Creations, int Drops, int Pois, IReadOnlyList<string> RevealedPois);

/// <summary>What the perception actions read from the world. The host's game layer implements it.</summary>
public interface IPerceptionWorld
{
    /// <summary>The host's clock, in seconds.</summary>
    double Now { get; }

    /// <summary>The host's physics tick.</summary>
    long Tick { get; }

    /// <summary>Physics ticks per second.</summary>
    int TickRate { get; }

    /// <summary>The world's game mode.</summary>
    GameMode Mode { get; }

    /// <summary>The world's difficulty.</summary>
    Difficulty Difficulty { get; }

    /// <summary>Whether players can hurt each other.</summary>
    bool FriendlyFire { get; }

    /// <summary>The world generator, or null before the world is made.</summary>
    WorldGenerator? Generator { get; }

    /// <summary>Destroyed and damaged world objects, or null before the world is made.</summary>
    WorldState? State { get; }

    /// <summary>Every player with a body, by peer.</summary>
    IReadOnlyList<PlayerView> Players { get; }

    /// <summary>Item piles on the ground, by id.</summary>
    IReadOnlyDictionary<int, ItemDrop> Drops { get; }

    /// <summary>Qualified ids of the POIs the story has revealed.</summary>
    IReadOnlyCollection<string> RevealedPois { get; }

    /// <summary>How many Creations exist.</summary>
    int CreationCount { get; }

    /// <summary>The Creation with this id, or null.</summary>
    Creation? FindCreation(int id);

    /// <summary>The first thing a ray hits within <paramref name="range"/> metres, passing through <paramref name="ignorePlayer"/> (0: none).</summary>
    RayHit? Cast(Vector3 from, Vector3 direction, float range, int ignorePlayer);
}
