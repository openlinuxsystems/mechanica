using Godot;
using Mechanica.Core.Ui;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// The shared UI foundation (UI spec §11, §13): every scene gets the blue theme on its root window,
/// the window scales from a 1280×720 base and won't shrink below it, tooltips wait 0.25 s, buttons grow
/// 1.05× on hover, a <see cref="ToolWindow"/> closes on Esc and its X (through its guard), a
/// <see cref="ConfirmDialog"/> answers yes or no, and nothing in the theme or the main menu is orange.
/// </summary>
public partial class UiTest : StagedTest
{
    private Button _button = null!;
    private ToolWindow _window = null!;
    private int _closes;
    private string _answer = "";

    protected override string Tag => "ui";

    public override void _Ready()
    {
        Add("the root window has the theme and the window rules", _ =>
        {
            var root = GetTree().Root;
            Check(root.Theme == MechanicaTheme.Theme, "the root window uses the shared theme");
            Check(root.Theme.IsTypeVariation("PrimaryButton", "Button"), "with a cyan primary button variation");
            Check(root.Theme.GetStylebox("panel", "PanelContainer") is StyleBoxFlat { BorderColor: var border, BgColor: var bg }
                && border.IsEqualApprox(UiTokens.Blue) && bg.IsEqualApprox(UiTokens.Panel),
                "panels are navy at 85% with a blue border");
            Check(Mathf.IsEqualApprox((float)ProjectSettings.GetSetting("gui/timers/tooltip_delay_sec"), UiPalette.TooltipDelaySeconds),
                "tooltips wait 0.25 s");
            Check((string)ProjectSettings.GetSetting("display/window/stretch/mode") == "canvas_items"
                && (int)ProjectSettings.GetSetting("display/window/size/viewport_width") == 1280
                && (int)ProjectSettings.GetSetting("display/window/size/viewport_height") == 720,
                "the UI is laid out for 1280×720 and stretches with the window");
            Check(root.MinSize == new Vector2I(1280, 720), $"the window can't shrink below 1280×720 ({root.MinSize})");
            UiRoot.UiScale = 2f;
            Check(Mathf.IsEqualApprox(root.ContentScaleFactor, 1.5f), "the UI scale is clamped to 150%");
            UiRoot.UiScale = 1f;
            return true;
        });
        Add("nothing in the theme is orange", _ =>
        {
            var theme = MechanicaTheme.Theme;
            foreach (var type in theme.GetColorTypeList())
            foreach (var name in theme.GetColorList(type))
                Check(!UiTokens.IsOrange(theme.GetColor(name, type)), $"{type}/{name} is not orange");
            foreach (var type in theme.GetStyleboxTypeList())
            foreach (var name in theme.GetStyleboxList(type))
            {
                if (theme.GetStylebox(name, type) is StyleBoxFlat flat && (UiTokens.IsOrange(flat.BgColor) || UiTokens.IsOrange(flat.BorderColor)))
                    Check(false, $"{type}/{name} is orange");
            }
            return true;
        });
        Add("buttons grow on hover", frame =>
        {
            if (frame == 0)
            {
                _button = new Button { Text = "Hover me", Position = new Vector2(100, 100), Size = new Vector2(200, 40) };
                AddChild(_button);
                Check(_button.HasMeta("hover_scale"), "new buttons get the hover scale");
                _button.EmitSignal(Control.SignalName.MouseEntered);
            }
            if (frame < 15)
                return false;
            Check(_button.Scale.IsEqualApprox(new Vector2(1.05f, 1.05f)), $"to 1.05 ({_button.Scale})");
            Check(_button.PivotOffset.IsEqualApprox(_button.Size / 2), "around their centre");
            return true;
        });
        Add("and shrink back when the mouse leaves", frame =>
        {
            if (frame == 0)
                _button.EmitSignal(Control.SignalName.MouseExited);
            if (frame < 15)
                return false;
            Check(_button.Scale.IsEqualApprox(Vector2.One), $"to 1 ({_button.Scale})");
            var check = new CheckBox();
            AddChild(check);
            Check(!check.HasMeta("hover_scale"), "check boxes keep their size");
            return true;
        });
        Add("a tool window closes on Esc and its X, through its guard", _ =>
        {
            _window = new ToolWindow { Title = "Test tool" };
            _window.CloseRequested += () => _closes++;
            AddChild(_window);
            Check(_window.TitleLabel.Text == "Test tool", "the title bar shows the title");
            Check(!_window.Actions.Visible, "the action bar hides until it has a button");
            var apply = _window.AddAction("Apply", () => { }, primary: true);
            Check(_window.Actions.Visible && apply.ThemeTypeVariation == "PrimaryButton", "a primary action is cyan, bottom right");
            Esc();
            Check(_closes == 1, "Esc closes it");
            _window.CloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(_closes == 2, "so does the X");
            Action? later = null;
            _window.CloseGuard = go => later = go;
            Esc();
            Check(_closes == 2 && later is not null, "a guard can hold the close");
            later!();
            Check(_closes == 3, "and let it go");
            return true;
        });
        Add("a confirm dialog answers yes or no", _ =>
        {
            ConfirmDialog.Ask(this, "Delete", "Delete it?", "Delete", () => _answer = "yes", () => _answer = "no");
            Check(ConfirmDialog.Current is { Message: "Delete it?" } d && d.ConfirmButton.ThemeTypeVariation == "DangerButton",
                "a destructive confirm uses the danger button");
            Check(ToolWindow.Topmost == ConfirmDialog.Current!.Window, "and sits on top");
            Esc();
            Check(_answer == "no" && _closes == 3, $"Esc cancels it and leaves the window behind alone ({_answer}, {_closes})");
            ConfirmDialog.Ask(this, "Reset", "Reset?", "Reset", () => _answer = "yes", destructive: false).Confirm();
            Check(_answer == "yes", "confirm runs the action");
            return true;
        });
        Add("the main menu has no orange", frame =>
        {
            if (frame == 0)
            {
                var menu = GD.Load<PackedScene>(MainMenu.MenuScene).Instantiate<MainMenu>();
                AddChild(menu);
                menu.OpenNewGame();
                menu.OpenJoin();
                menu.OpenSettings();
                return false;
            }
            foreach (var control in Walk(this).OfType<Control>())
            {
                if (UiTokens.IsOrange(control.Modulate) || UiTokens.IsOrange(control.SelfModulate))
                    Check(false, $"{control.GetPath()} is tinted orange");
            }
            return true;
        });
    }

    private void Esc() => GetViewport().PushInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape, Keycode = Key.Escape });

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren())
        foreach (var n in Walk(child))
            yield return n;
    }
}
