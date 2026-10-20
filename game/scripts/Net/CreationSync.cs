using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Net;
using Mechanica.Core.World;
using Mechanica.Game.Creations;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.Net;

/// <summary>
/// Keeps every peer's view of the Creations in step with their authority (chunk 4b, D10). In v1 the
/// host is the authority for every Creation (D5); the routing goes through
/// <see cref="Creation.AuthorityPeer"/> so client authority can be added later.
/// <list type="bullet">
/// <item>Interest: each tick the host works out which live Creations are near each peer's player
/// (<see cref="CreationInterest"/>). Parked Creations (chunk 12) and ones the peer simulates itself are
/// never sent, so a peer drops a Creation the host parks.</item>
/// <item>Full sync: a Creation entering a peer's interest, or whose graph changed
/// (<see cref="Creation.Revision"/>), is sent whole as save JSON with its poses and velocities
/// (<see cref="CreationJson"/>), reliably. Late join is the same path: a new peer's player is near
/// whatever it should see.</item>
/// <item>Snapshots: every <see cref="NetProtocol.SnapshotInterval"/> ticks the host sends each peer the
/// Body states of its Creations (<see cref="BodySnapshot"/>), unreliable and split into small packets;
/// sleeping Creations go less often. Clients interpolate them (<see cref="BodyTrack"/>) a few ticks
/// behind the newest, which hides lost packets and jitter.</item>
/// </list>
/// Runs after <see cref="PlayerSync"/> moved the players, before physics steps the bodies.
/// </summary>
public partial class CreationSync : Node
{
    /// <summary>Where the tuning is read from.</summary>
    public const string DefaultSettingsPath = "res://data/sync.json";

    private readonly Dictionary<int, Dictionary<int, int>> _sentRevisions = [];
    private readonly Dictionary<int, Remote> _remotes = [];
    private readonly InterpolationClock _clock = new();
    private CreationInterest _interest = null!;
    private uint _tick;
    private uint _newestTick;

    /// <summary>A Creation this client shows from the host's snapshots.</summary>
    private sealed record Remote(int Revision, BodyTrack[] Tracks);

    /// <summary>The session. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players, for interest. Set before adding to the tree.</summary>
    public PlayerSync PlayerSync { get; init; } = null!;

    /// <summary>The Creations to sync. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Resolves part ids in Creations received from the host. Set before adding to the tree.</summary>
    public IPartCatalog Catalog { get; init; } = null!;

    /// <summary>Meshes and shapes for Creations received from the host. Set before adding to the tree.</summary>
    public PartAssets Assets { get; init; } = null!;

    /// <summary>Tuning; null reads <see cref="DefaultSettingsPath"/> when the node starts.</summary>
    public SyncSettings? Settings { get; set; }

    /// <summary>Host physics ticks so far (host), or the newest snapshot tick seen (client).</summary>
    public uint Tick => Session.IsHost ? _tick : _newestTick;

    /// <summary>Client side: the host tick the remote Creations are shown at, or null before the first sync.</summary>
    public double? RenderTick => _clock.RenderTick;

    /// <summary>Host side: Creations a peer receives.</summary>
    public IReadOnlyCollection<int> InterestOf(int peer) => _interest.Of(peer);

    /// <summary>Client side: ids of the Creations shown from the host.</summary>
    public IReadOnlyCollection<int> RemoteCreations => _remotes.Keys;

    /// <summary>Host side: full Creation syncs sent so far.</summary>
    public int FullSyncsSent { get; private set; }

    /// <summary>Host side: body snapshot packets sent so far.</summary>
    public int PacketsSent { get; private set; }

    /// <summary>Client side: body snapshot packets received so far (including stale or ignored ones).</summary>
    public int PacketsReceived { get; private set; }

