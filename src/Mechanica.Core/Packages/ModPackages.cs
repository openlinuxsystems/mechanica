using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Mechanica.Core.Data;
using Mechanica.Core.Remote;

namespace Mechanica.Core.Packages;

/// <summary>Size limits an installer enforces, so a hostile archive cannot fill the disk (zip bombs).</summary>
/// <param name="MaxArchiveBytes">Largest archive file accepted.</param>
/// <param name="MaxFiles">Most entries in the archive.</param>
/// <param name="MaxFileBytes">Largest single file once extracted.</param>
/// <param name="MaxTotalBytes">Largest total once extracted.</param>
/// <param name="MaxRatio">Highest extracted-to-compressed size ratio for files over 1 MiB.</param>
public sealed record PackageLimits(
    long MaxArchiveBytes = 512L << 20,
    int MaxFiles = 20_000,
    long MaxFileBytes = 256L << 20,
    long MaxTotalBytes = 1L << 30,
    double MaxRatio = 200)
{
    /// <summary>The limits the game uses.</summary>
    public static PackageLimits Default { get; } = new();
}

/// <summary>What <see cref="ModPackages.Export"/> did.</summary>
/// <param name="Path">The written archive, or null when the export was refused.</param>
/// <param name="Manifest">The package manifest written into it, or null when refused.</param>
/// <param name="Diagnostics">Validation problems; any error refuses the export.</param>
/// <param name="Skipped">Files left out of the package (editor leftovers, hidden files), relative to the mod folder.</param>
public sealed record PackageExportResult(string? Path, PackageManifest? Manifest, IReadOnlyList<DataDiagnostic> Diagnostics, IReadOnlyList<string> Skipped)
{
    /// <summary>True when the archive was written.</summary>
    public bool Succeeded => Path is not null;
}

/// <summary>An archive checked by <see cref="ModPackages.Inspect"/>.</summary>
/// <param name="ArchivePath">The archive.</param>
/// <param name="Manifest">Its package manifest, or null when unreadable.</param>
/// <param name="Diagnostics">Every problem found. Any error means the archive must not be installed.</param>
public sealed record PackageInspection(string ArchivePath, PackageManifest? Manifest, IReadOnlyList<DataDiagnostic> Diagnostics)
{
    /// <summary>True when the archive is a sound, verified package for this game version.</summary>
    public bool IsValid => Manifest is not null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}

/// <summary>How an install changes what is installed.</summary>
public enum PackageChange
{
    /// <summary>The mod is not installed yet.</summary>
    New,

    /// <summary>An older version is installed and is replaced.</summary>
    Upgrade,

    /// <summary>The same version is installed and is replaced.</summary>
    Reinstall,

    /// <summary>A newer version is installed and is replaced by this older one.</summary>
    Downgrade,

    /// <summary>A folder with the mod's id exists but is not a valid mod; it is replaced.</summary>
    ReplaceBroken,
}

/// <summary>What installing a package into a mods folder would do (<see cref="ModPackages.PlanInstall"/>).</summary>
/// <param name="Inspection">The verified archive.</param>
/// <param name="Change">New install, or what it replaces.</param>
/// <param name="InstalledVersion">The version installed now, when there is one.</param>
/// <param name="Diagnostics">Inspection problems plus dependency (error) and conflict (warning) checks against the folder.</param>
public sealed record PackageInstallPlan(PackageInspection Inspection, PackageChange Change, SemVersion? InstalledVersion, IReadOnlyList<DataDiagnostic> Diagnostics)
{
    /// <summary>True when nothing blocks the install.</summary>
    public bool CanInstall => Inspection.IsValid && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    /// <summary>True when installing replaces an existing folder, which needs the player's confirmation.</summary>
    public bool Replaces => Change != PackageChange.New;
}

/// <summary>What <see cref="ModPackages.Install"/> did.</summary>
/// <param name="Plan">The plan it followed.</param>
/// <param name="Directory">The installed mod folder, or null when nothing was installed.</param>
/// <param name="Diagnostics">The plan's problems plus anything that went wrong while extracting.</param>
public sealed record PackageInstallResult(PackageInstallPlan Plan, string? Directory, IReadOnlyList<DataDiagnostic> Diagnostics)
{
    /// <summary>True when the mod was installed.</summary>
    public bool Succeeded => Directory is not null;
}

