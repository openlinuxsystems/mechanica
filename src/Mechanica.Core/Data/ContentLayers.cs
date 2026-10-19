using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mechanica.Core.Data;

/// <summary>
/// One entry of a content folder (bots, quests, POIs, characters) after every mod's overrides, patches and removals
/// are applied in load order. The loader checks <see cref="Json"/> as if it were in <see cref="OwnerModId"/>.
/// </summary>
/// <param name="OwnerModId">The mod the entry belongs to; its game-wide id is <c>owner:id</c>.</param>
/// <param name="Id">The entry's id within its owner.</param>
/// <param name="SourceModId">The mod whose file last changed it (the owner for an unchanged entry).</param>
/// <param name="DisplayFile">Where diagnostics point: <c>mod/path.json</c> of the last file that changed it.</param>
/// <param name="Json">The entry as JSON. Relative asset paths from other mods than the owner are written <c>mod:path</c>.</param>
public sealed record ContentEntry(string OwnerModId, string Id, string SourceModId, string DisplayFile, string Json)
{
    /// <summary>True when another mod replaced or patched the entry.</summary>
    public bool IsLayered => SourceModId != OwnerModId;

    /// <summary>The entry's game-wide id.</summary>
    public string QualifiedId => $"{OwnerModId}:{Id}";
}

/// <summary>
/// How a mod changes content another mod owns, the same way for every content kind (D6). Inside
/// <c>overrides/&lt;other_mod&gt;/</c>:
/// <list type="bullet">
/// <item><c>&lt;folder&gt;/**/x.json</c> replaces the entry with the same id (parts use any path outside the
/// reserved folders, recipes use <c>recipes/</c>);</item>
/// <item><c>&lt;folder&gt;/**/x.patch.json</c> is a JSON merge patch (RFC 7386) on the entry: objects merge key by key,
/// <c>null</c> removes a key, everything else replaces. A patch keeps working when the other mod changes keys it
/// doesn't touch;</item>
/// <item><c>remove.json</c> lists ids to take out per kind: <c>{"parts": ["wheel"], "bots": ["scuttler"]}</c>.</item>
/// </list>
/// Mods apply in load order, so a later mod builds on an earlier one's changes; two unrelated mods changing one entry
/// is a warning, and the later wins.
/// </summary>
public static class ContentLayers
{
    /// <summary>The removal list inside <c>overrides/&lt;other_mod&gt;/</c>.</summary>
    public const string RemoveFile = "remove.json";

    /// <summary>File name ending of a merge patch.</summary>
    public const string PatchSuffix = ".patch.json";

    /// <summary>
    /// Folders inside <c>overrides/&lt;other_mod&gt;/</c> that hold other content than parts, with the key each uses in
    /// <see cref="RemoveFile"/>. Part overrides may sit anywhere else under <c>overrides/&lt;other_mod&gt;/</c>.
    /// </summary>
    public static IReadOnlyList<string> ReservedFolders { get; } = ["recipes", "bots", "quests", "pois", "characters"];

    /// <summary>Keys <see cref="RemoveFile"/> accepts: <c>parts</c> plus every reserved folder.</summary>
    public static IReadOnlyList<string> RemoveKeys { get; } = ["parts", .. ReservedFolders];

    private static readonly string[] AssetExtensions = [".glb", ".gltf", ".png", ".lua", ".ogg", ".wav"];

