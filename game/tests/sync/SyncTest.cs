using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Net;
using Mechanica.Game.Creations;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Creation state sync test (chunk 4b): a host and three clients over localhost ENet. A tumbling machine
/// with a bearing falls onto the ground on the host; two clients watch from the start and a third joins
/// mid-fall. Every client must show each Body exactly where the host's snapshots put it, all the way
/// down, and end where the host's machine rests. Then: a far Creation reaches only the client whose
/// player walks up to it, a parked Creation disappears from clients and comes back where it was, and a
/// graph change reaches everyone. This is also chunk 4's two-instance check: the machine is built,
/// falls and collides on the host while clients are connected.
/// </summary>
public partial class SyncTest : StagedTest
{
    private const string Mod = "runtime_test";
    private const int Machine = 1;
    private const int Far = 2;
    // Bearing anchor in the machine's grid frame (the rotor face of the bearing).
    private static readonly Vector3 Hinge = new(0, 0.375f, 0);

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _a = null!;
    private Instance _b = null!;
    private Instance _late = null!;
    private ModLoadResult _content = null!;
    private int _rotorBlock;
    private readonly SortedDictionary<uint, Transform3D[]> _history = [];
    private readonly Dictionary<Instance, (float Position, float Angle, int Samples)> _errors = [];
    private readonly Dictionary<Instance, uint> _firstSeen = [];
    private float _truthError;
    private float _lateJoinSpeed = -1f;
    private List<Transform3D> _parkedAt = [];

    protected override string Tag => "sync";

    protected override int StageTimeoutFrames => 900;

    private CreationWorld HostCreations => _host.Main.Creations;

    private IEnumerable<Instance> Clients => new[] { _a, _b, _late }.Where(c => c is not null);

    public override void _Ready()
    {
        _content = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/creation/mods"));
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost(maxPlayers: 4, mods: _content);
        _a = _harness.StartClient("ClientA", mods: _content);
        _b = _harness.StartClient("ClientB", mods: _content);
        // One body per packet, so every snapshot is split across packets and reassembled.
        var sync = _host.Main.CreationSync;
        sync.Settings = sync.Settings! with { MaxPacketBytes = BodySnapshot.MinPacketBytes };

        Add("host and two clients join", _ =>
            _host.Main.PlayerSync.Players.Count == 3 && _a.Main.LocalPlayer is not null && _b.Main.LocalPlayer is not null);
        Wait("players settle", 20);
        AddFallStages();
        AddInterestStages();
        AddParkStages();
        AddGraphChangeStages();
    }

    private void AddFallStages()
    {
        Add("spawn a tumbling machine on the host", _ =>
        {
            // A row of blocks, heavy at one end, with a bearing on top carrying one block on its rotor.
            var machine = new Creation(Machine, _content);
            Place(machine, "block", new GridCell(-1, 0, 0));
            Place(machine, "block", GridCell.Zero);
            Place(machine, "block", new GridCell(1, 0, 0));
            Place(machine, "heavy_block", new GridCell(2, 0, 0));
            Place(machine, "bearing", new GridCell(0, 1, 0));
            _rotorBlock = Place(machine, "block", new GridCell(0, 2, 0));
            var tilt = new Basis(new Vector3(1, 0, 1).Normalized(), 0.6f);
            machine.SetAllBodyPoses(new Transform3D(tilt, new Vector3(3, 6, -2)).ToPose());
            var node = HostCreations.Add(machine, _host.Main.Assets);
            foreach (var body in node.Bodies)
            {
                body.LinearVelocity = new Vector3(0.5f, 2f, 0);
                body.AngularVelocity = new Vector3(1.5f, 0.5f, -2f);
            }
            node.Bodies[1].AngularVelocity += new Vector3(0, 6f, 0);
            Check(node.Bodies.Count == 2 && node.Joints is [HingeJoint3D], "machine has two bodies joined by a hinge");
            return true;
        });
        Add("it falls identically everywhere while a third client joins", frame =>
        {
            Record();
            if (frame == 20)
                _late = _harness.StartClient("ClientLate", mods: _content);
            if (_late is not null && _lateJoinSpeed < 0 && Remote(_late, Machine) is not null)
                _lateJoinSpeed = HostBodies(Machine).Max(b => b.LinearVelocity.Length());
            foreach (var client in Clients)
                Compare(client);
            return frame > 60 && HostBodies(Machine).All(b => b.Sleeping);
        });
        Wait("clients catch up", 20);
        Add("every client agrees with the host", _ =>
        {
            Check(_host.Main.Session.ReadyPeers.Count == 3, "three clients are connected");
            var main = HostBodies(Machine)[0].GlobalTransform;
            Check(main.Origin.Y is > 0.1f and < 0.8f, $"the machine comes to rest on the ground on the host (y={main.Origin.Y:F3})");
            var gap = (main * Hinge).DistanceTo(HostBodies(Machine)[1].GlobalTransform * Hinge);
            Check(gap < 0.02f, $"the hinge holds on the host (gap {gap:F4} m)");
            Check(_lateJoinSpeed > 1f, $"the late client joined while the machine was moving ({_lateJoinSpeed:F2} m/s)");
            foreach (var client in Clients)
            {
                var (position, angle, samples) = _errors.GetValueOrDefault(client);
                Check(samples > 30 && position < 0.005f && angle < 0.5f,
                    $"{client} showed the host's snapshots all the way down ({samples} ticks, off by at most {position * 1000:F2} mm, {angle:F3}°)");
                var node = Remote(client, Machine)!;
                Check(node.Bodies.All(b => b.Freeze) && !node.IsSimulated, $"{client} does not simulate the machine");
                for (var i = 0; i < node.Bodies.Count; i++)
                    ExpectSame(HostBodies(Machine)[i].GlobalTransform, node.Bodies[i].GlobalTransform, $"{client} rests body {i} where the host does");
            }
            GD.Print($"[sync] largest gap to the host's per-tick truth: {_truthError * 1000:F1} mm; " +
                $"host sent {_host.Main.CreationSync.PacketsSent} body packets and {_host.Main.CreationSync.FullSyncsSent} full syncs; " +
                $"clients received {string.Join(", ", Clients.Select(c => $"{c} {c.Main.CreationSync.PacketsReceived}"))}");
            return true;
        });
    }

