using Mechanica.Core.Building;

namespace Mechanica.Core.Tests.Building;

public class BuildSettingsTests
{
    [Fact]
    public void ReadsSnakeCaseWithComments()
    {
        var settings = BuildSettings.FromJson("""
            {
              // metres
              "reach": 6.5,
              "max_parts_per_creation": 100,
              "clearance_scale": 0.8,
            }
            """);
        Assert.Equal(new BuildSettings { Reach = 6.5f, MaxPartsPerCreation = 100, ClearanceScale = 0.8f }, settings);
    }

    [Fact]
    public void ReadsToolSections()
    {
        var settings = BuildSettings.FromJson("""
            {
              "lift": { "platform_size": 3, "base_height": 0.5, "level_height": 0.5, "max_level": 4 },
              "paint": { "palette": ["#ff0000", "#00FF00"], "max_parts_per_action": 8 },
              "sledge": { "damage": 5, "health_per_durability": 20, "swing_seconds": 1, "impulse": 0 }
            }
            """);
        Assert.Equal(new LiftSettings { PlatformSize = 3, BaseHeight = 0.5f, LevelHeight = 0.5f, MaxLevel = 4 }, settings.Lift);
        Assert.Equal(1.5f, settings.Lift.TopAt(2));
        Assert.Equal(["#ff0000", "#00FF00"], settings.Paint.Palette);
        Assert.Equal(8, settings.Paint.MaxPartsPerAction);
        Assert.Equal(new SledgeSettings { Damage = 5, HealthPerDurability = 20, SwingSeconds = 1, Impulse = 0 }, settings.Sledge);
        Assert.Equal(60f, settings.Sledge.HealthOf(3));
        Assert.Null(settings.Sledge.HealthOf(null));
    }

    [Fact]
    public void ShippedSettingsLoad()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "game", "data", "building.json");
        var settings = BuildSettings.FromJson(File.ReadAllText(path));
        Assert.Equal(16, settings.Paint.Palette.Count);
        Assert.Equal(new ConnectSettings(), settings.Connect);
    }

    [Theory]
    [InlineData("""{"lift": {"level_height": 0}}""", "lift.level_height")]
    [InlineData("""{"lift": {"max_level": -1}}""", "lift.max_level")]
    [InlineData("""{"paint": {"palette": []}}""", "paint.palette")]
    [InlineData("""{"paint": {"palette": ["red"]}}""", "red")]
    [InlineData("""{"sledge": {"damage": 0}}""", "sledge.damage")]
    [InlineData("""{"sledge": {"impulse": -1}}""", "sledge.impulse")]
    [InlineData("""{"lift": {"height": 1}}""", "height")]
    [InlineData("""{"hammer": {"drag_max_per_axis": 0}}""", "hammer.drag_max_per_axis")]
    [InlineData("""{"hammer": {"remove_seconds_by_material": {"wood": -1}}}""", "remove_seconds_by_material.wood")]
    [InlineData("""{"connect": {"node_pixels": 2}}""", "connect.node_pixels")]
    [InlineData("""{"connect": {"invalid_alpha": 2}}""", "connect.invalid_alpha")]
    [InlineData("""{"connect": {"snap_metres": 0}}""", "connect.snap_metres")]
    [InlineData("""{"connect": {"flash_seconds": -1}}""", "connect.flash_seconds")]
    [InlineData("""{"reach": 0}""", "reach")]
    [InlineData("""{"max_parts_per_creation": 0}""", "max_parts_per_creation")]
    [InlineData("""{"clearance_scale": 1.5}""", "clearance_scale")]
    [InlineData("""{"range": 3}""", "range")]
    [InlineData("null", "null")]
    public void RejectsBadValues(string json, string named)
    {
        var e = Assert.Throws<FormatException>(() => BuildSettings.FromJson(json));
        Assert.Contains(named, e.Message);
    }
}
