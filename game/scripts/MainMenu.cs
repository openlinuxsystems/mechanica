using Godot;
using Mechanica.Core;
using Mechanica.Core.Input;
using Mechanica.Core.Net;
using Mechanica.Game.Editor;
using Mechanica.Game.Menus;
using Mechanica.Game.Mods;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game;

/// <summary>
/// The start screen (chunk 24): start a new world (named, Survival or Creative, an optional seed, and
/// optionally open to the LAN), load a saved one, join a game found on the LAN or by address, set the
/// player's name, read the help (F1), or open the mod tools that ship in the game (D9): the Part Editor,
/// the Bot Behavior Editor, the Test Sandbox and the Mod Manager. Launching with <c>--host</c> or
/// <c>--join</c> skips straight to the world. <c>--part-batch file</c> runs a Part Editor batch, prints its
/// problems and quits (0 when everything exported); a Bot Behavior Editor batch (one with <c>bots</c>) and
/// a story batch (one with <c>quests</c> or <c>pois</c>) run the same way, which is how vanilla's content
/// is re-made headless.
/// </summary>
public partial class MainMenu : Control
{
    /// <summary>The main menu scene.</summary>
    public const string MenuScene = "res://scenes/MainMenu.tscn";

    /// <summary>The game world scene.</summary>
    public const string WorldScene = "res://scenes/Main.tscn";

    /// <summary>The Part Editor scene.</summary>
    public const string PartEditorScene = "res://scenes/PartEditor.tscn";

    /// <summary>Command-line flag that runs a Part Editor batch file and quits.</summary>
    public const string PartBatchFlag = "--part-batch";

    private readonly List<Control> _open = [];
    private Control _menu = null!;
    private HelpWindow _help = null!;

    /// <summary>The help window.</summary>
    public HelpWindow Help => _help;

