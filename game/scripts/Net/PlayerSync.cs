using Godot;
using Mechanica.Core.Net;
using Mechanica.Core.Player;
using Mechanica.Core.Remote;
using Mechanica.Game.Player;

namespace Mechanica.Game.Net;

/// <summary>
/// Spawns one <see cref="PlayerController"/> per peer and keeps them in step (D10, host-authoritative).
/// <list type="bullet">
/// <item>The host decides who exists: it spawns a player when a peer becomes ready, tells every ready
/// peer, and sends a late joiner everyone already present.</item>
/// <item>Clients send their own input every tick (<see cref="SubmitInputs"/>). The host simulates every
/// player, stepping remote players with their own queued inputs (<see cref="InputQueue"/>), and broadcasts a <see cref="WorldSnapshot"/>
/// every <see cref="NetProtocol.SnapshotInterval"/> ticks.</item>
/// <item>A client predicts its own player locally and corrects it when the host disagrees, replaying
/// the inputs the host has not seen yet. Other players are posed by interpolating snapshots.</item>
/// <item>AI agents that join through the AI endpoint get host-only players with no network peer
/// (<see cref="HostAddAgent"/>), stepped from <see cref="Agents"/>; clients see them like anyone else.</item>
/// </list>
/// Runs early in the physics frame so remote inputs and corrections land before players move.
/// </summary>
public partial class PlayerSync : Node
{
    /// <summary>Inputs repeated in each packet, so a lost or skipped packet costs nothing.</summary>
    public const int RedundantInputs = 4;

    private readonly Dictionary<int, PlayerController> _players = [];
    private readonly Dictionary<int, InputQueue> _queues = [];
    private readonly Dictionary<int, SnapshotBuffer> _buffers = [];
    private readonly PredictionHistory _history = new();
    private readonly InterpolationClock _clock = new();
    private PlayerSlots _slots = new(NetProtocol.DefaultMaxPlayers);
    private uint _tick;
    private uint _sequence;
    private WorldSnapshot? _pendingSnapshot;
    private uint _newestSnapshotTick;
    private bool _wasHosting;

    /// <summary>The session this syncs over. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Host side: the AI agents playing, whose players step from their held input.</summary>
    public AgentPlayers? Agents { get; set; }

    /// <summary>Where player nodes are added. Set before adding to the tree.</summary>
    public Node3D PlayersRoot { get; init; } = null!;

    /// <summary>Where the water surface is (world Y), or null for a world without water. Given to every player.</summary>
    public Func<float>? WaterLevel { get; init; }

    /// <summary>Spawn point for slot 0; other slots are offset from it. Set before adding to the tree.</summary>
    public Func<Transform3D> SpawnPoint { get; init; } = () => Transform3D.Identity;

    /// <summary>Players by peer id.</summary>
    public IReadOnlyDictionary<int, PlayerController> Players => _players;

    /// <summary>This peer's own player, if spawned.</summary>
    public PlayerController? LocalPlayer => _players.GetValueOrDefault(Session.LocalPeer);

    /// <summary>Client side: how many times the host corrected the local prediction.</summary>
    public int Corrections { get; private set; }

    /// <summary>Host physics ticks so far (host), or the newest snapshot tick seen (client).</summary>
    public uint Tick => Session.IsHost ? _tick : _newestSnapshotTick;

    /// <summary>Host side: the input queue of a remote peer, for diagnostics.</summary>
    public InputQueue? InputQueueOf(int peer) => _queues.GetValueOrDefault(peer);

    /// <summary>Test hook: the next this many outgoing input packets are dropped, as if lost.</summary>
    internal int DropInputPackets { get; set; }

    /// <summary>Raised after a player is added.</summary>
    public event Action<PlayerController>? PlayerSpawned;

    /// <summary>Raised before a player is removed.</summary>
    public event Action<PlayerController>? PlayerDespawning;

    /// <summary>
    /// A player is about to despawn, raised before <see cref="PlayerDespawning"/> while every system still
    /// holds their state (health, inventory), so it can be saved.
    /// </summary>
    public event Action<PlayerController>? PlayerLeaving;

