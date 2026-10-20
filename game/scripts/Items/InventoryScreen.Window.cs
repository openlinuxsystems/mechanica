using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Ui;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Items;

/// <summary>
/// The Inventory window (UI spec §0, §3): what the Inventory key opens, and the player's half of a container. A
/// <c>Chrome</c> header holds the satchel icon, the INVENTORY title with the fill pill (and a warning pill when nearly
/// full), the search field, the Organize button (merges stacks and sorts them by tab, then name, through
/// <c>SortInventory</c>) and X. Under it the shared category tabs, each with its colour dot, and the tile grid on
/// <c>Surface</c>, ten columns wide; every stack carries its category's dot. Typing searches every tab (the tab switches
/// to All) and the tab row shows the match count. In Creative every part and tool is listed under the grid, and a give
/// box sits in the footer with the status line. A container opened with Use (E) has its own screen
/// (InventoryScreen.Container.cs), whose header carries an Organize button too.
/// </summary>
public partial class InventoryScreen
{
    private const int InventoryColumns = 10;
    private const float ScrollRoom = 14;

    private Control _classic = null!;
    private Control _ownWindow = null!;
    private readonly SlotGrid _ownGrid = new(InventoryColumns, TileSize, tiled: true);
    private readonly SlotGrid _catalogGrid = new(InventoryColumns, TileSize, tiled: true);
    private Control _catalogPanel = null!;
    private Label _ownFillText = null!;
    private Control _warningPill = null!;
    private Label _warning = null!;
    private LineEdit _ownSearch = null!;
    private UiGlyph _searchGlyph = null!;
    private Button _organize = null!;
    private Label _ownMatches = null!;
    private Label _empty = null!;
    private readonly List<Button> _tabButtons = [];
    private Control _footer = null!;
    private HBoxContainer _footerRow = null!;
    private LineEdit _give = null!;

    /// <summary>The search text in the Inventory window.</summary>
    public string SearchText => _ownSearch.Text;

    /// <summary>The match count beside the tabs while searching, or empty.</summary>
    public string MatchesText => _ownMatches.Visible ? _ownMatches.Text : "";

    /// <summary>The fill pill (<c>12 / 30</c>).</summary>
    public string FillText => _ownFillText.Text;

    /// <summary>The Organize button of the Inventory window.</summary>
    public Button OrganizeButton => _organize;

    /// <summary>The category tab buttons, in <see cref="TabOrder"/>.</summary>
    public IReadOnlyList<Button> TabButtons => _tabButtons;

    /// <summary>The Inventory window.</summary>
    public Control InventoryWindow => _ownWindow;

    /// <summary>Shows only the items under <paramref name="tab"/>.</summary>
    public void SetTab(InventoryTab tab)
    {
        _tab = tab;
        _held = null;
        for (var i = 0; i < TabOrder.Length; i++)
            _tabButtons[i].SetPressedNoSignal(TabOrder[i] == tab);
        Refresh();
    }

    /// <summary>Lists only the stacks whose name or id contains every word of <paramref name="text"/>, across every tab.</summary>
    public void SearchInventory(string text)
    {
        _ownSearch.Text = text;
        InventorySearched(text);
    }

    /// <summary>Auto-organizes <paramref name="where"/> (the player's own inventory when null): stacks merge and sort by tab, then name.</summary>
    public void Organize(InventoryRef? where = null, Action<Core.Actions.ActionResult>? done = null)
    {
        _held = null;
        Actions.Request(InventoryActions.SortInventory, new SortInventoryArgs(where ?? InventoryRef.OfPlayer(Inventory.Session.LocalPeer)), result =>
        {
            _status.Text = result.Ok ? "" : result.Message ?? "";
            done?.Invoke(result);
        });
    }

