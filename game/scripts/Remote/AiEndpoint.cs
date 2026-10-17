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
/// It runs only while this peer is the host; joining a session closes it.
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

    /// <summary>The session; the endpoint closes when this peer stops being the host. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The host's Action API. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Record/replay, for the <c>mechanica.record.*</c> and <c>mechanica.replay.*</c> methods. Set before adding to the tree.</summary>
    public RecordReplay Log { get; init; } = null!;

    /// <summary>True while listening.</summary>
    public bool IsListening => _server?.IsListening() == true;

    /// <summary>The port it listens on, once listening.</summary>
    public int Port => _server?.GetLocalPort() ?? 0;

    /// <summary>Agents connected now (handshake done or in progress).</summary>
    public int ConnectionCount => _clients.Count;

    public override void _Ready() => Session.StateChanged += OnStateChanged;

    public override void _ExitTree()
    {
        Session.StateChanged -= OnStateChanged;
        Stop();
    }

    /// <summary>
    /// Starts listening on <paramref name="bind"/>:<paramref name="port"/> (0 picks a free port). A
    /// non-loopback address needs a <paramref name="token"/>. Returns an error message or null.
    /// </summary>
    public string? Listen(int port, string bind = "127.0.0.1", string? token = null)
    {
        Stop();
        if (!Session.IsHost)
            return "The AI endpoint runs on the host only.";
        if (string.IsNullOrEmpty(token) && !LaunchOptions.IsLoopback(bind))
            return $"Listening on {bind} opens the AI endpoint to the network; give a token (--ai-token or MECHANICA_AI_TOKEN).";
        var server = new TcpServer();
        var error = server.Listen((ushort)port, bind);
        if (error != Error.Ok)
            return $"Cannot listen on {bind}:{port} ({error}).";
        _server = server;
        _rpc = new JsonRpcEndpoint(Actions.Dispatcher, Log.Log, Session.LocalPeer, token);
        GD.Print($"[ai] endpoint listening on ws://{bind}:{Port}{(_rpc.RequiresToken ? " (token required)" : "")}");
        return null;
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
    }

    public override void _PhysicsProcess(double delta)
    {
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
        }
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
