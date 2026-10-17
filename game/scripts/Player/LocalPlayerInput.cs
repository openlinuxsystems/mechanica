using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Player;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Player;

/// <summary>
/// Turns this machine's keyboard and mouse into <see cref="PlayerInput"/> for one player.
/// It writes into the controller; on a client, <see cref="Net.PlayerSync"/> also sends each tick's input to the host.
/// Crouch slows the player (and dives in deep water). Click captures the mouse and Esc releases it. The camera starts
/// third-person (UI spec §6); Zoom in (X) and Zoom out (C) step it closer or further, and all the way in is first-person.
/// Look speed, inverted look and field of view come from the player's settings. While the mouse is free
/// (a menu, the inventory or a panel is open) the player stands still and a seated player stops driving.
/// </summary>
public partial class LocalPlayerInput : Node
{
    /// <summary>Radians of look per pixel of mouse motion.</summary>
    public float MouseSensitivity => GameSettings.Current.MouseSensitivity;

    /// <summary>The player this input drives.</summary>
    public PlayerController Target { get; set; } = null!;

    private float _yaw;
    private float _pitch;

    public override void _Ready()
    {
        GameInput.EnsureRegistered();
        _yaw = Target.Rotation.Y;
        Target.Camera.Fov = GameSettings.Current.FieldOfView;
        GameSettings.Changed += OnSettingsChanged;
        Target.MakeCameraCurrent();
        Target.CameraDistance = Target.Settings.CameraDistance;
        // Run before the controller so it simulates this frame's input.
        ProcessPhysicsPriority = Target.ProcessPhysicsPriority - 1;
    }

    public override void _ExitTree() => GameSettings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged() => Target.Camera.Fov = GameSettings.Current.FieldOfView;

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                var maxPitch = Mathf.DegToRad(Target.Settings.MaxPitchDegrees);
                _yaw = Mathf.Wrap(_yaw - motion.Relative.X * MouseSensitivity, -Mathf.Pi, Mathf.Pi);
                _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity * (GameSettings.Current.InvertY ? -1 : 1), -maxPitch, maxPitch);
                break;
            case InputEventMouseButton { Pressed: true } when Input.MouseMode != Input.MouseModeEnum.Captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
            default:
                if (GameInput.Pressed(@event, InputActions.Pause))
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                else if (GameInput.Pressed(@event, InputActions.ZoomIn) && Input.MouseMode == Input.MouseModeEnum.Captured)
                    Zoom(true);
                else if (GameInput.Pressed(@event, InputActions.ZoomOut) && Input.MouseMode == Input.MouseModeEnum.Captured)
                    Zoom(false);
                else
                    return;
                break;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>Turns the view by <paramref name="radians"/> of yaw (a seat turning under the player carries the view round).</summary>
    public void Turn(float radians) => _yaw = Mathf.Wrap(_yaw + radians, -Mathf.Pi, Mathf.Pi);

    /// <summary>Moves the camera one zoom step in or out.</summary>
    public void Zoom(bool zoomIn) => Target.CameraDistance = CameraZoom.Step(Target.CameraDistance, zoomIn, Target.Settings);

    public override void _PhysicsProcess(double delta)
    {
        // A menu or panel has the mouse: keys belong to it, so the player doesn't walk or drive.
        var playing = Input.MouseMode == Input.MouseModeEnum.Captured;
        var move = playing
            ? Input.GetVector(InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveBack, InputActions.MoveForward)
            : Vector2.Zero;
        Target.CurrentInput = new PlayerInput(
            move.X,
            move.Y,
            _yaw,
            _pitch,
            playing && Input.IsActionPressed(InputActions.Jump),
            playing && Input.IsActionPressed(InputActions.Sprint),
            playing && Input.IsActionPressed(InputActions.Crawl));
    }
}
