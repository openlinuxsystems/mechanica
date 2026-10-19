using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mechanica.Core.Data;

/// <summary>A part definition that loaded cleanly, with where it came from.</summary>
/// <param name="QualifiedId">Game-wide id, <c>mod_id:part_id</c>.</param>
/// <param name="ModId">
/// Mod whose files define it, so its relative asset paths resolve against this mod's folder. It differs
/// from the <see cref="QualifiedId"/> prefix when another mod overrides the part.
/// </param>
/// <param name="FilePath">Absolute path of the source file.</param>
/// <param name="Definition">The parsed data.</param>
public sealed record LoadedPart(string QualifiedId, string ModId, string FilePath, PartDefinition Definition)
{
    /// <summary>True when this definition is another mod's override of the part.</summary>
    public bool IsOverride => !QualifiedId.StartsWith(ModId + ":", StringComparison.Ordinal);
}

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

    /// <summary>Its replacements for other mods' parts (<c>overrides/&lt;mod_id&gt;/</c>), ordered by qualified id.</summary>
    public IReadOnlyList<LoadedPart> Overrides { get; init; } = [];
}

/// <summary>A mod folder whose <c>mod.json</c> is valid, found by <see cref="ModLoader.Scan"/>.</summary>
/// <param name="Manifest">Its validated <c>mod.json</c>.</param>
/// <param name="Directory">Absolute path of the mod folder.</param>
public sealed record InstalledMod(ModManifest Manifest, string Directory)
{
    /// <summary>The mod id.</summary>
    public string Id => Manifest.Id!;

    /// <summary>The mod version.</summary>
    public SemVersion Version => Manifest.ParsedVersion!;

    /// <summary>Ids of the mods it depends on.</summary>
    public IEnumerable<string> DependencyIds => (Manifest.Dependencies ?? []).Select(d => d.Id!);
}

/// <summary>What <see cref="ModLoader.Scan"/> found: installed mods by id, and manifest problems.</summary>
/// <param name="Mods">Mods with a valid manifest, ordered by id.</param>
/// <param name="Broken">Ids of mods whose manifest has errors.</param>
/// <param name="Diagnostics">Every manifest problem found.</param>
public sealed record ModScanResult(IReadOnlyList<InstalledMod> Mods, IReadOnlySet<string> Broken, IReadOnlyList<DataDiagnostic> Diagnostics);

/// <summary>Kinds of <see cref="ModConflict"/>.</summary>
public enum ModConflictKind
{
    /// <summary>A mod lists an enabled mod under <c>conflicts</c>, so it was not loaded.</summary>
    Incompatible,

    /// <summary>Two unrelated mods override the same part; the later one in load order wins.</summary>
    OverrideClash,
}

/// <summary>A conflict between two enabled mods, shown in the Mod Manager.</summary>
/// <param name="Kind">What kind of conflict.</param>
/// <param name="ModId">The mod that lost: not loaded (incompatible) or overridden (override clash).</param>
/// <param name="OtherModId">The mod it conflicts with.</param>
/// <param name="PartId">Qualified id of the contested part, for override clashes.</param>
/// <param name="Message">Readable description, the same as its diagnostic.</param>
public sealed record ModConflict(ModConflictKind Kind, string ModId, string OtherModId, string? PartId, string Message);

/// <summary>
/// Everything <see cref="ModLoader"/> produced: the mods that loaded (in load order), their
/// parts by qualified id, conflicts, and every warning and error. A mod with any error is left out,
/// along with every mod that depends on it, so the rest of the game can still start.
/// </summary>
public sealed class ModLoadResult : IPartCatalog
{
    internal ModLoadResult(IReadOnlyList<LoadedMod> mods, IReadOnlyDictionary<string, LoadedPart> parts,
        IReadOnlyList<ModConflict> conflicts, IReadOnlyList<DataDiagnostic> diagnostics)
    {
        Mods = mods;
        Parts = parts;
        Conflicts = conflicts;
        Diagnostics = diagnostics;
        Fingerprint = ComputeFingerprint(mods);
    }

