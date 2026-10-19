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
