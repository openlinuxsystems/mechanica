using System.Text;
using Godot;
using Mechanica.Core.Net;
using Mechanica.Core.Remote;
using Mechanica.Game.Net;

namespace Mechanica.Game.Remote;

/// <summary>
/// The external AI endpoint (§1, §3): a WebSocket server on the host that speaks JSON-RPC 2.0 in text
/// messages (<see cref="JsonRpcEndpoint"/>). Each call is an Action API call made through the host's
/// dispatcher as an external agent, validated exactly like a player's. It listens on loopback unless
/// told otherwise, and then only with a token, since an agent can do anything the Action API allows.
/// It runs only while this peer is the host; joining a session closes it. Each connection holds scopes
/// (observe, play, admin) from the token it gave, receives the <see cref="Events"/> it subscribed to, and
/// can call the methods of every module in <see cref="Modules"/>. When an agent loads or starts another world
/// (<c>world.*</c>) the endpoint is carried over to it (<see cref="Carry"/>, <see cref="TakeCarried"/>,
/// <see cref="Resume"/>): it keeps listening, connections stay open with their scopes and subscriptions, and
/// requests sent meanwhile wait and are answered by the new world.
/// </summary>
public partial class AiEndpoint : Node
{
    /// <summary>Agents connected at once.</summary>
    public const int MaxConnections = 8;

    private const int InboundBuffer = JsonRpcEndpoint.MaxMessageLength + 1024;
    private const int OutboundBuffer = 16 * 1024 * 1024;

    private readonly List<(WebSocketPeer Socket, RemoteConnection Connection)> _clients = [];
    private TcpServer? _server;
    private JsonRpcEndpoint? _rpc;
    private IDisposable? _actionWatch;
    private EndpointAccess _access = new();
    private bool _bound;

    /// <summary>An endpoint carried out of a world that is being replaced, waiting for the next one to take it.</summary>
    public static AiEndpoint? Carried { get; private set; }

    /// <summary>The session; the endpoint closes when this peer stops being the host. Set before adding to the tree.</summary>
    public NetSession Session { get; set; } = null!;

    /// <summary>The host's Action API. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; set; } = null!;

    /// <summary>Record/replay, for the <c>mechanica.record.*</c> and <c>mechanica.replay.*</c> methods. Set before adding to the tree.</summary>
    public RecordReplay Log { get; set; } = null!;

    /// <summary>True between <see cref="Carry"/> and <see cref="Resume"/>: listening, but no world answers yet.</summary>
    public bool IsCarried { get; private set; }

    /// <summary>Notifications agents subscribe to; host systems publish into it (see <see cref="RemoteEventSources"/>).</summary>
    public RemoteEvents Events { get; } = new();

    /// <summary>Endpoint method groups beyond the Action API. Add before <see cref="Listen(int, string, EndpointAccess)"/>.</summary>
    public List<IRpcModule> Modules { get; } = [];

    /// <summary>True while listening.</summary>
    public bool IsListening => _server?.IsListening() == true;

    /// <summary>The port it listens on, once listening.</summary>
    public int Port => _server?.GetLocalPort() ?? 0;

    /// <summary>Agents connected now (handshake done or in progress).</summary>
    public int ConnectionCount => _clients.Count;

    public override void _Ready()
    {
        // World tests pause the game; the endpoint keeps answering.
        ProcessMode = ProcessModeEnum.Always;
        AddDocumentationDirectories();
        Bind();
    }

    public override void _ExitTree()
    {
        if (IsCarried)
            return;
        Unbind();
        Stop();
    }

    private void Bind()
    {
        if (_bound)
            return;
        _bound = true;
        Session.StateChanged += OnStateChanged;
        Events.Clock = () => Log.Log.CurrentTick;
        _actionWatch = Events.WatchActions(Actions.Dispatcher);
    }

    private void Unbind()
    {
        if (!_bound)
            return;
        _bound = false;
        Session.StateChanged -= OnStateChanged;
        _actionWatch?.Dispose();
        _actionWatch = null;
        Events.Clock = null;
    }

    /// <summary>
    /// Takes the endpoint out of its world, which is about to be replaced: it moves to the tree's root and keeps
    /// its server and connections, but answers nothing until <see cref="Resume"/>. False when it isn't listening
    /// (then there is nothing to carry and it closes with the world).
    /// </summary>
    public bool Carry()
    {
        if (!IsListening || IsCarried)
            return false;
        Unbind();
        _rpc = null;
        IsCarried = true;
        Carried = this;
        var root = GetTree().Root;
        Callable.From(() =>
        {
            GetParent()?.RemoveChild(this);
            root.AddChild(this);
        }).CallDeferred();
        return true;
    }

    /// <summary>The endpoint a replaced world carried out, with its old modules removed, or null. The caller adds it to the new world.</summary>
    public static AiEndpoint? TakeCarried()
    {
        var carried = Carried;
        Carried = null;
        if (carried is null || !IsInstanceValid(carried))
            return null;
        carried.Modules.Clear();
        carried.GetParent()?.RemoveChild(carried);
        return carried;
    }

    /// <summary>
    /// Serves the new world's <see cref="Session"/>, <see cref="Actions"/>, <see cref="Log"/> and <see cref="Modules"/>
    /// after a carry: the same address and tokens, and every open connection carried over. Returns an error message or null.
    /// </summary>
    public string? Resume()
    {
        if (!IsCarried)
            return "The endpoint was not carried over.";
        IsCarried = false;
        Bind();
        if (!Session.IsHost)
        {
            Stop();
            return "The AI endpoint runs on the host only.";
        }
        _rpc = CreateEndpoint(_access);
        _rpc.InternalError += OnInternalError;
        foreach (var (_, connection) in _clients)
            _rpc.Adopt(connection);
        GD.Print($"[ai] endpoint carried over to the new world ({_clients.Count} connections)");
        return null;
    }

