using Mechanica.Core.Characters;
using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Characters;

public class HeldItemsTests
{
    private static KeyValuePair<string, PartDefinition> Item(string id, bool held, PartCategory category = PartCategory.Consumable) =>
        KeyValuePair.Create(id, new PartDefinition { Id = id.Split(':')[1], Category = category, Held = held ? new HeldDefinition() : null });

    [Fact]
    public void ItemsWithAHeldModelAreNumberedFromOneInIdOrder()
    {
        var held = new HeldItems([Item("mod:torch", true), Item("vanilla:carrot", false), Item("a:zap", true), Item("mod:block", true, PartCategory.Block)]);

        Assert.Equal(2, held.Count);
        Assert.Equal(1, held.IndexOf("a:zap"));
        Assert.Equal(2, held.IndexOf("mod:torch"));
        Assert.Equal(0, held.IndexOf("vanilla:carrot"));
        Assert.Equal(0, held.IndexOf("mod:block"));
        Assert.Equal(0, held.IndexOf(null));
        Assert.Equal("mod:torch", held.IdOf(2));
        Assert.Null(held.IdOf(0));
        Assert.Null(held.IdOf(3));
    }
}
