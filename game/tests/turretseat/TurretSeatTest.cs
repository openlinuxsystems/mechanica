using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.Saves;
using Mechanica.Core.World;
using Mechanica.Game.Mods;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Turret Seat (parts map batch 17d, engine item E8) with a host and a client, all through the client's input and
/// Action API. A deck carries a level 1 Turret Seat with a five-block barrel built on its hardpoint plate, near a chunk
/// border. The client sits in it and looks about: on the host the turret's aimed joint turns and tilts the shell and the
/// barrel toward the client's view at the level's speed, and stops at the level's yaw and pitch limits (up and down, left
/// and right); the client sees the barrel and the shell where the host has them, and rides the turning shell. The deck is
/// then pushed across the chunk border with the client aiming, and the turret stays whole and on target the whole way;
/// it parks and resumes without a jump; and a save made with it turned loads back with it turned.
/// </summary>
public partial class TurretSeatTest : StagedTest
{
    private const int RigId = 1;
    private const float Tolerance = 0.06f;
    // Near the border between chunks (0, 0) and (1, 0) (64 m chunks).
    private static readonly Vector3 RigAt = new(58, 0.2f, 0);

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private SaveStore _store = null!;
    private SaveGame? _save;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _turret, _tip;
    private int _settled;
    private (float Yaw, float Pitch) _held;
    private Vector3 _start;
    private readonly HashSet<ChunkCoord> _crossed = [];
    private readonly List<Transform3D> _parked = [];

    protected override string Tag => "turretseat";

    protected override int StageTimeoutFrames => 1500;

    private static string VanillaDir => Path.Combine(ModPaths.ModsRoot, "vanilla");

    private Creation HostRig => _host.Main.Creations.Find(RigId)!;

    private JointLink Link => HostRig.Joints.Single();

    private Creations.CreationNode HostNode => _host.Main.Creations.NodeOf(RigId)!;

    public override void _Ready()
    {
        var folder = ProjectSettings.GlobalizePath("user://test_saves_turretseat");
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        _store = new SaveStore(folder);
        Add("a host and a client start on vanilla in Creative", frame =>
        {
            if (frame == 0)
            {
                _mods = ModLoader.Load([VanillaDir]);
                Check(!_mods.HasErrors, "vanilla loads");
                _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
                AddChild(_harness);
                Start();
            }
            return frame > 0 && _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null;
        });
        Add("the host spawns a deck with a Turret Seat and a barrel on it, and the client walks up", frame =>
        {
            if (frame == 0)
            {
                Spawn();
                _host.Main.PlayerSync.HostTeleport(_client.Peer, RigAt + new Vector3(0.4f, 0.2f, 1.6f));
            }
            return frame >= 30 && _client.Main.Creations.NodeOf(RigId) is not null;
        });
        Wait("the deck settles", 90);
        Add("the barrel is its own body on the turret's aimed joint, on both sides", _ =>
        {
            Check(HostRig.Bodies.Count == 2 && Link.Connection.Type == ConnectionType.Aimed && Link.Connection.A == _turret,
                $"the turret joins the deck to the barrel by an aimed joint ({HostRig.Bodies.Count} bodies)");
            Check(HostNode.AimJointsOf(Link.Connection) is not null, "the host built the turret's yaw and pitch hinges");
            Check(_client.Main.Creations.NodeOf(RigId)!.Creation.Joints.Single().Connection.Type == ConnectionType.Aimed,
                "the client has the same joint");
            var at = HostNode.AimPosition(Link);
            Check(at is { } a && MathF.Abs(a.Yaw) < Tolerance && MathF.Abs(a.Pitch) < Tolerance, $"it stands straight ahead ({at})");
            return true;
        });
        Add("the client sits in the Turret Seat", frame =>
        {
            if (frame == 0)
                Request(LogicActions.EnterSeat, new LogicPartArgs(RigId, _turret));
            if (_results.Count < 1 || !_client.Main.LocalPlayer!.Seated)
                return false;
            Check(_results[0].Ok, $"sitting down succeeds ({_results[0]})");
            Check(_host.Main.Logic.Seats.Occupant(RigId, _turret) == _client.Peer, "the host seats the client");
            _results.Clear();
            return true;
        });
        Aim("left and up", 0.7f, 0.25f, 0.7f, 0.25f);
        Aim("right and down", -0.5f, -0.08f, -0.5f, -0.08f);
        // Level 1: ±90° of yaw, 8° down and 30° up.
        Aim("past the left and upper stops", 2.4f, 1.2f, MathF.PI / 2, 0.5236f);
        Aim("past the right and lower stops", -2.4f, -1.2f, -MathF.PI / 2, -0.1396f);
        Aim("forward again", 0.3f, 0.1f, 0.3f, 0.1f);
        AddDriveStages();
        AddParkStages();
        AddSaveStages();
    }

