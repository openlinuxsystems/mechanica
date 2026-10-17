using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;
using Mechanica.Core.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Joint primitives (chunk 9), host plus a client (D10), with the fixture parts in <c>tests/joints/mods</c>:
/// <list type="bullet">
/// <item>a free bearing turns the right way at a set speed, then servos to an angle and takes the short
/// way across ±π;</item>
/// <item>a limited hinge stops at its limit;</item>
/// <item>a piston moves to a length and holds it, keeps it when its Creation is rebuilt mid-stroke, and
/// drops back to its stop when freed;</item>
/// <item>a spring sags by m·g/k and comes to rest;</item>
/// <item>a car on four side-mounted suspensions settles level, drives, and stays together;</item>
/// <item>the client shows every joint where the host has it, and the joint debug overlay draws them all.</item>
/// </list>
/// </summary>
public partial class JointTest : StagedTest
{
    private const string Mod = "joint_test";
    private const int Turntable = 1, Hinge = 2, Piston = 3, Spring = 4, Car = 5;
    private static readonly ActionCaller Bot = new(Creation.HostPeer, ActionSource.Bot);

    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private readonly Dictionary<int, int> _jointPart = [];
    private readonly List<int> _carSprings = [];
    private readonly List<(int Part, float Sign)> _carWheels = [];
    private Vector3 _carStart;

    protected override string Tag => "joints";

    protected override int StageTimeoutFrames => 900;

