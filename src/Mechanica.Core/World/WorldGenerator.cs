using System.Numerics;

namespace Mechanica.Core.World;

/// <summary>
/// Generates the world from a seed and <see cref="WorldGenData"/> (chunk 19, D11). Everything is a pure
/// function of (seed, data, position), so any chunk can be generated alone, in any order, on any peer, and
/// neighbouring chunks agree on their shared border.
/// <list type="bullet">
/// <item><b>Biomes.</b> Inland ground is cut into jittered cells (<see cref="TerrainDefinition.BiomeCellSize"/>),
/// each given a biome by weight; neighbouring cells blend over <see cref="TerrainDefinition.BiomeBlend"/> metres.
/// Across that band the ground textures, flora and scattered objects of both biomes mix in noisy tongues and patches
/// (<see cref="TerrainDefinition.BiomeEdgeNoise"/>), thinning out the further they reach, so no border is a line.
/// The coast biome takes over near the world edge.</item>
/// <item><b>Height.</b> Each biome sets an average height and a relief that scales fractal noise. Within
/// <see cref="TerrainDefinition.CoastWidth"/> of the edge the land falls to the sea floor, so the world
/// is an island.</item>
/// <item><b>POIs.</b> Laid out once, when the generator is made: fixed POIs first, then spaced kinds by
/// priority, one jittered candidate per grid cell so neighbours stay within the prefab's spacing range.
/// A candidate must be in an allowed biome, on dry and not too steep ground, and clear of placed POIs.
/// Each POI then flattens its pad and applies its other terrain ops.</item>
/// <item><b>Objects.</b> Each biome scatter rule puts at most one candidate in every cell of its own grid.
/// A candidate is kept by chance when it is in that biome, dry, not too steep, outside every POI, off the roads and
/// clear of earlier rules' candidates, which keeps object ids (<see cref="WorldObjectId"/>) stable.</item>
/// </list>
/// The World Map spec adds a fixed layout on top: <see cref="WorldGenDefinition.Regions"/> decide which biome the cells
/// take, <see cref="WorldGenDefinition.Rivers"/> cut channels, <see cref="WorldGenDefinition.Sites"/> put the mandatory
/// structures at the same place in every world, and <see cref="Roads"/> connect them before the spaced POIs fill in.
/// </summary>
public sealed partial class WorldGenerator
{
    private const float PoiBucket = 128f;
    private const int PoiAttempts = 8;

    // Wavelength in metres of the noise that rags the edge of a POI's painted ground.
    private const float PaintNoiseScale = 9f;

    private readonly TerrainDefinition _terrain;
    private readonly float _halfX;
    private readonly float _halfZ;
    private readonly uint _heightSalt;
    private readonly uint _cellSalt;
    private readonly uint _biomeSalt;
    private readonly uint _poiSalt;
    private readonly uint _scatterSalt;
    private readonly uint _clusterSalt;
    private readonly uint _warpSalt;
    private readonly uint _coastSalt;
    private readonly uint _paintSalt;
    private readonly uint _hillSalt;
    private readonly uint _cliffSalt;
    private readonly uint _edgeSalt;
    private readonly uint _bumpSalt;
    private readonly int _cellReach;
    private readonly uint _sheerSalt;
    private readonly float _sheerThreshold;
    private readonly (Vector2 Centre, float Radius)[] _fixedPads;
    private readonly Biome[] _inland;
    private readonly float _inlandWeight;
    private readonly List<PoiInstance> _pois = [];
    private readonly Dictionary<(int, int), List<PoiInstance>> _poiBuckets = [];

    // The scatter rules that place objects which clear flora, themselves or as a scene's pieces.
    private readonly List<Scatter> _clearingRules;

    /// <summary>Lays out a square world's POIs; terrain and objects are generated on demand.</summary>
    /// <param name="data">Rules and prefabs.</param>
    /// <param name="seed">World seed.</param>
    /// <param name="worldSize">Side of the square world in metres, centred on the origin.</param>
    public WorldGenerator(WorldGenData data, long seed, float worldSize)
        : this(data, seed, worldSize, worldSize)
    {
    }

    /// <summary>Lays out the world's POIs and roads; terrain and objects are generated on demand.</summary>
    /// <param name="data">Rules and prefabs.</param>
    /// <param name="seed">World seed.</param>
    /// <param name="worldSize">East-west extent of the world in metres, centred on the origin.</param>
    /// <param name="worldDepth">North-south extent of the world in metres, centred on the origin.</param>
    public WorldGenerator(WorldGenData data, long seed, float worldSize, float worldDepth)
    {
        if (!(worldSize > 0f))
            throw new ArgumentOutOfRangeException(nameof(worldSize), worldSize, "World size must be positive.");
        if (!(worldDepth > 0f))
            throw new ArgumentOutOfRangeException(nameof(worldDepth), worldDepth, "World depth must be positive.");
        Data = data;
        Seed = seed;
        WorldSize = worldSize;
        WorldDepth = worldDepth;
        _terrain = data.Terrain;
        _halfX = worldSize / 2f;
        _halfZ = worldDepth / 2f;
        _heightSalt = Noise.Salt(seed, 1);
        _cellSalt = Noise.Salt(seed, 2);
        _biomeSalt = Noise.Salt(seed, 3);
        _poiSalt = Noise.Salt(seed, 4);
        _scatterSalt = Noise.Salt(seed, 5);
        _warpSalt = Noise.Salt(seed, 6);
        _coastSalt = Noise.Salt(seed, 7);
        _paintSalt = Noise.Salt(seed, 9);
        _hillSalt = Noise.Salt(seed, 10);
        _cliffSalt = Noise.Salt(seed, 11);
        _edgeSalt = Noise.Salt(seed, 13);
        _bumpSalt = Noise.Salt(seed, 14);
        _clusterSalt = Noise.Salt(seed, 101);
        // A cell two or more away is at least 0.19 cell sizes further than the nearest point, one three or more away
        // at least 1.19: look one cell out while every blend and ramp fits inside the first, two cells out otherwise.
        var widest = MathF.Max(_terrain.BiomeBlend, _terrain.Hills?.ShoreRamp ?? 0f);
        _cellReach = widest <= 0.18f * _terrain.BiomeCellSize ? 1 : 2;
        _sheerSalt = Noise.Salt(seed, 12);
        _sheerThreshold = SheerThreshold();
        // The hills keep away from sites with a fixed pad height, so the pad is not a pit or a mesa.
        _fixedPads = [.. data.Sites.Where(s => s.Definition.PadHeight is not null && s.Definition.At is [_, _])
            .Select(s => (new Vector2(s.Definition.At![0] * worldSize, s.Definition.At[1] * worldDepth), s.Poi?.Prefab.Radius ?? 0f)),
            .. data.Pois.Where(p => p.Prefab.Placement!.PadHeight is not null && p.Prefab.Placement.Fixed is [_, _])
            .Select(p => (new Vector2(p.Prefab.Placement!.Fixed![0], p.Prefab.Placement.Fixed[1]), p.Prefab.Radius))];
        _clearingRules = [.. data.ScatterRules.Where(r => r.Object.ClearFlora
            || r.Object.Scene?.Layouts?.Any(l => l.Pieces?.Any(p => data.Objects.GetValueOrDefault(p.Object ?? "")?.ClearFlora == true) == true) == true)];
        _inland = [.. data.Biomes.Where(b => b.Weight > 0f && b != data.CoastBiome)];
        _inlandWeight = _inland.Sum(b => b.Weight);
        BuildRegions();
        BuildRivers();
        LayOutFixedPois();
        LayOutSites();
        Roads = data.Definition.Roads is { } roads ? RoadNetwork.Build(this, roads) : RoadNetwork.Empty;
        LayOutSpacedPois();
        Roads.AddTracks(this);
        Spawn = FindSpawn();
    }

    /// <summary>The roads and dirt tracks (empty when the rules have none).</summary>
    public RoadNetwork Roads { get; }

    /// <summary>Rules and prefabs in use.</summary>
    public WorldGenData Data { get; }

    /// <summary>World seed.</summary>
    public long Seed { get; }

    /// <summary>East-west extent of the world in metres (x).</summary>
    public float WorldSize { get; }

    /// <summary>North-south extent of the world in metres (z).</summary>
    public float WorldDepth { get; }

    /// <summary>True when a world position lies inside the world's rectangle.</summary>
    public bool InWorld(float x, float z) => MathF.Abs(x) < _halfX && MathF.Abs(z) < _halfZ;

    /// <summary>Height of the sea surface.</summary>
    public float SeaLevel => _terrain.SeaLevel;

    /// <summary>Every placed POI, in placement order.</summary>
    public IReadOnlyList<PoiInstance> Pois => _pois;

    /// <summary>Where players start: the first <c>spawn</c> marker of a fixed POI, or null when no fixed POI has one.</summary>
    public Vector3? Spawn { get; }

    /// <summary>Ground height at a world position, POI and road shaping included.</summary>
    public float HeightAt(float x, float z)
    {
        var sample = SampleBiomes(x, z);
        return Shaped(x, z, sample);
    }

    // The ground: the raw terrain flattened by the POIs and cut by the roads, then the bumps over the top, and every fixed pad
    // held at its height.
    private float Shaped(float x, float z, in BiomeSample sample) =>
        KeepPads(x, z, Bumped(x, z, sample, Roads.Shape(x, z, ApplyPois(x, z, RawHeight(x, z, sample)))));

    // Metres beyond a road's bed or a POI's edge over which the bumps rise to full height.
    private const float BumpClearance = 20f;

    // The bumps go on last, so the roads plan their routes and the POIs their pads on the smoother ground under them, and
    // they ease out towards each road and POI, which keep the ground they shaped.
    private float Bumped(float x, float z, in BiomeSample sample, float h)
    {
        if (_terrain.Bumps is not { Height: > 0f } bumps || sample.Bumps <= 0f)
            return h;
        var keep = sample.Hills * Keep(x, z);
        if (keep <= 0f)
            return h;
        keep *= Noise.SmoothStep(1f, BumpClearance, Roads.Clearance(x, z));
        foreach (var poi in PoisNear(x, z))
        {
            if (keep <= 0f)
                break;
            keep *= Noise.SmoothStep(poi.Radius, poi.Radius + BumpClearance, Vector2.Distance(poi.Centre, new Vector2(x, z)));
        }
        if (keep <= 0f)
            return h;
        float u = x / bumps.Scale, v = z / bumps.Scale;
        var n = Noise.Fractal(u, v, _bumpSalt, bumps.Octaves, 2f, bumps.Gain);
        if (bumps.Banks > 0f)
        {
            // A bank: the noise folded about zero gives long crests; the square softens their foot.
            var fold = 1f - 2f * MathF.Abs(Noise.Fractal(u * 0.7f, v * 0.7f, Noise.Mix(_bumpSalt), bumps.Octaves, 2f, bumps.Gain));
            n += (fold * MathF.Abs(fold) - n) * bumps.Banks;
        }
        if (bumps.Cover < 1f)
        {
            // The noise is roughly even over -0.3..0.3, so this threshold leaves about the cover's share above it.
            var stretch = Noise.Fractal(x / bumps.CoverScale, z / bumps.CoverScale, Noise.Mix(_bumpSalt ^ 0xC0BEu), 2, 2f, 0.5f);
            n *= Noise.SmoothStep(-0.08f, 0.08f, stretch - 0.6f * (0.5f - bumps.Cover));
        }
        return h + bumps.Height * sample.Bumps * keep * n;
    }

    /// <summary>Ground height before roads cut and fill it: what the road router plans on.</summary>
    internal float GroundBeforeRoads(float x, float z) => ApplyPois(x, z, RawHeight(x, z));

    /// <summary>The biome that dominates a world position.</summary>
    public Biome BiomeAt(float x, float z) => Data.Biomes[SampleBiomes(x, z).Biome];

