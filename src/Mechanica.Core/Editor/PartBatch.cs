using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;

namespace Mechanica.Core.Editor;

/// <summary>
/// One part in a <see cref="PartBatch"/>: where it goes, what it starts from, the property edits on top
/// and its model pieces.
/// </summary>
public sealed record PartBatchEntry
{
    /// <summary>The part file to write, relative to the mod folder (<c>parts/blocks/wood_block.json</c>). Its name is the part id.</summary>
    public string? File { get; init; }

    /// <summary>Part Editor template to start from (<c>block</c>, <c>decor</c>, ...). Exactly one of this and <see cref="Base"/>.</summary>
    public string? Template { get; init; }

    /// <summary>Id of an earlier part in the same batch to start from, so a wedge inherits its block.</summary>
    public string? Base { get; init; }

    /// <summary>
    /// Part keys to change, as in a part file. Objects merge key by key into the starting part, other
    /// values replace, and <c>null</c> removes a key.
    /// </summary>
    public JsonObject? Set { get; init; }

    /// <summary>Model pieces, as in the editor source. Omitted means the part has no model of its own.</summary>
    public List<ModelPiece>? Model { get; init; }

    /// <summary>When true, the collision shape is the convex hull of the part's own baked model.</summary>
    public bool? CollisionFromModel { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A Part Editor batch: a list of part edits that the editor runs one after another exactly as if a
/// modder made each part by hand (new part from a template, set its properties, add model pieces, export).
/// It is how a mod authors many similar parts, such as vanilla's blocks and wedges, without hand-writing
/// part files (D3). A batch lives in its mod at <c>editor/batches/*.json</c>; the game never reads it.
/// Running it again updates the parts it made, so the batch is the source of truth for them.
/// </summary>
public sealed record PartBatch
{
    /// <summary>The batch format this build reads.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Folder inside a mod for batch files.</summary>
    public const string Folder = "editor/batches";

    /// <summary>Format version of the file.</summary>
    public int? FormatVersion { get; init; }

    /// <summary>The parts, in the order they are made.</summary>
    public List<PartBatchEntry>? Parts { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>Parses a batch file, reporting problems instead of throwing.</summary>
    public static PartBatch? Read(string json, string displayFile, List<DataDiagnostic> diagnostics) =>
        ModLoader.ParseJson<PartBatch>(json, displayFile, diagnostics);

    /// <summary>
    /// Turns every entry into an open <see cref="PartDocument"/> in the mod at <paramref name="modDirectory"/>,
    /// ready to bake and export: an existing part file is opened and edited, a missing one is started new.
    /// Each document's history holds the edits as undo steps. Returns null when any entry has an error;
    /// <paramref name="diagnostics"/> lists them all.
    /// </summary>
    /// <param name="batch">The batch.</param>
    /// <param name="modDirectory">Folder of the mod the parts belong to.</param>
    /// <param name="template">Returns a template's part JSON by name, or null when there is no such template.</param>
    /// <param name="displayFile">Batch file name shown in diagnostics.</param>
    /// <param name="diagnostics">Receives every problem.</param>
    public static IReadOnlyList<PartDocument>? Prepare(PartBatch batch, string modDirectory, Func<string, string?> template,
        string displayFile, List<DataDiagnostic> diagnostics)
    {
        var sink = new DiagnosticSink(displayFile, diagnostics);
        if (batch.FormatVersion is { } format && (format < 1 || format > CurrentFormatVersion))
            sink.Error(DiagnosticCodes.BatchInvalid, "$.format_version", $"format_version {format} is not supported; this build reads 1 to {CurrentFormatVersion}.");
        KnownKeys.WarnUnknown(batch.Extra, "$", sink, typeof(PartBatch));

        var modId = Path.GetFileName(Path.GetFullPath(modDirectory));
        var made = new Dictionary<string, PartDefinition>(StringComparer.Ordinal);
        var documents = new List<PartDocument>();
        var entries = batch.Parts ?? [];
        if (entries.Count == 0)
            sink.Error(DiagnosticCodes.BatchInvalid, "$.parts", "a batch needs at least one part.");
        for (var i = 0; i < entries.Count; i++)
        {
            var path = $"$.parts[{i}]";
            var errors = sink.ErrorCount;
            var entry = entries[i];
            KnownKeys.WarnUnknown(entry.Extra, path, sink, typeof(PartBatchEntry));

            var id = PartId(entry.File, $"{path}.file", sink);
            if (id is not null && made.ContainsKey(id))
                sink.Error(DiagnosticCodes.BatchInvalid, $"{path}.file", $"part '{id}' is already made earlier in this batch.");
            var start = Start(entry, made, template, path, sink);
            if (start is null || id is null || sink.ErrorCount > errors)
                continue;

            var json = (JsonObject)JsonNode.Parse(DataJson.Write(start))!;
            if (entry.Set?["id"] is { } setId && setId.GetValueKind() == JsonValueKind.String && setId.GetValue<string>() != id)
            {
                sink.Error(DiagnosticCodes.BatchInvalid, $"{path}.set.id", $"the id comes from the file name ('{id}'); leave it out of set.");
                continue;
            }
            if (entry.Set is { } set)
                Merge(json, set);
            json["id"] = id;
            var definition = ModLoader.ParseJson<PartDefinition>(json.ToJsonString(), new DiagnosticSink($"{displayFile} {path}.set", diagnostics));
            if (definition is null)
            {
                sink.Error(DiagnosticCodes.BatchInvalid, $"{path}.set", "the edited part is not a valid part; see the error before this one.");
                continue;
            }
            var source = PartSource.Empty with { Model = entry.Model is { Count: > 0 } model ? [.. model] : null };
            // Point at the model the way export will, so re-running an unchanged batch leaves its parts clean.
            if (source.NeedsBake)
                definition = definition with { Mesh = PartExporter.BakedMeshPath(entry.File!) };
            else if (source.Pieces.Count == 1)
                definition = definition with { Mesh = source.Pieces[0].Mesh };
            if (entry.CollisionFromModel == true)
            {
                if (!source.NeedsBake)
                {
                    sink.Error(DiagnosticCodes.BatchInvalid, $"{path}.collision_from_model", "collision_from_model needs model pieces to bake.");
                    continue;
                }
                definition = definition with
                {
                    Collision = new CollisionDefinition { Shape = CollisionShape.Convex, Mesh = PartExporter.BakedMeshPath(entry.File!) },
                };
            }
            foreach (var d in PartSource.Validate(source, modId, $"{displayFile} {path}.model"))
                sink.Error(d.Code, d.JsonPath, d.Message);
            if (sink.ErrorCount > errors)
                continue;

            made[id] = definition;
            documents.Add(Document(modDirectory, entry.File!, definition, source, diagnostics));
        }
        return sink.ErrorCount > 0 ? null : documents;
    }

    // An existing part file is opened so the batch edits it in place; otherwise a new part starts there.
    private static PartDocument Document(string modDirectory, string file, PartDefinition definition, PartSource source,
        List<DataDiagnostic> diagnostics)
    {
        var full = Path.Combine(modDirectory, file);
        var document = System.IO.File.Exists(full) ? PartDocument.Open(full, diagnostics) : null;
        if (document is null)
        {
            document = PartDocument.New(modDirectory, definition, definition.Id!, definition.Name ?? definition.Id!);
            document.RelativeFile = file;
        }
        document.Apply("Batch edit", definition, source);
        return document;
    }

    private static string? PartId(string? file, string path, DiagnosticSink sink)
    {
        if (string.IsNullOrEmpty(file))
        {
            sink.Error(DiagnosticCodes.BatchInvalid, path, "file is required: the part file to write, like parts/blocks/wood_block.json.");
            return null;
        }
        var segments = file.Split('/');
        var id = Path.GetFileNameWithoutExtension(file);
        if (segments[0] != ModLoader.PartsFolder || segments.Length < 2 || !file.EndsWith(".json", StringComparison.Ordinal)
            || segments.Any(s => s is "" or "." or "..") || file.Contains('\\') || file.Contains(':'))
        {
            sink.Error(DiagnosticCodes.BatchInvalid, path, $"'{file}' must be a .json file under {ModLoader.PartsFolder}/, with forward slashes and no '..'.");
            return null;
        }
        if (!Identifiers.IsValid(id))
        {
            sink.Error(DiagnosticCodes.BatchInvalid, path, $"the file name is the part id, so '{id}' must be lowercase snake_case.");
            return null;
        }
        return id;
    }

    private static PartDefinition? Start(PartBatchEntry entry, Dictionary<string, PartDefinition> made, Func<string, string?> template,
        string path, DiagnosticSink sink)
    {
        if ((entry.Template is null) == (entry.Base is null))
        {
            sink.Error(DiagnosticCodes.BatchInvalid, path, "give exactly one of template (a Part Editor template) or base (an earlier part in this batch).");
            return null;
        }
        if (entry.Base is { } baseId)
        {
            if (made.TryGetValue(baseId, out var definition))
                return definition;
            sink.Error(DiagnosticCodes.BatchInvalid, $"{path}.base", $"no part '{baseId}' is made earlier in this batch.");
            return null;
        }
        if (template(entry.Template!) is not { } text)
        {
            sink.Error(DiagnosticCodes.BatchInvalid, $"{path}.template", $"there is no template '{entry.Template}'.");
            return null;
        }
        var parsed = ModLoader.ParseJson<PartDefinition>(text, new DiagnosticSink($"{entry.Template}.json", []));
        if (parsed is null)
            sink.Error(DiagnosticCodes.BatchInvalid, $"{path}.template", $"template '{entry.Template}' could not be read.");
        return parsed;
    }

    // Objects merge key by key; anything else replaces; null removes.
    private static void Merge(JsonObject target, JsonObject changes)
    {
        foreach (var (key, value) in changes)
        {
            if (value is null)
                target.Remove(key);
            else if (value is JsonObject inner && target[key] is JsonObject existing)
                Merge(existing, inner);
            else
                target[key] = value.DeepClone();
        }
    }
}
