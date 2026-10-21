using System.Numerics;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Characters;

public class SeatRideTests
{
    private const float Dt = 1f / 60f;

    private static readonly Dictionary<string, float> Lengths = new()
    {
        ["idle"] = 3f, ["sit"] = 2f, ["sit_low"] = 2f, ["sit_ride"] = 2f, ["sit_ride_low"] = 2f,
        ["sit_saddle"] = 2f, ["sit_saddle_ride"] = 2f,
    };

    private static readonly SeatRideSettings Settings = new();

    private static CharacterAnimator Make() => new(new CharacterSettings(), Lengths);

    private static CharacterPose Sitting(SeatStyle seat, Vector3 velocity = default, float yaw = 0f) =>
        new(velocity, yaw, 0, OnFloor: true, Seated: true, Seat: seat);

    private static void Run(CharacterAnimator animator, Func<int, CharacterPose> pose, float seconds)
    {
        var frames = (int)MathF.Round(seconds / Dt);
        for (var i = 0; i < frames; i++)
            animator.Update(pose(i), Dt);
    }

    private static float Weight(CharacterAnimator animator, string clip) => animator.Body.Where(c => c.Clip == clip).Sum(c => c.Weight);

    [Fact]
    public void AChairHeightCushionPlaysTheChairPose()
    {
        var animator = Make();
        var cushion = Settings.ClipHips["sit"] - Settings.HipsAboveCushion;
        Run(animator, _ => Sitting(new SeatStyle(cushion, false, 0f)), 1f);
        Assert.Equal(1f, Weight(animator, "sit"), 3);
        Assert.Equal(0f, animator.Lift, 3);
    }

    [Fact]
    public void ALowSeatBlendsTowardTheLegsOutPoseSoTheHipsLandOnTheCushion()
    {
        var animator = Make();
        var (high, low) = (Settings.ClipHips["sit"], Settings.ClipHips["sit_low"]);
        var hips = (high + low) / 2f;
        Run(animator, _ => Sitting(new SeatStyle(hips - Settings.HipsAboveCushion, false, 0f)), 1f);
        Assert.Equal(0.5f, Weight(animator, "sit"), 3);
        Assert.Equal(0.5f, Weight(animator, "sit_low"), 3);
        Assert.Equal(0f, animator.Lift, 3);
    }

    [Fact]
    public void ASeatLowerThanTheLowPoseDropsTheBodyTheRest()
    {
        var animator = Make();
        var low = Settings.ClipHips["sit_low"];
        Run(animator, _ => Sitting(new SeatStyle(low - Settings.HipsAboveCushion - 0.1f, false, 0f)), 1f);
        Assert.Equal(1f, Weight(animator, "sit_low"), 3);
        Assert.Equal(-0.1f, animator.Lift, 3);
    }

    [Fact]
    public void APassengerRestsTheHandsOnTheThighs()
    {
        var animator = Make();
        Run(animator, _ => Sitting(new SeatStyle(float.NaN, true, 1f)), 1f);
        Assert.Equal("sit_ride", Assert.Single(animator.Body).Clip);
    }

    [Fact]
    public void ASaddleIsRiddenAstrideWithTheHandsOnTheBars()
    {
        var animator = Make();
        var cushion = Settings.ClipHips["sit_saddle"] - Settings.HipsAboveCushion;
        Run(animator, _ => Sitting(new SeatStyle(cushion, false, 1f, Saddle: true)), 1f);
        Assert.Equal("sit_saddle", Assert.Single(animator.Body).Clip);
        Assert.Equal(0f, animator.Lift, 3);
    }

    [Fact]
    public void ASaddlePassengerHoldsOnAndAHigherSaddleLiftsTheRider()
    {
        var animator = Make();
        var cushion = Settings.ClipHips["sit_saddle"] - Settings.HipsAboveCushion + 0.05f;
        Run(animator, _ => Sitting(new SeatStyle(cushion, true, 0f, Saddle: true)), 1f);
        Assert.Equal("sit_saddle_ride", Assert.Single(animator.Body).Clip);
        Assert.Equal(0.05f, animator.Lift, 3);
    }