    // The client looks toward (yaw, pitch) in the world; the turret turns there (or to its stops) on the host, the
    // client sees it there, and the client's player rides the shell.
    private void Aim(string what, float lookYaw, float lookPitch, float yaw, float pitch)
    {
        Add($"the client looks {what} and the turret follows", frame =>
        {
            Look(lookYaw, lookPitch);
            var at = HostNode.AimPosition(Link);
            var there = at is { } a && MathF.Abs(a.Yaw - yaw) < Tolerance && MathF.Abs(a.Pitch - pitch) < Tolerance;
            // It gets there, then holds there for a second.
            _settled = there ? _settled + 1 : 0;
            if (_settled < 60 && frame < 900)
                return false;
            Check(there, $"the host's turret turned to yaw {yaw:F3}, pitch {pitch:F3} and holds there (at {at})");
            ExpectBarrel(yaw, pitch);
            _settled = 0;
            return true;
        });
        Add($"the client sees the turret turned {what}", frame =>
        {
            Look(lookYaw, lookPitch);
            if (frame < 20)
                return false;
            var host = HostNode.AimPosition(Link)!.Value;
            var node = _client.Main.Creations.NodeOf(RigId)!;
            var link = node.Creation.Joints.Single();
            var seen = JointMath.Aim(FrameOf(node.Creation, link), node.Bodies[link.BodyA].GlobalTransform.ToPose(), node.Bodies[link.BodyB].GlobalTransform.ToPose());
            Check(MathF.Abs(seen.Yaw - host.Yaw) < Tolerance && MathF.Abs(seen.Pitch - host.Pitch) < Tolerance,
                $"the client's barrel is where the host's is ({seen} vs {host})");
            foreach (var (side, n) in new[] { ("host", HostNode), ("client", node) })
            {
                var shown = n.AimShown(_turret);
                Check(shown is { } s && MathF.Abs(s.Yaw - host.Yaw) < Tolerance && MathF.Abs(s.Pitch - host.Pitch) < Tolerance,
                    $"the {side} draws the shell turned with it ({shown})");
            }
            var feet = _host.Main.Logic.SeatPose(RigId, _turret)!.Value.Feet;
            Check(HostPlayer().GlobalPosition.DistanceTo(feet) < 0.1f, "the client's player sits on the turned shell");
            Check(_client.Main.LocalPlayer!.GlobalPosition.DistanceTo(HostPlayer().GlobalPosition) < 0.15f,
                "and the client has it there too");
            return true;
        });
    }

    private void AddDriveStages()
    {
        Add("the deck is pushed across the chunk border with the client aiming", frame =>
        {
            Look(0.3f, 0.1f);
            var deck = HostNode.Bodies[Link.BodyA];
            if (frame == 0)
            {
                _start = deck.GlobalPosition;
                _crossed.Clear();
            }
            var mass = HostNode.Bodies.Sum(b => b.Mass);
            if (frame < 300)
                deck.ApplyCentralForce(new Vector3(mass * (5f * (4f - deck.LinearVelocity.X) + 0.6f * 9.8f), 0, 0));
            ExpectWhole("while it is driven");
            _crossed.Add(_host.Main.Streamer.ChunkAt(deck.GlobalPosition));
            if (frame < 420)
                return false;
            Check(deck.GlobalPosition.X - _start.X > 10f, $"the deck went over 10 m (moved {deck.GlobalPosition - _start})");
            Check(_crossed.Contains(new ChunkCoord(0, 0)) && _crossed.Contains(new ChunkCoord(1, 0)),
                $"it crossed from chunk (0, 0) into (1, 0) ({string.Join(" ", _crossed)})");
            var at = HostNode.AimPosition(Link)!.Value;
            var aim = Expected(0.3f, 0.1f);
            Check(MathF.Abs(Wrap(at.Yaw - aim.Yaw)) < Tolerance && MathF.Abs(at.Pitch - aim.Pitch) < Tolerance,
                $"the turret is still on target after the crossing ({at} for {aim})");
            Check(Pivot(HostNode.Yokes.Single()).DistanceTo(Pivot(HostNode.Bodies[Link.BodyA])) < 0.05f,
                "its yoke is still at the pivot");
            return true;
        });
    }

