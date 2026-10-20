using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Sound;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Items;

/// <summary>
/// The local player's inventory screen (UI spec §3, §8, §9). Inventory (<c>I</c> or <c>B</c>) shows the player's own
/// inventory; Use (<c>E</c>) on a container or station opens it beside the player's (through
/// <see cref="Survival.SurvivalHud"/>). The grid is six columns and scrolls; tabs along the top (All, Blocks,
/// Interactive, Tools, Resources, Consumables) filter it. Stacks drag between slots, onto the other side and onto
/// the hotbar; a hotbar slot dragged onto the window empties it, and a stack dragged out of the window and let go
/// over the world is tossed on the ground (<see cref="Toss"/>). Clicking also moves stacks: click one to pick it up and click a slot to put it
/// there; right-click picks up half, Shift+right-click one, and Shift+click sends a stack straight to the other
/// side. In Creative every part and tool is listed under the grid to drag onto the hotbar or into the inventory,
/// and a give box makes items (<c>scrap_metal 50</c>). In Survival a warning shows when few slots are free. Hovering
/// a stack shows its name, description, stack and durability. A crafting station opens the Fabricator layout instead
/// (InventoryScreen.Station.cs): recipes, preview and queue, and storage side by side. The window fades in and out
/// (0.15 s). Every change goes through the Action API.
/// </summary>
public partial class InventoryScreen : CanvasLayer
{
    private const int Columns = 6;

    private static readonly InventoryTab[] TabOrder = Enum.GetValues<InventoryTab>();

    private Control _root = null!;
    private Label _status = null!;
    private InventoryRef? _open;
    private (InventoryRef Where, int Slot, int? Count)? _held;
    private InventoryTab _tab;
    private bool _shown;
    private Tween? _fade;

    /// <summary>Inventory sync. Set before adding to the tree.</summary>
    public InventorySync Inventory { get; init; } = null!;

    /// <summary>Where requests go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Players, for the local camera. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Item names. Set before adding to the tree.</summary>
    public IPartCatalog Catalog { get; init; } = null!;

    /// <summary>Every item there is, by qualified id, for the Creative list; null lists none.</summary>
    public Func<IEnumerable<string>>? AllItems { get; init; }

    /// <summary>The game mode now, for the Creative list and the nearly-full warning; null is Survival.</summary>
    public Func<GameMode?>? Mode { get; init; }

    /// <summary>The hotbar, for stacks dropped on it and its slots dropped here; null for none.</summary>
    public HotbarSync? Hotbar { get; init; }

    /// <summary>Item pictures; null shows initials.</summary>
    public ItemIcons? Icons { get; init; }

    /// <summary>Where the window's open and close sounds play; null plays none.</summary>
    public SoundSync? Sounds { get; init; }

    /// <summary>The Creations this peer shows, to tell a station from a container before its view arrives; null waits for the view.</summary>
    public CreationWorld? Creations { get; init; }

    /// <summary>The upgrade dialog the Fabricator screen opens; null hides its Upgrade button.</summary>
    public UpgradeDialog? Upgrades { get; init; }

    /// <summary>True while the screen is up (from the moment it starts fading in until it starts fading out).</summary>
    public bool IsOpen => _shown;

    /// <summary>The tab shown.</summary>
    public InventoryTab Tab => _tab;

    /// <summary>The player's own slots as shown: one per slot under All, only the matching stacks under another tab.</summary>
    public IReadOnlyList<ItemSlot> OwnSlots => _ownGrid.Slots;

    /// <summary>The Creative list of every part and tool, empty in Survival.</summary>
    public IReadOnlyList<ItemSlot> CatalogSlots => _catalogPanel.Visible ? _catalogGrid.Slots : [];

    /// <summary>The nearly-full warning, or empty when there is none.</summary>
    public string Warning => _warningPill.Visible ? _warning.Text : "";

    /// <summary>The stack picked up by clicking, waiting for a click on where it goes.</summary>
    public (InventoryRef Where, int Slot, int? Count)? Held => _held;