/// <summary>
/// Mod packages (marketplace-ready, AI support PR 8): export an installed mod to a verified
/// <c>&lt;id&gt;-&lt;version&gt;.zip</c>, check an archive before installing it, install it safely (staged,
/// hash-checked while extracting, swapped in last) and uninstall. Godot-free, so the Mod Manager, the AI
/// endpoint and a future mod store client share it. The format is in docs/packaging-guide.md §9.
/// </summary>
public static class ModPackages
{
    /// <summary>The package manifest's file name, inside the mod folder.</summary>
    public const string ManifestFileName = "package.json";

    private const long MaxManifestBytes = 4L << 20;
    private const string StagingPrefix = ".install-";
    private const string TrashPrefix = ".remove-";

    // Zip timestamps are fixed so the same mod always exports to the same bytes.
    private static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The archive name for a mod: <c>&lt;id&gt;-&lt;version&gt;.zip</c>.</summary>
    public static string FileName(string id, string version) => $"{id}-{version}.zip";

    /// <summary>True for files an export leaves out: hidden files and folders, Godot import files, backups.</summary>
    public static bool IsExcluded(string relativePath)
    {
        var segments = relativePath.Split('/');
        var name = segments[^1];
        return segments.Any(s => s.StartsWith('.'))
            || relativePath == ManifestFileName
            || name.EndsWith(".import", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".uid", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith('~')
            || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".orig", StringComparison.OrdinalIgnoreCase)
            || name is "Thumbs.db" or "desktop.ini";
    }

