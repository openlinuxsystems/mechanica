using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Logic;

namespace Mechanica.Core.Tests.Logic;

/// <summary>Chevrons, lanes, line hover and drag snapping for the Connect Tool (UI spec §10, §12).</summary>
public class ConnectionLayoutTests
{
    [Fact]
    public void ChevronsSitEverySpacingCentredOnTheLine()
    {
        var chevrons = ConnectionLayout.Chevrons(Vector2.Zero, new Vector2(100, 0), 40);
        Assert.Equal([new Vector2(30, 0), new Vector2(70, 0)], chevrons);
        Assert.Equal([new Vector2(10, 0)], ConnectionLayout.Chevrons(Vector2.Zero, new Vector2(20, 0), 40));
        Assert.Empty(ConnectionLayout.Chevrons(Vector2.One, Vector2.One, 40));
    }

    [Fact]
    public void LinksBetweenTheSamePartsGetTheirOwnLanes()
    {
        var lanes = ConnectionLayout.Lanes([new LogicLink(1, 2), new LogicLink(3, 4), new LogicLink(2, 1)]);
        Assert.Equal([(0, 2), (0, 1), (1, 2)], lanes);
        var a = ConnectionLayout.LaneOffset(Vector2.Zero, new Vector2(10, 0), 0, 2, 4);
        var b = ConnectionLayout.LaneOffset(new Vector2(10, 0), Vector2.Zero, 1, 2, 4, fromLower: false);
        Assert.Equal(new Vector2(0, -2), a);
        Assert.Equal(new Vector2(0, 2), b);
        Assert.Equal(Vector2.Zero, ConnectionLayout.LaneOffset(Vector2.Zero, new Vector2(10, 0), 0, 1, 4));
    }

    [Fact]
    public void DistanceToALineIsToItsNearestPoint()
    {
        Assert.Equal(3f, ConnectionLayout.DistanceToSegment(new Vector2(5, 3), Vector2.Zero, new Vector2(10, 0)));
        Assert.Equal(5f, ConnectionLayout.DistanceToSegment(new Vector2(-3, 4), Vector2.Zero, new Vector2(10, 0)));
        Assert.Equal(1f, ConnectionLayout.DistanceToSegment(new Vector2(1, 0), Vector2.Zero, Vector2.Zero));
    }

    [Fact]
    public void ADragSnapsToTheNodeNearestTheAimWithinTheRadius()
    {
        Vector3[] nodes = [new(0.4f, 0, -3), new(0.1f, 0, -5), new(0, 0, 2), new(0, 0.9f, -1), new(0, 0, -20)];
        Assert.Equal(1, ConnectionLayout.Snap(Vector3.Zero, -Vector3.UnitZ, nodes, 0.5f, 7.5f));
        Assert.Equal(-1, ConnectionLayout.Snap(Vector3.Zero, Vector3.UnitY, nodes, 0.5f, 7.5f));
        // Equally near the aim: the nearer one along it.
        Assert.Equal(0, ConnectionLayout.Snap(Vector3.Zero, -Vector3.UnitZ, [new(0.2f, 0, -3), new(-0.2f, 0, -4)], 0.5f, 7.5f));
        Assert.Equal(-1, ConnectionLayout.Snap(Vector3.Zero, -Vector3.UnitZ, [new(0, 0, -20)], 0.5f, 7.5f));
    }

    [Fact]
    public void ThePreviewSagsUntilItSnaps()
    {
        var a = Vector2.Zero;
        var b = new Vector2(100, 0);
        Assert.Equal(new Vector2(50, 12), ConnectionLayout.Tension(a, b, snapped: false));
        Assert.Equal(new Vector2(50, 0), ConnectionLayout.Tension(a, b, snapped: true));
        Assert.Equal(a, ConnectionLayout.Curve(a, new Vector2(50, 12), b, 0));
        Assert.Equal(new Vector2(50, 6), ConnectionLayout.Curve(a, new Vector2(50, 12), b, 0.5f));
    }
}
