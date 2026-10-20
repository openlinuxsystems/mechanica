using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Items;

public class ItemSearchTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("scrap", true)]
    [InlineData("METAL block", true)]
    [InlineData("vanilla:scrap", true)]
    [InlineData("wood", false)]
    [InlineData("scrap wood", false)]
    public void EveryWordMustBeInTheNameOrId(string query, bool matches) =>
        Assert.Equal(matches, ItemSearch.Matches(query, "Scrap Metal Block", "vanilla:scrap_metal_block"));

    [Fact]
    public void OnlyRealTextIsAnActiveSearch()
    {
        Assert.False(ItemSearch.Active(null));
        Assert.False(ItemSearch.Active(" "));
        Assert.True(ItemSearch.Active("a"));
    }
}
