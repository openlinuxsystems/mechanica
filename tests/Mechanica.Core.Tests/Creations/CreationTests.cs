using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Creations;

public class CreationTests
{
    private static readonly GridCell Up = new(0, 1, 0);

    private readonly Creation _creation = new(1, Catalog());

    [Fact]
    public void TouchingBlocksFormOneBody()
    {
        var a = _creation.Place(Block, new GridCell(0, 0, 0));
        var b = _creation.Place(Block, new GridCell(1, 0, 0));
        var c = _creation.Place(Block, new GridCell(1, 1, 0));

        Assert.Equal([a.Id, b.Id, c.Id], [1, 2, 3]);
        Assert.Equal([Connection.Rigid(1, 2), Connection.Rigid(2, 3)], _creation.Connections);
        var body = Assert.Single(_creation.Bodies);
        Assert.Equal([1, 2, 3], body.PartIds);
        Assert.Equal(12f, body.Mass);
        Assert.True((body.CentreOfMass - new Vec3(0.5f / 3, 0.25f / 3, 0)).Length < 1e-6f, body.CentreOfMass.ToString());
        Assert.Empty(_creation.Joints);
    }

    [Fact]
    public void PartNamesQualifyAsVanilla()
    {
        var catalog = new PartCatalog().Add("block", new PartDefinition { Category = PartCategory.Block, Mass = 1, Size = GridSize.One });
        var creation = new Creation(1, catalog);
        Assert.Equal("vanilla:block", creation.Place("block", GridCell.Zero).Part);
    }

    [Fact]
    public void CanPlacePartChecksWithoutChangingAnything()
    {
        _creation.Place(Block, GridCell.Zero);
        _creation.Place(Block, new GridCell(1, 0, 0));
        var before = CreationJson.Write(_creation);
        var connections = _creation.Connections;

        Assert.True(_creation.CanPlacePart(Block, new GridCell(1, 1, 0), default, out var attached, out _));
        Assert.Equal([2], attached);
        Assert.False(_creation.CanPlacePart(Block, GridCell.Zero, default, out _, out var error));
        Assert.Contains("already taken", error);
        Assert.False(_creation.CanPlacePart(Block, new GridCell(4, 0, 0), default, out _, out error));
        Assert.Contains("does not attach", error);
        Assert.False(_creation.CanPlacePart(Ore, new GridCell(0, 1, 0), default, out _, out error));
        Assert.Contains("cannot be placed", error);

        Assert.Equal(before, CreationJson.Write(_creation));
        // A check touches no edges, so their sorted list is kept.
        Assert.Same(connections, _creation.Connections);
        Assert.Equal(3, _creation.Place(Block, new GridCell(0, 1, 0)).Id);
        Assert.True(new Creation(2, Catalog()).CanPlacePart(Block, GridCell.Zero, default, out attached, out _));
        Assert.Empty(attached);
    }

    [Fact]
    public void PlacementFailures()
    {
        _creation.Place(Block, GridCell.Zero);

        Assert.False(_creation.TryPlacePart(Block, GridCell.Zero, default, out _, out var error));
        Assert.Contains("already taken by part 1", error);
        Assert.False(_creation.TryPlacePart(Block, new GridCell(5, 0, 0), default, out _, out error));
        Assert.Contains("does not attach", error);
        Assert.False(_creation.TryPlacePart("test:missing", new GridCell(1, 0, 0), default, out _, out error));
        Assert.Contains("unknown part", error);
        Assert.False(_creation.TryPlacePart(Ore, new GridCell(1, 0, 0), default, out _, out error));
        Assert.Contains("cannot be placed", error);

        Assert.Single(_creation.Parts);
        Assert.Equal(2, _creation.Place(Block, new GridCell(1, 0, 0)).Id);
    }

    [Fact]
    public void MultiCellPartsRotateAboutTheirOriginCell()
    {
        var turned = GridRotation.FromAxes(new GridCell(0, 0, 1), Up);
        var beam = _creation.Place(Beam, GridCell.Zero, turned);

        Assert.Equal([new GridCell(0, 0, 0), new GridCell(0, 0, 1)], _creation.CellsOf(beam.Id));
        Assert.Equal(beam, _creation.PartAt(new GridCell(0, 0, 1)));
        Assert.Equal(new Vec3(0, 0, 0.125f), _creation.Bodies[0].CentreOfMass);
        _creation.Place(Block, new GridCell(1, 0, 1));
        Assert.Equal([Connection.Rigid(1, 2)], _creation.Connections);
    }

