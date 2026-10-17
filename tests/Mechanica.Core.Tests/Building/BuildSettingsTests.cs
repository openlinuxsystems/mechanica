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

    [Theory]
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
