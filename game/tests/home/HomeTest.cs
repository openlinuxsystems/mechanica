using Godot;
using Mechanica.Core;
using Mechanica.Core.Saves;
using Mechanica.Core.Survival;
using Mechanica.Game.Saves;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// The home screen (UI spec §14): the buttons in the spec's order with Continue first and focused when a
/// save exists, Continue starting the newest world, the version along the bottom, Esc asking before quitting
/// only when Quit has focus, Credits, and Load Game rows with a picture, play time and their own Load and
/// Delete, where Delete asks first and removes the picture too. Nothing on the screen is orange.
/// </summary>
public partial class HomeTest : StagedTest
{
    private SaveStore _store = null!;
    private MainMenu _menu = null!;
    private string _newest = "";
    private bool _started;

    protected override string Tag => "home";

    public override void _Ready()
    {
        Add("the home screen has the spec's buttons with Continue first", frame =>
        {
            if (frame == 0)
            {
                var folder = ProjectSettings.GlobalizePath("user://test_home_saves");
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
                _store = new SaveStore(folder);
                _store.Write(new SaveGame { Name = "Old Farm", SavedAt = DateTimeOffset.Now.AddDays(-3), Time = 600, Mode = GameMode.Survival });
                _newest = _store.Write(new SaveGame { Name = "Rocket Yard", SavedAt = DateTimeOffset.Now.AddHours(-2), Time = 3 * 3600, Mode = GameMode.Creative });
                Image.CreateEmpty(32, 18, false, Image.Format.Rgb8).SavePng(SaveStore.ThumbnailFor(_newest));
                _menu = new MainMenu { Store = _store, StartWorld = () => _started = true };
                AddChild(_menu);
                return false;
            }
            var buttons = _menu.FindChild("Buttons", true, false)!.GetChildren().OfType<Button>().Select(b => b.Text).ToList();
            Check(buttons.SequenceEqual(["Continue", "New Game", "Load Game", "Join Game", "Options", "Credits", "Quit"]), $"buttons: {string.Join(", ", buttons)}");
            Check(_menu.ContinueButton!.HasFocus(), "Continue has the keyboard focus");
            Check(_menu.FindChild("ContinueInfo", true, false) is Label { Text: var info } && info.StartsWith("Rocket Yard", StringComparison.Ordinal) && info.Contains("hours ago"),
                "Continue names the newest world and when it was played");
            Check(_menu.FindChild("Version", true, false) is Label { Text: var v } && v.Contains(MechanicaInfo.Version), "the version is along the bottom");
            Check(_menu.FindChild("ModTools", true, false) is not null, "the mod tools sit beside the buttons");
            return true;
        });
        Add("Continue starts the newest world", _ =>
        {
            _menu.ContinueButton!.EmitSignal(BaseButton.SignalName.Pressed);
            Check(_started && Main.MenuSave is { Name: "Rocket Yard" }, $"Rocket Yard starts ({Main.MenuSave?.Name})");
            Main.MenuSave = null;
            return true;
        });
        Add("Esc asks before quitting only when Quit has focus", frame =>
        {
            if (frame == 0)
            {
                _menu.ContinueButton!.GrabFocus();
                _menu._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });
                Check(ConfirmDialog.Current is null, "Esc on Continue does nothing");
                _menu.QuitButton.GrabFocus();
                return false;
            }
            _menu._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });
            Check(ConfirmDialog.Current is { } d && d.Message.Contains("Quit"), "Esc on Quit asks");
            ConfirmDialog.Current?.Cancel();
            return true;
        });
        Add("Credits open and close back to the home screen", _ =>
        {
            var credits = _menu.OpenCredits();
            Check(credits.Body.GetChild<RichTextLabel>(0).GetParsedText().Contains("Godot"), "the credits name the engine");
            Check(!_menu.GetNode<Control>("Home").Visible, "the home buttons hide behind it");
            credits.RequestClose();
            Check(_menu.GetNode<Control>("Home").Visible, "and come back");
            return true;
        });
        Add("Load Game rows show a picture, mode and play time", _ =>
        {
            var screen = _menu.OpenLoad();
            Check(screen.Rows.Count == 2, $"two worlds ({screen.Rows.Count})");
            Check(Walk(screen.Rows[0]).OfType<TextureRect>().Any() && !Walk(screen.Rows[1]).OfType<TextureRect>().Any(), "the newest has its picture");
            var details = Walk(screen.Rows[0]).OfType<Label>().Select(l => l.Text).ToList();
            Check(details.Contains("Rocket Yard") && details.Any(t => t.Contains("Creative") && t.Contains("3 h 00 min")), $"details: {string.Join(" | ", details)}");
            Check(Walk(screen.Rows[0]).OfType<Button>().Select(b => b.Text).SequenceEqual(["Load", "Delete"]), "each row has Load and Delete");
            return true;
        });
        Add("Delete asks, then removes the save and its picture", frame =>
        {
            var screen = Walk(_menu).OfType<SaveScreen>().First();
            if (frame == 0)
            {
                var dialog = screen.Delete(0);
                Check(dialog is not null && File.Exists(_newest), "it asks first");
                dialog!.Confirm();
                return false;
            }
            Check(!File.Exists(_newest) && !File.Exists(SaveStore.ThumbnailFor(_newest)), "the save and picture are gone");
            Check(screen.Rows.Count == 1 && screen.Status.Contains("Deleted"), "one world is left");
            screen.RequestClose();
            return true;
        });
        Add("nothing on the home screen is orange", _ =>
        {
            foreach (var control in Walk(_menu).OfType<Control>())
            {
                if (UiTokens.IsOrange(control.Modulate) || UiTokens.IsOrange(control.SelfModulate))
                    Check(false, $"{control.GetPath()} is tinted orange");
            }
            Directory.Delete(_store.Directory, true);
            return true;
        });
    }

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren())
        foreach (var n in Walk(child))
            yield return n;
    }
}
