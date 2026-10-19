using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Editor;

/// <summary>
/// One recipe file the Recipe Editor lists.
/// </summary>
/// <param name="Mod">The mod whose folder holds the file.</param>
/// <param name="OverrideOf">For an override (<c>overrides/&lt;owner&gt;/recipes/</c>), the mod whose recipe it replaces; else null.</param>
/// <param name="File">The file and its parsed recipe.</param>
public sealed record RecipeEntry(string Mod, string? OverrideOf, RecipeFile File)
{
    /// <summary>The mod the recipe belongs to: <see cref="OverrideOf"/> for an override, else <see cref="Mod"/>.</summary>
    public string Owner => OverrideOf ?? Mod;

    /// <summary>The recipe's id as the game knows it, <c>owner:id</c>.</summary>
    public string QualifiedId => $"{Owner}:{File.Recipe.Id}";
}

/// <summary>
/// Every recipe of a set of mods, for the mod-wide Recipe Editor: each mod's own recipes and its overrides of other
/// mods' recipes, which one wins in load order, and how a mod starts a new recipe or an override of another's. Writing
/// goes through <see cref="RecipeExporter"/>.
/// </summary>
public static class RecipeCatalog
{
    /// <summary>
    /// The recipe files of <paramref name="mods"/> (folder names under <paramref name="modsRoot"/>, in load order):
    /// for each mod its own recipes, then its overrides by owner, each by path. Files that don't parse go to
    /// <paramref name="diagnostics"/>.
    /// </summary>
    public static IReadOnlyList<RecipeEntry> ReadAll(string modsRoot, IEnumerable<string> mods, List<DataDiagnostic> diagnostics)
    {
        var entries = new List<RecipeEntry>();
        foreach (var mod in mods)
        {
            var dir = Path.Combine(modsRoot, mod);
            if (!Directory.Exists(dir))
                continue;
            entries.AddRange(RecipeExporter.ReadAll(dir, diagnostics).Select(f => new RecipeEntry(mod, null, f)));
            var overrides = Path.Combine(dir, ModLoader.OverridesFolder);
            if (!Directory.Exists(overrides))
                continue;
            foreach (var owner in Directory.GetDirectories(overrides).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal))
            {
                if (owner != mod)
                    entries.AddRange(RecipeExporter.ReadAll(dir, diagnostics, owner).Select(f => new RecipeEntry(mod, owner, f)));
            }
        }
        return entries;
    }

    /// <summary>
    /// The entry that wins for each recipe id: the owner's own recipe, replaced by the last override in load order
    /// (<paramref name="entries"/> as <see cref="ReadAll"/> lists them).
    /// </summary>
    public static IReadOnlyDictionary<string, RecipeEntry> Effective(IReadOnlyList<RecipeEntry> entries)
    {
        var winners = new Dictionary<string, RecipeEntry>(StringComparer.Ordinal);
        foreach (var entry in entries.Where(e => e.OverrideOf is null))
            winners.TryAdd(entry.QualifiedId, entry);
        foreach (var entry in entries.Where(e => e.OverrideOf is not null))
        {
            if (winners.ContainsKey(entry.QualifiedId))
                winners[entry.QualifiedId] = entry;
        }
        return winners;
    }

    /// <summary>
    /// The first of <paramref name="wanted"/>, <c>wanted_2</c>, <c>wanted_3</c>, … that no recipe of the mod at
    /// <paramref name="modDirectory"/> uses yet.
    /// </summary>
    public static string FreeId(string modDirectory, string wanted)
    {
        var taken = RecipeExporter.ReadAll(modDirectory, []).Select(f => f.Recipe.Id ?? "").ToHashSet(StringComparer.Ordinal);
        var id = wanted;
        for (var n = 2; taken.Contains(id); n++)
            id = $"{wanted}_{n}";
        return id;
    }

    /// <summary>
    /// A new recipe in mod <paramref name="modId"/> (its folder <paramref name="modDirectory"/>) at the Fabricator, with no
    /// inputs yet: one of <paramref name="output"/> (an item id; bare means this mod), or one blank output row when null.
    /// </summary>
    public static RecipeDefinition NewRecipe(string modDirectory, string modId, string? output)
    {
        var item = string.IsNullOrWhiteSpace(output) ? null : Identifiers.Qualify(output, modId);
        return new RecipeDefinition
        {
            Id = FreeId(modDirectory, item?.Split(':')[1] ?? "new_recipe"),
            Stations = ["fabricator"],
            Inputs = [],
            Outputs = [new RecipeItem { Item = item ?? "", Count = 1 }],
            Time = 1,
        };
    }

    /// <summary>
    /// A copy of another mod's recipe as a new recipe of the mod at <paramref name="modDirectory"/>, with a free id.
    /// Ids in recipe files mean the same in every mod (bare is vanilla's), so the rest is copied as it is.
    /// </summary>
    public static RecipeDefinition CopyAsNew(RecipeEntry source, string modDirectory) =>
        source.File.Recipe with { Id = FreeId(modDirectory, source.File.Recipe.Id ?? "recipe") };
}
