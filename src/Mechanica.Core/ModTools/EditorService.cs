using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Items;
using Mechanica.Core.Sound;

namespace Mechanica.Core.ModTools;

/// <summary>What a batch run did.</summary>
/// <param name="Kind"><c>parts</c>, <c>bots</c> or <c>story</c>.</param>
/// <param name="Ok">True when every entry exported.</param>
/// <param name="Exports">One per part, recipe, bot, raid table, POI or quest written or tried.</param>
/// <param name="Diagnostics">The batch's own problems, then each export's.</param>
public sealed record BatchReport(string Kind, bool Ok, IReadOnlyList<ExportInfo> Exports, IReadOnlyList<DiagnosticInfo> Diagnostics);

/// <summary>One export's outcome.</summary>
/// <param name="Ok">True when it was written.</param>
/// <param name="Id">Qualified id of what was exported.</param>
/// <param name="File">The file written, relative to its mod, or null.</param>
/// <param name="Diagnostics">Its problems (warnings even when it was written).</param>
public sealed record ExportInfo(bool Ok, string Id, string? File, IReadOnlyList<DiagnosticInfo> Diagnostics);

/// <summary>An open Part Editor document.</summary>
/// <param name="Doc">Handle for the other <c>editor.part.*</c> calls.</param>
/// <param name="Mod">Mod it belongs to.</param>
/// <param name="File">Its file in the mod once exported or opened, else null.</param>
/// <param name="Definition">The part data (the <c>parts/*.json</c> schema).</param>
/// <param name="Source">The model pieces (the <c>editor/parts/*.json</c> schema).</param>
/// <param name="Dirty">True when it differs from the file.</param>
/// <param name="Undo">Label of the step undo would take back, or null.</param>
/// <param name="Redo">Label of the step redo would redo, or null.</param>
/// <param name="NeedsBake">True when export bakes the model pieces into a mesh.</param>
public sealed record PartState(string Doc, string Mod, string? File, JsonElement Definition, JsonElement Source, bool Dirty,
    string? Undo, string? Redo, bool NeedsBake);

/// <summary>An open Bot Behavior Editor document.</summary>
/// <param name="Doc">Handle for the other <c>editor.bot.*</c> calls.</param>
/// <param name="Mod">Mod it belongs to.</param>
/// <param name="File">The file it was opened from or exported to, else null.</param>
/// <param name="Bot">The bot data (the <c>bots/*.json</c> schema), behaviour tree included.</param>
/// <param name="Dirty">True when it differs from the file.</param>
/// <param name="Undo">Label of the step undo would take back, or null.</param>
public sealed record BotState(string Doc, string Mod, string? File, JsonElement Bot, bool Dirty, string? Undo);

/// <summary>A face contact the part would make, as the Part Editor previews it.</summary>
/// <param name="Cell">Cell of the part.</param>
/// <param name="Direction">Outward face direction.</param>
/// <param name="Connection">How the face connects (<c>rigid</c>, <c>none</c>, …), or null for the default.</param>
/// <param name="Socket">Socket on that face, or null.</param>
public sealed record ContactInfo(int[] Cell, int[] Direction, string? Connection, string? Socket);

/// <summary>The game behind the editor operations: its templates, its model baker and its batch runners.</summary>
public interface IEditorHost
{
    /// <summary>Part Editor template names.</summary>
    IReadOnlyList<string> PartTemplates { get; }

    /// <summary>A part template's JSON, or null.</summary>
    string? PartTemplate(string name);

    /// <summary>Bot Behavior Editor template names.</summary>
    IReadOnlyList<string> BotTemplates { get; }

    /// <summary>A bot template's JSON, or null.</summary>
    string? BotTemplate(string name);

    /// <summary>Bakes the document's model pieces into a glTF binary with the editor's model builder; null when it has no geometry.</summary>
    Task<byte[]?> BakeAsync(PartDocument document);

    /// <summary>Runs a batch file the way <c>--part-batch</c> does (parts and recipes, bots and raids, or story).</summary>
    Task<(string Kind, bool Ok, IReadOnlyList<ExportResult> Exports, IReadOnlyList<DataDiagnostic> Diagnostics)> RunBatchAsync(string batchFile);
}

