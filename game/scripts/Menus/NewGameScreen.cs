using Godot;
using Mechanica.Core.Net;
using Mechanica.Core.Survival;

namespace Mechanica.Game.Menus;

/// <summary>
/// The main menu's New game screen: the world's name, Survival or Creative, an optional seed (blank picks
/// one), and whether to open the world to the LAN, on which port.
/// </summary>
public partial class NewGameScreen : PanelContainer
{
    private LineEdit _name = null!;
    private OptionButton _mode = null!;
    private LineEdit _seed = null!;
    private CheckBox _lan = null!;
    private LineEdit _port = null!;
    private Label _status = null!;

    /// <summary>Raised with how to start the world and its name.</summary>
    public event Action<LaunchOptions, string>? StartRequested;

    /// <summary>Raised by Back.</summary>
    public event Action? CloseRequested;

    /// <summary>The world name box.</summary>
    public LineEdit WorldName => _name;

    /// <summary>The seed box.</summary>
    public LineEdit Seed => _seed;

    /// <summary>The Open to LAN box.</summary>
    public CheckBox Lan => _lan;

    /// <summary>Why Start did nothing, or empty.</summary>
    public string Status => _status.Text;

    public override void _Ready()
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        AddChild(column);
        column.AddChild(new Label { Text = "New game", ThemeTypeVariation = "HeaderLarge", HorizontalAlignment = HorizontalAlignment.Center });
        var grid = new GridContainer { Columns = 2 };
        column.AddChild(grid);
        _name = Row(grid, "World name", new LineEdit { Text = "My World", MaxLength = 48 });
        _mode = Row(grid, "Mode", new OptionButton());
        _mode.AddItem("Survival", (int)GameMode.Survival);
        _mode.AddItem("Creative", (int)GameMode.Creative);
        _seed = Row(grid, "Seed", new LineEdit { PlaceholderText = "random" });
        _lan = Row(grid, "Open to LAN", new CheckBox());
        _port = Row(grid, "Port", new LineEdit { Text = NetProtocol.DefaultPort.ToString() });
        _status = new Label { Modulate = new Color(1f, 0.6f, 0.5f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_status);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        column.AddChild(buttons);
        var back = new Button { Text = "Back" };
        back.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(back);
        var start = new Button { Name = "Start", Text = "Start" };
        start.Pressed += Start;
        buttons.AddChild(start);
    }

    /// <summary>Picks Survival or Creative.</summary>
    public void SetMode(GameMode mode) => _mode.Select(_mode.GetItemIndex((int)mode));

    /// <summary>Checks the boxes and raises <see cref="StartRequested"/>.</summary>
    public void Start()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0)
        {
            _status.Text = "Give the world a name.";
            return;
        }
        long? seed = null;
        var seedText = _seed.Text.Trim();
        if (seedText.Length > 0)
        {
            // A number is used as it is; any other text is turned into one, so "island" is always the same world.
            seed = long.TryParse(seedText, out var number) ? number : StableHash(seedText);
        }
        var options = LaunchOptions.Offline with { Seed = seed, GameMode = (GameMode)_mode.GetSelectedId() };
        if (_lan.ButtonPressed)
        {
            if (!NetEndpoint.TryParsePort(_port.Text.Trim(), out var port))
            {
                _status.Text = "Enter a port from 1 to 65535.";
                return;
            }
            options = options with { Mode = SessionMode.Host, Port = port };
        }
        _status.Text = "";
        StartRequested?.Invoke(options, name);
    }

    private static long StableHash(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return BitConverter.ToInt64(bytes, 0);
    }

    private static T Row<T>(GridContainer grid, string label, T control) where T : Control
    {
        grid.AddChild(new Label { Text = label });
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        grid.AddChild(control);
        return control;
    }
}
