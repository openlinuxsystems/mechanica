using System.Globalization;
using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Game.Creations;
using Mechanica.Game.Items;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Logic;

/// <summary>
/// The menu of a part whose menu has a <see cref="LeverDefinition"/> (an engine, UI spec §12), in the station design
/// language (§0): the part's name and level over its picture, the fuel it has left in a slot under the picture, a tall
/// power lever from Off to the top step (steps the part's level does not reach yet sit locked above a Limit mark), its
/// other readouts (speed, torque), and a band with what the next level brings, its cost and Upgrade (U). Moving the
/// lever sends <c>ConfigurePart</c> (the bottom notch turns the <see cref="LeverDefinition.Off"/> toggle off); Upgrade
/// sends <c>UpgradePart</c>. <see cref="PartSettingsPanel"/> opens and closes it.
/// </summary>
public partial class LeverMenu : PanelContainer
{
    /// <summary>The menu's width at 1280×720.</summary>
    public const float Width = 420;

    private readonly List<(ReadoutDefinition Readout, Label Value)> _stats = [];
    private Label _title = null!;
    private Control _levelPill = null!;
    private Label _level = null!;
    private TextureRect _picture = null!;
    private Control _fuel = null!;
    private TextureRect _fuelIcon = null!;
    private Label _fuelCount = null!;
    private MeterBar _fuelBar = null!;
    private PowerLever _lever = null!;
    private HFlowContainer _statRow = null!;
    private Label _hint = null!;
    private Control _band = null!;
    private TextureRect _nextPicture = null!;
    private VBoxContainer _gains = null!;
    private KeyButton _upgrade = null!;
    private HBoxContainer _cost = null!;
    private Label _reason = null!;
    private int _shownLevel = -1;
    private int _seenValue = int.MinValue;
    private string? _status;

    /// <summary>Where changes go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>The Creations, for the part. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>What part scripts show; null shows none.</summary>
    public Func<PartReadouts?>? Readouts { get; init; }

    /// <summary>The player's items, for the upgrade's cost; null counts nothing.</summary>
    public InventorySync? Inventory { get; init; }

    /// <summary>Item pictures; null shows none.</summary>
    public ItemIcons? Icons { get; init; }

    /// <summary>True while the local player plays in Creative, where upgrades are free; null asks the inventory settings.</summary>
    public Func<bool>? Creative { get; init; }

    /// <summary>The part shown, or null.</summary>
    public (int Creation, int Part)? Target { get; private set; }

    /// <summary>The power lever.</summary>
    public PowerLever Lever => _lever;

    /// <summary>The title as shown (the part's name).</summary>
    public string TitleText => _title.Text;

    /// <summary>The level pill's text (<c>Level 2 / 5</c>), empty when the part has no levels.</summary>
    public string LevelText => _levelPill.Visible ? _level.Text : "";

    /// <summary>The fuel slot's count, empty when the part shows no fuel.</summary>
    public string FuelText => _fuel.Visible ? _fuelCount.Text : "";

    /// <summary>The fuel bar's fill (0 to 1).</summary>
    public float FuelBar => _fuelBar.Value;

    /// <summary>The readouts beside the lever as shown (<c>Speed 120 rpm</c>).</summary>
    public IReadOnlyList<string> StatTexts => [.. _stats.Select(s => $"{s.Readout.Name ?? s.Readout.Id} {s.Value.Text}")];

    /// <summary>True while the upgrade band shows (the part has a next level).</summary>
    public bool UpgradeShowing => _band.Visible;

    /// <summary>The upgrade band's gains as shown (<c>Bearings +2</c>).</summary>
    public IReadOnlyList<string> GainTexts => [.. _gains.GetChildren().OfType<HBoxContainer>().Select(r => string.Join(" ", r.GetChildren().OfType<Label>().Select(l => l.Text)))];

