using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
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
/// the hotbar; a hotbar slot dragged onto the window empties it, and a stack dragged out of the window with Force
/// Build held drops on the ground. Clicking also moves stacks: click one to pick it up and click a slot to put it
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
    private Control _classic = null!;
    private readonly SlotGrid _ownGrid = new();
    private readonly SlotGrid _otherGrid = new();
    private readonly SlotGrid _catalogGrid = new();
    private Control _catalogPanel = null!;
    private Label _otherTitle = null!;
    private Label _warning = null!;
    private VBoxContainer _recipes = null!;
    private VBoxContainer _queue = null!;
    private Control _otherPanel = null!;
    private Label _status = null!;
    private LineEdit _give = null!;
    private readonly List<Button> _tabButtons = [];
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
    public string Warning => _warning.Visible ? _warning.Text : "";

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
        var columns = new HBoxContainer { Name = "Columns" };
        columns.AddThemeConstantOverride("separation", 24);
        _root.AddChild(columns);
        _classic = columns;

        var own = new VBoxContainer();
        var header = new HBoxContainer();
        header.AddChild(new Label { Text = "Inventory", ThemeTypeVariation = "TitleLabel" });
        _warning = Loc.Verbatim(new Label { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right });
        _warning.AddThemeColorOverride("font_color", UiTokens.Warning);
        header.AddChild(_warning);
        own.AddChild(header);
        var tabs = new HBoxContainer { Name = "Tabs" };
        var group = new ButtonGroup();
        foreach (var tab in TabOrder)
        {
            var button = new Button { Text = TabName(tab), ToggleMode = true, ButtonGroup = group, ButtonPressed = tab == _tab, Name = tab.ToString() };
            button.Pressed += () => SetTab(tab);
            tabs.AddChild(button);
            _tabButtons.Add(button);
        }
        own.AddChild(tabs);
        own.AddChild(_ownGrid.Build("OwnGrid", rows: 4, this));
        _catalogPanel = new VBoxContainer { Name = "Catalog" };
        _catalogPanel.AddChild(new Label { Text = "All parts and tools (drag onto the hotbar)", ThemeTypeVariation = "SecondaryLabel" });
        _catalogPanel.AddChild(_catalogGrid.Build("CatalogGrid", rows: 2, this));
        own.AddChild(_catalogPanel);
        _give = new LineEdit { PlaceholderText = "give: item_id count", Visible = Inventory.Settings!.AllowGive };
        _give.TextSubmitted += OnGive;
        own.AddChild(_give);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(320, 0) };
        own.AddChild(_status);
        columns.AddChild(own);

        var other = new VBoxContainer();
        _otherPanel = other;
        _otherTitle = Loc.Verbatim(new Label { ThemeTypeVariation = "TitleLabel" });
        other.AddChild(_otherTitle);
        other.AddChild(_otherGrid.Build("OtherGrid", rows: 4, this));
        _queue = new VBoxContainer();
        other.AddChild(_queue);
        _recipes = new VBoxContainer();
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(360, 200) };
        scroll.AddChild(_recipes);
        _recipes.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        other.AddChild(scroll);
        columns.AddChild(other);
        BuildStation();

        Inventory.ViewChanged += _ => Refresh();
        Inventory.ViewClosed += OnViewClosed;
        if (Icons is not null)
            Icons.IconReady += _ => Refresh();
    }

    public override void _Process(double delta)
    {
        // The scrim fades with the window and goes when it closes.
        _scrimLayer.Visible = StationMode && _root.Visible;
        _scrim.Modulate = _root.Modulate;
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

    /// <summary>Shows only the items under <paramref name="tab"/>.</summary>
    public void SetTab(InventoryTab tab)
    {
        _tab = tab;
        _held = null;
        for (var i = 0; i < TabOrder.Length; i++)
            _tabButtons[i].SetPressedNoSignal(TabOrder[i] == tab);
        Refresh();
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

    private (CreationNode Node, int Part)? Aimed()
    {
        if (Players.LocalPlayer is not { } player)
            return null;
        var from = player.AimOrigin;
        var query = PhysicsRayQueryParameters3D.Create(from, from - player.Camera.GlobalBasis.Z * Inventory.Settings!.Reach);
        query.Exclude = [player.GetRid()];
        var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not CreationBody body || body.GetParent() is not CreationNode node)
            return null;
        var toGrid = body.GlobalTransform.AffineInverse();
        var (cell, _) = BuildAim.FromHit((toGrid * hit["position"].AsVector3()).ToNumerics(), (toGrid.Basis * hit["normal"].AsVector3()).ToNumerics());
        return node.Creation.PartAt(cell) is { } part ? (node, part.Id) : null;
    }

    private void Refresh()
    {
        if (!IsOpen)
            return;
        _classic.Visible = !StationMode;
        _station.Visible = StationMode;
        if (StationMode)
        {
            RefreshStation();
            return;
        }
        var own = Inventory.Own;
        var me = InventoryRef.OfPlayer(Inventory.Session.LocalPeer);
        var slots = own?.SlotCount ?? Inventory.Settings!.PlayerSlots;
        var shown = _tab == InventoryTab.All
            ? Enumerable.Range(0, slots).Select(i => (i, own?.At(i)))
            : own?.Items.Where(s => Shows(s.Item)).OrderBy(s => s.Slot).Select(s => (s.Slot, (SlotView?)s)) ?? [];
        _ownGrid.Fill(me, [.. shown]);
        var free = slots - (own?.Items.Select(s => s.Slot).Distinct().Count() ?? 0);
        _warning.Visible = !Creative && own is not null && Inventory.Settings!.NearlyFull(free);
        _warning.Text = free == 0 ? Loc.T("Inventory full") : Loc.T("Nearly full: {0} free", free);
        _catalogPanel.Visible = Creative && AllItems is not null;
        if (_catalogPanel.Visible)
            _catalogGrid.FillCatalog([.. AllItems!().Where(Shows).OrderBy(ItemName, StringComparer.CurrentCultureIgnoreCase)]);
        var view = _open is { } where ? Inventory.Shown.GetValueOrDefault(where) : null;
        _otherPanel.Visible = _open is not null;
        if (_open is null)
            return;
        _otherTitle.Text = view is null ? Loc.T("Opening…") : view.Blocked ? Loc.T("{0} (full: take the output)", ItemName(view.Part!)) : ItemName(view.Part!);
        _otherGrid.Fill(_open, [.. Enumerable.Range(0, view?.SlotCount ?? 0).Select(i => (i, view?.At(i)))]);
        FillStation(view);
    }

    private bool Shows(string item) =>
        InventoryTabs.Shows(_tab, Catalog.FindDefinition(item)?.Category, HotbarTools.IsTool(item));

    private static string TabName(InventoryTab tab) => tab switch
    {
        InventoryTab.All => "All",
        InventoryTab.Blocks => "Blocks",
        InventoryTab.Interactive => "Interactive",
        InventoryTab.Tools => "Tools",
        InventoryTab.Resources => "Resources",
        _ => "Consumables",
    };

    private void FillStation(InventoryView? view)
    {
        foreach (var child in _queue.GetChildren().Concat(_recipes.GetChildren()))
            child.QueueFree();
        if (view?.Queue is not { } queue)
            return;
        for (var i = 0; i < queue.Count; i++)
        {
            var job = queue[i];
            var index = i;
            var row = new HBoxContainer();
            var done = job.Time > 0 ? job.Progress / job.Time : 1f;
            row.AddChild(Loc.Verbatim(new Label { Text = $"{RecipeName(job.Recipe)} × {job.Count}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }));
            row.AddChild(new ProgressBar { Value = done * 100, CustomMinimumSize = new Vector2(100, 0), ShowPercentage = false });
            var cancel = new Button { Text = "Cancel" };
            cancel.Pressed += () => Send(InventoryActions.CancelCraft, new CancelCraftArgs(_open!.Creation!.Value, _open.Part!.Value, index));
            row.AddChild(cancel);
            _queue.AddChild(row);
        }
        foreach (var recipe in RecipeBrowser.Ordered(Inventory.Recipes.ForStation(view.Part!)))
        {
            var row = new HBoxContainer();
            var inputs = string.Join(", ", recipe.Inputs.Select(a => $"{a.Count} {ItemName(a.Item)}"));
            var outputs = string.Join(", ", recipe.Outputs.Select(a => $"{a.Count} {ItemName(a.Item)}"));
            row.AddChild(Loc.Verbatim(new Label
            {
                Text = Loc.T("{0}  ←  {1}  ({2} s)", outputs, inputs, $"{recipe.Time / (view.Speed ?? 1f):0.#}"),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            }));
            var count = new SpinBox { MinValue = 1, MaxValue = Inventory.Settings!.MaxCraftCount, Value = 1 };
            row.AddChild(count);
            var craft = new Button { Text = "Craft", Disabled = Inventory.Own is not { } own || !recipe.Inputs.All(a => own.Count(a.Item) >= a.Count) };
            var id = recipe.QualifiedId;
            craft.Pressed += () => Send(InventoryActions.Craft, new CraftArgs(_open!.Creation!.Value, _open.Part!.Value, id, (int)count.Value));
            row.AddChild(craft);
            _recipes.AddChild(row);
        }
    }

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

    // A stack of the player's own dragged out of the window with Force Build held drops on the ground.
    private void OnDragEnded(ItemDrag drag, bool taken)
    {
        if (taken || drag.From is not { IsPlayer: true } || !Input.IsActionPressed(InputActions.ForceBuild) || GetViewport().GuiGetHoveredControl() is not null)
            return;
        Send(SurvivalActions.DropItems, new DropItemsArgs(drag.FromSlot, drag.Count));
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
            }
        }

        private ItemSlot Make(int index)
        {
            var view = new ItemSlot { Name = $"Slot{index}", CustomMinimumSize = new Vector2(size, size), Tiled = tiled };
            view.DragSource = () => index < _shown.Count && _shown[index].Item is { } item
                ? new ItemDrag { Entry = item, From = _where, FromSlot = _shown[index].Slot, Count = _where is null ? null : HeldCount(index) }
                : null;
            view.CanDrop = drag => _where is not null && index < _shown.Count && (drag.From is not null || drag.HotbarSlot is not null || _where.IsPlayer);
            view.Dropped = drag => _screen.Drop(drag, _where!, _shown[index].Slot);
            view.DragEnded = taken =>
            {
                if (view.Entry is { } item && _where is { } w)
                    _screen.OnDragEnded(new ItemDrag { Entry = item, From = w, FromSlot = _shown[index].Slot }, taken);
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
