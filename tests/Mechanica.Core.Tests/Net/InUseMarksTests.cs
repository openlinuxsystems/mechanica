using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Core.Net;

namespace Mechanica.Core.Tests.Net;

public class InUseMarksTests
{
    private static KeyValuePair<int, IEnumerable<InventoryRef>> Sees(int peer, params InventoryRef[] where) => new(peer, where);

    [Fact]
    public void OpenLeavesOutPlayerInventoriesAndSortsStably()
    {
        var open = InUseMarks.Open([
            Sees(3, InventoryRef.OfPlayer(3), InventoryRef.OfPart(7, 2)),
            Sees(1, InventoryRef.OfPlayer(1), InventoryRef.OfPart(7, 2), InventoryRef.OfPart(4, 9)),
        ]);

        Assert.Equal([new OpenInventory(1, 4, 9), new OpenInventory(1, 7, 2), new OpenInventory(3, 7, 2)], open);
    }

    [Fact]
    public void OpenIsTheSameWhateverOrderViewersCome()
    {
        var a = InUseMarks.Open([Sees(1, InventoryRef.OfPart(5, 1)), Sees(2, InventoryRef.OfPart(5, 1))]);
        var b = InUseMarks.Open([Sees(2, InventoryRef.OfPart(5, 1)), Sees(1, InventoryRef.OfPart(5, 1))]);

        Assert.Equal(a, b);
    }

    [Fact]
    public void OwnUseMarksNothing()
    {
        var marks = InUseMarks.For(1, [new OpenInventory(1, 4, 9)], [new SeatAssignment(1, 4, 3)], [1]);

        Assert.Empty(marks);
    }

    [Fact]
    public void OneMarkPerObjectWithEveryOtherUser()
    {
        var marks = InUseMarks.For(1,
            [new OpenInventory(1, 7, 2), new OpenInventory(3, 7, 2), new OpenInventory(2, 7, 2), new OpenInventory(2, 4, 9)],
            [], []);

        Assert.Equal(2, marks.Count);
        Assert.Equal((InUseKind.Open, 4, 9), (marks[0].Kind, marks[0].Creation, marks[0].Part));
        Assert.Equal([2], marks[0].Peers);
        Assert.Equal((InUseKind.Open, 7, 2), (marks[1].Kind, marks[1].Creation, marks[1].Part));
        Assert.Equal([2, 3], marks[1].Peers);
    }

    [Fact]
    public void MarksOtherPlayersSeatsAndLifts()
    {
        var marks = InUseMarks.For(1, [], [new SeatAssignment(1, 4, 3), new SeatAssignment(5, 8, 1)], [6, 1, 2]);

        Assert.Equal(3, marks.Count);
        Assert.Equal((InUseKind.Seat, 8, 1), (marks[0].Kind, marks[0].Creation, marks[0].Part));
        Assert.Equal([5], marks[0].Peers);
        Assert.Equal((InUseKind.Lift, 0, 2), (marks[1].Kind, marks[1].Creation, marks[1].Part));
        Assert.Equal((InUseKind.Lift, 6), (marks[2].Kind, marks[2].Part));
        Assert.Equal([6], marks[2].Peers);
    }
}
