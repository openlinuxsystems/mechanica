using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Items;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Editor;

/// <summary>
/// The Part Editor's Recipes tab (chunk 18): the recipes in the open part's mod that make the part. Pick
/// one or start a new one, edit its stations, inputs, outputs and time, and save it into the mod's
/// <c>recipes/</c> folder. A save is checked exactly as loading would (against the enabled mods plus the
/// part being edited), and problems show in the editor's problems pane. All file and data logic lives in
/// Core (<see cref="RecipeExporter"/>, <see cref="RecipeBook"/>).
/// </summary>
public partial class RecipePanel : VBoxContainer
{
    private ItemList _list = null!;
    private PropertyGrid _grid = null!;
    private VBoxContainer _inputs = null!;
    private VBoxContainer _outputs = null!;
    private Button _save = null!;
    private Button _delete = null!;
    private List<RecipeFile> _recipes = [];
    private string? _file;

    /// <summary>The editor this panel belongs to. Set before adding to the tree.</summary>
    public PartEditorScreen Editor { get; init; } = null!;

    /// <summary>Recipes in the part's mod that make the part, ordered by file.</summary>
    public IReadOnlyList<RecipeFile> Recipes => _recipes;

    /// <summary>The recipe being edited, or null.</summary>
    public RecipeDefinition? Current { get; private set; }

    /// <summary>True when <see cref="Current"/> has changes that are not saved.</summary>
    public bool IsDirty { get; private set; }