    /// <summary>Reads the tuning from <see cref="DefaultSettingsPath"/>.</summary>
    public static SyncSettings LoadDefaultSettings()
    {
        using var file = FileAccess.Open(DefaultSettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {DefaultSettingsPath}: {FileAccess.GetOpenError()}");
        return SyncSettings.FromJson(file.GetAsText());
    }

    public override void _Ready()
    {
        ProcessPhysicsPriority = -50;
        Settings ??= LoadDefaultSettings();
        _interest = new CreationInterest(Settings);
        Session.PeerLeft += OnPeerLeft;
        Session.StateChanged += OnStateChanged;
        Creations.Removed += OnCreationRemoved;
    }

    public override void _ExitTree()
    {
        Session.PeerLeft -= OnPeerLeft;
        Session.StateChanged -= OnStateChanged;
        Creations.Removed -= OnCreationRemoved;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Session.State == NetSession.SessionState.Hosting)
            HostTick();
        else if (Session.State == NetSession.SessionState.Connected)
            ClientTick((float)delta);
    }

    private void HostTick()
    {
        _tick++;
        var local = Session.LocalPeer;
        // Only Creations this peer simulates are sent; one a client simulates would be relayed from it (not in v1, D5).
        var live = Creations.All
            .Where(c => c.AuthorityPeer == local && Creations.NodeOf(c.Id) is not null)
            .Select(c => (Creation: c, Candidate: new InterestCandidate(c.Id, Creations.Sample(c.Id).Bounds)))
            .ToList();
        var json = new Dictionary<int, string>();
        var sendSnapshot = _tick % NetProtocol.SnapshotInterval == 0;
        var sleepingEvery = Math.Max(1, (uint)Mathf.RoundToInt(Settings!.SleepingSnapshotInterval * Engine.PhysicsTicksPerSecond));
        var states = new Dictionary<int, CreationBodies?>();

        foreach (var peer in Session.ReadyPeers)
        {
            var player = PlayerSync.Players.GetValueOrDefault(peer)?.GlobalPosition.ToNumerics();
            var change = _interest.Update(peer, player, live.Where(l => l.Creation.AuthorityPeer != peer).Select(l => l.Candidate));
            if (!_sentRevisions.TryGetValue(peer, out var sent))
                _sentRevisions[peer] = sent = [];
            foreach (var id in change.Left)
            {
                sent.Remove(id);
                RpcId(peer, MethodName.ClientForget, id);
            }
            foreach (var (creation, _) in live.Where(l => _interest.Contains(peer, l.Creation.Id)))
            {
                if (sent.TryGetValue(creation.Id, out var revision) && revision == creation.Revision)
                    continue;
                if (!json.TryGetValue(creation.Id, out var text))
                {
                    Creations.NodeOf(creation.Id)!.SyncToCreation();
                    json[creation.Id] = text = CreationJson.Write(creation);
                }
                sent[creation.Id] = creation.Revision;
                RpcId(peer, MethodName.ClientFullSync, creation.Revision, _tick, text);
                FullSyncsSent++;
            }

            if (!sendSnapshot)
                continue;
            var runs = new List<CreationBodies>();
            foreach (var id in sent.Keys.Order())
            {
                if (!states.TryGetValue(id, out var run))
                    states[id] = run = SnapshotOf(id, sleepingEvery);
                if (run is not null)
                    runs.Add(run);
            }
            foreach (var packet in BodySnapshot.Pack(_tick, runs, Settings.MaxPacketBytes))
            {
                RpcId(peer, MethodName.ClientBodies, packet.Encode());
                PacketsSent++;
            }
        }
    }

    /// <summary>The Bodies of a live Creation, or null when it is asleep and not due a keep-alive snapshot.</summary>
    private CreationBodies? SnapshotOf(int id, uint sleepingEvery)
    {
        if (Creations.NodeOf(id) is not { } node || Creations.Find(id) is not { } creation)
            return null;
        if (node.Bodies.All(b => b.Sleeping) && _tick % sleepingEvery >= NetProtocol.SnapshotInterval)
            return null;
        var bodies = node.Bodies.Select(b => new BodyState(
            b.GlobalPosition.ToNumerics(),
            b.GlobalBasis.GetRotationQuaternion().ToNumerics(),
            b.LinearVelocity.ToNumerics(),
            b.AngularVelocity.ToNumerics())).ToList();
        return new CreationBodies(id, (ushort)creation.Revision, 0, bodies);
    }

