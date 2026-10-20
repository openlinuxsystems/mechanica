using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Game.Items;
using Mechanica.Game.Ui;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The hotbar and the inventory (UI spec PR 8), host plus a client. The client's bar starts with the five tools,
/// the Sledgehammer first and in hand; picking a slot puts its tool in hand, a part slot puts that part in hand to
/// build with, an empty slot empties the hand, and a food slot puts the food in hand. The Sledgehammer can be moved
/// but never taken off the bar. Slots are set and swapped through the host, which keeps the same bar and refuses an unknown
/// item. On the client's own inventory window: tabs filter the grid and the Creative list, a listed part dropped on
/// the bar fills the slot, a bar slot dropped on the inventory empties it, a stack right-clicked picks up half (Shift
/// one) and a click puts it down, a dragged stack moves, a stack's tooltip names it and its stack, and in Survival a
/// nearly full inventory warns. The window fades in and out.
/// </summary>
public partial class HotbarTest : StagedTest
{
    private const string Block = "vanilla:aluminum_block";
    private const string Carrot = "vanilla:carrot";
    private const string Scrap = "vanilla:scrap_wood_block";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private InventoryScreen _screen = null!;
    private HotbarView _bar = null!;
    private GameMode _mode = GameMode.Creative;

    protected override string Tag => "hotbar";

    private Building.ToolBelt Tools => _client.Main.Tools!;

    private HotbarSync Bar => _client.Main.Hotbar;

    private IReadOnlyList<string?> HostBar => _host.Main.Hotbar.Bars.SlotsOf(_client.Peer);

