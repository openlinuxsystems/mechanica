using Godot;
using Mechanica.Core.Net;
using Mechanica.Game.Menus;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// Windows keep their own mouse input in a running world (UI spec §13). Wheel turns and clicks anywhere over
/// the in-game menu, Options (every tab, scrolled past both ends), the save screen, the inventory and a
/// confirm dialog never reach the game behind them, nor do clicks on the screen around a window opened from
/// the menu, where a wheel changes the hotbar and a click takes the
/// mouse back and closes the menu. The windows are still open afterwards.
/// </summary>
public partial class WindowsTest : StagedTest
{
    private readonly List<InputEvent> _leaked = [];
    private Main _main = null!;
    private PauseMenu _menu = null!;

    protected override string Tag => "windows";

    public override void _Ready()
    {
        var recorder = new Recorder { Name = "Recorder", Leaked = _leaked };
        AddChild(recorder);
        Add("a world starts with the player's own input", frame =>
        {
            if (frame == 0)
            {
                _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
                _main.Options = LaunchOptions.Offline with { Seed = 7 };
                AddChild(_main);
                return false;
            }
            if (_main.LocalPlayer is null || _main.GetNodeOrNull("LocalPlayerInput") is null)
                return false;
            _menu = _main.GetNode<PauseMenu>("PauseMenu");
            // Last in the tree, so it hears unhandled input before the game's own handlers can use it up.
            MoveChild(recorder, -1);
            return true;
        });
        Add("the in-game menu keeps the wheel and clicks", frame =>
        {
            if (frame == 0)
            {
                _menu.Open();
                return false;
            }
            Scrub(_menu.GetNode<Control>("Menu").GetChild<Control>(0));
            Check(_menu.IsOpen, "the menu is still open");
            return Clean("the menu");
        });
        Add("Options keeps the wheel on every tab, past both ends", frame =>
        {
            if (frame == 0)
            {
                _menu.OpenOptions();
                return false;
            }
            var options = Find<OptionsScreen>(_menu);
            for (var tab = 0; tab < OptionsScreen.Tabs.Length; tab++)
            {
                options.ShowTab(tab);
                Scrub(options, wheels: 80);
            }
            Scrub(_menu.GetNode<Control>("Menu"), wheels: 4);
            Check(_menu.IsOpen && IsInstanceValid(options) && !options.IsQueuedForDeletion(), "Options is still open");
            return Clean("Options");
        });
        Add("a confirm dialog keeps them too", frame =>
        {
            var options = Find<OptionsScreen>(_menu);
            if (frame == 0)
            {
                ConfirmDialog.Ask(options, "Reset", "Reset?", "Reset", () => { });
                return false;
            }
            Scrub(ConfirmDialog.Current!.Window);
            Scrub(_menu.GetNode<Control>("Menu"), wheels: 4);
            ConfirmDialog.Current!.Cancel();
            options.RequestClose();
            return Clean("the confirm dialog and the dimmed screen behind it");
        });
        Add("the save screen keeps them", frame =>
        {
            if (frame == 0)
            {
                _menu.OpenSaves();
                return false;
            }
            Scrub(Find<Saves.SaveScreen>(_menu));
            Check(_menu.IsOpen, "the menu is still open");
            _menu.Close();
            return Clean("the save screen");
        });
        Add("the inventory keeps them", frame =>
        {
            var inventory = _main.GetNode<Items.InventoryScreen>("InventoryScreen");
            if (frame == 0)
            {
                inventory.Show(null);
                return false;
            }
            Scrub(inventory.GetNode<Control>("Panel"));
            Check(inventory.IsOpen, "the inventory is still open");
            inventory.Hide();
            return Clean("the inventory");
        });
    }

    private bool Clean(string what)
    {
        Check(_leaked.Count == 0, $"no click or wheel over {what} reaches the game ({_leaked.Count} did{(_leaked.Count > 0 ? $", first {_leaked[0].AsText()} at {((InputEventMouse)_leaked[0]).Position}" : "")})");
        _leaked.Clear();
        return true;
    }

    // Wheel both ways, well past either end, then a click on every point of a grid over the control's
    // padding and labels (buttons are left alone: pressing them is not what this checks).
    private void Scrub(Control control, int wheels = 30)
    {
        foreach (var at in Points(control.GetGlobalRect()))
        {
            for (var i = 0; i < wheels; i++)
                Press(at, i < wheels / 2 ? MouseButton.WheelDown : MouseButton.WheelUp);
            Move(at);
            if (GetViewport().GuiGetHoveredControl() is not BaseButton and not Godot.Range and not LineEdit)
                Press(at, MouseButton.Right);
        }
    }

    private static IEnumerable<Vector2> Points(Rect2 rect)
    {
        for (var x = 0.04f; x < 1; x += 0.12f)
        for (var y = 0.04f; y < 1; y += 0.12f)
            yield return rect.Position + rect.Size * new Vector2(x, y);
    }

    private void Move(Vector2 at) => GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });

    private void Press(Vector2 at, MouseButton button)
    {
        Move(at);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at, Factor = 1 });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at, Factor = 1 });
    }

    private static T Find<T>(Node root) where T : Node => Walk(root).OfType<T>().First();

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren())
        foreach (var n in Walk(child))
            yield return n;
    }

    // Stands in for the game behind the windows: every press that reaches unhandled input.
    private sealed partial class Recorder : Node
    {
        public List<InputEvent> Leaked { get; init; } = null!;

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { Pressed: true })
                Leaked.Add(@event);
        }
    }
}
