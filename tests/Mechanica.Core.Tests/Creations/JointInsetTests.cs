using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Creations;

// A hinge's inset (JointDefinition.Inset): what goes on a thin bearing's rotor sits flush on its disc, off the grid.
public class JointInsetTests
{
    private const float Inset = 0.17f;
    private static readonly Pose Lifted = new(new Vector3(1, 2.5f, -3), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f));

    // A block, a disc bearing on top (rotor up) and a block on the rotor.
    private static Creation Stack(out int bearing, out int wheel)
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        creation.SetAllBodyPoses(Lifted);
        bearing = creation.Place(DiscBearing, new GridCell(0, 1, 0)).Id;
        wheel = creation.Place(Block, new GridCell(0, 2, 0)).Id;
        return creation;
    }

    private static Vector3 Offset(Creation creation, int part) =>
        Lifted.Inverse().Compose(creation.BodyPose(creation.BodyIndexOf(part))).Position;

    [Fact]
    public void APartPlacedOnTheRotorSitsTheInsetBack()
    {
        var creation = Stack(out _, out var wheel);

        Assert.Equal(2, creation.Bodies.Count);
        AssertClose(new Vector3(0, -Inset, 0), Offset(creation, wheel));
        Assert.Equal(Lifted.Rotation, creation.BodyPose(creation.BodyIndexOf(wheel)).Rotation);
    }

    [Fact]
    public void ThePlainBearingKeepsItsSideOnTheGrid()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        creation.SetAllBodyPoses(Lifted);
        creation.Place(Bearing, new GridCell(0, 1, 0));
        var top = creation.Place(Block, new GridCell(0, 2, 0)).Id;

        AssertClose(Vector3.Zero, Offset(creation, top));
    }

    [Fact]
    public void PartsBuiltOnTheRotorSideJoinItsPose()
    {
        var creation = Stack(out _, out var wheel);
        var beside = creation.Place(Block, new GridCell(1, 2, 0)).Id;

        Assert.Equal(creation.BodyIndexOf(wheel), creation.BodyIndexOf(beside));
        AssertClose(new Vector3(0, -Inset, 0), Offset(creation, beside));
    }

    [Fact]
    public void ABearingOnTheRotorSideInsetsWhatGoesOnItToo()
    {
        var creation = Stack(out _, out _);
        creation.Place(DiscBearing, new GridCell(0, 3, 0));
        var top = creation.Place(Block, new GridCell(0, 4, 0)).Id;

        AssertClose(new Vector3(0, -2 * Inset, 0), Offset(creation, top));
    }

    [Fact]
    public void SpawningPutsEveryRotorSideOnItsDisc()
    {
        var creation = Stack(out _, out var wheel);
        var spawned = new Pose(new Vector3(5, 1, 5), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f));

        creation.SetAllBodyPoses(spawned);

        var relative = spawned.Inverse().Compose(creation.BodyPose(creation.BodyIndexOf(wheel)));
        AssertClose(new Vector3(0, -Inset, 0), relative.Position);
    }

    [Fact]
    public void AnInsetSideThatHasTurnedIsLeftWhereItIs()
    {
        var creation = Stack(out _, out var wheel);
        var body = creation.BodyIndexOf(wheel);
        // Turned a quarter about the hinge's axis (up through the rotor socket at x = z = 0), still flush on the disc.
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        var turned = Lifted.Compose(new Pose(new Vector3(0, -Inset, 0), turn));
        creation.SetBodyPose(body, turned);

        creation.SettleInsets();

        Assert.Equal(turned, creation.BodyPose(body));
    }

    [Fact]
    public void ASaveFromBeforeInsetsLoadsWithTheRotorSideOnTheDisc()
    {
        var creation = Stack(out _, out var wheel);
        // As an older build saved it: both sides in one grid frame.
        foreach (var body in creation.Bodies)
            creation.SetBodyPose(body.Index, Lifted);
        var diagnostics = new List<DataDiagnostic>();

        var loaded = CreationJson.Read(CreationJson.Write(creation), creation.Catalog, "old.json", diagnostics);

        Assert.NotNull(loaded);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        AssertClose(new Vector3(0, -Inset, 0), Offset(loaded, wheel));
    }

    [Fact]
    public void ASettledCreationRoundTripsUnchanged()
    {
        var creation = Stack(out _, out _);
        var json = CreationJson.Write(creation);

        var loaded = CreationJson.Read(json, creation.Catalog, "built.json", []);

        Assert.NotNull(loaded);
        Assert.Equal(json, CreationJson.Write(loaded));
    }

    [Fact]
    public void TheBuildGhostSitsOnTheDisc()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        var bearing = creation.Place(DiscBearing, new GridCell(0, 1, 0)).Id;
        var definition = creation.Catalog.FindDefinition(Block)!;

        AssertClose(new Vector3(0, -Inset, 0), creation.PlacementInset(definition, new GridCell(0, 2, 0), default, bearing));
        // Beside the bearing it goes on the block, on the grid.
        AssertClose(Vector3.Zero, creation.PlacementInset(definition, new GridCell(1, 0, 0), default, bearing));
    }

    private static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");
}
