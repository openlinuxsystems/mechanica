using Mechanica.Core;

namespace Mechanica.Core.Tests;

public class MechanicaInfoTests
{
    [Fact]
    public void GridSizeIsQuarterMetre() => Assert.Equal(0.25f, MechanicaInfo.GridSize);

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.25f, 1)]
    [InlineData(1f, 4)]
    [InlineData(-0.5f, -2)]
    [InlineData(0.26f, 1)]
    public void ToGridCellSnapsToNearestCell(float metres, int expected) =>
        Assert.Equal(expected, MechanicaInfo.ToGridCell(metres));

    [Fact]
    public void GridCellRoundTrips() =>
        Assert.Equal(7, MechanicaInfo.ToGridCell(MechanicaInfo.FromGridCell(7)));
}
