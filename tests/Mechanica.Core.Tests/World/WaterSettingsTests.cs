using Mechanica.Core.Settings;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

public class WaterSettingsTests
{
    [Fact]
    public void DefaultsAreValid() => new WaterSettings().Validate();

    [Fact]
    public void ShippedGameDataParses()
    {
        var path = Path.Combine(RepoRoot(), "game", "data", "water.json");
        var settings = WaterSettings.FromJson(File.ReadAllText(path));
        Assert.Equal(1, settings.Low.Layers);
        Assert.False(settings.Low.Depth);
        Assert.Equal(0f, settings.Low.Waves);
        Assert.True(settings.High.Layers >= settings.Medium.Layers);
        Assert.True(settings.High.Waves > settings.Medium.Waves);
    }

    [Fact]
    public void ReadsSnakeCaseWithCommentsAndTrailingCommas()
    {
        var settings = WaterSettings.FromJson("""
            {
              // calmer water for a test
              "wave_amplitude": 0.05,
              "medium": { "layers": 1, "waves": 0.2, "foam": 0.1, "depth": false },
            }
            """);
        Assert.Equal(0.05f, settings.WaveAmplitude);
        Assert.Equal(1, settings.Medium.Layers);
        Assert.Equal(new WaterSettings().ShallowColor, settings.ShallowColor);
    }

    [Theory]
    [InlineData(Quality.Low, 1)]
    [InlineData(Quality.Medium, 2)]
    [InlineData(Quality.High, 3)]
    [InlineData(Quality.Ultra, 3)]
    [InlineData(Quality.Custom, 3)]
    public void EachQualityPicksItsTier(Quality quality, int layers) => Assert.Equal(layers, new WaterSettings().Tier(quality).Layers);

    [Fact]
    public void ParsesHexColours()
    {
        var (r, g, b) = WaterSettings.ParseColor("#00B4D8");
        Assert.Equal(0f, r);
        Assert.Equal(0xB4 / 255f, g);
        Assert.Equal(0xD8 / 255f, b);
    }

    [Theory]
    [InlineData("""{ "low": { "layers": 0 } }""", "low.layers")]
    [InlineData("""{ "high": { "layers": 4 } }""", "high.layers")]
    [InlineData("""{ "medium": { "waves": 1.5 } }""", "medium.waves")]
    [InlineData("""{ "shallow_color": "cyan" }""", "shallow_color")]
    [InlineData("""{ "deep_color": "#12345" }""", "deep_color")]
    [InlineData("""{ "transparency": 0.95 }""", "transparency")]
    [InlineData("""{ "normal_strength": 0.1 }""", "normal_strength")]
    [InlineData("""{ "wave_amplitude": 0.5 }""", "wave_amplitude")]
    [InlineData("""{ "wave_frequency": 1 }""", "wave_frequency")]
    [InlineData("""{ "scroll_primary": 1 }""", "scroll_primary")]
    [InlineData("""{ "lod_start": 100, "lod_end": 50 }""", "lod_end")]
    [InlineData("""{ "mesh_spacing": 0 }""", "mesh_spacing")]
    [InlineData("""{ "wave_hight": 0.1 }""", "wave_hight")]
    [InlineData("null", "null")]
    public void BadDataGivesAClearError(string json, string mentioned)
    {
        var error = Assert.Throws<FormatException>(() => WaterSettings.FromJson(json));
        Assert.Contains(mentioned, error.Message);
    }

    [Fact]
    public void PresetsSetWaterQuality()
    {
        var graphics = new GraphicsSettings();
        Assert.Equal(Quality.High, graphics.Water);
        Assert.Equal(Quality.Low, graphics.WithPreset(Quality.Low).Water);
        Assert.Equal(Quality.Medium, graphics.WithPreset(Quality.Medium).Water);
        Assert.Equal(Quality.High, graphics.WithPreset(Quality.Ultra).Water);
        var custom = (graphics with { Water = Quality.Medium }).WithPreset(Quality.Custom);
        Assert.Equal(Quality.Medium, custom.Effective().Water);
        Assert.Equal(Quality.High, (graphics with { Water = Quality.Ultra }).Clamped().Water);
        Assert.Equal(Quality.High, (graphics with { Water = (Quality)42 }).Clamped().Water);
    }

    [Fact]
    public void WaterQualityRoundTripsThroughUserSettings()
    {
        var settings = new UserSettings { Graphics = new GraphicsSettings { Preset = Quality.Custom, Water = Quality.Low } };
        var read = UserSettings.Parse(settings.ToJson())!;
        Assert.Equal(Quality.Low, read.Graphics.Water);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mechanica.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Mechanica.sln not found above the test output.");
    }
}