    private InventoryRef Mine => InventoryRef.OfPlayer(_client.Peer);

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins with the five tools on its bar", _ =>
        {
            if (_client.Main.PlayerSync.Players.Count != 2 || _client.Main.Tools is null || Bar.Own.Count != HotbarActions.SlotCount)
                return false;
            Check(Bar.Own.SequenceEqual(HotbarActions.Default), $"the default bar ({string.Join(", ", Bar.Own)})");
            Check(Tools.Selected == 0 && Tools.Current == Tools.Sledge, "slot 1 holds the Sledgehammer, in hand");
            Tools.Select(1);
            Check(Tools.Current == Tools.Lift, "slot 2 puts the Lift in hand");
            Tools.Select(4);
            Check(Tools.Current == Tools.Connector, "slot 5 puts the Connect Tool in hand");
            Tools.Select(5);
            Check(Tools.Current == Tools.Builder && Tools.Builder.Selected is null && Tools.Builder.Prop == Core.Characters.CharacterProp.None,
                $"an empty slot empties the hand, which only removes parts ({Tools.Builder.Selected})");
            return true;
        });
        Add("a part goes in a slot through the host", frame =>
        {
            if (frame == 0)
                Bar.Set(6, Block, _results.Add);
            if (_results.Count == 0)
                return false;
            Check(_results[0].Ok, $"the host takes it ({_results[0].Message})");
            Check(Bar.Own[6] == Block && HostBar[6] == Block, "client and host both have the block in slot 7");
            Tools.Select(6);
            Check(Tools.Current == Tools.Builder && Tools.Builder.Selected == Block, $"slot 7 puts the block in hand ({Tools.Builder.Selected})");
            _results.Clear();
            return true;
        });
        Add("an unknown item is refused", frame =>
        {
            if (frame == 0)
                Bar.Set(7, "nope:nothing", _results.Add);
            if (_results.Count == 0)
                return false;
            Check(!_results[0].Ok, "the host refuses it");
            Check(Bar.Own[7] is null && HostBar[7] is null, "slot 8 stays empty");
            _results.Clear();
            return true;
        });
        Add("the Sledgehammer cannot be taken off the bar", frame =>
        {
            if (frame == 0)
                Bar.Set(0, null, _results.Add);
            if (_results.Count == 0)
                return false;
            Check(!_results[0].Ok, "the host refuses to empty its slot");
            Check(Bar.Own[0] == HotbarTools.Sledge && HostBar[0] == HotbarTools.Sledge, "it stays in slot 1");
            _results.Clear();
            return true;
        });
        Add("two slots swap", frame =>
        {
            if (frame == 0)
                Bar.Swap(0, 6, _results.Add);
            if (_results.Count == 0 || Bar.Own[0] != Block)
                return frame < 300 ? false : Check(false, "the swap arrives");
            Check(Bar.Own[6] == HotbarTools.Sledge && HostBar[0] == Block && HostBar[6] == HotbarTools.Sledge, "client and host agree, the Sledgehammer moved");
            _results.Clear();
            return true;
        });
        Add("a food slot puts the food in hand", frame =>
        {
            if (frame == 0)
            {
                Request(InventoryActions.GiveItems, new GiveItemsArgs(Carrot, 3));
                Request(InventoryActions.GiveItems, new GiveItemsArgs(Scrap, 50));
                Bar.Set(8, Carrot, _results.Add);
            }
            if (_results.Count < 3 || _client.Main.Inventory.Own?.Count(Carrot) != 3 || _client.Main.Inventory.Own?.Count(Scrap) != 50)
                return false;
            Check(_results.All(r => r.Ok), $"all taken ({string.Join("; ", _results.Select(r => r.Message))})");
            Tools.Select(8);
            Check(Tools.Current == Tools.Item && Tools.Item.InHand?.Item == Carrot, $"the carrot is in hand ({Tools.Item.InHand?.Item})");
            Tools.Select(9);
            Check(Tools.Current == Tools.Builder && Tools.Builder.Selected is null, "an empty slot leaves the hand empty");
            _results.Clear();
            return true;
        });
        Add("the bar shows the slots", frame =>
        {
            if (frame == 0)
            {
                var layer = new CanvasLayer { Name = "Ui" };
                AddChild(layer);
                _bar = new HotbarView { Name = "Bar", Belt = Tools };
                layer.AddChild(_bar);
                _screen = new InventoryScreen
                {
                    Name = "InventoryScreen", Inventory = _client.Main.Inventory, Actions = _client.Main.Actions, Players = _client.Main.PlayerSync,
                    Catalog = _client.Main.Build.Catalog, Hotbar = Bar, Mode = () => _mode,
                    AllItems = () => HotbarTools.All.Concat(_client.Main.Build.Catalog.Parts.Where(p => p.Value.Definition is { Category: not null, IsItem: true }).Select(p => p.Key)),
                };
                AddChild(_screen);
                Tools.Select(6);
                return false;
            }
            if (frame < 2)
                return false;
            var size = GetViewport().GetVisibleRect().Size;
            _bar.Layout(size, new Vector2(10, 10));
            Check(_bar.SlotViews.Count == 10, "ten slots");
            Check(Mathf.IsEqualApprox(_bar.SlotSize, Mathf.Clamp(size.Y / 12f, 48f, 72f)) && _bar.SlotSize == HotbarView.SlotSizeFor(size), $"slots sized by the screen ({_bar.SlotSize})");
            Check(_bar.SlotViews[6].Selected && _bar.SlotViews.Count(s => s.Selected) == 1, "only the picked slot has the cyan border");
            Check(_bar.SlotViews[0].Entry == Block && _bar.SlotViews[0].Count is null, "Creative shows no count");
            Check(_bar.SlotViews[6].Initials.Length > 0, $"a tool with no picture shows its initials ({_bar.SlotViews[6].Initials})");
            Check(_bar.SlotViews[9].Entry is null, "slot 10 is empty");
            return true;
        });
        Add("the inventory fades in with tabs and the Creative list", frame =>
        {
            var panel = _screen.GetNode<Control>("Panel");
            if (frame == 0)
            {
                _screen.Show(null);
                Check(_screen.IsOpen && panel.Modulate.A < 0.5f, $"opening starts faded ({panel.Modulate.A})");
                return false;
            }
            if (panel.Modulate.A < 0.99f)
                return frame < 60 ? false : Check(false, "faded in");
            Check(_screen.OwnSlots.Count(s => s.Visible) == _client.Main.Inventory.Settings!.PlayerSlots, "All shows every slot");
            Check(_screen.CatalogSlots.Any(s => s.Entry == Block) && _screen.CatalogSlots.Any(s => s.Entry == HotbarTools.Weld), "Creative lists parts and tools");
            _screen.SetTab(InventoryTab.Tools);
            var listed = _screen.CatalogSlots.Where(s => s.Visible).Select(s => s.Entry).ToList();
            Check(HotbarTools.All.All(listed.Contains) && !listed.Contains(Block), "Tools lists the tools, not blocks");
            _screen.SetTab(InventoryTab.Blocks);
            Check(_screen.OwnSlots.Where(s => s.Visible).Select(s => s.Entry).SequenceEqual([Scrap]), "Blocks shows only the scrap");
            _screen.SetTab(InventoryTab.All);
            return true;
        });
        Add("a listed part dropped on the bar fills the slot", frame =>
        {
            if (frame == 0)
            {
                var drag = new ItemDrag { Entry = Carrot };
                var target = _bar.SlotViews[9];
                Check(target._CanDropData(Vector2.Zero, drag), "the bar takes it");
                target._DropData(Vector2.Zero, drag);
            }
            if (Bar.Own[9] != Carrot)
                return frame < 300 ? false : Check(false, "slot 10 holds the carrot");
            return HostBar[9] == Carrot || Check(false, "the host agrees");
        });
        Add("a bar slot dropped on the inventory empties", frame =>
        {
            if (frame == 0)
            {
                var drag = new ItemDrag { Entry = Carrot, HotbarSlot = 9 };
                var target = _screen.OwnSlots[^1];
                Check(target._CanDropData(Vector2.Zero, drag), "the inventory takes it");
                target._DropData(Vector2.Zero, drag);
            }
            if (Bar.Own[9] is not null)
                return frame < 300 ? false : Check(false, "slot 10 empties");
            return HostBar[9] is null || Check(false, "the host agrees");
        });
        Add("right-click picks up half and a click puts it down", frame =>
        {
            var from = SlotOf(Scrap);
            if (frame == 0)
            {
                Click(_screen.OwnSlots[from], MouseButton.Right);
                Check(_screen.Held is { Count: 25 } held && held.Slot == from, $"half the scrap picked up ({_screen.Held})");
                Check(_screen.OwnSlots[from].Selected, "the picked stack is highlighted");
                Click(_screen.OwnSlots[Empty()], MouseButton.Left);
                return false;
            }
            var stacks = Stacks(Scrap);
            if (stacks.Count < 2)
                return frame < 300 ? false : Check(false, "the scrap splits");
            return Check(stacks.SequenceEqual([25, 25]), $"25 and 25 ({string.Join(", ", stacks)})");
        });
        Add("Shift+right-click picks up one and a drag moves it", frame =>
        {
            if (frame == 0)
            {
                var from = SlotOf(Scrap);
                Click(_screen.OwnSlots[from], MouseButton.Right, shift: true);
                Check(_screen.Held is { Count: 1 }, $"one picked up ({_screen.Held})");
                var to = Empty();
                // A drag from the picked stack carries the one.
                var drag = _screen.OwnSlots[from].DragSource!()!;
                Check(drag.Count == 1 && drag.From == Mine, "the drag carries the one");
                _screen.OwnSlots[to]._DropData(Vector2.Zero, drag);
                Check(_screen.Held is null, "dropping lets go of the picked stack");
                return false;
            }
            var stacks = Stacks(Scrap);
            if (stacks.Count < 3)
                return frame < 300 ? false : Check(false, "one scrap moves");
            return Check(stacks.Order().SequenceEqual([1, 24, 25]), $"1, 24 and 25 ({string.Join(", ", stacks)})");
        });
        Add("a stack's tooltip names it and its stack", _ =>
        {
            var slot = _screen.OwnSlots[SlotOf(Carrot)];
            var (title, body) = slot.Details!()!.Value;
            var name = _client.Main.Build.Catalog.FindDefinition(Carrot)!.Name!;
            Check(title == name, $"titled {name} ({title})");
            Check(body.Contains('3'), $"says how many ({body})");
            return true;
        });
        Add("Survival warns when nearly full", frame =>
        {
            var settings = _client.Main.Inventory.Settings!;
            var free = settings.PlayerSlots - Own.Count;
            if (frame == 0)
            {
                Check(_screen.Warning.Length == 0, "Creative never warns");
                _mode = GameMode.Survival;
                // One each of other items, each in its own slot, until only the warning's few are free.
                var others = _client.Main.Build.Catalog.Parts.Keys.Where(k => Own.All(s => s.Item != k)).Order(StringComparer.Ordinal);
                foreach (var item in others.Take(free - settings.NearFullSlots))
                    Request(InventoryActions.GiveItems, new GiveItemsArgs(item, 1));
                return false;
            }
            if (free > settings.NearFullSlots || _screen.Warning.Length == 0)
                return frame < 300 ? false : Check(false, $"a warning shows ({free} free)");
            Check(_screen.Warning.Contains($"{free}"), $"it says how many are free ({_screen.Warning})");
            _mode = GameMode.Creative;
            return true;
        });
        Add("the inventory fades out", frame =>
        {
            var panel = _screen.GetNode<Control>("Panel");
            if (frame == 0)
            {
                _screen.Hide();
                Check(!_screen.IsOpen && panel.Visible, "closing fades rather than vanishing");
                return false;
            }
            if (panel.Visible)
                return frame < 60 ? false : Check(false, "gone after the fade");
            return true;
        });
    }

    private void Request<TArgs>(string action, TArgs args) => _client.Main.Actions.Request(action, args, _results.Add);

    private IReadOnlyList<SlotView> Own => _client.Main.Inventory.Own?.Items ?? [];

    private int SlotOf(string item) => Own.FirstOrDefault(s => s.Item == item)?.Slot ?? -1;

    private List<int> Stacks(string item) => [.. Own.Where(s => s.Item == item).Select(s => s.Count)];

    private int Empty() => Enumerable.Range(0, _client.Main.Inventory.Settings!.PlayerSlots).First(i => Own.All(s => s.Slot != i));

    // A press and release over the slot, through the GUI.
    private void Click(Control slot, MouseButton button, bool shift = false)
    {
        var at = slot.GetGlobalRect().GetCenter();
        foreach (var pressed in new[] { true, false })
            slot.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, ShiftPressed = shift, Position = at - slot.GlobalPosition, GlobalPosition = at });
    }
}
