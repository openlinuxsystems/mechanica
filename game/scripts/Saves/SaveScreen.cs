using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Saves;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Saves;

/// <summary>
/// The save and load screen (UI spec §14 "Load Game"): the worlds in the save folder, newest first, one row
/// each with its picture, name, mode, play time and when it was last played, and its own Load and Delete
/// buttons. Delete asks first. In a world the host also gets a name box and Save. Loading reads the file and
/// raises <see cref="LoadRequested"/>; the owner starts the world (see <see cref="Main.MenuSave"/>).
/// </summary>
public partial class SaveScreen : ToolWindow
{
    /// <summary>Size of a row's picture.</summary>
    public static readonly Vector2 ThumbnailSize = new(160, 90);

    private VBoxContainer _rows = null!;
    private LineEdit _name = null!;
    private Label _status = null!;
    private IReadOnlyList<SaveSummary> _saves = [];

    /// <summary>The save folder. Null uses <see cref="SaveSync.UserStore"/>.</summary>
    public SaveStore? Store { get; set; }

    /// <summary>The world to save from, or null on the main menu (load only).</summary>
    public SaveSync? Saves { get; init; }

    /// <summary>A save was picked and read.</summary>
    public event Action<SaveGame>? LoadRequested;

    /// <summary>The worlds listed, newest first.</summary>
    public IReadOnlyList<SaveSummary> Listed => _saves;

    /// <summary>The status line, for tests.</summary>
    public string Status => _status.Text;

    /// <summary>The list's rows, one per save, for tests.</summary>
    public IReadOnlyList<Control> Rows => [.. _rows.GetChildren().OfType<Control>()];

    public override void _Ready()
    {
        Title = Saves is null ? "Load Game" : "Save or Load";
        Name = "Saves";
        Store ??= Saves?.Store ?? SaveSync.UserStore;
        CustomMinimumSize = new Vector2(640, 480);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Body.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_rows);

        if (Saves is { Session.IsHost: true })
        {
            var saveRow = new HBoxContainer();
            _name = new LineEdit { Text = Saves.WorldName, PlaceholderText = "World name", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _name.TextSubmitted += _ => Save();
            saveRow.AddChild(_name);
            var save = new Button { Text = "Save", ThemeTypeVariation = "PrimaryButton", TooltipText = "Save the world under this name" };
            save.Pressed += Save;
            saveRow.AddChild(save);
            Body.AddChild(saveRow);
        }
        else
        {
            _name = new LineEdit { Visible = false };
            Body.AddChild(_name);
        }
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = "SecondaryLabel" };
        Body.AddChild(_status);
        AddAction("Back", RequestClose, tooltip: "Close (Esc)");
        Refresh();
    }

    /// <summary>Reads the save folder again.</summary>
    public void Refresh()
    {
        _saves = Store!.List();
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        for (var i = 0; i < _saves.Count; i++)
            _rows.AddChild(Row(i));
        if (_saves.Count == 0)
            _rows.AddChild(new Label { Text = "No saved worlds yet.", ThemeTypeVariation = "SecondaryLabel" });
    }

    /// <summary>Saves the world under the name in the box (host only).</summary>
    public void Save()
    {
        if (Saves is null)
            return;
        var path = Saves.SaveNow(_name.Text);
        _status.Text = path is null ? Loc.T("Could not save the world; see the log.") : Loc.T("Saved as {0}.", Path.GetFileName(path));
        Refresh();
    }

    /// <summary>Loads the listed save at <paramref name="index"/>.</summary>
    public void Load(int index)
    {
        if (index < 0 || index >= _saves.Count)
            return;
        var diagnostics = new List<DataDiagnostic>();
        if (SaveStore.Read(_saves[index].Path, diagnostics) is not { } save)
        {
            _status.Text = Loc.T("Can't load {0}: {1}", _saves[index].Name, diagnostics.FirstOrDefault()?.Message);
            return;
        }
        LoadRequested?.Invoke(save);
    }

    /// <summary>Asks, then deletes the listed save at <paramref name="index"/>.</summary>
    public ConfirmDialog? Delete(int index)
    {
        if (index < 0 || index >= _saves.Count)
            return null;
        var summary = _saves[index];
        return ConfirmDialog.Ask(this, "Delete world", Loc.T("Delete {0}? This can't be undone.", summary.Name), "Delete", () =>
        {
            _status.Text = Store!.Delete(summary.Path) ? Loc.T("Deleted {0}.", summary.Name) : Loc.T("Could not delete {0}.", summary.Name);
            Refresh();
        });
    }

    private Control Row(int index)
    {
        var summary = _saves[index];
        var panel = new PanelContainer { Name = $"Save{index}" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        panel.AddChild(row);
        row.AddChild(Thumbnail(summary));
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        text.AddChild(Loc.Verbatim(new Label { Text = summary.Name, ThemeTypeVariation = "TitleLabel" }));
        var when = summary.SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        text.AddChild(summary.Error is null
            ? Loc.Verbatim(new Label { Text = Loc.T("{0}  ·  played {1}  ·  last played {2}", Loc.T(summary.Mode.ToString()), SaveSummary.FormatPlayTime(summary.PlaySeconds, Loc.T), when), ThemeTypeVariation = "SecondaryLabel" })
            : Loc.Verbatim(new Label { Text = Loc.T("Can't load: {0}", summary.Error), ThemeTypeVariation = "ErrorLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart }));
        row.AddChild(text);
        var buttons = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        var load = new Button { Text = "Load", ThemeTypeVariation = "PrimaryButton", Disabled = summary.Error is not null, CustomMinimumSize = new Vector2(100, 32), TooltipText = Loc.T("Play {0}", summary.Name) };
        load.Pressed += () => Load(index);
        var delete = new Button { Text = "Delete", ThemeTypeVariation = "DangerButton", CustomMinimumSize = new Vector2(100, 32), TooltipText = Loc.T("Delete {0}", summary.Name) };
        delete.Pressed += () => Delete(index);
        buttons.AddChild(load);
        buttons.AddChild(delete);
        row.AddChild(buttons);
        panel.GuiInput += e =>
        {
            if (e is InputEventMouseButton { DoubleClick: true, ButtonIndex: MouseButton.Left } && summary.Error is null)
                Load(index);
            else if (e is InputEventMouseButton { Pressed: true } && summary.Error is null && _name.Visible)
                _name.Text = summary.Name;
        };
        return panel;
    }

    private static Control Thumbnail(SaveSummary summary)
    {
        using var image = summary.Thumbnail is { } file ? Image.LoadFromFile(file) : null;
        if (image is not null && !image.IsEmpty())
        {
            return new TextureRect
            {
                Texture = ImageTexture.CreateFromImage(image),
                CustomMinimumSize = ThumbnailSize,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            };
        }
        var blank = new Panel { CustomMinimumSize = ThumbnailSize };
        blank.AddThemeStyleboxOverride("panel", MechanicaTheme.Box(UiTokens.DeepNavy, UiTokens.Blue));
        return blank;
    }
}