    /// <summary>Loaded mods in load order (dependencies first, then the profile's order, then by id).</summary>
    public IReadOnlyList<LoadedMod> Mods { get; }

    /// <summary>
    /// All parts from loaded mods, keyed by <c>mod_id:part_id</c>. An overridden part maps to the
    /// override of the last mod in load order that overrides it.
    /// </summary>
    public IReadOnlyDictionary<string, LoadedPart> Parts { get; }

    /// <summary>Conflicts between enabled mods. Each one also has a diagnostic.</summary>
    public IReadOnlyList<ModConflict> Conflicts { get; }

    /// <summary>Every problem found, in discovery order.</summary>
    public IReadOnlyList<DataDiagnostic> Diagnostics { get; }

    /// <summary>True when any diagnostic is an error.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// SHA-256 (hex) of the canonical form of every loaded manifest, part and override, in load order.
    /// Host and clients compare it on join to confirm they run the same content (D10).
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>Looks up a part; a bare id means vanilla. An upgraded part's item (<c>gas_engine@2</c>) is its part at that level.</summary>
    public LoadedPart? FindPart(string id)
    {
        var (part, level) = PartUpgrades.SplitItem(id);
        if (Parts.GetValueOrDefault(Identifiers.Qualify(part)) is not { } found || level < 0 || level > PartUpgrades.MaxLevel(found.Definition))
            return null;
        return level == 0 ? found : found with { Definition = PartUpgrades.At(found.Definition, level) };
    }

    /// <inheritdoc />
    public PartDefinition? FindDefinition(string id) => FindPart(id)?.Definition;

    private static string ComputeFingerprint(IReadOnlyList<LoadedMod> mods)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var mod in mods)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(DataJson.Write(mod.Manifest)));
            foreach (var part in mod.Parts)
                hash.AppendData(Encoding.UTF8.GetBytes(DataJson.Write(part.Definition)));
            foreach (var part in mod.Overrides)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(part.QualifiedId));
                hash.AppendData(Encoding.UTF8.GetBytes(DataJson.Write(part.Definition)));
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

/// <summary>
/// Discovers mods in a <c>mods/</c> folder, applies the player's <see cref="ModProfile"/> (enabled
/// mods and load order), resolves dependencies, versions and conflicts, then loads and validates each
/// mod's <c>parts/**/*.json</c> and <c>overrides/&lt;mod_id&gt;/**/*.json</c>. Pure file system code,
/// no Godot (D6, D8).
/// </summary>
public static class ModLoader
{
    /// <summary>File name of a mod manifest.</summary>
    public const string ManifestFileName = "mod.json";

    /// <summary>Folder inside a mod that holds part definitions.</summary>
    public const string PartsFolder = "parts";

    /// <summary>Folder inside a mod that holds replacements for other mods' parts, one subfolder per mod id.</summary>
    public const string OverridesFolder = "overrides";

    /// <summary>Folder inside <c>overrides/&lt;mod_id&gt;/</c> for replacements of that mod's recipes.</summary>
    public const string RecipeOverridesFolder = "recipes";

    /// <summary>Loads every mod folder directly inside <paramref name="modsRoot"/>.</summary>
    public static ModLoadResult LoadAll(string modsRoot) => Load(modsRoot, null);

    /// <summary>
    /// Loads the mods inside <paramref name="modsRoot"/> that <paramref name="profile"/> enables, in its
    /// load order. A null profile enables every mod. Vanilla is always enabled.
    /// </summary>
    public static ModLoadResult Load(string modsRoot, ModProfile? profile)
    {
        var scan = Scan(modsRoot);
        return Load(scan, profile, [.. scan.Diagnostics]);
    }

