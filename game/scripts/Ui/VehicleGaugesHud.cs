using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Ui;
using Mechanica.Game.Items;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Ui;

/// <summary>
/// The vehicle gauges (bottom left while the player sits in any seat): how full each container on their Creation is
/// that feeds a gauge, built like the health bar (<see cref="Survival.SurvivalHud"/>): the same translucent panel, a
/// <see cref="GaugeIcon"/> beside a 240 × 18 bar with a shaded fill and outlined numbers. Kinds stack in
/// <see cref="GaugeKind"/> order, never mixed, with a little more room between kinds. Options, Interface, Vehicle gauges
/// shows one row per container with its name (<see cref="GaugeNames"/>), one per kind summed with no name, or none. A row
/// under <see cref="HudSettings.GaugeLow"/> pulses its track red. The rows come from the host (<see cref="GaugeSync"/>).
/// </summary>
public partial class VehicleGaugesHud : CanvasLayer
{
    // Pixels between rows, and the extra between kinds.
    private const int RowGap = 4;
    private const int KindGap = 4;

    private readonly List<Row> _rows = [];
    private VBoxContainer _root = null!;
    private PanelContainer _panel = null!;
    private VBoxContainer _groups = null!;
    private IReadOnlyList<GaugeRow> _shown = [];
    private string _layout = "";
    private double _time;

    /// <summary>Where the rows come from. Set before adding to the tree.</summary>
    public GaugeSync Gauges { get; init; } = null!;

    /// <summary>How the rows show; tests set it (the player's setting otherwise).</summary>
    public Func<VehicleGaugeMode> Mode { get; init; } = () => GameSettings.Current.Interface.VehicleGauges;

    /// <summary>The panel, or null before it is built; for tests.</summary>
    public Control Panel => _panel;

    /// <summary>True while the gauges show.</summary>
    public bool Showing => _panel.Visible && Visible;

    /// <summary>The rows as shown: kind, the text on the left (a name, or empty), the amount, and whether it pulses.</summary>
    public IEnumerable<(GaugeKind Kind, string Name, string Amount, bool Low)> Shown =>
        _rows.Select(r => (r.Kind, r.Name.Text, r.Amount.Text, r.Low));

    /// <summary>
    /// How far up from the bottom of the screen the panel's top is, in pixels, or 0 while it is hidden: the chat sits
    /// above it.
    /// </summary>
    public float Top => _panel.Visible && Visible ? GetViewport().GetVisibleRect().Size.Y - _root.GetGlobalRect().Position.Y : 0;

    public override void _Ready()
    {
        AddToGroup(HudRoot.Group);
        _root = new VBoxContainer { Name = "Gauges", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_root);
        _panel = new PanelContainer { Name = "GaugePanel", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _panel.AddThemeStyleboxOverride("panel", MechanicaTheme.Box(UiTokens.Scrim, margin: 6));
        _root.AddChild(_panel);
        _groups = new VBoxContainer();
        _groups.AddThemeConstantOverride("separation", RowGap + KindGap);
        _panel.AddChild(_groups);
        Gauges.Changed += Refresh;
        GetViewport().SizeChanged += Layout;
        Refresh();
        Layout();
    }

    public override void _ExitTree()
    {
        Gauges.Changed -= Refresh;
        GetViewport().SizeChanged -= Layout;
    }

    // Bottom left, inside the safe area, growing up.
    private void Layout()
    {
        var margin = HudRoot.Margin(GetViewport());
        _root.AnchorLeft = _root.AnchorRight = 0;
        _root.AnchorTop = _root.AnchorBottom = 1;
        _root.GrowHorizontal = Control.GrowDirection.End;
        _root.GrowVertical = Control.GrowDirection.Begin;
        _root.OffsetLeft = _root.OffsetRight = margin.X;
        _root.OffsetTop = _root.OffsetBottom = -margin.Y;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_layout != Key(Mode()))
            Refresh();
        _panel.Visible = _rows.Count > 0;
        var hz = HudRoot.Settings.GaugePulseHz;
        var pulse = 0.5f - 0.5f * Mathf.Cos((float)(_time * hz * Mathf.Tau));
        foreach (var row in _rows)
            row.Track.BgColor = row.Low ? Track.Lerp(UiTokens.Error with { A = 0.6f }, pulse) : Track;
    }

    private static readonly Color Track = UiTokens.DeepNavy with { A = 0.7f };

