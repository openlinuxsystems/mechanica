using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Items;
using Mechanica.Game.Creations;
using Mechanica.Game.Logic;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Items;

/// <summary>
/// The upgrade dialog (UI spec §0 "Upgrade dialog", §4, §8), opened with Upgrade (U) on a part that has upgrade levels,
/// or from the Fabricator screen. A small <c>Card</c> in the station design language on a scrim: the part's name and a
/// <c>Level 1 → 2</c> pill, the part as it is beside the part one level up, what changes (<c>Queue 2 → 4</c>, the new
/// value in green), and a band with the cost as material tiles (have / need, red when short, free in Creative), Close and
/// Upgrade (U), with a reason line saying why Upgrade is off or what the last upgrade did. The host checks and applies it
/// (<c>UpgradePart</c>). Esc, the X, Close or a click on the scrim shuts it.
/// </summary>
public partial class UpgradeDialog : CanvasLayer
{
    /// <summary>The dialog's width at 1280×720.</summary>
    public const float Width = 460;

    private Control _root = null!;
    private PanelContainer _card = null!;
    private Label _title = null!;
    private Label _level = null!;
    private TextureRect _nowPicture = null!;
    private Label _nowCaption = null!;
    private Control _next = null!;
    private TextureRect _nextPicture = null!;
    private Label _nextCaption = null!;
    private Control _changesSection = null!;
    private VBoxContainer _changes = null!;
    private HBoxContainer _cost = null!;
    private Label _costHeading = null!;
    private Label _reason = null!;
    private Button _close = null!;
    private KeyButton _confirm = null!;
    private readonly List<(Label Count, string Name, bool Missing)> _costRows = [];
    private int _shownLevel = -1;
    private string _shownCost = "";
    private (string Text, bool Ok)? _status;

    /// <summary>Inventory sync, for what the player holds. Set before adding to the tree.</summary>
    public InventorySync Inventory { get; init; } = null!;

    /// <summary>Where requests go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>The Creations this peer shows. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Part definitions. Set before adding to the tree.</summary>
    public IPartCatalog Catalog { get; init; } = null!;

    /// <summary>The part under the crosshair in reach, or null. Set before adding to the tree.</summary>
    public Func<(int Creation, int Part)?> Aimed { get; init; } = () => null;

    /// <summary>Item pictures; null shows initials.</summary>
    public ItemIcons? Icons { get; init; }

    /// <summary>True while the dialog is up.</summary>
    public bool IsOpen => _root.Visible;

    /// <summary>The part shown, or null.</summary>
    public (int Creation, int Part)? Target { get; private set; }

    /// <summary>The dialog's card.</summary>
    public Control Card => _card;

    /// <summary>The Upgrade button.</summary>
    public Button Confirm => _confirm;

    /// <summary>The Close button.</summary>
    public Button CloseButton => _close;

    /// <summary>The title (the part's name without its level number).</summary>
    public string TitleText => _title.Text;

    /// <summary>The level pill (<c>Level 1 → 2</c>, or <c>Level 5 / 5</c> at the top).</summary>
    public string LevelText => _level.Text;

    /// <summary>The cost tiles as shown (<c>1 / 5 Component Kit</c>), and whether each is short.</summary>
    public IReadOnlyList<(string Text, bool Missing)> CostLines => [.. _costRows.Select(r => ($"{r.Count.Text} {r.Name}", r.Missing))];

    /// <summary>The change rows as shown (<c>Queue: 2 → 4</c>).</summary>
    public IReadOnlyList<string> ChangeLines =>
        [.. _changes.GetChildren().OfType<Control>().Select(r => r.FindChildren("*", nameof(Label), true, false).OfType<Label>().ToArray())
            .Select(l => $"{l[0].Text}: {l[1].Text} → {l[3].Text}")];

    /// <summary>The reason line: why Upgrade is off, or what the last upgrade did. Empty when there is nothing to say.</summary>
    public string Status => _reason.Visible ? _reason.Text : "";