    [Fact]
    public void ACharacterWithoutSaddleClipsSitsOnASaddleAsOnAChair()
    {
        var animator = new CharacterAnimator(new CharacterSettings(), Lengths.Where(p => !p.Key.StartsWith("sit_saddle", StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => p.Value));
        var cushion = Settings.ClipHips["sit"] - Settings.HipsAboveCushion;
        Run(animator, _ => Sitting(new SeatStyle(cushion, false, 0f, Saddle: true)), 1f);
        Assert.Equal(1f, Weight(animator, "sit"), 3);
    }

    [Theory]
    [InlineData(SeatPose.Saddle, true)]
    [InlineData(SeatPose.Chair, false)]
    [InlineData(null, false)]
    public void ASeatsPoseComesFromItsData(SeatPose? pose, bool saddle)
    {
        var part = new PartDefinition
        {
            Id = "t:seat", Name = "Seat", Size = new GridSize(3, 4, 4),
            Seat = new SeatDefinition { Offset = new Vec3(0, -0.45f, 0.12f), Cushion = new Vec3(0, 0.078f, 0.12f), Pose = pose },
        };
        var style = SeatStyle.For(part);
        Assert.Equal(saddle, style.Saddle);
        Assert.Equal(0.528f, style.Cushion, 3);
    }

    [Fact]
    public void SittingDownSinksIntoTheSeatAndSettles()
    {
        var animator = Make();
        var seat = new SeatStyle(float.NaN, false, 1f);
        var lowest = 0f;
        for (var i = 0; i < 30; i++)
        {
            animator.Update(Sitting(seat), Dt);
            lowest = MathF.Min(lowest, animator.SeatBob);
        }
        Assert.True(lowest < -0.01f, $"the body sinks in ({lowest})");
        Run(animator, _ => Sitting(seat), 2f);
        Assert.Equal(0f, animator.SeatBob, 3);
    }

    [Fact]
    public void ASmoothRideKeepsStillAndABumpyOneBounces()
    {
        var seat = new SeatStyle(float.NaN, false, 1f);
        var smooth = Make();
        Run(smooth, _ => Sitting(seat, new Vector3(0, 0, -10)), 2f);
        var smoothMost = 0f;
        for (var i = 0; i < 120; i++)
        {
            smooth.Update(Sitting(seat, new Vector3(0, 0, -10)), Dt);
            smoothMost = MathF.Max(smoothMost, MathF.Abs(smooth.SeatBob));
        }

        var bumpy = Make();
        Run(bumpy, _ => Sitting(seat, new Vector3(0, 0, -10)), 2f);
        var bumpyMost = 0f;
        for (var i = 0; i < 120; i++)
        {
            // Over a washboard: the seat's vertical speed swings up and down five times a second.
            var up = MathF.Sin(i * Dt * 2f * MathF.PI * 5f) * 0.6f;
            bumpy.Update(Sitting(seat, new Vector3(0, up, -10)), Dt);
            bumpyMost = MathF.Max(bumpyMost, MathF.Abs(bumpy.SeatBob));
        }
        Assert.True(smoothMost < 1e-4f, $"a smooth ride keeps still ({smoothMost})");
        Assert.True(bumpyMost > 0.005f, $"a bumpy ride bounces ({bumpyMost})");
        Assert.True(bumpyMost <= Settings.MaxBob + 1e-5f);
    }

    [Fact]
    public void ABouncierSeatThrowsTheBodyFurther()
    {
        float Peak(float bounce)
        {
            var animator = Make();
            var seat = new SeatStyle(float.NaN, false, bounce);
            Run(animator, _ => Sitting(seat), 2f);
            animator.Update(Sitting(seat, new Vector3(0, 0.5f, 0)), Dt);
            var most = 0f;
            for (var i = 0; i < 60; i++)
            {
                animator.Update(Sitting(seat, new Vector3(0, 0.5f, 0)), Dt);
                most = MathF.Max(most, MathF.Abs(animator.SeatBob));
            }
            return most;
        }
        Assert.Equal(0f, Peak(0f), 4);
        Assert.True(Peak(1.6f) > Peak(1f));
    }

    [Fact]
    public void BrakingTipsTheBodyForwardAndATurnLeansItOut()
    {
        var seat = new SeatStyle(float.NaN, false, 1f);
        var braking = Make();
        Run(braking, _ => Sitting(seat, new Vector3(0, 0, -10)), 2f);
        // Facing -Z at 10 m/s, it stops within half a second.
        Run(braking, i => Sitting(seat, new Vector3(0, 0, -MathF.Max(0f, 10f - i * Dt * 20f))), 0.2f);
        Assert.True(braking.SeatLean.X < -0.02f, $"braking tips it forward ({braking.SeatLean.X})");

        var turning = Make();
        Run(turning, _ => Sitting(seat, new Vector3(0, 0, -10)), 2f);
        // The velocity swings toward +X: a turn to the right, so the body leans left.
        Run(turning, i => Sitting(seat, new Vector3(MathF.Sin(i * Dt * 1.5f) * 10f, 0, -MathF.Cos(i * Dt * 1.5f) * 10f)), 0.3f);
        Assert.True(turning.SeatLean.Y > 0.005f, $"a right turn leans it left ({turning.SeatLean.Y})");
        Assert.True(turning.SeatLean.Length() <= Settings.MaxLeanDegrees * MathF.PI / 180f + 1e-5f);
    }

    [Fact]
    public void AJumpInPlaceIsNotABump()
    {
        var animator = Make();
        var seat = new SeatStyle(float.NaN, false, 1f);
        Run(animator, _ => Sitting(seat), 2f);
        animator.Update(Sitting(seat, new Vector3(0, 50, 0)), Dt);
        Assert.Equal(0f, animator.SeatBob, 4);
    }

    [Fact]
    public void StandingUpLetsGoOfTheRide()
    {
        var animator = Make();
        Run(animator, _ => Sitting(new SeatStyle(float.NaN, false, 1f)), 0.05f);
        animator.Update(new CharacterPose(Vector3.Zero, 0, 0, OnFloor: true), Dt);
        Assert.Equal(0f, animator.SeatBob);
        Assert.Equal(Vector2.Zero, animator.SeatLean);
    }

    [Fact]
    public void ASeatStyleComesFromItsPartData()
    {
        var driver = new PartDefinition
        {
            Size = new GridSize(2, 2, 2), Logic = new LogicDefinition { Outputs = 10 },
            Seat = new SeatDefinition { Offset = new Vec3(0, -0.25f, 0), Cushion = new Vec3(0, -0.03f, 0.03f) },
        };
        var style = SeatStyle.For(driver);
        Assert.Equal(0.22f, style.Cushion, 4);
        Assert.False(style.HandsFree);
        Assert.Equal(1f, style.Bounce);
        Assert.Equal(new Vec3(0, -0.25f, 0.03f), SeatStyle.Anchor(driver));

        var passenger = driver with { Logic = null, Seat = new SeatDefinition { Cushion = new Vec3(0, 0, 0), Bounce = 2f } };
        style = SeatStyle.For(passenger);
        Assert.True(style.HandsFree);
        Assert.Equal(0.25f, style.Cushion, 4);
        Assert.Equal(2f, style.Bounce);

        var old = driver with { Seat = new SeatDefinition { Offset = new Vec3(0, -0.1f, 0), Hands = SeatHands.Free } };
        Assert.True(float.IsNaN(SeatStyle.For(old).Cushion));
        Assert.True(SeatStyle.For(old).HandsFree);
        Assert.Equal(new Vec3(0, -0.1f, 0), SeatStyle.Anchor(old));
    }
}
