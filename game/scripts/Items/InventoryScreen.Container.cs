using Godot;
using Mechanica.Core.Items;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Items;

/// <summary>
/// The container screen (UI spec §0 "Chests and containers"): what Use (E) on a container that is not a station opens
/// (a Gas Container, a chest), over the scrim with the hotbar left clear. Two windows are stacked: on top the container,
/// with a chrome header (its picture, name, how full it is and X) over its slots on the surface, and under it the
/// player's inventory, with its own header (fill count and the nearly-full warning) over every slot in rows of ten that
/// scroll. Empty slots of a container that takes only one item (gasoline in a Gas Container) show that item's silhouette.
/// Stacks drag, click and Shift+click between the two exactly as in the plain inventory screen.
/// </summary>
public partial class InventoryScreen
{
    private const int ContainerTile = 76;
    private const int ContainerColumns = 10;
    private const int OwnRowsShown = 3;

    private Control _containerScreen = null!;
    private Control _containerWindow = null!;
    private TextureRect _containerArt = null!;
    private Label _containerTitle = null!;
    private Label _containerFill = null!;
    private Control _containerFillPill = null!;
    private readonly SlotGrid _containerGrid = new(ContainerColumns, ContainerTile, tiled: true);
    private ScrollContainer _containerScroll = null!;
    private VBoxContainer _containerBody = null!;
    private Label _bagFill = null!;
    private Control _bagWarning = null!;
    private Label _bagWarningText = null!;
    private readonly SlotGrid _bagGrid = new(ContainerColumns, ContainerTile, tiled: true);
    private ScrollContainer _bagScroll = null!;

    /// <summary>True while the screen shows an open container (not a station) above the player's inventory.</summary>
    public bool ContainerMode => _open is not null && !StationMode;

    /// <summary>The open container's slots as shown.</summary>
    public IReadOnlyList<ItemSlot> ContainerSlots => _containerGrid.Slots;

    /// <summary>The player's slots under an open container.</summary>
    public IReadOnlyList<ItemSlot> ContainerOwnSlots => _bagGrid.Slots;

    /// <summary>The container header's title and fill text (<c>16 / 50 Gasoline</c>).</summary>
    public (string Title, string Fill) ContainerHeader => (_containerTitle.Text, _containerFill.Text);

    /// <summary>The container window and the inventory window under it.</summary>
    public IReadOnlyList<Control> ContainerPanels => [_containerWindow, (Control)_containerScreen.GetChild(1)];

