using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Player;
using Mechanica.Game.Player;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless player controller test: drives a <see cref="PlayerController"/> with scripted
/// <see cref="PlayerInput"/> on a flat floor and checks walk, turn, jump, look and push.
/// Also checks the main test world spawns a grounded local player. Exits 0 on success, 1 on failure.
/// </summary>
public partial class PlayerTest : Node3D
{
    private const int StageTimeoutFrames = 600;

    private readonly Queue<Stage> _stages = new();
    private Stage? _stage;
    private int _stageFrames;
    private bool _done;

    private Main _main = null!;
    private PlayerController _player = null!;
    private StaticBody3D _ceiling = null!;
    private float _crouchedTop;
    private RigidBody3D _crate = null!;
    private Vector3 _mark;
    private float _peak;
    private bool _leftFloor;

    private sealed record Stage(string Name, Func<int, bool> Tick);

    public override void _Ready()
    {
        GameInput.EnsureRegistered();
        Check(InputMap.HasAction(InputActions.MoveForward) && InputMap.HasAction(InputActions.Jump),
            "player input actions are registered");

        _main = ResourceLoader.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        AddChild(_main);
        Check(_main.LocalPlayer is not null, "main scene spawns a local player");

        Add("main scene player lands on the ground", frame =>
        {
            if (frame < 30 || !_main.LocalPlayer!.IsOnFloor())
                return false;
            var at = _main.LocalPlayer.GlobalPosition;
            var ground = _main.Streamer.Generator.HeightAt(at.X, at.Z);
            Check(Mathf.Abs(at.Y - ground) < 0.05f, $"local player stands at ground level (y={at.Y:F3}, ground {ground:F3})");
            _main.QueueFree();
            BuildArena();
            return true;
        });
        Add("player settles on the floor", frame => frame > 10 && _player.IsOnFloor() && Mark());
        Add("walk forward one second", frame =>
        {
            _player.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            if (frame < 60)
                return false;
            var moved = _player.GlobalPosition - _mark;
            Check(moved.Z is < -3f and > -4.1f && Mathf.Abs(moved.X) < 0.05f,
                $"walking forward at yaw 0 moves along -Z (moved {moved})");
            return Mark();
        });
        Add("turn left and walk", frame =>
        {
            _player.CurrentInput = new PlayerInput(0, 1, Mathf.Pi / 2, 0, false, false);
            if (frame < 60)
                return false;
            var moved = _player.GlobalPosition - _mark;
            Check(moved.X < -2.5f, $"walking at yaw 90° moves along -X (moved {moved})");
            Check(Mathf.IsEqualApprox(_player.Rotation.Y, Mathf.Pi / 2, 1e-3f), "body yaw follows input");
            return true;
        });
        Add("stop", frame =>
        {
            _player.CurrentInput = PlayerInput.None with { Yaw = Mathf.Pi / 2 };
            if (frame < 30)
                return false;
            Check(_player.Velocity.Length() < 0.01f, $"player stops without input (v={_player.Velocity})");
            return Mark();
        });
        Add("jump", frame =>
        {
            _player.CurrentInput = PlayerInput.None with { Jump = frame == 0 };
            _peak = Mathf.Max(_peak, _player.GlobalPosition.Y - _mark.Y);
            _leftFloor |= !_player.IsOnFloor();
            if (!_leftFloor || !_player.IsOnFloor())
                return false;
            Check(_peak is > 0.9f and < 1.2f, $"jump peaks about 1 m up (peak={_peak:F3})");
            return true;
        });
        Add("look", frame =>
        {
            _player.CurrentInput = PlayerInput.None with { Pitch = 10f };
            if (frame < 1)
                return false;
            var pitch = _player.Head.Rotation.X;
            Check(Mathf.IsEqualApprox(pitch, Mathf.DegToRad(_player.Settings.MaxPitchDegrees), 1e-3f),
                $"look pitch is clamped (pitch={Mathf.RadToDeg(pitch):F1}°)");
            Check(_player.Camera.GlobalPosition.Y > _player.GlobalPosition.Y + 1.5f, "camera sits at eye height");
            return true;
        });
        Add("push a crate", frame =>
        {
            if (frame == 0)
            {
                _crate.GlobalPosition = _player.GlobalPosition + new Vector3(0, 0.25f, -1.5f);
                _crate.LinearVelocity = Vector3.Zero;
                _mark = _crate.GlobalPosition;
            }
            _player.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            if (frame < 90)
                return false;
            var moved = _crate.GlobalPosition - _mark;
            Check(moved.Z < -0.5f, $"walking into a crate pushes it (moved {moved})");
            return true;
        });
        Add("crouch shrinks the capsule and lowers the camera", frame =>
        {
            if (frame == 0)
            {
                _player.GlobalPosition = new Vector3(-20, 0.1f, -20);
                _player.Velocity = Vector3.Zero;
            }
            _player.CurrentInput = new PlayerInput(0, 0, 0, 0, false, false, Crawl: true);
            if (frame < 30)
                return false;
            var settings = _player.Settings;
            Check(_player.Crouched && Mathf.IsEqualApprox(_player.CapsuleHeight, settings.CrouchHeight), $"the capsule is {settings.CrouchHeight} m ({_player.CapsuleHeight})");
            Check(Mathf.IsEqualApprox(_player.Head.Position.Y, settings.CrouchEyeHeight), $"the camera is at {settings.CrouchEyeHeight} m ({_player.Head.Position.Y:F2})");
            Check(_player.Body.HasFlag(Core.Net.BodyFlags.Crawl), "the body shows crouching");
            return true;
        });
        Add("under a low ceiling the player stays down", frame =>
        {
            if (frame == 0)
            {
                // 1.3 m clear: room to crouch, not to stand.
                _ceiling = new StaticBody3D { Name = "Ceiling", Position = new Vector3(-20, 1.4f, -20) };
                _ceiling.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2, 0.2f, 2) } });
                AddChild(_ceiling);
            }
            _player.CurrentInput = PlayerInput.None;
            if (frame < 30)
                return false;
            Check(_player.Crouched && Mathf.IsEqualApprox(_player.CapsuleHeight, _player.Settings.CrouchHeight), "letting go of crouch under the ceiling keeps the player crouched");
            Check(_player.GlobalPosition.Y < 0.1f && _player.IsOnFloor(), $"the player is not pushed through the floor or ceiling (y={_player.GlobalPosition.Y:F2})");
            return true;
        });
        Add("out from under it the player stands", frame =>
        {
            _player.CurrentInput = new PlayerInput(0, 1, 0, 0, false, true);
            if (_player.Crouched)
            {
                var speed = new Vector2(_player.Velocity.X, _player.Velocity.Z).Length();
                _crouchedTop = Mathf.Max(_crouchedTop, speed);
                return false;
            }
            Check(_crouchedTop > 1f && _crouchedTop <= _player.Settings.CrawlSpeed + 0.01f, $"kept down, the player moved at crouching speed ({_crouchedTop:F2} m/s)");
            Check(_player.GlobalPosition.Z < -21f + _player.Settings.Radius, $"the player stood up only past the ceiling's edge (z={_player.GlobalPosition.Z:F2})");
            Check(Mathf.IsEqualApprox(_player.CapsuleHeight, _player.Settings.Height), "the capsule is full height again");
            _player.CurrentInput = PlayerInput.None;
            return true;
        });
        AddStairs("walk up one-block stairs", 30f, sprint: false);
        AddStairs("run up one-block stairs", 34f, sprint: true);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done)
            return;
        if (_stage is null)
        {
            if (!_stages.TryDequeue(out _stage))
            {
                Finish(0);
                return;
            }
            _stageFrames = 0;
        }

        if (_stage.Tick(_stageFrames))
        {
            GD.Print($"[player] done: {_stage.Name} ({_stageFrames} frames)");
            _stage = null;
        }
        else if (++_stageFrames > StageTimeoutFrames)
        {
            Check(false, $"stage '{_stage.Name}' timed out");
        }
    }

    private void BuildArena()
    {
        var floor = new StaticBody3D { Name = "Floor", Position = new Vector3(0, -0.5f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(100, 1, 100) } });
        AddChild(floor);

        _crate = new RigidBody3D { Name = "Crate", Mass = 20, Position = new Vector3(20, 0.25f, 20) };
        _crate.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * 0.5f } });
        AddChild(_crate);

        _player = new PlayerController { Name = "Player" };
        AddChild(_player);
        _player.GlobalPosition = new Vector3(0, 0.5f, 0);
    }

    // Two one-block (0.25 m) steps and then a two-block wall along -Z at x: the player walks up both without
    // jumping and stops at the wall.
    private void AddStairs(string name, float x, bool sprint)
    {
        Add(name, frame =>
        {
            if (frame == 0)
            {
                AddBox(new Vector3(x, 0.125f, -3f), new Vector3(2f, 0.25f, 2f));
                AddBox(new Vector3(x, 0.25f, -5f), new Vector3(2f, 0.5f, 2f));
                AddBox(new Vector3(x, 0.5f, -7f), new Vector3(2f, 1f, 2f));
                _player.GlobalPosition = new Vector3(x, 0.05f, 0f);
                _player.Velocity = Vector3.Zero;
                _leftFloor = false;
            }
            _player.CurrentInput = new PlayerInput(0, frame < 10 ? 0 : 1, 0, 0, false, sprint);
            if (frame > 10)
                _leftFloor |= _player.Velocity.Y > _player.Settings.JumpSpeed * 0.9f;
            if (frame < 150)
                return false;
            var at = _player.GlobalPosition;
            Check(at.Y is > 0.45f and < 0.6f && _player.IsOnFloor(), $"the player walked up onto the second step (y={at.Y:F3})");
            Check(at.Z is > -6f and < -5.5f, $"the two-block wall stops the player (z={at.Z:F3})");
            Check(!_leftFloor, "the player never jumped");
            Check(_player.Head.Position.Y <= _player.Settings.EyeHeight + 1e-3f && _player.Head.Position.Y > _player.Settings.EyeHeight - 0.01f,
                $"the camera has caught up with the body ({_player.Head.Position.Y:F3})");
            _player.CurrentInput = PlayerInput.None;
            return true;
        });
    }

    private void AddBox(Vector3 centre, Vector3 size)
    {
        var box = new StaticBody3D { Position = centre };
        box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(box);
    }

    private bool Mark()
    {
        _mark = _player.GlobalPosition;
        return true;
    }

    private void Add(string name, Func<int, bool> tick) => _stages.Enqueue(new Stage(name, tick));

    private void Check(bool ok, string what)
    {
        GD.Print($"[player] {(ok ? "PASS" : "FAIL")}: {what}");
        if (!ok)
            Finish(1);
    }

    private void Finish(int code)
    {
        if (_done)
            return;
        _done = true;
        GD.Print($"[player] {(code == 0 ? "OK" : "FAILED")}");
        StagedTest.QuitCleanly(this, code);
    }
}
