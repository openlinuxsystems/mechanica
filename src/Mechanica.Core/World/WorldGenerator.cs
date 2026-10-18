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
/// A candidate is kept by chance when it is in that biome, dry, not too steep, outside every POI and clear
/// of earlier rules' candidates, which keeps object ids (<see cref="WorldObjectId"/>) stable.</item>
/// </list>
/// </summary>
public sealed class WorldGenerator
{
    private const float PoiBucket = 128f;
    private const int PoiAttempts = 8;

    // Wavelength in metres of the noise that rags the edge of a POI's painted ground.
    private const float PaintNoiseScale = 9f;

    private readonly TerrainDefinition _terrain;
    private readonly float _half;
    private readonly uint _heightSalt;
    private readonly uint _cellSalt;
    private readonly uint _biomeSalt;
    private readonly uint _poiSalt;
    private readonly uint _scatterSalt;
    private readonly uint _warpSalt;
    private readonly uint _coastSalt;
    private readonly uint _paintSalt;
    private readonly Biome[] _inland;
    private readonly float _inlandWeight;
    private readonly List<PoiInstance> _pois = [];
    private readonly Dictionary<(int, int), List<PoiInstance>> _poiBuckets = [];

    /// <summary>Lays out the world's POIs; terrain and objects are generated on demand.</summary>
    /// <param name="data">Rules and prefabs.</param>
    /// <param name="seed">World seed.</param>
    /// <param name="worldSize">Side of the square world in metres, centred on the origin.</param>
    public WorldGenerator(WorldGenData data, long seed, float worldSize)
    {
        if (!(worldSize > 0f))
            throw new ArgumentOutOfRangeException(nameof(worldSize), worldSize, "World size must be positive.");
        Data = data;
        Seed = seed;
        WorldSize = worldSize;
        _terrain = data.Terrain;
        _half = worldSize / 2f;
        _heightSalt = Noise.Salt(seed, 1);
        _cellSalt = Noise.Salt(seed, 2);
        _biomeSalt = Noise.Salt(seed, 3);
        _poiSalt = Noise.Salt(seed, 4);
        _scatterSalt = Noise.Salt(seed, 5);
        _warpSalt = Noise.Salt(seed, 6);
        _coastSalt = Noise.Salt(seed, 7);
        _paintSalt = Noise.Salt(seed, 9);
        _inland = [.. data.Biomes.Where(b => b.Weight > 0f && b != data.CoastBiome)];
        _inlandWeight = _inland.Sum(b => b.Weight);
        LayOutPois();
        Spawn = FindSpawn();
    }

    /// <summary>Rules and prefabs in use.</summary>
    public WorldGenData Data { get; }

    /// <summary>World seed.</summary>
    public long Seed { get; }

    /// <summary>Side of the square world in metres.</summary>
    public float WorldSize { get; }

    /// <summary>Height of the sea surface.</summary>
    public float SeaLevel => _terrain.SeaLevel;

    /// <summary>Every placed POI, in placement order.</summary>
    public IReadOnlyList<PoiInstance> Pois => _pois;

    /// <summary>Where players start: the first <c>spawn</c> marker of a fixed POI, or null when no fixed POI has one.</summary>
    public Vector3? Spawn { get; }

