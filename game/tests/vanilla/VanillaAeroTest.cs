using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.World;
using Mechanica.Game.Editor;
using Mechanica.Game.Mods;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless test for chunk 11's aero and propulsion parts. Re-runs the Part Editor batch that authors the
/// thruster, wing, control surface, rotor and hover jet into a scratch copy of the mod and checks every file
/// it writes is byte-for-byte what is checked in (D3). Then, on a host with a client (D10), all through the
/// client's Action API: it wires a plane of vanilla parts (a seat to its thrusters on the Space/Shift
/// throttle lever and to its elevators on W/S), sits in it, opens the throttle, rolls down the runway, pulls
/// up and climbs; and wires a helicopter (four rotors, paired clockwise and counter-clockwise, and a
/// stabilizing hover jet, all on the Space/Shift collective), lifts off, climbs, holds a hover without
/// spinning and comes back down. The client shows both where the host has them, and the client's aero force
/// overlay draws the host's pushes. The Aero Tutorial's example mods' batches are checked the same way. It also prints how far the client's view of the moving plane strays
/// from the host's path (the snapshot rate check of PR #11's follow-up).
/// </summary>
public partial class VanillaAeroTest : StagedTest
{
    private const string BatchFile = "editor/batches/aero.json";
    private const int PlaneId = 1, HeliId = 2;
    private static readonly string[] Outputs = ["thruster", "wing", "control_surface", "rotor", "hover_jet"];

    // The plane takes off along the diagonal of one 64 m ground chunk, toward its corner at the origin.
    private static readonly Vector3 PlaneAt = new(58, 1.2f, 58);
    private static readonly Quaternion PlaneHeading = new(Vector3.Up, Mathf.Pi / 4);
    private static readonly Vector3 HeliAt = new(-20, 1.2f, -40);

    private readonly List<ActionResult> _results = [];
    private readonly List<(ulong Tick, Vector3 At)> _hostPath = [];
    private static readonly string[] ExampleMods = ["example_bearing", "example_thruster"];

    private Task<BatchRunResult>? _batch;
    private List<(string Mod, Task<BatchRunResult> Batch)> _examples = [];
    private string _scratchMod = "";
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _planeSeat, _heliSeat, _heliJet;
    private readonly List<int> _thrusters = [], _elevators = [], _rotors = [];
    private Vector3 _start;
    private float _maxStray, _sumStray;
    private int _strays;
    private float _heading;

    protected override string Tag => "vanilla-aero";

    protected override int StageTimeoutFrames => 1500;

    private static string VanillaDir => Path.Combine(ModPaths.ModsRoot, "vanilla");

    private static string ExamplesDir => Path.Combine(ModPaths.ModsRoot, "..", "examples", "mods");