    public override void _Ready()
    {
        _mods = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/joints/mods"));
        _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost(mods: _mods);
        _client = _harness.StartClient("Client", mods: _mods);

        Add("the client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Add("spawn the rigs on the host", _ =>
        {
            Check(!_mods.HasErrors, $"fixture mod loads ({string.Join("; ", _mods.Diagnostics)})");
            Rig(Turntable, "bearing", new Vector3(2, 0.2f, 3));
            Rig(Hinge, "hinge", new Vector3(4, 0.2f, 3));
            Rig(Piston, "piston", new Vector3(6, 0.2f, 3));
            Rig(Spring, "spring", new Vector3(8, 0.2f, 3));
            SpawnCar(new Vector3(5, 0.6f, 6));
            return true;
        });
        Wait("everything settles", 150);

        Add("a bearing turns forward at a set speed", frame =>
        {
            if (frame == 0)
                Act(MotionActions.SetBearingTarget, new SetBearingTargetArgs(Turntable, _jointPart[Turntable], 2f, 100f));
            if (frame < 30)
                return false;
            var angle = HostPosition(Turntable);
            Check(angle is > 0.5f and < 1.1f, $"after 0.5 s at +2 rad/s the bearing is at {angle:F3} rad (right-handed about its rotor)");
            return true;
        });
        Add("a bearing servos to an angle", frame => Servo(frame, Turntable, 1.5f));
        Add("a bearing takes the short way across ±π", frame => Servo(frame, Turntable, -2.8f));
        Add("the client shows the bearing's angle", frame => frame >= 30 && CheckClient(Turntable, 0.05f));

        Add("a limited hinge stops at its limit", frame =>
        {
            if (frame == 0)
                Act(MotionActions.SetBearingTarget, new SetBearingTargetArgs(Hinge, _jointPart[Hinge], 3f, 30f));
            if (frame < 120)
                return false;
            var angle = HostPosition(Hinge);
            Check(angle is > 0.9f and < 1.06f, $"the hinge stops at {angle:F3} rad (limit 1)");
            Check(Act(MotionActions.SetBearingTarget, new SetBearingTargetArgs(Hinge, _jointPart[Hinge], 1f, null, 1.5f)) is { Ok: false },
                "an angle past the limit is refused");
            return true;
        });

        Add("a piston extends to a length and holds it", frame =>
        {
            if (frame == 0)
                Act(MotionActions.SetPistonTarget, new SetPistonTargetArgs(Piston, _jointPart[Piston], 0.75f, 1f));
            if (frame < 120)
                return false;
            var length = HostPosition(Piston);
            Check(MathF.Abs(length - 0.75f) < 0.01f, $"the piston holds 0.75 m (at {length:F4})");
            Check(Act(MotionActions.SetPistonTarget, new SetPistonTargetArgs(Piston, _jointPart[Piston], 1.5f)) is { Ok: false },
                "a length past its travel is refused");
            return true;
        });
        Add("the client shows the piston's length", frame => frame >= 30 && CheckClient(Piston, 0.01f));
        Add("a rebuild mid-stroke keeps the piston where it is", frame =>
        {
            if (frame == 0)
            {
                var creation = _host.Main.Creations.Find(Piston)!;
                Check(creation.TryPlacePart($"{Mod}:block", new GridCell(1, 0, 0), default, out _, out var error), $"a block is added to the base ({error})");
                _host.Main.Creations.Rebuild(Piston);
            }
            if (frame < 60)
                return false;
            var length = HostPosition(Piston);
            Check(MathF.Abs(length - 0.75f) < 0.02f, $"after the rebuild the piston still holds 0.75 m (at {length:F4})");
            return true;
        });
        Add("a freed piston drops back to its stop", frame =>
        {
            if (frame == 0)
                Act(MotionActions.SetPistonTarget, new SetPistonTargetArgs(Piston, _jointPart[Piston], null));
            if (frame < 120)
                return false;
            var length = HostPosition(Piston);
            Check(length is > -0.02f and < 0.02f, $"the freed piston rests at {length:F4} m");
            return true;
        });

        Add("a spring sags by m·g/k and rests", frame =>
        {
            if (frame < 10)
                return false;
            var sag = HostPosition(Spring);
            var expected = -10f * 9.81f / 2000f;
            var top = _host.Main.Creations.NodeOf(Spring)!.Bodies[1];
            Check(MathF.Abs(sag - expected) < 0.008f, $"the spring holds its 10 kg block at {sag:F4} m (m·g/k = {expected:F4})");
            Check(top.LinearVelocity.Length() < 0.02f, $"the block on the spring is at rest ({top.LinearVelocity.Length():F4} m/s)");
            return CheckClient(Spring, 0.01f);
        });

        Add("the car settles level on its suspension", frame =>
        {
            if (frame < 30)
                return false;
            var node = _host.Main.Creations.NodeOf(Car)!;
            var chassis = node.Bodies[0];
            var sags = _carSprings.Select(p => Position(_host, Car, p)).ToList();
            Check(sags.All(s => s is > 0.005f and < 0.1f), $"every spring is pushed up by its wheel within its travel ({string.Join(", ", sags.Select(s => s.ToString("F3")))})");
            Check(sags.Max() - sags.Min() < 0.01f, "the springs share the load evenly");
            Check(chassis.GlobalBasis.Y.Dot(Vector3.Up) > 0.999f, $"the chassis is level ({chassis.GlobalBasis.Y})");
            Check(node.Bodies.All(b => b.LinearVelocity.Length() < 0.05f), "the car is at rest");
            _carStart = chassis.GlobalPosition;
            return true;
        });
        Add("the car drives on its suspension and stays together", frame =>
        {
            if (frame == 0)
            {
                foreach (var (part, sign) in _carWheels)
                    Act(MotionActions.SetBearingTarget, new SetBearingTargetArgs(Car, part, sign * 6f, 60f));
            }
            var node = _host.Main.Creations.NodeOf(Car)!;
            var chassis = node.Bodies[0];
            // Checked every frame, logged only when it goes wrong.
            if (chassis.GlobalBasis.Y.Dot(Vector3.Up) < 0.95f)
                return !Check(false, $"the car stays upright (tipped at frame {frame})");
            foreach (var spring in _carSprings)
            {
                var p = Position(_host, Car, spring);
                if (p is < -0.11f or > 0.11f)
                    return !Check(false, $"spring {spring} stays within its travel (at {p:F3} on frame {frame})");
            }
            if (frame < 180)
                return false;
            Check(true, "the car stayed upright with every spring within its travel");
            var moved = chassis.GlobalPosition - _carStart;
            Check(MathF.Abs(moved.Z) > 1.5f && MathF.Abs(moved.X) < 0.5f, $"the car drove straight ({moved})");
            foreach (var (part, _) in _carWheels)
                Act(MotionActions.SetBearingTarget, new SetBearingTargetArgs(Car, part, 0f, 60f));
            return true;
        });
        Add("the client shows the car where the host has it", frame =>
        {
            if (frame < 90)
                return false;
            var host = _host.Main.Creations.NodeOf(Car)!.Bodies[0].GlobalPosition;
            var client = _client.Main.Creations.NodeOf(Car)!.Bodies[0].GlobalPosition;
            Check(host.DistanceTo(client) < 0.05f, $"the client's car is {host.DistanceTo(client):F3} m from the host's");
            foreach (var spring in _carSprings)
                Check(MathF.Abs(Position(_host, Car, spring) - Position(_client, Car, spring)) < 0.01f, $"the client's spring {spring} matches the host's");
            return true;
        });
        Add("the joint overlay draws every joint on the host and the client", _ => CheckOverlay(_host) && CheckOverlay(_client));
    }

