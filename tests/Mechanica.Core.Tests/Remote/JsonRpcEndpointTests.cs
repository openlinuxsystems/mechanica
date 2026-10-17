using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Remote;
using Codes = Mechanica.Core.Remote.JsonRpcEndpoint.Codes;

namespace Mechanica.Core.Tests.Remote;

public class JsonRpcEndpointTests
{
    private sealed record MoveArgs(int Distance);

    private readonly ActionDispatcher _dispatcher = new();
    private readonly ActionLog _log;
    private readonly List<ActionCaller> _callers = [];
    private double _now;

    public JsonRpcEndpointTests()
    {
        _dispatcher.Register<MoveArgs>("Move",
            (_, args) => args.Distance > 10 ? "That is too far." : null,
            (caller, args) =>
            {
                _callers.Add(caller);
                return JsonSerializer.SerializeToElement(new { moved = args.Distance });
            });
        _dispatcher.Register<NoArgs>("Boom", (_, _) => null, (_, _) => throw new InvalidOperationException("kaboom"));
        _log = new ActionLog(_dispatcher, 60);
    }

    private JsonRpcEndpoint Endpoint(string? token = null, ActionRateLimiter? limiter = null) =>
        new(_dispatcher, _log, hostPeer: 1, token, limiter);

    private JsonElement Send(JsonRpcEndpoint endpoint, RemoteConnection connection, string message)
    {
        var text = endpoint.Handle(connection, message, _now);
        Assert.NotNull(text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static int? Code(JsonElement response) =>
        response.TryGetProperty("error", out var e) ? e.GetProperty("code").GetInt32() : null;

    [Fact]
    public void ActionsAreMethodsDispatchedAsExternal()
    {
        var endpoint = Endpoint();
        var response = Send(endpoint, endpoint.Connect(), """{ "jsonrpc": "2.0", "id": "a", "method": "Move", "params": { "distance": 3 } }""");

        Assert.Equal("a", response.GetProperty("id").GetString());
        Assert.Equal(3, response.GetProperty("result").GetProperty("moved").GetInt32());
        Assert.Equal([new ActionCaller(1, ActionSource.External)], _callers);
    }

    [Theory]
    [InlineData("""{ "jsonrpc": "2.0", "id": 1, "method": "Fly" }""", Codes.MethodNotFound, "unknown_action")]
    [InlineData("""{ "jsonrpc": "2.0", "id": 1, "method": "mechanica.fly" }""", Codes.MethodNotFound, "unknown_action")]
    [InlineData("""{ "jsonrpc": "2.0", "id": 1, "method": "Move", "params": { "distance": "x" } }""", Codes.InvalidParams, "bad_args")]
    [InlineData("""{ "jsonrpc": "2.0", "id": 1, "method": "Move", "params": [3] }""", Codes.InvalidParams, "bad_args")]
    [InlineData("""{ "jsonrpc": "2.0", "id": 1, "method": "Move", "params": { "distance": 11 } }""", Codes.Rejected, "rejected")]
    [InlineData("""{ "jsonrpc": "2.0", "id": 1, "method": "Boom" }""", Codes.InternalError, "internal_error")]
    [InlineData("""{ "jsonrpc": "2.0", "id": 1, "method": "mechanica.record.stop" }""", Codes.Unavailable, null)]
    [InlineData("""{ "jsonrpc": "2.0", "id": 1, "method": "mechanica.info", "params": { "x": 1 } }""", Codes.InvalidParams, "bad_args")]
    [InlineData("""{ "jsonrpc": "1.0", "id": 1, "method": "Move" }""", Codes.InvalidRequest, null)]
    [InlineData("""{ "jsonrpc": "2.0", "id": 1 }""", Codes.InvalidRequest, null)]
    [InlineData("""{ "jsonrpc": "2.0", "id": {}, "method": "Move" }""", Codes.InvalidRequest, null)]
    [InlineData("""[]""", Codes.InvalidRequest, null)]
    [InlineData("""{ "jsonrpc": """, Codes.ParseError, null)]
    public void FailuresAreJsonRpcErrors(string message, int code, string? dataCode)
    {
        var endpoint = Endpoint();
        var response = Send(endpoint, endpoint.Connect(), message);

        Assert.Equal(code, Code(response));
        Assert.Equal("2.0", response.GetProperty("jsonrpc").GetString());
        Assert.True(response.TryGetProperty("id", out _));
        var error = response.GetProperty("error");
        Assert.Equal(dataCode, error.TryGetProperty("data", out var data) ? data.GetProperty("code").GetString() : null);
        Assert.Empty(_callers);
    }

    [Fact]
    public void NotificationsRunWithoutAnAnswer()
    {
        var endpoint = Endpoint();
        var connection = endpoint.Connect();

        Assert.Null(endpoint.Handle(connection, """{ "jsonrpc": "2.0", "method": "Move", "params": { "distance": 1 } }""", 0));
        Assert.Null(endpoint.Handle(connection, """[{ "jsonrpc": "2.0", "method": "Move", "params": { "distance": 1 } }, { "jsonrpc": "2.0", "method": "Fly" }]""", 0));
        Assert.Equal(2, _callers.Count);
    }

    [Fact]
    public void BatchesAnswerEachRequestInOrder()
    {
        var endpoint = Endpoint();
        var response = Send(endpoint, endpoint.Connect(), """
            [
              { "jsonrpc": "2.0", "id": 1, "method": "Move", "params": { "distance": 1 } },
              { "jsonrpc": "2.0", "method": "Move", "params": { "distance": 2 } },
              { "jsonrpc": "2.0", "id": 2, "method": "Move", "params": { "distance": 20 } },
              5,
            ]
            """);

        Assert.Equal(JsonValueKind.Array, response.ValueKind);
        Assert.Equal([1, 2, -1], response.EnumerateArray().Select(r => r.GetProperty("id").ValueKind == JsonValueKind.Null ? -1 : r.GetProperty("id").GetInt32()));
        Assert.Equal([null, Codes.Rejected, Codes.InvalidRequest], response.EnumerateArray().Select(Code));
        Assert.Equal(2, _callers.Count);
    }

    [Fact]
    public void BatchesAreLimited()
    {
        var endpoint = Endpoint();
        var batch = "[" + string.Join(",", Enumerable.Repeat("""{ "jsonrpc": "2.0", "method": "Move", "params": { "distance": 1 } }""", JsonRpcEndpoint.MaxBatch + 1)) + "]";

        Assert.Equal(Codes.InvalidRequest, Code(Send(endpoint, endpoint.Connect(), batch)));
        Assert.Empty(_callers);
    }

    [Fact]
    public void TokenGatesEverythingButAuth()
    {
        var endpoint = Endpoint(token: "s3cret");
        var connection = endpoint.Connect();
        Assert.True(endpoint.RequiresToken);

        Assert.Equal(Codes.Unauthorized, Code(Send(endpoint, connection, """{ "jsonrpc": "2.0", "id": 1, "method": "Move", "params": { "distance": 1 } }""")));
        Assert.Equal(Codes.Unauthorized, Code(Send(endpoint, connection, """{ "jsonrpc": "2.0", "id": 2, "method": "mechanica.auth", "params": { "token": "guess" } }""")));
        Assert.Equal(Codes.InvalidParams, Code(Send(endpoint, connection, """{ "jsonrpc": "2.0", "id": 3, "method": "mechanica.auth" }""")));
        Assert.Null(Code(Send(endpoint, connection, """{ "jsonrpc": "2.0", "id": 4, "method": "mechanica.auth", "params": { "token": "s3cret" } }""")));
        Assert.Null(Code(Send(endpoint, connection, """{ "jsonrpc": "2.0", "id": 5, "method": "Move", "params": { "distance": 1 } }""")));
        // Each connection authenticates on its own.
        Assert.Equal(Codes.Unauthorized, Code(Send(endpoint, endpoint.Connect(), """{ "jsonrpc": "2.0", "id": 6, "method": "mechanica.info" }""")));
    }

    [Fact]
    public void ConnectionsAreRateLimited()
    {
        var endpoint = Endpoint(limiter: new ActionRateLimiter(perSecond: 1, burst: 2));
        var a = endpoint.Connect();
        var b = endpoint.Connect();
        const string move = """{ "jsonrpc": "2.0", "id": 1, "method": "Move", "params": { "distance": 1 } }""";

        Assert.Null(Code(Send(endpoint, a, move)));
        Assert.Null(Code(Send(endpoint, a, move)));
        var limited = Send(endpoint, a, move);
        Assert.Equal(Codes.RateLimited, Code(limited));
        Assert.Equal("rate_limited", limited.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
        Assert.Null(Code(Send(endpoint, b, move)));
        _now = 1;
        Assert.Null(Code(Send(endpoint, a, move)));
    }

    [Fact]
    public void InfoListsActionsAndQueries()
    {
        _dispatcher.Register<NoArgs>("Peek", (_, _) => null, (_, _) => null, readOnly: true);
        var endpoint = Endpoint();
        var info = Send(endpoint, endpoint.Connect(), """{ "jsonrpc": "2.0", "id": 1, "method": "mechanica.info" }""").GetProperty("result");

        Assert.Equal(JsonRpcEndpoint.ProtocolVersion, info.GetProperty("protocol_version").GetInt32());
        Assert.Equal(["Boom", "Move", "Peek"], info.GetProperty("actions").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(["Peek"], info.GetProperty("queries").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(60, info.GetProperty("tick_rate").GetInt32());
    }

    [Fact]
    public void RecordAndReplayOverRpc()
    {
        var endpoint = Endpoint();
        var connection = endpoint.Connect();
        Assert.Null(Code(Send(endpoint, connection, """{ "jsonrpc": "2.0", "id": 1, "method": "mechanica.record.start" }""")));
        Assert.Equal(Codes.Unavailable, Code(Send(endpoint, connection, """{ "jsonrpc": "2.0", "id": 2, "method": "mechanica.record.start", "params": {} }""")));
        Send(endpoint, connection, """{ "jsonrpc": "2.0", "id": 3, "method": "Move", "params": { "distance": 4 } }""");
        var recording = Send(endpoint, connection, """{ "jsonrpc": "2.0", "id": 4, "method": "mechanica.record.stop" }""").GetProperty("result");
        Assert.Equal(1, recording.GetProperty("actions").GetArrayLength());
        Assert.Equal(Codes.Unavailable, Code(Send(endpoint, connection, """{ "jsonrpc": "2.0", "id": 5, "method": "mechanica.replay.status" }""")));

        var start = Send(endpoint, connection, $$"""{ "jsonrpc": "2.0", "id": 6, "method": "mechanica.replay.start", "params": { "recording": {{recording.GetRawText()}}, "timed": false } }""");
        Assert.Equal(1, start.GetProperty("result").GetProperty("total").GetInt32());
        _log.Tick(1);
        var status = Send(endpoint, connection, """{ "jsonrpc": "2.0", "id": 7, "method": "mechanica.replay.status" }""").GetProperty("result");
        Assert.False(status.GetProperty("running").GetBoolean());
        Assert.Equal(1, status.GetProperty("dispatched").GetInt32());
        Assert.Equal(0, status.GetProperty("mismatches").GetArrayLength());
        Assert.Equal(2, _callers.Count);
        Assert.Null(Code(Send(endpoint, connection, """{ "jsonrpc": "2.0", "id": 8, "method": "mechanica.replay.stop" }""")));
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "recording": { "tick_rate": 60, "actions": 3 } }""")]
    [InlineData("""{ "recording": { "tick_rate": 60, "actions": [] }, "speed": 2 }""")]
    public void BadReplayRequestsAreInvalidParams(string parameters)
    {
        var endpoint = Endpoint();
        var response = Send(endpoint, endpoint.Connect(), $$"""{ "jsonrpc": "2.0", "id": 1, "method": "mechanica.replay.start", "params": {{parameters}} }""");

        Assert.Equal(Codes.InvalidParams, Code(response));
        Assert.Null(_log.ReplayStatus);
    }

    [Fact]
    public void OversizedMessagesAreRefused()
    {
        var endpoint = Endpoint();
        var response = Send(endpoint, endpoint.Connect(), new string(' ', JsonRpcEndpoint.MaxMessageLength + 1));
        Assert.Equal(Codes.InvalidRequest, Code(response));
    }
}