    public override void _Ready()
    {
        Add("the Part Editor batch reproduces the checked-in aero parts exactly", frame =>
        {
            if (frame == 0)
            {
                var root = ProjectSettings.GlobalizePath("user://tests/vanilla_aero_mods");
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
        Add("the Aero Tutorial's example mods' batches reproduce their parts exactly", frame =>
        {
            if (frame == 0)
            {
                var root = ProjectSettings.GlobalizePath("user://tests/example_aero_mods");
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
                foreach (var mod in ExampleMods)
                {
                    var source = Path.Combine(ExamplesDir, mod);
                    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                        if (IsExampleOutput(relative))
                            continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, mod, relative))!);
                        File.Copy(file, Path.Combine(root, mod, relative));
                    }
                }
                _examples = ExampleMods.Select(mod => (mod, PartBatchRunner.RunAsync(this, Path.Combine(root, mod, "editor/batches/parts.json")))).ToList();
            }
            if (_examples.Any(e => !e.Batch.IsCompleted))
                return false;
            foreach (var (mod, batch) in _examples)
            {
                var result = batch.Result;
                Check(result.Succeeded && result.Exports.Count == 1, $"{mod}'s batch exports its part ({string.Join("; ", result.Diagnostics)})");
                var scratch = Path.Combine(ProjectSettings.GlobalizePath("user://tests/example_aero_mods"), mod);
                var made = Directory.GetFiles(scratch, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(scratch, f).Replace('\\', '/')).Where(IsExampleOutput).ToList();
                var source = Path.Combine(ExamplesDir, mod);
                var differing = made.Where(f => !File.Exists(Path.Combine(source, f))
                    || !File.ReadAllBytes(Path.Combine(source, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(scratch, f)))).ToList();
                Check(made.Count == 3 && differing.Count == 0,
                    $"{mod}: {made.Count} files, all checked in (differing: {string.Join(", ", differing)}); re-run tools/author-vanilla.sh");
            }
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
        Add("the host spawns an unwired plane, and the client walks up", frame =>
        {
            if (frame == 0)
            {
                SpawnPlane();
                _host.Main.PlayerSync.HostTeleport(_host.Peer, new Vector3(-40, 0.5f, -50));
                _host.Main.PlayerSync.HostTeleport(_client.Peer, PlaneAt + new Vector3(3f, -0.5f, -1f));
            }
            return frame >= 30 && _client.Main.Creations.NodeOf(PlaneId) is not null;
        });
        Wait("the plane settles on its wheels", 90);

        Add("the client wires the seat to the thrusters' throttle lever and the elevators", frame =>
        {
            if (frame == 0)
            {
                foreach (var thruster in _thrusters)
                {
                    Request(LogicActions.ConnectParts, new LinkPartsArgs(PlaneId, _planeSeat, thruster));
                    Configure(PlaneId, thruster, "control", "space_shift");
                }
                foreach (var elevator in _elevators)
                    Request(LogicActions.ConnectParts, new LinkPartsArgs(PlaneId, _planeSeat, elevator));
            }
            var links = _thrusters.Count + _elevators.Count;
            if (_results.Count < links + _thrusters.Count || _client.Main.Creations.Find(PlaneId)?.Links.Count != links)
                return false;
            Check(_results.All(r => r.Ok), $"linking and the settings succeed ({string.Join("; ", _results)})");
            _results.Clear();
            return true;
        });
        Add("the client sits in the plane", frame =>
        {
            if (frame == 0)
                Request(LogicActions.EnterSeat, new LogicPartArgs(PlaneId, _planeSeat));
            if (_results.Count < 1 || !_client.Main.LocalPlayer!.Seated)
                return false;
            Check(_results[0].Ok, $"sitting down succeeds ({_results[0]})");
            _results.Clear();
            _start = Body(_host, PlaneId).GlobalPosition;
            return true;
        });
        Add("Space opens the throttle and the plane rolls down the runway", frame =>
        {
            // Two seconds of Space opens the lever all the way (lever_rate 0.5 a second).
            Input(_client, jump: frame < 130);
            // Rolled up to 16 m/s: at about 100 kg the wings need a little over 14 to lift it.
            if (Body(_host, PlaneId).LinearVelocity.Length() < 16f && frame < 400)
                return false;
            var moved = Body(_host, PlaneId).GlobalPosition - _start;
            var speed = Body(_host, PlaneId).LinearVelocity.Length();
            var ahead = PlaneHeading * Vector3.Forward;
            Check(moved.Dot(ahead) > 10f && (moved - ahead * moved.Dot(ahead)).Length() < 0.1f * moved.Dot(ahead),
                $"it rolled straight ahead ({moved})");
            Check(speed > 14f, $"it picked up speed ({speed:F1} m/s in {frame / 60f:F1} s)");
            return true;
        });
        Add("the client's force overlay shows the thrusters and wings pushing", frame =>
        {
            Input(_client);
            if (frame == 0)
                _client.Main.ForceOverlay.Enabled = true;
            if (frame < 10)
                return false;
            var drawn = _client.Main.ForceOverlay.Drawn.Where(d => d.Creation == PlaneId).ToList();
            var thrusts = drawn.Where(d => d.Part is { } p && _thrusters.Contains(p)).ToList();
            Check(thrusts.Count == _thrusters.Count && thrusts.All(t => t.Force.Dot(PlaneHeading * Vector3.Forward) > 190f),
                $"every thruster's push is drawn, forward ({string.Join(", ", thrusts.Select(t => t.Force))})");
            Check(drawn.Count > _thrusters.Count + 4, $"and the wings' and elevators' ({drawn.Count} pushes)");
            _client.Main.ForceOverlay.Enabled = false;
            return true;
        });
        Add("S pulls the nose up and the plane takes off and climbs", frame =>
        {
            // The pilot holds S until the nose is about 10° up and eases off with W past 14°.
            var pitch = Mathf.RadToDeg(MathF.Asin(Math.Clamp(-Body(_host, PlaneId).GlobalBasis.Z.Y, -1f, 1f)));
            Input(_client, z: pitch < 8f ? -1 : pitch > 14f ? 1 : 0);
            Stray();
            var body = Body(_host, PlaneId);
            var height = body.GlobalPosition.Y - _start.Y;
            if (frame < 480)
                return false;
            Check(height > 8f, $"it climbed ({height:F1} m above the runway)");
            Check(body.GlobalBasis.Y.Dot(Vector3.Up) > 0.7f, $"it flies upright (up {body.GlobalBasis.Y})");
            Check(body.LinearVelocity.Y > -2f, $"it isn't falling ({body.LinearVelocity})");
            GD.Print($"[vanilla-aero] RESULT plane at {body.LinearVelocity.Length():F1} m/s: the client's view strays from the host's path by "
                + $"{_sumStray / Math.Max(1, _strays) * 100:F1} cm on average, {_maxStray * 100:F1} cm at most (snapshots every {Core.Net.NetProtocol.SnapshotInterval} ticks)");
            return true;
        });
        Add("the client sees the plane where the host has it", frame =>
        {
            Input(_client);
            Stray();
            if (frame < 30)
                return false;
            // The client shows the plane a few snapshots in the past, so it is behind by speed × delay.
            var host = Body(_host, PlaneId);
            var behind = Body(_client, PlaneId).GlobalPosition.DistanceTo(host.GlobalPosition);
            Check(behind < host.LinearVelocity.Length() * 0.2f + 0.5f, $"the client's plane is {behind:F2} m behind the host's");
            Check(_maxStray < 0.25f, $"and on its path (strays {_maxStray * 100:F1} cm at most)");
            return true;
        });
        Add("the client stands up", frame =>
        {
            if (frame == 0)
                Request(LogicActions.LeaveSeat, new NoArgs());
            if (_results.Count < 1 || _client.Main.LocalPlayer!.Seated)
                return false;
            Check(_results[0].Ok, $"standing up succeeds ({_results[0]})");
            _results.Clear();
            return true;
        });

        Add("the host spawns a helicopter and the client wires and boards it", frame =>
        {
            if (frame == 0)
                SpawnHelicopter();
            // The client stood up from the plane in flight and keeps its speed: hold it by the helicopter until it stops.
            if (frame <= 30)
                _host.Main.PlayerSync.HostTeleport(_client.Peer, HeliAt + new Vector3(0, 0.5f, -2.5f));
            if (frame == 40)
            {
                foreach (var part in _rotors.Append(_heliJet))
                {
                    Request(LogicActions.ConnectParts, new LinkPartsArgs(HeliId, _heliSeat, part));
                    Configure(HeliId, part, "control", "space_shift");
                }
            }
            if (frame == 120)
                Request(LogicActions.EnterSeat, new LogicPartArgs(HeliId, _heliSeat));
            if (frame < 120 || _results.Count < 11 || !_client.Main.LocalPlayer!.Seated)
                return false;
            Check(_results.All(r => r.Ok), $"linking, the settings and sitting down succeed ({string.Join("; ", _results)})");
            _results.Clear();
            _start = Body(_host, HeliId).GlobalPosition;
            _heading = Heading(Body(_host, HeliId));
            return true;
        });
        Add("Space raises the collective and the helicopter lifts off", frame =>
        {
            Input(_client, jump: frame < 90);
            if (frame < 300)
                return false;
            var body = Body(_host, HeliId);
            var height = body.GlobalPosition.Y - _start.Y;
            Check(height > 4f, $"it climbed ({height:F1} m)");
            Check(body.GlobalBasis.Y.Dot(Vector3.Up) > 0.95f, $"it stays level (up {body.GlobalBasis.Y})");
            return true;
        });
        Add("Shift lowers the collective to a hover without spinning", frame =>
        {
            // Half a second less a little: the 4 x 4 x 4 Driver's Seat on its post makes it a little heavier.
            Input(_client, sprint: frame < 26);
            if (frame < 300)
                return false;
            var body = Body(_host, HeliId);
            Check(MathF.Abs(body.LinearVelocity.Y) < 1.5f, $"it climbs or sinks slowly ({body.LinearVelocity.Y:F2} m/s)");
            Check(body.GlobalBasis.Y.Dot(Vector3.Up) > 0.95f, $"it stays level (up {body.GlobalBasis.Y})");
            var turned = Mathf.RadToDeg(Mathf.AngleDifference(_heading, Heading(body)));
            Check(MathF.Abs(turned) < 15f && body.AngularVelocity.Length() < 0.3f,
                $"the paired rotors' twists cancel (turned {turned:F1}°, spinning {body.AngularVelocity.Length():F2} rad/s)");
            var client = Body(_client, HeliId).GlobalPosition.DistanceTo(body.GlobalPosition);
            Check(client < body.LinearVelocity.Length() * 0.2f + 0.1f, $"the client has the helicopter {client:F2} m from the host");
            return true;
        });
        Add("a touch of Shift lets it sink gently back down", frame =>
        {
            // A fifth of a second of Shift takes the collective just under a hover.
            Input(_client, sprint: frame < 12);
            var body = Body(_host, HeliId);
            if (body.GlobalPosition.Y - _start.Y > 0.5f && frame < 900)
                return false;
            Check(body.GlobalPosition.Y - _start.Y < 0.5f, $"it landed ({body.GlobalPosition.Y - _start.Y:F1} m up)");
            Check(_host.Main.Creations.Find(HeliId)!.Parts.Count == 20, "in one piece");
            return true;
        });
    }

    // A cardboard plane flying toward -Z: a fuselage along Z with the seat on top (its 4 x 4 x 4 blocks and the pilot lift
    // the centre of mass, which the four 6 kg bearings low on the landing gear pull down and forward, back onto the
    // thrusters' line), two wings a side behind the centre of mass, elevators and a fin at the tail, four
    // thrusters pushing from the tail and four free wheels.
    private void SpawnPlane()
    {
        var plane = new Creation(PlaneId, _mods);
        const string Frame = "vanilla:cardboard_block";
        Place(plane, Frame, GridCell.Zero);
        for (var z = 1; z <= 8; z++)
            Place(plane, Frame, new GridCell(0, 0, z));
        // The Driver's Seat is 4 x 4 x 4 blocks: centred half a block right of the fuselage, over z 0..3.
        _planeSeat = Place(plane, "vanilla:drivers_seat", new GridCell(-1, 1, 0));
        var flipped = GridRotation.FromAxes(new GridCell(-1, 0, 0), new GridCell(0, 1, 0));
        // Wings by their roots on the fuselage's sides, and a second wing on each tip.
        Place(plane, "vanilla:wing", new GridCell(1, 0, 3));
        Place(plane, "vanilla:wing", new GridCell(5, 0, 3));
        Place(plane, "vanilla:wing", new GridCell(-1, 0, 6), flipped);
        Place(plane, "vanilla:wing", new GridCell(-5, 0, 6), flipped);
        _elevators.Add(Place(plane, "vanilla:control_surface", new GridCell(1, 0, 7)));
        _elevators.Add(Place(plane, "vanilla:control_surface", new GridCell(-1, 0, 8), flipped));
        // The fin stands on the tail by its hinge edge, its long side along Z.
        Place(plane, "vanilla:control_surface", new GridCell(0, 1, 4), GridRotation.FromAxes(new GridCell(0, 0, 1), new GridCell(1, 0, 0)));
        // The thrusters stack from y -2 to 1, so they push along a line through the centre of mass.
        Place(plane, Frame, new GridCell(0, 1, 8));
        Place(plane, Frame, new GridCell(0, -1, 8));
        Place(plane, Frame, new GridCell(0, -2, 8));
        foreach (var y in new[] { -2, -1, 0, 1 })
            _thrusters.Add(Place(plane, "vanilla:thruster", new GridCell(0, y, 9), Facing(new GridCell(0, 0, 1))));
        foreach (var z in new[] { 1, 5 })
        {
            Place(plane, Frame, new GridCell(0, -1, z));
            Place(plane, Frame, new GridCell(0, -2, z));
            foreach (var side in new[] { 1, -1 })
            {
                var outward = new GridCell(side, 0, 0);
                var bearing = new GridCell(side, -2, z);
                Place(plane, "vanilla:bearing", bearing, Facing(outward));
                var rotation = Facing(outward);
                Place(plane, "vanilla:scrap_wheel", bearing + outward - rotation.Apply(new GridCell(1, 0, 1)), rotation);
            }
        }
        plane.SetAllBodyPoses(new Pose(PlaneAt.ToNumerics(), PlaneHeading.ToNumerics()));
        var main = plane.Bodies.OrderByDescending(b => b.Mass).First();
        GD.Print($"[vanilla-aero] plane: {plane.Parts.Count} parts, {plane.Bodies.Count} bodies, {plane.Bodies.Sum(b => b.Mass):F0} kg, main body's centre of mass at {main.CentreOfMass}");
        _host.Main.Creations.Add(plane, _host.Main.Assets);
    }

    // A cardboard cross with the seat on a post in the middle, a rotor on each arm's end (clockwise on X, counter-clockwise
    // on Z) and a hover jet underneath.
    private void SpawnHelicopter()
    {
        var heli = new Creation(HeliId, _mods);
        const string Frame = "vanilla:cardboard_block";
        Place(heli, Frame, GridCell.Zero);
        foreach (var d in new[] { new GridCell(1, 0, 0), new GridCell(-1, 0, 0), new GridCell(0, 0, 1), new GridCell(0, 0, -1) })
        {
            for (var i = 1; i <= 3; i++)
                Place(heli, Frame, new GridCell(d.X * i, 0, d.Z * i));
        }
        // The Driver's Seat (4 x 4 x 4 blocks) sits on a post over the rotors' level.
        Place(heli, Frame, new GridCell(0, 1, 0));
        _heliSeat = Place(heli, "vanilla:drivers_seat", new GridCell(-1, 2, -1));
        foreach (var hub in new[] { new GridCell(3, 1, 0), new GridCell(-3, 1, 0), new GridCell(0, 1, 3), new GridCell(0, 1, -3) })
        {
            var rotor = Place(heli, "vanilla:rotor", hub - new GridCell(1, 0, 1));
            _rotors.Add(rotor);
            if (hub.Z != 0)
                heli.SetPartSettings(rotor, PartSettings.Apply(heli.DefinitionOf(rotor), null,
                    new Dictionary<string, JsonElement?> { ["spin"] = JsonSerializer.SerializeToElement("counterclockwise") }));
        }
        _heliJet = Place(heli, "vanilla:hover_jet", new GridCell(0, -1, 1), Facing(new GridCell(0, -1, 0)));
        heli.SetAllBodyPoses(new Pose(HeliAt.ToNumerics(), System.Numerics.Quaternion.Identity));
        GD.Print($"[vanilla-aero] helicopter: {heli.Parts.Count} parts, {heli.Bodies.Count} bodies, {heli.Bodies.Sum(b => b.Mass):F0} kg");
        _host.Main.Creations.Add(heli, _host.Main.Assets);
    }

    private void Input(Instance instance, float z = 0, bool jump = false, bool sprint = false) =>
        instance.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, z, 0, 0, jump, sprint);

