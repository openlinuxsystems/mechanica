using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Game.Mods;

namespace Mechanica.Game.Editor;

/// <summary>
/// Runs a story batch (<see cref="StoryBatch"/>: POIs and quests) into the mod that holds it, through the
/// same exporter and checks as the loader. The <c>--part-batch file</c> command line uses it for batches
/// that have <c>quests</c> or <c>pois</c>.
/// </summary>
public static class StoryBatchRunner
{
    /// <summary>Runs the batch file at <paramref name="batchFile"/>.</summary>
    public static BatchRunResult Run(string batchFile)
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
        var batch = StoryBatch.Read(File.ReadAllText(file), display, diagnostics);
        if (batch is null)
            return new BatchRunResult(false, [], diagnostics);
        var gameMods = Directory.Exists(ModPaths.ModsRoot) ? Directory.GetDirectories(ModPaths.ModsRoot) : [];
        var exports = StoryBatch.Run(batch, modDirectory, display, diagnostics, gameMods);
        return new BatchRunResult(exports.Count > 0 && exports.All(e => e.Succeeded), exports, diagnostics);
    }
}
