using System.Numerics;
using Mechanica.Core.Player;

namespace Mechanica.Core.Tests.Player;

/// <summary>Ladder climbing (Parts map E2): grabbing, climbing, the top, the bottom and jumping off.</summary>
public class LadderClimbTests
{
    private static readonly PlayerMovementSettings Settings = new();

    // A 0.5 m wide, 2 m tall, 0.25 m deep ladder standing on the ground at the origin, its faces toward +Z and -Z.
    private static readonly LadderFrame Ladder =
        LadderFrame.FromBox(new Vector3(0, 1, 0), new Vector3(0.25f, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 0.125f))!.Value;

    // Feet in front of the +Z face, touching it.
    private static readonly Vector3 Front = new(0, 0, 0.125f + 0.35f + 0.02f);

    // Forward at yaw 0 is -Z: toward the ladder from the front.
    private static PlayerInput Move(float x, float z, bool jump = false) => new(x, z, 0f, 0f, jump, false);

    [Fact]
    public void TheLongAxisIsClimbedAndTheThinnestIsTheDepth()
    {
        Assert.Equal(Vector3.UnitY, Ladder.Up);
        Assert.Equal(Vector3.UnitX, Ladder.Across);
        Assert.Equal(Vector3.UnitZ, Ladder.Normal);
        Assert.Equal((1f, 0.25f, 0.125f), (Ladder.HalfHeight, Ladder.HalfWidth, Ladder.HalfDepth));
        Assert.Equal(2f, Ladder.Top.Y, 4);
        // Built on its side (the long axis along X) it is no ladder; leaning 20 degrees it still is, upside down too.
        Assert.Null(LadderFrame.FromBox(Vector3.Zero, new Vector3(1, 0, 0), new Vector3(0, 0.25f, 0), new Vector3(0, 0, 0.125f)));
        var lean = MathF.PI / 9f;
        var leaning = LadderFrame.FromBox(Vector3.Zero, new Vector3(0.25f, 0, 0), new Vector3(0, -MathF.Cos(lean), MathF.Sin(lean)), new Vector3(0, 0.125f * MathF.Sin(lean), 0.125f * MathF.Cos(lean)));
        Assert.NotNull(leaning);
        Assert.True(leaning.Value.Up.Y > 0.9f);
    }

    [Fact]
    public void WalkingIntoAFaceGrabsItAndWalkingPastOrAwayDoesNot()
    {
        Assert.True(LadderClimb.CanGrab(Ladder, Front, new Vector3(0, 0, -1), Settings));
        // From behind, walking into the other face.
        Assert.True(LadderClimb.CanGrab(Ladder, Front with { Z = -Front.Z }, new Vector3(0, 0, 1), Settings));
        Assert.False(LadderClimb.CanGrab(Ladder, Front, new Vector3(0, 0, 1), Settings), "walking away");
        Assert.False(LadderClimb.CanGrab(Ladder, Front, new Vector3(1, 0, 0), Settings), "walking along it");
        Assert.False(LadderClimb.CanGrab(Ladder, Front, Vector3.Zero, Settings), "standing still");
        Assert.False(LadderClimb.CanGrab(Ladder, Front with { Z = 1.5f }, new Vector3(0, 0, -1), Settings), "too far");
        Assert.False(LadderClimb.CanGrab(Ladder, Front with { X = 1.2f }, new Vector3(0, 0, -1), Settings), "beside it");
        Assert.False(LadderClimb.CanGrab(Ladder, Front with { Y = 1.9f }, new Vector3(0, 0, -1), Settings), "at its top");
    }

    [Fact]
    public void ForwardClimbsUpAndBackClimbsDownWithoutGravity()
    {
        var up = LadderClimb.Step(Ladder, Front with { Y = 0.5f }, Move(0, 1), Settings, onFloor: false, floorBehindTop: false);
        Assert.Equal(ClimbResult.Climbing, up.Result);
        Assert.Equal(Settings.ClimbSpeed, up.Velocity.Y, 4);
        var down = LadderClimb.Step(Ladder, Front with { Y = 0.5f }, Move(0, -1), Settings, onFloor: false, floorBehindTop: false);
        Assert.Equal(-Settings.ClimbSpeed, down.Velocity.Y, 4);
        var still = LadderClimb.Step(Ladder, Front with { Y = 0.5f }, Move(0, 0), Settings, onFloor: false, floorBehindTop: false);
        Assert.Equal(ClimbResult.Climbing, still.Result);
        Assert.Equal(0f, still.Velocity.Y, 4);
    }

