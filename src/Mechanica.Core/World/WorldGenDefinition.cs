using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;

namespace Mechanica.Core.World;

/// <summary>
/// World generation rules, loaded from <c>world/worldgen.json</c> in a mod (the last mod in load order
/// that ships one wins). Every number here is tuning data in SI units; the defaults only fill keys the
/// file leaves out. POI layouts live in their own files (<see cref="PoiPrefab"/>).
/// </summary>
public sealed record WorldGenDefinition
{
    /// <summary>Shape of the ground.</summary>
    public TerrainDefinition Terrain { get; init; } = new();

    /// <summary>Biome that takes over along the world edge, where the land falls into the sea.</summary>
    public string? CoastBiome { get; init; }

    /// <summary>Biomes. Inland regions pick one by <see cref="BiomeDefinition.Weight"/>.</summary>
    public List<BiomeDefinition>? Biomes { get; init; }

    /// <summary>Destructible objects that biomes scatter on the ground (trees, rocks, ...).</summary>
    public List<WorldObjectDefinition>? Objects { get; init; }

    /// <summary>Ground textures (at most <see cref="GroundDefinition.MaxGrounds"/>) that biomes, slopes and POIs paint the terrain with.</summary>
    public List<GroundDefinition>? Grounds { get; init; }

    /// <summary>The ground that steep slopes take over with (rock), whatever the biome.</summary>
    public SlopeGroundDefinition? SlopeGround { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Height field settings.</summary>
public sealed record TerrainDefinition
{
    /// <summary>Metres between height samples. Must divide the chunk size.</summary>
    public float Resolution { get; init; } = 1f;

    /// <summary>Height of the sea surface in metres.</summary>
    public float SeaLevel { get; init; } = -4f;

    /// <summary>Height of the sea floor at the world edge.</summary>
    public float SeaFloor { get; init; } = -24f;

    /// <summary>Metres from the world edge over which the land falls to the sea floor (0: no sea).</summary>
    public float CoastWidth { get; init; } = 320f;

    /// <summary>Metres from the world edge within which the coast biome takes over.</summary>
    public float CoastBiomeWidth { get; init; } = 420f;

    /// <summary>How far inland, in metres, noise may pull the coastline, so the island is not a square.</summary>
    public float CoastWobble { get; init; } = 120f;

    /// <summary>Wavelength of the largest hills in metres.</summary>
    public float NoiseScale { get; init; } = 420f;

    /// <summary>Noise layers; each adds detail at <see cref="Lacunarity"/> times the frequency.</summary>
    public int Octaves { get; init; } = 5;

    /// <summary>Frequency step between octaves.</summary>
    public float Lacunarity { get; init; } = 2f;

    /// <summary>Amplitude step between octaves.</summary>
    public float Gain { get; init; } = 0.5f;

    /// <summary>Side of the square cells inland biomes are chosen in, in metres.</summary>
    public float BiomeCellSize { get; init; } = 760f;

    /// <summary>Metres over which neighbouring biomes blend. At most 0.18 × <see cref="BiomeCellSize"/>.</summary>
    public float BiomeBlend { get; init; } = 80f;

    /// <summary>How far, in metres, noise bends biome borders, so they are not straight lines.</summary>
    public float BiomeWarp { get; init; } = 160f;

    /// <summary>Steepest ground (rise over run) that scattered objects stand on.</summary>
    public float MaxScatterSlope { get; init; } = 0.7f;

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>One biome: how high and rough its ground is, its colour and what grows on it.</summary>
public sealed record BiomeDefinition
{
    /// <summary>Id, unique in the file (lowercase snake_case).</summary>
    public string? Id { get; init; }

    /// <summary>Display name.</summary>
    public string? Name { get; init; }

    /// <summary>Relative chance an inland region is this biome (0: only where placed explicitly, like the coast).</summary>
    public float Weight { get; init; } = 1f;

    /// <summary>Average ground height in metres.</summary>
    public float Height { get; init; }

    /// <summary>How far the ground rises and falls around <see cref="Height"/>, in metres.</summary>
    public float Relief { get; init; }

    /// <summary>Ground colour, <c>#rrggbb</c>: the map, and the ground itself where no <see cref="Ground"/> texture is set.</summary>
    public string? Color { get; init; }

    /// <summary>Id of the <see cref="GroundDefinition"/> this biome's ground is drawn with. Omitted: plain <see cref="Color"/>.</summary>
    public string? Ground { get; init; }

    /// <summary>Objects scattered on this biome's ground.</summary>
    public List<ScatterRule>? Scatter { get; init; }

    /// <summary>Decorative grass, ground cover and bushes growing on this biome's ground.</summary>
    public List<FloraRule>? Flora { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// Scatters one object kind over a biome: the ground is cut into <see cref="Spacing"/>-metre cells, each
/// cell gets one candidate spot, and the spot is used with probability <see cref="Chance"/> when it is in
/// this biome, above water, flat enough, outside every POI and clear of objects from earlier rules.
/// </summary>
public sealed record ScatterRule
{
    /// <summary>Id of a <see cref="WorldObjectDefinition"/>.</summary>
    public string? Object { get; init; }

    /// <summary>Cell side in metres.</summary>
    public float Spacing { get; init; }

    /// <summary>Probability (0..1) that a cell's spot holds an object.</summary>
    public float Chance { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A destructible object placed on the ground (D11): saw, drill and sledge targets. Until meshes are
/// authored it is drawn from primitive <see cref="Pieces"/>, or from a built-in flora <see cref="Model"/>.
/// </summary>
public sealed record WorldObjectDefinition
{
    /// <summary>Id from CATALOG_IDS.md (<c>tree</c>, <c>rock_node</c>, <c>boulder</c>, ...).</summary>
    public string? Id { get; init; }

    /// <summary>Collision shape, relative to the object's ground point.</summary>
    public ShapeDefinition? Collider { get; init; }

    /// <summary>Visual pieces, relative to the object's ground point.</summary>
    public List<ShapeDefinition>? Pieces { get; init; }

    /// <summary>
    /// A built-in flora model (<see cref="FloraModels"/>, such as <c>bush_berry</c>) drawn instead of <see cref="Pieces"/>,
    /// standing on the object's ground point.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// False for an object players, bots and Creations walk and drive through, like a bush: its collider only
    /// catches the sledge, so it can still be broken for its drops. True (the default) blocks movement.
    /// </summary>
    public bool Solid { get; init; } = true;

    /// <summary>Radius in metres that other scattered objects keep clear of.</summary>
    public float Footprint { get; init; } = 1f;

    /// <summary>Random uniform scale range <c>[min, max]</c>. Omitted means 1.</summary>
    public List<float>? Scale { get; init; }

    /// <summary>Metres the object sinks into the ground, so it sits on slopes.</summary>
    public float Sink { get; init; }

    /// <summary>
    /// Health of the object (chunk 20): sledge hits and weapons take it off, and at 0 the object breaks
    /// and leaves its <see cref="Drops"/>. Omitted means it cannot be broken that way.
    /// </summary>
    public float? Health { get; init; }

    /// <summary>
    /// Health one Sledgehammer hit takes from the object: its gathering damage (stone 25, quartz 20 in Chase's
    /// sledgehammer spec). Omitted means <c>sledge.object_damage</c> from <c>game/data/building.json</c>.
    /// </summary>
    public float? SledgeDamage { get; init; }

    /// <summary>Items the object leaves on the ground when it breaks.</summary>
    public List<ObjectDrop>? Drops { get; init; }

    /// <summary>
    /// True for a raw resource node (a tree, a rock node): holding Use (E) on it for
    /// <see cref="Survival.SurvivalSettings.RefineSeconds"/> refines it, and its <see cref="Drops"/> go straight into
    /// the player's inventory (UI spec §9, §10).
    /// </summary>
    public bool Refine { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Items a broken world object leaves behind.</summary>
public sealed record ObjectDrop
{
    /// <summary>Item id (<c>scrap_wood</c>, or <c>mod_id:item</c>).</summary>
    public string? Item { get; init; }

    /// <summary>How many (1 or more).</summary>
    public int Count { get; init; } = 1;

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Primitive used for placeholder visuals and colliders.</summary>
public enum PrimitiveShape
{
    /// <summary>Box of <see cref="ShapeDefinition.Size"/>.</summary>
    Box,

    /// <summary>Sphere of <see cref="ShapeDefinition.Radius"/>.</summary>
    Sphere,

    /// <summary>Upright cylinder of <see cref="ShapeDefinition.Radius"/> and <see cref="ShapeDefinition.Height"/>.</summary>
    Cylinder,
}

/// <summary>A primitive shape centred at <see cref="At"/>, turned <see cref="Yaw"/> degrees about +Y.</summary>
public sealed record ShapeDefinition
{
    /// <summary>Which primitive.</summary>
    public PrimitiveShape? Shape { get; init; }

    /// <summary>Box size in metres.</summary>
    public Vec3? Size { get; init; }

    /// <summary>Sphere or cylinder radius in metres.</summary>
    public float? Radius { get; init; }

    /// <summary>Cylinder height in metres.</summary>
    public float? Height { get; init; }

    /// <summary>Centre, relative to the owner's origin.</summary>
    public Vec3 At { get; init; }

    /// <summary>Turn about +Y in degrees (counter-clockwise seen from above).</summary>
    public float Yaw { get; init; }

    /// <summary>Colour, <c>#rrggbb</c>. Omitted means grey.</summary>
    public string? Color { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A ground texture the terrain is painted with (<c>grounds[]</c>): grass, forest floor, sand, rock, burnt earth... Biomes,
/// steep slopes and POIs pick grounds by id, and neighbouring grounds blend.
/// </summary>
public sealed record GroundDefinition
{
    /// <summary>Most grounds one world can use: the shader blends this many texture layers.</summary>
    public const int MaxGrounds = 8;

    /// <summary>Id, unique in the file (lowercase snake_case).</summary>
    public string? Id { get; init; }

    /// <summary>Tileable <c>.png</c>, relative to the mod root (<c>other_mod:path</c> for another mod's). Square; drawn at 512 px.</summary>
    public string? Texture { get; init; }

    /// <summary>Metres of ground one repeat of the texture covers.</summary>
    public float Tile { get; init; } = 4f;

    /// <summary>False for ground nothing grows on (burnt earth, concrete): flora skips where it covers most of the ground.</summary>
    public bool Flora { get; init; } = true;

    /// <summary>Tint, <c>#rrggbb</c>, that plants growing on or next to this ground take (charred grass by burnt earth).</summary>
    public string? FloraTint { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Steep ground's own texture (<c>slope_ground</c>): it fades in from <see cref="From"/> to <see cref="To"/>.</summary>
public sealed record SlopeGroundDefinition
{
    /// <summary>Id of the <see cref="GroundDefinition"/>.</summary>
    public string? Ground { get; init; }

    /// <summary>Slope (rise over run) where it starts to show.</summary>
    public float From { get; init; } = 0.7f;

    /// <summary>Slope where it covers the ground fully.</summary>
    public float To { get; init; } = 1.1f;

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A disc of ground a POI paints (<c>ground[]</c>), full inside <see cref="Radius"/> and fading out over <see cref="Blend"/>,
/// with a ragged edge <see cref="Noise"/> metres deep. Later paints go over earlier ones.
/// </summary>
public sealed record GroundPaint
{
    /// <summary>Id of the <see cref="GroundDefinition"/>.</summary>
    public string? Ground { get; init; }

    /// <summary>Centre <c>[x, z]</c>, relative to the POI. Omitted means the POI centre.</summary>
    public List<float>? At { get; init; }

    /// <summary>Full-strength radius in metres.</summary>
    public float Radius { get; init; }

    /// <summary>Metres over which it fades out.</summary>
    public float Blend { get; init; }

    /// <summary>How far, in metres, noise pushes the edge in and out.</summary>
    public float Noise { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A point of interest, loaded from <c>pois/*.json</c> in a mod (§21.3): where it may be placed, how it
/// shapes the ground, its static structure, and markers that later systems fill (loot, bots, fires, the
/// player spawn). Positions are relative to the POI centre on its flattened pad, before the POI's turn.
/// </summary>
public sealed record PoiPrefab
{
    /// <summary>Id, unique within the mod.</summary>
    public string? Id { get; init; }

    /// <summary>Display name.</summary>
    public string? Name { get; init; }

    /// <summary>Footprint radius in metres, including terrain blending. POIs never overlap, and nothing is scattered inside.</summary>
    public float Radius { get; init; }

    /// <summary>Where the POI goes.</summary>
    public PoiPlacement? Placement { get; init; }

    /// <summary>Ground shaping, applied in order.</summary>
    public List<TerrainOp>? Terrain { get; init; }

    /// <summary>Static structure pieces (placeholder geometry until blocks and blueprints exist).</summary>
    public List<ShapeDefinition>? Pieces { get; init; }

    /// <summary>Named points for later systems.</summary>
    public List<PoiMarker>? Markers { get; init; }

    /// <summary>Ground painted on the footprint, in order (<see cref="GroundPaint"/>).</summary>
    public List<GroundPaint>? Ground { get; init; }

    /// <summary>
    /// Id of a biome whose flora grows on the footprint, except on grounds without <see cref="GroundDefinition.Flora"/>.
    /// Omitted: nothing grows inside the POI.
    /// </summary>
    public string? Flora { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// Placement rules. Either <see cref="Fixed"/> (one POI at that spot) or <see cref="Spacing"/>: one
/// candidate per cell of a grid whose step is the middle of the range, jittered so neighbours stay within it.
/// </summary>
public sealed record PoiPlacement
{
    /// <summary>World <c>[x, z]</c> of a single fixed POI.</summary>
    public List<float>? Fixed { get; init; }

    /// <summary>Pad height of a fixed POI. Omitted means the natural ground height there.</summary>
    public float? PadHeight { get; init; }

    /// <summary>Turn of a fixed POI in degrees, a multiple of 90. Spaced POIs pick a random quarter turn.</summary>
    public float Yaw { get; init; }

    /// <summary>Distance range <c>[min, max]</c> in metres between neighbouring POIs of this kind.</summary>
    public List<float>? Spacing { get; init; }

    /// <summary>Biomes a spaced POI may stand in.</summary>
    public List<string>? Biomes { get; init; }

    /// <summary>Higher goes first and wins contested ground.</summary>
    public int Priority { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Kinds of <see cref="TerrainOp"/>.</summary>
public enum TerrainOpKind
{
    /// <summary>Levels a disc to the pad height.</summary>
    Flatten,

    /// <summary>Sinks a disc to <see cref="TerrainOp.Depth"/> below the pad (a pond when below sea level).</summary>
    Basin,
}

/// <summary>A disc of ground shaping, full strength inside <see cref="Radius"/> and fading out over <see cref="Blend"/>.</summary>
public sealed record TerrainOp
{
    /// <summary>What it does.</summary>
    public TerrainOpKind? Op { get; init; }

    /// <summary>Centre <c>[x, z]</c>, relative to the POI. Omitted means the POI centre.</summary>
    public List<float>? At { get; init; }

    /// <summary>Full-strength radius in metres.</summary>
    public float Radius { get; init; }

    /// <summary>Metres over which the effect fades out.</summary>
    public float Blend { get; init; }

    /// <summary>Basin depth below the pad, in metres.</summary>
    public float Depth { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A named point in a POI for a later system to fill: <c>spawn</c> (player start), <c>item</c>,
/// <c>bot</c>, <c>fire</c>, <c>door</c> and so on. <see cref="Ref"/> names the catalog id involved
/// (the item, the bot, or the key a door needs).
/// </summary>
public sealed record PoiMarker
{
    /// <summary>Marker kind (lowercase snake_case).</summary>
    public string? Type { get; init; }

    /// <summary>Catalog id the marker refers to, if any.</summary>
    public string? Ref { get; init; }

    /// <summary>Position relative to the POI centre on its pad.</summary>
    public Vec3 At { get; init; }

    /// <summary>Turn about +Y in degrees.</summary>
    public float Yaw { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
