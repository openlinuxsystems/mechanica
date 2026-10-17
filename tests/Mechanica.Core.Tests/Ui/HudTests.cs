using System.Numerics;
using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class HudTests
{
    [Fact]
    public void TheTooltipWaitsThenFadesIn()
    {
        var hover = new HoverTimer(new HudSettings { TooltipDelaySeconds = 0.25, TooltipFadeSeconds = 0.1 });
        hover.Tick("crate", 0);
        hover.Tick("crate", 0.2);
        Assert.False(hover.Showing);
        hover.Tick("crate", 0.1);
        Assert.Equal(0.5f, hover.Opacity, 3);
        hover.Tick("crate", 0.1);
        Assert.Equal(1f, hover.Opacity);
    }

    [Fact]
    public void TheTooltipGoesAtOnceWhenTheTargetChanges()
    {
        var hover = new HoverTimer(new HudSettings());
        hover.Tick("crate", 0);
        hover.Tick("crate", 1);
        Assert.True(hover.Showing);
        hover.Tick("seat", 0.016);
        Assert.False(hover.Showing);
        hover.Tick(null, 1);
        Assert.False(hover.Showing);
        Assert.Null(hover.Target);
    }

    [Fact]
    public void AHoldCompletesOnceAndLettingGoResetsIt()
    {
        var hold = new HoldTimer(0.6);
        Assert.False(hold.Tick(true, 0.3));
        Assert.Equal(0.5f, hold.Fraction, 3);
        Assert.True(hold.Tick(true, 0.3));
        Assert.False(hold.Tick(true, 1));
        Assert.False(hold.Tick(false, 0.1));
        Assert.Equal(0f, hold.Fraction);
        hold.Tick(true, 0.5);
        hold.Cancel();
        Assert.Equal(0f, hold.Fraction);
    }

    [Fact]
    public void TooltipsStayOnScreen()
    {
        var screen = new Vector2(1280, 720);
        var margin = new Vector2(25.6f, 14.4f);
        var size = new Vector2(300, 100);
        Assert.Equal(new Vector2(664, 384), TooltipPlacement.Place(new Vector2(640, 360), size, screen, margin, 24));
        // Near the right and bottom edges it flips to the other side.
        Assert.Equal(new Vector2(926, 576), TooltipPlacement.Place(new Vector2(1250, 700), size, screen, margin, 24));
        // Bigger than the screen: pinned to the safe corner.
        Assert.Equal(margin, TooltipPlacement.Place(new Vector2(10, 10), new Vector2(2000, 2000), screen, margin, 24));
    }

    [Fact]
    public void TheSafeAreaIsAShareOfTheScreen()
    {
        var margin = new HudSettings().Margin(new Vector2(1920, 1080));
        Assert.Equal(38.4f, margin.X, 3);
        Assert.Equal(21.6f, margin.Y, 3);
    }

    [Fact]
    public void CrosshairColoursFollowTheState()
    {
        Assert.Equal((UiPalette.Text, 1f), Crosshair.Look(CrosshairState.Default, 0, 2));
        Assert.Equal((UiPalette.Blue, 1f), Crosshair.Look(CrosshairState.Valid, 0, 2));
        var bright = Crosshair.Look(CrosshairState.Connection, 0, 2);
        var dim = Crosshair.Look(CrosshairState.Connection, 0.25, 2);
        Assert.Equal(UiPalette.Cyan, bright.Hex);
        Assert.Equal(1f, bright.Alpha, 3);
        Assert.Equal(0.55f, dim.Alpha, 3);
    }

    [Fact]
    public void ShippedHudDataParses()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mechanica.sln")))
            dir = dir.Parent;
        var settings = HudSettings.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "game", "data", "hud.json")));
        Assert.Equal(0.25, settings.TooltipDelaySeconds);
        Assert.Equal(0.1, settings.TooltipFadeSeconds);
        Assert.Equal(0.02f, settings.SafeArea);
        Assert.Throws<FormatException>(() => HudSettings.Parse("""{ "safe_area": 0.5 }"""));
        Assert.Throws<FormatException>(() => HudSettings.Parse("""{ "hint_dismiss_seconds": 0 }"""));
    }
}
