using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Cars drive up a one-block step (<see cref="Creations.StepRamps"/>). Host plus a client: the client drives the debug scrap
/// car (rear-wheel drive, Scrap Wheels on rigid bearings, free front wheels) head on into a 0.25 m block step on flat
/// ground at about 3.5 m/s. Without the wheels' ramps it bounced back off the step and stalled there for good; now both
/// axles roll up it, keeping their pace, without being thrown or tipped, and the car stops level on top with every ramp
/// gone. No ramp is laid on the level away from the step. Parked with its front wheels against a second step it stays
/// put, and from standing a little way back it drives up that one too.
/// </summary>
public partial class CarStepTest : StagedTest
{
    private const float StepHeight = 0.25f, WheelRadius = 0.36f;

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _car;
    private float _ground;
    private float _stepFront;
    private float _kick;
    private float _slowest;
    private float _tilt;
    private float _startY;
    private float _approach;
    private Vector3 _start;

    protected override string Tag => "carstep";

    protected override int StageTimeoutFrames => 900;

    private Core.Creations.Creation HostCar => _host.Main.Creations.Find(_car)!;

    private Creations.CreationBody Chassis => _host.Main.Creations.NodeOf(_car)!.Bodies[0];

    private Vector3 Middle => Chassis.GlobalTransform * Chassis.CenterOfMass;

    private Creations.StepRamps Ramps => _host.Main.Creations.NodeOf(_car)!.Ramps;

    // The centres of the car's wheels.
    private IEnumerable<Vector3> Wheels => _host.Main.Creations.NodeOf(_car)!.Bodies
        .Where(b => b.PartIds.Any(id => HostCar.FindPart(id)!.Part == "vanilla:scrap_wheel"))
        .Select(b => b.GlobalTransform * b.CenterOfMass);

    private void MoveSecondStep(float front)
    {
        _stepFront = front;
        foreach (var instance in new[] { _host, _client })
            instance.Main.GetNode<StaticBody3D>("SecondStep").Position = new Vector3(Middle.X, _ground + StepHeight * 1.5f, front - 6f);
    }

