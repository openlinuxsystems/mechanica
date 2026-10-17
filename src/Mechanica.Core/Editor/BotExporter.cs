using Mechanica.Core.Bots;
using Mechanica.Core.Data;

namespace Mechanica.Core.Editor;

/// <summary>
/// One-click export for the Bot Behavior Editor (chunk 21): checks a bot exactly as loading would and
/// writes it in canonical form to <c>bots/&lt;id&gt;.json</c>, saves its Lua script, and saves the raid
/// table to <c>raids/raids.json</c>. The mod's <c>mod.json</c> is created when missing. A failed export
/// leaves the existing files untouched.
/// </summary>
public static class BotExporter
{
    /// <summary>Folder a bot's own script is saved to.</summary>
    public const string ScriptFolder = "scripts/bots";

    /// <summary>Where <see cref="SaveScript"/> puts the script of bot <paramref name="id"/>, relative to the mod root.</summary>
    public static string ScriptFile(string id) => $"{ScriptFolder}/{id}.lua";

    /// <summary>
    /// Checks and writes <paramref name="document"/> into its mod. <paramref name="catalog"/> resolves drop
    /// items. A bot with a different id already in the mod's <c>bots/</c> is refused; when the id changed,
    /// the file it came from is removed.
    /// </summary>
    public static ExportResult Export(BotDocument document, IPartCatalog catalog)
    {
        var target = document.TargetFile;
        var qualified = $"{document.ModId}:{document.Id}";
        var diagnostics = document.Validate(catalog).ToList();
        var targetPath = Path.Combine(document.ModDirectory, target);
        if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error) && document.RelativeFile != target && File.Exists(targetPath))
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.DuplicateBot, target, "$.id", null,
                $"bot '{qualified}' already exists; pick another id."));
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) || document.Definition() is not { } definition)
            return new ExportResult(false, null, qualified, diagnostics);
        try
        {
            PartExporter.EnsureManifest(document.ModDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            DataJson.WriteFile(targetPath, DataJson.Write(definition));
            if (document.RelativeFile is { } previous && previous != target && File.Exists(Path.Combine(document.ModDirectory, previous)))
                File.Delete(Path.Combine(document.ModDirectory, previous));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.ExportFailed, target, null, null, $"could not write the bot: {e.Message}"));
            return new ExportResult(false, null, qualified, diagnostics);
        }
        document.MarkSaved(target);
        return new ExportResult(true, targetPath, qualified, diagnostics);
    }

    /// <summary>
    /// Writes <paramref name="code"/> as the bot's own script (<see cref="ScriptFile"/>, unless the bot already
    /// names a script in its mod) and points the bot at it. Check the Lua's syntax first; this only writes.
    /// Returns the script path, relative to the mod root.
    /// </summary>
    public static string SaveScript(BotDocument document, string code)
    {
        var current = document.Json["script"]?.GetValue<string>();
        var file = current is not null && !current.Contains(':') ? current : ScriptFile(document.Id ?? "new_bot");
        var path = Path.Combine(document.ModDirectory, file);
        PartExporter.EnsureManifest(document.ModDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code.Replace("\r\n", "\n"), new System.Text.UTF8Encoding(false));
        if (current != file)
            document.Set("script", file);
        return file;
    }

    /// <summary>The source of the bot's own script, or null when it has none in its mod.</summary>
    public static string? ReadScript(BotDocument document)
    {
        var current = document.Json["script"]?.GetValue<string>();
        if (current is null || !AssetPath.TryParse(current, document.ModId, AssetKind.Script, out var asset, out _) || asset.ModId != document.ModId)
            return null;
        var path = asset.Resolve(document.ModDirectory);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>Reads the mod's raid table, or null when it has none (or it does not parse; see <paramref name="diagnostics"/>).</summary>
    public static RaidTableDefinition? ReadRaids(string modDirectory, List<DataDiagnostic> diagnostics)
    {
        var path = Path.Combine(modDirectory, RaidTable.FilePath);
        return File.Exists(path) ? ModLoader.ParseJson<RaidTableDefinition>(File.ReadAllText(path), RaidTable.FilePath, diagnostics) : null;
    }

    /// <summary>Checks <paramref name="raids"/> against <paramref name="bots"/> and writes it to the mod's <c>raids/raids.json</c>.</summary>
    public static ExportResult ExportRaids(string modDirectory, RaidTableDefinition raids, IBotCatalog bots)
    {
        var dir = Path.GetFullPath(modDirectory);
        var qualified = $"{Path.GetFileName(dir)}:raids";
        var diagnostics = RaidTable.Validate(raids, bots, RaidTable.FilePath, Path.GetFileName(dir)).ToList();
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new ExportResult(false, null, qualified, diagnostics);
        var path = Path.Combine(dir, RaidTable.FilePath);
        try
        {
            PartExporter.EnsureManifest(dir);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            DataJson.WriteFile(path, DataJson.Write(raids));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.ExportFailed, RaidTable.FilePath, null, null, $"could not write the raid table: {e.Message}"));
            return new ExportResult(false, null, qualified, diagnostics);
        }
        return new ExportResult(true, path, qualified, diagnostics);
    }
}
