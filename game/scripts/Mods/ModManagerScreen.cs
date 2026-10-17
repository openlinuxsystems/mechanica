using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Ui;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Mods;

/// <summary>
/// The in-game Mod Manager (D9): lists installed mods in load order with a checkbox each, moves them
/// up and down, shows each mod's details, and lists every error, warning and conflict the current
/// selection would load with. Apply saves the profile; the game reads it the next time content loads.
/// All logic is in <see cref="ModPackageManager"/>; this node only draws it.
/// </summary>
public partial class ModManagerScreen : Control
{
    private readonly List<DataDiagnostic> _profileDiagnostics = [];
    private string _savedProfile = "";
    private Tree _list = null!;
    private RichTextLabel _details = null!;
    private RichTextLabel _problems = null!;
    private Button _up = null!;
    private Button _down = null!;
    private Button _apply = null!;
    private Button _revert = null!;

    /// <summary>Mods folder to manage. Set before the node enters the tree.</summary>
    public string ModsRoot { get; set; } = ModPaths.ModsRoot;

    /// <summary>Profile file to read and save. Set before the node enters the tree.</summary>
    public string ProfilePath { get; set; } = ModPaths.ProfilePath;

    /// <summary>The current, possibly unsaved, setup.</summary>
    public ModPackageManager Manager { get; private set; } = null!;

    /// <summary>What the current setup loads: mods, parts, conflicts and diagnostics.</summary>
    public ModLoadResult Preview { get; private set; } = null!;

    /// <summary>The selected mod's id, or null.</summary>
    public string? SelectedId => _list.GetSelected()?.GetMetadata(0).AsString();

    /// <summary>True when the setup differs from the saved profile.</summary>
    public bool HasUnsavedChanges => DataJson.Write(Manager.ToProfile()) != _savedProfile;

    /// <summary>Text of the problems panel, for tests.</summary>
    public string ProblemsText => _problems.GetParsedText();

    /// <summary>Row texts in display order (name, version, status), for tests.</summary>
    public IEnumerable<(string Id, bool Enabled, string Status)> Rows => _list.GetRoot()?.GetChildren()
        .Select(i => (i.GetMetadata(0).AsString(), i.IsChecked(0), i.GetText(2))) ?? [];

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        AddChild(margin);

        var column = new VBoxContainer();
        margin.AddChild(column);
        column.AddChild(new Label { Text = "Mods", ThemeTypeVariation = "HeaderLarge" });
        column.AddChild(new Label
        {
            Text = "Mods load top to bottom; a later mod's overrides win. Changes apply the next time you start or host a game. "
                + "Players who join need the same mods in the same order.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(split);

        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2 };
        split.AddChild(left);
        _list = new Tree
        {
            Columns = 3,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _list.SetColumnTitle(0, "Mod");
        _list.SetColumnTitle(1, "Version");
        _list.SetColumnTitle(2, "Status");
        _list.SetColumnExpand(1, false);
        _list.SetColumnCustomMinimumWidth(1, 80);
        _list.ItemEdited += OnItemEdited;
        _list.ItemSelected += UpdateDetails;
        left.AddChild(_list);

        var rowButtons = new HBoxContainer();
        left.AddChild(rowButtons);
        _up = AddButton(rowButtons, "Move up", () => MoveSelected(-1));
        _down = AddButton(rowButtons, "Move down", () => MoveSelected(1));
        AddButton(rowButtons, "Rescan", Reopen);
        AddButton(rowButtons, "Open mods folder", () => OS.ShellOpen(ModsRoot));

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(right);
        _details = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SelectionEnabled = true };
        right.AddChild(_details);
        right.AddChild(new Label { Text = "Problems" });
        _problems = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill, SelectionEnabled = true };
        right.AddChild(_problems);

        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        column.AddChild(bottom);
        _revert = AddButton(bottom, "Revert", Reopen);
        _apply = AddButton(bottom, "Apply", Apply);