    public override void _Ready()
    {
        Layer = 6;
        // A scrim under the card: the dialog asks for a decision, so the screen or world behind it waits.
        _root = new Control { Name = "Upgrade", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Theme = StationTheme.Theme };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var scrim = new ColorRect { Name = "Scrim", Color = UiTokens.Scrim, MouseFilter = Control.MouseFilterEnum.Stop };
        scrim.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right })
                Close();
        };
        _root.AddChild(scrim);
        scrim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _card = UiInput.KeepMouse(new PanelContainer
        {
            Name = "Card", ThemeTypeVariation = "MenuCard", ClipChildren = Control.ClipChildrenMode.AndDraw, CustomMinimumSize = new Vector2(Width, 0),
        });
        center.AddChild(_card);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        _card.AddChild(column);
        column.AddChild(BuildHeader());
        column.AddChild(BuildBody());
        column.AddChild(BuildBand());
        EscapeStack.Add(this, () => IsOpen, Close);
        Inventory.ViewChanged += OnViewChanged;
    }

    private Control BuildHeader()
    {
        var header = new PanelContainer { Name = "Header", ThemeTypeVariation = "Header" };
        var row = new HBoxContainer();
        header.AddChild(row);
        // A spacer as wide as the X keeps the title in the middle.
        row.AddChild(new Control { CustomMinimumSize = new Vector2(32, 0) });
        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 6);
        titles.AddChild(new Label { Name = "Kicker", Text = "Upgrade", ThemeTypeVariation = "SectionLabel", Uppercase = true, HorizontalAlignment = HorizontalAlignment.Center });
        _title = Loc.Verbatim(new Label { Name = "Title", ThemeTypeVariation = "DisplayLabel", Uppercase = true, HorizontalAlignment = HorizontalAlignment.Center });
        titles.AddChild(_title);
        var pill = new PanelContainer { Name = "LevelPill", ThemeTypeVariation = "Pill", SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        _level = Loc.Verbatim(new Label { Name = "Level", ThemeTypeVariation = "PillLabel", Uppercase = true });
        pill.AddChild(_level);
        titles.AddChild(pill);
        row.AddChild(titles);
        var close = new Button
        {
            Name = "X", Text = "✕", ThemeTypeVariation = "CloseButton", CustomMinimumSize = new Vector2(32, 32), TooltipText = "Close (Esc)",
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, FocusMode = Control.FocusModeEnum.None,
        };
        close.Pressed += Close;
        row.AddChild(close);
        return header;
    }

    private Control BuildBody()
    {
        var body = new MarginContainer { Name = "Body" };
        foreach (var (side, margin) in new[] { ("left", 24), ("right", 24), ("top", 18), ("bottom", 18) })
            body.AddThemeConstantOverride($"margin_{side}", margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        body.AddChild(column);

        // The part as it is, an arrow, and the part one level up.
        var pictures = new HBoxContainer { Name = "Pictures", Alignment = BoxContainer.AlignmentMode.Center };
        pictures.AddThemeConstantOverride("separation", 14);
        column.AddChild(pictures);
        pictures.AddChild(Picture("Now", out _nowPicture, out _nowCaption));
        _next = new HBoxContainer { Name = "Next" };
        _next.AddThemeConstantOverride("separation", 14);
        _next.AddChild(new NextArrow { Name = "Arrow", CustomMinimumSize = new Vector2(34, 30), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore });
        _next.AddChild(Picture("NextLevel", out _nextPicture, out _nextCaption));
        pictures.AddChild(_next);

        var changes = new VBoxContainer { Name = "ChangesSection" };
        changes.AddThemeConstantOverride("separation", 8);
        _changesSection = changes;
        changes.AddChild(new Label { Text = "What changes", ThemeTypeVariation = "SectionLabel", Uppercase = true });
        _changes = new VBoxContainer { Name = "Changes" };
        _changes.AddThemeConstantOverride("separation", 6);
        changes.AddChild(_changes);
        column.AddChild(changes);
        return body;
    }

    // A level's picture on an inset tile with its caption under it.
    private static Control Picture(string name, out TextureRect picture, out Label caption)
    {
        var box = new VBoxContainer { Name = name };
        box.AddThemeConstantOverride("separation", 6);
        var tile = new PanelContainer { ThemeTypeVariation = "InsetPanel", CustomMinimumSize = new Vector2(132, 116) };
        picture = new TextureRect
        {
            Name = "Picture", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        tile.AddChild(picture);
        box.AddChild(tile);
        caption = Loc.Verbatim(new Label { Name = "Caption", ThemeTypeVariation = "SectionLabel", Uppercase = true, HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(caption);
        return box;
    }

    private Control BuildBand()
    {
        var band = new PanelContainer { Name = "Band", ThemeTypeVariation = "MenuBand" };
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 8);
        band.AddChild(outer);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        outer.AddChild(row);
        var costs = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        costs.AddThemeConstantOverride("separation", 6);
        _costHeading = new Label { Text = "Cost", ThemeTypeVariation = "SectionLabel", Uppercase = true };
        costs.AddChild(_costHeading);
        _cost = new HBoxContainer { Name = "Cost" };
        _cost.AddThemeConstantOverride("separation", 8);
        costs.AddChild(_cost);
        row.AddChild(costs);
        var buttons = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        buttons.AddThemeConstantOverride("separation", 10);
        _close = new Button { Name = "Close", Text = "Close", CustomMinimumSize = new Vector2(84, 44), TooltipText = "Close (Esc)" };
        _close.Pressed += Close;
        buttons.AddChild(_close);
        _confirm = new KeyButton { Name = "Confirm", Label = "Upgrade", ThemeTypeVariation = "PrimaryButton", TooltipText = "Spend the cost and raise the part one level" };
        _confirm.CustomMinimumSize = new Vector2(_confirm.CustomMinimumSize.X, 44);
        _confirm.Pressed += () => Upgrade();
        buttons.AddChild(_confirm);
        row.AddChild(buttons);
        _reason = Loc.Verbatim(new Label { Name = "Reason", ThemeTypeVariation = "ErrorTextLabel", HorizontalAlignment = HorizontalAlignment.Right, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false });
        outer.AddChild(_reason);
        return band;
    }

    public override void _ExitTree()
    {
        Inventory.ViewChanged -= OnViewChanged;
        EscapeStack.Remove(this);
    }

    // The player's own items decide whether the cost is short.
    private void OnViewChanged(InventoryView view)
    {
        if (IsOpen && view.Inventory == InventoryRef.OfPlayer(Inventory.Session.LocalPeer))
            Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!GameInput.Pressed(@event, InputActions.Upgrade) || GameInput.Typing(GetViewport()))
            return;
        // Upgrade (U) is the key on the dialog's Upgrade button; when it cannot upgrade, U closes the dialog again.
        if (IsOpen && !_confirm.Disabled)
            Upgrade();
        else if (IsOpen)
            Close();
        else if (Input.MouseMode == Input.MouseModeEnum.Captured && Aimed() is { } part && CanUpgrade(part))
            Open(part);
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (!IsOpen)
            return;
        if (Target is not { } target || Part(target) is not { } part)
        {
            Close();
            return;
        }
        if (part.Instance.Level != _shownLevel)
            Refresh();
        _confirm.Key = GameInput.Label(InputActions.Upgrade);
    }

    /// <summary>True when the part exists here and has upgrade levels.</summary>
    public bool CanUpgrade((int Creation, int Part) target) => Part(target) is { } part && PartUpgrades.MaxLevel(part.Written) > 0;

    /// <summary>Shows the dialog for a part.</summary>
    public void Open((int Creation, int Part) target)
    {
        Target = target;
        _status = null;
        _shownLevel = -1;
        Refresh();
        _root.Visible = true;
        EscapeStack.Raise(this);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Hides the dialog. The mouse goes back to the game unless another window is up.</summary>
    public void Close()
    {
        if (!IsOpen)
            return;
        _root.Visible = false;
        Target = null;
        if (EscapeStack.Topmost is null && !OtherScreenOpen())
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>A screen under the dialog that keeps the mouse (the Fabricator screen); null for none.</summary>
    public Func<bool> OtherScreenOpen { get; init; } = () => false;

    /// <summary>Asks the host to upgrade the part. False when there is nothing to upgrade.</summary>
    public bool Upgrade(Action<ActionResult>? done = null)
    {
        if (Target is not { } target || Part(target) is not { } part || part.Instance.Level >= PartUpgrades.MaxLevel(part.Written))
            return false;
        var top = PartUpgrades.MaxLevel(part.Written);
        var name = LeverMenu.Title(PartUpgrades.At(part.Written, part.Instance.Level).Name ?? part.Instance.Part, part.Instance.Level, top);
        var level = part.Instance.Level + 2;
        Actions.Request(InventoryActions.UpgradePart, new UpgradePartArgs(target.Creation, target.Part), result =>
        {
            if (!IsInstanceValid(this))
                return;
            _status = result.Ok ? (Loc.T("{0} upgraded to level {1}.", Loc.Mod(name), level), true) : (result.Message ?? "", false);
            Refresh();
            done?.Invoke(result);
        });
        return true;
    }

    private void Refresh()
    {
        if (Target is not { } target || Part(target) is not { } part)
            return;
        var (instance, written) = part;
        var top = PartUpgrades.MaxLevel(written);
        var level = instance.Level;
        var id = Identifiers.Qualify(instance.Part);
        var atTop = level >= top;
        if (level != _shownLevel)
        {
            _shownLevel = level;
            // Players count levels from 1 (the part as made is level 1), as the stations' screen does.
            _title.Text = Loc.Mod(LeverMenu.Title(PartUpgrades.At(written, level).Name ?? instance.Part, level, top));
            _level.Text = atTop ? Loc.T("Level {0} / {1}", level + 1, top + 1) : Loc.T("Level {0} → {1}", level + 1, level + 2);
            _nowPicture.Texture = Icons?.For(PartUpgrades.ItemKey(id, level));
            _nowCaption.Text = atTop ? Loc.T("Top level") : Loc.T("Level {0}", level + 1);
            _next.Visible = !atTop;
            _nextPicture.Texture = atTop ? null : Icons?.For(PartUpgrades.ItemKey(id, level + 1));
            _nextCaption.Text = Loc.T("Level {0}", level + 2);
            Clear(_changes);
            if (!atTop)
            {
                foreach (var change in PartUpgrades.Changes(PartUpgrades.At(written, level), PartUpgrades.At(written, level + 1)))
                    _changes.AddChild(ChangeRow(Loc.T(change.Label), change.From, change.To));
            }
            _changesSection.Visible = _changes.GetChildCount() > 0;
            _shownCost = "";
        }
        _confirm.Key = GameInput.Label(InputActions.Upgrade);
        RefreshCost(written, level, atTop);
    }

    // The next level's cost as material tiles, have / need, red when short; free in Creative.
    private void RefreshCost(PartDefinition written, int level, bool atTop)
    {
        List<RecipeItem> cost = atTop ? [] : written.Upgrades![level].Cost ?? [];
        var free = !atTop && (Inventory.Settings?.FreeUpgrades == true || cost.Count == 0);
        var own = Inventory.Own;
        var counts = cost.Select(c => (Item: Identifiers.Qualify(c.Item!), Need: c.Count ?? 1)).Select(c => (c.Item, c.Need, Have: own?.Count(c.Item) ?? 0)).ToList();
        var key = $"{atTop}|{free}|{string.Join(";", counts)}";
        if (key != _shownCost)
        {
            _shownCost = key;
            Clear(_cost);
            _costRows.Clear();
            _costHeading.Visible = !atTop;
            if (free)
                _cost.AddChild(new Label { Text = cost.Count == 0 ? "Nothing" : "Free in Creative", ThemeTypeVariation = "MutedLabel" });
            else
            {
                foreach (var (item, need, have) in counts)
                    _cost.AddChild(CostTile(item, need, have));
            }
        }
        string? missing = null;
        if (atTop)
            missing = Loc.T("This is the top level.");
        else if (!free)
        {
            foreach (var (item, need, have) in counts)
            {
                if (have < need)
                    missing ??= Loc.T("Needs {0} more {1}.", need - have, Loc.Mod(ItemName(item)));
            }
        }
        _confirm.Disabled = missing is not null;
        // What the last upgrade did shows until something stops the next one.
        var (text, ok) = missing is not null && _status is not { Ok: false } ? (missing, false) : _status ?? ("", true);
        _reason.Text = text;
        _reason.ThemeTypeVariation = ok ? "SuccessTextLabel" : "ErrorTextLabel";
        _reason.Visible = text.Length > 0;
    }

    // One cost item: its picture on an inset tile (ringed in red when short) with have / need under it.
    private Control CostTile(string item, int need, int have)
    {
        var missing = have < need;
        var name = ItemName(item);
        var box = new VBoxContainer { Name = $"Cost_{item.Replace(':', '_')}", CustomMinimumSize = new Vector2(62, 0), TooltipText = Loc.Mod(name) };
        box.AddThemeConstantOverride("separation", 2);
        var tile = new ItemSlot { CustomMinimumSize = new Vector2(62, 56), MouseFilter = Control.MouseFilterEnum.Ignore };
        tile.Show(item, Icons?.For(item), name, null);
        var ring = StationTheme.Box(UiTokens.Inset, StationTheme.TileCorner);
        ring.SetBorderWidthAll(missing ? 2 : 1);
        ring.BorderColor = missing ? UiTokens.ErrorMark : UiTokens.ChromeLine;
        tile.AddThemeStyleboxOverride("panel", ring);
        box.AddChild(tile);
        var count = Loc.Verbatim(new Label { Text = $"{have} / {need}", HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "NumericLabel" });
        if (missing)
            count.AddThemeColorOverride("font_color", UiTokens.ErrorText);
        box.AddChild(count);
        _costRows.Add((count, name, missing));
        return box;
    }

    // One change: its name, the value now, a green arrow and the value one level up in green.
    private static Control ChangeRow(string name, string from, string to)
    {
        var chip = new PanelContainer { ThemeTypeVariation = "InsetPanel" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        chip.AddChild(row);
        row.AddChild(Loc.Verbatim(new Label { Text = name, ThemeTypeVariation = "SectionLabel", Uppercase = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center }));
        var now = Loc.Verbatim(new Label { Text = from, ThemeTypeVariation = "NumericLabel", HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(52, 0) });
        now.AddThemeColorOverride("font_color", UiTokens.TextMuted);
        row.AddChild(now);
        row.AddChild(Loc.Verbatim(new Label { Text = "→", ThemeTypeVariation = "SuccessTextLabel", VerticalAlignment = VerticalAlignment.Center }));
        var next = Loc.Verbatim(new Label { Text = to, ThemeTypeVariation = "NumericLabel", CustomMinimumSize = new Vector2(52, 0) });
        next.AddThemeColorOverride("font_color", UiTokens.SuccessText);
        row.AddChild(next);
        return chip;
    }

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private (Core.Creations.PartInstance Instance, PartDefinition Written)? Part((int Creation, int Part) target) =>
        Creations.Find(target.Creation)?.FindPart(target.Part) is { } instance && Catalog.FindDefinition(instance.Part) is { } written
            ? (instance, written)
            : null;

    private string ItemName(string id) => Catalog.FindDefinition(id)?.Name ?? id;
}

/// <summary>The green arrow between a part's level and the next in the upgrade dialog, drawn like the engine menu's <see cref="UpArrow"/>.</summary>
public partial class NextArrow : Control
{
    public override void _Draw()
    {
        var (w, h) = (Size.X, Size.Y);
        Vector2[] arrow = [new(w, h / 2), new(w * 0.5f, h), new(w * 0.5f, h * 0.68f), new(0, h * 0.68f), new(0, h * 0.32f), new(w * 0.5f, h * 0.32f), new(w * 0.5f, 0)];
        DrawColoredPolygon(arrow, UiTokens.SuccessFill);
        DrawPolyline([.. arrow, arrow[0]], UiTokens.Ink, 2, true);
    }
}
