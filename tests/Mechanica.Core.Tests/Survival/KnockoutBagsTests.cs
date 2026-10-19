using System.Numerics;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Tests.Survival;

public class KnockoutBagsTests
{
    private static ItemDrop Pile(int id, bool knockedOut, int? owner) =>
        new(id, new Vec3(id, 0, 2), [new ItemStack("test:scrap", 1)], 0, KnockedOut: knockedOut, Owner: owner);

    [Fact]
    public void APlayerSeesOnlyTheirOwnBagsNewestFirst()
    {
        var drops = new[] { Pile(1, true, 2), Pile(2, false, null), Pile(3, true, 3), Pile(4, true, 2) };

        var mine = KnockoutBags.WaypointsFor(drops, 2, "Knockout bag").ToList();

        Assert.Equal(["knockout_bag:4", "knockout_bag:1"], mine.Select(w => w.Id));
        Assert.All(mine, w => Assert.Equal("Knockout bag", w.Name));
        Assert.Equal(new Vector3(4, 0, 2), mine[0].Position);
        Assert.Empty(KnockoutBags.WaypointsFor(drops, 5, "Knockout bag"));
    }
}
