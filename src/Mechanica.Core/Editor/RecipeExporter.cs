using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Editor;

/// <summary>A recipe file in a mod, as the editor's recipe panel lists it.</summary>
/// <param name="RelativeFile">Path inside the mod, like <c>recipes/chest.json</c>.</param>
/// <param name="Recipe">The parsed recipe.</param>
public sealed record RecipeFile(string RelativeFile, RecipeDefinition Recipe);

/// <summary>
/// Reads and writes a mod's recipe files for the editor's recipe panel (chunk 18). A save checks the
/// recipe exactly as loading would, then writes it in canonical form to <c>recipes/&lt;id&gt;.json</c>
/// (renaming the file when the id changed), creating the mod's <c>mod.json</c> when missing. A failed save
/// leaves the existing file untouched.
/// </summary>
public static class RecipeExporter
{
    /// <summary>
    /// The file a recipe with <paramref name="id"/> is saved to, relative to the mod root: <c>recipes/&lt;id&gt;.json</c>,
    /// or <c>overrides/&lt;owner&gt;/recipes/&lt;id&gt;.json</c> for an override of mod <paramref name="overrideOf"/>'s recipe.
    /// </summary>
    public static string TargetFile(string id, string? overrideOf = null) => $"{FolderFor(overrideOf)}/{id}.json";

    /// <summary>The folder (relative to the mod root) of a mod's own recipes, or of its overrides of <paramref name="overrideOf"/>'s.</summary>
    public static string FolderFor(string? overrideOf) => overrideOf is null
        ? RecipeBook.Folder
        : $"{ModLoader.OverridesFolder}/{overrideOf}/{ModLoader.RecipeOverridesFolder}";

    /// <summary>
    /// Every recipe file in the mod at <paramref name="modDirectory"/> (or its overrides of mod
    /// <paramref name="overrideOf"/>'s recipes), ordered by path. Files that don't parse are reported in
    /// <paramref name="diagnostics"/> and skipped.
    /// </summary>
    public static IReadOnlyList<RecipeFile> ReadAll(string modDirectory, List<DataDiagnostic> diagnostics, string? overrideOf = null)
    {
        var folder = Path.Combine(modDirectory, FolderFor(overrideOf));
        if (!Directory.Exists(folder))
            return [];
        var files = new List<RecipeFile>();
        foreach (var path in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(modDirectory, path).Replace('\\', '/');
            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.FileUnreadable, relative, null, null, $"could not read file: {e.Message}"));
                continue;
            }
            if (ModLoader.ParseJson<RecipeDefinition>(json, relative, diagnostics) is { } recipe)
                files.Add(new RecipeFile(relative, recipe));
        }
        return [.. files.OrderBy(f => f.RelativeFile, StringComparer.Ordinal)];
    }

    /// <summary>The recipes in a mod whose outputs include <paramref name="itemId"/> (a bare id means this mod).</summary>
    public static IEnumerable<RecipeFile> Making(IEnumerable<RecipeFile> files, string modId, string itemId)
    {
        var item = Identifiers.Qualify(itemId, modId);
        return files.Where(f => (f.Recipe.Outputs ?? []).Any(o => o.Item is { } i && Identifiers.Qualify(i) == item));
    }

    /// <summary>
    /// Checks <paramref name="recipe"/> and writes it into the mod at <paramref name="modDirectory"/>.
    /// <paramref name="catalog"/> resolves item and station ids; <paramref name="extraParts"/> adds parts
    /// that are not loaded yet (the part open in the editor), keyed by qualified id.
    /// <paramref name="previousFile"/> is the file the recipe was opened from, removed when the id changed.
    /// With <paramref name="overrideOf"/> the recipe replaces that mod's recipe of the same id and is written to
    /// <c>overrides/&lt;overrideOf&gt;/recipes/</c>.
    /// </summary>
    public static ExportResult Export(string modDirectory, RecipeDefinition recipe, IPartCatalog catalog,
        IReadOnlyDictionary<string, PartDefinition>? extraParts = null, string? previousFile = null, string? overrideOf = null)
    {
        var dir = Path.GetFullPath(modDirectory);
        var folderMod = Path.GetFileName(dir);
        if (overrideOf == folderMod)
            overrideOf = null;
        var modId = overrideOf ?? folderMod;
        var target = TargetFile(recipe.Id ?? "new_recipe", overrideOf);
        var qualified = $"{modId}:{recipe.Id}";
        var lookup = new Overlay(catalog, extraParts ?? new Dictionary<string, PartDefinition>());
        var diagnostics = RecipeBook.Validate(recipe, modId, lookup, target).ToList();
        var targetPath = Path.Combine(dir, target);
        if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error) && previousFile != target && File.Exists(targetPath))
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.DuplicateRecipe, target, "$.id", null,
                $"recipe '{qualified}' already exists; pick another id."));
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new ExportResult(false, null, qualified, diagnostics);
        try
        {
            PartExporter.EnsureManifest(dir);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            DataJson.WriteFile(targetPath, DataJson.Write(recipe));
            if (previousFile is not null && previousFile != target && File.Exists(Path.Combine(dir, previousFile)))
                File.Delete(Path.Combine(dir, previousFile));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.ExportFailed, target, null, null, $"could not write the recipe: {e.Message}"));
            return new ExportResult(false, null, qualified, diagnostics);
        }
        return new ExportResult(true, targetPath, qualified, diagnostics);
    }

    /// <summary>Deletes a recipe file (own or override) from the mod. Returns false when it was not there.</summary>
    public static bool Delete(string modDirectory, string relativeFile)
    {
        var path = Path.GetFullPath(Path.Combine(modDirectory, relativeFile));
        var parts = relativeFile.Replace('\\', '/').Split('/');
        var overrideOf = parts.Length > 3 && parts[0] == ModLoader.OverridesFolder && parts[2] == ModLoader.RecipeOverridesFolder ? parts[1] : null;
        var folder = Path.GetFullPath(Path.Combine(modDirectory, FolderFor(overrideOf))) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(folder, StringComparison.Ordinal) || !File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }

    private sealed class Overlay(IPartCatalog inner, IReadOnlyDictionary<string, PartDefinition> extra) : IPartCatalog
    {
        public PartDefinition? FindDefinition(string id) =>
            PartUpgrades.SplitItem(id).Level == 0 && extra.TryGetValue(Identifiers.Qualify(id), out var part) ? part : inner.FindDefinition(id);
    }
}
