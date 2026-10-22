using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using static Mechanica.Core.Tests.Items.TransportParts;

namespace Mechanica.Core.Tests.Items;

/// <summary>
/// The item transport network (Parts map E1): IO ports on the same block face from either side join, conduits (Vacuum
/// Pipes) pass items from each of their ports to the others, and which way items go follows the ports' modes. The network
/// is worked out from the Creation's parts, so it follows every change to the build.
/// </summary>
public sealed class TransportNetworkTests
{
    private readonly PartCatalog _catalog = Catalog();

    // A deck of blocks under x 0..7, z 0..3 (y -1), so every part set on it attaches and nothing splits off.
    private Creation New()
    {
        var creation = new Creation(1, _catalog);
        for (var x = 0; x < 8; x++)
        {
            for (var z = 0; z < 4; z++)
                Place(creation, Block, x, -1, z);
        }
        return creation;
    }

    private static int Place(Creation creation, string part, int x, int y, int z, GridRotation rotation = default)
    {
        Assert.True(creation.TryPlacePart(part, new GridCell(x, y, z), rotation, out var placed, out var error), error);
        return placed!.Id;
    }

    // A chest (port facing +Z), two pipes along Z and a pump facing back down them.
    private (Creation Creation, int Chest, int Pipe1, int Pipe2, int Pump) Line(string chest = Chest)
    {
        var c = New();
        var box = Place(c, chest, 0, 0, 0);
        var p1 = Place(c, Pipe, 0, 0, 1);
        var p2 = Place(c, Pipe, 0, 0, 2);
        var pump = Place(c, Pump, 0, 0, 3);
        return (c, box, p1, p2, pump);
    }

    [Fact]
    public void PortsOnTheSameFaceJoinThroughPipes()
    {
        var (c, chest, p1, p2, pump) = Line();
        var link = Assert.Single(TransportNetwork.Reach(c, pump));
        Assert.Equal(("port", chest, "port", true, true), (link.Port, link.Part, link.RemotePort, link.TakesIn, link.GivesOut));
        Assert.Equal([p2, p1], link.Pipes);
        Assert.Equal([chest], TransportNetwork.Into(c, pump));
        Assert.Equal([chest], TransportNetwork.From(c, pump));
        // Either end sees the other; the pipes themselves are not ends.
        Assert.Equal([pump], TransportNetwork.Reach(c, chest).Select(l => l.Part));
    }

    [Fact]
    public void AGapOrAPipeTurnedSidewaysBreaksTheNetwork()
    {
        var (c, _, p1, _, pump) = Line();
        c.RemovePart(p1, () => 9);
        Assert.Empty(TransportNetwork.Reach(c, pump));

        // A pipe across the line (its ends facing ±X) does not join the chest's +Z port.
        Place(c, Pipe, 0, 0, 1, AlongX);
        Assert.Empty(TransportNetwork.Reach(c, pump));
    }

    [Fact]
    public void ACornerTurnsTheNetwork()
    {
        var c = New();
        var chest = Place(c, Chest, 0, 0, 0);
        var corner = Place(c, Corner, 0, 0, 1);
        var pipe = Place(c, Pipe, 1, 0, 1, AlongX);
        // The pump's port (its local -Z) faces -X, back along the pipe.
        var pump = Place(c, Pump, 2, 0, 1, AlongX);
        var link = Assert.Single(TransportNetwork.Reach(c, pump));
        Assert.Equal(chest, link.Part);
        Assert.Equal([pipe, corner], link.Pipes);
    }

    [Fact]
    public void PortsThatTouchJoinWithoutAPipe()
    {
        var c = New();
        var chest = Place(c, Chest, 0, 0, 0);
        var pump = Place(c, Pump, 0, 0, 1);
        Assert.Equal([chest], TransportNetwork.Into(c, pump));
        Assert.Empty(TransportNetwork.Reach(c, chest).Single().Pipes);
    }

    [Fact]
    public void ItemsGoOnlyTheWayThePortsPoint()
    {
        var (input, _, _, _, pumpIn) = Line(InChest);
        Assert.Single(TransportNetwork.Into(input, pumpIn));
        Assert.Empty(TransportNetwork.From(input, pumpIn));

        var (output, _, _, _, pumpOut) = Line(OutChest);
        Assert.Empty(TransportNetwork.Into(output, pumpOut));
        Assert.Single(TransportNetwork.From(output, pumpOut));
    }

    [Fact]
    public void AStationTakesInputsFromItsInputPortAndSendsOutputsToItsOutputPort()
    {
        var c = New();
        // Its input port (-X) piped from a chest that sends out; its output port (+X) to one that takes in.
        var source = Place(c, OutChest, 0, 0, 0, AlongX);
        Place(c, Pipe, 1, 0, 0, AlongX);
        Place(c, Pipe, 2, 0, 0, AlongX);
        var station = Place(c, Station, 3, 0, 0);
        Place(c, Pipe, 4, 0, 0, AlongX);
        Place(c, Pipe, 5, 0, 0, AlongX);
        var target = Place(c, InChest, 6, 0, 0, GridRotation.FromAxes(new GridCell(0, 0, 1), new GridCell(0, 1, 0)));

        Assert.Equal([source], StorageLinks.InputsOf(c, station));
        Assert.Equal([target], StorageLinks.OutputsOf(c, station));
        Assert.Equal([source, target], StorageLinks.Of(c, station));
        Assert.Equal([station], StorageLinks.StationsOf(c, source));
        Assert.Empty(StorageLinks.NearOf(c, station));
    }

    [Fact]
    public void TheNetworkFollowsTheBuild()
    {
        var c = New();
        var chest = Place(c, Chest, 0, 0, 0);
        var pump = Place(c, Pump, 0, 0, 2);
        Assert.Empty(TransportNetwork.Reach(c, pump));
        var pipe = Place(c, Pipe, 0, 0, 1);
        Assert.Equal([chest], TransportNetwork.Into(c, pump));
        c.RemovePart(pipe, () => 9);
        Assert.Empty(TransportNetwork.Into(c, pump));
    }

    [Fact]
    public void APortFaceIsTheCellItSitsInAndTheWayItFaces()
    {
        var pipe = _catalog.FindDefinition(Pipe)!;
        Assert.Equal((new GridCell(4, 1, 2), new GridCell(1, 0, 0)), TransportNetwork.FaceOf(pipe, pipe.IoPorts![1], new GridCell(4, 1, 2), AlongX));
        Assert.Equal((new GridCell(4, 1, 2), new GridCell(-1, 0, 0)), TransportNetwork.FaceOf(pipe, pipe.IoPorts![0], new GridCell(4, 1, 2), AlongX));
        // A big part's port: a 4 x 3 x 3 container's on its +X end, at the middle block of the end.
        var tank = Placeable("tank") with
        {
            Size = new GridSize(4, 3, 3),
            IoPorts = [new ItemPortDefinition { Id = "port", Position = new Vec3(0.5f, 0, 0), Normal = new Vec3(1, 0, 0) }],
        };
        Assert.Equal((new GridCell(3, 1, 1), new GridCell(1, 0, 0)), TransportNetwork.FaceOf(tank, tank.IoPorts![0], GridCell.Zero, default));
    }
}
