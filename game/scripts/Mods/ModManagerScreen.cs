using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Packages;
using Mechanica.Core.Ui;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Mods;

/// <summary>
/// The in-game Mod Manager (D9): lists installed mods in load order with a checkbox each, moves them
/// up and down, shows each mod's details, and lists every error, warning and conflict the current
/// selection would load with. Apply saves the profile; the game reads it the next time content loads.
/// Export writes the selected mod to a verified package (<c>&lt;id&gt;-&lt;version&gt;.zip</c>), Install checks and
/// installs one (confirming before it replaces a mod), and Uninstall deletes a mod after confirming.
/// All logic is in <see cref="ModPackageManager"/> and <see cref="ModPackages"/>; this node only draws it.
/// </summary>
public partial class ModManagerScreen : Control
{
    private readonly List<DataDiagnostic> _profileDiagnostics = [];
    private string _savedProfile = "";
    private Tree _list = null!;
    private RichTextLabel _details = null!;
    private RichTextLabel _problems = null!;
    private RichTextLabel _packageLog = null!;
    private Button _up = null!;
    private Button _down = null!;
    private Button _export = null!;
    private Button _uninstall = null!;
    private FileDialog _exportDialog = null!;
    private FileDialog _installDialog = null!;
    private Button _apply = null!;
    private Button _revert = null!;
    private Button _editInfo = null!;
    private ConfirmationDialog _newDialog = null!;
    private LineEdit _newId = null!;
    private LineEdit _newName = null!;
    private ConfirmationDialog _infoDialog = null!;
    private LineEdit _infoName = null!;
    private LineEdit _infoVersion = null!;
    private LineEdit _infoAuthors = null!;
    private LineEdit _infoDependencies = null!;
    private TextEdit _infoDescription = null!;
    private string? _validation;

    /// <summary>Mods folder to manage. Set before the node enters the tree.</summary>
    public string ModsRoot { get; set; } = ModPaths.ModsRoot;

    /// <summary>Profile file to read and save. Set before the node enters the tree.</summary>
    public string ProfilePath { get; set; } = ModPaths.ProfilePath;

    /// <summary>Folder Export offers first. Set before the node enters the tree.</summary>
    public string PackagesFolder { get; set; } = ModPaths.PackagesRoot;

    /// <summary>The screen's window: its X and Esc close it (asking first about an unapplied setup).</summary>
    public ToolWindow Window { get; private set; } = null!;

    /// <summary>The window was closed (X or Esc).</summary>
    public event Action? CloseRequested;

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

    /// <summary>Text of the packages panel (the last export, install or uninstall), for tests.</summary>
    public string PackageText => _packageLog.GetParsedText();

    /// <summary>The folder picker Export opens.</summary>
    public FileDialog ExportDialog => _exportDialog;

    /// <summary>The file picker Install opens.</summary>
    public FileDialog InstallDialog => _installDialog;

    /// <summary>Row texts in display order (name, version, status), for tests.</summary>
    public IEnumerable<(string Id, bool Enabled, string Status)> Rows => _list.GetRoot()?.GetChildren()
        .Select(i => (i.GetMetadata(0).AsString(), i.IsChecked(0), i.GetText(2))) ?? [];

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Window = new ToolWindow { Name = "Window", Title = "Mod Manager" };
        Window.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Window.CloseGuard = Discard;
        Window.CloseRequested += () => CloseRequested?.Invoke();
        AddChild(Window);
        var column = Window.Body;
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
        // Mod names show as written; the titles and statuses are translated here.
        _list = Loc.Verbatim(new Tree
        {
            Columns = 3,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        });
        _list.SetColumnTitle(0, Loc.T("Mod"));
        _list.SetColumnTitle(1, Loc.T("Version"));
        _list.SetColumnTitle(2, Loc.T("Status"));
        _list.SetColumnExpand(1, false);
        _list.SetColumnCustomMinimumWidth(1, 80);
        _list.ItemEdited += OnItemEdited;
        _list.ItemSelected += UpdateDetails;
        left.AddChild(_list);