    [Fact]
    public void StrafeMovesAlongTheRungsAndStopsAtTheirEnd()
    {
        // Facing the +Z face (looking toward -Z), right is +X.
        var right = LadderClimb.Step(Ladder, Front with { Y = 0.5f }, Move(1, 0), Settings, false, false);
        Assert.Equal(Settings.ClimbSideSpeed, right.Velocity.X, 4);
        var atEnd = LadderClimb.Step(Ladder, Front with { X = 0.3f, Y = 0.5f }, Move(1, 0), Settings, false, false);
        Assert.Equal(0f, atEnd.Velocity.X, 4);
        // From the back face right is -X.
        var back = LadderClimb.Step(Ladder, new Vector3(0, 0.5f, -Front.Z), Move(1, 0), Settings, false, false);
        Assert.Equal(-Settings.ClimbSideSpeed, back.Velocity.X, 4);
    }

    [Fact]
    public void AClimberAwayFromTheFaceIsDrawnBackIn()
    {
        var step = LadderClimb.Step(Ladder, Front with { Y = 0.5f, Z = Front.Z + 0.2f }, Move(0, 0), Settings, false, false);
        Assert.True(step.Velocity.Z < 0f);
        Assert.Equal(ClimbResult.LetGo, LadderClimb.Step(Ladder, Front with { Y = 0.5f, Z = 2f }, Move(0, 1), Settings, false, false).Result);
    }

    [Fact]
    public void WithFloorBehindTheTopClimbingOnStepsOverOntoIt()
    {
        var below = LadderClimb.Step(Ladder, Front with { Y = 1.9f }, Move(0, 1), Settings, false, floorBehindTop: true);
        Assert.Equal(ClimbResult.Climbing, below.Result);
        Assert.True(below.Velocity.Y > 0f);
        var top = LadderClimb.Step(Ladder, Front with { Y = 2f }, Move(0, 1), Settings, false, floorBehindTop: true);
        Assert.Equal(ClimbResult.TopOut, top.Result);
        Assert.True(top.Velocity.Z < -1f, "over the top, toward the floor behind");
        Assert.True(top.Velocity.Y > 0f);
    }

    [Fact]
    public void WithNothingBehindTheTopTheClimberHangsFromIt()
    {
        var hang = new Vector3(Front.X, 2f - LadderClimb.TopHang, Front.Z);
        var step = LadderClimb.Step(Ladder, hang, Move(0, 1), Settings, false, floorBehindTop: false);
        Assert.Equal(ClimbResult.Climbing, step.Result);
        Assert.Equal(0f, step.Velocity.Y, 4);
    }

    [Fact]
    public void ClimbingDownOntoTheFloorLetsGoAndJumpPushesOff()
    {
        Assert.Equal(ClimbResult.LetGo, LadderClimb.Step(Ladder, Front, Move(0, -1), Settings, onFloor: true, floorBehindTop: false).Result);
        Assert.Equal(ClimbResult.Climbing, LadderClimb.Step(Ladder, Front, Move(0, 1), Settings, onFloor: true, floorBehindTop: false).Result);
        var jump = LadderClimb.Step(Ladder, Front with { Y = 1f }, Move(0, 1, jump: true), Settings, false, false);
        Assert.Equal(ClimbResult.JumpedOff, jump.Result);
        Assert.Equal(Settings.ClimbJumpSpeed, jump.Velocity.Z, 4);
        Assert.Equal(Settings.ClimbJumpUp, jump.Velocity.Y, 4);
        // Below the bottom the climber falls off.
        Assert.Equal(ClimbResult.LetGo, LadderClimb.Step(Ladder, Front with { Y = -1f }, Move(0, -1), Settings, false, false).Result);
    }

    [Fact]
    public void StandingOnTheFloorBehindTheTopAndWalkingToItMountsTheFrontFace()
    {
        // On a deck behind the ladder, at its top (feet at 2 m), walking toward +Z.
        var deck = new Vector3(0.1f, 2f, -Front.Z);
        var mount = LadderClimb.MountFromTop(Ladder, deck, new Vector3(0, 0, 1), Settings);
        Assert.NotNull(mount);
        Assert.Equal(Front.Z + LadderClimb.HoldGap - 0.02f, mount.Value.Z, 3);
        Assert.Equal(2f - LadderClimb.TopHang, mount.Value.Y, 3);
        Assert.Equal(0.1f, mount.Value.X, 3);
        Assert.Null(LadderClimb.MountFromTop(Ladder, deck, new Vector3(0, 0, -1), Settings));
        Assert.Null(LadderClimb.MountFromTop(Ladder, deck with { Y = 0f }, new Vector3(0, 0, 1), Settings));
    }

    [Fact]
    public void BadInputIsSafe()
    {
        var step = LadderClimb.Step(Ladder, Front with { Y = 0.5f }, new PlayerInput(float.NaN, float.PositiveInfinity, float.NaN, 0f, false, false), Settings, false, false);
        Assert.True(float.IsFinite(step.Velocity.X) && float.IsFinite(step.Velocity.Y) && float.IsFinite(step.Velocity.Z));
        Assert.False(LadderClimb.CanGrab(Ladder, Front, new Vector3(float.NaN, 0, 0), Settings));
    }
}
