using Godot;
using Mechanica.Core.Data;

namespace Mechanica.Game.Ui;

/// <summary>
/// A vehicle gauge's icon, drawn in code like the health bar's <see cref="HeartIcon"/>: a fuel can for gas, a battery for
/// charge, a flask for chemicals, a drop for water and a potato for ammo, white with dark details.
/// </summary>
public partial class GaugeIcon : Control
{
    // Shapes are written on a 32 × 29 grid (the heart's) and scaled into the control.
    private const float GridWidth = 32;
    private const float GridHeight = 29;

    private GaugeKind _kind;

    /// <summary>Which icon it draws.</summary>
    public GaugeKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            QueueRedraw();
        }
    }

    /// <summary>The icon's colour.</summary>
    public Color Colour { get; set; } = UiTokens.Text;

    /// <summary>The colour of its details (the can's cross, the bolt, the potato's eyes).</summary>
    public Color Detail { get; set; } = UiTokens.DeepNavy;

    public override void _Draw()
    {
        switch (Kind)
        {
            case GaugeKind.Gas:
                // A jerry can: body with a cut corner, a spout, and an embossed cross.
                Fill([new(5, 9), new(9, 5), new(25, 5), new(27, 7), new(27, 27), new(5, 27)]);
                Fill([new(7, 1), new(13, 1), new(13, 4), new(7, 4)]);
                Line(new(11, 13), new(21, 23), 2.6f);
                Line(new(21, 13), new(11, 23), 2.6f);
                break;
            case GaugeKind.Electric:
                // A battery on its side with its terminal and a bolt.
                Fill(Rect(2, 7, 25, 15));
                Fill(Rect(27, 11, 3, 7));
                Fill([new(16, 9), new(10, 16), new(15, 16), new(13, 21), new(19, 14), new(14, 14)], Detail);
                break;
            case GaugeKind.Chemical:
                // A flask with liquid in its bottom.
                Fill([new(11, 2), new(21, 2), new(21, 4), new(19, 4), new(19, 11), new(27, 24), new(25, 27), new(7, 27), new(5, 24), new(13, 11), new(13, 4), new(11, 4)]);
                Fill([new(10, 19), new(22, 19), new(24.5f, 23.5f), new(7.5f, 23.5f)], Detail with { A = 0.55f });
                break;
            case GaugeKind.Water:
                // A drop.
                Fill(Drop());
                break;
            case GaugeKind.Ammo:
                // A potato with three eyes.
                Fill(Ellipse(16, 15, 12, 9, -0.31f));
                foreach (var (x, y, r) in new[] { (11f, 13f, 1.4f), (19f, 11f, 1.2f), (17f, 18f, 1.3f) })
                    DrawCircle(At(new(x, y)), r * Unit, Detail, antialiased: true);
                break;
        }
    }

    private float Unit => Mathf.Min(Size.X / GridWidth, Size.Y / GridHeight);

    private Vector2 At(Vector2 p) => new(p.X * Size.X / GridWidth, p.Y * Size.Y / GridHeight);

    private void Fill(Vector2[] points, Color? colour = null)
    {
        var scaled = points.Select(At).ToArray();
        var c = colour ?? Colour;
        DrawColoredPolygon(scaled, c);
        // A thin antialiased edge, since the filled polygon is not.
        DrawPolyline([.. scaled, scaled[0]], c, 1f, antialiased: true);
    }

    private void Line(Vector2 from, Vector2 to, float width) => DrawLine(At(from), At(to), Detail, width * Unit, antialiased: true);

    private static Vector2[] Rect(float x, float y, float w, float h) => [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)];

    private static Vector2[] Ellipse(float cx, float cy, float rx, float ry, float turn) =>
        [.. Enumerable.Range(0, 24).Select(i =>
        {
            var a = Mathf.Tau * i / 24;
            var p = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry).Rotated(turn);
            return new Vector2(cx, cy) + p;
        })];

    // A circle of radius 9 at (16, 19) with a point at (16, 2).
    private static Vector2[] Drop()
    {
        var points = new List<Vector2> { new(16, 2) };
        for (var i = 0; i <= 20; i++)
        {
            var a = Mathf.DegToRad(-30 + 240f * i / 20);
            points.Add(new Vector2(16 + Mathf.Cos(a) * 9, 19 + Mathf.Sin(a) * 9));
        }
        return [.. points];
    }
}
