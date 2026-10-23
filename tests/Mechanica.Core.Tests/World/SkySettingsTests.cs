using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

public class SkySettingsTests
{
    [Fact]
    public void DefaultsAreValid() => new SkySettings().Validate();

    [Fact]
    public void ShippedGameDataParses()
    {
        var settings = Shipped();
        Assert.True(settings.Keys.Count >= 6);
        // Night is dark and starry, noon bright and starless.
        var midnight = settings.At(0);
        var noon = settings.At(12);
        Assert.True(midnight.Moon);
        Assert.False(noon.Moon);
        Assert.Equal(1f, midnight.Stars);
        Assert.Equal(0f, noon.Stars);
        Assert.True(noon.LightEnergy > 3 * midnight.LightEnergy);
        Assert.True(Brightness(noon.SkyTop) > 2 * Brightness(midnight.SkyTop));
        // Never pitch black.
        Assert.True(midnight.AmbientEnergy > 0.2f);
    }

    [Fact]
    public void BlendsBetweenKeysAndRoundMidnight()
    {
        var settings = new SkySettings
        {
            Keys =
            [
                new() { Hour = 6, SkyTop = "#000000", Stars = 0 },
                new() { Hour = 18, SkyTop = "#ffffff", Stars = 1 },
            ],
        };
        Assert.Equal(0.5f, settings.At(12).SkyTop.R, 3);
        Assert.Equal(0.5f, settings.At(12).Stars, 3);
        // From 18 round to 6 is 12 hours: midnight is half way back.
        Assert.Equal(0.5f, settings.At(0).SkyTop.R, 3);
        Assert.Equal(0.75f, settings.At(21).SkyTop.R, 3);
        Assert.Equal(settings.At(3).SkyTop, settings.At(27).SkyTop);
        Assert.Equal(settings.At(3).SkyTop, settings.At(-21).SkyTop);
    }

    [Fact]
    public void TheSunRisesInTheEastAndStandsHighestAtNoon()
    {
        var settings = new SkySettings { Sunrise = 6, Sunset = 18, NoonElevation = 60, SunriseBearing = 90 };
        var (dawn, dawnMoon, dawnHeight) = settings.LightAt(6.01);
        Assert.False(dawnMoon);
        Assert.InRange(dawnHeight, 0f, 1f);
        // The light points from the sun toward the world: away from the east at dawn.
        Assert.True(dawn.X < -0.9f);
        var (noon, _, noonHeight) = settings.LightAt(12);
        Assert.Equal(60f, noonHeight, 2);
        Assert.True(noon.Y < -0.8f);
        var (dusk, _, _) = settings.LightAt(17.99);
        Assert.True(dusk.X > 0.9f);
        var (_, nightMoon, nightHeight) = settings.LightAt(0);
        Assert.True(nightMoon);
        Assert.True(nightHeight > 0);
    }

    [Fact]
    public void TheLightFadesAtTheHorizon()
    {
        var settings = Shipped();
        Assert.True(settings.At(settings.Sunrise + 0.01).LightEnergy < 0.1f);
        Assert.True(settings.At(settings.Sunrise + 1.5).LightEnergy > 0.5f);
    }

    [Theory]
    [InlineData("""{ "keys": [ { "hour": 3 } ] }""", "at least two")]
    [InlineData("""{ "keys": [ { "hour": 5 }, { "hour": 4 } ] }""", "keys[1].hour")]
    [InlineData("""{ "keys": [ { "hour": 1 }, { "hour": 2, "sky_top": "blue" } ] }""", "keys[1].sky_top")]
    [InlineData("""{ "sunrise": 19 }""", "sunset")]
    [InlineData("""{ "haze_curve": 9 }""", "haze_curve")]
    [InlineData("""{ "grade": { "tonemap": "sepia" } }""", "grade.tonemap")]
    [InlineData("""{ "glow": 1 }""", "glow")]
    public void RejectsBadData(string json, string named)
    {
        var e = Assert.Throws<FormatException>(() => SkySettings.FromJson(json));
        Assert.Contains(named, e.Message);
    }

    [Theory]
    [InlineData(0, 1440, 6)]
    [InlineData(360, 1440, 12)]
    [InlineData(1080, 1440, 0)]
    [InlineData(1440, 1440, 6)]
    [InlineData(100, 0, 6 + 24 * 100 / 1440.0)]
    public void TheClockStartsAtSixAndRunsADayPerDayLength(double time, double dayLength, double hour) =>
        Assert.Equal(hour, WorldClock.HourAt(time, dayLength), 6);

    private static float Brightness((float R, float G, float B) c) => c.R + c.G + c.B;

    private static SkySettings Shipped()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mechanica.sln")))
            dir = dir.Parent;
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("Mechanica.sln not found above the test output.");
        return SkySettings.FromJson(File.ReadAllText(Path.Combine(root, "game", "data", "sky.json")));
    }
}
