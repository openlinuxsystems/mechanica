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
    public void SoilUsesWaterOnlyWhileACropGrows()
    {
        var soil = new SoilState();
        soil.AddWater(100, 60);
        Assert.Equal(60f, soil.Water);
        soil.Tick(10, 1, 40);
        Assert.Equal(60f, soil.Water); // nothing planted

        soil.Plant("vanilla:carrot_seed");
        var revision = soil.Revision;
        soil.Tick(5, 1, 40);
        Assert.Equal((5f, 55f, 0), (soil.Grown, soil.Water, soil.StageFor(40)));
        Assert.Equal(revision, soil.Revision); // same stage: nothing new to show
        soil.Tick(5, 1, 40);
        Assert.Equal(1, soil.StageFor(40));
        Assert.True(soil.Revision > revision);
        Assert.Throws<InvalidOperationException>(() => soil.Plant("vanilla:carrot_seed"));
        soil.Clear();
        Assert.False(soil.IsEmpty);
    }

    [Theory]
    [InlineData("""{ "id": "c", "name": "C", "category": "consumable", "food": {} }""", "$.food.health", "health is required")]
    [InlineData("""{ "id": "c", "name": "C", "category": "consumable", "seed": { "grow_time": 10 } }""", "$.seed.crop", "crop")]
    [InlineData("""{ "id": "c", "name": "C", "category": "consumable", "seed": { "crop": "carrot", "grow_time": 0 } }""", "$.seed.grow_time", "greater than 0")]
    [InlineData("""{ "id": "c", "name": "C", "category": "consumable", "watering": { "seconds": -1 } }""", "$.watering.seconds", "greater than 0")]
    [InlineData("""{ "id": "w", "name": "W", "category": "weapon", "weapon": { "damage": 1, "interval": 1, "range": 1, "pellets": 99 } }""", "$.weapon.pellets", "1 to 32")]
    [InlineData("""{ "id": "w", "name": "W", "category": "weapon", "weapon": { "damage": 1, "interval": 1 } }""", "$.weapon.range", "range (m) is required")]
    [InlineData("""{ "id": "w", "name": "W", "category": "weapon", "weapon": { "damage": 1, "interval": 1, "range": 1, "spread": 95 } }""", "$.weapon.spread", "under 90")]
    [InlineData("""{ "id": "s", "name": "S", "category": "resource", "soil": { "capacity": 10 } }""", "$.soil", "only placeable parts")]
    [InlineData("""{ "id": "s", "name": "S", "category": "resource", "respawn_point": true }""", "$.respawn_point", "only placeable parts")]
    public void SurvivalSectionsAreChecked(string json, string path, string message)
    {
        using var mods = new TestMods().Mod("m");
        mods.Part("m", "p.json", json);
        var diagnostic = Assert.Single(mods.Load().Diagnostics, d => d.File == "mods/m/parts/p.json" && d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(path, diagnostic.JsonPath);
        Assert.Contains(message, diagnostic.Message);
    }

    [Fact]
    public void PlaceablePartsCannotBeFood()
    {
        using var mods = new TestMods().Mod("m");
        mods.Part("m", "p.json", TestMods.Block().Replace("\"id\"", "\"food\": { \"health\": 5 }, \"id\""));
        Assert.Contains(mods.Load().Diagnostics, d => d.JsonPath == "$.food" && d.Message.Contains("only items"));
    }

    [Theory]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "tree", "collider": { "shape": "sphere", "radius": 1 }, "health": 0 }] }""", "$.objects[0].health")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "tree", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "drops": [{ "item": "Bad Id" }] }] }""", "$.objects[0].drops[0].item")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "tree", "collider": { "shape": "sphere", "radius": 1 }, "health": 5, "drops": [{ "item": "wood", "count": 0 }] }] }""", "$.objects[0].drops[0].count")]
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
        Assert.Contains(data.Objects["tree"].Drops!, d => d.Item == "scrap_wood");
        Assert.Contains(data.Objects["rock_node"].Drops!, d => d.Item == "scrap_metal");
    }

    [Fact]
    public void VanillaSurvivalContentHangsTogether()
    {
        var mods = ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"));
        Assert.DoesNotContain(mods.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var settings = SurvivalSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "survival.json")));
        foreach (var start in settings.StartingItems)
            Assert.True(mods.FindDefinition(start.Item) is not null, $"starting item {start.Item} exists");
        Assert.NotNull(mods.FindDefinition(settings.WaterItem)?.Watering);
        foreach (var seed in new[] { "carrot_seed", "potato_seed", "tomato_seed" })
            Assert.NotNull(mods.FindDefinition(mods.FindDefinition(seed)!.Seed!.Crop!)?.Food);
        foreach (var weapon in new[] { "potato_blaster", "potato_scattergun", "potato_launcher", "popper" })
            Assert.NotNull(mods.FindDefinition(mods.FindDefinition(weapon)!.Weapon!.Ammo!));
        Assert.NotNull(mods.FindDefinition("soil")!.Soil);
        Assert.True(mods.FindDefinition("respawn_bed")!.RespawnPoint);
        Assert.Contains("vanilla:carrot", mods.FindDefinition("fridge")!.Inventory!.Accepts!.Select(a => Identifiers.Qualify(a)));
        var recipes = RecipeBook.Load(mods);
        Assert.Empty(recipes.Diagnostics);
        foreach (var made in new[] { "soil", "respawn_bed", "potato_blaster", "potato_scattergun", "potato_launcher", "popper" })
            Assert.Contains(recipes.All, r => r.Outputs.Any(o => o.Item == $"vanilla:{made}"));
    }
}
