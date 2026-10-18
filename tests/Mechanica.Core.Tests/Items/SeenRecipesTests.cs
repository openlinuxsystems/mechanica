using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Items;

public class SeenRecipesTests
{
    [Fact]
    public void TheFirstOpeningTagsNothing()
    {
        var seen = new SeenRecipes();
        Assert.Empty(seen.Open("vanilla:fabricator", ["vanilla:chest", "vanilla:wheel"]));
        Assert.Empty(seen.Open("vanilla:fabricator", ["vanilla:chest", "vanilla:wheel"]));
    }

    [Fact]
    public void RecipesAddedSinceAreNewOnce()
    {
        var seen = new SeenRecipes();
        seen.Open("vanilla:fabricator", ["vanilla:chest"]);
        Assert.Equal(["vanilla:wheel"], seen.Open("vanilla:fabricator", ["vanilla:chest", "vanilla:wheel"]));
        Assert.Empty(seen.Open("vanilla:fabricator", ["vanilla:chest", "vanilla:wheel"]));
    }

    [Fact]
    public void EachKindOfStationKeepsItsOwn()
    {
        var seen = new SeenRecipes();
        seen.Open("vanilla:fabricator", ["vanilla:chest"]);
        Assert.Empty(seen.Open("vanilla:refiner", ["vanilla:scrap_metal"]));
        Assert.Equal(["vanilla:scrap_metal"], seen.Open("vanilla:fabricator", ["vanilla:chest", "vanilla:scrap_metal"]));
    }

    [Fact]
    public void JsonRoundTrips()
    {
        var seen = new SeenRecipes();
        seen.Open("vanilla:fabricator", ["vanilla:wheel", "vanilla:chest"]);
        var again = SeenRecipes.Parse(seen.ToJson());
        Assert.Equal(["vanilla:chest", "vanilla:wheel"], again.Stations["vanilla:fabricator"]);
        Assert.Equal(seen.ToJson(), again.ToJson());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    public void BadDataStartsEmpty(string? json) => Assert.Empty(SeenRecipes.Parse(json).Stations);
}