    private void BuildContainer()
    {
        var screen = new VBoxContainer { Name = "Container", Visible = false, Theme = StationTheme.Theme };
        screen.AddThemeConstantOverride("separation", (int)PanelGap);
        _root.AddChild(screen);
        _containerScreen = screen;

        // The container: its own width (as many tiles as it has slots, up to a row of ten), on the left.
        var window = new PanelContainer { Name = "ContainerWindow", ThemeTypeVariation = "MainWindow", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, ClipChildren = CanvasItem.ClipChildrenMode.AndDraw };
        // Flat windows: no drop shadow haloing them over the world (the scrim already sets them apart).
        var flat = StationTheme.Box(UiTokens.Surface, StationTheme.WindowCorner);
        window.AddThemeStyleboxOverride("panel", flat);
        screen.AddChild(window);
        _containerWindow = window;
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        window.AddChild(column);
        var header = new PanelContainer { Name = "Header", ThemeTypeVariation = "Header" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        header.AddChild(row);
        _containerArt = new TextureRect
        {
            Name = "Art", CustomMinimumSize = new Vector2(88, 72), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        row.AddChild(_containerArt);
        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        titles.AddThemeConstantOverride("separation", 6);
        _containerTitle = Loc.Verbatim(new Label { Name = "ContainerTitle", ThemeTypeVariation = "DisplayLabel", Uppercase = true });
        titles.AddChild(_containerTitle);
        _containerFillPill = new PanelContainer { Name = "FillPill", ThemeTypeVariation = "Pill", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        _containerFill = Loc.Verbatim(new Label { Name = "Fill", ThemeTypeVariation = "PillLabel", Uppercase = true });
        _containerFillPill.AddChild(_containerFill);
        titles.AddChild(_containerFillPill);
        row.AddChild(titles);
        var close = new Button { Name = "Close", Text = "✕", ThemeTypeVariation = "CloseButton", CustomMinimumSize = new Vector2(32, 32), TooltipText = "Close", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        close.Pressed += Hide;
        row.AddChild(close);
        column.AddChild(header);
        var surface = new PanelContainer { Name = "Surface", ThemeTypeVariation = "SurfacePanel" };
        _containerBody = new VBoxContainer();
        _containerBody.AddThemeConstantOverride("separation", 8);
        surface.AddChild(_containerBody);
        _containerScroll = new ScrollContainer { Name = "ContainerScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _containerScroll.AddChild(_containerGrid.Build("ContainerGrid", rows: 1, this, scroll: false));
        _containerBody.AddChild(_containerScroll);
        column.AddChild(surface);

        // The player's inventory, full width, three rows shown and the rest scrolled to.
        var bag = new PanelContainer { Name = "InventoryWindow", ThemeTypeVariation = "MainWindow", ClipChildren = CanvasItem.ClipChildrenMode.AndDraw };
        bag.AddThemeStyleboxOverride("panel", flat);
        screen.AddChild(bag);
        var bagColumn = new VBoxContainer();
        bagColumn.AddThemeConstantOverride("separation", 0);
        bag.AddChild(bagColumn);
        var bagHeader = new PanelContainer { Name = "Header", ThemeTypeVariation = "Header" };
        var bagRow = new HBoxContainer();
        bagRow.AddThemeConstantOverride("separation", 12);
        bagHeader.AddChild(bagRow);
        bagRow.AddChild(new Label { Text = "Inventory", ThemeTypeVariation = "DisplayLabel", Uppercase = true });
        _bagWarning = new PanelContainer { Name = "Warning", ThemeTypeVariation = "WarningPill", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Visible = false };
        _bagWarningText = Loc.Verbatim(new Label { ThemeTypeVariation = "PillLabel", Uppercase = true });
        _bagWarning.AddChild(_bagWarningText);
        bagRow.AddChild(_bagWarning);
        bagRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _bagFill = Loc.Verbatim(new Label { Name = "Fill", ThemeTypeVariation = "SectionLabel", VerticalAlignment = VerticalAlignment.Center, SizeFlagsVertical = Control.SizeFlags.Fill });
        bagRow.AddChild(_bagFill);
        bagColumn.AddChild(bagHeader);
        var bagSurface = new PanelContainer { Name = "Surface", ThemeTypeVariation = "SurfacePanel" };
        _bagScroll = new ScrollContainer { Name = "InventoryScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _bagScroll.AddChild(_bagGrid.Build("InventoryGrid", rows: OwnRowsShown, this, scroll: false));
        bagSurface.AddChild(_bagScroll);
        bagColumn.AddChild(bagSurface);
    }

    // The scroll areas hold whole rows of tiles: the container up to two rows, the inventory three.
    private void SizeContainer(Vector2 screen)
    {
        var view = Inventory.Shown.GetValueOrDefault(_open!);
        var slots = Math.Max(1, view?.SlotCount ?? 1);
        var bagSlots = Inventory.Own?.SlotCount ?? Inventory.Settings!.PlayerSlots;
        var tile = ContainerTile + 8;
        // Short windows (below 720 tall at this scale) show fewer rows rather than reaching under the hotbar.
        var tall = screen.Y >= 700;
        var containerRows = Math.Min(tall ? 2 : 1, (slots + ContainerColumns - 1) / ContainerColumns);
        var bagRows = Math.Min(tall ? OwnRowsShown : 2, (bagSlots + ContainerColumns - 1) / ContainerColumns);
        var scrollbar = (slots + ContainerColumns - 1) / ContainerColumns > containerRows ? 14 : 0;
        _containerScroll.CustomMinimumSize = new Vector2(Math.Min(slots, ContainerColumns) * tile - 8 + scrollbar, containerRows * tile - 8);
        var bagScrollbar = (bagSlots + ContainerColumns - 1) / ContainerColumns > bagRows ? 14 : 0;
        _bagScroll.CustomMinimumSize = new Vector2(ContainerColumns * tile - 8 + bagScrollbar, bagRows * tile - 8);
        // Wide enough for the title over a small container.
        _containerWindow.CustomMinimumSize = new Vector2(420, 0);
    }

    private void RefreshContainer()
    {
        var view = _open is { } where ? Inventory.Shown.GetValueOrDefault(where) : null;
        var id = view?.Part ?? (_open is { Creation: { } c, Part: { } p } ? Creations?.Find(c)?.FindPart(p)?.Part : null);
        _containerTitle.Text = id is null ? Loc.T("Opening…") : ItemName(id);
        _containerArt.Texture = id is null ? null : Icons?.For(id);
        _containerArt.Visible = _containerArt.Texture is not null;
        var only = view?.Accepts is [var single] ? single : null;
        _containerFillPill.Visible = view is not null;
        if (view is not null)
            _containerFill.Text = only is not null
                ? Loc.T("{0} / {1} {2}", view.Count(only), view.SlotCount * Math.Max(1, Catalog.FindDefinition(only)?.StackSize ?? 1), Loc.Mod(ItemName(only)))
                : Loc.T("{0} / {1} slots", view.Items.Count, view.SlotCount);
        _containerGrid.Fill(_open!, [.. Enumerable.Range(0, view?.SlotCount ?? 0).Select(i => (i, view?.At(i)))]);
        _containerGrid.Ghost = only is null ? null : Icons?.For(only);

        var own = Inventory.Own;
        var me = InventoryRef.OfPlayer(Inventory.Session.LocalPeer);
        var slots = own?.SlotCount ?? Inventory.Settings!.PlayerSlots;
        _bagGrid.Fill(me, [.. Enumerable.Range(0, slots).Select(i => (i, own?.At(i)))]);
        var used = own?.Items.Select(s => s.Slot).Distinct().Count() ?? 0;
        _bagFill.Text = $"{used} / {slots}";
        var free = slots - used;
        _bagWarning.Visible = !Creative && own is not null && Inventory.Settings!.NearlyFull(free);
        _bagWarningText.Text = free == 0 ? Loc.T("Inventory full") : Loc.T("Nearly full: {0} free", free);
        _warning.Text = _bagWarningText.Text;
        _warning.Visible = _bagWarning.Visible;
    }
}
