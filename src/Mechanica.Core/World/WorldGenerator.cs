using System.Numerics;

namespace Mechanica.Core.World;

/// <summary>
/// Generates the world from a seed and <see cref="WorldGenData"/> (chunk 19, D11). Everything is a pure
/// function of (seed, data, position), so any chunk can be generated alone, in any order, on any peer, and
/// neighbouring chunks agree on their shared border.
/// <list type="bullet">
/// <item><b>Biomes.</b> Inland ground is cut into jittered cells (<see cref="TerrainDefinition.BiomeCellSize"/>),
/// each given a biome by weight; neighbouring cells blend over <see cref="TerrainDefinition.BiomeBlend"/> metres.
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
    private readonly uint _warpSalt;
    private readonly uint _coastSalt;
    private readonly uint _paintSalt;
    private readonly uint _hillSalt;
    private readonly uint _cliffSalt;
    private readonly (Vector2 Centre, float Radius)[] _fixedPads;
    private readonly Biome[] _inland;
    private readonly float _inlandWeight;
    private readonly List<PoiInstance> _pois = [];
    private readonly Dictionary<(int, int), List<PoiInstance>> _poiBuckets = [];

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
        // The hills keep away from sites with a fixed pad height, so the pad is not a pit or a mesa.
        _fixedPads = [.. data.Sites.Where(s => s.Definition.PadHeight is not null && s.Definition.At is [_, _])
            .Select(s => (new Vector2(s.Definition.At![0] * worldSize, s.Definition.At[1] * worldDepth), s.Poi?.Prefab.Radius ?? 0f)),
            .. data.Pois.Where(p => p.Prefab.Placement!.PadHeight is not null && p.Prefab.Placement.Fixed is [_, _])
            .Select(p => (new Vector2(p.Prefab.Placement!.Fixed![0], p.Prefab.Placement.Fixed[1]), p.Prefab.Radius))];
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
        return KeepPads(x, z, Roads.Shape(x, z, ApplyPois(x, z, RawHeight(x, z, sample))));
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
                var h = KeepPads(x, z, Roads.Shape(x, z, ApplyPois(x, z, RawHeight(x, z, sample))));
                bordered[j * m + i] = h;
                if (i >= 1 && i <= n && j >= 1 && j <= n)
                {
                    heights[(j - 1) * n + i - 1] = h;
                    colors[(j - 1) * n + i - 1] = sample.Color;
                    biomes[(j - 1) * n + i - 1] = sample.Biome;
                    covered[(j - 1) * n + i - 1] = PoisAt(x, z).Any() || Roads.Covers(x, z);
                    if (grounds.Length > 0)
                    {
                        var g = ((j - 1) * n + i - 1) * layers;
                        for (var b = 0; b < weights.Length; b++)
                        {
                            if (Data.BiomeGrounds[b] >= 0)
                                grounds[g + Data.BiomeGrounds[b]] += weights[b];
                        }
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
        var pieces = new List<PoiPiecePlacement>();
        var markers = new List<PoiMarkerPlacement>();
        var pools = new List<PoiPoolPlacement>();
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
            foreach (var marker in prefab.Markers ?? [])
            {
                var at = poi.ToWorld(ToVector(marker.At));
                if (Inside(at, ox, oz, x1, z1))
                    markers.Add(new PoiMarkerPlacement(poi, marker, at, poi.Yaw + Radians(marker.Yaw)));
            }
        }
        return new TerrainChunkData(coord, chunkSize, res, n, heights, normals, colors, biomes, covered, SeaLevel, objects, pieces, markers)
        {
            Grounds = grounds,
            Pools = pools,
            Roads = RoadMesher.Build(this, ox, oz, x1, z1),
        };
    }

    /// <summary>Every marker of <paramref name="type"/> in every placed POI, in POI then marker order.</summary>
    public IEnumerable<PoiMarkerPlacement> MarkersOfType(string type)
    {
        foreach (var poi in _pois)
        {
            foreach (var marker in poi.Poi.Prefab.Markers ?? [])
            {
                if (marker.Type == type)
                    yield return new PoiMarkerPlacement(poi, marker, poi.ToWorld(ToVector(marker.At)), poi.Yaw + Radians(marker.Yaw));
            }
        }
    }

    /// <summary>
    /// Objects whose ground point lies in [x0, x1) × [z0, z1): scattered ones in rule then cell order, then the ones POI
    /// <c>object</c> markers place, in POI then marker order.
    /// </summary>
    public IEnumerable<WorldObjectPlacement> ObjectsIn(float x0, float z0, float x1, float z1) =>
        ScatteredIn(x0, z0, x1, z1).Concat(PoiObjectsIn(x0, z0, x1, z1));

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

    private IEnumerable<WorldObjectPlacement> ScatteredIn(float x0, float z0, float x1, float z1)
    {
        foreach (var rule in Data.ScatterRules)
        {
            var s = rule.Spacing;
            int ix0 = (int)MathF.Floor(x0 / s), ix1 = (int)MathF.Floor(x1 / s);
            int iz0 = (int)MathF.Floor(z0 / s), iz1 = (int)MathF.Floor(z1 / s);
            for (var iz = iz0; iz <= iz1; iz++)
            {
                for (var ix = ix0; ix <= ix1; ix++)
                {
                    if (!Candidate(rule, ix, iz, out var spot) || spot.X < x0 || spot.X >= x1 || spot.Y < z0 || spot.Y >= z1)
                        continue;
                    if (Fits(rule, spot) && !Crowded(rule, spot))
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
        if (id.Rule < 0 || id.Rule >= Data.ScatterRules.Count)
            return null;
        var rule = Data.ScatterRules[id.Rule];
        if (!Candidate(rule, id.CellX, id.CellZ, out var spot) || !Fits(rule, spot) || Crowded(rule, spot))
            return null;
        return Place(rule, id.CellX, id.CellZ, spot).FirstOrDefault(p => p.Id.Piece == id.Piece);
    }

    // The object a scatter spot holds: one, or every cube of a rock outcrop.
    private IEnumerable<WorldObjectPlacement> Place(Scatter rule, int ix, int iz, Vector2 spot)
    {
        var o = rule.Object;
        var salt = Noise.Hash(_scatterSalt ^ (uint)rule.Index, ix, iz, 3);
        var scale = o.Scale is [var lo, var hi] ? lo + (hi - lo) * Noise.Unit(Noise.Mix(salt)) : 1f;
        var yaw = Noise.Unit(salt) * 2f * MathF.PI;
        if (o.Outcrop is not { } outcrop)
        {
            var y = HeightAt(spot.X, spot.Y) - o.Sink * scale;
            yield return new WorldObjectPlacement(new WorldObjectId(rule.Index, ix, iz), o, new Vector3(spot.X, y, spot.Y), yaw, scale);
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
            yield return new WorldObjectPlacement(new WorldObjectId(rule.Index, ix, iz, k), definition, new Vector3(g.X, y, g.Y), yaw + b.Yaw, scale, b.Variant);
        }
    }

    // A block's middle and its four bottom corners, in half sizes from its middle.
    private static readonly (float, float)[] Corners = [(0, 0), (-1, -1), (1, -1), (-1, 1), (1, 1)];

    // A deposit block object's collider box, or null when it has none.
    private Vector3? BlockSize(string id) =>
        Data.Objects.GetValueOrDefault(id)?.Collider is { Shape: PrimitiveShape.Box, Size: { } size } ? new Vector3(size.X, size.Y, size.Z) : null;

    // How many models a deposit block object has to pick from.
    private int BlockVariants(string id) => 1 + (Data.Objects.GetValueOrDefault(id)?.Mesh?.Variants?.Count ?? 0);

    // The scatter rule's spot in a cell, if the cell's dice roll keeps it.
    private bool Candidate(Scatter rule, int ix, int iz, out Vector2 spot)
    {
        spot = default;
        var salt = _scatterSalt ^ (uint)rule.Index;
        if (Noise.Unit(Noise.Hash(salt, ix, iz, 0)) >= rule.Chance)
            return false;
        var s = rule.Spacing;
        spot = new Vector2((ix + 0.1f + 0.8f * Noise.Unit(Noise.Hash(salt, ix, iz, 1))) * s, (iz + 0.1f + 0.8f * Noise.Unit(Noise.Hash(salt, ix, iz, 2))) * s);
        return true;
    }

    // Whether the ground at a spot suits the rule, ignoring other objects.
    private bool Fits(Scatter rule, Vector2 spot)
    {
        if (!InWorld(spot.X, spot.Y))
            return false;
        if (SampleBiomes(spot.X, spot.Y).Biome != rule.Biome)
            return false;
        if (PoisAt(spot.X, spot.Y).Any() || Roads.Near(spot.X, spot.Y, rule.Object.Footprint + 1f))
            return false;
        if (HeightAt(spot.X, spot.Y) < SeaLevel + 0.2f || SlopeAt(spot.X, spot.Y) > MathF.Min(_terrain.MaxScatterSlope, rule.Object.MaxSlope ?? float.MaxValue))
            return false;
        // A rock outcrop spreads its cubes over its footprint, so the whole of it must suit, not just its middle.
        if (rule.Object.Outcrop is not null)
        {
            var reach = rule.Object.Footprint * (rule.Object.Scale is [_, var hi] ? hi : 1f);
            for (var a = 0; a < 8; a++)
            {
                var p = spot + new Vector2(MathF.Cos(a * MathF.PI / 4f), MathF.Sin(a * MathF.PI / 4f)) * reach;
                if (!InWorld(p.X, p.Y) || SampleBiomes(p.X, p.Y).Biome != rule.Biome
                    || PoisAt(p.X, p.Y).Any() || Roads.Near(p.X, p.Y, 1f) || HeightAt(p.X, p.Y) < SeaLevel + 0.2f)
                    return false;
            }
        }
        return true;
    }

    // Whether an earlier rule's fitting candidate sits too close. Earlier rules win, so ids never depend on later rules.
    private bool Crowded(Scatter rule, Vector2 spot)
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
                    if (Candidate(other, ix, iz, out var near) && Vector2.Distance(near, spot) < reach && Fits(other, near))
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
        if (t.BiomeWarp > 0f)
        {
            // Bend the borders: look the cells up at a point pushed around by smooth noise.
            var (u, v) = (x / t.NoiseScale, z / t.NoiseScale);
            x += t.BiomeWarp * Noise.Fractal(u, v, _warpSalt, 3, 2f, 0.5f);
            z += t.BiomeWarp * Noise.Fractal(u, v, Noise.Mix(_warpSalt), 3, 2f, 0.5f);
        }
        int cx = (int)MathF.Floor(x / size), cz = (int)MathF.Floor(z / size);
        Span<float> distance = stackalloc float[9];
        Span<int> biome = stackalloc int[9];
        var nearest = float.PositiveInfinity;
        var nearestBiome = 0;
        for (var k = 0; k < 9; k++)
        {
            int ix = cx + k % 3 - 1, iz = cz + k / 3 - 1;
            var px = (ix + 0.25f + 0.5f * Noise.Unit(Noise.Hash(_cellSalt, ix, iz, 0))) * size;
            var pz = (iz + 0.25f + 0.5f * Noise.Unit(Noise.Hash(_cellSalt, ix, iz, 1))) * size;
            distance[k] = MathF.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
            biome[k] = CellBiome(ix, iz);
            if (distance[k] < nearest)
                (nearest, nearestBiome) = (distance[k], biome[k]);
        }
        var result = new BiomeSample { Biome = nearestBiome, Hills = 1f };
        var ramp = t.Hills?.ShoreRamp ?? 0f;
        for (var k = 0; k < 9; k++)
        {
            // A flat biome (a lake) holds its neighbours down too, easing to full hills over the ramp.
            var keep = Data.Biomes[biome[k]].Hills;
            if (keep < 1f)
                result.Hills = MathF.Min(result.Hills, keep + (1f - keep) * Noise.SmoothStep(0f, ramp, distance[k] - nearest));
        }
        var total = 0f;
        for (var k = 0; k < 9; k++)
        {
            var w = 1f - Noise.SmoothStep(0f, t.BiomeBlend, distance[k] - nearest);
            if (w <= 0f)
                continue;
            var b = Data.Biomes[biome[k]];
            if (!weights.IsEmpty)
                weights[biome[k]] += w;
            result.Height += w * b.Height;
            result.Relief += w * b.Relief;
            result.Ridge += w * b.Ridge;
            result.Color += w * b.Color;
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
        for (var k = 0; k < weights.Length; k++)
            weights[k] /= total;

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
        if (t.Hills is not null || t.Cliffs is not null)
            h += HillsAndCliffs(x, z, sample.Hills);
        if (t.CoastWidth > 0f)
            h = t.SeaFloor + (h - t.SeaFloor) * Noise.SmoothStep(0f, t.CoastWidth, EdgeDistance(x, z));
        return MathF.Min(h, RiverSurface(x, z));
    }

    // The broad hills and the cliff terraces (Scrap Mechanic's two layers of relief): both lie flat at the shores, on
    // rivers, in flat biomes and round fixed pads, and climb to full height over the shore and pad ramps.
    private float HillsAndCliffs(float x, float z, float keep)
    {
        var t = _terrain;
        var ramp = t.Hills?.ShoreRamp ?? 0f;
        var edge = EdgeDistance(x, z);
        keep *= Noise.SmoothStep(t.CoastWidth, t.CoastWidth + ramp, edge);
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
            var face = Math.Clamp(cliffs.Face * MathF.Sqrt(noiseClimb * noiseClimb + rampClimb * rampClimb), 0.02f, 1f);
            var floor = MathF.Floor(level);
            h += cliffs.Step * (floor + Noise.SmoothStep(1f - face, 1f, level - floor));
        }
        return h;
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
                var centre = poi.ToWorld(op.At is [var ax, var az] ? new Vector2(ax, az) : Vector2.Zero);
                var d = Vector2.Distance(p, centre);
                if (d >= (op.Radius + op.Blend) * (1f + (op.Wobble ?? 0f)))
                    continue;
                var s = Wobble(poi, i, op.Wobble ?? 0f, p - centre);
                var k = 1f - Noise.SmoothStep(op.Radius * s, (op.Radius + op.Blend) * s, d);
                var target = op.Op == TerrainOpKind.Basin ? poi.PadHeight - op.Depth : poi.PadHeight;
                h += (target - h) * k;
            }
        }
        return h;
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
            foreach (var op in poi.Poi.Prefab.Terrain ?? [])
            {
                var centre = poi.ToWorld(op.At is [var ax, var az] ? new Vector2(ax, az) : Vector2.Zero);
                var d = Vector2.Distance(p, centre);
                if (op.Op == TerrainOpKind.Basin && d < (op.Radius + op.Blend) * (1f + (op.Wobble ?? 0f)))
                    return h;
                if (op.Op == TerrainOpKind.Flatten)
                    hold = MathF.Max(hold, 1f - Noise.SmoothStep(0.9f * op.Radius, op.Radius, d));
            }
            if (hold > 0f)
                return Roads.Covers(x, z) ? h : h + (poi.PadHeight - h) * hold;
        }
        return h;
    }

    private IReadOnlyList<PoiInstance> PoisNear(float x, float z) =>
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
                var placed = !_pois.Any(p => p.Poi == poi && Vector2.Distance(p.Centre, spot) < min) && SiteFits(poi, spot, out pad)
                    && MathF.Abs(pad - height) <= RoadsideRise;
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
        return poi.Prefab.Placement.Shore is not { } shore || NearWater(spot, radius, shore);
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
