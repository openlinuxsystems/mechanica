using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.World;

/// <summary>
/// What the world generator laid out, for design work and checks (World Map spec §7 output requirements): the placement
/// manifest (every POI with its coordinates, type, size and road connection, the roads as sampled centre lines, the rivers
/// and the biome shares) and the pictures (height, biome and overview maps). Pure: the same seed exports the same files.
/// </summary>
public static class WorldExport
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>The placement manifest, sampling biome shares every <paramref name="sampleStep"/> metres.</summary>
    public static WorldManifest Manifest(WorldGenerator gen, float sampleStep = 32f)
    {
        var pois = gen.Pois.Select(p => new ManifestPoi(p.Index,
            p.Site?.Id, p.Site?.Definition.Label, p.Poi.QualifiedId, p.Poi.Prefab.Name, Round(p.Centre.X), Round(p.Centre.Y), Round(p.PadHeight),
            p.QuarterTurns * 90, p.Poi.Prefab.Size is { } s ? [s.X, s.Y, s.Z] : null, p.Radius, gen.BiomeAt(p.Centre.X, p.Centre.Y).Id,
            gen.Roads.Connected.TryGetValue(p.Index, out var kind) ? kind == RoadKind.Road ? RoadLinks.Road : RoadLinks.Track : RoadLinks.None)).ToList();
        var roads = gen.Roads.Chains.Select(c => new ManifestRoad(c.Index, c.Kind == RoadKind.Road ? RoadLinks.Road : RoadLinks.Track, c.Width,
            Round(c.Length), Node(gen, c.From), Node(gen, c.To),
            [.. Enumerable.Range(0, c.Points.Length).Select(i => new[] { Round(c.Points[i].X), Round(c.Heights[i]), Round(c.Points[i].Y) })],
            BridgeSpans(c))).ToList();
        var rivers = gen.Rivers.Select(r => (IReadOnlyList<float[]>)[.. r.Select(p => new[] { Round(p.X), Round(p.Y) })]).ToList();
        var (w, h) = (Math.Max(1, (int)(gen.WorldSize / sampleStep)), Math.Max(1, (int)(gen.WorldDepth / sampleStep)));
        var counts = new int[gen.Data.Biomes.Count];
        var water = 0;
        for (var j = 0; j < h; j++)
        {
            for (var i = 0; i < w; i++)
            {
                float x = -gen.WorldSize / 2 + (i + 0.5f) * sampleStep, z = -gen.WorldDepth / 2 + (j + 0.5f) * sampleStep;
                if (gen.Data.Biomes.Count > 0)
                    counts[gen.BiomeAt(x, z).Index]++;
                if (gen.HeightAt(x, z) < gen.SeaLevel)
                    water++;
            }
        }
        var total = (float)(w * h);
        var shares = gen.Data.Biomes.Where(b => counts[b.Index] > 0).ToDictionary(b => b.Id, b => MathF.Round(counts[b.Index] / total, 4));
        return new WorldManifest(gen.Seed, gen.WorldSize, gen.WorldDepth, gen.SeaLevel, gen.Spawn is { } spawn ? [Round(spawn.X), Round(spawn.Y), Round(spawn.Z)] : null,
            shares, MathF.Round(water / total, 4), Round(gen.Roads.LengthOf(RoadKind.Road)), Round(gen.Roads.LengthOf(RoadKind.Track)), pois, roads, rivers);
    }

    /// <summary>The manifest as pretty-printed JSON with a stable key order.</summary>
    public static string ToJson(WorldManifest manifest) => JsonSerializer.Serialize(manifest, Options) + "\n";

    /// <summary>
    /// The world's ground height every <paramref name="metresPerPixel"/> metres, row by row from the north-west corner
    /// (<paramref name="width"/> by <paramref name="height"/> samples).
    /// </summary>
    public static float[] Heights(WorldGenerator gen, float metresPerPixel, out int width, out int height)
    {
        var (w, h) = Dimensions(gen, metresPerPixel);
        (width, height) = (w, h);
        var heights = new float[w * h];
        Parallel.For(0, h, j =>
        {
            for (var i = 0; i < w; i++)
            {
                var (x, z) = Centre(gen, metresPerPixel, i, j);
                heights[j * w + i] = gen.HeightAt(x, z);
            }
        });
        return heights;
    }

    /// <summary>Greyscale bytes of <paramref name="heights"/>: black at the sea floor, white at the highest ground.</summary>
    public static byte[] HeightImage(float[] heights, out float low, out float high)
    {
        (low, high) = (heights.Min(), heights.Max());
        var (l, span) = (low, MathF.Max(1e-3f, high - low));
        return [.. heights.Select(v => (byte)Math.Clamp((int)MathF.Round((v - l) / span * 255f), 0, 255))];
    }

    /// <summary>RGB bytes of the biome map: each biome's colour, with water in blue.</summary>
    public static byte[] BiomeImage(WorldGenerator gen, float metresPerPixel, out int width, out int height)
    {
        var (w, h) = Dimensions(gen, metresPerPixel);
        (width, height) = (w, h);
        var rgb = new byte[w * h * 3];
        Parallel.For(0, h, j =>
        {
            for (var i = 0; i < w; i++)
            {
                var (x, z) = Centre(gen, metresPerPixel, i, j);
                Put(rgb, (j * w + i) * 3, gen.Data.Biomes.Count > 0 ? gen.BiomeAt(x, z).Color : new Vector3(0.4f));
            }
        });
        return rgb;
    }

    /// <summary>
    /// RGB bytes of a top-down overview like the spec's reference map: biome colours lit from the north-west, water,
    /// rivers, roads (bridges pale), dirt tracks and every POI's footprint (mandatory sites in red, scattered ones in yellow).
    /// </summary>
    public static byte[] OverviewImage(WorldGenerator gen, float[] heights, float metresPerPixel, int width, int height)
    {
        var rgb = new byte[width * height * 3];
        var deep = new Vector3(0.10f, 0.28f, 0.52f);
        var shallow = new Vector3(0.33f, 0.62f, 0.82f);
        Parallel.For(0, height, j =>
        {
            for (var i = 0; i < width; i++)
            {
                var (x, z) = Centre(gen, metresPerPixel, i, j);
                var h = heights[j * width + i];
                Vector3 c;
                if (h < gen.SeaLevel)
                {
                    c = Vector3.Lerp(shallow, deep, Math.Clamp((gen.SeaLevel - h) / 15f, 0f, 1f));
                }
                else
                {
                    c = gen.Data.Biomes.Count > 0 ? gen.BiomeAt(x, z).Color : new Vector3(0.45f, 0.6f, 0.3f);
                    var east = heights[j * width + Math.Min(width - 1, i + 1)] - heights[j * width + Math.Max(0, i - 1)];
                    var south = heights[Math.Min(height - 1, j + 1) * width + i] - heights[Math.Max(0, j - 1) * width + i];
                    var light = 1f - 1.1f * (east + south) / (2f * metresPerPixel);
                    c *= Math.Clamp(light, 0.45f, 1.35f);
                    // High ground fades towards pale rock.
                    c = Vector3.Lerp(c, new Vector3(0.82f), Math.Clamp((h - gen.SeaLevel - 60f) / 200f, 0f, 0.5f));
                }
                if (gen.Roads.Nearest(x, z, MathF.Max(3f, 0.75f * metresPerPixel)) is { } road
                    && road.Distance <= MathF.Max(road.Chain.Width / 2f, 0.75f * metresPerPixel))
                {
                    c = road.Chain.Kind == RoadKind.Track ? Vector3.Lerp(c, new Vector3(0.55f, 0.42f, 0.27f), 0.8f)
                        : road.OnBridge ? new Vector3(0.93f, 0.92f, 0.88f) : new Vector3(0.16f, 0.16f, 0.18f);
                }
                Put(rgb, (j * width + i) * 3, c);
            }
        });
        // POI footprints as outlined squares of their built size (or radius when they give none).
        foreach (var poi in gen.Pois)
        {
            var colour = poi.Site is not null ? new Vector3(0.9f, 0.15f, 0.12f) : new Vector3(0.98f, 0.86f, 0.15f);
            var size = poi.Poi.Prefab.Size;
            var (hx, hz) = size is { } s ? (s.X / 2f, s.Z / 2f) : (poi.Radius * 0.5f, poi.Radius * 0.5f);
            if (poi.QuarterTurns % 2 == 1)
                (hx, hz) = (hz, hx);
            int i0 = Pixel(poi.Centre.X - hx, -gen.WorldSize / 2, metresPerPixel), i1 = Pixel(poi.Centre.X + hx, -gen.WorldSize / 2, metresPerPixel);
            int j0 = Pixel(poi.Centre.Y - hz, -gen.WorldDepth / 2, metresPerPixel), j1 = Pixel(poi.Centre.Y + hz, -gen.WorldDepth / 2, metresPerPixel);
            // At least a few pixels across, so small POIs show at the scale of the whole map.
            (i0, i1) = Widen(i0, i1);
            (j0, j1) = Widen(j0, j1);
            for (var j = j0; j <= j1; j++)
            {
                for (var i = i0; i <= i1; i++)
                {
                    if (i < 0 || j < 0 || i >= width || j >= height)
                        continue;
                    var edge = i == i0 || i == i1 || j == j0 || j == j1;
                    var at = (j * width + i) * 3;
                    if (edge)
                        Put(rgb, at, colour);
                    else
                        Put(rgb, at, Vector3.Lerp(new Vector3(rgb[at], rgb[at + 1], rgb[at + 2]) / 255f, new Vector3(0.62f, 0.6f, 0.56f), 0.6f));
                }
            }
        }
        return rgb;
    }

    private static (int, int) Widen(int a, int b) => b - a >= 3 ? (a, b) : ((a + b) / 2 - 1, (a + b) / 2 + 2);

    private static int Pixel(float v, float min, float metresPerPixel) => (int)MathF.Floor((v - min) / metresPerPixel);

    private static (int W, int H) Dimensions(WorldGenerator gen, float metresPerPixel) =>
        (Math.Max(1, (int)MathF.Round(gen.WorldSize / metresPerPixel)), Math.Max(1, (int)MathF.Round(gen.WorldDepth / metresPerPixel)));

    private static (float X, float Z) Centre(WorldGenerator gen, float metresPerPixel, int i, int j) =>
        (-gen.WorldSize / 2 + (i + 0.5f) * metresPerPixel, -gen.WorldDepth / 2 + (j + 0.5f) * metresPerPixel);

    private static void Put(byte[] rgb, int at, Vector3 c)
    {
        rgb[at] = (byte)Math.Clamp((int)MathF.Round(c.X * 255f), 0, 255);
        rgb[at + 1] = (byte)Math.Clamp((int)MathF.Round(c.Y * 255f), 0, 255);
        rgb[at + 2] = (byte)Math.Clamp((int)MathF.Round(c.Z * 255f), 0, 255);
    }

    private static float Round(float v) => MathF.Round(v, 2);

    private static ManifestRoadEnd Node(WorldGenerator gen, int index)
    {
        var node = gen.Roads.Nodes[index];
        var site = node.Poi >= 0 ? gen.Pois[node.Poi].Site?.Id : null;
        return new ManifestRoadEnd(node.Kind.ToString().ToLowerInvariant(), Round(node.At.X), Round(node.At.Y), node.Poi >= 0 ? node.Poi : null, site);
    }

    // [from, to] metres along the chain of each bridge.
    private static IReadOnlyList<float[]> BridgeSpans(RoadChain chain)
    {
        var spans = new List<float[]>();
        var start = -1;
        for (var i = 0; i <= chain.Points.Length; i++)
        {
            var on = i < chain.Points.Length && chain.Bridge[i];
            if (on && start < 0)
                start = i;
            else if (!on && start >= 0)
            {
                spans.Add([Round(chain.Along[start]), Round(chain.Along[i - 1])]);
                start = -1;
            }
        }
        return spans;
    }
}

