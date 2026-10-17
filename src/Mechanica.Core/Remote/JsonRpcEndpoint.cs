using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;

namespace Mechanica.Core.Remote;

/// <summary>One external agent's connection to the endpoint.</summary>
/// <param name="Id">Connection id, unique for the endpoint's lifetime.</param>
public sealed class RemoteConnection(int id)
{
    private readonly HashSet<AgentScope> _scopes = [];

    /// <summary>Connection id.</summary>
    public int Id { get; } = id;

    /// <summary>True once the agent gave a valid token (or when the endpoint has none).</summary>
    public bool Authenticated { get; internal set; }

    /// <summary>What this connection may do.</summary>
    public IReadOnlySet<AgentScope> Scopes => _scopes;

    /// <summary>Who this connection's calls act as. Starts as an external agent on the host's peer.</summary>
    public ActionCaller Caller { get; set; }

    internal RemoteEvents.Subscriber? Subscriber { get; set; }

    internal List<(JsonElement? Id, string Method, Task<JsonElement?> Task)> Pending { get; } = [];

    internal void Grant(IEnumerable<AgentScope> scopes) => _scopes.UnionWith(scopes);
}

/// <summary>
/// The external AI endpoint's protocol (§1, §3): JSON-RPC 2.0 over text messages. Every registered
/// action is a method of the same name whose <c>params</c> are the action's arguments, and it goes
/// through the host's <see cref="ActionDispatcher"/> exactly like a player's call, as the connection's
/// <see cref="RemoteConnection.Caller"/>. <c>mechanica.*</c> methods describe the game (with JSON
/// Schemas), subscribe to pushed events and control record/replay, and <see cref="IRpcModule"/>s add more
/// methods. Each connection holds <see cref="AgentScope"/>s that decide what it may call. The transport
/// (WebSocket) lives in the game layer.
/// <para>
/// Action failures map to JSON-RPC errors whose <c>data.code</c> is the <see cref="ActionErrors"/> code:
/// <c>unknown_action</c> is -32601, <c>bad_args</c> -32602, <c>rejected</c> -32001, <c>rate_limited</c>
/// -32002 and <c>internal_error</c> -32603. A missing scope is -32005 with <c>data.code</c> <c>forbidden</c>.
/// </para>
/// </summary>
public sealed class JsonRpcEndpoint
{
    /// <summary>Version of the <c>mechanica.*</c> method set and error mapping.</summary>
    public const int ProtocolVersion = 2;

    /// <summary>Largest message accepted, in characters (a replayed recording can be big).</summary>
    public const int MaxMessageLength = 4 * 1024 * 1024;

    /// <summary>Most requests in one batch.</summary>
    public const int MaxBatch = 256;

    /// <summary>JSON-RPC error codes this endpoint uses.</summary>
    public static class Codes
    {
        /// <summary>Not valid JSON.</summary>
        public const int ParseError = -32700;

        /// <summary>Not a JSON-RPC request.</summary>
        public const int InvalidRequest = -32600;

        /// <summary>No such method.</summary>
        public const int MethodNotFound = -32601;

        /// <summary>Params did not fit the method.</summary>
        public const int InvalidParams = -32602;

        /// <summary>The host failed while handling the call.</summary>
        public const int InternalError = -32603;

        /// <summary>Validation refused the action.</summary>
        public const int Rejected = -32001;

        /// <summary>Too many calls too quickly.</summary>
        public const int RateLimited = -32002;

        /// <summary>The connection must call <c>mechanica.auth</c> first.</summary>
        public const int Unauthorized = -32003;

        /// <summary>The request can't be done now (nothing recording, no replay, something busy).</summary>
        public const int Unavailable = -32004;