    public override void _Ready()
    {
        ProcessPhysicsPriority = -100;
        Session.PeerReady += HostAddPeer;
        Session.PeerLeft += HostRemovePeer;
        Session.StateChanged += OnStateChanged;
        if (Session.IsHost)
            StartOffline();
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= HostAddPeer;
        Session.PeerLeft -= HostRemovePeer;
        Session.StateChanged -= OnStateChanged;
    }

    /// <summary>
    /// Host side: puts a player back on its spawn spot. Returns false if the peer has no player. The
    /// <c>Respawn</c> action (<see cref="Core.Survival.SurvivalActions"/>) does this for living players.
    /// </summary>
    public bool HostRespawn(int peer) => SpawnSpotOf(peer) is { } spot && HostTeleport(peer, spot);

    /// <summary>Where a peer's player spawns (its slot's spot), or null when it has no slot.</summary>
    public Vector3? SpawnSpotOf(int peer) => _slots.SlotOf(peer) is { } slot ? SpawnTransform(slot).Origin : null;

    /// <summary>
    /// Host side: moves a player's feet to <paramref name="feet"/>, at rest. A client's own prediction
    /// is corrected by the next snapshot. <paramref name="yaw"/> turns them too. Returns false if the peer has no player.
    /// </summary>
    public bool HostTeleport(int peer, Vector3 feet, float? yaw = null)
    {
        if (!Session.IsHost || !_players.TryGetValue(peer, out var player))
            return false;
        player.ApplyState(feet, Vector3.Zero, yaw ?? player.Rotation.Y, player.Head.Rotation.X);
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Session.IsHost)
            HostTick();
        else if (Session.State == NetSession.SessionState.Connected)
            ClientTick((float)delta);
    }

    private void HostTick()
    {
        _tick++;
        if (Session.State == NetSession.SessionState.Hosting && _tick % NetProtocol.SnapshotInterval == 0)
        {
            var packet = BuildSnapshot().Encode();
            foreach (var peer in Session.ReadyPeers)
                RpcId(peer, MethodName.ReceiveSnapshot, packet);
        }
        var delta = (float)GetPhysicsProcessDeltaTime();
        foreach (var (peer, queue) in _queues)
        {
            if (!_players.TryGetValue(peer, out var player))
                continue;
            foreach (var input in queue.NextTick())
                player.Step(input, delta);
        }
        if (Agents is { } agents)
        {
            foreach (var peer in agents.All.Keys)
            {
                if (_players.TryGetValue(peer, out var player))
                    player.Step(agents.NextInput(peer, player.LastInput), delta);
            }
        }
    }

    /// <summary>
    /// Host side: spawns an AI agent's player on a free slot and tells every ready client. Its profile must
    /// already be known to <see cref="Session"/>. Returns an error, or null.
    /// </summary>
    public string? HostAddAgent(int peer)
    {
        if (!Session.IsHost)
            return "Only the host spawns agent players.";
        if (_players.ContainsKey(peer))
            return "That player already exists.";
        if (_slots.Claim(peer) is not { } slot)
            return $"The world is full ({Session.MaxPlayers} players).";
        var spawn = SpawnTransform(slot);
        var player = AddPlayer(peer, spawn);
        player.Simulated = false; // stepped from Agents in HostTick
        PlayerSpawned?.Invoke(player);
        if (Session.State == NetSession.SessionState.Hosting)
        {
            foreach (var other in Session.ReadyPeers)
                RpcId(other, MethodName.ClientSpawn, peer, player.GlobalPosition, player.Rotation.Y, player.DisplayName);
        }
        return null;
    }

    /// <summary>Host side: removes an AI agent's player (it is saved first) and tells every ready client.</summary>
    public void HostRemoveAgent(int peer)
    {
        if (!Session.IsHost || !_players.ContainsKey(peer))
            return;
        _slots.Release(peer);
        RemovePlayer(peer);
        if (Session.State != NetSession.SessionState.Hosting)
            return;
        foreach (var other in Session.ReadyPeers)
            RpcId(other, MethodName.ClientDespawn, peer);
    }

    private WorldSnapshot BuildSnapshot() => new(_tick, [.. _players.Values.Select(p => new PlayerState(
        p.PeerId,
        _queues.GetValueOrDefault(p.PeerId)?.LastApplied ?? 0,
        p.GlobalPosition.ToNumerics(),
        p.Velocity.ToNumerics(),
        p.Rotation.Y,
        p.Head.Rotation.X,
        p.Body,
        p.ShownProp,
        p.ShownGesture.Gesture,
        p.ShownGesture.Count))]);

    private void ClientTick(float delta)
    {
        _clock.Advance();
        if (_pendingSnapshot is { } snapshot)
        {
            _pendingSnapshot = null;
            _clock.OnSnapshot(snapshot.Tick);
            foreach (var state in snapshot.Players)
            {
                if (state.Peer == Session.LocalPeer)
                    Reconcile(state, delta);
                else if (_buffers.TryGetValue(state.Peer, out var buffer))
                    buffer.Add(snapshot.Tick, state);
            }
        }

        if (_clock.RenderTick is not { } renderTick)
            return;
        foreach (var (peer, buffer) in _buffers)
        {
            if (buffer.Sample(renderTick) is { } s && _players.TryGetValue(peer, out var player))
            {
                player.ApplyState(s.Position.ToGodot(), s.Velocity.ToGodot(), s.Yaw, s.Pitch);
                player.ApplyBody(s.Body, s.Prop, s.Gesture, s.GestureCount);
            }
        }
    }

    private void Reconcile(PlayerState state, float delta)
    {
        // A seated player sits where its seat is; there is no walk to correct.
        if (state.AckSequence == 0 || LocalPlayer is not { } player || player.Seated)
            return;
        if (!_history.Reconcile(state.AckSequence, state.Position))
            return;

        Corrections++;
        player.ApplyState(state.Position.ToGodot(), state.Velocity.ToGodot(), player.Rotation.Y, player.Head.Rotation.X);
        foreach (var entry in _history.Pending.ToList())
        {
            player.Step(entry.Input, delta);
            _history.Overwrite(entry.Sequence, player.GlobalPosition.ToNumerics(), player.Velocity.ToNumerics());
        }
    }

    private void OnLocalStepped(PlayerInput input)
    {
        if (Session.State != NetSession.SessionState.Connected || LocalPlayer is not { } player)
            return;
        _history.Record(new PredictionHistory.Entry(++_sequence, input, player.GlobalPosition.ToNumerics(), player.Velocity.ToNumerics()));
        if (DropInputPackets > 0)
        {
            DropInputPackets--;
            return;
        }
        var packet = InputPacket.Encode([.. _history.Newest(RedundantInputs).Select(e => new SequencedInput(e.Sequence, e.Input))]);
        RpcId(NetProtocol.HostPeer, MethodName.SubmitInputs, packet);
    }

    /// <summary>
    /// Client → host: the client's newest inputs, unreliable on their own channel. Each packet repeats
    /// the last few inputs, so a lost one costs nothing and none waits behind a lost one.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable,
        TransferChannel = NetProtocol.InputChannel)]
    private void SubmitInputs(byte[] packet)
    {
        if (Session.State != NetSession.SessionState.Hosting)
            return;
        var peer = Multiplayer.GetRemoteSenderId();
        if (!_queues.TryGetValue(peer, out var queue) || InputPacket.Decode(packet) is not { } inputs)
            return;
        queue.Receive(inputs);
    }

    /// <summary>Host → clients: every player's state.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered,
        TransferChannel = NetProtocol.SnapshotChannel)]
    private void ReceiveSnapshot(byte[] packet)
    {
        if (WorldSnapshot.Decode(packet) is not { } snapshot || snapshot.Tick <= _newestSnapshotTick)
            return;
        _newestSnapshotTick = snapshot.Tick;
        _pendingSnapshot = snapshot;
    }

    /// <summary>Host → client: a player exists.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientSpawn(int peer, Vector3 position, float yaw, string name)
    {
        if (Session.IsHost || _players.ContainsKey(peer))
            return;
        var local = peer == Session.LocalPeer;
        var player = AddPlayer(peer, new Transform3D(new Basis(Vector3.Up, yaw), position));
        player.DisplayName = Core.Saves.PlayerProfile.CleanName(name);
        player.Simulated = local;
        player.PushesBodies = false;
        if (local)
        {
            player.CurrentInput = PlayerInput.None with { Yaw = yaw };
            player.Stepped += OnLocalStepped;
        }
        else
        {
            _buffers[peer] = new SnapshotBuffer();
        }
        PlayerSpawned?.Invoke(player);
    }

    /// <summary>Host → client: a player left.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientDespawn(int peer)
    {
        if (!Session.IsHost)
            RemovePlayer(peer);
    }

    private void HostAddPeer(int peer)
    {
        if (_slots.Claim(peer) is not { } slot)
        {
            GD.PushWarning($"[net] no free player slot for peer {peer}");
            return;
        }
        var spawn = SpawnTransform(slot);
        var player = AddPlayer(peer, spawn);
        player.Simulated = false; // stepped from its input queue in HostTick
        _queues[peer] = new InputQueue();
        PlayerSpawned?.Invoke(player);

        foreach (var other in Session.ReadyPeers.Where(p => p != peer))
            RpcId(other, MethodName.ClientSpawn, peer, spawn.Origin, player.Rotation.Y, player.DisplayName);
        foreach (var existing in _players.Values)
            RpcId(peer, MethodName.ClientSpawn, existing.PeerId, existing.GlobalPosition, existing.Rotation.Y, existing.DisplayName);
    }

    private void HostRemovePeer(int peer)
    {
        _slots.Release(peer);
        _queues.Remove(peer);
        RemovePlayer(peer);
        foreach (var other in Session.ReadyPeers)
            RpcId(other, MethodName.ClientDespawn, peer);
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        switch (state)
        {
            case NetSession.SessionState.Hosting:
                _slots = new PlayerSlots(Session.MaxPlayers);
                foreach (var peer in _players.Keys)
                    _slots.Claim(peer);
                break;
            case NetSession.SessionState.Connecting:
                ClearPlayers();
                break;
            case NetSession.SessionState.Offline when _wasHosting:
                // The host keeps its world, its player and AI agents' players; only the remote players go.
                foreach (var peer in _players.Keys.Where(p => p != NetProtocol.HostPeer && Agents?.IsAgent(p) != true).ToList())
                    HostRemovePeer(peer);
                break;
            case NetSession.SessionState.Offline:
                ClearPlayers();
                StartOffline();
                break;
        }
        _wasHosting = state == NetSession.SessionState.Hosting;
    }

    private void StartOffline()
    {
        _slots = new PlayerSlots(Session.MaxPlayers);
        _slots.Claim(NetProtocol.HostPeer);
        var player = AddPlayer(NetProtocol.HostPeer, SpawnTransform(0));
        PlayerSpawned?.Invoke(player);
    }

    private void ClearPlayers()
    {
        foreach (var peer in _players.Keys.ToList())
            RemovePlayer(peer);
        _queues.Clear();
        _buffers.Clear();
        _history.Clear();
        _pendingSnapshot = null;
        _newestSnapshotTick = 0;
        _sequence = 0;
        _tick = 0;
        Corrections = 0;
    }

    private Transform3D SpawnTransform(int slot)
    {
        var spawn = SpawnPoint();
        return spawn with { Origin = spawn.Origin + spawn.Basis * PlayerSlots.SpawnOffset(slot).ToGodot() };
    }

    private PlayerController AddPlayer(int peer, Transform3D transform)
    {
        var player = new PlayerController { Name = $"Player{peer}", PeerId = peer, WaterLevel = WaterLevel, DisplayName = Session.ProfileOf(peer)?.Name ?? $"Player {peer}" };
        PlayersRoot.AddChild(player);
        player.GlobalTransform = transform;
        player.CurrentInput = PlayerInput.None with { Yaw = player.Rotation.Y };
        _players[peer] = player;
        return player;
    }

    private void RemovePlayer(int peer)
    {
        if (!_players.Remove(peer, out var player))
            return;
        _buffers.Remove(peer);
        PlayerLeaving?.Invoke(player);
        PlayerDespawning?.Invoke(player);
        player.Stepped -= OnLocalStepped;
        PlayersRoot.RemoveChild(player);
        player.QueueFree();
    }
}
