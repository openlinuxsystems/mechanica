using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Game.Mods;

namespace Mechanica.Game.Editor;

/// <summary>
/// Runs a Bot Behavior Editor batch (<see cref="BotBatch"/>) into the mod that holds it, with the editor's
/// own templates and exporter, exactly as making each bot in the editor and pressing Export would. The
/// <c>--part-batch file</c> command line uses it for batches that have <c>bots</c>.
/// </summary>
public static class BotBatchRunner
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
        var batch = BotBatch.Read(File.ReadAllText(file), display, diagnostics);
        if (batch is null)
            return new BatchRunResult(false, [], diagnostics);
        // A mod kept outside the game's mods folder (like the docs' example) still sees the game's own mods.
        var gameMods = Directory.Exists(ModPaths.ModsRoot) ? Directory.GetDirectories(ModPaths.ModsRoot) : [];
        var exports = BotBatch.Run(batch, modDirectory, BotEditorScreen.Template, display, diagnostics, gameMods);
        return new BatchRunResult(exports.Count > 0 && exports.All(e => e.Succeeded), exports, diagnostics);
    }
}
