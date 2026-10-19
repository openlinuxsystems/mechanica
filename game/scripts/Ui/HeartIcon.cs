using Godot;

namespace Mechanica.Game.Ui;

/// <summary>A filled heart drawn in code, the health bar's icon.</summary>
public partial class HeartIcon : Control
{
    private const int Steps = 12;

    /// <summary>The heart's colour.</summary>
    public Color Colour { get; set; } = UiTokens.Text;

    public override void _Draw()
    {
        // Two lobes and a point, as one outline: the classic parametric heart, scaled into the control.
        var points = new Vector2[Steps * 4];
        for (var i = 0; i < points.Length; i++)
        {
            var t = Mathf.Tau * i / points.Length;
            var x = 16 * Mathf.Pow(Mathf.Sin(t), 3);
            var y = 13 * Mathf.Cos(t) - 5 * Mathf.Cos(2 * t) - 2 * Mathf.Cos(3 * t) - Mathf.Cos(4 * t);
            // x spans ±16, y spans about -17..12; map to the control with the point at the bottom.
            points[i] = new Vector2((x + 16) / 32 * Size.X, (12 - y) / 29 * Size.Y);
        }
        DrawColoredPolygon(points, Colour);
        // A thin antialiased edge, since the filled polygon is not.
        DrawPolyline([.. points, points[0]], Colour, 1f, antialiased: true);
    }
}
