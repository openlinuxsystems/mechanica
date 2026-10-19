using System.Text.Json.Nodes;
using Mechanica.Core.Data;

namespace Mechanica.Core.Editor;

/// <summary>How <see cref="PartDerive.Derive"/> starts a part in a mod from a part of another mod.</summary>
public enum PartDeriveMode
{
    /// <summary>
    /// An override in <c>overrides/&lt;owner&gt;/</c>: same id, replaces the original everywhere (saved Creations
    /// and recipes keep working).
    /// </summary>
    Override,

    /// <summary>A new part of the target mod with its own id, starting as a copy of the original.</summary>
    Copy,
}

/// <summary>
/// The Part Editor's "Override in my mod" and "Copy as new part": a new, unsaved <see cref="PartDocument"/> in
/// another mod that starts as a copy of an open part. Relative asset paths (meshes, textures, scripts) are
/// rewritten to point back at the original mod (<c>vanilla:meshes/x.glb</c>), so the copy looks and behaves the
/// same until the modder changes it, and the target mod gets the dependencies the result needs.
/// </summary>
public static class PartDerive
{
    private static readonly string[] AssetExtensions = [".glb", ".gltf", ".png", ".lua", ".ogg", ".wav"];

    /// <summary>
    /// The mod whose part <paramref name="document"/> really is: the folder mod for its own parts, the overridden
    /// mod for a file under <c>overrides/&lt;owner&gt;/</c>.
    /// </summary>
    public static string OwnerOf(PartDocument document)
    {
        var parts = (document.RelativeFile ?? "").Split('/');
        return parts.Length > 2 && parts[0] == ModLoader.OverridesFolder ? parts[1] : document.ModId;
    }

    /// <summary>
    /// Starts a part in the mod at <paramref name="targetModDirectory"/> from <paramref name="original"/>. For
    /// <see cref="PartDeriveMode.Copy"/>, <paramref name="newId"/> is the copy's id and <paramref name="newName"/>
    /// its name (the original's when empty). Nothing is written until the document is exported, except the target's
    /// <c>mod.json</c> (see <see cref="EnsureDependencies"/>). Throws <see cref="ArgumentException"/> with a
    /// player-facing message when the request makes no sense.
    /// </summary>
    public static PartDocument Derive(PartDocument original, string targetModDirectory, PartDeriveMode mode, string? newId = null, string? newName = null)
    {
        var target = Path.GetFullPath(targetModDirectory);
        var targetId = Path.GetFileName(target);
        if (!Identifiers.IsValid(targetId))
            throw new ArgumentException($"Mod id '{targetId}' must be lowercase snake_case.");
        var owner = OwnerOf(original);
        var id = original.Definition.Id ?? "part";
        var sameMod = string.Equals(target, original.ModDirectory, StringComparison.Ordinal);

        var definition = sameMod ? original.Definition : PointAssetsAt(original.Definition, original.ModId);
        var source = sameMod ? original.Source : PointAssetsAt(original.Source, original.ModId);
        string? file;
        if (mode == PartDeriveMode.Override)
        {
            if (owner == targetId)
                throw new ArgumentException($"{owner}:{id} already belongs to {targetId}; open it and edit it directly.");
            file = $"{ModLoader.OverridesFolder}/{owner}/{InnerPath(original.RelativeFile, id)}";
        }
        else
        {
            if (newId is null || !Identifiers.IsValid(newId))
                throw new ArgumentException($"Part id '{newId}' must be lowercase snake_case.");
            definition = definition with { Id = newId, Name = string.IsNullOrWhiteSpace(newName) ? definition.Name : newName.Trim() };
            file = null;
        }
        return PartDocument.Derived(target, definition, source, file);
    }

    /// <summary>
    /// Writes the target's <c>mod.json</c> when it has none and adds what a part derived from
    /// <paramref name="original"/> needs: the mod it came from (its assets) and, for an override, the overridden
    /// mod. Returns the dependency ids it added.
    /// </summary>
    public static IReadOnlyList<string> EnsureDependencies(PartDocument original, string targetModDirectory, PartDeriveMode mode)
    {
        PartExporter.EnsureManifest(targetModDirectory);
        var targetId = Path.GetFileName(Path.GetFullPath(targetModDirectory));
        var needed = new List<string>();
        if (original.ModId != targetId)
            needed.Add(original.ModId);
        if (mode == PartDeriveMode.Override && OwnerOf(original) is var owner && owner != targetId && !needed.Contains(owner))
            needed.Add(owner);
        return ModManifestFile.AddDependencies(targetModDirectory, needed);
    }

