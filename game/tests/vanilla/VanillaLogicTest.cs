using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.World;
using Mechanica.Game.Editor;
using Mechanica.Game.Logic;
using Mechanica.Game.Mods;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless test for chunk 10's logic and power parts. Re-runs the Part Editor batch that authors the
/// seats, engines, wheels and logic parts into a scratch copy of the mod and checks every file it writes is
/// byte-for-byte what is checked in (D3). Then, on a host with a client (D10), all through the client's
/// Action API: it wires a car of vanilla parts with the Connect Tool's actions (seat to engine and to the
/// steering bearing, engine to the four wheel bearings) and sets the engine's gear, sits in the driver's
/// seat, drives forward toward the engine's front, steers right, brakes and stands up, with the client
/// showing the car where the host has it. The Logic Visualizer and the Creation Inspector read the host's
/// signals, and a switch drives the vanilla piston through a link.
/// </summary>
public partial class VanillaLogicTest : StagedTest
{
    private const string BatchFile = "editor/batches/logic_power.json";
    private const int CarId = 1, LiftId = 2;
    private static readonly string[] Outputs =
    [
        "drivers_seat", "scrap_drivers_seat", "passenger_seat", "scrap_seat", "gas_engine", "scrap_gas_engine", "electric_engine",
        "scrap_wheel", "wheel", "big_wheel", "logic_gate", "timer", "sensor", "switch", "button", "controller",
    ];

    private static readonly Vector3 CarAt = new(4, 1.0f, 8);

    private readonly List<ActionResult> _results = [];
    private Task<BatchRunResult>? _batch;
    private string _scratchMod = "";
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _seat, _engine, _steering, _switch, _piston;
    private readonly List<int> _wheels = [];
    private Vector3 _start;

    protected override string Tag => "vanilla-logic";

    protected override int StageTimeoutFrames => 1200;

    private static string VanillaDir => Path.Combine(ModPaths.ModsRoot, "vanilla");