/// <summary>
/// The in-game editors' operations for the AI endpoint (<c>editor.*</c>), through the editors' own Core code:
/// Part Editor documents (<see cref="PartDocument"/>, <see cref="PartExporter"/>), batches, Bot Behavior Editor
/// documents (<see cref="BotDocument"/>, <see cref="BotExporter"/>) and the raid table, recipes
/// (<see cref="RecipeExporter"/>) and sound banks (<see cref="SoundBankDocument"/>). Writes stay inside the mods
/// of the <see cref="ModWorkspace"/>, and vanilla only changes through its batches.
/// </summary>
public sealed class EditorService(ModWorkspace workspace, IEditorHost host)
{
    /// <summary>Most documents open at once.</summary>
    public const int MaxDocuments = 64;

    private readonly Dictionary<string, PartDocument> _parts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BotDocument> _bots = new(StringComparer.Ordinal);
    private int _next;

    /// <summary>The workspace edits go into.</summary>
    public ModWorkspace Workspace { get; } = workspace;

    /// <summary>The game behind the editors.</summary>
    public IEditorHost Host { get; } = host;

    // ---- Part Editor ----

    /// <summary>Starts a new part from a template, unsaved until <see cref="ExportPartAsync"/>.</summary>
    public PartState NewPart(string modId, string template, string id, string? name)
    {
        var dir = WritableMod(modId);
        if (!Identifiers.IsValid(id))
            throw new ModToolException($"'{id}' is not a valid part id (lowercase snake_case).");
        if (Host.PartTemplate(template) is not { } text)
            throw new ModToolException($"There is no part template '{template}' ({string.Join(", ", Host.PartTemplates)}).");
        var definition = ModLoader.ParseJson<PartDefinition>(text, $"{template}.json", [])
            ?? throw new ModToolException($"The part template '{template}' could not be read.");
        return PartStateOf(Add(_parts, "part", PartDocument.New(dir, definition, id, string.IsNullOrWhiteSpace(name) ? id : name)));
    }

    /// <summary>Opens a part file of a mod (<c>parts/…</c> or <c>overrides/…</c>) with its model pieces.</summary>
    public PartState OpenPart(string modId, string path)
    {
        var full = ModFiles.Resolve(Workspace.Files.ModDirectory(modId), path, file: true);
        if (!File.Exists(full))
            throw new ModToolException($"{modId}/{path} does not exist.");
        var diagnostics = new List<DataDiagnostic>();
        var document = PartDocument.Open(full, diagnostics) ?? throw new ModToolException(string.Join("; ", diagnostics));
        return PartStateOf(Add(_parts, "part", document));
    }

    /// <summary>The document as it stands.</summary>
    public PartState GetPart(string doc) => State(Part(doc));

    /// <summary>
    /// One undo step: replaces the definition and/or the model pieces, or merges <paramref name="set"/> into the
    /// definition (keys set to null are removed, objects merge, everything else replaces).
    /// </summary>
    public PartState EditPart(string doc, string? label, JsonElement? definition, JsonElement? set, JsonElement? source)
    {
        var document = Part(doc);
        if (definition is not null && set is not null)
            throw new ModToolException("Give definition or set, not both.");
        var next = document.Definition;
        if (definition is { } whole)
            next = Parse<PartDefinition>(whole, "definition");
        else if (set is { } changes)
        {
            var json = (JsonObject)JsonNode.Parse(DataJson.Write(next))!;
            Merge(json, JsonNode.Parse(changes.GetRawText()) as JsonObject ?? throw new ModToolException("set must be an object."));
            next = Parse<PartDefinition>(JsonSerializer.SerializeToElement(json), "set");
        }
        var nextSource = source is { } s
            ? PartSource.Read(s.GetRawText(), "source", []) ?? throw new ModToolException("source does not fit the model piece schema.")
            : document.Source;
        document.Apply(string.IsNullOrWhiteSpace(label) ? "Edit" : label, next, nextSource);
        return State(document);
    }

