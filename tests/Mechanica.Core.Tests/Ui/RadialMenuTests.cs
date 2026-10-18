using System.Numerics;
using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class RadialMenuTests
{
    [Theory]
    [InlineData(0, -100, 0)]
    [InlineData(100, 0, 1)]
    [InlineData(0, 100, 2)]
    [InlineData(-100, 0, 3)]
    [InlineData(70, -100, 0)]
    [InlineData(-70, -100, 0)]
    [InlineData(-100, -80, 3)]
    public void TheMousePicksTheItemInItsDirection(float x, float y, int item) =>
        Assert.Equal(item, RadialMenu.Pick(new Vector2(x, y), 4, 20));

    [Theory]
    [InlineData(0, -45, 0)]       // straight up, innermost ring
    [InlineData(0, -75, 1)]
    [InlineData(0, -130, 3)]      // beyond the last ring stays on it
    [InlineData(100, 0, 3 * 4 + 3)]
    [InlineData(0, 55, 6 * 4 + 0)]
    public void TheMousePicksASpokeByDirectionAndARingByDistance(float x, float y, int cell) =>
        Assert.Equal(cell, RadialMenu.PickCell(new Vector2(x, y), 12, 4, 40, 20));

    [Fact]
    public void InsideTheRingsPicksNoCell() => Assert.Equal(-1, RadialMenu.PickCell(new Vector2(10, -10), 10, 4, 40, 20));

    [Fact]
    public void TheMiddleAndAnEmptyMenuPickNothing()
    {
        Assert.Equal(-1, RadialMenu.Pick(new Vector2(5, 5), 4, 20));
        Assert.Equal(-1, RadialMenu.Pick(new Vector2(100, 0), 0, 20));
    }

    [Fact]
    public void ItemsGoClockwiseFromTheTop()
    {
        Assert.True(Vector2.Distance(new Vector2(0, -50), RadialMenu.Position(0, 4, 50)) < 1e-4f);
        Assert.True(Vector2.Distance(new Vector2(50, 0), RadialMenu.Position(1, 4, 50)) < 1e-4f);
        for (var i = 0; i < 16; i++)
            Assert.Equal(i, RadialMenu.Pick(RadialMenu.Position(i, 16, 80), 16, 20));
    }
}