    private int Part(string id) => HostCar.Parts.First(p => p.Part == id).Id;

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
        Add("the car is moved onto open ground with a step ahead", frame =>
        {
            if (frame == 0)
            {
                var pose = Chassis.GlobalTransform with { Origin = Chassis.GlobalPosition + new Vector3(20f, 0.05f, 0f) };
                HostCar.SetAllBodyPoses(pose.ToPose());
                _host.Main.Creations.Moved(_car);
                return false;
            }
            if (frame < 90)
                return false;
            var middle = Middle;
            var hit = Chassis.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(middle, middle + Vector3.Down * 5f, 1));
            Check(hit.Count > 0, "the car stands on the ground");
            _ground = hit["position"].AsVector3().Y;
            // The step's face is 5 m ahead of the front wheels (they are 1.1 m ahead of the middle).
            _stepFront = middle.Z - 6f;
            foreach (var instance in new[] { _host, _client })
            {
                // Deep enough that however far the car rolls on top, the second step still stands on this one.
                var step = new StaticBody3D { Name = "Step", Position = new Vector3(middle.X, _ground + StepHeight / 2f, _stepFront - 20f) };
                step.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6f, StepHeight, 40f) } });
                instance.Main.AddChild(step);
            }
            _startY = middle.Y;
            return true;
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
        Add("W drives the car up the step", frame =>
        {
            _client.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            var middle = Middle;
            var ahead = middle.Z - _stepFront;
            if (frame == 0)
                (_kick, _slowest, _tilt, _approach) = (0f, float.MaxValue, 0f, 0f);
            _kick = MathF.Max(_kick, Chassis.LinearVelocity.Y);
            _tilt = MathF.Max(_tilt, Mathf.RadToDeg(Chassis.GlobalBasis.Y.AngleTo(Vector3.Up)));
            // On the level, well short of the step, no wheel lays a ramp.
            if (ahead > 4f)
            {
                _approach = -Chassis.LinearVelocity.Z;
                if (Ramps.Laid.Any())
                    return !Check(false, $"no ramp is laid on flat ground ({ahead:0.00} m from the step)");
            }
            // From the front wheels reaching the step until the rear wheels are up.
            else if (ahead > -2.2f)
                _slowest = MathF.Min(_slowest, -Chassis.LinearVelocity.Z);
            if (frame < 600 && ahead > -3f)
                return false;
            _client.Main.LocalPlayer.CurrentInput = new PlayerInput(0, 0, 0, 0, false, false);
            GD.Print($"[carstep] reached the step at {_approach:0.00} m/s, slowest {_slowest:0.00} m/s, kick {_kick:0.00} m/s, tilt {_tilt:0.0}°");
            Check(ahead <= -3f, $"both axles drive up onto the step ({-ahead:0.00} m past its face)");
            Check(_slowest > _approach * 0.6f, $"the car keeps its pace up the step (slowest {_slowest:0.00} m/s from {_approach:0.00}; without ramps it bounced back and stalled)");
            // A one-block rise at 4 m/s is a bump, not a launch: the chassis pitches about 6° with its front up.
            Check(_kick < 1.6f, $"the step bumps the car up at most {_kick:0.00} m/s");
            Check(_tilt < 25f, $"the car stays upright (pitched at most {_tilt:0.0}°)");
            return true;
        });
        Add("the car stops level on top of the step", frame =>
        {
            if (frame < 60 || Chassis.LinearVelocity.Length() > 0.05f)
                return false;
            var rise = Middle.Y - _startY;
            Check(MathF.Abs(rise - StepHeight) < 0.04f, $"the car sits a step higher ({rise:0.000} m)");
            Check(Chassis.GlobalBasis.Y.Dot(Vector3.Up) > 0.998f, "the car sits level");
            Check(!Ramps.Laid.Any(), "its wheels have taken their ramps away");
            return true;
        });
        Add("a second step is put against its front wheels", frame =>
        {
            if (frame == 0)
            {
                var front = Wheels.OrderBy(w => w.Z).First();
                foreach (var instance in new[] { _host, _client })
                {
                    var step = new StaticBody3D { Name = "SecondStep" };
                    step.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6f, StepHeight, 12f) } });
                    instance.Main.AddChild(step);
                }
                MoveSecondStep(front.Z - WheelRadius - 0.003f);
                _start = Middle;
                return false;
            }
            return frame >= 120;
        });
        Add("parked against it, the car stays put", _ =>
        {
            var moved = Middle - _start;
            Check(moved.Length() < 0.03f, $"it does not creep up the step on its own (moved {moved})");
            return true;
        });
        Add("the second step is moved a little way ahead", frame =>
        {
            // A wheel already against a step meets its edge side on; one standing a little way back rolls up.
            if (frame == 0)
                MoveSecondStep(Wheels.OrderBy(w => w.Z).First().Z - 1.2f);
            _startY = Middle.Y;
            return frame >= 30;
        });
        Add("from standing, W drives the car up the second step", frame =>
        {
            _client.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            if (frame == 0)
                (_kick, _tilt) = (0f, 0f);
            _kick = MathF.Max(_kick, Chassis.LinearVelocity.Y);
            _tilt = MathF.Max(_tilt, Mathf.RadToDeg(Chassis.GlobalBasis.Y.AngleTo(Vector3.Up)));
            var ahead = Middle.Z - _stepFront;
            if (frame < 600 && ahead > -3f)
                return false;
            _client.Main.LocalPlayer.CurrentInput = new PlayerInput(0, 0, 0, 0, false, false);
            GD.Print($"[carstep] from standing: {frame} frames, kick {_kick:0.00} m/s, tilt {_tilt:0.0}°");
            Check(ahead <= -3f, $"both axles drive up onto the second step ({-ahead:0.00} m past its face)");
            Check(_kick < 1.6f, $"the step bumps the car up at most {_kick:0.00} m/s");
            Check(_tilt < 25f, $"the car stays upright (pitched at most {_tilt:0.0}°)");
            return true;
        });
        Add("the car stops level on top of the second step", frame =>
        {
            if (frame < 60 || Chassis.LinearVelocity.Length() > 0.05f)
                return false;
            var rise = Middle.Y - _startY;
            Check(MathF.Abs(rise - StepHeight) < 0.04f, $"the car sits another step higher ({rise:0.000} m)");
            Check(Chassis.GlobalBasis.Y.Dot(Vector3.Up) > 0.998f, "the car sits level");
            return true;
        });
    }
}