    /// <summary>Binds one of the part's sound names to a sound event, or unbinds it (null).</summary>
    public PartState SetPartSound(string doc, string key, string? sound)
    {
        if (!Identifiers.IsValid(key))
            throw new ModToolException($"'{key}' is not a valid sound name.");
        var document = Part(doc);
        document.SetSound(key, sound);
        return State(document);
    }

    /// <summary>Undoes (or with <paramref name="redo"/>, redoes) one step.</summary>
    public PartState UndoPart(string doc, bool redo)
    {
        var document = Part(doc);
        if (!(redo ? document.Redo() : document.Undo()))
            throw new ModToolException(redo ? "Nothing to redo." : "Nothing to undo.");
        return State(document);
    }

    /// <summary>Checks the part and its model pieces as export would.</summary>
    public IReadOnlyList<DiagnosticInfo> ValidatePart(string doc)
    {
        var document = Part(doc);
        return Tag("parts", document.ModId, document.Validate());
    }

    /// <summary>The face contacts the part would make, as the Part Editor's socket preview shows them.</summary>
    public IReadOnlyList<ContactInfo> PartContacts(string doc) =>
        [.. SocketTools.PreviewConnections(Part(doc).Definition).Select(c => new ContactInfo(
            [c.Cell.X, c.Cell.Y, c.Cell.Z], [c.Direction.X, c.Direction.Y, c.Direction.Z],
            c.Connection is { } k ? JsonNamingPolicy.SnakeCaseLower.ConvertName(k.ToString()) : null, c.Socket))];

    /// <summary>Exports the part into its mod, baking model pieces with the editor's model builder when needed.</summary>
    public async Task<ExportInfo> ExportPartAsync(string doc)
    {
        var document = Part(doc);
        var baked = document.Source.NeedsBake ? await Host.BakeAsync(document) : null;
        var result = PartExporter.Export(document, baked);
        return Info("parts", document.ModId, result, document.ModDirectory);
    }

    /// <summary>Closes a document; unsaved changes are dropped.</summary>
    public void Close(string doc)
    {
        if (!_parts.Remove(doc) && !_bots.Remove(doc))
            throw new ModToolException($"No document '{doc}' is open.");
    }

    // ---- Batches ----

    /// <summary>Runs a Part Editor, Bot Behavior Editor or story batch file of a mod, as <c>tools/author-vanilla.sh</c> does.</summary>
    public async Task<BatchReport> RunBatchAsync(string modId, string path)
    {
        var dir = Workspace.Files.ModDirectory(modId);
        var full = ModFiles.Resolve(dir, path, file: true);
        if (!path.EndsWith(".json", StringComparison.Ordinal) || !File.Exists(full))
            throw new ModToolException($"{modId}/{path} is not a batch file.");
        var (kind, ok, exports, diagnostics) = await Host.RunBatchAsync(full);
        return new BatchReport(kind, ok, [.. exports.Select(e => Info(kind, modId, e, dir))], Tag(kind, modId, diagnostics));
    }

    // ---- Bot Behavior Editor ----

    /// <summary>Starts a new bot from a template, unsaved until <see cref="ExportBot"/>.</summary>
    public BotState NewBot(string modId, string template, string id, string? name)
    {
        var dir = WritableMod(modId);
        if (!Identifiers.IsValid(id))
            throw new ModToolException($"'{id}' is not a valid bot id (lowercase snake_case).");
        if (Host.BotTemplate(template) is not { } text)
            throw new ModToolException($"There is no bot template '{template}' ({string.Join(", ", Host.BotTemplates)}).");
        return BotStateOf(Add(_bots, "bot", BotDocument.New(dir, text, id, string.IsNullOrWhiteSpace(name) ? id : name)));
    }

    /// <summary>Opens a bot file of a mod (<c>bots/…</c>).</summary>
    public BotState OpenBot(string modId, string path)
    {
        var dir = Workspace.Files.ModDirectory(modId);
        if (!File.Exists(ModFiles.Resolve(dir, path, file: true)))
            throw new ModToolException($"{modId}/{path} does not exist.");
        var diagnostics = new List<DataDiagnostic>();
        var document = BotDocument.Open(dir, path, diagnostics) ?? throw new ModToolException(string.Join("; ", diagnostics));
        return BotStateOf(Add(_bots, "bot", document));
    }

