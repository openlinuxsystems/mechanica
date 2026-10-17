using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;

namespace Mechanica.Core.Tests.Editor;

public class SocketToolsTests
{
    [Fact]
    public void OuterFacesOfABeam()
    {
        var faces = SocketTools.OuterFaces(new GridSize(2, 1, 1)).ToList();

        Assert.Equal(10, faces.Count);
        Assert.DoesNotContain((new GridCell(0, 0, 0), new GridCell(1, 0, 0)), faces);
    }

    [Fact]
    public void BlocksConnectOnEveryFace()
    {
        var contacts = SocketTools.PreviewConnections(EditorParts.Block());

        Assert.Equal(6, contacts.Count);
        Assert.All(contacts, c => Assert.Equal(ConnectionType.Structural, c.Connection));
    }

    [Fact]
    public void BearingShowsItsBaseRotorAndClosedSides()
    {
        var contacts = SocketTools.PreviewConnections(EditorParts.Bearing()).ToDictionary(c => c.Direction);

        Assert.Equal((ConnectionType?)ConnectionType.Structural, contacts[new GridCell(0, -1, 0)].Connection);
        Assert.Equal("base", contacts[new GridCell(0, -1, 0)].Socket);
        Assert.Equal((ConnectionType?)ConnectionType.Rotational, contacts[new GridCell(0, 1, 0)].Connection);
        Assert.Equal("rotor", contacts[new GridCell(0, 1, 0)].Socket);
        Assert.Null(contacts[new GridCell(1, 0, 0)].Connection);
        Assert.Null(contacts[new GridCell(1, 0, 0)].Socket);
    }

    [Fact]
    public void ItemsHaveNoPreview() =>
        Assert.Empty(SocketTools.PreviewConnections(new PartDefinition { Id = "x", Name = "X", Category = PartCategory.Resource }));

    [Fact]
    public void FaceCentreIsRelativeToThePartCentre()
    {
        Assert.Equal(new Vec3(0, 0.125f, 0), SocketTools.FaceCentre(GridSize.One, GridCell.Zero, new GridCell(0, 1, 0)));
        Assert.Equal(new Vec3(0.25f, 0, 0), SocketTools.FaceCentre(new GridSize(2, 1, 1), new GridCell(1, 0, 0), new GridCell(1, 0, 0)));
        Assert.Equal(new Vec3(-0.125f, 0, 0.125f), SocketTools.FaceCentre(new GridSize(2, 1, 1), GridCell.Zero, new GridCell(0, 0, 1)));
    }

    [Fact]
    public void SnapPutsTheSocketOnTheFaceItFaces()
    {
        var (position, normal) = SocketTools.SnapToFace(new GridSize(2, 1, 1), new Vec3(0.1f, 0.05f, 0.4f), new Vec3(0.2f, 0.1f, 0.9f));

        Assert.Equal(new Vec3(0, 0, 1), normal);
        Assert.Equal(new Vec3(0.125f, 0.0625f, 0.125f), position);
    }

    [Fact]
    public void SideSocketSitsOnTheSideCentre()
    {
        var socket = SocketTools.SideSocket(new GridSize(1, 2, 1), new GridCell(0, 1, 0), "top", SocketType.Rotational);

        Assert.Equal(new Vec3(0, 0.25f, 0), socket.Position);
        Assert.Equal(new Vec3(0, 1, 0), socket.Normal);
        Assert.Equal(SocketType.Rotational, socket.Type);
    }

    [Fact]
    public void UnusedSocketIdSkipsTakenOnes()
    {
        var part = EditorParts.Block() with { Sockets = [new SocketDefinition { Id = "socket_1" }] };

        Assert.Equal("socket_2", SocketTools.UnusedSocketId(part));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 1, 0)]
    [InlineData(-3, 1, 2, -1, 0, 0)]
    [InlineData(0.1, -0.2, 0.1, 0, -1, 0)]
    public void ClosestAxis(float x, float y, float z, int ex, int ey, int ez) =>
        Assert.Equal(new GridCell(ex, ey, ez), SocketTools.ClosestAxis(new Vec3(x, y, z)));
}
