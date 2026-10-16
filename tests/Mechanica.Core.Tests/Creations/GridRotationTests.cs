using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Creations;

public class GridRotationTests
{
    [Fact]
    public void ThereAre24DistinctRightHandedRotations()
    {
        Assert.Equal(24, GridRotation.All.Distinct().Count());
        foreach (var r in GridRotation.All)
        {
            var (x, y, z) = (r.XAxis, r.YAxis, r.ZAxis);
            Assert.Equal(new GridCell(x.Y * y.Z - x.Z * y.Y, x.Z * y.X - x.X * y.Z, x.X * y.Y - x.Y * y.X), z);
        }
    }

    [Fact]
    public void DefaultIsIdentity()
    {
        Assert.Equal(GridRotation.Identity, GridRotation.All[0]);
        Assert.Equal("+x+y", default(GridRotation).ToString());
        Assert.Equal(new GridCell(1, 2, 3), GridRotation.Identity.Apply(new GridCell(1, 2, 3)));
    }

    [Fact]
    public void TextFormRoundTrips()
    {
        foreach (var r in GridRotation.All)
        {
            Assert.True(GridRotation.TryParse(r.ToString(), out var parsed));
            Assert.Equal(r, parsed);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("+x+x")]
    [InlineData("+x-x")]
    [InlineData("+y")]
    [InlineData("x+y+")]
    [InlineData("+X+Y")]
    public void RejectsBadText(string? text) => Assert.False(GridRotation.TryParse(text, out _));

    [Fact]
    public void InverseUndoesAndCombineComposes()
    {
        var v = new GridCell(1, 2, 3);
        foreach (var a in GridRotation.All)
        {
            Assert.Equal(v, a.Inverse().Apply(a.Apply(v)));
            foreach (var b in GridRotation.All)
                Assert.Equal(b.Apply(a.Apply(v)), GridRotation.Combine(a, b).Apply(v));
        }
    }

    [Fact]
    public void QuarterTurnAboutY()
    {
        var r = GridRotation.FromAxes(new GridCell(0, 0, -1), new GridCell(0, 1, 0));
        Assert.Equal("-z+y", r.ToString());
        Assert.Equal(new GridCell(1, 0, 0), r.ZAxis);
        Assert.Equal(new Vec3(0, 0, -2), r.Apply(new Vec3(2, 0, 0)));
    }
}