    public override void _Ready()
    {
        Add("the Part Editor batch reproduces the checked-in logic and power parts exactly", frame =>
        {
            if (frame == 0)
            {
                var root = ProjectSettings.GlobalizePath("user://tests/vanilla_logic_mods");
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
            Check(result.Succeeded && result.Exports.Count == 2 * Outputs.Length,
                $"the batch exports {Outputs.Length} parts and their recipes ({string.Join("; ", result.Diagnostics)})");
            var made = Directory.GetFiles(_scratchMod, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_scratchMod, f).Replace('\\', '/'))
                .Where(IsBatchOutput)
                .Order(StringComparer.Ordinal).ToList();
            Check(made.Count == 4 * Outputs.Length, $"it writes a part, an editor source, a mesh and a recipe for each ({made.Count} files)");
            var differing = made.Where(f => !File.Exists(Path.Combine(VanillaDir, f))
                || !File.ReadAllBytes(Path.Combine(VanillaDir, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(_scratchMod, f)))).ToList();
            Check(differing.Count == 0, $"every file matches mods/vanilla (differing: {string.Join(", ", differing)}); re-run tools/author-vanilla.sh");
            return true;
        });

        Add("a host and a client start on vanilla in Creative", frame =>
        {
            if (frame == 0)
            {
                _mods = ModLoader.Load([VanillaDir]);
                Check(!_mods.HasErrors, $"vanilla loads ({string.Join("; ", _mods.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))})");
                _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
                AddChild(_harness);
                _host = _harness.StartHost(mods: _mods);
                _client = _harness.StartClient("Client", mods: _mods);
            }
            return frame > 0 && _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null;
        });
        Add("the host spawns an unwired car and a switch with a piston, and the client walks up", frame =>
        {
            if (frame == 0)
            {
                SpawnCar();
                SpawnLift();
                _host.Main.PlayerSync.HostTeleport(_client.Peer, CarAt + new Vector3(1.5f, -1f, 0.5f));
            }
            return frame >= 30 && _client.Main.Creations.NodeOf(CarId) is not null && _client.Main.Creations.NodeOf(LiftId) is not null;
        });
        Wait("the car settles", 120);

        Add("the client wires the car and picks second gear", frame =>
        {
            if (frame == 0)
            {
                Request(LogicActions.ConnectParts, new LinkPartsArgs(CarId, _seat, _engine));
                Request(LogicActions.ConnectParts, new LinkPartsArgs(CarId, _seat, _steering));
                foreach (var wheel in _wheels)
                    Request(LogicActions.ConnectParts, new LinkPartsArgs(CarId, _engine, wheel));
                Request(LogicActions.ConfigurePart, new ConfigurePartArgs(CarId, _engine,
                    new Dictionary<string, JsonElement?> { ["gear"] = JsonSerializer.SerializeToElement(2) }));
            }
            if (_results.Count < 7 || _client.Main.Creations.Find(CarId)?.Links.Count != 6)
                return false;
            Check(_results.All(r => r.Ok), $"linking and the gear succeed ({string.Join("; ", _results)})");
            Check(_host.Main.Creations.Find(CarId)!.Links.Count == 6, "the host has the six links");
            Check(_client.Main.Creations.Find(CarId)!.FindPart(_engine)!.Settings?["gear"].GetInt32() == 2, "the client sees second gear");
            _results.Clear();
            return true;
        });
        Add("the client sits in the driver's seat", frame =>
        {
            if (frame == 0)
                Request(LogicActions.EnterSeat, new LogicPartArgs(CarId, _seat));
            if (_results.Count < 1 || !_client.Main.LocalPlayer!.Seated)
                return false;
            Check(_results[0].Ok, $"sitting down succeeds ({_results[0]})");
            Check(_host.Main.Logic.Seats.Occupant(CarId, _seat) == _client.Peer, "the host seats the client");
            Check(HostPlayer().Seated, "the host shows the client seated");
            var feet = _host.Main.Logic.SeatPose(CarId, _seat)!.Value.Feet;
            Check(HostPlayer().GlobalPosition.DistanceTo(feet) < 0.05f, "the client's player is on the seat");
            _results.Clear();
            _start = Chassis(_host).GlobalPosition;
            return true;
        });
        Add("W drives the car toward the engine's front", frame =>
        {
            _client.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            if (Chassis(_host).GlobalBasis.Y.Dot(Vector3.Up) < 0.9f)
                return !Check(false, $"the car stays upright (tipped at frame {frame}: up {Chassis(_host).GlobalBasis.Y}, moved {Chassis(_host).GlobalPosition - _start})");
            if (frame < 90)
                return false;
            var moved = Chassis(_host).GlobalPosition - _start;
            // The seat and engine sit a little off the centre line, so it drifts a little.
            Check(moved.Z < -1.5f && MathF.Abs(moved.X) < 0.15f * -moved.Z, $"the car drove forward along -Z ({moved})");
            var inspection = _host.Main.Logic.Actions!.Inspect(_host.Main.Creations.Find(CarId)!);
            Check(inspection.Parts.First(p => p.Id == _seat).Output == 1f && inspection.Parts.First(p => p.Id == _engine).Input == 1f,
                "the seat's throttle reaches the engine");
            Check(HostPlayer().GlobalPosition.DistanceTo(_host.Main.Logic.SeatPose(CarId, _seat)!.Value.Feet) < 0.1f, "the driver rides along");
            return true;
        });
        Add("the client's Logic Visualizer and Creation Inspector show the live signals", frame =>
        {
            _client.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            if (frame == 0)
            {
                _client.Main.LogicOverlay.Enabled = true;
                _client.Main.Inspector.Open(CarId);
            }
            if (frame < 30)
                return false;
            var drawn = _client.Main.LogicOverlay.Drawn.Where(d => d.Creation == CarId).ToList();
            Check(drawn.Count == 6, $"the visualizer draws the car's six links ({drawn.Count})");
            Check(drawn.Where(d => d.Link.From == _seat || d.Link.From == _engine).All(d => d.On), "every link carries the throttle");
            var inspection = _client.Main.Inspector.Inspection;
            Check(inspection is { } i && i.Parts.First(p => p.Id == _seat).Occupant == _client.Peer && i.Parts.First(p => p.Id == _engine).Output == 1f,
                "the inspector shows the driver and the engine's output");
            Check(CreationInspector.Describe(inspection!).Contains("gear=2"), "the inspector lists the engine's gear");
            _client.Main.LogicOverlay.Enabled = false;
            _client.Main.Inspector.Close();
            return true;
        });
        Add("A and D steer: right turns the car toward +X", frame =>
        {
            if (frame == 0)
                _start = Chassis(_host).GlobalPosition;
            _client.Main.LocalPlayer!.CurrentInput = new PlayerInput(1, 1, 0, 0, false, false);
            if (Chassis(_host).GlobalBasis.Y.Dot(Vector3.Up) < 0.9f)
                return !Check(false, $"the car stays upright in the turn (tipped at frame {frame})");
            if (frame < 120)
                return false;
            var steer = Joint(_host, _steering);
            Check(steer is > 0.3f and < 0.6f, $"the front axle turned to about 30° right ({steer:F3} rad about its downward axis)");
            var heading = -Chassis(_host).GlobalBasis.Z;
            Check(heading.X > 0.2f, $"the car turned right (heading {heading})");
            Check((Chassis(_host).GlobalPosition - _start).X > 0.2f, "and moved toward +X");
            return true;
        });
        Add("letting go straightens the wheels and brakes", frame =>
        {
            _client.Main.LocalPlayer!.CurrentInput = PlayerInput.None;
            if (frame < 120)
                return false;
            Check(MathF.Abs(Joint(_host, _steering)) < 0.05f, $"the wheels point ahead again ({Joint(_host, _steering):F3})");
            Check(Chassis(_host).LinearVelocity.Length() < 0.3f, $"the car has stopped ({Chassis(_host).LinearVelocity.Length():F2} m/s)");
            return true;
        });
        Add("the client sees the car where the host has it", frame =>
        {
            if (frame < 30)
                return false;
            var host = Chassis(_host).GlobalPosition;
            var client = Chassis(_client).GlobalPosition;
            Check(host.DistanceTo(client) < 0.05f, $"the client has the car {host.DistanceTo(client):F3} m from the host");
            return true;
        });
        Add("the client stands up", frame =>
        {
            if (frame == 0)
                Request(LogicActions.LeaveSeat, new NoArgs());
            if (_results.Count < 1 || _client.Main.LocalPlayer!.Seated)
                return false;
            Check(_results[0].Ok, $"standing up succeeds ({_results[0]})");
            Check(_host.Main.Logic.Seats.All.Count == 0 && !HostPlayer().Seated, "nobody sits in the car any more");
            _results.Clear();
            return true;
        });
        Add("a switch linked to the piston pushes it out", frame =>
        {
            if (frame == 0)
            {
                _host.Main.PlayerSync.HostTeleport(_client.Peer, Lift(_host).GlobalPosition + new Vector3(1.5f, -0.2f, 0));
            }
            if (frame == 20)
            {
                Request(LogicActions.ConnectParts, new LinkPartsArgs(LiftId, _switch, _piston));
                Request(LogicActions.ActivatePart, new LogicPartArgs(LiftId, _switch));
            }
            if (frame < 150)
                return false;
            Check(_results.Count == 2 && _results.All(r => r.Ok), $"linking and flipping the switch succeed ({string.Join("; ", _results)})");
            Check(MathF.Abs(Joint(_host, _piston, LiftId) - 1f) < 0.02f, $"the host's piston is out ({Joint(_host, _piston, LiftId):F3} m)");
            Check(MathF.Abs(Joint(_client, _piston, LiftId) - 1f) < 0.02f, $"the client's piston is out ({Joint(_client, _piston, LiftId):F3} m)");
            _results.Clear();
            return true;
        });
    }

