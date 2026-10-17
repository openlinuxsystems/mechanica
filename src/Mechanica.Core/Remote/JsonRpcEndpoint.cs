using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mechanica.Core.Actions;

namespace Mechanica.Core.Remote;

/// <summary>One external agent's connection to the endpoint.</summary>
/// <param name="Id">Connection id, unique for the endpoint's lifetime.</param>
public sealed class RemoteConnection(int id)
{
    /// <summary>Connection id.</summary>
    public int Id { get; } = id;

    /// <summary>True once the agent gave the right token (or when the endpoint has none).</summary>
    public bool Authenticated { get; internal set; }
}

/// <summary>
/// The external AI endpoint's protocol (§1, §3): JSON-RPC 2.0 over text messages. Every registered
/// action is a method of the same name whose <c>params</c> are the action's arguments, and it goes
/// through the host's <see cref="ActionDispatcher"/> exactly like a player's call, as an
/// <see cref="ActionSource.External"/> caller on the host's peer. A few <c>mechanica.*</c> methods
/// describe the game and control record/replay. The transport (WebSocket) lives in the game layer.
/// <para>
/// Action failures map to JSON-RPC errors whose <c>data.code</c> is the <see cref="ActionErrors"/> code:
/// <c>unknown_action</c> is -32601, <c>bad_args</c> -32602, <c>rejected</c> -32001, <c>rate_limited</c>
/// -32002 and <c>internal_error</c> -32603.
/// </para>
/// </summary>
public sealed class JsonRpcEndpoint
{
    /// <summary>Version of the <c>mechanica.*</c> method set and error mapping.</summary>
    public const int ProtocolVersion = 1;

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

