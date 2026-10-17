using Mechanica.Core.Building;

namespace Mechanica.Core.Tests.Building;

public class LiftMotionTests
{
    private static readonly LiftSettings Settings = new() { RaiseSpeed = 1f, RaiseAcceleration = 4f };

    private static (int Steps, int Stops) Run(LiftMotion motion, float target, double dt = 1 / 60.0, int max = 1000)
    {
        var stops = 0;
        for (var i = 1; i <= max; i++)
        {
            if (motion.Advance(dt, target))
                stops++;
            if (!motion.Moving && motion.Height == target)
                return (i, stops);
        }
        return (max, stops);
    }

    [Fact]
    public void EasesOneLevelUpAndStopsExactly()
    {
        var motion = new LiftMotion(Settings, 0.5f);
        var heights = new List<float>();
        for (var i = 0; i < 30 && (i == 0 || motion.Moving); i++)
        {
            motion.Advance(1 / 60.0, 0.75f);
            heights.Add(motion.Height);
        }
        Assert.Equal(0.75f, motion.Height);
        Assert.False(motion.Moving);
        Assert.True(heights.Zip(heights.Skip(1)).All(p => p.Second >= p.First), "it never goes back down");
        // Accelerates from rest: the first step is slow.
        Assert.True(heights[0] - 0.5f < 0.002f);
    }

    [Fact]
    public void ReportsOneStopPerMove()
    {
        var motion = new LiftMotion(Settings, 0.5f);
        var (steps, stops) = Run(motion, 5.5f);
        Assert.Equal(1, stops);
        // 5 m at 1 m/s plus the ramps: a bit over 5 seconds.
        Assert.InRange(steps / 60.0, 5.0, 5.6);
        Assert.False(motion.Advance(1 / 60.0, 5.5f), "at rest on the target there is nothing to report");
    }

    [Fact]
    public void NeverExceedsTheTopSpeed()
    {
        var motion = new LiftMotion(Settings, 0.5f);
        for (var i = 0; i < 200; i++)
        {
            motion.Advance(1 / 60.0, 4f);
            Assert.True(MathF.Abs(motion.Velocity) <= Settings.RaiseSpeed + 1e-5f);
        }
    }

    [Fact]
    public void TurnsBackWithoutJumping()
    {
        var motion = new LiftMotion(Settings, 0.5f);
        for (var i = 0; i < 20; i++)
            motion.Advance(1 / 60.0, 2f);
        var before = motion.Height;
        motion.Advance(1 / 60.0, 0.5f);
        Assert.True(MathF.Abs(motion.Height - before) < 0.05f, "a new target slows it down rather than snapping");
        var (_, stops) = Run(motion, 0.5f);
        Assert.Equal(0.5f, motion.Height);
        Assert.Equal(1, stops);
    }

    [Fact]
    public void ResetPutsItAtRest()
    {
        var motion = new LiftMotion(Settings, 0.5f);
        motion.Advance(0.1, 3f);
        motion.Reset(1f);
        Assert.Equal(1f, motion.Height);
        Assert.False(motion.Moving);
    }
}
