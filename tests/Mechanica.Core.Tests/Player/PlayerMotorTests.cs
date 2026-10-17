using System.Numerics;
using Mechanica.Core.Player;

namespace Mechanica.Core.Tests.Player;

public class PlayerMotorTests
{
    private const float Dt = 1f / 60f;
    private static readonly PlayerMovementSettings Settings = new();

    [Fact]
    public void ForwardAtZeroYawIsNegativeZ() =>
        AssertNear(new Vector3(0, 0, -1), PlayerMotor.WishDirection(0, 1, 0));

    [Fact]
    public void RightAtZeroYawIsPositiveX() =>
        AssertNear(new Vector3(1, 0, 0), PlayerMotor.WishDirection(1, 0, 0));

    [Fact]
    public void QuarterTurnLeftFacesNegativeX() =>
        AssertNear(new Vector3(-1, 0, 0), PlayerMotor.WishDirection(0, 1, MathF.PI / 2));

    [Fact]
    public void DiagonalWishIsNormalised() =>
        Assert.Equal(1f, PlayerMotor.WishDirection(1, 1, 0.3f).Length(), 4);

    [Fact]
    public void WalkingReachesWalkSpeedAndNoMore()
    {
        var velocity = Simulate(new PlayerInput(0, 1, 0, 0, false, false), onFloor: true, seconds: 1f);
        AssertNear(new Vector3(0, 0, -Settings.WalkSpeed), velocity);
    }

    [Fact]
    public void SprintingReachesSprintSpeed()
    {
        var velocity = Simulate(new PlayerInput(0, 1, 0, 0, false, true), onFloor: true, seconds: 1f);
        Assert.Equal(Settings.SprintSpeed, velocity.Length(), 3);
    }

    [Fact]
    public void GroundAccelerationIsLimitedPerStep()
    {
        var velocity = PlayerMotor.Step(Vector3.Zero, true, new PlayerInput(1, 0, 0, 0, false, false), Settings, Dt);
        Assert.Equal(Settings.GroundAcceleration * Dt, velocity.X, 4);
    }

    [Fact]
    public void ReleasingInputStopsOnTheGround()
    {
        var velocity = Simulate(PlayerInput.None, onFloor: true, seconds: 1f, start: new Vector3(4, 0, 0));
        AssertNear(Vector3.Zero, velocity);
    }

    [Fact]
    public void AirControlIsWeakerThanGroundControl()
    {
        var input = new PlayerInput(1, 0, 0, 0, false, false);
        var ground = PlayerMotor.Step(Vector3.Zero, true, input, Settings, Dt);
        var air = PlayerMotor.Step(Vector3.Zero, false, input, Settings, Dt);
        Assert.True(air.X < ground.X);
    }

    [Fact]
    public void JumpOnlyWorksOnTheFloor()
    {
        var jump = new PlayerInput(0, 0, 0, 0, true, false);
        Assert.Equal(Settings.JumpSpeed, PlayerMotor.Step(Vector3.Zero, true, jump, Settings, Dt).Y);
        Assert.True(PlayerMotor.Step(Vector3.Zero, false, jump, Settings, Dt).Y < 0);
    }

    [Fact]
    public void GravityPullsDownUpToMaxFallSpeed()
    {
        var oneStep = PlayerMotor.Step(Vector3.Zero, false, PlayerInput.None, Settings, Dt);
        Assert.Equal(-Settings.Gravity * Dt, oneStep.Y, 4);

        var longFall = Simulate(PlayerInput.None, onFloor: false, seconds: 30f);
        Assert.Equal(-Settings.MaxFallSpeed, longFall.Y, 3);
    }

    [Fact]
    public void JumpHeightMatchesBallistics()
    {
        // v² / 2g with the default numbers is about 1.03 m: high enough to step onto a 1 m block.
        var velocity = PlayerMotor.Step(Vector3.Zero, true, new PlayerInput(0, 0, 0, 0, true, false), Settings, Dt);
        var height = 0f;
        var peak = 0f;
        while (velocity.Y > 0 || height > 0)
        {
            height += velocity.Y * Dt;
            peak = MathF.Max(peak, height);
            velocity = PlayerMotor.Step(velocity, false, PlayerInput.None, Settings, Dt);
        }

        var expected = Settings.JumpSpeed * Settings.JumpSpeed / (2 * Settings.Gravity);
        Assert.InRange(peak, expected - 0.05f, expected + 0.05f);
    }

