using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Remote;
using Codes = Mechanica.Core.Remote.JsonRpcEndpoint.Codes;

namespace Mechanica.Core.Tests.Remote;

/// <summary>Protocol v2: scopes, modules, mechanica.describe and pushed events.</summary>
public class EndpointProtocolTests
{
    private sealed record EchoParams(string Text, int Times = 1);

    private sealed class EchoModule : IRpcModule
    {
        public List<RpcContext> Calls { get; } = [];

        public IEnumerable<RpcMethodInfo> Methods =>
        [
            RpcMethod.Create<EchoParams>("test.echo", AgentScope.Observe, "Repeats text.", (context, args) =>
            {
                Calls.Add(context);
                return RpcMethod.Value(string.Concat(Enumerable.Repeat(args.Text, args.Times)));
            }),
            RpcMethod.Create<NoArgs>("test.wipe", AgentScope.Admin, "Wipes everything.", (_, _) => null),
            RpcMethod.Create<NoArgs>("test.refuse", AgentScope.Observe, "Always refuses.",
                (_, _) => throw new RpcException(RpcErrors.Rejected, "No.", new { reason = "because" })),
        ];
    }

    private readonly ActionDispatcher _dispatcher = new();
    private readonly ActionLog _log;
    private readonly RemoteEvents _events = new();
    private long _tick = 42;

    public EndpointProtocolTests()
    {
        _dispatcher.Register<PlacePartArgs>(BuildActions.PlacePart, (_, _) => null, (_, a) => JsonSerializer.SerializeToElement(new { placed = a.Part }));
        _dispatcher.Register<NoArgs>("Look", (_, _) => null, (_, _) => null, readOnly: true);
        _dispatcher.Register<NoArgs>("SpawnBot", (_, _) => null, (_, _) => null);
        _dispatcher.Register<NoArgs>("Crash", (_, _) => null, (_, _) => throw new InvalidOperationException("boom"));
        _log = new ActionLog(_dispatcher, 60);
        _events.Clock = () => _tick;
    }

    private JsonRpcEndpoint Endpoint(EndpointAccess? access = null)
    {
        var endpoint = new JsonRpcEndpoint(_dispatcher, _log, 1, access ?? new EndpointAccess(), _events);
        return endpoint;
    }

