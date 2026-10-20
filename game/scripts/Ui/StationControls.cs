using Godot;
using Mechanica.Core.Ui;

namespace Mechanica.Game.Ui;

/// <summary>A button with a key cap after its label (UI spec §0 "Key hint"), such as <c>Upgrade [U]</c>.</summary>
public partial class KeyButton : Button
{
    private readonly HBoxContainer _row = new() { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
    private readonly Label _label = new() { VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _key = Loc.Verbatim(new Label { ThemeTypeVariation = "KeyCapLabel", MouseFilter = MouseFilterEnum.Ignore });

    public KeyButton()
    {
        _row.AddThemeConstantOverride("separation", 8);
        _row.AddChild(_label);
        var cap = new PanelContainer { ThemeTypeVariation = "KeyCap", MouseFilter = MouseFilterEnum.Ignore };
        cap.AddChild(_key);
        _row.AddChild(cap);
        AddChild(_row);
        _row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        CustomMinimumSize = new Vector2(0, 32);
        ThemeChanged += Fit;
    }

    /// <summary>The label (translated when it is set as literal text).</summary>
    public string Label
    {
        get => _label.Text;
        set
        {
            _label.Text = value;
            Fit();
        }
    }

    /// <summary>The key cap's text, from the current binding.</summary>
    public string Key
    {
        get => _key.Text;
        set
        {
            _key.Text = value;
            Fit();
        }
    }

    public override void _Process(double delta)
    {
        // The label follows the button's colours (white, or ink while it is on).
        // Only on a change: each override re-themes the label and lays it out again.
        var color = Disabled ? GetThemeColor("font_disabled_color") : GetThemeColor(ButtonPressed ? "font_pressed_color" : "font_color");
        var font = GetThemeFont("font");
        var size = GetThemeFontSize("font_size");
        if ((color, font, size) == _shown)
            return;
        _shown = (color, font, size);
        _label.AddThemeColorOverride("font_color", color);
        _label.AddThemeFontOverride("font", font);
        _label.AddThemeFontSizeOverride("font_size", size);
        // On a filled primary button the key cap takes the button's ink, so it shows on the Accent fill.
        if (ThemeTypeVariation == "PrimaryButton")
            _key.AddThemeColorOverride("font_color", color with { A = 0.75f });
    }

    private (Color, Font?, int) _shown;

    private void Fit() => CustomMinimumSize = new Vector2(_row.GetCombinedMinimumSize().X + 24, 32);
}

/// <summary>The − 1 + quantity stepper (UI spec §8): Shift+click steps by ten, and the number can be typed.</summary>
public partial class QuantityStepper : PanelContainer
{
    private readonly LineEdit _field = new() { Name = "Value", ThemeTypeVariation = "StepperField", Alignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(44, 0) };
    private int _value = 1;
    private bool _shift;

    /// <summary>Raised when the value changes.</summary>
    public event Action<int>? ValueChanged;

    /// <summary>The largest value.</summary>
    public int MaxValue { get; set; } = 99;

    /// <summary>The minus button.</summary>
    public Button Less { get; } = new() { Name = "Less", Text = "−", ThemeTypeVariation = "StepperButton", TooltipText = "One fewer (Shift: ten)" };

    /// <summary>The plus button.</summary>
    public Button More { get; } = new() { Name = "More", Text = "+", ThemeTypeVariation = "StepperButton", TooltipText = "One more (Shift: ten)" };

    public QuantityStepper()
    {
        ThemeTypeVariation = "InsetPanel";
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        AddChild(row);
        row.AddChild(Less);
        row.AddChild(Loc.Verbatim(_field));
        row.AddChild(More);
        // Shift on the click that pressed the button steps by ten.
        foreach (var (button, sign) in new[] { (Less, -1), (More, 1) })
        {
            button.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
                    _shift = click.ShiftPressed;
            };
            button.Pressed += () =>
            {
                Value += sign * (_shift ? 10 : 1);
                _shift = false;
            };
        }
        _field.TextSubmitted += Typed;
        _field.FocusExited += () => Typed(_field.Text);
        _field.Text = "1";
    }

    /// <summary>The quantity, from 1 to <see cref="MaxValue"/>.</summary>
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 1, Math.Max(1, MaxValue));
            _field.Text = clamped.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (clamped == _value)
                return;
            _value = clamped;
            ValueChanged?.Invoke(clamped);
        }
    }

    private void Typed(string text) => Value = int.TryParse(text, out var n) ? n : _value;
}

/// <summary>A stat's ten pips (UI spec §0 "Stat pips"): filled in <c>Accent</c>, the rest <c>PipOff</c>.</summary>
public partial class PipBar : Control
{
    private const float Pip = 9, Gap = 2;
    private int _filled;

    /// <summary>How many pips are filled.</summary>
    public int Filled
    {
        get => _filled;
        set
        {
            _filled = value;
            QueueRedraw();
        }
    }

    public PipBar()
    {
        CustomMinimumSize = new Vector2(ItemStats.Pips * (Pip + Gap) - Gap, Pip);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        for (var i = 0; i < ItemStats.Pips; i++)
            DrawRect(new Rect2(i * (Pip + Gap), 0, Pip, Pip), i < _filled ? UiTokens.Accent : UiTokens.PipOff);
    }
}

/// <summary>One cell of the queue strip (UI spec §8): a chevron filled by its state, with progress filling it in <c>Accent</c>.</summary>
public partial class QueueCell : Control
{
    /// <summary>How deep the chevron's point and notch are.</summary>
    public const float Point = 16;

    private Color _fill = UiTokens.Surface;
    private float _progress;
    private bool _first;

    /// <summary>The cell's fill.</summary>
    public Color Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            QueueRedraw();
        }
    }

    /// <summary>How much of the cell the progress fills (0 to 1).</summary>
    public float Progress
    {
        get => _progress;
        set
        {
            _progress = Math.Clamp(value, 0, 1);
            QueueRedraw();
        }
    }

    /// <summary>True for the first cell, which has a straight left edge.</summary>
    public bool First
    {
        get => _first;
        set
        {
            _first = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (Size.X <= 2 * Point || Size.Y <= 0)
            return;
        DrawColoredPolygon(Shape(Size.X), _fill);
        if (Size.X * _progress > Point + 1)
            DrawColoredPolygon(Shape(Size.X * _progress), UiTokens.Accent with { A = 0.55f });
    }

    // The chevron, or its part left of `right` for the progress fill.
    private Vector2[] Shape(float right)
    {
        var (w, h) = (Size.X, Size.Y);
        Vector2[] edge;
        if (right >= w)
            edge = [new(w - Point, 0), new(w, h / 2), new(w - Point, h)];
        else if (right <= w - Point)
            edge = [new(right, 0), new(right, h)];
        else
        {
            var y = (right - (w - Point)) / Point * h / 2;
            edge = [new(w - Point, 0), new(right, y), new(right, h - y), new(w - Point, h)];
        }
        // The first cell's left edge is straight; the others are notched to take the cell before's point.
        Vector2[] left = _first ? [new(0, h)] : [new(0, h), new(Point, h / 2)];
        return [new(0, 0), .. edge, .. left];
    }
}
