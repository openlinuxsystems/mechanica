using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// Consumables batch C1: the produce, seeds, Chemical and Gasoline are the Part Editor batch
/// <c>mods/vanilla/editor/batches/consumables.json</c>, and the consumables we have take the wiki's stats (1.5 kg per weight
/// bar; durability = bars; friction and buoyancy = bars / 10), the Food page's heals and the wiki's recipes, with Ember for
/// Glow. Emptying a Chemical Bucket at the Fabricator gives the empty Water Bucket back as the recipe's second output.
/// Batch C2b adds the drinks (Bloom Tea, Grazer Shake: loot; the shake is also made at the Fabricator since crops batch K4) and Fertilizer, made from Ember and a full Water Bucket.
/// Batch C4 adds the Field Kitchen's burgers (heal 30, cooked there: the 3200 recipe group), and the Potato stops being
/// food (ammo and a cooking input).
/// </summary>
public class ConsumablesTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));
    private static readonly string Mod = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("carrot", 1.5, 1, 0.4, 0.4, 10)]
    [InlineData("tomato", 1.5, 1, 0.4, 0.4, 10)]
    [InlineData("potato", 3, 2, 0.4, 0.3, 50)]
    [InlineData("battery_pack", 3, 2, 0.4, 0.7, 20)]
    [InlineData("chemical", 3, 2, 0.4, 0.7, 20)]
    [InlineData("gasoline", 20, 2, 0.4, 0.7, 20)]
    [InlineData("glowstick", 3, 2, 0.4, 0.7, 20)]
    [InlineData("paint_ammo", 3, 2, 0.4, 0.7, 50)]
    [InlineData("fertilizer", 3, 2, 0.4, 0.7, 20)]
    [InlineData("bloom_tea", 3, 1, 0.4, 0.7, 10, false)]
    [InlineData("grazer_shake", 3, 1, 0.4, 0.7, 10, false)]
    [InlineData("grazer_milk", 3, 1, 0.4, 0.7, 5)]
    [InlineData("grazer_steak", 6, 2, 0.6, 0.3, 5)]
    [InlineData("garden_burger", 3, 1, 0.4, 0.7, 10)]
    [InlineData("colony_burger", 3, 1, 0.4, 0.7, 10)]
    public void TakesTheWikiStats(string id, double mass, int durability, double friction, double buoyancy, int stack, bool flammable = true)
    {
        var part = Part(id);

        Assert.Equal((mass, durability, friction, buoyancy, flammable, stack),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, part.StackSize!.Value));
        Assert.Equal(InventoryTab.Consumable, InventoryTabs.Of(part, false));
    }

    [Theory]
    [InlineData("carrot", 25)]
    [InlineData("tomato", 20)]
    [InlineData("bloom_tea", 20)]
    [InlineData("grazer_shake", 100)]
    [InlineData("grazer_milk", 20)]
    [InlineData("garden_burger", 30)]
    [InlineData("colony_burger", 30)]
    public void FoodHealsTheFoodPagesHealth(string id, int health)
    {
        var part = Part(id);

        Assert.Equal("scripts/food.lua", part.Script);
        Assert.Equal(health, part.Properties!["health"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("carrot", false, 1.4f)]
    [InlineData("tomato", false, 1.4f)]
    [InlineData("garden_burger", false, 1.4f)]
    [InlineData("colony_burger", false, 1.4f)]
    [InlineData("bloom_tea", true, 0.67f)]
    [InlineData("grazer_shake", true, 0.67f)]
    [InlineData("grazer_milk", true, 0.67f)]
    public void DrinksAreDrunkQuicklyAndFoodIsEatenInBites(string id, bool drink, float seconds)
    {
        var part = Part(id);

        Assert.Equal(new Mechanica.Core.Survival.FoodUse(drink, seconds), Mechanica.Core.Survival.FoodUse.Of(part));
        if (drink)
            Assert.Equal("drink_gulp", part.Sounds!["drink"]);
    }

    [Fact]
    public void ThePotatoIsNoLongerFoodButAmmoAndACookingInput()
    {
        var part = Part("potato");
        var book = RecipeBook.Load(Vanilla.Value);

        Assert.Null(part.Script);
        Assert.True(part.Properties?["health"] is null, "no heal");
        Assert.False(part.Sounds?.ContainsKey("eat") ?? false, "no eat sound");
        Assert.Null(part.Held!.Juice);
        Assert.Equal(PartCategory.Resource, part.Category);
        Assert.Equal(InventoryTab.Consumable, InventoryTabs.Of(part, false));
        // Still the potato guns' ammo, and cooked into both burgers.
        Assert.Equal("potato", Part("potato_blaster").Properties!["ammo"]!.GetValue<string>());
        Assert.All(new[] { "garden_burger", "colony_burger" },
            id => Assert.Contains(book.Find(id)!.Inputs, i => i.Item == "vanilla:potato" && i.Count == 10));
    }

    [Theory]
    [InlineData("garden_burger", 10, 3220, "carrot:4", "tomato:2", "potato:10")]
    [InlineData("colony_burger", 13, 3230, "grazer_steak:1", "tomato:5", "potato:10")]
    public void TheFieldKitchenCooksTheBurgers(string id, float time, int order, params string[] inputs)
    {
        var content = Vanilla.Value;
        var book = RecipeBook.Load(content);
        var recipe = book.Find(id)!;

        Assert.Equal(["vanilla:field_kitchen"], recipe.Stations);
        Assert.Equal(inputs.Select(i => $"vanilla:{i}"), recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(($"vanilla:{id}", 1), (recipe.Outputs.Single().Item, recipe.Outputs.Single().Count));
        Assert.Equal((time, order), (recipe.Time, recipe.Definition.Order));
        Assert.DoesNotContain(book.ForStation("fabricator"), r => r.QualifiedId == recipe.QualifiedId);

        // An order queued at a Field Kitchen (its inputs were taken when it was queued) cooks one burger into it.
        var kitchen = Inventory.For(content, content.FindDefinition("vanilla:field_kitchen")!)!;
        var queue = new CraftingQueue();
        queue.Enqueue(new CraftJob(recipe.QualifiedId, 1));
        Assert.Equal(1, queue.Tick(recipe.Time, 1, book, kitchen));
        Assert.Equal(1, kitchen.Count($"vanilla:{id}"));
    }

    [Fact]
    public void TheFieldKitchenIsAStationWithItsOwnRecipesTheFoodAndCookingGroup()
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var kitchen = Part("field_kitchen");

        Assert.Equal(PartCategory.Station, kitchen.Category);
        Assert.True(kitchen.Crafting is not null && kitchen.Inventory!.Slots >= 2, "crafts and holds what it makes");
        Assert.Equal(("field_kitchen_start", "field_kitchen_work", "field_kitchen_complete"),
            (kitchen.Sounds!["start"], kitchen.Sounds!["work"], kitchen.Sounds!["complete"]));
        // The standard two-way IO port on its right side, at the floor-level height every machine shares (docs/io-ports.md).
        var port = Assert.Single(kitchen.IoPorts!);
        Assert.Equal(Part("freezer").IoPorts!.Single().Position, port.Position);
        Assert.Equal(Part("freezer").IoPorts!.Single().Normal, port.Normal);
        // Its list is the 3200 group (Food and cooking), and every recipe in that group is cooked here.
        Assert.Equal(["vanilla:garden_burger", "vanilla:colony_burger"],
            RecipeBrowser.Ordered(book.ForStation("field_kitchen")).Select(r => r.QualifiedId));
        // (3210 is the Fabricator's Grazer Shake, crops batch K4.)
        Assert.All(book.All.Where(r => r.Definition.Order / 100 == 32 && r.Definition.Order != 3210), r => Assert.Equal(["vanilla:field_kitchen"], r.Stations));
        // It is made at the Fabricator, among the stations (1800).
        Assert.Equal(1850, book.Find("field_kitchen")!.Definition.Order);
    }

    [Fact]
    public void TheDrinksAreLoot()
    {
        // Bloom Tea is loot only; the Grazer Shake is loot and, since crops batch K4, made at the Fabricator (CropsTests).
        var book = RecipeBook.Load(Vanilla.Value);
        Assert.DoesNotContain(book.All, r => r.Outputs.Any(o => o.Item is "vanilla:bloom_tea"));
        Assert.Equal(["vanilla:fabricator"], book.All.Where(r => r.Outputs.Any(o => o.Item == "vanilla:grazer_shake")).SelectMany(r => r.Stations));
        var worldgen = File.ReadAllText(Path.Combine(Mod, "world", "worldgen.json"));
        Assert.Contains("\"bloom_tea\"", worldgen);
        Assert.Contains("\"grazer_shake\"", worldgen);
        Assert.Contains("\"fertilizer\"", worldgen);
    }

    [Fact]
    public void FertilizerBoostsGrowthFifteenTimesAndIsMadeFromEmberAndAFullBucket()
    {
        var content = Vanilla.Value;
        var part = Part("fertilizer");
        Assert.Equal(("scripts/fertilizer.lua", 15f), (part.Script, part.Properties!["boost"]!.GetValue<float>()));
        Assert.Equal("fertilizer_use", part.Sounds!["use"]);

        var book = RecipeBook.Load(content);
        var recipe = book.Find("fertilizer")!;
        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal([("vanilla:ember", 10), ("vanilla:water_bucket_full", 1)], recipe.Inputs.Select(i => (i.Item, i.Count)));
        Assert.Equal([("vanilla:fertilizer", 5), ("vanilla:water_bucket", 1)], recipe.Outputs.Select(o => (o.Item, o.Count)));
        Assert.Equal((10f, 1960), (recipe.Time, recipe.Definition.Order));

        var station = Inventory.For(content, content.FindDefinition("vanilla:fabricator")!)!;
        var queue = new CraftingQueue();
        queue.Enqueue(new CraftJob(recipe.QualifiedId, 1));
        Assert.Equal(1, queue.Tick(recipe.Time, 1, book, station));
        Assert.Equal((5, 1), (station.Count("vanilla:fertilizer"), station.Count("vanilla:water_bucket")));
    }

    [Theory]
    [InlineData("battery_pack", 20, 5, 940, "fabricator", "scrap_metal_block:10", "ember:5", "chemical:5")]
    [InlineData("glowstick", 20, 4, 1610, "fabricator", "chemical:4", "ember:4")]
    [InlineData("gasoline", 5, 5, 910, "fabricator,refiner,portable_fabricator", "crude_oil:5")]
    public void TakesTheWikiRecipe(string id, float time, int count, int order, string stations, params string[] inputs)
    {
        var recipe = RecipeBook.Load(Vanilla.Value).Find(id)!;

        Assert.Equal(stations.Split(',').Select(s => $"vanilla:{s}"), recipe.Stations);
        Assert.Equal(inputs.Select(i => $"vanilla:{i}"), recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(($"vanilla:{id}", count), (recipe.Outputs.Single().Item, recipe.Outputs.Single().Count));
        Assert.Equal(time, recipe.Time);
        Assert.Equal(order, recipe.Definition.Order);
    }

    [Fact]
    public void TheFabricatorEmptiesAChemicalBucketAndGivesTheBucketBack()
    {
        var content = Vanilla.Value;
        var book = RecipeBook.Load(content);
        var recipe = book.Find("chemical")!;
        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(("vanilla:chemical_bucket", 1), (recipe.Inputs.Single().Item, recipe.Inputs.Single().Count));
        Assert.Equal(5, recipe.Time);
        Assert.Equal(945, recipe.Definition.Order);
        // Five, the same as the bucket pours into a Chemical Container.
        Assert.Equal(5, Part("chemical_bucket").Properties!["count"]!.GetValue<int>());
        Assert.Equal("water_bucket", Part("chemical_bucket").Properties!["empty"]!.GetValue<string>());

        var station = Inventory.For(content, content.FindDefinition("vanilla:fabricator")!)!;
        var queue = new CraftingQueue();
        queue.Enqueue(new CraftJob(recipe.QualifiedId, 2));
        Assert.Equal(2, queue.Tick(recipe.Time * 2, 1, book, station));

        Assert.Equal(10, station.Count("vanilla:chemical"));
        Assert.Equal(2, station.Count("vanilla:water_bucket"));
    }

    [Fact]
    public void TheBatchHoldsTheProduceSeedsChemicalAndGasolineAtTheirPaths()
    {
        var batch = PartBatch.Read(File.ReadAllText(Path.Combine(Mod, "editor", "batches", "consumables.json")), "consumables.json", []);

        Assert.Equal(["parts/carrot.json", "parts/tomato.json", "parts/potato.json", "parts/carrot_seed.json", "parts/tomato_seed.json",
                "parts/potato_seed.json", "parts/cotton_seed.json", "parts/pigment_flower.json", "parts/pigment_flower_seed.json",
                "parts/beetroot.json", "parts/banana.json", "parts/chili.json", "parts/beetroot_seed.json", "parts/banana_seed.json",
                "parts/chili_seed.json", "parts/blueberry.json", "parts/orange.json", "parts/broccoli.json", "parts/pineapple.json",
                "parts/blueberry_seed.json", "parts/orange_seed.json", "parts/broccoli_seed.json", "parts/pineapple_seed.json", "parts/corn.json",
                "parts/chemical.json", "parts/gasoline.json",
                "parts/bloom_tea.json", "parts/grazer_shake.json", "parts/grazer_milk.json", "parts/grazer_steak.json", "parts/fertilizer.json",
                "parts/garden_burger.json", "parts/colony_burger.json"],
            batch!.Parts!.Select(e => e.File));
        // Moved, not copied: no other batch writes these files.
        foreach (var other in Directory.GetFiles(Path.Combine(Mod, "editor", "batches"), "*.json").Where(f => !f.EndsWith("consumables.json", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(other);
            foreach (var entry in batch.Parts!)
                Assert.DoesNotContain($"\"{entry.File}\"", text);
        }
    }
}
