using System.Diagnostics;
using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Saves;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// The Lift's blueprint menu (UI spec §5, §8, §9), opened with Use (E) on the player's Lift: the newest saves (at most
/// <see cref="Core.Building.LiftSettings.MaxBlueprints"/>) with their pictures, a name field and Save for what the Lift
/// holds, Load to build the chosen blueprint on the empty Lift (the host checks it, and in Survival takes the parts from
/// the inventory), and Delete after a confirmation. Esc, the X or Close shuts it.
/// </summary>
public partial class BlueprintMenu : CanvasLayer
{
    /// <summary>Picture size in a row.</summary>
    public static readonly Vector2 ThumbnailSize = new(128, 72);

    private readonly List<BlueprintSummary> _list = [];
    private ToolWindow _window = null!;
    private VBoxContainer _rows = null!;
    private LineEdit _name = null!;
    private Button _save = null!;
    private Button _load = null!;
    private Button _delete = null!;
    private Label _status = null!;
    private int _selected = -1;

    /// <summary>Where blueprints are kept. Set before adding to the tree.</summary>
    public BlueprintStore Store { get; init; } = null!;

    /// <summary>The local player's Lift tool, or null while there is no player. Set before adding to the tree.</summary>
    public Func<LiftTool?> Lift { get; init; } = () => null;

    /// <summary>Draws pictures for new blueprints, or null for none.</summary>
    public CreationSnapshots? Snapshots { get; init; }

    /// <summary>True while the menu is up.</summary>
    public bool IsOpen => _window.Visible;

    /// <summary>The blueprints listed, newest first.</summary>
    public IReadOnlyList<BlueprintSummary> Listed => _list;

    /// <summary>The listed blueprint chosen, or -1.</summary>
    public int Selected => _selected;

    /// <summary>The window.</summary>
    public ToolWindow Window => _window;

    /// <summary>The line under the list: what the last save, load or delete did.</summary>
    public string Status => _status.Text;

    /// <summary>Seconds the last refresh took to read the list and load its pictures.</summary>
    public double LastRefreshSeconds { get; private set; }

