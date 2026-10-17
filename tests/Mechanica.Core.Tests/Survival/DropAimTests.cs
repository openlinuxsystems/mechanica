using System.Numerics;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Tests.Survival;

public class DropAimTests
{
    private static ItemDrop Pile(int id, float x, float z) => new(id, new Vec3(x, 0, z), [new ItemStack("test:scrap", 1)], 0);

    [Fact]
    public void TheNearestPileOnTheRayInReachIsAimed()
    {
        var piles = new[] { Pile(1, 0, -3), Pile(2, 0, -2), Pile(3, 1, -1) };
        var from = new Vector3(0, 1.6f, 0);
        var down = Vector3.Normalize(new Vector3(0, -1.45f, -2));
        Assert.Equal(2, DropAim.Aimed(piles, from, down, 5)?.Id);
        // Something solid nearer than the pile hides it.
        Assert.Null(DropAim.Aimed(piles, from, down, 1));
        // Looking past every pile.
        Assert.Null(DropAim.Aimed(piles, from, -Vector3.UnitZ, 5));
        Assert.Null(DropAim.Aimed(piles, from, Vector3.UnitZ, 5));
    }
}
