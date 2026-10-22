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
    public void MostCliffStepsAreSlopesAndOnlySomeStaySheer()
    {
        // Chase: hard cliffs everywhere meant long detours. Most 8 m steps are slopes a player walks or drives up; a
        // few stretches of the island keep sheer faces.
        var gen = Vanilla.Value;
        int steps = 0, sheer = 0;
        for (var z = -gen.WorldDepth / 2f + 600f; z < gen.WorldDepth / 2f - 600f; z += 53f)
        {
            for (var x = -gen.WorldSize / 2f + 600f; x < gen.WorldSize / 2f - 600f; x += 4f)
            {
                var h = gen.HeightAt(x, z);
                if (h <= gen.SeaLevel || gen.BiomeAt(x, z).Id is "mountain" or "lake" || gen.RiverDistance(x, z) < 200f)
                    continue;
                // A step: the ground climbs most of a cliff level within 40 m; sheer if it does so within 6 m.
                if (MathF.Abs(gen.HeightAt(x + 40f, z) - h) < 6f)
                    continue;
                steps++;
                for (var d = 0f; d < 40f; d += 2f)
                {
                    if (MathF.Abs(gen.HeightAt(x + d + 6f, z) - gen.HeightAt(x + d, z)) > 6f)
                    {
                        sheer++;
                        break;
                    }
                }
                x += 40f;
            }
        }
        Assert.True(steps > 200, $"steps ({steps})");
        Assert.InRange(sheer, 10, steps / 5);
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
    public void BiomeBordersMixAndStaySeamlessAcrossChunks()
    {
        var gen = Vanilla.Value;
        // A west-east run of chunks across the first border between two inland biomes found going out from the middle.
        var (bx, bz, west, east) = (0f, 0f, -1, -1);
        for (var z = 0f; west < 0 && z < 4000f; z += 97f)
        {
            for (var x = -3000f; x < 3000f; x += 16f)
            {
                var (a, b) = (gen.BiomeAt(x - 120f, z), gen.BiomeAt(x + 120f, z));
                if (a != b && a.Hills >= 1f && b.Hills >= 1f)
                {
                    (bx, bz, west, east) = (x, z, a.Index, b.Index);
                    break;
                }
            }
        }
        Assert.True(west >= 0, "no border found");
        int cx0 = (int)MathF.Floor((bx - 192f) / ChunkSize), cz = (int)MathF.Floor(bz / ChunkSize);
        var chunks = Enumerable.Range(cx0, 7).Select(i => gen.GenerateChunk(new ChunkCoord(i, cz), ChunkSize)).ToList();
        var n = chunks[0].Samples;
        var layers = TerrainChunkData.GroundLayers;
        var mixed = 0;
        for (var c = 0; c < chunks.Count; c++)
        {
            var chunk = chunks[c];
            var (ox, oz) = chunk.Coord.Origin(ChunkSize);
            for (var k = 0; k < n * n; k++)
            {
                var (x, z) = (ox + k % n * chunk.Resolution, oz + k / n * chunk.Resolution);
                if (chunk.Biomes[k] != gen.BiomeAt(x, z).Index)
                    mixed++;
            }
            if (c == 0)
                continue;
            // The shared column of two neighbours grows the same flora biome and paints the same grounds.
            var before = chunks[c - 1];
            for (var j = 0; j < n; j++)
            {
                Assert.Equal(before.Biomes[j * n + n - 1], chunk.Biomes[j * n]);
                Assert.Equal(before.Grounds.AsSpan((j * n + n - 1) * layers, layers).ToArray(), chunk.Grounds.AsSpan(j * n * layers, layers).ToArray());
            }
        }
        // Across the border each biome's flora reaches into the other's ground in patches, not along one line.
        Assert.True(mixed > 2000, $"only {mixed} samples grow the other biome's flora");
        Assert.InRange(gen.BiomeShare(bx, bz, west) + gen.BiomeShare(bx, bz, east), 0.5f, 1.0001f);
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
        Assert.Equal(crash.ToWorld(new Vector3(22, 0, -12)), gen.Spawn);
        // Level for 55 m round the wreck, except the lake to the west.
        var lake = new Vector2(-54, 0);
        for (var x = -55f; x <= 55f; x += 2f)
            for (var z = -55f; z <= 55f; z += 2f)
                if (x * x + z * z <= 55f * 55f && Vector2.Distance(new Vector2(x, z), lake) > 34f)
                    Assert.Equal(6f, gen.HeightAt(crash.Centre.X + x, crash.Centre.Y + z));
        var pond = crash.Centre + new Vector2(-56, -3);
        Assert.True(gen.HeightAt(pond.X, pond.Y) < gen.SeaLevel - 1f, "a lake sits next to the crash site");
        // A big lake with an irregular shore: its water's reach from the centre varies a lot round the compass.
        var area = 0f;
        var reach = new float[36];
        for (var x = -40f; x <= 40f; x += 1f)
            for (var z = -40f; z <= 40f; z += 1f)
            {
                var at = crash.Centre + lake + new Vector2(x, z);
                if (gen.HeightAt(at.X, at.Y) >= gen.SeaLevel)
                    continue;
                area += 1f;
                var sector = (int)((MathF.Atan2(z, x) + MathF.PI) / (2f * MathF.PI) * 36f) % 36;
                reach[sector] = MathF.Max(reach[sector], MathF.Sqrt(x * x + z * z));
            }
        Assert.True(area > 900f, $"lake area {area} m²");
        Assert.True(reach.Max() > 1.8f * reach.Min(), $"lake shore from {reach.Min():0.#} to {reach.Max():0.#} m out: not round");
        // Its banks are gentle enough to walk out of: under 40° where the controller climbs 45° (slope 1), so the first
        // water a new player meets is never a trap. Where two basins overlap their banks add up, so allow more than one
        // basin's own slope.
        for (var a = 0; a < 360; a += 10)
            for (var r = 0f; r <= 40f; r += 0.5f)
            {
                var (sin, cos) = MathF.SinCos(a * MathF.PI / 180f);
                float x = crash.Centre.X + lake.X + r * cos, z = crash.Centre.Y + lake.Y + r * sin;
                Assert.True(gen.SlopeAt(x, z) < 0.84f, $"lake bank at ({x - crash.Centre.X:0.#}, {z - crash.Centre.Y:0.#}) from the centre has slope {gen.SlopeAt(x, z):0.##}");
            }
        // Nothing the site places stands in the lake.
        foreach (var at in crash.Poi.Prefab.Markers!.Select(m => m.At).Concat(crash.Poi.Prefab.Models!.Select(m => m.At)))
        {
            var world = crash.ToWorld(new Vector2(at.X, at.Z));
            Assert.True(gen.HeightAt(world.X, world.Y) >= gen.SeaLevel, $"({at.X}, {at.Z}) is in the lake");
        }
        // Fires at the wreck's three ways in, three inside and by four of the burst containers.
        Assert.Equal(10, crash.Poi.Prefab.Markers!.Count(m => m.Type == "fire"));
        Assert.Single(crash.Poi.Prefab.Markers!, m => m.Ref == "master_battery");
        // The walk-in wreck in four pieces (docs/art-direction.md), the containers it lost with their cargo spilled in front
        // of them, and starting scrap to pick up round it.
        Assert.Equal(4, crash.Poi.Prefab.Models!.Count(m => m.File is "meshes/structures/sv04_aft.glb" or "meshes/structures/sv04_spine_a.glb"
            or "meshes/structures/sv04_spine_b.glb" or "meshes/structures/sv04_bow.glb"));
        Assert.Equal(12, crash.Poi.Prefab.Models!.Count(m => m.File!.StartsWith("meshes/structures/sv04_container_", StringComparison.Ordinal)));
        var spills = crash.Poi.Prefab.Markers!.Select((m, k) => (m, k)).Where(e => e.m.Ref?.StartsWith("cargo_spill_", StringComparison.Ordinal) == true)
            .Select(e => gen.Find(WorldObjectId.ForPoi(crash.Index, e.k))!).ToList();
        Assert.Equal(24, spills.Count);
        Assert.All(spills, o => Assert.True(o.Definition.Harvest && o.Definition.Drops!.Count > 0));
        Assert.Equal(5, spills.Select(o => o.Definition.Id).Distinct().Count());
        var scrap = crash.Poi.Prefab.Markers!.Select((m, k) => (m, k)).Where(e => e.m.Ref?.StartsWith("scrap_pile_", StringComparison.Ordinal) == true)
            .Select(e => gen.Find(WorldObjectId.ForPoi(crash.Index, e.k))!).ToList();
        Assert.True(scrap.Count >= 6, $"scrap piles ({scrap.Count})");
        Assert.All(scrap, o => Assert.True(o.Definition.Harvest && o.Definition.Regrow is null));
        Assert.Contains(scrap, o => o.Definition.Drops!.Any(d => d.Item == "scrap_metal_block"));
        Assert.Contains(scrap, o => o.Definition.Drops!.Any(d => d.Item == "scrap_wood_block"));
        // Fire Extinguishers lie a few steps from the spawn, enough for every fire (one sprays long enough for two).
        var spawn = crash.Poi.Prefab.Markers!.First(m => m.Type == "spawn").At;
        var extinguishers = crash.Poi.Prefab.Markers!.Select((m, k) => (m, k)).Where(e => e.m.Ref == "dropped_extinguisher")
            .Select(e => (e.m, Object: gen.Find(WorldObjectId.ForPoi(crash.Index, e.k))!)).ToList();
        Assert.All(extinguishers, e => Assert.True(Vector2.Distance(new(e.m.At.X, e.m.At.Z), new(spawn.X, spawn.Z)) is > 6f and < 10f, "near the spawn, clear of the players spawning there"));
        Assert.True(extinguishers.Sum(e => e.Object.Definition.Drops!.Single(d => d.Item == "fire_extinguisher").Count) * 2 >= 10, "enough for every fire");
        // Its road heads north to the first Workshop (spec §1).
        var station = Assert.Single(gen.Pois, p => p.Site?.Id == "poi_37");
        Assert.True(station.Centre.Y < crash.Centre.Y, "the first Workshop is north of the crash site");
        Assert.Equal(RoadKind.Road, gen.Roads.Connected[crash.Index]);
    }

    // The story's first Workshop (the farmer marks it on the map) is in walking distance of the crash on every seed.
    [Theory]
    [InlineData(1234)]
    [InlineData(1)]
    [InlineData(77)]
    [InlineData(2026)]
    public void FirstWorkshopOutpostIsWithin350MetresOfTheCrashSite(long seed)
    {
        var gen = seed == Seed ? Vanilla.Value : new WorldGenerator(VanillaData.Value, seed, VanillaSize, VanillaDepth);
        var crash = Assert.Single(gen.Pois, p => p.Poi.QualifiedId == "vanilla:crash_site");
        var outpost = Assert.Single(gen.Pois, p => p.Poi.QualifiedId == "vanilla:outpost");
        var distance = Vector2.Distance(crash.Centre, outpost.Centre);
        Assert.True(distance <= 350f, $"seed {seed}: the outpost is {distance:0} m from the crash site");
        Assert.True(distance > crash.Radius + outpost.Radius, $"seed {seed}: the outpost overlaps the crash site");
        Assert.True(outpost.PadHeight > gen.SeaLevel, $"seed {seed}: the outpost's pad is under water");
        Assert.Equal(RoadKind.Road, gen.Roads.Connected[outpost.Index]);
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

            // The pad is level wherever no basin cuts into it and no road crosses its edge, even where its own track falls away.
            if (poi.Poi.Prefab.Terrain!.FirstOrDefault(t => t.Op == TerrainOpKind.Flatten) is not { } flatten)
                continue;
            for (var a = 0; a < 8; a++)
            {
                var local = new Vector2(a % 3 - 1, a / 3 - 1) * flatten.Radius * 0.6f;
                var p = poi.ToWorld(local);
                var inBasin = poi.Poi.Prefab.Terrain!.Any(t => t.Op == TerrainOpKind.Basin
                    && Vector2.Distance(local, t.At is [var bx, var bz] ? new Vector2(bx, bz) : Vector2.Zero) < (t.Radius + t.Blend) * (1f + (t.Wobble ?? 0f)));
                if (!inBasin && !gen.Roads.Near(p.X, p.Y, 2f))
                    Assert.True(MathF.Abs(poi.PadHeight - gen.HeightAt(p.X, p.Y)) < 1e-3f, $"{poi.Poi.QualifiedId} at {poi.Centre}: pad {poi.PadHeight}, ground {gen.HeightAt(p.X, p.Y)} at {p}");
            }
        }
    }

    [Fact]
    public void ObjectsStayOnDryGroundOutsideBuiltPoisInTheirBiome()
    {
        var gen = Vanilla.Value;
        var objects = gen.ObjectsIn(-1024, -1024, 1024, 1024).Where(o => !o.Id.IsPoi).ToList();
        Assert.NotEmpty(objects);
        foreach (var o in objects)
        {
            var rule = gen.Data.ScatterRuleOf(o.Id)!;
            // A rock deposit's blocks are its own object or the block objects its mounds are heaped from, and a scene's
            // pieces are the objects its layouts name.
            if (rule.Object.Scene is { } scene)
                Assert.Contains(scene.Layouts!.SelectMany(l => l.Pieces!), p => p.Object == o.Definition.Id);
            else if (rule.Object.Outcrop is null)
                Assert.Same(rule.Object, o.Definition);
            else
                Assert.Equal(PrimitiveShape.Box, o.Definition.Collider!.Shape);
            // In its biome, or reaching across a border into the next.
            Assert.True(gen.BiomeShare(o.Position.X, o.Position.Z, rule.Biome) > 0f, $"{o.Id} grows outside {gen.Data.Biomes[rule.Biome].Id}");
            // Only wild POIs (ponds, ravines) let the land's own objects in; water rules keep to water, the rest to dry land.
            Assert.DoesNotContain(gen.PoisAt(o.Position.X, o.Position.Z), p => !p.Poi.Prefab.Wild);
            if (rule.Water is null)
                Assert.True(gen.HeightAt(o.Position.X, o.Position.Z) > gen.WaterLevelAt(o.Position.X, o.Position.Z), $"{o.Id} in the water at {o.Position}");
        }
    }

    [Fact]
    public void SmallFindsAreScatteredOverTheLand()
    {
        var gen = Vanilla.Value;
        // World POI plan (2026-10-19): about 400 small finds a world, mostly loot crates, then wrecks and Bloom Pods
        // (green in the open; purple and gold rarer, up on cliff tops and ledges).
        var counts = new Dictionary<string, int>();
        var perched = new List<(string Id, float Rise)>();
        foreach (var rule in gen.Data.ScatterRules.Where(r => r.Object.Id is "loot_crate" or "scrap_wreck" || r.Object.Id!.StartsWith("bloom_pod_")))
        {
            var s = rule.Spacing;
            for (var iz = (int)MathF.Floor(-VanillaDepth / 2 / s); iz <= (int)MathF.Floor(VanillaDepth / 2 / s); iz++)
            {
                for (var ix = (int)MathF.Floor(-VanillaSize / 2 / s); ix <= (int)MathF.Floor(VanillaSize / 2 / s); ix++)
                {
                    if (gen.Find(new WorldObjectId(rule.Key, ix, iz)) is not { } find)
                        continue;
                    counts[find.Definition.Id!] = counts.GetValueOrDefault(find.Definition.Id!) + 1;
                    Assert.DoesNotContain(gen.PoisAt(find.Position.X, find.Position.Z), p => !p.Poi.Prefab.Wild);
                    Assert.False(gen.Roads.Covers(find.Position.X, find.Position.Z), $"{find.Definition.Id} on a road at {find.Position}");
                    if (rule.Perch is { } perch)
                    {
                        var lowest = Enumerable.Range(0, 8).Min(a => gen.HeightAt(find.Position.X + MathF.Cos(a * MathF.PI / 4) * perch.Within,
                            find.Position.Z + MathF.Sin(a * MathF.PI / 4) * perch.Within));
                        perched.Add((find.Definition.Id!, gen.HeightAt(find.Position.X, find.Position.Z) - lowest));
                    }
                }
            }
        }
        foreach (var (id, fewest, most) in new[] { ("loot_crate", 180, 340), ("bloom_pod_green", 35, 90), ("bloom_pod_purple", 8, 40),
                     ("bloom_pod_gold", 3, 20), ("scrap_wreck", 55, 120) })
            Assert.True(counts.GetValueOrDefault(id) >= fewest && counts.GetValueOrDefault(id) <= most, $"{id}: {counts.GetValueOrDefault(id)}");
        Assert.True(counts.GetValueOrDefault("bloom_pod_gold") < counts.GetValueOrDefault("bloom_pod_purple"), string.Join(" ", counts));
        Assert.True(counts.Values.Sum() is >= 330 and <= 520, string.Join(" ", counts));
        // Purple and gold pods take a climb: each stands well above the ground round it.
        Assert.All(perched, p => Assert.True(p.Rise >= 6f, $"{p.Id} only {p.Rise} m up"));

        // Crates, wrecks and pods all break for their drops; pods roll theirs from their rarity's loot table.
        var objects = gen.Data.Objects;
        Assert.True(objects["loot_crate"].Health > 0 && objects["scrap_wreck"].Health > objects["loot_crate"].Health);
        Assert.All(new[] { "loot_crate", "scrap_wreck" }, id => Assert.Contains(objects[id].Drops!, d => d.Item == "component_kit"));
        Assert.False(objects.ContainsKey("loot_chest"));
        foreach (var (id, table, picks, most) in new[] { ("bloom_pod_green", "common", 1, 3), ("bloom_pod_purple", "epic", 1, 3), ("bloom_pod_gold", "legendary", 3, 3) })
        {
            var pod = objects[id];
            Assert.Equal((table, picks, most), (pod.Loot!.Table, pod.Loot.Picks, pod.Loot.PicksMax ?? pod.Loot.Picks));
            Assert.NotNull(pod.Glow);
            Assert.Equal(("settle", "idle", "hit", "break"), (pod.Mesh!.Clips!.Appear, pod.Mesh.Clips.Idle, pod.Mesh.Clips.Hit, pod.Mesh.Clips.Break));
            // The model carries every clip the data names.
            var clips = Mechanica.Core.Characters.GltfSummary.Read(gen.Data.MeshFileOf(pod)!).Animations.Select(a => a.Name).Order().ToList();
            Assert.Equal(["break", "hit", "idle", "settle"], clips);
        }
        Assert.True(objects["bloom_pod_green"].Health < objects["bloom_pod_purple"].Health && objects["bloom_pod_purple"].Health < objects["bloom_pod_gold"].Health);
    }

    [Fact]
    public void ClusteredScatterGathersIntoPatchesWithOpenGroundBetween()
    {
        // The same rule spread evenly, kept to patches covering 30% of the ground, and with half its chance outside them;
        // a bush rule sharing the trees' layer grows in the same patches.
        List<WorldObjectPlacement> Objects(string cluster, string bushCluster = "")
        {
            using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, $$"""
                { "biomes": [{ "id": "a", "height": 10, "scatter": [
                    { "object": "t", "spacing": 10, "chance": 1 {{cluster}} },
                    { "object": "b", "spacing": 10, "chance": 0.5 {{bushCluster}} }] }],
                  "objects": [{ "id": "t", "collider": { "shape": "sphere", "radius": 1 }, "footprint": 0.5 },
                              { "id": "b", "collider": { "shape": "sphere", "radius": 1 }, "footprint": 0.5 }] }
                """);
            var data = WorldGenData.Load(mods.Load());
            Assert.Empty(data.Diagnostics);
            return [.. new WorldGenerator(data, 3, WorldSize).ObjectsIn(-1000f, -1000f, 1000f, 1000f)];
        }
        static int Count(List<WorldObjectPlacement> all, string id) => all.Count(o => o.Definition.Id == id);
        // Share of 20 m squares a rule leaves bare.
        static float Bare(List<WorldObjectPlacement> all, string id)
        {
            var filled = all.Where(o => o.Definition.Id == id).Select(o => ((int)MathF.Floor(o.Position.X / 20f), (int)MathF.Floor(o.Position.Z / 20f))).ToHashSet();
            return 1f - filled.Count / (100f * 100f);
        }
        var even = Objects("");
        var patches = Objects(""", "cluster": { "layer": "grove", "scale": 90, "cover": 0.3 }""");
        var thinned = Objects(""", "cluster": { "scale": 90, "cover": 0.3, "outside": 0.5 }""");
        var shared = Objects(""", "cluster": { "layer": "grove", "scale": 90, "cover": 0.3 }""", """, "cluster": { "layer": "grove", "scale": 90, "cover": 0.3 }""");

        var ratio = Count(patches, "t") / (float)Count(even, "t");
        Assert.InRange(ratio, 0.22f, 0.38f);
        Assert.InRange(Count(thinned, "t") / (float)Count(even, "t"), 0.55f, 0.75f);
        // Evenly spread, nearly every 20 m square has a tree; in patches most squares are open ground.
        Assert.True(Bare(even, "t") < 0.1f, $"{Bare(even, "t")}");
        Assert.True(Bare(patches, "t") > 0.5f, $"{Bare(patches, "t")}");
        // Bushes on the trees' layer stand among the trees, so nearly every bush has a tree close by.
        var trees = shared.Where(o => o.Definition.Id == "t").Select(o => new Vector2(o.Position.X, o.Position.Z)).ToList();
        var bushes = shared.Where(o => o.Definition.Id == "b").ToList();
        var nearTree = bushes.Count(b => trees.Any(t => Vector2.Distance(t, new Vector2(b.Position.X, b.Position.Z)) < 16f)) / (float)bushes.Count;
        Assert.True(nearTree > 0.9f, $"{nearTree}");
        // The same seed lays the same patches.
        Assert.Equal(patches.Select(o => o.Id), Objects(""", "cluster": { "layer": "grove", "scale": 90, "cover": 0.3 }""").Select(o => o.Id));
    }

    [Fact]
    public void ScenesLayTheirPiecesOutRoundTheirSpot()
    {
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, """
            { "biomes": [{ "id": "a", "height": 10, "scatter": [{ "object": "camp", "spacing": 60, "chance": 1 }] }],
              "objects": [
                { "id": "camp", "footprint": 8, "scene": { "layouts": [
                    { "weight": 1, "pieces": [{ "object": "cart", "at": [0, 0] }, { "object": "rail", "at": [4, 0], "yaw": 90 }, { "object": "rail", "at": [-4, 0], "chance": 0.5 }] },
                    { "weight": 1, "pieces": [{ "object": "rail", "at": [0, 3] }, { "object": "rail", "at": [0, -3] }] }] } },
                { "id": "cart", "collider": { "shape": "box", "size": [2, 1, 3] }, "health": 50, "footprint": 1.5 },
                { "id": "rail", "collider": { "shape": "box", "size": [2, 1, 0.2] }, "footprint": 1 }] }
            """);
        var data = WorldGenData.Load(mods.Load());
        Assert.Empty(data.Diagnostics);
        var gen = new WorldGenerator(data, 4, WorldSize);
        var placed = gen.ObjectsIn(-600f, -600f, 600f, 600f).ToList();
        // The scene object itself never stands anywhere: only its pieces do.
        Assert.DoesNotContain(placed, p => p.Definition.Id == "camp");
        var scenes = placed.GroupBy(p => (p.Id.Rule, p.Id.CellX, p.Id.CellZ)).ToList();
        Assert.True(scenes.Count > 50, $"{scenes.Count}");
        var carts = 0;
        var halfRails = 0;
        foreach (var scene in scenes)
        {
            var pieces = scene.ToList();
            if (pieces.FirstOrDefault(p => p.Definition.Id == "cart") is { } cart)
            {
                carts++;
                // The first layout: a rail 4 m to one side of the cart, turned a quarter more than the scene.
                var rail = pieces.Single(p => p.Id.Piece == 1);
                Assert.Equal(4f, Vector2.Distance(new Vector2(cart.Position.X, cart.Position.Z), new Vector2(rail.Position.X, rail.Position.Z)), 2);
                Assert.Equal(MathF.PI / 2f, rail.Yaw - cart.Yaw, 3);
                if (pieces.Any(p => p.Id.Piece == 2))
                    halfRails++;
            }
            else
            {
                Assert.Equal([0, 1], pieces.Select(p => p.Id.Piece).Order());
                Assert.All(pieces, p => Assert.Equal("rail", p.Definition.Id));
            }
            // Every piece can be found again by its id, as a destroyed one must be.
            Assert.All(pieces, p => Assert.Equal(p, gen.Find(p.Id)));
        }
        // Both layouts turn up, and the half-chance rail stands in about half the first layout's scenes.
        Assert.InRange(carts / (float)scenes.Count, 0.3f, 0.7f);
        Assert.InRange(halfRails / (float)carts, 0.3f, 0.7f);
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

    [Fact]
    public void RegrowingObjectsComeBackAtTheirTimeAndTheirTimesSave()
    {
        var state = new WorldState(5);
        var geyser = WorldObjectId.ForPoi(12, 3);
        Assert.True(state.Destroy(geyser, regrowAt: 4600));
        state.Destroy(new WorldObjectId(1, 2, 3));
        Assert.Empty(state.RegrownBy(4599));
        Assert.Equal([geyser], state.RegrownBy(4600));

        var json = state.ToJson();
        Assert.Contains("\"regrow\": {\n    \"poi.12.3\": 4600\n  }", json);
        var back = WorldState.FromJson(json);
        Assert.Equal(4600, back.Regrowing[geyser]);
        Assert.Equal(json, back.ToJson());

        Assert.True(back.Restore(geyser));
        Assert.False(back.IsDestroyed(geyser));
        Assert.Empty(back.Regrowing);
        Assert.False(back.Restore(geyser));
        Assert.DoesNotContain("regrow", back.ToJson());
        Assert.Throws<FormatException>(() => WorldState.FromJson("""{ "seed": 1, "destroyed": [], "regrow": { "poi.1.1": 5 } }"""));
    }

    [Theory]
    [InlineData("poi.7.2", -1, 7, 2)]
    [InlineData("3.-10.4", 3, -10, 4)]
    public void WorldObjectIdsRoundTrip(string text, int rule, int x, int z)
    {
        Assert.True(WorldObjectId.TryParse(text, out var id));
        Assert.Equal(new WorldObjectId(rule, x, z), id);
        Assert.Equal(text, id.ToString());
    }

    [Theory]
    [InlineData("poi.-1.2")]
    [InlineData("poi.1.2.3")]
    [InlineData("poi.a.2")]
    [InlineData("-1.7.2")]
    public void BadWorldObjectIdsDoNotParse(string text) => Assert.False(WorldObjectId.TryParse(text, out _));

    [Fact]
    public void OilPondsAndChemicalPoolsFillTheWorldWithGeysersAndPools()
    {
        var gen = Vanilla.Value;
        var ponds = gen.Pois.Where(p => p.Poi.Prefab.Id == "oil_pond").ToList();
        var pools = gen.Pois.Where(p => p.Poi.Prefab.Pools?.Any(l => l.Liquid == "chemical") == true).ToList();
        // Abundant (World POI plan): a pond every kilometre or so of desert and burned forest, chemicals in every region.
        Assert.True(ponds.Count >= 15, $"oil ponds ({ponds.Count})");
        Assert.True(pools.Count >= 12, $"chemical pools ({pools.Count})");
        Assert.Contains(pools, p => p.Poi.Prefab.Id == "poi_48");
        Assert.Contains(pools, p => p.Poi.Prefab.Id == "poi_23");

        var geysers = gen.Pois.SelectMany(p => (p.Poi.Prefab.Markers ?? []).Select((m, k) => (Poi: p, Marker: m, Index: k)))
            .Where(m => m.Marker.Type == "object" && m.Marker.Ref == "oil_geyser").ToList();
        Assert.True(geysers.Count >= 60, $"oil geysers ({geysers.Count})");
        foreach (var (poi, marker, index) in geysers.Take(40))
        {
            var id = WorldObjectId.ForPoi(poi.Index, index);
            var found = gen.Find(id)!;
            Assert.Equal("oil_geyser", found.Definition.Id);
            Assert.True(found.Definition.Harvest);
            // It stands on the ground beside its pond, not in the oil, and its chunk draws it.
            Assert.Equal(gen.HeightAt(found.Position.X, found.Position.Z) - found.Definition.Sink * found.Scale, found.Position.Y, 3);
            Assert.Null(gen.LiquidAt(found.Position + new System.Numerics.Vector3(0, 0.2f, 0)));
            Assert.Contains(gen.ObjectsIn(found.Position.X - 1, found.Position.Z - 1, found.Position.X + 1, found.Position.Z + 1), o => o.Id == id);
        }
        Assert.Null(gen.Find(WorldObjectId.ForPoi(ponds[0].Index, 99)));
        Assert.Null(gen.Find(WorldObjectId.ForPoi(gen.Pois.Count, 0)));

        // A pool's liquid lies in its basin: under the surface over the middle, not above it or past its edge.
        var pond = pools.First(p => p.Poi.Prefab.Id == "poi_48");
        var surface = pond.PadHeight + pond.Poi.Prefab.Pools![0].Level;
        Assert.True(gen.HeightAt(pond.Centre.X, pond.Centre.Y) < surface - 0.3f);
        Assert.Equal("chemical", gen.LiquidAt(new System.Numerics.Vector3(pond.Centre.X, surface - 0.2f, pond.Centre.Y))!.Liquid.Id);
        Assert.Null(gen.LiquidAt(new System.Numerics.Vector3(pond.Centre.X, surface + 0.2f, pond.Centre.Y)));
        Assert.Null(gen.LiquidAt(new System.Numerics.Vector3(pond.Centre.X + 40f, surface - 0.2f, pond.Centre.Y)));
        var chunk = gen.GenerateChunk(ChunkCoord.At(pond.Centre.X, pond.Centre.Y, 64f), 64f);
        Assert.Contains(chunk.Pools, p => p.Poi == pond);
    }

    [Fact]
    public void SmallPoisFillTheLandAsThePlanSays()
    {
        var gen = Vanilla.Value;
        var counts = gen.Pois.GroupBy(p => p.Poi.Prefab.Id!).ToDictionary(g => g.Key, g => g.Count());
        // World POI plan (2026-10-19): about 70 camps, 40 forest ruins, 40 kiosks, 15 farming patches, 10 build areas,
        // 20 shore wrecks and 120 ruins on this doubled map; each seed lands near those.
        foreach (var (id, fewest, most) in new[] { ("camp", 50, 95), ("forest_ruin", 28, 60), ("roadside_kiosk", 28, 70),
                     ("farming_patch", 8, 24), ("build_area", 5, 16), ("shore_wreck", 8, 34), ("ruin_cluster", 95, 150) })
            Assert.True(counts.GetValueOrDefault(id) >= fewest && counts.GetValueOrDefault(id) <= most, $"{id}: {counts.GetValueOrDefault(id)}");

        foreach (var kiosk in gen.Pois.Where(p => p.Poi.Prefab.Id == "roadside_kiosk"))
        {
            // Beside a road, clear of it, level with it, and facing it.
            var road = gen.Roads.Nearest(kiosk.Centre.X, kiosk.Centre.Y, 48f, RoadKind.Road);
            Assert.True(road is not null, $"kiosk at {kiosk.Centre} stands by a road");
            Assert.False(gen.Roads.Near(kiosk.Centre.X, kiosk.Centre.Y, kiosk.Radius));
            Assert.True(MathF.Abs(road!.Value.Height - kiosk.PadHeight) <= 1.6f, $"kiosk at {kiosk.Centre} is level with its road");
            var front = kiosk.ToWorld(new Vector2(0f, -1f)) - kiosk.Centre;
            Assert.True(Vector2.Dot(front, Vector2.Normalize(road.Value.Point - kiosk.Centre)) > 0.6f, $"kiosk at {kiosk.Centre} faces its road");
        }
        foreach (var wreck in gen.Pois.Where(p => p.Poi.Prefab.Id == "shore_wreck"))
        {
            var wet = Enumerable.Range(0, 64).Any(k => gen.HeightAt(wreck.Centre.X + MathF.Cos(k * MathF.Tau / 64) * (wreck.Radius + 24f),
                wreck.Centre.Y + MathF.Sin(k * MathF.Tau / 64) * (wreck.Radius + 24f)) < gen.SeaLevel
                || gen.HeightAt(wreck.Centre.X + MathF.Cos(k * MathF.Tau / 64) * (wreck.Radius + 12f),
                    wreck.Centre.Y + MathF.Sin(k * MathF.Tau / 64) * (wreck.Radius + 12f)) < gen.SeaLevel);
            Assert.True(wet, $"shore wreck at {wreck.Centre} is by the water");
        }
    }

    [Fact]
    public void PondsAndMarshesDotTheLandWithLiliesAndReeds()
    {
        var gen = Vanilla.Value;
        var counts = gen.Pois.GroupBy(p => p.Poi.Prefab.Id!).ToDictionary(g => g.Key, g => g.Count());
        // Scrap Mechanic's lake tiles are its biggest set: small water turns up all over (World fill, item 6).
        foreach (var (id, fewest) in new[] { ("pond", 50), ("pond_large", 6), ("marsh", 15) })
            Assert.True(counts.GetValueOrDefault(id) >= fewest, $"{id}: {counts.GetValueOrDefault(id)}");
        var lilies = 0;
        var reeds = 0;
        foreach (var pond in gen.Pois.Where(p => p.Poi.Prefab.Id is "pond" or "pond_large" or "marsh").Take(12))
        {
            // Every one holds swimmable water above sea level, the land round it is dry, and plants grow in and round it.
            var pool = pond.Poi.Prefab.Pools![0];
            var level = pond.PadHeight + pool.Level;
            Assert.True(level > gen.SeaLevel);
            Assert.True(Enumerable.Range(0, 400).Any(k => gen.WaterLevelAt(pond.Centre.X + (k % 20 - 10) * 3f, pond.Centre.Y + (k / 20 - 10) * 3f) == level
                && gen.HeightAt(pond.Centre.X + (k % 20 - 10) * 3f, pond.Centre.Y + (k / 20 - 10) * 3f) < level - 0.4f), $"{pond.Poi.Prefab.Id} at {pond.Centre} holds water");
            var near = gen.ObjectsIn(pond.Centre.X - pond.Radius, pond.Centre.Y - pond.Radius, pond.Centre.X + pond.Radius, pond.Centre.Y + pond.Radius).ToList();
            lilies += near.Count(o => o.Definition.Id == "lily_pad_cluster");
            reeds += near.Count(o => o.Definition.Id is "reed_bed" or "cattail_clump");
        }
        Assert.True(lilies >= 12, $"lily pads ({lilies})");
        Assert.True(reeds >= 30, $"reeds ({reeds})");
    }

    // Every cell of each rule scattering one of these ids: what it holds, by rule.
    private static IEnumerable<(Scatter Rule, List<WorldObjectPlacement> Placed)> Scattered(WorldGenerator gen, Func<Scatter, bool> which)
    {
        foreach (var rule in gen.Data.ScatterRules.Where(which))
        {
            var s = rule.Spacing;
            for (var iz = (int)MathF.Floor(-VanillaDepth / 2 / s); iz <= (int)MathF.Floor(VanillaDepth / 2 / s); iz++)
            {
                for (var ix = (int)MathF.Floor(-VanillaSize / 2 / s); ix <= (int)MathF.Floor(VanillaSize / 2 / s); ix++)
                {
                    var placed = gen.InCell(rule, ix, iz).ToList();
                    if (placed.Count > 0)
                        yield return (rule, placed);
                }
            }
        }
    }

    [Fact]
    public void FieldsAndOrchardsPatchTheFarmlandOnLevelGround()
    {
        var gen = Vanilla.Value;
        var farms = new[] { "scene_wheat_field", "scene_vegetable_field", "scene_corn_field", "scene_ploughed_field", "scene_overgrown_field", "scene_orchard", "scene_field_ditch" };
        var counts = new Dictionary<string, int>();
        var patches = 0;
        var gaps = new List<float>();
        foreach (var (rule, placed) in Scattered(gen, r => farms.Contains(r.Object.Id)))
        {
            var biome = gen.Data.Biomes[rule.Biome].Id!;
            counts[biome] = counts.GetValueOrDefault(biome) + 1;
            foreach (var o in placed.Where(p => p.Definition.ClearFlora && p.Definition.Id!.StartsWith("field_", StringComparison.Ordinal)))
            {
                // Each 8 m patch lies along the ground under it, so none pokes through, with every corner and side within
                // its 0.45 m soil skirt, so no gap shows under it, and mostly far less (no wall of skirt on its low side);
                // and it is never on a road.
                patches++;
                Assert.False(gen.Roads.Covers(o.Position.X, o.Position.Z), $"{o.Definition.Id} on a road at {o.Position}");
                var bottom = o.Position + new Vector3(0f, o.Definition.Sink, 0f);    // the ground the model's origin was sunk from
                var turn = o.Tilt * Quaternion.CreateFromAxisAngle(Vector3.UnitY, o.Yaw);
                foreach (var (cx, cz) in new[] { (-4f, -4f), (4f, -4f), (-4f, 4f), (4f, 4f), (0f, 0f), (4f, 0f), (-4f, 0f), (0f, 4f), (0f, -4f) })
                {
                    var at = bottom + Vector3.Transform(new Vector3(cx, 0f, cz), turn);
                    var gap = at.Y - gen.HeightAt(at.X, at.Z);
                    Assert.InRange(gap, -0.01f, 0.45f - o.Definition.Sink);
                    gaps.Add(gap);
                }
            }
        }
        // Lying along the slope, a patch's rim stays within a hand's breadth of the ground nearly everywhere.
        gaps.Sort();
        Assert.True(gaps[(int)(gaps.Count * 0.95)] < 0.1f, $"95% of field patch rims within {gaps[(int)(gaps.Count * 0.95)]} m of the ground");
        // The farmland reads as a patchwork of fields (World fill: props 300); the meadow has fewer, round its farms.
        Assert.True(counts.GetValueOrDefault("field") >= 150, $"fields and orchards in the field biome ({counts.GetValueOrDefault("field")})");
        Assert.True(counts.GetValueOrDefault("meadow") >= 30, $"fields and orchards in the meadow ({counts.GetValueOrDefault("meadow")})");
        Assert.True(patches >= 2000, $"field patches ({patches})");
    }

    [Fact]
    public void CablePolesLineTheMainRoadsFacingThem()
    {
        var gen = Vanilla.Value;
        var poles = Scattered(gen, r => r.Object.Id == "colony_cable_pole").SelectMany(c => c.Placed).ToList();
        // About one every 40 m of road, the forests' trees and the junctions taking a few.
        Assert.True(poles.Count >= gen.Roads.LengthOf(RoadKind.Road) / 40f * 0.6f, $"poles ({poles.Count}) along {gen.Roads.LengthOf(RoadKind.Road)} m of road");
        foreach (var pole in poles)
        {
            var p = new Vector2(pole.Position.X, pole.Position.Z);
            var road = gen.Roads.Nearest(p.X, p.Y, 48f, RoadKind.Road)!.Value;
            // A few metres past the edge of the bed, never on it, and facing it.
            Assert.False(gen.Roads.Covers(p.X, p.Y), $"pole on the road at {p}");
            Assert.InRange(road.Distance - gen.Roads.HalfBed(road.Chain), 1.5f, 4f);
            var front = new Vector2(MathF.Sin(pole.Yaw), MathF.Cos(pole.Yaw));
            Assert.True(Vector2.Dot(front, Vector2.Normalize(road.Point - p)) > 0.99f, $"pole at {p} faces the road");
        }
        // Direction signs stand by the junctions.
        var junctions = gen.Roads.Nodes.Where(n => n.Kind == RoadNodeKind.Junction).Select(n => n.At).ToList();
        foreach (var (_, placed) in Scattered(gen, r => r.Object.Id == "road_sign_junction"))
            Assert.True(junctions.Min(j => Vector2.Distance(j, new Vector2(placed[0].Position.X, placed[0].Position.Z))) < 70f, $"junction sign at {placed[0].Position}");
        // Signs and markers line the roads too, and the tracks get the shrines.
        var signs = Scattered(gen, r => r.Roadside is not null && r.Object.Id != "colony_cable_pole").SelectMany(c => c.Placed)
            .GroupBy(o => o.Definition.Id!).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (id, fewest) in new[] { ("km_marker", 25), ("road_reflector_post", 300), ("road_sign_junction", 10), ("road_sign_warning_curve", 5),
            ("guard_rail", 5), ("roadside_shrine", 5) })
            Assert.True(signs.GetValueOrDefault(id) >= fewest, $"{id}: {signs.GetValueOrDefault(id)}");
    }

    [Fact]
    public void PondsAndLakesAreDeepEnoughToFish()
    {
        var gen = Vanilla.Value;
        // Each pond has shallows by its bank and a deep hole; a large pond goes deeper, a marsh stays shallow, and each
        // knows which water it is (a fishing game keys its catch on that).
        foreach (var (id, deepest, most) in new[] { ("pond", 3.5f, 5f), ("pond_large", 5.5f, 7f), ("marsh", 0.4f, 1.2f) })
        {
            foreach (var pond in gen.Pois.Where(p => p.Poi.Prefab.Id == id).Take(6))
            {
                var bodies = Enumerable.Range(0, 1600)
                    .Select(k => gen.WaterBodyAt(pond.Centre.X + (k % 40 - 20) * 2f, pond.Centre.Y + (k / 40 - 20) * 2f))
                    .OfType<WaterBody>().Where(b => b.Kind == WaterBodyKind.Pond).ToList();
                Assert.NotEmpty(bodies);
                Assert.All(bodies, b => Assert.Equal($"vanilla:{id}", b.Id));
                var depth = bodies.Max(b => b.Depth);
                Assert.InRange(depth, deepest, most);
                Assert.Contains(bodies, b => b.Depth is > 0.2f and < 1.2f);
            }
        }
        // The lakes are open water at sea level, many metres deep over most of their bed.
        var lake = new List<float>();
        for (var x = -6000f; x < 6000f; x += 40f)
        {
            for (var z = -4500f; z < 4500f; z += 40f)
            {
                if (gen.WaterBodyAt(x, z) is { Kind: WaterBodyKind.Open, Id: "lake" } body)
                    lake.Add(body.Depth);
            }
        }
        Assert.True(lake.Count > 1000, $"lake water ({lake.Count} samples)");
        Assert.True(lake.Count(d => d > 10f) > lake.Count / 2, "most of the lake is over 10 m deep");
        Assert.Contains(lake, d => d < 2f);
        // The creeks are rivers, wadeable deep.
        var creek = gen.Rivers.SelectMany(line => line).Select(p => gen.WaterBodyAt(p.X, p.Y)).OfType<WaterBody>().Where(b => b.Kind == WaterBodyKind.River).ToList();
        Assert.NotEmpty(creek);
        Assert.All(creek, b => Assert.Equal("river", b.Id));
    }

    [Fact]
    public void RavinesAndMesasRoughenTheLand()
    {
        var gen = Vanilla.Value;
        var counts = gen.Pois.GroupBy(p => p.Poi.Prefab.Id!).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (id, fewest) in new[] { ("ravine", 8), ("ravine_bend", 5), ("gully", 5), ("mesa", 8), ("butte", 8) })
            Assert.True(counts.GetValueOrDefault(id) >= fewest, $"{id}: {counts.GetValueOrDefault(id)}");
        foreach (var ravine in gen.Pois.Where(p => p.Poi.Prefab.Id == "ravine").Take(6))
        {
            // Cut well below its rim along its length, and no road runs down it.
            foreach (var along in new[] { -60f, 0f, 60f })
            {
                var floor = ravine.ToWorld(new Vector3(along, 0f, 0f));
                Assert.True(gen.HeightAt(floor.X, floor.Z) < ravine.PadHeight - 7f, $"ravine at {ravine.Centre} is cut at {along}");
                Assert.False(gen.Roads.Covers(floor.X, floor.Z));
            }
        }
        foreach (var mesa in gen.Pois.Where(p => p.Poi.Prefab.Id == "mesa").Take(6))
            Assert.True(gen.HeightAt(mesa.Centre.X, mesa.Centre.Y) > mesa.PadHeight + 15f, $"mesa at {mesa.Centre} stands up");
    }

    [Fact]
    public void RockFormationsStandInTheirBiomes()
    {
        var gen = Vanilla.Value;
        // The arches, the window and the hoodoos are wild POIs with a colliding model, in their biomes, on a pad levelled
        // under their legs; no scattered object grows inside them.
        var kinds = new (string Id, string[] Biomes, int Fewest, int Most)[]
        {
            ("rock_arch_medium", ["desert", "mountain", "burned_forest", "meadow"], 12, 40),
            ("rock_arch_large", ["desert", "mountain"], 3, 15),
            ("rock_arch_double", ["desert", "mountain", "burned_forest"], 4, 20),
            ("rock_arch_collapsed", ["desert", "mountain", "burned_forest", "meadow"], 5, 25),
            ("rock_window", ["desert", "mountain", "burned_forest"], 5, 25),
            ("hoodoo_cluster", ["desert"], 30, 60),
        };
        var arches = 0;
        var report = string.Join(", ", kinds.Select(k => $"{k.Id} {gen.Pois.Count(p => p.Poi.Prefab.Id == k.Id)}"));
        foreach (var (id, biomes, fewest, most) in kinds)
        {
            var placed = gen.Pois.Where(p => p.Poi.Prefab.Id == id).ToList();
            Assert.True(placed.Count >= fewest && placed.Count <= most, $"{id}: {placed.Count} ({report})");
            if (id.StartsWith("rock_arch", StringComparison.Ordinal))
                arches += placed.Count;
            foreach (var poi in placed)
            {
                var prefab = poi.Poi.Prefab;
                Assert.True(prefab.Wild);
                Assert.Contains(gen.BiomeAt(poi.Centre.X, poi.Centre.Y).Id, biomes);
                var model = Assert.Single(prefab.Models!);
                Assert.True(File.Exists(VanillaData.Value.ModelFileOf(poi.Poi, model)), model.File);
                Assert.Equal(poi.PadHeight, gen.HeightAt(poi.Centre.X, poi.Centre.Y), 0.05f);
                var r = prefab.Radius;
                Assert.Empty(gen.ObjectsIn(poi.Centre.X - r / 2, poi.Centre.Y - r / 2, poi.Centre.X + r / 2, poi.Centre.Y + r / 2));
            }
        }
        // Notable, not common: a few dozen arches over the island.
        Assert.InRange(arches, 40, 80);
        // Sea stacks stand in the coast's shallows, balanced rocks in the desert.
        foreach (var (id, biome, fewest, most) in new[] { ("sea_stack", "coastal", 20, 40), ("balanced_rock", "desert", 15, 45) })
        {
            var placed = Scattered(gen, r => r.Object.Id == id).ToList();
            Assert.True(placed.Count >= fewest && placed.Count <= most, $"{id}: {placed.Count}");
            Assert.All(placed, c => Assert.Equal(biome, gen.Data.Biomes[c.Rule.Biome].Id));
            var all = placed.SelectMany(c => c.Placed).ToList();
            foreach (var o in all)
            {
                var p = o.Position;
                if (id == "sea_stack")
                    Assert.InRange(gen.WaterLevelAt(p.X, p.Z) - gen.HeightAt(p.X, p.Z), -0.3f, 0.6f);
                // Each stands alone, clear of the next.
                Assert.All(all.Where(q => q != o), q => Assert.True(Vector3.Distance(q.Position, p) > 2f * o.Definition.Footprint, $"{id} at {p} and {q.Position}"));
            }
        }
    }

    [Fact]
    public void LandmarksStandAloneAcrossTheIsland()
    {
        var gen = Vanilla.Value;
        var ids = new[]
        {
            "landmark_relay_spire", "landmark_cooling_towers", "landmark_aqueduct", "landmark_kneeling_giant",
            "landmark_sky_arch", "landmark_ziggurat", "landmark_windmill_tower", "landmark_freighter_wreck",
        };
        var landmarks = gen.Pois.Where(p => ids.Contains(p.Poi.Prefab.Id)).ToList();
        // Something to steer by in every region: each one a few times, a couple of dozen in all.
        foreach (var id in ids)
            Assert.True(landmarks.Count(p => p.Poi.Prefab.Id == id) >= 2, $"{id}: {landmarks.Count(p => p.Poi.Prefab.Id == id)}");
        Assert.InRange(landmarks.Count, 20, 30);
        foreach (var a in landmarks)
        {
            var prefab = a.Poi.Prefab;
            // Built, not wild, with no loot and no road, on a level pad; its model is a file of the mod.
            Assert.False(prefab.Wild);
            Assert.Null(prefab.Markers);
            Assert.Null(prefab.Road);
            Assert.Equal(a.PadHeight, gen.HeightAt(a.Centre.X, a.Centre.Y), 0.05f);
            var model = Assert.Single(prefab.Models!);
            Assert.True(File.Exists(VanillaData.Value.ModelFileOf(a.Poi, model)), model.File);
            foreach (var b in landmarks.Where(b => b != a))
            {
                // They keep far apart: one kind at least its spacing, and any two clear of each other's footprint.
                var apart = Vector2.Distance(a.Centre, b.Centre);
                if (b.Poi.Prefab.Id == prefab.Id)
                    Assert.True(apart >= prefab.Placement!.Spacing![0], $"{prefab.Id} at {a.Centre} and {b.Centre}");
                Assert.True(apart >= a.Radius + b.Radius, $"{prefab.Id} at {a.Centre} and {b.Poi.Prefab.Id} at {b.Centre}");
            }
        }
    }

    [Fact]
    public void PoolShoresWanderWithTheirBasin()
    {
        var gen = Vanilla.Value;
        var shapes = new List<float[]>();
        foreach (var poi in gen.Pois.Where(p => p.Poi.Prefab.Id is "chemical_pool" or "oil_pond").Take(4))
        {
            var pool = gen.PoolsAt(poi.Centre.X, poi.Centre.Y).Single();
            // Walk out from the middle in 24 directions to where the liquid ends: the shore.
            var shore = new float[24];
            for (var a = 0; a < shore.Length; a++)
            {
                var direction = new System.Numerics.Vector2(MathF.Cos(a * MathF.Tau / shore.Length), MathF.Sin(a * MathF.Tau / shore.Length));
                var r = 0f;
                while (r < pool.Radius && gen.LiquidAt(Point(poi.Centre + direction * (r + 0.25f), pool.Centre.Y - 0.05f)) is not null)
                    r += 0.25f;
                shore[a] = r;
                // The surface reaches past the shore, where the ground stands above it: no liquid there.
                Assert.True(r < pool.Radius - 0.5f, $"{poi.Poi.Prefab.Id} shore {r} m reaches the surface's edge ({pool.Radius} m)");
                Assert.Null(gen.LiquidAt(Point(poi.Centre + direction * (r + 1f), pool.Centre.Y - 0.05f)));
            }
            // Not a circle: the shore wanders in and out by well over a metre.
            Assert.True(shore.Max() - shore.Min() > 0.25f * shore.Average(), $"{poi.Poi.Prefab.Id} shore {string.Join(", ", shore)}");
            shapes.Add(shore);
        }
        // Each pond its own shape.
        Assert.Equal(4, shapes.Count);
        Assert.NotEqual(shapes[0], shapes[1]);
        Assert.NotEqual(shapes[2], shapes[3]);

        System.Numerics.Vector3 Point(System.Numerics.Vector2 at, float y) => new(at.X, y, at.Y);
    }

    [Theory]
    [InlineData("""{ "biomes": [] }""", "$.biomes")]
    [InlineData("""{ "terrain": { "resolution": 0 }, "biomes": [{ "id": "a" }] }""", "$.terrain.resolution")]
    [InlineData("""{ "terrain": { "biome_blend": 900 }, "biomes": [{ "id": "a" }] }""", "$.terrain.biome_blend")]
    [InlineData("""{ "terrain": { "sea_floor": 5, "sea_level": 0 }, "biomes": [{ "id": "a" }] }""", "$.terrain.sea_floor")]
    [InlineData("""{ "terrain": { "hills": { "height": 40, "scale": 0 } }, "biomes": [{ "id": "a" }] }""", "$.terrain.hills.scale")]
    [InlineData("""{ "terrain": { "hills": { "height": 40, "octaves": 12 } }, "biomes": [{ "id": "a" }] }""", "$.terrain.hills.octaves")]
    [InlineData("""{ "terrain": { "cliffs": { "levels": 40 } }, "biomes": [{ "id": "a" }] }""", "$.terrain.cliffs.levels")]
    [InlineData("""{ "terrain": { "cliffs": { "face": 0 } }, "biomes": [{ "id": "a" }] }""", "$.terrain.cliffs.face")]
    [InlineData("""{ "terrain": { "cliffs": { "sheer": 1.5 } }, "biomes": [{ "id": "a" }] }""", "$.terrain.cliffs.sheer")]
    [InlineData("""{ "terrain": { "cliffs": { "gentle_face": 0 } }, "biomes": [{ "id": "a" }] }""", "$.terrain.cliffs.gentle_face")]
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
    [InlineData("""{ "biomes": [{ "id": "a" }], "liquids": [{ "id": "goo", "roughness": 2 }] }""", "$.liquids[0].roughness")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "liquids": [{ "id": "goo", "color": "green" }] }""", "$.liquids[0].color")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "liquids": [{ "id": "goo", "sheen": 2 }] }""", "$.liquids[0].sheen")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "liquids": [{ "id": "goo", "bubbles": -1 }] }""", "$.liquids[0].bubbles")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "liquids": [{ "id": "goo", "ripple_size": 0 }] }""", "$.liquids[0].ripple_size")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "liquids": [{ "id": "goo", "flow": -1 }] }""", "$.liquids[0].flow")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "liquids": [{ "id": "goo", "ripples": 1.5 }] }""", "$.liquids[0].ripples")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "liquids": [{ "id": "goo", "edge_color": "scum" }] }""", "$.liquids[0].edge_color")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "liquids": [{ "id": "goo" }, { "id": "goo" }] }""", "$.liquids[1].id")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "liquids": [{ "id": "goo", "collect": { "item": "goo", "count": 0 } }] }""", "$.liquids[0].collect.count")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "liquids": [{ "id": "goo", "bucket": "../goo" }] }""", "$.liquids[0].bucket")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "liquids": [{ "id": "pond", "water": true, "bucket": "goo_bucket" }] }""", "$.liquids[0].bucket")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "regrow": 0 }] }""", "$.objects[0].regrow")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "harvest": true, "refine": true }] }""", "$.objects[0].harvest")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "drops": [{ "item": "x", "count": 3, "count_max": 2 }] }] }""", "$.objects[0].drops[0].count_max")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "drops": [{ "item": "x", "chance": 1.5 }] }] }""", "$.objects[0].drops[0].chance")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "loot": { "table": "none" } }] }""", "$.objects[0].loot.table")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "loot_tables": { "t": { "items": [{ "item": "x" }] } }, "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "loot": { "table": "t", "picks": 0 } }] }""", "$.objects[0].loot.picks")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "loot_tables": { "t": { "items": [{ "item": "x" }] } }, "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "loot": { "table": "t", "picks": 2, "picks_max": 1 } }] }""", "$.objects[0].loot.picks_max")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "loot_tables": { "t": { "items": [] } } }""", "$.loot_tables.t.items")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "loot_tables": { "t": { "items": [{ "item": "x", "weight": 0 }] } } }""", "$.loot_tables.t.items[0].weight")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "loot_tables": { "t": { "items": [{ "item": "x", "count": 3, "count_max": 1 }] } } }""", "$.loot_tables.t.items[0].count_max")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "glow": { "color": "glowy" } }] }""", "$.objects[0].glow.color")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "glow": { "range": 0 } }] }""", "$.objects[0].glow.range")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "mesh": { "file": "m.glb", "clips": { "break_seconds": 1, "fade_seconds": 2 } } }] }""", "$.objects[0].mesh.clips.fade_seconds")]
    [InlineData("""{ "biomes": [{ "id": "a", "scatter": [{ "object": "g", "spacing": 10, "chance": 1, "perch": { "rise": 0 } }] }], "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "health": 5 }] }""", "$.biomes[0].scatter[0].perch.rise")]
    [InlineData("""{ "biomes": [{ "id": "a", "scatter": [{ "object": "g", "spacing": 10, "chance": 1, "cluster": { "cover": 0 } }] }], "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "health": 5 }] }""", "$.biomes[0].scatter[0].cluster.cover")]
    [InlineData("""{ "biomes": [{ "id": "a", "scatter": [{ "object": "g", "spacing": 10, "chance": 1, "cluster": { "outside": 2 } }] }], "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "health": 5 }] }""", "$.biomes[0].scatter[0].cluster.outside")]
    [InlineData("""{ "biomes": [{ "id": "a", "scatter": [{ "object": "g", "spacing": 10, "chance": 1, "cluster": { "scale": 0 } }] }], "objects": [{ "id": "g", "collider": { "shape": "sphere", "radius": 1 }, "health": 5 }] }""", "$.biomes[0].scatter[0].cluster.scale")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "s", "footprint": 5, "scene": { "layouts": [] } }] }""", "$.objects[0].scene.layouts")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "s", "footprint": 0, "scene": { "layouts": [{ "pieces": [{ "object": "g", "at": [0, 0] }] }] } }, { "id": "g", "collider": { "shape": "sphere", "radius": 1 } }] }""", "$.objects[0].scene")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "s", "footprint": 5, "scene": { "layouts": [{ "pieces": [{ "object": "x", "at": [0, 0] }] }] } }] }""", "$.objects[0].scene.layouts[0].pieces[0].object")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "s", "footprint": 5, "scene": { "layouts": [{ "pieces": [{ "object": "s", "at": [0, 0] }] }] } }] }""", "$.objects[0].scene.layouts[0].pieces[0].object")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "s", "footprint": 5, "scene": { "layouts": [{ "pieces": [{ "object": "g", "at": [6, 0] }] }] } }, { "id": "g", "collider": { "shape": "sphere", "radius": 1 } }] }""", "$.objects[0].scene.layouts[0].pieces[0].at")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "s", "footprint": 5, "scene": { "layouts": [{ "pieces": [{ "object": "g", "at": [1, 0], "chance": 0 }] }] } }, { "id": "g", "collider": { "shape": "sphere", "radius": 1 } }] }""", "$.objects[0].scene.layouts[0].pieces[0].chance")]
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
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "markers": [{ "type": "object", "ref": "tree" }] }""", "$.markers[0].ref")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "markers": [{ "type": "object" }] }""", "$.markers[0].ref")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "markers": [{ "type": "part" }] }""", "$.markers[0].ref")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "markers": [{ "type": "part", "ref": "fabricator" }] }""", "$.markers[0].ref")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "terrain": [{ "op": "basin", "radius": 2, "blend": 3, "depth": 1, "wobble": 0.6 }] }""", "$.terrain[0].wobble")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "terrain": [{ "op": "basin", "radius": 4, "blend": 4, "depth": 1, "wobble": 0.4 }] }""", "$.terrain[0]")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "terrain": [{ "op": "basin", "radius": 2, "blend": 2, "depth": 1, "wobble": 0.4 }] }""", "$.terrain[0].blend")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0], "roadside": 2 } }""", "$.placement.roadside")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "spacing": [40, 60], "biomes": ["a"], "clearance": 8, "roadside": 2 } }""", "$.placement.roadside")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "spacing": [40, 60], "biomes": ["a"], "shore": 0 } }""", "$.placement.shore")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "pools": [{ "liquid": "oil", "radius": 4 }] }""", "$.pools[0].liquid")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "pools": [{ "liquid": "goo", "radius": 0 }] }""", "$.pools[0].radius")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "pools": [{ "liquid": "goo", "at": [6, 0], "radius": 5 }] }""", "$.pools[0]")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "models": [{ "file": "../ship.glb" }] }""", "$.models[0].file")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "models": [{ "file": "meshes/ship.png" }] }""", "$.models[0].file")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "models": [{ "at": [0, 0, 0] }] }""", "$.models[0].file")]
    [InlineData("""{ "id": "p", "radius": 10, "placement": { "fixed": [0, 0] }, "models": [{ "file": "meshes/ship.glb", "at": [8, 0, 8] }] }""", "$.models[0].at")]
    public void BadPoiGivesAClearErrorAndIsLeftOut(string json, string path)
    {
        using var mods = new TestMods().Mod("vanilla")
            .File("vanilla", WorldGenData.DefinitionFile, """
                { "biomes": [{ "id": "a" }], "grounds": [{ "id": "burnt", "texture": "b.png" }], "liquids": [{ "id": "goo" }],
                  "objects": [{ "id": "geyser", "collider": { "shape": "sphere", "radius": 1 } }] }
                """)
            .File("vanilla", "pois/p.json", json);
        var data = WorldGenData.Load(mods.Load());
        Assert.Contains(data.Diagnostics, d => d.Code == DiagnosticCodes.PoiInvalid && d.JsonPath == path);
        Assert.Empty(data.Pois);
    }

    [Fact]
    public void PoiModelsLoadFromTheirOwnModAndBelongToOneChunk()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("wrecks", dependencies: """[{ "id": "vanilla", "version": ">=1.0.0" }]""")
            .File("vanilla", WorldGenData.DefinitionFile, """{ "biomes": [{ "id": "a" }] }""")
            .File("wrecks", "pois/hulk.json", """
                { "id": "hulk", "radius": 40, "placement": { "fixed": [100, 100] }, "terrain": [{ "op": "flatten", "radius": 20, "blend": 10 }],
                  "models": [{ "file": "meshes/structures/hulk.glb", "at": [10, 0, -5], "yaw": 90 },
                             { "file": "vanilla:meshes/structures/mast.glb", "at": [-10, 0, 0], "solid": false }] }
                """);
        var loaded = mods.Load();
        var data = WorldGenData.Load(loaded);
        Assert.Empty(data.Diagnostics);
        var gen = new WorldGenerator(data, 1, WorldSize);
        var hulk = Assert.Single(gen.Pois);
        var models = Enumerable.Range(-3, 6).SelectMany(x => Enumerable.Range(-3, 6).Select(z => new ChunkCoord(
                (int)MathF.Floor(hulk.Centre.X / 64f) + x, (int)MathF.Floor(hulk.Centre.Y / 64f) + z)))
            .SelectMany(c => gen.GenerateChunk(c, 64f).Models).ToList();
        Assert.Equal([0, 1], models.Select(m => m.Index).Order());
        var first = models.Single(m => m.Index == 0);
        Assert.Equal(hulk.ToWorld(new Vector3(10, 0, -5)), first.Position);
        Assert.Equal(MathF.PI / 2f, first.Yaw, 4);
        Assert.True(first.Model.Solid);
        string Folder(string id) => loaded.Mods.Single(m => m.Id == id).Directory;
        Assert.Equal(Path.Combine(Folder("wrecks"), "meshes", "structures", "hulk.glb"), first.File);
        var mast = models.Single(m => m.Index == 1);
        Assert.False(mast.Model.Solid);
        Assert.Equal(Path.Combine(Folder("vanilla"), "meshes", "structures", "mast.glb"), mast.File);
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

    [Fact]
    public void ObjectsKeepTheirIdsAndSpotsWhenOtherRulesAreAddedOrMoved()
    {
        // A rule that places nothing, put first in every biome's list, moves every other rule one place down; the same
        // biome's rules reversed move them all. Neither may move an object or change its id (a save's destroyed ids).
        var definition = VanillaData.Value.Definition;
        var padded = definition with
        {
            Biomes = [.. definition.Biomes!.Select(b => b with
            {
                Scatter = b.Scatter is { Count: > 0 } rules
                    ? [rules[0] with { Object = definition.Objects!.First(o => rules.All(r => r.Object != o.Id)).Id, Chance = 0f }, .. rules]
                    : b.Scatter,
            })],
        };
        var reversed = definition with
        {
            Biomes = [.. definition.Biomes!.Select(b => b with { Scatter = b.Scatter is { } rules ? [.. Enumerable.Reverse(rules)] : null })],
        };
        var before = WorldGenData.FromDefinition(definition);
        var after = WorldGenData.FromDefinition(padded);
        Assert.Equal(before.ScatterRules.Select(r => r.Key), after.ScatterRules.Where((r, i) => after.ScatterRules.Take(i).Any(o => o.Biome == r.Biome)).Select(r => r.Key));
        Assert.Equal(before.ScatterRules.Select(r => r.Key).Order(), WorldGenData.FromDefinition(reversed).ScatterRules.Select(r => r.Key).Order());
        Assert.Equal(before.ScatterRules.Count, before.ScatterRules.Select(r => r.Key).Distinct().Count());

        var a = new WorldGenerator(before, Seed, VanillaSize, VanillaDepth);
        var b = new WorldGenerator(after, Seed, VanillaSize, VanillaDepth);
        var placed = 0;
        foreach (var (x, z) in new[] { (-3000f, -2000f), (0f, 0f), (2500f, 1500f), (-1200f, 2600f), (3800f, -2900f) })
        {
            var expected = a.ObjectsIn(x, z, x + 300f, z + 300f).Select(o => (o.Id, o.Position)).ToList();
            Assert.Equal(expected, b.ObjectsIn(x, z, x + 300f, z + 300f).Select(o => (o.Id, o.Position)));
            placed += expected.Count;
        }
        Assert.True(placed > 100, $"only {placed} objects to compare");
    }

    [Fact]
    public void FactionSitesStandOnTheirFactionsGround()
    {
        // Each bot faction's site (its lair with its smaller pieces) stands where its bots live, in good numbers on the vanilla
        // world but never crowding it: Scuttlers by scrap and wrecks, Rakehands by farms, Spoolers (rare on the surface) by
        // warehouses and industry, Threshers by the Farmbot Scrapyards and harvester wrecks. None within 100 m of a Workshop.
        var gen = Vanilla.Value;
        var lairs = new Dictionary<string, string>
        {
            ["scene_scuttler_nest"] = "scuttler_nest", ["scene_rakehand_shrine"] = "rakehand_shrine",
            ["scene_spooler_tower"] = "spooler_reel_tower", ["scene_thresher_den"] = "thresher_den",
        };
        var sites = new Dictionary<string, List<(string Biome, Vector2 At, bool Lair)>>();
        var marks = new Dictionary<string, int>();
        foreach (var rule in gen.Data.ScatterRules.Where(r => r.Object.Id!.StartsWith("scene_") && (lairs.ContainsKey(r.Object.Id) || r.Object.Id.EndsWith("_marks"))))
        {
            var s = rule.Spacing;
            var pieces = rule.Object.Scene!.Layouts!.Max(l => l.Pieces!.Count);
            for (var iz = (int)MathF.Floor(-VanillaDepth / 2 / s); iz <= (int)MathF.Floor(VanillaDepth / 2 / s); iz++)
            {
                for (var ix = (int)MathF.Floor(-VanillaSize / 2 / s); ix <= (int)MathF.Floor(VanillaSize / 2 / s); ix++)
                {
                    var placed = Enumerable.Range(0, pieces).Select(k => gen.Find(new WorldObjectId(rule.Key, ix, iz, k))).OfType<WorldObjectPlacement>().ToList();
                    if (placed.Count == 0)
                        continue;
                    var id = rule.Object.Id!;
                    if (!lairs.TryGetValue(id, out var lair))
                    {
                        marks[id] = marks.GetValueOrDefault(id) + 1;
                        continue;
                    }
                    var at = new Vector2(placed[0].Position.X, placed[0].Position.Z);
                    (sites.TryGetValue(id, out var list) ? list : sites[id] = []).Add((gen.Data.Biomes[rule.Biome].Id, at, placed.Any(p => p.Definition.Id == lair)));
                    foreach (var workshop in gen.Pois.Where(p => p.Poi.QualifiedId is "vanilla:outpost" or "vanilla:poi_02" or "vanilla:poi_13"))
                        Assert.True(Vector2.Distance(workshop.Centre, at) >= workshop.Radius + 100f, $"{id} at {at} is within 100 m of {workshop.Poi.QualifiedId}");
                }
            }
        }
        string Report() => string.Join("; ", sites.Select(kv => $"{kv.Key}: {kv.Value.Count} ("
            + string.Join(", ", kv.Value.GroupBy(v => v.Biome).Select(g => $"{g.Key} {g.Count()}")) + $", {kv.Value.Count(v => v.Lair)} with lair)"))
            + " | " + string.Join(", ", marks.Select(kv => $"{kv.Key} {kv.Value}"));
        // About 15 to 30 sites a faction on the 12 x 9 km world; Spoolers fewer. Every site has its lair.
        foreach (var (id, (low, high)) in new Dictionary<string, (int, int)>
        {
            ["scene_scuttler_nest"] = (15, 35), ["scene_rakehand_shrine"] = (15, 35), ["scene_spooler_tower"] = (4, 15), ["scene_thresher_den"] = (15, 35),
        })
        {
            Assert.True(sites.TryGetValue(id, out var list) && list.Count >= low && list.Count <= high, Report());
            Assert.True(list!.All(v => v.Lair), Report());
        }
        Assert.True(sites["scene_spooler_tower"].Count < sites.Where(kv => kv.Key != "scene_spooler_tower").Min(kv => kv.Value.Count), Report());
        // Scuttlers spread over the scrap of the meadows, fields and forests; Rakehands and Threshers keep to the farmland.
        Assert.True(sites["scene_scuttler_nest"].Select(v => v.Biome).Distinct().Count() >= 3, Report());
        Assert.Contains(sites["scene_rakehand_shrine"], v => v.Biome == "field");
        Assert.Contains(sites["scene_thresher_den"], v => v.Biome == "field");
        // A few loose marks round them, never more than the sites themselves by far.
        foreach (var faction in new[] { "scuttler", "rakehand", "spooler", "thresher" })
        {
            var count = marks.GetValueOrDefault($"scene_{faction}_marks");
            Assert.True(count > 0 && count <= 2 * sites.Single(kv => kv.Key.Contains(faction)).Value.Count, Report());
        }
    }

    [Fact]
    public void ModelsThatCollideCarryTheirColliderMesh()
    {
        // An object whose mesh collides is walked into and onto by its own collider mesh, so its model must have one.
        var gen = Vanilla.Value;
        var colliding = gen.Data.Objects.Values.Where(o => o.Mesh is { Collide: true }).ToList();
        Assert.NotEmpty(colliding);
        foreach (var o in colliding)
        {
            var bytes = File.ReadAllBytes(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", o.Mesh!.File!));
            Assert.Equal("glTF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            var length = BitConverter.ToInt32(bytes, 12);
            using var json = System.Text.Json.JsonDocument.Parse(bytes.AsMemory(20, length));
            Assert.Contains(json.RootElement.GetProperty("meshes").EnumerateArray(),
                m => m.TryGetProperty("name", out var name) && name.GetString()!.StartsWith("collider", StringComparison.Ordinal));
        }
    }
}
