using Mechanica.Core.Settings;

namespace Mechanica.Core.Tests.Settings;

public class SettingsGroupsTests
{
    [Fact]
    public void PresetsSetEveryLevel()
    {
        var low = new GraphicsSettings().WithPreset(Quality.Low);
        Assert.Equal((Quality.Low, Quality.Low, Quality.Low, Quality.Low), (low.Shadows, low.Antialiasing, low.Textures, low.Effects));
        Assert.True(low.ViewDistance < new GraphicsSettings().WithPreset(Quality.Ultra).ViewDistance);
    }

    [Fact]
    public void CustomKeepsHandSetLevels()
    {
        var custom = new GraphicsSettings { Shadows = Quality.Low, ViewDistance = 777 }.WithPreset(Quality.Custom);
        Assert.Equal(Quality.Low, custom.Effective().Shadows);
        Assert.Equal(777, custom.Effective().ViewDistance);
        Assert.Equal(Quality.High, new GraphicsSettings { Shadows = Quality.Low }.Effective().Shadows);
    }

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData("1280x720", 1280, 720)]
    public void ResolutionsParse(string text, int w, int h) =>
        Assert.Equal((w, h), new GraphicsSettings { Resolution = text }.ParseResolution());

    [Theory]
    [InlineData("800x600")]
    [InlineData("big")]
    [InlineData(null)]
    public void SmallOrBadResolutionsAreDropped(string? text)
    {
        var g = new GraphicsSettings { Resolution = text }.Clamped();
        Assert.Null(g.Resolution);
        Assert.Null(g.ParseResolution());
    }

    [Fact]
    public void VolumesClampAndConvertToDecibels()
    {
        var a = new AudioSettings { Master = 2, Music = -1, Sfx = float.NaN }.Clamped();
        Assert.Equal((1f, 0f, 1f), (a.Master, a.Music, a.Sfx));
        Assert.Equal(0f, AudioSettings.ToDecibels(1f), 3);
        Assert.Equal(-80f, AudioSettings.ToDecibels(0f));
        Assert.Equal(-6.02f, AudioSettings.ToDecibels(0.5f), 2);
    }

    [Fact]
    public void GameplayValuesStayAllowed()
    {
        var g = new GameplaySettings { AutoSaveMinutes = 7, MaxPlayers = 99 }.Clamped();
        Assert.Equal(10, g.AutoSaveMinutes);
        Assert.Equal(GameplaySettings.MaxMaxPlayers, g.MaxPlayers);
    }

    [Fact]
    public void AutoSaveFiresOnTheIntervalAndNeverWhenOff()
    {
        var clock = new AutoSaveClock();
        Assert.False(clock.Tick(299, 5));
        Assert.True(clock.Tick(1, 5));
        Assert.Equal(0, clock.SinceLastSave);
        Assert.False(clock.Tick(100_000, 0));
        clock.Reset();
        Assert.False(clock.Tick(10, 5));
    }

    [Fact]
    public void NestedGroupsRoundTripAndKeepUnknownKeys()
    {
        var json = """{"graphics": {"display_mode": "borderless", "zz": 1, "aa": 2}, "audio": {"mute": true}, "interface": {"show_hotbar": false}}""";
        var s = UserSettings.Parse(json)!;
        Assert.Equal(DisplayMode.Borderless, s.Graphics.DisplayMode);
        Assert.True(s.Audio.Mute);
        Assert.False(s.Interface.ShowHotbar);
        var text = s.ToJson();
        Assert.True(text.IndexOf("\"aa\"", StringComparison.Ordinal) < text.IndexOf("\"zz\"", StringComparison.Ordinal));
        Assert.Equal(text, UserSettings.Parse(text)!.ToJson());
    }
}
