using Mechanica.Core.Input;
using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class TutorialTests
{
    private static readonly string GameData = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "game", "data", "tutorial.json");

    [Fact]
    public void EachTipShowsOnceForItsTrigger()
    {
        var tutorial = Tutorial.Parse("""
            {"hints": [
              {"id": "a", "trigger": "tool:hammer", "text": "first"},
              {"id": "b", "trigger": "tool:hammer", "text": "second"},
            ]}
            """);
        Assert.Equal("a", tutorial.Next("tool:hammer", new HashSet<string>())!.Id);
        Assert.Equal("b", tutorial.Next("tool:hammer", new HashSet<string> { "a" })!.Id);
        Assert.Null(tutorial.Next("tool:hammer", new HashSet<string> { "a", "b" }));
        Assert.Null(tutorial.Next("tool:lift", new HashSet<string>()));
    }

    [Fact]
    public void KeysFollowTheBindings()
    {
        var keymap = new Keymap();
        Assert.Equal("Press E, not {nope}.", Tutorial.Fill("Press {interact}, not {nope}.", id => InputActions.Find(id) is null ? null : keymap.Label(id)));
    }

    [Theory]
    [InlineData("""{"hints": [{"id": "a", "trigger": "t", "text": "x"}, {"id": "a", "trigger": "t", "text": "y"}]}""")]
    [InlineData("""{"hints": [{"id": "a", "trigger": "t", "text": ""}]}""")]
    [InlineData("""{"tips": [""]}""")]
    [InlineData("""{"hint_seconds": 0}""")]
    [InlineData("{")]
    public void BadDataIsRefused(string json) => Assert.Throws<FormatException>(() => Tutorial.Parse(json));

    [Fact]
    public void TheShippedTipsNameRealActions()
    {
        var tutorial = Tutorial.Parse(File.ReadAllText(GameData));
        Assert.NotEmpty(tutorial.Hints);
        Assert.NotEmpty(tutorial.Tips);
        foreach (var text in tutorial.Hints.Select(h => h.Text).Concat(tutorial.Tips))
            Assert.DoesNotContain("{", Tutorial.Fill(text, id => InputActions.Find(id) is null ? null : "K"));
    }
}
