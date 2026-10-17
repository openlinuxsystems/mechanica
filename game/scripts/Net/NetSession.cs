using Godot;
using Mechanica.Core;
using Mechanica.Core.Net;
using Mechanica.Core.Saves;
using Mechanica.Core.Ui;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Net;

/// <summary>
/// Connection lifecycle over ENet: host on a port, join an address, leave. With no connection the
/// instance is an offline host (Godot's offline peer is peer 1), so single player is hosting with no
/// clients (D10).
/// <para>
/// Joining is a handshake: once ENet connects, the client sends <see cref="Hello"/> with its protocol
/// version, <see cref="Password"/>, <see cref="Content"/> manifest and <see cref="Profile"/>; the host answers <see cref="Welcome"/> and only then treats the peer as
/// <see cref="IsReady">ready</see> (spawns a player for it, accepts its input and actions). Peers that
/// send the wrong version or different mods, or nothing within <see cref="NetProtocol.HandshakeTimeoutSeconds"/>, are
/// dropped. Every RPC sits on a node at the same path on every peer, so the scene layout is the protocol.
/// </para>
/// </summary>
public partial class NetSession : Node
{
    /// <summary>Where this instance stands.</summary>
    public enum SessionState
    {
        /// <summary>No socket; acting as host of a one-player session.</summary>
        Offline,

        /// <summary>Hosting; clients can join.</summary>
        Hosting,

        /// <summary>Joining; waiting for ENet and the handshake.</summary>
        Connecting,

        /// <summary>Joined and welcomed by the host.</summary>
        Connected,
    }

    // 0 is unlimited. An explicit limit is a trap: ENet works out bytes per interval as limit × elapsed ms
    // in 32 bits, which overflows above about 4 MB/s.
    private const int BandwidthBytesPerSecond = 0;

    private readonly HashSet<int> _readyPeers = [];
    private readonly Dictionary<int, PlayerProfile> _profiles = [];
    private readonly Dictionary<int, ContentManifest> _contents = [];
    private int _maxPlayers = NetProtocol.DefaultMaxPlayers;
    private string? _refusal;
    private string _password = "";

    /// <summary>
    /// Content this instance runs. A joining client's manifest must match the host's; the refusal
    /// names the mods that differ. Empty until the game loads mods at startup.
    /// </summary>
    public ContentManifest Content { get; set; } = ContentManifest.Empty;

    /// <summary>This instance's player. Set before the session starts.</summary>
    public PlayerProfile Profile { get; set; } = PlayerProfile.Fixed("Player");

    /// <summary>
    /// Who a ready peer is: the profile it sent on join, or <see cref="Profile"/> for this instance's own peer.
    /// Null for anyone else. On a client only its own profile is known.
    /// </summary>
    public PlayerProfile? ProfileOf(int peer) =>
        peer == LocalPeer || (IsHost && peer == NetProtocol.HostPeer) ? Profile : _profiles.GetValueOrDefault(peer);

    /// <summary>
    /// The world's password. Hosting, joining players must send it (empty lets anyone in); joining, this
    /// player sends it. Set before hosting or joining.
    /// </summary>
    public string Password
    {
        get => PasswordFrom?.Invoke() ?? _password;
        set
        {
            _password = value;
            PasswordFrom = null;
        }
    }

    /// <summary>Where a host reads <see cref="Password"/> from at each join (the player's settings), or null for the value set.</summary>
    public Func<string>? PasswordFrom { get; set; }

    /// <summary>Current state.</summary>
    public SessionState State { get; private set; } = SessionState.Offline;

    /// <summary>True when this instance simulates the world (offline or hosting).</summary>
    public bool IsHost => State is SessionState.Offline or SessionState.Hosting;

    /// <summary>Players allowed, host included. Fixed when hosting starts.</summary>
    public int MaxPlayers => _maxPlayers;

    /// <summary>The port this host listens on, once it has hosted.</summary>
    public int Port { get; private set; }

    /// <summary>This instance's peer id (1 for the host).</summary>
    public int LocalPeer => Multiplayer.GetUniqueId();

    /// <summary>Remote peers that finished the handshake (host side).</summary>
    public IReadOnlyCollection<int> ReadyPeers => _readyPeers;

    /// <summary>Why the last connection attempt or session ended, for the UI.</summary>
    public string LastMessage { get; private set; } = "";

    /// <summary>
    /// Host: where its own world load stands (UI spec §15), sent to each client as it joins and on every change.
    /// Client: the host's, as last reported; <see cref="LoadStage.WaitingForHost"/> until the host says.
    /// </summary>
    public LoadStage HostStage { get; private set; } = LoadStage.Entering;

    /// <summary>Client: the host player's name, once the host reports its load stage.</summary>
    public string HostName { get; private set; } = "";

    /// <summary>Host side: a peer finished the handshake.</summary>
    public event Action<int>? PeerReady;

