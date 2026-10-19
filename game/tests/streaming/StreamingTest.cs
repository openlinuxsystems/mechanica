using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.World;
using Mechanica.Game.Creations;
using Mechanica.Game.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Chunk streaming test (chunk 12), host plus one client over localhost ENet. A 20 m two-segment vehicle
/// joined by a bearing drives across chunk borders, and a plane with a propeller on a bearing flies
/// diagonally over a chunk corner, both far from every player, so only their own force-loading keeps
/// ground and simulation around them. Each must stay in one piece the whole way. The plane then lands,
/// falls asleep and parks as its ground unloads; the client's player walking up (teleported on the host)
/// brings the ground back and resumes it exactly where it parked. The test (not parts) is the engine:
/// drivable and flyable parts come in chunks 10 and 11. The world is flat so heights stay simple;
/// generated terrain has its own test.
/// </summary>
public partial class StreamingTest : StagedTest
{
    private const string Mod = "runtime_test";
    private const float Gravity = 9.8f;
    private const int Vehicle = 1;
    private const int Plane = 2;
    // Bearing anchors in each Creation's grid frame (the rotor face of the bearing).
    private static readonly Vector3 VehicleHinge = new(40.5f * 0.25f, 0, 0);
    private static readonly Vector3 PlaneHinge = new(16.5f * 0.25f, 0, 0);
    private static readonly Vector3 Diagonal = new Vector3(1, 0, 1).Normalized();

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private ModLoadResult _content = null!;
    private PartAssets _assets = null!;
    private Vector3 _start;
    private readonly HashSet<ChunkCoord> _crossed = [];
    private List<Pose> _parkedPoses = [];
    private Vector3 _visit;

    protected override string Tag => "streaming";

    protected override int StageTimeoutFrames => 1500;

    private WorldStreamer HostWorld => _host.Main.Streamer;

    private CreationWorld HostCreations => _host.Main.Creations;