    /// <summary>The document as it stands.</summary>
    public BotState GetBot(string doc) => State(Bot(doc));

    /// <summary>One undo step: merges <paramref name="set"/> into the bot (null removes a key).</summary>
    public BotState EditBot(string doc, JsonElement set, string? label)
    {
        var document = Bot(doc);
        var changes = JsonNode.Parse(set.GetRawText()) as JsonObject ?? throw new ModToolException("set must be an object.");
        if (changes.ContainsKey("id") && changes["id"]?.GetValueKind() != JsonValueKind.String)
            throw new ModToolException("id must be a string.");
        document.Merge(changes, string.IsNullOrWhiteSpace(label) ? "Edit" : label);
        return State(document);
    }

    /// <summary>
    /// A behaviour tree edit at a node path (child indexes from the root; [] is the root): <c>add</c> a node of
    /// <paramref name="type"/> under it, <c>remove</c> it, <c>move</c> it by <paramref name="delta"/> among its
    /// siblings, or <c>set</c> its <paramref name="name"/> parameter to <paramref name="value"/> (null removes it).
    /// Returns the affected node's path and the document.
    /// </summary>
    public (IReadOnlyList<int>? Path, BotState State) EditBotTree(string doc, string op, IReadOnlyList<int> path, string? type, int? delta,
        string? name, JsonElement? value)
    {
        var document = Bot(doc);
        IReadOnlyList<int>? result = op switch
        {
            "add" => document.AddNode(path, type ?? throw new ModToolException("add needs a node type."))
                ?? throw new ModToolException($"A '{type}' node can't go under {Describe(path)}."),
            "remove" => document.RemoveNode(path) ? null : throw new ModToolException($"There is no node at {Describe(path)}."),
            "move" => document.MoveNode(path, delta ?? throw new ModToolException("move needs a delta."))
                ?? throw new ModToolException($"The node at {Describe(path)} can't move by {delta}."),
            "set" => document.SetParameter(path, name ?? throw new ModToolException("set needs a parameter name."),
                    value is { ValueKind: not JsonValueKind.Null } v ? JsonNode.Parse(v.GetRawText()) : null)
                ? path : throw new ModToolException($"Can't set '{name}' at {Describe(path)}."),
            _ => throw new ModToolException($"'{op}' is not a tree edit (add, remove, move, set)."),
        };
        return (result, State(document));
    }

    /// <summary>Undoes (or with <paramref name="redo"/>, redoes) one step.</summary>
    public BotState UndoBot(string doc, bool redo)
    {
        var document = Bot(doc);
        if (!(redo ? document.Redo() : document.Undo()))
            throw new ModToolException(redo ? "Nothing to redo." : "Nothing to undo.");
        return State(document);
    }

    /// <summary>Checks the bot exactly as loading would, against the parts the mods have on disk now.</summary>
    public IReadOnlyList<DiagnosticInfo> ValidateBot(string doc)
    {
        var document = Bot(doc);
        return Tag("bots", document.ModId, document.Validate(Catalog(document.ModId)));
    }

    /// <summary>Exports the bot into its mod.</summary>
    public ExportInfo ExportBot(string doc)
    {
        var document = Bot(doc);
        return Info("bots", document.ModId, BotExporter.Export(document, Catalog(document.ModId)), document.ModDirectory);
    }

    /// <summary>The bot's own script, or null when it has none in its mod.</summary>
    public string? BotScript(string doc) => BotExporter.ReadScript(Bot(doc));

    /// <summary>
    /// Writes the bot's own script (after a compile check) and points the bot at it. Returns the script path.
    /// </summary>
    public string SaveBotScript(string doc, string code)
    {
        var document = Bot(doc);
        if (Workspace.CheckScript(code) is { } problem)
            throw new ModToolException($"The script does not compile: line {problem.Line}: {problem.Message}");
        return BotExporter.SaveScript(document, code);
    }

