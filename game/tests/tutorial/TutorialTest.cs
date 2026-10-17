using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Net;
using Mechanica.Core.Ui;
using Mechanica.Game.Menus;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// Tutorial hints (UI spec §16 Gameplay): a tip shows when a world starts, then one for the tool in hand,
/// each with the player's keys and each only once. Picking up another tool or opening the inventory shows
/// theirs; with hints off nothing shows, and showing tips again brings them back.
/// </summary>
public partial class TutorialTest : StagedTest
{
    private Main _main = null!;
    private TutorialHints _hints = null!;
    private bool _shownWhileOff;

    protected override string Tag => "tutorial";

    public override void _Ready()
    {
        Add("a world starts", frame =>
        {
            if (frame == 0)
            {
                GameSettings.Apply(new());
                _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
                _main.Options = LaunchOptions.Offline with { Seed = 7 };
                AddChild(_main);
                return false;
            }
            if (_main.LocalPlayer is null || _main.Tools is null || _main.GetNodeOrNull("TutorialHints") is null)
                return false;
            // A new world asks which character to play first, and tips wait while that window covers the game.
            if (_main.Characters.Window is { } choice)
            {
                choice.Confirm();
                return false;
            }
            if (_main.Characters.LocalNeedsChoice)
                return false;
            _hints = _main.GetNode<TutorialHints>("TutorialHints");
            // The shipped tips, shown for a fifth of a second each.
            using var file = Godot.FileAccess.Open(TutorialHints.DataPath, Godot.FileAccess.ModeFlags.Read);
            _hints.Tutorial = Tutorial.Parse(file.GetAsText().Replace("\"hint_seconds\": 8", "\"hint_seconds\": 0.2"));
            return true;
        });
        Add("the start tip shows with the player's keys", _ =>
        {
            if (_hints.Showing is null)
                return false;
            Check(_hints.Showing.Id == "start", $"the first tip is the start one ({_hints.Showing.Id})");
            Check(_hints.Text.Contains(GameInput.Label(InputActions.Logbook)) && !_hints.Text.Contains('{'), $"keys are filled in: {_hints.Text}");
            return true;
        });
        Add("then the tool in hand", _ =>
        {
            if (_hints.Showing?.Id is null or "start")
                return false;
            Check(_hints.Showing.Id == "hammer", $"the Hammer's tip follows ({_hints.Showing.Id})");
            Check(Seen.Contains("start"), "the start tip is remembered as seen");
            return true;
        });
        Add("another tool shows its own", frame =>
        {
            if (frame == 0)
                _main.Tools!.Use(_main.Tools.Lift);
            if (!Until(_hints.Showing?.Id == "lift", frame, "the Lift's tip shows"))
                return false;
            Check(Seen.Contains("hammer"), "the Hammer's tip is seen");
            return true;
        });
        Add("each shows once", frame =>
        {
            if (frame == 0)
                _main.Tools!.Use(_main.Tools.Hammer);
            if (frame < 60)
                return false;
            Check(_hints.Showing is null, $"no tip comes back ({_hints.Showing?.Id})");
            return true;
        });
        Add("with hints off nothing shows", frame =>
        {
            var inventory = _main.GetNode<Items.InventoryScreen>("InventoryScreen");
            if (frame == 0)
            {
                GameSettings.Apply(GameSettings.Current with { Gameplay = GameSettings.Current.Gameplay with { ShowTutorialHints = false } });
                inventory.Show(null);
            }
            _shownWhileOff |= _hints.Showing is not null;
            if (frame < 60)
                return false;
            Check(!_shownWhileOff, "no tip while hints are off");
            Check(!Seen.Contains("inventory"), "the inventory tip is still unseen");
            GameSettings.Apply(GameSettings.Current with { Gameplay = GameSettings.Current.Gameplay with { ShowTutorialHints = true } });
            return true;
        });
        Add("turned back on, the open screen's tip shows", frame =>
        {
            if (!Until(_hints.Showing?.Id == "inventory", frame, "the inventory tip shows"))
                return false;
            _main.GetNode<Items.InventoryScreen>("InventoryScreen").Hide();
            return true;
        });
        Add("showing tips again brings them back", frame =>
        {
            if (frame == 0)
                GameSettings.Apply(GameSettings.Current with { Gameplay = GameSettings.Current.Gameplay with { SeenHints = null } });
            if (!Until(_hints.Showing?.Id == "start", frame, "the start tip shows again"))
                return false;
            return true;
        });
    }

    // True once done holds, or (failing the check) after two seconds.
    private bool Until(bool done, int frame, string what)
    {
        if (done)
            return true;
        if (frame < 120)
            return false;
        Check(false, what);
        return true;
    }

    private IReadOnlySet<string> Seen => GameSettings.Current.Gameplay.SeenHints ?? [];
}
