using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Net;
using Mechanica.Game.Editor;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Menus;

/// <summary>
/// The in-game menu (chunk 24), opened with Esc while playing and closed with Esc, Resume or a click on
/// the world. It saves and loads the world (host only), opens the world to the LAN or stops hosting, shows
/// how others join, opens help, and quits to the main menu or the desktop. The world keeps running behind
/// it: other players are still playing. F1 opens help on the page for what the player is doing.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private Control _root = null!;
    private Control _panel = null!;
    private Label _session = null!;
    private LineEdit _port = null!;
    private Button _host = null!;
    private Button _stop = null!;
    private Button _save = null!;
    private Label _status = null!;
    private HelpWindow _help = null!;
    private Control? _saveScreen;

    /// <summary>The session. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The world's saves; null hides saving and loading.</summary>
    public Saves.SaveSync? Saves { get; init; }

    /// <summary>The LAN beacon, to say whether the world is listed; optional.</summary>
    public LanBeacon? Lan { get; init; }

    /// <summary>The help page for what the player is doing now (<c>quests.md</c>, ...).</summary>
    public Func<string> HelpTopic { get; init; } = () => "playing.md";

    /// <summary>True while the menu is shown.</summary>
    public bool IsOpen => _root.Visible;

    /// <summary>The help window.</summary>
    public HelpWindow Help => _help;

    /// <summary>The session line, for tests.</summary>
    public string SessionText => _session.Text;

    /// <summary>True when the Save button can be pressed.</summary>
    public bool CanSave => !_save.Disabled;

    public override void _Ready()
    {
        Layer = 10;
        _root = new CenterContainer { Name = "Menu", Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        _panel = new PanelContainer();
        _root.AddChild(_panel);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        _panel.AddChild(column);
        column.AddChild(new Label { Text = "Menu", ThemeTypeVariation = "HeaderLarge", HorizontalAlignment = HorizontalAlignment.Center });
        AddButton(column, "Resume", Close);
        if (Saves is not null)
        {
            _save = AddButton(column, "Save or load…", OpenSaves);
        }
        else
        {
            _save = new Button { Disabled = true, Visible = false };
            column.AddChild(_save);
        }

        column.AddChild(new HSeparator());
        _session = new Label { Name = "Session", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_session);
        var hostRow = new HBoxContainer();
        _port = new LineEdit { Text = NetProtocol.DefaultPort.ToString(), PlaceholderText = "Port", CustomMinimumSize = new Vector2(90, 0) };
        _host = new Button { Text = "Open to LAN", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _host.Pressed += OpenToLan;
        hostRow.AddChild(_port);
        hostRow.AddChild(_host);
        column.AddChild(hostRow);
        _stop = AddButton(column, "Stop hosting", () => Session.Leave("Stopped hosting."));

        column.AddChild(new HSeparator());
        AddButton(column, "Help (F1)", () => OpenHelp());
        AddButton(column, "Quit to main menu", () => GetTree().ChangeSceneToFile(MainMenu.MenuScene));
        AddButton(column, "Quit game", () => GetTree().Quit());
        _status = new Label { Name = "Status", ThemeTypeVariation = "ErrorLabel" };
        column.AddChild(_status);

        _help = new HelpWindow { Name = "Help", Visible = false };
        AddChild(_help);
    }

    public override void _Input(InputEvent @event)
    {
        if (GameInput.Pressed(@event, InputActions.Handbook) && !GameInput.Typing(GetViewport()))
        {
            OpenHelp();
            GetViewport().SetInputAsHandled();
        }
        // Esc while playing opens the menu; while another screen has the mouse, that screen closes first.
        else if (GameInput.Pressed(@event, InputActions.Pause) && (IsOpen || Input.MouseMode == Input.MouseModeEnum.Captured))
        {
            if (IsOpen)
                Close();
            else
                Open();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        // A click on the world takes the mouse back, which closes the menu.
        if (IsOpen && Input.MouseMode == Input.MouseModeEnum.Captured)
            Hide();
        if (!IsOpen)
            return;
        var state = Session.State;
        _host.Disabled = state != NetSession.SessionState.Offline;
        _port.Editable = state == NetSession.SessionState.Offline;
        _stop.Visible = state == NetSession.SessionState.Hosting;
        _save.Disabled = !Session.IsHost;
        _session.Text = state switch
        {
            NetSession.SessionState.Hosting =>
                $"Hosting on port {Session.Port}, {Session.ReadyPeers.Count + 1} of {Session.MaxPlayers} players. "
                + (Lan?.Listening == true ? "Players on your network see this world under Join game." : "Others join by your address and port."),
            NetSession.SessionState.Offline => "Playing alone. Open the world to let others on your network join.",
            _ => $"{state}. {Session.LastMessage}",
        };
    }

    /// <summary>Shows the menu and frees the mouse.</summary>
    public void Open()
    {
        _status.Text = "";
        _root.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _Process(0);
    }

    /// <summary>Hides the menu and takes the mouse back for play.</summary>
    public void Close()
    {
        Hide();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Opens help on <paramref name="topic"/>, or on the page for what the player is doing.</summary>
    public void OpenHelp(string? topic = null)
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _help.Open(topic ?? HelpTopic());
    }

    /// <summary>Hosts on the port in the box; the world and players carry over.</summary>
    public void OpenToLan()
    {
        if (!NetEndpoint.TryParsePort(_port.Text.Trim(), out var port))
        {
            _status.Text = "Enter a port from 1 to 65535.";
            return;
        }
        _status.Text = Session.Host(port) == Error.Ok ? "" : Session.LastMessage;
    }

    private new void Hide()
    {
        _root.Visible = false;
        _saveScreen?.QueueFree();
        _saveScreen = null;
        _panel.Visible = true;
    }

    /// <summary>Opens the save screen (the host saves and loads; a client can load a world of its own).</summary>
    public void OpenSaves()
    {
        if (_saveScreen is not null)
            return;
        var holder = new CenterContainer();
        holder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var screen = new Saves.SaveScreen { Saves = Saves };
        screen.CloseRequested += () =>
        {
            holder.QueueFree();
            _saveScreen = null;
            _panel.Visible = true;
        };
        // Loading starts the world again from the save; a hosted session ends and can be hosted again.
        screen.LoadRequested += save =>
        {
            Main.MenuSave = save;
            GetTree().ChangeSceneToFile(MainMenu.WorldScene);
        };
        holder.AddChild(screen);
        _root.AddChild(holder);
        _saveScreen = holder;
        _panel.Visible = false;
    }

    private static Button AddButton(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }
}
