using Godot;

namespace Mechanica.Game.Ui;

/// <summary>
/// The knockout bag's mark on the compass and over the bag in the world: a tied sack (gathered top, cinched neck,
/// round body) with a navy rim, so the player's own bag never reads as just another waypoint diamond.
/// </summary>
public static class BagIcon
{
    // The sack's width over its height.
    private const float Aspect = 0.92f;

    // Navy around the sack and across its tie, as a fraction of its height.
    private const float Rim = 0.1f;

    private static ImageTexture? _texture;

    /// <summary>
    /// The sack's outline, <paramref name="height"/> tall and centred on <paramref name="centre"/>: the gathered top, the
    /// neck, then the body round from the right shoulder under the bottom to the left one.
    /// </summary>
    public static Vector2[] Outline(Vector2 centre, float height)
    {
        var points = new List<Vector2> { new(-0.34f, -0.5f), new(-0.14f, -0.4f), new(0f, -0.46f), new(0.14f, -0.4f), new(0.34f, -0.5f), new(0.13f, -0.25f) };
        const int Steps = 14;
        for (var i = 0; i <= Steps; i++)
        {
            var angle = Mathf.DegToRad(-58f + 296f * i / Steps);
            points.Add(new Vector2(0.5f * Mathf.Cos(angle), 0.17f + 0.33f * Mathf.Sin(angle)));
        }
        points.Add(new(-0.13f, -0.25f));
        return [.. points.Select(p => centre + new Vector2(p.X * Aspect, p.Y) * height)];
    }

    /// <summary>Draws the sack on <paramref name="canvas"/> in <paramref name="colour"/>, rimmed and tied in navy.</summary>
    public static void Draw(CanvasItem canvas, Vector2 centre, float height, Color colour)
    {
        var outline = Outline(centre, height);
        var rim = UiTokens.DeepNavy with { A = colour.A };
        canvas.DrawPolyline([.. outline, outline[0]], rim, height * Rim * 2, true);
        canvas.DrawColoredPolygon(outline, colour);
        var tie = centre.Y - 0.27f * height;
        canvas.DrawLine(new Vector2(centre.X - 0.17f * height, tie), new Vector2(centre.X + 0.17f * height, tie), rim, height * Rim, true);
    }

    /// <summary>
    /// The sack as a white texture with its rim, for the marker over the bag in the world; tint it with the sprite's
    /// modulate. Made once, by sampling <see cref="Outline"/>.
    /// </summary>
    public static Texture2D Texture()
    {
        if (_texture is not null)
            return _texture;
        const int Size = 64;
        var height = Size * 0.78f;
        var outline = Outline(new Vector2(Size / 2f, Size / 2f), height);
        var rim = height * Rim;
        var tieY = Size / 2f - 0.27f * height;
        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        var navy = UiTokens.DeepNavy;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var edge = float.MaxValue;
                for (var i = 0; i < outline.Length; i++)
                    edge = Mathf.Min(edge, p.DistanceTo(Geometry2D.GetClosestPointToSegment(p, outline[i], outline[(i + 1) % outline.Length])));
                var inside = Geometry2D.IsPointInPolygon(p, outline);
                // Signed distance from the outline, negative inside; one pixel of blend at each edge.
                var signed = inside ? -edge : edge;
                var alpha = Mathf.Clamp(rim + 0.5f - signed, 0f, 1f);
                var fill = Mathf.Clamp(-signed - rim + 0.5f, 0f, 1f);
                if (Mathf.Abs(p.Y - tieY) < rim / 2 + 0.5f && Mathf.Abs(p.X - Size / 2f) < 0.17f * height)
                    fill = 0f;
                image.SetPixel(x, y, navy.Lerp(Colors.White, fill) with { A = alpha });
            }
        }
        image.GenerateMipmaps();
        return _texture = ImageTexture.CreateFromImage(image);
    }
}