    /// <summary>A mod's raid table, or null when it has none.</summary>
    public JsonElement? Raids(string modId)
    {
        var diagnostics = new List<DataDiagnostic>();
        var raids = BotExporter.ReadRaids(Workspace.Files.ModDirectory(modId), diagnostics);
        if (raids is null && diagnostics.Count > 0)
            throw new ModToolException(string.Join("; ", diagnostics));
        return raids is null ? null : JsonDocument.Parse(DataJson.Write(raids)).RootElement.Clone();
    }

    /// <summary>Checks a raid table against the bots on disk and writes it to the mod's <c>raids/raids.json</c>.</summary>
    public ExportInfo SaveRaids(string modId, JsonElement raids)
    {
        var dir = WritableMod(modId);
        var definition = Parse<RaidTableDefinition>(raids, "raids");
        var mods = Load(modId);
        return Info("raids", modId, BotExporter.ExportRaids(dir, definition, BotBook.Load(mods)), dir);
    }

    // ---- Recipes ----

    /// <summary>Every recipe file of a mod, with its recipe.</summary>
    public IReadOnlyList<(string File, JsonElement Recipe)> Recipes(string modId)
    {
        var diagnostics = new List<DataDiagnostic>();
        var files = RecipeExporter.ReadAll(Workspace.Files.ModDirectory(modId), diagnostics);
        return [.. files.Select(f => (f.RelativeFile, JsonDocument.Parse(DataJson.Write(f.Recipe)).RootElement.Clone()))];
    }

    /// <summary>
    /// Checks and writes a recipe as the Recipes tab does; <paramref name="previousFile"/> is the file it came
    /// from, removed when its id changed.
    /// </summary>
    public ExportInfo SaveRecipe(string modId, JsonElement recipe, string? previousFile)
    {
        var dir = WritableMod(modId);
        if (previousFile is not null)
            ModFiles.Resolve(dir, previousFile, file: true);
        var definition = Parse<RecipeDefinition>(recipe, "recipe");
        return Info("recipes", modId, RecipeExporter.Export(dir, definition, Load(modId), previousFile: previousFile), dir);
    }

    /// <summary>Deletes a recipe file of a mod.</summary>
    public void DeleteRecipe(string modId, string path)
    {
        var dir = WritableMod(modId);
        ModFiles.Resolve(dir, path, file: true);
        if (!RecipeExporter.Delete(dir, path))
            throw new ModToolException($"{modId}/{path} is not a recipe file.");
    }

    // ---- Sound banks ----

    /// <summary>A mod's sound event files.</summary>
    public IReadOnlyList<string> SoundBanks(string modId) => SoundBankDocument.Files(Workspace.Files.ModDirectory(modId));

    /// <summary>The events of a sound bank (the mod's default bank when <paramref name="path"/> is null); empty when it is new.</summary>
    public JsonElement SoundBank(string modId, string? path)
    {
        var bank = OpenBank(Workspace.Files.ModDirectory(modId), modId, path).Bank;
        return JsonDocument.Parse(DataJson.Write(bank)).RootElement.Clone();
    }

    /// <summary>Replaces a sound bank's events, checks them as loading would and writes the file.</summary>
    public ExportInfo SaveSoundBank(string modId, string? path, JsonElement events)
    {
        var dir = WritableMod(modId);
        var document = OpenBank(dir, modId, path);
        var list = Parse<List<SoundEventDefinition>>(events, "events");
        while (document.Events.Count > 0)
            document.Remove(document.Events.Count - 1);
        foreach (var e in list)
            document.Set(document.Add(e.Id ?? "sound"), e);
        var result = document.Save();
        var exported = new ExportResult(result.Succeeded, result.Succeeded ? result.File : null, $"{modId}:{document.RelativeFile}", result.Diagnostics);
        return Info("sounds", modId, exported, dir);
    }

    // ---- helpers ----

