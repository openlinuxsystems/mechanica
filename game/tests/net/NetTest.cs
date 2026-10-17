using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Net;
using Mechanica.Core.Player;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Multiplayer foundation test (chunk 2b): a host and clients in one process over localhost ENet.
/// Checks join and spawn, host-authoritative movement with client prediction, snapshot interpolation
/// on other clients, the Action API round trip and its rejections, bad packets, late join, a full
/// session, and clients and host leaving.
/// </summary>
public partial class NetTest : StagedTest
{
    private const float Tolerance = 0.05f;

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _a = null!;
    private Instance _b = null!;
    private Instance _late = null!;
    private Instance _extra = null!;
    private Vector3 _mark;
    private float _peak;
    private readonly List<ActionResult> _results = [];

    protected override string Tag => "net";

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost(maxPlayers: 4);
        _a = _harness.StartClient("ClientA");
        _b = _harness.StartClient("ClientB");
        GD.Print($"[net] host on port {_harness.Port}");

        Add("everyone joins and sees three players", _ =>
        {
            if (!AllSee(3, _host, _a, _b))
                return false;
            Check(_a.Main.Session.State == NetSession.SessionState.Connected, "client A is connected");
            Check(_host.Main.Session.ReadyPeers.Count == 2, "host has two ready peers");
            var spots = _host.Main.PlayerSync.Players.Values.Select(p => p.GlobalPosition).ToList();
            Check(spots.Distinct().Count() == 3, $"players spawn on distinct spots ({string.Join(", ", spots)})");
            Check(_a.Main.LocalPlayer!.Simulated && !_a.Main.PlayerSync.Players[_b.Peer].Simulated,
                "a client simulates only its own player");
            Check(_a.Main.GetNode<RigidBody3D>("Crate1").Freeze && !_host.Main.GetNode<RigidBody3D>("Crate1").Freeze,
                "props are frozen on clients and simulated on the host");
            return true;
        });
        Wait("players settle", 30);
        Add("client A walks forward", frame => Walk(_a, new PlayerInput(0, 1, 0, 0, false, false), frame, 60));
        Wait("snapshots catch up", 30);
        Add("everyone agrees where client A is", _ =>
        {
            var moved = PlayerOn(_host, _a).GlobalPosition - _mark;
            Check(moved.Z is < -3f and > -4.2f && Mathf.Abs(moved.X) < 0.05f, $"host moved A along -Z (moved {moved})");
            ExpectSame(_a, "A's own prediction");
            ExpectSame(_a, "B's view", _b);
            var queue = _host.Main.PlayerSync.InputQueueOf(_a.Peer)!;
            GD.Print($"[net] client A: {_a.Main.PlayerSync.Corrections} corrections, host dropped {queue.Dropped} and stalled {queue.Stalled} ticks");
            return true;
        });
        Add("host turns and sprints", frame => Walk(_host, new PlayerInput(0, 1, -Mathf.Pi / 2, 0.3f, false, true), frame, 45));
        Wait("snapshots catch up", 30);
        Add("clients see the host move", _ =>
        {
            var moved = PlayerOn(_host, _host).GlobalPosition - _mark;
            Check(moved.X > 3f, $"host walked along +X (moved {moved})");
            ExpectSame(_host, "A's view", _a);
            ExpectSame(_host, "B's view", _b);
            var seen = PlayerOn(_a, _host);
            Check(Mathf.IsEqualApprox(seen.Rotation.Y, -Mathf.Pi / 2, 1e-3f) && Mathf.IsEqualApprox(seen.Head.Rotation.X, 0.3f, 1e-3f),
                "clients see the host's yaw and pitch");
            return true;
        });
        Add("client B jumps and A sees it", frame =>
        {
            var b = _b.Main.LocalPlayer!;
            b.CurrentInput = PlayerInput.None with { Jump = frame < 2 };
            _peak = Mathf.Max(_peak, PlayerOn(_a, _b).GlobalPosition.Y);
            if (frame < 90)
                return false;
            Check(_peak > 0.8f, $"A saw B jump (peak {_peak:F2} m)");
            return true;
        });
        Add("client A walks on with a lost packet", frame =>
        {
            // Sideways for 20 ticks while two input packets are lost; the next packet repeats their inputs.
            var player = _a.Main.LocalPlayer!;
            if (frame == 5)
                _a.Main.PlayerSync.DropInputPackets = 2;
            player.CurrentInput = frame < 20 ? new PlayerInput(1, 0, 0, 0, false, false) : PlayerInput.None;
            return frame >= 60;
        });
        Add("A still agrees with the host", _ =>
        {
            ExpectSame(_a, "A's own prediction");
            Check(_a.Main.PlayerSync.Corrections < 10, $"A needed few corrections ({_a.Main.PlayerSync.Corrections})");
            return true;
        });

