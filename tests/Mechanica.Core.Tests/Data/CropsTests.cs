using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Building;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// Crops batch K1 (crops map): the four seeds we have take the wiki's seed stats (3/2/4/3/Y, stack 20: 4.5 kg, durability
/// 2, friction 0.4, buoyancy 0.3, flammable), every crop grows in 1260 s, harvests keep our yields (Potato 10, as SM) with
/// 1 to 3 seeds back (farmed Cotton exactly 1), and the raid values are SM's for these crops. One full Water Bucket waters a
/// soil mound for a whole crop (both 1260 s), a Growbed holds 2160 s, and a Water Cannon's jets from one bucket water as
/// much as throwing it. Wild cotton keeps its 40 % seed.
/// Batch K2 adds the Pigment Flower: a 6 kg craft input, not food, whose seed grows like the others (2 flowers, exactly
/// 1 seed back, raid value 0) in soil or a Growbed; wild ones in meadows and forests are picked like wild cotton; and the
/// Fabricator makes 10 Paint Ammo from one in 5 s, no longer from tomatoes.
/// Batch K3 adds Beetroot, Banana and Chili: fruit stats, food heals from the Food page (Beetroot 30, Banana 35), the Chili
/// a joke food that hurts 50; their seeds grow like the others (2 crops, 1 to 3 seeds back, raid values 5, 10 and 10) in
/// soil or a Growbed; Beetroot Seed is common loot, Banana and Chili Seeds epic, the Chili a rare legendary find.
/// Wild cotton is one light mesh like the wild Pigment Flower.
/// Batch K4 adds Blueberry, Orange, Broccoli and Pineapple: fruit stats, heals 40, 45, 50 and 55; seeds that grow like the
/// others (raid values 31, 61, 90 and 161), Broccoli and Pineapple thirsty (two buckets a crop); only in the rare loot tiers;
/// and the Fabricator's Grazer Shake from 100 Ice Blocks, 3 Oranges and a Grazer Milk (order 3210).
/// Batch K5 adds Corn: the wiki's stats (6 kg, stack 10), heals 4, no seed; wild corn in fields and meadows (and at the
/// edges of corn fields) breaks under two sledge blows or a creation's saw for 3 to 4 Corn and regrows; tame Grazers eat
/// it from their Feeding Trough like carrots; and the Fabricator makes 2 Poppers from 5 Corn and a Gasoline (order 2541).
/// </summary>
public class CropsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static readonly Lazy<WorldGenDefinition> World = new(() => WorldGenData.Load(Vanilla.Value).Definition);

    private static WorldObjectDefinition WorldObject(string id) => World.Value.Objects!.Single(o => o.Id == id);

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("carrot_seed")]
    [InlineData("tomato_seed")]
    [InlineData("potato_seed")]
    [InlineData("cotton_seed")]
    [InlineData("pigment_flower_seed")]
    [InlineData("beetroot_seed")]
    [InlineData("banana_seed")]
    [InlineData("chili_seed")]
    [InlineData("blueberry_seed")]
    [InlineData("orange_seed")]
    [InlineData("broccoli_seed")]
    [InlineData("pineapple_seed")]
    public void SeedsTakeTheWikisSeedStats(string id)
    {
        var part = Part(id);

        Assert.Equal((4.5, 2, 0.4, 0.3, true, 20),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, part.StackSize!.Value));
        Assert.Equal(InventoryTab.Consumable, InventoryTabs.Of(part, false));
        Assert.Equal("scripts/seed.lua", part.Script);
    }

    [Theory]
    [InlineData("carrot_seed", "carrot", 2, 1, 3, 2)]
    [InlineData("tomato_seed", "tomato", 2, 1, 3, 1)]
    [InlineData("potato_seed", "potato", 10, 1, 3, 1)]
    [InlineData("cotton_seed", "cotton", 2, 1, 1, 0)]
    [InlineData("pigment_flower_seed", "pigment_flower", 2, 1, 1, 0)]
    [InlineData("beetroot_seed", "beetroot", 2, 1, 3, 5)]
    [InlineData("banana_seed", "banana", 2, 1, 3, 10)]
    [InlineData("chili_seed", "chili", 2, 1, 3, 10)]
    public void EveryCropGrowsIn1260SecondsWithItsHarvestAndRaidValue(string id, string crop, int yield, int seeds, int seedsMax, double value)
    {
        var seed = Part(id).Seed!;

        Assert.Equal((crop, 1260f, yield, seeds, seedsMax, (float)value), (seed.Crop, seed.GrowTime, seed.Yield, seeds, seed.SeedsMax, seed.Value));
        Assert.Equal(seeds, seed.Seeds);
        Assert.Null(seed.Thirst); // default 1: one second of water a second
        Assert.True(Part(crop).StackSize >= yield, $"a {crop} harvest fits one stack");
    }

    [Theory]
    [InlineData("blueberry_seed", "blueberry", 31, null)]
    [InlineData("orange_seed", "orange", 61, null)]
    [InlineData("broccoli_seed", "broccoli", 90, 2f)]
    [InlineData("pineapple_seed", "pineapple", 161, 2f)]
    public void TheLateCropsGrowLikeTheOthersWithTheirRaidValueAndThirst(string id, string crop, double value, float? thirst)
    {
        var seed = Part(id).Seed!;

        // Raid values (crops map, open question 4): one of each reaches the raid level SM's would; the raid table's levels
        // start at 10, 31, 61, 101 and 161, so a Blueberry draws a level 2 raid, an Orange or a Broccoli level 3, a Pineapple 5.
        Assert.Equal((crop, 1260f, 2, 1, 3, (float)value), (seed.Crop, seed.GrowTime, seed.Yield, seed.Seeds, seed.SeedsMax, seed.Value));
        Assert.Equal(thirst, seed.Thirst);
        Assert.Equal($"meshes/crops/{crop}_plant.glb", seed.Plant);
        Assert.Contains(id, Part("growbed").Soil!.Seeds!);
    }

    [Theory]
    [InlineData("broccoli_seed")]
    [InlineData("pineapple_seed")]
    public void ThirstyCropsNeedTwoBuckets(string id)
    {
        var bucket = Part("water_bucket_full").Properties!["seconds"]!.GetValue<double>();
        var seed = Part(id).Seed!;
        var drinks = seed.GrowTime!.Value * seed.Thirst!.Value;

        Assert.True(drinks > bucket && drinks > Part("soil").Soil!.Capacity, $"one bucket is not enough ({drinks} s)");
        Assert.True(drinks <= 2 * bucket, $"two buckets are ({drinks} s)");
        Assert.True(drinks > Part("growbed").Soil!.Capacity, "a full Growbed is not enough either");
        Assert.Contains("twice", Part(id).Description);
    }

    [Fact]
    public void OneBucketGrowsOneCrop()
    {
        var bucket = Part("water_bucket_full").Properties!["seconds"]!.GetValue<double>();
        var soil = Part("soil").Soil!;
        var growbed = Part("growbed").Soil!;
        var cannon = Part("water_cannon").Properties!;

        Assert.Equal(1260.0, bucket);
        Assert.Equal(1260f, soil.Capacity);
        Assert.Equal(2160f, growbed.Capacity);
        Assert.Equal(bucket, cannon["water"]!.GetValue<double>() * cannon["jets"]!.GetValue<int>(), 3);
        Assert.Equal("water_bucket_full", cannon["water_item"]!.GetValue<string>());
        foreach (var id in new[] { "carrot_seed", "tomato_seed", "potato_seed", "cotton_seed", "pigment_flower_seed", "beetroot_seed", "banana_seed", "chili_seed",
                     "blueberry_seed", "orange_seed" })
        {
            var drinks = Part(id).Seed!.GrowTime!.Value * (Part(id).Seed!.Thirst ?? 1f);
            Assert.True(drinks <= bucket && drinks <= soil.Capacity, $"a {id} crop drinks one bucket at most ({drinks} s)");
        }
    }

    [Fact]
    public void WildCottonKeepsItsFortyPercentSeed()
    {
        var path = Path.Combine(TestMods.RepoRoot, "mods", "vanilla", "world", "worldgen.json");
        var json = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        })!;
        var drops = json.AsObject().Select(p => p.Value).OfType<JsonArray>().SelectMany(a => a.OfType<JsonObject>())
            .Single(o => (string?)o["id"] == "wild_cotton")["drops"]!.AsArray();
        var seed = drops.OfType<JsonObject>().Single(d => (string?)d["item"] == "cotton_seed");

        Assert.Equal((1, 0.4), (seed["count"]!.GetValue<int>(), seed["chance"]!.GetValue<double>()));
    }

    [Fact]
    public void ThePigmentFlowerIsACraftInputNotFood()
    {
        var flower = Part("pigment_flower");

        Assert.Equal((6.0, 2, 0.4, 0.3, true, 20),
            (Math.Round((double)flower.Mass!.Value, 3), (int)flower.Durability!.Value, Math.Round((double)flower.Friction!.Value, 3),
                Math.Round((double)flower.Buoyancy!.Value, 3), flower.Flammable!.Value, flower.StackSize!.Value));
        Assert.Equal(PartCategory.Resource, flower.Category);
        Assert.Null(flower.Script); // not eaten: no food.lua
        Assert.Equal("textures/items/pigment_flower_icon.png", flower.Icon);
        Assert.Equal("meshes/items/pigment_flower_held.glb", flower.Held?.Mesh);
        Assert.Equal("meshes/crops/pigment_flower_plant.glb", Part("pigment_flower_seed").Seed!.Plant);
        foreach (var file in new[] { "meshes/items/pigment_flower.glb", "meshes/items/pigment_flower_held.glb", "meshes/items/pigment_flower_seed.glb",
                     "meshes/crops/pigment_flower_plant.glb", "meshes/world/props/wild_pigment_flower.glb", "textures/items/pigment_flower_icon.png",
                     "textures/items/pigment_flower_seed_icon.png" })
            Assert.True(File.Exists(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", file)), $"{file} is built");
    }

    [Fact]
    public void TheFabricatorMakesTenPaintAmmoFromOnePigmentFlower()
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find("paint_ammo")!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(["vanilla:pigment_flower:1"], recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(("vanilla:paint_ammo", 10), (recipe.Outputs.Single().Item, recipe.Outputs.Single().Count));
        Assert.Equal((5f, 2440), (recipe.Time, recipe.Definition.Order));
        // Its place in the tools group is its own, and tomatoes make no paint any more.
        Assert.Single(book.All, r => r.Definition.Order == 2440);
        Assert.DoesNotContain(book.All, r => r.Outputs.Any(o => o.Item == "vanilla:paint_ammo") && r.Inputs.Any(i => i.Item == "vanilla:tomato"));
    }

    [Fact]
    public void TheGrowbedGrowsPigmentFlowers()
    {
        var growbed = Part("growbed");

        Assert.Equal(["carrot_seed", "potato_seed", "tomato_seed", "cotton_seed", "pigment_flower_seed", "beetroot_seed", "banana_seed", "chili_seed",
                "blueberry_seed", "orange_seed", "broccoli_seed", "pineapple_seed"],
            growbed.Soil!.Seeds);
        Assert.Contains("pigment flower, beetroot, banana, chili, blueberry, orange, broccoli or pineapple seeds", growbed.Description);
    }

    [Fact]
    public void TheWildPigmentFlowerIsOneLightMesh()
    {
        // The world draws an object from the first mesh in its file (TerrainChunk), many times over a biome.
        var bytes = File.ReadAllBytes(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", "meshes", "world", "props", "wild_pigment_flower.glb"));
        using var doc = JsonDocument.Parse(bytes.AsMemory(20, BitConverter.ToInt32(bytes, 12)));
        var meshes = doc.RootElement.GetProperty("meshes");
        var accessors = doc.RootElement.GetProperty("accessors");
        var triangles = meshes.EnumerateArray().SelectMany(m => m.GetProperty("primitives").EnumerateArray())
            .Sum(p => accessors[p.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32() / 3);

        Assert.Equal(1, meshes.GetArrayLength());
        Assert.InRange(triangles, 1000, 12000);
    }

    [Fact]
    public void WildPigmentFlowersArePickedLikeWildCottonInMeadowsAndForests()
    {
        var cotton = WorldObject("wild_cotton");
        var flower = WorldObject("wild_pigment_flower");

        Assert.True(flower.Harvest, "Use picks it");
        Assert.Equal(cotton.Regrow, flower.Regrow);
        Assert.Equal([("pigment_flower", 1, (int?)null, (float?)null), ("pigment_flower_seed", 1, null, 0.4f)],
            flower.Drops!.Select(d => (d.Item, d.Count, d.CountMax, d.Chance)));
        Assert.Equal(cotton.Drops!.Single(d => d.Item == "cotton_seed").Chance, flower.Drops!.Single(d => d.Item == "pigment_flower_seed").Chance);
        var cottonRule = World.Value.Biomes!.SelectMany(b => b.Scatter ?? []).Single(r => r.Object == "wild_cotton");
        var biomes = World.Value.Biomes!.Where(b => b.Scatter?.Any(r => r.Object == "wild_pigment_flower") == true).Select(b => b.Id).ToList();
        Assert.Equal(["meadow", "forest"], biomes);
        Assert.All(World.Value.Biomes!.SelectMany(b => b.Scatter ?? []).Where(r => r.Object == "wild_pigment_flower"),
            r => Assert.Equal((cottonRule.Spacing, cottonRule.Chance), (r.Spacing, r.Chance)));
    }

    [Fact]
    public void PigmentFlowerSeedsAreRareLoot()
    {
        var tables = World.Value.LootTables!.Where(t => t.Value.Items!.Any(i => i.Item == "pigment_flower_seed")).ToList();

        Assert.NotEmpty(tables);
        foreach (var (_, table) in tables)
        {
            var seed = table.Items!.Single(i => i.Item == "pigment_flower_seed");
            Assert.True(seed.Weight <= table.Items!.Where(i => i.Item!.EndsWith("_seed", StringComparison.Ordinal)).Min(i => i.Weight),
                "no likelier than any other seed in its table");
        }
    }

    [Theory]
    [InlineData("beetroot", 30, "eat_crunch")]
    [InlineData("banana", 35, "eat_soft")]
    [InlineData("chili", -50, "eat_crunch")]
    [InlineData("blueberry", 40, "eat_soft")]
    [InlineData("orange", 45, "eat_soft")]
    [InlineData("broccoli", 50, "eat_crunch")]
    [InlineData("pineapple", 55, "eat_crunch")]
    public void TheNewProduceIsFoodWithTheFruitStats(string id, int health, string eat)
    {
        var food = Part(id);

        Assert.Equal((1.5, 1, 0.4, 0.4, true, 10),
            (Math.Round((double)food.Mass!.Value, 3), (int)food.Durability!.Value, Math.Round((double)food.Friction!.Value, 3),
                Math.Round((double)food.Buoyancy!.Value, 3), food.Flammable!.Value, food.StackSize!.Value));
        Assert.Equal(PartCategory.Consumable, food.Category);
        Assert.Equal("scripts/food.lua", food.Script);
        Assert.Equal((health, 1.4), (food.Properties!["health"]!.GetValue<int>(), food.Properties!["eat_seconds"]!.GetValue<double>()));
        Assert.Equal(eat, food.Sounds!["eat"]);
        Assert.Equal($"meshes/items/{id}_held.glb", food.Held?.Mesh);
        Assert.Equal($"meshes/crops/{id}_plant.glb", Part(id + "_seed").Seed!.Plant);
        foreach (var file in new[] { $"meshes/items/{id}.glb", $"meshes/items/{id}_held.glb", $"meshes/items/{id}_seed.glb",
                     $"meshes/crops/{id}_plant.glb", $"textures/items/{id}_icon.png", $"textures/items/{id}_seed_icon.png" })
            Assert.True(File.Exists(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", file)), $"{file} is built");
        Assert.Contains(id, Part("fridge").Inventory!.Accepts!);
    }

    [Fact]
    public void TheChiliHurtsHalfOfAFullHealthAndDoesNotKillFromFull()
    {
        // food.lua takes a negative health through holder.hurt; at full health one Chili leaves the eater standing.
        var hurt = -Part("chili").Properties!["health"]!.GetValue<float>();
        var max = Mechanica.Core.Survival.SurvivalSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "survival.json"))).MaxHealth;

        Assert.Equal(50f, hurt);
        Assert.True(hurt < max, $"one Chili does not kill from full health ({hurt} of {max})");
        Assert.Contains("holder.hurt", File.ReadAllText(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", "scripts", "food.lua")));
    }

    [Theory]
    [InlineData("beetroot_seed", "common")]
    [InlineData("beetroot_seed", "crate")]
    [InlineData("banana_seed", "epic")]
    [InlineData("chili_seed", "epic")]
    [InlineData("chili", "legendary")]
    public void TheNewSeedsAreSparingLootInTheirTiers(string item, string table)
    {
        var tables = World.Value.LootTables!;
        var entry = tables[table].Items!.Single(i => i.Item == item);

        Assert.True(entry.Weight <= 1.0, $"{item} is a sparing find in {table} ({entry.Weight})");
        // Rarer crops only in rarer tiers: Banana and Chili Seeds are never common loot.
        Assert.DoesNotContain(tables["common"].Items!, i => i.Item is "banana_seed" or "chili_seed");
        Assert.DoesNotContain(tables["crate"].Items!, i => i.Item is "banana_seed" or "chili_seed");
    }

    [Fact]
    public void BeetrootSeedsComeFromTheSeedScuttlerAndFarmCargo()
    {
        var bots = Mechanica.Core.Bots.BotBook.Load(Vanilla.Value);
        Assert.Contains(bots.FindBot("scuttler_seed")!.Drops, d => d.Item == "vanilla:beetroot_seed");
        Assert.Contains(WorldObject("cargo_spill_farming").Drops!, d => d.Item == "beetroot_seed");
    }

    [Fact]
    public void TheWildCottonIsOneLightMesh()
    {
        // As the wild Pigment Flower: the world draws only the first mesh of a prop's file (TerrainChunk), many times over.
        var bytes = File.ReadAllBytes(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", "meshes", "world", "props", "wild_cotton.glb"));
        using var doc = JsonDocument.Parse(bytes.AsMemory(20, BitConverter.ToInt32(bytes, 12)));
        var meshes = doc.RootElement.GetProperty("meshes");
        var accessors = doc.RootElement.GetProperty("accessors");
        var triangles = meshes.EnumerateArray().SelectMany(m => m.GetProperty("primitives").EnumerateArray())
            .Sum(p => accessors[p.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32() / 3);

        Assert.Equal(1, meshes.GetArrayLength());
        Assert.InRange(triangles, 1000, 12000);
    }

    [Theory]
    [InlineData("blueberry_seed", "epic")]
    [InlineData("blueberry_seed", "warehouse_crate")]
    [InlineData("orange_seed", "legendary")]
    [InlineData("orange_seed", "warehouse_crate")]
    [InlineData("broccoli_seed", "legendary")]
    [InlineData("pineapple_seed", "legendary")]
    [InlineData("pineapple_seed", "warehouse_crate")]
    public void TheLateSeedsAreRarerThanTheK3SeedsAndOnlyInTheRareTiers(string item, string table)
    {
        var tables = World.Value.LootTables!;
        var entry = tables[table].Items!.Single(i => i.Item == item);

        Assert.True(entry.Weight < 0.5, $"{item} is rarer in {table} than Banana and Chili Seeds ({entry.Weight})");
        Assert.Equal((1, (int?)null), (entry.Count, entry.CountMax));
        // Never in the common tiers: green pods, crates, lockers, ruin chests.
        foreach (var common in new[] { "common", "crate", "locker", "ruin_chest" })
            Assert.DoesNotContain(tables[common].Items!, i => i.Item == item);
        // The later the crop, the rarer its seed in gold pods.
        var gold = tables["legendary"].Items!;
        Assert.True(gold.Single(i => i.Item == "orange_seed").Weight >= gold.Single(i => i.Item == "broccoli_seed").Weight);
        Assert.True(gold.Single(i => i.Item == "broccoli_seed").Weight >= gold.Single(i => i.Item == "pineapple_seed").Weight);
    }

    [Fact]
    public void TheFabricatorMakesAGrazerShakeFromIceOrangesAndMilk()
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find("grazer_shake")!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(["vanilla:ice_block:100", "vanilla:orange:3", "vanilla:grazer_milk:1"], recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(("vanilla:grazer_shake", 1), (recipe.Outputs.Single().Item, recipe.Outputs.Single().Count));
        Assert.Equal((10f, 3210), (recipe.Time, recipe.Definition.Order));
        // 3210 is kept for it in the Food and cooking group (docs/part-data-schema.md, Recipe order): no other recipe takes it.
        Assert.Single(book.All, r => r.Definition.Order == 3210);
        // Still a loot find too.
        Assert.Contains(World.Value.LootTables!["legendary"].Items!, i => i.Item == "grazer_shake");
    }

    [Fact]
    public void CornIsALittleFoodWithTheWikisStatsAndNoSeed()
    {
        var corn = Part("corn");

        Assert.Equal((6.0, 2, 0.4, 0.3, true, 10),
            (Math.Round((double)corn.Mass!.Value, 3), (int)corn.Durability!.Value, Math.Round((double)corn.Friction!.Value, 3),
                Math.Round((double)corn.Buoyancy!.Value, 3), corn.Flammable!.Value, corn.StackSize!.Value));
        Assert.Equal(PartCategory.Consumable, corn.Category);
        Assert.Equal("scripts/food.lua", corn.Script);
        Assert.Equal((4, 1.4), (corn.Properties!["health"]!.GetValue<int>(), corn.Properties!["eat_seconds"]!.GetValue<double>()));
        Assert.Equal("eat_crunch", corn.Sounds!["eat"]);
        Assert.Equal("meshes/items/corn_held.glb", corn.Held?.Mesh);
        foreach (var file in new[] { "meshes/items/corn.glb", "meshes/items/corn_held.glb", "meshes/world/props/wild_corn.glb", "textures/items/corn_icon.png" })
            Assert.True(File.Exists(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", file)), $"{file} is built");
        // Not plantable (crops map): no seed, and nothing grows corn.
        Assert.Null(Vanilla.Value.FindDefinition("corn_seed"));
        Assert.DoesNotContain(Vanilla.Value.Parts.Values, p => p.Definition.Seed?.Crop == "corn");
        Assert.Contains("corn", Part("fridge").Inventory!.Accepts!);
    }

    [Fact]
    public void TheFabricatorMakesTwoPoppersFromFiveCornBesideTheTomatoRecipe()
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find("popper_corn")!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(["vanilla:corn:5", "vanilla:gasoline:1"], recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(("vanilla:popper", 2), (recipe.Outputs.Single().Item, recipe.Outputs.Single().Count));
        Assert.Equal((6f, 2541), (recipe.Time, recipe.Definition.Order));
        Assert.Single(book.All, r => r.Definition.Order == 2541);
        // The tomato one stays, just before it in the weapons group.
        var tomato = book.Find("popper")!;
        Assert.Equal(["vanilla:tomato:2", "vanilla:gasoline:1"], tomato.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(2540, tomato.Definition.Order);
    }

    [Fact]
    public void TheWildCornIsOneLightMesh()
    {
        // As the other wild plants: the world draws only the first mesh of a prop's file (TerrainChunk), many times over.
        var bytes = File.ReadAllBytes(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", "meshes", "world", "props", "wild_corn.glb"));
        using var doc = JsonDocument.Parse(bytes.AsMemory(20, BitConverter.ToInt32(bytes, 12)));
        var meshes = doc.RootElement.GetProperty("meshes");
        var accessors = doc.RootElement.GetProperty("accessors");
        var triangles = meshes.EnumerateArray().SelectMany(m => m.GetProperty("primitives").EnumerateArray())
            .Sum(p => accessors[p.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32() / 3);

        Assert.Equal(1, meshes.GetArrayLength());
        Assert.InRange(triangles, 1000, 12000);
    }

    [Fact]
    public void WildCornBreaksUnderTheSledgeOrACreationForThreeToFourCornAndRegrows()
    {
        var corn = WorldObject("wild_corn");
        var building = BuildSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "building.json")));
        var blow = corn.SledgeDamage ?? building.Sledge.ObjectDamage;

        Assert.False(corn.Harvest, "broken, not picked");
        Assert.Equal(2, (int)MathF.Ceiling(corn.Health!.Value / blow)); // two Sledgehammer blows
        foreach (var kind in new[] { DamageKinds.Sledge, DamageKinds.Saw, DamageKinds.Drill, DamageKinds.Explosion, DamageKinds.Weapon })
            Assert.True(corn.TakesDamageFrom(kind), $"{kind} breaks it");
        Assert.True(corn.Solid, "it stands in the way, so a creation's saw finds it");
        Assert.Equal([("corn", 3, (int?)4, (float?)null)], corn.Drops!.Select(d => (d.Item, d.Count, d.CountMax, d.Chance)));
        Assert.Equal(WorldObject("wild_cotton").Regrow, corn.Regrow);
        Assert.Equal("meshes/world/props/wild_corn.glb", corn.Mesh!.File);
        // Scattered in fields (more) and meadows (fewer), and self-sown at the edges of corn fields.
        var rules = World.Value.Biomes!.SelectMany(b => (b.Scatter ?? []).Where(r => r.Object == "wild_corn").Select(r => (b.Id, r.Spacing, r.Chance))).ToList();
        Assert.Equal(["field", "meadow"], rules.Select(r => r.Id).Order());
        var field = rules.Single(r => r.Id == "field");
        var meadow = rules.Single(r => r.Id == "meadow");
        Assert.True(field.Chance / (field.Spacing * field.Spacing) > meadow.Chance / (meadow.Spacing * meadow.Spacing), "thicker in fields than meadows");
        var scene = WorldObject("scene_corn_field").Scene!;
        Assert.All(scene.Layouts!.Where(l => l.Pieces!.Any(p => p.Object == "field_corn")),
            l => Assert.Contains(l.Pieces!, p => p.Object == "wild_corn"));
    }

    [Fact]
    public void GrazersEatCornFromTheirTroughAsWellAsCarrots()
    {
        var bots = Mechanica.Core.Bots.BotBook.Load(Vanilla.Value);
        foreach (var id in new[] { "grazer", "grazer_calf" })
            Assert.Equal(["vanilla:carrot", "vanilla:corn"], bots.FindBot(id)!.Tame!.Foods.Order());
        var trough = Part("feeding_trough");
        Assert.Equal(["carrot", "corn"], trough.Inventory!.Accepts!);
        Assert.Equal(["carrot", "corn"], trough.Properties!["foods"]!.AsArray().Select(f => f!.GetValue<string>()));
        Assert.Contains("corn", trough.Description);
        // Grazer Milk follows: five meals of either.
        Assert.Equal(5, bots.FindBot("grazer")!.Tame!.Produce!.Feeds);
    }
}
