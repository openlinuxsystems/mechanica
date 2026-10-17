using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Saves;

namespace Mechanica.Game.Saves;

/// <summary>
/// The save and load screen: the worlds in the save folder, newest first, with Load and Delete. In a world
/// the host also gets a name box and Save. Loading reads the file and raises <see cref="LoadRequested"/>; the
/// owner starts the world (see <see cref="Main.MenuSave"/>).
/// </summary>
public partial class SaveScreen : PanelContainer
{
    private ItemList _list = null!;
    private LineEdit _name = null!;
    private Label _status = null!;
    private Button _load = null!;
    private Button _delete = null!;
    private IReadOnlyList<SaveSummary> _saves = [];

    /// <summary>The save folder. Null uses <see cref="SaveSync.UserStore"/>.</summary>
    public SaveStore? Store { get; set; }

    /// <summary>The world to save from, or null on the main menu (load only).</summary>
    public SaveSync? Saves { get; init; }

    /// <summary>A save was picked and read.</summary>
    public event Action<SaveGame>? LoadRequested;

    /// <summary>Back was pressed.</summary>
    public event Action? CloseRequested;

    /// <summary>The worlds listed, newest first.</summary>
    public IReadOnlyList<SaveSummary> Listed => _saves;

    /// <summary>The status line, for tests.</summary>
    public string Status => _status.Text;

    public override void _Ready()
    {
        Store ??= Saves?.Store ?? SaveSync.UserStore;
        CustomMinimumSize = new Vector2(520, 420);
        var rows = new VBoxContainer();
        AddChild(rows);
        rows.AddChild(new Label { Text = Saves is null ? "Load a world" : "Save or load", ThemeTypeVariation = "HeaderMedium" });
        _list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 240) };
        _list.ItemSelected += i => Select((int)i);
        _list.ItemActivated += i => Load((int)i);
        rows.AddChild(_list);

        if (Saves is { Session.IsHost: true })
        {
            var saveRow = new HBoxContainer();
            _name = new LineEdit { Text = Saves.WorldName, PlaceholderText = "World name", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _name.TextSubmitted += _ => Save();
            saveRow.AddChild(_name);
            var save = new Button { Text = "Save" };
            save.Pressed += Save;
            saveRow.AddChild(save);
            rows.AddChild(saveRow);
        }
        else
        {
            _name = new LineEdit { Visible = false };
        }

        var buttons = new HBoxContainer();
        _load = new Button { Text = "Load", Disabled = true };
        _load.Pressed += () => Load(Selected);
        _delete = new Button { Text = "Delete", Disabled = true };
        _delete.Pressed += Delete;
        var back = new Button { Text = "Back" };
        back.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(_load);
        buttons.AddChild(_delete);
        buttons.AddChild(back);
        rows.AddChild(buttons);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        rows.AddChild(_status);
        Refresh();
    }

    /// <summary>Reads the save folder again.</summary>
    public void Refresh()
    {
        _saves = Store!.List();
        _list.Clear();
        foreach (var save in _saves)
        {
            var when = save.SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            _list.AddItem(save.Error is null ? $"{save.Name}  ·  {save.Mode}  ·  {when}" : $"{save.Name}  ·  can't load: {save.Error}");
        }
        Select(-1);
    }

    /// <summary>Saves the world under the name in the box (host only).</summary>
    public void Save()
    {
        if (Saves is null)
            return;
        var path = Saves.SaveNow(_name.Text);
        _status.Text = path is null ? "Could not save the world; see the log." : $"Saved as {Path.GetFileName(path)}.";
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
            _status.Text = $"Can't load {_saves[index].Name}: {diagnostics.FirstOrDefault()?.Message}";
            return;
        }
        LoadRequested?.Invoke(save);
    }

    private int Selected => _list.GetSelectedItems() is [var i, ..] ? i : -1;

    private void Select(int index)
    {
        _load.Disabled = index < 0 || _saves[index].Error is not null;
        _delete.Text = "Delete";
        _delete.Disabled = index < 0;
        if (index >= 0 && _name.Visible && _saves[index].Error is null)
            _name.Text = _saves[index].Name;
    }

    // The first press asks; the second deletes.
    private void Delete()
    {
        if (Selected is not (var i and >= 0))
            return;
        if (_delete.Text != "Really delete?")
        {
            _delete.Text = "Really delete?";
            return;
        }
        if (Store!.Delete(_saves[i].Path))
            _status.Text = $"Deleted {_saves[i].Name}.";
        Refresh();
    }
}