    /// <summary>
    /// Validates the installed mod <paramref name="modId"/> and writes it to
    /// <c>&lt;outputDirectory&gt;/&lt;id&gt;-&lt;version&gt;.zip</c> (replacing a file of that name). Refuses, writing
    /// nothing, when the mod has errors, contains a symbolic link or would break <paramref name="limits"/>.
    /// </summary>
    public static PackageExportResult Export(string modsRoot, string modId, string outputDirectory, PackageLimits? limits = null)
    {
        limits ??= PackageLimits.Default;
        var diagnostics = new List<DataDiagnostic>(ModContentCheck.Validate(modsRoot, modId));
        var skipped = new List<string>();
        var mod = ModLoader.Scan(modsRoot).Mods.FirstOrDefault(m => m.Id == modId);
        if (mod is null || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return Refused(diagnostics, skipped, modId, mod is null ? null : $"{modId} has errors; fix them before exporting.");

        var files = new List<(string Relative, string Full)>();
        foreach (var full in Directory.EnumerateFileSystemEntries(mod.Directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(mod.Directory, full).Replace('\\', '/');
            var info = new FileInfo(full);
            if (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                if (IsExcluded(relative))
                    continue;
                diagnostics.Add(Error(DiagnosticCodes.PackageUnsafePath, $"{modId}/{relative}",
                    "symbolic links cannot be packaged; replace it with the file itself."));
                continue;
            }
            if ((info.Attributes & FileAttributes.Directory) != 0)
                continue;
            if (IsExcluded(relative))
            {
                // List leftovers by name, but not every file inside a hidden folder such as .git.
                if (!InHiddenFolder(relative))
                    skipped.Add(relative);
                continue;
            }
            if (PathProblem(relative) is { } problem)
            {
                diagnostics.Add(Error(DiagnosticCodes.PackageUnsafePath, $"{modId}/{relative}", problem));
                continue;
            }
            files.Add((relative, full));
        }
        files.Sort((a, b) => string.CompareOrdinal(a.Relative, b.Relative));
        skipped.Sort(StringComparer.Ordinal);

        var listed = new List<PackageFile>();
        long total = 0;
        foreach (var (relative, full) in files)
        {
            var size = new FileInfo(full).Length;
            total += size;
            if (size > limits.MaxFileBytes)
                diagnostics.Add(Error(DiagnosticCodes.PackageTooLarge, $"{modId}/{relative}",
                    $"file is {Mib(size)}; packages allow at most {Mib(limits.MaxFileBytes)} per file."));
            using var stream = File.OpenRead(full);
            listed.Add(new PackageFile { Path = relative, Size = size, Sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)) });
        }
        if (files.Count + 1 > limits.MaxFiles)
            diagnostics.Add(Error(DiagnosticCodes.PackageTooLarge, $"{modId}/", $"mod has {files.Count} files; packages allow at most {limits.MaxFiles - 1}."));
        if (total > limits.MaxTotalBytes)
            diagnostics.Add(Error(DiagnosticCodes.PackageTooLarge, $"{modId}/", $"mod is {Mib(total)}; packages allow at most {Mib(limits.MaxTotalBytes)}."));
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return Refused(diagnostics, skipped, modId, null);

        var m = mod.Manifest;
        var manifest = new PackageManifest
        {
            PackageFormat = PackageManifest.CurrentFormat,
            Id = mod.Id,
            Name = m.Name,
            Version = mod.Version.ToString(),
            Description = m.Description,
            Authors = m.Authors,
            Dependencies = m.Dependencies,
            Conflicts = m.Conflicts,
            MinGameVersion = MechanicaInfo.Version,
            MinProtocolVersion = JsonRpcEndpoint.ProtocolVersion,
            Files = listed,
            Sha256 = PackageManifest.ComputePackageHash(listed),
            Signature = "",
        };

        Directory.CreateDirectory(outputDirectory);
        var target = Path.Combine(outputDirectory, FileName(mod.Id, manifest.Version!));
        var temp = target + ".partial";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                AddEntry(zip, $"{mod.Id}/{ManifestFileName}", Encoding.UTF8.GetBytes(DataJson.Write(manifest)));
                foreach (var (relative, full) in files)
                    AddEntry(zip, $"{mod.Id}/{relative}", File.ReadAllBytes(full));
            }
            File.Move(temp, target, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            diagnostics.Add(Error(DiagnosticCodes.FileUnreadable, target, $"could not write the package: {e.Message}"));
            return Refused(diagnostics, skipped, modId, null);
        }
        return new PackageExportResult(target, manifest, diagnostics, skipped);
    }

    /// <summary>
    /// Checks an archive without extracting it: safe paths only (no absolute paths, <c>..</c>, backslashes,
    /// drive letters or symbolic links, one top folder named after the mod), within <paramref name="limits"/>,
    /// a valid <c>package.json</c> whose file list matches the archive exactly, every file's SHA-256 and the
    /// package hash correct, a <c>mod.json</c> that agrees with it, and a game version new enough.
    /// </summary>
    public static PackageInspection Inspect(string archivePath, PackageLimits? limits = null)
    {
        limits ??= PackageLimits.Default;
        var diagnostics = new List<DataDiagnostic>();
        var display = Path.GetFileName(archivePath);
        try
        {
            var length = new FileInfo(archivePath).Length;
            if (length > limits.MaxArchiveBytes)
                return Fail($"archive is {Mib(length)}; packages allow at most {Mib(limits.MaxArchiveBytes)}.", DiagnosticCodes.PackageTooLarge);
            using var zip = ZipFile.OpenRead(archivePath);
            var (manifest, entries) = ReadEntries(zip, display, limits, diagnostics);
            if (manifest is not null && entries is not null)
                CheckContents(manifest, entries, display, diagnostics);
            return new PackageInspection(archivePath, manifest, diagnostics);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            return Fail($"not a readable zip archive: {e.Message}", DiagnosticCodes.PackageInvalid);
        }

        PackageInspection Fail(string message, string code)
        {
            diagnostics.Add(Error(code, display, message));
            return new PackageInspection(archivePath, null, diagnostics);
        }
    }

    /// <summary>
    /// Inspects <paramref name="archivePath"/> and checks it against the mods installed in
    /// <paramref name="modsRoot"/>: every dependency installed in a matching version (error otherwise),
    /// conflicts in either direction and installed mods whose dependency range the new version breaks
    /// (warnings), and what it would replace. Vanilla ships with the game and cannot be installed.
    /// </summary>
    public static PackageInstallPlan PlanInstall(string modsRoot, string archivePath, PackageLimits? limits = null) =>
        Plan(modsRoot, Inspect(archivePath, limits));

    private static PackageInstallPlan Plan(string modsRoot, PackageInspection inspection)
    {
        var diagnostics = new List<DataDiagnostic>(inspection.Diagnostics);
        var manifest = inspection.Manifest;
        if (manifest is null || !inspection.IsValid)
            return new PackageInstallPlan(inspection, PackageChange.New, null, diagnostics);
        var id = manifest.Id!;
        var version = manifest.ParsedVersion!;
        var file = Path.GetFileName(inspection.ArchivePath);
        if (id == Identifiers.VanillaModId)
            diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, file, "vanilla ships with the game and cannot be installed from a package."));

        var scan = Directory.Exists(modsRoot) ? ModLoader.Scan(modsRoot) : new ModScanResult([], new HashSet<string>(), []);
        var installed = scan.Mods.Where(m => m.Id != id).ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var dep in manifest.Dependencies ?? [])
        {
            if (!installed.TryGetValue(dep.Id!, out var have))
                diagnostics.Add(Error(DiagnosticCodes.PackageDependency, file, $"needs '{dep.Id}' {dep.Version ?? "(any version)"}, which is not installed. Install it first."));
            else if (!dep.ParsedVersion!.IsSatisfiedBy(have.Version))
                diagnostics.Add(Error(DiagnosticCodes.PackageDependency, file, $"needs '{dep.Id}' {dep.Version}, but {have.Version} is installed."));
        }
        foreach (var conflict in manifest.Conflicts ?? [])
        {
            if (installed.TryGetValue(conflict.Id!, out var have) && conflict.ParsedVersion!.IsSatisfiedBy(have.Version))
                diagnostics.Add(Warning(DiagnosticCodes.PackageConflict, file, $"conflicts with the installed '{conflict.Id}' {have.Version}; only one of them can be enabled."));
        }
        foreach (var other in installed.Values)
        {
            foreach (var conflict in other.Manifest.Conflicts ?? [])
            {
                if (conflict.Id == id && conflict.ParsedVersion?.IsSatisfiedBy(version) == true)
                    diagnostics.Add(Warning(DiagnosticCodes.PackageConflict, file, $"the installed '{other.Id}' conflicts with {id} {version}; only one of them can be enabled."));
            }
            foreach (var dep in other.Manifest.Dependencies ?? [])
            {
                if (dep.Id == id && dep.ParsedVersion?.IsSatisfiedBy(version) == false)
                    diagnostics.Add(Warning(DiagnosticCodes.PackageDependency, file, $"the installed '{other.Id}' needs {id} {dep.Version} and will not load with {version}."));
            }
        }

        var existing = scan.Mods.FirstOrDefault(m => m.Id == id);
        var folderExists = Directory.Exists(Path.Combine(modsRoot, id)) || File.Exists(Path.Combine(modsRoot, id));
        var change = existing is null
            ? folderExists ? PackageChange.ReplaceBroken : PackageChange.New
            : existing.Version.CompareTo(version) switch { < 0 => PackageChange.Upgrade, 0 => PackageChange.Reinstall, _ => PackageChange.Downgrade };
        if (existing is not null && !string.Equals(Path.GetFileName(existing.Directory), id, StringComparison.Ordinal))
            diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, file, $"'{id}' is installed in a folder named differently ({existing.Directory}); remove it first."));
        return new PackageInstallPlan(inspection, change, existing?.Version, diagnostics);
    }

    /// <summary>
    /// Installs a package into <c>&lt;modsRoot&gt;/&lt;id&gt;/</c>. Nothing changes unless the plan allows it and,
    /// when a folder would be replaced, <paramref name="replace"/> is true (the player confirmed). Files are
    /// extracted into a hidden staging folder and hashed again as they are written; the old folder is swapped
    /// out only after every file checked out, and restored if the swap fails. The extracted mod keeps its
    /// <c>package.json</c>, so <see cref="VerifyInstalled"/> can later tell whether its files were changed.
    /// </summary>
    public static PackageInstallResult Install(string modsRoot, string archivePath, bool replace, PackageLimits? limits = null)
    {
        limits ??= PackageLimits.Default;
        var plan = PlanInstall(modsRoot, archivePath, limits);
        var diagnostics = new List<DataDiagnostic>(plan.Diagnostics);
        var file = Path.GetFileName(archivePath);
        if (!plan.CanInstall)
            return new PackageInstallResult(plan, null, diagnostics);
        if (plan.Replaces && !replace)
        {
            diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, file,
                $"'{plan.Inspection.Manifest!.Id}' is already installed ({plan.InstalledVersion?.ToString() ?? "broken"}); confirm replacing it."));
            return new PackageInstallResult(plan, null, diagnostics);
        }

        var manifest = plan.Inspection.Manifest!;
        var id = manifest.Id!;
        Directory.CreateDirectory(modsRoot);
        var staging = Path.Combine(modsRoot, StagingPrefix + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(modsRoot, id);
        var trash = Path.Combine(modsRoot, TrashPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            var staged = Path.Combine(staging, id);
            Directory.CreateDirectory(staged);
            var expected = manifest.Files!.ToDictionary(f => $"{id}/{f.Path}", StringComparer.Ordinal);
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
                {
                    var relative = entry.FullName[(id.Length + 1)..];
                    var path = Path.Combine(staged, relative.Replace('/', Path.DirectorySeparatorChar));
                    // Defence in depth: inspection already refused anything that could leave the folder.
                    if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(staged) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException($"{entry.FullName} leaves the mod folder.");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var isManifest = relative == ManifestFileName;
                    var cap = isManifest ? MaxManifestBytes : expected[entry.FullName].Size!.Value;
                    string hash;
                    using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                        hash = CopyHashed(entry, output, cap, exact: !isManifest);
                    if (!isManifest && hash != expected[entry.FullName].Sha256)
                        throw new InvalidDataException($"{entry.FullName} changed while installing (SHA-256 mismatch).");
                }
            }

            var hadOld = Directory.Exists(target) || File.Exists(target);
            if (hadOld)
                Move(target, trash);
            try
            {
                Directory.Move(staged, target);
            }
            catch
            {
                if (hadOld)
                    Move(trash, target);
                throw;
            }
            if (hadOld)
                DeletePath(trash);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, file, $"install failed, nothing was changed: {e.Message}"));
            return new PackageInstallResult(plan, null, diagnostics);
        }
        finally
        {
            DeletePath(staging);
        }
        return new PackageInstallResult(plan, target, diagnostics);
    }

    /// <summary>Installed mods that depend on <paramref name="modId"/>; they stop loading when it is uninstalled.</summary>
    public static IReadOnlyList<string> Dependents(string modsRoot, string modId) =>
        [.. ModLoader.Scan(modsRoot).Mods.Where(m => m.DependencyIds.Contains(modId)).Select(m => m.Id)];

    /// <summary>
    /// Deletes the installed mod <paramref name="modId"/> from <paramref name="modsRoot"/>. Vanilla cannot be
    /// uninstalled. Its entry in the mod profile is kept, so a reinstall gets its place back.
    /// </summary>
    /// <returns>Problems; empty on success.</returns>
    public static IReadOnlyList<DataDiagnostic> Uninstall(string modsRoot, string modId)
    {
        var display = $"{modId}/";
        if (modId == Identifiers.VanillaModId)
            return [Error(DiagnosticCodes.PackageInvalid, display, "vanilla ships with the game and cannot be uninstalled.")];
        var mod = ModLoader.Scan(modsRoot).Mods.FirstOrDefault(m => m.Id == modId);
        if (mod is null)
            return [Error(DiagnosticCodes.PackageInvalid, display, $"mod '{modId}' is not installed.")];
        var trash = Path.Combine(modsRoot, TrashPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            // Rename first so a half-deleted mod never shows up as installed.
            Move(mod.Directory, trash);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [Error(DiagnosticCodes.PackageInvalid, display, $"could not remove the mod folder: {e.Message}")];
        }
        DeletePath(trash);
        return [];
    }

    /// <summary>
    /// Compares an installed mod folder with the <c>package.json</c> it was installed with: files changed,
    /// missing or added since (warnings, <c>MD1310</c>). Returns an error when the folder has no package
    /// manifest (it was not installed from a package).
    /// </summary>
    public static IReadOnlyList<DataDiagnostic> VerifyInstalled(string modDirectory)
    {
        var id = Path.GetFileName(Path.TrimEndingDirectorySeparator(modDirectory));
        var manifestPath = Path.Combine(modDirectory, ManifestFileName);
        var diagnostics = new List<DataDiagnostic>();
        if (!File.Exists(manifestPath))
            return [Error(DiagnosticCodes.PackageInvalid, $"{id}/{ManifestFileName}", "the mod was not installed from a package.")];
        var manifest = ModLoader.ParseJson<PackageManifest>(File.ReadAllText(manifestPath), $"{id}/{ManifestFileName}", diagnostics);
        if (manifest?.Files is null)
            return diagnostics.Count > 0 ? diagnostics : [Error(DiagnosticCodes.PackageInvalid, $"{id}/{ManifestFileName}", "package.json lists no files.")];
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in manifest.Files)
        {
            if (f.Path is null || PathProblem(f.Path) is not null)
                continue;
            listed.Add(f.Path);
            var path = Path.Combine(modDirectory, f.Path);
            if (!File.Exists(path))
            {
                diagnostics.Add(Warning(DiagnosticCodes.PackageModified, $"{id}/{f.Path}", "file is missing since install."));
                continue;
            }
            using var stream = File.OpenRead(path);
            if (Convert.ToHexStringLower(SHA256.HashData(stream)) != f.Sha256)
                diagnostics.Add(Warning(DiagnosticCodes.PackageModified, $"{id}/{f.Path}", "file was changed since install."));
        }
        foreach (var full in Directory.EnumerateFiles(modDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(modDirectory, full).Replace('\\', '/');
            if (!listed.Contains(relative) && !IsExcluded(relative))
                diagnostics.Add(Warning(DiagnosticCodes.PackageModified, $"{id}/{relative}", "file was added since install."));
        }
        return diagnostics;
    }

    // Reads the central directory: safe names, one top folder, limits, and the package manifest.
    private static (PackageManifest?, Dictionary<string, ZipArchiveEntry>?) ReadEntries(
        ZipArchive zip, string display, PackageLimits limits, List<DataDiagnostic> diagnostics)
    {
        if (zip.Entries.Count > limits.MaxFiles)
        {
            diagnostics.Add(Error(DiagnosticCodes.PackageTooLarge, display, $"archive has {zip.Entries.Count} entries; packages allow at most {limits.MaxFiles}."));
            return (null, null);
        }
        var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? top = null;
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            var isDirectory = name.EndsWith('/');
            var path = isDirectory ? name[..^1] : name;
            if (PathProblem(path) is { } problem)
            {
                diagnostics.Add(Error(DiagnosticCodes.PackageUnsafePath, $"{display}:{name}", problem));
                continue;
            }
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000)
            {
                diagnostics.Add(Error(DiagnosticCodes.PackageUnsafePath, $"{display}:{name}", "symbolic links are not allowed in packages."));
                continue;
            }
            var first = path.Split('/')[0];
            top ??= first;
            if (first != top)
            {
                diagnostics.Add(Error(DiagnosticCodes.PackageUnsafePath, $"{display}:{name}", $"everything must be inside one mod folder ('{top}/')."));
                continue;
            }
            if (isDirectory)
                continue;
            if (path == top)
            {
                diagnostics.Add(Error(DiagnosticCodes.PackageUnsafePath, $"{display}:{name}", "files must be inside the mod folder."));
                continue;
            }
            if (!folded.Add(path))
            {
                diagnostics.Add(Error(DiagnosticCodes.PackageUnsafePath, $"{display}:{name}", "the archive holds this path twice (letter case ignored)."));
                continue;
            }
            if (entry.Length > limits.MaxFileBytes)
                diagnostics.Add(Error(DiagnosticCodes.PackageTooLarge, $"{display}:{name}", $"file is {Mib(entry.Length)}; packages allow at most {Mib(limits.MaxFileBytes)} per file."));
            else if (entry.Length > 1L << 20 && entry.Length > limits.MaxRatio * Math.Max(1, entry.CompressedLength))
                diagnostics.Add(Error(DiagnosticCodes.PackageTooLarge, $"{display}:{name}", "file compresses suspiciously well (possible zip bomb)."));
            total += entry.Length;
            files.Add(path, entry);
        }
        if (total > limits.MaxTotalBytes)
            diagnostics.Add(Error(DiagnosticCodes.PackageTooLarge, display, $"archive extracts to {Mib(total)}; packages allow at most {Mib(limits.MaxTotalBytes)}."));
        if (top is null)
        {
            diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, display, "archive is empty."));
            return (null, null);
        }
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return (null, null);

        var manifestDisplay = $"{display}:{top}/{ManifestFileName}";
        if (!files.TryGetValue($"{top}/{ManifestFileName}", out var manifestEntry))
        {
            diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, manifestDisplay,
                $"not a mod package: no {top}/{ManifestFileName}. Export packages from the Mod Manager."));
            return (null, null);
        }
        using var buffer = new MemoryStream();
        CopyHashed(manifestEntry, buffer, MaxManifestBytes, exact: false);
        var manifest = ModLoader.ParseJson<PackageManifest>(Encoding.UTF8.GetString(buffer.ToArray()), manifestDisplay, diagnostics);
        if (manifest is null)
            return (null, null);
        var before = diagnostics.Count;
        CheckManifest(manifest, top, manifestDisplay, diagnostics);
        return diagnostics.Skip(before).Any(d => d.Severity == DiagnosticSeverity.Error) ? (null, null) : (manifest, files);
    }

    private static void CheckManifest(PackageManifest manifest, string top, string file, List<DataDiagnostic> diagnostics)
    {
        if ((manifest.PackageFormat ?? 1) > PackageManifest.CurrentFormat)
            diagnostics.Add(Error(DiagnosticCodes.PackageGameVersion, file, $"package format {manifest.PackageFormat} is newer than this game reads ({PackageManifest.CurrentFormat}); update the game."));
        if (!Identifiers.IsValid(manifest.Id))
            diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, file, "id is missing or not lowercase snake_case."));
        else if (manifest.Id != top)
            diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, file, $"id '{manifest.Id}' does not match the archive's folder '{top}'."));
        if (manifest.ParsedVersion is null)
            diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, file, "version is missing or not major.minor.patch."));
        foreach (var dep in (manifest.Dependencies ?? []).Concat(manifest.Conflicts ?? []))
        {
            if (!Identifiers.IsValid(dep.Id) || dep.ParsedVersion is null)
                diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, file, $"dependency or conflict '{dep.Id}' {dep.Version} is not a valid mod reference."));
        }
        if (manifest.MinGameVersion is not null)
        {
            if (!SemVersion.TryParse(manifest.MinGameVersion, out var min))
                diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, file, "min_game_version is not major.minor.patch."));
            else if (min > SemVersion.Parse(MechanicaInfo.Version))
                diagnostics.Add(Error(DiagnosticCodes.PackageGameVersion, file, $"needs game version {min} or later; this is {MechanicaInfo.Version}."));
        }
        if (manifest.MinProtocolVersion > JsonRpcEndpoint.ProtocolVersion)
            diagnostics.Add(Error(DiagnosticCodes.PackageGameVersion, file, $"needs AI protocol {manifest.MinProtocolVersion} or later; this game speaks {JsonRpcEndpoint.ProtocolVersion}."));
        if (manifest.Files is null)
            diagnostics.Add(Error(DiagnosticCodes.PackageFileList, file, "files is missing."));
        if (manifest.Sha256 is null)
            diagnostics.Add(Error(DiagnosticCodes.PackageFileList, file, "sha256 is missing."));
    }

    // Matches the archive to the file list and hashes every file, reading at most the listed size + 1.
    private static void CheckContents(PackageManifest manifest, Dictionary<string, ZipArchiveEntry> entries, string display,
        List<DataDiagnostic> diagnostics)
    {
        var id = manifest.Id!;
        var listed = new Dictionary<string, PackageFile>(StringComparer.Ordinal);
        foreach (var f in manifest.Files!)
        {
            if (f.Path is null || PathProblem(f.Path) is { } || f.Path == ManifestFileName || f.Size is not >= 0
                || f.Sha256 is not { Length: 64 } || !f.Sha256.All(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c)))
                diagnostics.Add(Error(DiagnosticCodes.PackageFileList, $"{display}:{id}/{ManifestFileName}", $"files entry '{f.Path}' needs a safe path, a size and a lowercase SHA-256."));
            else if (!listed.TryAdd(f.Path, f))
                diagnostics.Add(Error(DiagnosticCodes.PackageFileList, $"{display}:{id}/{ManifestFileName}", $"'{f.Path}' is listed twice."));
        }
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return;
        foreach (var path in entries.Keys.Where(p => p != $"{id}/{ManifestFileName}" && !listed.ContainsKey(p[(id.Length + 1)..])))
            diagnostics.Add(Error(DiagnosticCodes.PackageFileList, $"{display}:{path}", "file is in the archive but not in package.json."));
        foreach (var path in listed.Keys.Where(p => !entries.ContainsKey($"{id}/{p}")))
            diagnostics.Add(Error(DiagnosticCodes.PackageFileList, $"{display}:{id}/{path}", "package.json lists this file but the archive does not have it."));
        if (!listed.ContainsKey(ModLoader.ManifestFileName))
            diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, $"{display}:{id}/{ModLoader.ManifestFileName}", "the package has no mod.json."));
        if (PackageManifest.ComputePackageHash(listed.Values) != manifest.Sha256)
            diagnostics.Add(Error(DiagnosticCodes.PackageHashMismatch, $"{display}:{id}/{ManifestFileName}", "the package hash does not match its file list; the package was altered."));
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return;

        foreach (var (path, f) in listed.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var entry = entries[$"{id}/{path}"];
            using var sink = path == ModLoader.ManifestFileName ? new MemoryStream() : Stream.Null;
            string hash;
            try
            {
                hash = CopyHashed(entry, sink, f.Size!.Value, exact: true);
            }
            catch (InvalidDataException e)
            {
                diagnostics.Add(Error(DiagnosticCodes.PackageHashMismatch, $"{display}:{id}/{path}", e.Message));
                continue;
            }
            if (hash != f.Sha256)
            {
                diagnostics.Add(Error(DiagnosticCodes.PackageHashMismatch, $"{display}:{id}/{path}", "SHA-256 does not match package.json; the file was altered or damaged."));
                continue;
            }
            if (sink is MemoryStream modJson)
                CheckModJson(manifest, Encoding.UTF8.GetString(modJson.ToArray()), $"{display}:{id}/{path}", diagnostics);
        }
    }

    private static void CheckModJson(PackageManifest manifest, string json, string file, List<DataDiagnostic> diagnostics)
    {
        var mod = ModLoader.ParseJson<ModManifest>(json, file, diagnostics);
        if (mod is null)
            return;
        if (mod.Id != manifest.Id)
            diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, file, $"mod.json id '{mod.Id}' does not match package.json ('{manifest.Id}')."));
        if (mod.ParsedVersion is null || mod.ParsedVersion.CompareTo(manifest.ParsedVersion) != 0)
            diagnostics.Add(Error(DiagnosticCodes.PackageInvalid, file, $"mod.json version '{mod.Version}' does not match package.json ('{manifest.Version}')."));
        if ((mod.FormatVersion ?? 1) > ModManifest.CurrentFormatVersion)
            diagnostics.Add(Error(DiagnosticCodes.PackageGameVersion, file, $"the mod's format_version {mod.FormatVersion} is newer than this game reads; update the game."));
    }

    // Copies an entry while hashing, refusing to read more than cap bytes (the header's size can lie).
    // exact: the entry must be exactly cap bytes long.
    private static string CopyHashed(ZipArchiveEntry entry, Stream output, long cap, bool exact)
    {
        using var input = entry.Open();
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > cap)
                throw new InvalidDataException($"{entry.FullName} is larger than package.json says ({cap} bytes).");
            sha.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
        }
        if (exact && total != cap)
            throw new InvalidDataException($"{entry.FullName} is {total} bytes, but package.json says {cap}.");
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    /// <summary>Why <paramref name="path"/> (relative, <c>/</c>-separated) is unsafe to extract, or null when it is fine.</summary>
    public static string? PathProblem(string path)
    {
        if (path.Length == 0)
            return "empty path.";
        if (path.Length > 240)
            return "path is longer than 240 characters.";
        if (path.Contains('\\'))
            return "backslashes are not allowed; use '/'.";
        if (path.StartsWith('/') || path.Contains(':'))
            return "absolute paths, drive letters and ':' are not allowed.";
        if (path.Any(c => char.IsControl(c) || c is '<' or '>' or '"' or '|' or '?' or '*'))
            return "path contains characters that are not allowed in file names.";
        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or "..")
                return "'..', '.' and empty path segments are not allowed.";
            if (segment.EndsWith('.') || segment.EndsWith(' '))
                return "names cannot end with '.' or a space.";
        }
        return null;
    }

    private static void AddEntry(ZipArchive zip, string name, byte[] data)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTime;
        using var stream = entry.Open();
        stream.Write(data);
    }

    private static void Move(string from, string to)
    {
        if (Directory.Exists(from))
            Directory.Move(from, to);
        else
            File.Move(from, to);
    }

    private static void DeletePath(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (info.LinkTarget is not null)
                info.Delete();
            else if (info.Exists)
                info.Delete(recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover hidden folder is skipped by the loader; the next install or uninstall tries again.
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool InHiddenFolder(string relative) => relative.Split('/')[..^1].Any(s => s.StartsWith('.'));

    private static PackageExportResult Refused(List<DataDiagnostic> diagnostics, List<string> skipped, string modId, string? summary)
    {
        if (summary is not null && !diagnostics.Any(d => d.Code == DiagnosticCodes.PackageExportRefused))
            diagnostics.Add(Error(DiagnosticCodes.PackageExportRefused, $"{modId}/{ModLoader.ManifestFileName}", summary));
        return new PackageExportResult(null, null, diagnostics, skipped);
    }

    private static string Mib(long bytes) => $"{bytes / (1024.0 * 1024.0):0.#} MiB";

    private static DataDiagnostic Error(string code, string file, string message) =>
        new(DiagnosticSeverity.Error, code, file, null, null, message);

    private static DataDiagnostic Warning(string code, string file, string message) =>
        new(DiagnosticSeverity.Warning, code, file, null, null, message);
}
