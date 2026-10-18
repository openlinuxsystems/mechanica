using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.Survival;

public class SurvivalDataTests
{
    [Fact]
    public void TheGamesSurvivalSettingsLoad()
    {
        var settings = SurvivalSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "survival.json")));
        Assert.Equal(100f, settings.MaxHealth);
        Assert.NotEmpty(settings.StartingItems);
    }

    [Theory]
    [InlineData("""{ "max_health": 0 }""", "max_health")]
    [InlineData("""{ "low_health": 1 }""", "low_health")]
    [InlineData("""{ "aim_tolerance": 0 }""", "aim_tolerance")]
    [InlineData("""{ "starting_items": [{ "item": "carrot", "count": 0 }] }""", "starting_items")]
    [InlineData("""{ "fall": { "min_speed": -1 } }""", "fall.min_speed")]
    [InlineData("""{ "impact": { "min_speed": 0 } }""", "impact.min_speed")]
    [InlineData("""{ "stamina": 5 }""", "stamina")]
    public void BadSettingsAreRefused(string json, string named)
    {
        var e = Assert.Throws<FormatException>(() => SurvivalSettings.FromJson(json));
        Assert.Contains(named, e.Message);
    }

    [Fact]
    public void FallAndImpactCurves()
    {
        var fall = new FallSettings { MinSpeed = 10, DamagePerSpeed = 5 };
        Assert.Equal(0f, fall.DamageAt(10));
        Assert.Equal(25f, fall.DamageAt(15));
        Assert.Equal(11f, new ImpactSettings { MinSpeed = 8, SpeedPerDurability = 1 }.ThresholdFor(3));
    }

    [Fact]
    public void SoilStoresWhatItsScriptDecides()
    {
        var soil = new SoilState();
        soil.AddWater(100, 60);
        Assert.Equal(60f, soil.Water);
        soil.Set(grown: 10, stage: 2, updated: 5);
        Assert.Equal((0f, 0, 5.0), (soil.Grown, soil.Stage, soil.Updated)); // nothing planted: no growth to store

        soil.Plant("vanilla:carrot_seed");
        var revision = soil.Revision;
        soil.Set(grown: 5, water: 55, stage: 0, updated: 10);
        Assert.Equal((5f, 55f, 0, 10.0), (soil.Grown, soil.Water, soil.Stage, soil.Updated));
        Assert.Equal(revision, soil.Revision); // same stage, still wet: nothing new to show
        soil.Set(grown: 10, stage: 1);
        Assert.True(soil.Revision > revision);
        Assert.Equal((10f, 55f, 10.0), (soil.Grown, soil.Water, soil.Updated)); // what is left out stays
        revision = soil.Revision;
        soil.Set(water: 0);
        Assert.True(soil.Revision > revision); // ran dry
        Assert.False(soil.Ripe);
        soil.Set(stage: SoilState.Stages);
        Assert.True(soil.Ripe);
        Assert.Throws<ArgumentOutOfRangeException>(() => soil.Set(stage: SoilState.Stages + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => soil.Set(grown: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => soil.Set(water: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => soil.Set(updated: double.PositiveInfinity));
        Assert.Throws<InvalidOperationException>(() => soil.Plant("vanilla:carrot_seed"));
        soil.Clear();
        Assert.Equal((null, 0f, 0, false), (soil.Seed, soil.Grown, soil.Stage, soil.Ripe));
        Assert.True(soil.IsEmpty);
    }

    [Theory]
    [InlineData("""{ "id": "c", "name": "C", "category": "consumable", "seed": { "grow_time": 10 } }""", "$.seed.crop", "crop")]
    [InlineData("""{ "id": "c", "name": "C", "category": "consumable", "seed": { "crop": "carrot", "grow_time": 0 } }""", "$.seed.grow_time", "greater than 0")]
    [InlineData("""{ "id": "s", "name": "S", "category": "resource", "soil": { "capacity": 10 } }""", "$.soil", "only placeable parts")]
    [InlineData("""{ "id": "s", "name": "S", "category": "resource", "respawn_point": true }""", "$.respawn_point", "only placeable parts")]
    [InlineData("""{ "id": "r", "name": "R", "category": "resource", "refine": [{ "item": "block", "count": 20 }] }""", "$.refine", "only loose parts")]
    [InlineData("""{ "id": "r", "name": "R", "category": "resource", "breaks_into": { "parts": [{ "part": "rod" }] } }""", "$.breaks_into", "only placeable parts")]
    [InlineData("""{ "id": "r", "name": "R", "category": "resource", "loose": true, "mass": 5, "friction": 0.5, "buoyancy": 0, "flammable": false, "size": [1, 1, 4], "refine": [{ "item": "block", "count": 0 }] }""", "$.refine[0].count", "1 or more")]
    [InlineData("""{ "id": "r", "name": "R", "category": "resource", "loose": true, "mass": 5, "friction": 0.5, "buoyancy": 0, "flammable": false, "size": [1, 1, 4], "sledge_damage": -1 }""", "$.sledge_damage", "0 or more")]
    [InlineData("""{ "id": "r", "name": "R", "category": "resource", "loose": true, "mass": 5, "friction": 0.5, "buoyancy": 0, "flammable": false, "size": [1, 1, 4], "breaks_into": { "count": 99, "parts": [{ "part": "rod" }] } }""", "$.breaks_into.count", "1 to 16")]
    public void SurvivalSectionsAreChecked(string json, string path, string message)
    {
        using var mods = new TestMods().Mod("m");
        mods.Part("m", "p.json", json);
        var diagnostic = Assert.Single(mods.Load().Diagnostics, d => d.File == "mods/m/parts/p.json" && d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(path, diagnostic.JsonPath);
        Assert.Contains(message, diagnostic.Message);
    }

    [Fact]
    public void PlaceablePartsCannotBeSeeds()
    {
        using var mods = new TestMods().Mod("m");
        mods.Part("m", "p.json", TestMods.Block().Replace("\"id\"", "\"seed\": { \"crop\": \"carrot\", \"grow_time\": 5 }, \"id\""));
        Assert.Contains(mods.Load().Diagnostics, d => d.JsonPath == "$.seed" && d.Message.Contains("only items"));
    }

    [Theory]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "tree", "collider": { "shape": "sphere", "radius": 1 }, "health": 0 }] }""", "$.objects[0].health")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "tree", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "drops": [{ "item": "Bad Id" }] }] }""", "$.objects[0].drops[0].item")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "tree", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "drops": [{ "item": "wood", "count": 0 }] }] }""", "$.objects[0].drops[0].count")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "rock", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "damage_from": ["hammer"] }] }""", "$.objects[0].damage_from[0]")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "rock", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "breaks_into": { "count": 0, "parts": [{ "part": "chunk" }] } }] }""", "$.objects[0].breaks_into.count")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "rock", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "breaks_into": { "count": 1 } }] }""", "$.objects[0].breaks_into.parts")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "rock", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "breaks_into": { "parts": [{ "part": "chunk", "weight": 0 }] } }] }""", "$.objects[0].breaks_into.parts[0].weight")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "rock", "collider": { "shape": "box", "size": [1, 1, 1] }, "outcrop": { "cubes": [8, 4], "height": [1, 3] } }] }""", "$.objects[0].outcrop.cubes")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "rock", "collider": { "shape": "box", "size": [1, 1, 1] }, "outcrop": { "cubes": [4, 8], "height": [0, 3] } }] }""", "$.objects[0].outcrop.height")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "rock", "collider": { "shape": "box", "size": [1, 1, 1] }, "mesh": { "file": "../rock.glb" } }] }""", "$.objects[0].mesh.file")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "rock", "collider": { "shape": "box", "size": [1, 1, 1] }, "max_slope": -1 }] }""", "$.objects[0].max_slope")]
    public void BreakableWorldObjectsAreChecked(string json, string path)
    {
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, json);
        Assert.Contains(WorldGenData.Load(mods.Load()).Diagnostics, d => d.Code == DiagnosticCodes.WorldGenInvalid && d.JsonPath == path);
    }

    [Fact]
    public void VanillaTreesAndRocksBreakIntoResources()
    {
        var data = WorldGenData.Load(ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));
        Assert.DoesNotContain(data.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(data.Objects["rock_node"].Drops!, d => d.Item == "scrap_metal");
    }

    [Theory]
    // Tree Harvesting spec §4.3: log segments and Wood Rods per tree, each rod 20 Scrap Wood Blocks.
    [InlineData("birch_tree", "birch_log", 2, 3, 3, 6)]
    [InlineData("leafy_tree", "wood_log", 4, 6, 8, 12)]
    [InlineData("spruce_tree", "wood_log", 4, 6, 8, 12)]
    [InlineData("pine_tree", "pine_log", 8, 12, 16, 24)]
    public void VanillaTreesFellIntoLogsThatBreakIntoWoodRods(string tree, string log, int fewestLogs, int mostLogs, int fewestRods, int mostRods)
    {
        var mods = ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"));
        var data = WorldGenData.Load(mods);
        Assert.DoesNotContain(data.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var o = data.Objects[tree];
        Assert.NotNull(o.Fell);
        Assert.False(o.Refine);
        var breaks = o.BreaksInto!;
        Assert.Equal(log, Assert.Single(breaks.Parts!).Part);
        Assert.Equal(fewestLogs, breaks.CountFor(0f));
        Assert.Equal(mostLogs, breaks.CountFor(0.999f));
        var logPart = mods.FindDefinition(log)!;
        Assert.True(logPart.Loose);
        Assert.Equal("wood_rod", Assert.Single(logPart.BreaksInto!.Parts!).Part);
        Assert.InRange(fewestLogs * logPart.BreaksInto.CountFor(0f), fewestRods, mostRods);
        Assert.InRange(mostLogs * logPart.BreaksInto.CountFor(0.999f), fewestRods, mostRods);
        var rod = mods.FindDefinition("wood_rod")!;
        Assert.Equal(("vanilla:scrap_wood_block", 20), (Identifiers.Qualify(Assert.Single(rod.Refine!).Item!), rod.Refine![0].Count));
        // Its own knock and fall, and a model with a far version, all shipped.
        foreach (var sound in o.Sounds!.Values)
            Assert.True(File.Exists(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", "sounds", "trees", sound + ".ogg")), sound);
        Assert.True(File.Exists(data.MeshFileOf(o)), tree);
        Assert.True(File.Exists(data.LodFileOf(o)), tree);
        // Only a spinning saw cuts a standing Pine; the others take the Sledgehammer too.
        Assert.True(o.TakesDamageFrom(DamageKinds.Saw));
        Assert.Equal(tree != "pine_tree", o.TakesDamageFrom(DamageKinds.Sledge));
        Assert.False(o.TakesDamageFrom(DamageKinds.Drill));
    }

    [Fact]
    public void VanillaSurvivalContentHangsTogether()
    {
        var mods = ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"));
        Assert.DoesNotContain(mods.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var settings = SurvivalSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "survival.json")));
        foreach (var start in settings.StartingItems)
            Assert.True(mods.FindDefinition(start.Item) is not null, $"starting item {start.Item} exists");
        // Food, weapons, seeds and the bucket are Lua item scripts (chunk 20b) whose numbers are properties.
        var bucket = mods.FindDefinition(settings.WaterItem)!;
        Assert.Equal("scripts/watering.lua", bucket.Script);
        Assert.True(bucket.Properties!["seconds"]!.GetValue<double>() > 0);
        foreach (var seed in new[] { "carrot_seed", "potato_seed", "tomato_seed" })
        {
            Assert.Equal("scripts/seed.lua", mods.FindDefinition(seed)!.Script);
            var crop = mods.FindDefinition(mods.FindDefinition(seed)!.Seed!.Crop!)!;
            Assert.Equal("scripts/food.lua", crop.Script);
            Assert.True(crop.Properties!["health"]!.GetValue<double>() > 0);
        }
        foreach (var weapon in new[] { "potato_blaster", "potato_scattergun", "potato_launcher", "popper" })
        {
            var definition = mods.FindDefinition(weapon)!;
            Assert.Equal("scripts/potato_gun.lua", definition.Script);
            Assert.NotNull(mods.FindDefinition(definition.Properties!["ammo"]!.GetValue<string>()));
        }
        Assert.NotNull(mods.FindDefinition("soil")!.Soil);
        Assert.True(mods.FindDefinition("respawn_bed")!.RespawnPoint);
        Assert.Contains("vanilla:carrot", mods.FindDefinition("fridge")!.Inventory!.Accepts!.Select(a => Identifiers.Qualify(a)));
        var recipes = RecipeBook.Load(mods);
        Assert.Empty(recipes.Diagnostics);
        foreach (var made in new[] { "soil", "respawn_bed", "potato_blaster", "potato_scattergun", "potato_launcher", "popper" })
            Assert.Contains(recipes.All, r => r.Outputs.Any(o => o.Item == $"vanilla:{made}"));
    }
}
