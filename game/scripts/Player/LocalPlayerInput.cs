using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Player;
using Mechanica.Game.Settings;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Player;

/// <summary>
/// Turns this machine's keyboard and mouse into <see cref="PlayerInput"/> for one player.
/// It writes into the controller; on a client, <see cref="Net.PlayerSync"/> also sends each tick's input to the host.
/// Crouch slows the player (and dives in deep water). Click captures the mouse (unless a window is open) and Esc releases it. The camera starts
/// third-person (UI spec §6); Zoom in (X) and Zoom out (C) step it closer or further, and all the way in is first-person.
/// Look speed, inverted look and field of view come from the player's settings. While the mouse is free
/// (a menu, the inventory or a panel is open) the player stands still and a seated player stops driving.
/// </summary>
public partial class LocalPlayerInput : Node
{
    // Read every physics tick: one StringName per action rather than one per read.
    private static readonly StringName MoveLeft = InputActions.MoveLeft;
    private static readonly StringName MoveRight = InputActions.MoveRight;
    private static readonly StringName MoveBack = InputActions.MoveBack;
    private static readonly StringName MoveForward = InputActions.MoveForward;
    private static readonly StringName Jump = InputActions.Jump;
    private static readonly StringName Sprint = InputActions.Sprint;
    private static readonly StringName Crawl = InputActions.Crawl;

    /// <summary>Radians of look per pixel of mouse motion.</summary>
    public float MouseSensitivity => GameSettings.Current.MouseSensitivity;

    /// <summary>True while the player's owner watches someone else (<see cref="Spectator"/>): the player stands still.</summary>
    public Func<bool> Suspended { get; set; } = () => false;

    /// <summary>Takes the mouse for the game when the player clicks the world (tests count the calls).</summary>
    public Action CaptureMouse { get; set; } = () => Input.MouseMode = Input.MouseModeEnum.Captured;

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
        Target.CameraDistance = Target.Settings.CameraStartsFirstPerson ? 0f : Target.Settings.CameraDistance;
        // Run before the controller so it simulates this frame's input.
        ProcessPhysicsPriority = Target.ProcessPhysicsPriority - 1;
    }

    public override void _ExitTree() => GameSettings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged() => Target.Camera.Fov = GameSettings.Current.FieldOfView * Target.AimZoom;

    public override void _Process(double delta)
    {
        // Aiming a weapon zooms in a little (UI spec §10), quickly but not in one jump.
        var want = GameSettings.Current.FieldOfView * Target.AimZoom;
        Target.Camera.Fov = Mathf.MoveToward(Target.Camera.Fov, want, (float)delta * AimZoomSpeed);
    }

    /// <summary>Degrees of field of view per second the camera moves while aiming starts or stops.</summary>
    public const float AimZoomSpeed = 180f;

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
                // A click on the screen around an open window leaves the mouse with the window: only closing it
                // gives the mouse back to the game.
                if (EscapeStack.AnyOpenIn(GetViewport()))
                    return;
                CaptureMouse();
                break;
            default:
                // Esc frees the mouse; the key goes on to the in-game menu, which opens or closes what is open.
                if (GameInput.Pressed(@event, InputActions.Pause))
                {
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    return;
                }
                if (GameInput.Pressed(@event, InputActions.ZoomIn) && Input.MouseMode == Input.MouseModeEnum.Captured)
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

    /// <summary>Looks level towards <paramref name="yaw"/> (radians, as the player turns): where the opening cinematic leaves the player.</summary>
    public void Face(float yaw) => (_yaw, _pitch) = (Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi), 0f);

    /// <summary>Moves the camera one zoom step in or out.</summary>
    public void Zoom(bool zoomIn) => Target.CameraDistance = CameraZoom.Step(Target.CameraDistance, zoomIn, Target.Settings);

    public override void _PhysicsProcess(double delta)
    {
        // A menu or panel has the mouse: keys belong to it, so the player doesn't walk or drive.
        var playing = Input.MouseMode == Input.MouseModeEnum.Captured && !Suspended();
        // Knocked off their feet, the player can look about but not move until they are up again.
        var down = Target.Character is CharacterModel { Animator.Grounded: true } || Target.Arms is CharacterModel { Animator.Grounded: true };
        var move = playing && !down
            ? Input.GetVector(MoveLeft, MoveRight, MoveBack, MoveForward)
            : Vector2.Zero;
        Target.CurrentInput = new PlayerInput(
            move.X,
            move.Y,
            _yaw,
            _pitch,
            playing && !down && Input.IsActionPressed(Jump),
            playing && Input.IsActionPressed(Sprint) && Target.CanSprint,
            playing && Input.IsActionPressed(Crawl));
    }
}
