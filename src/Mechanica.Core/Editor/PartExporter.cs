using Mechanica.Core.Data;

namespace Mechanica.Core.Editor;

/// <summary>What <see cref="PartExporter.Export"/> did.</summary>
/// <param name="Succeeded">True when the part file was written.</param>
/// <param name="PartFile">Absolute path of the written part file, or null on failure.</param>
/// <param name="QualifiedId">The part's game-wide id, <c>mod_id:part_id</c> (for overrides, the overridden mod's id).</param>
/// <param name="Diagnostics">Every problem found; errors stop the export.</param>
public sealed record ExportResult(bool Succeeded, string? PartFile, string QualifiedId, IReadOnlyList<DataDiagnostic> Diagnostics);

/// <summary>
/// One-click export of a <see cref="PartDocument"/> into its mod folder (chunk 5). It creates the mod's
/// <c>mod.json</c> when missing, writes the baked model mesh, validates the part exactly as the loader
/// would, and only then writes the part file in canonical form and its editor source. A failed export
/// leaves the existing part file untouched.
/// </summary>
public static class PartExporter
{
    /// <summary>Version a new mod's manifest starts at.</summary>
    public const string NewModVersion = "0.1.0";

    /// <summary>
    /// Where the baked model mesh of a part file goes, relative to the mod root: <c>meshes/</c> plus the
    /// part file's path inside <c>parts/</c> (or <c>overrides/</c>), as <c>.glb</c>.
    /// </summary>
    public static string BakedMeshPath(string relativePartFile)
    {
        var relative = relativePartFile.Replace('\\', '/');
        var inner = relative.StartsWith(ModLoader.PartsFolder + "/", StringComparison.Ordinal)
            ? relative[(ModLoader.PartsFolder.Length + 1)..]
            : relative;
        return $"meshes/{Path.ChangeExtension(inner, ".glb")}";
    }

    /// <summary>
    /// The part file in <paramref name="modDirectory"/> (relative to it) named for <paramref name="id"/>, in
    /// <c>parts/</c> or any folder under it, or null when there is none. The Part Editor asks before
    /// starting a new part over one of these.
    /// </summary>
    public static string? ExistingPartFile(string modDirectory, string id)
    {
        var parts = Path.Combine(modDirectory, ModLoader.PartsFolder);
        if (!Directory.Exists(parts))
            return null;
        var found = Directory.EnumerateFiles(parts, id + ".json", SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault();
        return found is null ? null : Path.GetRelativePath(modDirectory, found).Replace('\\', '/');
    }

    /// <summary>
    /// The part file an export will write: the file the part came from, renamed when its id changed, or
    /// <c>parts/&lt;id&gt;.json</c> for a new part.
    /// </summary>
    public static string TargetFile(PartDocument document)
    {
        var id = document.Definition.Id ?? "new_part";
        if (document.RelativeFile is not { } current)
            return $"{ModLoader.PartsFolder}/{id}.json";
        var folder = Path.GetDirectoryName(current)?.Replace('\\', '/');
        return string.IsNullOrEmpty(folder) ? $"{id}.json" : $"{folder}/{id}.json";
    }

    /// <summary>
    /// Writes <paramref name="document"/> into its mod. <paramref name="bakedMesh"/> is the glTF binary
    /// of the model pieces, required when <see cref="PartSource.NeedsBake"/> is true (the game bakes it
    /// with Godot's CSG and glTF writer, which Core cannot use).
    /// </summary>
    public static ExportResult Export(PartDocument document, byte[]? bakedMesh)
    {
        var diagnostics = new List<DataDiagnostic>();
        var target = TargetFile(document);
        var qualified = QualifiedId(document.ModId, target, document.Definition.Id);
        void Fail(string message) =>
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.ExportFailed, target, null, null, message));

        if (!target.StartsWith(ModLoader.PartsFolder + "/", StringComparison.Ordinal)
            && !target.StartsWith(ModLoader.OverridesFolder + "/", StringComparison.Ordinal))
            Fail($"part files must be under {ModLoader.PartsFolder}/ or {ModLoader.OverridesFolder}/.");
        if (!Identifiers.IsValid(document.ModId))
            Fail($"mod folder name '{document.ModId}' is not a valid mod id (lowercase snake_case).");

