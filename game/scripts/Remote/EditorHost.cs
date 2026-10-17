using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.ModTools;
using Mechanica.Game.Creations;
using Mechanica.Game.Editor;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.Remote;

/// <summary>
/// The game behind the editor operations of the AI endpoint: the Part Editor's and Bot Behavior Editor's own
/// templates, the editor's model baker (CSG is evaluated under <paramref name="parent"/>, which must be in the
/// tree) and the batch runners <c>--part-batch</c> uses.
/// </summary>
public sealed class EditorHost(Node parent) : IEditorHost
{
    /// <inheritdoc />
    public IReadOnlyList<string> PartTemplates => PartEditorScreen.TemplateNames;

    /// <inheritdoc />
    public string? PartTemplate(string name) => Read(PartEditorScreen.TemplatesPath, name);

    /// <inheritdoc />
    public IReadOnlyList<string> BotTemplates =>
        [.. DirAccess.GetFilesAt(BotEditorScreen.TemplatesPath).Where(f => f.EndsWith(".json", StringComparison.Ordinal)).Select(f => f[..^5]).Order(StringComparer.Ordinal)];

    /// <inheritdoc />
    public string? BotTemplate(string name) => BotEditorScreen.Template(name);

    /// <inheritdoc />
    public Task<byte[]?> BakeAsync(PartDocument document)
    {
        var mods = Path.GetDirectoryName(document.ModDirectory)!;
        var assets = new PartAssets(modId => Path.Combine(mods, modId));
        return ModelBuilder.BakeAsync(parent, document.Source, path => assets.LoadMeshes(path, document.ModId));
    }

    /// <inheritdoc />
    public async Task<(string Kind, bool Ok, IReadOnlyList<ExportResult> Exports, IReadOnlyList<DataDiagnostic> Diagnostics)> RunBatchAsync(string batchFile)
    {
        var text = File.ReadAllText(batchFile);
        var (kind, result) = BotBatch.IsBotBatch(text) ? ("bots", BotBatchRunner.Run(batchFile))
            : StoryBatch.IsStoryBatch(text) ? ("story", StoryBatchRunner.Run(batchFile))
            : ("parts", await PartBatchRunner.RunAsync(parent, batchFile));
        return (kind, result.Succeeded, result.Exports, result.Diagnostics);
    }

    private static string? Read(string folder, string name)
    {
        var path = $"{folder}/{name}.json";
        return Identifiers.IsValid(name) && FileAccess.FileExists(path) ? FileAccess.GetFileAsString(path) : null;
    }
}
