using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

public class StreamingSettingsTests
{
    [Fact]
    public void DefaultsAreValid() => new StreamingSettings().Validate();

    [Fact]
    public void ReadsSnakeCaseWithCommentsAndTrailingCommas()
    {
        var settings = StreamingSettings.FromJson("""
            {
              // small chunks for a test
              "chunk_size": 16,
              "max_unattended_creations": 2,
            }
            """);
        Assert.Equal(16f, settings.ChunkSize);
        Assert.Equal(2, settings.MaxUnattendedCreations);
        Assert.Equal(new StreamingSettings().UnloadDelay, settings.UnloadDelay);
    }

    [Fact]
    public void ShippedGameDataParses()
    {
        var path = Path.Combine(RepoRoot(), "game", "data", "streaming.json");
        var settings = StreamingSettings.FromJson(File.ReadAllText(path));
        Assert.True(settings.PlayerLoadDistance >= settings.PlayerNearDistance);
    }

    [Theory]
    [InlineData("""{ "chunk_size": 0 }""", "chunk_size")]
    [InlineData("""{ "unload_delay": -1 }""", "unload_delay")]
    [InlineData("""{ "max_unattended_chunks": -1 }""", "max_unattended_chunks")]
    [InlineData("""{ "world_size": 32, "chunk_size": 64 }""", "world_size")]
    [InlineData("""{ "chunk_sise": 64 }""", "chunk_sise")]
    [InlineData("null", "null")]
    public void BadDataGivesAClearError(string json, string mentioned)
    {
        var error = Assert.Throws<FormatException>(() => StreamingSettings.FromJson(json));
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