        /// <summary>The recording or replay request can't be done now.</summary>
        public const int Unavailable = -32004;
    }

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 64,
    };

    private readonly ActionDispatcher _dispatcher;
    private readonly ActionLog _log;
    private readonly byte[]? _token;
    private readonly ActionRateLimiter _limiter;
    private readonly ActionCaller _caller;
    private int _nextConnection;

    /// <summary>Creates the endpoint.</summary>
    /// <param name="dispatcher">The host's dispatcher.</param>
    /// <param name="log">Record/replay for the <c>mechanica.record.*</c> and <c>mechanica.replay.*</c> methods.</param>
    /// <param name="hostPeer">Peer id external calls are made for (the host's own).</param>
    /// <param name="token">When set, each connection must call <c>mechanica.auth</c> with it before anything else.</param>
    /// <param name="limiter">Per-connection rate limit; null allows 600 calls/s with bursts of 1200.</param>
    public JsonRpcEndpoint(ActionDispatcher dispatcher, ActionLog log, int hostPeer, string? token = null, ActionRateLimiter? limiter = null)
    {
        _dispatcher = dispatcher;
        _log = log;
        _caller = new ActionCaller(hostPeer, ActionSource.External);
        _token = string.IsNullOrEmpty(token) ? null : Encoding.UTF8.GetBytes(token);
        _limiter = limiter ?? new ActionRateLimiter(perSecond: 600, burst: 1200);
    }

    /// <summary>True when connections must authenticate.</summary>
    public bool RequiresToken => _token is not null;

    /// <summary>Opens a connection.</summary>
    public RemoteConnection Connect() => new(++_nextConnection) { Authenticated = _token is null };

    /// <summary>Forgets a closed connection.</summary>
    public void Disconnect(RemoteConnection connection) => _limiter.Forget(connection.Id);

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
        JsonElement? idValue = hasId ? id : null;

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
        if (!hasId)
            return false;
        if (outcome.Error is { } error)
            Error(w, idValue, error.Code, error.Message, error.DataCode);
        else
            Result(w, idValue, outcome.Value);
        return true;
    }

    private readonly record struct Failure(int Code, string Message, string? DataCode);

    private readonly record struct Outcome(JsonElement? Value, Failure? Error)
    {
        public static Outcome Ok(JsonElement? value = null) => new(value, null);

        public static Outcome Fail(int code, string message, string? dataCode = null) => new(null, new Failure(code, message, dataCode));
    }

    private Outcome Call(RemoteConnection connection, string method, string paramsJson, JsonElement parameters, double now)
    {
        if (method == "mechanica.auth")
            return Authenticate(connection, parameters);
        if (!connection.Authenticated)
            return Outcome.Fail(Codes.Unauthorized, "Call mechanica.auth with the endpoint's token first.");
        try
        {
            return method switch
            {
                "mechanica.info" => NoParams(parameters) ?? Outcome.Ok(Value(new
                {
                    game = MechanicaInfo.GameName,
                    protocol_version = ProtocolVersion,
                    tick_rate = _log.TickRate,
                    actions = _dispatcher.Names,
                    queries = _dispatcher.Names.Where(_dispatcher.IsReadOnly).ToList(),
                })),
                "mechanica.record.start" => NoParams(parameters) ?? (_log.StartRecording()
                    ? Outcome.Ok()
                    : Outcome.Fail(Codes.Unavailable, "A recording is already running.")),
                "mechanica.record.stop" => NoParams(parameters) ?? (_log.StopRecording() is { } recording
                    ? Outcome.Ok(recording.ToElement())
                    : Outcome.Fail(Codes.Unavailable, "Nothing is being recorded.")),
                "mechanica.replay.start" => StartReplay(parameters),
                "mechanica.replay.status" => NoParams(parameters) ?? (_log.ReplayStatus is { } status
                    ? Outcome.Ok(Value(status))
                    : Outcome.Fail(Codes.Unavailable, "No replay has started.")),
                "mechanica.replay.stop" => NoParams(parameters) ?? Stop(),
                _ when method.StartsWith("mechanica.", StringComparison.Ordinal) || method.StartsWith("rpc.", StringComparison.Ordinal) =>
                    Outcome.Fail(Codes.MethodNotFound, $"No method named '{Shorten(method)}'.", ActionErrors.UnknownAction),
                _ => Dispatch(connection, method, paramsJson, now),
            };
        }
        catch (Exception e)
        {
            return Outcome.Fail(Codes.InternalError, $"{e.GetType().Name}: {e.Message}", ActionErrors.Internal);
        }
    }

    private Outcome Dispatch(RemoteConnection connection, string method, string paramsJson, double now)
    {
        if (!_dispatcher.Has(method))
            return Outcome.Fail(Codes.MethodNotFound, $"No method named '{Shorten(method)}'.", ActionErrors.UnknownAction);
        if (!_limiter.TryAcquire(connection.Id, now))
            return Outcome.Fail(Codes.RateLimited, "Too many calls; slow down.", ActionErrors.RateLimited);
        var result = _dispatcher.Dispatch(_caller, method, paramsJson);
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
        return Outcome.Fail(code, result.Message ?? result.Code ?? "failed", result.Code);
    }

    private Outcome Authenticate(RemoteConnection connection, JsonElement parameters)
    {
        if (_token is null)
            return Outcome.Ok(Value(true));
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("token", out var token) || token.ValueKind != JsonValueKind.String)
            return Outcome.Fail(Codes.InvalidParams, "mechanica.auth takes { \"token\": \"...\" }.", ActionErrors.BadArgs);
        var given = Encoding.UTF8.GetBytes(token.GetString()!);
        if (!CryptographicOperations.FixedTimeEquals(given, _token))
            return Outcome.Fail(Codes.Unauthorized, "Wrong token.");
        connection.Authenticated = true;
        return Outcome.Ok(Value(true));
    }

    private Outcome StartReplay(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("recording", out var recordingElement))
            return Outcome.Fail(Codes.InvalidParams, "mechanica.replay.start takes { \"recording\": {...}, \"timed\": true }.", ActionErrors.BadArgs);
        var timed = true;
        foreach (var property in parameters.EnumerateObject())
        {
            if (property.Name == "timed" && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                timed = property.Value.GetBoolean();
            else if (property.Name != "recording")
                return Outcome.Fail(Codes.InvalidParams, $"Unknown parameter '{Shorten(property.Name)}'.", ActionErrors.BadArgs);
        }
        ActionRecording recording;
        try
        {
            recording = ActionRecording.FromElement(recordingElement);
        }
        catch (FormatException e)
        {
            return Outcome.Fail(Codes.InvalidParams, e.Message, ActionErrors.BadArgs);
        }
        var total = _log.StartReplay(recording, _caller, timed);
        return Outcome.Ok(Value(new { total }));
    }

    private Outcome Stop()
    {
        _log.StopReplay();
        return Outcome.Ok();
    }

    private static Outcome? NoParams(JsonElement parameters) =>
        parameters.ValueKind is JsonValueKind.Undefined || (parameters.ValueKind == JsonValueKind.Object && !parameters.EnumerateObject().Any())
            ? null
            : Outcome.Fail(Codes.InvalidParams, "This method takes no params.", ActionErrors.BadArgs);

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

    private static bool Error(Utf8JsonWriter w, JsonElement? id, int code, string message, string? dataCode)
    {
        w.WriteStartObject();
        w.WriteString("jsonrpc", "2.0");
        w.WriteStartObject("error");
        w.WriteNumber("code", code);
        w.WriteString("message", message);
        if (dataCode is not null)
        {
            w.WriteStartObject("data");
            w.WriteString("code", dataCode);
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
        using (var writer = new Utf8JsonWriter(buffer))
            write(writer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);

    private static string Shorten(string text) => text.Length <= 64 ? text : text[..64] + "…";
}