    /// <summary>
    /// Ground slope (rise over run) at a world position, from central differences over one
    /// <see cref="TerrainDefinition.Resolution"/>.
    /// </summary>
    public float SlopeAt(float x, float z)
    {
        var r = _terrain.Resolution;
        var dx = (HeightAt(x + r, z) - HeightAt(x - r, z)) / (2f * r);
        var dz = (HeightAt(x, z + r) - HeightAt(x, z - r)) / (2f * r);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>The POIs whose footprint covers a world position.</summary>
    public IEnumerable<PoiInstance> PoisAt(float x, float z) =>
        PoisNear(x, z).Where(p => Vector2.Distance(p.Centre, new Vector2(x, z)) < p.Radius);

    /// <summary>Generates one chunk of a square chunk grid with side <paramref name="chunkSize"/>.</summary>
    /// <exception cref="ArgumentException">The terrain resolution does not divide <paramref name="chunkSize"/>.</exception>
    public TerrainChunkData GenerateChunk(ChunkCoord coord, float chunkSize)
    {
        var res = _terrain.Resolution;
        var cells = (int)MathF.Round(chunkSize / res);
        if (cells < 1 || MathF.Abs(cells * res - chunkSize) > 1e-4f)
            throw new ArgumentException($"Terrain resolution {res} m must divide the chunk size {chunkSize} m.", nameof(chunkSize));
        var n = cells + 1;
        var (ox, oz) = coord.Origin(chunkSize);

        // Heights with a one-sample border, so normals at the chunk edge match the neighbour's. Samples
        // are independent and the generator is read-only, so rows run in parallel.
        var m = n + 2;
        var bordered = new float[m * m];
        var heights = new float[n * n];
        var colors = new Vector3[n * n];
        var biomes = new int[n * n];
        var covered = new bool[n * n];
        var layers = TerrainChunkData.GroundLayers;
        var grounds = Data.Grounds.Count > 0 ? new float[n * n * layers] : [];
        Parallel.For(0, m, j =>
        {
            Span<float> weights = stackalloc float[Data.Biomes.Count];
            for (var i = 0; i < m; i++)
            {
                float x = ox + (i - 1) * res, z = oz + (j - 1) * res;
                weights.Clear();
                var sample = SampleBiomes(x, z, weights);
                var h = Shaped(x, z, sample);
                bordered[j * m + i] = h;
                if (i >= 1 && i <= n && j >= 1 && j <= n)
                {
                    heights[(j - 1) * n + i - 1] = h;
                    colors[(j - 1) * n + i - 1] = sample.Color;
                    biomes[(j - 1) * n + i - 1] = MixedBiome(x, z, weights, sample.Biome);
                    covered[(j - 1) * n + i - 1] = PoisAt(x, z).Any() || Roads.Covers(x, z);
                    if (grounds.Length > 0)
                    {
                        var g = ((j - 1) * n + i - 1) * layers;
                        for (var b = 0; b < weights.Length; b++)
                        {
                            if (Data.BiomeGrounds[b] >= 0)
                                grounds[g + Data.BiomeGrounds[b]] += weights[b];
                        }
                        PaintPatches(grounds, g, weights, x, z);
                    }
                }
            }
        });
        var normals = new Vector3[n * n];
        for (var j = 0; j < n; j++)
        {
            for (var i = 0; i < n; i++)
            {
                var c = (j + 1) * m + i + 1;
                var normal = new Vector3(bordered[c - 1] - bordered[c + 1], 2f * res, bordered[c - m] - bordered[c + m]);
                normals[j * n + i] = Vector3.Normalize(normal);
            }
        }

        float x1 = ox + chunkSize, z1 = oz + chunkSize;
        if (grounds.Length > 0)
            PaintGrounds(grounds, normals, n, res, ox, oz, x1, z1);
        var objects = ObjectsIn(ox, oz, x1, z1).ToList();
        var clearers = FloraClearersIn(ox, oz, x1, z1);
        var pieces = new List<PoiPiecePlacement>();
        var markers = new List<PoiMarkerPlacement>();
        var pools = new List<PoiPoolPlacement>();
        var models = new List<PoiModelPlacement>();
        foreach (var poi in PoisOverlapping(ox, oz, x1, z1))
        {
            pools.AddRange(PoolsOf(poi).Where(p => Inside(p.Centre, ox, oz, x1, z1)));
            var prefab = poi.Poi.Prefab;
            for (var k = 0; k < (prefab.Pieces?.Count ?? 0); k++)
            {
                var piece = prefab.Pieces![k];
                var at = poi.ToWorld(ToVector(piece.At));
                if (Inside(at, ox, oz, x1, z1))
                    pieces.Add(new PoiPiecePlacement(poi, k, piece, at, poi.Yaw + Radians(piece.Yaw)));
            }
            for (var k = 0; k < (prefab.Models?.Count ?? 0); k++)
            {
                var model = prefab.Models![k];
                var at = poi.ToWorld(ToVector(model.At));
                if (Inside(at, ox, oz, x1, z1))
                    models.Add(new PoiModelPlacement(poi, k, model, Data.ModelFileOf(poi.Poi, model), at, poi.Yaw + Radians(model.Yaw)));
            }
            for (var k = 0; k < (prefab.Markers?.Count ?? 0); k++)
            {
                var marker = prefab.Markers![k];
                var at = poi.ToWorld(ToVector(marker.At));
                if (Inside(at, ox, oz, x1, z1))
                    markers.Add(new PoiMarkerPlacement(poi, marker, at, poi.Yaw + Radians(marker.Yaw)) { Index = k });
            }
        }
        return new TerrainChunkData(coord, chunkSize, res, n, heights, normals, colors, biomes, covered, SeaLevel, objects, pieces, markers)
        {
            Grounds = grounds,
            Pools = pools,
            Models = models,
            Roads = RoadMesher.Build(this, ox, oz, x1, z1),
            FloraClearers = clearers,
        };
    }

    /// <summary>
    /// The scattered objects that clear flora (<see cref="WorldObjectDefinition.ClearFlora"/>, such as crop field patches)
    /// and reach into [x0, x1) × [z0, z1), wherever their own ground point or their scene's spot lies. Only the rules
    /// that place such objects are looked at, so this is cheap where there are none.
    /// </summary>
    public List<WorldObjectPlacement> FloraClearersIn(float x0, float z0, float x1, float z1)
    {
        var found = new List<WorldObjectPlacement>();
        foreach (var rule in _clearingRules)
        {
            // A scene's pieces stand within its footprint of its spot, and reach a little further themselves.
            var margin = rule.Object.Footprint + ClearMargin;
            foreach (var placed in ScatteredIn(x0 - margin, z0 - margin, x1 + margin, z1 + margin, [rule]))
            {
                var reach = ClearReach(placed);
                if (placed.Definition.ClearFlora && placed.Position.X + reach >= x0 && placed.Position.X - reach < x1
                    && placed.Position.Z + reach >= z0 && placed.Position.Z - reach < z1)
                    found.Add(placed);
            }
        }
        return found;
    }

    /// <summary>
    /// Whether a flora-clearing object covers a world position: inside its box collider (turned and scaled with it), or
    /// inside its footprint when its collider is not a box.
    /// </summary>
    public static bool Clears(WorldObjectPlacement placed, float x, float z)
    {
        var o = placed.Definition;
        var dx = x - placed.Position.X;
        var dz = z - placed.Position.Z;
        if (o.Collider is not { Shape: PrimitiveShape.Box, Size: { } size } box)
            return dx * dx + dz * dz <= o.Footprint * placed.Scale * o.Footprint * placed.Scale;
        // Into the object's own frame: undo its turn about +Y.
        var local = Vector3.Transform(new Vector3(dx, 0f, dz), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -placed.Yaw)) / placed.Scale;
        return MathF.Abs(local.X - box.At.X) <= size.X / 2f && MathF.Abs(local.Z - box.At.Z) <= size.Z / 2f;
    }

    /// <summary>Furthest a flora-clearing object reaches from its ground point (<see cref="Clears"/> is false beyond it).</summary>
    public static float ClearReach(WorldObjectPlacement placed) =>
        placed.Definition.Collider is { Shape: PrimitiveShape.Box, Size: { } size } box
            ? (MathF.Sqrt(size.X * size.X + size.Z * size.Z) / 2f + MathF.Sqrt(box.At.X * box.At.X + box.At.Z * box.At.Z)) * placed.Scale
            : placed.Definition.Footprint * placed.Scale;

    // How far past a clearing rule's footprint its objects may reach (an 8 m field patch at a scene's edge).
    private const float ClearMargin = 8f;

    /// <summary>Every marker of <paramref name="type"/> in every placed POI, in POI then marker order.</summary>
    public IEnumerable<PoiMarkerPlacement> MarkersOfType(string type)
    {
        foreach (var poi in _pois)
        {
            for (var k = 0; k < (poi.Poi.Prefab.Markers?.Count ?? 0); k++)
            {
                var marker = poi.Poi.Prefab.Markers![k];
                if (marker.Type == type)
                    yield return new PoiMarkerPlacement(poi, marker, poi.ToWorld(ToVector(marker.At)), poi.Yaw + Radians(marker.Yaw)) { Index = k };
            }
        }
    }

    /// <summary>
    /// Objects whose ground point lies in [x0, x1) × [z0, z1): scattered ones in rule then cell order, then the ones POI
    /// <c>object</c> markers place, in POI then marker order.
    /// </summary>
    public IEnumerable<WorldObjectPlacement> ObjectsIn(float x0, float z0, float x1, float z1) =>
        ScatteredIn(x0, z0, x1, z1).Concat(PoiObjectsIn(x0, z0, x1, z1));

    /// <summary>The id an object was placed as: its scene's for a scene's piece, else its own.</summary>
    public string SourceOf(WorldObjectPlacement placed) =>
        Data.ScatterRuleOf(placed.Id) is { } rule ? rule.Object.Id! : placed.Definition.Id!;

    private IEnumerable<WorldObjectPlacement> PoiObjectsIn(float x0, float z0, float x1, float z1)
    {
        foreach (var poi in PoisOverlapping(x0, z0, x1, z1))
        {
            for (var k = 0; k < (poi.Poi.Prefab.Markers?.Count ?? 0); k++)
            {
                if (PlacePoiObject(poi, k) is { } placed && placed.Position.X >= x0 && placed.Position.X < x1 && placed.Position.Z >= z0 && placed.Position.Z < z1)
                    yield return placed;
            }
        }
    }

    // The object a POI's object marker places: on the ground under the marker, raised by the marker's height and sunk by
    // the object's sink. Null for any other marker, or one naming no object.
    private WorldObjectPlacement? PlacePoiObject(PoiInstance poi, int marker)
    {
        if (poi.Poi.Prefab.Markers![marker] is not { Type: PoiMarker.ObjectType, Ref: { } id } m || !Data.Objects.TryGetValue(id, out var o))
            return null;
        var objectId = WorldObjectId.ForPoi(poi.Index, marker);
        var salt = Noise.Hash(_scatterSalt ^ 0x9015u, poi.Index, marker, 3);
        var scale = o.Scale is [var lo, var hi] ? lo + (hi - lo) * Noise.Unit(Noise.Mix(salt)) : 1f;
        var at = poi.ToWorld(new Vector2(m.At.X, m.At.Z));
        var y = HeightAt(at.X, at.Y) + m.At.Y - o.Sink * scale;
        return new WorldObjectPlacement(objectId, o, new Vector3(at.X, y, at.Y), poi.Yaw + Radians(m.Yaw), scale);
    }

    /// <summary>The liquid at <paramref name="point"/> (under a POI pool's surface), or null.</summary>
    public PoiPoolPlacement? LiquidAt(Vector3 point) =>
        PoolsAt(point.X, point.Z).FirstOrDefault(p => p.Holds(point) && HeightAt(point.X, point.Z) < p.Centre.Y);

    /// <summary>
    /// Height of the water surface over a world position: a pond's (a POI pool of plain water, <see cref="LiquidDefinition.Water"/>)
    /// where one covers it, else the sea's. The water is only where the ground is lower.
    /// </summary>
    public float WaterLevelAt(float x, float z)
    {
        foreach (var pool in PoolsAt(x, z))
        {
            if (pool.Liquid.Water && pool.Holds(new Vector3(x, pool.Centre.Y, z)))
                return MathF.Max(pool.Centre.Y, SeaLevel);
        }
        return SeaLevel;
    }

    /// <summary>
    /// The body of water over a world position, for fishing and anything else that cares which water it is and how deep:
    /// a pond (a POI pool of plain water, named by its POI), a river or creek, or open water at sea level (a lake or the
    /// sea, named by the biome it lies in). Null on dry ground.
    /// </summary>
    public WaterBody? WaterBodyAt(float x, float z)
    {
        var ground = HeightAt(x, z);
        foreach (var pool in PoolsAt(x, z))
        {
            if (pool.Liquid.Water && pool.Centre.Y > SeaLevel && pool.Holds(new Vector3(x, pool.Centre.Y, z)) && ground < pool.Centre.Y)
                return new WaterBody(WaterBodyKind.Pond, pool.Poi.Poi.QualifiedId, pool.Centre.Y, pool.Centre.Y - ground);
        }
        if (ground >= SeaLevel)
            return null;
        return RiverDistance(x, z) <= 0f
            ? new WaterBody(WaterBodyKind.River, "river", SeaLevel, SeaLevel - ground)
            : new WaterBody(WaterBodyKind.Open, BiomeAt(x, z).Id, SeaLevel, SeaLevel - ground);
    }

    /// <summary>The liquid pools of the POIs whose footprint covers a world position.</summary>
    public IEnumerable<PoiPoolPlacement> PoolsAt(float x, float z) => PoisAt(x, z).SelectMany(PoolsOf);

