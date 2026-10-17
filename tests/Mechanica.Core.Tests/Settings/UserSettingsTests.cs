using Mechanica.Core.Settings;

namespace Mechanica.Core.Tests.Settings;

public class UserSettingsTests
{
    [Fact]
    public void MissingValuesTakeDefaults()
    {
        var settings = UserSettings.Parse("{ // comment\n \"invert_y\": true, }")!;
        Assert.True(settings.InvertY);
        Assert.Equal(UserSettings.DefaultMouseSensitivity, settings.MouseSensitivity);
        Assert.Equal(UserSettings.DefaultFieldOfView, settings.FieldOfView);
        Assert.Equal(1f, settings.UiScale);
    }

    [Fact]
    public void ValuesAreClampedToTheirRanges()
    {
        var settings = UserSettings.Parse("{\"field_of_view\": 400, \"ui_scale\": 0.1, \"mouse_sensitivity\": 5}")!;
        Assert.Equal(UserSettings.MaxFieldOfView, settings.FieldOfView);
        Assert.Equal(0.75f, settings.UiScale);
        Assert.Equal(0.02f, settings.MouseSensitivity);
    }

    [Fact]
    public void BrokenFilesGiveDefaults()
    {
        Assert.Null(UserSettings.Parse("{ not json"));
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "[1, 2");
        try
        {
            Assert.Equal(new UserSettings().ToJson(), UserSettings.Load(path).ToJson());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SavingIsStableAndKeepsUnknownKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}", UserSettings.FileName);
        try
        {
            var settings = UserSettings.Parse("{\"zeta\": 1, \"alpha\": 2, \"invert_y\": true}")!;
            settings.Save(path);
            var text = File.ReadAllText(path);
            Assert.True(text.IndexOf("\"alpha\"", StringComparison.Ordinal) < text.IndexOf("\"zeta\"", StringComparison.Ordinal));
            UserSettings.Load(path).Save(path);
            Assert.Equal(text, File.ReadAllText(path));
            Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