    // parts/wheels/wheel.json -> wheels/wheel.json; overrides/vanilla/wheel.json -> wheel.json.
    private static string InnerPath(string? relativeFile, string id)
    {
        if (relativeFile is null)
            return $"{id}.json";
        var parts = relativeFile.Split('/');
        if (parts[0] == ModLoader.PartsFolder && parts.Length > 1)
            return string.Join('/', parts[1..]);
        if (parts[0] == ModLoader.OverridesFolder && parts.Length > 2)
            return string.Join('/', parts[2..]);
        return parts[^1];
    }

    /// <summary>Rewrites every relative asset path in the part to <c>modId:path</c>.</summary>
    public static PartDefinition PointAssetsAt(PartDefinition definition, string modId)
    {
        var node = JsonNode.Parse(DataJson.Write(definition))!;
        Rewrite(node, modId);
        return ModLoader.ParseJson<PartDefinition>(node.ToJsonString(), "part", []) ?? definition;
    }

    /// <summary>Rewrites every relative asset path in the model pieces to <c>modId:path</c>.</summary>
    public static PartSource PointAssetsAt(PartSource source, string modId)
    {
        if (source.Pieces.Count == 0)
            return source;
        var node = JsonNode.Parse(PartSource.Write(source))!;
        Rewrite(node, modId);
        return PartSource.Read(node.ToJsonString(), "source", []) ?? source;
    }

    /// <summary>True for a string written as a path inside the mod that holds it (<c>meshes/x.glb</c>).</summary>
    public static bool IsRelativeAsset(string text) =>
        !text.Contains(':') && text.Contains('/')
        && AssetExtensions.Contains(Path.GetExtension(text).ToLowerInvariant());

    private static void Rewrite(JsonNode? node, string modId)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (obj[key] is JsonValue v && v.TryGetValue<string>(out var s) && IsRelativeAsset(s))
                        obj[key] = $"{modId}:{s}";
                    else
                        Rewrite(obj[key], modId);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue v && v.TryGetValue<string>(out var s) && IsRelativeAsset(s))
                        array[i] = $"{modId}:{s}";
                    else
                        Rewrite(array[i], modId);
                }
                break;
        }
    }
}

/// <summary>Small edits to a mod's <c>mod.json</c> made by the editors.</summary>
public static class ModManifestFile
{
    /// <summary>
    /// Adds each id in <paramref name="ids"/> that the manifest does not list yet to its dependencies (any version)
    /// and rewrites the file. Returns the ids it added.
    /// </summary>
    public static IReadOnlyList<string> AddDependencies(string modDirectory, IEnumerable<string> ids)
    {
        var path = Path.Combine(modDirectory, ModLoader.ManifestFileName);
        var manifest = ModLoader.ParseJson<ModManifest>(File.ReadAllText(path), ModLoader.ManifestFileName, [])
            ?? throw new ArgumentException($"{ModLoader.ManifestFileName} in {Path.GetFileName(modDirectory)} could not be read.");
        var existing = manifest.Dependencies ?? [];
        var added = ids.Where(id => existing.All(d => d.Id != id)).Distinct(StringComparer.Ordinal).ToList();
        if (added.Count == 0)
            return [];
        DataJson.WriteFile(path, DataJson.Write(manifest with { Dependencies = [.. existing, .. added.Select(id => new ModDependency { Id = id })] }));
        return added;
    }

    /// <summary>
    /// Rewrites a mod's name, version, description, authors and dependency ids (the Mod Manager's Edit info), keeping
    /// everything else in the file, including the version range of every dependency it still lists. Returns the
    /// problems that stopped the write (empty when it was written).
    /// </summary>
    public static IReadOnlyList<DataDiagnostic> Update(string modDirectory, string name, string version, string? description,
        IReadOnlyList<string> authors, IReadOnlyList<string> dependencies)
    {
        var path = Path.Combine(modDirectory, ModLoader.ManifestFileName);
        var problems = new List<DataDiagnostic>();
        var display = $"{Path.GetFileName(modDirectory)}/{ModLoader.ManifestFileName}";
        var manifest = ModLoader.ParseJson<ModManifest>(File.ReadAllText(path), display, problems);
        if (manifest is null)
            return problems;
        var old = manifest.Dependencies ?? [];
        var updated = manifest with
        {
            Name = name.Trim(),
            Version = version.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Authors = authors.Count == 0 ? null : [.. authors],
            Dependencies = [.. dependencies.Distinct(StringComparer.Ordinal).Select(id => old.FirstOrDefault(d => d.Id == id) ?? new ModDependency { Id = id })],
        };
        ManifestValidator.Validate(updated, new DiagnosticSink(display, problems));
        if (problems.Any(p => p.Severity == DiagnosticSeverity.Error))
            return [.. problems.Where(p => p.Severity == DiagnosticSeverity.Error)];
        DataJson.WriteFile(path, DataJson.Write(updated));
        return [];
    }
}
