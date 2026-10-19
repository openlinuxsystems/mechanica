using Mechanica.Core.Player;

namespace Mechanica.Core.Tests.Player;

public class PlayerMovementSettingsTests
{
    [Fact]
    public void DefaultsAreValid() => new PlayerMovementSettings().Validate();

    [Fact]
    public void ReadsSnakeCaseWithCommentsAndTrailingCommas()
    {
        var settings = PlayerMovementSettings.FromJson("""
            {
              // tuned for testing
              "walk_speed": 3.5,
              "jump_speed": 5,
            }
            """);
        Assert.Equal(3.5f, settings.WalkSpeed);
        Assert.Equal(5f, settings.JumpSpeed);
        Assert.Equal(new PlayerMovementSettings().SprintSpeed, settings.SprintSpeed);
    }

    [Fact]
    public void ShippedGameDataParses()
    {
        var path = Path.Combine(RepoRoot(), "game", "data", "player.json");
        var settings = PlayerMovementSettings.FromJson(File.ReadAllText(path));
        Assert.True(settings.SprintSpeed > settings.WalkSpeed);
        Assert.InRange(settings.StepHeight, 0.25f, 0.5f - 0.01f); // one block, never two
    }

    [Theory]
    [InlineData("""{ "walk_speed": 0 }""", "walk_speed")]
    [InlineData("""{ "gravity": -1 }""", "gravity")]
    [InlineData("""{ "max_pitch_degrees": 90 }""", "max_pitch_degrees")]
    [InlineData("""{ "height": 0.5, "radius": 0.3 }""", "radius")]
    [InlineData("""{ "eye_height": 2 }""", "eye_height")]
    [InlineData("""{ "crouch_height": 2 }""", "crouch_height")]
    [InlineData("""{ "crouch_height": 0.5 }""", "crouch_height")]
    [InlineData("""{ "crouch_eye_height": 1.3 }""", "crouch_eye_height")]
    [InlineData("""{ "stance_speed": 0 }""", "stance_speed")]
    [InlineData("""{ "walk_sped": 4 }""", "walk_sped")]
    [InlineData("""{ "walk_speed": "fast" }""", "walk_speed")]
    [InlineData("""{ "camera_max_distance": 1 }""", "camera_max_distance")]
    [InlineData("""{ "camera_distance": 1 }""", "camera_distance")]
    [InlineData("""{ "camera_zoom_step": 0 }""", "camera_zoom_step")]
    [InlineData("""{ "camera_shoulder": 5 }""", "camera_shoulder")]
    [InlineData("""{ "step_height": -0.1 }""", "step_height")]
    [InlineData("""{ "step_height": 0.6 }""", "step_height")]
    [InlineData("null", "null")]
    public void BadDataGivesAClearError(string json, string mentioned)
    {
        var error = Assert.Throws<FormatException>(() => PlayerMovementSettings.FromJson(json));
        Assert.Contains(mentioned, error.Message);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mechanica.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Mechanica.sln not found above the test output.");
    }
}
