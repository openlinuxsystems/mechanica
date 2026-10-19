using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;
using Mechanica.Core.World;
using Mechanica.Game.Editor;
using Mechanica.Game.Mods;
using Mechanica.Scripting;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless test for chunk 9's vanilla joint parts. Re-runs the Part Editor batch that authors the
/// bearing, piston and both suspensions into a scratch copy of the mod and checks every file it writes
/// is byte-for-byte what is checked in (D3). Then, on a host with a client (D10), with vanilla plus the
/// joint fixture mod for a wheel: a car on sport suspension and one on off-road suspension settle with
/// the soft one sagging more and the sport coils flexing to their mounts, both drive on vanilla bearings, the vanilla piston's script pushes
/// its load out to the end of its stroke when its input comes on and pulls it back when it goes off,
/// with the client showing the same, and the sport springs upgraded to level 5 carry their car stiffer.
/// </summary>
public partial class VanillaJointsTest : StagedTest
{
    private const string BatchFile = "editor/batches/joints.json";
    private const int Sport = 1, OffRoad = 2, Lift = 3;
    private static readonly ActionCaller Bot = new(Creation.HostPeer, ActionSource.Bot);
    private static readonly string[] Outputs = ["bearing", "piston", "sport_suspension", "offroad_suspension"];

    private Task<BatchRunResult>? _batch;
    private string _scratchMod = "";
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private readonly Dictionary<int, List<int>> _springs = [];
    private readonly Dictionary<int, List<(int Part, int Sign)>> _wheels = [];
    private readonly Dictionary<int, Vector3> _start = [];
    private int _piston;
    private float _sportSag;

    protected override string Tag => "vanilla-joints";

    protected override int StageTimeoutFrames => 1200;

    private static string VanillaDir => Path.Combine(ModPaths.ModsRoot, "vanilla");

