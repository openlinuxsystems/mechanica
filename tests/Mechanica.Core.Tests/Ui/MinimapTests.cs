using System.Numerics;
using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class MinimapTests
{
    [Fact]
    public void APinInViewSitsByItsOffsetNorthUp()
    {
        // 2 m a pixel on a 200 pixel minimap: 100 m east and 40 m north of the player.
        var (at, edge) = Minimap.Pin(new Vector2(1000, 500), new Vector2(1100, 460), 2f, 200f, 8f);
        Assert.False(edge);
        Assert.Equal(new Vector2(150, 80), at);
    }

    [Fact]
    public void APinPastTheEdgeStaysOnTheRimAlongItsBearing()
    {
        // 1 km due west: held at the left rim, 8 pixels in, level with the player.
        var (at, edge) = Minimap.Pin(Vector2.Zero, new Vector2(-1000, 0), 2f, 200f, 8f);
        Assert.True(edge);
        Assert.Equal(new Vector2(8, 100), at);
        // North-east, further north than east: on the top rim, keeping the direction.
        (at, edge) = Minimap.Pin(Vector2.Zero, new Vector2(500, -1000), 2f, 200f, 8f);
        Assert.True(edge);
        Assert.Equal(8f, at.Y, 3);
        Assert.Equal(100f + 46f, at.X, 3);
    }

    [Fact]
    public void ThePictureCoversTwiceTheViewAndIsRedrawnAfterAnEighth()
    {
        var (min, metres) = Minimap.Cover(new Vector2(301, -99), 2f, 200f);
        Assert.Equal(800f, metres);
        Assert.Equal(new Vector2(-100, -500), min);
        Assert.False(Minimap.NeedsRedraw(min, metres, new Vector2(301 + 90, -99)));
        Assert.True(Minimap.NeedsRedraw(min, metres, new Vector2(301, -99 - 110)));
    }
}