    // The car of LogicScriptTests: a metal chassis along Z (z 0..4) with a driver's seat and a gas engine on
    // top facing -Z, so the front is z 0: the front wheels hang off an axle under a steering bearing there, the rear
    // wheels off a post at z 4.
    private void SpawnCar()
    {
        var car = new Creation(CarId, _mods);
        Place(car, "vanilla:metal_block", GridCell.Zero);
        for (var z = 1; z <= 4; z++)
            Place(car, "vanilla:metal_block", new GridCell(0, 0, z));
        _seat = Place(car, "vanilla:drivers_seat", new GridCell(0, 1, 1));
        _engine = Place(car, "vanilla:gas_engine", new GridCell(0, 1, 3));
        Place(car, "vanilla:metal_block", new GridCell(0, -1, 4));
        Place(car, "vanilla:metal_block", new GridCell(0, -2, 4));
        _steering = Place(car, "vanilla:bearing", new GridCell(0, -1, 0), Facing(new GridCell(0, -1, 0)));
        Place(car, "vanilla:metal_block", new GridCell(0, -2, 0));
        foreach (var z in new[] { 0, 4 })
        {
            foreach (var side in new[] { 1, -1 })
            {
                var outward = new GridCell(side, 0, 0);
                var bearing = new GridCell(side, -2, z);
                _wheels.Add(Place(car, "vanilla:bearing", bearing, Facing(outward)));
                var rotation = Facing(outward);
                Place(car, "vanilla:wheel", bearing + outward - rotation.Apply(new GridCell(1, 0, 1)), rotation);
            }
        }
        car.SetAllBodyPoses(new Pose(CarAt.ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Creations.Add(car, _host.Main.Assets);
    }

    private void SpawnLift()
    {
        var lift = new Creation(LiftId, _mods);
        Place(lift, "vanilla:metal_block", GridCell.Zero);
        Place(lift, "vanilla:metal_block", new GridCell(1, 0, 0));
        _switch = Place(lift, "vanilla:switch", new GridCell(1, 1, 0));
        _piston = Place(lift, "vanilla:piston", new GridCell(0, 1, 0));
        Place(lift, "vanilla:metal_block", new GridCell(0, 2, 0));
        lift.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(-4, 0.2f, 4), System.Numerics.Quaternion.Identity));
        _host.Main.Creations.Add(lift, _host.Main.Assets);
    }