    /// <summary>The upgrade's cost as shown (<c>0/2</c>), and whether each is short.</summary>
    public IReadOnlyList<(string Text, bool Short)> CostTexts =>
        [.. _cost.GetChildren().OfType<HBoxContainer>().Select(r => r.GetChild<Label>(1)).Select(l => (l.Text, l.ThemeTypeVariation == "ErrorTextLabel"))];

    /// <summary>The Upgrade button.</summary>
    public Button UpgradeButton => _upgrade;

    /// <summary>The reason line under Upgrade (why it cannot, or what the last upgrade did).</summary>
    public string Reason => _reason.Visible ? _reason.Text : "";

    /// <summary>Raised by the X.</summary>
    public event Action? CloseRequested;

    public LeverMenu()
    {
        Name = "LeverMenu";
        Theme = StationTheme.Theme;
        ThemeTypeVariation = "MenuCard";
        ClipChildren = ClipChildrenMode.AndDraw;
        CustomMinimumSize = new Vector2(Width, 0);
        UiInput.KeepMouse(this);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        AddChild(column);
        column.AddChild(BuildHeader());
        column.AddChild(BuildBody());
        column.AddChild(BuildBand());
    }

    private Control BuildHeader()
    {
        var header = new PanelContainer { Name = "Header", ThemeTypeVariation = "Header" };
        var row = new HBoxContainer();
        header.AddChild(row);
        // A spacer as wide as the X keeps the title in the middle.
        row.AddChild(new Control { CustomMinimumSize = new Vector2(32, 0) });
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 6);
        _title = Loc.Verbatim(new Label { Name = "Title", ThemeTypeVariation = "DisplayLabel", Uppercase = true, HorizontalAlignment = HorizontalAlignment.Center });
        titles.AddChild(_title);
        _levelPill = new PanelContainer { Name = "LevelPill", ThemeTypeVariation = "Pill", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        _level = Loc.Verbatim(new Label { Name = "Level", ThemeTypeVariation = "PillLabel", Uppercase = true });
        _levelPill.AddChild(_level);
        titles.AddChild(_levelPill);
        row.AddChild(titles);
        var close = new Button
        {
            Name = "Close", Text = "✕", ThemeTypeVariation = "CloseButton", CustomMinimumSize = new Vector2(32, 32), TooltipText = "Close (Esc)",
            SizeFlagsVertical = SizeFlags.ShrinkBegin, FocusMode = FocusModeEnum.None,
        };
        close.Pressed += () => CloseRequested?.Invoke();
        row.AddChild(close);
        return header;
    }

