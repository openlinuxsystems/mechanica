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
    private const string Plank = "test:plank";

    private static readonly ActionCaller Player = new(2, ActionSource.Player);

    private readonly FakeWorld _world = new();
    private readonly ActionDispatcher _dispatcher = new();
    private readonly PartCatalog _catalog;

    public BuildActionsTests()
    {
        var catalog = _catalog = Catalog().Add(Plank, new PartDefinition
        {
            Id = "plank",
            Name = "Plank",
            Category = PartCategory.Block,
            Mass = 2,
            Size = GridSize.One,
            SoundMaterial = "wood",
            Sounds = new() { ["remove"] = "plank_pop" },
        });
        new BuildActions(catalog, new BuildSettings { Reach = 5f, MaxPartsPerCreation = 3 }, _world).Register(_dispatcher);
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

    [Fact]
    public void PlacingAndRemovingPlayThePartsSounds()
    {
        var id = Start(Plank);
        Assert.True(Place(new { part = Block, creation = id, cell = new[] { 1, 0, 0 } }).Ok);
        Assert.True(Place(new { part = Bearing, creation = id, cell = new[] { 0, 1, 0 } }).Ok);
        Assert.True(Remove(id, 3).Ok);
        Assert.True(Remove(id, 2).Ok);
        Assert.True(Remove(id, 1).Ok);

        Assert.Equal(
        [
            "place_wood@test (0, 0.125, 0)", // the plank's material, looked up in its own mod first
            "part_attach@vanilla (0.25, 0.125, 0)", // no sound material
            "part_attach@vanilla (0, 0.375, 0)",
            "part_detach@vanilla (0, 0.375, 0)", // an interactive part
            "remove_block@vanilla (0.25, 0.125, 0)", // a block
            "plank_pop@test (0, 0.125, 0)", // bound by the part, so its own mod's id
        ], _world.Sounds);
    }

    [Fact]
    public void RefusedActionsPlayNothing()
    {
        _world.Blocked = "A player is in the way.";
        Assert.False(Place(new { part = Plank, cell = new[] { 0, 0, 0 }, position = new[] { 0f, 0.125f, 0 } }).Ok);
        Assert.Empty(_world.Sounds);
    }

    [Fact]
    public void ADragPlacesARowAsOneCreationAndSkipsWhatIsBlocked()
    {
        var dispatcher = Batch();
        var cells = BuildDrag.Cells(GridCell.Zero, new GridCell(3, 0, 0), 1, new GridSize(1, 1, 1), 16);
        var result = dispatcher.Dispatch(Player, BuildActions.PlaceParts, new PlacePartsArgs(Block, cells, Position: new Vec3(0, 0.125f, 0)));

        Assert.True(result.Ok, result.ToString());
        var batch = result.Value!.Value.Deserialize<BatchResult>(ActionJson.Options)!;
        Assert.Equal((4, 0), (batch.Done, batch.Skipped));
        Assert.Equal(4, Assert.Single(_world.Creations.Values).Parts.Count);
        // One sound for the drag, not one per part.
        Assert.Single(_world.Sounds);

        _world.Blocked = "A player is in the way.";
        var more = dispatcher.Dispatch(Player, BuildActions.PlaceParts, new PlacePartsArgs(Block, [new(0, 1, 0), new(1, 1, 0)], Creation: batch.Creation));
        Assert.False(more.Ok);
        Assert.Equal("A player is in the way.", more.Message);
    }

    [Theory]
    [InlineData(0, "at least one")]
    [InlineData(257, "At most 256")]
    public void ADragHasLimits(int count, string message)
    {
        var cells = Enumerable.Range(0, count).Select(i => new GridCell(i % 16, 0, i / 16)).ToList();
        var result = Batch().Dispatch(Player, BuildActions.PlaceParts, new PlacePartsArgs(Block, cells, Position: new Vec3(0, 0.125f, 0)));

        Assert.False(result.Ok);
        Assert.Contains(message, result.Message);
    }

    [Fact]
    public void ADragRemovesPartsOfOneBodyEvenAsTheCreationSplits()
    {
        var dispatcher = Batch();
        var placed = dispatcher.Dispatch(Player, BuildActions.PlaceParts, new PlacePartsArgs(Block, [.. Enumerable.Range(0, 5).Select(x => new GridCell(x, 0, 0))], Position: new Vec3(0, 0.125f, 0)));
        var id = placed.Value!.Value.Deserialize<BatchResult>(ActionJson.Options)!.Creation;
        _world.Sounds.Clear();

        // Taking the middle three splits the ends apart part way through.
        var result = dispatcher.Dispatch(Player, BuildActions.RemoveParts, new RemovePartsArgs(id, [3, 2, 4]));

        Assert.True(result.Ok, result.ToString());
        var batch = result.Value!.Value.Deserialize<BatchResult>(ActionJson.Options)!;
        Assert.Equal((3, 0), (batch.Done, batch.Skipped));
        Assert.Equal(2, _world.Creations.Count);
        Assert.All(_world.Creations.Values, c => Assert.Single(c.Parts));
        Assert.Single(_world.Sounds);
    }

    [Fact]
    public void ADragNeverRemovesAcrossAJoint()
    {
        var dispatcher = Batch();
        var id = Start(Block);
        Assert.True(Place(new { part = Bearing, creation = id, cell = new[] { 0, 1, 0 } }).Ok);
        Assert.True(dispatcher.Dispatch(Player, BuildActions.PlacePart, new PlacePartArgs(Block, new GridCell(0, 2, 0), Creation: id)).Ok);
        var creation = _world.Creations[id];
        Assert.Equal(2, creation.Bodies.Count);

        var result = dispatcher.Dispatch(Player, BuildActions.RemoveParts, new RemovePartsArgs(id, [1, 3]));

        Assert.False(result.Ok);
        Assert.Contains("same body", result.Message);
        Assert.Equal(3, creation.Parts.Count);
    }

    [Fact]
    public void SoilGoesFlatOnTheGround()
    {
        _world.Ground = 0f;
        var result = Place(new { part = Soil, cell = new[] { 0, 0, 0 }, position = new[] { 0f, 0.125f, 0 } });

        Assert.True(result.Ok, result.ToString());
        Assert.True(Assert.Single(_world.Creations.Values).IsGrounded);
    }

    [Theory]
    [InlineData("+x+z")]
    [InlineData("+x-z")]
    [InlineData("+y-x")]
    [InlineData("+x-y")]
    public void SoilCannotBeTurnedOntoItsSide(string rotation)
    {
        _world.Ground = 0f;
        var result = Place(new { part = Soil, cell = new[] { 0, 0, 0 }, rotation, position = new[] { 0f, 0.5f, 0 } });

        Assert.False(result.Ok);
        Assert.Contains("only flat on the ground", result.Message);
        Assert.Empty(_world.Creations);
    }

    [Fact]
    public void SoilCannotFloatOffTheGround()
    {
        _world.Ground = 0f;
        var result = Place(new { part = Soil, cell = new[] { 0, 0, 0 }, position = new[] { 0f, 1.5f, 0 } });

        Assert.False(result.Ok);
        Assert.Contains("only flat on the ground", result.Message);
    }

    [Fact]
    public void SoilCannotGoOnACreation()
    {
        _world.Ground = 0f;
        var id = Start(Block);
        var result = Place(new { part = Soil, creation = id, cell = new[] { 0, 1, 0 } });

        Assert.False(result.Ok);
        Assert.Contains("not on a creation", result.Message);
        Assert.Single(_world.Creations[id].Parts);
    }

    [Fact]
    public void ABatchOfSoilGoesDownInARowOnTheGround()
    {
        _world.Ground = 0f;
        var dispatcher = Batch();
        var cells = new GridCell[] { new(0, 0, 0), new(1, 0, 0), new(2, 0, 0) };
        var result = dispatcher.Dispatch(Player, BuildActions.PlaceParts, new PlacePartsArgs(Soil, cells, Position: new Vec3(0, 0.125f, 0)));

        Assert.True(result.Ok, result.ToString());
        // Soil doesn't attach to soil, so each one is its own Creation, side by side on the ground.
        Assert.Equal(3, _world.Creations.Count);
        Assert.Equal([0f, 0.25f, 0.5f], _world.Creations.Values.Select(c => c.BodyPose(0).Position.X).Order());
        Assert.All(_world.Creations.Values, c => Assert.True(c.IsGrounded));
    }

    [Fact]
    public void ABatchOfSoilCannotStackUpwards()
    {
        _world.Ground = 0f;
        var dispatcher = Batch();
        var cells = new GridCell[] { new(0, 0, 0), new(0, 1, 0) };
        var result = dispatcher.Dispatch(Player, BuildActions.PlaceParts, new PlacePartsArgs(Soil, cells, Position: new Vec3(0, 0.125f, 0)));

        Assert.True(result.Ok, result.ToString());
        Assert.Equal(1, _world.Creations.Values.Sum(c => c.Parts.Count));
    }

    [Fact]
    public void APartPlacedOnTheGroundIsAnchoredThere()
    {
        _world.Ground = 0f;
        var id = Start(Block);

        var creation = _world.Creations[id];
        Assert.True(creation.IsGrounded);
        Assert.True(creation.IsAnchored(1));
        Assert.True(creation.IsBodyAnchored(0));
    }

    [Fact]
    public void APartPlacedOffTheGroundIsNotAnchored()
    {
        _world.Ground = 0f;
        var result = Place(new { part = Block, cell = new[] { 0, 0, 0 }, position = new[] { 0f, 1.5f, 0 } });

        Assert.True(result.Ok, result.ToString());
        Assert.False(Assert.Single(_world.Creations.Values).IsGrounded);
    }

    [Fact]
    public void PartsAddedToAGroundedCreationAreAnchoredOnlyWhereTheyTouchTheGround()
    {
        _world.Ground = 0f;
        var id = Start(Block);
        Assert.True(Place(new { part = Block, creation = id, cell = new[] { 1, 0, 0 } }).Ok);
        Assert.True(Place(new { part = Block, creation = id, cell = new[] { 0, 1, 0 } }).Ok);

        Assert.Equal([1, 2], _world.Creations[id].AnchoredParts);
    }

    [Fact]
    public void AnUnanchoredCreationOnTheGroundStaysFree()
    {
        // A car on its wheels is not anchored, and building onto it at ground level does not anchor it.
        _world.Ground = 0f;
        _world.Creations[7] = Fresh(7);
        Assert.True(Place(new { part = Block, creation = 7, cell = new[] { 1, 0, 0 } }).Ok);

        Assert.False(_world.Creations[7].IsGrounded);
    }

    [Fact]
    public void TakingAwayTheGroundedPartFreesWhatItHeld()
    {
        _world.Ground = 0f;
        var id = Start(Block);
        Assert.True(Place(new { part = Block, creation = id, cell = new[] { 0, 1, 0 } }).Ok);

        Assert.True(Remove(id, 1).Ok);

        var top = _world.Creations[id];
        Assert.Equal([2], top.Parts.Select(p => p.Id));
        Assert.False(top.IsGrounded);
    }

    [Fact]
    public void PiecesThatSplitOffKeepOnlyTheirOwnAnchors()
    {
        // A grounded row with a block on top of its end: taking out the middle leaves two grounded pieces,
        // and taking the end's ground block from under the top one drops it.
        _world.Ground = 0f;
        var dispatcher = Batch();
        var id = Start(Block);
        foreach (var cell in new[] { new GridCell(1, 0, 0), new GridCell(2, 0, 0), new GridCell(2, 1, 0) })
            Assert.True(dispatcher.Dispatch(Player, BuildActions.PlacePart, new PlacePartArgs(Block, cell, Creation: id)).Ok);
        Assert.Equal([1, 2, 3], _world.Creations[id].AnchoredParts);

        var split = dispatcher.Dispatch(Player, BuildActions.RemovePart, new RemovePartArgs(id, 2)).Value!.Value
            .Deserialize<RemovePartResult>(ActionJson.Options)!;
        var piece = _world.Creations[Assert.Single(split.SplitOff)];
        Assert.All(_world.Creations.Values, c => Assert.True(c.IsGrounded));
        var end = new[] { _world.Creations[id], piece }.Single(c => c.FindPart(3) is not null);

        Assert.True(dispatcher.Dispatch(Player, BuildActions.RemovePart, new RemovePartArgs(end.Id, 3)).Ok);
        Assert.False(end.IsGrounded);
    }

    // The actions again with room for a drag's worth of parts.
    private ActionDispatcher Batch()
    {
        var dispatcher = new ActionDispatcher();
        new BuildActions(_catalog, new BuildSettings { Reach = 10f }, _world).Register(dispatcher);
        return dispatcher;
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

        public List<string> Sounds { get; } = [];

        public Dictionary<int, Creation> Creations { get; } = [];
        public Dictionary<int, Vector3> Eyes { get; } = [];
        public List<string> Log { get; } = [];
        public string? Blocked { get; set; }
        public Creation? LastClearTarget { get; private set; }

        // Height of flat ground, or null for a world without any.
        public float? Ground { get; set; }

        public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

        public Vector3? EyeOf(int peer) => Eyes.TryGetValue(peer, out var eye) ? eye : null;

        public int NextCreationId() => _nextId++;

        public string? CheckClear(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose)
        {
            LastClearTarget = target;
            return Blocked;
        }

        public bool RestsOnWorld(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose)
        {
            // Cells are centred on their grid points.
            var bottom = PartGeometry.Cells(definition, cell, rotation)
                .Min(c => pose.Apply(new Vector3(c.X, c.Y, c.Z) * MechanicaInfo.GridSize).Y) - MechanicaInfo.GridSize / 2;
            return Ground is { } ground && bottom <= ground + 0.05f;
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

        public void PlaySound(string sound, string ownerModId, Vector3 position) =>
            Sounds.Add(FormattableString.Invariant($"{sound}@{ownerModId} ({position.X:0.###}, {position.Y:0.###}, {position.Z:0.###})"));
    }
}
