using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;

namespace Mechanica.Core.Tests.Logic;

/// <summary>Order numbers and bearing turn arrows the Connect Tool shows on linked parts.</summary>
public class ConnectionOrderTests
{
    private const string Seat = "test:seat", Controller = "test:controller", Engine = "test:engine", Switch = "test:switch";
    private const string Bearing = "test:bearing", Lamp = "test:lamp";

    private static readonly PartCatalog Catalog = new PartCatalog()
        .Add(Seat, Part(new LogicDefinition { Outputs = 10 }) with { Seat = new SeatDefinition() })
        .Add(Controller, Part(new LogicDefinition { Inputs = 1, Outputs = 10 }) with
        {
            Settings = [new SettingDefinition { Id = "frames", Name = "Frames", Type = SettingType.Sequence, Min = -90, Max = 90, Step = 1, MaxCount = 3 }],
        })
        .Add(Engine, Part(new LogicDefinition { Inputs = 1, Outputs = 10, Kind = PortKind.Power }))
        .Add(Switch, Part(new LogicDefinition { Inputs = 1, Outputs = 10 }))
        .Add(Bearing, Part(new LogicDefinition { Inputs = 4, Kind = PortKind.Bearing, Reverse = "reverse" }))
        .Add(Lamp, Part(new LogicDefinition { Inputs = 4 }));

    // parts 1..n along X in the given order
    private static Creation Row(params string[] parts)
    {
        var creation = new Creation(1, Catalog);
        for (var i = 0; i < parts.Length; i++)
            Assert.True(creation.TryPlacePart(parts[i], new GridCell(i, 0, 0), default, out _, out var error), error);
        return creation;
    }

    private static void Link(Creation c, int from, int to) => Assert.True(c.TryLink(from, to, out var error), error);

    [Fact]
    public void SeatsAndControllersNumberWhatTheyAreLinkedToInPartOrder()
    {
        var c = Row(Seat, Controller, Bearing, Lamp, Bearing, Switch);
        Link(c, 1, 5);
        Link(c, 1, 3);
        Link(c, 1, 4);
        Link(c, 2, 5);
        Link(c, 6, 3);
        Assert.Equal([(1, 1)], ConnectionOrder.NumbersOf(c, 3));
        Assert.Equal([(1, 2)], ConnectionOrder.NumbersOf(c, 4));
        Assert.Equal([(1, 3), (2, 1)], ConnectionOrder.NumbersOf(c, 5));
        // a switch numbers nothing, and nothing numbers a part with no links in
        Assert.Empty(ConnectionOrder.NumbersOf(c, 6));
        Assert.Equal([(1, 1)], ConnectionOrder.NumbersOf(c, 3));
    }

    [Fact]
    public void ABearingShowsWhichWayItTurnsOnlyWhenSomethingDrivesIt()
    {
        var c = Row(Engine, Switch, Bearing, Bearing, Bearing, Seat, Lamp);
        Assert.False(ConnectionOrder.ShowsDirection(c, 3));
        Link(c, 2, 3);
        Assert.False(ConnectionOrder.ShowsDirection(c, 3));
        Link(c, 1, 3);
        Assert.True(ConnectionOrder.ShowsDirection(c, 3));
        Link(c, 6, 4);
        Assert.True(ConnectionOrder.ShowsDirection(c, 4));
        Assert.False(ConnectionOrder.ShowsDirection(c, 5));
        // only parts that can be reversed have an arrow
        Link(c, 1, 7);
        Assert.False(ConnectionOrder.ShowsDirection(c, 7));
        Assert.False(ConnectionOrder.ShowsDirection(c, 99));
    }

    [Fact]
    public void DrivingAndNumberingComeFromThePartData()
    {
        Assert.True(ConnectionOrder.Numbers(Catalog.FindDefinition(Seat)!));
        Assert.True(ConnectionOrder.Numbers(Catalog.FindDefinition(Controller)!));
        Assert.False(ConnectionOrder.Numbers(Catalog.FindDefinition(Engine)!));
        Assert.True(ConnectionOrder.Drives(Catalog.FindDefinition(Engine)!));
        Assert.False(ConnectionOrder.Drives(Catalog.FindDefinition(Switch)!));
        Assert.False(ConnectionOrder.Drives(Part(null) with { Seat = new SeatDefinition() }));
    }

    private static PartDefinition Part(LogicDefinition? logic) => new()
    {
        Id = "x", Name = "X", Category = PartCategory.Block, Mass = 1, Size = GridSize.One, Logic = logic,
    };
}
