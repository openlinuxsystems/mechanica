using Mechanica.Core.Data;
using Mechanica.Core.Editor;

namespace Mechanica.Core.ModTools;

/// <summary>A compile error a <see cref="ScriptChecker"/> found.</summary>
/// <param name="Line">1-based line.</param>
/// <param name="Column">0-based column.</param>
/// <param name="Message">What is wrong.</param>
public sealed record ScriptProblem(int Line, int Column, string Message);

/// <summary>Compiles a Lua script the way the host does without running it; null when it compiles.</summary>
public delegate ScriptProblem? ScriptChecker(string source);

/// <summary>One installed mod as the Mod Manager lists it.</summary>
/// <param name="Id">Mod id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Version">Version.</param>
/// <param name="Enabled">Whether the profile enables it.</param>
/// <param name="CanDisable">False for vanilla.</param>
/// <param name="Loaded">Whether it loaded with the current profile (an enabled mod with errors does not).</param>
/// <param name="Dependencies">Ids of the mods it needs.</param>
/// <param name="Description">Its description, when it has one.</param>
public sealed record ModSummary(string Id, string Name, string Version, bool Enabled, bool CanDisable, bool Loaded,
    IReadOnlyList<string> Dependencies, string? Description);

/// <summary>The Mod Manager's view: mods in load order, plus what is wrong.</summary>
/// <param name="Mods">Installed mods in load order.</param>
/// <param name="Missing">Mods the profile lists that are not installed.</param>
/// <param name="Broken">Mod folders whose <c>mod.json</c> has errors, so they are not listed.</param>
public sealed record ModList(IReadOnlyList<ModSummary> Mods, IReadOnlyList<string> Missing, IReadOnlyList<string> Broken);

/// <summary>A diagnostic in the shape the endpoint sends.</summary>
/// <param name="Domain">Content area (<see cref="ContentDiagnostic.Domain"/>).</param>
/// <param name="Severity"><c>error</c> or <c>warning</c>.</param>
/// <param name="Code">Stable code, like <c>MD0201</c>.</param>
/// <param name="Mod">Mod the file is in, when the file is in one.</param>
/// <param name="File">File, relative to the mods folder's parent (<c>mods/x/parts/a.json</c>).</param>
/// <param name="JsonPath">JSON path inside the file, or null.</param>
/// <param name="Line">1-based line, or null.</param>
/// <param name="Message">What is wrong.</param>
public sealed record DiagnosticInfo(string Domain, string Severity, string Code, string? Mod, string File, string? JsonPath, int? Line, string Message)
{
    /// <summary>The endpoint shape of a diagnostic about a file of <paramref name="modId"/> (named relative to the mod).</summary>
    public static DiagnosticInfo ForMod(string domain, string modId, DataDiagnostic d) =>
        new(domain, d.Severity == DiagnosticSeverity.Error ? "error" : "warning", d.Code, modId, d.File, d.JsonPath, d.Line, d.Message);

    /// <summary>
    /// The endpoint shape of a diagnostic. Loaders name files either from the mods folder
    /// (<c>mods/x/parts/a.json</c>) or from the mod (<c>x/recipes/a.json</c>); <paramref name="modIds"/> tells
    /// which mod a file is in either way.
    /// </summary>
    public static DiagnosticInfo From(string domain, DataDiagnostic d, IReadOnlySet<string> modIds, string modsFolderName)
    {
        var segments = d.File.Split('/');
        var mod = segments.Length > 2 && segments[0] == modsFolderName && modIds.Contains(segments[1]) ? segments[1]
            : segments.Length > 1 && modIds.Contains(segments[0]) ? segments[0]
            : null;
        return new DiagnosticInfo(domain, d.Severity == DiagnosticSeverity.Error ? "error" : "warning", d.Code, mod, d.File, d.JsonPath, d.Line, d.Message);
    }
}

