using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Items;
using Mechanica.Core.ModTools;
using Mechanica.Game.Mods;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Editor;

/// <summary>
/// The Recipe Editor, a mod tool on the main menu: every recipe of every installed mod in one list, which one wins
/// in load order, and editing for one mod at a time. The mod being edited makes new recipes, copies another mod's
/// recipe as a new one, overrides another mod's recipe (<c>overrides/&lt;owner&gt;/recipes/</c>), and saves or deletes
/// its own. A save is checked exactly as loading would; problems show under the fields. Vanilla's recipes are
/// authored through the Part Editor batches, so vanilla is not offered as the mod to edit. File and data logic lives
/// in Core (<see cref="RecipeCatalog"/>, <see cref="RecipeExporter"/>).
/// </summary>
public partial class RecipeEditorScreen : Control
{
    private OptionButton _target = null!;
    private LineEdit _filter = null!;
    private Tree _list = null!;
    private Label _heading = null!;
    private PropertyGrid _grid = null!;
    private VBoxContainer _fields = null!;
    private VBoxContainer _inputs = null!;
    private VBoxContainer _outputs = null!;
    private RichTextLabel _view = null!;
    private RichTextLabel _problems = null!;
    private Button _new = null!;
    private Button _override = null!;
    private Button _copy = null!;
    private Button _save = null!;
    private Button _delete = null!;
    private List<string> _targets = [];
    private List<RecipeEntry> _shown = [];
    private IReadOnlyDictionary<string, RecipeEntry> _effective = new Dictionary<string, RecipeEntry>();
    private string? _file;
    private string? _overrideOf;
    // True while the list is redrawn or its highlight moved by code, so its selection signal is not a pick.
    private bool _building;

    /// <summary>Mods folder. Set before the node enters the tree.</summary>
    public string ModsRoot { get; set; } = ModPaths.ModsRoot;

    /// <summary>Profile that gives the load order. Set before the node enters the tree.</summary>
    public string ProfilePath { get; set; } = ModPaths.ProfilePath;

    /// <summary>The screen's window: its X and Esc close it (asking first about unsaved changes).</summary>
    public ToolWindow Window { get; private set; } = null!;

    /// <summary>The window was closed.</summary>
    public event Action? CloseRequested;

    /// <summary>Every recipe file of the installed mods, in load order.</summary>
    public IReadOnlyList<RecipeEntry> Entries { get; private set; } = [];

    /// <summary>The entries the filter lets through, in list order.</summary>
    public IReadOnlyList<RecipeEntry> Shown => _shown;

    /// <summary>The mod being edited, or null when there is none (only vanilla is installed).</summary>
    public string? TargetMod { get; private set; }

    /// <summary>The entry picked in the list, or null.</summary>
    public RecipeEntry? Selected { get; private set; }

    /// <summary>The recipe being edited (one of <see cref="TargetMod"/>'s, or a new one), or null.</summary>
    public RecipeDefinition? Current { get; private set; }

    /// <summary>The file <see cref="Current"/> was opened from, or null for a recipe not saved yet.</summary>
    public string? CurrentFile => _file;

    /// <summary>The mod whose recipe <see cref="Current"/> overrides, or null for one of the mod's own.</summary>
    public string? OverrideOfCurrent => _overrideOf;

    /// <summary>True when <see cref="Current"/> has changes that are not saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Text of the problems panel, for tests.</summary>
    public string ProblemsText => _problems.GetParsedText();