    /// <summary>
    /// Every entry of <paramref name="folder"/> (<c>bots</c>, <c>quests</c>…) across the mods of
    /// <paramref name="content"/>, with overrides, patches and removals applied. <paramref name="idOf"/> gives an
    /// entry's id from its mod-relative file path and JSON (null: the file has no usable id, reported by the caller's
    /// checks). Duplicate ids within a mod and changes to entries that don't exist are reported here.
    /// </summary>
    public static IReadOnlyList<ContentEntry> Collect(ModLoadResult content, string folder, Func<string, JsonObject, string?> idOf,
        List<DataDiagnostic> diagnostics, LayerCodes codes)
    {
        var entries = new Dictionary<string, ContentEntry>(StringComparer.Ordinal);
        // Who last replaced, patched or removed each entry, to warn when two unrelated mods both change one.
        var changedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        var removed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mod in content.Mods)
        {
            foreach (var (path, relative) in JsonFiles(mod.Directory, Path.Combine(mod.Directory, folder)))
            {
                var display = $"{mod.Id}/{relative}";
                if (Read(path, display, diagnostics) is not { } json)
                    continue;
                var id = idOf(relative, json) ?? $"\u0000{relative}"; // the caller's checks report the missing id
                var key = $"{mod.Id}:{id}";
                if (entries.TryGetValue(key, out var other))
                {
                    diagnostics.Add(Error(codes.Duplicate, display, "$.id", $"'{key}' is defined twice (also in {other.DisplayFile})."));
                    continue;
                }
                entries.Add(key, new ContentEntry(mod.Id, id, mod.Id, display, json.ToJsonString()));
            }

            var overridesDir = Path.Combine(mod.Directory, ModLoader.OverridesFolder);
            if (!Directory.Exists(overridesDir))
                continue;
            foreach (var targetDir in Directory.GetDirectories(overridesDir).Order(StringComparer.Ordinal))
            {
                var target = Path.GetFileName(targetDir);
                // The mod loader refuses a mod whose overrides name a mod it doesn't depend on, so the target is loaded.
                foreach (var (path, relative) in JsonFiles(mod.Directory, Path.Combine(targetDir, folder)))
                {
                    var display = $"{mod.Id}/{relative}";
                    if (Read(path, display, diagnostics) is not { } json)
                        continue;
                    var patch = relative.EndsWith(PatchSuffix, StringComparison.Ordinal);
                    var id = patch ? PatchId(relative, json) : idOf(relative.Replace(PatchSuffix, ".json", StringComparison.Ordinal), json);
                    if (id is null)
                    {
                        diagnostics.Add(Error(codes.Invalid, display, "$.id", "id is required."));
                        continue;
                    }
                    var key = $"{target}:{id}";
                    if (!entries.TryGetValue(key, out var current))
                    {
                        diagnostics.Add(Error(DiagnosticCodes.ContentOverrideUnknown, display, "$.id", removed.Contains(key)
                            ? $"'{key}' was removed by an earlier mod, so there is nothing to {(patch ? "patch" : "override")}."
                            : $"mod '{target}' has no {folder} entry '{id}' to {(patch ? "patch" : "override")}."));
                        continue;
                    }
                    WarnClash(content, changedBy, key, mod.Id, display, diagnostics);
                    PointAssetsAt(json, mod.Id);
                    JsonNode next;
                    if (patch)
                    {
                        next = JsonNode.Parse(current.Json)!;
                        json.Remove("id");
                        next = MergePatch(next, json)!;
                    }
                    else
                    {
                        next = json;
                    }
                    entries[key] = current with { SourceModId = mod.Id, DisplayFile = display, Json = next.ToJsonString() };
                }

                foreach (var id in ReadRemovals(mod.Id, targetDir, folder, diagnostics))
                {
                    var key = $"{target}:{id}";
                    var display = $"{mod.Id}/{ModLoader.OverridesFolder}/{target}/{RemoveFile}";
                    if (!entries.Remove(key))
                    {
                        diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Warning, DiagnosticCodes.ContentOverrideUnknown, display, $"$.{folder}", null,
                            $"mod '{target}' has no {folder} entry '{id}' to remove."));
                        continue;
                    }
                    WarnClash(content, changedBy, key, mod.Id, display, diagnostics);
                    removed.Add(key);
                }
            }
        }
        return [.. entries.Values.OrderBy(e => e.QualifiedId, StringComparer.Ordinal)];
    }

    /// <summary>The <c>"id"</c> member of an entry, when it is a string (the usual <c>idOf</c> for <see cref="Collect"/>).</summary>
    public static string? IdMember(string relativePath, JsonObject json) =>
        json["id"] is JsonValue v && v.TryGetValue<string>(out var id) ? id : null;

    /// <summary>
    /// The ids <c>overrides/&lt;target&gt;/remove.json</c> of a mod lists under <paramref name="key"/> (<c>parts</c>,
    /// <c>bots</c>…). A missing file is an empty list; a broken one is reported.
    /// </summary>
    public static IReadOnlyList<string> ReadRemovals(string modId, string targetDirectory, string key, List<DataDiagnostic> diagnostics)
    {
        var path = Path.Combine(targetDirectory, RemoveFile);
        if (!File.Exists(path))
            return [];
        var display = $"{modId}/{ModLoader.OverridesFolder}/{Path.GetFileName(targetDirectory)}/{RemoveFile}";
        if (Read(path, display, diagnostics) is not { } json)
            return [];
        var ids = new List<string>();
        foreach (var (name, value) in json)
        {
            if (!RemoveKeys.Contains(name))
            {
                if (key == RemoveKeys[0]) // report once, while the parts are loaded
                    diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Warning, DiagnosticCodes.UnknownKey, display, $"$.{name}", null,
                        $"unknown key '{name}'; {RemoveFile} lists ids under {string.Join(", ", RemoveKeys)}."));
                continue;
            }
            if (name != key)
                continue;
            if (value is not JsonArray array || array.Any(v => v is not JsonValue s || !s.TryGetValue<string>(out var id) || !Identifiers.IsValid(id)))
            {
                diagnostics.Add(Error(DiagnosticCodes.FileUnreadable, display, $"$.{name}", $"{name} must be a list of ids (lowercase snake_case)."));
                continue;
            }
            ids.AddRange(array.Select(v => v!.GetValue<string>()));
        }
        return ids;
    }

    /// <summary>
    /// Applies a JSON merge patch (RFC 7386) to <paramref name="target"/>: objects merge key by key, <c>null</c> removes
    /// a key, anything else replaces. Returns the result (a new node when <paramref name="patch"/> isn't an object).
    /// </summary>
    public static JsonNode? MergePatch(JsonNode? target, JsonNode? patch)
    {
        if (patch is not JsonObject patchObject)
            return patch?.DeepClone();
        if (target is not JsonObject targetObject)
            targetObject = [];
        foreach (var (key, value) in patchObject)
        {
            if (value is null)
                targetObject.Remove(key);
            else
                targetObject[key] = MergePatch(targetObject[key]?.DeepClone(), value);
        }
        return targetObject;
    }

    /// <summary>Rewrites every relative asset path in <paramref name="node"/> (<c>meshes/x.glb</c>) to <c>modId:path</c>.</summary>
    public static void PointAssetsAt(JsonNode? node, string modId)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (obj[key] is JsonValue v && v.TryGetValue<string>(out var s) && IsRelativeAsset(s))
                        obj[key] = $"{modId}:{s}";
                    else
                        PointAssetsAt(obj[key], modId);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue v && v.TryGetValue<string>(out var s) && IsRelativeAsset(s))
                        array[i] = $"{modId}:{s}";
                    else
                        PointAssetsAt(array[i], modId);
                }
                break;
        }
    }

    /// <summary>True for a string written as a path inside the mod that holds it (<c>meshes/x.glb</c>).</summary>
    public static bool IsRelativeAsset(string text) =>
        !text.Contains(':') && text.Contains('/')
        && AssetExtensions.Contains(Path.GetExtension(text).ToLowerInvariant());

    // A patch names its entry by "id", or by its file name (wheel.patch.json patches wheel).
    private static string? PatchId(string relative, JsonObject json)
    {
        if (json["id"] is JsonValue v && v.TryGetValue<string>(out var id))
            return id;
        var name = Path.GetFileName(relative);
        return name[..^PatchSuffix.Length];
    }

    private static void WarnClash(ModLoadResult content, Dictionary<string, string> changedBy, string key, string modId, string display,
        List<DataDiagnostic> diagnostics)
    {
        if (changedBy.TryGetValue(key, out var previous) && previous != modId && !DependsOn(content, modId, previous))
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Warning, DiagnosticCodes.ContentOverrideConflict, display, "$.id", null,
                $"mods '{previous}' and '{modId}' both change '{key}'; '{modId}' loads later and wins. "
                + "Change the load order in the Mod Manager to pick the other."));
        changedBy[key] = modId;
    }

    /// <summary>True when <paramref name="modId"/> depends on <paramref name="otherId"/>, directly or through other mods.</summary>
    public static bool DependsOn(ModLoadResult content, string modId, string otherId)
    {
        var mods = content.Mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>([modId]);
        while (stack.TryPop(out var id))
        {
            foreach (var dep in mods.GetValueOrDefault(id)?.Manifest.Dependencies ?? [])
            {
                if (dep.Id == otherId)
                    return true;
                if (dep.Id is not null && seen.Add(dep.Id))
                    stack.Push(dep.Id);
            }
        }
        return false;
    }

    private static JsonObject? Read(string path, string display, List<DataDiagnostic> diagnostics)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
                { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is JsonObject json)
                return json;
            diagnostics.Add(Error(DiagnosticCodes.InvalidJson, display, null, "the file must hold one JSON object."));
        }
        catch (JsonException e)
        {
            diagnostics.Add(Error(DiagnosticCodes.InvalidJson, display, null, $"invalid JSON: {e.Message}"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(DiagnosticCodes.FileUnreadable, display, null, $"could not read file: {e.Message}"));
        }
        return null;
    }

    private static IEnumerable<(string Path, string Relative)> JsonFiles(string modDir, string folder) => Directory.Exists(folder)
        ? Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories)
            .Select(f => (f, Path.GetRelativePath(modDir, f).Replace('\\', '/')))
            .OrderBy(f => f.Item2, StringComparer.Ordinal)
        : [];

    private static DataDiagnostic Error(string code, string file, string? path, string message) =>
        new(DiagnosticSeverity.Error, code, file, path, null, message);
}

/// <summary>The diagnostic codes a content kind reports from <see cref="ContentLayers.Collect"/>.</summary>
/// <param name="Invalid">A file that can't be read as an entry.</param>
/// <param name="Duplicate">An id defined twice in one mod.</param>
public sealed record LayerCodes(string Invalid, string Duplicate);
