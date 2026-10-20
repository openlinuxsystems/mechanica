using System.Globalization;
using System.Numerics;

namespace Mechanica.Core.World;

/// <summary>
/// Stable id of a scattered world object: the scatter rule that placed it and the rule's grid cell. The
/// same seed and world data always give the same ids, so destroyed objects can be saved and synced as ids.
/// Written as <c>rule.x.z</c>, or <c>rule.x.z.piece</c> for the second and later cubes of a rock outcrop
/// (<see cref="WorldObjectDefinition.Outcrop"/>), each of which is an object of its own. An object a POI's
/// <c>object</c> marker places (<see cref="ForPoi"/>) is <c>poi.index.marker</c>: its POI's place in
/// <see cref="WorldGenerator.Pois"/> and the marker's in the prefab.
/// </summary>
public readonly record struct WorldObjectId(int Rule, int CellX, int CellZ, int Piece = 0)
{
    /// <summary><see cref="Rule"/> of an object a POI marker places.</summary>
    public const int PoiRule = -1;

    /// <summary>The id of the object marker <paramref name="marker"/> of POI <paramref name="poi"/> places.</summary>
    public static WorldObjectId ForPoi(int poi, int marker) => new(PoiRule, poi, marker);

    /// <summary>True for an object a POI marker placed rather than a scatter rule.</summary>
    public bool IsPoi => Rule == PoiRule;

    /// <inheritdoc />
    public override string ToString() => IsPoi
        ? string.Create(CultureInfo.InvariantCulture, $"poi.{CellX}.{CellZ}")
        : Piece == 0
        ? string.Create(CultureInfo.InvariantCulture, $"{Rule}.{CellX}.{CellZ}")
        : string.Create(CultureInfo.InvariantCulture, $"{Rule}.{CellX}.{CellZ}.{Piece}");

    /// <summary>Parses the <see cref="ToString"/> form.</summary>
    public static bool TryParse(string? text, out WorldObjectId id)
    {
        id = default;
        var parts = text?.Split('.');
        var piece = 0;
        if (parts is [ "poi", var poiText, var markerText ])
        {
            if (!int.TryParse(poiText, NumberStyles.None, CultureInfo.InvariantCulture, out var poi)
                || !int.TryParse(markerText, NumberStyles.None, CultureInfo.InvariantCulture, out var marker))
                return false;
            id = ForPoi(poi, marker);
            return true;
        }
        if (parts is not { Length: 3 or 4 }
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var rule)
            || !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var z)
            || (parts.Length == 4 && (!int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out piece) || piece == 0)))
            return false;
        id = new WorldObjectId(rule, x, z, piece);
        return true;
    }
}

/// <summary>A scattered object in the world.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Definition">What it is.</param>
/// <param name="Position">World position of its ground point (already sunk by <see cref="WorldObjectDefinition.Sink"/>).</param>
/// <param name="Yaw">Turn about +Y in radians.</param>
/// <param name="Scale">Uniform scale.</param>
/// <param name="Variant">Which model it is drawn with: 0 for <see cref="MeshDefinition.File"/>, then each of <see cref="MeshDefinition.Variants"/>.</param>
public sealed record WorldObjectPlacement(WorldObjectId Id, WorldObjectDefinition Definition, Vector3 Position, float Yaw, float Scale, int Variant = 0);

/// <summary>A placed POI.</summary>
/// <param name="Index">Its place in <see cref="WorldGenerator.Pois"/>.</param>
/// <param name="Poi">Its prefab.</param>
/// <param name="Centre">World x/z of its centre.</param>
/// <param name="PadHeight">Height of its flattened pad.</param>
/// <param name="QuarterTurns">Turn about +Y in quarter turns (0..3).</param>
public sealed record PoiInstance(int Index, LoadedPoi Poi, Vector2 Centre, float PadHeight, int QuarterTurns)
{
    /// <summary>Turn about +Y in radians.</summary>
    public float Yaw => QuarterTurns * (MathF.PI / 2f);

    /// <summary>The site (<see cref="SiteDefinition"/>) that placed this POI, or null for a fixed or spaced one.</summary>
    public LoadedSite? Site { get; init; }