    [Fact]
    public void BearingMakesTwoBodiesAndAJoint()
    {
        _creation.Place(Block, GridCell.Zero);
        var bearing = _creation.Place(Bearing, Up);
        var top = _creation.Place(Block, new GridCell(0, 2, 0));

        Assert.Equal(
            [Connection.Rigid(1, 2), Connection.Joint(ConnectionType.Rotational, bearing.Id, "rotor", top.Id)],
            _creation.Connections);
        Assert.Equal(2, _creation.Bodies.Count);
        Assert.Equal([1, 2], _creation.Bodies[0].PartIds);
        Assert.Equal(7f, _creation.Bodies[0].Mass);
        var joint = Assert.Single(_creation.Joints);
        Assert.Equal((0, 1), (joint.BodyA, joint.BodyB));
        Assert.False(joint.IsLocked);
        Assert.Equal(1, _creation.BodyIndexOf(top.Id));

        Assert.True(_creation.TryGetSocketFrame(bearing.Id, "rotor", out var position, out var normal));
        Assert.Equal(new Vec3(0, 0.375f, 0), position);
        Assert.Equal(new Vec3(0, 1, 0), normal);
    }

    [Fact]
    public void BearingSidesDoNotAttach()
    {
        _creation.Place(Bearing, GridCell.Zero);
        Assert.False(_creation.TryPlacePart(Block, new GridCell(1, 0, 0), default, out _, out _));
        // A socketed block on the bearing's rotor face: two sockets meet, structural + rotational -> joint.
        _creation.Place(Capped, Up);
        Assert.Equal([Connection.Joint(ConnectionType.Rotational, 1, "rotor", 2, "bottom")], _creation.Connections);
    }

    [Fact]
    public void BlocksAttachOnEveryFaceWhateverTheirSockets()
    {
        _creation.Place(SocketedBlock, GridCell.Zero);
        _creation.Place(SocketedBlock, new GridCell(1, 0, 0));
        _creation.Place(Block, new GridCell(0, 0, 1));
        Assert.Equal([Connection.Rigid(1, 2), Connection.Rigid(1, 3)], _creation.Connections);
    }

    [Fact]
    public void RotatedBearingFacesSideways()
    {
        // Rotor (local +Y) points to +X.
        var sideways = GridRotation.FromAxes(new GridCell(0, -1, 0), new GridCell(1, 0, 0));
        _creation.Place(Bearing, GridCell.Zero, sideways);
        Assert.False(_creation.TryPlacePart(Block, Up, default, out _, out _));
        _creation.Place(Block, new GridCell(1, 0, 0));
        _creation.Place(Block, new GridCell(-1, 0, 0));

        Assert.Equal(
            [Connection.Joint(ConnectionType.Rotational, 1, "rotor", 2), Connection.Rigid(1, 3)],
            _creation.Connections);
    }

    [Fact]
    public void TwoJointSocketsNeverConnect()
    {
        _creation.Place(Bearing, GridCell.Zero);
        var flipped = GridRotation.FromAxes(new GridCell(1, 0, 0), new GridCell(0, -1, 0));
        Assert.False(_creation.TryPlacePart(Bearing, Up, flipped, out _, out _));
    }

    [Fact]
    public void JointLoopLocks()
    {
        // Block 1 under the bearing, and a U of blocks that reaches over and also touches the top block rigidly.
        _creation.Place(Block, GridCell.Zero);
        _creation.Place(Bearing, Up);
        _creation.Place(Block, new GridCell(0, 2, 0));
        _creation.Place(Block, new GridCell(1, 0, 0));
        _creation.Place(Block, new GridCell(1, 1, 0));
        _creation.Place(Block, new GridCell(1, 2, 0));

        Assert.Single(_creation.Bodies);
        Assert.True(Assert.Single(_creation.Joints).IsLocked);
    }

    [Fact]
    public void RemovingTheMiddleSplitsTheCreation()
    {
        _creation.AuthorityPeer = 3;
        for (var x = 0; x < 5; x++)
            _creation.Place(Block, new GridCell(x, 0, 0));

        var result = _creation.RemovePart(2, Ids());

        Assert.Equal(2, result.Removed.Id);
        var split = Assert.Single(result.SplitOff);
        Assert.Equal([3, 4, 5], _creation.Parts.Select(p => p.Id));
        Assert.Equal(100, split.Id);
        Assert.Equal(3, split.AuthorityPeer);
        Assert.Equal([1], split.Parts.Select(p => p.Id));
        Assert.Empty(split.Connections);
        Assert.Equal([Connection.Rigid(3, 4), Connection.Rigid(4, 5)], _creation.Connections);
        Assert.Null(_creation.PartAt(GridCell.Zero));
        Assert.Equal(1, split.PartAt(GridCell.Zero)?.Id);

        // The split-off piece keeps working as a Creation, and ids stay unique across both.
        Assert.Equal(6, split.Place(Block, Up).Id);
        Assert.Equal(6, _creation.Place(Block, new GridCell(4, 1, 0)).Id);
    }

    [Fact]
    public void TiesKeepTheLowestPartId()
    {
        for (var x = 0; x < 3; x++)
            _creation.Place(Block, new GridCell(x, 0, 0));
        var split = _creation.RemovePart(2, Ids()).SplitOff;
        Assert.Equal([1], _creation.Parts.Select(p => p.Id));
        Assert.Equal([3], Assert.Single(split).Parts.Select(p => p.Id));
    }

