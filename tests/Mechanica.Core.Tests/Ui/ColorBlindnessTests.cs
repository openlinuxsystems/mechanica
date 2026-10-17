using System.Numerics;
using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class ColorBlindnessTests
{
    private static readonly Vector3 Red = new(0.8f, 0.2f, 0.2f), Green = new(0.3f, 0.6f, 0.2f);
    private static readonly Vector3 Blue = new(0.2f, 0.3f, 0.9f), Teal = new(0.2f, 0.6f, 0.5f);

    [Fact]
    public void OffChangesNothing()
    {
        Assert.Equal(Red, ColorBlindness.Correct(Red, ColorBlindMode.Off));
        Assert.Equal(Matrix4x4.Identity, ColorBlindness.Correction(ColorBlindMode.Off));
    }

    [Theory]
    [InlineData(ColorBlindMode.Protanopia)]
    [InlineData(ColorBlindMode.Deuteranopia)]
    [InlineData(ColorBlindMode.Tritanopia)]
    public void GreysStayGrey(ColorBlindMode mode)
    {
        var grey = new Vector3(0.5f);
        Assert.True(Vector3.Distance(grey, ColorBlindness.Correct(grey, mode)) < 0.02f);
    }

    [Theory]
    [InlineData(ColorBlindMode.Protanopia, true)]
    [InlineData(ColorBlindMode.Deuteranopia, true)]
    [InlineData(ColorBlindMode.Tritanopia, false)]
    public void ColoursThatLookAlikeAreMovedApart(ColorBlindMode mode, bool redGreen)
    {
        var (a, b) = redGreen ? (Red, Green) : (Blue, Teal);
        var before = Vector3.Distance(ColorBlindness.Simulate(a, mode), ColorBlindness.Simulate(b, mode));
        var after = Vector3.Distance(ColorBlindness.Simulate(ColorBlindness.Correct(a, mode), mode), ColorBlindness.Simulate(ColorBlindness.Correct(b, mode), mode));
        Assert.True(after > before * 1.1f, $"{mode}: {before:0.000} -> {after:0.000}");
    }

    [Fact]
    public void TheShaderCarriesTheCorrection()
    {
        var shader = ColorBlindness.Shader(ColorBlindMode.Deuteranopia);
        Assert.StartsWith("shader_type canvas_item;", shader);
        Assert.Contains("hint_screen_texture", shader);
        var m = ColorBlindness.Correction(ColorBlindMode.Deuteranopia);
        Assert.Contains(m.M11.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), shader);
    }
}
