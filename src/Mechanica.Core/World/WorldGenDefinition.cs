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

    /// <summary>
    /// The world map's broad layout: each biome cell takes the biome of the nearest region. Omitted: every inland cell
    /// picks a biome at random by <see cref="BiomeDefinition.Weight"/>.
    /// </summary>
    public List<RegionDefinition>? Regions { get; init; }

    /// <summary>Rivers cut through the land.</summary>
    public List<RiverDefinition>? Rivers { get; init; }

    /// <summary>The road network between the <see cref="Sites"/> (and dirt tracks to camps). Omitted: no roads.</summary>
    public RoadsDefinition? Roads { get; init; }

    /// <summary>The world's mandatory structures, at the same place in every world.</summary>
    public List<SiteDefinition>? Sites { get; init; }

    /// <summary>Prefab pools that sites draw from, shuffled per seed.</summary>
    public List<PoolDefinition>? Pools { get; init; }

    /// <summary>Liquids that POI pools hold (<see cref="PoiPrefab.Pools"/>): chemicals, oil.</summary>
    public List<LiquidDefinition>? Liquids { get; init; }

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

    /// <summary>How far, in metres, slow noise bends the borders between <see cref="WorldGenDefinition.Regions"/>.</summary>
    public float RegionWarp { get; init; } = 400f;

    /// <summary>The broad hills every inland biome stands on. Omitted: none.</summary>
    public HillsDefinition? Hills { get; init; }

    /// <summary>Cliff terraces: plateaus that step up towards the middle of the island. Omitted: none.</summary>
    public CliffsDefinition? Cliffs { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// The broad hills under every inland biome: slow fractal noise, a kilometre or so from crest to crest, that lifts the land
/// from the shores up to <see cref="Height"/>. Water stays flat: the hills ease in over <see cref="ShoreRamp"/> from the
/// coast, rivers and flat biomes (lakes), and over <see cref="PadRamp"/> from sites with a fixed pad height.
/// </summary>
public sealed record HillsDefinition
{
    /// <summary>Most the hills lift the ground, in metres.</summary>
    public float Height { get; init; }

    /// <summary>Wavelength of the largest hills in metres.</summary>
    public float Scale { get; init; } = 1024f;

    /// <summary>Noise layers, each at twice the frequency of the last.</summary>
    public int Octaves { get; init; } = 4;

    /// <summary>Amplitude step between layers.</summary>
    public float Gain { get; init; } = 0.5f;

    /// <summary>How strongly the noise is stretched before it is clamped: higher gives broader flat valleys and tops.</summary>
    public float Contrast { get; init; } = 2f;

    /// <summary>Metres over which the hills rise from water and flat biomes to full height.</summary>
    public float ShoreRamp { get; init; } = 320f;

    /// <summary>Metres beyond a fixed pad's rim (a site's <c>pad_height</c>) over which the hills rise to full height.</summary>
    public float PadRamp { get; init; } = 160f;

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// Cliff terraces: the ground stands on up to <see cref="Levels"/> plateaus, each <see cref="Step"/> metres above the last,
/// with short steep rock faces between them. The levels come from slow noise, rise towards the middle of the island over
/// <see cref="Inland"/> and fade where the hills do, so shores and lakes lie flat.
/// </summary>
public sealed record CliffsDefinition
{
    /// <summary>Height of one cliff in metres.</summary>
    public float Step { get; init; } = 8f;

    /// <summary>Most plateaus stacked above the ground.</summary>
    public int Levels { get; init; } = 4;

    /// <summary>Wavelength of the noise the levels come from, in metres.</summary>
    public float Scale { get; init; } = 384f;

    /// <summary>How strongly the noise is stretched before it is clamped, like <see cref="HillsDefinition.Contrast"/>.</summary>
    public float Contrast { get; init; } = 2f;

    /// <summary>Metres of ground a cliff face takes, foot to top; smaller is steeper.</summary>
    public float Face { get; init; } = 6f;

    /// <summary>Metres from the coast over which the plateaus rise to their full number.</summary>
    public float Inland { get; init; } = 1200f;

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

    /// <summary>
    /// 0 to 1: how much of the relief is ridged noise, sharp crests between rounded valleys (mountains), rather than
    /// rolling hills.
    /// </summary>
    public float Ridge { get; init; }

    /// <summary>Height in metres of the steps the ground is terraced into (desert plateaus). 0: no terraces.</summary>
    public float Terrace { get; init; }

    /// <summary>
    /// 0 to 1: how much of the <see cref="TerrainDefinition.Hills"/> and <see cref="TerrainDefinition.Cliffs"/> this biome
    /// keeps (0 for lakes, which lie flat). Its neighbours ease to it over <see cref="HillsDefinition.ShoreRamp"/>.
    /// </summary>
    public float Hills { get; init; } = 1f;

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

    /// <summary>
    /// What may damage the object (<see cref="DamageKinds"/>: <c>sledge</c>, <c>drill</c>, <c>saw</c>, <c>weapon</c>,
    /// <c>explosion</c>, <c>bot</c>). Anything else does nothing to it. Omitted means everything does. A rock outcrop
    /// takes only <c>drill</c>: hand tools bounce off it.
    /// </summary>
    public List<string>? DamageFrom { get; init; }

    /// <summary>
    /// Loose physics parts (rock chunks) the object falls apart into when its health runs out, each its own Creation
    /// thrown a little apart. Given with or instead of <see cref="Drops"/>.
    /// </summary>
    public BreakDefinition? BreaksInto { get; init; }

    /// <summary>
    /// Makes each scattered spot of this object a rock deposit: a cluster of mounds heaped from blocks, each block an
    /// object of its own (<see cref="WorldObjectId.Piece"/>) with its own definition's collider, look and health.
    /// <see cref="Scale"/> is shared by the whole deposit, and <see cref="Footprint"/> bounds it.
    /// </summary>
    public OutcropDefinition? Outcrop { get; init; }

    /// <summary>
    /// A model (<c>.glb</c>/<c>.gltf</c>, a mod path) the object is drawn with instead of <see cref="Pieces"/>,
    /// centred <see cref="MeshDefinition.At"/> above the ground point.
    /// </summary>
    public MeshDefinition? Mesh { get; init; }

    /// <summary>Steepest ground (rise over run) this object stands on, below the terrain's <c>max_scatter_slope</c>. Omitted: that.</summary>
    public float? MaxSlope { get; init; }

    /// <summary>
    /// Makes the object fall like a tree when its health runs out: its <see cref="BreaksInto"/> pieces (log segments) are
    /// stacked up the trunk instead of thrown apart, and tip over together away from the blow that felled it.
    /// </summary>
    public FellDefinition? Fell { get; init; }

    /// <summary>
    /// Sound events the object makes itself (<c>hit</c> when damage reaches it, <c>break</c> when it breaks or falls),
    /// instead of the tool's own (the Sledgehammer's <c>smash_hit</c>, a cracking rock's <c>drill_break</c>).
    /// </summary>
    public Dictionary<string, string>? Sounds { get; init; }

    /// <summary>What flies off the object when it is hit and when it breaks: wood chips, falling leaves.</summary>
    public DebrisDefinition? Debris { get; init; }

    /// <summary>
    /// True for something gathered by hand (an oil geyser): pressing Use (E) on it puts its <see cref="Drops"/> straight
    /// into the player's inventory and the object is used up, until it <see cref="Regrow"/>s.
    /// </summary>
    public bool Harvest { get; init; }

    /// <summary>
    /// Seconds of world time after the object is destroyed (broken, refined or harvested) until it stands again, full.
    /// Omitted: it stays gone.
    /// </summary>
    public float? Regrow { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>True when damage of <paramref name="kind"/> (<see cref="DamageKinds"/>) can hurt the object.</summary>
    public bool TakesDamageFrom(string kind) => DamageFrom is null || DamageFrom.Contains(kind);
}

/// <summary>Sound keys of <see cref="WorldObjectDefinition.Sounds"/>.</summary>
public static class ObjectSounds
{
    /// <summary>Damage reached the object (a sledge blow, a saw's bite).</summary>
    public const string Hit = "hit";

    /// <summary>The object broke, or a tree started to fall.</summary>
    public const string Break = "break";

    /// <summary>Every key.</summary>
    public static readonly IReadOnlyList<string> All = [Hit, Break];
}

/// <summary>How a felled tree falls (<see cref="WorldObjectDefinition.Fell"/>).</summary>
public sealed record FellDefinition
{
    /// <summary>Height of the trunk in metres (before the object's scale) that the log segments are stacked up.</summary>
    public float Height { get; init; } = 6f;

    /// <summary>How fast the trunk starts to tip over, in rad/s about its foot.</summary>
    public float Spin { get; init; } = 0.6f;

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Cosmetic bits a world object throws off (<see cref="WorldObjectDefinition.Debris"/>), drawn by every peer.</summary>
public sealed record DebrisDefinition
{
    /// <summary>Colours (<c>#rrggbb</c>) of the chips that fly from where the object is hit.</summary>
    public List<string>? Chips { get; init; }

    /// <summary>Colours (<c>#rrggbb</c>) of the leaves that fall from its canopy when it is hit, and in a burst when it breaks.</summary>
    public List<string>? Leaves { get; init; }

    /// <summary>Middle of the canopy, relative to the ground point (before scale).</summary>
    public Vec3 Canopy { get; init; }

    /// <summary>Radius of the canopy in metres (before scale) that leaves fall from.</summary>
    public float CanopyRadius { get; init; } = 2f;

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>What deals damage, for <see cref="WorldObjectDefinition.DamageFrom"/>.</summary>
public static class DamageKinds
{
    /// <summary>A Sledgehammer swing.</summary>
    public const string Sledge = "sledge";

    /// <summary>A spinning drill part.</summary>
    public const string Drill = "drill";

    /// <summary>A spinning saw blade part.</summary>
    public const string Saw = "saw";

    /// <summary>A shot, a thrown or mounted weapon, any other script hit.</summary>
    public const string Weapon = "weapon";

    /// <summary>A blast's splash.</summary>
    public const string Explosion = "explosion";

    /// <summary>A bot's attack.</summary>
    public const string Bot = "bot";

    /// <summary>Every kind.</summary>
    public static readonly IReadOnlyList<string> All = [Sledge, Drill, Saw, Weapon, Explosion, Bot];
}

/// <summary>A model a world object is drawn with.</summary>
public sealed record MeshDefinition
{
    /// <summary>Mod path of the model (<c>meshes/world/rock_block_medium_a.glb</c>, or <c>other_mod:path</c>).</summary>
    public string? File { get; init; }

    /// <summary>
    /// Other models (mod paths) of the same size and origin, drawn instead of <see cref="File"/> on some placements
    /// (<see cref="WorldObjectPlacement.Variant"/>) so neighbours do not match. Only rock deposit blocks pick one.
    /// </summary>
    public List<string>? Variants { get; init; }

    /// <summary>Where the model's origin goes, relative to the object's ground point (before scale).</summary>
    public Vec3 At { get; init; }

    /// <summary>
    /// A simpler model (mod path) drawn instead past <see cref="LodDistance"/>, and always in far scenery. Omitted: the
    /// full model at every distance.
    /// </summary>
    public string? Lod { get; init; }

    /// <summary>Metres from the camera past which <see cref="Lod"/> is drawn instead of the full model.</summary>
    public float LodDistance { get; init; } = 60f;

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A rock deposit (<see cref="WorldObjectDefinition.Outcrop"/>): a cluster of mounds, each a heap of chiselled blocks of
/// mixed sizes, as in Scrap Mechanic's square rocks. A size is rolled per deposit, then a size per mound.
/// </summary>
public sealed record OutcropDefinition
{
    /// <summary>The deposit sizes, one picked per deposit by <see cref="DepositSize.Weight"/>.</summary>
    public List<DepositSize>? Deposits { get; init; }

    /// <summary>The mound sizes the deposits name, by name.</summary>
    public Dictionary<string, MoundDefinition>? Mounds { get; init; }

    /// <summary>Chance (0 to 1) that a mound other than a deposit's first is metal-rich: its blocks swapped by <see cref="Rich"/>.</summary>
    public float RichChance { get; init; }

    /// <summary>The object each block of a metal-rich mound becomes instead, by the block's own object id.</summary>
    public Dictionary<string, string>? Rich { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>One size of rock deposit (<see cref="OutcropDefinition.Deposits"/>).</summary>
public sealed record DepositSize
{
    /// <summary>How likely this size is, against the others.</summary>
    public float Weight { get; init; } = 1f;

    /// <summary>How many mounds, <c>[min, max]</c>.</summary>
    public List<int>? Mounds { get; init; }

    /// <summary>Metres from the deposit's middle the other mounds' middles may stand (before scale).</summary>
    public float Spread { get; init; }

    /// <summary>The mound sizes (<see cref="OutcropDefinition.Mounds"/> names), one picked per mound by weight.</summary>
    public List<MoundPick>? Sizes { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A mound size a deposit may pick (<see cref="DepositSize.Sizes"/>).</summary>
public sealed record MoundPick
{
    /// <summary>Name of the mound size in <see cref="OutcropDefinition.Mounds"/>.</summary>
    public string? Mound { get; init; }

    /// <summary>How likely it is, against the others.</summary>
    public float Weight { get; init; } = 1f;

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>One size of mound (<see cref="OutcropDefinition.Mounds"/>): a heap of blocks.</summary>
public sealed record MoundDefinition
{
    /// <summary>How many blocks, <c>[min, max]</c>.</summary>
    public List<int>? Blocks { get; init; }

    /// <summary>The blocks it is made of: world objects with a box collider, one picked per block by weight.</summary>
    public List<MoundBlock>? Kinds { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A block a mound may be made of (<see cref="MoundDefinition.Kinds"/>).</summary>
public sealed record MoundBlock
{
    /// <summary>Id of the world object the block is: its collider box is the block's size.</summary>
    public string? Object { get; init; }

    /// <summary>How likely it is, against the others.</summary>
    public float Weight { get; init; } = 1f;

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Loose parts something falls apart into (<see cref="WorldObjectDefinition.BreaksInto"/>, <see cref="Data.PartDefinition.BreaksInto"/>).</summary>
public sealed record BreakDefinition
{
    /// <summary>Most pieces one break may make.</summary>
    public const int MaxCount = 16;

    /// <summary>How many pieces (1 to <see cref="MaxCount"/>); the least when <see cref="CountMax"/> is given.</summary>
    public int Count { get; init; } = 1;

    /// <summary>The most pieces, when the number varies (<see cref="Count"/> to this, evenly). Omitted: always <see cref="Count"/>.</summary>
    public int? CountMax { get; init; }

    /// <summary>What each piece may be: one is picked per piece by <see cref="BreakPiece.Weight"/>.</summary>
    public List<BreakPiece>? Parts { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>How many pieces one break makes, from <paramref name="roll"/> in [0, 1), clamped to 1..<see cref="MaxCount"/>.</summary>
    public int CountFor(float roll)
    {
        var low = Math.Clamp(Count, 1, MaxCount);
        var high = Math.Clamp(CountMax ?? low, low, MaxCount);
        return Math.Min(high, low + (int)(Math.Clamp(roll, 0f, 1f) * (high - low + 1)));
    }

    /// <summary>The part a piece is, from <paramref name="roll"/> in [0, 1); null when there are no parts.</summary>
    public string? Pick(float roll)
    {
        var parts = (Parts ?? []).Where(p => p.Part is not null && p.Weight > 0).ToList();
        var total = parts.Sum(p => p.Weight);
        if (total <= 0)
            return null;
        var at = roll * total;
        foreach (var p in parts)
        {
            if (at < p.Weight)
                return p.Part;
            at -= p.Weight;
        }
        return parts[^1].Part;
    }
}

/// <summary>One kind of piece a <see cref="BreakDefinition"/> may make.</summary>
public sealed record BreakPiece
{
    /// <summary>Part id (a loose part: <see cref="Data.PartDefinition.Loose"/>).</summary>
    public string? Part { get; init; }

    /// <summary>Relative chance against the other pieces.</summary>
    public float Weight { get; init; } = 1f;

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

    /// <summary>The most there may be: each time, a random count from <see cref="Count"/> to this. Omitted: always <see cref="Count"/>.</summary>
    public int? CountMax { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A liquid POI pools hold (<c>liquids[]</c>): its look, what it does to a player standing in it, and what an empty bucket
/// dipped in it collects.
/// </summary>
public sealed record LiquidDefinition
{
    /// <summary>Id, unique in the file (lowercase snake_case), from CATALOG_IDS.md.</summary>
    public string? Id { get; init; }

    /// <summary>Surface colour, <c>#rrggbb</c>.</summary>
    public string? Color { get; init; }

    /// <summary>How brightly the surface glows in its own colour: 0 for none.</summary>
    public float Glow { get; init; }

    /// <summary>Surface roughness, 0 (mirror) to 1 (matte).</summary>
    public float Roughness { get; init; } = 0.1f;

    /// <summary>Size of the surface ripples in metres.</summary>
    public float RippleSize { get; init; } = 4f;

    /// <summary>How deep the ripples look, 0 (a still mirror) to 1.</summary>
    public float Ripples { get; init; } = 0.6f;

    /// <summary>How fast the ripples and swirls drift, in metres per second: low for a thick liquid.</summary>
    public float Flow { get; init; } = 0.05f;

    /// <summary>Strength of the rainbow film that drifts over the surface, 0 to 1 (an oil slick).</summary>
    public float Sheen { get; init; }

    /// <summary>How much the surface bubbles, 0 to 1.</summary>
    public float Bubbles { get; init; }

    /// <summary>Colour of the scum or crust along the shore, <c>#rrggbb</c>. Omitted: none.</summary>
    public string? EdgeColor { get; init; }

    /// <summary>Health a player standing in it loses per second.</summary>
    public float Damage { get; init; }

    /// <summary>What an empty bucket dipped in it collects; the bucket stays empty. Omitted: nothing.</summary>
    public ObjectDrop? Collect { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A round pool of a <see cref="LiquidDefinition"/> in a POI (<c>pools[]</c>), usually laid in a basin.</summary>
public sealed record LiquidPool
{
    /// <summary>Id of the <see cref="LiquidDefinition"/>.</summary>
    public string? Liquid { get; init; }

    /// <summary>Centre <c>[x, z]</c>, relative to the POI. Omitted means the POI centre.</summary>
    public List<float>? At { get; init; }

    /// <summary>
    /// Radius of the surface in metres. The liquid fills the part of that disc where the ground is lower than the
    /// surface, so its shore follows the basin; make it reach past the basin's edge.
    /// </summary>
    public float Radius { get; init; }

    /// <summary>Height of the surface above the POI's pad, in metres (below 0 in a basin).</summary>
    public float Level { get; init; }

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

    /// <summary>
    /// Named points for later systems. An <c>object</c> marker places the world object <see cref="PoiMarker.Ref"/> names
    /// (an oil geyser) there, with an id of its own (<see cref="WorldObjectId.ForPoi"/>).
    /// </summary>
    public List<PoiMarker>? Markers { get; init; }

    /// <summary>Pools of liquid (<see cref="LiquidPool"/>).</summary>
    public List<LiquidPool>? Pools { get; init; }

    /// <summary>Ground painted on the footprint, in order (<see cref="GroundPaint"/>).</summary>
    public List<GroundPaint>? Ground { get; init; }

    /// <summary>
    /// Id of a biome whose flora grows on the footprint, except on grounds without <see cref="GroundDefinition.Flora"/>.
    /// Omitted: nothing grows inside the POI.
    /// </summary>
    public string? Flora { get; init; }

    /// <summary>
    /// What connects the POI to the road network (<see cref="RoadLinks"/>): <c>road</c> (only where a site places it),
    /// <c>track</c> (a dirt track to the nearest road, when one is near) or <c>none</c> (the default).
    /// </summary>
    public string? Road { get; init; }

    /// <summary>Footprint as built, <c>[width, height, depth]</c> in metres: for the placement manifest and later design work.</summary>
    public Vec3? Size { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// Placement rules. Either <see cref="Fixed"/> (one POI at that spot) or <see cref="Spacing"/>: one
/// candidate per cell of a grid whose step is the middle of the range, jittered so neighbours stay within it.
/// With neither, the POI goes only where a site (<see cref="SiteDefinition"/>) places it.
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

    /// <summary>Metres a spaced POI's footprint keeps clear of every other POI's footprint and of roads.</summary>
    public float Clearance { get; init; }

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

    /// <summary>
    /// How far the edge wanders in and out, as a fraction of the radius (0 to 0.5): 0 is a disc, 0.3 a pond with
    /// natural lobes. Each POI gets its own shape. Omitted: 0.
    /// </summary>
    public float? Wobble { get; init; }

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
    /// <summary>Marker type that places a world object (<see cref="Ref"/> is its id).</summary>
    public const string ObjectType = "object";

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
