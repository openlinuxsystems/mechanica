using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Player;
using Mechanica.Core.Settings;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Tests;

/// <summary>
/// Rebindable controls (UI spec §1, §16): every action is a Godot input action with the UI spec's
/// default keys, presses match modifiers exactly (Q is not Shift+Q, the wheel is not Alt+wheel),
/// rebinding through the settings rebuilds the actions and the HUD labels, a test run never reads the
/// player's own settings file, and a player whose mouse is free (a menu is open) doesn't walk.
/// </summary>
public partial class ControlsTest : StagedTest
{
    private PlayerController _player = null!;

    protected override string Tag => "controls";

    public override void _Ready()
    {
        Add("every action is registered with the spec's keys", _ =>
        {
            Check(GameSettings.Current.Bindings is null, "a test run starts from the default settings");
            foreach (var def in InputActions.All)
                Check(InputMap.HasAction(def.Id), $"{def.Id} is an input action");
            Check(Has(InputActions.Interact, Key.E) && Has(InputActions.Inventory, Key.I) && Has(InputActions.Inventory, Key.Tab)
                && Has(InputActions.Logbook, Key.L) && Has(InputActions.Handbook, Key.H) && Has(InputActions.Hotbar(10), Key.Key0),
                "E uses, I or Tab opens the inventory, L the logbook, H the handbook, 0 is hotbar 10");
            return true;
        });
        Add("presses match modifiers exactly", _ =>
        {
            Check(GameInput.Pressed(KeyPress(Key.Q), InputActions.Rotate) && !GameInput.Pressed(KeyPress(Key.Q), InputActions.RotateBack),
                "Q is next rotation only");
            Check(GameInput.Pressed(KeyPress(Key.Q, shift: true), InputActions.RotateBack) && !GameInput.Pressed(KeyPress(Key.Q, shift: true), InputActions.Rotate),
                "Shift+Q is previous rotation only");
            Check(GameInput.Pressed(Wheel(MouseButton.WheelUp), InputActions.HotbarPrevious) && !GameInput.Pressed(Wheel(MouseButton.WheelUp), InputActions.ZoomIn),
                "the wheel changes hotbar slot");
            Check(GameInput.Pressed(Wheel(MouseButton.WheelUp, alt: true), InputActions.ZoomIn) && !GameInput.Pressed(Wheel(MouseButton.WheelUp, alt: true), InputActions.HotbarPrevious),
                "Alt+wheel zooms");
            Check(GameInput.Pressed(KeyPress(Key.Shift, shift: true), InputActions.Sprint), "a modifier key on its own is its own binding");
            Check(!GameInput.Pressed(new InputEventKey { PhysicalKeycode = Key.E, Pressed = true, Echo = true }, InputActions.Interact), "key repeats are not presses");
            Check(GameInput.Label(InputActions.Primary) == "Left Click" && GameInput.Label(InputActions.RotateBack) == "Shift + Q", "labels read as players expect");
            return true;
        });
        Add("rebinding rebuilds the actions and labels", _ =>
        {
            var keymap = new Keymap();
            keymap.Set(InputActions.Interact, BindingSlot.Primary, InputChord.Parse("F"), swap: true);
            GameSettings.Apply(new UserSettings().WithKeymap(keymap));
            Check(Has(InputActions.Interact, Key.F) && !Has(InputActions.Interact, Key.E), "Use moved to F");
            Check(Has(InputActions.ForceBuild, Key.E), "and Force build took E in the swap");
            Check(GameInput.Label(InputActions.Interact) == "F" && GameInput.Pressed(KeyPress(Key.F), InputActions.Interact), "presses and labels follow");
            GameSettings.Apply(new UserSettings());
            Check(Has(InputActions.Interact, Key.E), "defaults come back");
            return true;
        });
        Add("a player with the mouse free stands still", frame =>
        {
            if (frame == 0)
            {
                _player = new PlayerController { Name = "Player" };
                AddChild(_player);
                AddChild(new LocalPlayerInput { Name = "Input", Target = _player });
                Input.MouseMode = Input.MouseModeEnum.Visible;
                Input.ActionPress(InputActions.MoveForward);
                Input.ActionPress(InputActions.Jump);
                return false;
            }
            if (frame < 3)
                return false;
            var input = _player.CurrentInput;
            Input.ActionRelease(InputActions.MoveForward);
            Input.ActionRelease(InputActions.Jump);
            Check(input.MoveZ == 0 && !input.Jump, $"held keys don't move a player whose mouse a menu has ({input})");
            return true;
        });
    }

    private static bool Has(string action, Key key) =>
        InputMap.ActionGetEvents(action).OfType<InputEventKey>().Any(e => e.PhysicalKeycode == key);

    private static InputEventKey KeyPress(Key key, bool shift = false) =>
        new() { PhysicalKeycode = key, Pressed = true, ShiftPressed = shift };

    private static InputEventMouseButton Wheel(MouseButton button, bool alt = false) =>
        new() { ButtonIndex = button, Pressed = true, AltPressed = alt };
}