        AddActionStages();
        AddRobustnessStages();
        AddMembershipStages();
    }

    private void AddActionStages()
    {
        Add("client A respawns through the Action API", frame =>
        {
            if (frame == 0)
                _a.Main.Actions.Request("Respawn", new NoArgs(), _results.Add);
            if (_results.Count == 0)
                return false;
            Check(_results[0].Ok, $"Respawn succeeds ({_results[0]})");
            return true;
        });
        Wait("correction arrives", 20);
        Add("A is back on its spawn spot everywhere", _ =>
        {
            var spawn = _host.Main.GetNode<Node3D>("SpawnPoint").GlobalPosition + PlayerSlots.SpawnOffset(1).ToGodot();
            var onHost = PlayerOn(_host, _a).GlobalPosition;
            Check(onHost.DistanceTo(spawn) < Tolerance, $"host put A on its spawn spot ({onHost} vs {spawn})");
            ExpectSame(_a, "A's own prediction after the teleport");
            ExpectSame(_a, "B's view after the teleport", _b);
            return true;
        });
        Add("bad requests are rejected with codes", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _a.Main.Actions.RequestJson("Teleport", "{}", _results.Add);
                _a.Main.Actions.RequestJson("Respawn", """{"x": 1}""", _results.Add);
                _a.Main.Actions.RequestJson("Respawn", "not json", _results.Add);
                _host.Main.Actions.Request("Respawn", new NoArgs(), _results.Add);
            }
            if (_results.Count < 4)
                return false;
            // The host's own call answers synchronously, so it is first.
            Check(_results[0].Ok, $"the host's own Respawn succeeds ({_results[0]})");
            Check(_results[1].Code == ActionErrors.UnknownAction, $"unknown action ({_results[1]})");
            Check(_results[2].Code == ActionErrors.BadArgs, $"unexpected argument ({_results[2]})");
            Check(_results[3].Code == ActionErrors.BadArgs, $"malformed JSON ({_results[3]})");
            return true;
        });
    }

    private void AddRobustnessStages()
    {
        Add("forged and malformed packets are ignored", frame =>
        {
            if (frame == 0)
            {
                GD.Print("[net] sending forged packets; the engine logs two RPC errors for them");
                var sync = _b.Main.PlayerSync;
                sync.RpcId(1, "SubmitInputs", new byte[] { 1, 2, 3 });
                sync.RpcId(1, "SubmitInputs", Array.Empty<byte>());
                sync.RpcId(1, "ClientSpawn", 999, Vector3.Zero, 0f); // host-only RPC from a client
                sync.RpcId(1, "ReceiveSnapshot", new WorldSnapshot(1, []).Encode());
                _b.Main.Session.RpcId(1, "Hello", 99, "Mechanica", "{}"); // repeat handshake
                var inputs = InputPacket.Encode([new SequencedInput(uint.MaxValue - 1, new PlayerInput(float.NaN, 1e9f, float.PositiveInfinity, 50, true, true))]);
                sync.RpcId(1, "SubmitInputs", inputs);
            }
            if (frame < 30)
                return false;
            Check(_host.Main.PlayerSync.Players.Count == 3 && _host.Main.Session.ReadyPeers.Count == 2,
                "host still has the same three players");
            var b = PlayerOn(_host, _b);
            Check(float.IsFinite(b.GlobalPosition.X) && b.Velocity.Length() <= 7.01f,
                $"hostile input is sanitized (v={b.Velocity.Length():F2} m/s)");
            return true;
        });
        Add("client B still moves normally afterwards", frame => Walk(_b, new PlayerInput(0, -1, 0, 0, false, false), frame, 30));
        Wait("snapshots catch up", 30);
        Add("everyone agrees where client B is", _ =>
        {
            var moved = PlayerOn(_host, _b).GlobalPosition - _mark;
            Check(moved.Z > 1.5f, $"host moved B backwards along +Z (moved {moved})");
            ExpectSame(_b, "B's own prediction");
            ExpectSame(_b, "A's view of B", _a);
            return true;
        });
    }

    private void AddMembershipStages()
    {
        Add("a client with different mods is turned away with the list", frame =>
        {
            if (frame == 0)
            {
                _host.Main.Session.Content = new ContentManifest([new ModStamp("vanilla", "1.0.0"), new ModStamp("cars", "2.0.0")], "a");
                _extra = _harness.StartClient("ClientModded", new ContentManifest([new ModStamp("vanilla", "1.0.0"), new ModStamp("cars", "1.0.0")], "b"));
            }
            if (frame < 10 || _extra.Main.Session.State != NetSession.SessionState.Offline)
                return false;
            var message = _extra.Main.Session.LastMessage;
            Check(message.Contains("cars: host has 2.0.0, you have 1.0.0"), $"the refusal names the mod ({message})");
            Check(_host.Main.Session.ReadyPeers.Count == 2, "host still has two clients");
            _harness.Stop(_extra);
            _host.Main.Session.Content = ContentManifest.Empty;
            return true;
        });
        Add("a late joiner sees everyone where they are", frame =>
        {
            if (frame == 0)
                _late = _harness.StartClient("ClientLate");
            if (!AllSee(4, _host, _a, _b, _late) || frame < 30)
                return false;
            ExpectSame(_a, "late joiner's view of A", _late);
            ExpectSame(_host, "late joiner's view of the host", _late);
            return true;
        });
        Add("a fifth player is turned away", frame =>
        {
            if (frame == 0)
                _extra = _harness.StartClient("ClientExtra");
            if (frame < 10 || _extra.Main.Session.State != NetSession.SessionState.Offline)
                return false;
            Check(_host.Main.Session.ReadyPeers.Count == 3, "host still has three clients");
            Check(_extra.Main.PlayerSync.Players.Count == 1 && _extra.Main.LocalPlayer is not null,
                $"the refused instance falls back to single player ({_extra.Main.Session.LastMessage})");
            _harness.Stop(_extra);
            return true;
        });
        Add("client B leaves", frame =>
        {
            if (frame == 0)
                _harness.Stop(_b);
            return AllSee(3, _host, _a, _late);
        });
        Add("the host leaves and clients fall back to single player", frame =>
        {
            if (frame == 0)
                _host.Main.Session.Leave();
            if (_a.Main.Session.State != NetSession.SessionState.Offline || _late.Main.Session.State != NetSession.SessionState.Offline)
                return false;
            Check(AllSee(1, _host, _a, _late), "every instance is left with only its own player");
            Check(!_a.Main.GetNode<RigidBody3D>("Crate1").Freeze, "props simulate again offline");
            return true;
        });
    }

    private static PlayerController PlayerOn(Instance viewer, Instance owner) => viewer.Main.PlayerSync.Players[owner.Peer];

    private static bool AllSee(int count, params Instance[] instances) =>
        instances.All(i => i.Main.PlayerSync.Players.Count == count && i.Main.LocalPlayer is not null);

    private bool Walk(Instance who, PlayerInput input, int frame, int frames)
    {
        var player = who.Main.LocalPlayer!;
        if (frame == 0)
            _mark = PlayerOn(_host, who).GlobalPosition;
        player.CurrentInput = frame < frames ? input : PlayerInput.None with { Yaw = input.Yaw, Pitch = input.Pitch };
        return frame >= frames;
    }

    /// <summary>Checks that <paramref name="viewer"/> shows <paramref name="owner"/>'s player where the host has it.</summary>
    private void ExpectSame(Instance owner, string what, Instance? viewer = null)
    {
        var truth = PlayerOn(_host, owner).GlobalPosition;
        var seen = PlayerOn(viewer ?? owner, owner).GlobalPosition;
        Check(truth.DistanceTo(seen) < Tolerance, $"{what} matches the host (off by {truth.DistanceTo(seen):F3} m)");
    }
}
