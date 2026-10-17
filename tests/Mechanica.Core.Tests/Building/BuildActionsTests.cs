using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Building;

public class BuildActionsTests
{
    private static readonly ActionCaller Player = new(2, ActionSource.Player);

    private readonly FakeWorld _world = new();
    private readonly ActionDispatcher _dispatcher = new();

    public BuildActionsTests()
    {
        new BuildActions(Catalog(), new BuildSettings { Reach = 5f, MaxPartsPerCreation = 3 }, _world).Register(_dispatcher);
        _world.Eyes[2] = new Vector3(0, 1.6f, 2);
    }

    [Fact]
    public void PlacingOnTheGroundStartsACreation()
    {
        var result = Place(new { part = Block, cell = new[] { 0, 0, 0 }, position = new[] { 1f, 0.125f, 0 } });

        Assert.True(result.Ok, result.ToString());
        var value = result.Value!.Value;
        Assert.Equal(1, value.GetProperty("creation").GetInt32());
        Assert.Equal(1, value.GetProperty("part").GetInt32());
        var creation = Assert.Single(_world.Creations.Values);
        Assert.Equal(new Vector3(1f, 0.125f, 0), creation.BodyPose(0).Position);
        Assert.Equal(["added 1"], _world.Log);
    }

    [Fact]
    public void PlacingAttachesOnEveryFaceOfABlock()
    {
        var id = Start(Block);
        foreach (var direction in GridCell.Directions)
        {
            if (direction == new GridCell(0, -1, 0))
                continue; // the ground
            _world.Creations[id] = Fresh(id);
            var result = Place(new { part = SocketedBlock, creation = id, cell = Cells(direction) });
            Assert.True(result.Ok, $"{direction}: {result}");
        }
        Assert.All(_world.Creations.Values, c => Assert.Single(c.Bodies));
    }

    [Fact]
    public void PlacedPartJoinsTheBodyItTouches()
    {
        var id = Start(Block);
        var result = Place(new { part = Block, creation = id, cell = new[] { 1, 0, 0 }, rotation = "+z+y" });

        Assert.True(result.Ok, result.ToString());
        var creation = _world.Creations[id];
        Assert.Equal(GridRotation.FromAxes(new GridCell(0, 0, 1), new GridCell(0, 1, 0)), creation.FindPart(2)!.Rotation);
        Assert.Equal([Connection.Rigid(1, 2)], creation.Connections);
        Assert.Equal(["added 1", "changed 1"], _world.Log);
    }

    [Theory]
    [InlineData("""{"part": "test:block", "cell": [0, 0, 0]}""", "either creation")]
    [InlineData("""{"part": "test:block", "cell": [0, 0, 0], "creation": 1, "position": [0, 0, 0]}""", "not both")]
    [InlineData("""{"part": "test:missing", "cell": [0, 0, 0], "position": [0, 0, 0]}""", "Unknown part")]
    [InlineData("""{"part": "test:ore", "cell": [0, 0, 0], "position": [0, 0, 0]}""", "cannot be placed")]
    [InlineData("""{"part": "test:block", "cell": [0, 0, 0], "creation": 7}""", "no creation 7")]
    [InlineData("""{"part": "test:block", "cell": [0, 0, 0], "creation": 1}""", "already taken")]
    [InlineData("""{"part": "test:block", "cell": [3, 0, 0], "creation": 1}""", "does not attach")]
    [InlineData("""{"part": "test:block", "cell": [0, 0, 0], "position": [30, 0, 0]}""", "you can reach 5.0 m")]
    [InlineData("""{"part": "test:block", "cell": [0, 0, 0], "position": [0, 0, -1], "rotation": "+x+x"}""", null)]
    public void BadPlacementsAreRejected(string args, string? reason)
    {
        Start(Block);
        _world.Log.Clear();

        var result = _dispatcher.Dispatch(Player, BuildActions.PlacePart, args);

        Assert.False(result.Ok);
        if (reason is null)
            Assert.Equal(ActionErrors.BadArgs, result.Code);
        else
            Assert.Contains(reason, result.Message);
        Assert.Empty(_world.Log);
        Assert.Single(_world.Creations[1].Parts);
    }

    [Fact]
    public void ReachIsMeasuredFromTheMovedBody()
    {
        var id = Start(Block);
        _world.Creations[id].SetAllBodyPoses(new Pose(new Vector3(20, 0, 0), Quaternion.Identity));

        var result = Place(new { part = Block, creation = id, cell = new[] { 0, 1, 0 } });

        Assert.Contains("away", result.Message);
    }

    [Fact]
    public void BotsAreNotLimitedByReach()
    {
        var result = _dispatcher.Dispatch(new ActionCaller(1, ActionSource.Bot), BuildActions.PlacePart,
            JsonSerializer.Serialize(new { part = Block, cell = new[] { 0, 0, 0 }, position = new[] { 50f, 0, 0 } }));
        Assert.True(result.Ok, result.ToString());
    }

