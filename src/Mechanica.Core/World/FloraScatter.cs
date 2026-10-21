using System.Numerics;

namespace Mechanica.Core.World;

/// <summary>One plant of decorative flora.</summary>
/// <param name="Model">What it looks like.</param>
/// <param name="Position">World position of its ground point.</param>
/// <param name="Normal">Unit ground normal there.</param>
/// <param name="Yaw">Turn about +Y in radians.</param>
/// <param name="Scale">Uniform scale.</param>
/// <param name="Tint">Colour its leaves take (0..1 RGB).</param>
/// <param name="Rank">
/// Random 0..1. A plant grows at a foliage level when its rank is below that level's share of the density it was
/// grown at, so lower levels keep a subset of the plants of higher ones.
/// </param>
public readonly record struct FloraInstance(FloraModel Model, Vector3 Position, Vector3 Normal, float Yaw, float Scale, Vector3 Tint, float Rank);

/// <summary>
/// Grows a chunk's decorative flora from its biomes' <see cref="Flora"/> rules. Like the rest of world generation it is
/// a pure function of the seed, the data and the chunk, so every peer grows the same plants and nothing is synced.
/// Each ground cell of the chunk gets, per rule, a number of plants from the rule's density, thinned by a noise field
/// into patches; plants keep off water, steep ground, POIs and objects that clear flora (crop fields), and bushes keep
/// off scattered objects.
/// </summary>
public static class FloraScatter
{
    /// <summary>
    /// The plants of <paramref name="kind"/> in <paramref name="chunk"/>, grown at <paramref name="densityScale"/> times
    /// the rules' density (the densest foliage level, so <see cref="FloraInstance.Rank"/> can thin them for lower ones).
    /// </summary>
    public static List<FloraInstance> Grow(WorldGenerator generator, TerrainChunkData chunk, FloraKind kind, float densityScale)
    {
        var plants = new List<FloraInstance>();
        var data = generator.Data;
        if (densityScale <= 0f || !data.FloraRules.Any(f => f.Kind == kind))
            return plants;
        var salt = Noise.Salt(generator.Seed, 8);
        var n = chunk.Samples;
        var res = chunk.Resolution;
        var (ox, oz) = chunk.Coord.Origin(chunk.Size);
        var cellX = (int)MathF.Floor(ox / res);
        var cellZ = (int)MathF.Floor(oz / res);
        var area = res * res;
        var near = new List<WorldObjectPlacement>();
        for (var j = 0; j < n - 1; j++)
        {
            for (var i = 0; i < n - 1; i++)
            {
                var k = j * n + i;
                var (bare, tint) = GroundAt(data, chunk, k);
                if (bare > BareShare)
                    continue;
                var biome = chunk.Biomes[k];
                // Inside a POI only the biome it names grows, and only if it names one (a wild POI, a pond or a ravine, keeps the
                // ground's own); nothing grows on a road.
                var covered = chunk.Covered[k] || chunk.Covered[k + 1] || chunk.Covered[k + n] || chunk.Covered[k + n + 1];
                if (covered)
                {
                    biome = -1;
                    foreach (var poi in generator.PoisAt(ox + i * res, oz + j * res))
                    {
                        biome = poi.Poi.Prefab.Wild ? chunk.Biomes[k] : poi.Poi.Prefab.Flora is { } id ? data.Biomes.First(b => b.Id == id).Index : -1;
                        if (biome < 0)
                            break;
                    }
                    if (biome < 0)
                        continue;
                }
                var rules = data.FloraByBiome[biome];
                if (rules.Count == 0)
                    continue;
                float x0 = ox + i * res, z0 = oz + j * res;
                // The flora-clearing objects (crop fields) that may cover this cell.
                near.Clear();
                foreach (var o in chunk.FloraClearers)
                {
                    if (WorldGenerator.Clears(o, x0, z0) || WorldGenerator.Clears(o, x0 + res, z0) || WorldGenerator.Clears(o, x0, z0 + res)
                        || WorldGenerator.Clears(o, x0 + res, z0 + res) || WorldGenerator.Clears(o, x0 + res / 2f, z0 + res / 2f))
                        near.Add(o);
                }
                // The cell's steepest corner, so nothing grows in a cell that reaches up a cliff face.
                var flattest = MathF.Min(MathF.Min(chunk.Normals[k].Y, chunk.Normals[k + 1].Y), MathF.Min(chunk.Normals[k + n].Y, chunk.Normals[k + n + 1].Y));
                var slope = MathF.Sqrt(MathF.Max(0f, 1f - flattest * flattest)) / MathF.Max(flattest, 1e-3f);
                foreach (var rule in rules)
                {
                    if (rule.Kind != kind || slope > rule.MaxSlope)
                        continue;
                    var ruleSalt = salt ^ ((uint)rule.Index * 0x85EBCA6Bu);
                    var patch = Patch(rule, ruleSalt, x0 + res / 2f, z0 + res / 2f);
                    var expected = rule.Density * area * densityScale * patch;
                    if (expected <= 0f)
                        continue;
                    var cx = cellX + i;
                    var cz = cellZ + j;
                    var count = (int)expected + (Noise.Unit(Noise.Hash(ruleSalt, cx, cz, 0)) < expected - (int)expected ? 1 : 0);
                    for (var t = 0; t < count; t++)
                    {
                        var h = Noise.Hash(ruleSalt, cx, cz, 1 + t);
                        float u = Noise.Unit(h), v = Noise.Unit(Noise.Mix(h)), rank = Noise.Unit(Noise.Mix(h + 1));
                        var x = x0 + u * res;
                        var z = z0 + v * res;
                        var height = Lerp(Lerp(chunk.Heights[k], chunk.Heights[k + 1], u), Lerp(chunk.Heights[k + n], chunk.Heights[k + n + 1], u), v);
                        if (height < chunk.SeaLevel + 0.15f || (covered && (generator.Roads.Covers(x, z) || generator.LiquidAt(new Vector3(x, height, z)) is not null)))
                            continue;
                        if (kind == FloraKind.Bush && NearObject(chunk, x, z))
                            continue;
                        if (near.Count > 0 && near.Exists(o => WorldGenerator.Clears(o, x, z)))
                            continue;
                        var ground = Vector3.Normalize(Vector3.Lerp(Vector3.Lerp(chunk.Normals[k], chunk.Normals[k + 1], u), Vector3.Lerp(chunk.Normals[k + n], chunk.Normals[k + n + 1], u), v));
                        var pick = Noise.Mix(h + 2);
                        var model = rule.Models[(int)(pick % (uint)rule.Models.Count)];
                        var colour = rule.Colors[(int)(Noise.Mix(pick) % (uint)rule.Colors.Count)] * (0.92f + 0.16f * Noise.Unit(Noise.Mix(h + 3)));
                        if (tint is { } charred)
                            colour = Vector3.Lerp(colour, charred.Colour, charred.Share);
                        var scale = rule.MinScale + (rule.MaxScale - rule.MinScale) * Noise.Unit(Noise.Mix(h + 4));
                        var yaw = Noise.Unit(Noise.Mix(h + 5)) * 2f * MathF.PI;
                        plants.Add(new FloraInstance(model, new Vector3(x, height, z), ground, yaw, scale, Vector3.Clamp(colour, Vector3.Zero, Vector3.One), rank));
                    }
                }
            }
        }
        return plants;
    }