    // How far the client's plane is from the nearest point of the host's recent path.
    private void Stray()
    {
        var tick = Engine.GetPhysicsFrames();
        _hostPath.Add((tick, Body(_host, PlaneId).GlobalPosition));
        if (_hostPath.Count > 60)
            _hostPath.RemoveAt(0);
        if (_hostPath.Count < 30)
            return;
        var at = Body(_client, PlaneId).GlobalPosition;
        var best = float.MaxValue;
        for (var i = 1; i < _hostPath.Count; i++)
        {
            var (a, b) = (_hostPath[i - 1].At, _hostPath[i].At);
            var ab = b - a;
            var t = ab.LengthSquared() < 1e-9f ? 0f : Math.Clamp((at - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
            best = MathF.Min(best, at.DistanceTo(a + ab * t));
        }
        _maxStray = MathF.Max(_maxStray, best);
        _sumStray += best;
        _strays++;
    }

    private static float Heading(Node3D body) => MathF.Atan2(-body.GlobalBasis.Z.X, -body.GlobalBasis.Z.Z);

    // Local +Y turned to point along outward.
    private static GridRotation Facing(GridCell outward) =>
        outward.Y != 0 ? GridRotation.FromAxes(new GridCell(1, 0, 0), outward) : GridRotation.FromAxes(new GridCell(0, -outward.X - outward.Z, 0), outward);

    private static int Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        if (!creation.TryPlacePart(part, cell, rotation, out var placed, out var error))
            throw new InvalidOperationException($"{part} at {cell}: {error}");
        return placed.Id;
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    private void Configure(int creation, int part, string id, string value) =>
        Request(LogicActions.ConfigurePart, new ConfigurePartArgs(creation, part,
            new Dictionary<string, JsonElement?> { [id] = JsonSerializer.SerializeToElement(value) }));

    private static Creations.CreationBody Body(Instance instance, int creation) => instance.Main.Creations.NodeOf(creation)!.Bodies[0];

    private static bool IsExampleOutput(string relative) =>
        relative.StartsWith("parts/", StringComparison.Ordinal) || relative.StartsWith("meshes/", StringComparison.Ordinal)
        || relative.StartsWith("editor/parts/", StringComparison.Ordinal);

    private static bool IsBatchOutput(string relative) =>
        Outputs.Any(id => relative == $"parts/{id}.json" || relative == $"meshes/{id}.glb" || relative == $"editor/parts/{id}.json"
            || relative == $"recipes/{id}.json");
}
