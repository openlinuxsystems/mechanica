using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Items;
using Mechanica.Core.Ui;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Items;

/// <summary>
/// The Fabricator screen (UI spec §0, §4, §8): what Use (E) on any crafting station opens, over a scrim with the hotbar
/// left clear. A main window holds a chrome header (the station's picture, name, level pill, Upgrade, the linked storage
/// chip, the Storage toggle and X), category tabs with a search field, the recipe grid of picture tiles (dimmed with a
/// red dot when the inputs are short, "×n" for several, "NEW" for recipes added since this kind of station was last
/// opened) and the queue strip: finished stacks to take, the order in progress filling with cyan, waiting orders and
/// empty cells up to the queue size. The detail card on the right shows the chosen recipe's picture, stats, description,
/// materials with have / need (inventory plus linked storage), a reason whenever Craft is off, the quantity stepper and
/// Craft. The Storage drawer slides over the grid with every linked container and the player's inventory. The queue
/// strip flashes when a craft finishes.
/// </summary>
public partial class InventoryScreen
{
    private const int TileSize = 76;
    private const int TileGap = 8;
    private const int DrawerSlot = 64;
    private const int DrawerColumns = 6;
    private const float CardWidth = 302, NarrowCardWidth = 280, PanelGap = 12;
    private const float FlashSeconds = 0.45f;
    private const int CellOverlap = 6;
    private const float QueuePadding = 16;
    private const string SeenPath = "user://seen_recipes.json";

    private Control _station = null!;
    private CanvasLayer _scrimLayer = null!;
    private ColorRect _scrim = null!;
    private Control _main = null!;
    private Control _card = null!;
    private TextureRect _art = null!;
    private Label _stationTitle = null!;
    private Label _stationLevel = null!;
    private Control _levelPill = null!;
    private Control _fullPill = null!;
    private KeyButton _upgrade = null!;
    private Label _linkDot = null!;
    private Label _linkChip = null!;
    private KeyButton _storageButton = null!;
    private LineEdit _search = null!;
    private Label _matches = null!;
    private readonly List<Button> _filterButtons = [];
    private ScrollContainer _gridScroll = null!;
    private GridContainer _recipeGrid = null!;
    private readonly Dictionary<string, RecipeTile> _recipeTiles = [];
    private string _recipeKey = "";
    private Control _drawer = null!;
    private VBoxContainer _storage = null!;
    private readonly SlotGrid _stationOwnGrid = new(DrawerColumns, DrawerSlot, tiled: true);
    private readonly List<InventoryRef> _linked = [];
    private readonly Dictionary<InventoryRef, (Label Title, Label Fill, SlotGrid Grid, Control Box)> _linkedGrids = [];
    private Label _ownFill = null!;
    private Control _queueBand = null!;
    private Label _capacity = null!;
    private ScrollContainer _queueScroll = null!;
    private HBoxContainer _queueRow = null!;
    private Button _queueLeft = null!;
    private Button _queueRight = null!;
    private readonly List<ReadyCell> _readyCells = [];
    private readonly List<JobCell> _jobCells = [];
    private readonly List<QueueCell> _emptyCells = [];
    private TextureRect _hero = null!;
    private Label _heroInitials = null!;
    private Label _previewName = null!;
    private Label _previewSubtitle = null!;
    private Control _stats = null!;
    private GridContainer _statRows = null!;
    private Label _previewDescription = null!;
    private Label _previewTime = null!;
    private HFlowContainer _previewInputs = null!;
    private readonly List<(Label Count, string Name, bool Missing)> _materialRows = [];
    private string _previewKey = "";
    private Label _reason = null!;
    private QuantityStepper _quantity = null!;
    private Button _craft = null!;
    private InventoryTab _filter;
    private string? _chosen;
    private bool _stationMode;
    private bool _storageOpen;
    private int _remainingCrafts = -1;
    private Tween? _flash;
    private Control? _flashed;
    private SeenRecipes? _seen;
    private IReadOnlySet<string> _newRecipes = new HashSet<string>();
    private InventoryRef? _seenFor;

    /// <summary>True while the screen shows a station (the Fabricator layout).</summary>
    public bool StationMode => _stationMode && _open is not null;

    /// <summary>The recipes listed in the grid, by qualified id, in order.</summary>
    public IReadOnlyList<string> ListedRecipes => [.. _recipeGrid.GetChildren().OfType<RecipeTile>().Where(t => t.Visible).Select(t => t.Recipe)];

    /// <summary>The listed recipes with whether the player can make them, and whether each is tagged NEW.</summary>
    public IReadOnlyList<(string Recipe, bool CanMake, bool IsNew)> RecipeRows =>
        [.. _recipeGrid.GetChildren().OfType<RecipeTile>().Where(t => t.Visible).Select(t => (t.Recipe, !t.Short, t.IsNew))];

    /// <summary>The recipe chosen for the detail card, or null.</summary>
    public string? ChosenRecipe => _chosen;

    /// <summary>The detail card's materials (<c>2 / 3 Scrap Metal</c>) and whether each is short.</summary>
    public IReadOnlyList<(string Text, bool Missing)> PreviewInputs => [.. _materialRows.Select(m => ($"{m.Count.Text} {m.Name}", m.Missing))];

    /// <summary>The detail card's stat rows as shown: name, filled pips and value.</summary>
    public IReadOnlyList<StatRow> StatRowsShown =>
        !_stats.Visible ? [] : [.. _statRows.GetChildren().OfType<PipBar>().Select(p => new StatRow((string)p.GetMeta("stat"), p.Filled, ((Label)p.GetMeta("value").AsGodotObject()).Text))];

    /// <summary>Why Craft is off, or empty when it is on.</summary>
    public string Reason => _reason.Visible ? _reason.Text : "";

    /// <summary>The Craft button.</summary>
    public Button CraftButton => _craft;

    /// <summary>The order cells of the queue strip as shown: name, percent done and the time-left text.</summary>
    public IReadOnlyList<(string Name, double Percent, string Left)> QueueRows =>
        [.. _jobCells.Where(c => c.Visible).Select(c => (c.Order, Math.Round(c.Progress * 100), c.Note.Text))];

    /// <summary>Cells in the queue strip: finished stacks, orders and empty cells.</summary>
    public int QueueCells => _readyCells.Count(c => c.Visible) + _jobCells.Count(c => c.Visible) + _emptyCells.Count(c => c.Visible);

    /// <summary>The containers linked to the station, as the Storage drawer shows them.</summary>
    public IReadOnlyList<InventoryRef> LinkedShown => _linked;