/// <summary>What <see cref="ModWorkspace.Validate"/> found.</summary>
/// <param name="Ok">True when there are no errors.</param>
/// <param name="Errors">Number of errors.</param>
/// <param name="Warnings">Number of warnings.</param>
/// <param name="LoadedMods">Mods that loaded, in load order.</param>
/// <param name="Fingerprint">The content fingerprint peers compare on join.</param>
/// <param name="Diagnostics">Every problem, mods first.</param>
public sealed record ValidationReport(bool Ok, int Errors, int Warnings, IReadOnlyList<string> LoadedMods, string Fingerprint,
    IReadOnlyList<DiagnosticInfo> Diagnostics);

/// <summary>A conflict between enabled mods.</summary>
/// <param name="Kind"><c>incompatible</c> (not loaded) or <c>override_clash</c> (the later mod wins).</param>
/// <param name="Mod">The mod that lost.</param>
/// <param name="Other">The mod it conflicts with.</param>
/// <param name="Part">The contested part, for override clashes.</param>
/// <param name="Message">Readable description.</param>
public sealed record ConflictInfo(string Kind, string Mod, string Other, string? Part, string Message);

/// <summary>
/// The mod workspace an AI (or any tool) drives: the Mod Manager's list, enable and load order, new mods,
/// sandboxed file access (<see cref="Files"/>), asset import and validation of all content with every loader
/// the game uses. Profile changes are saved at once and take effect on the next mod reload. Pure file system
/// code over the same Core classes as the in-game tools; no Godot.
/// </summary>
public sealed class ModWorkspace
{
    private readonly ScriptChecker? _checkScript;

    /// <summary>Creates a workspace.</summary>
    /// <param name="modsRoot">The <c>mods/</c> folder.</param>
    /// <param name="profilePath">The player's <see cref="ModProfile"/> file.</param>
    /// <param name="checkScript">Compiles Lua for <see cref="Validate"/>; null skips script checks.</param>
    public ModWorkspace(string modsRoot, string profilePath, ScriptChecker? checkScript = null)
    {
        Files = new ModFiles(modsRoot);
        ProfilePath = profilePath;
        _checkScript = checkScript;
    }

    /// <summary>The <c>mods/</c> folder.</summary>
    public string ModsRoot => Files.ModsRoot;

    /// <summary>Where the profile is saved.</summary>
    public string ProfilePath { get; }

    /// <summary>Sandboxed reads and writes inside mod folders.</summary>
    public ModFiles Files { get; }

    /// <summary>The saved profile, or null when there is none (or it is broken): every mod enabled.</summary>
    public ModProfile? Profile => ModProfile.Load(ProfilePath, []);

    /// <summary>Opens the Mod Manager logic over the saved profile.</summary>
    public ModPackageManager Manager() => ModPackageManager.Open(ModsRoot, Profile);