    private string TargetDir => Path.Combine(ModsRoot, TargetMod!);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Window = new ToolWindow { Name = "Window", Title = "Recipe Editor" };
        Window.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Window.CloseGuard = then => ConfirmDialog.Discard(this, IsDirty, then);
        Window.CloseRequested += () => CloseRequested?.Invoke();
        AddChild(Window);
        var column = Window.Body;
        column.AddChild(new Label
        {
            Text = "Every recipe of every installed mod. Pick the mod to edit: it can add recipes, copy one as its own, or override another mod's recipe so its version wins when it loads later. Make a mod in the Mod Manager first.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var top = new HBoxContainer();
        column.AddChild(top);
        top.AddChild(new Label { Text = "Mod to edit" });
        _target = Loc.Verbatim(new OptionButton { CustomMinimumSize = new Vector2(220, 0), TooltipText = "The mod new recipes, copies and overrides are saved into" });
        _target.ItemSelected += i => ConfirmDialog.Discard(this, IsDirty, () => SetTarget(_targets[(int)i]), () => _target.Select(_targets.IndexOf(TargetMod ?? "")));
        top.AddChild(_target);
        _filter = new LineEdit { PlaceholderText = "Filter by recipe, item, station or mod", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClearButtonEnabled = true };
        _filter.TextChanged += _ => Rebuild();
        top.AddChild(_filter);

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(split);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3 };
        split.AddChild(left);
        _list = Loc.Verbatim(new Tree
        {
            Columns = 3,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        });
        _list.SetColumnTitle(0, Loc.T("Recipe"));
        _list.SetColumnTitle(1, Loc.T("Makes"));
        _list.SetColumnTitle(2, Loc.T("From"));
        _list.ItemSelected += () =>
        {
            if (_building)
                return;
            var index = (int)(_list.GetSelected()?.GetMetadata(0).AsInt32() ?? -1);
            ConfirmDialog.Discard(this, IsDirty, () => Select(index), ShowSelection);
        };
        left.AddChild(_list);
        var buttons = new HFlowContainer();
        left.AddChild(buttons);
        _new = AddButton(buttons, "New recipe", () => ConfirmDialog.Discard(this, IsDirty, () => NewRecipe()), "Start a recipe in the mod being edited");
        _copy = AddButton(buttons, "Copy to mod", () => ConfirmDialog.Discard(this, IsDirty, () => CopySelected()), "Start a new recipe in the mod being edited from the picked one");
        _override = AddButton(buttons, "Override", () => ConfirmDialog.Discard(this, IsDirty, () => OverrideSelected()), "Replace the picked recipe with the mod being edited's own version");

        var right = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        split.AddChild(right);
        var detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddChild(detail);
        _heading = Loc.Verbatim(new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart });
        detail.AddChild(_heading);
        _view = Loc.Verbatim(new RichTextLabel { BbcodeEnabled = true, FitContent = true, SelectionEnabled = true });
        detail.AddChild(_view);
        _fields = new VBoxContainer();
        detail.AddChild(_fields);
        _grid = new PropertyGrid();
        _fields.AddChild(_grid);
        _fields.AddChild(new Label { Text = "Inputs" });
        _inputs = new VBoxContainer();
        _fields.AddChild(_inputs);
        AddButton(_fields, "+ Input", () => Edit(r => r with { Inputs = [.. r.Inputs ?? [], new RecipeItem { Item = "", Count = 1 }] }, rebuild: true), "Add an item the recipe uses up");
        _fields.AddChild(new Label { Text = "Outputs" });
        _outputs = new VBoxContainer();
        _fields.AddChild(_outputs);
        AddButton(_fields, "+ Output", () => Edit(r => r with { Outputs = [.. r.Outputs ?? [], new RecipeItem { Item = "", Count = 1 }] }, rebuild: true), "Add an item the recipe makes");
        detail.AddChild(new Label { Text = "Problems" });
        _problems = Loc.Verbatim(new RichTextLabel { BbcodeEnabled = true, FitContent = true, SelectionEnabled = true });
        detail.AddChild(_problems);