    private void ClientTick(float delta)
    {
        _clock.Advance();
        if (_clock.RenderTick is not { } tick)
            return;
        var maxAhead = Settings!.MaxExtrapolation / delta;
        foreach (var (id, remote) in _remotes)
        {
            if (Creations.NodeOf(id) is not { } node || node.Bodies.Count != remote.Tracks.Length)
                continue;
            for (var i = 0; i < remote.Tracks.Length; i++)
            {
                if (remote.Tracks[i].Sample(tick, delta, maxAhead) is not { } state)
                    continue;
                // A frozen kinematic Body moved this way takes its velocity from the move, which is what players standing
                // on it ride along with. Setting the host's velocity as well would carry them twice as fast.
                node.Bodies[i].GlobalTransform = new Transform3D(new Basis(state.Rotation.ToGodot()), state.Position.ToGodot());
            }
        }
        PlayerSync.PlaceRiders();
    }

    /// <summary>Host → client: a whole Creation, new to this client or with a changed graph, as it was at host tick <paramref name="tick"/>.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientFullSync(int revision, uint tick, string json)
    {
        if (Session.State != NetSession.SessionState.Connected)
            return;
        var diagnostics = new List<DataDiagnostic>();
        if (CreationJson.Read(json, Catalog, "sync", diagnostics) is not { } creation)
        {
            GD.PushError($"[sync] the host sent a creation this client cannot load: {string.Join("; ", diagnostics.Select(d => d.Message))}");
            return;
        }
        var node = Creations.Replace(creation, Assets);
        var tracks = new BodyTrack[creation.Bodies.Count];
        for (var i = 0; i < tracks.Length; i++)
        {
            var body = creation.Bodies[i];
            var pose = creation.BodyPose(i);
            var velocity = creation.GetBodyVelocity(i);
            tracks[i] = new BodyTrack(body.CentreOfMass.ToNumerics());
            tracks[i].Add(tick, new BodyState(pose.Position, pose.Rotation, velocity.Linear, velocity.Angular));
        }
        _remotes[creation.Id] = new Remote(revision, tracks);
        OnHostTick(tick);
        GD.Print($"[sync] creation {creation.Id} synced ({node.Bodies.Count} bodies, revision {revision})");
    }

    /// <summary>Host → client: stop showing a Creation (out of interest, parked or destroyed).</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientForget(int creationId)
    {
        if (Session.State == NetSession.SessionState.Connected)
            Forget(creationId);
    }

    /// <summary>Host → client: Body states at one tick.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable,
        TransferChannel = NetProtocol.BodyChannel)]
    private void ClientBodies(byte[] packet)
    {
        if (Session.State != NetSession.SessionState.Connected || BodySnapshot.Decode(packet) is not { } snapshot)
            return;
        PacketsReceived++;
        foreach (var run in snapshot.Creations)
        {
            if (!_remotes.TryGetValue(run.CreationId, out var remote) || (ushort)remote.Revision != run.Revision
                || run.FirstBody + run.Bodies.Count > remote.Tracks.Length)
                continue;
            for (var i = 0; i < run.Bodies.Count; i++)
                remote.Tracks[run.FirstBody + i].Add(snapshot.Tick, run.Bodies[i]);
        }
        OnHostTick(snapshot.Tick);
    }

    private void OnHostTick(uint tick)
    {
        if (tick <= _newestTick)
            return;
        _newestTick = tick;
        _clock.OnSnapshot(tick);
    }

    private void Forget(int creationId)
    {
        _remotes.Remove(creationId);
        Creations.Remove(creationId);
    }

    private void OnCreationRemoved(int creationId)
    {
        _remotes.Remove(creationId);
        if (Session.State != NetSession.SessionState.Hosting)
            return;
        _interest.ForgetCreation(creationId);
        foreach (var (peer, sent) in _sentRevisions)
        {
            if (sent.Remove(creationId))
                RpcId(peer, MethodName.ClientForget, creationId);
        }
    }

    private void OnPeerLeft(int peer)
    {
        _interest.Forget(peer);
        _sentRevisions.Remove(peer);
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client's copies belong to the session it leaves; a host keeps its own Creations.
        foreach (var id in _remotes.Keys.ToList())
            Forget(id);
        _interest.Clear();
        _sentRevisions.Clear();
        _tick = _newestTick = 0;
        _clock.Reset();
    }
}
