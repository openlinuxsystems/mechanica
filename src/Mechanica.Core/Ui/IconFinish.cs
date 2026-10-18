namespace Mechanica.Core.Ui;

/// <summary>
/// The finish every item picture gets (UI spec §0): a 2 px <see cref="UiPalette.Ink"/> outline and a soft contact
/// shadow under it, so white and pale parts still read on a light tile. Works on square RGBA8 pixels so it can be
/// tested without Godot; the game crops and scales the render first (largest side to <see cref="Fill"/> of the picture).
/// </summary>
public static class IconFinish
{
    /// <summary>Share of the picture the item's largest side fills.</summary>
    public const float Fill = 0.8f;

    /// <summary>Outline width in pixels.</summary>
    public const int Outline = 2;

    /// <summary>Opacity of the outline.</summary>
    public const float OutlineOpacity = 0.75f;

    /// <summary>How far below the item the shadow falls, in pixels.</summary>
    public const int ShadowDrop = 3;

    /// <summary>Opacity of the shadow under a solid item.</summary>
    public const float ShadowOpacity = 0.45f;

    /// <summary>
    /// Returns <paramref name="rgba"/> (<paramref name="size"/> × <paramref name="size"/>, 4 bytes a pixel, straight
    /// alpha) with the shadow and outline drawn behind the item.
    /// </summary>
    public static byte[] Apply(byte[] rgba, int size)
    {
        if (rgba.Length != size * size * 4)
            throw new ArgumentException("Expected size × size RGBA pixels.", nameof(rgba));
        var alpha = new float[size * size];
        for (var i = 0; i < alpha.Length; i++)
            alpha[i] = rgba[i * 4 + 3] / 255f;
        var outline = Dilate(alpha, size, Outline);
        var shadow = Blur(Shift(alpha, size, ShadowDrop), size, 3);
        var (ir, ig, ib) = UiPalette.Parse(UiPalette.Ink);
        var result = new byte[rgba.Length];
        for (var i = 0; i < alpha.Length; i++)
        {
            // Back to front: shadow, outline, then the item itself.
            var (r, g, b, a) = (ir, ig, ib, shadow[i] * ShadowOpacity);
            (r, g, b, a) = Over((ir, ig, ib, outline[i] * OutlineOpacity), (r, g, b, a));
            (r, g, b, a) = Over((rgba[i * 4] / 255f, rgba[i * 4 + 1] / 255f, rgba[i * 4 + 2] / 255f, alpha[i]), (r, g, b, a));
            result[i * 4] = Byte(r);
            result[i * 4 + 1] = Byte(g);
            result[i * 4 + 2] = Byte(b);
            result[i * 4 + 3] = Byte(a);
        }
        return result;
    }

    // Straight-alpha "source over destination".
    private static (float R, float G, float B, float A) Over((float R, float G, float B, float A) src, (float R, float G, float B, float A) dst)
    {
        var a = src.A + dst.A * (1 - src.A);
        if (a <= 0)
            return (0, 0, 0, 0);
        float Mix(float s, float d) => (s * src.A + d * dst.A * (1 - src.A)) / a;
        return (Mix(src.R, dst.R), Mix(src.G, dst.G), Mix(src.B, dst.B), a);
    }

    // The largest alpha within a round brush of the given radius.
    private static float[] Dilate(float[] alpha, int size, int radius)
    {
        var result = new float[alpha.Length];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var best = 0f;
                for (var dy = -radius; dy <= radius; dy++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var (sx, sy) = (x + dx, y + dy);
                        if (dx * dx + dy * dy <= radius * radius + 1 && sx >= 0 && sy >= 0 && sx < size && sy < size)
                            best = Math.Max(best, alpha[sy * size + sx]);
                    }
                }
                result[y * size + x] = best;
            }
        }
        return result;
    }

    private static float[] Shift(float[] alpha, int size, int down)
    {
        var result = new float[alpha.Length];
        for (var y = down; y < size; y++)
            Array.Copy(alpha, (y - down) * size, result, y * size, size);
        return result;
    }

    // Two box-blur passes each way approximate a Gaussian.
    private static float[] Blur(float[] alpha, int size, int radius)
    {
        var a = alpha;
        for (var pass = 0; pass < 2; pass++)
        {
            a = BoxBlur(a, size, radius, horizontal: true);
            a = BoxBlur(a, size, radius, horizontal: false);
        }
        return a;
    }

    private static float[] BoxBlur(float[] alpha, int size, int radius, bool horizontal)
    {
        var result = new float[alpha.Length];
        for (var line = 0; line < size; line++)
        {
            for (var at = 0; at < size; at++)
            {
                var sum = 0f;
                for (var k = -radius; k <= radius; k++)
                {
                    var p = Math.Clamp(at + k, 0, size - 1);
                    sum += horizontal ? alpha[line * size + p] : alpha[p * size + line];
                }
                result[horizontal ? line * size + at : at * size + line] = sum / (2 * radius + 1);
            }
        }
        return result;
    }

    private static byte Byte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
}