    public override void _Ready()
    {
        _content = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/creation/mods"));
        _assets = new PartAssets(_content);
        _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost(mods: _content);
        _client = _harness.StartClient("Client", mods: _content);

        Add("client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Wait("players settle", 30);
        Add("chunks load around every player, and only there", _ =>
        {
            var settings = HostWorld.Streamer.Settings;
            Check(settings.ChunkSize == 64f, $"shipped chunk size is used ({settings.ChunkSize} m)");
            Check(HostWorld.Streamer.IsLoaded(new ChunkCoord(0, 0)) && HostWorld.Chunks.ContainsKey(new ChunkCoord(0, 0)), "host has ground at spawn");
            Check(_client.Main.Streamer.Streamer.IsLoaded(new ChunkCoord(0, 0)), "client streams ground for its own player");
            Check(!HostWorld.Streamer.IsLoaded(new ChunkCoord(4, 0)) && !HostWorld.Streamer.IsLoaded(new ChunkCoord(7, 7)), "nothing is loaded far from players yet");
            Check(_host.Main.LocalPlayer!.GlobalPosition.Y is > -0.05f and < 0.1f, $"host player stands on streamed ground (y={_host.Main.LocalPlayer.GlobalPosition.Y:F3})");
            return true;
        });

        AddVehicleStages();
        AddPlaneStages();
        AddParkStages();
        AddEdgeStages();
    }

    // Past the world's edge there is no ground to load; the edge walls must hold anything thrown at them.
    private void AddEdgeStages()
    {
        RigidBody3D? crate = null;
        Add("a crate thrown at the world's edge stays inside it", frame =>
        {
            var edge = HostWorld.Streamer.Settings.WorldSize / 2f;
            if (frame == 0)
            {
                crate = new RigidBody3D { Name = "EdgeCrate", Mass = 50f, CanSleep = false, LinearVelocity = new Vector3(40, 0, 0) };
                crate.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One } });
                HostWorld.AddChild(crate);
                crate.GlobalPosition = new Vector3(edge - 10f, 20f, 0);
                return false;
            }
            if (frame < 90)
                return false;
            Check(crate!.GlobalPosition.X < edge && crate.GlobalPosition.X > edge - 40f, $"the crate is inside the edge at x={crate.GlobalPosition.X:F2} (edge {edge})");
            crate.QueueFree();
            return true;
        });
    }

    private void AddVehicleStages()
    {
        Add("spawn a long vehicle astride a border", _ =>
        {
            // Two 10 m segments, two blocks wide, coupled by a bearing whose axis runs along the vehicle.
            var vehicle = new Creation(Vehicle, _content);
            for (var x = 0; x < 81; x++)
            {
                if (x == 40)
                {
                    Place(vehicle, "bearing", new GridCell(40, 0, 0), GridRotation.FromAxes(new GridCell(0, -1, 0), new GridCell(1, 0, 0)));
                    continue;
                }
                Place(vehicle, "block", new GridCell(x, 0, 0));
                Place(vehicle, "block", new GridCell(x, 0, 1));
            }
            Check(vehicle.Bodies.Count == 2 && vehicle.Joints.Count == 1, $"vehicle has two bodies and one hinge ({vehicle.Bodies.Count}, {vehicle.Joints.Count})");
            vehicle.SetAllBodyPoses(new Transform3D(Basis.Identity, new Vector3(300, 0.15f, 0)).ToPose());
            HostCreations.Add(vehicle, _assets);
            HostWorld.Step(0f);
            Check(HostWorld.Streamer.IsLoaded(new ChunkCoord(4, 0)) && HostWorld.Streamer.IsLoaded(new ChunkCoord(5, 0)),
                "the vehicle force-loads both chunks it straddles");
            _start = Bodies(Vehicle)[0].GlobalPosition;
            return true;
        });
        Add("drive the vehicle across chunk borders", frame =>
        {
            foreach (var body in Bodies(Vehicle))
            {
                var push = body.Mass * (5f * (10f - body.LinearVelocity.X) + 0.6f * Gravity);
                body.ApplyCentralForce(new Vector3(push, 0, 0));
            }
            ExpectIntact(Vehicle, VehicleHinge, "vehicle");
            if (frame < 720)
                return false;
            var moved = Bodies(Vehicle)[0].GlobalPosition - _start;
            Check(moved.X > 100f, $"vehicle drove over 100 m (moved {moved})");
            Check(_crossed.Contains(new ChunkCoord(4, 0)) && _crossed.Contains(new ChunkCoord(6, 0)), $"vehicle touched chunks 4 to 6 ({string.Join(" ", _crossed)})");
            Check(!HostWorld.Streamer.IsLoaded(new ChunkCoord(4, 0)) && !HostWorld.Chunks.ContainsKey(new ChunkCoord(4, 0)),
                "the chunk the vehicle left unloads after the delay");
            Check(!_client.Main.Streamer.Streamer.IsLoaded(new ChunkCoord(6, 0)), "the client streams nothing for host Creations");
            _crossed.Clear();
            return true;
        });
    }

    private void AddPlaneStages()
    {
        Add("launch a plane toward a chunk corner", _ =>
        {
            // Fuselage along +x, wings along z, a propeller on a bearing at the nose.
            var plane = new Creation(Plane, _content);
            for (var x = 0; x < 16; x++)
                Place(plane, "block", new GridCell(x, 0, 0));
            for (var z = 1; z <= 12; z++)
            {
                Place(plane, "block", new GridCell(8, 0, z));
                Place(plane, "block", new GridCell(8, 0, -z));
            }
            Place(plane, "bearing", new GridCell(16, 0, 0), GridRotation.FromAxes(new GridCell(0, -1, 0), new GridCell(1, 0, 0)));
            Place(plane, "block", new GridCell(17, 0, 0));
            for (var z = 1; z <= 3; z++)
            {
                Place(plane, "block", new GridCell(17, 0, z));
                Place(plane, "block", new GridCell(17, 0, -z));
            }
            Check(plane.Bodies.Count == 2 && plane.Joints.Count == 1, "plane has an airframe and a propeller on a hinge");
            // Yaw so the nose points along +x+z, 40 m up, heading over the corner of chunks (7..8, 7..8).
            var heading = new Basis(Vector3.Up, -Mathf.Pi / 4);
            plane.SetAllBodyPoses(new Transform3D(heading, new Vector3(470, 40, 470)).ToPose());
            var node = HostCreations.Add(plane, _assets);
            foreach (var body in node.Bodies)
                body.LinearVelocity = Diagonal * 30f;
            node.Bodies[1].AngularVelocity = Diagonal * 40f;
            _start = node.Bodies[0].GlobalPosition;
            return true;
        });
        Add("fly the plane over the corner", frame =>
        {
            Fly(Diagonal * 30f);
            ExpectIntact(Plane, PlaneHinge, "plane");
            if (frame < 360)
                return false;
            var moved = Bodies(Plane)[0].GlobalPosition - _start;
            Check(moved.Length() > 150f, $"plane flew over 150 m ({moved})");
            Check(new[] { new ChunkCoord(7, 7), new ChunkCoord(7, 8), new ChunkCoord(8, 7), new ChunkCoord(8, 8) }.All(_crossed.Contains),
                $"plane flew over all four chunks of the corner ({string.Join(" ", _crossed)})");
            Check(Bodies(Plane)[1].AngularVelocity.Length() > 5f, "the propeller still spins on its hinge");
            return true;
        });
        Add("land the plane", _ =>
        {
            Fly(new Vector3(0, -6f, 0));
            ExpectIntact(Plane, PlaneHinge, "plane");
            return Bodies(Plane).Min(b => b.GlobalPosition.Y) < 1f;
        });
    }

    private void AddParkStages()
    {
        Add("the landed plane sleeps and parks before its ground unloads", _ =>
        {
            if (!HostCreations.IsParked(Plane))
            {
                ExpectIntact(Plane, PlaneHinge, "plane");
                return false;
            }
            var plane = HostCreations.Find(Plane)!;
            _parkedPoses = [.. Enumerable.Range(0, plane.Bodies.Count).Select(plane.BodyPose)];
            _visit = _parkedPoses[0].Position.ToGodot();
            var chunk = HostWorld.ChunkAt(_visit);
            Check(HostCreations.NodeOf(Plane) is null, "a parked plane has no bodies in the physics world");
            Check(!HostWorld.Streamer.IsLoaded(chunk) && !HostWorld.Chunks.ContainsKey(chunk), $"the plane's chunk {chunk} unloaded");
            Check(_visit.Y is > 0f and < 1f, $"it parked on the ground (y={_visit.Y:F3})");
            return true;
        });
        Add("the client's player walks up to the parked plane", frame =>
        {
            var onHost = _host.Main.PlayerSync.Players[_client.Peer];
            if (frame == 0)
            {
                onHost.ApplyState(_visit + new Vector3(10, 0.05f, 0), Vector3.Zero, 0f, 0f);
                return false;
            }
            if (frame == 1)
            {
                Check(!HostCreations.IsParked(Plane), "the host resumes the plane as soon as a client player is near");
                var node = HostCreations.NodeOf(Plane)!;
                for (var i = 0; i < node.Bodies.Count; i++)
                {
                    var (now, then) = (node.Bodies[i].GlobalTransform, _parkedPoses[i].ToTransform());
                    Check(now.Origin.DistanceTo(then.Origin) < 1e-3f && now.Basis.GetRotationQuaternion().AngleTo(then.Basis.GetRotationQuaternion()) < 1e-3f,
                        $"plane body {i} resumes exactly where it parked");
                }
                Check(HostWorld.Streamer.IsLoaded(HostWorld.ChunkAt(_visit)), "the plane's ground is back");
            }
            var mine = _client.Main.LocalPlayer!;
            if (frame < 60 || mine.GlobalPosition.DistanceTo(onHost.GlobalPosition) > 0.1f)
                return false;
            Check(onHost.GlobalPosition.Y is > -0.05f and < 0.1f, $"the client's player stands on host ground there (y={onHost.GlobalPosition.Y:F3})");
            Check(_client.Main.Streamer.Streamer.IsLoaded(_client.Main.Streamer.ChunkAt(mine.GlobalPosition)), "the client streamed ground under its own player");
            Check(mine.GlobalPosition.Y is > -0.05f and < 0.1f, $"the client's own prediction stands on it too (y={mine.GlobalPosition.Y:F3})");
            Check(HostWorld.Streamer.IsLoaded(new ChunkCoord(0, 0)), "the host still has ground under its own player");
            ExpectIntact(Plane, PlaneHinge, "plane");
            return true;
        });
    }

    // Holds every plane body at a velocity against gravity, standing in for wings and thrust.
    private void Fly(Vector3 velocity)
    {
        foreach (var body in Bodies(Plane))
            body.ApplyCentralForce(body.Mass * (new Vector3(0, Gravity, 0) + 4f * (velocity - body.LinearVelocity)));
    }

    private void ExpectIntact(int id, Vector3 hinge, string what)
    {
        if (Done)
            return;
        if (HostCreations.NodeOf(id) is not { } node)
        {
            Check(false, $"{what} stays live while it moves");
            return;
        }
        var bodies = node.Bodies;
        var gap = (bodies[0].GlobalTransform * hinge).DistanceTo(bodies[1].GlobalTransform * hinge);
        if (gap > 0.05f)
            Check(false, $"{what} hinge holds (gap {gap:F3} m)");
        var sample = HostCreations.Sample(id);
        foreach (var chunk in HostWorld.Streamer.ChunksOf(sample.Bounds))
        {
            _crossed.Add(chunk);
            if (!HostWorld.Chunks.ContainsKey(chunk))
                Check(false, $"{what} chunk {chunk} is loaded under it");
        }
        if (bodies.Any(b => b.GlobalPosition.Y < 0f))
            Check(false, $"{what} stays above the ground");
    }

    private IReadOnlyList<CreationBody> Bodies(int id) => HostCreations.NodeOf(id)?.Bodies ?? [];

    private void Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        if (!creation.TryPlacePart($"{Mod}:{part}", cell, rotation, out _, out var error))
            Check(false, $"place {part} at {cell}: {error}");
    }
}