    /// <summary>Spawns the players agents join as (<c>mechanica.join</c>). Set before <see cref="Listen(int, string, EndpointAccess)"/>.</summary>
    public IAgentHost? Agents { get; set; }

    /// <summary>
    /// Starts listening on <paramref name="bind"/>:<paramref name="port"/> (0 picks a free port) with one
    /// optional token that grants every scope. Returns an error message or null.
    /// </summary>
    public string? Listen(int port, string bind = "127.0.0.1", string? token = null) =>
        Listen(port, bind, new EndpointAccess().AddToken(token, [.. EndpointAccess.All]));

    /// <summary>
    /// Starts listening on <paramref name="bind"/>:<paramref name="port"/> (0 picks a free port). A
    /// non-loopback address needs at least one token in <paramref name="access"/>. Returns an error message or null.
    /// </summary>
    public string? Listen(int port, string bind, EndpointAccess access)
    {
        Stop();
        if (!Session.IsHost)
            return "The AI endpoint runs on the host only.";
        if (!access.RequiresToken && !LaunchOptions.IsLoopback(bind))
            return $"Listening on {bind} opens the AI endpoint to the network; give a token (--ai-token or MECHANICA_AI_TOKEN).";
        var server = new TcpServer();
        var error = server.Listen((ushort)port, bind);
        if (error != Error.Ok)
            return $"Cannot listen on {bind}:{port} ({error}).";
        _server = server;
        _access = access;
        _rpc = CreateEndpoint(access);
        _rpc.InternalError += OnInternalError;
        GD.Print($"[ai] endpoint listening on ws://{bind}:{Port}{(_rpc.RequiresToken ? " (token required)" : "")}");
        return null;
    }

    /// <summary>
    /// The protocol over this host's dispatcher, recorder, events, agents and <see cref="Modules"/>, without a
    /// socket: what <see cref="Listen(int, string, EndpointAccess)"/> serves, and what the AI docs are generated from.
    /// </summary>
    public JsonRpcEndpoint CreateEndpoint(EndpointAccess access)
    {
        var rpc = new JsonRpcEndpoint(Actions.Dispatcher, Log.Log, Session.LocalPeer, access, Events) { Agents = Agents };
        foreach (var module in Modules)
            rpc.AddModule(module);
        return rpc;
    }

    /// <summary>Closes every connection and stops listening.</summary>
    public void Stop()
    {
        foreach (var (socket, connection) in _clients)
        {
            socket.Close(1001, "The host closed the AI endpoint.");
            _rpc?.Disconnect(connection);
        }
        _clients.Clear();
        _server?.Stop();
        _server = null;
        _rpc = null;
        IsCarried = false;
        if (Carried == this)
            Carried = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_server is not null && IsCarried)
        {
            // Between worlds: keep the sockets alive; their requests wait in the socket for the new world.
            foreach (var (socket, _) in _clients)
                socket.Poll();
            return;
        }
        if (_server is null || _rpc is null)
            return;
        while (_server.IsConnectionAvailable())
        {
            var tcp = _server.TakeConnection();
            if (_clients.Count >= MaxConnections)
            {
                tcp.DisconnectFromHost();
                continue;
            }
            var socket = new WebSocketPeer
            {
                InboundBufferSize = InboundBuffer,
                OutboundBufferSize = OutboundBuffer,
                MaxQueuedPackets = 1024,
            };
            if (socket.AcceptStream(tcp) == Error.Ok)
                _clients.Add((socket, _rpc.Connect()));
        }

        var now = Time.GetTicksMsec() / 1000.0;
        for (var i = _clients.Count - 1; i >= 0; i--)
        {
            var (socket, connection) = _clients[i];
            socket.Poll();
            var state = socket.GetReadyState();
            if (state == WebSocketPeer.State.Closed)
            {
                _rpc.Disconnect(connection);
                _clients.RemoveAt(i);
                continue;
            }
            if (state != WebSocketPeer.State.Open)
                continue;
            while (socket.GetAvailablePacketCount() > 0)
            {
                var packet = socket.GetPacket();
                if (!socket.WasStringPacket())
                {
                    socket.Close(1003, "Send JSON-RPC as text messages.");
                    break;
                }
                var response = _rpc.Handle(connection, Encoding.UTF8.GetString(packet), now);
                if (response is not null)
                    socket.SendText(response);
                if (_rpc is null)
                    return; // A call stopped the endpoint (the session ended).
            }
            foreach (var notification in _rpc.DrainOutgoing(connection))
                socket.SendText(notification);
        }
    }

    // Godot loads the game's assemblies without a file location, so tell mechanica.describe where their
    // XML docs are: the build folders when run from the project, the data folder next to an export.
    private static void AddDocumentationDirectories()
    {
        var folders = new List<string>();
        var builds = ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin");
        if (Directory.Exists(builds))
            folders.AddRange(Directory.GetDirectories(builds));
        var exe = Path.GetDirectoryName(OS.GetExecutablePath());
        if (exe is not null && Directory.Exists(exe))
        {
            folders.Add(exe);
            folders.AddRange(Directory.GetDirectories(exe, "data_*"));
        }
        foreach (var folder in folders)
            RpcSchema.AddDocumentationDirectory(folder);
    }

    private void OnInternalError(string method, string message)
    {
        GD.PushError($"[ai] {method} failed on the host: {message}");
        Events.Publish("messages", () => new { kind = "error", source = "endpoint", method, text = message });
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        if (!Session.IsHost && _server is not null)
        {
            GD.Print("[ai] closing the AI endpoint: this game is joining another host.");
            Stop();
        }
    }
}
