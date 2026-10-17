using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Ui;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// The Paint Tool's colour picker (UI spec §2, §9): a ring of the palette's swatches centred on the cursor, opened with
/// Q. Moving the mouse towards a swatch highlights it and left click takes it; the hotbar keys (1 to 0) take the first
/// ten. Esc or a second Q closes it without a change.
/// </summary>
public partial class PaintPicker : CanvasLayer
{
    private RingView _ring = null!;
    private int _current;

    /// <summary>The colours, <c>"#rrggbb"</c>. Set before adding to the tree.</summary>
    public IReadOnlyList<string> Palette { get; init; } = [];

    /// <summary>Hears the index of the colour taken. Set before adding to the tree.</summary>
    public Action<int> Chosen { get; init; } = _ => { };

    /// <summary>True while the ring is up.</summary>
    public bool IsOpen => _ring.Visible;

    /// <summary>The swatch the mouse points at, or -1.</summary>
    public int Hovered { get; private set; } = -1;

    /// <summary>The ring's control (it keeps the mouse to itself).</summary>
    public Control Ring => _ring;

    /// <summary>Centre of the ring on screen.</summary>
    public Vector2 Centre => _ring.Size / 2;

    /// <summary>Distance from the centre to the middle of each swatch, in pixels.</summary>
    public float Radius => Mathf.Min(_ring.Size.X, _ring.Size.Y) * 0.16f;

    /// <summary>Radius of a swatch, in pixels.</summary>
    public float SwatchRadius => Mathf.Max(10f, Radius * Mathf.Pi / Mathf.Max(8, Palette.Count) * 0.8f);

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

    /// <summary>Points at the swatch in the direction of <paramref name="screen"/> from the centre; returns it, or -1.</summary>
    public int PointAt(Vector2 screen)
    {
        var offset = screen - Centre;
        Hovered = RadialMenu.Pick(new System.Numerics.Vector2(offset.X, offset.Y), Palette.Count, SwatchRadius);
        _ring.QueueRedraw();
        return Hovered;
    }

    /// <summary>Takes swatch <paramref name="index"/> and closes; out of range does nothing.</summary>
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
        if (GameInput.Pressed(@event, InputActions.Rotate) || GameInput.Pressed(@event, InputActions.RotateBack))
            Close();
        else if (Enumerable.Range(1, InputActions.HotbarSlots).FirstOrDefault(s => GameInput.Pressed(@event, InputActions.Hotbar(s))) is var slot and > 0)
            Choose(slot - 1);
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
            var fontSize = Mathf.RoundToInt(Mathf.Max(12f, p.SwatchRadius * 0.7f));
            // Room for the swatches and the key labels outside them.
            var outer = p.Radius + p.SwatchRadius * 1.2f + fontSize * 1.8f;
            DrawCircle(p.Centre, outer, UiTokens.Panel);
            DrawArc(p.Centre, outer, 0, Mathf.Tau, 96, UiTokens.Blue, 2f);
            for (var i = 0; i < p.Palette.Count; i++)
            {
                var at = RadialMenu.Position(i, p.Palette.Count, p.Radius);
                var centre = p.Centre + new Vector2(at.X, at.Y);
                var hovered = i == p.Hovered;
                var radius = p.SwatchRadius * (hovered ? 1.2f : 1f);
                DrawCircle(centre, radius, new Color(p.Palette[i]));
                DrawArc(centre, radius, 0, Mathf.Tau, 48, hovered ? UiTokens.Cyan : i == p._current ? UiTokens.Text : UiTokens.Blue, hovered || i == p._current ? 3f : 1.5f);
                if (i < InputActions.HotbarSlots)
                {
                    var key = GameInput.Label(InputActions.Hotbar(i + 1));
                    var outward = new Vector2(at.X, at.Y).Normalized() * (radius + fontSize * 0.9f);
                    var size = font.GetStringSize(key, HorizontalAlignment.Left, -1, fontSize);
                    DrawString(font, centre + outward + new Vector2(-size.X / 2, size.Y / 3), key, HorizontalAlignment.Left, -1, fontSize, UiTokens.TextSecondary);
                }
            }
            var shown = p.Hovered >= 0 ? p.Hovered : p._current;
            if (shown >= 0 && shown < p.Palette.Count)
            {
                DrawCircle(p.Centre, p.SwatchRadius * 1.1f, new Color(p.Palette[shown]));
                DrawArc(p.Centre, p.SwatchRadius * 1.1f, 0, Mathf.Tau, 48, UiTokens.Cyan, 2f);
            }
        }
    }
}
