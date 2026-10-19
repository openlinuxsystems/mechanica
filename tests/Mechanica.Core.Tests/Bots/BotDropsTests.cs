using Mechanica.Core.Bots;
using Mechanica.Core.Tests.Survival;

namespace Mechanica.Core.Tests.Bots;

public class BotDropsTests
{
    private static BotBook Book(params BotDropDefinition[] drops) => BotBook.From(new SurvivalFixture().Catalog, ("test", new BotDefinition
    {
        Id = "grunt", Name = "Grunt", Health = 10, Speed = 1, RaidCost = 1, Drops = [.. drops],
    }));

    [Fact]
    public void ADropMayGiveARangeOfCounts()
    {
        var book = Book(new BotDropDefinition { Item = SurvivalFixture.Scrap, Count = 2, CountMax = 5 }, new BotDropDefinition { Item = SurvivalFixture.Scrap });

        Assert.False(book.HasErrors, string.Join("; ", book.Diagnostics));
        Assert.Equal([new BotDrop(SurvivalFixture.Scrap, 1f, 2, 5), new BotDrop(SurvivalFixture.Scrap, 1f, 1, 1)], book.FindBot("test:grunt")!.Drops);
    }

    [Fact]
    public void CountMaxBelowCountIsAnError()
    {
        var error = Assert.Throws<FormatException>(() => Book(new BotDropDefinition { Item = SurvivalFixture.Scrap, Count = 3, CountMax = 2 }));

        Assert.Contains("$.drops[0].count_max", error.Message);
    }
}
