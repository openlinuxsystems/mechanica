using Mechanica.Core.Building;

namespace Mechanica.Core.Tests.Building;

public class WeldMotionTests
{
    [Fact]
    public void TheArmsCloseHoldAndOpenWithinTheClip()
    {
        Assert.Equal(0f, WeldMotion.Closed(0f));
        Assert.InRange(WeldMotion.Closed(0.11f), 0.1f, 0.9f);
        Assert.Equal(1f, WeldMotion.Closed(0.2f));
        Assert.Equal(1f, WeldMotion.Closed(0.4f));
        Assert.InRange(WeldMotion.Closed(0.5f), 0.05f, 0.95f);
        Assert.Equal(0f, WeldMotion.Closed(WeldMotion.Seconds));
        // Never past shut or open, and smooth: no jumps between frames.
        var last = 0f;
        for (var t = 0f; t <= WeldMotion.Seconds; t += 1f / 120f)
        {
            var c = WeldMotion.Closed(t);
            Assert.InRange(c, 0f, 1f);
            Assert.True(Math.Abs(c - last) < 0.2f, $"jump at {t}");
            last = c;
        }
    }

    [Fact]
    public void TheRingGlowsBrighterAsTheArmsShut()
    {
        Assert.Equal(WeldMotion.IdleGlow, WeldMotion.Glow(0f));
        Assert.True(WeldMotion.Glow(1f) > WeldMotion.Glow(0.5f));
    }
}