    /// <summary>The finished stacks waiting in the queue strip (the station's own output slots that hold something).</summary>
    public IReadOnlyList<ItemSlot> OutputSlots => [.. _readyCells.Where(c => c.Visible).Select(c => c.Slot)];

    /// <summary>The slots of a linked container in the Storage drawer.</summary>
    public IReadOnlyList<ItemSlot> LinkedSlots(InventoryRef where) => _linkedGrids.TryGetValue(where, out var shown) ? shown.Grid.Slots : [];

    /// <summary>The player's slots in the Storage drawer.</summary>
    public IReadOnlyList<ItemSlot> StationOwnSlots => _stationOwnGrid.Slots;

    /// <summary>The station's level pill, empty when it has no upgrades.</summary>
    public string StationLevel => _levelPill.Visible ? _stationLevel.Text : "";

    /// <summary>True for a moment after a craft finishes (the queue strip flashes).</summary>
    public bool Flashing => _flash?.IsRunning() == true;

    /// <summary>True while the Storage drawer is open.</summary>
    public bool StorageOpen => _storageOpen;

    /// <summary>The recipes tagged NEW.</summary>
    public IReadOnlySet<string> NewRecipes => _newRecipes;

    /// <summary>The category tabs shown (those with recipes at this station).</summary>
    public IReadOnlyList<InventoryTab> TabsShown => [.. TabOrder.Where((_, i) => _filterButtons[i].Visible)];

    /// <summary>The main window and the detail card.</summary>
    public IReadOnlyList<Control> StationPanels => [_main, _card];

    /// <summary>Lists only recipes whose name or outputs contain every word of <paramref name="text"/>, across every tab.</summary>
    public void Search(string text)
    {
        _search.Text = text;
        Searched(text);
    }

    /// <summary>Lists only recipes whose main output is under <paramref name="tab"/>.</summary>
    public void Filter(InventoryTab tab)
    {
        _filter = tab;
        for (var i = 0; i < TabOrder.Length; i++)
            _filterButtons[i].SetPressedNoSignal(TabOrder[i] == tab);
        RefreshStation();
    }

    /// <summary>Shows <paramref name="recipe"/> (a qualified id) in the detail card.</summary>
    public void Choose(string recipe)
    {
        _chosen = recipe;
        RefreshStation();
    }

    /// <summary>Sets how many crafts Craft queues.</summary>
    public void SetQuantity(int count) => _quantity.Value = count;

    /// <summary>Opens or closes the Storage drawer.</summary>
    public void ToggleStorage() => SetStorage(!_storageOpen);

    /// <summary>Queues the chosen recipe at the station. False when nothing is chosen.</summary>
    public bool Craft(Action<Core.Actions.ActionResult>? done = null)
    {
        if (_chosen is null || _open is not { Creation: { } creation, Part: { } part })
            return false;
        Actions.Request(InventoryActions.Craft, new CraftArgs(creation, part, _chosen, _quantity.Value), result =>
        {
            _status.Text = result.Ok ? "" : result.Message ?? "";
            done?.Invoke(result);
        });
        return true;
    }