    // Local +Y turned to point along outward.
    private static GridRotation Facing(GridCell outward) =>
        outward.Y != 0 ? GridRotation.FromAxes(new GridCell(1, 0, 0), outward) : GridRotation.FromAxes(new GridCell(0, -outward.X - outward.Z, 0), outward);

    private static int Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        if (!creation.TryPlacePart(part, cell, rotation, out var placed, out var error))
            throw new InvalidOperationException(error);
        return placed.Id;
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    private Player.PlayerController HostPlayer() => _host.Main.PlayerSync.Players[_client.Peer];

    private static Creations.CreationBody Chassis(Instance instance) => instance.Main.Creations.NodeOf(CarId)!.Bodies[0];

    private static Creations.CreationBody Lift(Instance instance) => instance.Main.Creations.NodeOf(LiftId)!.Bodies[0];

    private static float Joint(Instance instance, int part, int creation = CarId)
    {
        var node = instance.Main.Creations.NodeOf(creation) ?? throw new InvalidOperationException($"{instance} has no live creation {creation}");
        var link = node.Creation.Joints.First(j => j.Connection.A == part);
        return node.JointPosition(link) ?? throw new InvalidOperationException($"joint {part} has no position on {instance}");
    }

    private static bool IsBatchOutput(string relative) =>
        Outputs.Any(id => relative == $"parts/{id}.json" || relative == $"meshes/{id}.glb" || relative == $"editor/parts/{id}.json"
            || relative == $"recipes/{id}.json");
}