    private Control BuildBody()
    {
        var body = new MarginContainer { Name = "Body" };
        foreach (var (side, margin) in new[] { ("left", 24), ("right", 24), ("top", 16), ("bottom", 16) })
            body.AddThemeConstantOverride($"margin_{side}", margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        body.AddChild(column);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 28);
        column.AddChild(row);

        var left = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        left.AddThemeConstantOverride("separation", 14);
        row.AddChild(left);
        _picture = new TextureRect
        {
            Name = "Picture", CustomMinimumSize = new Vector2(132, 112), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        left.AddChild(_picture);
        var fuel = new VBoxContainer { Name = "Fuel" };
        fuel.AddThemeConstantOverride("separation", 6);
        _fuel = fuel;
        var slot = new FuelSlot { Name = "FuelSlot", CustomMinimumSize = new Vector2(132, 92), MouseFilter = MouseFilterEnum.Stop };
        fuel.AddChild(slot);
        var inside = new Control { MouseFilter = MouseFilterEnum.Ignore };
        slot.AddChild(inside);
        _fuelIcon = new TextureRect
        {
            Name = "FuelIcon", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        inside.AddChild(_fuelIcon);
        _fuelIcon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        (_fuelIcon.OffsetLeft, _fuelIcon.OffsetTop, _fuelIcon.OffsetRight, _fuelIcon.OffsetBottom) = (18, 4, -18, -4);
        _fuelCount = Loc.Verbatim(new Label { Name = "FuelCount", ThemeTypeVariation = "NumericLabel", HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = MouseFilterEnum.Ignore });
        _fuelCount.AddThemeFontSizeOverride("font_size", 20);
        inside.AddChild(_fuelCount);
        _fuelCount.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        (_fuelCount.GrowHorizontal, _fuelCount.GrowVertical) = (GrowDirection.Begin, GrowDirection.Begin);
        _fuelBar = new MeterBar { Name = "FuelBar", CustomMinimumSize = new Vector2(0, 8), MouseFilter = MouseFilterEnum.Stop };
        fuel.AddChild(_fuelBar);
        left.AddChild(fuel);

        _lever = new PowerLever { Name = "PowerLever", CustomMinimumSize = new Vector2(150, 260) };
        _lever.Committed += Commit;
        row.AddChild(_lever);

        _statRow = new HFlowContainer { Name = "Stats", Alignment = FlowContainer.AlignmentMode.Center };
        _statRow.AddThemeConstantOverride("h_separation", 8);
        column.AddChild(_statRow);
        _hint = Loc.Verbatim(new Label { Name = "Hint", HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(_hint);
        return body;
    }

    private Control BuildBand()
    {
        var band = new PanelContainer { Name = "UpgradeBand", ThemeTypeVariation = "MenuBand" };
        _band = band;
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 6);
        band.AddChild(outer);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        outer.AddChild(row);
        var next = new Control { CustomMinimumSize = new Vector2(76, 76), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _nextPicture = new TextureRect
        {
            Name = "NextPicture", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TooltipText = "The next level",
        };
        next.AddChild(_nextPicture);
        _nextPicture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var arrow = new UpArrow { Name = "UpArrow", CustomMinimumSize = new Vector2(26, 30), MouseFilter = MouseFilterEnum.Ignore };
        next.AddChild(arrow);
        arrow.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        (arrow.GrowHorizontal, arrow.GrowVertical) = (GrowDirection.Begin, GrowDirection.Begin);
        row.AddChild(next);
        _gains = new VBoxContainer { Name = "Gains", SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        _gains.AddThemeConstantOverride("separation", 0);
        row.AddChild(_gains);
        var action = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        action.AddThemeConstantOverride("separation", 6);
        _upgrade = new KeyButton { Name = "Upgrade", Label = "Upgrade", ThemeTypeVariation = "PrimaryButton", TooltipText = "Spend the cost and raise the part one level" };
        _upgrade.CustomMinimumSize = new Vector2(_upgrade.CustomMinimumSize.X, 44);
        _upgrade.Pressed += () => Upgrade();
        action.AddChild(_upgrade);
        _cost = new HBoxContainer { Name = "Cost", Alignment = BoxContainer.AlignmentMode.Center };
        _cost.AddThemeConstantOverride("separation", 10);
        action.AddChild(_cost);
        row.AddChild(action);
        _reason = Loc.Verbatim(new Label { Name = "Reason", ThemeTypeVariation = "ErrorTextLabel", HorizontalAlignment = HorizontalAlignment.Right, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false });
        outer.AddChild(_reason);
        return band;
    }

    /// <summary>Shows the menu of a part with a lever. False when the part has none.</summary>
    public bool Open(int creationId, int partId)
    {
        if (Creations.Find(creationId) is not { } creation || creation.FindPart(partId) is null || creation.DefinitionOf(partId).Menu?.Lever is null)
            return false;
        Target = (creationId, partId);
        _shownLevel = -1;
        _seenValue = int.MinValue;
        _status = null;
        Visible = true;
        Refresh();
        return true;
    }

    /// <summary>Hides the menu.</summary>
    public void Close()
    {
        Visible = false;
        Target = null;
        _lever.CancelDrag();
    }

    /// <summary>Follows the part: its level, settings, readouts and the player's items. Called each frame while open.</summary>
    public void Refresh()
    {
        if (Target is not { } target || Creations.Find(target.Creation) is not { } creation || creation.FindPart(target.Part) is not { } part)
            return;
        var definition = creation.DefinitionOf(target.Part);
        var written = creation.Catalog.FindDefinition(part.Part) ?? definition;
        var lever = definition.Menu!.Lever!;
        if (part.Level != _shownLevel)
            Build(creation, part, definition, written, lever);
        var id = Identifiers.Qualify(part.Part);
        _picture.Texture ??= Icons?.For(PartUpgrades.ItemKey(id, part.Level));

        // The lever follows the part's settings when they change (another player, the host's answer), not while dragged.
        var value = LeverValue(definition, part, lever);
        if (value != _seenValue && !_lever.Dragging)
        {
            _seenValue = value;
            _lever.Value = value;
        }

        var readouts = Readouts?.Invoke();
        if (lever.Fuel is { } fuel)
        {
            var fuelId = Identifiers.Qualify(fuel);
            _fuelIcon.Texture ??= Icons?.For(fuelId);
            var count = lever.FuelCount is { } c ? readouts?.Number(creation.Id, target.Part, c) : null;
            var empty = count is not > 0;
            _fuelCount.Text = count is { } n ? n.ToString("0", CultureInfo.InvariantCulture) : "—";
            _fuelCount.ThemeTypeVariation = empty ? "ErrorTextLabel" : "NumericLabel";
            _fuelIcon.Modulate = Colors.White with { A = empty ? 0.35f : 1f };
            _fuelBar.Value = lever.FuelBar is { } b ? readouts?.Number(creation.Id, target.Part, b) ?? 0 : 0;
        }
        foreach (var (readout, label) in _stats)
        {
            var number = readouts?.Number(creation.Id, target.Part, readout.Id!);
            label.Text = readouts?.Text(creation.Id, target.Part, readout.Id!) ?? (number is { } n
                ? $"{n.ToString($"F{readout.Decimals ?? 0}", CultureInfo.InvariantCulture)}{(readout.Unit is { } unit ? $" {unit}" : "")}"
                : "—");
        }
        RefreshCost(creation.Catalog, written, part.Level);
    }

    // The part's level changed (or the menu opened): its name, picture, lever steps and next level.
    private void Build(Creation creation, PartInstance part, PartDefinition definition, PartDefinition written, LeverDefinition lever)
    {
        _shownLevel = part.Level;
        var top = PartUpgrades.MaxLevel(written);
        _title.Text = Title(definition.Name ?? part.Part, part.Level, top);
        _levelPill.Visible = top > 0;
        _level.Text = Loc.T("Level {0} / {1}", part.Level + 1, top + 1);
        var id = Identifiers.Qualify(part.Part);
        _picture.Texture = Icons?.For(PartUpgrades.ItemKey(id, part.Level));

        var setting = PartSettings.Find(definition, lever.Setting!)!;
        var highest = PartSettings.Find(PartUpgrades.At(written, top), lever.Setting!) ?? setting;
        _lever.Configure(lever.Off is null ? (int)(setting.Min ?? 0) : 0, (int)(setting.Max ?? 1), (int)(highest.Max ?? setting.Max ?? 1), lever.Off is not null);
        _hint.Text = lever.Hint is { } hint ? Loc.Mod(hint) : Loc.T("Drag to adjust the power");

        _fuel.Visible = lever.Fuel is not null;
        _fuelIcon.Texture = lever.Fuel is { } fuel ? Icons?.For(Identifiers.Qualify(fuel)) : null;
        if (lever.Fuel is { } f)
            _fuel.GetChild<Control>(0).TooltipText = Loc.T("{0} left in the tanks it burns from", Loc.Mod(creation.Catalog.FindDefinition(f)?.Name ?? f));
        _fuelBar.TooltipText = Loc.T("What is left of the {0} burning now", Loc.Mod(creation.Catalog.FindDefinition(lever.Fuel ?? "")?.Name ?? lever.Fuel ?? ""));

        foreach (var child in _statRow.GetChildren())
        {
            _statRow.RemoveChild(child);
            child.QueueFree();
        }
        _stats.Clear();
        foreach (var readout in definition.Menu!.Readouts ?? [])
        {
            if (readout.Id == lever.FuelCount || readout.Id == lever.FuelBar || readout.Kind == ReadoutKind.Bar)
                continue;
            var chip = new PanelContainer { Name = $"Stat_{readout.Id}", ThemeTypeVariation = "InsetPanel" };
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            chip.AddChild(row);
            row.AddChild(Loc.Verbatim(new Label { Text = readout.Name ?? readout.Id, ThemeTypeVariation = "SectionLabel", Uppercase = true, VerticalAlignment = VerticalAlignment.Center }));
            var value = Loc.Verbatim(new Label { Name = "Value", ThemeTypeVariation = "NumericLabel", VerticalAlignment = VerticalAlignment.Center });
            row.AddChild(value);
            _statRow.AddChild(chip);
            _stats.Add((readout, value));
        }

        foreach (var child in _gains.GetChildren())
        {
            _gains.RemoveChild(child);
            child.QueueFree();
        }
        _band.Visible = part.Level < top;
        if (part.Level >= top)
            return;
        var now = PartUpgrades.At(written, part.Level);
        var next = PartUpgrades.At(written, part.Level + 1);
        _nextPicture.Texture = Icons?.For(PartUpgrades.ItemKey(id, part.Level + 1));
        if (lever.UpgradeStats is { Count: > 0 } stats)
        {
            foreach (var stat in stats)
            {
                if (PartUpgrades.Gain(now, next, stat.Value, stat.Show ?? UpgradeStatShow.Difference) is { } gain)
                    _gains.AddChild(GainRow(Loc.Mod(stat.Name ?? stat.Value ?? ""), gain));
            }
        }
        else
        {
            foreach (var change in PartUpgrades.Changes(now, next))
                _gains.AddChild(GainRow(Loc.T(change.Label), $"{change.From} → {change.To}"));
        }
    }

    private static HBoxContainer GainRow(string name, string gain)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(Loc.Verbatim(new Label { Text = name, ThemeTypeVariation = "MutedLabel" }));
        row.AddChild(Loc.Verbatim(new Label { Text = gain, ThemeTypeVariation = "SuccessTextLabel" }));
        return row;
    }

    // The next level's cost, have / need, red when short; free in Creative.
    private void RefreshCost(IPartCatalog catalog, PartDefinition written, int level)
    {
        if (!_band.Visible)
            return;
        _upgrade.Key = GameInput.Label(InputActions.Upgrade);
        var cost = written.Upgrades![level].Cost ?? [];
        var free = (Creative?.Invoke() ?? Inventory?.Settings?.FreeUpgrades == true) || cost.Count == 0;
        var own = Inventory?.Own;
        var rows = _cost.GetChildren().OfType<HBoxContainer>().ToList();
        if (rows.Count != (free ? 0 : cost.Count))
        {
            foreach (var child in _cost.GetChildren())
            {
                _cost.RemoveChild(child);
                child.QueueFree();
            }
            rows.Clear();
            if (free)
                _cost.AddChild(new Label { Text = "Free in Creative", ThemeTypeVariation = "MutedLabel" });
            else
            {
                foreach (var _ in cost)
                {
                    var row = new HBoxContainer();
                    row.AddThemeConstantOverride("separation", 4);
                    row.AddChild(new TextureRect
                    {
                        CustomMinimumSize = new Vector2(24, 24), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    });
                    row.AddChild(Loc.Verbatim(new Label { VerticalAlignment = VerticalAlignment.Center }));
                    _cost.AddChild(row);
                    rows.Add(row);
                }
            }
        }
        string? missing = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var item = Identifiers.Qualify(cost[i].Item!);
            var need = cost[i].Count ?? 1;
            var have = own?.Count(item) ?? 0;
            var picture = rows[i].GetChild<TextureRect>(0);
            picture.Texture ??= Icons?.For(item);
            var name = Loc.Mod(catalog.FindDefinition(item)?.Name ?? item);
            picture.TooltipText = name;
            var label = rows[i].GetChild<Label>(1);
            label.Text = $"{have}/{need}";
            label.ThemeTypeVariation = have < need ? "ErrorTextLabel" : "NumericLabel";
            if (have < need)
                missing ??= Loc.T("Needs {0} more ({1}).", need - have, name);
        }
        _upgrade.Disabled = missing is not null;
        _reason.Text = _status ?? missing ?? "";
        _reason.ThemeTypeVariation = _status is not null && missing is null ? "SuccessTextLabel" : "ErrorTextLabel";
        _reason.Visible = _reason.Text.Length > 0;
    }

    /// <summary>Asks the host to raise the part one level. False when it is at the top or the cost is short.</summary>
    public bool Upgrade(Action<ActionResult>? done = null)
    {
        if (Target is not { } target || !_band.Visible || _upgrade.Disabled)
            return false;
        _status = null;
        Actions.Request(InventoryActions.UpgradePart, new UpgradePartArgs(target.Creation, target.Part), result =>
        {
            if (!IsInstanceValid(this))
                return;
            _status = result.Ok ? null : result.Message;
            done?.Invoke(result);
        });
        return true;
    }

    // The lever moved to a new step: the setting, and On or Off, go to the host.
    private void Commit(int value)
    {
        if (Target is not { } target || Creations.Find(target.Creation) is not { } creation || creation.FindPart(target.Part) is not { } part)
            return;
        var lever = creation.DefinitionOf(target.Part).Menu!.Lever!;
        var changes = new Dictionary<string, JsonElement?>();
        if (lever.Off is { } off)
            changes[off] = JsonSerializer.SerializeToElement(value > 0);
        if (value > 0 || lever.Off is null)
            changes[lever.Setting!] = JsonSerializer.SerializeToElement(value);
        _seenValue = value;
        Actions.Request(LogicActions.ConfigurePart, new ConfigurePartArgs(target.Creation, target.Part, changes), result =>
        {
            if (IsInstanceValid(this) && !result.Ok)
            {
                _status = result.Message;
                _seenValue = int.MinValue;
            }
        });
    }

    /// <summary>Where the lever stands for a part's settings: 0 when its Off toggle is off, otherwise its setting.</summary>
    public static int LeverValue(PartDefinition definition, PartInstance part, LeverDefinition lever)
    {
        if (lever.Off is { } off && PartSettings.Value(definition, part.Settings, off) is { ValueKind: JsonValueKind.False })
            return 0;
        return PartSettings.Value(definition, part.Settings, lever.Setting!) is { ValueKind: JsonValueKind.Number } v ? (int)Math.Round(v.GetDouble()) : 0;
    }

    /// <summary>The menu's title: the part's name without the level number an upgraded name ends in (<c>Gas Engine 4</c> is <c>Gas Engine</c>).</summary>
    public static string Title(string name, int level, int top)
    {
        var suffix = $" {level + 1}";
        return top > 0 && name.EndsWith(suffix, StringComparison.Ordinal) ? name[..^suffix.Length] : name;
    }
}

/// <summary>
/// A tall power lever (UI spec §12): a track from Off (or the lowest step) at the bottom to <see cref="Top"/>, a tick
/// per step, filled in <c>Accent</c> up to the handle. Steps above <see cref="Limit"/> are hatched and locked, with a
/// Limit mark, until the part is upgraded. Drag the handle or click the track to pick a step; the wheel steps by one.
/// <see cref="Committed"/> is raised when a new step is picked (on release while dragging).
/// </summary>
public partial class PowerLever : Control
{
    private const float TrackWidth = 46, HandleHeight = 26, Pad = 16, TickGap = 10, TickLength = 12;
    private int _value, _min, _limit = 1, _top = 1;
    private bool _hasOff;

    /// <summary>Raised with the new step when the player picks one.</summary>
    public event Action<int>? Committed;

    /// <summary>The step shown.</summary>
    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, _min, _limit);
            QueueRedraw();
        }
    }

