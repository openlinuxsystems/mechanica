using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Creations;

public class BodyPoseTests
{
    private static readonly Pose Lifted = new(new Vector3(1, 2.5f, -3), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f));

    [Fact]
    public void GridQuaternionMatchesGridRotation()
    {
        foreach (var rotation in GridRotation.All)
        {
            var q = Pose.ToQuaternion(rotation);
            foreach (var axis in GridCell.Directions)
                AssertClose(rotation.Apply(axis.AsVector).ToNumerics(), Vector3.Transform(axis.AsVector.ToNumerics(), q));
        }
    }

    [Fact]
    public void ComposeAppliesTheInnerPoseFirst()
    {
        var inner = Pose.FromGrid(GridRotation.FromAxes(new GridCell(0, 1, 0), new GridCell(-1, 0, 0)), new GridCell(4, 0, 0));
        var point = new Vector3(0.25f, 0.5f, 0.75f);

        AssertClose(Lifted.Apply(inner.Apply(point)), Lifted.Compose(inner).Apply(point));
        AssertClose(point, Lifted.Inverse().Apply(Lifted.Apply(point)));
    }

    [Fact]
    public void BodiesWithoutAStoredPoseSitAtTheOrigin()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        Assert.False(creation.HasBodyPose(0));
        Assert.Equal(Pose.Identity, creation.BodyPose(0));
    }

    [Fact]
    public void PlacedPartsJoinTheirBodysPose()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        creation.SetAllBodyPoses(Lifted);
        creation.Place(Block, new GridCell(1, 0, 0));
        creation.Place(Bearing, new GridCell(0, 1, 0));
        creation.Place(Block, new GridCell(0, 2, 0));

        Assert.Equal(2, creation.Bodies.Count);
        Assert.Equal(Lifted, creation.BodyPose(0));
        // The rotor-side block took the pose of the bearing it attached to.
        Assert.Equal(Lifted, creation.BodyPose(1));
    }

    [Fact]
    public void PosesSurviveRemovingTheLowestPart()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        creation.Place(Block, new GridCell(1, 0, 0));
        creation.SetBodyPose(0, Lifted);

        creation.RemovePart(1, Ids());

        Assert.Equal(Lifted, creation.BodyPose(0));
    }

    [Fact]
    public void SplitPiecesKeepTheirPoses()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        creation.Place(Block, new GridCell(1, 0, 0));
        creation.Place(Block, new GridCell(2, 0, 0));
        creation.SetBodyPose(0, Lifted);

        var split = Assert.Single(creation.RemovePart(2, Ids()).SplitOff);

        Assert.Equal(Lifted, creation.BodyPose(0));
        Assert.Equal(Lifted, split.BodyPose(0));
    }

    [Fact]
    public void JointedBodiesKeepSeparatePoses()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Bearing, GridCell.Zero);
        creation.Place(Block, new GridCell(0, 1, 0));
        creation.SetAllBodyPoses(Pose.Identity);
        var turned = Pose.Identity with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1f) };

        creation.SetBodyPose(1, turned);

        Assert.Equal(Pose.Identity, creation.BodyPose(0));
        Assert.Equal(turned, creation.BodyPose(1));
    }

    [Fact]
    public void MergedPartsStayWhereTheyWereInTheWorld()
    {
        var catalog = Catalog();
        var target = new Creation(1, catalog);
        target.Place(Block, GridCell.Zero);
        target.SetAllBodyPoses(Lifted);
        // A bearing with a free block on its rotor, about to be merged on top of the target.
        var other = new Creation(2, catalog);
        other.Place(Bearing, GridCell.Zero);
        other.Place(Block, new GridCell(0, 1, 0));
        var rotation = GridRotation.FromAxes(new GridCell(0, 0, 1), new GridCell(0, 1, 0));
        var offset = new GridCell(0, 1, 0);
        var rotorPose = new Pose(new Vector3(5, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f));
        other.SetBodyPose(other.BodyIndexOf(2), rotorPose);
        var rotorCentre = rotorPose.Apply(new GridCell(0, 1, 0).Metres.ToNumerics());

        Assert.True(target.TryMergeFrom(other, rotation, offset, weld: false, out var map, out var error), error);

        // The bearing joined the target's Body and takes its pose; the rotor block keeps its world position.
        Assert.Equal(Lifted, target.BodyPose(target.BodyIndexOf(map[1])));
        var moved = target.FindPart(map[2])!;
        AssertClose(rotorCentre, target.BodyPose(target.BodyIndexOf(map[2])).Apply(moved.Cell.Metres.ToNumerics()));
    }

    [Fact]
    public void PosesRoundTripThroughJson()
    {
        var creation = new Creation(4, Catalog());
        creation.Place(Bearing, GridCell.Zero);
        creation.Place(Block, new GridCell(0, 1, 0));
        creation.Place(Block, new GridCell(0, 2, 0));
        creation.SetBodyPose(0, Lifted);
        creation.SetBodyPose(1, new Pose(new Vector3(0, 0.1f, 0), Quaternion.Normalize(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f))));

        var json = CreationJson.Write(creation);
        var diagnostics = new List<DataDiagnostic>();
        var loaded = CreationJson.Read(json, creation.Catalog, "posed.json", diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(loaded);
        Assert.Contains("\"bodies\": [", json);
        Assert.Equal(json, CreationJson.Write(loaded));
        Assert.Equal(creation.BodyPose(0), loaded.BodyPose(0));
        Assert.Equal(creation.BodyPose(1), loaded.BodyPose(1));
        // Every member of the Body gets the pose, not just the one named in the file.
        var split = Assert.Single(loaded.RemovePart(2, Ids()).SplitOff);
        Assert.Equal(creation.BodyPose(1), split.BodyPose(0));
    }

    [Fact]
    public void UnposedCreationsWriteNoBodies()
    {
        var creation = new Creation(4, Catalog());
        creation.Place(Block, GridCell.Zero);
        Assert.DoesNotContain("bodies", CreationJson.Write(creation));
    }

    [Theory]
    [InlineData("""{ "part": 9, "position": [0, 0, 0], "rotation": [0, 0, 0, 1] }""", "not in the creation")]
    [InlineData("""{ "part": 1, "position": [0, 0, 0], "rotation": [0, 0, 0, 2] }""", "unit quaternion")]
    [InlineData("""{ "part": 1, "position": [0, 0, 0] }""", "needs part, position and rotation")]
    [InlineData("""{ "part": 1, "position": [0, 0, 0], "rotation": [0, 0, 0, 1] }, { "part": 2, "position": [0, 0, 0], "rotation": [0, 0, 0, 1] }""", "same body")]
    public void BadBodiesAreRejected(string bodies, string message)
    {
        var json = $$"""
            {
              "format_version": 1, "id": 3,
              "parts": [
                { "id": 1, "part": "test:block", "cell": [0, 0, 0] },
                { "id": 2, "part": "test:block", "cell": [1, 0, 0] }
              ],
              "connections": [ { "type": "structural", "a": 1, "b": 2 } ],
              "bodies": [ {{bodies}} ]
            }
            """;
        var diagnostics = new List<DataDiagnostic>();
        Assert.Null(CreationJson.Read(json, Catalog(), "bad.json", diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains(message));
    }

    private static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");
}