    private bool CheckOverlay(Instance instance)
    {
        instance.Main.JointOverlay.Redraw();
        var expected = instance.Main.Creations.All.Sum(c => c.Joints.Count);
        Check(expected == 12 && instance.Main.JointOverlay.JointsDrawn == expected,
            $"{instance}'s joint overlay draws all {expected} joints ({instance.Main.JointOverlay.JointsDrawn})");
        return true;
    }

    // A heavy base, the joint part on top of it and a block on the joint's head.
    private void Rig(int id, string joint, Vector3 at)
    {
        var creation = new Creation(id, _mods);
        Place(creation, "heavy_block", GridCell.Zero);
        _jointPart[id] = Place(creation, joint, new GridCell(0, 1, 0)).Id;
        Place(creation, "block", new GridCell(0, 2, 0));
        creation.SetAllBodyPoses(new Pose(at.ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Creations.Add(creation, _host.Main.Assets);
    }

    // A three-block chassis along Z with a suspension, a bearing and a wheel at each corner.
    private void SpawnCar(Vector3 at)
    {
        var car = new Creation(Car, _mods);
        for (var z = 0; z < 3; z++)
            Place(car, "block", new GridCell(0, 0, z));
        foreach (var z in new[] { 0, 2 })
        {
            foreach (var side in new[] { 1, -1 })
            {
                // The left side is the right side turned half a turn about Y, so its wheels spin the other way.
                var turn = side > 0 ? GridRotation.Identity : GridRotation.FromAxes(new GridCell(-1, 0, 0), new GridCell(0, 1, 0));
                _carSprings.Add(Place(car, "side_spring", new GridCell(side, 0, z), turn).Id);
                _carWheels.Add((Place(car, "side_bearing", new GridCell(2 * side, 0, z), turn).Id, side));
                Place(car, "wheel", new GridCell(3 * side, 0, z), GridRotation.FromAxes(new GridCell(0, -side, 0), new GridCell(side, 0, 0)));
            }
        }
        car.SetAllBodyPoses(new Pose(at.ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Creations.Add(car, _host.Main.Assets);
    }

    private bool Servo(int frame, int id, float angle)
    {
        if (frame == 0)
            Check(Act(MotionActions.SetBearingTarget, new SetBearingTargetArgs(id, _jointPart[id], 4f, 100f, angle)).Ok, $"servo bearing {id} to {angle} rad");
        if (frame < 120)
            return false;
        var at = HostPosition(id);
        Check(MathF.Abs(JointMath.Wrap(at - angle)) < 0.03f, $"bearing {id} holds {angle} rad (at {at:F3})");
        return true;
    }

    private bool CheckClient(int id, float tolerance)
    {
        var host = HostPosition(id);
        var client = Position(_client, id, _jointPart[id]);
        Check(MathF.Abs(JointMath.Wrap(host - client)) < tolerance, $"creation {id}: the client shows the joint at {client:F3} (host {host:F3})");
        return true;
    }

    private float HostPosition(int id) => Position(_host, id, _jointPart[id]);

    private static float Position(Instance instance, int id, int part)
    {
        var node = instance.Main.Creations.NodeOf(id) ?? throw new InvalidOperationException($"{instance} has no live creation {id}");
        var link = node.Creation.Joints.First(j => j.Connection.A == part);
        return node.JointPosition(link) ?? throw new InvalidOperationException($"joint {part} of creation {id} has no position on {instance}");
    }

    private ActionResult Act<TArgs>(string action, TArgs args)
    {
        var result = _host.Main.Actions.Dispatcher.Dispatch(Bot, action, args);
        if (!result.Ok)
            GD.Print($"[joints] {action} refused: {result.Message}");
        return result;
    }

    private PartInstance Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        if (!creation.TryPlacePart($"{Mod}:{part}", cell, rotation, out var placed, out var error))
            throw new InvalidOperationException(error);
        return placed;
    }
}