    /// <summary>Raised instead of changing scene when a world would start, for tests; null changes scene.</summary>
    public Action? StartWorld { get; set; }

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, PartBatchFlag) is var at and >= 0)
        {
            _ = RunBatch(at + 1 < args.Length ? args[at + 1] : null);
            return;
        }
        if (LaunchOptions.TryParse(OS.GetCmdlineUserArgs(), out var options, out _) && options.Mode != SessionMode.Offline)
        {
            Callable.From(() => GetTree().ChangeSceneToFile(WorldScene)).CallDeferred();
            return;
        }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new Panel { AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        center.AddChild(column);
        _menu = column;
        column.AddChild(Loc.Verbatim(new Label { Text = MechanicaInfo.GameName, ThemeTypeVariation = "HeaderLarge", HorizontalAlignment = HorizontalAlignment.Center }));
        AddButton(column, "New game", () => OpenNewGame());
        AddButton(column, "Load game", () => OpenLoad());
        AddButton(column, "Join game", () => OpenJoin());
        AddButton(column, "Options", () => OpenOptions());
        Loc.Live(AddButton(column, "", () => _help.Open("playing.md")), () => Loc.T("Help ({0})", GameInput.Label(InputActions.Handbook)));
        column.AddChild(new HSeparator());
        column.AddChild(new Label { Text = "Mod tools", HorizontalAlignment = HorizontalAlignment.Center });
        AddButton(column, "Part Editor", () => GetTree().ChangeSceneToFile(PartEditorScene));
        AddButton(column, "Bot Behavior Editor", () => OpenBotEditor());
        AddButton(column, "Test Sandbox", () => OpenSandbox());
        AddButton(column, "Mod Manager", () => OpenModManager());
        column.AddChild(new HSeparator());
        AddButton(column, "Quit", () => GetTree().Quit());
        _help = new HelpWindow { Name = "Help", Visible = false };
        AddChild(_help);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_help is not null && GameInput.Pressed(@event, InputActions.Handbook))
        {
            _help.Open("playing.md");
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Opens the New game screen.</summary>
    public NewGameScreen OpenNewGame()
    {
        var screen = new NewGameScreen { Name = "NewGame" };
        screen.CloseRequested += () => Close(screen);
        screen.StartRequested += (options, name) =>
        {
            Main.MenuOptions = options;
            Main.MenuWorldName = name;
            Main.MenuCharacter = screen.Character;
            Go();
        };
        Open(Centered(screen));
        return screen;
    }

    /// <summary>Opens the Join game screen.</summary>
    public JoinScreen OpenJoin()
    {
        var screen = new JoinScreen { Name = "Join" };
        screen.CloseRequested += () => Close(screen);
        screen.JoinRequested += endpoint =>
        {
            Main.MenuOptions = LaunchOptions.ForJoin(endpoint) with { Password = NetPassword.Clamp(screen.Password) };
            Go();
        };
        Open(Centered(screen));
        return screen;
    }

    /// <summary>Opens Options.</summary>
    public OptionsScreen OpenOptions()
    {
        var screen = new OptionsScreen();
        screen.CloseRequested += () => Close(screen);
        Open(Centered(screen));
        return screen;
    }

    private void Go()
    {
        if (StartWorld is { } start)
            start();
        else
            GetTree().ChangeSceneToFile(WorldScene);
    }

    private async Task RunBatch(string? file)
    {
        if (file is null)
        {
            GD.PrintErr($"[batch] {PartBatchFlag} needs a batch file.");
            GetTree().Quit(2);
            return;
        }
        if (File.Exists(file) && Core.Editor.BotBatch.IsBotBatch(File.ReadAllText(file)))
        {
            var bots = BotBatchRunner.Run(file);
            Report(bots, file, "bots and raid tables");
            return;
        }
        if (File.Exists(file) && Core.Editor.StoryBatch.IsStoryBatch(File.ReadAllText(file)))
        {
            Report(StoryBatchRunner.Run(file), file, "POIs and quests");
            return;
        }
        var result = await PartBatchRunner.RunAsync(this, file);
        Report(result, file, "parts and recipes");
    }

    private void Report(BatchRunResult result, string file, string what)
    {
        foreach (var d in result.Diagnostics)
        {
            if (d.Severity == Core.Data.DiagnosticSeverity.Error)
                GD.PrintErr($"[batch] {d}");
            else
                GD.Print($"[batch] {d}");
        }
        GD.Print($"[batch] {(result.Succeeded ? "OK" : "FAILED")}: {result.Exports.Count(e => e.Succeeded)} of {result.Exports.Count} {what} exported from {file}.");
        GetTree().Quit(result.Succeeded ? 0 : 1);
    }

    /// <summary>Opens Load game.</summary>
    public Saves.SaveScreen OpenLoad()
    {
        var holder = Centered(new Saves.SaveScreen());
        var screen = (Saves.SaveScreen)holder.GetChild(0);
        screen.CloseRequested += () => Close(holder);
        screen.LoadRequested += save =>
        {
            Main.MenuSave = save;
            Go();
        };
        Open(holder);
        return screen;
    }

    /// <summary>Opens the Test Sandbox.</summary>
    public SandboxScreen OpenSandbox()
    {
        var sandbox = new SandboxScreen { Name = "Sandbox" };
        sandbox.BackRequested += () => Close(sandbox);
        Open(sandbox);
        return sandbox;
    }

    /// <summary>Opens the Bot Behavior Editor.</summary>
    public BotEditorMode OpenBotEditor()
    {
        var mode = new BotEditorMode { Name = "BotEditorMode" };
        mode.CloseRequested += () => Close(mode);
        Open(mode);
        return mode;
    }

    /// <summary>Opens the Mod Manager.</summary>
    public Control OpenModManager()
    {
        var holder = new Control { Name = "ModManager" };
        holder.SetAnchorsPreset(LayoutPreset.FullRect);
        holder.AddChild(new Panel { AnchorRight = 1, AnchorBottom = 1 });
        holder.AddChild(new ModManagerScreen());
        var back = new Button { Text = "Back" };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        (back.OffsetLeft, back.OffsetTop, back.OffsetRight, back.OffsetBottom) = (16, -56, 96, -16);
        back.Pressed += () => Close(holder);
        holder.AddChild(back);
        Open(holder);
        return holder;
    }

    private static CenterContainer Centered(Control screen)
    {
        var holder = new CenterContainer { Name = $"{screen.Name}Holder" };
        holder.SetAnchorsPreset(LayoutPreset.FullRect);
        holder.AddChild(screen);
        return holder;
    }

    /// <summary>Closes every screen opened from the menu.</summary>
    public void CloseAll()
    {
        foreach (var screen in _open.Where(IsInstanceValid))
            screen.QueueFree();
        _open.Clear();
        _menu.GetParent<Control>().Visible = true;
    }

    private void Open(Control screen)
    {
        _menu.GetParent<Control>().Visible = false;
        _open.Add(screen);
        AddChild(screen);
    }

    // Closes a screen, or the centring holder around it.
    private void Close(Control screen)
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        (screen.GetParent() is CenterContainer holder && holder.GetParent() == this ? holder : screen).QueueFree();
        _menu.GetParent<Control>().Visible = true;
    }

    private static Button AddButton(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }
}
