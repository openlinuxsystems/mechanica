using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// A parked car stays put. Host plus a client: the client drives the debug scrap car (Scrap Gas Engine on the rear
/// wheels, free front wheels) on open ground and gets up while it rolls. With nobody in the seat its engine holds the
/// wheels with its brake, as it does with no throttle, so the car stops in a few metres instead of coasting away, and
/// then stays put on flat ground and on a gentle slope (gravity tilted). On a steep slope it creeps down slowly,
/// sliding on its brake, far slower than a car rolling freely.
/// </summary>
public partial class ParkingTest : StagedTest
{
    private const float Gentle = 12f, Steep = 25f, Seconds = 4f;

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _car;
    private Vector3 _start;

    protected override string Tag => "parking";

    protected override int StageTimeoutFrames => 900;

    private Core.Creations.Creation HostCar => _host.Main.Creations.Find(_car)!;

    private Creations.CreationBody Chassis => _host.Main.Creations.NodeOf(_car)!.Bodies[0];

    private int Part(string id) => HostCar.Parts.First(p => p.Part == id).Id;

    private int Frames => (int)(Seconds * Engine.PhysicsTicksPerSecond);

    // Tilts gravity toward the car's back (+Z) or front (-Z) by `degrees`: the ground becomes a slope falling that way.
    private void Tilt(float degrees, float toward)
    {
        var slope = Mathf.DegToRad(degrees);
        PhysicsServer3D.AreaSetParam(Chassis.GetWorld3D().Space, PhysicsServer3D.AreaParameter.GravityVector,
            new Vector3(0f, -Mathf.Cos(slope), toward * Mathf.Sin(slope)));
        Chassis.Sleeping = false;
    }

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat, Setup = main => main.DebugCar = true };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the host's new world has the car and the client sees it", _ =>
        {
            if (_client.Main.LocalPlayer is not { } player || !player.IsOnFloor() || _host.Main.DebugCarId is not { } id
                || _client.Main.Creations.Find(id) is not { } seen || seen.Parts.Count != _host.Main.Creations.Find(id)!.Parts.Count)
                return false;
            _car = id;
            return true;
        });
        Add("the car is moved onto open ground", frame =>
        {
            // East of the spawn, clear of the Main scene's test steps in front of it.
            if (frame == 0)
            {
                var pose = Chassis.GlobalTransform with { Origin = Chassis.GlobalPosition + new Vector3(20f, 0.05f, 0f) };
                HostCar.SetAllBodyPoses(pose.ToPose());
                _host.Main.Creations.Moved(_car);
                return false;
            }
            return frame >= 90;
        });
        Add("the client sits in the driver's seat", frame =>
        {
            if (frame == 0)
            {
                _host.Main.PlayerSync.HostTeleport(_client.Peer, Chassis.GlobalTransform * new Vector3(-1.2f, 0f, 1.5f), 0f);
                return false;
            }
            if (frame == 20)
                _client.Main.Actions.Request(LogicActions.EnterSeat, new LogicPartArgs(_car, Part("vanilla:scrap_drivers_seat")), _results.Add);
            return _results.Count > 0 && _client.Main.LocalPlayer!.Seated && Check(_results[0].Ok, $"sitting down succeeds ({_results[0]})");
        });
        Add("W gets the car rolling", frame =>
        {
            _client.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            if (frame < 600 && -Chassis.LinearVelocity.Z < 3f)
                return false;
            Check(-Chassis.LinearVelocity.Z >= 3f, $"the car drives forward ({-Chassis.LinearVelocity.Z:0.00} m/s)");
            _client.Main.LocalPlayer.CurrentInput = new PlayerInput(0, 0, 0, 0, false, false);
            _results.Clear();
            _client.Main.Actions.Request(LogicActions.LeaveSeat, new NoArgs(), _results.Add);
            _start = Chassis.GlobalPosition;
            return true;
        });
        Add("the driver gets up and the car stops in a few metres", frame =>
        {
            if (frame < 30 || Chassis.LinearVelocity.Length() > 0.05f)
                return frame > 600 && !Check(false, $"the car stops (still {Chassis.LinearVelocity.Length():0.00} m/s)");
            Check(_results.Count > 0 && _results[0].Ok && !_client.Main.LocalPlayer!.Seated, "the client gets up");
            var rolled = (Chassis.GlobalPosition - _start).Length();
            GD.Print($"[parking] rolled {rolled:0.00} m after the driver got up");
            Check(rolled < 4f, $"it stops within 4 m ({rolled:0.00} m; free wheels coasted on and on)");
            return true;
        });
        Add("parked on flat ground, the car stays put", frame =>
        {
            if (frame == 0)
                _start = Chassis.GlobalPosition;
            if (frame < Frames)
                return false;
            var moved = (Chassis.GlobalPosition - _start).Length();
            Check(moved < 0.05f, $"it stays put ({moved:0.000} m in {Seconds} s)");
            return true;
        });
        Add($"parked on a gentle {Gentle}° slope, facing down it, the car stays put", frame =>
        {
            if (frame == 0)
            {
                Tilt(Gentle, -1f);
                _start = Chassis.GlobalPosition;
            }
            if (frame < Frames)
                return false;
            var moved = (Chassis.GlobalPosition - _start).Length();
            Check(moved < 0.1f, $"it stays put ({moved:0.000} m in {Seconds} s)");
            return true;
        });
        Add($"parked on a gentle {Gentle}° slope, facing up it, the car stays put", frame =>
        {
            if (frame == 0)
            {
                Tilt(Gentle, 1f);
                _start = Chassis.GlobalPosition;
            }
            if (frame < Frames)
                return false;
            var moved = (Chassis.GlobalPosition - _start).Length();
            Check(moved < 0.1f, $"it stays put ({moved:0.000} m in {Seconds} s)");
            return true;
        });
        Add($"parked on a steep {Steep}° slope, the car creeps down it", frame =>
        {
            if (frame == 0)
            {
                Tilt(Steep, 1f);
                _start = Chassis.GlobalPosition;
            }
            if (frame < Frames)
                return false;
            Tilt(0f, 0f);
            var moved = Chassis.GlobalPosition - _start;
            GD.Print($"[parking] crept {moved.Z:0.00} m down the {Steep}° slope in {Seconds} s, {Chassis.LinearVelocity.Length():0.00} m/s at the end");
            Check(moved.Z > 0.3f, $"it creeps down the slope ({moved.Z:0.00} m in {Seconds} s)");
            // Rolling freely it would gain g·sin 25° ≈ 4.1 m/s each second: about 33 m in 4 s.
            Check(moved.Z < 12f, $"slowly, held back by its brake ({moved.Z:0.00} m in {Seconds} s)");
            return true;
        });
    }
}
