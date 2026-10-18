using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Ui;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// The Paint Tool's colour picker (UI spec §2, §9; Chase's paint tool spec): the palette's base colours round a ring
/// centred on the cursor, opened with Q, each spoke holding that colour's shades from light (inside) to dark (outside).
/// Moving the mouse over a cell highlights it and left click takes it; the hotbar keys (1 to 0) take a base colour in
/// the shade now chosen. Esc or a second Q closes it without a change.
/// </summary>
public partial class PaintPicker : CanvasLayer
{
    private RingView _ring = null!;
    private int _current;

    /// <summary>The colours, <c>"#rrggbb"</c>, base colour by base colour, each in its <see cref="Shades"/>. Set before adding to the tree.</summary>
    public IReadOnlyList<string> Palette { get; init; } = [];

    /// <summary>Shades of each base colour (the palette's rings). Set before adding to the tree.</summary>
    public int Shades { get; init; } = 1;

    /// <summary>Hears the index of the colour taken. Set before adding to the tree.</summary>
    public Action<int> Chosen { get; init; } = _ => { };

    /// <summary>Base colours (the ring's spokes).</summary>
    public int Hues => Palette.Count / Math.Max(1, Shades);

    /// <summary>True while the ring is up.</summary>
    public bool IsOpen => _ring.Visible;

    /// <summary>The colour the mouse points at, or -1.</summary>
    public int Hovered { get; private set; } = -1;

    /// <summary>The ring's control (it keeps the mouse to itself).</summary>
    public Control Ring => _ring;

    /// <summary>Centre of the ring on screen.</summary>
    public Vector2 Centre => _ring.Size / 2;

    /// <summary>Radius of the empty middle (the chosen colour shows there), in pixels.</summary>
    public float Inner => Mathf.Min(_ring.Size.X, _ring.Size.Y) * 0.07f;

    /// <summary>Width of each shade's ring, in pixels.</summary>
    public float RingWidth => Mathf.Min(_ring.Size.X, _ring.Size.Y) * 0.05f;

    /// <summary>Radius of the outside of the last ring, in pixels.</summary>
    public float Outer => Inner + RingWidth * Math.Max(1, Shades);