    // A POI's liquid pools in world space (pools of liquids the world has no rules for are left out).
    private IEnumerable<PoiPoolPlacement> PoolsOf(PoiInstance poi)
    {
        foreach (var pool in poi.Poi.Prefab.Pools ?? [])
        {
            if (pool.Liquid is not { } id || !Data.Liquids.TryGetValue(id, out var liquid))
                continue;
            var at = poi.ToWorld(pool.At is [var ax, var az] ? new Vector2(ax, az) : Vector2.Zero);
            yield return new PoiPoolPlacement(poi, pool, liquid, new Vector3(at.X, poi.PadHeight + pool.Level, at.Y));
        }
    }

    private IEnumerable<WorldObjectPlacement> ScatteredIn(float x0, float z0, float x1, float z1, IReadOnlyList<Scatter>? rules = null)
    {
        // A perched rule's spot takes a search, and every later rule's spots nearby ask for it again.
        var perched = new Dictionary<(int Rule, int X, int Z), Vector2>();
        foreach (var rule in rules ?? Data.ScatterRules)
        {
            var s = rule.Spacing;
            int ix0 = (int)MathF.Floor(x0 / s), ix1 = (int)MathF.Floor(x1 / s);
            int iz0 = (int)MathF.Floor(z0 / s), iz1 = (int)MathF.Floor(z1 / s);
            for (var iz = iz0; iz <= iz1; iz++)
            {
                for (var ix = ix0; ix <= ix1; ix++)
                {
                    if (rule.Face is not null)
                    {
                        if (FaceSpot(rule, ix, iz) is { } foot && foot.X >= x0 && foot.X < x1 && foot.Y >= z0 && foot.Y < z1 && !Crowded(rule, foot, perched))
                        {
                            foreach (var placement in Place(rule, ix, iz, foot))
                                yield return placement;
                        }
                        continue;
                    }
                    if (!Candidate(rule, ix, iz, out var spot, perched) || spot.X < x0 || spot.X >= x1 || spot.Y < z0 || spot.Y >= z1)
                        continue;
                    if (Fits(rule, spot) && !Crowded(rule, spot, perched))
                    {
                        foreach (var placement in Place(rule, ix, iz, spot))
                            yield return placement;
                    }
                }
            }
        }
    }

    /// <summary>The object with this id, or null when the world has none (it never grew there).</summary>
    public WorldObjectPlacement? Find(WorldObjectId id)
    {
        if (id.IsPoi)
            return id.CellX >= 0 && id.CellX < _pois.Count && id.CellZ >= 0 && id.CellZ < (_pois[id.CellX].Poi.Prefab.Markers?.Count ?? 0) && id.Piece == 0
                ? PlacePoiObject(_pois[id.CellX], id.CellZ)
                : null;
        return Data.ScatterRuleOf(id) is { } rule ? InCell(rule, id.CellX, id.CellZ).FirstOrDefault(p => p.Id.Piece == id.Piece) : null;
    }

    /// <summary>
    /// What a scatter rule places in one of its cells: its object, a scene's pieces or a rock deposit's blocks; nothing when
    /// the cell holds none.
    /// </summary>
    public IEnumerable<WorldObjectPlacement> InCell(Scatter rule, int cellX, int cellZ)
    {
        if (rule.Face is not null)
            return FaceSpot(rule, cellX, cellZ) is { } foot && !Crowded(rule, foot) ? Place(rule, cellX, cellZ, foot) : [];
        return Candidate(rule, cellX, cellZ, out var spot) && Fits(rule, spot) && !Crowded(rule, spot) ? Place(rule, cellX, cellZ, spot) : [];
    }

    // Face rules' fitting spots by cell. Finding a face's foot takes dozens of height samples and every later rule's
    // crowding check asks for the same cells again, so the answers are kept (they never change), up to FaceCacheSize.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int Rule, int X, int Z), Vector2?> _faceSpots = new();
    private const int FaceCacheSize = 200_000;
    private int _faceSpotCount;

    private Vector2? FaceSpot(Scatter rule, int ix, int iz)
    {
        if (_faceSpots.TryGetValue((rule.Index, ix, iz), out var known))
            return known;
        Vector2? found = Candidate(rule, ix, iz, out var spot) && Fits(rule, spot) ? spot : null;
        // Counted by hand: a concurrent dictionary's Count takes every one of its locks.
        if (_faceSpots.TryAdd((rule.Index, ix, iz), found) && Interlocked.Increment(ref _faceSpotCount) >= FaceCacheSize)
        {
            _faceSpots.Clear();
            Interlocked.Exchange(ref _faceSpotCount, 0);
        }
        return found;
    }

    // The object a scatter spot holds: one, or every cube of a rock outcrop.
    private IEnumerable<WorldObjectPlacement> Place(Scatter rule, int ix, int iz, Vector2 spot)
    {
        var o = rule.Object;
        var salt = Noise.Hash(_scatterSalt ^ (uint)rule.Key, ix, iz, 3);
        var scale = o.Scale is [var lo, var hi] ? lo + (hi - lo) * Noise.Unit(Noise.Mix(salt)) : 1f;
        var yaw = rule.Roadside is { } roadside && Roads.Nearest(spot.X, spot.Y, RoadReach, roadside.Road) is { } road
            ? MathF.Atan2(road.Point.X - spot.X, road.Point.Y - spot.Y)
            : rule.Face is not null && Uphill(spot) is { } up
            ? MathF.Atan2(-up.X, -up.Y)
            : Noise.Unit(salt) * 2f * MathF.PI;
        if (o.Scene is { } scene)
        {
            foreach (var piece in PlaceScene(rule, scene, ix, iz, spot, yaw, salt))
                yield return piece;
            yield break;
        }
        if (o.Outcrop is not { } outcrop)
        {
            var y = (rule.Water == ScatterWater.Surface ? WaterLevelAt(spot.X, spot.Y) : HeightAt(spot.X, spot.Y)) - o.Sink * scale;
            // Rock along a face picks among its models, so a row of walls never repeats.
            var variant = rule.Face is not null && o.Mesh?.Variants is { Count: > 0 } variants ? (int)(Noise.Mix(salt ^ 0xFACEu) % (uint)(variants.Count + 1)) : 0;
            yield return new WorldObjectPlacement(new WorldObjectId(rule.Key, ix, iz), o, new Vector3(spot.X, y, spot.Y), yaw, scale, variant);
            yield break;
        }
        // A rock deposit: mounds of blocks turned by the deposit's yaw, each mound stood on the lowest ground under
        // its own bottom blocks (sunk by sink) so none of them floats on a slope.
        var blocks = OutcropLayout.Blocks(outcrop, BlockSize, BlockVariants, o.Footprint, Noise.Mix(salt ^ 0x5EEDu));
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        Vector2 Ground(Vector3 local)
        {
            var w = Vector3.Transform(local * scale, turn);
            return new Vector2(spot.X + w.X, spot.Y + w.Z);
        }
        var floors = new Dictionary<int, float>();
        foreach (var b in blocks.Where(b => b.Bottom.Y == 0f))
        {
            foreach (var (dx, dz) in Corners)
            {
                var g = Ground(b.Bottom + new Vector3(dx * b.Half.X, 0, dz * b.Half.Y));
                floors[b.Mound] = MathF.Min(floors.GetValueOrDefault(b.Mound, float.MaxValue), HeightAt(g.X, g.Y));
            }
        }
        for (var k = 0; k < blocks.Count; k++)
        {
            var b = blocks[k];
            var definition = Data.Objects[b.Object];
            var g = Ground(b.Bottom);
            var y = floors.GetValueOrDefault(b.Mound, HeightAt(g.X, g.Y)) - definition.Sink * scale + b.Bottom.Y * scale;
            yield return new WorldObjectPlacement(new WorldObjectId(rule.Key, ix, iz, k), definition, new Vector3(g.X, y, g.Y), yaw + b.Yaw, scale, b.Variant);
        }
    }

    // A scene's pieces: a layout picked by weight, turned by the scene's yaw, each piece kept by its chance and standing on
    // the ground under it. A piece that would land on a road, a POI or in water is left out; ids keep the piece's place in
    // the layout, so the rest never change.
    private List<WorldObjectPlacement> PlaceScene(Scatter rule, SceneDefinition scene, int ix, int iz, Vector2 spot, float yaw, uint salt)
    {
        var layouts = scene.Layouts!;
        var pick = Noise.Unit(Noise.Mix(salt ^ 0x5CE9u)) * layouts.Sum(l => l.Weight);
        var layout = layouts[^1];
        foreach (var l in layouts)
        {
            pick -= l.Weight;
            if (pick < 0f)
            {
                layout = l;
                break;
            }
        }
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        var placed = new List<WorldObjectPlacement>();
        for (var k = 0; k < layout.Pieces!.Count; k++)
        {
            var piece = layout.Pieces[k];
            var roll = Noise.Hash(salt ^ 0x9E3Cu, k, 0, 4);
            if (Noise.Unit(roll) >= piece.Chance)
                continue;
            var definition = Data.Objects[piece.Object!];
            var w = Vector3.Transform(new Vector3(piece.At![0], 0f, piece.At[1]), turn);
            var g = new Vector2(spot.X + w.X, spot.Y + w.Z);
            if (!InWorld(g.X, g.Y) || Built(g) || Roads.Near(g.X, g.Y, definition.Footprint + 1f)
                || !Dry(g) || SlopeAt(g.X, g.Y) > MathF.Min(_terrain.MaxScatterSlope, definition.MaxSlope ?? float.MaxValue)
                || Covers(definition, g, yaw + piece.Yaw * MathF.PI / 180f) is not { } cover)
            {
                // A level scene (a field) is whole or not there at all: no hole where one patch would not lie flat.
                if (scene.Level is not null && definition.ClearFlora)
                    return [];
                continue;
            }
            var scale = definition.Scale is [var lo, var hi] ? lo + (hi - lo) * Noise.Unit(Noise.Mix(roll)) : 1f;
            var y = cover.Height - definition.Sink * scale;
            placed.Add(new WorldObjectPlacement(new WorldObjectId(rule.Key, ix, iz, k), definition, new Vector3(g.X, y, g.Y),
                yaw + piece.Yaw * MathF.PI / 180f, scale) { Tilt = cover.Tilt });
        }
        return placed;
    }

