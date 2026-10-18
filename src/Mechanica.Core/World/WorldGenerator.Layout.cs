using System.Numerics;

namespace Mechanica.Core.World;

// The world map's fixed layout (World Map spec §2): biome regions and rivers.
public sealed partial class WorldGenerator
{
    // Wavelength in metres of the slow noise that bends region borders.
    private const float RegionWarpScale = 2600f;

    // Metres between the points a river's meandering line is drawn through.
    private const float RiverStep = 30f;

    // Cells whose point is closer to a site than this many cell sides take the site's biome.
    private const float SitePinReach = 0.7f;

    // Wavelength in metres of a river's meanders.
    private const float MeanderScale = 900f;

    // Bucket side in metres for the river segment index.
    private const float RiverBucket = 128f;

    // Furthest a river's valley reaches from the water's edge, in metres.
    private const float ValleyReach = 240f;

    // How quickly a valley's sides steepen with distance from the water (1/m), so a deep valley ends on the hills.
    private const float ValleyCurve = 1f / 600f;

    private Region[] _regions = [];
    private int[] _regionCells = [];
    private int _regionX0, _regionZ0, _regionW, _regionH;
    private readonly List<RiverSegment> _river = [];
    private readonly Dictionary<(int, int), List<int>> _riverBuckets = [];

    private sealed record Region(Vector2 Centre, float Size, int Biome, (int Biome, float Chance)[] Mix);

    private readonly record struct RiverSegment(Vector2 A, Vector2 B, float WidthA, float WidthB, float Bed, float Slope);

    /// <summary>Every river's centre line, as world x/z points from source to mouth, for the map and the manifest.</summary>
    public IReadOnlyList<IReadOnlyList<Vector2>> Rivers { get; private set; } = [];

    // Regions in world metres, and the biome of every biome cell over the world, worked out once.
    private void BuildRegions()
    {
        var defs = Data.Definition.Regions ?? [];
        if (defs.Count == 0)
            return;
        int Index(string id) => Data.Biomes.First(b => b.Id == id).Index;
        _regions = [.. defs.Select(r => new Region(new Vector2(r.At![0] * WorldSize, r.At[1] * WorldDepth), r.Size, Index(r.Biome!),
            [.. (r.Mix ?? []).Select(m => (Index(m.Biome!), m.Chance))]))];
        var size = _terrain.BiomeCellSize;
        _regionX0 = (int)MathF.Floor(-_halfX / size) - 3;
        _regionZ0 = (int)MathF.Floor(-_halfZ / size) - 3;
        _regionW = (int)MathF.Ceiling(_halfX / size) + 3 - _regionX0 + 1;
        _regionH = (int)MathF.Ceiling(_halfZ / size) + 3 - _regionZ0 + 1;
        var cells = new int[_regionW * _regionH];
        Parallel.For(0, _regionH, j =>
        {
            for (var i = 0; i < _regionW; i++)
                cells[j * _regionW + i] = PickRegionBiome(_regionX0 + i, _regionZ0 + j);
        });
        // A site keeps its biome in every world: the cells round it take the site's biome, whatever the region or mix says.
        foreach (var site in Data.Sites.Where(s => s.Biome >= 0))
        {
            var at = new Vector2(site.Definition.At![0] * WorldSize, site.Definition.At[1] * WorldDepth);
            for (var j = 0; j < _regionH; j++)
            {
                for (var i = 0; i < _regionW; i++)
                {
                    if (Vector2.Distance(CellPoint(_regionX0 + i, _regionZ0 + j), at) < SitePinReach * size)
                        cells[j * _regionW + i] = site.Biome;
                }
            }
        }
        _regionCells = cells;
    }

    // A biome cell's point, jittered as SampleBiomes jitters it.
    private Vector2 CellPoint(int ix, int iz)
    {
        var size = _terrain.BiomeCellSize;
        return new Vector2((ix + 0.25f + 0.5f * Noise.Unit(Noise.Hash(_cellSalt, ix, iz, 0))) * size,
            (iz + 0.25f + 0.5f * Noise.Unit(Noise.Hash(_cellSalt, ix, iz, 1))) * size);
    }

    private int RegionBiome(int ix, int iz)
    {
        int i = ix - _regionX0, j = iz - _regionZ0;
        return i >= 0 && j >= 0 && i < _regionW && j < _regionH ? _regionCells[j * _regionW + i] : PickRegionBiome(ix, iz);
    }

    // The cell's point (as SampleBiomes jitters it), bent by slow noise, takes the biome of the region that is nearest
    // for its size; then the region's mix may swap it for a patch of another biome.
    private int PickRegionBiome(int ix, int iz)
    {
        var p = CellPoint(ix, iz);
        if (_terrain.RegionWarp > 0f)
        {
            var (u, v) = (p.X / RegionWarpScale, p.Y / RegionWarpScale);
            p += _terrain.RegionWarp * new Vector2(Noise.Fractal(u, v, _warpSalt ^ 0x4E610u, 2, 2f, 0.5f), Noise.Fractal(u, v, _warpSalt ^ 0x4E611u, 2, 2f, 0.5f));
        }
        Region? best = null;
        var nearest = float.PositiveInfinity;
        foreach (var r in _regions)
        {
            var d = Vector2.Distance(p, r.Centre) / r.Size;
            if (d < nearest)
                (nearest, best) = (d, r);
        }
        var roll = Noise.Unit(Noise.Hash(_biomeSalt, ix, iz));
        foreach (var (biome, chance) in best!.Mix)
        {
            if (roll < chance)
                return biome;
            roll -= chance;
        }
        return best.Biome;
    }