    /// <summary>Footprint radius.</summary>
    public float Radius => Poi.Prefab.Radius;

    /// <summary>World position of a point given relative to the POI centre on its pad.</summary>
    public Vector3 ToWorld(Vector3 local)
    {
        var turned = Turn(new Vector2(local.X, local.Z));
        return new Vector3(Centre.X + turned.X, PadHeight + local.Y, Centre.Y + turned.Y);
    }

    /// <summary>World x/z of a point given as x/z relative to the centre.</summary>
    public Vector2 ToWorld(Vector2 local) => Centre + Turn(local);

    // Counter-clockwise seen from above, like Godot's Basis(Vector3.Up, angle): +x turns to -z. Exact.
    private Vector2 Turn(Vector2 v) => QuarterTurns switch
    {
        1 => new Vector2(v.Y, -v.X),
        2 => new Vector2(-v.X, -v.Y),
        3 => new Vector2(-v.Y, v.X),
        _ => v,
    };
}

/// <summary>A structure piece of a POI.</summary>
/// <param name="Poi">The POI it belongs to.</param>
/// <param name="Piece">Its index in the prefab's pieces.</param>
/// <param name="Shape">Its shape.</param>
/// <param name="Position">World centre.</param>
/// <param name="Yaw">Turn about +Y in radians (the POI's and the piece's).</param>
public sealed record PoiPiecePlacement(PoiInstance Poi, int Piece, ShapeDefinition Shape, Vector3 Position, float Yaw);

/// <summary>A model of a POI (<see cref="PoiModel"/>), in world space.</summary>
/// <param name="Poi">The POI it belongs to.</param>
/// <param name="Index">Its index in the prefab's models.</param>
/// <param name="Model">The model data.</param>
/// <param name="File">Full path of its file, or null when the world was built without mods.</param>
/// <param name="Position">World position of its origin.</param>
/// <param name="Yaw">Turn about +Y in radians (the POI's and the model's).</param>
public sealed record PoiModelPlacement(PoiInstance Poi, int Index, PoiModel Model, string? File, Vector3 Position, float Yaw);

/// <summary>A marker of a POI, in world space.</summary>
/// <param name="Poi">The POI it belongs to.</param>
/// <param name="Marker">The marker data.</param>
/// <param name="Position">World position.</param>
/// <param name="Yaw">Turn about +Y in radians.</param>
public sealed record PoiMarkerPlacement(PoiInstance Poi, PoiMarker Marker, Vector3 Position, float Yaw);

/// <summary>A pool of liquid in a POI, in world space.</summary>
/// <param name="Poi">The POI it belongs to.</param>
/// <param name="Pool">The pool data.</param>
/// <param name="Liquid">What it holds.</param>
/// <param name="Centre">World centre of its surface.</param>
public sealed record PoiPoolPlacement(PoiInstance Poi, LiquidPool Pool, LiquidDefinition Liquid, Vector3 Centre)
{
    /// <summary>Radius of the surface.</summary>
    public float Radius => Pool.Radius;

    /// <summary>
    /// True when <paramref name="point"/> is under the surface and over its disc. The liquid is only where the ground is
    /// lower too (<see cref="WorldGenerator.LiquidAt"/>).
    /// </summary>
    public bool Holds(Vector3 point) =>
        point.Y <= Centre.Y && Vector2.DistanceSquared(new Vector2(point.X, point.Z), new Vector2(Centre.X, Centre.Z)) <= Radius * Radius;
}

/// <summary>
/// One generated chunk: a square grid of <see cref="Samples"/> × <see cref="Samples"/> heights spaced
/// <see cref="Resolution"/> metres apart, covering the chunk edge to edge so neighbours share their border
/// samples, plus the objects, POI pieces and markers whose positions fall inside the chunk.
/// </summary>
public sealed class TerrainChunkData
{
    internal TerrainChunkData(ChunkCoord coord, float size, float resolution, int samples, float[] heights, Vector3[] normals,
        Vector3[] colors, int[] biomes, bool[] covered, float seaLevel, IReadOnlyList<WorldObjectPlacement> objects, IReadOnlyList<PoiPiecePlacement> pieces,
        IReadOnlyList<PoiMarkerPlacement> markers)
    {
        Coord = coord;
        Size = size;
        Resolution = resolution;
        Samples = samples;
        Heights = heights;
        Normals = normals;
        Colors = colors;
        Biomes = biomes;
        Covered = covered;
        SeaLevel = seaLevel;
        Objects = objects;
        Pieces = pieces;
        Markers = markers;
        MinHeight = heights.Min();
        MaxHeight = heights.Max();
    }