    // The ground height a scene's piece stands on and its lean, or null when the ground does not suit it. Most stand upright
    // on the ground at their middle. One that covers the ground (clears flora, a field patch) needs ground rising and falling
    // no more than its slope limit allows over half its box's diagonal, and lies along the plane that best fits the ground
    // round its box, lifted just enough that no sampled ground pokes through it, so it neither floats on its low side nor
    // sinks into its high side; its skirt hides what gap is left.
    private (float Height, Quaternion Tilt)? Covers(WorldObjectDefinition o, Vector2 at, float yaw)
    {
        var h = HeightAt(at.X, at.Y);
        if (!o.ClearFlora || o.Collider is not { Shape: PrimitiveShape.Box, Size: { } size })
            return (h, Quaternion.Identity);
        var slope = MathF.Min(_terrain.MaxScatterSlope, o.MaxSlope ?? float.MaxValue);
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        Span<Vector3> samples = stackalloc Vector3[Rim.Length + 1];
        samples[0] = new Vector3(0f, h, 0f);
        float highest = h, lowest = h;
        for (var i = 0; i < Rim.Length; i++)
        {
            var (dx, dz) = Rim[i];
            var w = Vector3.Transform(new Vector3(o.Collider.At.X + dx * size.X / 2f, 0f, o.Collider.At.Z + dz * size.Z / 2f), turn);
            var ground = HeightAt(at.X + w.X, at.Y + w.Z);
            samples[i + 1] = new Vector3(w.X, ground, w.Z);
            highest = MathF.Max(highest, ground);
            lowest = MathF.Min(lowest, ground);
        }
        if (highest - lowest > slope * MathF.Sqrt(size.X * size.X + size.Z * size.Z) / 2f)
            return null;
        var (height, gx, gz) = FitPlane(samples);
        var lift = 0f;
        foreach (var p in samples)
            lift = MathF.Max(lift, p.Y - (height + gx * p.X + gz * p.Z));
        var normal = Vector3.Normalize(new Vector3(-gx, 1f, -gz));
        var axis = Vector3.Cross(Vector3.UnitY, normal);
        var tilt = axis.LengthSquared() < 1e-12f
            ? Quaternion.Identity
            : Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(normal.Y, -1f, 1f)));
        return (height + lift, tilt);
    }

    // Least-squares plane y = height + gx x + gz z through points given relative to the plane's middle.
    private static (float Height, float Gx, float Gz) FitPlane(ReadOnlySpan<Vector3> points)
    {
        double n = points.Length, sx = 0, sz = 0, sy = 0, sxx = 0, szz = 0, sxz = 0, sxy = 0, szy = 0;
        foreach (var p in points)
        {
            sx += p.X;
            sz += p.Z;
            sy += p.Y;
            sxx += p.X * p.X;
            szz += p.Z * p.Z;
            sxz += p.X * p.Z;
            sxy += p.X * p.Y;
            szy += p.Z * p.Y;
        }
        // Centred sums, so the 3 x 3 system becomes a 2 x 2 one for the slopes.
        double mx = sx / n, mz = sz / n, my = sy / n;
        double cxx = sxx - n * mx * mx, czz = szz - n * mz * mz, cxz = sxz - n * mx * mz, cxy = sxy - n * mx * my, czy = szy - n * mz * my;
        var det = cxx * czz - cxz * cxz;
        if (Math.Abs(det) < 1e-9)
            return ((float)my, 0f, 0f);
        var gx = (cxy * czz - czy * cxz) / det;
        var gz = (czy * cxx - cxy * cxz) / det;
        return ((float)(my - gx * mx - gz * mz), (float)gx, (float)gz);
    }

    // A box's corners and the middles of its sides, in half sizes from its middle.
    private static readonly (float, float)[] Rim = [(-1, -1), (1, -1), (-1, 1), (1, 1), (-1, 0), (1, 0), (0, -1), (0, 1)];

    // A block's middle and its four bottom corners, in half sizes from its middle.
    private static readonly (float, float)[] Corners = [(0, 0), (-1, -1), (1, -1), (-1, 1), (1, 1)];

    // A deposit block object's collider box, or null when it has none.
    private Vector3? BlockSize(string id) =>
        Data.Objects.GetValueOrDefault(id)?.Collider is { Shape: PrimitiveShape.Box, Size: { } size } ? new Vector3(size.X, size.Y, size.Z) : null;

    // How many models a deposit block object has to pick from.
    private int BlockVariants(string id) => 1 + (Data.Objects.GetValueOrDefault(id)?.Mesh?.Variants?.Count ?? 0);

    // The scatter rule's spot in a cell, if the cell's dice roll keeps it.
    // A cell's random spot before any rule moves it, or null when the cell's roll leaves it empty.
    private Vector2? CellSpot(Scatter rule, int ix, int iz)
    {
        var salt = _scatterSalt ^ (uint)rule.Key;
        if (Noise.Unit(Noise.Hash(salt, ix, iz, 0)) >= rule.Chance)
            return null;
        var s = rule.Spacing;
        return new Vector2((ix + 0.1f + 0.8f * Noise.Unit(Noise.Hash(salt, ix, iz, 1))) * s, (iz + 0.1f + 0.8f * Noise.Unit(Noise.Hash(salt, ix, iz, 2))) * s);
    }

    private bool Candidate(Scatter rule, int ix, int iz, out Vector2 spot, Dictionary<(int Rule, int X, int Z), Vector2>? perched = null)
    {
        spot = default;
        var salt = _scatterSalt ^ (uint)rule.Key;
        if (Noise.Unit(Noise.Hash(salt, ix, iz, 0)) >= rule.Chance)
            return false;
        if (rule.Perch is not null && perched is not null)
        {
            if (!perched.TryGetValue((rule.Index, ix, iz), out var known))
            {
                Candidate(rule, ix, iz, out var found);
                perched[(rule.Index, ix, iz)] = known = found;
            }
            spot = known;
            return true;
        }
        var s = rule.Spacing;
        spot = new Vector2((ix + 0.1f + 0.8f * Noise.Unit(Noise.Hash(salt, ix, iz, 1))) * s, (iz + 0.1f + 0.8f * Noise.Unit(Noise.Hash(salt, ix, iz, 2))) * s);
        // A face's foot takes many height samples to find: only look for it in the rule's own biome. The cheap tests go
        // first, since every biome's face rules ask everywhere.
        if (rule.Face is { } face)
            return (!face.Sheer || NearSheer(spot)) && BiomeNear(spot, rule.Biome) && BiomeShare(spot.X, spot.Y, rule.Biome) > 0f && AtFoot(face, ref spot, out _);
        if (rule.Roadside is { } roadside)
        {
            if (roadside.Even)
                spot = new Vector2((ix + 0.5f) * s, (iz + 0.5f) * s);
            return BesideRoad(roadside, ix, iz, s, ref spot);
        }
        // A perched find looks round its cell for a cliff top or ledge, since most of the land is easy slopes.
        if (rule.Perch is { } perch && !Perched(spot, perch))
        {
            for (var k = 1; k < PerchTries; k++)
            {
                var other = new Vector2((ix + 0.1f + 0.8f * Noise.Unit(Noise.Hash(salt, ix, iz, 1 + 2 * k))) * s,
                    (iz + 0.1f + 0.8f * Noise.Unit(Noise.Hash(salt, ix, iz, 2 + 2 * k))) * s);
                if (Perched(other, perch))
                {
                    spot = other;
                    break;
                }
            }
        }
        return true;
    }

    // Spots a perched scatter rule tries in its cell.
    private const int PerchTries = 4;

    // Furthest a roadside rule looks for its road (the road network answers within 48 m).
    private const float RoadReach = 48f;

    // A roadside rule's spot: the centre-line point nearest the cell's spot, if it lies in the cell (so each stretch of road
    // is one cell's), moved out past the bed by the gap. Never on a bridge, or far above or below the road.
    private bool BesideRoad(RoadsideDefinition roadside, int ix, int iz, float s, ref Vector2 spot)
    {
        if (Roads.Nearest(spot.X, spot.Y, MathF.Min(s, RoadReach), roadside.Road) is not { } hit || hit.OnBridge
            || MathF.Floor(hit.Point.X / s) != ix || MathF.Floor(hit.Point.Y / s) != iz)
            return false;
        var chain = hit.Chain;
        var along = Vector2.Normalize(chain.Points[hit.Segment + 1] - chain.Points[hit.Segment]);
        var left = new Vector2(along.Y, -along.X);
        var at = chain.Along[hit.Segment] + hit.T * (chain.Along[hit.Segment + 1] - chain.Along[hit.Segment]);
        if (roadside.Junction is { } near
            && !(at <= near && Roads.Nodes[chain.From].Kind == RoadNodeKind.Junction)
            && !(chain.Length - at <= near && Roads.Nodes[chain.To].Kind == RoadNodeKind.Junction))
            return false;
        // The road's heading over the 20 m before the spot and the 20 m after it: how far it turns, and which way.
        var before = Vector2.Normalize(hit.Point - PointAlong(chain, MathF.Max(0f, at - BendReach)).Point + along * 1e-3f);
        var after = Vector2.Normalize(PointAlong(chain, MathF.Min(chain.Length, at + BendReach)).Point - hit.Point + along * 1e-3f);
        if (roadside.Bend is { } bend && !(MathF.Acos(Math.Clamp(Vector2.Dot(before, after), -1f, 1f)) >= bend * MathF.PI / 180f))
            return false;
        var side = roadside.Side switch
        {
            RoadSide.Left => 1f,
            RoadSide.Right => -1f,
            // Turning towards the left side puts the outside on the right.
            RoadSide.Outside => Vector2.Dot(after, new Vector2(before.Y, -before.X)) > 0f ? -1f : 1f,
            _ => Vector2.Dot(spot - hit.Point, left) >= 0f ? 1f : -1f,
        };
        spot = hit.Point + left * side * (Roads.HalfBed(chain) + roadside.Gap);
        return MathF.Abs(HeightAt(spot.X, spot.Y) - hit.Height) <= RoadsideRise + 1f;
    }

    // The way up a face from its foot: the slope's direction 3 m up it, where the face is steepest, so the foot's own
    // flattening never turns the object. Null on level ground.
    private Vector2? Uphill(Vector2 foot)
    {
        if (Gradient(foot) is not { } dir)
            return null;
        return Gradient(foot + dir * 3f) is { } face && Vector2.Dot(face, dir) > 0.5f ? face : dir;
    }

    // The direction the ground rises fastest at a point, from central differences over 2 m, or null where it is level.
    private Vector2? Gradient(Vector2 p)
    {
        var g = new Vector2(HeightAt(p.X + 2f, p.Y) - HeightAt(p.X - 2f, p.Y), HeightAt(p.X, p.Y + 2f) - HeightAt(p.X, p.Y - 2f)) / 4f;
        return g.Length() < 0.05f ? null : Vector2.Normalize(g);
    }

    // Slides a face rule's spot along the slope to the foot of the face above or below it, the last metre before the
    // ground climbs steeply, and says whether the face there climbs as the rule wants. The spot moves at most FootReach.
    private bool AtFoot(FaceDefinition face, ref Vector2 spot, out Vector2 up)
    {
        up = default;
        if (Gradient(spot) is not { } dir)
            return false;
        // Most of the land has no face in reach: two samples rule it out before anything finer.
        var low = spot - dir * FootReach;
        var high = spot + dir * (FootReach + face.Run);
        if (HeightAt(high.X, high.Y) - HeightAt(low.X, low.Y) < face.Rise)
            return false;
        // Then a coarse line of samples up the slope finds where it climbs enough within a run, if anywhere: a hillside
        // climbs over the whole line, a face within a run of it.
        const float step = 3f;
        var n = (int)((2f * FootReach + face.Run) / step) + 1;
        Span<float> line = stackalloc float[n];
        for (var k = 0; k < n; k++)
        {
            var p = low + dir * (k * step);
            line[k] = HeightAt(p.X, p.Y);
        }
        var span = (int)MathF.Ceiling(face.Run / step);
        var first = -1;
        for (var k = 0; k + span < n && k * step <= 2f * FootReach; k++)
        {
            if (line[k + span] - line[k] >= face.Rise)
            {
                first = k;
                break;
            }
        }
        if (first < 0)
            return false;
        // The foot lies within a step or two of where that run starts: heights every half metre from a metre below
        // the window to a metre above it.
        var t0 = MathF.Max(-FootReach, first * step - FootReach - step);
        var t1 = MathF.Min(FootReach, first * step - FootReach + 2f * step);
        var m = (int)MathF.Round((t1 - t0) / 0.5f) + 5;
        Span<float> fine = stackalloc float[m];
        for (var k = 0; k < m; k++)
        {
            var p = spot + dir * (t0 - 1f + k * 0.5f);
            fine[k] = HeightAt(p.X, p.Y);
        }
        for (var k = 2; k + 2 < m; k++)
        {
            var t = t0 - 1f + k * 0.5f;
            var p = spot + dir * t;
            var h = fine[k];
            // Steep just above, gentle just below: the foot.
            if (fine[k + 2] - h < 0.6f || h - fine[k - 2] > 0.45f)
                continue;
            var rise = HeightAt(p.X + dir.X * face.Run, p.Y + dir.Y * face.Run) - h;
            if (rise < face.Rise || rise > (face.Max ?? float.MaxValue))
                return false;
            spot = p;
            up = dir;
            return true;
        }
        return false;
    }

    // Furthest a face rule's spot slides to find the foot of its face.
    private const float FootReach = 8f;

    // Which biomes have ground in or near each BiomeTile-metre tile, as bits, from a 3 by 3 grid of samples a little
    // wider than the tile: face rules from every biome ask everywhere, and a biome sample is dear.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int X, int Z), ulong> _biomeTiles = new();
    private const float BiomeTile = 32f;
    private int _biomeTileCount;

    private bool BiomeNear(Vector2 spot, int biome)
    {
        if (Data.Biomes.Count > 64)
            return true;
        var key = ((int)MathF.Floor(spot.X / BiomeTile), (int)MathF.Floor(spot.Y / BiomeTile));
        if (!_biomeTiles.TryGetValue(key, out var bits))
        {
            Span<float> weights = stackalloc float[Data.Biomes.Count];
            for (var j = 0; j < 3; j++)
            {
                for (var i = 0; i < 3; i++)
                {
                    SampleBiomes((key.Item1 + i * 0.5f) * BiomeTile + (i - 1) * 8f, (key.Item2 + j * 0.5f) * BiomeTile + (j - 1) * 8f, weights);
                    for (var b = 0; b < weights.Length; b++)
                    {
                        if (weights[b] > 0f)
                            bits |= 1ul << b;
                    }
                }
            }
            if (_biomeTiles.TryAdd(key, bits) && Interlocked.Increment(ref _biomeTileCount) >= FaceCacheSize)
            {
                _biomeTiles.Clear();
                Interlocked.Exchange(ref _biomeTileCount, 0);
            }
        }
        return (bits & (1ul << biome)) != 0;
    }

    // Metres before and after a roadside spot over which the road's bend is measured.
    private const float BendReach = 20f;

    // Whether a structure stands on a spot: a POI's footprint, unless the POI is a wild one (a pond, a ravine) with no
    // models. A wild POI with a model (a rock arch) grows its biome's plants but keeps scattered objects out of the rock.
    private bool Built(Vector2 spot) => PoisAt(spot.X, spot.Y).Any(p => !p.Poi.Prefab.Wild || p.Poi.Prefab.Models is { Count: > 0 });

    // Whether a spot is dry land: above the sea and any pond.
    private bool Dry(Vector2 spot) => HeightAt(spot.X, spot.Y) >= WaterLevelAt(spot.X, spot.Y) + 0.2f;

    // Whether a water rule's spot is wet the way it wants: deep enough to float on, or in the shallows at the edge.
    private bool Wet(ScatterWater water, Vector2 spot)
    {
        var depth = WaterLevelAt(spot.X, spot.Y) - HeightAt(spot.X, spot.Y);
        return water == ScatterWater.Surface ? depth is >= 0.5f and <= 3f : depth is >= -0.3f and <= 0.6f;
    }

    // Whether the ground at a spot suits the rule, ignoring other objects.
    private bool Fits(Scatter rule, Vector2 spot)
    {
        if (!InWorld(spot.X, spot.Y))
            return false;
        // Patches first: they turn most of a clustered rule's spots away for the price of one noise sample.
        if (rule.Cluster is { } cluster && !InCluster(rule, cluster, spot))
            return false;
        // Across a border a biome's objects thin out with its share of the ground, in groves rather than one by one.
        if (BiomeShare(spot.X, spot.Y, rule.Biome) <= EdgeRoll(spot.X, spot.Y, _edgeSalt ^ (uint)rule.Key, GroveSize))
            return false;
        // A roadside scene's pieces each keep off the road on their own, so only its spot has to.
        var keep = rule.Roadside is not null && rule.Object.Scene is not null ? 0f : rule.Object.Footprint;
        if (Built(spot) || Roads.Near(spot.X, spot.Y, keep + 1f))
            return false;
        if (rule.Water is { } water)
            return Wet(water, spot) && (water == ScatterWater.Surface || SlopeAt(spot.X, spot.Y) <= MathF.Min(_terrain.MaxScatterSlope, rule.Object.MaxSlope ?? float.MaxValue));
        if (!Dry(spot) || SlopeAt(spot.X, spot.Y) > MathF.Min(rule.Face is null ? _terrain.MaxScatterSlope : float.MaxValue, rule.Object.MaxSlope ?? float.MaxValue))
            return false;
        // A face's rock reaches back up the face and over its top: none of that may be a road, a structure or water.
        if (rule.Face is { } face && Uphill(spot) is { } up)
        {
            for (var d = 2f; d <= face.Run; d += 3f)
            {
                var p = spot + up * d;
                if (!InWorld(p.X, p.Y) || Built(p) || Roads.Near(p.X, p.Y, 1f) || !Dry(p))
                    return false;
            }
        }
        if (rule.Perch is { } perch && !Perched(spot, perch))
            return false;
        if (rule.Object.Scene is { Level: { } level } && !Level(spot, rule.Object.Footprint, level))
            return false;
        if (rule.Near is { } near && !Near(rule, near, spot))
            return false;
        if (rule.AwayPois is { } away && _pois.Any(p => away.Contains(p.Poi.QualifiedId)
                && Vector2.Distance(p.Centre, spot) < p.Radius + rule.Away!.Distance + rule.Object.Footprint))
            return false;
        // A rock outcrop spreads its cubes over its footprint, so the whole of it must suit, not just its middle.
        if (rule.Object.Outcrop is not null)
        {
            var reach = rule.Object.Footprint * (rule.Object.Scale is [_, var hi] ? hi : 1f);
            for (var a = 0; a < 8; a++)
            {
                var p = spot + new Vector2(MathF.Cos(a * MathF.PI / 4f), MathF.Sin(a * MathF.PI / 4f)) * reach;
                if (!InWorld(p.X, p.Y) || BiomeShare(p.X, p.Y, rule.Biome) <= 0f
                    || Built(p) || Roads.Near(p.X, p.Y, 1f) || !Dry(p))
                    return false;
            }
        }
        return true;
    }

    // Whether a spot of a rule scattering one of the near objects lies within reach, on ground that suits it. Only its
    // ground counts, not what crowds it, so the answer never depends on rules placed after the near one.
    private bool Near(Scatter rule, NearDefinition near, Vector2 spot)
    {
        if (rule.NearPois is { } pois && _pois.Any(p => pois.Contains(p.Poi.QualifiedId) && Vector2.Distance(p.Centre, spot) <= p.Radius + near.Within))
            return true;
        foreach (var other in Data.ScatterRules)
        {
            if (other == rule || near.Objects?.Contains(other.Object.Id!) != true)
                continue;
            var s = other.Spacing;
            for (var iz = (int)MathF.Floor((spot.Y - near.Within) / s); iz <= (int)MathF.Floor((spot.Y + near.Within) / s); iz++)
            {
                for (var ix = (int)MathF.Floor((spot.X - near.Within) / s); ix <= (int)MathF.Floor((spot.X + near.Within) / s); ix++)
                {
                    if (Candidate(other, ix, iz, out var p) && Vector2.Distance(p, spot) <= near.Within && Fits(other, p))
                        return true;
                }
            }
        }
        return false;
    }

    // Whether the ground round a spot, out to a radius, lies within a height of the spot's own.
    private bool Level(Vector2 spot, float radius, float level)
    {
        var h = HeightAt(spot.X, spot.Y);
        for (var a = 0; a < 16; a++)
        {
            var r = a < 8 ? radius : radius / 2f;
            var p = spot + new Vector2(MathF.Cos(a * MathF.PI / 4f + (a < 8 ? 0f : MathF.PI / 8f)), MathF.Sin(a * MathF.PI / 4f + (a < 8 ? 0f : MathF.PI / 8f))) * r;
            if (MathF.Abs(HeightAt(p.X, p.Y) - h) > level)
                return false;
        }
        return true;
    }

    // Whether a clustered rule keeps a spot: always inside its layer's patches, and only Outside of the time elsewhere.
    private bool InCluster(Scatter rule, ClusterDefinition cluster, Vector2 spot)
    {
        var layer = cluster.Layer is { } name ? Noise.Hash(name) : 0xC105u ^ (uint)rule.Key * 0x9E3779B1u;
        if (ClusterRoll(spot.X, spot.Y, _clusterSalt ^ layer, cluster.Scale) < cluster.Cover)
            return true;
        return cluster.Outside > 0f
            && Noise.Unit(Noise.Hash(_clusterSalt ^ (uint)rule.Key, (int)MathF.Floor(spot.X * 8f), (int)MathF.Floor(spot.Y * 8f), 7)) < cluster.Outside;
    }

    // A roll spread roughly evenly over 0..1 that changes slowly across the ground, so the spots under a cover form solid
    // patches the size of the scale; a little dice per spot frays their edges.
    private static float ClusterRoll(float x, float z, uint salt, float size)
    {
        var smooth = 1f / (1f + MathF.Exp(-8f * Noise.Fractal(x / size, z / size, salt, 2, 2.1f, 0.5f)));
        var dice = Noise.Unit(Noise.Hash(Noise.Mix(salt), (int)MathF.Floor(x * 4f), (int)MathF.Floor(z * 4f)));
        return 0.88f * smooth + 0.12f * dice;
    }

    // Whether a spot stands high above the ground round it (a cliff top, a ledge): rarer finds sit where they take a climb.
    private bool Perched(Vector2 spot, PerchDefinition perch)
    {
        var lowest = float.MaxValue;
        for (var a = 0; a < 8; a++)
        {
            var p = spot + new Vector2(MathF.Cos(a * MathF.PI / 4f), MathF.Sin(a * MathF.PI / 4f)) * perch.Within;
            lowest = MathF.Min(lowest, HeightAt(p.X, p.Y));
        }
        return HeightAt(spot.X, spot.Y) - lowest >= perch.Rise;
    }

    // Whether an earlier rule's fitting candidate sits too close. Earlier rules win, so a rule added after the others never
    // takes an existing object's place.
    private bool Crowded(Scatter rule, Vector2 spot, Dictionary<(int Rule, int X, int Z), Vector2>? perched = null)
    {
        for (var r = 0; r < rule.Index; r++)
        {
            var other = Data.ScatterRules[r];
            var reach = rule.Object.Footprint + other.Object.Footprint;
            var s = other.Spacing;
            for (var iz = (int)MathF.Floor((spot.Y - reach) / s); iz <= (int)MathF.Floor((spot.Y + reach) / s); iz++)
            {
                for (var ix = (int)MathF.Floor((spot.X - reach) / s); ix <= (int)MathF.Floor((spot.X + reach) / s); ix++)
                {
                    if (other.Face is not null)
                    {
                        // The foot is at most FootReach from the cell's spot: rule out far cells before looking for it.
                        if (CellSpot(other, ix, iz) is { } raw && Vector2.Distance(raw, spot) < reach + FootReach
                            && FaceSpot(other, ix, iz) is { } foot && Vector2.Distance(foot, spot) < reach)
                            return true;
                        continue;
                    }
                    if (Candidate(other, ix, iz, out var near, perched) && Vector2.Distance(near, spot) < reach && Fits(other, near))
                        return true;
                }
            }
        }
        return false;
    }

    private struct BiomeSample
    {
        public float Height;
        public float Relief;
        public float Ridge;
        public float TerraceStep;
        public float TerraceWeight;
        public float Hills;
        public float Bumps;
        public float Patch;
        public Vector3 Color;
        public int Biome;
    }

    // Steep slopes take the slope ground, then POIs paint theirs over the top, in order.
    private void PaintGrounds(float[] grounds, Vector3[] normals, int n, float res, float ox, float oz, float x1, float z1)
    {
        var layers = TerrainChunkData.GroundLayers;
        var slope = Data.Definition.SlopeGround;
        var paints = new List<(int Ground, Vector3 At, GroundPaint Paint, uint Salt)>();
        foreach (var poi in PoisOverlapping(ox, oz, x1, z1))
        {
            for (var k = 0; k < (poi.Poi.Prefab.Ground?.Count ?? 0); k++)
            {
                var paint = poi.Poi.Prefab.Ground![k];
                var at = paint.At is [var px, var pz] ? new Vector3(px, 0f, pz) : Vector3.Zero;
                paints.Add((Data.GroundIndex(paint.Ground!), poi.ToWorld(at), paint, Noise.Hash(_paintSalt, poi.Index, k, 0)));
            }
        }
        for (var j = 0; j < n; j++)
        {
            for (var i = 0; i < n; i++)
            {
                var k = j * n + i;
                var g = k * layers;
                if (Data.SlopeGround >= 0 && slope is not null)
                {
                    var up = normals[k].Y;
                    var steep = MathF.Sqrt(MathF.Max(0f, 1f - up * up)) / MathF.Max(up, 1e-3f);
                    Over(grounds, g, Data.SlopeGround, Noise.SmoothStep(slope.From, slope.To, steep));
                }
                float x = ox + i * res, z = oz + j * res;
                foreach (var (ground, at, paint, salt) in paints)
                {
                    var d = MathF.Sqrt((x - at.X) * (x - at.X) + (z - at.Z) * (z - at.Z));
                    if (paint.Noise > 0f)
                        d += paint.Noise * Noise.Fractal(x / PaintNoiseScale, z / PaintNoiseScale, salt, 3, 2f, 0.5f);
                    Over(grounds, g, ground, 1f - Noise.SmoothStep(paint.Radius, paint.Radius + paint.Blend, d));
                }
                if (Roads.Paint(x, z, out var shoulder, out var track))
                {
                    if (Data.ShoulderGround >= 0)
                        Over(grounds, g, Data.ShoulderGround, shoulder);
                    if (Data.TrackGround >= 0)
                        Over(grounds, g, Data.TrackGround, track);
                }
            }
        }
    }

    // How far, in the layer's noise, the ground rises from a patch's edge to its full height: about a sixth of the patch
    // size, so a knoll's sides stay as gentle as the bumps.
    private const float PatchRamp = 0.35f;

    // How deep a point lies inside a patch layer's patches, in the layer's noise: 0 at the edge where the clustered rules'
    // objects stop (their roll is the same noise, see ClusterRoll), above 0 inside, below 0 outside.
    private float PatchDepth(Patch patch, float x, float z)
    {
        // ClusterRoll keeps a spot when 0.88 * smooth + 0.12 * dice < cover; at the average dice that is
        // smooth < (cover - 0.06) / 0.88, and smooth is a logistic of 8 times the noise.
        var c = Math.Clamp((patch.Cover - 0.06f) / 0.88f, 1e-3f, 1f - 1e-3f);
        var edge = -MathF.Log(1f / c - 1f) / 8f;
        var salt = _clusterSalt ^ Noise.Hash(patch.Layer);
        return edge - Noise.Fractal(x / patch.Scale, z / patch.Scale, salt, 2, 2.1f, 0.5f);
    }

    // Metres a biome's patches lift (or sink) the ground at a point.
    private float PatchRise(Biome biome, float x, float z)
    {
        var rise = 0f;
        foreach (var patch in biome.Patches)
        {
            if (patch.Height != 0f)
                rise += patch.Height * Noise.SmoothStep(-patch.Soft, PatchRamp, PatchDepth(patch, x, z));
        }
        return rise;
    }

    /// <summary>
    /// 0 to 1: how fully a point lies in one of a biome's patches (<see cref="Biome.Patches"/>), soft across the edge.
    /// </summary>
    public float PatchWeight(int biome, int patch, float x, float z)
    {
        var p = Data.Biomes[biome].Patches[patch];
        return Noise.SmoothStep(-p.Soft, p.Soft, PatchDepth(p, x, z));
    }

    // Paints a sample's biome patches over its biome grounds, each weighted by its biome's share there.
    private void PaintPatches(float[] grounds, int at, ReadOnlySpan<float> weights, float x, float z)
    {
        for (var b = 0; b < weights.Length; b++)
        {
            if (weights[b] <= 0f)
                continue;
            var patches = Data.Biomes[b].Patches;
            for (var p = 0; p < patches.Count; p++)
            {
                if (patches[p].Ground >= 0)
                    Over(grounds, at, patches[p].Ground, weights[b] * patches[p].Paint * PatchWeight(b, p, x, z));
            }
        }
    }

    // Lays a ground over a sample's others with a weight: the others shrink to make room.
    private static void Over(float[] grounds, int at, int ground, float weight)
    {
        if (weight <= 0f)
            return;
        for (var l = 0; l < TerrainChunkData.GroundLayers; l++)
            grounds[at + l] *= 1f - weight;
        grounds[at + ground] += weight;
    }

    private BiomeSample SampleBiomes(float x, float z, Span<float> weights = default)
    {
        var t = _terrain;
        var size = t.BiomeCellSize;
        var edge = EdgeDistance(x, z);
        var (px0, pz0) = (x, z);
        if (t.BiomeWarp > 0f)
        {
            // Bend the borders: look the cells up at a point pushed around by smooth noise.
            var (u, v) = (x / t.NoiseScale, z / t.NoiseScale);
            x += t.BiomeWarp * Noise.Fractal(u, v, _warpSalt, 3, 2f, 0.5f);
            z += t.BiomeWarp * Noise.Fractal(u, v, Noise.Mix(_warpSalt), 3, 2f, 0.5f);
        }
        int cx = (int)MathF.Floor(x / size), cz = (int)MathF.Floor(z / size);
        // The cells around this one, out to the reach that holds every cell the blend and ramps can see.
        var reach = _cellReach;
        var side = 2 * reach + 1;
        var cells = side * side;
        Span<float> distance = stackalloc float[cells];
        Span<int> biome = stackalloc int[cells];
        var nearest = float.PositiveInfinity;
        var nearestBiome = 0;
        for (var k = 0; k < cells; k++)
        {
            int ix = cx + k % side - reach, iz = cz + k / side - reach;
            var px = (ix + 0.25f + 0.5f * Noise.Unit(Noise.Hash(_cellSalt, ix, iz, 0))) * size;
            var pz = (iz + 0.25f + 0.5f * Noise.Unit(Noise.Hash(_cellSalt, ix, iz, 1))) * size;
            distance[k] = MathF.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
            biome[k] = CellBiome(ix, iz);
            if (distance[k] < nearest)
                (nearest, nearestBiome) = (distance[k], biome[k]);
        }
        var result = new BiomeSample { Biome = nearestBiome, Hills = 1f };
        var ramp = t.Hills?.ShoreRamp ?? 0f;
        for (var k = 0; k < cells; k++)
        {
            // A flat biome (a lake) holds its neighbours down too, easing to full hills over the ramp.
            var keep = Data.Biomes[biome[k]].Hills;
            if (keep < 1f)
                result.Hills = MathF.Min(result.Hills, keep + (1f - keep) * Noise.SmoothStep(0f, ramp, distance[k] - nearest));
        }
        var total = 0f;
        var shares = 0f;
        Span<float> share = stackalloc float[Data.Biomes.Count];
        var jitter = MathF.Min(t.BiomeEdgeNoise, 0.6f * t.BiomeBlend);
        for (var k = 0; k < cells; k++)
        {
            var d = distance[k] - nearest;
            if (!weights.IsEmpty)
            {
                // The look (ground, flora, objects) takes the cell's edge pulled back by its own noise, so neighbours
                // reach into each other in tongues; the height below blends evenly. Pulled back only, never pushed out:
                // a cell never reaches further than the blend, so no cell outside the reach is ever missed.
                if (jitter > 0f && d < t.BiomeBlend)
                {
                    int ix = cx + k % side - reach, iz = cz + k / side - reach;
                    var f = Noise.Fractal(x / t.BiomeEdgeScale, z / t.BiomeEdgeScale, Noise.Hash(_edgeSalt, ix, iz), 2, 2.3f, 0.45f);
                    d += jitter * Math.Clamp(0.5f + 0.5f * f, 0f, 1f);
                }
                var look = 1f - Noise.SmoothStep(0f, t.BiomeBlend, d);
                weights[biome[k]] += look;
                shares += look;
                d = distance[k] - nearest;
            }
            var w = 1f - Noise.SmoothStep(0f, t.BiomeBlend, d);
            if (w <= 0f)
                continue;
            var b = Data.Biomes[biome[k]];
            result.Height += w * b.Height;
            result.Relief += w * b.Relief;
            result.Ridge += w * b.Ridge;
            result.Color += w * b.Color;
            result.Bumps += w * b.Bumps;
            share[b.Index] += w;
            if (b.Terrace > 0f)
            {
                result.TerraceStep += w * b.Terrace;
                result.TerraceWeight += w;
            }
            total += w;
        }
        if (result.TerraceWeight > 0f)
            result.TerraceStep /= result.TerraceWeight;
        result.Height /= total;
        result.Relief /= total;
        result.Ridge /= total;
        result.TerraceWeight /= total;
        result.Color /= total;
        result.Bumps /= total;
        for (var b = 0; b < share.Length; b++)
        {
            if (share[b] > 0f && Data.Biomes[b].Patches.Count > 0)
                result.Patch += share[b] / total * PatchRise(Data.Biomes[b], px0, pz0);
        }
        for (var k = 0; k < weights.Length; k++)
            weights[k] /= shares;

        if (Data.CoastBiome is { } coast && t.CoastBiomeWidth > 0f)
        {
            var k = 1f - Noise.SmoothStep(t.CoastBiomeWidth - t.BiomeBlend, t.CoastBiomeWidth + t.BiomeBlend, edge);
            if (k > 0f)
            {
                result.Height += (coast.Height - result.Height) * k;
                result.Relief += (coast.Relief - result.Relief) * k;
                result.Ridge += (coast.Ridge - result.Ridge) * k;
                result.TerraceWeight *= 1f - k;
                result.Hills += (MathF.Min(result.Hills, coast.Hills) - result.Hills) * k;
                result.Color += (coast.Color - result.Color) * k;
                result.Bumps += (coast.Bumps - result.Bumps) * k;
                result.Patch += (PatchRise(coast, px0, pz0) - result.Patch) * k;
                for (var b = 0; b < weights.Length; b++)
                    weights[b] *= 1f - k;
                if (!weights.IsEmpty)
                    weights[coast.Index] += k;
                if (k > 0.5f)
                    result.Biome = coast.Index;
            }
        }
        return result;
    }

    // Sizes, in metres, of the patches a border mixes flora in and the groves it thins objects in.
    private const float PatchSize = 22f;
    private const float GroveSize = 35f;

    /// <summary>
    /// A biome's share (0..1) of how a world position looks: its ground, flora and objects. 1 inside the biome, 0 away
    /// from it, and in between across a border, where the biomes' looks mix.
    /// </summary>
    public float BiomeShare(float x, float z, int biome)
    {
        Span<float> weights = stackalloc float[Data.Biomes.Count];
        SampleBiomes(x, z, weights);
        return weights[biome];
    }

    // The biome whose flora grows at a position: one of the biomes sharing it, picked by smooth noise so a border's
    // grasses mix in patches, each biome's patches thinning the further it reaches.
    private int MixedBiome(float x, float z, ReadOnlySpan<float> weights, int dominant)
    {
        var roll = EdgeRoll(x, z, _edgeSalt, PatchSize);
        for (var b = 0; b < weights.Length; b++)
        {
            roll -= weights[b];
            if (roll < 0f)
                return b;
        }
        return dominant;
    }

    // A roll spread evenly over 0..1, the same at a position on every peer and in every chunk: half smooth noise of the
    // given size (the logistic curve flattens its bell), half a dice roll per spot, so patches and groves have soft,
    // scattered edges and one biome thins out gradually among the other.
    private static float EdgeRoll(float x, float z, uint salt, float size)
    {
        var smooth = 1f / (1f + MathF.Exp(-8f * Noise.Fractal(x / size, z / size, salt, 2, 2.1f, 0.5f)));
        var dice = Noise.Unit(Noise.Hash(Noise.Mix(salt), (int)MathF.Floor(x * 4f), (int)MathF.Floor(z * 4f)));
        // The sum of two even rolls peaks in the middle; its own curve spreads it evenly again.
        var sum = 0.5f * (smooth + dice);
        var even = sum < 0.5f ? 2f * sum * sum : 1f - 2f * (1f - sum) * (1f - sum);
        return MathF.Min(0.999f, even);
    }

    // Metres to the world edge, pulled in by up to the coast wobble so the coastline meanders; the sea floor is reached at 0.
    private float EdgeDistance(float x, float z)
    {
        var edge = MathF.Min(_halfX - MathF.Abs(x), _halfZ - MathF.Abs(z));
        var t = _terrain;
        if (t.CoastWobble > 0f)
            edge -= t.CoastWobble * (0.5f + 0.5f * Noise.Fractal(x / t.NoiseScale, z / t.NoiseScale, _coastSalt, 3, 2f, 0.5f));
        return edge;
    }

    private int CellBiome(int ix, int iz)
    {
        if (_regions.Length > 0)
            return RegionBiome(ix, iz);
        var roll = Noise.Unit(Noise.Hash(_biomeSalt, ix, iz)) * _inlandWeight;
        foreach (var b in _inland)
        {
            roll -= b.Weight;
            if (roll < 0f)
                return b.Index;
        }
        return _inland[^1].Index;
    }

    private float RawHeight(float x, float z, in BiomeSample sample)
    {
        var t = _terrain;
        float u = x / t.NoiseScale, v = z / t.NoiseScale;
        var relief = Noise.Fractal(u, v, _heightSalt, t.Octaves, t.Lacunarity, t.Gain);
        if (sample.Ridge > 0f)
            relief += (Ridged(u, v, _heightSalt ^ 0x51D6Eu, t.Octaves, t.Lacunarity, t.Gain) - relief) * sample.Ridge;
        var h = sample.Height + sample.Relief * relief;
        if (sample.TerraceWeight > 0f && sample.TerraceStep > 0f)
        {
            // Plateaus: the ground keeps level for most of each step, then climbs to the next.
            var steps = (h - t.SeaLevel) / sample.TerraceStep;
            var floor = MathF.Floor(steps);
            var terraced = t.SeaLevel + sample.TerraceStep * (floor + Noise.SmoothStep(0.6f, 1f, steps - floor));
            h += (terraced - h) * sample.TerraceWeight;
        }
        if (t.Hills is not null || t.Cliffs is not null || sample.Patch != 0f)
            h += HillsAndCliffs(x, z, sample);
        if (t.CoastWidth > 0f)
            h = t.SeaFloor + (h - t.SeaFloor) * Noise.SmoothStep(0f, t.CoastWidth, EdgeDistance(x, z));
        return MathF.Min(h, RiverSurface(x, z));
    }

    // Share of the hills kept at a point by the coast, the rivers and the fixed pads, which they ease up from over their ramps.
    private float Keep(float x, float z)
    {
        var t = _terrain;
        var ramp = t.Hills?.ShoreRamp ?? 0f;
        var keep = Noise.SmoothStep(t.CoastWidth, t.CoastWidth + ramp, EdgeDistance(x, z));
        if (keep > 0f && RiverDistance(x, z) is var river && river < ramp)
            keep *= Noise.SmoothStep(0f, ramp, river);
        var padRamp = t.Hills?.PadRamp ?? 0f;
        foreach (var (centre, radius) in _fixedPads)
        {
            if (keep <= 0f)
                break;
            var d = Vector2.Distance(new Vector2(x, z), centre);
            if (d < radius + padRamp)
                keep *= Noise.SmoothStep(radius, radius + padRamp, d);
        }
        return keep;
    }

    // The broad hills and the cliff terraces (Scrap Mechanic's two layers of relief): both lie flat at the shores, on
    // rivers, in flat biomes and round fixed pads, and climb to full height over the shore and pad ramps.
    // The biomes' patch heights (and the bumps, in Bumped) lie flat the same way.
    private float HillsAndCliffs(float x, float z, in BiomeSample sample)
    {
        var t = _terrain;
        var ramp = t.Hills?.ShoreRamp ?? 0f;
        var edge = EdgeDistance(x, z);
        var keep = sample.Hills > 0f ? sample.Hills * Keep(x, z) : 0f;
        if (keep <= 0f)
            return 0f;
        var h = 0f;
        if (t.Hills is { Height: > 0f } hills)
        {
            var n = Noise.Fractal(x / hills.Scale, z / hills.Scale, _hillSalt, hills.Octaves, 2f, hills.Gain);
            h += hills.Height * keep * Noise.SmoothStep(0f, 1f, 0.5f + hills.Contrast * n);
        }
        if (t.Cliffs is { Step: > 0f, Levels: > 0 } cliffs)
        {
            var inland = Noise.SmoothStep(t.CoastWidth, t.CoastWidth + cliffs.Inland, edge);
            var scale = cliffs.Levels * keep * inland;
            var noise = CliffNoise(x, z, cliffs);
            var level = scale * noise;
            // Each face takes about the same run of ground however fast the levels change: the share of a level it
            // takes is the face's width times the levels climbed per metre, from the noise and from the ramps that
            // fade the cliffs out by the water (a smooth step over w climbs at most 6t(1 - t)/w).
            var noiseClimb = scale * MathF.Sqrt(Square(CliffNoise(x + 1f, z, cliffs) - CliffNoise(x - 1f, z, cliffs))
                + Square(CliffNoise(x, z + 1f, cliffs) - CliffNoise(x, z - 1f, cliffs))) / 2f;
            var rampClimb = cliffs.Levels * noise * (inland * 6f * keep * (1f - keep) / MathF.Max(ramp, 1f)
                + keep * 6f * inland * (1f - inland) / MathF.Max(cliffs.Inland, 1f));
            var face = Math.Clamp(FaceWidth(x, z, cliffs) * MathF.Sqrt(noiseClimb * noiseClimb + rampClimb * rampClimb), 0.02f, 1f);
            var floor = MathF.Floor(level);
            h += cliffs.Step * (floor + Noise.SmoothStep(1f - face, 1f, level - floor));
        }
        return h + sample.Patch * keep;
    }

    // Metres of ground a cliff face takes here: sheer (face) where the sheer noise is above the threshold that leaves
    // the sheer share of the island over it, an easy slope (gentle_face) elsewhere, blending between the two.
    private float FaceWidth(float x, float z, CliffsDefinition cliffs)
    {
        if (cliffs.Sheer >= 1f || cliffs.GentleFace <= cliffs.Face)
            return cliffs.Face;
        if (cliffs.Sheer <= 0f)
            return cliffs.GentleFace;
        var sheer = Noise.SmoothStep(_sheerThreshold - SheerBlend, _sheerThreshold + SheerBlend, SheerNoise(x, z, cliffs));
        return cliffs.GentleFace + (cliffs.Face - cliffs.GentleFace) * sheer;
    }

    // Whether a point lies in or near the sheer share of the cliff terraces (terrain.cliffs.sheer), within about the reach a
    // face rule's spot slides: one noise sample that spares most of the island the search for a face's foot.
    private bool NearSheer(Vector2 p) =>
        _terrain.Cliffs is not { } cliffs || cliffs.Sheer >= 1f || SheerNoise(p.X, p.Y, cliffs) >= _sheerThreshold - 2f * SheerBlend;

    // Half the width, in sheer noise, of the blend from a sheer face to a gentle one.
    private const float SheerBlend = 0.04f;

    private float SheerNoise(float x, float z, CliffsDefinition cliffs) =>
        Noise.Fractal(x / cliffs.SheerScale, z / cliffs.SheerScale, _sheerSalt, 3, 2f, 0.5f);

    // The sheer noise value that the sheer share of the world lies above, from a fixed grid of samples.
    private float SheerThreshold()
    {
        if (_terrain.Cliffs is not { Sheer: > 0f and < 1f } cliffs)
            return 0f;
        const int n = 64;
        var samples = new float[n * n];
        for (var j = 0; j < n; j++)
        {
            for (var i = 0; i < n; i++)
                samples[j * n + i] = SheerNoise(((i + 0.5f) / n - 0.5f) * WorldSize, ((j + 0.5f) / n - 0.5f) * WorldDepth, cliffs);
        }
        Array.Sort(samples);
        return samples[Math.Clamp((int)((1f - cliffs.Sheer) * samples.Length), 0, samples.Length - 1)];
    }

    private float CliffNoise(float x, float z, CliffsDefinition cliffs) =>
        Math.Clamp(0.5f + cliffs.Contrast * Noise.Fractal(x / cliffs.Scale, z / cliffs.Scale, _cliffSalt, 2, 2f, 0.5f), 0f, 1f);

    private static float Square(float v) => v * v;

    // Ridged fractal noise mapped to [-1, 1]: sharp crests where the gradient noise crosses zero.
    private static float Ridged(float x, float z, uint salt, int octaves, float lacunarity, float gain)
    {
        float sum = 0f, norm = 0f, amplitude = 1f, frequency = 1f;
        for (var i = 0; i < octaves; i++)
        {
            var r = 1f - MathF.Abs(Noise.Gradient(x * frequency, z * frequency, Noise.Mix(salt + (uint)i)));
            sum += amplitude * r * r;
            norm += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }
        return 2f * sum / norm - 1f;
    }

    private float RawHeight(float x, float z) => RawHeight(x, z, SampleBiomes(x, z));

    private float ApplyPois(float x, float z, float h)
    {
        if (_pois.Count == 0)
            return h;
        var p = new Vector2(x, z);
        foreach (var poi in PoisNear(x, z))
        {
            var terrain = poi.Poi.Prefab.Terrain ?? [];
            for (var i = 0; i < terrain.Count; i++)
            {
                var op = terrain[i];
                var (d, s) = OpReach(poi, i, op, p);
                if (s <= 0f)
                    continue;
                var k = 1f - Noise.SmoothStep(op.Radius * s, (op.Radius + op.Blend) * s, d);
                var target = op.Op switch
                {
                    TerrainOpKind.Basin => poi.PadHeight - op.Depth,
                    TerrainOpKind.Raise => poi.PadHeight + op.Height,
                    _ => poi.PadHeight,
                };
                h += (target - h) * k;
            }
        }
        return h;
    }

    // How far a point is from a terrain op's centre (or, for a strip, its centre line), and the scale its wobble gives the
    // radius there; a scale of 0 when the point is out of the op's reach.
    private (float Distance, float Scale) OpReach(PoiInstance poi, int index, TerrainOp op, Vector2 p)
    {
        var centre = poi.ToWorld(op.At is [var ax, var az] ? new Vector2(ax, az) : Vector2.Zero);
        var amount = op.Wobble ?? 0f;
        if (op.To is not [var tx, var tz])
        {
            var d = Vector2.Distance(p, centre);
            return d >= (op.Radius + op.Blend) * (1f + amount) ? (d, 0f) : (d, Wobble(poi, index, amount, p - centre));
        }
        var end = poi.ToWorld(new Vector2(tx, tz));
        var line = end - centre;
        var length = line.Length();
        var t = length > 0f ? Math.Clamp(Vector2.Dot(p - centre, line) / (length * length), 0f, 1f) : 0f;
        var across = p - (centre + line * t);
        var distance = across.Length();
        if (distance >= (op.Radius + op.Blend) * (1f + amount))
            return (distance, 0f);
        if (amount <= 0f)
            return (distance, 1f);
        // A strip's edges wander along its length, each side on its own, so a ravine's walls never run straight.
        var side = length > 0f && (line.X * across.Y - line.Y * across.X) < 0f ? 1 : 0;
        var salt = Noise.Hash(_poiSalt ^ 0x57121Bu, poi.Index, index, side);
        var wander = Noise.Fractal(t * length / MathF.Max(op.Radius + op.Blend, 1f), side * 17.3f, salt, 3, 2f, 0.5f);
        return (distance, 1f + amount * Math.Clamp(wander, -1f, 1f));
    }

    // Scale of a terrain op's radius towards an offset from its centre: 1 ± amount, from a few sines round the circle
    // whose sizes and phases come from the POI, so each pond has its own lobes and the edge closes on itself.
    private float Wobble(PoiInstance poi, int op, float amount, Vector2 offset)
    {
        if (amount <= 0f)
            return 1f;
        var angle = MathF.Atan2(offset.Y, offset.X);
        float sum = 0f, norm = 0f;
        for (var k = 1; k <= 5; k++)
        {
            var h = Noise.Hash(_poiSalt ^ 0xB0A51Bu, poi.Index, op, k);
            var size = (0.4f + Noise.Unit(h)) / MathF.Pow(k, 0.7f);
            sum += size * MathF.Sin(k * angle + Noise.Unit(Noise.Mix(h)) * 2f * MathF.PI);
            norm += size;
        }
        return 1f + amount * sum / norm;
    }

    // A road or track bank never cuts into a POI's level pad (a POI's own track ends at its edge and can fall away
    // steeply there): inside a flatten's full-strength disc, clear of basins and of the road bed itself, the pad holds,
    // easing back to the shaped ground over the disc's outer tenth.
    private float KeepPads(float x, float z, float h)
    {
        if (_pois.Count == 0)
            return h;
        var p = new Vector2(x, z);
        foreach (var poi in PoisNear(x, z))
        {
            if (MathF.Abs(h - poi.PadHeight) < 1e-4f)
                continue;
            var hold = 0f;
            var terrain = poi.Poi.Prefab.Terrain ?? [];
            for (var i = 0; i < terrain.Count; i++)
            {
                var op = terrain[i];
                var (d, s) = OpReach(poi, i, op, p);
                if (op.Op is TerrainOpKind.Basin or TerrainOpKind.Raise && s > 0f)
                    return h;
                if (op.Op == TerrainOpKind.Flatten && op.To is null)
                    hold = MathF.Max(hold, 1f - Noise.SmoothStep(0.9f * op.Radius, op.Radius, d));
            }
            if (hold > 0f)
                return Roads.Covers(x, z) ? h : h + (poi.PadHeight - h) * hold;
        }
        return h;
    }

    // The POIs whose footprint reaches into the bucket a position is in.
    internal IReadOnlyList<PoiInstance> PoisNear(float x, float z) =>
        _poiBuckets.GetValueOrDefault(((int)MathF.Floor(x / PoiBucket), (int)MathF.Floor(z / PoiBucket))) ?? (IReadOnlyList<PoiInstance>)[];

    private IEnumerable<PoiInstance> PoisOverlapping(float x0, float z0, float x1, float z1) =>
        _pois.Where(p => p.Centre.X + p.Radius >= x0 && p.Centre.X - p.Radius < x1 && p.Centre.Y + p.Radius >= z0 && p.Centre.Y - p.Radius < z1);

    // Prefabs with a fixed position (in metres) go first, exactly where they say.
    private void LayOutFixedPois()
    {
        foreach (var poi in Data.Pois.Where(p => p.Prefab.Placement!.Fixed is not null).OrderBy(p => p.QualifiedId, StringComparer.Ordinal))
        {
            var placement = poi.Prefab.Placement!;
            if (placement.Fixed is not [var fx, var fz])
                continue;
            if (MathF.Abs(fx) + poi.Prefab.Radius > _halfX || MathF.Abs(fz) + poi.Prefab.Radius > _halfZ)
                continue;
            Add(poi, new Vector2(fx, fz), placement.PadHeight ?? RawHeight(fx, fz), QuarterTurns(placement.Yaw));
        }
    }

    // The mandatory structures (World Map spec §4): each at its fixed fraction of the world, moved at most its jitter
    // when the seed finds dry, level enough ground there, and given a prefab from its pool when it has one.
    private void LayOutSites()
    {
        var drawn = new Dictionary<string, Queue<LoadedPoi>>();
        foreach (var pool in Data.Sites.Select(s => s.Pool).OfType<LoadedPool>().DistinctBy(p => p.Id))
        {
            var entries = pool.Entries.ToArray();
            var salt = _poiSalt ^ Noise.Hash("pool:" + pool.Id);
            for (var k = entries.Length - 1; k > 0; k--)
            {
                var j = (int)(Noise.Hash(salt, k, 0) % (uint)(k + 1));
                (entries[k], entries[j]) = (entries[j], entries[k]);
            }
            drawn[pool.Id] = new Queue<LoadedPoi>(entries);
        }
        foreach (var site in Data.Sites)
        {
            var poi = site.Poi ?? drawn[site.Pool!.Id].Dequeue();
            var def = site.Definition;
            var centre = new Vector2(def.At![0] * WorldSize, def.At[1] * WorldDepth);
            if (def.Jitter > 0f)
            {
                var salt = _poiSalt ^ Noise.Hash("site:" + site.Id);
                // The given spot first, then random ones within the jitter: dry, level enough, in the site's biome and clear.
                for (var attempt = 0; attempt < PoiAttempts; attempt++)
                {
                    var spot = attempt == 0 ? centre : centre + def.Jitter * new Vector2(
                        2f * Noise.Unit(Noise.Hash(salt, attempt, 0)) - 1f,
                        2f * Noise.Unit(Noise.Hash(salt, attempt, 1)) - 1f);
                    if (DryAndLevel(spot, poi.Prefab.Radius, out _) && (site.Biome < 0 || SampleBiomes(spot.X, spot.Y).Biome == site.Biome)
                        && !_pois.Any(p => Vector2.Distance(p.Centre, spot) < p.Radius + poi.Prefab.Radius))
                    {
                        centre = spot;
                        break;
                    }
                }
            }
            var pad = def.PadHeight ?? MathF.Max(RawHeight(centre.X, centre.Y), SeaLevel + SitePadAboveSea);
            Add(poi, centre, pad, QuarterTurns(def.Yaw), site);
        }
    }

    // Spaced kinds, highest priority first, fill the land the sites and roads leave.
    private void LayOutSpacedPois()
    {
        var ordered = Data.Pois
            .Where(p => p.Prefab.Placement!.Spacing is not null)
            .OrderByDescending(p => p.Prefab.Placement!.Priority)
            .ThenBy(p => p.QualifiedId, StringComparer.Ordinal);
        foreach (var poi in ordered)
        {
            var placement = poi.Prefab.Placement!;
            if (placement.Spacing is not [var min, var max])
                continue;
            if (placement.Roadside is { } gap)
            {
                LayOutRoadside(poi, min, max, gap);
                continue;
            }
            // Shore spots are rare among random ones, so a shore kind tries harder in each cell.
            var attempts = placement.Shore is null ? PoiAttempts : ShoreAttempts;
            // Neighbouring cells' spots are step ± 2·jitter apart along an axis: at least min, and at most
            // max even with the sideways jitter, so nearest neighbours land in [min, max].
            var step = (min + max) / 2f;
            var jitter = 0.225f * (max - min);
            var salt = _poiSalt ^ Noise.Hash(poi.QualifiedId);
            var offset = new Vector2(Noise.Unit(Noise.Hash(salt, 0, 0, 1)), Noise.Unit(Noise.Hash(salt, 0, 0, 2))) * step;
            var first = (int)MathF.Floor((-_halfX - offset.X) / step);
            var last = (int)MathF.Ceiling((_halfX - offset.X) / step);
            var firstZ = (int)MathF.Floor((-_halfZ - offset.Y) / step);
            var lastZ = (int)MathF.Ceiling((_halfZ - offset.Y) / step);
            for (var j = firstZ; j <= lastZ; j++)
            {
                for (var i = first; i <= last; i++)
                {
                    var cell = offset + new Vector2(i + 0.5f, j + 0.5f) * step;
                    // Most cells have no water anywhere near, and a coarse look says so for far fewer samples.
                    if (placement.Shore is { } shore && !WaterWithin(cell, jitter + poi.Prefab.Radius + shore))
                        continue;
                    for (var attempt = 0; attempt < attempts; attempt++)
                    {
                        var spot = cell + jitter * new Vector2(
                            2f * Noise.Unit(Noise.Hash(salt, i, j, 3 * attempt + 3)) - 1f,
                            2f * Noise.Unit(Noise.Hash(salt, i, j, 3 * attempt + 4)) - 1f);
                        if (!SiteFits(poi, spot, out var pad))
                            continue;
                        Add(poi, spot, pad, (int)(Noise.Hash(salt, i, j, 3 * attempt + 5) & 3));
                        break;
                    }
                }
            }
        }
    }

    // Roadside kinds walk each road (not the tracks, which come later) a spacing step at a time and stand beside it,
    // on a side the seed picks, turned so their −z side faces the road.
    private void LayOutRoadside(LoadedPoi poi, float min, float max, float gap)
    {
        if (Roads.Definition is not { } def)
            return;
        var salt = _poiSalt ^ Noise.Hash(poi.QualifiedId) ^ 0x51DEu;
        foreach (var chain in Roads.Chains.Where(c => c.Kind == RoadKind.Road))
        {
            var reach = chain.Width / 2f + def.Curb + def.Shoulder + gap + poi.Prefab.Radius;
            var k = 0;
            for (var along = min / 2f + Noise.Unit(Noise.Hash(salt, chain.Index, 0, 1)) * (max - min); along < chain.Length; k++)
            {
                var (point, direction, height) = PointAlong(chain, along);
                var side = (Noise.Hash(salt, chain.Index, k, 2) & 1) == 0 ? 1f : -1f;
                var normal = new Vector2(-direction.Y, direction.X) * side;
                var spot = point + normal * reach;
                // Roads bend and meet, so a spot can come near one placed from another stretch: keep the spacing anyway.
                var pad = 0f;
                // Level with the road, so the road's banks do not cut into the pad.
                // Clear of junctions, where other roads come by at their own heights.
                var placed = !_pois.Any(p => p.Poi == poi && Vector2.Distance(p.Centre, spot) < min)
                    && !Roads.Nodes.Any(n => n.Kind == RoadNodeKind.Junction && Vector2.Distance(n.At, point) < n.Radius + reach + 20f)
                    && SiteFits(poi, spot, out pad)
                    && MathF.Abs(pad - height) <= RoadsideRise
                    // And the road nearest it is level with it too, not another stretch winding by at its own height
                    // (the other leg of a switchback).
                    && Roads.Nearest(spot.X, spot.Y, reach + 20f, RoadKind.Road) is { } nearest && MathF.Abs(nearest.Height - pad) <= RoadsideRise;
                if (placed)
                    Add(poi, spot, pad, FacingTurns(-normal));
                // A spot that does not fit is tried again a little further on.
                along += placed ? min + Noise.Unit(Noise.Hash(salt, chain.Index, k, 3)) * (max - min) : 0.1f * min;
            }
        }
    }

    private const float RoadsideRise = 1.5f;

    // Where a chain is at a distance along it, which way it runs there, and the height of its surface.
    private static (Vector2 Point, Vector2 Direction, float Height) PointAlong(RoadChain chain, float along)
    {
        var found = Array.BinarySearch(chain.Along, along);
        var i = Math.Clamp(found < 0 ? ~found - 1 : found, 0, chain.Points.Length - 2);
        var span = chain.Along[i + 1] - chain.Along[i];
        var t = span > 0f ? Math.Clamp((along - chain.Along[i]) / span, 0f, 1f) : 0f;
        var d = chain.Points[i + 1] - chain.Points[i];
        return (Vector2.Lerp(chain.Points[i], chain.Points[i + 1], t), d.LengthSquared() > 0f ? Vector2.Normalize(d) : Vector2.UnitX,
            chain.Heights[i] + (chain.Heights[i + 1] - chain.Heights[i]) * t);
    }

    // The quarter turn that points a POI's −z side most nearly along a world direction.
    private static int FacingTurns(Vector2 direction)
    {
        var best = 0;
        var most = float.NegativeInfinity;
        for (var q = 0; q < 4; q++)
        {
            var front = new PoiInstance(0, null!, Vector2.Zero, 0f, q).ToWorld(new Vector2(0f, -1f));
            if (Vector2.Dot(front, direction) > most)
                (best, most) = (q, Vector2.Dot(front, direction));
        }
        return best;
    }

    private const int ShoreAttempts = 24;

    // Any open water in the square of half-size half round centre, sampled every WaterGrid metres.
    private bool WaterWithin(Vector2 centre, float half)
    {
        for (var dz = -half; dz <= half; dz += WaterGrid)
        {
            for (var dx = -half; dx <= half; dx += WaterGrid)
            {
                if (RawHeight(centre.X + dx, centre.Y + dz) < SeaLevel)
                    return true;
            }
        }
        return false;
    }

    private const float WaterGrid = 24f;

    // Open water within reach of the footprint's edge somewhere round it.
    private bool NearWater(Vector2 spot, float radius, float reach)
    {
        for (var k = 0; k < 16; k++)
        {
            var angle = k * MathF.Tau / 16f;
            for (var d = radius + 2f; d <= radius + reach; d += 4f)
            {
                if (RawHeight(spot.X + MathF.Cos(angle) * d, spot.Y + MathF.Sin(angle) * d) < SeaLevel)
                    return true;
            }
        }
        return false;
    }

    // A site's pad never sits lower than this above the sea, so a structure by a lake stands on its bank.
    private const float SitePadAboveSea = 1.5f;

    private static int QuarterTurns(float yaw) => (((int)(yaw / 90f) % 4) + 4) % 4;

    private bool SiteFits(LoadedPoi poi, Vector2 spot, out float pad)
    {
        pad = 0f;
        var radius = poi.Prefab.Radius;
        var clearance = poi.Prefab.Placement!.Clearance;
        if (MathF.Abs(spot.X) + radius > _halfX || MathF.Abs(spot.Y) + radius > _halfZ)
            return false;
        if (_pois.Any(p => Vector2.Distance(p.Centre, spot) < p.Radius + radius + clearance))
            return false;
        if (!poi.Biomes.Contains(SampleBiomes(spot.X, spot.Y).Biome))
            return false;
        if (Roads.Near(spot.X, spot.Y, radius + clearance))
            return false;
        if (!DryAndLevel(spot, radius, out pad))
            return false;
        if (poi.Prefab.Placement.Level is { } level && !LevelRound(spot, radius, level))
            return false;
        return poi.Prefab.Placement.Shore is not { } shore || NearWater(spot, radius, shore);
    }

    // Whether the natural ground on a footprint's rim and halfway in lies within level metres of its centre's.
    private bool LevelRound(Vector2 spot, float radius, float level)
    {
        var centre = RawHeight(spot.X, spot.Y);
        for (var k = 0; k < 16; k++)
        {
            var angle = k * MathF.Tau / 8f + (k < 8 ? 0f : MathF.PI / 8f);
            var r = k < 8 ? radius : radius / 2f;
            if (MathF.Abs(RawHeight(spot.X + MathF.Cos(angle) * r, spot.Y + MathF.Sin(angle) * r) - centre) > level)
                return false;
        }
        return true;
    }

    // Dry at the centre and all round the rim, and not so far above or below the centre that the pad becomes a cliff.
    private bool DryAndLevel(Vector2 spot, float radius, out float pad)
    {
        pad = 0f;
        var centre = RawHeight(spot.X, spot.Y);
        if (centre < SeaLevel + 0.5f)
            return false;
        // Eight points on the rim: dry, and not so far above or below the centre that the pad becomes a cliff.
        ReadOnlySpan<float> rim = [1f, 0f, 0.7071068f, 0.7071068f, 0f, 1f, -0.7071068f, 0.7071068f, -1f, 0f, -0.7071068f, -0.7071068f, 0f, -1f, 0.7071068f, -0.7071068f];
        for (var k = 0; k < rim.Length; k += 2)
        {
            var h = RawHeight(spot.X + rim[k] * radius, spot.Y + rim[k + 1] * radius);
            if (h < SeaLevel + 0.5f || MathF.Abs(h - centre) > 0.6f * radius)
                return false;
        }
        pad = centre;
        return true;
    }

    private void Add(LoadedPoi poi, Vector2 centre, float pad, int quarterTurns, LoadedSite? site = null)
    {
        var instance = new PoiInstance(_pois.Count, poi, centre, pad, quarterTurns) { Site = site };
        _pois.Add(instance);
        var r = instance.Radius;
        for (var bz = (int)MathF.Floor((centre.Y - r) / PoiBucket); bz <= (int)MathF.Floor((centre.Y + r) / PoiBucket); bz++)
        {
            for (var bx = (int)MathF.Floor((centre.X - r) / PoiBucket); bx <= (int)MathF.Floor((centre.X + r) / PoiBucket); bx++)
            {
                if (!_poiBuckets.TryGetValue((bx, bz), out var list))
                    _poiBuckets[(bx, bz)] = list = [];
                list.Add(instance);
            }
        }
    }

    private Vector3? FindSpawn()
    {
        foreach (var poi in _pois.Where(p => p.Site is not null || p.Poi.Prefab.Placement!.Fixed is not null))
        {
            if (poi.Poi.Prefab.Markers?.FirstOrDefault(m => m.Type == "spawn") is { } marker)
                return poi.ToWorld(ToVector(marker.At));
        }
        return null;
    }

    private static bool Inside(Vector3 p, float x0, float z0, float x1, float z1) => p.X >= x0 && p.X < x1 && p.Z >= z0 && p.Z < z1;

    private static Vector3 ToVector(Data.Vec3 v) => new(v.X, v.Y, v.Z);

    private static float Radians(float degrees) => degrees * (MathF.PI / 180f);
}
