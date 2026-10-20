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
        Assert.Equal(HotbarTools.All, bar.Take(5));
        Assert.Equal(HotbarTools.Sledge, bar[0]);
        Assert.All(bar.Skip(5), Assert.Null);
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
        Assert.True(_dispatcher.Dispatch(Player, HotbarActions.SetHotbarSlot, new SetHotbarSlotArgs(1, null)).Ok);
        Assert.True(_dispatcher.Dispatch(Player, HotbarActions.SetHotbarSlot, new SetHotbarSlotArgs(9, "lift")).Ok);
        var bar = Read(Player).Slots;
        Assert.Equal(ItemParts.Block, bar[6]);
        Assert.Null(bar[1]);
        Assert.Equal(HotbarTools.Lift, bar[9]);
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

    [Theory]
    [InlineData(0, null)]
    [InlineData(0, "test:block")]
    [InlineData(0, "lift")]
    public void TheSledgehammerNeverLeavesTheBar(int slot, string? entry) =>
        Assert.Equal("The Sledgehammer always stays on the hotbar; move it to another slot instead.",
            _dispatcher.Dispatch(Player, HotbarActions.SetHotbarSlot, new SetHotbarSlotArgs(slot, entry)).Message);

    [Fact]
    public void TheSledgehammerMovesByCopyingItFirst()
    {
        // A swap as the hotbar does it: the Sledgehammer goes in its new slot first, then the old slot takes the other entry.
        Assert.True(_dispatcher.Dispatch(Player, HotbarActions.SetHotbarSlot, new SetHotbarSlotArgs(7, "sledgehammer")).Ok);
        Assert.True(_dispatcher.Dispatch(Player, HotbarActions.SetHotbarSlot, new SetHotbarSlotArgs(0, null)).Ok);
        var bar = Read(Player).Slots;
        Assert.Null(bar[0]);
        Assert.Equal(HotbarTools.Sledge, bar[7]);
        Assert.False(_dispatcher.Dispatch(Player, HotbarActions.SetHotbarSlot, new SetHotbarSlotArgs(7, null)).Ok);
    }

    [Fact]
    public void RestoringDropsWhatThisWorldNoLongerHas()
    {
        _hotbar.Restore(Player.Peer, ["test:block", "gone:item", null, "lift", "sledgehammer"]);
        var bar = _hotbar.SlotsOf(Player.Peer);
        Assert.Equal(["test:block", null, null, HotbarTools.Lift, HotbarTools.Sledge, null, null, null, null, null], bar);
        _hotbar.Restore(Player.Peer, null);
        Assert.Equal(HotbarActions.Default, _hotbar.SlotsOf(Player.Peer));
    }

    [Theory]
    [InlineData(PartCategory.Block, InventoryTab.Blocks)]
    [InlineData(PartCategory.Station, InventoryTab.Interactive)]
    [InlineData(PartCategory.Light, InventoryTab.Interactive)]
    [InlineData(PartCategory.Weapon, InventoryTab.Tools)]
    [InlineData(PartCategory.Decor, InventoryTab.Parts)]
    [InlineData(PartCategory.Resource, InventoryTab.Parts)]
    [InlineData(PartCategory.Consumable, InventoryTab.Consumable)]
    public void ItemsFallUnderTheirCategorysTab(PartCategory category, InventoryTab tab)
    {
        var item = new PartDefinition { Id = "thing", Name = "Thing", Category = category };
        Assert.Equal(tab, InventoryTabs.Default(category));
        Assert.Equal(tab, InventoryTabs.Of(item));
        Assert.True(InventoryTabs.Shows(tab, item));
        Assert.True(InventoryTabs.Shows(InventoryTab.All, item));
        Assert.False(InventoryTabs.Shows(tab == InventoryTab.Blocks ? InventoryTab.Tools : InventoryTab.Blocks, item));
    }

    [Fact]
    public void AnItemsTabKeyOverridesItsCategory()
    {
        var gasoline = new PartDefinition { Id = "gasoline", Name = "Gasoline", Category = PartCategory.Resource, Tab = InventoryTab.Consumable };
        Assert.Equal(InventoryTab.Consumable, InventoryTabs.Of(gasoline));
        Assert.False(InventoryTabs.Shows(InventoryTab.Parts, gasoline));
    }

    [Fact]
    public void AnOldSavesHammerComesBackAsTheSledgehammer()
    {
        // Worlds saved before the Hammer was taken out still name it; the slot empties and the Sledgehammer, missing from
        // that bar, takes the first empty slot.
        _hotbar.Restore(Player.Peer, ["vanilla:hammer", "lift", "weld_tool", "paint_tool", "test:block", "connect_tool"]);
        Assert.Equal([HotbarTools.Sledge, HotbarTools.Lift, HotbarTools.Weld, HotbarTools.Paint, "test:block", HotbarTools.Connect, null, null, null, null],
            _hotbar.SlotsOf(Player.Peer));
        // A full bar without it gives up its first slot.
        _hotbar.Restore(Player.Peer, Enumerable.Repeat<string?>("test:block", 10).ToList());
        Assert.Equal(HotbarTools.Sledge, _hotbar.SlotsOf(Player.Peer)[0]);
        Assert.Single(_hotbar.SlotsOf(Player.Peer), e => e == HotbarTools.Sledge);
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
