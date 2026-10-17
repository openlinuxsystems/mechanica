using System.Collections.Concurrent;
using System.Numerics;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

public class WorldGenTests
{
    private const float WorldSize = 4096f;
    private const float ChunkSize = 64f;
    private const long Seed = 1234;

    private static readonly Lazy<WorldGenData> VanillaData = new(() => WorldGenData.Load(ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"))));
    private static readonly Lazy<WorldGenerator> Vanilla = new(() => new WorldGenerator(VanillaData.Value, Seed, WorldSize));

    [Fact]
    public void VanillaWorldDataLoadsCleanly()
    {
        var data = VanillaData.Value;

        Assert.Empty(data.Diagnostics);
        Assert.Equal(["meadow", "forest", "ruin_field", "warehouse_district", "coastal"], data.Biomes.Select(b => b.Id));
        Assert.Equal("coastal", data.CoastBiome?.Id);
        Assert.Equal(["vanilla:ai_core_facility", "vanilla:crash_site", "vanilla:faction_outpost", "vanilla:outpost", "vanilla:ruin_cluster", "vanilla:warehouse"], data.Pois.Select(p => p.QualifiedId));
        Assert.Contains("tree", data.Objects.Keys);
        Assert.Contains("rock_node", data.Objects.Keys);
        Assert.Equal(64, data.Fingerprint.Length);
    }

    [Fact]
    public void FourKilometreWorldGenerates()
    {
        var gen = Vanilla.Value;
        var chunks = (int)(WorldSize / ChunkSize);
        var ids = new ConcurrentDictionary<WorldObjectId, byte>();
        var objectsByBiome = new ConcurrentDictionary<string, int>();
        var (low, high) = (float.PositiveInfinity, float.NegativeInfinity);
        var gate = new object();
        Parallel.For(0, chunks * chunks, k =>
        {
            var coord = new ChunkCoord(k % chunks - chunks / 2, k / chunks - chunks / 2);
            var chunk = gen.GenerateChunk(coord, ChunkSize);
            Assert.Equal(65 * 65, chunk.Heights.Length);
            Assert.All(chunk.Heights, h => Assert.True(float.IsFinite(h)));
            Assert.All(chunk.Normals, n => Assert.True(n.Y > 0f && MathF.Abs(n.Length() - 1f) < 1e-4f));
            foreach (var o in chunk.Objects)
            {
                Assert.True(ids.TryAdd(o.Id, 0), $"object id {o.Id} is unique");
                objectsByBiome.AddOrUpdate(gen.BiomeAt(o.Position.X, o.Position.Z).Id, 1, (_, n) => n + 1);
            }
            lock (gate)
                (low, high) = (MathF.Min(low, chunk.MinHeight), MathF.Max(high, chunk.MaxHeight));
        });

        Assert.Equal(gen.Data.Terrain.SeaFloor, low, 0.5f);
        Assert.InRange(high, 10f, 80f);
        Assert.True(ids.Count > 20_000, $"objects scattered ({ids.Count})");
        Assert.True(objectsByBiome.GetValueOrDefault("forest") > objectsByBiome.GetValueOrDefault("meadow"), "forests are denser than meadows");
        foreach (var biome in gen.Data.Biomes)
            Assert.True(objectsByBiome.GetValueOrDefault(biome.Id) > 0, $"{biome.Id} has objects");
    }

    [Fact]
    public void EveryBiomeAppearsAndTheEdgeIsSea()
    {
        var gen = Vanilla.Value;
        var counts = new Dictionary<string, int>();
        var samples = 0;
        for (var z = -2040f; z < 2048f; z += 16f)
        {
            for (var x = -2040f; x < 2048f; x += 16f)
            {
                var id = gen.BiomeAt(x, z).Id;
                counts[id] = counts.GetValueOrDefault(id) + 1;
                samples++;
            }
        }
        foreach (var biome in gen.Data.Biomes)
            Assert.True(counts.GetValueOrDefault(biome.Id) > samples * 0.03, $"{biome.Id} covers over 3% ({counts.GetValueOrDefault(biome.Id)} of {samples})");

        for (var t = -2048f; t <= 2048f; t += 64f)
        {
            foreach (var (x, z) in new[] { (t, -2048f), (t, 2048f), (-2048f, t), (2048f, t) })
                Assert.True(gen.HeightAt(x, z) < gen.SeaLevel - 10f, $"world edge at ({x}, {z}) is under the sea");
        }
        Assert.True(gen.HeightAt(0, 0) > gen.SeaLevel, "the centre is land");
    }

    [Fact]
    public void SameSeedGivesTheSameWorldAndAnotherSeedDoesNot()
    {
        var a = new WorldGenerator(VanillaData.Value, 99, WorldSize);
        var b = new WorldGenerator(VanillaData.Value, 99, WorldSize);
        var c = new WorldGenerator(VanillaData.Value, 100, WorldSize);
        Assert.Equal(a.Pois.Select(p => (p.Poi.QualifiedId, p.Centre, p.PadHeight, p.QuarterTurns)), b.Pois.Select(p => (p.Poi.QualifiedId, p.Centre, p.PadHeight, p.QuarterTurns)));
        Assert.NotEqual(a.Pois.Select(p => p.Centre), c.Pois.Select(p => p.Centre));

        var coord = new ChunkCoord(7, -5);
        var (ca, cb, cc) = (a.GenerateChunk(coord, ChunkSize), b.GenerateChunk(coord, ChunkSize), c.GenerateChunk(coord, ChunkSize));
        Assert.Equal(ca.Heights, cb.Heights);
        Assert.Equal(ca.Objects.Select(o => (o.Id, o.Position, o.Yaw, o.Scale)), cb.Objects.Select(o => (o.Id, o.Position, o.Yaw, o.Scale)));
        Assert.NotEqual(ca.Heights, cc.Heights);
    }

    [Fact]
    public void NeighbouringChunksShareTheirBorder()
    {
        var gen = Vanilla.Value;
        var here = gen.GenerateChunk(new ChunkCoord(3, -2), ChunkSize);
        var east = gen.GenerateChunk(new ChunkCoord(4, -2), ChunkSize);
        var south = gen.GenerateChunk(new ChunkCoord(3, -1), ChunkSize);
        var n = here.Samples;
        for (var k = 0; k < n; k++)
        {
            Assert.Equal(here.HeightAt(n - 1, k), east.HeightAt(0, k));
            Assert.Equal(here.Normals[k * n + n - 1], east.Normals[k * n]);
            Assert.Equal(here.HeightAt(k, n - 1), south.HeightAt(k, 0));
            Assert.Equal(here.Normals[(n - 1) * n + k], south.Normals[k]);
        }
        var (ox, oz) = here.Coord.Origin(ChunkSize);
        Assert.Equal(gen.HeightAt(ox + 17, oz + 40), here.HeightAt(17, 40));
    }

    [Fact]
    public void ObjectsBelongToExactlyOneChunk()
    {
        var gen = Vanilla.Value;
        var whole = gen.ObjectsIn(256, 256, 384, 384).Select(o => o.Id).ToHashSet();
        var parts = new List<WorldObjectId>();
        for (var cz = 4; cz < 6; cz++)
            for (var cx = 4; cx < 6; cx++)
                parts.AddRange(gen.GenerateChunk(new ChunkCoord(cx, cz), ChunkSize).Objects.Select(o => o.Id));
        Assert.Equal(whole.Count, parts.Count);
        Assert.True(whole.SetEquals(parts));

        var some = gen.ObjectsIn(256, 256, 384, 384).First();
        Assert.Equal(some, gen.Find(some.Id));
        Assert.Null(gen.Find(new WorldObjectId(999, 0, 0)));
        Assert.Null(gen.Find(new WorldObjectId(0, 0, 0))); // inside the crash site
    }

    [Fact]
    public void CrashSiteIsAFixedFlatStartWithWaterNearby()
    {
        var gen = Vanilla.Value;
        var crash = Assert.Single(gen.Pois, p => p.Poi.QualifiedId == "vanilla:crash_site");
        Assert.Equal(Vector2.Zero, crash.Centre);
        Assert.Equal(0f, crash.PadHeight);
        Assert.Equal(new Vector3(0, 0, 4), gen.Spawn);
        for (var x = -20f; x <= 20f; x += 2f)
            for (var z = -20f; z <= 10f; z += 2f)
                Assert.Equal(0f, gen.HeightAt(x, z));
        Assert.True(gen.HeightAt(30, 26) < gen.SeaLevel - 1f, "a pond sits next to the crash site");
        // Its banks are gentle enough to walk out of (the player controller climbs up to 45°), so the first water a
        // new player meets is never a trap.
        for (var a = 0; a < 360; a += 10)
            for (var r = 0f; r <= 22f; r += 0.5f)
            {
                var (sin, cos) = MathF.SinCos(a * MathF.PI / 180f);
                float x = 30f + r * cos, z = 26f + r * sin;
                Assert.True(gen.SlopeAt(x, z) < 0.7f, $"pond bank at ({x:0.#}, {z:0.#}) has slope {gen.SlopeAt(x, z):0.##}");
            }
        Assert.Equal(3, crash.Poi.Prefab.Markers!.Count(m => m.Type == "fire"));
        Assert.Single(crash.Poi.Prefab.Markers!, m => m.Ref == "master_battery");
    }

    [Fact]
    public void PoisFollowTheirPlacementRules()
    {
        var gen = Vanilla.Value;
        var counts = gen.Pois.GroupBy(p => p.Poi.Prefab.Id!).ToDictionary(g => g.Key, g => g.Count());
        Assert.True(counts.GetValueOrDefault("outpost") >= 6, $"outposts placed ({counts.GetValueOrDefault("outpost")})");
        Assert.True(counts.GetValueOrDefault("warehouse") >= 3, $"warehouses placed ({counts.GetValueOrDefault("warehouse")})");
        Assert.True(counts.GetValueOrDefault("ruin_cluster") >= 6, $"ruins placed ({counts.GetValueOrDefault("ruin_cluster")})");

        foreach (var poi in gen.Pois)
        {
            foreach (var other in gen.Pois.Where(o => o != poi))
                Assert.True(Vector2.Distance(poi.Centre, other.Centre) >= poi.Radius + other.Radius, $"{poi.Poi.QualifiedId} and {other.Poi.QualifiedId} do not overlap");
            Assert.True(poi.PadHeight > gen.SeaLevel, $"{poi.Poi.QualifiedId} at {poi.Centre} is dry");

            if (poi.Poi.Prefab.Placement!.Spacing is not [var min, _])
                continue;
            Assert.Contains(gen.BiomeAt(poi.Centre.X, poi.Centre.Y).Index, poi.Poi.Biomes);
            var nearest = gen.Pois.Where(o => o != poi && o.Poi == poi.Poi).Min(o => Vector2.Distance(o.Centre, poi.Centre));
            Assert.True(nearest >= min, $"{poi.Poi.QualifiedId} at {poi.Centre}: nearest of its kind is {nearest} m, under {min} m");

            // The pad is level wherever no basin cuts into it.
            var flatten = poi.Poi.Prefab.Terrain!.First(t => t.Op == TerrainOpKind.Flatten);
            for (var a = 0; a < 8; a++)
            {
                var p = poi.ToWorld(new Vector2(a % 3 - 1, a / 3 - 1) * flatten.Radius * 0.6f);
                Assert.Equal(poi.PadHeight, gen.HeightAt(p.X, p.Y), 3);
            }
        }
    }

    [Fact]
    public void ObjectsStayOnDryGroundOutsidePoisInTheirBiome()
    {
        var gen = Vanilla.Value;
        var objects = gen.ObjectsIn(-1024, -1024, 1024, 1024).ToList();
        Assert.NotEmpty(objects);
        foreach (var o in objects)
        {
            var rule = gen.Data.ScatterRules[o.Id.Rule];
            Assert.Same(rule.Object, o.Definition);
            Assert.Equal(rule.Biome, gen.BiomeAt(o.Position.X, o.Position.Z).Index);
            Assert.Empty(gen.PoisAt(o.Position.X, o.Position.Z));
            Assert.True(gen.HeightAt(o.Position.X, o.Position.Z) > gen.SeaLevel);
        }
    }

    [Fact]
    public void FlatWorldIsFlatAndEmpty()
    {
        var gen = new WorldGenerator(WorldGenData.Flat, 5, WorldSize);
        var chunk = gen.GenerateChunk(new ChunkCoord(-3, 9), ChunkSize);
        Assert.All(chunk.Heights, h => Assert.Equal(0f, h));
        Assert.Empty(chunk.Objects);
        Assert.Empty(gen.Pois);
        Assert.False(chunk.HasWater);
        Assert.Null(gen.Spawn);
    }

    [Fact]
    public void ResolutionMustDivideTheChunk()
    {
        var gen = new WorldGenerator(WorldGenData.Flat, 5, WorldSize);
        Assert.Throws<ArgumentException>(() => gen.GenerateChunk(default, 64.5f));
    }

    [Fact]
    public void WorldStateRoundTrips()
    {
        var state = new WorldState(-77);
        Assert.True(state.Destroy(new WorldObjectId(3, -10, 4)));
        Assert.False(state.Destroy(new WorldObjectId(3, -10, 4)));
        state.Destroy(new WorldObjectId(0, 1, 2));
        var json = state.ToJson();
        Assert.Contains("\"destroyed\": [\n    \"0.1.2\",\n    \"3.-10.4\"\n  ]", json);

        var back = WorldState.FromJson(json);
        Assert.Equal(-77, back.Seed);
        Assert.True(back.IsDestroyed(new WorldObjectId(3, -10, 4)));
        Assert.Equal(2, back.Destroyed.Count);
        Assert.Equal(json, back.ToJson());
        Assert.Throws<FormatException>(() => WorldState.FromJson("""{ "seed": 1, "destroyed": ["x"] }"""));
        Assert.Throws<FormatException>(() => WorldState.FromJson("""{ "destroyed": [] }"""));
    }

    [Theory]
    [InlineData("""{ "biomes": [] }""", "$.biomes")]
    [InlineData("""{ "terrain": { "resolution": 0 }, "biomes": [{ "id": "a" }] }""", "$.terrain.resolution")]
    [InlineData("""{ "terrain": { "biome_blend": 500 }, "biomes": [{ "id": "a" }] }""", "$.terrain.biome_blend")]
    [InlineData("""{ "terrain": { "sea_floor": 5, "sea_level": 0 }, "biomes": [{ "id": "a" }] }""", "$.terrain.sea_floor")]
    [InlineData("""{ "biomes": [{ "id": "a", "color": "green" }] }""", "$.biomes[0].color")]
    [InlineData("""{ "biomes": [{ "id": "a", "scatter": [{ "object": "tree", "spacing": 5, "chance": 0.5 }] }] }""", "$.biomes[0].scatter[0].object")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "coast_biome": "sea" }""", "$.coast_biome")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "tree", "collider": { "shape": "sphere" } }] }""", "$.objects[0].collider.radius")]
    [InlineData("""{ "biomes": [{ "id": "a", "weight": 0 }] }""", "$.biomes")]
    public void BadWorldGenDataGivesAClearError(string json, string path)
    {
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, json);
        var data = WorldGenData.Load(mods.Load());
        Assert.Contains(data.Diagnostics, d => d.Code == DiagnosticCodes.WorldGenInvalid && d.JsonPath == path);
        Assert.Contains(data.Diagnostics, d => d.Code == DiagnosticCodes.WorldGenMissing);
        Assert.Same(WorldGenData.Flat.Definition, data.Definition);
    }

    [Theory]
    [InlineData("""{ "id": "p", "radius": 10 }""", "$.placement")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0], "spacing": [100, 200], "biomes": ["a"] } }""", "$.placement")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "spacing": [100, 200], "biomes": ["b"] } }""", "$.placement.biomes[0]")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "spacing": [300, 200], "biomes": ["a"] } }""", "$.placement.spacing")]
    [InlineData("""{ "id": "p", "radius": 100, "placement": { "spacing": [100, 200], "biomes": ["a"] } }""", "$.placement.spacing")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0], "yaw": 45 } }""", "$.placement.yaw")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "terrain": [{ "op": "flatten", "radius": 8, "blend": 5 }] }""", "$.terrain[0]")]
    [InlineData("""{ "id": "p", "radius": 20, "placement": { "fixed": [0, 0] }, "terrain": [{ "op": "basin", "radius": 4, "blend": 6, "depth": 7 }] }""", "$.terrain[0].blend")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "markers": [{ "type": "Bot" }] }""", "$.markers[0].type")]
    [InlineData("""{ "id": "P", "radius": 10, "placement": { "fixed": [0, 0] } }""", "$.id")]
    public void BadPoiGivesAClearErrorAndIsLeftOut(string json, string path)
    {
        using var mods = new TestMods().Mod("vanilla")
            .File("vanilla", WorldGenData.DefinitionFile, """{ "biomes": [{ "id": "a" }] }""")
            .File("vanilla", "pois/p.json", json);
        var data = WorldGenData.Load(mods.Load());
        Assert.Contains(data.Diagnostics, d => d.Code == DiagnosticCodes.PoiInvalid && d.JsonPath == path);
        Assert.Empty(data.Pois);
    }

    [Fact]
    public void LaterModsReplaceTheRulesAndAddPois()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("islands", dependencies: """[{ "id": "vanilla", "version": ">=1.0.0" }]""")
            .File("vanilla", WorldGenData.DefinitionFile, """{ "biomes": [{ "id": "a" }] }""")
            .File("islands", WorldGenData.DefinitionFile, """{ "biomes": [{ "id": "b", "height": 3 }] }""")
            .File("islands", "pois/hut.json", """{ "id": "hut", "radius": 8, "placement": { "fixed": [100, 100] }, "terrain": [{ "op": "flatten", "radius": 4, "blend": 4 }] }""");
        var data = WorldGenData.Load(mods.Load());
        Assert.Empty(data.Diagnostics);
        Assert.Equal(["b"], data.Biomes.Select(b => b.Id));
        var hut = Assert.Single(new WorldGenerator(data, 1, WorldSize).Pois);
        Assert.Equal("islands:hut", hut.Poi.QualifiedId);
        Assert.Equal(3f, hut.PadHeight, 4);
    }
}
