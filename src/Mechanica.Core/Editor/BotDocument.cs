using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;

namespace Mechanica.Core.Editor;

/// <summary>
/// One bot open in the Bot Behavior Editor (chunk 21, D9): its bot file as JSON (stats, attacks, drops,
/// behaviour tree), the mod it belongs to and an undo history. Every change goes through a method here,
/// which records the previous state, so the UI never edits the JSON directly. Tree nodes are addressed by
/// a path of child indices from the root (the root itself is the empty path). Pure logic; the Godot editor
/// shows it and <see cref="BotExporter"/> writes it.
/// </summary>
public sealed class BotDocument
{
    /// <summary>How many steps <see cref="Undo"/> can go back.</summary>
    public const int HistoryLimit = 200;

    private readonly List<(string Json, string Label)> _undo = [];
    private readonly List<(string Json, string Label)> _redo = [];
    private JsonObject _json;
    private string? _saved;

    private BotDocument(string modDirectory, JsonObject json, string? relativeFile)
    {
        ModDirectory = Path.GetFullPath(modDirectory);
        _json = json;
        RelativeFile = relativeFile;
        if (relativeFile is not null)
            _saved = Text;
    }

    /// <summary>Raised after every change, undo and redo.</summary>
    public event Action? Changed;

    /// <summary>Absolute folder of the mod the bot is saved into.</summary>
    public string ModDirectory { get; }

    /// <summary>The mod id (its folder name).</summary>
    public string ModId => Path.GetFileName(ModDirectory);

    /// <summary>The bot file as it stands. Read only: change it through this class.</summary>
    public JsonObject Json => _json;

    /// <summary>The bot's id.</summary>
    public string? Id => _json["id"] is JsonValue v && v.TryGetValue<string>(out var id) ? id : null;

    /// <summary>The file it was opened from or last exported to (<c>bots/scuttler_green.json</c>), or null.</summary>
    public string? RelativeFile { get; internal set; }

    /// <summary>The file export writes: <c>bots/&lt;id&gt;.json</c>.</summary>
    public string TargetFile => $"{BotBook.Folder}/{Id ?? "new_bot"}.json";

    /// <summary>The behaviour tree's root, or null when the bot has none.</summary>
    public JsonObject? Tree => _json["behavior"] as JsonObject;

    /// <summary>True when there are changes since it was opened or last exported.</summary>
    public bool IsDirty => _saved != Text;

    /// <summary>True when <see cref="Undo"/> has something to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True when <see cref="Redo"/> has something to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Label of the step <see cref="Undo"/> would undo.</summary>
    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    private string Text => _json.ToJsonString();

