using Godot;

namespace Mechanica.Game.Ui;

/// <summary>The small line icons drawn in code (the inventory screen's header): no textures, so they scale cleanly.</summary>
public enum GlyphShape
{
    /// <summary>A satchel with a flap and a strap: the inventory window's icon.</summary>
    Satchel,

    /// <summary>A magnifier: the search field.</summary>
    Search,

    /// <summary>Bars from long to short beside a down arrow: auto-organize (merge and sort).</summary>
    Organize,
}

/// <summary>One <see cref="GlyphShape"/> drawn in code, filling the control.</summary>
public partial class UiGlyph : Control
{
    private GlyphShape _shape;
    private Color _colour = UiTokens.Text;

    public UiGlyph()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>The icon drawn.</summary>
    public GlyphShape Shape
    {
        get => _shape;
        set
        {
            _shape = value;
            QueueRedraw();
        }
    }

    /// <summary>The icon's colour.</summary>
    public Color Colour
    {
        get => _colour;
        set
        {
            _colour = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // Every shape is laid out on a 24 × 24 grid and scaled to the control.
        var s = Mathf.Min(Size.X, Size.Y) / 24f;
        var o = (Size - new Vector2(24, 24) * s) / 2;
        Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
        var line = Mathf.Max(1.5f, 2f * s);
        switch (_shape)
        {
            case GlyphShape.Satchel:
                // Strap, body, flap and a buckle.
                DrawArc(P(12, 8), 5 * s, Mathf.Pi, Mathf.Tau, 16, _colour, line, true);
                Rounded(P(3, 8), P(21, 22), 3.5f * s, _colour);
                Rounded(P(3, 8), P(21, 14.5f), 3.5f * s, _colour.Darkened(0.28f));
                Rounded(P(9.5f, 12.5f), P(14.5f, 17), 1.5f * s, _colour.Lightened(0.35f));
                break;
            case GlyphShape.Search:
                DrawArc(P(10, 10), 6.5f * s, 0, Mathf.Tau, 24, _colour, line, true);
                DrawLine(P(14.8f, 14.8f), P(20.5f, 20.5f), _colour, line * 1.3f, true);
                break;
            case GlyphShape.Organize:
                // Long to short bars, then an arrow pointing down: sorted top to bottom.
                foreach (var (y, w) in new[] { (5f, 11f), (10.5f, 8f), (16f, 5f) })
                    Rounded(P(3, y), P(3 + w, y + 3), 1.5f * s, _colour);
                DrawLine(P(18.5f, 4.5f), P(18.5f, 19.5f), _colour, line, true);
                DrawColoredPolygon([P(14, 15.5f), P(23, 15.5f), P(18.5f, 21)], _colour);
                break;
        }
    }

    private void Rounded(Vector2 from, Vector2 to, float corner, Color colour)
    {
        var box = new StyleBoxFlat { BgColor = colour, AntiAliasing = true, CornerDetail = 6 };
        box.SetCornerRadiusAll((int)Mathf.Round(corner));
        DrawStyleBox(box, new Rect2(from, to - from));
    }
}
