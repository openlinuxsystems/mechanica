using Godot;
using Mechanica.Core.Net;
using Mechanica.Core.Survival;

namespace Mechanica.Game.Menus;

/// <summary>
/// The main menu's New game screen: the world's name, Survival or Creative, the difficulty (Survival), an optional seed (blank picks
/// one), and whether to open the world to the LAN, on which port.
/// </summary>
public partial class NewGameScreen : Ui.ToolWindow
{
    private LineEdit _name = null!;
    private OptionButton _mode = null!;
    private OptionButton _difficulty = null!;
    private LineEdit _seed = null!;
    private CheckBox _lan = null!;
    private LineEdit _port = null!;
    private Label _status = null!;

    /// <summary>Raised with how to start the world and its name.</summary>
    public event Action<LaunchOptions, string>? StartRequested;

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
        Title = "New Game";
        var column = Body;
        column.CustomMinimumSize = new Vector2(420, 0);
        var grid = new GridContainer { Columns = 2 };
        column.AddChild(grid);
        _name = Row(grid, "World name", new LineEdit { Text = "My World", MaxLength = 48 });
        _mode = Row(grid, "Mode", new OptionButton());
        _mode.AddItem("Survival", (int)GameMode.Survival);
        _mode.AddItem("Creative", (int)GameMode.Creative);
        _difficulty = Row(grid, "Difficulty", new OptionButton { Name = "Difficulty", TooltipText = "Easy: bots hit softer and raids are smaller and rarer. Kept with the world." });
        _difficulty.AddItem("Normal", (int)Difficulty.Normal);
        _difficulty.AddItem("Easy", (int)Difficulty.Easy);
        _mode.ItemSelected += _ => _difficulty.Disabled = (GameMode)_mode.GetSelectedId() == GameMode.Creative;
        _seed = Row(grid, "Seed", new LineEdit { PlaceholderText = "random" });
        _lan = Row(grid, "Open to LAN", new CheckBox());
        _port = Row(grid, "Port", new LineEdit { Text = NetProtocol.DefaultPort.ToString() });
        _status = new Label { ThemeTypeVariation = "ErrorLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_status);
        AddAction("Back", RequestClose, tooltip: "Close (Esc)");
        AddAction("Create", Start, primary: true, tooltip: "Create the world and start playing").Name = "Create";
    }

    /// <summary>Picks Survival or Creative.</summary>
    public void SetMode(GameMode mode)
    {
        _mode.Select(_mode.GetItemIndex((int)mode));
        _difficulty.Disabled = mode == GameMode.Creative;
    }

    /// <summary>Picks how hard a Survival world is.</summary>
    public void SetDifficulty(Difficulty difficulty) => _difficulty.Select(_difficulty.GetItemIndex((int)difficulty));

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
        var options = LaunchOptions.Offline with { Seed = seed, GameMode = (GameMode)_mode.GetSelectedId(), Difficulty = (Difficulty)_difficulty.GetSelectedId() };
        if (_lan.ButtonPressed)
        {
            if (!NetEndpoint.TryParsePort(_port.Text.Trim(), out var port))
            {
                _status.Text = "Enter a port from 1 to 65535.";
                return;
            }
            options = options with { Mode = SessionMode.Host, Port = port, MaxPlayers = Settings.GameSettings.Current.Gameplay.MaxPlayers };
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