    /// <summary>
    /// Loads with the profile saved at <paramref name="profilePath"/> (no file: every mod enabled). A
    /// broken profile is reported first in the diagnostics and ignored, so the game still starts.
    /// </summary>
    public static ModLoadResult LoadWithProfile(string modsRoot, string profilePath)
    {
        var diagnostics = new List<DataDiagnostic>();
        var profile = ModProfile.Load(profilePath, diagnostics);
        var scan = Scan(modsRoot);
        diagnostics.AddRange(scan.Diagnostics);
        return Load(scan, profile, diagnostics);
    }

    /// <summary>Loads the given mod folders, all enabled.</summary>
    public static ModLoadResult Load(IEnumerable<string> modDirectories)
    {
        var scan = Scan(modDirectories, []);
        return Load(scan, null, [.. scan.Diagnostics]);
    }

    /// <summary>
    /// Reads and validates the manifest of every mod folder directly inside <paramref name="modsRoot"/>
    /// without loading parts. This is what the Mod Manager lists.
    /// </summary>
    public static ModScanResult Scan(string modsRoot)
    {
        var diagnostics = new List<DataDiagnostic>();
        if (!Directory.Exists(modsRoot))
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.FileUnreadable,
                modsRoot, null, null, "mods folder does not exist."));
            return new ModScanResult([], new HashSet<string>(), diagnostics);
        }
        // Hidden folders (.git, and the package installer's staging and trash folders) are never mods.
        return Scan(Directory.GetDirectories(modsRoot).Where(d => !Path.GetFileName(d).StartsWith('.'))
            .Order(StringComparer.Ordinal), diagnostics);
    }

    /// <summary>
    /// Parses one data file's JSON into <typeparamref name="T"/>, reporting malformed JSON as a
    /// diagnostic with its line and JSON path instead of throwing.
    /// </summary>
    public static T? ParseJson<T>(string json, string displayFile, List<DataDiagnostic> diagnostics) where T : class =>
        ParseJson<T>(json, new DiagnosticSink(displayFile, diagnostics));

    /// <summary>
    /// Validates one part as if it were a file in the mod at <paramref name="modDirectory"/>, the way
    /// loading would, without loading the mod. The Part Editor checks a part with this before export.
    /// The mod id is the folder name; its <c>mod.json</c>, when present, supplies the dependencies that
    /// <c>other_mod:path</c> assets may name, found as sibling folders.
    /// </summary>
    /// <param name="part">The part to check.</param>
    /// <param name="modDirectory">Folder of the mod the part belongs to; it need not exist yet.</param>
    /// <param name="displayFile">File name shown in the diagnostics.</param>
    public static IReadOnlyList<DataDiagnostic> ValidatePart(PartDefinition part, string modDirectory, string displayFile)
    {
        var diagnostics = new List<DataDiagnostic>();
        var dir = Path.GetFullPath(modDirectory);
        var modId = Path.GetFileName(dir);
        var manifestPath = Path.Combine(dir, ManifestFileName);
        var deps = new HashSet<string>(StringComparer.Ordinal);
        if (File.Exists(manifestPath) && ReadFile<ModManifest>(manifestPath, new DiagnosticSink(displayFile, [])) is { } manifest)
            deps.UnionWith((manifest.Dependencies ?? []).Select(d => d.Id).OfType<string>());
        var root = Path.GetDirectoryName(dir) ?? dir;
        var validator = new PartValidator(modId, deps, id => id == modId ? dir : Path.Combine(root, id));
        validator.Validate(part, new DiagnosticSink(displayFile, diagnostics));
        return diagnostics;
    }

    private static ModScanResult Scan(IEnumerable<string> modDirectories, List<DataDiagnostic> diagnostics)
    {
        var candidates = new Dictionary<string, InstalledMod>(StringComparer.Ordinal);
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
            candidates.Add(manifest.Id!, new InstalledMod(manifest, dir));
        }
        return new ModScanResult([.. candidates.Values.OrderBy(m => m.Id, StringComparer.Ordinal)], broken, diagnostics);
    }

    private static ModLoadResult Load(ModScanResult scan, ModProfile? profile, List<DataDiagnostic> diagnostics)
    {
        var entries = (profile?.Mods ?? []).Where(e => e.Id is not null).ToDictionary(e => e.Id!, StringComparer.Ordinal);
        var enabled = new Dictionary<string, InstalledMod>(StringComparer.Ordinal);
        var disabled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mod in scan.Mods)
        {
            if (entries.TryGetValue(mod.Id, out var entry) && !entry.IsEnabled && mod.Id != Identifiers.VanillaModId)
                disabled.Add(mod.Id);
            else
                enabled.Add(mod.Id, mod);
        }
        var preference = PreferredOrder(scan.Mods.Select(m => m.Id), profile);

        var conflicts = new List<ModConflict>();
        var loaded = new Dictionary<string, LoadedMod>(StringComparer.Ordinal);
        var mods = new List<LoadedMod>();
        foreach (var id in ResolveOrder(enabled, disabled, scan.Broken, preference, conflicts, diagnostics))
        {
            var mod = enabled[id];
            var file = Display(mod.Directory, ManifestFileName);
            var failedDep = mod.DependencyIds.FirstOrDefault(d => !loaded.ContainsKey(d));
            if (failedDep is not null)
            {
                diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.DependencyFailed,
                    file, null, null, $"mod '{id}' was not loaded because its dependency '{failedDep}' failed to load."));
                continue;
            }
            var loadedMod = LoadParts(mod.Manifest, mod.Directory, loaded, diagnostics);
            if (loadedMod is null)
                continue;
            loaded.Add(id, loadedMod);
            mods.Add(loadedMod);
        }
        return new ModLoadResult(mods, ApplyOverrides(mods, conflicts, diagnostics), conflicts, diagnostics);
    }

    /// <summary>
    /// The profile's mods in its order, then every other id by ordinal. Ids may include mods that are
    /// not installed; callers filter.
    /// </summary>
    internal static List<string> PreferredOrder(IEnumerable<string> installed, ModProfile? profile)
    {
        var order = (profile?.Mods ?? []).Select(e => e.Id).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var listed = order.ToHashSet(StringComparer.Ordinal);
        order.AddRange(installed.Where(id => !listed.Contains(id)).Order(StringComparer.Ordinal));
        return order;
    }

    /// <summary>
    /// Stable dependency sort: walks <paramref name="preference"/> and repeatedly takes the first id
    /// whose <paramref name="mustPrecede"/> ids are already placed, so the preferred order is kept
    /// wherever dependencies allow. Ids caught in a cycle are returned in <c>Stuck</c>.
    /// </summary>
    internal static (List<string> Order, List<string> Stuck) SortByDependencies(
        IEnumerable<string> preference, Func<string, IEnumerable<string>> mustPrecede)
    {
        var order = new List<string>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var pending = preference.ToList();
        while (pending.Count > 0)
        {
            var next = pending.FindIndex(id => mustPrecede(id).All(placed.Contains));
            if (next < 0)
                break;
            placed.Add(pending[next]);
            order.Add(pending[next]);
            pending.RemoveAt(next);
        }
        return (order, pending);
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
    /// Orders enabled mods so dependencies come first, otherwise following <paramref name="preference"/>.
    /// Mods with missing, disabled, wrong-version or cyclic dependencies, or with an enabled mod listed
    /// under their <c>conflicts</c>, are reported and left out.
    /// </summary>
    private static List<string> ResolveOrder(
        Dictionary<string, InstalledMod> enabled, HashSet<string> disabled, IReadOnlySet<string> broken,
        List<string> preference, List<ModConflict> conflicts, List<DataDiagnostic> diagnostics)
    {
        var usable = new HashSet<string>(enabled.Keys, StringComparer.Ordinal);
        foreach (var (id, mod) in enabled.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            var file = Display(mod.Directory, ManifestFileName);
            var deps = mod.Manifest.Dependencies ?? [];
            for (var i = 0; i < deps.Count; i++)
            {
                var dep = deps[i];
                var error = !enabled.TryGetValue(dep.Id!, out var target)
                    ? disabled.Contains(dep.Id!)
                        ? (DiagnosticCodes.DependencyDisabled, $"mod '{id}' needs mod '{dep.Id}', which is disabled; enable it in the Mod Manager.")
                        : broken.Contains(dep.Id!)
                            ? (DiagnosticCodes.DependencyFailed, $"mod '{id}' needs mod '{dep.Id}', whose mod.json has errors.")
                            : (DiagnosticCodes.MissingDependency, $"mod '{id}' needs mod '{dep.Id}', which is not installed.")
                    : !dep.ParsedVersion!.IsSatisfiedBy(target.Version)
                        ? (DiagnosticCodes.DependencyVersion,
                            $"mod '{id}' needs '{dep.Id}' version {dep.ParsedVersion.Text}, but {target.Version} is installed.")
                        : default((string, string)?);
                if (error is { } e)
                {
                    diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, e.Item1, file, $"$.dependencies[{i}]", null, e.Item2));
                    usable.Remove(id);
                }
            }

            var refused = mod.Manifest.Conflicts ?? [];
            for (var i = 0; i < refused.Count; i++)
            {
                if (!enabled.TryGetValue(refused[i].Id!, out var other) || !refused[i].ParsedVersion!.IsSatisfiedBy(other.Version))
                    continue;
                var message = $"mod '{id}' was not loaded because it conflicts with '{other.Id}' {other.Version}, "
                    + "which is enabled; disable one of them in the Mod Manager.";
                diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.ModIncompatible, file,
                    $"$.conflicts[{i}]", null, message));
                conflicts.Add(new ModConflict(ModConflictKind.Incompatible, id, other.Id, null, message));
                usable.Remove(id);
            }
        }

        // A mod whose dependency is unusable is placed anyway; the caller reports it as a failed dependency.
        var (order, stuck) = SortByDependencies(preference.Where(usable.Contains),
            id => enabled[id].DependencyIds.Where(usable.Contains));
        foreach (var id in stuck)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.DependencyCycle,
                Display(enabled[id].Directory, ManifestFileName), "$.dependencies", null,
                $"mod '{id}' is part of a dependency cycle ({string.Join(", ", stuck.Order(StringComparer.Ordinal))}) and was not loaded."));
        }
        return order;
    }

    /// <summary>
    /// Builds the part table: every mod's own parts, then its overrides in load order, so a later mod's
    /// override wins. Two overrides of one part are a conflict unless one mod depends on the other.
    /// </summary>
    private static Dictionary<string, LoadedPart> ApplyOverrides(List<LoadedMod> mods, List<ModConflict> conflicts,
        List<DataDiagnostic> diagnostics)
    {
        var byId = mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var parts = new Dictionary<string, LoadedPart>(StringComparer.Ordinal);
        foreach (var mod in mods)
        {
            foreach (var part in mod.Parts)
                parts.Add(part.QualifiedId, part);
            foreach (var part in mod.Overrides)
            {
                var previous = parts[part.QualifiedId];
                if (previous.IsOverride && !DependsOn(mod.Id, previous.ModId, byId))
                {
                    var message = $"mods '{previous.ModId}' and '{mod.Id}' both override '{part.QualifiedId}'; "
                        + $"'{mod.Id}' loads later and wins. Change the load order in the Mod Manager to pick the other.";
                    diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Warning, DiagnosticCodes.OverrideConflict,
                        Display(mod.Directory, Path.GetRelativePath(mod.Directory, part.FilePath)), "$.id", null, message));
                    conflicts.Add(new ModConflict(ModConflictKind.OverrideClash, previous.ModId, mod.Id, part.QualifiedId, message));
                }
                parts[part.QualifiedId] = part;
            }
        }
        return parts;
    }

    private static bool DependsOn(string modId, string otherId, Dictionary<string, LoadedMod> mods)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>([modId]);
        while (stack.TryPop(out var id))
        {
            foreach (var dep in mods.GetValueOrDefault(id)?.Manifest.Dependencies ?? [])
            {
                if (dep.Id == otherId)
                    return true;
                if (seen.Add(dep.Id!))
                    stack.Push(dep.Id!);
            }
        }
        return false;
    }

    private static LoadedMod? LoadParts(ModManifest manifest, string dir, Dictionary<string, LoadedMod> loaded,
        List<DataDiagnostic> diagnostics)
    {
        var modId = manifest.Id!;
        var deps = (manifest.Dependencies ?? []).Select(d => d.Id!).ToHashSet(StringComparer.Ordinal);
        var validator = new PartValidator(modId, deps,
            id => id == modId ? dir : loaded.GetValueOrDefault(id)?.Directory);
        var errors = 0;

        var parts = new Dictionary<string, LoadedPart>(StringComparer.Ordinal);
        foreach (var file in JsonFiles(dir, Path.Combine(dir, PartsFolder)))
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

        var overrides = new Dictionary<string, LoadedPart>(StringComparer.Ordinal);
        var overridesDir = Path.Combine(dir, OverridesFolder);
        IEnumerable<string> targets = Directory.Exists(overridesDir) ? Directory.GetDirectories(overridesDir).Order(StringComparer.Ordinal) : [];
        foreach (var targetDir in targets)
        {
            var targetId = Path.GetFileName(targetDir);
            if (!deps.Contains(targetId) || !loaded.TryGetValue(targetId, out var target))
            {
                var sink = new DiagnosticSink(Display(dir, Path.GetRelativePath(dir, targetDir)), diagnostics);
                sink.Error(DiagnosticCodes.OverrideUndeclaredMod, null,
                    $"overrides for mod '{targetId}' need '{targetId}' in this mod's dependencies, so it loads first.");
                errors += sink.ErrorCount;
                continue;
            }
            // overrides/<mod>/recipes/ holds recipe overrides, which the RecipeBook reads.
            var recipesDir = Path.Combine(targetDir, RecipeOverridesFolder) + Path.DirectorySeparatorChar;
            foreach (var file in JsonFiles(dir, targetDir).Where(f => !f.StartsWith(recipesDir, StringComparison.Ordinal)))
            {
                var sink = new DiagnosticSink(Display(dir, Path.GetRelativePath(dir, file)), diagnostics);
                var part = ReadFile<PartDefinition>(file, sink);
                if (part is not null)
                {
                    validator.Validate(part, sink);
                    var qualified = $"{targetId}:{part.Id}";
                    if (part.Id is not null && !target.Parts.Any(p => p.Definition.Id == part.Id))
                        sink.Error(DiagnosticCodes.OverrideUnknownPart, "$.id",
                            $"mod '{targetId}' has no part '{part.Id}' to override.");
                    else if (part.Id is not null && overrides.TryGetValue(qualified, out var other))
                        sink.Error(DiagnosticCodes.DuplicatePart, "$.id",
                            $"'{qualified}' is already overridden in {Display(dir, Path.GetRelativePath(dir, other.FilePath))}.");
                    if (sink.ErrorCount == 0)
                        overrides.Add(qualified, new LoadedPart(qualified, modId, file, part));
                }
                errors += sink.ErrorCount;
            }
        }

        if (errors > 0)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.ModNotLoaded,
                Display(dir, ManifestFileName), null, null,
                $"mod '{modId}' was not loaded because {errors} error(s) were found in its files."));
            return null;
        }
        return new LoadedMod(manifest, dir, [.. parts.Values.OrderBy(p => p.Definition.Id, StringComparer.Ordinal)])
        {
            Overrides = [.. overrides.Values.OrderBy(p => p.QualifiedId, StringComparer.Ordinal)],
        };
    }

    // Every *.json under folder, sorted by mod-relative path so all machines read files in the same order.
    private static IEnumerable<string> JsonFiles(string modDir, string folder) => Directory.Exists(folder)
        ? Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories)
            .OrderBy(f => Path.GetRelativePath(modDir, f).Replace('\\', '/'), StringComparer.Ordinal)
        : [];

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

    internal static T? ParseJson<T>(string json, DiagnosticSink sink) where T : class
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