/// <summary>The world's placement manifest (<see cref="WorldExport.Manifest"/>).</summary>
/// <param name="Seed">World seed.</param>
/// <param name="WorldSize">East–west extent in metres, centred on the origin (x east).</param>
/// <param name="WorldDepth">North–south extent in metres, centred on the origin (z south).</param>
/// <param name="SeaLevel">Water surface height in metres.</param>
/// <param name="Spawn">Where players start, <c>[x, y, z]</c>.</param>
/// <param name="BiomeShares">Share of the world's area each biome covers (0 to 1).</param>
/// <param name="WaterShare">Share of the area under water (sea, lakes and rivers).</param>
/// <param name="RoadLength">Metres of paved road.</param>
/// <param name="TrackLength">Metres of dirt track.</param>
/// <param name="Pois">Every POI.</param>
/// <param name="Roads">Every road and track as a sampled, graded centre line.</param>
/// <param name="Rivers">Every river's centre line, <c>[x, z]</c> points from source to mouth.</param>
public sealed record WorldManifest(long Seed, float WorldSize, float WorldDepth, float SeaLevel, float[]? Spawn, IReadOnlyDictionary<string, float> BiomeShares,
    float WaterShare, float RoadLength, float TrackLength, IReadOnlyList<ManifestPoi> Pois, IReadOnlyList<ManifestRoad> Roads, IReadOnlyList<IReadOnlyList<float[]>> Rivers);

