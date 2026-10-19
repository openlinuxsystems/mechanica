using Godot;
using Mechanica.Core.Input;
using Mechanica.Game.Player;

namespace Mechanica.Game.Logic;

/// <summary>
/// The local player's camera while they sit in a seat (UI spec §8, §9, §10). It locks to the seat: the view starts
/// facing the seat's front and turns with the Creation, and the mouse still looks around. Ctrl (Seated camera)
/// switches between the interior view, from the seated body's own eyes with its head hidden, and an external chase camera behind and above the
/// seat that follows the view. Sitting down, standing up and switching glide over the seat transition
/// (<see cref="Core.Player.PlayerMovementSettings.SeatTransitionSeconds"/>), and the chase camera catches up smoothly.
/// Standing up gives the player's own camera back.
/// </summary>
public partial class SeatCamera : Node3D
{
    private Transform3D _from;
    private double _blend = 1;
    private float _seatYaw;
    private bool _seated;

    /// <summary>Seats. Set before adding to the tree.</summary>
    public LogicSync Logic { get; init; } = null!;

    /// <summary>Players, for the local one. Set before adding to the tree.</summary>
    public Net.PlayerSync Players { get; init; } = null!;

    /// <summary>Turns the local player's view by a yaw in radians, so it turns with the seat; null leaves the view alone.</summary>
    public Action<float>? Turn { get; init; }

    /// <summary>False in automated tests: Ctrl does nothing (tests call <see cref="Toggle"/>).</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>The seat camera.</summary>
    public Camera3D Camera { get; private set; } = null!;

    /// <summary>True for the external chase camera, false for the interior view.</summary>
    public bool Chase { get; private set; }

    /// <summary>True while the local player sits and this camera shows the view.</summary>
    public bool Active => _seated;

    public override void _Ready()
    {
        Name = "SeatCamera";
        // After the bodies have posed this frame, so the interior view sits at the eyes they are drawn with.
        ProcessPriority = 1;
        Camera = new Camera3D { Name = "Camera", Near = 0.05f, TopLevel = true };
        AddChild(Camera);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!ReadDeviceInput || !_seated || Input.MouseMode != Input.MouseModeEnum.Captured || !GameInput.Pressed(@event, InputActions.SeatCamera))
            return;
        Toggle();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Switches between the interior and the chase camera.</summary>
    public void Toggle()
    {
        Chase = !Chase;
        StartBlend(Camera.GlobalTransform);
    }

    public override void _Process(double delta)
    {
        var player = Players.LocalPlayer;
        var seat = player is null ? null : Logic.Seats.SeatOf(player.PeerId);
        var pose = seat is { } s ? Logic.SeatTransform(s.Creation, s.Part) : null;
        if (player is null || pose is not { } at)
        {
            if (_seated)
            {
                _seated = false;
                if (player is not null && Camera.Current)
                    player.Camera.MakeCurrent();
                if (player is not null)
                    player.HeadHidden = false;
            }
            return;
        }
        var settings = player.Settings;
        var forward = Flat(-at.Feet.Basis.Z);
        var yaw = forward == Vector3.Zero ? _seatYaw : Mathf.Atan2(-forward.X, -forward.Z);
        if (!_seated)
        {
            _seated = true;
            Camera.Fov = player.Camera.Fov;
            StartBlend(player.Camera.GlobalTransform);
            Camera.GlobalTransform = player.Camera.GlobalTransform;
            Camera.MakeCurrent();
            // Sitting down faces the seat's front.
            Turn?.Invoke(Mathf.Wrap(yaw - player.Rotation.Y, -Mathf.Pi, Mathf.Pi));
        }
        else
        {
            Turn?.Invoke(Mathf.Wrap(yaw - _seatYaw, -Mathf.Pi, Mathf.Pi));
        }
        _seatYaw = yaw;
        Camera.Fov = player.Camera.Fov;
        var look = player.Head.GlobalBasis.Orthonormalized();
        var up = at.Feet.Basis.Y.Normalized();
        Transform3D target;
        if (Chase)
        {
            var seatAt = at.Feet.Origin + Vector3.Up * 1f;
            var back = Flat(look.Z) is var b && b != Vector3.Zero ? b : -forward;
            var wanted = at.Feet.Origin + back * settings.ChaseDistance + Vector3.Up * settings.ChaseHeight;
            // The chase camera catches up rather than sticking to every bump.
            var current = _blend < 1 ? wanted : Camera.GlobalPosition.Lerp(wanted, 1f - Mathf.Exp(-settings.ChaseStiffness * (float)delta));
            target = new Transform3D(Basis.LookingAt(seatAt - current, Vector3.Up), current);
        }
        else
        {
            // From the seated body's own eyes, wherever this seat's cushion puts them.
            var eyes = player.Character is CharacterModel { Eyes: { } e } ? e : at.Feet.Origin + up * settings.SeatedEyeHeight;
            target = new Transform3D(look, eyes);
        }
        var inside = !Chase && Camera.Current;
        if (player.HeadHidden != inside)
            player.HeadHidden = inside;
        if (_blend < 1)
        {
            _blend = Math.Min(1, _blend + delta / settings.SeatTransitionSeconds);
            var t = (float)_blend;
            target = _from.InterpolateWith(target, t * t * (3f - 2f * t));
        }
        Camera.GlobalTransform = target;
    }

    private void StartBlend(Transform3D from)
    {
        _from = from;
        _blend = 0;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.X, 0, v.Z) is var f && f.LengthSquared() > 1e-4f ? f.Normalized() : Vector3.Zero;
}
