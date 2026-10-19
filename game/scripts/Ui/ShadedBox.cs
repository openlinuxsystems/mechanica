using Godot;

namespace Mechanica.Game.Ui;

/// <summary>
/// A rounded box filled with a faint vertical shade, a little lighter at the top and darker at the bottom, so a bar
/// reads as slightly raised. Used for the health bar's fill.
/// </summary>
public partial class ShadedBox : StyleBox
{
    private const int ArcSteps = 4;

    /// <summary>The colour in the middle of the shade.</summary>
    public Color Fill { get; set; } = UiTokens.Health;

    /// <summary>How far the top lightens and the bottom darkens, 0 to 1.</summary>
    public float Shade { get; set; } = 0.14f;

    /// <summary>The corner radius, in pixels.</summary>
    public float Radius { get; set; } = 4;

    public override void _Draw(Rid toCanvasItem, Rect2 rect)
    {
        if (rect.Size.X < 1 || rect.Size.Y < 1)
            return;
        var r = Mathf.Min(Radius, Mathf.Min(rect.Size.X, rect.Size.Y) / 2);
        var top = Fill.Lightened(Shade);
        var bottom = Fill.Darkened(Shade);
        // The outline round the corners, then a fan from the centre (the box is convex), coloured by height.
        var corners = new[]
        {
            (new Vector2(rect.End.X - r, rect.Position.Y + r), -Mathf.Pi / 2),
            (new Vector2(rect.End.X - r, rect.End.Y - r), 0f),
            (new Vector2(rect.Position.X + r, rect.End.Y - r), Mathf.Pi / 2),
            (new Vector2(rect.Position.X + r, rect.Position.Y + r), Mathf.Pi),
        };
        var points = new List<Vector2> { rect.GetCenter() };
        foreach (var (centre, from) in corners)
            for (var i = 0; i <= ArcSteps; i++)
            {
                var a = from + Mathf.Pi / 2 * i / ArcSteps;
                points.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        var colours = points.Select(p => top.Lerp(bottom, (p.Y - rect.Position.Y) / rect.Size.Y)).ToArray();
        var indices = new List<int>();
        for (var i = 1; i < points.Count; i++)
            indices.AddRange([0, i, i == points.Count - 1 ? 1 : i + 1]);
        RenderingServer.CanvasItemAddTriangleArray(toCanvasItem, [.. indices], [.. points], colours);
    }
}