        var definition = document.Definition;
        var source = document.Source;
        diagnostics.AddRange(PartSource.Validate(source, document.ModId, PartDocument.SourcePathFor(target)));
        if (source.NeedsBake && bakedMesh is not { Length: > 0 })
            Fail("the model has primitive or transformed pieces, so it needs a baked mesh to export.");
        if (diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error) > 0)
            return new ExportResult(false, null, qualified, diagnostics);

        // The model decides the mesh: baked pieces, a single imported mesh, or none (keep what the data says).
        var bakedPath = BakedMeshPath(target);
        if (source.NeedsBake)
            definition = definition with { Mesh = bakedPath };
        else if (source.Pieces.Count == 1)
            definition = definition with { Mesh = source.Pieces[0].Mesh };

        try
        {
            EnsureManifest(document.ModDirectory);
            if (source.NeedsBake)
                WriteBytes(Path.Combine(document.ModDirectory, bakedPath), bakedMesh!);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Fail($"could not write to the mod folder: {e.Message}");
            return new ExportResult(false, null, qualified, diagnostics);
        }

        diagnostics.AddRange(ModLoader.ValidatePart(definition, document.ModDirectory, target));
        if (definition.Id is { } id)
            CheckDuplicate(document, target, id, Fail);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new ExportResult(false, null, qualified, diagnostics);

        var file = Path.Combine(document.ModDirectory, target);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            DataJson.WriteFile(file, DataJson.Write(definition));
            var sourceFile = Path.Combine(document.ModDirectory, PartDocument.SourcePathFor(target));
            if (source.Pieces.Count > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
                DataJson.WriteFile(sourceFile, PartSource.Write(source));
            }
            else if (File.Exists(sourceFile))
            {
                File.Delete(sourceFile);
            }
            if (document.RelativeFile is { } previous && previous != target)
                RemoveRenamed(document, previous);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Fail($"could not write the part file: {e.Message}");
            return new ExportResult(false, null, qualified, diagnostics);
        }

        document.MarkSaved(definition, source, target);
        return new ExportResult(true, file, qualified, diagnostics);
    }

    /// <summary>Writes a <c>mod.json</c> for a new mod (id from the folder name) when the folder has none.</summary>
    public static void EnsureManifest(string modDirectory)
    {
        var path = Path.Combine(modDirectory, ModLoader.ManifestFileName);
        if (File.Exists(path))
            return;
        Directory.CreateDirectory(modDirectory);
        var id = Path.GetFileName(Path.GetFullPath(modDirectory));
        DataJson.WriteFile(path, DataJson.Write(new ModManifest
        {
            Id = id,
            Name = id,
            Version = NewModVersion,
            Dependencies = [],
        }));
    }

    private static string QualifiedId(string modId, string relativeFile, string? partId)
    {
        var parts = relativeFile.Split('/');
        var owner = parts.Length > 2 && parts[0] == ModLoader.OverridesFolder ? parts[1] : modId;
        return $"{owner}:{partId}";
    }

    // Another file of the same kind (own parts, or overrides of the same mod) must not already define the id.
    private static void CheckDuplicate(PartDocument document, string target, string id, Action<string> fail)
    {
        var top = target.Split('/');
        var folder = top[0] == ModLoader.OverridesFolder && top.Length > 2
            ? Path.Combine(document.ModDirectory, top[0], top[1])
            : Path.Combine(document.ModDirectory, ModLoader.PartsFolder);
        if (!Directory.Exists(folder))
            return;
        var targetFull = Path.GetFullPath(Path.Combine(document.ModDirectory, target));
        var previousFull = document.RelativeFile is { } p ? Path.GetFullPath(Path.Combine(document.ModDirectory, p)) : null;
        foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            if (full == targetFull || full == previousFull)
                continue;
            var other = ModLoader.ParseJson<PartDefinition>(File.ReadAllText(file), file, []);
            if (other?.Id == id)
                fail($"part id '{id}' is already used by {Path.GetRelativePath(document.ModDirectory, file).Replace('\\', '/')}; pick another id.");
        }
    }

    // After a rename the old part file would load as a second part. Meshes stay: other parts may use them.
    private static void RemoveRenamed(PartDocument document, string previous)
    {
        foreach (var file in new[] { previous, PartDocument.SourcePathFor(previous) })
        {
            if (File.Exists(Path.Combine(document.ModDirectory, file)))
                File.Delete(Path.Combine(document.ModDirectory, file));
        }
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
