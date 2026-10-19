using System.Numerics;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

public class RoadNetworkTests
{
    private static readonly Lazy<WorldGenerator> Vanilla = new(() =>
        new WorldGenerator(WorldGenData.Load(ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"))), 1234, 12032f, 9024f));

    [Fact]
    public void EverySiteThatAsksForARoadOrTrackHasOne()
    {
        var gen = Vanilla.Value;
        foreach (var poi in gen.Pois.Where(p => p.Site is not null))
        {
            var link = poi.Site!.Definition.Road ?? poi.Poi.Prefab.Road ?? RoadLinks.None;
            if (link == RoadLinks.Road)
                Assert.True(gen.Roads.Connected.GetValueOrDefault(poi.Index, (RoadKind)(-1)) == RoadKind.Road, $"{poi.Site.Id} ({poi.Poi.QualifiedId}) is on a road");
            else if (link == RoadLinks.Track)
                Assert.True(gen.Roads.Connected.ContainsKey(poi.Index), $"{poi.Site.Id} ({poi.Poi.QualifiedId}) has a track");
        }
        // Spec §3: paved roads 8-12 m wide, and the network is long (1.6-1.8× Scrap Mechanic's over twice the area).
        Assert.InRange(gen.Roads.Definition!.Width, 8f, 12f);
        Assert.True(gen.Roads.LengthOf(RoadKind.Road) > 30_000f, $"road length {gen.Roads.LengthOf(RoadKind.Road)} m");
        Assert.True(gen.Roads.LengthOf(RoadKind.Track) > 10_000f, $"track length {gen.Roads.LengthOf(RoadKind.Track)} m");
    }

    [Fact]
    public void TheRoadsAreOneNetwork()
    {
        var gen = Vanilla.Value;
        var roads = gen.Roads;
        // Nodes joined by a road, and every end at the same POI, are one place.
        var parent = Enumerable.Range(0, roads.Nodes.Count).ToArray();
        int Root(int i) => parent[i] == i ? i : parent[i] = Root(parent[i]);
        foreach (var chain in roads.Chains.Where(c => c.Kind == RoadKind.Road))
            parent[Root(chain.From)] = Root(chain.To);
        foreach (var group in roads.Nodes.Where(n => n.Poi >= 0).GroupBy(n => n.Poi))
            foreach (var node in group.Skip(1))
                parent[Root(node.Index)] = Root(group.First().Index);
        var served = roads.Chains.Where(c => c.Kind == RoadKind.Road).SelectMany(c => new[] { c.From, c.To }).Select(Root).Distinct().ToList();
        Assert.Single(served);
        // Spec §3: junctions along the way, and bridges over the water.
        Assert.True(roads.Nodes.Count(n => n.Kind == RoadNodeKind.Junction) >= 10, "junctions");
        Assert.Contains(roads.Chains, c => c.Kind == RoadKind.Road && c.Bridge.Any(b => b));
    }

    [Fact]
    public void JunctionsAreTeesAndCrossroads()
    {
        var roads = Vanilla.Value.Roads;
        var hw = roads.Definition!.Width / 2f;
        foreach (var node in roads.Nodes.Where(n => n.Kind == RoadNodeKind.Junction))
        {
            // Every road leaves down its own arm, at right angles to the others, level and straight across the plate.
            var used = 0;
            foreach (var chain in roads.Chains.Where(c => c.Kind == RoadKind.Road && (c.From == node.Index || c.To == node.Index)))
            {
                foreach (var atStart in new[] { true, false }.Where(s => (s ? chain.From : chain.To) == node.Index))
                {
                    var points = atStart ? chain.Points : [.. chain.Points.Reverse()];
                    var heights = atStart ? chain.Heights : [.. chain.Heights.Reverse()];
                    var arm = Enumerable.Range(0, 4).Where(k => (node.Arms & 1 << k) != 0)
                        .MaxBy(k => Vector2.Dot(node.Arm(k), Vector2.Normalize(points[2] - node.At)));
                    Assert.True((used & 1 << arm) == 0, $"junction at {node.At}: one road to an arm");
                    used |= 1 << arm;
                    for (var i = 0; Vector2.Distance(points[i], node.At) <= node.Radius + 0.01f; i++)
                    {
                        var off = points[i] - node.At;
                        var across = MathF.Abs(off.X * node.Arm(arm).Y - off.Y * node.Arm(arm).X);
                        Assert.True(across < 0.01f && Vector2.Dot(off, node.Arm(arm)) >= -0.01f, $"junction at {node.At}: the road runs down its arm");
                        Assert.True(MathF.Abs(heights[i] - node.Height) < 0.01f, $"junction at {node.At}: level across the plate");
                    }
                }
            }
            Assert.Equal(node.Arms, used);
            Assert.InRange(System.Numerics.BitOperations.PopCount((uint)node.Arms), 2, 4);
            // The plate's mouths are the road's width, so the asphalt meets it edge to edge.
            var outline = roads.PlateOutline(node);
            for (var k = 0; k < 4; k++)
            {
                if ((node.Arms & 1 << k) == 0)
                    continue;
                var across = new Vector2(-node.Arm(k).Y, node.Arm(k).X);
                foreach (var side in new[] { -1f, 1f })
                    Assert.Contains(outline, p => Vector2.Distance(p, node.At + node.Arm(k) * node.Radius + across * side * hw) < 0.01f);
            }
            Assert.Equal(0f, roads.OffPlate(node, node.At));
        }
    }

    [Fact]
    public void TracksMeetTheirRoadsAtRightAngles()
    {
        var roads = Vanilla.Value.Roads;
        var tracks = roads.Chains.Where(c => c.Kind == RoadKind.Track).ToList();
        var square = 0;
        foreach (var track in tracks)
        {
            var end = track.Points[^1];
            var hit = roads.Chains.Where(c => c != track)
                .SelectMany(c => Enumerable.Range(0, c.Points.Length - 1).Select(i => (c, i)))
                .MinBy(s => Vector2.Distance((s.c.Points[s.i] + s.c.Points[s.i + 1]) / 2f, end));
            var road = Vector2.Normalize(hit.c.Points[hit.i + 1] - hit.c.Points[hit.i]);
            var arrive = Vector2.Normalize(track.Points[^1] - track.Points[^3]);
            if (MathF.Abs(Vector2.Dot(road, arrive)) < 0.17f)
                square++;
        }
        // A few join as they come rather than cross another POI's ground.
        Assert.True(square >= tracks.Count * 0.9f, $"{square} of {tracks.Count} tracks meet their road square");
    }

    [Fact]
    public void RoadsKeepToTheirGradeAndBridgesClearTheWater()
    {
        var gen = Vanilla.Value;
        var def = gen.Roads.Definition!;
        foreach (var chain in gen.Roads.Chains)
        {
            var max = chain.Kind == RoadKind.Road ? def.MaxGrade : def.TrackMaxGrade;
            for (var i = 1; i < chain.Points.Length; i++)
            {
                var run = chain.Along[i] - chain.Along[i - 1];
                Assert.True(MathF.Abs(chain.Heights[i] - chain.Heights[i - 1]) <= max * run + 1e-3f,
                    $"chain {chain.Index} at {chain.Points[i]}: {MathF.Abs(chain.Heights[i] - chain.Heights[i - 1]) / run:0.###} over {max}");
                if (chain.Kind == RoadKind.Road && chain.Bridge[i])
                    Assert.True(chain.Heights[i] >= gen.SeaLevel + 0.5f, $"bridge deck at {chain.Points[i]} stands over the water");
            }
        }
    }

    [Fact]
    public void TheGroundMeetsTheRoad()
    {
        var gen = Vanilla.Value;
        var checkedPoints = 0;
        foreach (var chain in gen.Roads.Chains.Where(c => c.Kind == RoadKind.Road))
        {
            for (var i = 3; i < chain.Points.Length - 3; i += 5)
            {
                if (chain.Bridge[i] || gen.Roads.Nodes.Any(n => Vector2.Distance(n.At, chain.Points[i]) < 30f) || gen.PoisAt(chain.Points[i].X, chain.Points[i].Y).Any())
                    continue;
                var p = chain.Points[i];
                Assert.True(MathF.Abs(gen.HeightAt(p.X, p.Y) - chain.Heights[i]) < 0.3f, $"ground under the road at {p} is {gen.HeightAt(p.X, p.Y)}, road {chain.Heights[i]}");
                checkedPoints++;
            }
        }
        Assert.True(checkedPoints > 1000);
    }

    [Fact]
    public void SameSeedLaysTheSameRoads()
    {
        var data = Vanilla.Value.Data;
        var again = new WorldGenerator(data, 1234, 12032f, 9024f);
        Assert.Equal(Vanilla.Value.Roads.Chains.Select(c => (c.Kind, c.Length)), again.Roads.Chains.Select(c => (c.Kind, c.Length)));
        Assert.Equal(Vanilla.Value.Roads.Chains.SelectMany(c => c.Points), again.Roads.Chains.SelectMany(c => c.Points));
    }

    [Fact]
    public void RoadsOnLandHaveNoCurbsAndNothingGrowsOnThem()
    {
        var gen = Vanilla.Value;
        Assert.Equal(0f, gen.Roads.Definition!.Curb);
        // The road out of the crash site, which grows its own meadow round it, and a stretch of road on open land.
        var crash = gen.Pois.First(p => p.Poi.Prefab.Id == "crash_site");
        var road = gen.Roads.Chains.Where(c => c.Kind == RoadKind.Road)
            .OrderBy(c => c.Points.Min(p => Vector2.Distance(p, crash.Centre))).First();
        var coords = road.Points.Where((p, i) => !road.Bridge[i]).Take(60)
            .Select(p => new ChunkCoord((int)MathF.Floor(p.X / 64f), (int)MathF.Floor(p.Y / 64f))).Distinct().ToList();
        var plants = 0;
        foreach (var coord in coords)
        {
            var chunk = gen.GenerateChunk(coord, 64f);
            // Only the asphalt: no concrete curbs off the bridges.
            if (!chunk.Roads.Surfaces.Any(s => s.Material == RoadMaterial.Hazard))
                Assert.DoesNotContain(chunk.Roads.Surfaces, s => s.Material == RoadMaterial.Concrete);
            foreach (var kind in new[] { FloraKind.Grass, FloraKind.Bush })
            {
                foreach (var plant in FloraScatter.Grow(gen, chunk, kind, 1f))
                {
                    plants++;
                    Assert.False(gen.Roads.Covers(plant.Position.X, plant.Position.Z), $"a {kind} grows on the road at {plant.Position}");
                }
            }
        }
        Assert.True(plants > 0, "flora grows beside the road");
    }

    [Fact]
    public void RoadMeshesFaceUpAndBridgesHaveTheirFurniture()
    {
        var gen = Vanilla.Value;
        var bridge = gen.Roads.Chains.First(c => c.Kind == RoadKind.Road && c.Bridge.Count(b => b) > 8);
        var middle = bridge.Points[Array.IndexOf(bridge.Bridge, true) + 4];
        var coord = new ChunkCoord((int)MathF.Floor(middle.X / 64f), (int)MathF.Floor(middle.Y / 64f));
        var chunk = gen.GenerateChunk(coord, 64f);
        var materials = chunk.Roads.Surfaces.Select(s => s.Material).ToHashSet();
        Assert.Contains(RoadMaterial.Asphalt, materials);
        Assert.Contains(RoadMaterial.Concrete, materials);
        var asphalt = chunk.Roads.Surfaces.Single(s => s.Material == RoadMaterial.Asphalt);
        Assert.Equal(asphalt.Vertices.Count, asphalt.Uv2.Count);
        for (var t = 0; t < asphalt.Vertices.Count; t += 3)
        {
            var (a, b, c) = (asphalt.Vertices[t], asphalt.Vertices[t + 1], asphalt.Vertices[t + 2]);
            Assert.True(asphalt.Normals[t].Y > 0.8f, "the road surface faces up");
            // Godot draws triangles clockwise seen from the front.
            Assert.True(Vector3.Dot(Vector3.Cross(c - a, b - a), asphalt.Normals[t]) >= 0f);
        }
        // Somewhere along the network, bridges have hazard posts and caution signs at their ends.
        var all = new HashSet<RoadMaterial>();
        foreach (var k in Enumerable.Range(0, bridge.Points.Length).Where(k => k > 0 && bridge.Bridge[k] != bridge.Bridge[k - 1]))
        {
            var p = bridge.Points[k];
            for (var dz = -1; dz <= 1; dz++)
                for (var dx = -1; dx <= 1; dx++)
                    all.UnionWith(gen.GenerateChunk(new ChunkCoord((int)MathF.Floor(p.X / 64f) + dx, (int)MathF.Floor(p.Y / 64f) + dz), 64f).Roads.Surfaces.Select(s => s.Material));
        }
        Assert.Contains(RoadMaterial.Hazard, all);
        Assert.Contains(RoadMaterial.Sign, all);
    }

    [Fact]
    public void WheelsRollOntoTheRoadsFromTheSide()
    {
        // Walking out from the asphalt over its edge onto the shoulder, the surface a wheel rolls on (the road mesh or the
        // ground, whichever is higher) never steps: the asphalt's sides, its ends and the junction plates slope down under
        // the levelled ground instead of standing proud of it with a bare lip.
        var gen = Vanilla.Value;
        var roads = gen.Roads;
        var hw = roads.Definition!.Width / 2f;
        var chunks = new Dictionary<ChunkCoord, (TerrainChunkData Data, List<(Vector3 A, Vector3 B, Vector3 C)> Faces)>();
        var crossed = 0;
        void Walk(Vector2 from, Vector2 outward, float length, string what)
        {
            const float step = 0.02f;
            var last = Top(from);
            for (var d = step; d <= length; d += step)
            {
                var p = from + outward * d;
                var top = Top(p);
                Assert.True(MathF.Abs(top - last) <= 0.4f * step + 0.004f, $"{what}: the surface steps {(top - last) * 100f:F1} cm at {p} ({d:F2} m out)");
                last = top;
            }
            crossed++;
        }
        (TerrainChunkData Data, List<(Vector3 A, Vector3 B, Vector3 C)> Faces) Chunk(ChunkCoord coord)
        {
            if (!chunks.TryGetValue(coord, out var chunk))
            {
                var data = gen.GenerateChunk(coord, 64f);
                var origin = new Vector3(coord.X * 64f, 0f, coord.Z * 64f);
                var faces = new List<(Vector3, Vector3, Vector3)>();
                foreach (var surface in data.Roads.Surfaces.Where(s => s.Solid))
                    for (var t = 0; t < surface.Vertices.Count; t += 3)
                        faces.Add((surface.Vertices[t] + origin, surface.Vertices[t + 1] + origin, surface.Vertices[t + 2] + origin));
                chunks[coord] = chunk = (data, faces);
            }
            return chunk;
        }
        // Road pieces belong to the chunk their middle is in and can reach over its border, so look in the neighbours too.
        float Top(Vector2 p)
        {
            var coord = new ChunkCoord((int)MathF.Floor(p.X / 64f), (int)MathF.Floor(p.Y / 64f));
            var top = Ground(Chunk(coord).Data, p.X - coord.X * 64f, p.Y - coord.Z * 64f);
            for (var dz = -1; dz <= 1; dz++)
                for (var dx = -1; dx <= 1; dx++)
                    foreach (var (a, b, c) in Chunk(new ChunkCoord(coord.X + dx, coord.Z + dz)).Faces)
                        if (HeightIn(a, b, c, p.X, p.Y) is { } h)
                            top = MathF.Max(top, h);
            return top;
        }

        // Straight-ish stretches on land, away from junctions and bridges, both sides.
        foreach (var chain in roads.Chains.Where(c => c.Kind == RoadKind.Road).Take(12))
        {
            for (var i = 3; i + 3 < chain.Points.Length; i += 9)
            {
                if (chain.Bridge.Skip(i - 3).Take(7).Any(b => b) || roads.Nodes.Any(n => n.Kind == RoadNodeKind.Junction && Vector2.Distance(n.At, chain.Points[i]) < n.Radius + 2f * chain.Width)
                    || Crowded(roads, chain, i))
                    continue;
                var tangent = Vector2.Normalize(chain.Points[i + 1] - chain.Points[i - 1]);
                var right = new Vector2(-tangent.Y, tangent.X);
                var mid = (chain.Points[i] + chain.Points[i + 1]) / 2f;
                foreach (var side in new[] { -1f, 1f })
                    Walk(mid + right * side * (hw - 1f), right * side, 3f, $"the side of road {chain.Index}");
            }
            // A road's end at a POI.
            foreach (var (node, end, inward) in new[] { (chain.From, chain.Points[0], chain.Points[1]), (chain.To, chain.Points[^1], chain.Points[^2]) })
            {
                if (roads.Nodes[node].Kind != RoadNodeKind.End || chain.Bridge[node == chain.From ? 0 : ^1])
                    continue;
                var off = Vector2.Normalize(end - inward);
                Walk(end - off, off, 2.5f, $"the end of road {chain.Index}");
            }
        }
        // Junction plates, out through their rounded corners and their sides where no road leaves.
        foreach (var node in roads.Nodes.Where(n => n.Kind == RoadNodeKind.Junction && n.Arms != 0).Take(10))
        {
            for (var k = 0; k < 4; k++)
            {
                var (u, v) = (node.Arm(k), node.Arm(k + 1));
                var corner = Vector2.Normalize(u + v);
                var rim = 40f - roads.OffPlate(node, node.At + corner * 40f);
                Walk(node.At + corner * (rim - 1f), corner, 3f, $"the corner of junction {node.Index}");
            }
        }
        Assert.True(crossed > 100, $"crossed {crossed} edges");
    }

    // True where another road, or this one further along (a switchback's other leg), passes within two road widths.
    private static bool Crowded(RoadNetwork roads, RoadChain chain, int i) =>
        roads.Chains.Where(c => c.Kind == RoadKind.Road).Any(c => c.Points.Where((q, k) =>
            (c != chain || MathF.Abs(c.Along[k] - chain.Along[i]) > 3f * chain.Width) && Vector2.Distance(q, chain.Points[i]) < 2f * chain.Width).Any());

    // Height of the ground at chunk-local x/z, between its samples as the height map collides.
    private static float Ground(TerrainChunkData chunk, float x, float z)
    {
        var (fx, fz) = (x / chunk.Resolution, z / chunk.Resolution);
        var (i, j) = (Math.Clamp((int)fx, 0, chunk.Samples - 2), Math.Clamp((int)fz, 0, chunk.Samples - 2));
        var (tx, tz) = (fx - i, fz - j);
        float H(int a, int b) => chunk.Heights[(j + b) * chunk.Samples + i + a];
        return (H(0, 0) * (1 - tx) + H(1, 0) * tx) * (1 - tz) + (H(0, 1) * (1 - tx) + H(1, 1) * tx) * tz;
    }

    // Height of a triangle over x/z, or null when the point is outside it seen from above.
    private static float? HeightIn(Vector3 a, Vector3 b, Vector3 c, float x, float z)
    {
        var det = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
        if (MathF.Abs(det) < 1e-9f)
            return null;
        var l1 = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / det;
        var l2 = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / det;
        var l3 = 1f - l1 - l2;
        const float eps = -1e-5f;
        return l1 >= eps && l2 >= eps && l3 >= eps ? l1 * a.Y + l2 * b.Y + l3 * c.Y : null;
    }

    [Fact]
    public void TheManifestListsEveryPoiAndRoad()
    {
        var gen = Vanilla.Value;
        var manifest = WorldExport.Manifest(gen, 128f);
        Assert.Equal(gen.Pois.Count, manifest.Pois.Count);
        var crash = Assert.Single(manifest.Pois, p => p.Site == "poi_36");
        Assert.Equal(("POI-36", "vanilla:crash_site", RoadLinks.Road), (crash.Label, crash.Poi, crash.RoadLink));
        Assert.NotNull(crash.Size);
        Assert.Equal(gen.Roads.Chains.Count, manifest.Roads.Count);
        Assert.Contains(manifest.Roads, r => r.Bridges.Count > 0);
        Assert.Equal(gen.Rivers.Count, manifest.Rivers.Count);
        var json = WorldExport.ToJson(manifest);
        Assert.Contains("\"site\": \"poi_36\"", json);
        Assert.Contains("\"road_link\": \"road\"", json);
        Assert.Equal(json, WorldExport.ToJson(WorldExport.Manifest(gen, 128f)));

        var heights = WorldExport.Heights(gen, 256f, out var w, out var h);
        Assert.Equal((47, 35), (w, h));
        Assert.Equal(w * h * 3, WorldExport.OverviewImage(gen, heights, 256f, w, h).Length);
        Assert.Equal(w * h, WorldExport.HeightImage(heights, out var low, out var high).Length);
        Assert.True(low < gen.SeaLevel && high > 100f);
    }

    [Theory]
    [InlineData("""{ "biomes": [{ "id": "a" }], "regions": [{ "biome": "b", "at": [0, 0] }] }""", "$.regions[0].biome")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "regions": [{ "biome": "a", "at": [0.7, 0] }] }""", "$.regions[0].at")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "rivers": [{ "points": [[0, 0]], "width": [10, 20] }] }""", "$.rivers[0].points")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "sites": [{ "id": "s", "at": [0, 0] }] }""", "$.sites[0]")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "sites": [{ "id": "s", "poi": "hut", "at": [0, 0], "yaw": 45 }] }""", "$.sites[0].yaw")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "sites": [{ "id": "s", "poi": "hut", "at": [0, 0], "road": "rail" }] }""", "$.sites[0].road")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "roads": { "max_grade": 2 } }""", "$.roads.max_grade")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "roads": { "links": [["x", "y"]] } }""", "$.roads.links[0]")]
    public void BadLayoutGivesAClearError(string json, string path)
    {
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, json)
            .File("vanilla", "pois/hut.json", """{ "id": "hut", "radius": 10, "placement": {} }""");
        var data = WorldGenData.Load(mods.Load());
        Assert.Contains(data.Diagnostics, d => d.Code == DiagnosticCodes.WorldGenInvalid && d.JsonPath == path);
    }

    [Fact]
    public void APoolMustFillEverySiteThatDrawsFromIt()
    {
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, """
            { "biomes": [{ "id": "a" }], "sites": [{ "id": "s1", "pool": "p", "at": [0, 0] }, { "id": "s2", "pool": "p", "at": [0.2, 0] }],
              "pools": [{ "id": "p", "fill": [{ "poi": "hut", "count": 1 }] }] }
            """).File("vanilla", "pois/hut.json", """{ "id": "hut", "radius": 10, "placement": {} }""");
        var data = WorldGenData.Load(mods.Load());
        Assert.Contains(data.Diagnostics, d => d.Code == DiagnosticCodes.WorldGenInvalid && d.JsonPath == "$.pools[0].fill");
    }

    [Fact]
    public void SitesStandWhereTheLayoutSaysInEveryWorld()
    {
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, """
            { "biomes": [{ "id": "a", "height": 5 }],
              "sites": [{ "id": "home", "poi": "hut", "at": [0.25, -0.25] }, { "id": "away", "poi": "hut", "at": [-0.25, 0.25], "pad_height": 9 }] }
            """).File("vanilla", "pois/hut.json", """{ "id": "hut", "radius": 10, "placement": {}, "terrain": [{ "op": "flatten", "radius": 4, "blend": 4 }] }""");
        var data = WorldGenData.Load(mods.Load());
        Assert.Empty(data.Diagnostics);
        foreach (var seed in new long[] { 1, 2, 3 })
        {
            var gen = new WorldGenerator(data, seed, 2000f, 1000f);
            Assert.Equal(new Vector2(500, -250), gen.Pois.Single(p => p.Site?.Id == "home").Centre);
            var away = gen.Pois.Single(p => p.Site?.Id == "away");
            Assert.Equal((new Vector2(-500, 250), 9f), (away.Centre, away.PadHeight));
            Assert.Equal(9f, gen.HeightAt(-500, 250));
        }
    }
}
