using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Quests;
using Mechanica.Core.World;

namespace Mechanica.Core.Editor;

/// <summary>A POI in a <see cref="StoryBatch"/>.</summary>
public sealed record StoryPoiEntry
{
    /// <summary>The file to write, <c>pois/&lt;id&gt;.json</c>.</summary>
    public string? File { get; init; }

    /// <summary>The POI prefab; its id comes from the file name.</summary>
    public PoiPrefab? Poi { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A quest in a <see cref="StoryBatch"/>.</summary>
public sealed record StoryQuestEntry
{
    /// <summary>The file to write, <c>quests/&lt;id&gt;.json</c>.</summary>
    public string? File { get; init; }

    /// <summary>The quest; its id comes from the file name.</summary>
    public QuestDefinition? Quest { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A story batch (chunk 24): the POIs and quests of a mod's story, exported one after another through the
/// same checks the loader runs, so story content is authored like parts and bots (D3) rather than as
/// hand-written files. It lives at <c>editor/batches/*.json</c>, told apart by its <c>quests</c> or
/// <c>pois</c> key, and runs through <c>--part-batch</c> and <c>tools/author-vanilla.sh</c>. POIs are written
/// first, so the quests that follow can name them. Running it again rewrites the same files.
/// </summary>
public sealed record StoryBatch
{
    /// <summary>The batch format this build reads.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Format version of the file.</summary>
    public int? FormatVersion { get; init; }

    /// <summary>POIs to write.</summary>
    public List<StoryPoiEntry>? Pois { get; init; }

    /// <summary>Quests to write.</summary>
    public List<StoryQuestEntry>? Quests { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>True when <paramref name="json"/> looks like a story batch.</summary>
    public static bool IsStoryBatch(string json)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                is JsonObject o && (o.ContainsKey("quests") || o.ContainsKey("pois"));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Parses a batch file, reporting problems instead of throwing.</summary>
    public static StoryBatch? Read(string json, string displayFile, List<DataDiagnostic> diagnostics) =>
        ModLoader.ParseJson<StoryBatch>(json, displayFile, diagnostics);

    /// <summary>
    /// Runs <paramref name="batch"/> into the mod at <paramref name="modDirectory"/>. The mods beside it, plus
    /// <paramref name="otherMods"/> whose ids none of those has, are loaded for biomes, items, recipes and other
    /// mods' POIs. Returns one result per POI and quest; a POI or quest with errors is not written, and nothing
    /// is written when the batch itself is malformed.
    /// </summary>
    public static IReadOnlyList<ExportResult> Run(StoryBatch batch, string modDirectory, string displayFile, List<DataDiagnostic> diagnostics,
        IEnumerable<string>? otherMods = null)
    {
        var sink = new DiagnosticSink(displayFile, diagnostics);
        if (batch.FormatVersion is { } format && (format < 1 || format > CurrentFormatVersion))
            sink.Error(DiagnosticCodes.BatchInvalid, "$.format_version", $"format_version {format} is not supported; this build reads 1 to {CurrentFormatVersion}.");
        KnownKeys.WarnUnknown(batch.Extra, "$", sink, typeof(StoryBatch));
        var pois = new List<(string Id, string File, PoiPrefab Poi)>();
        foreach (var (entry, i) in (batch.Pois ?? []).Select((e, i) => (e, i)))
        {
            KnownKeys.WarnUnknown(entry.Extra, $"$.pois[{i}]", sink, typeof(StoryPoiEntry));
            if (IdOf(entry.File, WorldGenData.PoiFolder, $"$.pois[{i}].file", sink) is { } id)
            {
                if (entry.Poi is null)
                    sink.Error(DiagnosticCodes.BatchInvalid, $"$.pois[{i}].poi", "poi is required.");
                else if (entry.Poi.Id is not null)
                    sink.Error(DiagnosticCodes.BatchInvalid, $"$.pois[{i}].poi.id", $"the id comes from the file name ('{id}'); leave it out.");
                else
                    pois.Add((id, entry.File!, entry.Poi with { Id = id }));
            }
        }
        var quests = new List<(string Id, string File, QuestDefinition Quest)>();
        foreach (var (entry, i) in (batch.Quests ?? []).Select((e, i) => (e, i)))
        {
            KnownKeys.WarnUnknown(entry.Extra, $"$.quests[{i}]", sink, typeof(StoryQuestEntry));
            if (IdOf(entry.File, QuestBook.Folder, $"$.quests[{i}].file", sink) is { } id)
            {
                if (entry.Quest is null)
                    sink.Error(DiagnosticCodes.BatchInvalid, $"$.quests[{i}].quest", "quest is required.");
                else if (entry.Quest.Id is not null)
                    sink.Error(DiagnosticCodes.BatchInvalid, $"$.quests[{i}].quest.id", $"the id comes from the file name ('{id}'); leave it out.");
                else
                    quests.Add((id, entry.File!, entry.Quest));
            }
        }
        if (pois.Count + quests.Count == 0)
            sink.Error(DiagnosticCodes.BatchInvalid, "$", "a story batch needs at least one POI or quest.");
        if (sink.ErrorCount > 0)
            return [];

        var modId = Path.GetFileName(Path.GetFullPath(modDirectory));
        var modsRoot = Path.GetDirectoryName(Path.GetFullPath(modDirectory))!;
        var beside = Directory.GetDirectories(modsRoot).Where(d => System.IO.File.Exists(Path.Combine(d, ModLoader.ManifestFileName))).ToList();
        var names = beside.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        var folders = beside.Concat((otherMods ?? [])
            .Where(o => System.IO.File.Exists(Path.Combine(o, ModLoader.ManifestFileName)) && !names.Contains(Path.GetFileName(o)))).ToList();

        var results = new List<ExportResult>();
        var loaded = WorldGenData.Load(ModLoader.Load(folders));
        var biomes = loaded.Biomes.Select(b => b.Id).ToList();
        var grounds = loaded.Grounds.Select(g => g.Id).ToList();
        foreach (var (id, file, poi) in pois)
        {
            var found = new List<DataDiagnostic>();
            WorldGenValidator.Validate(poi, biomes, new DiagnosticSink(file, found), grounds, [.. loaded.Objects.Keys], [.. loaded.Liquids.Keys]);
            results.Add(Write(modDirectory, file, $"{modId}:{id}", DataJson.Write(poi), found));
        }
        var content = ModLoader.Load(folders);
        var world = WorldGenData.Load(content);
        var recipes = RecipeBook.Load(content);
        foreach (var (id, file, quest) in quests)
        {
            var found = QuestBook.Validate(quest, id, modId, content, recipes, world, file).ToList();
            results.Add(Write(modDirectory, file, $"{modId}:{id}", DataJson.Write(quest), found));
        }
        foreach (var result in results)
            diagnostics.AddRange(result.Diagnostics);
        return results;
    }

    private static ExportResult Write(string modDirectory, string file, string id, string json, List<DataDiagnostic> found)
    {
        if (found.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new ExportResult(false, null, id, found);
        var path = Path.Combine(modDirectory, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        DataJson.WriteFile(path, json);
        return new ExportResult(true, path, id, found);
    }

    private static string? IdOf(string? file, string folder, string path, DiagnosticSink sink)
    {
        var id = Path.GetFileNameWithoutExtension(file ?? "");
        if (file != $"{folder}/{id}.json" || !Identifiers.IsValid(id))
        {
            sink.Error(DiagnosticCodes.BatchInvalid, path, $"file must be {folder}/<id>.json with a lowercase snake_case id (got '{file}').");
            return null;
        }
        return id;
    }
}