        var rowButtons = new HBoxContainer();
        left.AddChild(rowButtons);
        _up = AddButton(rowButtons, "Move up", () => MoveSelected(-1), "Load the selected mod earlier");
        _down = AddButton(rowButtons, "Move down", () => MoveSelected(1), "Load the selected mod later, so its overrides win");
        AddButton(rowButtons, "Rescan", () => Discard(Reopen), "Look for new and changed mods in the mods folder");
        AddButton(rowButtons, "Open mods folder", () => OS.ShellOpen(ModsRoot), "Show the mods folder in your file manager");

        var packageButtons = new HBoxContainer();
        left.AddChild(packageButtons);
        _export = AddButton(packageButtons, "Export…", OpenExportDialog, "Check the selected mod and save it as a package to share");
        AddButton(packageButtons, "Install…", OpenInstallDialog, "Install a mod package (.zip)");
        _uninstall = AddButton(packageButtons, "Uninstall", () => { if (SelectedId is { } id) UninstallMod(id); }, "Delete the selected mod from the mods folder");

        var makeButtons = new HBoxContainer();
        left.AddChild(makeButtons);
        AddButton(makeButtons, "New mod…", OpenNewDialog, "Make an empty mod to fill with the Part Editor, Lua and data files");
        _editInfo = AddButton(makeButtons, "Edit info…", OpenInfoDialog, "Change the selected mod's name, version, description, authors and dependencies");
        AddButton(makeButtons, "Check all content", () => ValidateAll(), "Load every kind of content of this setup the way a world starts, compile every script, and list the problems");

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(right);
        _details = Loc.Verbatim(new RichTextLabel { BbcodeEnabled = true, FitContent = true, SelectionEnabled = true });
        right.AddChild(_details);
        right.AddChild(new Label { Text = "Problems" });
        _problems = Loc.Verbatim(new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill, SelectionEnabled = true });
        right.AddChild(_problems);
        right.AddChild(new Label { Text = "Packages" });
        _packageLog = Loc.Verbatim(new RichTextLabel { BbcodeEnabled = true, FitContent = true, SelectionEnabled = true });
        _packageLog.Text = Loc.T("Export a mod to share it, or install a package someone shared with you.");
        right.AddChild(_packageLog);
        BuildFileDialogs();
        BuildModDialogs();

        _revert = Window.AddAction("Revert", () => Discard(Reopen), tooltip: "Go back to the saved setup");
        _apply = Window.AddAction("Apply", Apply, primary: true, tooltip: "Save this setup; it loads with the next world");

        Reopen();
        // The mod picked when the screen last closed (UI spec §13 "Preserve user state").
        if (_lastSelected is { } id && Rows.Any(r => r.Id == id))
            Select(id);
    }

    private static string? _lastSelected;

    public override void _ExitTree() => _lastSelected = SelectedId ?? _lastSelected;

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

    /// <summary>
    /// Validates the mod and writes <c>&lt;id&gt;-&lt;version&gt;.zip</c> into <paramref name="folder"/>. A mod with
    /// errors is not exported; its problems are listed in the packages panel.
    /// </summary>
    public PackageExportResult ExportMod(string id, string folder)
    {
        var result = ModPackages.Export(ModsRoot, id, folder);
        var lines = new List<string>();
        if (result.Succeeded)
        {
            lines.Add(Colored(UiPalette.Success, Loc.T("Exported {0} {1} to {2}", id, result.Manifest!.Version!, result.Path!)));
            lines.Add(Colored(UiPalette.TextSecondary, Loc.T("{0} file(s), SHA-256 {1}", result.Manifest.Files!.Count, result.Manifest.Sha256!)));
            if (result.Skipped.Count > 0)
                lines.Add(Colored(UiPalette.TextSecondary, Loc.T("Left out: {0}", string.Join(", ", result.Skipped))));
        }
        else
        {
            lines.Add(Colored(UiPalette.Error, Loc.T("{0} was not exported. Fix these problems first:", id)));
        }
        lines.AddRange(DiagnosticLines(result.Diagnostics));
        _packageLog.Text = string.Join("\n", lines);
        return result;
    }

    /// <summary>
    /// Checks a package and, when it can be installed, asks to install it (to replace the installed copy, when there is
    /// one). A package that cannot be installed only lists why. Returns the plan.
    /// </summary>
    public PackageInstallPlan InstallPackage(string archivePath)
    {
        var plan = ModPackages.PlanInstall(ModsRoot, archivePath);
        if (!plan.CanInstall)
        {
            _packageLog.Text = string.Join("\n",
                [Colored(UiPalette.Error, Loc.T("{0} cannot be installed:", Path.GetFileName(archivePath))), .. DiagnosticLines(plan.Diagnostics)]);
            return plan;
        }
        var m = plan.Inspection.Manifest!;
        var message = plan.Change switch
        {
            PackageChange.Upgrade => Loc.T("Update {0} from {1} to {2}? The installed files are replaced.", m.Name ?? m.Id!, plan.InstalledVersion!, m.Version!),
            PackageChange.Reinstall => Loc.T("{0} {1} is already installed. Replace it with this package?", m.Name ?? m.Id!, m.Version!),
            PackageChange.Downgrade => Loc.T("Replace {0} {1} with the older {2}? Worlds built with the newer version may lose parts.", m.Name ?? m.Id!, plan.InstalledVersion!, m.Version!),
            PackageChange.ReplaceBroken => Loc.T("The mods folder already has a folder named '{0}' that is not a working mod. Replace it with {1} {2}?", m.Id!, m.Name ?? m.Id!, m.Version!),
            _ => Loc.T("Install {0} {1}?", m.Name ?? m.Id!, m.Version!),
        };
        var warnings = plan.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).Select(d => d.Message).ToList();
        if (warnings.Count > 0)
            message += "\n\n" + string.Join("\n", warnings);
        ConfirmDialog.Ask(this, plan.Replaces ? "Replace mod" : "Install mod", message, plan.Replaces ? "Replace" : "Install",
            () => FinishInstall(archivePath, plan.Replaces), destructive: plan.Replaces);
        return plan;
    }

    private void FinishInstall(string archivePath, bool replace)
    {
        var result = ModPackages.Install(ModsRoot, archivePath, replace);
        var lines = new List<string>();
        if (result.Succeeded)
        {
            var m = result.Plan.Inspection.Manifest!;
            lines.Add(Colored(UiPalette.Success, Loc.T("Installed {0} {1}. It loads with the next world.", m.Name ?? m.Id!, m.Version!)));
            Rescan(m.Id);
        }
        else
        {
            lines.Add(Colored(UiPalette.Error, Loc.T("{0} was not installed:", Path.GetFileName(archivePath))));
        }
        lines.AddRange(DiagnosticLines(result.Diagnostics));
        _packageLog.Text = string.Join("\n", lines);
    }

    /// <summary>Asks, then deletes the mod's folder. Mods that need it are named in the question.</summary>
    public void UninstallMod(string id)
    {
        var mod = Manager.Mods.FirstOrDefault(m => m.Id == id);
        if (mod is null || !ModPackageManager.CanDisable(id))
            return;
        var message = Loc.T("Delete {0} {1} from the mods folder? This cannot be undone.", mod.Manifest.Name ?? id, mod.Version);
        var dependents = ModPackages.Dependents(ModsRoot, id);
        if (dependents.Count > 0)
            message += "\n\n" + Loc.T("These mods need it and will stop loading: {0}", string.Join(", ", dependents));
        ConfirmDialog.Ask(this, "Uninstall mod", message, "Uninstall", () =>
        {
            var problems = ModPackages.Uninstall(ModsRoot, id);
            _packageLog.Text = problems.Count == 0
                ? Colored(UiPalette.Success, Loc.T("Uninstalled {0}.", mod.Manifest.Name ?? id))
                : string.Join("\n", [Colored(UiPalette.Error, Loc.T("{0} was not uninstalled:", id)), .. DiagnosticLines(problems)]);
            Rescan(null);
        });
    }

    // Rescans the folder but keeps the unapplied setup on screen (a new mod joins it enabled, at the end).
    private void Rescan(string? select)
    {
        var hadChanges = HasUnsavedChanges;
        Manager = ModPackageManager.Open(ModsRoot, Manager.ToProfile());
        // A setup with nothing pending stays that way: the game loads a new mod enabled anyway.
        if (!hadChanges)
            _savedProfile = DataJson.Write(Manager.ToProfile());
        Refresh(select ?? SelectedId);
    }

    private void BuildFileDialogs()
    {
        _exportDialog = new FileDialog
        {
            Title = "Export mod package",
            ModeOverridesTitle = false,
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenDir,
            UseNativeDialog = false,
        };
        _exportDialog.DirSelected += dir => { if (SelectedId is { } id) ExportMod(id, dir); };
        AddChild(_exportDialog);
        _installDialog = new FileDialog
        {
            Title = "Install mod package",
            ModeOverridesTitle = false,
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Filters = ["*.zip ; Mod packages"],
            UseNativeDialog = false,
        };
        _installDialog.FileSelected += path => InstallPackage(path);
        AddChild(_installDialog);
    }

    private void OpenExportDialog()
    {
        if (SelectedId is null)
            return;
        Directory.CreateDirectory(PackagesFolder);
        _exportDialog.CurrentDir = PackagesFolder;
        _exportDialog.PopupCentered(new Vector2I(800, 520));
    }

    private void OpenInstallDialog()
    {
        if (Directory.Exists(PackagesFolder))
            _installDialog.CurrentDir = PackagesFolder;
        _installDialog.PopupCentered(new Vector2I(800, 520));
    }

    private static IEnumerable<string> DiagnosticLines(IEnumerable<DataDiagnostic> diagnostics) => diagnostics.Select(d =>
        Colored(d.Severity == DiagnosticSeverity.Error ? UiPalette.Error : UiPalette.Warning, d.ToString()));

    private static string Colored(string color, string text) => $"[color={color}]{Escape(text)}[/color]";

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
        // A full check is for the setup it ran on; any change goes back to the quick loader check.
        _validation = null;
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
            return (Loc.T("Disabled"), UiTokens.Disabled);
        if (loaded is null)
        {
            var reason = DiagnosticsFor(mod.Id).FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error)?.Message;
            return (reason is null ? Loc.T("Not loaded") : Loc.T("Not loaded: {0}", reason), UiTokens.Error);
        }
        var lost = Preview.Conflicts.Count(c => c.ModId == mod.Id);
        var status = loaded.Overrides.Count > 0 ? Loc.T("Loaded, overrides {0} part(s)", loaded.Overrides.Count) : Loc.T("Loaded");
        return lost > 0 ? (Loc.T("{0}; {1} override(s) replaced by later mods", status, lost), UiTokens.Warning) : (status, UiTokens.Success);
    }

    private IEnumerable<DataDiagnostic> DiagnosticsFor(string id)
    {
        var prefix = $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(ModsRoot))}/{id}/";
        return Preview.Diagnostics.Where(d => d.File.StartsWith(prefix, StringComparison.Ordinal));
    }

    private string ProblemsBbcode()
    {
        if (_validation is { } report)
            return report;
        var lines = new List<string>();
        foreach (var d in _profileDiagnostics.Concat(Preview.Diagnostics))
        {
            var color = d.Severity == DiagnosticSeverity.Error ? UiPalette.Error : UiPalette.Warning;
            lines.Add($"[color={color}]{Escape(d.ToString())}[/color]");
        }
        foreach (var id in Manager.MissingMods)
            lines.Add($"[color={UiPalette.TextSecondary}]{Escape(Loc.T("'{0}' is in your mod list but not installed; it keeps its place if you reinstall it.", id))}[/color]");
        return lines.Count == 0 ? Loc.T("No problems.") : string.Join("\n", lines);
    }

    private void UpdateDetails()
    {
        var id = SelectedId;
        var mod = id is null ? null : Manager.Mods.FirstOrDefault(m => m.Id == id);
        _up.Disabled = _down.Disabled = _export.Disabled = mod is null;
        _editInfo.Disabled = mod is null || !ModPackageManager.CanDisable(mod.Id);
        _uninstall.Disabled = mod is null || !ModPackageManager.CanDisable(mod.Id);
        if (mod is null)
        {
            _details.Text = Loc.T("Select a mod to see its details.");
            return;
        }
        var m = mod.Manifest;
        var text = $"[b]{Escape(m.Name ?? mod.Id)}[/b] {mod.Version}  [color={UiPalette.TextSecondary}]{mod.Id}[/color]\n";
        if (m.Authors is { Count: > 0 })
            text += Loc.T("By {0}", Escape(string.Join(", ", m.Authors))) + "\n";
        if (m.Description is not null)
            text += $"{Escape(m.Description)}\n";
        text += Loc.T("Needs: {0}", References(m.Dependencies)) + "\n" + Loc.T("Conflicts with: {0}", References(m.Conflicts)) + "\n";
        if (Preview.Mods.FirstOrDefault(l => l.Id == mod.Id) is { } loaded)
            text += Loc.T("{0} part(s), {1} override(s)", loaded.Parts.Count, loaded.Overrides.Count) + "\n";
        text += $"[color={UiPalette.TextSecondary}]{Escape(mod.Directory)}[/color]";
        _details.Text = text;
    }

    private static string References(List<ModDependency>? list) => list is { Count: > 0 }
        ? Escape(string.Join(", ", list.Select(d => d.Version is null ? d.Id : $"{d.Id} {d.Version}")))
        : Loc.T("nothing");

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

    private void BuildModDialogs()
    {
        _newDialog = new ConfirmationDialog { Title = "New mod" };
        var grid = new GridContainer { Columns = 2 };
        _newDialog.AddChild(grid);
        _newId = new LineEdit { Text = "my_mod", CustomMinimumSize = new Vector2(260, 0), TooltipText = Loc.T("Folder name: lowercase letters, digits and _") };
        _newName = new LineEdit { Text = "My Mod", TooltipText = Loc.T("Name shown in the Mod Manager") };
        grid.AddChild(new Label { Text = "Mod id" });
        grid.AddChild(Loc.Verbatim(_newId));
        grid.AddChild(new Label { Text = "Name" });
        grid.AddChild(Loc.Verbatim(_newName));
        _newDialog.Confirmed += () => CreateMod(_newId.Text.Trim(), _newName.Text.Trim());
        AddChild(_newDialog);

        _infoDialog = new ConfirmationDialog { Title = "Edit mod info" };
        var info = new GridContainer { Columns = 2 };
        _infoDialog.AddChild(info);
        _infoName = new LineEdit { CustomMinimumSize = new Vector2(320, 0) };
        _infoVersion = new LineEdit { TooltipText = Loc.T("major.minor.patch, for example 1.2.0") };
        _infoAuthors = new LineEdit { TooltipText = Loc.T("Names, separated by commas") };
        _infoDependencies = new LineEdit { TooltipText = Loc.T("Mod ids that must load first, separated by commas") };
        _infoDescription = new TextEdit { CustomMinimumSize = new Vector2(320, 80), WrapMode = TextEdit.LineWrappingMode.Boundary };
        foreach (var (label, field) in new (string, Control)[]
            { ("Name", _infoName), ("Version", _infoVersion), ("Authors", _infoAuthors), ("Needs", _infoDependencies), ("Description", _infoDescription) })
        {
            info.AddChild(new Label { Text = label });
            info.AddChild(Loc.Verbatim(field));
        }
        _infoDialog.Confirmed += () =>
        {
            if (SelectedId is { } id)
                EditInfo(id, _infoName.Text, _infoVersion.Text, _infoDescription.Text, List(_infoAuthors.Text), List(_infoDependencies.Text));
        };
        AddChild(_infoDialog);
    }

    private static List<string> List(string text) =>
        [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private void OpenNewDialog() => _newDialog.PopupCentered(new Vector2I(420, 130));

    private void OpenInfoDialog()
    {
        if (SelectedId is not { } id || Manager.Mods.FirstOrDefault(m => m.Id == id) is not { } mod)
            return;
        var m = mod.Manifest;
        _infoName.Text = m.Name ?? id;
        _infoVersion.Text = m.Version ?? "";
        _infoAuthors.Text = string.Join(", ", m.Authors ?? []);
        _infoDependencies.Text = string.Join(", ", (m.Dependencies ?? []).Select(d => d.Id));
        _infoDescription.Text = m.Description ?? "";
        _infoDialog.PopupCentered(new Vector2I(480, 280));
    }

    /// <summary>Makes an empty mod (depending on vanilla) in the mods folder and selects it. Returns why it failed, or null.</summary>
    public string? CreateMod(string id, string name)
    {
        string? error = null;
        try
        {
            Workspace().Create(id, name, null, null, null, null);
        }
        catch (Core.ModTools.ModToolException e)
        {
            error = e.Message;
        }
        _packageLog.Text = error is null
            ? Colored(UiPalette.Success, Loc.T("Made {0} in the mods folder. Open the Part Editor to add parts to it.", id))
            : Colored(UiPalette.Error, Loc.T("{0} was not made: {1}", id, error));
        if (error is null)
            Rescan(id);
        return error;
    }

    /// <summary>Rewrites a mod's <c>mod.json</c> info. Returns the problems that stopped it (empty when it was saved).</summary>
    public IReadOnlyList<DataDiagnostic> EditInfo(string id, string name, string version, string? description, IReadOnlyList<string> authors,
        IReadOnlyList<string> dependencies)
    {
        var mod = Manager.Mods.First(m => m.Id == id);
        var problems = Core.Editor.ModManifestFile.Update(mod.Directory, name, version, description, authors, dependencies);
        _packageLog.Text = problems.Count == 0
            ? Colored(UiPalette.Success, Loc.T("Saved {0}'s info.", name.Trim()))
            : string.Join("\n", [Colored(UiPalette.Error, Loc.T("{0}'s info was not saved:", id)), .. DiagnosticLines(problems)]);
        if (problems.Count == 0)
            Rescan(id);
        return problems;
    }

    /// <summary>
    /// Checks this setup (applied or not) the way a world starts: parts, sounds, world generation, recipes, bots,
    /// raids, quests, characters and every Lua script, and lists the problems. Returns the report.
    /// </summary>
    public Core.ModTools.ValidationReport ValidateAll()
    {
        var report = Workspace().Validate(setup: Manager.ToProfile());
        var lines = new List<string>
        {
            report.Ok
                ? Colored(UiPalette.Success, Loc.T("Every kind of content loads: {0} warning(s).", report.Warnings))
                : Colored(UiPalette.Error, Loc.T("{0} error(s) and {1} warning(s) in this setup:", report.Errors, report.Warnings)),
        };
        lines.AddRange(report.Diagnostics.Select(d => Colored(d.Severity == "error" ? UiPalette.Error : UiPalette.Warning,
            $"[{d.Domain}] {d.File}{(d.JsonPath is null ? "" : " " + d.JsonPath)}{(d.Line is { } line ? $":{line}" : "")}: {d.Message}")));
        _validation = string.Join("\n", lines);
        _problems.Text = _validation;
        return report;
    }

    private Core.ModTools.ModWorkspace Workspace() => new(ModsRoot, ProfilePath, Mechanica.Scripting.Editor.LuaSyntax.Problem);

    /// <summary>Runs <paramref name="then"/>, asking first when it would throw away unsaved changes.</summary>
    public void Discard(Action then) => ConfirmDialog.Discard(this, HasUnsavedChanges, then,
        message: "Your changes to the mod setup are not applied. Discard them?");

    private static Button AddButton(Container parent, string text, Action pressed, string tooltip)
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
