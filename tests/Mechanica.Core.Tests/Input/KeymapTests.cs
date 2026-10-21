using Mechanica.Core.Input;
using Mechanica.Core.Settings;

namespace Mechanica.Core.Tests.Input;

public class KeymapTests
{
    [Theory]
    [InlineData("Q", "Q", false, false, false)]
    [InlineData("Shift+Q", "Q", true, false, false)]
    [InlineData("Alt+Z", "Z", false, false, true)]
    [InlineData("Ctrl+Shift+Alt+WheelUp", "WheelUp", true, true, true)]
    public void ChordsParse(string text, string code, bool shift, bool ctrl, bool alt)
    {
        var chord = InputChord.Parse(text);
        Assert.Equal(new InputChord(code, shift, ctrl, alt), chord);
        Assert.Equal(chord, InputChord.Parse(chord.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Shift+")]
    [InlineData("Shift+Shift+Q")]
    [InlineData("Hyper+Q")]
    [InlineData("Q-")]
    public void BadChordsAreRefused(string text) => Assert.False(InputChord.TryParse(text, out _));

    [Theory]
    [InlineData("Shift+Q", "Shift + Q")]
    [InlineData("MouseLeft", "Left Click")]
    [InlineData("Alt+WheelUp", "Alt + Scroll Up")]
    [InlineData("Key0", "0")]
    [InlineData("Escape", "Esc")]
    public void ChordsDisplayForPlayers(string text, string shown) => Assert.Equal(shown, InputChord.Parse(text).Display());

    [Fact]
    public void DefaultsMatchTheSpec()
    {
        var map = new Keymap();
        Assert.Equal("E", map.Get(InputActions.Interact)?.ToString());
        Assert.Equal("I", map.Get(InputActions.Inventory)?.ToString());
        Assert.Equal("Tab", map.Get(InputActions.Inventory, BindingSlot.Alternate)?.ToString());
        Assert.Equal("L", map.Get(InputActions.Logbook)?.ToString());
        Assert.Equal("H", map.Get(InputActions.Handbook)?.ToString());
        Assert.Equal("Shift+Q", map.Get(InputActions.RotateBack)?.ToString());
        Assert.Equal("Up", map.Get(InputActions.LiftUp)?.ToString());
        Assert.Equal("Key0", map.Get(InputActions.Hotbar(10))?.ToString());
        Assert.Equal("Alt+Z", map.Get(InputActions.HideGui)?.ToString());
        Assert.Equal("X", map.Get(InputActions.ZoomIn)?.ToString());
    }

    [Fact]
    public void DefaultsHaveNoConflicts() => Assert.Empty(new Keymap().Conflicting());

    [Fact]
    public void ActionIdsAreUnique() =>
        Assert.Equal(InputActions.All.Count, InputActions.All.Select(a => a.Id).Distinct().Count());

    [Fact]
    public void CrawlAndSeatedCameraShareCtrlBecauseTheyNeverOverlap()
    {
        var map = new Keymap();
        Assert.Equal(map.Get(InputActions.Crawl), map.Get(InputActions.SeatCamera));
        Assert.Empty(map.ConflictsWith(InputActions.Crawl, InputChord.Parse("Ctrl")));
    }

    [Fact]
    public void RebindingOntoAnotherActionsKeyReportsAndTakesIt()
    {
        var map = new Keymap();
        var conflicts = map.ConflictsWith(InputActions.Reload, InputChord.Parse("E"));
        Assert.Equal([new BindingConflict(InputActions.Interact, BindingSlot.Primary)], conflicts);
        map.Set(InputActions.Reload, BindingSlot.Primary, InputChord.Parse("E"));
        Assert.Equal("E", map.Get(InputActions.Reload)?.ToString());
        Assert.Null(map.Get(InputActions.Interact));
        Assert.Equal("unbound", map.Label(InputActions.Interact));
    }

    [Fact]
    public void SwappingGivesTheOtherActionTheOldKey()
    {
        var map = new Keymap();
        map.Set(InputActions.Reload, BindingSlot.Primary, InputChord.Parse("E"), swap: true);
        Assert.Equal("E", map.Get(InputActions.Reload)?.ToString());
        Assert.Equal("R", map.Get(InputActions.Interact)?.ToString());
        Assert.Empty(map.Conflicting());
    }

    [Fact]
    public void ResetsPutDefaultsBack()
    {
        var map = new Keymap();
        map.Set(InputActions.Jump, BindingSlot.Primary, InputChord.Parse("J"));
        map.Set(InputActions.Inventory, BindingSlot.Alternate, null);
        map.ResetCategory("Movement");
        Assert.Equal("Space", map.Get(InputActions.Jump)?.ToString());
        Assert.Null(map.Get(InputActions.Inventory, BindingSlot.Alternate));
        map.ResetAll();
        Assert.Equal("Tab", map.Get(InputActions.Inventory, BindingSlot.Alternate)?.ToString());
    }

    [Fact]
    public void OverridesRoundTripThroughSettings()
    {
        var map = new Keymap();
        map.Set(InputActions.Jump, BindingSlot.Primary, InputChord.Parse("Ctrl+J"));
        map.Set(InputActions.Inventory, BindingSlot.Alternate, null);
        var settings = new UserSettings().WithKeymap(map);
        Assert.Equal(["Ctrl+J", ""], settings.Bindings![InputActions.Jump]);
        Assert.Equal(["I", ""], settings.Bindings[InputActions.Inventory]);
        Assert.Equal(2, settings.Bindings.Count);

        var back = UserSettings.Parse(settings.ToJson())!.Keymap();
        Assert.Equal("Ctrl+J", back.Get(InputActions.Jump)?.ToString());
        Assert.Null(back.Get(InputActions.Inventory, BindingSlot.Alternate));
        Assert.Equal("W", back.Get(InputActions.MoveForward)?.ToString());
    }

    [Fact]
    public void UnknownActionsAndBadChordsInTheFileAreSkipped()
    {
        var map = Keymap.FromOverrides(new Dictionary<string, List<string>>
        {
            ["no_such_action"] = ["Q"],
            [InputActions.Jump] = ["Not a key!"],
        });
        Assert.Equal("Space", map.Get(InputActions.Jump)?.ToString());
    }

    [Fact]
    public void BindingTheSameChordToBothSlotsKeepsOne()
    {
        var map = new Keymap();
        map.Set(InputActions.Inventory, BindingSlot.Primary, InputChord.Parse("Tab"));
        Assert.Equal("Tab", map.Get(InputActions.Inventory)?.ToString());
        Assert.Null(map.Get(InputActions.Inventory, BindingSlot.Alternate));
    }

    [Theory]
    [InlineData("Shift+WheelDown", "Shift", "WheelDown")]
    [InlineData("Ctrl+Shift+Q", "Ctrl", "Shift+Q")]
    [InlineData("Alt+WheelUp", "Shift", "Alt+WheelUp")]
    [InlineData("Shift+WheelUp", "Q", "Shift+WheelUp")]
    [InlineData("Shift", "Shift", "Shift")]
    public void HeldModifiersComeOff(string chord, string held, string left) =>
        Assert.Equal(left, InputChord.Parse(chord).Without(InputChord.Parse(held)).ToString());
}