    /// <summary>A new bot in the mod at <paramref name="modDirectory"/>, started from a template's JSON, with <paramref name="id"/>.</summary>
    public static BotDocument New(string modDirectory, string templateJson, string id, string name)
    {
        var json = JsonNode.Parse(templateJson, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject ?? throw new FormatException("A bot template must be a JSON object.");
        json["id"] = id;
        json["name"] = name;
        return new BotDocument(modDirectory, json, null);
    }

    /// <summary>Opens <paramref name="relativeFile"/> (<c>bots/x.json</c>) in the mod at <paramref name="modDirectory"/>; null with diagnostics when it can't be read.</summary>
    public static BotDocument? Open(string modDirectory, string relativeFile, List<DataDiagnostic> diagnostics)
    {
        var path = Path.Combine(modDirectory, relativeFile);
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.FileUnreadable, relativeFile, null, null, $"could not read file: {e.Message}"));
            return null;
        }
        // Read through the schema first, so an unreadable file is reported as the loader would.
        if (ModLoader.ParseJson<BotDefinition>(text, relativeFile, diagnostics) is not { } definition)
            return null;
        return new BotDocument(modDirectory, (JsonObject)JsonNode.Parse(DataJson.Write(definition))!, relativeFile);
    }

    /// <summary>Every bot file in the mod, ordered by path.</summary>
    public static IReadOnlyList<string> Files(string modDirectory)
    {
        var folder = Path.Combine(modDirectory, BotBook.Folder);
        return Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(modDirectory, f).Replace('\\', '/')).Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>The bot as its schema reads it; null when the JSON does not fit the schema.</summary>
    public BotDefinition? Definition(List<DataDiagnostic>? diagnostics = null) =>
        ModLoader.ParseJson<BotDefinition>(Text, TargetFile, diagnostics ?? []);

    /// <summary>Checks the bot exactly as loading would. <paramref name="catalog"/> resolves drop items.</summary>
    public IReadOnlyList<DataDiagnostic> Validate(IPartCatalog catalog)
    {
        var diagnostics = new List<DataDiagnostic>();
        if (Definition(diagnostics) is { } definition)
            diagnostics.AddRange(BotBook.Validate(definition, ModId, ModDirectory, catalog, TargetFile));
        return diagnostics;
    }

    // ---- Edits ----

    /// <summary>Sets a top-level key (<c>health</c>, <c>attacks</c>, …); null removes it.</summary>
    public void Set(string key, JsonNode? value) => Edit($"Set {key}", json =>
    {
        if (value is null)
            json.Remove(key);
        else
            json[key] = value.DeepClone();
        return true;
    });

    /// <summary>
    /// Applies a batch's <c>set</c>: objects merge key by key, anything else replaces and null removes, except
    /// <c>behavior</c>, which always replaces (a tree is one thing).
    /// </summary>
    public void Merge(JsonObject changes, string label = "Batch edit") => Edit(label, json =>
    {
        MergeInto(json, changes, topLevel: true);
        return true;
    });

    /// <summary>The node at <paramref name="path"/>, or null.</summary>
    public JsonObject? Node(IReadOnlyList<int> path)
    {
        var node = Tree;
        foreach (var index in path)
        {
            if (node?["children"] is not JsonArray children || index < 0 || index >= children.Count)
                return null;
            node = children[index] as JsonObject;
        }
        return node;
    }

    /// <summary>
    /// Adds a node of <paramref name="type"/>: as the root when the tree is empty and <paramref name="parent"/>
    /// is empty, otherwise as the last child of the composite or decorator at <paramref name="parent"/>
    /// (a decorator takes one child). Built-in parameters start at their defaults. Returns the new node's
    /// path, or null when it can't go there.
    /// </summary>
    public IReadOnlyList<int>? AddNode(IReadOnlyList<int> parent, string type)
    {
        if (!Identifiers.IsValid(type))
            return null;
        var node = NewNode(type);
        if (Tree is null)
        {
            if (parent.Count != 0)
                return null;
            Edit($"Add {type}", json =>
            {
                json["behavior"] = node;
                return true;
            });
            return [];
        }
        var target = Node(parent);
        BotNodeShape? shape = target is null ? null : ShapeOf(target);
        if (target is null || shape == BotNodeShape.Leaf)
            return null;
        var count = (target["children"] as JsonArray)?.Count ?? 0;
        if (shape == BotNodeShape.Decorator && count >= 1)
            return null;
        Edit($"Add {type}", json =>
        {
            var at = NodeIn(json, parent)!;
            if (at["children"] is not JsonArray children)
                at["children"] = children = [];
            children.Add(node);
            return true;
        });
        return [.. parent, count];
    }

    /// <summary>Removes the node at <paramref name="path"/> and everything under it (the whole tree for the root).</summary>
    public bool RemoveNode(IReadOnlyList<int> path)
    {
        if (Node(path) is null)
            return false;
        return Edit("Remove node", json =>
        {
            if (path.Count == 0)
                json.Remove("behavior");
            else
            {
                var children = (JsonArray)NodeIn(json, path.Take(path.Count - 1).ToList())!["children"]!;
                children.RemoveAt(path[^1]);
            }
            return true;
        });
    }

    /// <summary>Moves the node at <paramref name="path"/> among its siblings by <paramref name="delta"/>. Returns its new path, or null.</summary>
    public IReadOnlyList<int>? MoveNode(IReadOnlyList<int> path, int delta)
    {
        if (path.Count == 0 || Node([.. path.Take(path.Count - 1)]) is not { } parent || parent["children"] is not JsonArray siblings)
            return null;
        var to = path[^1] + delta;
        if (to < 0 || to >= siblings.Count || delta == 0)
            return null;
        Edit("Move node", json =>
        {
            var children = (JsonArray)NodeIn(json, path.Take(path.Count - 1).ToList())!["children"]!;
            var node = children[path[^1]]!;
            children.RemoveAt(path[^1]);
            children.Insert(to, node);
            return true;
        });
        return [.. path.Take(path.Count - 1), to];
    }

    /// <summary>Sets a node parameter (<c>attack</c>, <c>range</c>, …); null removes it. <c>type</c> and <c>children</c> can't be set this way.</summary>
    public bool SetParameter(IReadOnlyList<int> path, string name, JsonNode? value)
    {
        if (Node(path) is null || name is "type" or "children" || name.Length == 0)
            return false;
        return Edit($"Set {name}", json =>
        {
            var node = NodeIn(json, path)!;
            if (value is null)
                node.Remove(name);
            else
                node[name] = value.DeepClone();
            return true;
        });
    }

    /// <summary>Undoes the last change. False when there is nothing to undo.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;
        var (json, label) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add((Text, label));
        _json = (JsonObject)JsonNode.Parse(json)!;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Redoes the last undone change. False when there is nothing to redo.</summary>
    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;
        var (json, label) = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add((Text, label));
        _json = (JsonObject)JsonNode.Parse(json)!;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Marks the document as saved to <paramref name="relativeFile"/>.</summary>
    internal void MarkSaved(string relativeFile)
    {
        RelativeFile = relativeFile;
        _saved = Text;
    }

    /// <summary>A node of <paramref name="type"/> with the built-in parameters that have defaults filled in.</summary>
    public static JsonObject NewNode(string type)
    {
        var node = new JsonObject { ["type"] = type };
        if (BotBehavior.Find(type) is not { } info)
            return node;
        foreach (var p in info.Parameters.Where(p => p.Default.Length > 0))
            node[p.Name] = p.Kind == "number" && double.TryParse(p.Default, System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? JsonValue.Create(number)
                : JsonValue.Create(p.Default);
        if (info.Shape != BotNodeShape.Leaf)
            node["children"] = new JsonArray();
        return node;
    }

    /// <summary>How a node takes children: built-in nodes by their kind; anything else (a script's leaf) is a leaf.</summary>
    public static BotNodeShape ShapeOf(JsonObject node) =>
        node["type"] is JsonValue v && v.TryGetValue<string>(out var type) && BotBehavior.Find(type) is { } info ? info.Shape : BotNodeShape.Leaf;

    private static JsonObject? NodeIn(JsonObject json, IReadOnlyList<int> path)
    {
        var node = json["behavior"] as JsonObject;
        foreach (var index in path)
            node = (node?["children"] as JsonArray)?[index] as JsonObject;
        return node;
    }

    private bool Edit(string label, Func<JsonObject, bool> change)
    {
        var before = Text;
        var copy = (JsonObject)JsonNode.Parse(before)!;
        if (!change(copy) || copy.ToJsonString() == before)
            return false;
        _undo.Add((before, label));
        if (_undo.Count > HistoryLimit)
            _undo.RemoveAt(0);
        _redo.Clear();
        _json = copy;
        Changed?.Invoke();
        return true;
    }

    private static void MergeInto(JsonObject target, JsonObject changes, bool topLevel)
    {
        foreach (var (key, value) in changes)
        {
            if (value is null)
                target.Remove(key);
            else if (value is JsonObject inner && target[key] is JsonObject existing && !(topLevel && key == "behavior"))
                MergeInto(existing, inner, topLevel: false);
            else
                target[key] = value.DeepClone();
        }
    }
}