    /// <summary>Ground layers per sample in <see cref="Grounds"/>: <see cref="GroundDefinition.MaxGrounds"/>.</summary>
    public const int GroundLayers = GroundDefinition.MaxGrounds;

    /// <summary>
    /// How much of each ground (<see cref="WorldGenData.Grounds"/>) covers each sample, <see cref="GroundLayers"/> weights per
    /// sample, row by row. They sum to at most 1; the rest is the plain biome colour. Empty when the world has no grounds.
    /// </summary>
    public float[] Grounds { get; init; } = [];

    /// <summary>Liquid pools whose centre is in the chunk.</summary>
    public IReadOnlyList<PoiPoolPlacement> Pools { get; init; } = [];

    /// <summary>
    /// POI models whose origin is in the chunk. A model is built by the one chunk that holds its origin; a chunk is near
    /// (colliding) whenever a player is within the near distance of it, so a model up to that size is always solid
    /// round a player standing in or on it.
    /// </summary>
    public IReadOnlyList<PoiModelPlacement> Models { get; init; } = [];

    /// <summary>Road surfaces, curbs, junctions and bridges in the chunk, in chunk-local metres (empty when there are none).</summary>
    public RoadMesh Roads { get; init; } = RoadMesh.Empty;

    /// <summary>Which chunk.</summary>
    public ChunkCoord Coord { get; }

    /// <summary>Side in metres.</summary>
    public float Size { get; }

    /// <summary>Metres between samples.</summary>
    public float Resolution { get; }

    /// <summary>Samples per side.</summary>
    public int Samples { get; }

    /// <summary>Heights, row by row: sample (i, j) is at index j × <see cref="Samples"/> + i, at x = origin + i × resolution, z = origin + j × resolution.</summary>
    public float[] Heights { get; }

    /// <summary>Unit ground normals, laid out like <see cref="Heights"/>.</summary>
    public Vector3[] Normals { get; }

    /// <summary>Ground colours (0..1 RGB), laid out like <see cref="Heights"/>.</summary>
    public Vector3[] Colors { get; }

    /// <summary>
    /// Index of the biome whose flora grows at each sample, laid out like <see cref="Heights"/>: the dominant one, except
    /// across a border, where both biomes' patches mix (<see cref="TerrainDefinition.BiomeEdgeNoise"/>).
    /// </summary>
    public int[] Biomes { get; }

    /// <summary>True where a POI's footprint covers the sample, laid out like <see cref="Heights"/>. Nothing grows there.</summary>
    public bool[] Covered { get; }

    /// <summary>Height of the sea surface.</summary>
    public float SeaLevel { get; }

    /// <summary>Lowest height in the chunk.</summary>
    public float MinHeight { get; }

    /// <summary>Highest height in the chunk.</summary>
    public float MaxHeight { get; }

    /// <summary>True when some of the ground is under the sea surface.</summary>
    public bool HasWater => MinHeight < SeaLevel;

    /// <summary>Scattered objects whose ground point is in the chunk, destroyed ones included.</summary>
    public IReadOnlyList<WorldObjectPlacement> Objects { get; }

    /// <summary>POI structure pieces whose centre is in the chunk.</summary>
    public IReadOnlyList<PoiPiecePlacement> Pieces { get; }

    /// <summary>POI markers in the chunk.</summary>
    public IReadOnlyList<PoiMarkerPlacement> Markers { get; }

    /// <summary>The height at sample (i, j).</summary>
    public float HeightAt(int i, int j) => Heights[j * Samples + i];
}