    /// <summary>Ground height at a world position, POI shaping included.</summary>
    public float HeightAt(float x, float z)
    {
        var sample = SampleBiomes(x, z);
        return ApplyPois(x, z, RawHeight(x, z, sample));
    }

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
                var h = ApplyPois(x, z, RawHeight(x, z, sample));
                bordered[j * m + i] = h;
                if (i >= 1 && i <= n && j >= 1 && j <= n)
                {
                    heights[(j - 1) * n + i - 1] = h;
                    colors[(j - 1) * n + i - 1] = sample.Color;
                    biomes[(j - 1) * n + i - 1] = sample.Biome;
                    covered[(j - 1) * n + i - 1] = PoisAt(x, z).Any();
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
        foreach (var poi in PoisOverlapping(ox, oz, x1, z1))
        {
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
        return new TerrainChunkData(coord, chunkSize, res, n, heights, normals, colors, biomes, covered, SeaLevel, objects, pieces, markers) { Grounds = grounds };
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

    /// <summary>Scattered objects whose ground point lies in [x0, x1) × [z0, z1), in rule then cell order.</summary>
    public IEnumerable<WorldObjectPlacement> ObjectsIn(float x0, float z0, float x1, float z1)
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
        // An outcrop: whole cubes on a grid turned by the outcrop's yaw, stood on the lowest ground under its
        // footprint (sunk by sink) so no cube floats on a slope.
        var cubes = OutcropLayout.Cubes(outcrop, Noise.Mix(salt ^ 0x5EEDu));
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        var centre = new Vector2((float)cubes.Average(c => c.X), (float)cubes.Average(c => c.Z));
        Vector2 Ground(OutcropCube c, float dx, float dz)
        {
            var local = Vector3.Transform(new Vector3((c.X - centre.X + dx) * scale, 0, (c.Z - centre.Y + dz) * scale), turn);
            return new Vector2(spot.X + local.X, spot.Y + local.Z);
        }
        var floor = float.MaxValue;
        foreach (var c in cubes.Where(c => c.Y == 0))
        {
            foreach (var (dx, dz) in Corners)
            {
                var g = Ground(c, dx, dz);
                floor = MathF.Min(floor, HeightAt(g.X, g.Y));
            }
        }
        var baseY = floor - o.Sink * scale;
        for (var k = 0; k < cubes.Count; k++)
        {
            var c = cubes[k];
            var g = Ground(c, 0, 0);
            yield return new WorldObjectPlacement(new WorldObjectId(rule.Index, ix, iz, k), o, new Vector3(g.X, baseY + c.Y * scale, g.Y),
                yaw + c.Turn * MathF.PI / 2f, scale);
        }
    }

    // A cube's centre and its four bottom corners, in cubes from its centre.
    private static readonly (float, float)[] Corners = [(0, 0), (-0.5f, -0.5f), (0.5f, -0.5f), (-0.5f, 0.5f), (0.5f, 0.5f)];

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
        if (MathF.Max(MathF.Abs(spot.X), MathF.Abs(spot.Y)) >= _half)
            return false;
        if (SampleBiomes(spot.X, spot.Y).Biome != rule.Biome)
            return false;
        if (PoisAt(spot.X, spot.Y).Any())
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
                if (MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Y)) >= _half || SampleBiomes(p.X, p.Y).Biome != rule.Biome
                    || PoisAt(p.X, p.Y).Any() || HeightAt(p.X, p.Y) < SeaLevel + 0.2f)
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
        var result = new BiomeSample { Biome = nearestBiome };
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
            result.Color += w * b.Color;
            total += w;
        }
        result.Height /= total;
        result.Relief /= total;
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
        var edge = _half - MathF.Max(MathF.Abs(x), MathF.Abs(z));
        var t = _terrain;
        if (t.CoastWobble > 0f)
            edge -= t.CoastWobble * (0.5f + 0.5f * Noise.Fractal(x / t.NoiseScale, z / t.NoiseScale, _coastSalt, 3, 2f, 0.5f));
        return edge;
    }

    private int CellBiome(int ix, int iz)
    {
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
        var h = sample.Height + sample.Relief * Noise.Fractal(x / t.NoiseScale, z / t.NoiseScale, _heightSalt, t.Octaves, t.Lacunarity, t.Gain);
        if (t.CoastWidth > 0f)
            h = t.SeaFloor + (h - t.SeaFloor) * Noise.SmoothStep(0f, t.CoastWidth, EdgeDistance(x, z));
        return h;
    }

    private float RawHeight(float x, float z) => RawHeight(x, z, SampleBiomes(x, z));

    private float ApplyPois(float x, float z, float h)
    {
        if (_pois.Count == 0)
            return h;
        var p = new Vector2(x, z);
        foreach (var poi in PoisNear(x, z))
        {
            foreach (var op in poi.Poi.Prefab.Terrain ?? [])
            {
                var centre = poi.ToWorld(op.At is [var ax, var az] ? new Vector2(ax, az) : Vector2.Zero);
                var d = Vector2.Distance(p, centre);
                if (d >= op.Radius + op.Blend)
                    continue;
                var k = 1f - Noise.SmoothStep(op.Radius, op.Radius + op.Blend, d);
                var target = op.Op == TerrainOpKind.Basin ? poi.PadHeight - op.Depth : poi.PadHeight;
                h += (target - h) * k;
            }
        }
        return h;
    }

    private IReadOnlyList<PoiInstance> PoisNear(float x, float z) =>
        _poiBuckets.GetValueOrDefault(((int)MathF.Floor(x / PoiBucket), (int)MathF.Floor(z / PoiBucket))) ?? (IReadOnlyList<PoiInstance>)[];

    private IEnumerable<PoiInstance> PoisOverlapping(float x0, float z0, float x1, float z1) =>
        _pois.Where(p => p.Centre.X + p.Radius >= x0 && p.Centre.X - p.Radius < x1 && p.Centre.Y + p.Radius >= z0 && p.Centre.Y - p.Radius < z1);

    private void LayOutPois()
    {
        var ordered = Data.Pois
            .OrderBy(p => p.Prefab.Placement!.Fixed is null)
            .ThenByDescending(p => p.Prefab.Placement!.Priority)
            .ThenBy(p => p.QualifiedId, StringComparer.Ordinal);
        foreach (var poi in ordered)
        {
            var placement = poi.Prefab.Placement!;
            if (placement.Fixed is [var fx, var fz])
            {
                var centre = new Vector2(fx, fz);
                if (MathF.Max(MathF.Abs(fx), MathF.Abs(fz)) + poi.Prefab.Radius > _half)
                    continue;
                var quarter = (((int)(placement.Yaw / 90f) % 4) + 4) % 4;
                Add(poi, centre, placement.PadHeight ?? RawHeight(fx, fz), quarter);
                continue;
            }
            if (placement.Spacing is not [var min, var max])
                continue;
            // Neighbouring cells' spots are step ± 2·jitter apart along an axis: at least min, and at most
            // max even with the sideways jitter, so nearest neighbours land in [min, max].
            var step = (min + max) / 2f;
            var jitter = 0.225f * (max - min);
            var salt = _poiSalt ^ Noise.Hash(poi.QualifiedId);
            var offset = new Vector2(Noise.Unit(Noise.Hash(salt, 0, 0, 1)), Noise.Unit(Noise.Hash(salt, 0, 0, 2))) * step;
            var first = (int)MathF.Floor((-_half - offset.X) / step);
            var last = (int)MathF.Ceiling((_half - offset.X) / step);
            var firstZ = (int)MathF.Floor((-_half - offset.Y) / step);
            var lastZ = (int)MathF.Ceiling((_half - offset.Y) / step);
            for (var j = firstZ; j <= lastZ; j++)
            {
                for (var i = first; i <= last; i++)
                {
                    var cell = offset + new Vector2(i + 0.5f, j + 0.5f) * step;
                    for (var attempt = 0; attempt < PoiAttempts; attempt++)
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

    private bool SiteFits(LoadedPoi poi, Vector2 spot, out float pad)
    {
        pad = 0f;
        var radius = poi.Prefab.Radius;
        if (MathF.Max(MathF.Abs(spot.X), MathF.Abs(spot.Y)) + radius > _half)
            return false;
        if (_pois.Any(p => Vector2.Distance(p.Centre, spot) < p.Radius + radius))
            return false;
        if (!poi.Biomes.Contains(SampleBiomes(spot.X, spot.Y).Biome))
            return false;
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

    private void Add(LoadedPoi poi, Vector2 centre, float pad, int quarterTurns)
    {
        var instance = new PoiInstance(_pois.Count, poi, centre, pad, quarterTurns);
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
        foreach (var poi in _pois.Where(p => p.Poi.Prefab.Placement!.Fixed is not null))
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