        Reopen();
    }

    /// <summary>Rescans the mods folder and reloads the saved profile, dropping unsaved changes.</summary>
    public void Reopen()
    {
        _profileDiagnostics.Clear();
        var profile = ModProfile.Load(ProfilePath, _profileDiagnostics);
        Manager = ModPackageManager.Open(ModsRoot, profile);
        _savedProfile = DataJson.Write(Manager.ToProfile());
        Refresh(SelectedId);
    }

    /// <summary>Enables or disables a mod (with its dependencies or dependents) and refreshes.</summary>
    public IReadOnlyList<string> SetModEnabled(string id, bool enabled)
    {
        var changed = Manager.SetEnabled(id, enabled);
        Refresh(id);
        return changed;
    }

    /// <summary>Moves a mod by <paramref name="delta"/> rows, as far as its dependencies allow.</summary>
    public int MoveMod(string id, int delta)
    {
        var index = Manager.Mods.Select(m => m.Id).ToList().IndexOf(id);
        var moved = Manager.Move(id, index + delta);
        Refresh(id);
        return moved;
    }

    /// <summary>Saves the setup to <see cref="ProfilePath"/>.</summary>
    public void Apply()
    {
        var profile = Manager.ToProfile();
        profile.Save(ProfilePath);
        _savedProfile = DataJson.Write(profile);
        Refresh(SelectedId);
    }

    /// <summary>Selects a mod's row.</summary>
    public void Select(string id)
    {
        var item = _list.GetRoot()?.GetChildren().FirstOrDefault(i => i.GetMetadata(0).AsString() == id);
        if (item is not null)
        {
            item.Select(0);
            UpdateDetails();
        }
    }

    private void Refresh(string? select)
    {
        Preview = Manager.Load();
        _list.Clear();
        var root = _list.CreateItem();
        var loaded = Preview.Mods.ToDictionary(m => m.Id);
        foreach (var mod in Manager.Mods)
        {
            var item = _list.CreateItem(root);
            item.SetCellMode(0, TreeItem.TreeCellMode.Check);
            item.SetChecked(0, Manager.IsEnabled(mod.Id));
            item.SetEditable(0, ModPackageManager.CanDisable(mod.Id));
            item.SetText(0, mod.Manifest.Name ?? mod.Id);
            item.SetTooltipText(0, mod.Id);
            item.SetMetadata(0, mod.Id);
            item.SetText(1, mod.Version.ToString());
            var (status, color) = Status(mod, loaded.GetValueOrDefault(mod.Id));
            item.SetText(2, status);
            item.SetCustomColor(2, color);
        }
        _problems.Text = ProblemsBbcode();
        _apply.Disabled = _revert.Disabled = !HasUnsavedChanges;
        if (select is not null)
            Select(select);
        UpdateDetails();
    }

    private (string Text, Color Color) Status(InstalledMod mod, LoadedMod? loaded)
    {
        if (!Manager.IsEnabled(mod.Id))
            return ("Disabled", UiTokens.Disabled);
        if (loaded is null)
        {
            var reason = DiagnosticsFor(mod.Id).FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error)?.Message;
            return ($"Not loaded{(reason is null ? "" : $": {reason}")}", UiTokens.Error);
        }
        var lost = Preview.Conflicts.Count(c => c.ModId == mod.Id);
        var status = loaded.Overrides.Count > 0 ? $"Loaded, overrides {loaded.Overrides.Count} part(s)" : "Loaded";
        return lost > 0 ? ($"{status}; {lost} override(s) replaced by later mods", UiTokens.Warning) : (status, UiTokens.Success);
    }

    private IEnumerable<DataDiagnostic> DiagnosticsFor(string id)
    {
        var prefix = $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(ModsRoot))}/{id}/";
        return Preview.Diagnostics.Where(d => d.File.StartsWith(prefix, StringComparison.Ordinal));
    }

    private string ProblemsBbcode()
    {
        var lines = new List<string>();
        foreach (var d in _profileDiagnostics.Concat(Preview.Diagnostics))
        {
            var color = d.Severity == DiagnosticSeverity.Error ? UiPalette.Error : UiPalette.Warning;
            lines.Add($"[color={color}]{Escape(d.ToString())}[/color]");
        }
        foreach (var id in Manager.MissingMods)
            lines.Add($"[color={UiPalette.TextSecondary}]{Escape($"'{id}' is in your mod list but not installed; it keeps its place if you reinstall it.")}[/color]");
        return lines.Count == 0 ? "No problems." : string.Join("\n", lines);
    }

    private void UpdateDetails()
    {
        var id = SelectedId;
        var mod = id is null ? null : Manager.Mods.FirstOrDefault(m => m.Id == id);
        _up.Disabled = _down.Disabled = mod is null;
        if (mod is null)
        {
            _details.Text = "Select a mod to see its details.";
            return;
        }
        var m = mod.Manifest;
        var text = $"[b]{Escape(m.Name ?? mod.Id)}[/b] {mod.Version}  [color={UiPalette.TextSecondary}]{mod.Id}[/color]\n";
        if (m.Authors is { Count: > 0 })
            text += $"By {Escape(string.Join(", ", m.Authors))}\n";
        if (m.Description is not null)
            text += $"{Escape(m.Description)}\n";
        text += $"Needs: {References(m.Dependencies)}\nConflicts with: {References(m.Conflicts)}\n";
        if (Preview.Mods.FirstOrDefault(l => l.Id == mod.Id) is { } loaded)
            text += $"{loaded.Parts.Count} part(s), {loaded.Overrides.Count} override(s)\n";
        text += $"[color={UiPalette.TextSecondary}]{Escape(mod.Directory)}[/color]";
        _details.Text = text;
    }

    private static string References(List<ModDependency>? list) => list is { Count: > 0 }
        ? Escape(string.Join(", ", list.Select(d => d.Version is null ? d.Id : $"{d.Id} {d.Version}")))
        : "nothing";

    private void OnItemEdited()
    {
        var item = _list.GetEdited();
        if (item is not null)
            SetModEnabled(item.GetMetadata(0).AsString(), item.IsChecked(0));
    }

    private void MoveSelected(int delta)
    {
        if (SelectedId is { } id)
            MoveMod(id, delta);
    }

    private static Button AddButton(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
