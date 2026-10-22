using System.Text.Json.Nodes;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Tests.Survival;

/// <summary>How a food is eaten (consumables batch C2): <c>properties.drink</c> and <c>properties.eat_seconds</c>.</summary>
public sealed class FoodUseTests
{
    private static PartDefinition Food(JsonObject? properties) =>
        new() { Id = "snack", Name = "Snack", Category = PartCategory.Consumable, Properties = properties };

    [Fact]
    public void FoodWithNeitherKeyIsEatenForTheClipsLength()
    {
        Assert.Equal(FoodUse.Default, FoodUse.Of(null));
        Assert.Equal(FoodUse.Default, FoodUse.Of(Food(null)));
        Assert.Equal(FoodUse.Default, FoodUse.Of(Food(new JsonObject { ["health"] = 20 })));
        Assert.Equal(0.86f, FoodUse.Default.SecondsOr(0.86f));
    }

    [Fact]
    public void ADrinkAndItsSecondsAreRead()
    {
        var use = FoodUse.Of(Food(new JsonObject { ["drink"] = true, ["eat_seconds"] = 0.67 }));
        Assert.Equal(new FoodUse(true, 0.67f), use);
        Assert.Equal(0.67f, use.SecondsOr(2f));
        Assert.Equal(new FoodUse(false, 1.4f), FoodUse.Of(Food(new JsonObject { ["eat_seconds"] = 1.4 })));
    }

    [Theory]
    [InlineData("\"yes\"", "0")]
    [InlineData("1", "-1")]
    [InlineData("false", "11")]
    [InlineData("null", "\"2\"")]
    public void OtherValuesCountAsUnset(string drink, string seconds)
    {
        var properties = JsonNode.Parse($$"""{ "drink": {{drink}}, "eat_seconds": {{seconds}} }""")!.AsObject();
        Assert.Equal(FoodUse.Default, FoodUse.Of(Food(properties)));
    }
}