    private void AddParkStages()
    {
        Add("the turret parks and resumes without a jump", frame =>
        {
            Look(0.3f, 0.1f);
            if (frame == 0)
            {
                _held = HostNode.AimPosition(Link)!.Value;
                _parked.Clear();
                _parked.AddRange(HostNode.Bodies.Select(b => b.GlobalTransform));
                _host.Main.Creations.Park(RigId);
                Check(_host.Main.Creations.IsParked(RigId), "it parks");
                _host.Main.Creations.Resume(RigId);
                var node = HostNode;
                for (var i = 0; i < node.Bodies.Count; i++)
                    Check(node.Bodies[i].GlobalTransform.Origin.DistanceTo(_parked[i].Origin) < 1e-3f, $"body {i} resumes where it parked");
                Check(Pivot(node.Yokes.Single()).DistanceTo(Pivot(node.Bodies[Link.BodyA])) < 1e-3f, "the yoke is rebuilt at the pivot");
                var at = node.AimPosition(Link)!.Value;
                Check(MathF.Abs(at.Yaw - _held.Yaw) < 1e-3f && MathF.Abs(at.Pitch - _held.Pitch) < 1e-3f, $"still turned ({at})");
                return false;
            }
            ExpectWhole("after resuming");
            if (frame < 90)
                return false;
            var now = HostNode.AimPosition(Link)!.Value;
            Check(MathF.Abs(now.Yaw - _held.Yaw) < Tolerance && MathF.Abs(now.Pitch - _held.Pitch) < Tolerance,
                $"and it holds its aim ({now} for {_held})");
            return true;
        });
    }

    private void AddSaveStages()
    {
        Add("the client stands up and the host saves the world", frame =>
        {
            if (frame == 0)
                Request(LogicActions.LeaveSeat, new NoArgs());
            if (_results.Count < 1 || _client.Main.LocalPlayer!.Seated || frame < 60)
                return false;
            Check(_results[0].Ok, $"standing up succeeds ({_results[0]})");
            _results.Clear();
            _held = HostNode.AimPosition(Link)!.Value;
            Check(MathF.Abs(_held.Yaw) > 0.15f, $"the turret stays turned when its gunner leaves ({_held})");
            _host.Main.Saves.Store = _store;
            var path = _host.Main.Saves.SaveNow("Turret World");
            var diagnostics = new List<DataDiagnostic>();
            _save = path is null ? null : SaveStore.Read(path, diagnostics);
            Check(_save is { Creations.Count: 1 } && diagnostics.Count == 0, $"the save is written and reads back ({path})");
            return true;
        });
        Add("both stop and a new host loads the save", frame =>
        {
            if (frame == 0)
            {
                _harness.Stop(_client);
                _harness.Stop(_host);
                _harness.Save = _save;
                Start();
            }
            if (_host.Main.PlayerSync.Players.Count != 2 || _host.Main.Creations.NodeOf(RigId) is null
                || _client.Main.Creations.NodeOf(RigId) is null || frame < 60)
                return false;
            Check(HostRig.Bodies.Count == 2 && HostNode.AimJointsOf(Link.Connection) is not null, "the turret loads with its joint");
            var at = HostNode.AimPosition(Link)!.Value;
            Check(MathF.Abs(at.Yaw - _held.Yaw) < Tolerance && MathF.Abs(at.Pitch - _held.Pitch) < Tolerance,
                $"turned as it was saved ({at} for {_held})");
            ExpectWhole("after loading");
            return true;
        });
        Wait("the loaded turret stands", 120);
        Add("the loaded turret holds its aim", _ =>
        {
            var at = HostNode.AimPosition(Link)!.Value;
            Check(MathF.Abs(at.Yaw - _held.Yaw) < Tolerance && MathF.Abs(at.Pitch - _held.Pitch) < Tolerance,
                $"it still points where it was saved ({at} for {_held})");
            ExpectWhole("standing loaded");
            return true;
        });
    }