    [Fact]
    public void JointsHoldPiecesTogetherButSplitBodies()
    {
        _creation.Place(Block, GridCell.Zero);
        _creation.Place(Block, new GridCell(1, 0, 0));
        _creation.Place(Piston, new GridCell(1, 1, 0));
        _creation.Place(Block, new GridCell(1, 2, 0));
        _creation.Place(Block, new GridCell(2, 2, 0));

        Assert.Empty(_creation.RemovePart(1, Ids()).SplitOff);
        Assert.Equal(2, _creation.Bodies.Count);
        Assert.Equal(ConnectionType.Linear, Assert.Single(_creation.Joints).Connection.Type);

        // Removing the piston leaves two pieces.
        var split = Assert.Single(_creation.RemovePart(3, Ids()).SplitOff);
        Assert.Equal([2], split.Parts.Select(p => p.Id));
        Assert.Equal([4, 5], _creation.Parts.Select(p => p.Id));
    }

    [Fact]
    public void RemovingTheLastPartLeavesItEmpty()
    {
        _creation.Place(Block, GridCell.Zero);
        Assert.Empty(_creation.RemovePart(1, Ids()).SplitOff);
        Assert.True(_creation.IsEmpty);
        Assert.Empty(_creation.Bodies);
        Assert.Throws<ArgumentException>(() => _creation.RemovePart(1, Ids()));
    }

    [Fact]
    public void MergeMapsTheOtherGridAndRenumbers()
    {
        _creation.Place(Block, GridCell.Zero);
        var other = new Creation(2, _creation.Catalog);
        other.Place(Block, GridCell.Zero);
        other.Place(Bearing, Up);
        other.Place(Block, new GridCell(0, 2, 0));

        // Lay the other one on its side (its +Y becomes +X), one cell over.
        var onSide = GridRotation.FromAxes(new GridCell(0, -1, 0), new GridCell(1, 0, 0));
        Assert.True(_creation.TryMergeFrom(other, onSide, new GridCell(1, 0, 0), weld: false, out var map, out var error), error);

        Assert.Equal(new Dictionary<int, int> { [1] = 2, [2] = 3, [3] = 4 }, map);
        Assert.Equal(new GridCell(3, 0, 0), _creation.FindPart(4)!.Cell);
        Assert.Equal(
            [Connection.Rigid(1, 2), Connection.Rigid(2, 3), Connection.Joint(ConnectionType.Rotational, 3, "rotor", 4)],
            _creation.Connections);
        Assert.Equal(2, _creation.Bodies.Count);
        Assert.Equal(3, other.Parts.Count);
    }

    [Fact]
    public void FailedMergeChangesNothing()
    {
        _creation.Place(Block, GridCell.Zero);
        var other = new Creation(2, _creation.Catalog);
        other.Place(Block, GridCell.Zero);

        Assert.False(_creation.TryMergeFrom(other, default, GridCell.Zero, weld: false, out _, out var error));
        Assert.Contains("overlap", error);
        Assert.False(_creation.TryMergeFrom(other, default, new GridCell(3, 0, 0), weld: false, out _, out error));
        Assert.Contains("does not attach", error);

        Assert.Single(_creation.Parts);
        Assert.Equal(2, _creation.Place(Block, Up).Id);
    }

    [Fact]
    public void WeldAttachesFacesWithoutSockets()
    {
        _creation.Place(Capped, GridCell.Zero);
        var other = new Creation(2, _creation.Catalog);
        other.Place(Capped, GridCell.Zero);

        Assert.False(_creation.TryMergeFrom(other, default, new GridCell(1, 0, 0), weld: false, out _, out _));
        Assert.True(_creation.TryMergeFrom(other, default, new GridCell(1, 0, 0), weld: true, out _, out var error), error);
        Assert.Equal([Connection.Rigid(1, 2)], _creation.Connections);
    }

    [Fact]
    public void ConnectChecksTheEdge()
    {
        _creation.Place(Block, GridCell.Zero);
        _creation.Place(Bearing, Up);
        _creation.Place(Block, new GridCell(0, 2, 0));
        _creation.Place(Block, new GridCell(1, 0, 0));

        Assert.True(_creation.TryConnect(Connection.Rigid(1, 2), out _));
        Assert.False(_creation.TryConnect(Connection.Rigid(2, 4), out var error));
        Assert.Contains("do not touch", error);
        Assert.False(_creation.TryConnect(new Connection(ConnectionType.Structural, 2, 1), out error));
        Assert.Contains("a < b", error);
        Assert.False(_creation.TryConnect(Connection.Joint(ConnectionType.Rotational, 2, "base", 1), out error));
        Assert.Contains("is structural, not rotational", error);
        Assert.False(_creation.TryConnect(Connection.Joint(ConnectionType.Rotational, 2, "nope", 3), out error));
        Assert.Contains("no socket 'nope'", error);
        Assert.False(_creation.TryConnect(Connection.Joint(ConnectionType.Linear, 2, "rotor", 3), out error));
        Assert.False(_creation.TryConnect(Connection.Rigid(1, 9), out error));
        Assert.Contains("not in creation", error);
    }

    [Fact]
    public void AuthorityPeerMustBeAPeer() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _creation.AuthorityPeer = 0);
}