    // Shows what the host sent, in the player's mode, building rows again only when the set of rows changes.
    private void Refresh()
    {
        var mode = Mode();
        _shown = mode switch
        {
            VehicleGaugeMode.Off => [],
            VehicleGaugeMode.PerType => VehicleGauges.PerKind(Gauges.Rows),
            _ => Gauges.Rows,
        };
        var key = Key(mode);
        if (key != _layout)
        {
            _layout = key;
            Build();
        }
        var named = mode == VehicleGaugeMode.PerContainer;
        for (var i = 0; i < _rows.Count; i++)
        {
            var (row, data) = (_rows[i], _shown[i]);
            row.Bar.MaxValue = Math.Max(1, data.Capacity);
            row.Bar.Value = data.Held;
            row.Amount.Text = $"{data.Held} / {data.Capacity}";
            row.Name.Text = named ? GaugeNames.Of(data) : "";
            row.Low = HudRoot.Settings.IsLow(data);
        }
    }

    // The rows' kinds and parts, and the mode: what the rows are built from.
    private string Key(VehicleGaugeMode mode) =>
        $"{mode}:" + string.Join(",", (mode == VehicleGaugeMode.Off ? [] : mode == VehicleGaugeMode.PerType ? VehicleGauges.PerKind(Gauges.Rows) : Gauges.Rows)
            .Select(r => $"{r.Kind}/{r.Part}"));

    private void Build()
    {
        foreach (var child in _groups.GetChildren())
        {
            _groups.RemoveChild(child);
            child.QueueFree();
        }
        _rows.Clear();
        VBoxContainer? group = null;
        GaugeKind? kind = null;
        var named = Mode() == VehicleGaugeMode.PerContainer;
        foreach (var data in _shown)
        {
            if (group is null || kind != data.Kind)
            {
                group = new VBoxContainer();
                group.AddThemeConstantOverride("separation", RowGap);
                _groups.AddChild(group);
                kind = data.Kind;
            }
            _rows.Add(MakeRow(group, data.Kind, named));
        }
    }

    // An icon and a bar with the health bar's look, the name left and the amount right (or the amount centred).
    private static Row MakeRow(VBoxContainer group, GaugeKind kind, bool named)
    {
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 8);
        group.AddChild(line);
        line.AddChild(new GaugeIcon { Name = "Icon", Kind = kind, CustomMinimumSize = new Vector2(18, 16), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var bar = new ProgressBar { Name = "Bar", CustomMinimumSize = new Vector2(240, 18), ShowPercentage = false, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        bar.AddThemeStyleboxOverride("fill", new ShadedBox { Fill = UiTokens.Gauge[kind] });
        var track = MechanicaTheme.Box(Track, margin: 0);
        bar.AddThemeStyleboxOverride("background", track);
        line.AddChild(bar);
        var text = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", 6);
        bar.AddChild(text);
        text.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        text.OffsetLeft = 7;
        text.OffsetRight = -7;
        var name = Loc.Verbatim(Outlined(new Label { Name = "Name", ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = named }));
        var amount = Loc.Verbatim(Outlined(new Label
        {
            Name = "Amount",
            HorizontalAlignment = named ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            SizeFlagsHorizontal = named ? Control.SizeFlags.ShrinkEnd : Control.SizeFlags.ExpandFill,
        }));
        text.AddChild(name);
        text.AddChild(amount);
        return new Row(kind, bar, track, name, amount);
    }

    // The health bar's numbers: small, centred on the bar's height, with a dark outline so they read on any fill.
    private static Label Outlined(Label label)
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeConstantOverride("outline_size", 3);
        label.AddThemeColorOverride("font_outline_color", UiTokens.DeepNavy);
        return label;
    }

    private sealed class Row(GaugeKind kind, ProgressBar bar, StyleBoxFlat track, Label name, Label amount)
    {
        public GaugeKind Kind { get; } = kind;
        public ProgressBar Bar { get; } = bar;
        public StyleBoxFlat Track { get; } = track;
        public Label Name { get; } = name;
        public Label Amount { get; } = amount;
        public bool Low { get; set; }
    }
}

/// <summary>What a vehicle gauge's container is called: the name a player gave it, or its kind and number ("Gas 1").</summary>
public static class GaugeNames
{
    /// <summary>The name <paramref name="row"/> shows.</summary>
    public static string Of(GaugeRow row) => row.Name ?? Default(row.Kind, row.Number);

    /// <summary>The default name of the <paramref name="number"/>th container of <paramref name="kind"/>.</summary>
    public static string Default(GaugeKind kind, int number) => kind switch
    {
        GaugeKind.Gas => Loc.T("Gas {0}", number),
        GaugeKind.Electric => Loc.T("Battery {0}", number),
        GaugeKind.Chemical => Loc.T("Chemical {0}", number),
        GaugeKind.Water => Loc.T("Water {0}", number),
        _ => Loc.T("Ammo {0}", number),
    };
}