    /// <summary>Host side: a ready peer left.</summary>
    public event Action<int>? PeerLeft;

    /// <summary>Client side: the host welcomed us.</summary>
    public event Action? Joined;

    /// <summary>Raised whenever <see cref="State"/> changes, after the change.</summary>
    public event Action<SessionState>? StateChanged;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        CloseSocket();
        _readyPeers.Clear();
        _profiles.Clear();
        _contents.Clear();
        State = SessionState.Offline;
    }

    /// <summary>True when <paramref name="peer"/> is the host or a remote peer that finished the handshake.</summary>
    public bool IsReady(int peer) => peer == NetProtocol.HostPeer || _readyPeers.Contains(peer);

    /// <summary>Starts hosting. The existing offline world and player carry over.</summary>
    public Error Host(int port, int maxPlayers = NetProtocol.DefaultMaxPlayers)
    {
        if (State != SessionState.Offline)
            return Error.AlreadyInUse;
        // One ENet slot more than remote players, so a player over the limit gets a "session is full"
        // refusal instead of an unanswered connect.
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateServer(port, maxPlayers, NetProtocol.ChannelCount, BandwidthBytesPerSecond, BandwidthBytesPerSecond);
        if (error != Error.Ok)
        {
            LastMessage = Loc.T("Could not host on port {0}: {1}.", port, error);
            return error;
        }
        // Godot's create_server passes its arguments to the ENet host one place off, so the host's
        // incoming bandwidth ends up as the channel count (a few bytes per second). Every client then
        // throttles its unreliable packets to 1 in 32 and holds back reliable ones. Reset it before anyone joins.
        peer.Host.BandwidthLimit(BandwidthBytesPerSecond, BandwidthBytesPerSecond);
        _maxPlayers = maxPlayers;
        Port = port;
        Multiplayer.MultiplayerPeer = peer;
        if (Multiplayer is SceneMultiplayer scene)
            scene.ServerRelay = false; // clients only ever talk to the host
        LastMessage = Loc.T("Hosting on port {0}.", port);
        SetState(SessionState.Hosting);
        return Error.Ok;
    }

    /// <summary>Starts joining a host. Completion is reported by <see cref="Joined"/> or a state change back to offline.</summary>
    public Error Join(NetEndpoint endpoint)
    {
        if (State != SessionState.Offline)
            return Error.AlreadyInUse;
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(endpoint.Address, endpoint.Port, NetProtocol.ChannelCount, BandwidthBytesPerSecond, BandwidthBytesPerSecond);
        if (error != Error.Ok)
        {
            LastMessage = Loc.T("Could not connect to {0}: {1}.", endpoint, error);
            return error;
        }
        Multiplayer.MultiplayerPeer = peer;
        _refusal = null;
        HostStage = LoadStage.WaitingForHost;
        HostName = "";
        LastMessage = Loc.T("Connecting to {0}…", endpoint);
        SetState(SessionState.Connecting);
        return Error.Ok;
    }

    /// <summary>Closes the connection and goes back to offline.</summary>
    public void Leave(string reason = "Left the session.")
    {
        if (State == SessionState.Offline)
            return;
        CloseSocket();
        _readyPeers.Clear();
        _profiles.Clear();
        _contents.Clear();
        // Back to hosting its own (loaded) world.
        if (State == SessionState.Connecting || State == SessionState.Connected)
            HostStage = LoadStage.Entering;
        LastMessage = reason;
        SetState(SessionState.Offline);
    }

    /// <summary>Client → host: first message after connecting.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hello(int protocolVersion, string gameName, string password, string contentJson, string profileJson)
    {
        if (State != SessionState.Hosting)
            return;
        var peer = Multiplayer.GetRemoteSenderId();
        if (_readyPeers.Contains(peer))
            return;
        if (protocolVersion != NetProtocol.Version || gameName != MechanicaInfo.GameName)
        {
            Refuse(peer, $"Version mismatch: host runs protocol {NetProtocol.Version}, you run {protocolVersion}.");
            return;
        }
        if (NetPassword.Check(Password, password) is { } wrong)
        {
            Refuse(peer, wrong);
            return;
        }
        if (ContentManifest.FromJson(contentJson) is not { } content)
        {
            Refuse(peer, "Your client sent an unreadable mod list.");
            return;
        }
        if (ContentManifest.Differences(Content, content) is { Count: > 0 } differences)
        {
            Refuse(peer, $"Your mods don't match the host's: {string.Join("; ", differences)}.");
            return;
        }
        if (profileJson.Length > 4096 || PlayerProfile.FromJson(profileJson) is not { } profile)
        {
            Refuse(peer, "Your client sent an unreadable player profile.");
            return;
        }
        if (profile.Id == Profile.Id || _profiles.Values.Any(p => p.Id == profile.Id))
        {
            Refuse(peer, $"A player with your profile ({profile.Name}) is already in this session.");
            return;
        }
        if (_readyPeers.Count + 1 >= _maxPlayers)
        {
            Refuse(peer, "The session is full.");
            return;
        }
        _readyPeers.Add(peer);
        _profiles[peer] = profile;
        _contents[peer] = content;
        RpcId(peer, MethodName.Welcome, NetProtocol.Version);
        RpcId(peer, MethodName.HostLoading, (int)HostStage, Profile.Name);
        GD.Print($"[net] peer {peer} ({profile.Name}) joined");
        PeerReady?.Invoke(peer);
    }

    /// <summary>Host → client: the handshake succeeded.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Welcome(int protocolVersion)
    {
        if (State != SessionState.Connecting)
            return;
        LastMessage = Loc.T("Connected.");
        SetState(SessionState.Connected);
        Joined?.Invoke();
    }

    /// <summary>Host: reports its own load stage to every client, and to each that joins later.</summary>
    public void ReportStage(LoadStage stage)
    {
        if (!IsHost || stage == HostStage)
            return;
        HostStage = stage;
        if (State != SessionState.Hosting)
            return;
        foreach (var peer in _readyPeers)
            RpcId(peer, MethodName.HostLoading, (int)stage, Profile.Name);
    }

    /// <summary>Host → client: where the host's own world load stands.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void HostLoading(int stage, string name)
    {
        if (State != SessionState.Connected || !Enum.IsDefined((LoadStage)stage))
            return;
        HostStage = (LoadStage)stage;
        HostName = name.Length > 64 ? name[..64] : name;
    }

    /// <summary>Host → client: the host is about to drop us, and why.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Refused(string reason)
    {
        GD.Print($"[net] refused by host: {reason}");
        _refusal = reason;
    }

    private void Refuse(int peer, string reason)
    {
        GD.Print($"[net] refusing peer {peer}: {reason}");
        RpcId(peer, MethodName.Refused, reason);
        // Give the reliable message a moment to leave before cutting the peer.
        GetTree().CreateTimer(0.25).Timeout += () => Kick(peer);
    }

    /// <summary>
    /// Host: drops every joined client whose content (as sent on join) no longer matches <see cref="Content"/>,
    /// telling it why, after the host reloaded its mods. Returns the peers dropped.
    /// </summary>
    public IReadOnlyList<int> RecheckContent()
    {
        var dropped = new List<int>();
        foreach (var peer in _readyPeers.ToList())
        {
            if (_contents.TryGetValue(peer, out var content) && ContentManifest.Differences(Content, content) is { Count: > 0 } differences)
            {
                Refuse(peer, $"The host reloaded its mods and yours no longer match: {string.Join("; ", differences)}.");
                dropped.Add(peer);
            }
        }
        return dropped;
    }

    private void Kick(int peer)
    {
        if (IsInstanceValid(this) && State == SessionState.Hosting && Multiplayer.GetPeers().Contains(peer))
            Multiplayer.MultiplayerPeer.DisconnectPeer(peer);
    }

    private void OnPeerConnected(long id)
    {
        if (State != SessionState.Hosting)
            return;
        var peer = (int)id;
        DisableThrottle(peer);
        GetTree().CreateTimer(NetProtocol.HandshakeTimeoutSeconds).Timeout += () =>
        {
            if (IsInstanceValid(this) && !_readyPeers.Contains(peer))
                Kick(peer);
        };
    }

    private void OnPeerDisconnected(long id)
    {
        var peer = (int)id;
        if (State == SessionState.Hosting && _readyPeers.Remove(peer))
        {
            GD.Print($"[net] peer {peer} left");
            PeerLeft?.Invoke(peer);
            _profiles.Remove(peer);
            _contents.Remove(peer);
        }
    }

    private void OnConnectedToServer()
    {
        DisableThrottle(NetProtocol.HostPeer);
        RpcId(NetProtocol.HostPeer, MethodName.Hello, NetProtocol.Version, MechanicaInfo.GameName, Password, Content.ToJson(), Profile.ToJson());
    }

    /// <summary>
    /// ENet's packet throttle randomly drops unreliable packets when round-trip times vary, which
    /// starves the host of inputs and clients of snapshots in bursts. Inputs already carry redundancy
    /// and both streams are tiny, so keep the throttle fully open.
    /// </summary>
    private void DisableThrottle(int peer)
    {
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet && enet.GetPeer(peer) is { } packetPeer)
            packetPeer.ThrottleConfigure(5000, 32, 0);
    }

    private void OnConnectionFailed() => Leave("Could not reach the host.");

    private void OnServerDisconnected() => Leave(_refusal ?? "The host closed the session.");

    private void CloseSocket()
    {
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet)
            enet.Close();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
    }

    private void SetState(SessionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}
