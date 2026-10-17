using System.Numerics;
using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class CompassTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(-MathF.PI / 2, 90f)] // turned right faces east (+x)
    [InlineData(MathF.PI / 2, 270f)]
    [InlineData(MathF.PI, 180f)]
    public void HeadingFollowsTheYaw(float yaw, float heading) => Assert.Equal(heading, Compass.Heading(yaw), 3);

    [Fact]
    public void BearingsAreClockwiseFromNorth()
    {
        Assert.Equal(0f, Compass.Bearing(Vector3.Zero, new Vector3(0, 0, -10)), 3);
        Assert.Equal(90f, Compass.Bearing(Vector3.Zero, new Vector3(10, 5, 0)), 3);
        Assert.Equal(225f, Compass.Bearing(Vector3.Zero, new Vector3(-10, 0, 10)), 3);
    }

    [Fact]
    public void AWaypointAheadIsCentredAndOneToTheRightIsRightOfCentre()
    {
        var ahead = Compass.Mark(Vector3.Zero, 0f, new Vector3(0, 0, -300));
        Assert.Equal(new CompassMark(0f, false, 300f), ahead);
        var right = Compass.Mark(Vector3.Zero, 0f, new Vector3(100, 0, -100));
        Assert.Equal(0.5f, right.X, 3);
        Assert.False(right.Behind);
    }

    [Fact]
    public void AWaypointBehindIsPinnedToTheNearerEdge()
    {
        var behindLeft = Compass.Mark(Vector3.Zero, 0f, new Vector3(-1, 0, 100));
        Assert.Equal((-1f, true), (behindLeft.X, behindLeft.Behind));
        var behindRight = Compass.Mark(Vector3.Zero, 0f, new Vector3(1, 0, 100));
        Assert.Equal((1f, true), (behindRight.X, behindRight.Behind));
    }

    [Fact]
    public void TheBarWrapsThroughNorth()
    {
        Assert.Equal(20f, Compass.Offset(350f, 10f), 3);
        Assert.Equal(-20f, Compass.Offset(10f, 350f), 3);
        Assert.Null(Compass.X(0f, 180f));
        Assert.Equal(-1f, Compass.X(90f, 0f)!.Value, 3);
    }

    [Fact]
    public void DistanceShowsWithin500Metres()
    {
        Assert.Equal("420 m", Compass.DistanceLabel(420.4f));
        Assert.Null(Compass.DistanceLabel(501f));
    }
}
