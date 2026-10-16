using Godot;
using Mechanica.Core.Player;

namespace Mechanica.Game.Player;

/// <summary>
/// Turns this machine's keyboard and mouse into <see cref="PlayerInput"/> for one player.
/// Today it writes straight into the controller; in chunk 2b the same struct is sent to the host.
/// Click captures the mouse, Esc releases it, V toggles the third-person camera.
/// </summary>
public partial class LocalPlayerInput : Node
{
    /// <summary>Radians of look per pixel of mouse motion.</summary>
    [Export] public float MouseSensitivity { get; set; } = 0.0025f;

    /// <summary>The player this input drives.</summary>
    public PlayerController Target { get; set; } = null!;

    private float _yaw;
    private float _pitch;

    public override void _Ready()
    {
        PlayerInputActions.EnsureRegistered();
        _yaw = Target.Rotation.Y;
        Target.MakeCameraCurrent();
        // Run before the controller so it simulates this frame's input.
        ProcessPhysicsPriority = Target.ProcessPhysicsPriority - 1;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                var maxPitch = Mathf.DegToRad(Target.Settings.MaxPitchDegrees);
                _yaw = Mathf.Wrap(_yaw - motion.Relative.X * MouseSensitivity, -Mathf.Pi, Mathf.Pi);
                _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -maxPitch, maxPitch);
                break;
            case InputEventMouseButton { Pressed: true } when Input.MouseMode != Input.MouseModeEnum.Captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
            default:
                if (@event.IsActionPressed(PlayerInputActions.ReleaseMouse))
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                else if (@event.IsActionPressed(PlayerInputActions.ToggleCamera))
                    Target.ThirdPerson = !Target.ThirdPerson;
                else
                    return;
                break;
        }

        GetViewport().SetInputAsHandled();
    }

    public override void _PhysicsProcess(double delta)
    {
        var move = Input.GetVector(
            PlayerInputActions.MoveLeft, PlayerInputActions.MoveRight,
            PlayerInputActions.MoveBack, PlayerInputActions.MoveForward);
        Target.CurrentInput = new PlayerInput(
            move.X,
            move.Y,
            _yaw,
            _pitch,
            Input.IsActionPressed(PlayerInputActions.Jump),
            Input.IsActionPressed(PlayerInputActions.Sprint));
    }
}
