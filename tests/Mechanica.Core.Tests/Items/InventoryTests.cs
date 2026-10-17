using Mechanica.Core.Items;
using static Mechanica.Core.Tests.Items.ItemParts;

namespace Mechanica.Core.Tests.Items;

public class InventoryTests
{
    private readonly Mechanica.Core.Data.PartCatalog _catalog = Catalog();

    [Fact]
    public void AddingFillsMatchingStacksThenEmptySlots()
    {
        var inventory = new Inventory(_catalog, 3);
        inventory.SetSlot(1, new ItemStack(Scrap, 45));

        Assert.Equal(60, inventory.Add(Scrap, 60));

        Assert.Equal(new ItemStack(Scrap, 50), inventory[0]);
        Assert.Equal(new ItemStack(Scrap, 50), inventory[1]);
        Assert.Equal(new ItemStack(Scrap, 5), inventory[2]);
        Assert.Equal(105, inventory.Count(Scrap));
    }

    [Fact]
    public void AddingStopsWhenFullAndReportsHowManyFit()
    {
        var inventory = new Inventory(_catalog, 2);
        inventory.Add(Wood, 50);

        Assert.Equal(50, inventory.SpaceFor(Scrap));
        Assert.Equal(50, inventory.Add(Scrap, 80));
        Assert.Equal(0, inventory.SpaceFor(Scrap));
        Assert.Equal(0, inventory.Add(Scrap, 1));
    }

    [Fact]
    public void ItemsWithoutAStackSizeTakeOneSlotEach()
    {
        var inventory = new Inventory(_catalog, 2);

        Assert.Equal(2, inventory.Add(Key, 3));
        Assert.Equal(new ItemStack(Key, 1), inventory[1]);
    }

    [Fact]
    public void BareIdsMeanVanillaAndUnknownItemsAreRefused()
    {
        var inventory = new Inventory(_catalog, 2);

        Assert.Equal(0, inventory.Add("nothing", 1));
        Assert.Throws<ArgumentException>(() => inventory.SetSlot(0, new ItemStack("vanilla:nothing", 1)));
    }

    [Fact]
    public void AnInventoryWithAcceptsTakesOnlyThoseItems()
    {
        var tank = new Inventory(_catalog, 1, [Fuel]);

        Assert.Equal(0, tank.Add(Scrap, 1));
        Assert.Equal(5, tank.Add(Fuel, 5));
        Assert.Throws<ArgumentException>(() => tank.SetSlot(0, new ItemStack(Scrap, 1)));
    }

    [Fact]
    public void RemovingTakesFromTheLastSlotsFirst()
    {
        var inventory = new Inventory(_catalog, 3);
        inventory.Add(Scrap, 70);
        inventory.SetSlot(2, new ItemStack(Scrap, 10));

        Assert.Equal(15, inventory.Remove(Scrap, 15));

        Assert.Equal(new ItemStack(Scrap, 50), inventory[0]);
        Assert.Equal(new ItemStack(Scrap, 15), inventory[1]);
        Assert.Null(inventory[2]);
        Assert.Equal(65, inventory.Remove(Scrap, 100));
        Assert.True(inventory.IsEmpty);
    }

    [Fact]
    public void TryAddAllAddsNothingUnlessEverythingFits()
    {
        var inventory = new Inventory(_catalog, 1);

        Assert.False(inventory.TryAddAll([new ItemAmount(Scrap, 10), new ItemAmount(Wood, 1)]));
        Assert.True(inventory.IsEmpty);
        Assert.Equal(0, inventory.Revision);
        Assert.True(inventory.TryAddAll([new ItemAmount(Scrap, 10), new ItemAmount(Scrap, 40)]));
        Assert.Equal(50, inventory.Count(Scrap));
    }

    [Fact]
    public void HasAllAddsUpAmountsOfOneItem()
    {
        var inventory = new Inventory(_catalog, 2);
        inventory.Add(Scrap, 3);

        Assert.True(inventory.HasAll([new ItemAmount(Scrap, 2), new ItemAmount("test:scrap", 1)]));
        Assert.False(inventory.HasAll([new ItemAmount(Scrap, 2), new ItemAmount(Scrap, 2)]));
    }

    [Fact]
    public void EveryChangeBumpsTheRevision()
    {
        var inventory = new Inventory(_catalog, 2);
        inventory.Add(Scrap, 5);
        inventory.Remove(Scrap, 1);
        inventory.SetSlot(1, new ItemStack(Wood, 2));
        inventory.SetSlot(1, new ItemStack(Wood, 2));
        inventory.Remove(Kit, 1);

        Assert.Equal(3, inventory.Revision);
    }

