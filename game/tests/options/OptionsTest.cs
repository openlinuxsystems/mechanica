using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Settings;
using Mechanica.Game.Menus;
using Mechanica.Game.Settings;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Options screen (UI spec §16): its five tabs, rebinding by pressing a key with the swap or overwrite
/// choice on a conflict, Esc cancelling a capture, sliders and toggles applying at once, presets reaching
/// cameras added later, the window's Apply with its timed revert, Reset to Defaults per tab, and Close saving.
/// </summary>
public partial class OptionsTest : StagedTest
{
    private OptionsScreen _screen = null!;
    private string _profile = "";

    protected override string Tag => "options";

    public override void _Ready()
    {
        Add("Options opens with its five tabs", frame =>
        {
            if (frame == 0)
            {
                GameSettings.Apply(new UserSettings());
                _profile = ProjectSettings.GlobalizePath("user://test_options_profile.json");
                if (File.Exists(_profile))
                    File.Delete(_profile);
                _screen = new OptionsScreen { ProfileFile = _profile };
                AddChild(_screen);
                return false;
            }
            Check(OptionsScreen.Tabs.SequenceEqual(["Controls", "Graphics", "Audio", "Gameplay", "Interface"]), "Controls, Graphics, Audio, Gameplay, Interface");
            _screen.ShowTab(2);
            Check(_screen.CurrentTab == "Audio" && Find<Control>("Audio").Visible && !Find<Control>("Controls").Visible, "one tab shows at a time");
            return true;
        });
        Add("pressing a key rebinds, and a conflict can swap", _ =>
        {
            _screen.StartCapture(InputActions.Interact, BindingSlot.Primary);
            Check(_screen.Capturing == InputActions.Interact, "the key field waits for a key");
            _screen._Input(new InputEventKey { PhysicalKeycode = Key.F, Pressed = true });
            Check(_screen.Capturing is null && ConfirmDialog.Current is not null, "F is Force build's, so Options asks");
            Press(ConfirmDialog.Current!, "Swap");
            var keymap = GameSettings.Current.Keymap();
            Check(keymap.Get(InputActions.Interact) == InputChord.Parse("F") && keymap.Get(InputActions.ForceBuild) == InputChord.Parse("E"),
                $"Use is F and Force build took E ({keymap.Label(InputActions.Interact)}, {keymap.Label(InputActions.ForceBuild)})");
            Check(InputMap.ActionGetEvents(InputActions.Interact).OfType<InputEventKey>().Any(k => k.PhysicalKeycode == Key.F), "the game's actions follow at once");
            return true;
        });
        Add("Esc cancels a capture and overwrite leaves a gap", _ =>
        {
            _screen.StartCapture(InputActions.Inventory, BindingSlot.Primary);
            _screen._Input(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });
            Check(_screen.Capturing is null && GameSettings.Current.Keymap().Get(InputActions.Inventory) == InputChord.Parse("I"), "Esc keeps I");
            _screen.Bind(InputActions.Inventory, BindingSlot.Primary, InputChord.Parse("L"));
            ConfirmDialog.Current!.Confirm();
            var keymap = GameSettings.Current.Keymap();
            Check(keymap.Get(InputActions.Inventory) == InputChord.Parse("L") && keymap.Get(InputActions.Logbook) is null, "Overwrite takes L from the logbook");
            return true;
        });
        Add("sliders and toggles apply at once", _ =>
        {
            Find<HSlider>("Master").Value = 0.5;
            Check(Mathf.IsEqualApprox(GameSettings.Current.Audio.Master, 0.5f), "master volume follows the slider");
            Check(Mathf.Abs(AudioServer.GetBusVolumeDb(0) - -6.02f) < 0.1f, $"the Master bus is at -6 dB ({AudioServer.GetBusVolumeDb(0)})");
            Find<CheckButton>("Mute all").ButtonPressed = true;
            Check(AudioServer.IsBusMute(0), "Mute all mutes the Master bus");
            Find<CheckButton>("Invert Y").ButtonPressed = true;
            Check(GameSettings.Current.InvertY, "Invert Y");
            Find<CheckButton>("Show the tool bar and key hints").ButtonPressed = false;
            Check(!GameSettings.Current.Interface.ShowHotbar, "the tool bar can be hidden");
            return true;
        });
        Add("a preset reaches cameras added later", _ =>
        {
            Find<OptionButton>("Preset").Select(0);
            Find<OptionButton>("Preset").EmitSignal(OptionButton.SignalName.ItemSelected, 0);
            var camera = new Camera3D();
            AddChild(camera);
            var low = GraphicsSettings.MinViewDistance + DisplayApplier.FogMargin;
            Check(GameSettings.Current.Graphics.Preset == Quality.Low && Mathf.IsEqualApprox(camera.Far, low), $"Low draws only the simulated ground ({camera.Far} m)");
            camera.QueueFree();
            return true;
        });
        Add("window changes wait for Apply and go back unless kept", _ =>
        {
            var apply = Walk(_screen).OfType<Button>().First(b => b.Text == "Apply");
            Check(apply.Disabled, "nothing to apply yet");
            Find<CheckButton>("VSync").ButtonPressed = false;
            Check(!apply.Disabled && GameSettings.WindowSettings.VSync, "VSync off waits for Apply");
            _screen.ApplyWindow();
            Check(_screen.RevertPending && !GameSettings.WindowSettings.VSync, "Apply uses it and asks to keep it");
            _screen._Process(OptionsScreen.RevertSeconds + 1);
            Check(!_screen.RevertPending && GameSettings.WindowSettings.VSync && _screen.Draft.Graphics.VSync, "unanswered, it goes back");
            return true;
        });
        Add("Reset to Defaults resets the open tab after asking", frame =>
        {
            if (frame == 0)
            {
                _screen.ShowTab(2);
                Press(_screen, "Reset to Defaults");
                Check(ConfirmDialog.Current is not null, "it asks first");
                ConfirmDialog.Current!.Confirm();
                return false;
            }
            Check(GameSettings.Current.Audio == new AudioSettings() && GameSettings.Current.InvertY, "audio is back to defaults; the other tabs keep theirs");
            return true;
        });
        Add("Close saves the name and settings", _ =>
        {
            _screen.PlayerName.Text = "  Quill ";
            _screen.RequestClose();
            Check(Main.UserProfile(null, _profile).Name == "Quill", "the profile has the new name");
            Check(GameSettings.Current.InvertY && GameSettings.Current.Keymap().Get(InputActions.Interact) == InputChord.Parse("F"), "settings stay as set");
            _screen.QueueFree();
            File.Delete(_profile);
            GameSettings.Apply(new UserSettings());
            return true;
        });
    }

    private T Find<T>(string name) where T : Node =>
        _screen.FindChild(name, true, false) as T ?? throw new InvalidOperationException($"no {typeof(T).Name} named {name}");

    private static void Press(Node root, string text)
    {
        var button = Walk(root).OfType<Button>().First(b => b.Text == text);
        button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren())
        foreach (var n in Walk(child))
            yield return n;
    }
}
