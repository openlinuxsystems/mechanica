using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Data;

namespace Mechanica.Core.Creations;

/// <summary>What a blueprint file says about itself, read without the part catalog.</summary>
/// <param name="Name">The name it was saved under.</param>
/// <param name="SavedAt">When it was saved.</param>
/// <param name="Parts">How many parts it has.</param>
public sealed record BlueprintInfo(string Name, DateTimeOffset SavedAt, int Parts);

/// <summary>
/// A saved Creation that can be loaded onto a Lift again (UI spec §5, §8): its parts, joints, paint, settings and logic
/// links as <see cref="CreationJson"/> writes them, without anything that belongs to the world it came from (its id,
/// who simulates it, whether it was on a Lift, damage, fire, what its storage held, crafting orders, crops, motion).
/// <code>
/// {
///   "format_version": 1,
///   "name": "Buggy",
///   "saved_at": "2026-10-17T12:00:00+00:00",
///   "creation": { "format_version": 1, "id": 0, "parts": [ … ], "connections": [ … ], "bodies": [ … ] }
/// }
/// </code>
/// </summary>
public static class Blueprint
{
    /// <summary>The format version this build reads and writes.</summary>
    public const int FormatVersion = 1;

    /// <summary>Longest name, in characters.</summary>
    public const int MaxNameLength = 48;

    private static readonly string[] WorldKeys = ["authority_peer", "on_lift"];
    private static readonly string[] PartWorldKeys = ["damage", "fire", "inventory", "crafting", "soil"];
    private static readonly string[] BodyWorldKeys = ["linear_velocity", "angular_velocity"];

    /// <summary>The blueprint of <paramref name="creation"/> named <paramref name="name"/> (trimmed and shortened).</summary>
    public static string Write(Creation creation, string name, DateTimeOffset savedAt)
    {
        var shape = JsonNode.Parse(CreationJson.Write(creation))!.AsObject();
        Strip(shape);
        var file = new JsonObject
        {
            ["format_version"] = FormatVersion,
            ["name"] = CleanName(name),
            ["saved_at"] = savedAt.ToString("O"),
            ["creation"] = shape,
        };
        return file.ToJsonString(DataJson.WriteOptions) + "\n";
    }

    /// <summary>A name as blueprints store it: trimmed, at most <see cref="MaxNameLength"/> characters, "Blueprint" when empty.</summary>
    public static string CleanName(string name)
    {
        var trimmed = new string([.. name.Trim().Where(c => !char.IsControl(c))]);
        if (trimmed.Length > MaxNameLength)
            trimmed = trimmed[..MaxNameLength].TrimEnd();
        return trimmed.Length == 0 ? "Blueprint" : trimmed;
    }

    /// <summary>Name, time and part count of a blueprint; null (with diagnostics) when it is not one.</summary>
    public static BlueprintInfo? Peek(string json, string file, List<DataDiagnostic> diagnostics) =>
        Envelope(json, file, diagnostics) is { } envelope ? envelope.Info : null;

    /// <summary>
    /// The Creation a blueprint holds, as a new Creation with id <paramref name="id"/> simulated by the host, validated
    /// against <paramref name="catalog"/> like a saved one. Anything that belongs to a world is left out even when the
    /// file has it. Null (with diagnostics) when it does not load.
    /// </summary>
    public static Creation? Read(string json, IPartCatalog catalog, int id, string file, List<DataDiagnostic> diagnostics)
    {
        if (Envelope(json, file, diagnostics) is not { } envelope)
            return null;
        Strip(envelope.Creation);
        envelope.Creation["id"] = id;
        return CreationJson.Read(envelope.Creation.ToJsonString(), catalog, file, diagnostics);
    }

    private static (BlueprintInfo Info, JsonObject Creation)? Envelope(string json, string file, List<DataDiagnostic> diagnostics)
    {
        var sink = new DiagnosticSink(file, diagnostics);
        const string invalid = DiagnosticCodes.CreationInvalid;
        JsonObject root;
        try
        {
            if (JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is not JsonObject parsed)
            {
                sink.Error(invalid, "$", "a blueprint is a JSON object.");
                return null;
            }
            root = parsed;
        }
        catch (JsonException e)
        {
            sink.Error(invalid, "$", $"not valid JSON: {e.Message}");
            return null;
        }
        if (Int(root["format_version"]) is not { } version)
            sink.Error(invalid, "$.format_version", "format_version is required.");
        else if (version != FormatVersion)
            sink.Error(DiagnosticCodes.UnsupportedFormat, "$.format_version", $"format_version {version} is not supported; this build reads {FormatVersion}.");
        var name = Text(root["name"]);
        if (name is null)
            sink.Error(invalid, "$.name", "name is required.");
        var savedAt = DateTimeOffset.MinValue;
        if (root["saved_at"] is { } at && !(Text(at) is { } stamp && DateTimeOffset.TryParse(stamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out savedAt)))
            sink.Error(invalid, "$.saved_at", "saved_at must be a date and time like \"2026-10-17T12:00:00+00:00\".");
        if (root["creation"] is not JsonObject creation)
        {
            sink.Error(invalid, "$.creation", "creation is required.");
            return null;
        }
        if (sink.ErrorCount > 0)
            return null;
        var parts = creation["parts"] is JsonArray list ? list.Count : 0;
        return (new BlueprintInfo(CleanName(name!), savedAt, parts), creation);
    }

    private static void Strip(JsonObject creation)
    {
        foreach (var key in WorldKeys)
            creation.Remove(key);
        foreach (var part in (creation["parts"] as JsonArray ?? []).OfType<JsonObject>())
        {
            foreach (var key in PartWorldKeys)
                part.Remove(key);
        }
        foreach (var body in (creation["bodies"] as JsonArray ?? []).OfType<JsonObject>())
        {
            foreach (var key in BodyWorldKeys)
                body.Remove(key);
        }
    }

    private static int? Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var n) ? n : null;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
