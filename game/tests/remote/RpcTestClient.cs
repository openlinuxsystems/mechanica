using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Mechanica.Core.Actions;

namespace Mechanica.Game.Tests;

/// <summary>
/// A minimal external agent for tests: a Godot WebSocket client that sends JSON-RPC requests to the AI
/// endpoint and collects the answers by id. Poll it every frame.
/// </summary>
public sealed class RpcTestClient
{
    private readonly WebSocketPeer _socket = new()
    {
        InboundBufferSize = 16 * 1024 * 1024,
        OutboundBufferSize = 8 * 1024 * 1024,
    };
    private readonly Dictionary<long, JsonElement> _responses = [];
    private long _nextId;

    /// <summary>Answers that carried no id (parse errors and the like), in arrival order.</summary>
    public List<JsonElement> Unmatched { get; } = [];

    /// <summary>Messages received, single or batch.</summary>
    public int MessagesReceived { get; private set; }

    /// <summary>Starts connecting.</summary>
    public Error Connect(int port) => _socket.ConnectToUrl($"ws://127.0.0.1:{port}");

    /// <summary>True once the handshake is done.</summary>
    public bool IsOpen => _socket.GetReadyState() == WebSocketPeer.State.Open;

    /// <summary>True once the socket closed.</summary>
    public bool IsClosed => _socket.GetReadyState() == WebSocketPeer.State.Closed;

    /// <summary>Sends and receives.</summary>
    public void Poll()
    {
        _socket.Poll();
        while (_socket.GetAvailablePacketCount() > 0)
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(_socket.GetPacket()));
            MessagesReceived++;
            var root = document.RootElement;
            foreach (var response in root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : [root])
            {
                if (response.GetProperty("id") is { ValueKind: JsonValueKind.Number } id)
                    _responses[id.GetInt64()] = response.Clone();
                else
                    Unmatched.Add(response.Clone());
            }
        }
    }

    /// <summary>Sends one request; returns its id.</summary>
    public long Call(string method, object? parameters = null)
    {
        var request = Request(method, parameters, out var id);
        _socket.SendText(request.ToJsonString());
        return id;
    }

    /// <summary>Sends a notification (no id, no answer).</summary>
    public void Notify(string method, object? parameters = null)
    {
        var request = Request(method, parameters, out _);
        request.Remove("id");
        _socket.SendText(request.ToJsonString());
    }

    /// <summary>Sends requests as one batch; returns their ids in order.</summary>
    public long[] Batch(IEnumerable<(string Method, object? Params)> calls)
    {
        var ids = new List<long>();
        var batch = new JsonArray();
        foreach (var (method, parameters) in calls)
        {
            batch.Add(Request(method, parameters, out var id));
            ids.Add(id);
        }
        _socket.SendText(batch.ToJsonString());
        return [.. ids];
    }

    /// <summary>Sends text as is.</summary>
    public void SendRaw(string text) => _socket.SendText(text);

    /// <summary>The answer to <paramref name="id"/>, once it arrived.</summary>
    public JsonElement? Response(long id) => _responses.TryGetValue(id, out var r) ? r : null;

    /// <summary>The <c>result</c> of a successful answer.</summary>
    public static JsonElement Result(JsonElement response) => response.GetProperty("result");

    /// <summary>The error code of a failed answer, or null on success.</summary>
    public static int? ErrorCode(JsonElement response) =>
        response.TryGetProperty("error", out var error) ? error.GetProperty("code").GetInt32() : null;

    /// <summary>Closes the socket.</summary>
    public void Close() => _socket.Close();

    private JsonObject Request(string method, object? parameters, out long id)
    {
        id = ++_nextId;
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (parameters is not null)
            request["params"] = parameters as JsonNode ?? JsonSerializer.SerializeToNode(parameters, parameters.GetType(), ActionJson.Options);
        return request;
    }
}