    public override void _Ready()
    {
        Add("the Part Editor batch reproduces the checked-in joint parts exactly", frame =>
        {
            if (frame == 0)
            {
                var root = ProjectSettings.GlobalizePath("user://tests/vanilla_joint_mods");
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
                _scratchMod = Path.Combine(root, "vanilla");
                foreach (var file in Directory.GetFiles(VanillaDir, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(VanillaDir, file).Replace('\\', '/');
                    if (IsBatchOutput(relative))
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_scratchMod, relative))!);
                    File.Copy(file, Path.Combine(_scratchMod, relative));
                }
                _batch = PartBatchRunner.RunAsync(this, Path.Combine(_scratchMod, BatchFile));
            }
            if (_batch is not { IsCompleted: true })
                return false;
            var result = _batch.Result;
            Check(result.Succeeded && result.Exports.Count == 8, $"the batch exports 4 parts and 4 recipes ({string.Join("; ", result.Diagnostics)})");
            var made = Directory.GetFiles(_scratchMod, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_scratchMod, f).Replace('\\', '/'))
                .Where(IsBatchOutput)
                .Order(StringComparer.Ordinal).ToList();
            // Every joint part's models are files made elsewhere (tools/bearing_engine, tools/piston,
            // tools/sport_suspension, tools/offroad_suspension), so none gets a generated mesh.
            Check(made.Count == 12, $"it writes a part, an editor source and a recipe for each ({made.Count} files)");
            var differing = made.Where(f => !File.Exists(Path.Combine(VanillaDir, f))
                || !File.ReadAllBytes(Path.Combine(VanillaDir, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(_scratchMod, f)))).ToList();
            Check(differing.Count == 0, $"every file matches mods/vanilla (differing: {string.Join(", ", differing)}); re-run tools/author-vanilla.sh");
            return true;
        });

        Add("a host and a client start on vanilla and the joint fixtures", frame =>
        {
            if (frame == 0)
            {
                _mods = ModLoader.Load([VanillaDir, ProjectSettings.GlobalizePath("res://tests/joints/mods/joint_test")]);
                Check(!_mods.HasErrors, $"the mods load ({string.Join("; ", _mods.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))})");
                _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
                AddChild(_harness);
                _host = _harness.StartHost(mods: _mods);
                _client = _harness.StartClient("Client", mods: _mods);
            }
            return frame > 0 && _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null;
        });
        Add("spawn two cars and a piston lift", _ =>
        {
            SpawnCar(Sport, "vanilla:sport_suspension", new Vector3(3, 0.6f, 4));
            SpawnCar(OffRoad, "vanilla:offroad_suspension", new Vector3(7, 0.6f, 4));
            var lift = new Creation(Lift, _mods);
            Place(lift, "joint_test:heavy_block", GridCell.Zero);
            _piston = Place(lift, "vanilla:piston", new GridCell(0, 1, 0)).Id;
            Place(lift, "joint_test:block", new GridCell(0, 2, 0));
            lift.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(5, 0.2f, 1), System.Numerics.Quaternion.Identity));
            _host.Main.Creations.Add(lift, _host.Main.Assets);
            return true;
        });
        Wait("everything settles", 180);

        Add("both cars rest level, the off-road one lower on its softer springs", _ =>
        {
            var sport = _springs[Sport].Select(p => Position(_host, Sport, p)).ToList();
            var offroad = _springs[OffRoad].Select(p => Position(_host, OffRoad, p)).ToList();
            Check(sport.All(s => s is > 0.001f and < 0.08f), $"sport springs carry the car ({Join(sport)})");
            Check(offroad.All(s => s is > 0.001f and < 0.2f), $"off-road springs carry the car ({Join(offroad)})");
            Check(offroad.Average() > sport.Average() * 2, "the off-road suspension sags more than the sport one");
            _sportSag = sport.Average();
            var node = _host.Main.Creations.NodeOf(Sport)!;
            foreach (var part in _springs[Sport])
            {
                var flex = node.Flexes.Where(f => f.Part == part).Select(f => f.Offset).ToList();
                var position = Position(_host, Sport, part);
                Check(flex.Count == 1 && MathF.Abs(flex[0] - position) < 0.002f,
                    $"spring {part}'s coil reaches its mount ({Join(flex)} for {position:F4})");
            }
            foreach (var id in new[] { Sport, OffRoad })
            {
                var chassis = _host.Main.Creations.NodeOf(id)!.Bodies[0];
                Check(chassis.GlobalBasis.Y.Dot(Vector3.Up) > 0.999f, $"car {id} is level");
                _start[id] = chassis.GlobalPosition;
            }
            return true;
        });
        Add("the piston rests retracted under its script", _ =>
        {
            Check(_host.Main.Scripts.Runtime.Instances.Any(i => i.Part == _piston && i.Fault is null), "the piston's script runs on the host");
            var length = Position(_host, Lift, _piston);
            Check(MathF.Abs(length) < 0.01f, $"the piston holds 0 m ({length:F4})");
            return true;
        });
        Add("both cars drive on vanilla bearings", frame =>
        {
            if (frame == 0)
            {
                foreach (var id in new[] { Sport, OffRoad })
                    foreach (var (part, sign) in _wheels[id])
                        Check(Act(MotionActions.SetBearingTarget, new SetBearingTargetArgs(id, part, sign * 6f, 80f)).Ok, $"car {id} wheel {part} drives");
            }
            foreach (var id in new[] { Sport, OffRoad })
            {
                if (_host.Main.Creations.NodeOf(id)!.Bodies[0].GlobalBasis.Y.Dot(Vector3.Up) < 0.95f)
                    return !Check(false, $"car {id} stays upright (tipped at frame {frame})");
            }
            if (frame < 150)
                return false;
            foreach (var id in new[] { Sport, OffRoad })
            {
                var moved = _host.Main.Creations.NodeOf(id)!.Bodies[0].GlobalPosition - _start[id];
                Check(moved.Z > 1.5f && MathF.Abs(moved.X) < 0.5f, $"car {id} drove forward ({moved})");
                foreach (var (part, _) in _wheels[id])
                    Act(MotionActions.SetBearingTarget, new SetBearingTargetArgs(id, part, 0f, 80f));
            }
            return true;
        });
        Add("the piston pushes out when its input comes on", frame => Stroke(frame, 1f, 1.75f));
        Add("the client shows the piston extended, rod and all", frame => frame >= 30 && CheckClient(_piston) && CheckRod(14));
        Add("the piston pulls back when its input goes off", frame => Stroke(frame, 0f, 0f));
        Add("the rod goes back into the piston", frame => frame >= 30 && CheckRod(0));
        Add("the client shows the cars where the host has them", frame =>
        {
            if (frame < 60)
                return false;
            foreach (var id in new[] { Sport, OffRoad })
            {
                var host = _host.Main.Creations.NodeOf(id)!.Bodies[0].GlobalPosition;
                var client = _client.Main.Creations.NodeOf(id)!.Bodies[0].GlobalPosition;
                Check(host.DistanceTo(client) < 0.05f, $"car {id}: the client has it {host.DistanceTo(client):F3} m from the host");
            }
            return true;
        });
        Add("level 5 sport springs carry the car stiffer", frame =>
        {
            var node = _host.Main.Creations.NodeOf(Sport)!;
            if (frame == 0)
            {
                foreach (var part in _springs[Sport])
                    node.Creation.SetLevel(part, 4);
                _host.Main.Creations.Rebuild(Sport);
                return false;
            }
            if (frame < 180)
                return false;
            foreach (var part in _springs[Sport])
            {
                var link = node.Creation.Joints.First(j => j.Connection.A == part);
                Check(node.JointOf(link.Connection) is { } joint && node.FrameOf(joint).Stiffness == 125000f,
                    $"spring {part} is rebuilt with level 5's stiffness");
            }
            var sag = _springs[Sport].Select(p => Position(_host, Sport, p)).ToList();
            var mean = sag.Average();
            Check(mean > 0f && mean < _sportSag * 0.5f, $"they sag less than level 1's {_sportSag:F4} ({Join(sag)})");
            return true;
        });
    }

    private bool Stroke(int frame, float input, float expected)
    {
        if (frame == 0)
        {
            var call = _host.Main.Scripts.Runtime.Invoke(Lift, _piston, ScriptRuntime.Hooks.OnPower, input);
            Check(call.Ok, $"on_power({input}) runs ({call.Error})");
        }
        // Piston 1's 7 blocks (1.75 m) at 2 blocks/s (0.5 m/s), plus the slow-down at the end.
        if (frame < 300)
            return false;
        var length = Position(_host, Lift, _piston);
        Check(MathF.Abs(length - expected) < 0.01f, $"the piston holds {expected} m ({length:F4})");
        return true;
    }

    // The rod shows one 0.125 m segment per half block of stroke, on host and client, and the head rides the far side.
    private bool CheckRod(int segments)
    {
        foreach (var instance in new[] { _host, _client })
        {
            var node = instance.Main.Creations.NodeOf(Lift)!;
            var shown = node.RodSegments.Where(r => r.Part == _piston).Select(r => r.Segments).ToList();
            Check(shown.Count == 1 && shown[0] == segments, $"{instance} shows {segments} rod segments ({string.Join(", ", shown)})");
            Check(node.Bodies.Skip(1).Any(b => b.GetNodeOrNull($"Part{_piston}head") is not null), $"{instance} moves the head with what is on it");
        }
        return true;
    }

    private bool CheckClient(int part)
    {
        var host = Position(_host, Lift, part);
        var client = Position(_client, Lift, part);
        Check(MathF.Abs(host - client) < 0.01f, $"the client shows the piston at {client:F3} m (host {host:F3})");
        return true;
    }

    // A three-block metal chassis along Z with a suspension, a vanilla bearing and a fixture wheel at each corner.
    private void SpawnCar(int id, string suspension, Vector3 at)
    {
        var car = new Creation(id, _mods);
        _springs[id] = [];
        _wheels[id] = [];
        for (var z = 0; z < 3; z++)
            Place(car, "vanilla:metal_block", new GridCell(0, 0, z));
        // The sport suspension is two blocks long, the off-road one one.
        var length = _mods.FindDefinition(suspension)!.Size?.X ?? 1;
        foreach (var z in new[] { 0, 2 })
        {
            foreach (var side in new[] { 1, -1 })
            {
                var outward = new GridCell(side, 0, 0);
                // The suspension's mount and the bearing's rotor both face away from the chassis.
                var mount = side > 0 ? GridRotation.Identity : GridRotation.FromAxes(new GridCell(-1, 0, 0), new GridCell(0, 1, 0));
                _springs[id].Add(Place(car, suspension, new GridCell(side, 0, z), mount).Id);
                _wheels[id].Add((Place(car, "vanilla:bearing", new GridCell((length + 1) * side, 0, z), GridRotation.FromAxes(new GridCell(0, -side, 0), outward)).Id, side));
                Place(car, "joint_test:wheel", new GridCell((length + 2) * side, 0, z), GridRotation.FromAxes(new GridCell(0, -side, 0), outward));
            }
        }
        car.SetAllBodyPoses(new Pose(at.ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Creations.Add(car, _host.Main.Assets);
    }

    private static float Position(Instance instance, int id, int part)
    {
        var node = instance.Main.Creations.NodeOf(id) ?? throw new InvalidOperationException($"{instance} has no live creation {id}");
        var link = node.Creation.Joints.First(j => j.Connection.A == part);
        return node.JointPosition(link) ?? throw new InvalidOperationException($"joint {part} of creation {id} has no position on {instance}");
    }

    private ActionResult Act<TArgs>(string action, TArgs args) => _host.Main.Actions.Dispatcher.Dispatch(Bot, action, args);

    private static PartInstance Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        if (!creation.TryPlacePart(part, cell, rotation, out var placed, out var error))
            throw new InvalidOperationException(error);
        return placed;
    }

    private static string Join(IEnumerable<float> values) => string.Join(", ", values.Select(v => v.ToString("F4")));

    private static bool IsBatchOutput(string relative) =>
        Outputs.Any(id => relative == $"parts/{id}.json" || relative == $"meshes/{id}.glb" || relative == $"editor/parts/{id}.json"
            || relative == $"recipes/{id}.json");
}
