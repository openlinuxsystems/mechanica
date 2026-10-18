using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Tests.Creations;

namespace Mechanica.Core.Tests.Actions;

public class CreationActionsTests
{
    [Theory]
    [InlineData("test:block", new[] { 0f, 1f }, 0, "position must be")]
    [InlineData("test:block", new[] { float.NaN, 1f, 0f }, 0, "finite")]
    [InlineData("test:block", new[] { 9000f, 1f, 0f }, 0, "from the world centre")]
    [InlineData("test:block", new[] { 0f, 5000f, 0f }, 0, "height must be")]
    [InlineData("test:nope", new[] { 0f, 1f, 0f }, 0, "no part 'test:nope'")]
    [InlineData("test:ore", new[] { 0f, 1f, 0f }, 0, "inventory item")]
    [InlineData("test:block", new[] { 0f, 1f, 0f }, CreationActions.MaxCreations, "clear some first")]
    public void BadSpawnsAreRefused(string part, float[] position, int count, string reason)
    {
        var refusal = CreationActions.ValidateSpawn(new SpawnPartArgs(part, position), TestParts.Catalog(), count);

        Assert.NotNull(refusal);
        Assert.Contains(reason, refusal);
    }

    [Fact]
    public void GoodSpawnIsAccepted() =>
        Assert.Null(CreationActions.ValidateSpawn(new SpawnPartArgs("test:beam", [1, 2, 3], 90), TestParts.Catalog(), 0));

    [Fact]
    public void SpawnPutsTheFootprintCentreAtThePosition()
    {
        var creation = CreationActions.BuildSpawn(new SpawnPartArgs("test:beam", [1, 2, 3], 90), 7, TestParts.Catalog());

        Assert.Equal(7, creation.Id);
        Assert.Equal(Creation.HostPeer, creation.AuthorityPeer);
        var part = Assert.Single(creation.Parts);
        Assert.Equal("test:beam", part.Part);
        var centre = PartGeometry.CentreMetres(creation.DefinitionOf(part.Id), part.Cell, part.Rotation).ToNumerics();
        var world = creation.BodyPose(0).Apply(centre);
        Assert.True(Vector3.Distance(new Vector3(1, 2, 3), world) < 1e-5f, $"centre at {world}");
        // Turned 90° about Y, the beam's long local X axis lies along world -Z.
        var end = creation.BodyPose(0).Apply(centre + Vector3.UnitX);
        Assert.True(Vector3.Distance(new Vector3(1, 2, 2), end) < 1e-5f, $"x axis at {end}");
    }

    [Theory]
    [InlineData("""{ "part": "test:block", "position": [0, 1, 0] }""", true)]
    [InlineData("""{ "part": "test:block", "position": [0, 1, 0], "yaw": 45 }""", true)]
    [InlineData("""{ "part": "test:block" }""", false)]
    [InlineData("""{ "part": "test:block", "position": [0, 1, 0], "peer": 2 }""", false)]
    public void ArgumentsParseStrictly(string json, bool ok)
    {
        var dispatcher = new ActionDispatcher();
        dispatcher.Register<SpawnPartArgs>(CreationActions.SpawnPart,
            (_, args) => CreationActions.ValidateSpawn(args, TestParts.Catalog(), 0),
            (_, _) => null);

        Assert.Equal(ok, dispatcher.Dispatch(ActionCaller.HostPlayer, CreationActions.SpawnPart, json).Ok);
    }
}
