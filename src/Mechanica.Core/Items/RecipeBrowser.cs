using Mechanica.Core.Data;

namespace Mechanica.Core.Items;

/// <summary>One input of a recipe preview: how many a craft needs and how many there are to take from.</summary>
/// <param name="Item">Qualified item id.</param>
/// <param name="Need">How many the order takes.</param>
/// <param name="Have">How many the player and the linked storage hold.</param>
public sealed record RecipeInput(string Item, int Need, int Have)
{
    /// <summary>True when there are not enough (the Fabricator screen shows it red).</summary>
    public bool Missing => Have < Need;
}

/// <summary>
/// The Fabricator screen's recipe list and preview (UI spec §4, §8), kept out of the UI so it can be tested: search and
/// category filtering, what an order needs against what there is, and how long the queue has left.
/// </summary>
public static class RecipeBrowser
{
    /// <summary>
    /// The recipes whose main output is under <paramref name="tab"/> and whose name or output names contain every word
    /// of <paramref name="query"/> (any case), by name.
    /// </summary>
    public static IReadOnlyList<Recipe> Filter(IEnumerable<Recipe> recipes, string? query, InventoryTab tab, IPartCatalog catalog)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return [.. recipes
            .Where(r => tab == InventoryTab.All || r.Outputs.Count > 0 && InventoryTabs.Of(catalog.FindDefinition(r.Outputs[0].Item)?.Category) == tab)
            .Where(r => words.All(w => Text(r, catalog).Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.QualifiedId, StringComparer.Ordinal)];
    }

    /// <summary>What <paramref name="count"/> crafts of <paramref name="recipe"/> need, against <paramref name="have"/> (item to count).</summary>
    public static IReadOnlyList<RecipeInput> Inputs(Recipe recipe, int count, Func<string, int> have) =>
        [.. recipe.InputsFor(count).Select(i => new RecipeInput(i.Item, i.Count, have(i.Item)))];

    /// <summary>Seconds an order has left at <paramref name="speed"/>: the rest of the current craft and every one after it.</summary>
    public static float Remaining(float time, float progress, int count, float speed) =>
        speed > 0 ? Math.Max(0, time * count - progress) / speed : float.PositiveInfinity;

    /// <summary>Seconds the whole queue has left at <paramref name="speed"/>.</summary>
    public static float Remaining(IEnumerable<CraftJobView> queue, float speed) =>
        queue.Sum(j => Remaining(j.Time, j.Progress, j.Count, speed));

    // What a search looks through: the recipe's name and its outputs' names and ids.
    private static string Text(Recipe recipe, IPartCatalog catalog) =>
        string.Join(' ', recipe.Outputs.SelectMany(o => new[] { catalog.FindDefinition(o.Item)?.Name ?? "", o.Item }).Prepend(recipe.Name));
}
