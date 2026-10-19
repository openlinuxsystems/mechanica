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
    private static readonly Lazy<WorldGenerator> Vanilla = new(() => new WorldGenerator(VanillaData.Value, Seed, VanillaSize, VanillaDepth));

    // The vanilla world (game/data/streaming.json, World Map spec §1).
    private const float VanillaSize = 12032f;
    private const float VanillaDepth = 9024f;

    [Fact]
    public void VanillaWorldDataLoadsCleanly()
    {
        var data = VanillaData.Value;

        Assert.Empty(data.Diagnostics);
        Assert.Equal(["meadow", "forest", "field", "desert", "autumn_forest", "burned_forest", "lake", "mountain", "coastal"], data.Biomes.Select(b => b.Id));
        Assert.Equal("coastal", data.CoastBiome?.Id);
        Assert.Superset(new HashSet<string> { "birch_tree", "leafy_tree", "spruce_tree", "pine_tree", "autumn_leafy_tree", "burnt_tree", "cactus" },
            data.Objects.Keys.ToHashSet());
        Assert.Contains("rock_node", data.Objects.Keys);
        Assert.Equal(64, data.Fingerprint.Length);
    }

    [Fact]
    public void EveryMandatoryStructureHasItsOwnSiteAndPrefab()
    {
        var data = VanillaData.Value;
        var numbered = data.Sites.Where(s => s.Definition.Label is not null).ToList();
        Assert.Equal(Enumerable.Range(1, 50).Select(n => $"POI-{n:00}"), numbered.Select(s => s.Definition.Label));
        Assert.Equal(Enumerable.Range(1, 50).Select(n => $"poi_{n:00}"), numbered.Select(s => s.Id));
        // Each of the 50 is its own prefab, so each can be designed on its own; the crash site and the quests' outpost keep theirs.
        Assert.All(numbered, s => Assert.NotNull(s.Poi));
        Assert.Equal(50, numbered.Select(s => s.Poi!.QualifiedId).Distinct().Count());
        Assert.All(numbered.Where(s => s.Id is not ("poi_36" or "poi_37")), s => Assert.Equal($"vanilla:{s.Id}", s.Poi!.QualifiedId));
        Assert.Equal("vanilla:crash_site", numbered.Single(s => s.Id == "poi_36").Poi!.QualifiedId);
        Assert.Equal("vanilla:outpost", numbered.Single(s => s.Id == "poi_37").Poi!.QualifiedId);
        Assert.All(numbered, s => Assert.NotNull(s.Poi!.Prefab.Size));
        // 18 large slots in all (spec §1: 18-20), six of them drawn by seed.
        Assert.Equal(6, data.Sites.Count(s => s.Pool is not null));
    }

    [Fact]
    public void TheLandRollsInHillsAndCliffTerracesButLiesFlatByTheWater()
    {
        var gen = Vanilla.Value;
        var land = new List<float>();
        var cliffs = 0;
        for (var z = -gen.WorldDepth / 2f + 600f; z < gen.WorldDepth / 2f - 600f; z += 61f)
        {
            for (var x = -gen.WorldSize / 2f + 600f; x < gen.WorldSize / 2f - 600f; x += 61f)
            {
                var h = gen.HeightAt(x, z);
                if (h <= gen.SeaLevel)
                    continue;
                land.Add(h);
                // A cliff: the ground climbs a whole step within a few metres.
                if (MathF.Abs(gen.HeightAt(x + 6f, z) - h) > 6f && gen.BiomeAt(x, z).Id != "mountain")
                    cliffs++;
            }
        }
        land.Sort();
        // Not a flat map: inland heights spread over tens of metres (Scrap Mechanic's hills and 8 m terraces).
        Assert.True(land[land.Count * 9 / 10] - land[land.Count / 10] > 40f, $"heights spread {land[land.Count / 10]} to {land[land.Count * 9 / 10]}");
        Assert.True(cliffs > 20, $"cliff faces ({cliffs})");
        // The hills ease out by the water: a lake's bank and the shore just above the beach stay low.
        var lake = gen.Data.Biomes.Single(b => b.Id == "lake");
        var shore = 0;
        for (var x = -gen.WorldSize / 2f + 400f; x < gen.WorldSize / 2f - 400f; x += 97f)
        {
            var z = gen.WorldDepth / 2f - gen.Data.Terrain.CoastWidth - 40f;
            if (gen.BiomeAt(x, z).Id == "coastal" && gen.RiverDistance(x, z) > 500f)
                Assert.InRange(gen.HeightAt(x, z), gen.Data.Terrain.SeaFloor, 30f);
            shore++;
        }
        Assert.True(shore > 50);
        Assert.Equal(0f, lake.Hills);
    }

    [Fact]
    public void ChunksAcrossTheWorldGenerate()
    {
        var gen = Vanilla.Value;
        // Every fifth chunk each way covers every biome without building all 26,000.
        int cols = (int)(gen.WorldSize / ChunkSize), rows = (int)(gen.WorldDepth / ChunkSize);
        var coords = new List<ChunkCoord>();
        for (var j = 0; j < rows; j += 5)
            for (var i = 0; i < cols; i += 5)
                coords.Add(new ChunkCoord(i - cols / 2, j - rows / 2));
        var ids = new ConcurrentDictionary<WorldObjectId, byte>();
        var objectsByBiome = new ConcurrentDictionary<string, int>();
        var (low, high) = (float.PositiveInfinity, float.NegativeInfinity);
        var gate = new object();
        Parallel.ForEach(coords, coord =>
        {
            var chunk = gen.GenerateChunk(coord, ChunkSize);
            Assert.Equal(65 * 65, chunk.Heights.Length);
            Assert.All(chunk.Heights, h => Assert.True(float.IsFinite(h)));
            Assert.All(chunk.Normals, n => Assert.True(n.Y > 0f && MathF.Abs(n.Length() - 1f) < 1e-4f));
            foreach (var o in chunk.Objects)
            {
                Assert.True(ids.TryAdd(o.Id, 0), $"object id {o.Id} is unique");
                // A deposit's blocks share one scatter spot: count the spot, not every block.
                if (o.Id.Piece == 0)
                    objectsByBiome.AddOrUpdate(gen.BiomeAt(o.Position.X, o.Position.Z).Id, 1, (_, n) => n + 1);
            }
            lock (gate)
                (low, high) = (MathF.Min(low, chunk.MinHeight), MathF.Max(high, chunk.MaxHeight));
        });

        Assert.Equal(gen.Data.Terrain.SeaFloor, low, 0.5f);
        // Spec §1: base terrain to 80 m, mountain peaks to 180-220 m.
        Assert.InRange(high, 120f, 230f);
        Assert.True(ids.Count > 10_000, $"objects scattered ({ids.Count})");
        Assert.True(objectsByBiome.GetValueOrDefault("forest") > objectsByBiome.GetValueOrDefault("meadow"), "forests are denser than meadows");
        foreach (var biome in gen.Data.Biomes.Where(b => b.Id != "lake"))
            Assert.True(objectsByBiome.GetValueOrDefault(biome.Id) > 0, $"{biome.Id} has objects");
    }

    [Fact]
    public void BiomesTakeTheirSharesAndTheEdgeIsSea()
    {
        var gen = Vanilla.Value;
        var manifest = WorldExport.Manifest(gen, 48f);
        // Spec §2's shares, with room for the seed and the coast (which takes its share from every biome along the edge).
        foreach (var (biome, min, max) in new[] { ("meadow", 0.2f, 0.32f), ("forest", 0.15f, 0.27f), ("field", 0.06f, 0.12f), ("desert", 0.07f, 0.13f),
                     ("autumn_forest", 0.05f, 0.12f), ("burned_forest", 0.05f, 0.11f), ("mountain", 0.04f, 0.09f) })
            Assert.InRange(manifest.BiomeShares.GetValueOrDefault(biome), min, max);
        Assert.InRange(manifest.WaterShare, 0.10f, 0.18f);

        float hx = gen.WorldSize / 2, hz = gen.WorldDepth / 2;
        for (var t = -1f; t <= 1f; t += 1f / 64f)
        {
            foreach (var (x, z) in new[] { (t * hx, -hz), (t * hx, hz), (-hx, t * hz), (hx, t * hz) })
                Assert.True(gen.HeightAt(x, z) < gen.SeaLevel - 10f, $"world edge at ({x}, {z}) is under the sea");
        }
        // The heart of the map is the big lake; the north is mountains, the south-west desert (spec reference map).
        Assert.Equal("lake", gen.BiomeAt(0f, 0f).Id);
        Assert.Equal("mountain", gen.BiomeAt(-0.05f * gen.WorldSize, -0.43f * gen.WorldDepth).Id);
        Assert.Equal("desert", gen.BiomeAt(-0.38f * gen.WorldSize, 0.25f * gen.WorldDepth).Id);
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
        // In the western forest.
        var whole = gen.ObjectsIn(-4352, -1024, -4224, -896).Select(o => o.Id).ToHashSet();
        var parts = new List<WorldObjectId>();
        for (var cz = -16; cz < -14; cz++)
            for (var cx = -68; cx < -66; cx++)
                parts.AddRange(gen.GenerateChunk(new ChunkCoord(cx, cz), ChunkSize).Objects.Select(o => o.Id));
        Assert.Equal(whole.Count, parts.Count);
        Assert.True(whole.SetEquals(parts));

        var some = gen.ObjectsIn(-4352, -1024, -4224, -896).First();
        Assert.Equal(some, gen.Find(some.Id));
        Assert.Null(gen.Find(new WorldObjectId(999, 0, 0)));
        Assert.Null(gen.Find(new WorldObjectId(0, 0, 0))); // under the central lake
    }

    [Fact]
    public void CrashSiteIsAFixedFlatStartInTheSouthEastWithWaterNearby()
    {
        var gen = Vanilla.Value;
        var crash = Assert.Single(gen.Pois, p => p.Poi.QualifiedId == "vanilla:crash_site");
        Assert.Equal("poi_36", crash.Site?.Id);
        Assert.Equal(new Vector2(0.19f * gen.WorldSize, 0.38f * gen.WorldDepth), crash.Centre);
        Assert.Equal(6f, crash.PadHeight);
        Assert.Equal(0, crash.QuarterTurns);
        Assert.Equal(crash.ToWorld(new Vector3(0, 0, 4)), gen.Spawn);
        for (var x = -20f; x <= 20f; x += 2f)
            for (var z = -20f; z <= 10f; z += 2f)
                Assert.Equal(6f, gen.HeightAt(crash.Centre.X + x, crash.Centre.Y + z));
        var pond = crash.Centre + new Vector2(30, 26);
        Assert.True(gen.HeightAt(pond.X, pond.Y) < gen.SeaLevel - 1f, "a pond sits next to the crash site");
        // Its banks are gentle enough to walk out of (the player controller climbs up to 45°), so the first water a
        // new player meets is never a trap.
        for (var a = 0; a < 360; a += 10)
            for (var r = 0f; r <= 22f; r += 0.5f)
            {
                var (sin, cos) = MathF.SinCos(a * MathF.PI / 180f);
                float x = pond.X + r * cos, z = pond.Y + r * sin;
                Assert.True(gen.SlopeAt(x, z) < 0.7f, $"pond bank at ({x:0.#}, {z:0.#}) has slope {gen.SlopeAt(x, z):0.##}");
            }
        Assert.Equal(3, crash.Poi.Prefab.Markers!.Count(m => m.Type == "fire"));
        Assert.Single(crash.Poi.Prefab.Markers!, m => m.Ref == "master_battery");
        // Its road heads north to the first Mechanic Station (spec §1).
        var station = Assert.Single(gen.Pois, p => p.Site?.Id == "poi_37");
        Assert.True(station.Centre.Y < crash.Centre.Y, "the first Mechanic Station is north of the crash site");
        Assert.Equal(RoadKind.Road, gen.Roads.Connected[crash.Index]);
    }

    [Fact]
    public void PoisFollowTheirPlacementRules()
    {
        var gen = Vanilla.Value;
        var counts = gen.Pois.GroupBy(p => p.Poi.Prefab.Id!).ToDictionary(g => g.Key, g => g.Count());
        Assert.True(counts.GetValueOrDefault("ruin_cluster") >= 20, $"ruins placed ({counts.GetValueOrDefault("ruin_cluster")})");
        Assert.True(counts.GetValueOrDefault("faction_outpost") >= 1, "a faction outpost is placed");
        Assert.Equal(1, counts.GetValueOrDefault("ai_core_facility"));
        // All 50 structures, each once, and the large slots' totals (spec §4: 12-14 warehouses, the rest scrapyards).
        Assert.Equal(Enumerable.Range(1, 50).Select(n => $"poi_{n:00}"), gen.Pois.Where(p => p.Site?.Definition.Label is not null).Select(p => p.Site!.Id).Order());
        Assert.Equal(12, gen.Pois.Count(p => p.Poi.Prefab.Markers?.Any(m => m.Type == "door" && m.Ref == "warehouse_key") == true));
        Assert.Equal(4, gen.Pois.Count(p => p.Poi.Prefab.Id == "farmbot_scrapyard"));
        Assert.Equal(3, gen.Pois.Count(p => p.Poi.Prefab.Markers?.Any(m => m.Ref == "fabricator") == true));

        foreach (var poi in gen.Pois)
        {
            foreach (var other in gen.Pois.Where(o => o != poi))
                Assert.True(Vector2.Distance(poi.Centre, other.Centre) >= poi.Radius + other.Radius, $"{poi.Poi.QualifiedId} and {other.Poi.QualifiedId} do not overlap");
            Assert.True(poi.PadHeight > gen.SeaLevel, $"{poi.Poi.QualifiedId} at {poi.Centre} is dry");
            if (poi.Site is { Biome: >= 0 } site)
                Assert.Equal(gen.Data.Biomes[site.Biome].Id, gen.BiomeAt(poi.Centre.X, poi.Centre.Y).Id);

            if (poi.Poi.Prefab.Placement!.Spacing is not [var min, _])
                continue;
            Assert.Contains(gen.BiomeAt(poi.Centre.X, poi.Centre.Y).Index, poi.Poi.Biomes);
            var nearest = gen.Pois.Where(o => o != poi && o.Poi == poi.Poi).Min(o => Vector2.Distance(o.Centre, poi.Centre));
            Assert.True(nearest >= min, $"{poi.Poi.QualifiedId} at {poi.Centre}: nearest of its kind is {nearest} m, under {min} m");

            // The pad is level wherever no basin cuts into it and no road crosses its edge.
            var flatten = poi.Poi.Prefab.Terrain!.First(t => t.Op == TerrainOpKind.Flatten);
            for (var a = 0; a < 8; a++)
            {
                var p = poi.ToWorld(new Vector2(a % 3 - 1, a / 3 - 1) * flatten.Radius * 0.6f);
                if (!gen.Roads.Near(p.X, p.Y, 2f))
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
            // A rock deposit's blocks are its own object or the block objects its mounds are heaped from.
            if (rule.Object.Outcrop is null)
                Assert.Same(rule.Object, o.Definition);
            else
                Assert.Equal(PrimitiveShape.Box, o.Definition.Collider!.Shape);
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
    [InlineData("""{ "terrain": { "hills": { "height": 40, "scale": 0 } }, "biomes": [{ "id": "a" }] }""", "$.terrain.hills.scale")]
    [InlineData("""{ "terrain": { "hills": { "height": 40, "octaves": 12 } }, "biomes": [{ "id": "a" }] }""", "$.terrain.hills.octaves")]
    [InlineData("""{ "terrain": { "cliffs": { "levels": 40 } }, "biomes": [{ "id": "a" }] }""", "$.terrain.cliffs.levels")]
    [InlineData("""{ "terrain": { "cliffs": { "face": 0 } }, "biomes": [{ "id": "a" }] }""", "$.terrain.cliffs.face")]
    [InlineData("""{ "biomes": [{ "id": "a", "hills": 2 }] }""", "$.biomes[0].hills")]
    [InlineData("""{ "biomes": [{ "id": "a", "color": "green" }] }""", "$.biomes[0].color")]
    [InlineData("""{ "biomes": [{ "id": "a", "scatter": [{ "object": "tree", "spacing": 5, "chance": 0.5 }] }] }""", "$.biomes[0].scatter[0].object")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "coast_biome": "sea" }""", "$.coast_biome")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "tree", "collider": { "shape": "sphere" } }] }""", "$.objects[0].collider.radius")]
    [InlineData("""{ "biomes": [{ "id": "a", "weight": 0 }] }""", "$.biomes")]
    [InlineData("""{ "biomes": [{ "id": "a", "ground": "grass" }] }""", "$.biomes[0].ground")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "grounds": [{ "id": "grass", "texture": "../grass.png" }] }""", "$.grounds[0].texture")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "grounds": [{ "id": "grass", "texture": "textures/grass.jpg" }] }""", "$.grounds[0].texture")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "grounds": [{ "id": "grass", "texture": "g.png", "tile": 0 }] }""", "$.grounds[0].tile")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "grounds": [{ "id": "grass", "texture": "g.png", "flora_tint": "brown" }] }""", "$.grounds[0].flora_tint")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "grounds": [{ "id": "g", "texture": "g.png" }, { "id": "g", "texture": "g.png" }] }""", "$.grounds[1].id")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "grounds": [{ "id": "g", "texture": "g.png" }], "slope_ground": { "ground": "rock" } }""", "$.slope_ground.ground")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "grounds": [{ "id": "g", "texture": "g.png" }], "slope_ground": { "ground": "g", "from": 1, "to": 0.5 } }""", "$.slope_ground")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "grounds": [{ "id": "a1", "texture": "g.png" }, { "id": "a2", "texture": "g.png" }, { "id": "a3", "texture": "g.png" }, { "id": "a4", "texture": "g.png" }, { "id": "a5", "texture": "g.png" }, { "id": "a6", "texture": "g.png" }, { "id": "a7", "texture": "g.png" }, { "id": "a8", "texture": "g.png" }, { "id": "a9", "texture": "g.png" }] }""", "$.grounds")]
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
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "ground": [{ "ground": "sand", "radius": 4 }] }""", "$.ground[0].ground")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "ground": [{ "ground": "burnt", "radius": 8, "blend": 4 }] }""", "$.ground[0]")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "flora": "jungle" }""", "$.flora")]
    public void BadPoiGivesAClearErrorAndIsLeftOut(string json, string path)
    {
        using var mods = new TestMods().Mod("vanilla")
            .File("vanilla", WorldGenData.DefinitionFile, """{ "biomes": [{ "id": "a" }], "grounds": [{ "id": "burnt", "texture": "b.png" }] }""")
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