    private void AddInterestStages()
    {
        Add("a far creation reaches nobody", frame =>
        {
            if (frame == 0)
            {
                var far = new Creation(Far, _content);
                Place(far, "block", GridCell.Zero);
                Place(far, "block", new GridCell(1, 0, 0));
                far.SetAllBodyPoses(new Transform3D(Basis.Identity, new Vector3(300, 0.13f, 0)).ToPose());
                HostCreations.Add(far, _host.Main.Assets);
                _host.Main.Streamer.Step(0f);
            }
            if (frame < 20)
                return false;
            Check(Clients.All(c => Remote(c, Far) is null), "no client receives a creation 300 m from every player");
            return true;
        });
        Add("client A's player walks up to it", frame =>
        {
            if (frame == 0)
                PlayerOnHost(_a).ApplyState(new Vector3(290, 0.05f, 0), Vector3.Zero, 0f, 0f);
            if (Remote(_a, Far) is not { } node)
                return false;
            Check(Remote(_b, Far) is null && Remote(_late, Far) is null, "only client A receives it");
            Check(_host.Main.CreationSync.InterestOf(_a.Peer).Contains(Far), "the host counts it in A's interest");
            ExpectSame(HostBodies(Far)[0].GlobalTransform, node.Bodies[0].GlobalTransform, "A shows it where the host has it");
            return true;
        });
        Add("client A walks home and drops it", frame =>
        {
            if (frame == 0)
                Check(_host.Main.PlayerSync.HostRespawn(_a.Peer), "the host puts A back on its spawn spot");
            return Remote(_a, Far) is null && Remote(_a, Machine) is not null;
        });
    }

    private void AddParkStages()
    {
        Add("the host parks the machine and clients hide it", frame =>
        {
            if (frame == 0)
            {
                HostCreations.Park(Machine);
                var parked = HostCreations.Find(Machine)!;
                _parkedAt = [.. Enumerable.Range(0, parked.Bodies.Count).Select(i => parked.BodyPose(i).ToTransform())];
            }
            if (Clients.Any(c => Remote(c, Machine) is not null))
                return false;
            Check(Clients.All(c => !c.Main.CreationSync.RemoteCreations.Contains(Machine)), "every client dropped the parked machine");
            return true;
        });
        Add("the host resumes it and clients show it where it parked", frame =>
        {
            if (frame == 0)
                HostCreations.Resume(Machine);
            if (Clients.Any(c => Remote(c, Machine) is null))
                return false;
            // The full sync carries the host's state a tick or two after the resume, so allow for that much motion.
            foreach (var client in Clients)
            {
                var node = Remote(client, Machine)!;
                for (var i = 0; i < node.Bodies.Count; i++)
                    ExpectSame(_parkedAt[i], node.Bodies[i].GlobalTransform, $"{client} shows body {i} where it parked", 5e-3f, 1f);
            }
            return true;
        });
        Wait("the machine settles again", 60);
        Add("clients follow it after the resume", _ =>
        {
            foreach (var client in Clients)
            {
                var node = Remote(client, Machine)!;
                for (var i = 0; i < node.Bodies.Count; i++)
                    ExpectSame(HostBodies(Machine)[i].GlobalTransform, node.Bodies[i].GlobalTransform, $"{client} shows body {i} where the host has it");
            }
            return true;
        });
    }

