using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mechanica.Core.Data;

/// <summary>A part definition that loaded cleanly, with where it came from.</summary>
/// <param name="QualifiedId">Game-wide id, <c>mod_id:part_id</c>.</param>
/// <param name="ModId">Owning mod.</param>
/// <param name="FilePath">Absolute path of the source file.</param>
/// <param name="Definition">The parsed data.</param>
public sealed record LoadedPart(string QualifiedId, string ModId, string FilePath, PartDefinition Definition);

/// <summary>A mod that loaded cleanly.</summary>
/// <param name="Manifest">Its validated <c>mod.json</c>.</param>
/// <param name="Directory">Absolute path of the mod folder.</param>
/// <param name="Parts">Its parts, ordered by id.</param>
public sealed record LoadedMod(ModManifest Manifest, string Directory, IReadOnlyList<LoadedPart> Parts)
{
    /// <summary>The mod id.</summary>
    public string Id => Manifest.Id!;

    /// <summary>The mod version.</summary>
    public SemVersion Version => Manifest.ParsedVersion!;
}

/// <summary>
/// Everything <see cref="ModLoader"/> produced: the mods that loaded (in dependency order), their
/// parts by qualified id, and every warning and error. A mod with any error is left out, along
/// with every mod that depends on it, so the rest of the game can still start.
/// </summary>
public sealed class ModLoadResult
{
    internal ModLoadResult(IReadOnlyList<LoadedMod> mods, IReadOnlyList<DataDiagnostic> diagnostics)
    {
        Mods = mods;
        Diagnostics = diagnostics;
        Parts = mods.SelectMany(m => m.Parts).ToDictionary(p => p.QualifiedId, StringComparer.Ordinal);
        Fingerprint = ComputeFingerprint(mods);
    }

    /// <summary>Loaded mods in load order (dependencies first, ties broken by id).</summary>
    public IReadOnlyList<LoadedMod> Mods { get; }

    /// <summary>All parts from loaded mods, keyed by <c>mod_id:part_id</c>.</summary>
    public IReadOnlyDictionary<string, LoadedPart> Parts { get; }

    /// <summary>Every problem found, in discovery order.</summary>
    public IReadOnlyList<DataDiagnostic> Diagnostics { get; }

    /// <summary>True when any diagnostic is an error.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// SHA-256 (hex) of the canonical form of every loaded manifest and part, in load order. Host and
    /// clients compare it on join to confirm they run the same content (D10).
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>Looks up a part; a bare id means vanilla.</summary>
    public LoadedPart? FindPart(string id) => Parts.GetValueOrDefault(Identifiers.Qualify(id));

    private static string ComputeFingerprint(IReadOnlyList<LoadedMod> mods)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var mod in mods)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(DataJson.Write(mod.Manifest)));
            foreach (var part in mod.Parts)
                hash.AppendData(Encoding.UTF8.GetBytes(DataJson.Write(part.Definition)));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

/// <summary>
/// Discovers mods in a <c>mods/</c> folder, resolves their dependencies and versions, then loads
/// and validates each mod's <c>parts/**/*.json</c>. Pure file system code, no Godot (D6, D8).
/// </summary>
public static class ModLoader
{
    /// <summary>File name of a mod manifest.</summary>
    public const string ManifestFileName = "mod.json";

    /// <summary>Folder inside a mod that holds part definitions.</summary>
    public const string PartsFolder = "parts";