    public override void _Ready()
    {
        Layer = 6;
        _ring = UiInput.KeepMouse(new RingView { Name = "Ring", Picker = this, Visible = false });
        _ring.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_ring);
        EscapeStack.Add(this, () => IsOpen, Close);
    }

    /// <summary>Opens the ring round the middle of the screen (where the crosshair is), <paramref name="current"/> marked.</summary>
    public void Open(int current)
    {
        _current = current;
        Hovered = -1;
        _ring.Visible = true;
        EscapeStack.Raise(this);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _ring.WarpMouse(Centre);
        _ring.QueueRedraw();
    }

    /// <summary>Closes the ring without choosing.</summary>
    public void Close()
    {
        if (!IsOpen)
            return;
        _ring.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Points at the colour under <paramref name="screen"/>; returns it, or -1.</summary>
    public int PointAt(Vector2 screen)
    {
        var offset = screen - Centre;
        var cell = RadialMenu.PickCell(new System.Numerics.Vector2(offset.X, offset.Y), Hues, Math.Max(1, Shades), Inner, RingWidth);
        Hovered = cell < Palette.Count ? cell : -1;
        _ring.QueueRedraw();
        return Hovered;
    }

    /// <summary>Where the middle of colour <paramref name="index"/>'s cell is on screen.</summary>
    public Vector2 CellCentre(int index)
    {
        var shades = Math.Max(1, Shades);
        var at = RadialMenu.Position(index / shades, Hues, Inner + RingWidth * (index % shades + 0.5f));
        return Centre + new Vector2(at.X, at.Y);
    }

    /// <summary>The colour of base colour <paramref name="hue"/> in the shade of the colour now chosen.</summary>
    public int HueInCurrentShade(int hue) => hue * Math.Max(1, Shades) + _current % Math.Max(1, Shades);

    /// <summary>Takes colour <paramref name="index"/> and closes; out of range does nothing.</summary>
    public void Choose(int index)
    {
        if (index < 0 || index >= Palette.Count)
            return;
        Close();
        Chosen(index);
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen)
            return;
        if ((GameInput.Pressed(@event, InputActions.Pause) && EscapeStack.Topmost == this) || GameInput.Pressed(@event, InputActions.Rotate) || GameInput.Pressed(@event, InputActions.RotateBack))
            Close();
        else if (Enumerable.Range(1, InputActions.HotbarSlots).FirstOrDefault(s => GameInput.Pressed(@event, InputActions.Hotbar(s))) is var slot and > 0)
        {
            if (slot <= Hues)
                Choose(HueInCurrentShade(slot - 1));
        }
        else if (@event is InputEventMouseMotion motion)
        {
            PointAt(motion.Position);
            return;
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            Choose(PointAt(click.Position));
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Draws the ring.</summary>
    public partial class RingView : Control
    {
        /// <summary>The picker it belongs to.</summary>
        public PaintPicker Picker { get; init; } = null!;

        public override void _Draw()
        {
            var p = Picker;
            var font = ThemeDB.FallbackFont;
            var fontSize = Mathf.RoundToInt(Mathf.Max(12f, p.RingWidth * 0.45f));
            var shades = Math.Max(1, p.Shades);
            var hues = Math.Max(1, p.Hues);
            // Room for the cells and the key labels outside them.
            var outer = p.Outer + fontSize * 2f;
            DrawCircle(p.Centre, outer, UiTokens.Panel);
            DrawArc(p.Centre, outer, 0, Mathf.Tau, 96, UiTokens.Blue, 2f);
            var step = Mathf.Tau / hues;
            for (var i = 0; i < p.Palette.Count; i++)
            {
                int hue = i / shades, shade = i % shades;
                // Clockwise from the top, as RadialMenu measures it: the spoke's middle at hue * step.
                float from = hue * step - step / 2 + 0.015f, to = hue * step + step / 2 - 0.015f;
                float r0 = p.Inner + p.RingWidth * shade + 1f, r1 = p.Inner + p.RingWidth * (shade + 1) - 1f;
                DrawPolygon(Sector(p.Centre, r0, r1, from, to), [new Color(p.Palette[i])]);
                if (i == p.Hovered || i == p._current)
                    DrawPolyline(Close(Sector(p.Centre, r0, r1, from, to)), i == p.Hovered ? UiTokens.Cyan : UiTokens.Text, i == p.Hovered ? 3f : 2f);
            }
            for (var hue = 0; hue < hues && hue < InputActions.HotbarSlots; hue++)
            {
                var key = GameInput.Label(InputActions.Hotbar(hue + 1));
                var at = RadialMenu.Position(hue, hues, p.Outer + fontSize * 0.9f);
                var size = font.GetStringSize(key, HorizontalAlignment.Left, -1, fontSize);
                DrawString(font, p.Centre + new Vector2(at.X, at.Y) + new Vector2(-size.X / 2, size.Y / 3), key, HorizontalAlignment.Left, -1, fontSize, UiTokens.TextSecondary);
            }
            var shown = p.Hovered >= 0 ? p.Hovered : p._current;
            if (shown >= 0 && shown < p.Palette.Count)
            {
                DrawCircle(p.Centre, p.Inner * 0.8f, new Color(p.Palette[shown]));
                DrawArc(p.Centre, p.Inner * 0.8f, 0, Mathf.Tau, 48, UiTokens.Cyan, 2f);
            }
        }

        // An annular sector between radii r0 and r1, angles clockwise from straight up.
        private static Vector2[] Sector(Vector2 centre, float r0, float r1, float from, float to)
        {
            const int segments = 8;
            var points = new Vector2[(segments + 1) * 2];
            for (var k = 0; k <= segments; k++)
            {
                var a = from + (to - from) * k / segments;
                var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
                points[k] = centre + dir * r1;
                points[points.Length - 1 - k] = centre + dir * r0;
            }
            return points;
        }

        private static Vector2[] Close(Vector2[] points) => [.. points, points[0]];
    }
}
