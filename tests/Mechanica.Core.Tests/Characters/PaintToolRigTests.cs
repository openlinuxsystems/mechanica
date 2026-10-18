using Mechanica.Core.Characters;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Characters;

/// <summary>The Paint Tool's tank twists off and back on through the reload the way the model builder posed it.</summary>
public class PaintToolRigTests
{
    private static PaintToolSettings Shipped() =>
        CharacterSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "character.json"))).PaintTool;

    [Fact]
    public void TheReloadTwistsThePullsAndSeatsTheTank()
    {
        var settings = Shipped();
        Assert.Equal((0f, 0f), PaintToolRig.Tank(settings, "paint_reload", 0f));
        var out_ = PaintToolRig.Tank(settings, "paint_reload", 0.68f);
        Assert.NotNull(out_);
        Assert.Equal(-85f, out_.Value.Twist, 3);
        Assert.Equal(0.03f, out_.Value.Pull, 4);
        Assert.Equal((0f, 0f), PaintToolRig.Tank(settings, "paint_reload", settings.ReloadSeconds + 1f));
        Assert.Equal(PaintToolRig.Tank(settings, "paint_reload", 0.45f), PaintToolRig.Tank(settings, "paint_reload_fp", 0.45f));
        Assert.Null(PaintToolRig.Tank(settings, "paint_spray", 0.1f));
    }

    [Fact]
    public void KeysEaseBetweenEachOther()
    {
        var mid = PaintToolRig.Tank(Shipped(), "paint_reload", 0.45f);
        Assert.NotNull(mid);
        Assert.Equal(-42.5f, mid.Value.Twist, 2);
    }

    [Theory]
    [InlineData("{ \"paint_tool\": { \"reload_seconds\": 0 } }")]
    [InlineData("{ \"paint_tool\": { \"tank\": { \"paint_reload\": [[0, 0, 0], [0, 1]] } } }")]
    [InlineData("{ \"paint_tool\": { \"tank\": { \"paint_reload\": [[0.5, 0, 0], [0.2, 0, 0]] } } }")]
    public void BadPaintToolSettingsAreRejected(string json) =>
        Assert.Throws<FormatException>(() => CharacterSettings.FromJson(json));
}