    /// <summary>The lowest step (0 is Off).</summary>
    public int Min => _min;

    /// <summary>The highest step the part's level reaches.</summary>
    public int Limit => _limit;

    /// <summary>The highest step at the part's last level (the lever's MAX).</summary>
    public int Top => _top;

    /// <summary>True while the player drags the handle.</summary>
    public bool Dragging { get; private set; }

    public PowerLever()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "Drag to set the power";
    }

    /// <summary>Sets the steps: <paramref name="min"/> to <paramref name="limit"/> can be picked, up to <paramref name="top"/> show.</summary>
    public void Configure(int min, int limit, int top, bool hasOff)
    {
        (_min, _limit, _top, _hasOff) = (min, Math.Max(min, limit), Math.Max(Math.Max(min, limit), top), hasOff);
        Value = _value;
    }

    /// <summary>Picks a step as the player would.</summary>
    public void Pick(int value)
    {
        Value = value;
        Committed?.Invoke(_value);
    }

    /// <summary>Stops a drag without picking.</summary>
    public void CancelDrag() => Dragging = false;

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                if (click.Pressed)
                {
                    Dragging = true;
                    Value = StepAt(click.Position.Y);
                }
                else if (Dragging)
                {
                    Dragging = false;
                    Pick(StepAt(click.Position.Y));
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when Dragging:
                Value = StepAt(motion.Position.Y);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                var step = wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
                if (Math.Clamp(_value + step, _min, _limit) != _value)
                    Pick(_value + step);
                AcceptEvent();
                break;
        }
    }

    // The track's top and bottom (the handle's centre at the top step and at the lowest).
    private (float Top, float Bottom) Span() => (Pad + HandleHeight / 2, Size.Y - Pad - HandleHeight / 2);

    private float YOf(int step)
    {
        var (top, bottom) = Span();
        return _top == _min ? bottom : bottom - (step - _min) * (bottom - top) / (_top - _min);
    }

    private int StepAt(float y)
    {
        var (top, bottom) = Span();
        var step = _top == _min ? _min : _min + (int)Math.Round((bottom - y) / (bottom - top) * (_top - _min));
        return Math.Clamp(step, _min, _limit);
    }

    public override void _Draw()
    {
        var x = 0f;
        var track = new Rect2(x, Pad - 4, TrackWidth, Size.Y - 2 * Pad + 8);
        DrawStyleBox(StationTheme.Box(UiTokens.Inset, StationTheme.ButtonCorner), track);
        var inner = track.Grow(-5);
        DrawStyleBox(StationTheme.Box(UiTokens.Ink, 7), inner);
        // Locked steps above the limit: hatched.
        if (_limit < _top)
        {
            var lockedBottom = (YOf(_limit) + YOf(_limit + 1)) / 2;
            var locked = new Rect2(inner.Position, inner.Size.X, lockedBottom - inner.Position.Y);
            for (var d = -locked.Size.Y; d < locked.Size.X; d += 7)
            {
                var a = new Vector2(locked.Position.X + Math.Max(d, 0), locked.Position.Y + Math.Max(-d, 0));
                var bLen = Math.Min(locked.Size.X - Math.Max(d, 0), locked.Size.Y - Math.Max(-d, 0));
                DrawLine(a, a + new Vector2(bLen, bLen), UiTokens.ChromeLine, 1.5f, true);
            }
            DrawDashedLine(new Vector2(inner.Position.X + 2, lockedBottom), new Vector2(inner.End.X - 2, lockedBottom), UiTokens.Warning, 1.5f, 3);
        }
        // The fill: Accent from the bottom up to the handle, deeper at the bottom.
        var handleY = YOf(_value);
        if (_value > _min || !_hasOff)
        {
            var fill = new Rect2(inner.Position.X + 2, handleY, inner.Size.X - 4, inner.End.Y - 2 - handleY);
            if (fill.Size.Y > 0)
            {
                Color[] colors = [UiTokens.Accent, UiTokens.Accent, UiTokens.SurfaceDeep, UiTokens.SurfaceDeep];
                DrawPolygon([fill.Position, new Vector2(fill.End.X, fill.Position.Y), fill.End, new Vector2(fill.Position.X, fill.End.Y)], colors);
            }
        }
        // Ticks and labels.
        var font = UiFonts.Tracked(UiFonts.CondensedBold, 2);
        var tickX = track.End.X + TickGap;
        for (var step = _min; step <= _top; step++)
        {
            var y = YOf(step);
            var mark = step == _limit && _limit < _top;
            var color = mark ? UiTokens.Warning : step > _limit ? UiTokens.ChromeLine : UiTokens.TextMuted;
            var length = step == _min || step == _top || mark ? TickLength + 4 : TickLength;
            DrawLine(new Vector2(tickX, y), new Vector2(tickX + length, y), color, mark ? 2 : 1.5f, true);
            var label = step == _top ? "MAX" : mark ? "LIMIT" : step == _min ? (_hasOff ? "OFF" : _min.ToString(CultureInfo.InvariantCulture)) : null;
            if (label is not null)
                DrawString(font, new Vector2(tickX + length + 6, y + 5), Loc.T(label), HorizontalAlignment.Left, -1, 13, mark ? UiTokens.Warning : UiTokens.TextMuted);
        }
        // The handle: a pale cap wider than the track with a pointer at the ticks, on a darker lip.
        var handle = new Rect2(x - 6, handleY - HandleHeight / 2, TrackWidth + 12, HandleHeight);
        DrawStyleBox(StationTheme.Box(UiTokens.TileSelected, 7, edge: 3), handle);
        DrawRect(new Rect2(handle.Position.X + 6, handle.Position.Y + 4, handle.Size.X - 12, 2), Colors.White with { A = 0.6f });
        DrawColoredPolygon([new Vector2(handle.End.X, handleY - 6), new Vector2(handle.End.X + 7, handleY), new Vector2(handle.End.X, handleY + 6)], UiTokens.TileSelected);
        var text = _value == 0 && _hasOff ? Loc.T("OFF") : _value.ToString(CultureInfo.InvariantCulture);
        DrawString(UiFonts.CondensedBold, new Vector2(handle.Position.X, handleY + 6), text, HorizontalAlignment.Center, handle.Size.X, 16, UiTokens.Ink);
    }
}