    public override void _Ready()
    {
        AddChild(new Label
        {
            Text = "Recipes in this mod that make this part. Stations are parts with crafting turned on.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _list = Loc.Verbatim(new ItemList { CustomMinimumSize = new Vector2(0, 100) });
        _list.ItemSelected += i => Select((int)i);
        AddChild(_list);
        var buttons = new HFlowContainer();
        AddChild(buttons);
        AddButton(buttons, "New recipe", () => NewRecipe());
        _save = AddButton(buttons, "Save recipe", () => Save());
        _delete = AddButton(buttons, "Delete", () => DeleteCurrent());
        _grid = new PropertyGrid();
        AddChild(_grid);
        AddChild(new Label { Text = "Inputs" });
        _inputs = new VBoxContainer();
        AddChild(_inputs);
        AddButton(this, "+ Input", () => Edit(r => r with { Inputs = [.. r.Inputs ?? [], new RecipeItem { Item = "", Count = 1 }] }, rebuild: true));
        AddChild(new Label { Text = "Outputs" });
        _outputs = new VBoxContainer();
        AddChild(_outputs);
        AddButton(this, "+ Output", () => Edit(r => r with { Outputs = [.. r.Outputs ?? [], new RecipeItem { Item = "", Count = 1 }] }, rebuild: true));
        Rebuild();
    }

    /// <summary>Reads the open part's recipes from its mod again and selects the first.</summary>
    public void Reload()
    {
        var diagnostics = new List<DataDiagnostic>();
        _recipes = Editor.Document is { } doc && doc.Definition.Id is { } id
            ? [.. RecipeExporter.Making(RecipeExporter.ReadAll(doc.ModDirectory, diagnostics), doc.ModId, id)]
            : [];
        if (diagnostics.Count > 0)
            Editor.ShowProblems(diagnostics, "Some recipe files could not be read.");
        if (_recipes.Count > 0)
            Select(0);
        else
            SetCurrent(null, null);
    }

    /// <summary>Edits the recipe at <paramref name="index"/> in <see cref="Recipes"/>.</summary>
    public void Select(int index)
    {
        if (index >= 0 && index < _recipes.Count)
            SetCurrent(_recipes[index].Recipe, _recipes[index].RelativeFile);
    }

    /// <summary>
    /// Starts a new recipe that makes one of the open part, run at <paramref name="stations"/> (default: the
    /// Fabricator for vanilla parts, none otherwise). Returns an error, or null.
    /// </summary>
    public string? NewRecipe(IReadOnlyList<string>? stations = null)
    {
        if (Editor.Document is not { Definition.Id: { } id } doc)
            return "Open or create a part first.";
        var taken = new HashSet<string>(RecipeExporter.ReadAll(doc.ModDirectory, []).Select(f => f.Recipe.Id ?? ""));
        var recipeId = id;
        for (var n = 2; taken.Contains(recipeId); n++)
            recipeId = $"{id}_{n}";
        var output = doc.ModId == Identifiers.VanillaModId ? id : $"{doc.ModId}:{id}";
        stations ??= doc.ModId == Identifiers.VanillaModId ? ["fabricator"] : [];
        SetCurrent(new RecipeDefinition
        {
            Id = recipeId,
            Stations = [.. stations],
            Inputs = [],
            Outputs = [new RecipeItem { Item = output, Count = 1 }],
            Time = 1,
        }, null);
        IsDirty = true;
        return null;
    }

    /// <summary>Changes the recipe being edited. <paramref name="rebuild"/> redraws the fields.</summary>
    public void Edit(Func<RecipeDefinition, RecipeDefinition> change, bool rebuild = false)
    {
        if (Current is null)
            return;
        Current = change(Current);
        IsDirty = true;
        if (rebuild)
            Rebuild();
    }

    /// <summary>Checks and writes the recipe into the part's mod; problems go to the editor's problems pane.</summary>
    public ExportResult? Save()
    {
        if (Current is null || Editor.Document is not { } doc)
            return null;
        var catalog = ModLoader.LoadAll(Editor.ModsRoot);
        var extra = doc.Definition.Id is { } id
            ? new Dictionary<string, PartDefinition> { [$"{doc.ModId}:{id}"] = doc.Definition }
            : [];
        var clean = Current with
        {
            Name = string.IsNullOrWhiteSpace(Current.Name) ? null : Current.Name,
            Inputs = Current.Inputs?.Where(i => !string.IsNullOrWhiteSpace(i.Item)).ToList(),
            Outputs = Current.Outputs?.Where(i => !string.IsNullOrWhiteSpace(i.Item)).ToList(),
        };
        var result = RecipeExporter.Export(doc.ModDirectory, clean, catalog, extra, _file);
        Editor.ShowProblems(result.Diagnostics, result.Succeeded
            ? Loc.T("Saved recipe {0} to {1}.", result.QualifiedId, RecipeExporter.TargetFile(clean.Id!))
            : Loc.T("The recipe was not saved."));
        if (!result.Succeeded)
            return result;
        var saved = RecipeExporter.TargetFile(clean.Id!);
        Reload();
        var index = _recipes.FindIndex(f => f.RelativeFile == saved);
        if (index >= 0)
            Select(index);
        else
            SetCurrent(clean, saved); // a recipe that no longer makes this part stays open
        return result;
    }

    /// <summary>Deletes the recipe being edited from the mod. Returns false when it was never saved.</summary>
    public bool DeleteCurrent()
    {
        if (_file is null || Editor.Document is not { } doc)
            return false;
        var deleted = RecipeExporter.Delete(doc.ModDirectory, _file);
        Reload();
        return deleted;
    }

    private void SetCurrent(RecipeDefinition? recipe, string? file)
    {
        Current = recipe;
        _file = file;
        IsDirty = false;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_grid is null)
            return;
        _list.Clear();
        foreach (var f in _recipes)
            _list.AddItem($"{f.Recipe.Id} ({f.RelativeFile})");
        if (_file is not null && _recipes.FindIndex(f => f.RelativeFile == _file) is var selected and >= 0)
            _list.Select(selected);
        _grid.Clear();
        foreach (var child in _inputs.GetChildren().Concat(_outputs.GetChildren()))
            child.QueueFree();
        _save.Disabled = Current is null;
        _delete.Disabled = _file is null;
        if (Current is not { } r)
            return;
        if (_file is null)
            _grid.AddHeading("New recipe (not saved)");
        else
            Loc.Verbatim(_grid.AddHeading(_file));
        _grid.AddText("Id", r.Id, v => Edit(x => x with { Id = v }), "snake_case");
        _grid.AddText("Name", r.Name, v => Edit(x => x with { Name = v }), "the output's name");
        _grid.AddText("Stations", string.Join(", ", r.Stations ?? []), v => Edit(x => x with
        {
            Stations = [.. (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
        }), "fabricator, mini_fabricator");
        _grid.AddNumber("Time", r.Time, 0, 3600, 0.5, v => Edit(x => x with { Time = (float?)v }), suffix: "s");
        AddItemRows(_inputs, r.Inputs ?? [], list => Edit(x => x with { Inputs = list }, rebuild: true), list => Edit(x => x with { Inputs = list }));
        AddItemRows(_outputs, r.Outputs ?? [], list => Edit(x => x with { Outputs = list }, rebuild: true), list => Edit(x => x with { Outputs = list }));
    }

    // One row per item: id, count and a remove button. Removing redraws; typing does not.
    private static void AddItemRows(VBoxContainer box, List<RecipeItem> items, Action<List<RecipeItem>> replace, Action<List<RecipeItem>> update)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var row = new HBoxContainer();
            var item = new LineEdit { Text = items[i].Item ?? "", PlaceholderText = "item id", SizeFlagsHorizontal = SizeFlags.ExpandFill };
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

    private static Button AddButton(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }
}
