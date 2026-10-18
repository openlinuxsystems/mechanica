using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class IconFinishTests
{
    private const int Size = 16;

    // A white 4×4 square in the middle of a clear picture.
    private static byte[] Square()
    {
        var rgba = new byte[Size * Size * 4];
        for (var y = 6; y < 10; y++)
        {
            for (var x = 6; x < 10; x++)
                rgba.AsSpan((y * Size + x) * 4, 4).Fill(255);
        }
        return rgba;
    }

    private static (byte R, byte G, byte B, byte A) At(byte[] rgba, int x, int y) =>
        (rgba[(y * Size + x) * 4], rgba[(y * Size + x) * 4 + 1], rgba[(y * Size + x) * 4 + 2], rgba[(y * Size + x) * 4 + 3]);

    [Fact]
    public void TheItemItselfIsUntouched() => Assert.Equal((255, 255, 255, 255), At(IconFinish.Apply(Square(), Size), 7, 7));

    [Fact]
    public void AnInkOutlineRingsTheItem()
    {
        var (r, g, b, a) = At(IconFinish.Apply(Square(), Size), 4, 7);
        var (ir, ig, ib) = UiPalette.Parse(UiPalette.Ink);
        Assert.True(a >= 190, $"alpha {a}");
        Assert.True(Math.Abs(r - ir * 255) < 4 && Math.Abs(g - ig * 255) < 4 && Math.Abs(b - ib * 255) < 4, $"colour {r},{g},{b}");
    }

    [Fact]
    public void TheShadowFallsBelowNotAbove()
    {
        var done = IconFinish.Apply(Square(), Size);
        Assert.True(At(done, 7, 13).A > 0, "below");
        Assert.Equal(0, At(done, 7, 1).A);
    }

    [Fact]
    public void FarCornersStayClear() => Assert.Equal(0, At(IconFinish.Apply(Square(), Size), 0, 0).A);

    [Fact]
    public void RejectsTheWrongSize() => Assert.Throws<ArgumentException>(() => IconFinish.Apply(new byte[10], Size));
}