/// <summary>A thin bar filled in <c>Accent</c> from 0 to 1 on an <c>Inset</c> track (fuel or charge left).</summary>
public partial class MeterBar : Control
{
    private float _value;

    /// <summary>The fill, 0 to 1.</summary>
    public float Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (clamped == _value)
                return;
            _value = clamped;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var corner = (int)(Size.Y / 2);
        DrawStyleBox(StationTheme.Box(UiTokens.Inset, corner), new Rect2(Vector2.Zero, Size));
        if (_value > 0)
            DrawStyleBox(StationTheme.Box(UiTokens.Accent, corner), new Rect2(Vector2.Zero, new Vector2(Math.Max(Size.Y, Size.X * _value), Size.Y)));
    }
}

/// <summary>The fuel slot under a lever menu's picture: an <c>Inset</c> tile with a notch pointing up at the part.</summary>
public partial class FuelSlot : PanelContainer
{
    public FuelSlot() => ThemeTypeVariation = "InsetPanel";

    public override void _Draw()
    {
        var middle = Size.X / 2;
        DrawColoredPolygon([new Vector2(middle - 10, 0), new Vector2(middle, -10), new Vector2(middle + 10, 0)], UiTokens.Inset);
    }
}

/// <summary>The green up arrow on the next level's picture.</summary>
public partial class UpArrow : Control
{
    public override void _Draw()
    {
        var (w, h) = (Size.X, Size.Y);
        Vector2[] arrow = [new(w / 2, 0), new(w, h * 0.5f), new(w * 0.68f, h * 0.5f), new(w * 0.68f, h), new(w * 0.32f, h), new(w * 0.32f, h * 0.5f), new(0, h * 0.5f)];
        DrawColoredPolygon(arrow, UiTokens.SuccessFill);
        DrawPolyline([.. arrow, arrow[0]], UiTokens.Ink, 2, true);
    }
}