        _delete = Window.AddAction("Delete", () => AskDelete(), tooltip: "Delete this recipe's file from the mod");
        _save = Window.AddAction("Save recipe", () => Save(), primary: true, tooltip: "Check the recipe and write it into the mod");
        Reload();
    }

    /// <summary>Reads the installed mods and every recipe file again, keeping the mod being edited when it is still there.</summary>
    public void Reload()
    {
        var mods = new ModWorkspace(ModsRoot, ProfilePath).List().Mods;
        _targets = [.. mods.Where(m => m.Id != Identifiers.VanillaModId).Select(m => m.Id)];
        _target.Clear();
        foreach (var mod in mods.Where(m => m.Id != Identifiers.VanillaModId))
            _target.AddItem(mod.Name == mod.Id ? mod.Id : $"{mod.Name} ({mod.Id})");
        _target.Disabled = _targets.Count == 0;
        if (_targets.Count == 0)
            _target.AddItem(Loc.T("No mod to edit yet"));
        TargetMod = TargetMod is { } kept && _targets.Contains(kept) ? kept : _targets.FirstOrDefault();
        if (TargetMod is not null)
            _target.Select(_targets.IndexOf(TargetMod));
        var diagnostics = new List<DataDiagnostic>();
        Entries = RecipeCatalog.ReadAll(ModsRoot, mods.Select(m => m.Id), diagnostics);
        _effective = RecipeCatalog.Effective(Entries);
        _problems.Text = diagnostics.Count == 0 ? "" : string.Join("\n", diagnostics.Select(d => d.ToString()));
        Selected = null;
        SetCurrent(null, null, null);
    }

    /// <summary>Makes <paramref name="mod"/> the mod being edited. False when it is not one of the installed mods (or is vanilla).</summary>
    public bool SetTarget(string mod)
    {
        if (!_targets.Contains(mod))
            return false;
        TargetMod = mod;
        _target.Select(_targets.IndexOf(mod));
        if (Selected is { } picked)
            Select(_shown.IndexOf(picked));
        else
            SetCurrent(null, null, null);
        return true;
    }

    /// <summary>Shows only the recipes whose id, items, stations or mod contain <paramref name="text"/> (any case).</summary>
    public void Filter(string text)
    {
        _filter.Text = text;
        Rebuild();
    }

    /// <summary>
    /// Picks the entry at <paramref name="index"/> in <see cref="Shown"/>. One of the mod being edited's own files opens
    /// for editing; another mod's shows read-only, ready to copy or override.
    /// </summary>
    public void Select(int index)
    {
        if (index < 0 || index >= _shown.Count)
            return;
        var entry = _shown[index];
        Selected = entry;
        if (entry.Mod == TargetMod)
            SetCurrent(entry.File.Recipe, entry.File.RelativeFile, entry.OverrideOf);
        else
            SetCurrent(null, null, null);
    }

    /// <summary>Picks the entry of mod <paramref name="mod"/> saved in <paramref name="relativeFile"/>, clearing the filter if it hides it.</summary>
    public bool Select(string mod, string relativeFile)
    {
        if (!_shown.Any(e => e.Mod == mod && e.File.RelativeFile == relativeFile))
            Filter("");
        var index = _shown.FindIndex(e => e.Mod == mod && e.File.RelativeFile == relativeFile);
        Select(index);
        return index >= 0;
    }

    /// <summary>Starts a new recipe in the mod being edited, making the picked recipe's first output when one is picked. Returns an error, or null.</summary>
    public string? NewRecipe()
    {
        if (TargetMod is null)
            return Loc.T("Make a mod in the Mod Manager first.");
        var output = Selected?.File.Recipe.Outputs?.FirstOrDefault()?.Item;
        SetCurrent(RecipeCatalog.NewRecipe(TargetDir, TargetMod, output is null ? null : Identifiers.Qualify(output)), null, null);
        IsDirty = true;
        return null;
    }

    /// <summary>Starts a new recipe in the mod being edited as a copy of the picked one. Returns an error, or null.</summary>
    public string? CopySelected()
    {
        if (TargetMod is null)
            return Loc.T("Make a mod in the Mod Manager first.");
        if (Selected is not { } source)
            return Loc.T("Pick a recipe to copy.");
        SetCurrent(RecipeCatalog.CopyAsNew(source, TargetDir), null, null);
        IsDirty = true;
        return null;
    }

    /// <summary>
    /// Starts the mod being edited's override of the picked recipe (or opens the one it already has). Returns an error, or null.
    /// </summary>
    public string? OverrideSelected()
    {
        if (TargetMod is null)
            return Loc.T("Make a mod in the Mod Manager first.");
        if (Selected is not { } source)
            return Loc.T("Pick a recipe to override.");
        if (source.Owner == TargetMod)
            return Loc.T("{0} is this mod's own recipe; edit it instead.", source.QualifiedId);
        if (Entries.FirstOrDefault(e => e.Mod == TargetMod && e.OverrideOf == source.Owner && e.File.Recipe.Id == source.File.Recipe.Id) is { } existing)
        {
            Select(existing.Mod, existing.File.RelativeFile);
            return null;
        }
        SetCurrent(source.File.Recipe, null, source.Owner);
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
            ShowCurrent();
    }

    /// <summary>Checks and writes the recipe into the mod being edited; problems show under the fields.</summary>
    public ExportResult? Save()
    {
        if (Current is null || TargetMod is null)
            return null;
        var clean = RecipeFields.Clean(Current);
        var result = RecipeExporter.Export(TargetDir, clean, ModLoader.LoadAll(ModsRoot), null, _file, _overrideOf);
        var saved = RecipeExporter.TargetFile(clean.Id ?? "", _overrideOf);
        var lines = result.Diagnostics.Select(d => d.ToString()).ToList();
        lines.Insert(0, result.Succeeded ? Loc.T("Saved recipe {0} to {1}.", result.QualifiedId, saved) : Loc.T("The recipe was not saved."));
        if (result.Succeeded)
        {
            var mod = TargetMod;
            Reload();
            Select(mod, saved);
        }
        _problems.Text = string.Join("\n", lines);
        return result;
    }

    /// <summary>Deletes the recipe being edited from the mod being edited. False when it was never saved.</summary>
    public bool DeleteCurrent()
    {
        if (_file is null || TargetMod is null)
            return false;
        var deleted = RecipeExporter.Delete(TargetDir, _file);
        Reload();
        return deleted;
    }

    /// <summary>Asks, then deletes the recipe being edited. Null when it was never saved.</summary>
    public ConfirmDialog? AskDelete() => _file is null ? null
        : ConfirmDialog.Ask(this, "Delete recipe", Loc.T("Delete {0}? This removes the file from the mod.", _file), "Delete", () => DeleteCurrent());

    private void SetCurrent(RecipeDefinition? recipe, string? file, string? overrideOf)
    {
        Current = recipe;
        _file = file;
        _overrideOf = overrideOf;
        IsDirty = false;
        Rebuild();
    }

    // The list, filtered, then the picked entry.
    private void Rebuild()
    {
        if (_list is null)
            return;
        var filter = _filter.Text.Trim();
        _shown = [.. Entries.Where(e => filter.Length == 0 || Matches(e, filter))];
        _building = true;
        _list.Clear();
        var root = _list.CreateItem();
        for (var i = 0; i < _shown.Count; i++)
        {
            var e = _shown[i];
            var row = _list.CreateItem(root);
            row.SetMetadata(0, i);
            row.SetText(0, e.OverrideOf is null ? e.QualifiedId : Loc.T("{0} (override)", e.QualifiedId));
            row.SetText(1, string.Join(", ", (e.File.Recipe.Outputs ?? []).Select(o => $"{o.Count ?? 1}× {o.Item}")));
            var winner = _effective.GetValueOrDefault(e.QualifiedId);
            row.SetText(2, winner is null ? Loc.T("{0} (overrides nothing)", e.Mod)
                : winner == e ? e.Mod
                : Loc.T("{0} (replaced by {1})", e.Mod, winner.Mod));
            row.SetTooltipText(0, $"{e.Mod}/{e.File.RelativeFile}");
            if (e == Selected)
                row.Select(0);
        }
        _building = false;
        ShowCurrent();
    }

    private static bool Matches(RecipeEntry e, string filter)
    {
        bool Has(string? s) => s is not null && s.Contains(filter, StringComparison.OrdinalIgnoreCase);
        var r = e.File.Recipe;
        return Has(e.QualifiedId) || Has(e.Mod) || Has(r.Name) || (r.Stations ?? []).Any(Has)
            || (r.Inputs ?? []).Any(i => Has(i.Item)) || (r.Outputs ?? []).Any(o => Has(o.Item));
    }

    // The fields for the recipe being edited, or a read-only summary of another mod's.
    private void ShowCurrent()
    {
        _grid.Clear();
        foreach (var child in _inputs.GetChildren().Concat(_outputs.GetChildren()))
            child.QueueFree();
        var editing = Current is not null;
        _fields.Visible = editing;
        _view.Visible = !editing && Selected is not null;
        _save.Disabled = !editing || TargetMod is null;
        _delete.Disabled = _file is null;
        _new.Disabled = TargetMod is null;
        _copy.Disabled = TargetMod is null || Selected is null;
        _override.Disabled = TargetMod is null || Selected is null || Selected.Owner == TargetMod;
        if (Current is { } r)
        {
            _heading.Text = _file is not null ? $"{TargetMod}/{_file}"
                : _overrideOf is not null ? Loc.T("New override of {0}:{1} in {2} (not saved)", _overrideOf, r.Id, TargetMod)
                : Loc.T("New recipe in {0} (not saved)", TargetMod);
            RecipeFields.Add(_grid, _inputs, _outputs, r, (change, rebuild) => Edit(change, rebuild));
            return;
        }
        if (Selected is not { } e)
        {
            _heading.Text = Loc.T("Pick a recipe, or start a new one.");
            return;
        }
        _heading.Text = $"{e.Mod}/{e.File.RelativeFile}";
        var s = e.File.Recipe;
        static string Items(IEnumerable<RecipeItem>? items) => string.Join(", ", (items ?? []).Select(i => $"{i.Count ?? 1}× {i.Item}"));
        _view.Text = string.Join("\n",
            Loc.T("[b]{0}[/b] at {1}, {2} s", e.QualifiedId, string.Join(", ", s.Stations ?? []), s.Time ?? 0),
            Loc.T("Uses: {0}", Items(s.Inputs)),
            Loc.T("Makes: {0}", Items(s.Outputs)),
            TargetMod is null ? "" : Loc.T("Copy it to {0} as a new recipe, or override it there.", TargetMod));
    }

    // Puts the list's highlight back on the picked entry (after a switch was cancelled).
    private void ShowSelection()
    {
        var index = Selected is null ? -1 : _shown.IndexOf(Selected);
        _building = true;
        if (index >= 0)
            _list.GetRoot()?.GetChild(index)?.Select(0);
        else
            _list.DeselectAll();
        _building = false;
    }

    private static Button AddButton(Container parent, string text, Action pressed, string tooltip)
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }
}