    [Fact]
    public void PlayersWithoutABodyCannotBuild()
    {
        var result = _dispatcher.Dispatch(new ActionCaller(9, ActionSource.Player), BuildActions.PlacePart,
            JsonSerializer.Serialize(new { part = Block, cell = new[] { 0, 0, 0 }, position = new[] { 0f, 0, 0 } }));
        Assert.Contains("no player", result.Message);
    }

    [Fact]
    public void ClearanceCheckCanRefuse()
    {
        var id = Start(Block);
        _world.Blocked = "A player is in the way.";

        var result = Place(new { part = Block, creation = id, cell = new[] { 0, 1, 0 } });

        Assert.Equal(ActionErrors.Rejected, result.Code);
        Assert.Equal("A player is in the way.", result.Message);
        Assert.Equal(_world.Creations[id], _world.LastClearTarget);
    }

    [Fact]
    public void PartLimitIsEnforced()
    {
        var id = Start(Block);
        Assert.True(Place(new { part = Block, creation = id, cell = new[] { 1, 0, 0 } }).Ok);
        Assert.True(Place(new { part = Block, creation = id, cell = new[] { 2, 0, 0 } }).Ok);

        var result = Place(new { part = Block, creation = id, cell = new[] { 3, 0, 0 } });

        Assert.Contains("most parts allowed (3)", result.Message);
    }

    [Fact]
    public void RemovingTheMiddleSplitsTheCreation()
    {
        var id = Start(Block);
        Place(new { part = Block, creation = id, cell = new[] { 1, 0, 0 } });
        Place(new { part = Block, creation = id, cell = new[] { 2, 0, 0 } });
        _world.Log.Clear();

        var result = Remove(id, 2);

        Assert.True(result.Ok, result.ToString());
        var value = result.Value!.Value;
        Assert.False(value.GetProperty("destroyed").GetBoolean());
        Assert.Equal([2], value.GetProperty("split_off").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(["changed 1", "added 2"], _world.Log);
        Assert.Equal([1], _world.Creations[1].Parts.Select(p => p.Id));
        Assert.Equal([3], _world.Creations[2].Parts.Select(p => p.Id));
    }

    [Fact]
    public void RemovingTheLastPartDestroysTheCreation()
    {
        var id = Start(Block);
        _world.Log.Clear();

        var result = Remove(id, 1);

        Assert.True(result.Value!.Value.GetProperty("destroyed").GetBoolean());
        Assert.Equal(["removed 1"], _world.Log);
        Assert.Empty(_world.Creations);
    }

    [Fact]
    public void BadRemovalsAreRejected()
    {
        var id = Start(Block);
        Assert.Contains("no creation 5", Remove(5, 1).Message);
        Assert.Contains("has no part 9", Remove(id, 9).Message);
        _world.Eyes[2] = new Vector3(0, 0, 40);
        Assert.Contains("away", Remove(id, 1).Message);
        Assert.Single(_world.Creations[id].Parts);
    }

    private int Start(string part)
    {
        var result = Place(new { part, cell = new[] { 0, 0, 0 }, position = new[] { 0f, 0.125f, 0 } });
        Assert.True(result.Ok, result.ToString());
        return result.Value!.Value.GetProperty("creation").GetInt32();
    }

    private Creation Fresh(int id)
    {
        var creation = new Creation(id, Catalog());
        creation.Place(Block, GridCell.Zero);
        creation.SetAllBodyPoses(new Pose(new Vector3(0, 0.125f, 0), Quaternion.Identity));
        return creation;
    }

    private static int[] Cells(GridCell c) => [c.X, c.Y, c.Z];

    private ActionResult Place(object args) =>
        _dispatcher.Dispatch(Player, BuildActions.PlacePart, JsonSerializer.Serialize(args));

    private ActionResult Remove(int creation, int part) =>
        _dispatcher.Dispatch(Player, BuildActions.RemovePart, new RemovePartArgs(creation, part));

    private sealed class FakeWorld : IBuildWorld
    {
        private int _nextId = 1;

        public Dictionary<int, Creation> Creations { get; } = [];
        public Dictionary<int, Vector3> Eyes { get; } = [];
        public List<string> Log { get; } = [];
        public string? Blocked { get; set; }
        public Creation? LastClearTarget { get; private set; }

        public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

        public Vector3? EyeOf(int peer) => Eyes.TryGetValue(peer, out var eye) ? eye : null;

        public int NextCreationId() => _nextId++;

        public string? CheckClear(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose)
        {
            LastClearTarget = target;
            return Blocked;
        }

        public void Added(Creation creation)
        {
            Creations.Add(creation.Id, creation);
            Log.Add($"added {creation.Id}");
        }

        public void Changed(Creation creation) => Log.Add($"changed {creation.Id}");

        public void Removed(int creationId)
        {
            Creations.Remove(creationId);
            Log.Add($"removed {creationId}");
        }
    }
}