    private void BuildInventory()
    {
        var columns = new HBoxContainer { Name = "Columns", Theme = StationTheme.Theme };
        columns.AddThemeConstantOverride("separation", (int)PanelGap);
        _root.AddChild(columns);
        _classic = columns;

        var window = new PanelContainer { Name = "Inventory", ThemeTypeVariation = "MainWindow", ClipChildren = CanvasItem.ClipChildrenMode.AndDraw };
        _ownWindow = window;
        columns.AddChild(window);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        window.AddChild(column);
        column.AddChild(BuildInventoryHeader());
        column.AddChild(BuildInventoryTabs());

        var body = new PanelContainer { Name = "Surface", ThemeTypeVariation = "SurfacePanel", SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(body);
        var scroll = new ScrollContainer { Name = "OwnScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(scroll);
        var list = new VBoxContainer { Name = "OwnList", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(list);
        list.AddChild(_ownGrid.Build("OwnGrid", rows: 3, this, scroll: false));
        _empty = Loc.Verbatim(new Label { Name = "Empty", ThemeTypeVariation = "InkLabel", Visible = false });
        list.AddChild(_empty);
        _catalogPanel = new VBoxContainer { Name = "Catalog" };
        _catalogPanel.AddThemeConstantOverride("separation", 6);
        var catalogTitle = new HBoxContainer();
        catalogTitle.AddChild(new Label { Text = "All parts and tools", ThemeTypeVariation = "InkSectionLabel", Uppercase = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        catalogTitle.AddChild(new Label { Text = "Drag onto the hotbar or into your inventory", ThemeTypeVariation = "InkLabel" });
        _catalogPanel.AddChild(catalogTitle);
        _catalogPanel.AddChild(_catalogGrid.Build("CatalogGrid", rows: 2, this, scroll: false));
        list.AddChild(_catalogPanel);

        // The footer: the status line, and the give box in Creative.
        var footer = new PanelContainer { Name = "Footer", ThemeTypeVariation = "QueueBand" };
        _footer = footer;
        _footerRow = new HBoxContainer();
        _footerRow.AddThemeConstantOverride("separation", 12);
        footer.AddChild(_footerRow);
        _status = new Label { Name = "Status", ThemeTypeVariation = "InkLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        _footerRow.AddChild(_status);
        _give = new LineEdit { Name = "Give", PlaceholderText = "give: item_id count", Visible = Inventory.Settings!.AllowGive, CustomMinimumSize = new Vector2(240, 32) };
        _give.TextSubmitted += OnGive;
        _footerRow.AddChild(_give);
        column.AddChild(footer);
    }

    private Control BuildInventoryHeader()
    {
        var header = new PanelContainer { Name = "Header", ThemeTypeVariation = "Header" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        header.AddChild(row);
        row.AddChild(new UiGlyph { Name = "Icon", Shape = GlyphShape.Satchel, Colour = UiTokens.Accent, CustomMinimumSize = new Vector2(44, 44), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        titles.AddThemeConstantOverride("separation", 4);
        titles.AddChild(new Label { Name = "Title", Text = "Inventory", ThemeTypeVariation = "DisplayLabel", Uppercase = true });
        var pills = new HBoxContainer();
        pills.AddThemeConstantOverride("separation", 8);
        var fill = new PanelContainer { Name = "FillPill", ThemeTypeVariation = "Pill", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "Slots used" };
        _ownFillText = Loc.Verbatim(new Label { Name = "Fill", ThemeTypeVariation = "PillLabel" });
        fill.AddChild(_ownFillText);
        pills.AddChild(fill);
        _warningPill = new PanelContainer { Name = "WarningPill", ThemeTypeVariation = "WarningPill", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Visible = false };
        _warning = Loc.Verbatim(new Label { Name = "Warning", ThemeTypeVariation = "PillLabel", Uppercase = true });
        _warningPill.AddChild(_warning);
        pills.AddChild(_warningPill);
        titles.AddChild(pills);
        row.AddChild(titles);

        var actions = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        actions.AddThemeConstantOverride("separation", 8);
        // The search field, with a magnifier that gives way to the clear button once there is text.
        var search = new Control { Name = "SearchBox", CustomMinimumSize = new Vector2(240, 36) };
        _ownSearch = new LineEdit { Name = "Search", PlaceholderText = "Search", ClearButtonEnabled = true };
        _ownSearch.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _ownSearch.TextChanged += InventorySearched;
        search.AddChild(_ownSearch);
        _searchGlyph = new UiGlyph { Name = "Magnifier", Shape = GlyphShape.Search, Colour = UiTokens.TextMuted };
        _searchGlyph.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterRight);
        (_searchGlyph.OffsetLeft, _searchGlyph.OffsetTop, _searchGlyph.OffsetRight, _searchGlyph.OffsetBottom) = (-30, -9, -12, 9);
        search.AddChild(_searchGlyph);
        actions.AddChild(search);
        _organize = MakeOrganize("Organize");
        _organize.Pressed += () => Organize();
        actions.AddChild(_organize);
        var close = new Button { Name = "Close", Text = "✕", ThemeTypeVariation = "CloseButton", CustomMinimumSize = new Vector2(36, 36), TooltipText = "Close" };
        close.Pressed += Hide;
        actions.AddChild(close);
        row.AddChild(actions);
        return header;
    }

    // The square Organize button: the sort glyph on a secondary button.
    private static Button MakeOrganize(string name)
    {
        var button = new Button { Name = name, CustomMinimumSize = new Vector2(36, 36), TooltipText = "Organize: stack and sort items" };
        var glyph = new UiGlyph { Shape = GlyphShape.Organize, Colour = UiTokens.Text };
        glyph.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        (glyph.OffsetLeft, glyph.OffsetTop, glyph.OffsetRight, glyph.OffsetBottom) = (8, 8, -8, -8);
        button.AddChild(glyph);
        return button;
    }

    private Control BuildInventoryTabs()
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
                Name = tab.ToString(), Text = TabName(tab), ToggleMode = true, ButtonGroup = group, ButtonPressed = tab == _tab,
                ThemeTypeVariation = "StationTab", SizeFlagsVertical = Control.SizeFlags.ShrinkEnd, Icon = TabDot(tab),
            };
            button.Pressed += () => SetTab(tab);
            row.AddChild(button);
            _tabButtons.Add(button);
        }
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _ownMatches = Loc.Verbatim(new Label { Name = "Matches", ThemeTypeVariation = "MutedLabel", VerticalAlignment = VerticalAlignment.Center, Visible = false });
        row.AddChild(_ownMatches);
        return strip;
    }

    // Ten tile columns wide, as tall as the Fabricator's window, above the hotbar.
    private void SizeInventory(Vector2 screen, Vector2 margin, float bar)
    {
        var height = Mathf.Max(420, Mathf.Min(screen.Y - 124, screen.Y - margin.Y * 2 - bar - 24));
        _ownWindow.CustomMinimumSize = new Vector2(GridWidth(InventoryColumns), height);
        _searchGlyph.Visible = _ownSearch.Text.Length == 0;
        _footer.Visible = _give.Visible || _status.Text.Length > 0;
    }

    // A window wide enough for this many tiles, the surface's padding and its scrollbar.
    private static float GridWidth(int columns) => columns * (TileSize + TileGap) - TileGap + 16 + 8 + ScrollRoom;

    private void InventorySearched(string text)
    {
        _searchGlyph.Visible = text.Length == 0;
        // A search looks through every tab.
        if (ItemSearch.Active(text) && _tab != InventoryTab.All)
            SetTab(InventoryTab.All);
        else
            Refresh();
    }

    private void RefreshInventory()
    {
        var own = Inventory.Own;
        var me = InventoryRef.OfPlayer(Inventory.Session.LocalPeer);
        var slots = own?.SlotCount ?? Inventory.Settings!.PlayerSlots;
        var query = _ownSearch.Text;
        var filtered = _tab != InventoryTab.All || ItemSearch.Active(query);
        var shown = !filtered
            ? Enumerable.Range(0, slots).Select(i => (i, own?.At(i)))
            : own?.Items.Where(s => Lists(s.Item, query)).OrderBy(s => s.Slot).Select(s => (s.Slot, (SlotView?)s)) ?? [];
        _ownGrid.Fill(me, [.. shown]);
        var used = own?.Items.Select(s => s.Slot).Distinct().Count() ?? 0;
        var free = slots - used;
        _ownFillText.Text = $"{used} / {slots}";
        _warningPill.Visible = !Creative && own is not null && Inventory.Settings!.NearlyFull(free);
        _warning.Text = free == 0 ? Loc.T("Inventory full") : Loc.T("Nearly full: {0} free", free);
        _ownMatches.Visible = ItemSearch.Active(query);
        var matches = _ownGrid.Slots.Count(s => s.Visible);
        _ownMatches.Text = matches == 1 ? Loc.T("1 match") : Loc.T("{0} matches", matches);
        _empty.Visible = filtered && matches == 0;
        _empty.Text = ItemSearch.Active(query) ? Loc.T("Nothing in your inventory matches “{0}”.", query.Trim()) : Loc.T("You carry nothing under this tab.");
        _organize.Disabled = own is null || own.Items.Count == 0;
        _catalogPanel.Visible = Creative && AllItems is not null;
        if (_catalogPanel.Visible)
            _catalogGrid.FillCatalog([.. AllItems!().Where(i => Lists(i, query)).OrderBy(ItemName, StringComparer.CurrentCultureIgnoreCase)]);
    }

    // Whether the Inventory window lists an item: under the tab shown, and matching the search.
    private bool Lists(string item, string query) => Shows(item) && ItemSearch.Matches(query, ItemName(item), item);

    // The dot of the tab an item is under, for its tile.
    private Color? DotOf(string item) =>
        UiTokens.CategoryDot(InventoryTabs.Of(Catalog.FindDefinition(item), HotbarTools.IsTool(item)));

    private static readonly Dictionary<InventoryTab, Texture2D?> TabDots = [];

    // A tab's dot as a small round texture for the button's icon; All has none.
    private static Texture2D? TabDot(InventoryTab tab)
    {
        if (TabDots.TryGetValue(tab, out var known))
            return known;
        Texture2D? texture = null;
        if (UiTokens.CategoryDot(tab) is { } colour)
        {
            const int size = 10;
            var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Antialiased edge: coverage falls off over the last pixel.
                    var d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).Length();
                    image.SetPixel(x, y, colour with { A = Mathf.Clamp(size / 2f - d + 0.5f, 0, 1) });
                }
            }
            texture = ImageTexture.CreateFromImage(image);
        }
        return TabDots[tab] = texture;
    }
}
