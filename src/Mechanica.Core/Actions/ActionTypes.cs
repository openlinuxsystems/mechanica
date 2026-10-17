using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Actions;

/// <summary>Who is behind an Action API call. Every source goes through the same validation.</summary>
public enum ActionSource
{
    /// <summary>A human player, local or over the network.</summary>
    Player,

    /// <summary>An internal bot.</summary>
    Bot,

    /// <summary>An external AI over the remote endpoint.</summary>
    External,

    /// <summary>A Lua script.</summary>
    Script,
}

/// <summary>The caller of an action, as established by the host (never taken from the request itself).</summary>
/// <param name="Peer">Godot peer id the call is made for; the host is 1.</param>
/// <param name="Source">What kind of agent is calling.</param>
public readonly record struct ActionCaller(int Peer, ActionSource Source)
{
    /// <summary>The host's own player.</summary>
    public static ActionCaller HostPlayer => new(1, ActionSource.Player);
}

/// <summary>Machine-readable failure codes in <see cref="ActionResult.Code"/>.</summary>
public static class ActionErrors
{
    /// <summary>No action has that name.</summary>
    public const string UnknownAction = "unknown_action";

    /// <summary>The arguments did not parse into the action's argument type.</summary>
    public const string BadArgs = "bad_args";

    /// <summary>The action's validation refused the call; the message says why.</summary>
    public const string Rejected = "rejected";

    /// <summary>The caller sent too many actions too quickly.</summary>
    public const string RateLimited = "rate_limited";

    /// <summary>The client has no connection to a host.</summary>
    public const string NotConnected = "not_connected";

    /// <summary>The action threw while applying. The host logs the details.</summary>
    public const string Internal = "internal_error";
}

/// <summary>Outcome of an action. Serialized as JSON when the host answers a client.</summary>
public sealed record ActionResult
{
    /// <summary>True when the action was applied.</summary>
    public required bool Ok { get; init; }

    /// <summary>One of <see cref="ActionErrors"/> when <see cref="Ok"/> is false.</summary>
    public string? Code { get; init; }

    /// <summary>Human-readable reason when <see cref="Ok"/> is false.</summary>
    public string? Message { get; init; }

    /// <summary>Optional result data from the action.</summary>
    public JsonElement? Value { get; init; }

    /// <summary>A success, optionally carrying a value.</summary>
    public static ActionResult Success(JsonElement? value = null) => new() { Ok = true, Value = value };

    /// <summary>A failure with a code and reason.</summary>
    public static ActionResult Failure(string code, string message) => new() { Ok = false, Code = code, Message = message };

    /// <summary>The wire form.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, ActionJson.Options);

    /// <summary>Parses the wire form, or returns null when it is not a valid result.</summary>
    public static ActionResult? FromJson(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<ActionResult>(json, ActionJson.Options);
            return result is { Ok: false, Code: null } ? null : result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Ok ? "ok" : $"{Code}: {Message}";
}

/// <summary>A call the dispatcher handled, for logs and record/replay.</summary>
/// <param name="Caller">Who called.</param>
/// <param name="Action">Action name.</param>
/// <param name="ArgsJson">Arguments exactly as received.</param>
/// <param name="Result">What happened.</param>
public sealed record ActionRecord(ActionCaller Caller, string Action, string ArgsJson, ActionResult Result);

/// <summary>Argument type for actions that take none (<c>{}</c>).</summary>
public sealed record NoArgs;

/// <summary>JSON settings for Action API arguments and results (D8: comments and trailing commas allowed on read).</summary>
public static class ActionJson
{
    /// <summary>Largest argument payload accepted, in characters.</summary>
    public const int MaxArgsLength = 16 * 1024;

    /// <summary>Shared options: snake_case names, unknown members rejected, required members enforced.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            AllowDuplicateProperties = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            NumberHandling = JsonNumberHandling.Strict,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 32,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