/// <summary>One POI in the manifest.</summary>
/// <param name="Index">Its index, which road ends refer to.</param>
/// <param name="Site">Site id for a mandatory structure (<c>poi_36</c>), null for a scattered POI.</param>
/// <param name="Label">The design spec's label (<c>POI-36</c>).</param>
/// <param name="Poi">Qualified prefab id.</param>
/// <param name="Name">Prefab display name.</param>
/// <param name="X">Centre x (east) in metres.</param>
/// <param name="Z">Centre z (south) in metres.</param>
/// <param name="PadHeight">Height of its flattened pad in metres.</param>
/// <param name="Yaw">Turn in degrees.</param>
/// <param name="Size">Footprint as built, <c>[width, height, depth]</c> in metres, before the turn.</param>
/// <param name="Radius">Footprint radius including terrain blending.</param>
/// <param name="Biome">Biome at the centre.</param>
/// <param name="RoadLink">What connects it: <c>road</c>, <c>track</c> or <c>none</c>.</param>
public sealed record ManifestPoi(int Index, string? Site, string? Label, string Poi, string? Name, float X, float Z, float PadHeight, int Yaw, float[]? Size, float Radius,
    string Biome, string RoadLink);

/// <summary>One road or track in the manifest.</summary>
/// <param name="Index">Chain index.</param>
/// <param name="Kind"><c>road</c> or <c>track</c>.</param>
/// <param name="Width">Width in metres.</param>
/// <param name="Length">Length in metres.</param>
/// <param name="From">Where it starts.</param>
/// <param name="To">Where it ends.</param>
/// <param name="Points">Centre line samples, <c>[x, y, z]</c>, y the road surface.</param>
/// <param name="Bridges">Bridges as <c>[from, to]</c> metres along it.</param>
public sealed record ManifestRoad(int Index, string Kind, float Width, float Length, ManifestRoadEnd From, ManifestRoadEnd To, IReadOnlyList<float[]> Points,
    IReadOnlyList<float[]> Bridges);

/// <summary>One end of a road in the manifest.</summary>
/// <param name="Kind"><c>junction</c>, <c>end</c> (at a POI or a dead end) or <c>join</c> (a track meeting a road).</param>
/// <param name="X">x in metres.</param>
/// <param name="Z">z in metres.</param>
/// <param name="Poi">Index of the POI it serves, if any.</param>
/// <param name="Site">That POI's site id, if it is a mandatory structure.</param>
public sealed record ManifestRoadEnd(string Kind, float X, float Z, int? Poi, string? Site);