    /// <summary>Loads every mod folder directly inside <paramref name="modsRoot"/>.</summary>
    public static ModLoadResult LoadAll(string modsRoot)
    {
        var diagnostics = new List<DataDiagnostic>();
        if (!Directory.Exists(modsRoot))
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.FileUnreadable,
                modsRoot, null, null, "mods folder does not exist."));
            return new ModLoadResult([], diagnostics);
        }
        var folders = Directory.GetDirectories(modsRoot).Order(StringComparer.Ordinal);
        return Load(folders, diagnostics);
    }

    /// <summary>Loads the given mod folders.</summary>
    public static ModLoadResult Load(IEnumerable<string> modDirectories) => Load(modDirectories, []);

    /// <summary>
    /// Parses one data file's JSON into <typeparamref name="T"/>, reporting malformed JSON as a
    /// diagnostic with its line and JSON path instead of throwing.
    /// </summary>
    public static T? ParseJson<T>(string json, string displayFile, List<DataDiagnostic> diagnostics) where T : class =>
        ParseJson<T>(json, new DiagnosticSink(displayFile, diagnostics));

    private static ModLoadResult Load(IEnumerable<string> modDirectories, List<DataDiagnostic> diagnostics)
    {
        var candidates = new Dictionary<string, (ModManifest Manifest, string Directory)>(StringComparer.Ordinal);
        var broken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dir in modDirectories.Select(Path.GetFullPath))
        {
            var manifest = ReadManifest(dir, diagnostics, broken);
            if (manifest is null)
                continue;
            if (candidates.TryGetValue(manifest.Id!, out var existing))
            {
                diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.DuplicateMod,
                    Display(dir, ManifestFileName), "$.id", null,
                    $"mod id '{manifest.Id}' is already used by {Display(existing.Directory, ManifestFileName)}; this copy is skipped."));
                continue;
            }
            candidates.Add(manifest.Id!, (manifest, dir));
        }

        var loaded = new Dictionary<string, LoadedMod>(StringComparer.Ordinal);
        foreach (var id in ResolveOrder(candidates, broken, diagnostics))
        {
            var (manifest, dir) = candidates[id];
            var file = Display(dir, ManifestFileName);
            var failedDep = manifest.Dependencies?.Select(d => d.Id!).FirstOrDefault(d => !loaded.ContainsKey(d));
            if (failedDep is not null)
            {
                diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.DependencyFailed,
                    file, null, null, $"mod '{id}' was not loaded because its dependency '{failedDep}' failed to load."));
                continue;
            }
            var mod = LoadParts(manifest, dir, loaded, diagnostics);
            if (mod is not null)
                loaded.Add(id, mod);
        }
        return new ModLoadResult([.. loaded.Values], diagnostics);
    }

    private static ModManifest? ReadManifest(string dir, List<DataDiagnostic> diagnostics, HashSet<string> broken)
    {
        var path = Path.Combine(dir, ManifestFileName);
        var sink = new DiagnosticSink(Display(dir, ManifestFileName), diagnostics);
        if (!File.Exists(path))
        {
            sink.Warning(DiagnosticCodes.ManifestMissing, null, "folder has no mod.json and is not a mod; skipped.");
            return null;
        }
        var manifest = ReadFile<ModManifest>(path, sink);
        if (manifest is null)
            return null;
        ManifestValidator.Validate(manifest, sink);
        var folder = Path.GetFileName(dir);
        if (manifest.Id is not null && manifest.Id != folder)
            sink.Error(DiagnosticCodes.ModFolderMismatch, "$.id", $"mod id '{manifest.Id}' must match its folder name '{folder}'.");
        if (sink.ErrorCount == 0)
            return manifest;
        if (Identifiers.IsValid(manifest.Id))
            broken.Add(manifest.Id);
        return null;
    }

    /// <summary>
    /// Orders mods so dependencies come first; ties go by id so every machine gets the same order.
    /// Mods with missing, wrong-version or cyclic dependencies are reported and left out.
    /// </summary>
    private static List<string> ResolveOrder(
        Dictionary<string, (ModManifest Manifest, string Directory)> candidates, HashSet<string> broken,
        List<DataDiagnostic> diagnostics)
    {
        var usable = new SortedSet<string>(candidates.Keys, StringComparer.Ordinal);
        foreach (var (id, (manifest, dir)) in candidates.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            var deps = manifest.Dependencies ?? [];
            for (var i = 0; i < deps.Count; i++)
            {
                var dep = deps[i];
                var error = !candidates.TryGetValue(dep.Id!, out var target)
                    ? broken.Contains(dep.Id!)
                        ? (DiagnosticCodes.DependencyFailed, $"mod '{id}' needs mod '{dep.Id}', whose mod.json has errors.")
                        : (DiagnosticCodes.MissingDependency, $"mod '{id}' needs mod '{dep.Id}', which is not installed.")
                    : !dep.ParsedVersion!.IsSatisfiedBy(target.Manifest.ParsedVersion!)
                        ? (DiagnosticCodes.DependencyVersion,
                            $"mod '{id}' needs '{dep.Id}' version {dep.ParsedVersion.Text}, but {target.Manifest.ParsedVersion} is installed.")
                        : default((string, string)?);
                if (error is { } e)
                {
                    diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, e.Item1,
                        Display(dir, ManifestFileName), $"$.dependencies[{i}]", null, e.Item2));
                    usable.Remove(id);
                }
            }
        }

        // Kahn's algorithm over usable mods; a mod whose dependency is unusable is handled by the caller.
        var order = new List<string>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var pending = new SortedSet<string>(usable, StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var next = pending.FirstOrDefault(id => (candidates[id].Manifest.Dependencies ?? [])
                .All(d => placed.Contains(d.Id!) || !usable.Contains(d.Id!)));
            if (next is null)
                break;
            pending.Remove(next);
            placed.Add(next);
            order.Add(next);
        }
        foreach (var id in pending)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.DependencyCycle,
                Display(candidates[id].Directory, ManifestFileName), "$.dependencies", null,
                $"mod '{id}' is part of a dependency cycle ({string.Join(", ", pending)}) and was not loaded."));
        }
        return order;
    }

    private static LoadedMod? LoadParts(ModManifest manifest, string dir, Dictionary<string, LoadedMod> loaded,
        List<DataDiagnostic> diagnostics)
    {
        var modId = manifest.Id!;
        var deps = (manifest.Dependencies ?? []).Select(d => d.Id!).ToHashSet(StringComparer.Ordinal);
        var validator = new PartValidator(modId, deps,
            id => id == modId ? dir : loaded.GetValueOrDefault(id)?.Directory);
        var partsDir = Path.Combine(dir, PartsFolder);
        var files = Directory.Exists(partsDir)
            ? Directory.GetFiles(partsDir, "*.json", SearchOption.AllDirectories)
                .OrderBy(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), StringComparer.Ordinal)
            : Enumerable.Empty<string>();

        var parts = new Dictionary<string, LoadedPart>(StringComparer.Ordinal);
        var errors = 0;
        foreach (var file in files)
        {
            var sink = new DiagnosticSink(Display(dir, Path.GetRelativePath(dir, file)), diagnostics);
            var part = ReadFile<PartDefinition>(file, sink);
            if (part is not null)
            {
                validator.Validate(part, sink);
                if (part.Id is not null && parts.TryGetValue(part.Id, out var other))
                    sink.Error(DiagnosticCodes.DuplicatePart, "$.id",
                        $"part id '{part.Id}' is already defined in {Display(dir, Path.GetRelativePath(dir, other.FilePath))}.");
                if (sink.ErrorCount == 0)
                    parts.Add(part.Id!, new LoadedPart($"{modId}:{part.Id}", modId, file, part));
            }
            errors += sink.ErrorCount;
        }
        if (errors > 0)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.ModNotLoaded,
                Display(dir, ManifestFileName), null, null,
                $"mod '{modId}' was not loaded because {errors} error(s) were found in its files."));
            return null;
        }
        return new LoadedMod(manifest, dir, [.. parts.Values.OrderBy(p => p.Definition.Id, StringComparer.Ordinal)]);
    }

    private static T? ReadFile<T>(string path, DiagnosticSink sink) where T : class
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            sink.Error(DiagnosticCodes.FileUnreadable, null, $"could not read file: {e.Message}");
            return null;
        }
        return ParseJson<T>(json, sink);
    }

    private static T? ParseJson<T>(string json, DiagnosticSink sink) where T : class
    {
        try
        {
            var value = DataJson.Read<T>(json);
            if (value is null)
                sink.Error(DiagnosticCodes.InvalidJson, "$", "expected a JSON object, got null.");
            return value;
        }
        catch (JsonException e)
        {
            var line = e.LineNumber is { } l ? (int)l + 1 : (int?)null;
            sink.Error(DiagnosticCodes.InvalidJson, e.Path, CleanMessage(e), line);
            return null;
        }
    }

    // System.Text.Json appends "Path: ... | LineNumber: ..." to its messages; the diagnostic carries those itself.
    private static string CleanMessage(JsonException e)
    {
        var message = e.Message;
        var cut = message.IndexOf(" Path: ", StringComparison.Ordinal);
        return cut >= 0 ? message[..cut] : message;
    }

    /// <summary>Path shown in diagnostics: <c>&lt;mods folder&gt;/&lt;mod folder&gt;/&lt;file&gt;</c>.</summary>
    private static string Display(string modDir, string relative)
    {
        var root = Path.GetDirectoryName(modDir) ?? modDir;
        return Path.Combine(Path.GetFileName(root), Path.GetFileName(modDir), relative).Replace('\\', '/');
    }
}