    private static SoundBankDocument OpenBank(string dir, string modId, string? path)
    {
        if (path is not null)
        {
            ModFiles.Resolve(dir, path, file: true);
            if (!path.StartsWith(SoundRegistry.Folder + "/", StringComparison.Ordinal) || !path.EndsWith(".json", StringComparison.Ordinal))
                throw new ModToolException($"Sound banks are .json files under {SoundRegistry.Folder}/.");
        }
        var diagnostics = new List<DataDiagnostic>();
        return SoundBankDocument.Open(dir, path ?? SoundBankDocument.DefaultFile(modId), diagnostics)
            ?? throw new ModToolException(string.Join("; ", diagnostics));
    }

    private string WritableMod(string modId)
    {
        var dir = Workspace.Files.ModDirectory(modId);
        if (modId == Identifiers.VanillaModId)
            throw new ModToolException("Vanilla is authored through its editor batches: edit a batch under vanilla/editor/batches/ and run it.");
        return dir;
    }

    // The mods on disk now, with modId enabled, so an edit is checked against what a reload would load.
    private ModLoadResult Load(string modId)
    {
        var manager = Workspace.Manager();
        if (manager.Mods.Any(m => m.Id == modId))
            manager.SetEnabled(modId, true);
        return ModLoader.Load(Workspace.ModsRoot, manager.ToProfile());
    }

    private IPartCatalog Catalog(string modId) => Load(modId);

    private string Add<T>(Dictionary<string, T> into, string kind, T document)
    {
        if (_parts.Count + _bots.Count >= MaxDocuments)
            throw new ModToolException($"{MaxDocuments} documents are open; close some with editor.close.");
        var id = $"{kind}_{++_next}";
        into[id] = document;
        return id;
    }

    private PartDocument Part(string doc) => _parts.GetValueOrDefault(doc) ?? throw new ModToolException($"No part document '{doc}' is open.");

    private BotDocument Bot(string doc) => _bots.GetValueOrDefault(doc) ?? throw new ModToolException($"No bot document '{doc}' is open.");

    private PartState PartStateOf(string doc) => State(doc, _parts[doc]);

    private PartState State(PartDocument document) => State(_parts.First(p => ReferenceEquals(p.Value, document)).Key, document);

    private static PartState State(string doc, PartDocument d) => new(doc, d.ModId, d.RelativeFile,
        JsonDocument.Parse(DataJson.Write(d.Definition)).RootElement.Clone(),
        JsonDocument.Parse(PartSource.Write(d.Source)).RootElement.Clone(), d.IsDirty, d.UndoLabel, d.RedoLabel, d.Source.NeedsBake);

    private BotState State(BotDocument document)
    {
        var doc = _bots.First(p => ReferenceEquals(p.Value, document)).Key;
        return new BotState(doc, document.ModId, document.RelativeFile, JsonSerializer.SerializeToElement(document.Json), document.IsDirty, document.UndoLabel);
    }

    private BotState BotStateOf(string doc) => State(_bots[doc]);

    private static T Parse<T>(JsonElement json, string name) where T : class
    {
        var diagnostics = new List<DataDiagnostic>();
        return ModLoader.ParseJson<T>(json.GetRawText(), name, diagnostics)
            ?? throw new ModToolException($"{name} does not fit the schema: {string.Join("; ", diagnostics.Select(d => $"{d.JsonPath} {d.Message}"))}");
    }

    private static void Merge(JsonObject target, JsonObject changes)
    {
        foreach (var (key, value) in changes.ToList())
        {
            if (value is null)
                target.Remove(key);
            else if (value is JsonObject inner && target[key] is JsonObject existing)
                Merge(existing, inner);
            else
                target[key] = value.DeepClone();
        }
    }

    private static string Describe(IReadOnlyList<int> path) => path.Count == 0 ? "the root" : $"[{string.Join(", ", path)}]";

    private static IReadOnlyList<DiagnosticInfo> Tag(string domain, string modId, IEnumerable<DataDiagnostic> diagnostics) =>
        [.. diagnostics.Select(d => DiagnosticInfo.ForMod(domain, modId, d))];

    private static ExportInfo Info(string domain, string modId, ExportResult result, string modDirectory) =>
        new(result.Succeeded, result.QualifiedId,
            result.PartFile is { } file ? Path.GetRelativePath(modDirectory, file).Replace('\\', '/') : null,
            Tag(domain, modId, result.Diagnostics));
}
