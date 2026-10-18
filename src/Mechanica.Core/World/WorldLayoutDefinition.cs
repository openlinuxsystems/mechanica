using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.World;

/// <summary>
/// A macro region of the world map (World Map spec §2): the biome cells nearest its centre take its biome, so the
/// broad layout (forest in the north-west, desert in the south-west, ...) is the same in every world while borders,
/// patches and terrain differ by seed. Positions are fractions of the world: x from -0.5 (west edge) to 0.5 (east),
/// z from -0.5 (north edge) to 0.5 (south).
/// </summary>
public sealed record RegionDefinition
{
    /// <summary>Id of the region's biome.</summary>
    public string? Biome { get; init; }

    /// <summary>Centre <c>[x, z]</c> as fractions of the world (-0.5 to 0.5).</summary>
    public List<float>? At { get; init; }

    /// <summary>Relative reach: a region of size 2 claims cells twice as far from its centre as a region of size 1.</summary>
    public float Size { get; init; } = 1f;

    /// <summary>Other biomes mixed into the region's cells, each with the chance (0..1) a cell takes it; the rest is <see cref="Biome"/>.</summary>
    public List<RegionMix>? Mix { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A biome mixed into a <see cref="RegionDefinition"/>'s cells.</summary>
public sealed record RegionMix
{
    /// <summary>Biome id.</summary>
    public string? Biome { get; init; }

    /// <summary>Chance (0..1) that one of the region's cells takes this biome.</summary>
    public float Chance { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A river (World Map spec §2: rivers connect major lakes): a channel cut below sea level along a line through
/// <see cref="Points"/>, which the seed bends into meanders, with banks that rise gently into a valley so the land
/// beside it never ends in a cliff.
/// </summary>
public sealed record RiverDefinition
{
    /// <summary>Points <c>[x, z]</c> as fractions of the world (-0.5 to 0.5), from source to mouth.</summary>
    public List<List<float>>? Points { get; init; }

    /// <summary>Width of the water in metres at the source and at the mouth, <c>[source, mouth]</c>.</summary>
    public List<float>? Width { get; init; }

    /// <summary>Depth of the bed below sea level in metres.</summary>
    public float Depth { get; init; } = 4f;

    /// <summary>How far, in metres, the seed bends the river sideways from the straight line between points.</summary>
    public float Meander { get; init; } = 80f;

    /// <summary>Slope (rise over run) of the valley sides that climb from the banks to the surrounding ground.</summary>
    public float ValleySlope { get; init; } = 0.2f;

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// The road network (World Map spec §3): two-lane asphalt roads between the sites that ask for one, laid out as a
/// minimum spanning tree plus loops, routed around steep ground and deep water, with bridges over rivers and
/// junctions where roads meet; and dirt tracks from camps and small ruins to the nearest road.
/// </summary>
public sealed record RoadsDefinition
{
    /// <summary>Asphalt width in metres, edge line to edge line plus a little.</summary>
    public float Width { get; init; } = 10f;

    /// <summary>Width of the concrete curb on each side, in metres.</summary>
    public float Curb { get; init; } = 0.3f;

    /// <summary>Metres of levelled gravel beyond each curb before the ground blends back.</summary>
    public float Shoulder { get; init; } = 1.5f;

    /// <summary>Steepest grade (rise over run) a road climbs; 0.24 is about 13.5°.</summary>
    public float MaxGrade { get; init; } = 0.24f;

    /// <summary>Side of the routing grid's cells in metres.</summary>
    public float Cell { get; init; } = 32f;

    /// <summary>Metres between a bridge's deck and the water below it.</summary>
    public float BridgeClearance { get; init; } = 3f;

    /// <summary>How much more a metre of bridge costs the router than a metre of road on flat ground.</summary>
    public float BridgeCost { get; init; } = 6f;

    /// <summary>A loop road is added between two sites when the network makes the trip this many times longer than the straight line.</summary>
    public float LoopFactor { get; init; } = 1.7f;

    /// <summary>Only sites closer than this many metres are considered for a loop road.</summary>
    public float LoopReach { get; init; } = 2600f;

    /// <summary>Most loop roads added.</summary>
    public int MaxLoops { get; init; } = 14;

    /// <summary>Required connections, as pairs of site ids, routed before anything else in this order.</summary>
    public List<List<string>>? Links { get; init; }

    /// <summary>Ground painted on each shoulder (gravel), from <c>grounds[]</c>.</summary>
    public string? ShoulderGround { get; init; }

    /// <summary>Width of a dirt track in metres.</summary>
    public float TrackWidth { get; init; } = 4f;

    /// <summary>Steepest grade a dirt track climbs.</summary>
    public float TrackMaxGrade { get; init; } = 0.32f;

    /// <summary>
    /// A scattered POI that asks for a track gets one when a road is within this many metres. A site that asks for one always
    /// gets it, however far the road is.
    /// </summary>
    public float TrackReach { get; init; } = 650f;

    /// <summary>Ground a dirt track is painted with, from <c>grounds[]</c>.</summary>
    public string? TrackGround { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>What connects a site or POI to the road network.</summary>
public static class RoadLinks
{
    /// <summary>An asphalt road.</summary>
    public const string Road = "road";

    /// <summary>A dirt track to the nearest road, when one is near enough.</summary>
    public const string Track = "track";

    /// <summary>Nothing.</summary>
    public const string None = "none";

    /// <summary>Every value.</summary>
    public static readonly IReadOnlyList<string> All = [Road, Track, None];
}

/// <summary>
/// One of the world's mandatory structures (World Map spec §4): a POI prefab, or one drawn from a <see cref="PoolDefinition"/>,
/// at a fixed position that is the same in every world. Its id is the permanent reference later design work uses.
/// </summary>
public sealed record SiteDefinition
{
    /// <summary>Id, unique in the file (lowercase snake_case, such as <c>poi_36</c>).</summary>
    public string? Id { get; init; }

    /// <summary>Label from the design spec, such as <c>POI-36</c>.</summary>
    public string? Label { get; init; }

    /// <summary>POI prefab placed here (<c>poi_id</c>, or <c>mod_id:poi_id</c>). Give this or <see cref="Pool"/>.</summary>
    public string? Poi { get; init; }

    /// <summary>Pool the prefab is drawn from, per seed. Give this or <see cref="Poi"/>.</summary>
    public string? Pool { get; init; }

    /// <summary>Centre <c>[x, z]</c> as fractions of the world (-0.5 to 0.5; x east, z south).</summary>
    public List<float>? At { get; init; }

    /// <summary>How far, in metres, the seed may move the site from <see cref="At"/>. 0 (the default) keeps it exactly there.</summary>
    public float Jitter { get; init; }

    /// <summary>Biome the site is meant to stand in; the vanilla layout keeps it there in every world.</summary>
    public string? Biome { get; init; }

    /// <summary>Pad height in metres. Omitted: the natural ground height, kept clear of the water.</summary>
    public float? PadHeight { get; init; }

    /// <summary>Turn in degrees, a multiple of 90.</summary>
    public float Yaw { get; init; }

    /// <summary>What connects the site to the roads (<see cref="RoadLinks"/>). Omitted: the prefab's own <see cref="PoiPrefab.Road"/>.</summary>
    public string? Road { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A set of POI prefabs shared out over the sites that name the pool, shuffled by the seed (World Map spec §4: large slots
/// hold warehouses of random size or Farmbot scrapyards, while the totals stay the same in every world). The counts
/// must add up to the number of sites that use it.
/// </summary>
public sealed record PoolDefinition
{
    /// <summary>Id, unique in the file.</summary>
    public string? Id { get; init; }

    /// <summary>The prefabs and how many of each.</summary>
    public List<PoolEntry>? Fill { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A prefab in a <see cref="PoolDefinition"/>.</summary>
public sealed record PoolEntry
{
    /// <summary>POI prefab (<c>poi_id</c> or <c>mod_id:poi_id</c>).</summary>
    public string? Poi { get; init; }

    /// <summary>How many sites get it.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
