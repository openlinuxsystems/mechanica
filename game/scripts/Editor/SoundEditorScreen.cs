using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.ModTools;
using Mechanica.Game.Mods;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Editor;

/// <summary>
/// The Sound Editor, a mod tool on the main menu: a mod's sound events (<c>sounds/**/*.json</c>) without opening a part,
/// for sounds that scripts, bots, quests or other mods play. It is the Part Editor's Sound Event Binder without the part
/// bindings (<see cref="SoundBinderPanel.OpenMod"/>): pick the mod and its bank file, add events and clips, preview
/// and save. Vanilla's sounds are not offered, like the Recipe Editor.
/// </summary>
public partial class SoundEditorScreen : Control
{
    private OptionButton _target = null!;
    private OptionButton _file = null!;
    private FileDialog _importDialog = null!;
    private AcceptDialog _message = null!;
    private Action<string>? _importTarget;
    private List<string> _targets = [];
    private List<string> _files = [];

    /// <summary>Mods folder. Set before the node enters the tree.</summary>
    public string ModsRoot { get; set; } = ModPaths.ModsRoot;

    /// <summary>Profile that gives the mod list. Set before the node enters the tree.</summary>
    public string ProfilePath { get; set; } = ModPaths.ProfilePath;

    /// <summary>The screen's window: its X and Esc close it (asking first about unsaved changes).</summary>
    public ToolWindow Window { get; private set; } = null!;

    /// <summary>The window was closed.</summary>
    public event Action? CloseRequested;

    /// <summary>The last problem shown, for tests.</summary>
    public string? LastError { get; private set; }

    /// <summary>The binder that edits the bank.</summary>
    public SoundBinderPanel Binder { get; private set; } = null!;

    /// <summary>The mod whose sounds are open, or null when there is none to edit.</summary>
    public string? TargetMod { get; private set; }

    /// <summary>The mods that can be edited, in list order.</summary>
    public IReadOnlyList<string> Targets => _targets;

    /// <summary>The open mod's bank files (relative), plus the default new one when it has none.</summary>
    public IReadOnlyList<string> Files => _files;

    private bool IsDirty => Binder.Bank?.IsDirty == true;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Window = new ToolWindow { Name = "Window", Title = "Sound Editor" };
        Window.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Window.CloseGuard = then => ConfirmDialog.Discard(this, IsDirty, then);
        Window.CloseRequested += () => CloseRequested?.Invoke();
        AddChild(Window);
        var column = Window.Body;
        column.AddChild(new Label
        {
            Text = "A mod's sound events, for sounds its scripts, bots and quests play. Parts bind theirs in the Part Editor's Sounds tab. Make a mod in the Mod Manager first.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var top = new HBoxContainer();
        column.AddChild(top);
        top.AddChild(new Label { Text = "Mod to edit" });
        _target = Loc.Verbatim(new OptionButton { CustomMinimumSize = new Vector2(220, 0), TooltipText = "The mod whose sound events are edited" });
        _target.ItemSelected += i => ConfirmDialog.Discard(this, IsDirty, () => SetTarget(_targets[(int)i]), () => _target.Select(_targets.IndexOf(TargetMod ?? "")));
        top.AddChild(_target);
        top.AddChild(new Label { Text = "Bank file" });
        _file = Loc.Verbatim(new OptionButton { CustomMinimumSize = new Vector2(220, 0), TooltipText = "The sound bank file in the mod's sounds folder" });
        _file.ItemSelected += i => ConfirmDialog.Discard(this, IsDirty, () => OpenFile(_files[(int)i]), () => _file.Select(_files.IndexOf(Binder.Bank?.RelativeFile ?? "")));
        top.AddChild(_file);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(scroll);
        Binder = new SoundBinderPanel { Name = "Binder", ModsRoot = () => ModsRoot, Import = Import, ShowError = ShowError };
        scroll.AddChild(Binder);
        _importDialog = new FileDialog
        {
            Title = "Import",
            ModeOverridesTitle = false,
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            UseNativeDialog = false,
        };
        _importDialog.FileSelected += path => _importTarget?.Invoke(path);
        AddChild(_importDialog);
        _message = new AcceptDialog { Title = "Sound Editor" };
        AddChild(_message);
        Reload();
    }

    /// <summary>Reads the installed mods again, keeping the open mod when it is still there.</summary>
    public void Reload()
    {
        var mods = new ModWorkspace(ModsRoot, ProfilePath).List().Mods.Where(m => m.Id != Identifiers.VanillaModId).ToList();
        _targets = [.. mods.Select(m => m.Id)];
        _target.Clear();
        foreach (var mod in mods)
            _target.AddItem(mod.Name == mod.Id ? mod.Id : $"{mod.Name} ({mod.Id})");
        _target.Disabled = _targets.Count == 0;
        if (_targets.Count == 0)
            _target.AddItem(Loc.T("No mod to edit yet"));
        var target = TargetMod is { } kept && _targets.Contains(kept) ? kept : _targets.FirstOrDefault();
        if (target is null)
        {
            TargetMod = null;
            _files = [];
            _file.Clear();
            _file.Disabled = true;
            Binder.SetPart(null);
            return;
        }
        SetTarget(target);
    }

    /// <summary>Opens <paramref name="mod"/>'s sounds (its first bank file). False when it is not an editable mod.</summary>
    public bool SetTarget(string mod)
    {
        if (!_targets.Contains(mod))
            return false;
        TargetMod = mod;
        _target.Select(_targets.IndexOf(mod));
        var dir = Path.Combine(ModsRoot, mod);
        _files = [.. SoundBankDocument.Files(dir)];
        if (_files.Count == 0)
            _files.Add(SoundBankDocument.DefaultFile(mod));
        _file.Clear();
        foreach (var file in _files)
            _file.AddItem(file);
        _file.Disabled = false;
        OpenFile(_files[0]);
        return true;
    }

    /// <summary>Opens one of the mod's bank files (relative to the mod). Returns the problem, or null.</summary>
    public string? OpenFile(string relativeFile)
    {
        if (TargetMod is null)
            return "No mod is open.";
        var error = Binder.OpenMod(Path.Combine(ModsRoot, TargetMod), relativeFile);
        if (_files.IndexOf(relativeFile) is >= 0 and var at)
            _file.Select(at);
        ShowError(error);
        return error;
    }

    private void Import(AssetKind kind, Action<string> then)
    {
        _importTarget = then;
        _importDialog.Filters = [$"{string.Join(", ", AssetPath.ExtensionsFor(kind).Select(e => "*" + e))} ; {kind}"];
        _importDialog.PopupCentered(new Vector2I(900, 600));
    }

    private void ShowError(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return;
        LastError = message;
        _message.DialogText = message;
        _message.PopupCentered();
    }
}
