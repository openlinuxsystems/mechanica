using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Cars drive up the edge of a building's floor (<see cref="Creations.StepRamps"/>), host plus a client: a one-block
/// (0.25 m) concrete slab with a bevelled top edge, collided as a trimesh the way structures are, on flat ground. The
/// client drives the debug scrap car (rear-wheel drive, free front wheels) at it from 6 m out, creeping up to it in
/// short pushes, and from standing with its front wheels 1.5 m, 0.6 m and 0.45 m from the edge and against it. Every
/// time both axles roll up onto the slab without being thrown or tipped, and the car stops level on top. Close in, the
/// wheels' ramps are steeper, or sunk under them and raised while the engine drives them at the edge.
/// </summary>
public partial class EdgeDriveTest : StagedTest
{
    private const float Height = 0.25f, Bevel = 0.06f, FrontWheels = 1.21f, WheelRadius = 0.36f;

    // Each run: where the front wheels' centres start from the edge (at most against it), and whether W is pressed in
    // short pushes rather than held.
    private static readonly (string Name, float From, bool Creep)[] Runs =
    [
        ("from 6 m", 6f, false),
        ("creeping from 3 m", 3f, true),
        ("from 1.5 m", 1.5f, false),
        ("from 0.6 m", 0.6f, false),
        ("from 0.45 m", 0.45f, false),
        ("against the edge", 0f, false),
    ];

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _car;
    private float _ground;
    private float _edge;
    private float _x;
    private float _rest;
    private float _kick;
    private float _tilt;

    protected override string Tag => "edgedrive";

    protected override int StageTimeoutFrames => 2700;

    private Core.Creations.Creation HostCar => _host.Main.Creations.Find(_car)!;

    private Creations.CreationBody Chassis => _host.Main.Creations.NodeOf(_car)!.Bodies[0];

    private Vector3 Middle => Chassis.GlobalTransform * Chassis.CenterOfMass;

    // How far the car's middle is out from the slab's edge (it drives in along -Z).
    private float Ahead => Middle.Z - _edge;

    private int Part(string id) => HostCar.Parts.First(p => p.Part == id).Id;

    // A slab with its front face at z = 0 facing +Z and its top edge bevelled, extruded along X: the faces a structure's
    // trimesh collider has.
    private static StaticBody3D Slab(Vector3 at, float width, float depth)
    {
        Vector2[] profile = [new(0f, 0f), new(0f, Height - Bevel), new(-Bevel, Height), new(-depth, Height), new(-depth, 0f)];
        var faces = new List<Vector3>();
        var (left, right) = (-width / 2f, width / 2f);
        for (var i = 0; i < profile.Length - 1; i++)
        {
            var (a, b) = (profile[i], profile[i + 1]);
            faces.AddRange([new(left, a.Y, a.X), new(right, a.Y, a.X), new(right, b.Y, b.X), new(left, a.Y, a.X), new(right, b.Y, b.X), new(left, b.Y, b.X)]);
        }
        for (var i = 1; i < profile.Length - 1; i++)
        {
            foreach (var x in new[] { left, right })
                faces.AddRange([new(x, profile[0].Y, profile[0].X), new(x, profile[i].Y, profile[i].X), new(x, profile[i + 1].Y, profile[i + 1].X)]);
        }
        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces([.. faces]);
        var slab = new StaticBody3D { Name = "Slab", Position = at };
        slab.AddChild(new CollisionShape3D { Shape = shape });
        return slab;
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
        Add("a slab is laid on open ground ahead of the car", frame =>
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
            (_x, _edge) = (middle.X, middle.Z - 8f);
            foreach (var instance in new[] { _host, _client })
                instance.Main.AddChild(Slab(new Vector3(_x, _ground, _edge), 8f, 60f));
            return true;
        });
        foreach (var run in Runs)
        {
            Add($"{run.Name}: the car stands short of the slab", frame =>
            {
                if (frame == 0)
                {
                    if (_client.Main.LocalPlayer!.Seated)
                        _client.Main.LocalPlayer.CurrentInput = default;
                    var middle = new Vector3(_x, _ground + 0.7f, _edge + MathF.Max(run.From, WheelRadius) + FrontWheels);
                    HostCar.SetAllBodyPoses(new Transform3D(Basis.Identity, middle - Chassis.CenterOfMass).ToPose());
                    _host.Main.Creations.Moved(_car);
                    return false;
                }
                if (frame < 120 || Chassis.LinearVelocity.Length() > 0.05f)
                    return false;
                _rest = Middle.Y;
                var front = _host.Main.Creations.NodeOf(_car)!.Bodies
                    .Where(b => b.PartIds.Any(id => HostCar.FindPart(id)!.Part == "vanilla:scrap_wheel"))
                    .Min(b => (b.GlobalTransform * b.CenterOfMass).Z) - _edge;
                // Dropped close to the slab it settles about 5 cm further out (0.66 m from 0.6 on every run), so allow 10.
                Check(MathF.Abs(front - MathF.Max(run.From, WheelRadius)) < 0.1f, $"its front wheels are {front:0.00} m from the edge");
                return true;
            });
            if (run == Runs[0])
            {
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
            }
            Add($"{run.Name}: W drives the car up onto the slab", frame =>
            {
                // Creeping: half a second of W, then half a second off (the car stops between pushes).
                var pressed = !run.Creep || frame % 60 < 30;
                _client.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, pressed ? 1 : 0, 0, 0, false, false);
                if (frame == 0)
                    (_kick, _tilt) = (0f, 0f);
                _kick = MathF.Max(_kick, Chassis.LinearVelocity.Y);
                _tilt = MathF.Max(_tilt, Mathf.RadToDeg(Chassis.GlobalBasis.Y.AngleTo(Vector3.Up)));
                var ahead = Ahead;
                if (frame < 2400 && ahead > -3f)
                    return false;
                _client.Main.LocalPlayer.CurrentInput = default;
                GD.Print($"[edgedrive] {run.Name}: {frame} frames, kick {_kick:0.00} m/s, tilt {_tilt:0.0}°");
                Check(ahead <= -3f, $"both axles drive up onto the slab ({-ahead:0.00} m past its edge)");
                Check(_kick < 1.6f, $"the edge bumps the car up at most {_kick:0.00} m/s");
                Check(_tilt < 25f, $"the car stays upright (pitched at most {_tilt:0.0}°)");
                return true;
            });
            Add($"{run.Name}: the car stops level on the slab", frame =>
            {
                if (frame < 60 || Chassis.LinearVelocity.Length() > 0.05f)
                    return false;
                var rise = Middle.Y - _rest;
                Check(MathF.Abs(rise - Height) < 0.04f, $"the car sits a block higher ({rise:0.000} m)");
                Check(Chassis.GlobalBasis.Y.Dot(Vector3.Up) > 0.998f, "the car sits level");
                Check(!_host.Main.Creations.NodeOf(_car)!.Ramps.Laid.Any(), "its wheels have taken their ramps away");
                return true;
            });
        }
    }
}
