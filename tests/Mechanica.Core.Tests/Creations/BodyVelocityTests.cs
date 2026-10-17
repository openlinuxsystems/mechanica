using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Creations;

public class BodyVelocityTests
{
    private static readonly Pose Lifted = new(new Vector3(1, 2.5f, -3), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f));
    private static readonly BodyVelocity Moving = new(new Vector3(3, -9.5f, 0.25f), new Vector3(0, 1.5f, -2));

    [Fact]
    public void BodiesWithoutAStoredVelocityAreAtRest()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        Assert.True(creation.GetBodyVelocity(0).IsZero);
    }

    [Fact]
    public void VelocitiesFollowPartsThroughPlacementAndSplits()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        creation.Place(Block, new GridCell(1, 0, 0));
        creation.SetAllBodyPoses(Lifted);
        creation.SetBodyVelocity(0, Moving);
        creation.Place(Block, new GridCell(2, 0, 0));

        var split = Assert.Single(creation.RemovePart(2, Ids()).SplitOff);

        Assert.Equal(Moving, creation.GetBodyVelocity(0));
        Assert.Equal(Moving, split.GetBodyVelocity(0));
    }

    [Fact]
    public void MergedPartsKeepTheirWorldVelocity()
    {
        var catalog = Catalog();
        var target = new Creation(1, catalog);
        target.Place(Bearing, GridCell.Zero);
        var other = new Creation(2, catalog);
        other.Place(Block, GridCell.Zero);
        other.SetAllBodyPoses(Lifted);
        other.SetBodyVelocity(0, Moving);

        Assert.True(target.TryMergeFrom(other, GridRotation.Identity, new GridCell(0, 1, 0), weld: false, out var map, out var error), error);

        // The block sits on the bearing's rotor, so it is its own Body and keeps moving as it was.
        Assert.Equal(Moving, target.GetBodyVelocity(target.BodyIndexOf(map[1])));
        Assert.True(target.GetBodyVelocity(target.BodyIndexOf(1)).IsZero);
    }

    [Fact]
    public void VelocitiesRoundTripThroughJson()
    {
        var creation = new Creation(4, Catalog());
        creation.Place(Bearing, GridCell.Zero);
        creation.Place(Block, new GridCell(0, 1, 0));
        creation.SetAllBodyPoses(Lifted);
        creation.SetBodyVelocity(1, Moving);

        var json = CreationJson.Write(creation);
        var diagnostics = new List<DataDiagnostic>();
        var loaded = CreationJson.Read(json, creation.Catalog, "moving.json", diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(loaded);
        Assert.Equal(json, CreationJson.Write(loaded));
        Assert.True(loaded.GetBodyVelocity(0).IsZero);
        Assert.Equal(Moving, loaded.GetBodyVelocity(1));
        // Resting bodies write no velocity keys, so older saves and still Creations look the same.
        Assert.Equal(1, json.Split("\"linear_velocity\"").Length - 1);
        Assert.Contains("\"angular_velocity\": [0, 1.5, -2]", json);
    }

    [Theory]
    [InlineData("\"linear_velocity\": [0, 1e39, 0]", "linear_velocity must be finite")]
    [InlineData("\"angular_velocity\": [0, -1e39, 0]", "angular_velocity must be finite")]
    public void NonFiniteVelocitiesAreRejected(string velocity, string message)
    {
        var json = $$"""
            {
              "format_version": 1, "id": 3,
              "parts": [ { "id": 1, "part": "test:block", "cell": [0, 0, 0] } ],
              "bodies": [ { "part": 1, "position": [0, 0, 0], "rotation": [0, 0, 0, 1], {{velocity}} } ]
            }
            """;
        var diagnostics = new List<DataDiagnostic>();
        Assert.Null(CreationJson.Read(json, Catalog(), "bad.json", diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains(message));
    }

    [Fact]
    public void GraphChangesBumpTheRevisionButStateDoesNot()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Block, GridCell.Zero);
        var placed = creation.Revision;
        creation.SetAllBodyPoses(Lifted);
        creation.SetBodyVelocity(0, Moving);
        Assert.Equal(placed, creation.Revision);

        creation.Place(Block, new GridCell(1, 0, 0));
        Assert.True(creation.Revision > placed);
        var grown = creation.Revision;
        creation.RemovePart(2, Ids());
        Assert.True(creation.Revision > grown);
    }
}