    // Plants skip ground that is more than this share bare (grounds without flora), and take a ground's flora tint fully
    // where it covers this share or more.
    private const float BareShare = 0.45f;
    private const float TintShare = 0.4f;

    // How much of a sample is bare ground, and the strongest flora tint of the grounds there (with its share, 0..1).
    private static (float Bare, (Vector3 Colour, float Share)? Tint) GroundAt(WorldGenData data, TerrainChunkData chunk, int k)
    {
        if (chunk.Grounds.Length == 0)
            return (0f, null);
        var bare = 0f;
        (Vector3 Colour, float Share)? tint = null;
        foreach (var ground in data.Grounds)
        {
            var w = chunk.Grounds[k * TerrainChunkData.GroundLayers + ground.Index];
            if (!ground.Flora)
                bare += w;
            if (ground.FloraTint is { } colour && w > 0f && (tint is null || w / TintShare > tint.Value.Share))
                tint = (colour, MathF.Min(1f, w / TintShare));
        }
        return (bare, tint);
    }

    /// <summary>How full (0..1) a rule's patch is at a world position: 1 everywhere at full coverage, otherwise noise patches.</summary>
    public static float Patch(Flora rule, uint ruleSalt, float x, float z)
    {
        if (rule.Coverage >= 1f)
            return 1f;
        if (rule.Coverage <= 0f)
            return 0f;
        var noise = 0.5f + 0.8f * Noise.Fractal(x / rule.PatchSize, z / rule.PatchSize, ruleSalt, 2, 2f, 0.5f);
        var threshold = 1f - rule.Coverage;
        return Noise.SmoothStep(threshold - 0.1f, threshold + 0.1f, noise);
    }

    private static bool NearObject(TerrainChunkData chunk, float x, float z)
    {
        foreach (var o in chunk.Objects)
        {
            var reach = o.Definition.Footprint * o.Scale * 0.8f + 0.5f;
            if ((o.Position.X - x) * (o.Position.X - x) + (o.Position.Z - z) * (o.Position.Z - z) < reach * reach)
                return true;
        }
        return false;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