    private void BuildStation()
    {
        // The scrim dims the world under the window but not the hotbar, which sits on the HUD layer above it.
        _scrimLayer = new CanvasLayer { Name = "Scrim", Layer = 0, Visible = false };
        AddChild(_scrimLayer);
        _scrim = new ColorRect { Name = "Scrim", Color = UiTokens.Scrim, MouseFilter = Control.MouseFilterEnum.Ignore };
        _scrim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _scrimLayer.AddChild(_scrim);

        var station = new HBoxContainer { Name = "Station", Visible = false, Theme = StationTheme.Theme };
        station.AddThemeConstantOverride("separation", (int)PanelGap);
        _root.AddChild(station);
        _station = station;

        var main = new PanelContainer { Name = "Main", ThemeTypeVariation = "MainWindow", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipChildren = CanvasItem.ClipChildrenMode.AndDraw };
        station.AddChild(main);
        _main = main;
        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 0);
        main.AddChild(column);
        column.AddChild(BuildHeader());
        column.AddChild(BuildTabs());
        column.AddChild(BuildBody());
        column.AddChild(BuildQueue());
        _card = BuildCard();
        station.AddChild(_card);
    }

    private Control BuildHeader()
    {
        var header = new PanelContainer { Name = "Header", ThemeTypeVariation = "Header" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        header.AddChild(row);
        _art = new TextureRect
        {
            Name = "Art", CustomMinimumSize = new Vector2(104, 72), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        row.AddChild(_art);
        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        titles.AddThemeConstantOverride("separation", 6);
        _stationTitle = Loc.Verbatim(new Label { Name = "StationTitle", ThemeTypeVariation = "DisplayLabel", Uppercase = true });
        titles.AddChild(_stationTitle);
        var under = new HBoxContainer();
        under.AddThemeConstantOverride("separation", 10);
        _levelPill = new PanelContainer { Name = "LevelPill", ThemeTypeVariation = "Pill", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _stationLevel = Loc.Verbatim(new Label { Name = "StationLevel", ThemeTypeVariation = "PillLabel", Uppercase = true });
        _levelPill.AddChild(_stationLevel);
        under.AddChild(_levelPill);
        _upgrade = new KeyButton { Name = "Upgrade", Label = "Upgrade", TooltipText = "Spend Component Kits to make the station faster and bigger" };
        _upgrade.Pressed += OpenUpgrade;
        under.AddChild(_upgrade);
        _fullPill = new PanelContainer { Name = "OutputFull", ThemeTypeVariation = "WarningPill", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Visible = false };
        _fullPill.AddChild(new Label { Text = "Output full: take items", ThemeTypeVariation = "PillLabel" });
        under.AddChild(_fullPill);
        titles.AddChild(under);
        row.AddChild(titles);
        var actions = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        actions.AddThemeConstantOverride("separation", 8);
        _linkDot = new Label { Name = "LinkDot", Text = "●", ThemeTypeVariation = "SuccessTextLabel", VerticalAlignment = VerticalAlignment.Center };
        actions.AddChild(_linkDot);
        _linkChip = Loc.Verbatim(new Label { Name = "LinkChip", ThemeTypeVariation = "MutedLabel", VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Stop });
        actions.AddChild(_linkChip);
        _storageButton = new KeyButton { Name = "Storage", Label = "Storage", ToggleMode = true, TooltipText = "Show linked storage and your inventory" };
        _storageButton.Toggled += SetStorage;
        actions.AddChild(_storageButton);
        var close = new Button { Name = "Close", Text = "✕", ThemeTypeVariation = "CloseButton", CustomMinimumSize = new Vector2(32, 32), TooltipText = "Close" };
        close.Pressed += Hide;
        actions.AddChild(close);
        row.AddChild(actions);
        return header;
    }

    private Control BuildTabs()
    {
        var strip = new PanelContainer { Name = "Tabs", ThemeTypeVariation = "TabStrip" };
        var row = new HBoxContainer { Name = "Filters" };
        row.AddThemeConstantOverride("separation", 4);
        strip.AddChild(row);
        var group = new ButtonGroup();
        foreach (var tab in TabOrder)
        {
            var button = new Button
            {
                Name = tab.ToString(), Text = TabName(tab), ToggleMode = true, ButtonGroup = group, ButtonPressed = tab == _filter,
                ThemeTypeVariation = "StationTab", SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
            };
            button.Pressed += () => Filter(tab);
            row.AddChild(button);
            _filterButtons.Add(button);
        }
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _matches = Loc.Verbatim(new Label { Name = "Matches", ThemeTypeVariation = "MutedLabel", VerticalAlignment = VerticalAlignment.Center });
        row.AddChild(_matches);
        _search = new LineEdit { Name = "Search", PlaceholderText = "Search", ClearButtonEnabled = true, CustomMinimumSize = new Vector2(150, 30), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _search.TextChanged += Searched;
        row.AddChild(_search);
        return strip;
    }

    private Control BuildBody()
    {
        var body = new Control { Name = "Body", SizeFlagsVertical = Control.SizeFlags.ExpandFill, ClipContents = true };
        var surface = new PanelContainer { Name = "Surface", ThemeTypeVariation = "SurfacePanel" };
        surface.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        body.AddChild(surface);
        _gridScroll = new ScrollContainer { Name = "RecipeScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        surface.AddChild(_gridScroll);
        _recipeGrid = new GridContainer { Name = "RecipeGrid", Columns = 10 };
        _recipeGrid.AddThemeConstantOverride("h_separation", TileGap);
        _recipeGrid.AddThemeConstantOverride("v_separation", TileGap);
        _gridScroll.AddChild(_recipeGrid);

        // The Storage drawer slides over the left half of the grid.
        var drawer = new PanelContainer { Name = "Drawer", ThemeTypeVariation = "Drawer", Visible = false };
        drawer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.LeftWide);
        body.AddChild(drawer);
        _drawer = drawer;
        var scroll = new ScrollContainer { Name = "StorageScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        drawer.AddChild(scroll);
        _storage = new VBoxContainer { Name = "StorageList", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _storage.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_storage);
        _storage.AddChild(new Label { Name = "NoLinks", Text = "No linked storage. Place a chest next to the station to link it.", ThemeTypeVariation = "InkLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var own = new HBoxContainer { Name = "OwnTitle" };
        own.AddChild(new Label { Text = "Inventory", ThemeTypeVariation = "InkSectionLabel", Uppercase = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _ownFill = Loc.Verbatim(new Label { ThemeTypeVariation = "InkSectionLabel" });
        own.AddChild(_ownFill);
        _storage.AddChild(own);
        _storage.AddChild(_stationOwnGrid.Build("StationOwnGrid", rows: 5, this, scroll: false));
        return body;
    }

    private Control BuildQueue()
    {
        var band = new PanelContainer { Name = "Queue", ThemeTypeVariation = "QueueBand" };
        _queueBand = band;
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        band.AddChild(column);
        var head = new HBoxContainer();
        head.AddChild(new Label { Text = "Queue", ThemeTypeVariation = "InkSectionLabel", Uppercase = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _capacity = Loc.Verbatim(new Label { Name = "Capacity", ThemeTypeVariation = "InkSectionLabel", Uppercase = true });
        head.AddChild(_capacity);
        column.AddChild(head);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        _queueLeft = new Button { Name = "QueueLeft", Text = "‹", ThemeTypeVariation = "StepperButton", Visible = false, TooltipText = "Scroll the queue" };
        _queueLeft.Pressed += () => _queueScroll.ScrollHorizontal -= (int)CellWidth();
        row.AddChild(_queueLeft);
        _queueScroll = new ScrollContainer
        {
            Name = "QueueScroll", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever, CustomMinimumSize = new Vector2(0, 70),
        };
        _queueRow = new HBoxContainer { Name = "QueueCells" };
        // Cells overlap so each one's point sits in the next one's notch.
        _queueRow.AddThemeConstantOverride("separation", -CellOverlap);
        _queueScroll.AddChild(_queueRow);
        row.AddChild(_queueScroll);
        _queueRight = new Button { Name = "QueueRight", Text = "›", ThemeTypeVariation = "StepperButton", Visible = false, TooltipText = "Scroll the queue" };
        _queueRight.Pressed += () => _queueScroll.ScrollHorizontal += (int)CellWidth();
        row.AddChild(_queueRight);
        column.AddChild(row);
        return band;
    }

    private Control BuildCard()
    {
        var card = new PanelContainer { Name = "Card", ThemeTypeVariation = "CardPanel", CustomMinimumSize = new Vector2(CardWidth, 0) };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        card.AddChild(column);
        var hero = new PanelContainer { Name = "Hero", ThemeTypeVariation = "InsetPanel", CustomMinimumSize = new Vector2(0, 100) };
        _hero = new TextureRect { Name = "HeroPicture", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        hero.AddChild(_hero);
        _heroInitials = Loc.Verbatim(new Label { ThemeTypeVariation = "HeadingLabel", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        hero.AddChild(_heroInitials);
        column.AddChild(hero);
        var names = new VBoxContainer();
        names.AddThemeConstantOverride("separation", 0);
        _previewName = Loc.Verbatim(new Label { Name = "PreviewName", ThemeTypeVariation = "HeadingLabel", TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        names.AddChild(_previewName);
        _previewSubtitle = Loc.Verbatim(new Label { Name = "PreviewSubtitle", ThemeTypeVariation = "MutedLabel", TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        names.AddChild(_previewSubtitle);
        column.AddChild(names);
        _stats = new PanelContainer { Name = "Stats", ThemeTypeVariation = "InsetPanel" };
        _statRows = new GridContainer { Name = "StatRows", Columns = 3 };
        _statRows.AddThemeConstantOverride("h_separation", 10);
        _statRows.AddThemeConstantOverride("v_separation", 2);
        _stats.AddChild(_statRows);
        column.AddChild(_stats);
        var description = new ScrollContainer { Name = "DescriptionScroll", SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 36) };
        _previewDescription = Loc.Verbatim(new Label { Name = "PreviewDescription", ThemeTypeVariation = "MutedLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        description.AddChild(_previewDescription);
        column.AddChild(description);
        var materials = new HBoxContainer();
        materials.AddChild(new Label { Text = "Materials", ThemeTypeVariation = "SectionLabel", Uppercase = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _previewTime = Loc.Verbatim(new Label { Name = "PreviewTime", ThemeTypeVariation = "NumericLabel", TooltipText = "Time at this station's speed", MouseFilter = Control.MouseFilterEnum.Stop });
        materials.AddChild(_previewTime);
        column.AddChild(materials);
        _previewInputs = new HFlowContainer { Name = "PreviewInputs" };
        _previewInputs.AddThemeConstantOverride("h_separation", 10);
        _previewInputs.AddThemeConstantOverride("v_separation", 6);
        column.AddChild(_previewInputs);
        _reason = Loc.Verbatim(new Label { Name = "Reason", ThemeTypeVariation = "ErrorTextLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false });
        column.AddChild(_reason);
        var actions = new HBoxContainer { Name = "CraftBar" };
        actions.AddThemeConstantOverride("separation", 8);
        _quantity = new QuantityStepper { Name = "Quantity", MaxValue = Inventory.Settings!.MaxCraftCount };
        _quantity.ValueChanged += _ => RefreshStation();
        actions.AddChild(_quantity);
        _craft = new Button { Name = "Craft", Text = "Craft", ThemeTypeVariation = "PrimaryButton", CustomMinimumSize = new Vector2(0, 48), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _craft.Pressed += () => Craft();
        actions.AddChild(_craft);
        column.AddChild(actions);
        return card;
    }

    // Whether the screen being opened is for a station, known from the Creation before its view arrives.
    private void StartStation(InventoryRef? where)
    {
        var station = where is { Creation: { } c, Part: { } p } && Creations?.Find(c) is { } creation && creation.FindPart(p) is not null
            && creation.DefinitionOf(p).Crafting is not null;
        _stationMode = station || (where is not null && Inventory.Shown.GetValueOrDefault(where)?.Queue is not null);
        _remainingCrafts = -1;
        if (_seenFor != where)
            _newRecipes = new HashSet<string>();
        if (_stationMode)
        {
            // The station draws its own panels; the screen's frame steps aside.
            _root.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            _status.Reparent(_card.GetChild(0));
            _card.GetChild(0).MoveChild(_status, _reason.GetIndex() + 1);
            _status.ThemeTypeVariation = "ErrorTextLabel";
            _status.CustomMinimumSize = Vector2.Zero;
            SetStorage(_storageOpen);
        }
        else
        {
            _root.RemoveThemeStyleboxOverride("panel");
            if (_status.GetParent() != _classic.GetChild(0))
            {
                _status.Reparent(_classic.GetChild(0));
                _status.ThemeTypeVariation = "";
                _status.CustomMinimumSize = new Vector2(320, 0);
            }
        }
    }

    // At 1280×720: the main window 886 wide and the card 302, 12 apart, centred above the hotbar (UI spec §8). Wider
    // screens give the grid more columns; below 1280 the card narrows.
    private void SizeStation(Vector2 screen, Vector2 margin, float bar)
    {
        var width = Mathf.Clamp(screen.X - 80, 720, 1800);
        var height = Mathf.Max(420, Mathf.Min(screen.Y - 124, screen.Y - margin.Y * 2 - bar - 24));
        var card = screen.X < UiPalette.MinWindowWidth ? NarrowCardWidth : CardWidth;
        _card.CustomMinimumSize = new Vector2(card, height);
        _main.CustomMinimumSize = new Vector2(width - card - PanelGap, height);
        _station.CustomMinimumSize = new Vector2(width, height);
        _recipeGrid.Columns = Math.Max(1, (int)((_main.CustomMinimumSize.X - 16 - 22 + TileGap) / (TileSize + TileGap)));
        _drawer.OffsetRight = Mathf.Max(DrawerColumns * (DrawerSlot + TileGap) + 36, _main.CustomMinimumSize.X / 2);
        var cell = CellWidth();
        foreach (var child in _queueRow.GetChildren().OfType<Control>())
            child.CustomMinimumSize = new Vector2(cell, 70);
        // More cells than the strip holds (finished stacks on top of a full queue) scroll sideways.
        _queueLeft.Visible = _queueRight.Visible = QueueCells > CellsShown();
    }

    // Wide enough that the queue size's worth of cells (up to 8) fills the strip.
    private float CellWidth()
    {
        var shown = CellsShown();
        var room = _main.CustomMinimumSize.X - 2 * QueuePadding;
        return Mathf.Max(96, (room + CellOverlap * (shown - 1)) / shown);
    }

    private int CellsShown() =>
        Math.Clamp(_open is null ? 8 : Inventory.Shown.GetValueOrDefault(_open)?.QueueSize ?? 8, 1, 8);

    private void RefreshStation()
    {
        if (!StationMode)
            return;
        var view = Inventory.Shown.GetValueOrDefault(_open!);
        if (view is not null && view.Queue is null)
        {
            // Not a station after all: back to the container layout.
            _stationMode = false;
            StartStation(_open);
            Refresh();
            return;
        }
        var stationId = view?.Part ?? Creations?.Find(_open!.Creation!.Value)?.FindPart(_open.Part!.Value)?.Part;
        _stationTitle.Text = stationId is null ? Loc.T("Opening…") : ItemName(stationId);
        _art.Texture = stationId is null ? null : Icons?.For(stationId);
        _art.Visible = _art.Texture is not null;
        _fullPill.Visible = view?.Blocked == true;
        _levelPill.Visible = view is { MaxLevel: > 0 };
        _stationLevel.Text = view is null ? "" : Loc.T("Level {0} / {1}", view.Level + 1, view.MaxLevel + 1);
        _upgrade.Visible = view is { MaxLevel: > 0 } && Upgrades is not null;
        _upgrade.Key = GameInput.Label(InputActions.Upgrade);
        _upgrade.Disabled = view is null || view.Level >= view.MaxLevel;
        _storageButton.Key = GameInput.Label(InputActions.Inventory);
        SyncLinks(view?.Linked ?? []);
        _linkDot.Visible = _linked.Count > 0;
        _linkChip.Text = _linked.Count switch
        {
            0 => Loc.T("No linked storage"),
            1 => Loc.T("1 linked container"),
            var n => Loc.T("{0} linked containers", n),
        };
        _linkChip.TooltipText = _linked.Count == 0 ? Loc.T("Place a chest next to the station to link it.") : "";

        var me = InventoryRef.OfPlayer(Inventory.Session.LocalPeer);
        var own = Inventory.Own;
        var slots = own?.SlotCount ?? Inventory.Settings!.PlayerSlots;
        _stationOwnGrid.Fill(me, [.. Enumerable.Range(0, slots).Select(i => (i, own?.At(i)))]);
        _ownFill.Text = $"{own?.Items.Count ?? 0} / {slots}";
        foreach (var where in _linked)
        {
            var shown = _linkedGrids[where];
            var linked = Inventory.Shown.GetValueOrDefault(where);
            shown.Title.Text = linked?.Part is { } part ? ItemName(part) : Loc.T("Opening…");
            shown.Fill.Text = linked is null ? "" : $"{linked.Items.Count} / {linked.SlotCount}";
            shown.Grid.Fill(where, [.. Enumerable.Range(0, linked?.SlotCount ?? 0).Select(i => (i, linked?.At(i)))]);
        }
        _storage.GetNode<Control>("NoLinks").Visible = _linked.Count == 0;

        if (stationId is null)
            return;
        var atStation = Inventory.Recipes.ForStation(stationId).ToList();
        if (_seenFor != _open)
        {
            _seenFor = _open;
            _newRecipes = MarkSeen(stationId, atStation.Select(r => r.QualifiedId));
        }
        for (var i = 0; i < TabOrder.Length; i++)
            _filterButtons[i].Visible = TabOrder[i] == InventoryTab.All || RecipeBrowser.Filter(atStation, "", TabOrder[i], Catalog).Count > 0;
        if (!_filterButtons[Array.IndexOf(TabOrder, _filter)].Visible)
            Filter(InventoryTab.All);
        var recipes = RecipeBrowser.Filter(atStation, _search.Text, _filter, Catalog);
        _matches.Text = _search.Text.Trim().Length == 0 ? "" : recipes.Count == 1 ? Loc.T("1 found") : Loc.T("{0} found", recipes.Count);
        if (_chosen is not null && Inventory.Recipes.Find(_chosen)?.RunsAt(stationId) != true)
            _chosen = null;
        _chosen ??= (recipes.FirstOrDefault(CanMake) ?? recipes.FirstOrDefault())?.QualifiedId;
        FillRecipes(recipes);
        FillPreview(view);
        FillQueue(view);
    }

    private void Searched(string text)
    {
        // A search looks through every tab.
        if (text.Trim().Length > 0 && _filter != InventoryTab.All)
            Filter(InventoryTab.All);
        else
            RefreshStation();
    }

    private void SetStorage(bool open)
    {
        _storageOpen = open;
        _drawer.Visible = open;
        _storageButton.SetPressedNoSignal(open);
    }

    // How many of an item the player and the linked storage hold, as last sent.
    private int Have(string item) =>
        (Inventory.Own?.Count(item) ?? 0) + _linked.Sum(l => Inventory.Shown.GetValueOrDefault(l)?.Count(item) ?? 0);

    private bool CanMake(Recipe recipe) => recipe.Inputs.All(i => Have(i.Item) >= i.Count);

    private IReadOnlySet<string> MarkSeen(string station, IEnumerable<string> recipes)
    {
        _seen ??= SeenRecipes.Parse(Godot.FileAccess.FileExists(SeenPath) ? Godot.FileAccess.GetFileAsString(SeenPath) : null);
        var fresh = _seen.Open(station, recipes);
        using var file = Godot.FileAccess.Open(SeenPath, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(_seen.ToJson());
        return fresh;
    }

    private void FillRecipes(IReadOnlyList<Recipe> recipes)
    {
        var key = string.Join('|', recipes.Select(r => r.QualifiedId));
        if (key != _recipeKey)
        {
            _recipeKey = key;
            foreach (var tile in _recipeTiles.Values)
                tile.Visible = false;
            for (var i = 0; i < recipes.Count; i++)
            {
                var id = recipes[i].QualifiedId;
                if (!_recipeTiles.TryGetValue(id, out var tile))
                {
                    tile = new RecipeTile(id) { Name = $"Recipe_{id.Replace(':', '_')}", CustomMinimumSize = new Vector2(TileSize, TileSize) };
                    tile.Pressed += () => Choose(id);
                    // Arrow keys move the focus between tiles, and the focused tile is the one chosen.
                    tile.FocusEntered += () =>
                    {
                        if (_chosen != id)
                            Choose(id);
                    };
                    _recipeGrid.AddChild(tile);
                    _recipeTiles[id] = tile;
                }
                tile.Visible = true;
                _recipeGrid.MoveChild(tile, i);
            }
            if (recipes.Count == 0 && _recipeGrid.GetNodeOrNull("NoRecipes") is null)
                _recipeGrid.AddChild(new Label { Name = "NoRecipes", Text = "No recipes match.", ThemeTypeVariation = "InkLabel" });
            if (_recipeGrid.GetNodeOrNull<Control>("NoRecipes") is { } none)
                none.Visible = recipes.Count == 0;
        }
        foreach (var recipe in recipes)
        {
            var output = recipe.Outputs.FirstOrDefault();
            var name = recipe.Name;
            var description = output.Item is null ? "" : Catalog.FindDefinition(output.Item)?.Description ?? "";
            // Pictures that finish rendering after the grid was built show up on the next refresh.
            _recipeTiles[recipe.QualifiedId].Show(output.Item is null ? null : Icons?.For(output.Item), name, output.Count, CanMake(recipe),
                _newRecipes.Contains(recipe.QualifiedId), recipe.QualifiedId == _chosen, description);
        }
    }

    private void FillPreview(InventoryView? view)
    {
        var recipe = _chosen is null ? null : Inventory.Recipes.Find(_chosen);
        var count = _quantity.Value;
        var inputs = recipe is null ? [] : RecipeBrowser.Inputs(recipe, count, Have);
        var queueFull = view?.Queue is { } queue && view.QueueSize is { } size && queue.Count >= size;
        _craft.Disabled = recipe is null || inputs.Any(i => i.Missing) || queueFull;
        _reason.Text = recipe is null
            ? Loc.T("Choose a recipe.")
            : inputs.FirstOrDefault(i => i.Missing) is { } short_
                ? inputs.Count(i => i.Missing) == 1
                    ? Loc.T("Needs {0} more {1}.", short_.Need - short_.Have, ItemName(short_.Item))
                    : Loc.T("Needs more {0} and {1} other inputs.", ItemName(short_.Item), inputs.Count(i => i.Missing) - 1)
                : queueFull ? Loc.T("The queue is full ({0}).", view!.QueueSize!.Value) : "";
        _reason.Visible = _craft.Disabled;
        var icon = recipe?.Outputs.FirstOrDefault().Item is { } shown ? Icons?.For(shown) : null;
        var key = recipe is null ? "" : $"{recipe.QualifiedId}:{count}:{string.Join(',', inputs.Select(i => i.Have))}:{view?.Speed}:{icon is not null}";
        if (key == _previewKey)
            return;
        _previewKey = key;
        foreach (var child in _previewInputs.GetChildren())
        {
            _previewInputs.RemoveChild(child);
            child.QueueFree();
        }
        _materialRows.Clear();
        foreach (var child in _statRows.GetChildren())
        {
            _statRows.RemoveChild(child);
            child.QueueFree();
        }
        if (recipe is null)
        {
            (_hero.Texture, _heroInitials.Text) = (null, "");
            _previewName.Text = Loc.T("Choose a recipe");
            (_previewSubtitle.Text, _previewDescription.Text, _previewTime.Text) = ("", "", "");
            _stats.Visible = false;
            return;
        }
        var output = recipe.Outputs.FirstOrDefault();
        var definition = output.Item is null ? null : Catalog.FindDefinition(output.Item);
        _hero.Texture = icon;
        _heroInitials.Text = icon is null && output.Item is not null ? ItemIcons.Initials(ItemName(output.Item)) : "";
        _previewName.Text = output.Count * count > 1 ? $"{recipe.Name} × {output.Count * count}" : recipe.Name;
        _previewSubtitle.Text = string.Join(" · ", new[] { definition?.Category is { } category ? CategoryName(category) : null, definition is null ? null : ItemStats.Size(definition) }.OfType<string>());
        _previewDescription.Text = definition?.Description ?? "";
        var speed = view?.Speed ?? 1f;
        _previewTime.Text = Clock(recipe.Time * count / speed);
        var rows = definition is null ? [] : ItemStats.Rows(definition, HudRoot.Settings.StatPips);
        foreach (var row in rows)
        {
            _statRows.AddChild(new Label { Text = Loc.T(row.Name), ThemeTypeVariation = "MutedLabel", CustomMinimumSize = new Vector2(78, 0) });
            var value = Loc.Verbatim(new Label { Text = row.Value, HorizontalAlignment = HorizontalAlignment.Right, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ThemeTypeVariation = "NumericLabel" });
            var pips = new PipBar { Filled = row.Pips };
            pips.SetMeta("stat", row.Name);
            pips.SetMeta("value", value);
            _statRows.AddChild(pips);
            _statRows.AddChild(value);
        }
        if (definition?.Flammable is { } flammable && rows.Count > 0)
        {
            _statRows.AddChild(new Label { Text = "Flammable", ThemeTypeVariation = "MutedLabel" });
            _statRows.AddChild(new Control());
            _statRows.AddChild(new Label { Text = flammable ? Loc.T("Yes") : Loc.T("No"), HorizontalAlignment = HorizontalAlignment.Right, ThemeTypeVariation = "NumericLabel" });
        }
        _stats.Visible = rows.Count > 0;
        foreach (var input in inputs)
            _previewInputs.AddChild(MaterialTile(input));
    }

    // One material: its picture on an inset tile (ringed in red when short), have / need, and its name.
    private Control MaterialTile(RecipeInput input)
    {
        var box = new VBoxContainer { Name = $"Input_{input.Item.Replace(':', '_')}", CustomMinimumSize = new Vector2(62, 0) };
        box.AddThemeConstantOverride("separation", 2);
        var tile = new ItemSlot { CustomMinimumSize = new Vector2(62, 56) };
        tile.Show(input.Item, Icons?.For(input.Item), ItemName(input.Item), null);
        var ring = StationTheme.Box(UiTokens.Inset, StationTheme.TileCorner);
        ring.SetBorderWidthAll(input.Missing ? 2 : 1);
        ring.BorderColor = input.Missing ? UiTokens.ErrorMark : UiTokens.ChromeLine;
        tile.AddThemeStyleboxOverride("panel", ring);
        var item = input.Item;
        tile.Details = () => DetailsOf(item, null);
        box.AddChild(tile);
        var count = Loc.Verbatim(new Label
        {
            Text = $"{input.Have} / {input.Need}", HorizontalAlignment = HorizontalAlignment.Center,
            ThemeTypeVariation = "NumericLabel",
        });
        if (input.Missing)
            count.AddThemeColorOverride("font_color", UiTokens.ErrorText);
        box.AddChild(count);
        var name = ItemName(input.Item);
        box.AddChild(Loc.Verbatim(new Label
        {
            Text = name, ThemeTypeVariation = "MutedLabel", HorizontalAlignment = HorizontalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new Vector2(62, 0), TooltipText = name, MouseFilter = Control.MouseFilterEnum.Pass,
        }));
        _materialRows.Add((count, name, input.Missing));
        return box;
    }

    private void FillQueue(InventoryView? view)
    {
        var jobs = view?.Queue ?? [];
        var speed = view?.Speed ?? 1f;
        // A craft finished: the orders left hold fewer crafts than before.
        var remaining = jobs.Sum(j => j.Count);
        var finished = _remainingCrafts >= 0 && remaining < _remainingCrafts;
        _remainingCrafts = remaining;
        var ready = view?.Items ?? [];
        var size = Math.Max(1, view?.QueueSize ?? 1);
        _capacity.Text = Loc.T("{0} / {1} orders · output {2} / {3}", jobs.Count, size, ready.Count, view?.SlotCount ?? 0);

        while (_readyCells.Count < ready.Count)
            _readyCells.Add(NewReadyCell());
        while (_jobCells.Count < jobs.Count)
            _jobCells.Add(NewJobCell(_jobCells.Count));
        var empty = Math.Max(0, size - jobs.Count);
        while (_emptyCells.Count < empty)
        {
            var cell = new QueueCell { Name = $"Empty{_emptyCells.Count}", Fill = UiTokens.Surface with { A = 0.55f }, MouseFilter = Control.MouseFilterEnum.Ignore };
            _queueRow.AddChild(cell);
            _emptyCells.Add(cell);
        }
        // Time order: finished stacks, the order in progress, waiting orders, then empty cells up to the queue size.
        var at = 0;
        for (var i = 0; i < _readyCells.Count; i++)
        {
            var cell = _readyCells[i];
            cell.Visible = i < ready.Count;
            if (!cell.Visible)
                continue;
            var stack = ready[i];
            cell.Stack = stack;
            cell.Slot.Show(stack.Item, Icons?.For(stack.Item), ItemName(stack.Item), null);
            cell.Count.Text = $"×{stack.Count}";
            _queueRow.MoveChild(cell, at++);
        }
        for (var i = 0; i < _jobCells.Count; i++)
        {
            var cell = _jobCells[i];
            cell.Visible = i < jobs.Count;
            if (!cell.Visible)
                continue;
            var job = jobs[i];
            cell.Order = $"{RecipeName(job.Recipe)} × {job.Count}";
            cell.TooltipText = cell.Order;
            var output = Inventory.Recipes.Find(job.Recipe)?.Outputs.FirstOrDefault().Item;
            cell.Picture.Texture = output is null ? null : Icons?.For(output);
            cell.Count.Text = $"×{job.Count}";
            cell.Progress = i == 0 && job.Time > 0 ? Math.Clamp(job.Progress / job.Time, 0, 1) : 0f;
            cell.Fill = i == 0 ? UiTokens.Tile : UiTokens.Surface;
            cell.Note.Text = i == 0 && view!.Blocked
                ? Loc.T("Output full")
                : i == 0 ? Loc.T("{0} left", Clock(RecipeBrowser.Remaining(job.Time, job.Progress, job.Count, speed))) : Loc.T("Waiting");
            _queueRow.MoveChild(cell, at++);
        }
        for (var i = 0; i < _emptyCells.Count; i++)
        {
            _emptyCells[i].Visible = i < empty;
            if (i < empty)
                _queueRow.MoveChild(_emptyCells[i], at++);
        }
        var first = true;
        foreach (var cell in _queueRow.GetChildren().OfType<QueueCell>().Where(c => c.Visible))
        {
            cell.First = first;
            first = false;
        }
        if (finished)
            Flash();
    }

    private ReadyCell NewReadyCell()
    {
        var cell = new ReadyCell { Name = $"Ready{_readyCells.Count}" };
        cell.Slot.DragSource = () => cell.Stack is { } stack && _open is { } where ? new ItemDrag { Entry = stack.Item, From = where, FromSlot = stack.Slot } : null;
        cell.Slot.Details = () => cell.Stack is { } stack ? DetailsOf(stack.Item, stack.Count) : null;
        cell.Slot.GuiInput += e =>
        {
            // Click takes the stack; Shift+click takes every finished stack.
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } click || _open is not { } where || cell.Stack is not { } stack)
                return;
            // Into linked storage when there is some, else the inventory.
            var to = _linked.Count > 0 ? _linked[0] : InventoryRef.OfPlayer(Inventory.Session.LocalPeer);
            var stacks = click.ShiftPressed ? _readyCells.Where(c => c.Visible && c.Stack is not null).Select(c => c.Stack!) : [stack];
            foreach (var each in stacks.ToList())
                Send(InventoryActions.MoveItems, new MoveItemsArgs(where, each.Slot, to));
        };
        _queueRow.AddChild(cell);
        return cell;
    }

    private JobCell NewJobCell(int index)
    {
        var cell = new JobCell { Name = $"Job{index}" };
        cell.Cancel.Pressed += () => CancelOrder(index);
        cell.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
                CancelOrder(index);
        };
        _queueRow.AddChild(cell);
        return cell;
    }

    private void CancelOrder(int index)
    {
        if (_open is { Creation: { } c, Part: { } p })
            Send(InventoryActions.CancelCraft, new CancelCraftArgs(c, p, index));
    }

    // A craft finished: the finished stack's cell glows and fades back (the whole strip when the output went straight
    // to linked storage).
    private void Flash()
    {
        _flash?.Kill();
        _flashed?.Set("modulate", Colors.White);
        _flashed = _readyCells.LastOrDefault(c => c.Visible) as Control ?? _queueBand;
        _flashed.Modulate = UiTokens.Accent.Lightened(0.4f);
        _flash = CreateTween();
        _flash.TweenProperty(_flashed, "modulate", Colors.White, FlashSeconds);
    }

    // Opens or closes linked containers' views to match the station's links.
    private void SyncLinks(IReadOnlyList<InventoryRef> wanted)
    {
        foreach (var gone in _linked.Where(l => !wanted.Contains(l)).ToList())
        {
            Inventory.Close(gone);
            _linked.Remove(gone);
            var shown = _linkedGrids[gone];
            _storage.RemoveChild(shown.Box);
            shown.Box.QueueFree();
            _linkedGrids.Remove(gone);
        }
        foreach (var where in wanted.Where(w => !_linked.Contains(w)))
        {
            _linked.Add(where);
            Inventory.Open(where);
            var box = new VBoxContainer { Name = $"Linked{where.Part}" };
            var head = new HBoxContainer();
            var title = Loc.Verbatim(new Label { Text = Loc.T("Opening…"), ThemeTypeVariation = "InkSectionLabel", Uppercase = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            head.AddChild(title);
            var fill = Loc.Verbatim(new Label { ThemeTypeVariation = "InkSectionLabel" });
            head.AddChild(fill);
            box.AddChild(head);
            var grid = new SlotGrid(DrawerColumns, DrawerSlot, tiled: true);
            box.AddChild(grid.Build($"LinkedGrid{where.Part}", rows: 2, this, scroll: false));
            _storage.AddChild(box);
            // Linked storage comes first, above the player's own inventory.
            _storage.MoveChild(box, _storage.GetNode("OwnTitle").GetIndex());
            _linkedGrids[where] = (title, fill, grid, box);
        }
    }

    private void CloseLinks() => SyncLinks([]);

    private void LinkClosed(InventoryRef where)
    {
        if (_linked.Contains(where))
            _status.Text = Loc.T("A linked container is out of reach.");
    }

    // Shift+click on the player's own stack: into the first linked container, else the station (or the open container).
    private InventoryRef? ShiftTarget() => StationMode && _linked.Count > 0 ? _linked[0] : _open;

    private void OpenUpgrade()
    {
        if (_open is { Creation: { } c, Part: { } p } && Upgrades is not null)
            Upgrades.Open((c, p));
    }

    private static string CategoryName(PartCategory category) => category switch
    {
        PartCategory.Block => Loc.T("Block"),
        PartCategory.Interactive => Loc.T("Interactive"),
        PartCategory.Container => Loc.T("Container"),
        PartCategory.Station => Loc.T("Station"),
        PartCategory.Light => Loc.T("Light"),
        PartCategory.Decor => Loc.T("Decor"),
        PartCategory.Resource => Loc.T("Resource"),
        PartCategory.Consumable => Loc.T("Consumable"),
        PartCategory.Tool => Loc.T("Tool"),
        PartCategory.Weapon => Loc.T("Weapon"),
        _ => Loc.T("Part"),
    };

    // A craft time as the detail card shows it: 0:12, 1:05.
    private static string Clock(float seconds)
    {
        if (!float.IsFinite(seconds))
            return "-";
        var whole = (int)Math.Ceiling(seconds - 0.05);
        return $"{whole / 60}:{whole % 60:00}";
    }

    // A recipe in the grid: its output's picture (or initials), "×n" for several, a red dot while the inputs are short
    // and a NEW tag; the tooltip names it.
    private sealed partial class RecipeTile : Button
    {
        private readonly TextureRect _picture = new() { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
        private readonly Label _initials = Loc.Verbatim(new Label { ThemeTypeVariation = "InkNumericLabel", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore });
        private readonly Label _count = Loc.Verbatim(new Label { ThemeTypeVariation = "InkNumericLabel", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, MouseFilter = MouseFilterEnum.Ignore });
        private readonly Panel _dot = new() { MouseFilter = MouseFilterEnum.Ignore, Size = new Vector2(10, 10) };
        private readonly PanelContainer _new = new() { ThemeTypeVariation = "Pill", MouseFilter = MouseFilterEnum.Ignore };

        public RecipeTile(string recipe)
        {
            Recipe = recipe;
            // The tooltip is the item's own name and description; the NEW tag translates as usual.
            Loc.Verbatim(this);
            ThemeTypeVariation = "RecipeTile";
            ToggleMode = true;
            _picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _picture.SetOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.KeepSize, 6);
            AddChild(_picture);
            _initials.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(_initials);
            _count.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            (_count.OffsetRight, _count.OffsetBottom) = (-5, -1);
            AddChild(_count);
            var dot = StationTheme.Box(UiTokens.ErrorMark, 5);
            dot.SetBorderWidthAll(2);
            dot.BorderColor = UiTokens.Tile;
            _dot.AddThemeStyleboxOverride("panel", dot);
            AddChild(_dot);
            _new.AddChild(new Label { Text = "New", ThemeTypeVariation = "PillLabel", Uppercase = true, MouseFilter = MouseFilterEnum.Ignore, AutoTranslateMode = AutoTranslateModeEnum.Always });
            _new.Position = new Vector2(4, 4);
            AddChild(_new);
            Resized += () => _dot.Position = new Vector2(Size.X - 16, 6);
        }

        /// <summary>The recipe's qualified id.</summary>
        public string Recipe { get; }

        /// <summary>True while the inputs are short (dimmed, red dot).</summary>
        public bool Short => _dot.Visible;

        /// <summary>True while tagged NEW.</summary>
        public bool IsNew => _new.Visible;

        public void Show(Texture2D? picture, string name, int count, bool canMake, bool isNew, bool chosen, string description)
        {
            _picture.Texture = picture;
            _initials.Text = picture is null ? ItemIcons.Initials(name) : "";
            _count.Text = count > 1 ? $"×{count}" : "";
            // Short: the picture desaturated to 45%, with the dot (colour is never the only signal).
            _picture.SelfModulate = canMake ? Colors.White : new Color(0.75f, 0.78f, 0.82f, 0.45f);
            _dot.Visible = !canMake;
            _new.Visible = isNew;
            SetPressedNoSignal(chosen);
            TooltipText = description.Length == 0 ? name : $"{name}\n{description}";
            if (!canMake)
                TooltipText += "\n" + Loc.T("Missing inputs");
        }
    }

    // A finished stack in the queue strip: a green cell to take it from.
    private sealed partial class ReadyCell : QueueCell
    {
        public ReadyCell()
        {
            Fill = UiTokens.SuccessFill;
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 4);
            row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            (row.OffsetLeft, row.OffsetRight) = (Point + 4, -Point);
            AddChild(row);
            Slot.CustomMinimumSize = new Vector2(44, 44);
            Slot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            Slot.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            row.AddChild(Slot);
            var text = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
            text.AddThemeConstantOverride("separation", 0);
            text.AddChild(Count);
            text.AddChild(new Label { Text = "Ready", ThemeTypeVariation = "InkLabel", MouseFilter = MouseFilterEnum.Ignore });
            row.AddChild(text);
        }

        public ItemSlot Slot { get; } = new();

        public Label Count { get; } = Loc.Verbatim(new Label { ThemeTypeVariation = "InkNumericLabel", MouseFilter = MouseFilterEnum.Ignore });

        public SlotView? Stack { get; set; }
    }

    // An order in the queue strip: its picture, crafts left and time left (or Waiting), with × to cancel.
    private sealed partial class JobCell : QueueCell
    {
        public JobCell()
        {
            // The tooltip is the order's recipe name.
            Loc.Verbatim(this);
            MouseFilter = MouseFilterEnum.Stop;
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 4);
            row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            (row.OffsetLeft, row.OffsetRight) = (Point + 4, -Point);
            AddChild(row);
            Picture.CustomMinimumSize = new Vector2(40, 40);
            row.AddChild(Picture);
            var text = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddThemeConstantOverride("separation", 0);
            text.AddChild(Count);
            text.AddChild(Note);
            row.AddChild(text);
            Cancel.Position = new Vector2(0, 2);
            AddChild(Cancel);
            Resized += () => Cancel.Position = new Vector2(Size.X - Point - Cancel.Size.X - 2, 2);
            MouseEntered += () => Cancel.Visible = true;
            MouseExited += () => Cancel.Visible = GetGlobalRect().HasPoint(GetGlobalMousePosition());
        }

        public string Order { get; set; } = "";

        public TextureRect Picture { get; } = new() { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };

        public Label Count { get; } = Loc.Verbatim(new Label { ThemeTypeVariation = "InkNumericLabel", MouseFilter = MouseFilterEnum.Ignore });

        public Label Note { get; } = Loc.Verbatim(new Label { ThemeTypeVariation = "InkLabel", MouseFilter = MouseFilterEnum.Ignore, ClipText = true });

        public Button Cancel { get; } = new() { Text = "×", ThemeTypeVariation = "StepperButton", Visible = false, TooltipText = "Cancel the order and get its inputs back (or right-click it)", CustomMinimumSize = new Vector2(22, 22), AutoTranslateMode = AutoTranslateModeEnum.Always };
    }
}