        /// <summary>The connection lacks the scope the method needs.</summary>
        public const int Forbidden = -32005;
    }

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 64,
    };

    private readonly ActionDispatcher _dispatcher;
    private readonly ActionLog _log;
    private readonly EndpointAccess _access;
    private readonly ActionRateLimiter _limiter;
    private readonly ActionCaller _caller;
    private readonly Dictionary<string, RpcMethodInfo> _methods = new(StringComparer.Ordinal);
    private readonly HashSet<string> _builtIns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonNode> _schemas = new(StringComparer.Ordinal);
    private int _nextConnection;

    /// <summary>Creates the endpoint with a single optional token that grants every scope.</summary>
    /// <param name="dispatcher">The host's dispatcher.</param>
    /// <param name="log">Record/replay for the <c>mechanica.record.*</c> and <c>mechanica.replay.*</c> methods.</param>
    /// <param name="hostPeer">Peer id external calls are made for (the host's own).</param>
    /// <param name="token">When set, each connection must call <c>mechanica.auth</c> with it before anything else.</param>
    /// <param name="limiter">Per-connection rate limit; null allows 600 calls/s with bursts of 1200.</param>
    public JsonRpcEndpoint(ActionDispatcher dispatcher, ActionLog log, int hostPeer, string? token = null, ActionRateLimiter? limiter = null)
        : this(dispatcher, log, hostPeer, new EndpointAccess().AddToken(token, [.. EndpointAccess.All]), null, limiter)
    {
    }

    /// <summary>Creates the endpoint.</summary>
    /// <param name="dispatcher">The host's dispatcher.</param>
    /// <param name="log">Record/replay for the <c>mechanica.record.*</c> and <c>mechanica.replay.*</c> methods.</param>
    /// <param name="hostPeer">Peer id external calls are made for (the host's own).</param>
    /// <param name="access">Tokens and the scopes they grant; with none every connection gets every scope.</param>
    /// <param name="events">Pushed notifications; null makes a private, silent one.</param>
    /// <param name="limiter">Per-connection rate limit; null allows 600 calls/s with bursts of 1200.</param>
    public JsonRpcEndpoint(ActionDispatcher dispatcher, ActionLog log, int hostPeer, EndpointAccess access, RemoteEvents? events, ActionRateLimiter? limiter = null)
    {
        _dispatcher = dispatcher;
        _log = log;
        _access = access;
        Events = events ?? new RemoteEvents();
        _caller = new ActionCaller(hostPeer, ActionSource.External);
        _limiter = limiter ?? new ActionRateLimiter(perSecond: 600, burst: 1200);
        foreach (var method in BuiltIns())
        {
            _methods.Add(method.Name, method);
            _builtIns.Add(method.Name);
        }
    }

    /// <summary>True when connections must authenticate.</summary>
    public bool RequiresToken => _access.RequiresToken;

    /// <summary>The notifications connections can subscribe to.</summary>
    public RemoteEvents Events { get; }

    /// <summary>
    /// Spawns and removes the players agents join as (<c>mechanica.join</c>, <c>mechanica.leave</c>). Null
    /// refuses joins.
    /// </summary>
    public IAgentHost? Agents { get; set; }

    /// <summary>Raised when a call fails inside the host (an exception), with the method and message, so the game can log it.</summary>
    public event Action<string, string>? InternalError;

    /// <summary>
    /// Adds a module's methods. Names must be new; <c>mechanica.*</c> built-ins and Action API names can't
    /// be replaced.
    /// </summary>
    public void AddModule(IRpcModule module)
    {
        var methods = module.Methods.ToList();
        foreach (var method in methods)
        {
            if (_methods.ContainsKey(method.Name) || methods.Count(m => m.Name == method.Name) > 1)
                throw new ArgumentException($"The endpoint method '{method.Name}' already exists.", nameof(module));
        }
        foreach (var method in methods)
            _methods.Add(method.Name, method);
    }

    /// <summary>Endpoint methods outside the Action API (built-ins and modules), sorted by name.</summary>
    public IReadOnlyList<RpcMethodInfo> Methods => [.. _methods.Values.OrderBy(m => m.Name, StringComparer.Ordinal)];

    /// <summary>Opens a connection. Without tokens it is authenticated with every scope.</summary>
    public RemoteConnection Connect()
    {
        var connection = new RemoteConnection(++_nextConnection) { Caller = _caller };
        if (!_access.RequiresToken)
        {
            connection.Authenticated = true;
            connection.Grant(EndpointAccess.All);
        }
        _contextConnections[connection.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = connection;
        return connection;
    }

    /// <summary>Forgets a closed connection.</summary>
    public void Disconnect(RemoteConnection connection)
    {
        if (connection.Caller != _caller)
            Agents?.Leave(ContextId(connection));
        connection.Caller = _caller;
        _limiter.Forget(connection.Id);
        _contextConnections.Remove(connection.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (connection.Subscriber is { } subscriber)
            Events.Remove(subscriber);
        connection.Subscriber = null;
        connection.Pending.Clear();
    }

    /// <summary>
    /// Takes everything waiting to be sent to <paramref name="connection"/>: answers to asynchronous calls
    /// that have finished, then pushed notifications. The transport calls this every frame.
    /// </summary>
    public IReadOnlyList<string> DrainOutgoing(RemoteConnection connection)
    {
        List<string>? list = null;
        for (var i = 0; i < connection.Pending.Count; i++)
        {
            var (id, method, task) = connection.Pending[i];
            if (!task.IsCompleted)
                continue;
            connection.Pending.RemoveAt(i--);
            var outcome = Finished(method, task);
            if (id is null)
                continue;
            (list ??= []).Add(Write(w =>
            {
                if (outcome.Error is { } error)
                    Error(w, id, error.Code, error.Message, error.DataCode, error.Detail);
                else
                    Result(w, id, outcome.Value);
                return true;
            }));
        }
        var events = DrainEvents(connection);
        if (list is null)
            return events;
        list.AddRange(events);
        return list;
    }

    /// <summary>Takes the notifications waiting for <paramref name="connection"/>, oldest first.</summary>
    public IReadOnlyList<string> DrainEvents(RemoteConnection connection) =>
        connection.Subscriber is { Count: > 0 } or { Dropped: > 0 } ? connection.Subscriber.Drain(Events) : [];

    /// <summary>
    /// Handles one message: a request, a notification or a batch. Returns the response text, or null when
    /// nothing needs answering (only notifications). Never throws.
    /// </summary>
    /// <param name="connection">Who sent it.</param>
    /// <param name="message">The message text.</param>
    /// <param name="now">Current time in seconds, for rate limiting.</param>
    public string? Handle(RemoteConnection connection, string message, double now)
    {
        if (message.Length > MaxMessageLength)
            return Write(w => Error(w, null, Codes.InvalidRequest, $"Messages are limited to {MaxMessageLength} characters.", null));
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(message, DocumentOptions);
        }
        catch (JsonException e)
        {
            return Write(w => Error(w, null, Codes.ParseError, $"Parse error: {e.Message}", null));
        }
        using (document)
        {
            var root = document.RootElement;
            var answered = false;
            if (root.ValueKind != JsonValueKind.Array)
            {
                var single = Write(w => answered = HandleOne(w, connection, root, now));
                return answered ? single : null;
            }
            if (root.GetArrayLength() == 0)
                return Write(w => Error(w, null, Codes.InvalidRequest, "An empty batch is not a request.", null));
            if (root.GetArrayLength() > MaxBatch)
                return Write(w => Error(w, null, Codes.InvalidRequest, $"Batches are limited to {MaxBatch} requests.", null));
            var text = Write(w =>
            {
                w.WriteStartArray();
                foreach (var request in root.EnumerateArray())
                    answered |= HandleOne(w, connection, request, now);
                w.WriteEndArray();
                return answered;
            });
            return answered ? text : null;
        }
    }

    /// <summary>Writes the response to one request; returns false (writing nothing) for a notification.</summary>
    private bool HandleOne(Utf8JsonWriter w, RemoteConnection connection, JsonElement request, double now)
    {
        if (request.ValueKind != JsonValueKind.Object
            || !request.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0"
            || !request.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
        {
            Error(w, IdOf(request), Codes.InvalidRequest, "A request is an object with \"jsonrpc\": \"2.0\" and a string \"method\".", null);
            return true;
        }
        var hasId = request.TryGetProperty("id", out var id);
        if (hasId && id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null))
        {
            Error(w, null, Codes.InvalidRequest, "id must be a string, a number or null.", null);
            return true;
        }
        JsonElement? idValue = hasId ? id.Clone() : null;

        var method = methodElement.GetString()!;
        string paramsJson = "{}";
        if (request.TryGetProperty("params", out var parameters))
        {
            if (parameters.ValueKind != JsonValueKind.Object)
            {
                if (hasId)
                    Error(w, idValue, Codes.InvalidParams, "params must be an object of named arguments.", ActionErrors.BadArgs);
                return hasId;
            }
            paramsJson = parameters.GetRawText();
        }

        var outcome = Call(connection, method, paramsJson, parameters, now);
        if (outcome.Later is { } later)
        {
            connection.Pending.Add((idValue, method, later));
            return false;
        }
        if (!hasId)
            return false;
        if (outcome.Error is { } error)
            Error(w, idValue, error.Code, error.Message, error.DataCode, error.Detail);
        else
            Result(w, idValue, outcome.Value);
        return true;
    }

    private readonly record struct Failure(int Code, string Message, string? DataCode, JsonElement? Detail = null);

    private readonly record struct Outcome(JsonElement? Value, Failure? Error, Task<JsonElement?>? Later = null)
    {
        public static Outcome Ok(JsonElement? value = null) => new(value, null);

        public static Outcome Pending(Task<JsonElement?> task) => new(null, null, task);

        public static Outcome Fail(int code, string message, string? dataCode = null, JsonElement? detail = null) =>
            new(null, new Failure(code, message, dataCode, detail));
    }

    private Outcome Call(RemoteConnection connection, string method, string paramsJson, JsonElement parameters, double now)
    {
        if (method == "mechanica.auth")
            return Authenticate(connection, parameters);
        if (!connection.Authenticated)
            return Outcome.Fail(Codes.Unauthorized, "Call mechanica.auth with the endpoint's token first.");
        // The host can remove an agent's player without a leave (it left the world): act as the host again.
        if (connection.Caller != _caller && Agents?.PeerOf(ContextId(connection)) != connection.Caller.Peer)
            connection.Caller = _caller;
        try
        {
            if (_methods.TryGetValue(method, out var info))
                return Invoke(connection, info, parameters, now);
            if (method.StartsWith("mechanica.", StringComparison.Ordinal) || method.StartsWith("rpc.", StringComparison.Ordinal) || method.Contains('.'))
                return Outcome.Fail(Codes.MethodNotFound, $"No method named '{Shorten(method)}'.", ActionErrors.UnknownAction);
            return Dispatch(connection, method, paramsJson, now);
        }
        catch (Exception e)
        {
            InternalError?.Invoke(method, $"{e.GetType().Name}: {e.Message}");
            return Outcome.Fail(Codes.InternalError, $"{e.GetType().Name}: {e.Message}", ActionErrors.Internal);
        }
    }

    private Outcome Invoke(RemoteConnection connection, RpcMethodInfo info, JsonElement parameters, double now)
    {
        if (!connection.Scopes.Contains(info.Scope))
            return Forbidden(info.Name, info.Scope);
        if (!_builtIns.Contains(info.Name) && !_limiter.TryAcquire(connection.Id, now))
            return Outcome.Fail(Codes.RateLimited, "Too many calls; slow down.", ActionErrors.RateLimited);
        try
        {
            var context = new RpcContext(connection.Caller, connection.Scopes, ContextId(connection));
            if (info.InvokeAsync is { } start)
            {
                var task = start(context, parameters);
                return task.IsCompleted ? Finished(info.Name, task) : Outcome.Pending(task);
            }
            return Outcome.Ok(info.Invoke!(context, parameters));
        }
        catch (RpcException e)
        {
            return Refused(e);
        }
    }

    private Outcome Finished(string method, Task<JsonElement?> task)
    {
        if (task.IsCompletedSuccessfully)
            return Outcome.Ok(task.Result);
        var error = task.Exception?.InnerException ?? new OperationCanceledException("The call was cancelled.");
        if (error is RpcException refused)
            return Refused(refused);
        InternalError?.Invoke(method, $"{error.GetType().Name}: {error.Message}");
        return Outcome.Fail(Codes.InternalError, $"{error.GetType().Name}: {error.Message}", ActionErrors.Internal);
    }

    private static Outcome Refused(RpcException e)
    {
        var dataCode = e.Code switch
        {
            Codes.Rejected => ActionErrors.Rejected,
            Codes.InvalidParams => ActionErrors.BadArgs,
            Codes.Forbidden => "forbidden",
            _ => null,
        };
        JsonElement? detail = e.Detail is null ? null : JsonSerializer.SerializeToElement(e.Detail, ActionJson.Options);
        return Outcome.Fail(e.Code, e.Message, dataCode, detail);
    }

    private Outcome Dispatch(RemoteConnection connection, string method, string paramsJson, double now)
    {
        if (!_dispatcher.Has(method))
            return Outcome.Fail(Codes.MethodNotFound, $"No method named '{Shorten(method)}'.", ActionErrors.UnknownAction);
        var scope = AgentPermissions.ScopeOf(_dispatcher, method);
        if (!connection.Scopes.Contains(scope))
            return Forbidden(method, scope);
        if (!_limiter.TryAcquire(connection.Id, now))
            return Outcome.Fail(Codes.RateLimited, "Too many calls; slow down.", ActionErrors.RateLimited);
        var result = _dispatcher.Dispatch(connection.Caller, method, paramsJson);
        if (result.Ok)
            return Outcome.Ok(result.Value);
        var code = result.Code switch
        {
            ActionErrors.UnknownAction => Codes.MethodNotFound,
            ActionErrors.BadArgs => Codes.InvalidParams,
            ActionErrors.Rejected => Codes.Rejected,
            ActionErrors.RateLimited => Codes.RateLimited,
            _ => Codes.InternalError,
        };
        if (code == Codes.InternalError)
            InternalError?.Invoke(method, result.Message ?? result.Code ?? "failed");
        return Outcome.Fail(code, result.Message ?? result.Code ?? "failed", result.Code);
    }

    private static Outcome Forbidden(string method, AgentScope scope) =>
        Outcome.Fail(Codes.Forbidden, $"'{Shorten(method)}' needs the {scope.ToString().ToLowerInvariant()} scope, which this connection doesn't have.", "forbidden");

    private Outcome Authenticate(RemoteConnection connection, JsonElement parameters)
    {
        if (!_access.RequiresToken)
            return Outcome.Ok(Value(true));
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("token", out var token) || token.ValueKind != JsonValueKind.String)
            return Outcome.Fail(Codes.InvalidParams, "mechanica.auth takes { \"token\": \"...\" }.", ActionErrors.BadArgs);
        if (_access.ScopesFor(token.GetString()!) is not { } scopes)
            return Outcome.Fail(Codes.Unauthorized, "Wrong token.");
        connection.Authenticated = true;
        connection.Grant(scopes);
        return Outcome.Ok(Value(true));
    }

    // ---- Built-in methods ----

    private sealed record AuthParams(string Token);

    private sealed record DescribeParams(IReadOnlyList<string>? Names = null);

    private sealed record TopicsParams(IReadOnlyList<string>? Topics = null);

    private sealed record ReplayParams(JsonElement Recording, bool Timed = true);

    /// <summary>Params of <c>mechanica.join</c>.</summary>
    /// <param name="Name">The agent's name. Its saved player (inventory, health, position) follows the name, so use the same one to come back.</param>
    private sealed record JoinParams(string Name);

    private static string ContextId(RemoteConnection connection) => connection.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private IEnumerable<RpcMethodInfo> BuiltIns()
    {
        yield return RpcMethod.Create<AuthParams>("mechanica.auth", AgentScope.Observe,
            "Gives the endpoint's token. Needed first when the host set one; grants that token's scopes.",
            (_, _) => throw new InvalidOperationException("mechanica.auth is handled before routing."));
        yield return RpcMethod.Create<NoArgs>("mechanica.info", AgentScope.Observe,
            "Game name, protocol version, tick rate, the current tick, this connection's scopes and caller, and the names of every action and query.",
            (context, _) => Value(new
            {
                game = MechanicaInfo.GameName,
                protocol_version = ProtocolVersion,
                tick_rate = _log.TickRate,
                tick = _log.CurrentTick,
                scopes = context.Scopes.Order().Select(s => s.ToString().ToLowerInvariant()),
                peer = context.Caller.Peer,
                source = context.Caller.Source,
                actions = _dispatcher.Names,
                queries = _dispatcher.Names.Where(_dispatcher.IsReadOnly).ToList(),
                methods = _methods.Keys.Order(StringComparer.Ordinal),
                topics = RemoteEvents.Topics.Keys,
            }));
        yield return RpcMethod.Create<DescribeParams>("mechanica.describe", AgentScope.Observe,
            "Every method (Action API actions and queries, built-ins and module methods) with its scope, description and a JSON Schema of its params, plus the event topics. Pass names to describe only those.",
            (_, args) => Describe(args.Names));
        yield return RpcMethod.Create<TopicsParams>("mechanica.subscribe", AgentScope.Observe,
            "Starts pushing mechanica.event notifications for the given topics (see mechanica.describe's topics). Returns the topics now subscribed.",
            (context, args) => Subscribe(Connection(context), args.Topics ?? throw new RpcException(RpcErrors.BadParams, "Give the topics to subscribe to."), add: true));
        yield return RpcMethod.Create<TopicsParams>("mechanica.unsubscribe", AgentScope.Observe,
            "Stops the given topics, or every topic when none are given. Returns the topics still subscribed.",
            (context, args) => Subscribe(Connection(context), args.Topics, add: false));
        yield return RpcMethod.Create<JoinParams>("mechanica.join", AgentScope.Play,
            "Joins the world as a player of your own, marked as an AI. From then on this connection's actions act as that player (its inventory, reach and quests), and SetPlayerInput moves it. Returns its peer, profile id and shown name.",
            (context, args) =>
            {
                var connection = Connection(context);
                if (Agents is not { } agents)
                    throw new RpcException(RpcErrors.Busy, "This host doesn't take agent players.");
                if (connection.Caller != _caller)
                    throw new RpcException(RpcErrors.Rejected, "This connection already plays; call mechanica.leave first.");
                if (agents.Join(context.ConnectionId, args.Name, out var error) is not { } agent)
                    throw new RpcException(RpcErrors.Rejected, error ?? "The host refused the join.");
                connection.Caller = new ActionCaller(agent.Peer, ActionSource.Player);
                return Value(new { peer = agent.Peer, profile_id = agent.Profile.Id, name = agent.Profile.Name });
            });
        yield return RpcMethod.Create<NoArgs>("mechanica.leave", AgentScope.Play,
            "Removes the player this connection joined as (it is saved) and goes back to acting as an external agent on the host.",
            (context, _) =>
            {
                var connection = Connection(context);
                if (connection.Caller == _caller)
                    throw new RpcException(RpcErrors.Busy, "This connection isn't playing.");
                Agents?.Leave(context.ConnectionId);
                connection.Caller = _caller;
                return null;
            });
        yield return RpcMethod.Create<NoArgs>("mechanica.record.start", AgentScope.Observe,
            "Starts recording every state-changing Action API call on the host.",
            (_, _) => _log.StartRecording() ? null : throw new RpcException(RpcErrors.Busy, "A recording is already running."));
        yield return RpcMethod.Create<NoArgs>("mechanica.record.stop", AgentScope.Observe,
            "Stops recording and returns the recording.",
            (_, _) => _log.StopRecording() is { } recording ? recording.ToElement() : throw new RpcException(RpcErrors.Busy, "Nothing is being recorded."));
        yield return RpcMethod.Create<ReplayParams>("mechanica.replay.start", AgentScope.Admin,
            "Replays a recording through the validator as this connection, with the recorded tick spacing unless timed is false. Returns the number of calls it will replay.",
            (context, args) =>
            {
                ActionRecording recording;
                try
                {
                    recording = ActionRecording.FromElement(args.Recording);
                }
                catch (FormatException e)
                {
                    throw new RpcException(RpcErrors.BadParams, e.Message);
                }
                return Value(new { total = _log.StartReplay(recording, context.Caller, args.Timed) });
            });
        yield return RpcMethod.Create<NoArgs>("mechanica.replay.status", AgentScope.Observe,
            "Progress of the current or last replay: running, dispatched, total and mismatches.",
            (_, _) => _log.ReplayStatus is { } status ? Value(status) : throw new RpcException(RpcErrors.Busy, "No replay has started."));
        yield return RpcMethod.Create<NoArgs>("mechanica.replay.stop", AgentScope.Admin,
            "Stops a replay in progress.",
            (_, _) =>
            {
                _log.StopReplay();
                return null;
            });
    }

    private readonly Dictionary<string, RemoteConnection> _contextConnections = new(StringComparer.Ordinal);

    private RemoteConnection Connection(RpcContext context) =>
        _contextConnections.TryGetValue(context.ConnectionId, out var connection)
            ? connection
            : throw new InvalidOperationException("The connection is gone.");

    private JsonElement Subscribe(RemoteConnection connection, IReadOnlyList<string>? topics, bool add)
    {
        if (topics is not null)
            foreach (var topic in topics)
                if (!RemoteEvents.Topics.ContainsKey(topic) || topic == "events_dropped")
                    throw new RpcException(RpcErrors.BadParams, $"There is no event topic '{Shorten(topic)}'. Topics: {string.Join(", ", RemoteEvents.Topics.Keys.Where(t => t != "events_dropped"))}.");
        var subscriber = connection.Subscriber ??= Events.Add();
        if (add)
            subscriber.Topics.UnionWith(topics!);
        else if (topics is null)
            subscriber.Topics.Clear();
        else
            subscriber.Topics.ExceptWith(topics);
        return Value(subscriber.Topics.Order(StringComparer.Ordinal).ToList());
    }

    private JsonElement Describe(IReadOnlyList<string>? names)
    {
        var wanted = names is null ? null : new HashSet<string>(names, StringComparer.Ordinal);
        if (wanted is not null)
            foreach (var name in wanted)
                if (!_dispatcher.Has(name) && !_methods.ContainsKey(name))
                    throw new RpcException(RpcErrors.BadParams, $"No method named '{Shorten(name)}'.");
        var list = new JsonArray();
        foreach (var name in _dispatcher.Names)
        {
            if (wanted is not null && !wanted.Contains(name))
                continue;
            var type = _dispatcher.ArgsTypeOf(name)!;
            var entry = new JsonObject
            {
                ["name"] = name,
                ["kind"] = _dispatcher.IsReadOnly(name) ? "query" : "action",
                ["scope"] = AgentPermissions.ScopeOf(_dispatcher, name).ToString().ToLowerInvariant(),
                ["description"] = RpcSchema.DescribeAction(name, type),
                ["params"] = Schema(name, type),
            };
            if (_dispatcher.MaxArgsOf(name) != ActionJson.MaxArgsLength)
                entry["max_params_length"] = _dispatcher.MaxArgsOf(name);
            list.Add(entry);
        }
        foreach (var method in Methods)
        {
            if (wanted is not null && !wanted.Contains(method.Name))
                continue;
            list.Add(new JsonObject
            {
                ["name"] = method.Name,
                ["kind"] = "method",
                ["scope"] = method.Scope.ToString().ToLowerInvariant(),
                ["description"] = method.Description,
                ["params"] = Schema(method.Name, method.ParamsType),
            });
        }
        var topics = new JsonObject();
        foreach (var (topic, about) in RemoteEvents.Topics)
            topics[topic] = about;
        var document = new JsonObject
        {
            ["protocol_version"] = ProtocolVersion,
            ["methods"] = list,
            ["topics"] = topics,
            ["errors"] = new JsonObject
            {
                ["-32700"] = "not valid JSON",
                ["-32600"] = "not a JSON-RPC request, or an empty or oversized batch",
                ["-32601"] = "no such method (data.code unknown_action)",
                ["-32602"] = "params don't fit the method (data.code bad_args)",
                ["-32603"] = "the host failed while handling the call (data.code internal_error)",
                ["-32001"] = "the host's validation refused it; message says why (data.code rejected)",
                ["-32002"] = "too many calls (data.code rate_limited)",
                ["-32003"] = "call mechanica.auth first, or the token was wrong",
                ["-32004"] = "can't be done now (nothing recording, no replay, busy)",
                ["-32005"] = "this connection lacks the method's scope (data.code forbidden)",
            },
        };
        return JsonSerializer.SerializeToElement(document);
    }

    private JsonNode Schema(string name, Type type)
    {
        if (!_schemas.TryGetValue(name, out var schema))
            _schemas[name] = schema = RpcSchema.SchemaOf(type);
        return schema.DeepClone();
    }

    private static JsonElement? IdOf(JsonElement request) =>
        request.ValueKind == JsonValueKind.Object && request.TryGetProperty("id", out var id)
            && id.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? id
            : null;

    private static void Result(Utf8JsonWriter w, JsonElement? id, JsonElement? value)
    {
        w.WriteStartObject();
        w.WriteString("jsonrpc", "2.0");
        w.WritePropertyName("result");
        if (value is { } v)
            v.WriteTo(w);
        else
            w.WriteNullValue();
        WriteId(w, id);
        w.WriteEndObject();
    }

    private static bool Error(Utf8JsonWriter w, JsonElement? id, int code, string message, string? dataCode, JsonElement? detail = null)
    {
        w.WriteStartObject();
        w.WriteString("jsonrpc", "2.0");
        w.WriteStartObject("error");
        w.WriteNumber("code", code);
        w.WriteString("message", message);
        if (dataCode is not null || detail is not null)
        {
            w.WriteStartObject("data");
            if (dataCode is not null)
                w.WriteString("code", dataCode);
            if (detail is { } d)
            {
                w.WritePropertyName("detail");
                d.WriteTo(w);
            }
            w.WriteEndObject();
        }
        w.WriteEndObject();
        WriteId(w, id);
        w.WriteEndObject();
        return true;
    }

    private static void WriteId(Utf8JsonWriter w, JsonElement? id)
    {
        w.WritePropertyName("id");
        if (id is { } value)
            value.WriteTo(w);
        else
            w.WriteNullValue();
    }

    private static string Write(Func<Utf8JsonWriter, bool> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            write(writer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);

    private static string Shorten(string text) => text.Length <= 64 ? text : text[..64] + "…";
}
