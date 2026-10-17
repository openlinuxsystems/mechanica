using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;

namespace Mechanica.Core.Editor;

/// <summary>One bot in a <see cref="BotBatch"/>: where it goes, what it starts from and the edits on top.</summary>
public sealed record BotBatchEntry
{
    /// <summary>The bot file to write, relative to the mod folder (<c>bots/scuttler_green.json</c>). Its name is the bot id.</summary>
    public string? File { get; init; }

    /// <summary>Bot Behavior Editor template to start from (<c>melee</c>, <c>ranged</c>, <c>passive</c>). Exactly one of this and <see cref="Base"/>.</summary>
    public string? Template { get; init; }

    /// <summary>Id of an earlier bot in the same batch to start from, so a colour variant inherits its family.</summary>
    public string? Base { get; init; }

    /// <summary>
    /// Bot keys to change, as in a bot file. Objects merge key by key into the starting bot, other values
    /// replace, <c>null</c> removes a key, and <c>behavior</c> always replaces the whole tree.
    /// </summary>
    public JsonObject? Set { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A Bot Behavior Editor batch: bots the editor makes one after another exactly as a modder would (new bot
/// from a template, set its stats, attacks, drops and tree, export), then the raid table saved from the
/// Raids tab. It is how vanilla's bots and raids are authored (D3). A batch lives in its mod at
/// <c>editor/batches/*.json</c> next to the Part Editor's, told apart by its <c>bots</c> key; the game never
/// reads it. Running it again rewrites the files it made, so the batch is the source of truth for them.
/// </summary>
public sealed record BotBatch
{
    /// <summary>The batch format this build reads.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Format version of the file.</summary>
    public int? FormatVersion { get; init; }

    /// <summary>The bots, in the order they are made.</summary>
    public List<BotBatchEntry>? Bots { get; init; }

    /// <summary>The raid table to save, or null to leave the mod's alone.</summary>
    public RaidTableDefinition? Raids { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>True when <paramref name="json"/> looks like a bot batch rather than a Part Editor batch.</summary>
    public static bool IsBotBatch(string json)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                is JsonObject o && (o.ContainsKey("bots") || o.ContainsKey("raids"));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Parses a batch file, reporting problems instead of throwing.</summary>
    public static BotBatch? Read(string json, string displayFile, List<DataDiagnostic> diagnostics) =>
        ModLoader.ParseJson<BotBatch>(json, displayFile, diagnostics);

    /// <summary>
    /// Runs <paramref name="batch"/> into the mod at <paramref name="modDirectory"/>: every bot is made and
    /// exported with <see cref="BotExporter"/>, then the raid table is checked against the mods as they now
    /// are and saved. The mods folder holding the mod is loaded for items and bots of other mods, plus
    /// <paramref name="otherMods"/> (mod folders elsewhere, such as the game's own for a mod kept outside it)
    /// whose id no mod beside it has. Returns one result per bot, then one for the raid table; problems go to
    /// <paramref name="diagnostics"/>, and nothing is written when the batch itself has errors.
    /// </summary>
    public static IReadOnlyList<ExportResult> Run(BotBatch batch, string modDirectory, Func<string, string?> template,
        string displayFile, List<DataDiagnostic> diagnostics, IEnumerable<string>? otherMods = null)
    {
        var documents = Prepare(batch, modDirectory, template, displayFile, diagnostics);
        if (documents is null)
            return [];
        var modsRoot = Path.GetDirectoryName(Path.GetFullPath(modDirectory))!;
        var beside = Directory.GetDirectories(modsRoot)
            .Where(d => File.Exists(Path.Combine(d, ModLoader.ManifestFileName))).ToList();
        var names = beside.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        var folders = beside.Concat((otherMods ?? [])
            .Where(o => File.Exists(Path.Combine(o, ModLoader.ManifestFileName)) && !names.Contains(Path.GetFileName(o)))).ToList();
        ModLoadResult Load() => ModLoader.Load(folders);
        var content = Load();
        var results = new List<ExportResult>();
        foreach (var document in documents)
        {
            var result = BotExporter.Export(document, content);
            results.Add(result);
            diagnostics.AddRange(result.Diagnostics);
        }
        if (batch.Raids is { } raids)
        {
            var result = BotExporter.ExportRaids(modDirectory, raids, BotBook.Load(Load()));
            results.Add(result);
            diagnostics.AddRange(result.Diagnostics);
        }
        return results;
    }

    /// <summary>Turns every entry into a <see cref="BotDocument"/> ready to export; null when any entry has an error.</summary>
    public static IReadOnlyList<BotDocument>? Prepare(BotBatch batch, string modDirectory, Func<string, string?> template,
        string displayFile, List<DataDiagnostic> diagnostics)
    {
        var sink = new DiagnosticSink(displayFile, diagnostics);
        if (batch.FormatVersion is { } format && (format < 1 || format > CurrentFormatVersion))
            sink.Error(DiagnosticCodes.BatchInvalid, "$.format_version", $"format_version {format} is not supported; this build reads 1 to {CurrentFormatVersion}.");
        KnownKeys.WarnUnknown(batch.Extra, "$", sink, typeof(BotBatch));
        var made = new Dictionary<string, BotDocument>(StringComparer.Ordinal);
        var entries = batch.Bots ?? [];
        if (entries.Count == 0 && batch.Raids is null)
            sink.Error(DiagnosticCodes.BatchInvalid, "$.bots", "a bot batch needs at least one bot or a raid table.");
        for (var i = 0; i < entries.Count; i++)
        {
            var path = $"$.bots[{i}]";
            var entry = entries[i];
            KnownKeys.WarnUnknown(entry.Extra, path, sink, typeof(BotBatchEntry));
            if (BotId(entry.File, $"{path}.file", sink) is not { } id)
                continue;
            if (made.ContainsKey(id))
            {
                sink.Error(DiagnosticCodes.BatchInvalid, $"{path}.file", $"bot '{id}' is already made earlier in this batch.");
                continue;
            }
            if (entry.Set?["id"] is not null)
            {
                sink.Error(DiagnosticCodes.BatchInvalid, $"{path}.set.id", $"the id comes from the file name ('{id}'); leave it out of set.");
                continue;
            }
            if ((entry.Template is null) == (entry.Base is null))
            {
                sink.Error(DiagnosticCodes.BatchInvalid, path, "give exactly one of template (a Bot Behavior Editor template) or base (an earlier bot in this batch).");
                continue;
            }
            string start;
            if (entry.Base is { } baseId)
            {
                if (!made.TryGetValue(baseId, out var parent))
                {
                    sink.Error(DiagnosticCodes.BatchInvalid, $"{path}.base", $"no bot '{baseId}' is made earlier in this batch.");
                    continue;
                }
                start = parent.Json.ToJsonString();
            }
            else if (template(entry.Template!) is { } text)
                start = text;
            else
            {
                sink.Error(DiagnosticCodes.BatchInvalid, $"{path}.template", $"there is no template '{entry.Template}'.");
                continue;
            }
            var document = BotDocument.New(modDirectory, start, id, id);
            if (entry.Set is { } set)
                document.Merge(set);
            document.RelativeFile = System.IO.File.Exists(Path.Combine(modDirectory, entry.File!)) ? entry.File : null;
            made[id] = document;
        }
        return sink.ErrorCount > 0 ? null : [.. made.Values];
    }

    private static string? BotId(string? file, string path, DiagnosticSink sink)
    {
        if (string.IsNullOrEmpty(file))
        {
            sink.Error(DiagnosticCodes.BatchInvalid, path, "file is required: the bot file to write, like bots/scuttler_green.json.");
            return null;
        }
        var id = Path.GetFileNameWithoutExtension(file);
        if (file != $"{BotBook.Folder}/{id}.json")
        {
            sink.Error(DiagnosticCodes.BatchInvalid, path, $"'{file}' must be {BotBook.Folder}/<id>.json.");
            return null;
        }
        if (!Identifiers.IsValid(id))
        {
            sink.Error(DiagnosticCodes.BatchInvalid, path, $"the file name is the bot id, so '{id}' must be lowercase snake_case.");
            return null;
        }
        return id;
    }
}