    public override void _Ready()
    {
        Layer = 5;
        _window = new ToolWindow { Name = "Blueprints", Title = "Blueprints", Visible = false, CustomMinimumSize = new Vector2(560, 480) };
        _window.CloseRequested += Close;
        AddChild(_window);
        _window.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.KeepSize);
        var scroll = new ScrollContainer { Name = "List", SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 300) };
        _window.Body.AddChild(scroll);
        _rows = new VBoxContainer { Name = "Rows", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_rows);
        var saveRow = new HBoxContainer { Name = "SaveRow" };
        _window.Body.AddChild(saveRow);
        _name = new LineEdit { Name = "Name", PlaceholderText = "Blueprint name", MaxLength = Core.Creations.Blueprint.MaxNameLength, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _name.TextSubmitted += _ => Save();
        saveRow.AddChild(_name);
        _save = new Button { Name = "Save", Text = "Save", CustomMinimumSize = new Vector2(96, 32), TooltipText = "Save what your Lift holds under this name" };
        _save.Pressed += () => Save();
        saveRow.AddChild(_save);
        _status = Loc.Verbatim(new Label { Name = "Status", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _window.Body.AddChild(_status);
        _delete = _window.AddAction("Delete", () => Delete(), tooltip: "Delete the chosen blueprint");
        _delete.ThemeTypeVariation = "DangerButton";
        _window.AddAction("Close", Close);
        _load = _window.AddAction("Load", () => Load(), primary: true, tooltip: "Build the chosen blueprint on your empty Lift");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsOpen && GameInput.Pressed(@event, InputActions.Interact) && !GameInput.Typing(GetViewport()))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!IsOpen)
            return;
        var lift = Lift();
        _save.Disabled = lift?.Held is null;
        _load.Disabled = _selected < 0 || _list[_selected].Error is not null || lift?.Own is null || lift.Held is not null;
        _delete.Disabled = _selected < 0;
    }

    /// <summary>Shows the menu with the list read afresh.</summary>
    public void Open()
    {
        _status.Text = "";
        Refresh();
        _window.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Hides the menu.</summary>
    public void Close()
    {
        if (!IsOpen)
            return;
        _window.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Reads the list and the pictures again.</summary>
    public void Refresh()
    {
        var clock = Stopwatch.StartNew();
        var chosen = _selected >= 0 && _selected < _list.Count ? _list[_selected].Path : null;
        _list.Clear();
        _list.AddRange(Store.List());
        _selected = _list.FindIndex(b => b.Path == chosen);
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        for (var i = 0; i < _list.Count; i++)
            _rows.AddChild(Row(i));
        if (_list.Count == 0)
            _rows.AddChild(new Label { Name = "Empty", Text = "No blueprints yet. Put a creation on your Lift and save it.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        LastRefreshSeconds = clock.Elapsed.TotalSeconds;
        Highlight();
    }

    /// <summary>Chooses the listed blueprint at <paramref name="index"/> (-1 for none) and puts its name in the field.</summary>
    public void Select(int index)
    {
        _selected = index >= 0 && index < _list.Count ? index : -1;
        if (_selected >= 0)
            _name.Text = _list[_selected].Name;
        Highlight();
    }

    /// <summary>
    /// Saves what the Lift holds under <paramref name="name"/> (the field's text when null), asking first when that
    /// replaces a blueprint. Returns the confirmation asked, or null.
    /// </summary>
    public ConfirmDialog? Save(string? name = null)
    {
        name ??= _name.Text;
        if (Lift()?.Held is null)
        {
            _status.Text = Loc.T("Put a creation on your Lift to save it.");
            return null;
        }
        var clean = Core.Creations.Blueprint.CleanName(name);
        if (!Store.Exists(clean))
        {
            Write(clean);
            return null;
        }
        return ConfirmDialog.Ask(this, "Replace blueprint", Loc.T("Replace the blueprint {0}?", clean), "Replace", () => Write(clean));
    }

    /// <summary>Asks the host to build the chosen blueprint on the Lift. False when there is nothing to load.</summary>
    public bool Load(Action<Core.Actions.ActionResult>? done = null)
    {
        if (_selected < 0 || Lift() is not { } lift || Store.Read(_list[_selected].Path) is not { } json)
            return false;
        var name = _list[_selected].Name;
        if (!lift.LoadBlueprint(json, result =>
            {
                if (!IsInstanceValid(this))
                    return;
                _status.Text = result.Ok ? Loc.T("Loaded {0} onto your Lift.", name) : result.Message ?? "";
                if (result.Ok)
                    Close();
                done?.Invoke(result);
            }))
        {
            _status.Text = Loc.T("{0} is too big or broken to load.", name);
            return false;
        }
        return true;
    }

    /// <summary>Asks, then deletes the chosen blueprint. Returns the confirmation, or null with none chosen.</summary>
    public ConfirmDialog? Delete()
    {
        if (_selected < 0)
            return null;
        var chosen = _list[_selected];
        return ConfirmDialog.Ask(this, "Delete blueprint", Loc.T("Delete {0}? This can't be undone.", chosen.Name), "Delete", () =>
        {
            _status.Text = Store.Delete(chosen.Path) ? Loc.T("Deleted {0}.", chosen.Name) : Loc.T("Could not delete {0}.", chosen.Name);
            _selected = -1;
            Refresh();
        });
    }

    private void Write(string name)
    {
        var lift = Lift();
        string? path;
        try
        {
            path = lift?.SaveBlueprint(Store, name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = Loc.T("Could not save {0}: {1}", name, e.Message);
            return;
        }
        if (path is null)
            return;
        _status.Text = Loc.T("Saved {0}.", name);
        Refresh();
        Select(_list.FindIndex(b => b.Path == path));
        // The picture is drawn offscreen and appears once it is ready.
        if (lift!.Held is { } creation)
        {
            Snapshots?.Take(creation, BlueprintStore.ThumbnailFor(path), ok =>
            {
                if (ok && IsInstanceValid(this) && IsOpen)
                    Refresh();
            });
        }
    }

    private Control Row(int index)
    {
        var summary = _list[index];
        var panel = new PanelContainer { Name = $"Blueprint{index}", MouseFilter = Control.MouseFilterEnum.Stop };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        panel.AddChild(row);
        row.AddChild(Thumbnail(summary));
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        text.AddChild(Loc.Verbatim(new Label { Text = summary.Name, ThemeTypeVariation = "TitleLabel" }));
        text.AddChild(summary.Error is null
            ? Loc.Verbatim(new Label { Text = Loc.T("{0} parts  ·  saved {1}", summary.Parts, summary.SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")), ThemeTypeVariation = "SecondaryLabel" })
            : Loc.Verbatim(new Label { Text = Loc.T("Can't load: {0}", summary.Error), ThemeTypeVariation = "ErrorLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart }));
        row.AddChild(text);
        panel.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
                return;
            Select(index);
            if (click.DoubleClick)
                Load();
        };
        return panel;
    }

    private void Highlight()
    {
        for (var i = 0; i < _rows.GetChildCount(); i++)
        {
            if (_rows.GetChild(i) is PanelContainer panel)
                panel.AddThemeStyleboxOverride("panel", MechanicaTheme.Box(i == _selected ? UiTokens.ControlPressed : UiTokens.DeepNavy, i == _selected ? UiTokens.Cyan : UiTokens.Blue));
        }
    }

    private static Control Thumbnail(BlueprintSummary summary)
    {
        using var image = summary.Thumbnail is { } file ? Image.LoadFromFile(file) : null;
        if (image is not null && !image.IsEmpty())
        {
            return new TextureRect
            {
                Name = "Thumbnail",
                Texture = ImageTexture.CreateFromImage(image),
                CustomMinimumSize = ThumbnailSize,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
        }
        var blank = new Panel { Name = "NoThumbnail", CustomMinimumSize = ThumbnailSize };
        blank.AddThemeStyleboxOverride("panel", MechanicaTheme.Box(UiTokens.DeepNavy, UiTokens.Blue));
        return blank;
    }
}
