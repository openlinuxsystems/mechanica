using Godot;
using Mechanica.Core.Items;

namespace Mechanica.Game.Editor;

/// <summary>
/// The fields of one recipe (id, name, stations, time, order, inputs and outputs), shared by the Part Editor's
/// Recipes tab and the Recipe Editor. <c>edit</c> applies a change; its flag says whether the rows must be redrawn.
/// </summary>
public static class RecipeFields
{
    /// <summary>Fills <paramref name="grid"/>, <paramref name="inputs"/> and <paramref name="outputs"/> with editors for <paramref name="r"/>.</summary>
    public static void Add(PropertyGrid grid, VBoxContainer inputs, VBoxContainer outputs, RecipeDefinition r,
        Action<Func<RecipeDefinition, RecipeDefinition>, bool> edit)
    {
        grid.AddText("Id", r.Id, v => edit(x => x with { Id = v }, false), "snake_case");
        grid.AddText("Name", r.Name, v => edit(x => x with { Name = v }, false), "the output's name");
        grid.AddText("Stations", string.Join(", ", r.Stations ?? []), v => edit(x => x with
        {
            Stations = [.. (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
        }, false), "fabricator, portable_fabricator");
        grid.AddNumber("Time", r.Time, 0, 3600, 0.5, v => edit(x => x with { Time = (float?)v }, false), suffix: "s");
        grid.AddNumber("Order", r.Order, 0, 99999, 1, v => edit(x => x with { Order = v is { } o ? (int)o : null }, false), optional: true);
        AddItemRows(inputs, r.Inputs ?? [], list => edit(x => x with { Inputs = list }, true), list => edit(x => x with { Inputs = list }, false));
        AddItemRows(outputs, r.Outputs ?? [], list => edit(x => x with { Outputs = list }, true), list => edit(x => x with { Outputs = list }, false));
    }

    /// <summary>The recipe as it is saved: no blank name, and no rows without an item.</summary>
    public static RecipeDefinition Clean(RecipeDefinition r) => r with
    {
        Name = string.IsNullOrWhiteSpace(r.Name) ? null : r.Name,
        Inputs = r.Inputs?.Where(i => !string.IsNullOrWhiteSpace(i.Item)).ToList(),
        Outputs = r.Outputs?.Where(i => !string.IsNullOrWhiteSpace(i.Item)).ToList(),
    };

    // One row per item: id, count and a remove button. Removing redraws; typing does not.
    private static void AddItemRows(VBoxContainer box, List<RecipeItem> items, Action<List<RecipeItem>> replace, Action<List<RecipeItem>> update)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var row = new HBoxContainer();
            var item = new LineEdit { Text = items[i].Item ?? "", PlaceholderText = "item id", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            item.TextChanged += text => update([.. items.Select((x, j) => j == index ? x with { Item = text.Trim() } : x)]);
            row.AddChild(item);
            var count = new SpinBox { MinValue = 1, MaxValue = 9999, Value = items[i].Count ?? 1 };
            count.ValueChanged += v => update([.. items.Select((x, j) => j == index ? x with { Count = (int)v } : x)]);
            row.AddChild(count);
            var remove = new Button { Text = "×", TooltipText = "Remove" };
            remove.Pressed += () => replace([.. items.Where((_, j) => j != index)]);
            row.AddChild(remove);
            box.AddChild(row);
        }
    }
}
