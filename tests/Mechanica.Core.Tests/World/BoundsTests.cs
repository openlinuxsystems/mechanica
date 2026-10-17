using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.World;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.World;

public class BoundsTests
{
    [Theory]
    [InlineData(0f, 0f, 0, 0)]
    [InlineData(63.9f, 0f, 0, 0)]
    [InlineData(64f, 0f, 1, 0)]
    [InlineData(-0.01f, -64f, -1, -1)]
    [InlineData(-64.01f, 130f, -2, 2)]
    public void ChunkAtFloorsBySize(float x, float z, int cx, int cz) =>
        Assert.Equal(new ChunkCoord(cx, cz), ChunkCoord.At(x, z, 64f));

    [Fact]
    public void BoxCoversEveryChunkItsFootprintOverlaps()
    {
        var box = new WorldBounds(new Vector3(-1, 50, 10), new Vector3(65, 80, 20));
        Assert.Equal([new(-1, 0), new(0, 0), new(1, 0)], box.Chunks(64f).ToList());
        Assert.Empty(WorldBounds.Empty.Chunks(64f));
    }

    [Fact]
    public void HorizontalDistanceIgnoresHeight()
    {
        var box = new WorldBounds(Vector3.Zero, new Vector3(2, 2, 2));
        Assert.Equal(0f, box.HorizontalDistanceTo(new Vector3(1, 100, 1)));
        Assert.Equal(5f, box.HorizontalDistanceTo(new Vector3(5, 0, 6)), 4);
    }

    [Fact]
    public void TransformedBoxHoldsTheRotatedCorners()
    {
        var box = new WorldBounds(new Vector3(-1, 0, 0), new Vector3(1, 0, 0));
        var turned = box.Transform(new Pose(new Vector3(10, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)));
        AssertClose(new Vector3(10, 0, -1), turned.Min);
        AssertClose(new Vector3(10, 0, 1), turned.Max);
    }

    [Fact]
    public void CreationBoundsCoverEveryBodyAtItsPose()
    {
        // A beam on a bearing: the rotor block is its own Body.
        var creation = new Creation(1, Catalog());
        creation.Place(Beam, GridCell.Zero);
        creation.Place(Bearing, new GridCell(0, 1, 0));
        creation.Place(Block, new GridCell(0, 2, 0));
        Assert.Equal(2, creation.Bodies.Count);

        var local = CreationBounds.LocalBodyBounds(creation);
        AssertClose(new Vector3(-0.125f, -0.125f, -0.125f), local[0].Min);
        AssertClose(new Vector3(0.375f, 0.375f, 0.125f), local[0].Max);

        var moved = new Pose(new Vector3(100, 0, 0), Quaternion.Identity);
        var world = CreationBounds.World(local, [Pose.Identity, moved]);
        AssertClose(new Vector3(-0.125f, -0.125f, -0.125f), world.Min);
        AssertClose(new Vector3(100.125f, 0.625f, 0.125f), world.Max);

        creation.SetAllBodyPoses(moved);
        AssertClose(new Vector3(99.875f, -0.125f, -0.125f), CreationBounds.World(creation).Min);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");
}
