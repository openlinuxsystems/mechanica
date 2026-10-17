using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Items;

public class HotbarTests
{
    private static readonly ActionCaller Player = new(2, ActionSource.Player);
    private readonly ActionDispatcher _dispatcher = new();
    private readonly HotbarActions _hotbar = new(ItemParts.Catalog());

    public HotbarTests() => _hotbar.Register(_dispatcher);

    private HotbarSlots Read(ActionCaller caller) =>
        _dispatcher.Dispatch(caller, HotbarActions.GetHotbar, new NoArgs()).Value!.Value.Deserialize<HotbarSlots>(ActionJson.Options)!;

    [Fact]
    public void ANewPlayerHasTheToolsInKeyOrder()
    {
        var bar = Read(Player).Slots;
        Assert.Equal(10, bar.Count);
        Assert.Equal(HotbarTools.All, bar.Take(6));
        Assert.All(bar.Skip(6), Assert.Null);
        Assert.Null(_hotbar.SavedOf(Player.Peer));
    }

    [Fact]
    public void EmptyingASlotSurvivesTheWire()
    {
        // Null values are left out on the wire, so an empty entry must read back as null.
        var json = JsonSerializer.Serialize(new SetHotbarSlotArgs(3, null), ActionJson.Options);
        Assert.Equal(new SetHotbarSlotArgs(3, null), JsonSerializer.Deserialize<SetHotbarSlotArgs>(json, ActionJson.Options));
    }

    [Fact]
    public void SlotsTakeToolsAndItemsAndEmpty()
    {
        Assert.True(_dispatcher.Dispatch(Player, HotbarActions.SetHotbarSlot, new SetHotbarSlotArgs(6, ItemParts.Block)).Ok);
        Assert.True(_dispatcher.Dispatch(Player, HotbarActions.SetHotbarSlot, new SetHotbarSlotArgs(0, null)).Ok);
        Assert.True(_dispatcher.Dispatch(Player, HotbarActions.SetHotbarSlot, new SetHotbarSlotArgs(9, "hammer")).Ok);
        var bar = Read(Player).Slots;
        Assert.Equal(ItemParts.Block, bar[6]);
        Assert.Null(bar[0]);
        Assert.Equal(HotbarTools.Hammer, bar[9]);
        Assert.Equal(bar, _hotbar.SavedOf(Player.Peer));
        // Each player has their own.
        Assert.Equal(HotbarActions.Default, Read(ActionCaller.HostPlayer).Slots);
    }

    [Theory]
    [InlineData(10, "test:block", "Hotbar slots are 0 to 9.")]
    [InlineData(-1, null, "Hotbar slots are 0 to 9.")]
    [InlineData(0, "test:nothing", "There is no tool or item 'test:nothing'.")]
    public void BadSlotsAreRefused(int slot, string? entry, string message) =>
        Assert.Equal(message, _dispatcher.Dispatch(Player, HotbarActions.SetHotbarSlot, new SetHotbarSlotArgs(slot, entry)).Message);

    [Fact]
    public void OnlyPlayersHaveOne() =>
        Assert.False(_dispatcher.Dispatch(new ActionCaller(2, ActionSource.Bot), HotbarActions.SetHotbarSlot, new SetHotbarSlotArgs(0, null)).Ok);

    [Fact]
    public void RestoringDropsWhatThisWorldNoLongerHas()
    {
        _hotbar.Restore(Player.Peer, ["test:block", "gone:item", null, "lift"]);
        var bar = _hotbar.SlotsOf(Player.Peer);
        Assert.Equal(["test:block", null, null, HotbarTools.Lift, null, null, null, null, null, null], bar);
        _hotbar.Restore(Player.Peer, null);
        Assert.Equal(HotbarActions.Default, _hotbar.SlotsOf(Player.Peer));
    }

    [Theory]
    [InlineData(PartCategory.Block, InventoryTab.Blocks)]
    [InlineData(PartCategory.Station, InventoryTab.Interactive)]
    [InlineData(PartCategory.Light, InventoryTab.Interactive)]
    [InlineData(PartCategory.Weapon, InventoryTab.Tools)]
    [InlineData(PartCategory.Resource, InventoryTab.Resources)]
    [InlineData(PartCategory.Consumable, InventoryTab.Consumables)]
    public void ItemsFallUnderTheirTab(PartCategory category, InventoryTab tab)
    {
        Assert.Equal(tab, InventoryTabs.Of(category));
        Assert.True(InventoryTabs.Shows(tab, category));
        Assert.True(InventoryTabs.Shows(InventoryTab.All, category));
        Assert.False(InventoryTabs.Shows(tab == InventoryTab.Blocks ? InventoryTab.Tools : InventoryTab.Blocks, category));
    }

    [Fact]
    public void ToolsShowUnderTools() => Assert.Equal(InventoryTab.Tools, InventoryTabs.Of(null, tool: true));

    [Fact]
    public void TheWheelWrapsRoundTheBar()
    {
        Assert.Equal(1, InventoryTabs.Step(0, 1));
        Assert.Equal(9, InventoryTabs.Step(0, -1));
        Assert.Equal(0, InventoryTabs.Step(9, 1));
    }

    [Fact]
    public void NearlyFullFollowsTheSetting()
    {
        var settings = new InventorySettings { NearFullSlots = 3 };
        Assert.True(settings.NearlyFull(3));
        Assert.False(settings.NearlyFull(4));
        Assert.Throws<FormatException>(() => InventorySettings.FromJson("""{ "near_full_slots": 30 }"""));
    }
}