    // ---- Move ----

    [Fact]
    public void MovingPartOfAStackIntoAnEmptySlotSplitsIt()
    {
        var a = new Inventory(_catalog, 2);
        var b = new Inventory(_catalog, 2);
        a.Add(Scrap, 30);

        Assert.Equal(10, Inventory.Move(a, 0, b, 1, 10));

        Assert.Equal(new ItemStack(Scrap, 20), a[0]);
        Assert.Equal(new ItemStack(Scrap, 10), b[1]);
    }

    [Fact]
    public void MovingOntoTheSameItemMergesUpToTheStackSize()
    {
        var a = new Inventory(_catalog, 2);
        a.SetSlot(0, new ItemStack(Scrap, 30));
        a.SetSlot(1, new ItemStack(Scrap, 30));

        Assert.Equal("Only 20 more fit in slot 1.", Inventory.CheckMove(a, 0, a, 1, null));
        Assert.Equal(20, Inventory.Move(a, 0, a, 1, 20));
        Assert.Equal(new ItemStack(Scrap, 10), a[0]);
        Assert.Equal(new ItemStack(Scrap, 50), a[1]);
    }

    [Fact]
    public void MovingAWholeStackOntoADifferentItemSwapsThem()
    {
        var a = new Inventory(_catalog, 1);
        var b = new Inventory(_catalog, 1);
        a.Add(Scrap, 5);
        b.Add(Wood, 7);

        Assert.Equal(5, Inventory.Move(a, 0, b, 0, null));

        Assert.Equal(new ItemStack(Wood, 7), a[0]);
        Assert.Equal(new ItemStack(Scrap, 5), b[0]);
        Assert.Equal("Slot 0 holds something else; move the whole stack to swap.", Inventory.CheckMove(a, 0, b, 0, 3));
    }

    [Fact]
    public void MovingWithoutATargetSlotMovesWhatFits()
    {
        var a = new Inventory(_catalog, 1);
        var b = new Inventory(_catalog, 1);
        a.Add(Scrap, 50);
        b.Add(Scrap, 45);

        Assert.Equal(5, Inventory.Move(a, 0, b, null, null));
        Assert.Equal(45, a.Count(Scrap));
        Assert.Equal("There is no room for Scrap.", Inventory.CheckMove(a, 0, b, null, null));
    }

    [Fact]
    public void MovesRespectWhatTheTargetAccepts()
    {
        var pockets = new Inventory(_catalog, 1);
        var tank = new Inventory(_catalog, 1, [Fuel]);
        pockets.Add(Scrap, 5);

        Assert.Equal("That inventory does not take Scrap.", Inventory.CheckMove(pockets, 0, tank, 0, null));
        tank.Add(Fuel, 3);
        Assert.Equal("Swapping would put Scrap where it is not allowed.", Inventory.CheckMove(tank, 0, pockets, 0, null));
    }

    [Theory]
    [InlineData(5, null, null, "from_slot must be 0 to 1 (got 5).")]
    [InlineData(1, null, null, "Slot 1 is empty.")]
    [InlineData(0, 0, null, "An item cannot move onto its own slot.")]
    [InlineData(0, 9, null, "to_slot must be 0 to 1 (got 9).")]
    [InlineData(0, 1, 6, "count must be 1 to 5 (got 6).")]
    [InlineData(0, 1, 0, "count must be 1 to 5 (got 0).")]
    public void BadMovesAreRefused(int fromSlot, int? toSlot, int? count, string error)
    {
        var a = new Inventory(_catalog, 2);
        a.Add(Scrap, 5);

        Assert.Equal(error, Inventory.CheckMove(a, fromSlot, a, toSlot, count));
        Assert.Throws<InvalidOperationException>(() => Inventory.Move(a, fromSlot, a, toSlot, count));
    }

    [Fact]
    public void MovingWithinOneFullInventoryCountsTheFreedSpace()
    {
        var a = new Inventory(_catalog, 2);
        a.SetSlot(0, new ItemStack(Scrap, 50));
        a.SetSlot(1, new ItemStack(Scrap, 20));

        // Without a slot the stack can go back where it came from, merged first into slot 1.
        Assert.Equal(50, Inventory.Move(a, 0, a, null, null));
        Assert.Equal(70, a.Count(Scrap));
    }
}
