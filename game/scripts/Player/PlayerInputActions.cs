using Godot;

namespace Mechanica.Game.Player;

/// <summary>Input action names for on-foot play and their default key bindings.</summary>
public static class PlayerInputActions
{
    public const string MoveForward = "move_forward";
    public const string MoveBack = "move_back";
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string Jump = "jump";
    public const string Sprint = "sprint";
    public const string ToggleCamera = "toggle_camera";
    public const string ReleaseMouse = "release_mouse";

    private static readonly (string Action, Key Key)[] Defaults =
    [
        (MoveForward, Key.W),
        (MoveBack, Key.S),
        (MoveLeft, Key.A),
        (MoveRight, Key.D),
        (Jump, Key.Space),
        (Sprint, Key.Shift),
        (ToggleCamera, Key.V),
        (ReleaseMouse, Key.Escape),
    ];

    /// <summary>Adds any missing action with its default key. Existing bindings are left alone.</summary>
    public static void EnsureRegistered()
    {
        foreach (var (action, key) in Defaults)
        {
            if (InputMap.HasAction(action))
                continue;
            InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }
}