    /// <summary>Installed mods in load order with their state.</summary>
    public ModList List()
    {
        var manager = Manager();
        var loaded = manager.Load().Mods.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        return new ModList(
            [.. manager.Mods.Select(m => new ModSummary(m.Id, m.Manifest.Name ?? m.Id, m.Manifest.Version ?? "", manager.IsEnabled(m.Id),
                ModPackageManager.CanDisable(m.Id), loaded.Contains(m.Id), [.. m.DependencyIds], m.Manifest.Description))],
            manager.MissingMods,
            [.. manager.Scan.Broken.Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Enables or disables a mod (with its dependencies or dependents, as the Mod Manager does) and saves the
    /// profile. Returns every mod whose state changed.
    /// </summary>
    public IReadOnlyList<string> SetEnabled(string modId, bool enabled)
    {
        var manager = Manager();
        if (!manager.Mods.Any(m => m.Id == modId))
            throw new ModToolException($"mod '{modId}' is not installed.");
        if (!enabled && !ModPackageManager.CanDisable(modId))
            throw new ModToolException("vanilla can't be disabled; the game needs it.");
        var changed = manager.SetEnabled(modId, enabled);
        manager.ToProfile().Save(ProfilePath);
        return changed;
    }

    /// <summary>
    /// Moves a mod in the load order (stopping at its dependencies and dependents) and saves the profile.
    /// Returns the index it ended up at.
    /// </summary>
    public int Move(string modId, int index)
    {
        var manager = Manager();
        if (!manager.Mods.Any(m => m.Id == modId))
            throw new ModToolException($"mod '{modId}' is not installed.");
        var at = manager.Move(modId, index);
        manager.ToProfile().Save(ProfilePath);
        return at;
    }

    /// <summary>Conflicts between the enabled mods, as the Mod Manager previews them.</summary>
    public IReadOnlyList<ConflictInfo> Conflicts() =>
        [.. Manager().Load().Conflicts.Select(c => new ConflictInfo(
            c.Kind == ModConflictKind.Incompatible ? "incompatible" : "override_clash", c.ModId, c.OtherModId, c.PartId, c.Message))];

    /// <summary>
    /// Creates a new mod folder with a <c>mod.json</c> and empty <c>parts/</c> and <c>scripts/</c> folders. It
    /// depends on vanilla unless <paramref name="dependencies"/> says otherwise, and is enabled by default.
    /// </summary>
    /// <returns>The written manifest's path inside the mods folder.</returns>
    public string Create(string modId, string? name, string? version, string? description, IReadOnlyList<string>? authors,
        IReadOnlyList<string>? dependencies)
    {
        if (!Identifiers.IsValid(modId))
            throw new ModToolException($"'{modId}' is not a valid mod id (lowercase letters, digits and _, starting with a letter).");
        var dir = Path.Combine(ModsRoot, modId);
        if (Directory.Exists(dir) || File.Exists(dir))
            throw new ModToolException($"mods/{modId} already exists.");
        var deps = dependencies ?? [Identifiers.VanillaModId];
        if (deps.FirstOrDefault(d => !Identifiers.IsValid(d) || d == modId) is { } bad)
            throw new ModToolException($"'{bad}' can't be a dependency.");
        var manifest = new ModManifest
        {
            FormatVersion = ModManifest.CurrentFormatVersion,
            Id = modId,
            Name = string.IsNullOrWhiteSpace(name) ? modId : name,
            Version = version ?? PartExporter.NewModVersion,
            Description = description,
            Authors = authors is { Count: > 0 } ? [.. authors] : null,
            Dependencies = [.. deps.Distinct(StringComparer.Ordinal).Select(d => new ModDependency { Id = d })],
        };
        var problems = new List<DataDiagnostic>();
        ManifestValidator.Validate(manifest, new DiagnosticSink($"{modId}/{ModLoader.ManifestFileName}", problems));
        if (problems.FirstOrDefault(p => p.Severity == DiagnosticSeverity.Error) is { } problem)
            throw new ModToolException(problem.Message);
        Directory.CreateDirectory(Path.Combine(dir, ModLoader.PartsFolder));
        Directory.CreateDirectory(Path.Combine(dir, AssetImporter.FolderFor(AssetKind.Script)));
        DataJson.WriteFile(Path.Combine(dir, ModLoader.ManifestFileName), DataJson.Write(manifest));
        return $"{modId}/{ModLoader.ManifestFileName}";
    }

    /// <summary>
    /// Imports an asset sent as bytes into a mod, the way the Part Editor imports a file: into <c>meshes/</c>,
    /// <c>scripts/</c>, <c>textures/</c> or <c>sounds/</c> under a clean name, reusing an identical file. A
    /// <c>.gltf</c> brings its buffers and images in <paramref name="companions"/>.
    /// </summary>
    /// <returns>The mod-relative path to write in data.</returns>
    public string Import(string modId, AssetKind kind, string fileName, byte[] bytes, IReadOnlyDictionary<string, byte[]>? companions = null)
    {
        var dir = Files.ModDirectory(modId);
        if (modId == Identifiers.VanillaModId)
            throw new ModToolException("Vanilla is authored through its editor batches; import into your own mod.");
        var staging = Path.Combine(Path.GetTempPath(), "mechanica-import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            if (fileName.Contains('/'))
                throw new ModToolException($"'{fileName}' must be a file name without folders.");
            var source = Path.Combine(staging, CheckName(fileName));
            File.WriteAllBytes(source, bytes);
            foreach (var (name, data) in companions ?? new Dictionary<string, byte[]>())
            {
                var path = Path.Combine(staging, CheckName(name));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, data);
            }
            if (!AssetImporter.TryImport(source, dir, kind, out var relative, out var error))
                throw new ModToolException(error);
            return relative;
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>
    /// Loads all content with the saved profile, as a world would start, and compiles every Lua script of the
    /// loaded mods. With <paramref name="modId"/>, that mod is enabled for the check (without saving the
    /// profile) and only its problems are reported. <paramref name="setup"/> checks that mod setup instead of the
    /// saved one (the Mod Manager's unapplied changes).
    /// </summary>
    public ValidationReport Validate(string? modId = null, ModProfile? setup = null)
    {
        if (modId is not null)
            Files.ModDirectory(modId);
        var manager = setup is null ? Manager() : ModPackageManager.Open(ModsRoot, setup);
        if (modId is not null && manager.Mods.Any(m => m.Id == modId))
            manager.SetEnabled(modId, true);
        var profile = manager.ToProfile();
        var content = GameContent.Load(ModsRoot, profile);
        var folder = Path.GetFileName(ModsRoot);
        var ids = Directory.GetDirectories(ModsRoot).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var diagnostics = content.Diagnostics.Select(d => DiagnosticInfo.From(d.Domain, d.Diagnostic, ids, folder)).ToList();
        // Scripts compile on their own, so a mod that failed to load for other reasons still has them checked.
        if (_checkScript is not null)
            diagnostics.AddRange(CheckScripts(manager.Mods.Where(m => manager.IsEnabled(m.Id) && (modId is null || m.Id == modId)), ids));
        if (modId is not null)
            diagnostics = [.. diagnostics.Where(d => d.Mod == modId)];
        var errors = diagnostics.Count(d => d.Severity == "error");
        return new ValidationReport(errors == 0, errors, diagnostics.Count - errors, [.. content.Mods.Mods.Select(m => m.Id)],
            content.Manifest.Fingerprint, diagnostics);
    }

    /// <summary>Compiles one script; null when it compiles. Needs a <see cref="ScriptChecker"/>.</summary>
    public ScriptProblem? CheckScript(string source) =>
        (_checkScript ?? throw new InvalidOperationException("This workspace has no script checker.")).Invoke(source);

    private IEnumerable<DiagnosticInfo> CheckScripts(IEnumerable<InstalledMod> mods, IReadOnlySet<string> ids)
    {
        var folder = Path.GetFileName(ModsRoot);
        foreach (var mod in mods)
        {
            foreach (var file in Files.List(mod.Id).Where(f => f.Path.EndsWith(".lua", StringComparison.Ordinal)))
            {
                var source = File.ReadAllText(Path.Combine(mod.Directory, file.Path));
                if (_checkScript!(source) is { } problem)
                    yield return DiagnosticInfo.From("scripts", new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.ScriptSyntax,
                        $"{folder}/{mod.Id}/{file.Path}", null, problem.Line, problem.Message), ids, folder);
            }
        }
    }

    // A plain relative path for a staged file (a .gltf's companions may sit in subfolders).
    private static string CheckName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Any(char.IsControl)
            || name.Split('/').Any(s => s is "" or "." or ".." || s.StartsWith('.')))
            throw new ModToolException($"'{name}' is not a plain relative file name.");
        return name;
    }
}