    // Each river's line, bent into meanders by the seed (never at its ends), cut into short segments in a bucket index.
    private void BuildRivers()
    {
        var lines = new List<IReadOnlyList<Vector2>>();
        var defs = Data.Definition.Rivers ?? [];
        for (var r = 0; r < defs.Count; r++)
        {
            var def = defs[r];
            var corners = def.Points!.Select(p => new Vector2(p[0] * WorldSize, p[1] * WorldDepth)).ToList();
            var straight = new List<Vector2>();
            for (var k = 0; k + 1 < corners.Count; k++)
            {
                var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(corners[k], corners[k + 1]) / RiverStep));
                for (var s = 0; s < steps; s++)
                    straight.Add(Vector2.Lerp(corners[k], corners[k + 1], (float)s / steps));
            }
            straight.Add(corners[^1]);
            var length = 0f;
            var along = new float[straight.Count];
            for (var k = 1; k < straight.Count; k++)
                along[k] = length += Vector2.Distance(straight[k - 1], straight[k]);
            var salt = Noise.Salt(Seed, 20 + (uint)r);
            var line = new List<Vector2>(straight.Count);
            for (var k = 0; k < straight.Count; k++)
            {
                var a = straight[Math.Max(0, k - 1)];
                var b = straight[Math.Min(straight.Count - 1, k + 1)];
                var dir = b - a;
                var side = dir.LengthSquared() > 0f ? Vector2.Normalize(new Vector2(-dir.Y, dir.X)) : Vector2.Zero;
                var taper = Noise.SmoothStep(0f, 400f, along[k]) * Noise.SmoothStep(0f, 400f, length - along[k]);
                line.Add(straight[k] + side * (def.Meander * taper * Noise.Fractal(along[k] / MeanderScale, r * 7.31f, salt, 2, 2f, 0.5f)));
            }
            lines.Add(line);
            var (source, mouth) = (def.Width![0], def.Width[1]);
            for (var k = 0; k + 1 < line.Count; k++)
            {
                var segment = new RiverSegment(line[k], line[k + 1], source + (mouth - source) * along[k] / length,
                    source + (mouth - source) * along[k + 1] / length, _terrain.SeaLevel - def.Depth, def.ValleySlope);
                var index = _river.Count;
                _river.Add(segment);
                var reach = MathF.Max(segment.WidthA, segment.WidthB) / 2f + ValleyReach;
                var min = Vector2.Min(segment.A, segment.B) - new Vector2(reach);
                var max = Vector2.Max(segment.A, segment.B) + new Vector2(reach);
                for (var bz = (int)MathF.Floor(min.Y / RiverBucket); bz <= (int)MathF.Floor(max.Y / RiverBucket); bz++)
                {
                    for (var bx = (int)MathF.Floor(min.X / RiverBucket); bx <= (int)MathF.Floor(max.X / RiverBucket); bx++)
                    {
                        if (!_riverBuckets.TryGetValue((bx, bz), out var list))
                            _riverBuckets[(bx, bz)] = list = [];
                        list.Add(index);
                    }
                }
            }
        }
        Rivers = lines;
    }

    // The highest the ground may stand at a point because of the rivers: the bed in the channel, a short bank just above
    // the water, then valley sides that climb ever more steeply until they meet the land. +∞ away from every river.
    private float RiverSurface(float x, float z)
    {
        if (_river.Count == 0 || !_riverBuckets.TryGetValue(((int)MathF.Floor(x / RiverBucket), (int)MathF.Floor(z / RiverBucket)), out var near))
            return float.PositiveInfinity;
        var p = new Vector2(x, z);
        var sea = _terrain.SeaLevel;
        var surface = float.PositiveInfinity;
        foreach (var index in near)
        {
            var s = _river[index];
            var ab = s.B - s.A;
            var t = ab.LengthSquared() > 0f ? Math.Clamp(Vector2.Dot(p - s.A, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            var d = Vector2.Distance(p, s.A + ab * t);
            var half = (s.WidthA + (s.WidthB - s.WidthA) * t) / 2f;
            float h;
            if (d <= half)
                h = s.Bed + (sea - 0.7f - s.Bed) * Noise.SmoothStep(0f, half, d);
            else if (d <= half + 3f)
                h = sea - 0.7f + 2.2f * (d - half) / 3f;
            else
            {
                var x0 = d - half - 3f;
                if (x0 > ValleyReach)
                    continue;
                h = sea + 1.5f + s.Slope * x0 + x0 * x0 * ValleyCurve;
            }
            surface = MathF.Min(surface, h);
        }
        return surface;
    }

    /// <summary>Metres from a world position to the nearest river's water, or +∞ when no river is near.</summary>
    public float RiverDistance(float x, float z)
    {
        if (_river.Count == 0 || !_riverBuckets.TryGetValue(((int)MathF.Floor(x / RiverBucket), (int)MathF.Floor(z / RiverBucket)), out var near))
            return float.PositiveInfinity;
        var p = new Vector2(x, z);
        var best = float.PositiveInfinity;
        foreach (var index in near)
        {
            var s = _river[index];
            var ab = s.B - s.A;
            var t = ab.LengthSquared() > 0f ? Math.Clamp(Vector2.Dot(p - s.A, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            best = MathF.Min(best, MathF.Max(0f, Vector2.Distance(p, s.A + ab * t) - (s.WidthA + (s.WidthB - s.WidthA) * t) / 2f));
        }
        return best;
    }
}