    private bool Creative => Mode?.Invoke() == GameMode.Creative;

    public override void _Ready()
    {
        Layer = 5;
        EscapeStack.Add(this, () => IsOpen, Hide);
        _root = UiInput.KeepMouse(new PanelContainer { Name = "Panel", Visible = false });
        AddChild(_root);
        // A hotbar slot dropped anywhere on the window empties; a stack dropped on the window's background stays put.
        _root.SetDragForwarding(new Callable(), Callable.From((Vector2 _, Variant data) => data.Obj is ItemDrag { HotbarSlot: not null }), Callable.From((Vector2 _, Variant data) => DropOnWindow(data)));
        _root.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        BuildInventory();
        BuildStation();
        BuildContainer();

        Inventory.ViewChanged += _ => Refresh();
        Inventory.ViewClosed += OnViewClosed;
        if (Icons is not null)
            Icons.IconReady += _ => Refresh();
    }

    public override void _Process(double delta)
    {
        // The scrim fades with the window and goes when it closes.
        _scrimLayer.Visible = _root.Visible;
        _scrim.Modulate = _root.Modulate;
        // Under a container's slots the error line takes room only while it says something.
        _status.Visible = !ContainerMode || _status.Text.Length > 0;
        if (_root.Visible)
            Layout();
    }

    // Centred in the screen above the hotbar, so stacks can be dragged onto it.
    private void Layout()
    {
        var screen = GetViewport().GetVisibleRect().Size;
        var margin = HudRoot.Margin(GetViewport());
        var bar = HotbarView.SlotSizeFor(screen) + 8;
        if (StationMode)
            SizeStation(screen, margin, bar);
        else if (ContainerMode)
            SizeContainer(screen);
        else
            SizeInventory(screen, margin, bar);
        _root.Size = _root.GetCombinedMinimumSize();
        var room = screen.Y - margin.Y - bar;
        _root.Position = new Vector2(Mathf.Round((screen.X - _root.Size.X) / 2), Mathf.Round(Mathf.Max(margin.Y, (room - _root.Size.Y) / 2)));
    }