    // The deck, the turret and the barrel stay one piece: the barrel's pivot is the deck's.
    private void ExpectWhole(string when)
    {
        var node = HostNode;
        var (deck, barrel) = (node.Bodies[Link.BodyA], node.Bodies[Link.BodyB]);
        var frame = FrameOf(HostRig, Link);
        var pivot = frame.Aim!.Pivot.ToGodot();
        var gap = (deck.GlobalTransform * pivot).DistanceTo(barrel.GlobalTransform * pivot);
        if (gap > 0.05f)
            Check(false, $"the turret holds together {when} (gap {gap:F3} m)");
        var at = node.AimPosition(Link)!.Value;
        if (at.Pitch < frame.Aim.PitchMin - Tolerance || at.Pitch > frame.Aim.PitchMax + Tolerance || MathF.Abs(at.Yaw) > frame.Max + Tolerance)
            Check(false, $"the turret stays within its limits {when} ({at})");
    }

    // The barrel's far block is where the turret's pose puts it, turned about the pivot from the deck.
    private void ExpectBarrel(float yaw, float pitch)
    {
        var frame = FrameOf(HostRig, Link);
        var deck = HostNode.Bodies[Link.BodyA].GlobalTransform;
        var barrel = HostNode.Bodies[Link.BodyB].GlobalTransform;
        var tip = PartGeometry.CentreMetres(HostRig.DefinitionOf(_tip), HostRig.FindPart(_tip)!.Cell, GridRotation.Identity).ToGodot();
        var expected = deck * JointMath.AimPose(frame, yaw, pitch).ToTransform() * tip;
        var seen = barrel * tip;
        Check(seen.DistanceTo(expected) < 0.08f, $"the block at the barrel's end swung with it (off by {seen.DistanceTo(expected):F3} m)");
    }

    // Where the seat script aims for a look in the world: the look in the deck's frame, within the level 1 limits.
    private (float Yaw, float Pitch) Expected(float lookYaw, float lookPitch)
    {
        var deck = HostNode.Bodies[Link.BodyA].GlobalTransform.Basis;
        var world = new Vector3(-MathF.Sin(lookYaw) * MathF.Cos(lookPitch), MathF.Sin(lookPitch), -MathF.Cos(lookYaw) * MathF.Cos(lookPitch));
        var d = deck.Inverse() * world;
        var yaw = Math.Clamp(MathF.Atan2(-d.X, -d.Z), -MathF.PI / 2, MathF.PI / 2);
        var pitch = Math.Clamp(MathF.Atan2(d.Y, MathF.Sqrt(d.X * d.X + d.Z * d.Z)), -0.1396f, 0.5236f);
        return (yaw, pitch);
    }

    private Vector3 Pivot(Node3D deck)
    {
        return deck.GlobalTransform * FrameOf(HostRig, Link).Aim!.Pivot.ToGodot();
    }

    private static JointFrame FrameOf(Creation creation, JointLink link) =>
        creation.TryGetJointFrame(link.Connection, out var frame) && frame is not null ? frame : throw new InvalidOperationException("the turret's joint has no frame");

    private static float Wrap(float angle) => Mathf.Wrap(angle, -MathF.PI, MathF.PI);

    // The client's view, as its mouse would leave it; the host gets it with the client's input.
    private void Look(float yaw, float pitch)
    {
        _client.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Yaw = yaw, Pitch = pitch };
    }

    // A metal deck (x -3..5, z -3..3) with the Turret Seat (4 x 6 x 4) on it facing -Z, and a barrel of five metal blocks
    // on its hardpoint plate running out over its front.
    private void Spawn()
    {
        var rig = new Creation(RigId, _mods);
        for (var x = -3; x <= 5; x++)
            for (var z = -3; z <= 3; z++)
                Place(rig, "vanilla:metal_block", new GridCell(x, 0, z));
        _turret = Place(rig, "vanilla:turret_seat", new GridCell(0, 1, -1));
        for (var z = 0; z >= -4; z--)
            _tip = Place(rig, "vanilla:metal_block", new GridCell(1, 7, z));
        rig.SetAllBodyPoses(new Pose(RigAt.ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Creations.Add(rig, _host.Main.Assets);
    }

    private void Start()
    {
        _host = _harness.StartHost(mods: _mods);
        _client = _harness.StartClient("Client", mods: _mods);
    }

    private static int Place(Creation creation, string part, GridCell cell)
    {
        if (!creation.TryPlacePart(part, cell, GridRotation.Identity, out var placed, out var error))
            throw new InvalidOperationException(error);
        return placed.Id;
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    private PlayerController HostPlayer() => _host.Main.PlayerSync.Players[_client.Peer];
}
