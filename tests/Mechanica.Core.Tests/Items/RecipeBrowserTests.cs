using Mechanica.Core.Items;
using static Mechanica.Core.Tests.Items.ItemParts;

namespace Mechanica.Core.Tests.Items;

public class RecipeBrowserTests
{
    private readonly Core.Data.PartCatalog _catalog = Catalog();

    [Fact]
    public void SearchMatchesEveryWordOfTheNameOrOutputsAnyCase()
    {
        var recipes = Recipes(_catalog).All;

        Assert.Equal(["test:blocks", "test:chest"], Ids(RecipeBrowser.Filter(recipes, "", InventoryTab.All, _catalog)));
        Assert.Equal(["test:chest"], Ids(RecipeBrowser.Filter(recipes, "CHE", InventoryTab.All, _catalog)));
        Assert.Equal(["test:blocks"], Ids(RecipeBrowser.Filter(recipes, "test:block", InventoryTab.All, _catalog)));
        Assert.Empty(RecipeBrowser.Filter(recipes, "chest block", InventoryTab.All, _catalog));
    }

    [Fact]
    public void ACategoryKeepsRecipesWhoseMainOutputIsUnderIt()
    {
        var recipes = Recipes(_catalog).All;

        Assert.Equal(["test:blocks"], Ids(RecipeBrowser.Filter(recipes, null, InventoryTab.Blocks, _catalog)));
        Assert.Equal(["test:chest"], Ids(RecipeBrowser.Filter(recipes, null, InventoryTab.Interactive, _catalog)));
        Assert.Empty(RecipeBrowser.Filter(recipes, null, InventoryTab.Consumables, _catalog));
    }

    [Fact]
    public void InputsSayWhatIsMissingForTheOrder()
    {
        var chest = Recipes(_catalog).Find("test:chest")!;

        var inputs = RecipeBrowser.Inputs(chest, 2, item => item == Scrap ? 3 : 9);

        Assert.Equal([new RecipeInput(Scrap, 4, 3), new RecipeInput(Wood, 2, 9)], inputs);
        Assert.True(inputs[0].Missing);
        Assert.False(inputs[1].Missing);
    }

    [Fact]
    public void RemainingTimeCoversTheRestOfTheOrderAtTheStationSpeed()
    {
        Assert.Equal(3.5f, RecipeBrowser.Remaining(4, 1, 2, 2)); // 7 s of work left at 2×
        Assert.Equal(5f, RecipeBrowser.Remaining([new CraftJobView("a", 2, 1, 4), new CraftJobView("b", 1, 0, 3)], 2));
        Assert.Equal(float.PositiveInfinity, RecipeBrowser.Remaining(4, 0, 1, 0));
    }

    private static string[] Ids(IEnumerable<Recipe> recipes) => [.. recipes.Select(r => r.QualifiedId)];
}