    public override void _Input(InputEvent @event)
    {
        // Tab (the inventory key's default alternate) would otherwise move focus between the screen's controls before
        // the screen heard it, so an open screen handles its key here, ahead of the controls. At a station it opens and
        // closes the Storage drawer; Esc, E and X close the screen.
        if (IsOpen && GameInput.Pressed(@event, InputActions.Inventory) && !GameInput.Typing(GetViewport()))
        {
            if (StationMode)
                ToggleStorage();
            else
                Hide();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        var toggle = GameInput.Pressed(@event, InputActions.Inventory) && (IsOpen || Input.MouseMode == Input.MouseModeEnum.Captured);
        if (IsOpen && StationMode && GameInput.Pressed(@event, InputActions.Upgrade) && !GameInput.Typing(GetViewport()))
        {
            OpenUpgrade();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (GameInput.Typing(GetViewport()))
            return;
        if (toggle || (IsOpen && GameInput.Pressed(@event, InputActions.Interact)))
        {
            if (IsOpen)
                Hide();
            else
                Show(null);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Shows the screen, with a container or station beside the player's inventory.</summary>
    public void Show(InventoryRef? where)
    {
        if (_open is { } previous && previous != where)
        {
            Inventory.Close(previous);
            CloseLinks();
        }
        _open = where;
        if (where is not null)
            Inventory.Open(where);
        StartStation(where);
        _held = null;
        _status.Text = "";
        if (!_shown)
        {
            Sounds?.PlayUi("ui_open");
            Fade(true);
        }
        if (!_shown)
            EscapeStack.Raise(this);
        _shown = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
    }

    /// <summary>Hides the screen and stops watching the open container.</summary>
    public new void Hide()
    {
        if (_open is { } where)
            Inventory.Close(where);
        CloseLinks();
        _open = null;
        _held = null;
        if (_shown)
        {
            Sounds?.PlayUi("ui_close");
            Fade(false);
        }
        _shown = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // Fades the window in or out. While it fades out it still keeps the mouse, so a click over it never reaches the game.
    private void Fade(bool open)
    {
        _fade?.Kill();
        var seconds = HudRoot.Settings.WindowFadeSeconds;
        if (open)
        {
            _root.Visible = true;
            _root.Modulate = Colors.White with { A = seconds > 0 ? 0 : 1 };
        }
        if (seconds <= 0)
        {
            _root.Visible = open;
            return;
        }
        _fade = CreateTween();
        _fade.TweenProperty(_root, "modulate:a", open ? 1f : 0f, seconds);
        if (!open)
            _fade.TweenCallback(Callable.From(() => _root.Visible = false));
    }

    /// <summary>The container or station part under the crosshair, within reach; Use (E) on it opens it.</summary>
    public InventoryRef? AimedInventory() =>
        Aimed() is var (node, part) && node.Creation.DefinitionOf(part).Inventory is not null ? InventoryRef.OfPart(node.Creation.Id, part) : null;

    /// <summary>Any part under the crosshair, within reach (Upgrade, U, works on it), or null.</summary>
    public (int Creation, int Part)? AimedPart() => Aimed() is var (node, part) ? (node.Creation.Id, part) : null;

    private (CreationNode Node, int Part)? Aimed() =>
        Players.LocalPlayer is { } player ? HandTool.PartHit(HandTool.CastShown(player, Inventory.Settings!.Reach)) : null;

    private void Refresh()
    {
        if (!IsOpen)
            return;
        _classic.Visible = _open is null;
        _station.Visible = StationMode;
        _containerScreen.Visible = ContainerMode;
        if (StationMode)
        {
            RefreshStation();
            return;
        }
        if (ContainerMode && Inventory.Shown.GetValueOrDefault(_open!)?.Queue is not null)
        {
            // A station after all: over to the Fabricator layout.
            StartStation(_open);
            Refresh();
            return;
        }
        if (ContainerMode)
        {
            RefreshContainer();
            return;
        }
        RefreshInventory();
    }

    private bool Shows(string item) =>
        InventoryTabs.Shows(_tab, Catalog.FindDefinition(item), HotbarTools.IsTool(item));

    private static string TabName(InventoryTab tab) => tab switch
    {
        InventoryTab.All => "All",
        InventoryTab.Tools => "Tools",
        InventoryTab.Blocks => "Blocks",
        InventoryTab.Interactive => "Interactive",
        InventoryTab.Parts => "Parts",
        _ => "Consumable",
    };

    // Clicks: left picks up or puts down (on release, so a press that becomes a drag does neither), right picks up
    // half and Shift+right one; Shift+left sends the stack to the other side.
    private void OnSlotInput(InputEvent e, InventoryRef where, int slot, SlotView? stack)
    {
        if (e is not InputEventMouseButton click || click.ButtonIndex is not (MouseButton.Left or MouseButton.Right))
            return;
        if (click.ButtonIndex == MouseButton.Left ? click.Pressed : !click.Pressed)
            return;
        if (click.ShiftPressed && click.ButtonIndex == MouseButton.Left && stack is not null)
        {
            var other = where.IsPlayer ? ShiftTarget() : InventoryRef.OfPlayer(Inventory.Session.LocalPeer);
            if (other is not null)
                Send(InventoryActions.MoveItems, new MoveItemsArgs(where, slot, other));
            return;
        }
        if (_held is { } held)
        {
            _held = null;
            if (held.Where != where || held.Slot != slot)
                Send(InventoryActions.MoveItems, new MoveItemsArgs(held.Where, held.Slot, where, slot, held.Count));
            Refresh();
            return;
        }
        if (stack is null)
            return;
        int? count = click.ButtonIndex != MouseButton.Right ? null : click.ShiftPressed ? 1 : Math.Max(1, stack.Count / 2);
        _held = (where, slot, count);
        Refresh();
    }

    /// <summary>
    /// Drops <paramref name="drag"/> on <paramref name="slot"/> of <paramref name="where"/>: a stack moves there, a
    /// hotbar slot empties, and in Creative a listed item fills the slot with a stack.
    /// </summary>
    public void Drop(ItemDrag drag, InventoryRef where, int slot)
    {
        _held = null;
        if (drag.From is { } from)
        {
            if (from != where || drag.FromSlot != slot)
                Send(InventoryActions.MoveItems, new MoveItemsArgs(from, drag.FromSlot, where, slot, drag.Count));
        }
        else if (drag.HotbarSlot is { } bar)
        {
            Hotbar?.Set(bar, null);
        }
        else if (where.IsPlayer && Inventory.Settings!.AllowGive && !HotbarTools.IsTool(drag.Entry))
        {
            Send(InventoryActions.GiveItems, new GiveItemsArgs(drag.Entry, Math.Max(1, Catalog.FindDefinition(drag.Entry)?.StackSize ?? 1)));
        }
        Refresh();
    }

    private void DropOnWindow(Variant data)
    {
        if (data.Obj is ItemDrag { HotbarSlot: { } bar })
            Hotbar?.Set(bar, null);
    }

    // A stack dragged out of the window and let go over the world is tossed on the ground in front of the player.
    private void OnDragEnded(ItemDrag drag, bool taken)
    {
        if (!taken && GetViewport().GuiGetHoveredControl() is null)
            Toss(drag);
    }

    /// <summary>
    /// Tosses a dragged stack (or the part of it picked up) on the ground in front of the player, as Scrap Mechanic does
    /// with a stack dragged out of an item window: from the inventory, a container or a station's finished items. The
    /// host lands it <c>toss_distance</c> ahead as a pile anyone can pick up until it despawns. False for a drag that
    /// is not a stack (the Creative list, the hotbar).
    /// </summary>
    public bool Toss(ItemDrag drag)
    {
        if (drag.From is not { } from)
            return false;
        _held = null;
        Send(SurvivalActions.DropItems, new DropItemsArgs(drag.FromSlot, drag.Count, From: from.IsPlayer ? null : from));
        Refresh();
        return true;
    }

    private (string Title, string Body)? DetailsOf(string item, int? count) => ItemInfo.Details(Catalog, item, count);

    private void OnGive(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 1 or > 2 || !(words.Length == 1 || int.TryParse(words[1], out _)))
        {
            _status.Text = "Type an item id and a count, like scrap_metal 50.";
            return;
        }
        Send(InventoryActions.GiveItems, new GiveItemsArgs(words[0], words.Length == 2 ? int.Parse(words[1]) : 1));
        _give.Clear();
    }

    private void OnViewClosed(InventoryRef where, string reason)
    {
        if (where != _open)
        {
            LinkClosed(where);
            return;
        }
        _open = null;
        _status.Text = reason;
        Refresh();
    }

    private void Send<TArgs>(string action, TArgs args) =>
        Actions.Request(action, args, result => _status.Text = result.Ok ? "" : result.Message ?? "");

    private string ItemName(string id) => Catalog.FindDefinition(id)?.Name ?? id;

    private string RecipeName(string id) => Inventory.Recipes.Find(id)?.Name ?? id;

    // One grid of slots, kept between refreshes so a drag in progress keeps its source and the scroll stays put.
    private sealed class SlotGrid(int columns = Columns, int size = 60, bool tiled = false)
    {
        private readonly List<ItemSlot> _slots = [];
        private readonly List<(int Slot, SlotView? Stack, string? Item)> _shown = [];
        private GridContainer _grid = null!;
        private InventoryScreen _screen = null!;
        private InventoryRef? _where;

        public IReadOnlyList<ItemSlot> Slots => _slots;

        /// <summary>The silhouette empty slots show (the one item a container takes), or null for none.</summary>
        public Texture2D? Ghost
        {
            get => _ghost;
            set
            {
                if (_ghost == value)
                    return;
                _ghost = value;
                Show();
            }
        }

        private Texture2D? _ghost;

        /// <summary>The grid itself, for changing its column count.</summary>
        public GridContainer Grid => _grid;

        // A grid of rows × columns slots in its own scroll area, or with scroll false just the grid (it grows).
        public Control Build(string name, int rows, InventoryScreen screen, bool scroll = true)
        {
            _screen = screen;
            var gap = tiled ? 8 : 4;
            _grid = new GridContainer { Name = name, Columns = columns };
            _grid.AddThemeConstantOverride("h_separation", gap);
            _grid.AddThemeConstantOverride("v_separation", gap);
            if (!scroll)
                return _grid;
            var area = new ScrollContainer { Name = name + "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(columns * size + (columns - 1) * 4 + 12, rows * size + (rows - 1) * 4) };
            area.AddChild(_grid);
            return area;
        }

        public void Fill(InventoryRef where, IReadOnlyList<(int Slot, SlotView? Stack)> shown)
        {
            _where = where;
            _shown.Clear();
            _shown.AddRange(shown.Select(s => (s.Slot, s.Stack, s.Stack?.Item)));
            Show();
        }

        public void FillCatalog(IReadOnlyList<string> items)
        {
            _where = null;
            _shown.Clear();
            _shown.AddRange(items.Select(i => (-1, (SlotView?)null, (string?)i)));
            Show();
        }

        private void Show()
        {
            while (_slots.Count < _shown.Count)
                _slots.Add(Make(_slots.Count));
            for (var i = 0; i < _slots.Count; i++)
            {
                _slots[i].Visible = i < _shown.Count;
                if (i >= _shown.Count)
                    continue;
                var (slot, stack, item) = _shown[i];
                var held = _where is { } w && _screen._held is { } h && h.Where == w && h.Slot == slot;
                _slots[i].Show(item, item is null ? null : _screen.Icons?.For(item), item is null ? "" : _screen.ItemName(item), stack?.Count, selected: held);
                _slots[i].Dot = item is null ? null : _screen.DotOf(item);
                _slots[i].Ghost = _where is null ? null : _ghost;
            }
        }

        private ItemSlot Make(int index)
        {
            var view = new ItemSlot { Name = $"Slot{index}", CustomMinimumSize = new Vector2(size, size), Tiled = tiled };
            // The drag under way, so letting go over the world tosses what it carries (the half picked up, say).
            ItemDrag? dragged = null;
            view.DragSource = () => dragged = index < _shown.Count && _shown[index].Item is { } item
                ? new ItemDrag { Entry = item, From = _where, FromSlot = _shown[index].Slot, Count = _where is null ? null : HeldCount(index) }
                : null;
            view.CanDrop = drag => _where is not null && index < _shown.Count && (drag.From is not null || drag.HotbarSlot is not null || _where.IsPlayer);
            view.Dropped = drag => _screen.Drop(drag, _where!, _shown[index].Slot);
            view.DragEnded = taken =>
            {
                if (dragged is { } drag)
                    _screen.OnDragEnded(drag, taken);
                dragged = null;
            };
            view.Details = () => index < _shown.Count && _shown[index].Item is { } item ? _screen.DetailsOf(item, _shown[index].Stack?.Count) : null;
            view.GuiInput += e =>
            {
                if (_where is { } w && index < _shown.Count)
                    _screen.OnSlotInput(e, w, _shown[index].Slot, _shown[index].Stack);
            };
            _grid.AddChild(view);
            return view;
        }

        // A drag carries the half or one picked up by right-clicking this stack, else the whole stack.
        private int? HeldCount(int index) =>
            _screen._held is { } h && h.Where == _where && h.Slot == _shown[index].Slot ? h.Count : null;
    }
}