    private void AddGraphChangeStages()
    {
        Add("removing the rotor block reaches every client", frame =>
        {
            if (frame == 0)
            {
                var node = HostCreations.NodeOf(Machine)!;
                Check(node.Creation.RemovePart(_rotorBlock, () => 99).SplitOff.Count == 0, "removing the rotor block splits nothing");
                node.Rebuild();
            }
            return frame >= 20 && Clients.All(c => Remote(c, Machine) is { Bodies.Count: 1 }) && HostBodies(Machine).All(b => b.Sleeping);
        });
        Wait("clients catch up", 20);
        Add("every client has the changed machine where the host has it", _ =>
        {
            foreach (var client in Clients)
            {
                Check(Remote(client, Machine)!.Creation.Parts.Count == 5, $"{client} has the machine without its rotor block");
                ExpectSame(HostBodies(Machine)[0].GlobalTransform, Remote(client, Machine)!.Bodies[0].GlobalTransform, $"{client} shows it where the host has it");
            }
            return true;
        });
        var leaver = 0;
        Add("a client leaving drops its copies", frame =>
        {
            if (frame == 0)
            {
                leaver = _b.Peer;
                Check(_host.Main.CreationSync.InterestOf(leaver).Contains(Machine), "the host has the machine in B's interest");
                _b.Main.Session.Leave();
            }
            if (_b.Main.Creations.All.Any() || _host.Main.Session.ReadyPeers.Contains(leaver))
                return false;
            Check(HostCreations.Find(Machine) is not null, "the host keeps its machine");
            Check(_host.Main.CreationSync.InterestOf(leaver).Count == 0, "the host forgets the leaver's interest");
            return true;
        });
    }

    /// <summary>Remembers the host's Body transforms at its current sync tick.</summary>
    private void Record()
    {
        if (HostCreations.NodeOf(Machine) is { } node)
            _history[_host.Main.CreationSync.Tick] = [.. node.Bodies.Select(b => b.GlobalTransform)];
    }

    /// <summary>
    /// Checks a client against what a lossless client would show at its render tick: the host's states at
    /// the snapshot ticks either side, blended as <see cref="BodyTrack"/> does. Ticks where the client has
    /// not received the later snapshot yet are skipped.
    /// </summary>
    private void Compare(Instance client)
    {
        if (Remote(client, Machine) is not { } node || client.Main.CreationSync.RenderTick is not { } tick)
            return;
        // A client cannot show what happened before it got the Creation; it holds the first state until then.
        if (!_firstSeen.TryGetValue(client, out var first))
            _firstSeen[client] = first = _host.Main.CreationSync.Tick;
        var interval = (uint)NetProtocol.SnapshotInterval;
        var before = (uint)Math.Floor(tick / interval) * interval;
        var after = before + interval;
        if (before < first || !_history.TryGetValue(before, out var a) || !_history.TryGetValue(after, out var b))
            return;
        // Without the later snapshot the client is extrapolating, which a lossless client never does. That
        // happens when a frame stalls in this one-process test (a client starting up) and holds up delivery.
        if (client.Main.CreationSync.Tick < after)
            return;
        var t = (float)((tick - before) / interval);
        var (position, angle, samples) = _errors.GetValueOrDefault(client);
        for (var i = 0; i < node.Bodies.Count; i++)
        {
            var expected = new Transform3D(new Basis(a[i].Basis.GetRotationQuaternion().Slerp(b[i].Basis.GetRotationQuaternion(), t)),
                a[i].Origin.Lerp(b[i].Origin, t));
            var seen = node.Bodies[i].GlobalTransform;
            position = Mathf.Max(position, expected.Origin.DistanceTo(seen.Origin));
            angle = Mathf.Max(angle, Mathf.RadToDeg(expected.Basis.GetRotationQuaternion().AngleTo(seen.Basis.GetRotationQuaternion())));
            var whole = (uint)Math.Round(tick);
            if (Math.Abs(tick - whole) < 0.05 && _history.TryGetValue(whole, out var truth))
                _truthError = Mathf.Max(_truthError, truth[i].Origin.DistanceTo(seen.Origin));
        }
        _errors[client] = (position, angle, samples + 1);
    }

    private void ExpectSame(Transform3D expected, Transform3D seen, string what, float metres = 1e-3f, float degrees = 0.1f)
    {
        var off = expected.Origin.DistanceTo(seen.Origin);
        var turned = Mathf.RadToDeg(expected.Basis.GetRotationQuaternion().AngleTo(seen.Basis.GetRotationQuaternion()));
        Check(off < metres && turned < degrees, $"{what} (off by {off * 1000:F2} mm, {turned:F3}°)");
    }

    private IReadOnlyList<CreationBody> HostBodies(int id) => HostCreations.NodeOf(id)?.Bodies ?? [];

    private static CreationNode? Remote(Instance client, int id) => client.Main.Creations.NodeOf(id);

    private Player.PlayerController PlayerOnHost(Instance owner) => _host.Main.PlayerSync.Players[owner.Peer];

    private int Place(Creation creation, string part, GridCell cell)
    {
        if (creation.TryPlacePart($"{Mod}:{part}", cell, GridRotation.Identity, out var placed, out var error))
            return placed.Id;
        Check(false, $"place {part} at {cell}: {error}");
        return 0;
    }
}
