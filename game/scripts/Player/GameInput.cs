using Godot;
using Mechanica.Core.Input;

namespace Mechanica.Game.Player;

/// <summary>
/// Connects the player's <see cref="Keymap"/> to Godot (UI spec §1, "All binds are rebindable"). Every
/// <see cref="InputActions"/> id becomes an input action with the player's bindings, which is what held
/// keys are polled through (<c>Input.GetVector</c>, <c>Input.IsActionPressed</c>). Presses are matched
/// with <see cref="Pressed"/> instead, which compares modifiers exactly, so <c>Q</c> and <c>Shift+Q</c>
/// or the wheel and <c>Alt</c>+wheel are different actions. A chord nothing is bound to falls back to its
/// bare key, so a click while sprinting (Shift) or crawling (Ctrl) is still a click. While the player runs, the
/// Sprint key is not a modifier at all, so the wheel steps the hotbar as in Scrap Mechanic. HUD hints use
/// <see cref="Label"/> so they show the player's keys.
/// </summary>
public static class GameInput
{
    private static Keymap _keymap = new();

    /// <summary>The bindings in use.</summary>
    public static Keymap Keymap => _keymap;

    /// <summary>Uses <paramref name="keymap"/> and rebuilds every input action from it.</summary>
    public static void Apply(Keymap keymap)
    {
        _keymap = keymap;
        foreach (var def in InputActions.All)
        {
            if (InputMap.HasAction(def.Id))
                InputMap.ActionEraseEvents(def.Id);
            else
                InputMap.AddAction(def.Id);
            foreach (var chord in keymap.Bindings(def.Id))
            {
                if (ToEvent(chord) is { } e)
                    InputMap.ActionAddEvent(def.Id, e);
            }
        }
    }

    /// <summary>Registers the actions with the current bindings if nothing has yet.</summary>
    public static void EnsureRegistered()
    {
        if (!InputMap.HasAction(InputActions.MoveForward))
            Apply(_keymap);
    }

    /// <summary>The key the player reads for <paramref name="action"/>, e.g. <c>Q</c> or <c>Left Click</c>.</summary>
    public static string Label(string action) => _keymap.Label(action);

    /// <summary>
    /// True for a fresh press (no echo) of one of <paramref name="action"/>'s bindings. A press with
    /// modifiers held that no action is bound to counts as the bare key, so held Sprint or Crawl doesn't
    /// swallow a click or a key. While <see cref="Running"/>, the Sprint key is ignored as a modifier, so Shift+wheel is
    /// the wheel (the hotbar), not the turn key.
    /// </summary>
    public static bool Pressed(InputEvent @event, string action)
    {
        if (ToChord(@event, pressedOnly: true) is not { } chord)
            return false;
        if (Running())
        {
            foreach (var sprint in _keymap.Bindings(InputActions.Sprint))
                chord = chord.Without(sprint);
        }
        if (Matches(action, chord))
            return true;
        var bare = new InputChord(chord.Code);
        return bare != chord && Matches(action, bare) && !InputActions.All.Any(def => Matches(def.Id, chord));
    }

    /// <summary>True while Sprint and a movement key are held: the player is running, not holding Shift to turn a part.</summary>
    public static bool Running() =>
        InputMap.HasAction(InputActions.Sprint) && Input.IsActionPressed(InputActions.Sprint)
        && Input.GetVector(InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveBack, InputActions.MoveForward) != Vector2.Zero;

    private static bool Matches(string action, InputChord chord)
    {
        foreach (var bound in _keymap.Bindings(action))
        {
            if (bound == chord || (bound.IsBareModifier && bound.Code == chord.Code))
                return true;
        }
        return false;
    }

    /// <summary>True while a text box in <paramref name="viewport"/> has focus, so letter keys are typing, not actions.</summary>
    public static bool Typing(Viewport viewport) => viewport.GuiGetFocusOwner() is LineEdit or TextEdit;

    /// <summary>True for a release of one of <paramref name="action"/>'s bindings (modifiers ignored).</summary>
    public static bool Released(InputEvent @event, string action)
    {
        var code = @event switch
        {
            InputEventKey { Pressed: false } k => KeyCode(k),
            InputEventMouseButton { Pressed: false } m => MouseCode(m.ButtonIndex),
            _ => null,
        };
        return code is not null && _keymap.Bindings(action).Any(b => b.Code == code);
    }

    /// <summary>The Godot event for a chord, or null for a name Godot doesn't know.</summary>
    public static InputEvent? ToEvent(InputChord chord)
    {
        InputEventWithModifiers? e = chord.Code switch
        {
            "MouseLeft" => new InputEventMouseButton { ButtonIndex = MouseButton.Left },
            "MouseRight" => new InputEventMouseButton { ButtonIndex = MouseButton.Right },
            "MouseMiddle" => new InputEventMouseButton { ButtonIndex = MouseButton.Middle },
            "MouseX1" => new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton1 },
            "MouseX2" => new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton2 },
            "WheelUp" => new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp },
            "WheelDown" => new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown },
            _ => Enum.TryParse<Key>(chord.Code, out var key) ? new InputEventKey { PhysicalKeycode = key } : null,
        };
        if (e is null)
            return null;
        (e.ShiftPressed, e.CtrlPressed, e.AltPressed) = (chord.Shift, chord.Ctrl, chord.Alt);
        return e;
    }

    /// <summary>
    /// The chord an event is, for matching and for "Press any key…" rebinding; null for anything else
    /// (motion, a joystick, or a release when <paramref name="pressedOnly"/>).
    /// </summary>
    public static InputChord? ToChord(InputEvent @event, bool pressedOnly = true)
    {
        switch (@event)
        {
            case InputEventKey k when (!pressedOnly || (k.Pressed && !k.Echo)) && KeyCode(k) is { } code:
                // A modifier key on its own is that key, whatever flag Godot sets for it.
                return code is "Shift" or "Ctrl" or "Alt"
                    ? new InputChord(code)
                    : new InputChord(code, k.ShiftPressed, k.CtrlPressed, k.AltPressed);
            case InputEventMouseButton m when (!pressedOnly || m.Pressed) && MouseCode(m.ButtonIndex) is { } mouse:
                return new InputChord(mouse, m.ShiftPressed, m.CtrlPressed, m.AltPressed);
            default:
                return null;
        }
    }

    private static string? KeyCode(InputEventKey k)
    {
        var key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
        return key == Key.None ? null : key.ToString();
    }

    private static string? MouseCode(MouseButton button) => button switch
    {
        MouseButton.Left => "MouseLeft",
        MouseButton.Right => "MouseRight",
        MouseButton.Middle => "MouseMiddle",
        MouseButton.Xbutton1 => "MouseX1",
        MouseButton.Xbutton2 => "MouseX2",
        MouseButton.WheelUp => "WheelUp",
        MouseButton.WheelDown => "WheelDown",
        _ => null,
    };
}
