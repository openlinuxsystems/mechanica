using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// Consumables batch C1: the produce, seeds, Chemical and Gasoline are the Part Editor batch
/// <c>mods/vanilla/editor/batches/consumables.json</c>, and the consumables we have take the wiki's stats (1.5 kg per weight
/// bar; durability = bars; friction and buoyancy = bars / 10), the Food page's heals and the wiki's recipes, with Ember for
/// Glow. Emptying a Chemical Bucket at the Fabricator gives the empty Water Bucket back as the recipe's second output.
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
    public void TakesTheWikiStats(string id, double mass, int durability, double friction, double buoyancy, int stack)
    {
        var part = Part(id);

        Assert.Equal((mass, durability, friction, buoyancy, true, stack),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, part.StackSize!.Value));
        Assert.Equal(InventoryTab.Consumable, InventoryTabs.Of(part, false));
    }

    [Theory]
    [InlineData("carrot", 25)]
    [InlineData("tomato", 20)]
    [InlineData("potato", 8)]
    public void FoodHealsTheFoodPagesHealth(string id, int health)
    {
        var part = Part(id);

        Assert.Equal("scripts/food.lua", part.Script);
        Assert.Equal(health, part.Properties!["health"]!.GetValue<int>());
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
                "parts/potato_seed.json", "parts/cotton_seed.json", "parts/chemical.json", "parts/gasoline.json"],
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