    private static JsonElement Send(JsonRpcEndpoint endpoint, RemoteConnection connection, string method, string parameters = "{}")
    {
        var text = endpoint.Handle(connection, $$"""{ "jsonrpc": "2.0", "id": 1, "method": "{{method}}", "params": {{parameters}} }""", 0);
        Assert.NotNull(text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static int? Code(JsonElement response) =>
        response.TryGetProperty("error", out var e) ? e.GetProperty("code").GetInt32() : null;

    [Fact]
    public void WithoutTokensEveryConnectionHasEveryScope()
    {
        var connection = Endpoint().Connect();
        Assert.True(connection.Authenticated);
        Assert.Equal(EndpointAccess.All, connection.Scopes);
    }

    [Fact]
    public void TokensGrantTheirScopes()
    {
        var access = new EndpointAccess()
            .AddToken("watch", AgentScope.Observe)
            .AddToken("play", AgentScope.Observe, AgentScope.Play)
            .AddToken("root", AgentScope.Observe, AgentScope.Play, AgentScope.Admin);
        var endpoint = Endpoint(access);

        var watcher = endpoint.Connect();
        Assert.Null(Code(Send(endpoint, watcher, "mechanica.auth", """{ "token": "watch" }""")));
        Assert.Null(Code(Send(endpoint, watcher, "Look")));
        Assert.Equal(Codes.Forbidden, Code(Send(endpoint, watcher, "PlacePart", """{ "part": "vanilla:block", "cell": [0, 0, 0], "position": [0, 0, 0] }""")));

        var player = endpoint.Connect();
        Send(endpoint, player, "mechanica.auth", """{ "token": "play" }""");
        Assert.Null(Code(Send(endpoint, player, "PlacePart", """{ "part": "vanilla:block", "cell": [0, 0, 0], "position": [0, 0, 0] }""")));
        var forbidden = Send(endpoint, player, "SpawnBot");
        Assert.Equal(Codes.Forbidden, Code(forbidden));
        Assert.Equal("forbidden", forbidden.GetProperty("error").GetProperty("data").GetProperty("code").GetString());

        var admin = endpoint.Connect();
        Send(endpoint, admin, "mechanica.auth", """{ "token": "root" }""");
        Assert.Null(Code(Send(endpoint, admin, "SpawnBot")));
    }

    [Theory]
    [InlineData("observe,play", new[] { AgentScope.Observe, AgentScope.Play })]
    [InlineData(" Admin ", new[] { AgentScope.Admin })]
    [InlineData("observe,fly", null)]
    [InlineData("", null)]
    public void ScopeListsParse(string text, AgentScope[]? expected) =>
        Assert.Equal(expected, EndpointAccess.ParseScopes(text));

    [Fact]
    public void ModuleMethodsRunWithTheirContextAndScope()
    {
        var module = new EchoModule();
        var endpoint = Endpoint(new EndpointAccess().AddToken("watch", AgentScope.Observe));
        endpoint.AddModule(module);
        var connection = endpoint.Connect();
        Send(endpoint, connection, "mechanica.auth", """{ "token": "watch" }""");

        Assert.Equal("abab", Send(endpoint, connection, "test.echo", """{ "text": "ab", "times": 2 }""").GetProperty("result").GetString());
        Assert.Equal(new ActionCaller(1, ActionSource.External), module.Calls[0].Caller);
        Assert.Equal(connection.Id.ToString(), module.Calls[0].ConnectionId);
        Assert.Equal(Codes.InvalidParams, Code(Send(endpoint, connection, "test.echo", """{ "text": "ab", "loud": true }""")));
        Assert.Equal(Codes.InvalidParams, Code(Send(endpoint, connection, "test.echo")));
        Assert.Equal(Codes.Forbidden, Code(Send(endpoint, connection, "test.wipe")));

        var refused = Send(endpoint, connection, "test.refuse").GetProperty("error");
        Assert.Equal(Codes.Rejected, refused.GetProperty("code").GetInt32());
        Assert.Equal("because", refused.GetProperty("data").GetProperty("detail").GetProperty("reason").GetString());
        Assert.Equal(Codes.MethodNotFound, Code(Send(endpoint, connection, "test.nothing")));
    }

    private sealed record BlobParams(string Data);

    [Fact]
    public void ModuleParamsAreOnlyBoundByTheMessageSize()
    {
        var endpoint = Endpoint();
        endpoint.AddModule(new Module(RpcMethod.Create<BlobParams>("test.blob", AgentScope.Admin, "Takes a big file.", (_, a) => RpcMethod.Value(a.Data.Length))));
        var big = new string('x', 2 * 1024 * 1024);
        Assert.Equal(big.Length, Send(endpoint, endpoint.Connect(), "test.blob", $$"""{ "data": "{{big}}" }""").GetProperty("result").GetInt32());
    }

    [Fact]
    public void AsyncMethodsAnswerWhenTheyFinish()
    {
        var gate = new TaskCompletionSource<JsonElement?>();
        var failGate = new TaskCompletionSource();
        var endpoint = Endpoint();
        endpoint.AddModule(new Module(
            RpcMethod.CreateAsync<EchoParams>("test.later", AgentScope.Observe, "Answers later.", (_, _) => gate.Task),
            RpcMethod.CreateAsync<NoArgs>("test.now", AgentScope.Observe, "Answers at once.", (_, _) => Task.FromResult<JsonElement?>(RpcMethod.Value(1))),
            RpcMethod.CreateAsync<NoArgs>("test.fails", AgentScope.Observe, "Fails later.",
                async (_, _) =>
                {
                    await failGate.Task;
                    throw new RpcException(RpcErrors.Rejected, "Nope.");
                })));
        var connection = endpoint.Connect();

        Assert.Equal(1, Send(endpoint, connection, "test.now").GetProperty("result").GetInt32());
        Assert.Null(endpoint.Handle(connection, """{ "jsonrpc": "2.0", "id": "x", "method": "test.later", "params": { "text": "a" } }""", 0));
        Assert.Equal(Codes.InvalidParams, Code(Send(endpoint, connection, "test.later", """{ "nope": 1 }""")));
        Assert.Empty(endpoint.DrainOutgoing(connection));

        gate.SetResult(RpcMethod.Value("done"));
        var answer = JsonDocument.Parse(Assert.Single(endpoint.DrainOutgoing(connection))).RootElement;
        Assert.Equal("x", answer.GetProperty("id").GetString());
        Assert.Equal("done", answer.GetProperty("result").GetString());

        var batch = endpoint.Handle(connection, """[{ "jsonrpc": "2.0", "id": 1, "method": "test.now" }, { "jsonrpc": "2.0", "id": 2, "method": "test.fails" }]""", 0);
        Assert.Single(JsonDocument.Parse(batch!).RootElement.EnumerateArray());
        Assert.Empty(endpoint.DrainOutgoing(connection));
        failGate.SetResult();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        IReadOnlyList<string> later = [];
        while (later.Count == 0 && DateTime.UtcNow < deadline)
            later = endpoint.DrainOutgoing(connection);
        var failure = JsonDocument.Parse(Assert.Single(later)).RootElement;
        Assert.Equal(2, failure.GetProperty("id").GetInt32());
        Assert.Equal(Codes.Rejected, Code(failure));
    }

    private sealed class Module(params RpcMethodInfo[] methods) : IRpcModule
    {
        public IEnumerable<RpcMethodInfo> Methods => methods;
    }

    [Fact]
    public void ModulesCantReplaceMethods()
    {
        var endpoint = Endpoint();
        endpoint.AddModule(new EchoModule());
        Assert.Throws<ArgumentException>(() => endpoint.AddModule(new EchoModule()));
        Assert.Throws<ArgumentException>(() => RpcMethod.Create<NoArgs>("PlacePart", AgentScope.Play, "", (_, _) => null));
        Assert.Throws<ArgumentException>(() => RpcMethod.Create<NoArgs>("nodots", AgentScope.Play, "", (_, _) => null));
    }

    [Fact]
    public void DescribeGivesSchemasScopesAndDescriptions()
    {
        var endpoint = Endpoint();
        endpoint.AddModule(new EchoModule());
        var result = Send(endpoint, endpoint.Connect(), "mechanica.describe").GetProperty("result");
        var methods = result.GetProperty("methods").EnumerateArray().ToDictionary(m => m.GetProperty("name").GetString()!);

        var place = methods["PlacePart"];
        Assert.Equal("action", place.GetProperty("kind").GetString());
        Assert.Equal("play", place.GetProperty("scope").GetString());
        Assert.Contains("placing", place.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
        var schema = place.GetProperty("params");
        Assert.Equal("object", schema.GetProperty("type").GetString());
        var properties = schema.GetProperty("properties");
        Assert.Equal(3, properties.GetProperty("cell").GetProperty("minItems").GetInt32());
        Assert.Equal("integer", properties.GetProperty("cell").GetProperty("items").GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(properties.GetProperty("cell").GetProperty("description").GetString()));
        Assert.True(properties.TryGetProperty("rotation", out _));
        Assert.Contains("part", schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.DoesNotContain("rotation", schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));

        Assert.Equal("query", methods["Look"].GetProperty("kind").GetString());
        Assert.Equal("observe", methods["Look"].GetProperty("scope").GetString());
        Assert.Equal("admin", methods["SpawnBot"].GetProperty("scope").GetString());
        Assert.Equal("method", methods["test.echo"].GetProperty("kind").GetString());
        Assert.Equal("Repeats text.", methods["test.echo"].GetProperty("description").GetString());
        Assert.True(methods["test.echo"].GetProperty("params").GetProperty("properties").TryGetProperty("times", out _));
        Assert.True(methods.ContainsKey("mechanica.subscribe"));
        Assert.True(result.GetProperty("topics").TryGetProperty("hurt", out _));

        var one = Send(endpoint, endpoint.Connect(), "mechanica.describe", """{ "names": ["Look"] }""").GetProperty("result");
        Assert.Single(one.GetProperty("methods").EnumerateArray());
        Assert.Equal(Codes.InvalidParams, Code(Send(endpoint, endpoint.Connect(), "mechanica.describe", """{ "names": ["Fly"] }""")));
    }

    [Fact]
    public void InfoReportsTickScopesAndCaller()
    {
        _log.Tick(7);
        var endpoint = Endpoint(new EndpointAccess().AddToken("watch", AgentScope.Observe));
        var connection = endpoint.Connect();
        Send(endpoint, connection, "mechanica.auth", """{ "token": "watch" }""");
        var info = Send(endpoint, connection, "mechanica.info").GetProperty("result");

        Assert.Equal(2, info.GetProperty("protocol_version").GetInt32());
        Assert.Equal(7, info.GetProperty("tick").GetInt64());
        Assert.Equal(["observe"], info.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(1, info.GetProperty("peer").GetInt32());
        Assert.Contains("mechanica.describe", info.GetProperty("methods").EnumerateArray().Select(s => s.GetString()));
    }

    [Fact]
    public void SubscribersGetTheirTopicsOnly()
    {
        var endpoint = Endpoint();
        var a = endpoint.Connect();
        var b = endpoint.Connect();
        Assert.Equal(["chat", "hurt"], Send(endpoint, a, "mechanica.subscribe", """{ "topics": ["hurt", "chat"] }""")
            .GetProperty("result").EnumerateArray().Select(t => t.GetString()));
        Assert.False(_events.HasSubscribers("raid"));

        _events.Publish("hurt", new { peer = 1, amount = 5.5f, cause = "fall" });
        _events.Publish("raid", new { level = 3 });

        var pending = endpoint.DrainEvents(a);
        var note = JsonDocument.Parse(Assert.Single(pending)).RootElement;
        Assert.Equal("mechanica.event", note.GetProperty("method").GetString());
        Assert.False(note.TryGetProperty("id", out _));
        Assert.Equal("hurt", note.GetProperty("params").GetProperty("topic").GetString());
        Assert.Equal(42, note.GetProperty("params").GetProperty("tick").GetInt64());
        Assert.Equal("fall", note.GetProperty("params").GetProperty("data").GetProperty("cause").GetString());
        Assert.Empty(endpoint.DrainEvents(a));
        Assert.Empty(endpoint.DrainEvents(b));

        Assert.Equal(["chat"], Send(endpoint, a, "mechanica.unsubscribe", """{ "topics": ["hurt"] }""")
            .GetProperty("result").EnumerateArray().Select(t => t.GetString()));
        _events.Publish("hurt", new { peer = 1 });
        Assert.Empty(endpoint.DrainEvents(a));
        Assert.Equal(Codes.InvalidParams, Code(Send(endpoint, a, "mechanica.subscribe", """{ "topics": ["weather"] }""")));

        endpoint.Disconnect(a);
        Assert.False(_events.HasSubscribers("chat"));
    }

    [Fact]
    public void SlowSubscribersAreToldWhatTheyMissed()
    {
        var endpoint = Endpoint();
        var connection = endpoint.Connect();
        Send(endpoint, connection, "mechanica.subscribe", """{ "topics": ["chat"] }""");
        for (var i = 0; i < RemoteEvents.MaxPending + 10; i++)
            _events.Publish("chat", new { i });

        var pending = endpoint.DrainEvents(connection);
        Assert.Equal(RemoteEvents.MaxPending + 1, pending.Count);
        var notice = JsonDocument.Parse(pending[0]).RootElement.GetProperty("params");
        Assert.Equal("events_dropped", notice.GetProperty("topic").GetString());
        Assert.Equal(10, notice.GetProperty("data").GetProperty("count").GetInt32());
        Assert.Equal(10, JsonDocument.Parse(pending[1]).RootElement.GetProperty("params").GetProperty("data").GetProperty("i").GetInt32());
    }

    [Fact]
    public void LazyPayloadsAreOnlyBuiltForListeners()
    {
        var built = 0;
        _events.Publish("quest", () => ++built);
        Assert.Equal(0, built);
    }

    [Fact]
    public void ActionsTopicCarriesOtherCallersButNotScriptsOrQueries()
    {
        var endpoint = Endpoint();
        var connection = endpoint.Connect();
        Send(endpoint, connection, "mechanica.subscribe", """{ "topics": ["actions"] }""");
        using var watch = _events.WatchActions(_dispatcher);

        _dispatcher.Dispatch(new ActionCaller(2, ActionSource.Player), "PlacePart", """{ "part": "vanilla:block", "cell": [1, 2, 3], "position": [0, 0, 0] }""");
        _dispatcher.Dispatch(new ActionCaller(1, ActionSource.Script), "SpawnBot", "{}");
        _dispatcher.Dispatch(new ActionCaller(2, ActionSource.Player), "Look", "{}");

        var data = JsonDocument.Parse(Assert.Single(endpoint.DrainEvents(connection))).RootElement.GetProperty("params").GetProperty("data");
        Assert.Equal(2, data.GetProperty("peer").GetInt32());
        Assert.Equal("player", data.GetProperty("source").GetString());
        Assert.Equal("PlacePart", data.GetProperty("action").GetString());
        Assert.Equal(3, data.GetProperty("args").GetProperty("cell")[2].GetInt32());
        Assert.True(data.GetProperty("ok").GetBoolean());
        Assert.Equal("vanilla:block", data.GetProperty("value").GetProperty("placed").GetString());
    }

    [Fact]
    public void InternalErrorsAreRaisedForLogging()
    {
        var endpoint = Endpoint();
        var errors = new List<(string Method, string Message)>();
        endpoint.InternalError += (method, message) => errors.Add((method, message));

        Assert.Equal(Codes.InternalError, Code(Send(endpoint, endpoint.Connect(), "Crash")));
        Assert.Equal("Crash", Assert.Single(errors).Method);
        Assert.Contains("boom", errors[0].Message);
    }

    [Fact]
    public void ReplayNeedsAdmin()
    {
        var endpoint = Endpoint(new EndpointAccess().AddToken("play", AgentScope.Observe, AgentScope.Play));
        var connection = endpoint.Connect();
        Send(endpoint, connection, "mechanica.auth", """{ "token": "play" }""");
        Assert.Equal(Codes.Forbidden, Code(Send(endpoint, connection, "mechanica.replay.start", """{ "recording": { "tick_rate": 60, "actions": [] } }""")));
        Assert.Null(Code(Send(endpoint, connection, "mechanica.record.start")));
    }
}

public class RpcSchemaTests
{
    public static TheoryData<Type> ArgumentTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(ActionDispatcher).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition && t.Name.EndsWith("Args", StringComparison.Ordinal))
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
            data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(ArgumentTypes))]
    public void EveryArgumentRecordHasAnObjectSchema(Type type)
    {
        var schema = RpcSchema.SchemaOf(type).AsObject();
        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }
}
