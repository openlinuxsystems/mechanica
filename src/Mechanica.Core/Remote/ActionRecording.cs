using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Actions;

namespace Mechanica.Core.Remote;

/// <summary>
/// A recorded sequence of Action API calls, saved as JSON (D8: pretty-printed with a stable key order on
/// write; comments and trailing commas allowed on read). Each entry keeps the physics tick it ran on, who
/// called, the arguments exactly as received and what the host answered, so a replay can repeat the
/// calls with the same spacing and report where the outcome differs.
/// </summary>
public sealed record ActionRecording
{
    /// <summary>The format this code writes and reads.</summary>
    public const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions WriteOptions = CreateOptions(indented: true);
    private static readonly JsonSerializerOptions ReadOptions = CreateOptions(indented: false);

    /// <summary>File format version.</summary>
    public int FormatVersion { get; init; } = CurrentFormat;

    /// <summary>Physics ticks per second when it was recorded.</summary>
    public required int TickRate { get; init; }

    /// <summary>True when the recorder hit its entry limit and dropped later calls.</summary>
    public bool Truncated { get; init; }

    /// <summary>The calls, in the order they ran.</summary>
    public required IReadOnlyList<RecordedAction> Actions { get; init; }

    /// <summary>The file form.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions) + "\n";

    /// <summary>The recording as a JSON value (for JSON-RPC results).</summary>
    public JsonElement ToElement() => JsonSerializer.SerializeToElement(this, ReadOptions);

    /// <summary>Parses and checks a recording.</summary>
    /// <exception cref="FormatException">The JSON is malformed, of another format version, or an entry is invalid.</exception>
    public static ActionRecording FromJson(string json)
    {
        ActionRecording? recording;
        try
        {
            recording = JsonSerializer.Deserialize<ActionRecording>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid action recording: {e.Message}", e);
        }
        if (recording is null)
            throw new FormatException("Invalid action recording: it is null.");
        recording.Validate();
        return recording;
    }

    /// <summary>Parses and checks a recording given as a JSON value.</summary>
    /// <exception cref="FormatException">As <see cref="FromJson"/>.</exception>
    public static ActionRecording FromElement(JsonElement element) => FromJson(element.GetRawText());

    private void Validate()
    {
        if (FormatVersion != CurrentFormat)
            throw new FormatException($"Action recording format {FormatVersion} is not supported (expected {CurrentFormat}).");
        if (TickRate < 1)
            throw new FormatException($"tick_rate must be at least 1 (got {TickRate}).");
        for (var i = 0; i < Actions.Count; i++)
        {
            var entry = Actions[i];
            if (entry is null)
                throw new FormatException($"actions[{i}] is null.");
            if (entry.Tick < 0 || (i > 0 && entry.Tick < Actions[i - 1].Tick))
                throw new FormatException($"actions[{i}].tick must be 0 or more and never go back in time.");
            if (entry.Args.ValueKind != JsonValueKind.Object)
                throw new FormatException($"actions[{i}].args must be a JSON object.");
            if (entry.Result is { Ok: false, Code: null })
                throw new FormatException($"actions[{i}].result needs a code when ok is false.");
        }
    }

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
            MaxDepth = 64,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>One call in an <see cref="ActionRecording"/>.</summary>
/// <param name="Tick">Physics ticks since the recording started.</param>
/// <param name="Peer">Peer the call was made for.</param>
/// <param name="Source">What kind of agent called.</param>
/// <param name="Action">Action name.</param>
/// <param name="Args">Arguments as received (a JSON object).</param>
/// <param name="Result">What the host answered.</param>
public sealed record RecordedAction(long Tick, int Peer, ActionSource Source, string Action, JsonElement Args, ActionResult Result);
