using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Game.Creations;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.Editor;

/// <summary>What <see cref="PartBatchRunner.RunAsync"/> did.</summary>
/// <param name="Succeeded">True when the batch was valid and every part and recipe exported.</param>
/// <param name="Exports">One result per part, then per recipe, that was exported or tried, in batch order.</param>
/// <param name="Diagnostics">Every problem: the batch's own, then each export's.</param>
public sealed record BatchRunResult(bool Succeeded, IReadOnlyList<ExportResult> Exports, IReadOnlyList<DataDiagnostic> Diagnostics);

/// <summary>
/// Runs a Part Editor batch (<see cref="PartBatch"/>): each entry becomes a part document that is baked
/// with the editor's model builder and written by the editor's exporter, exactly as the Export button
/// does; its recipes are then saved with the recipe exporter, as the Recipes tab does. The editor's
/// "Run batch…" button and the <c>--part-batch file</c> command line both use it.
/// </summary>
public static class PartBatchRunner
{
    /// <summary>
    /// Runs the batch file at <paramref name="batchFile"/> into the mod that holds it. CSG models are
    /// evaluated under <paramref name="parent"/>, which must be in the scene tree.
    /// </summary>
    public static async Task<BatchRunResult> RunAsync(Node parent, string batchFile)
    {
        var diagnostics = new List<DataDiagnostic>();
        var file = Path.GetFullPath(batchFile);
        var modDirectory = PartDocument.FindModDirectory(file);
        if (modDirectory is null || !File.Exists(file))
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.BatchInvalid, batchFile, null, null,
                File.Exists(file) ? $"'{batchFile}' is not inside a mod folder (no {ModLoader.ManifestFileName} above it)." : $"'{batchFile}' does not exist."));
            return new BatchRunResult(false, [], diagnostics);
        }
        var display = Path.GetRelativePath(modDirectory, file).Replace('\\', '/');
        var batch = PartBatch.Read(File.ReadAllText(file), display, diagnostics);
        var documents = batch is null ? null : PartBatch.Prepare(batch, modDirectory, Template, display, diagnostics);
        if (documents is null)
            return new BatchRunResult(false, [], diagnostics);

        var assets = new PartAssets(modId => Path.Combine(Path.GetDirectoryName(modDirectory)!, modId));
        var exports = new List<ExportResult>();
        foreach (var document in documents)
        {
            var baked = document.Source.NeedsBake
                ? await ModelBuilder.BakeAsync(parent, document.Source, path => assets.LoadMeshes(path, document.ModId))
                : null;
            var result = PartExporter.Export(document, baked);
            exports.Add(result);
            diagnostics.AddRange(result.Diagnostics);
        }
        // Recipes go last, like saving them from the Recipes tab once their parts exist.
        var content = ModLoader.LoadAll(Path.GetDirectoryName(modDirectory)!);
        foreach (var result in PartBatch.ExportRecipes(batch!, modDirectory, content, documents))
        {
            exports.Add(result);
            diagnostics.AddRange(result.Diagnostics);
        }
        return new BatchRunResult(exports.All(e => e.Succeeded), exports, diagnostics);
    }

    // Templates are the editor's own, so a batch starts from exactly what "New…" offers.
    private static string? Template(string name)
    {
        var path = $"{PartEditorScreen.TemplatesPath}/{name}.json";
        return Identifiers.IsValid(name) && FileAccess.FileExists(path) ? FileAccess.GetFileAsString(path) : null;
    }
}