    [Fact]
    public void LandingClearsUpwardVelocityOnlyWhenFalling()
    {
        Assert.Equal(-1f, PlayerMotor.Step(new Vector3(0, -1, 0), true, PlayerInput.None, Settings, Dt).Y);
        Assert.Equal(0f, PlayerMotor.Step(new Vector3(0, 3, 0), true, PlayerInput.None, Settings, Dt).Y);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void InvalidDeltaLeavesVelocityAlone(float delta)
    {
        var velocity = new Vector3(1, 2, 3);
        Assert.Equal(velocity, PlayerMotor.Step(velocity, false, new PlayerInput(1, 1, 0, 0, true, true), Settings, delta));
    }

    [Fact]
    public void OversizedOrNonFiniteInputCannotExceedSprintSpeed()
    {
        var cheat = new PlayerInput(1000, float.PositiveInfinity, float.NaN, 0, false, true);
        var velocity = Simulate(cheat, onFloor: true, seconds: 2f);
        Assert.True(velocity.Length() <= Settings.SprintSpeed + 1e-3f);
    }

    [Fact]
    public void MoveTowardStopsAtTarget()
    {
        AssertNear(new Vector3(1, 0, 0), PlayerMotor.MoveToward(Vector3.Zero, new Vector3(1, 0, 0), 5));
        AssertNear(new Vector3(0.5f, 0, 0), PlayerMotor.MoveToward(Vector3.Zero, new Vector3(1, 0, 0), 0.5f));
    }

    [Fact]
    public void CrawlingIsSlowerThanWalking()
    {
        var velocity = Simulate(new PlayerInput(0, 1, 0, 0, false, true, Crawl: true), onFloor: true, seconds: 1f);
        Assert.Equal(Settings.CrawlSpeed, velocity.Length(), 3);
    }

    [Fact]
    public void WadingSlowsWalking()
    {
        var velocity = Vector3.Zero;
        for (var t = 0f; t < 1f; t += Dt)
            velocity = PlayerMotor.Step(velocity, true, new PlayerInput(0, 1, 0, 0, false, false), Settings, Dt, waterDepth: 0.5f);
        Assert.Equal(Settings.WalkSpeed * Settings.WadeSpeedScale, velocity.Length(), 3);
    }

    [Fact]
    public void DeepWaterSwims()
    {
        Assert.False(PlayerMotor.IsSwimming(Settings.SwimDepth - 0.01f, Settings));
        Assert.True(PlayerMotor.IsSwimming(Settings.SwimDepth, Settings));
        Assert.False(PlayerMotor.IsSwimming(float.NaN, Settings));
        var velocity = Vector3.Zero;
        for (var t = 0f; t < 2f; t += Dt)
            velocity = PlayerMotor.Step(velocity, false, new PlayerInput(0, 1, 0, 0, false, false), Settings, Dt, waterDepth: Settings.FloatDepth);
        AssertNear(new Vector3(0, 0, -Settings.SwimSpeed), velocity, 1e-3f);
    }

    [Fact]
    public void BuoyancyFloatsASinkingSwimmerUpAndCrawlDives()
    {
        var deep = Settings.FloatDepth + 2f;
        var up = PlayerMotor.Step(new Vector3(0, -1, 0), false, PlayerInput.None, Settings, Dt, waterDepth: deep);
        Assert.True(up.Y > -1f);
        var velocity = Vector3.Zero;
        for (var t = 0f; t < 2f; t += Dt)
            velocity = PlayerMotor.Step(velocity, false, new PlayerInput(0, 0, 0, 0, false, false, Crawl: true), Settings, Dt, waterDepth: deep);
        Assert.Equal(-Settings.SwimVerticalSpeed, velocity.Y, 3);
    }

    [Fact]
    public void JumpAtTheSurfaceLeapsAndUnderwaterRises()
    {
        var jump = new PlayerInput(0, 0, 0, 0, true, false);
        Assert.True(PlayerMotor.Step(Vector3.Zero, false, jump, Settings, Dt, waterDepth: Settings.FloatDepth).Y >= Settings.JumpSpeed);
        var velocity = Vector3.Zero;
        for (var t = 0f; t < 2f; t += Dt)
            velocity = PlayerMotor.Step(velocity, false, jump, Settings, Dt, waterDepth: Settings.FloatDepth + 3f);
        Assert.Equal(Settings.SwimVerticalSpeed, velocity.Y, 3);
    }

    [Fact]
    public void ASwimmerHoldingJumpLeapsOutOntoAnEdgeAboveTheWater()
    {
        // A floating swimmer holds jump: the feet must clear swim_leap_height above the surface, so the pond's
        // edge can never be a wall a player is stuck under.
        var jump = new PlayerInput(0, 1, 0, 0, true, false);
        var (depth, velocity, peak) = (Settings.FloatDepth, Vector3.Zero, float.NegativeInfinity);
        for (var t = 0f; t < 3f; t += Dt)
        {
            velocity = PlayerMotor.Step(velocity, false, jump, Settings, Dt, waterDepth: depth);
            depth -= velocity.Y * Dt;
            peak = MathF.Max(peak, -depth);
        }
        Assert.InRange(peak, Settings.SwimLeapHeight - 0.05f, Settings.SwimLeapHeight + 0.15f);
    }

    [Theory]
    [InlineData(true, false, true, false, true)]   // holding Crawl crouches
    [InlineData(false, true, true, false, false)]  // letting go with room stands up
    [InlineData(false, true, false, false, true)]  // under a low ceiling it stays down
    [InlineData(false, false, false, false, false)] // standing stays standing
    [InlineData(true, true, false, true, false)]   // swimmers never crouch
    public void CrouchingStandsUpOnlyWithRoom(bool want, bool was, bool room, bool swimming, bool expected) =>
        Assert.Equal(expected, PlayerMotor.Crouched(want, was, room, swimming));

    [Fact]
    public void EyeHeightMovesToTheStanceAtStanceSpeed()
    {
        var eye = Settings.EyeHeight;
        eye = PlayerMotor.EyeHeight(eye, crouched: true, Settings, 0.05f);
        Assert.Equal(Settings.EyeHeight - Settings.StanceSpeed * 0.05f, eye, 4);
        for (var i = 0; i < 100; i++)
            eye = PlayerMotor.EyeHeight(eye, crouched: true, Settings, Dt);
        Assert.Equal(Settings.CrouchEyeHeight, eye);
        Assert.Equal(Settings.EyeHeight, PlayerMotor.EyeHeight(float.NaN, crouched: false, Settings, Dt));
    }

    private static Vector3 Simulate(PlayerInput input, bool onFloor, float seconds, Vector3 start = default)
    {
        var velocity = start;
        for (var t = 0f; t < seconds; t += Dt)
            velocity = PlayerMotor.Step(velocity, onFloor, input, Settings, Dt);
        return velocity;
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
}
