using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class UiPaletteTests
{
    [Fact]
    public void SpecColoursAreExact()
    {
        Assert.Equal("#1A2332", UiPalette.Navy);
        Assert.Equal("#2E86DE", UiPalette.Blue);
        Assert.Equal("#00D4FF", UiPalette.Cyan);
        Assert.Equal("#B0B8C1", UiPalette.TextSecondary);
        Assert.Equal(0.85f, UiPalette.PanelOpacity);
    }

    [Fact]
    public void ParseReadsHex()
    {
        var (r, g, b) = UiPalette.Parse("#2E86DE");
        Assert.Equal(0x2E / 255f, r);
        Assert.Equal(0x86 / 255f, g);
        Assert.Equal(0xDE / 255f, b);
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("#GG0000")]
    public void ParseRejectsBadHex(string hex) => Assert.Throws<FormatException>(() => UiPalette.Parse(hex));

    [Fact]
    public void NoPaletteColourIsOrange()
    {
        foreach (var (name, hex) in UiPalette.All.Concat(UiPalette.Overlays).Concat(UiPalette.Syntax))
        {
            var (r, g, b) = UiPalette.Parse(hex);
            Assert.False(UiPalette.IsOrange(r, g, b), $"{name} {hex} reads as orange");
        }
    }

    [Fact]
    public void JointColoursCanBeToldApart()
    {
        string[] joints = [UiPalette.JointRigid, UiPalette.JointHinge, UiPalette.JointSlider, UiPalette.JointSpring, UiPalette.NoConnection];
        Assert.Equal(joints.Length, joints.Distinct().Count());
        Assert.NotEqual(UiPalette.SignalOn, UiPalette.SignalOff);
        Assert.NotEqual(UiPalette.ForceArrow, UiPalette.TorqueArrow);
    }

    [Fact]
    public void TheStyleGuideListsEveryColour()
    {
        var guide = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "ui-style-guide.md"));
        foreach (var (name, hex) in UiPalette.All.Concat(UiPalette.Overlays).Concat(UiPalette.Syntax))
            Assert.True(guide.Contains($"`{hex}`", StringComparison.OrdinalIgnoreCase), $"docs/ui-style-guide.md is missing {name} ({hex})");
    }

    [Theory]
    [InlineData(1f, 0.55f, 0.15f, true)]   // the old linear-joint gizmo
    [InlineData(0.85f, 0.62f, 0.12f, true)] // the old Lift
    [InlineData(1f, 0.85f, 0.4f, true)]    // the old held-slot amber
    [InlineData(1f, 0.6f, 0.5f, true)]     // the old salmon error text
    [InlineData(1f, 0.2f, 0.2f, false)]    // red
    [InlineData(0.9f, 0.89f, 0.63f, false)] // pale warning yellow
    [InlineData(0.6f, 0.5f, 0.35f, false)] // dull brown
    [InlineData(0.18f, 0.53f, 0.87f, false)] // blue
    public void IsOrangeSpotsOrangeAndAmber(float r, float g, float b, bool orange) =>
        Assert.Equal(orange, UiPalette.IsOrange(r, g, b));

    [Theory]
    [InlineData(0.5f, 0.75f)]
    [InlineData(1.2f, 1.2f)]
    [InlineData(2f, 1.5f)]
    public void UiScaleIsClamped(float scale, float expected) => Assert.Equal(expected, UiPalette.ClampUiScale(scale));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mechanica.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Mechanica.sln not found above the test output.");
    }
}
