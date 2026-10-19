using Godot;
using Mechanica.Core;
using Mechanica.Core.Input;
using Mechanica.Core.Docs;
using Mechanica.Core.Net;
using Mechanica.Core.Saves;
using Mechanica.Core.Ui;
using Mechanica.Game.Editor;
using Mechanica.Game.Menus;
using Mechanica.Game.Mods;
using Mechanica.Game.Player;
using Mechanica.Game.Saves;
using Mechanica.Game.Sound;
using Mechanica.Game.Ui;

namespace Mechanica.Game;

/// <summary>
/// The home screen (chunk 24, UI spec §14): an animated backdrop, the logo, Continue (the newest save, with
/// its name and when it was last played), New Game, Load Game, Join Game, Options, Credits and Quit, the mod
/// tools beside them, and the version and links along the bottom. Keyboard works (arrows and Enter); Esc
/// asks before quitting when Quit has focus and otherwise does nothing. New Game names the world, picks
/// Survival or Creative and an optional seed, and can open it to the LAN; Join Game finds games on the LAN
/// or by address; the help opens with H or F1. The mod tools that ship in the game (D9) are the Part
/// Editor, the Bot Behavior Editor, the Test Sandbox and the Mod Manager. Launching with <c>--host</c> or
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

    private static bool _booted;
    private readonly List<Control> _open = [];
    /// <summary>The home screen's links (UI spec §14 bottom bar).</summary>
    public const string LinksPath = "res://data/links.json";

    private Control _menu = null!;
    private HelpWindow _help = null!;
    private Button? _continue;
    private Button _quit = null!;

    /// <summary>The help window.</summary>
    public HelpWindow Help => _help;

    /// <summary>The save folder for Continue and Load Game; null uses the player's (none in test scenes).</summary>
    public SaveStore? Store { get; init; }

    private SaveStore? SaveFolder => Store ?? SaveSync.UserStoreIfPersisted;

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
        if (Array.IndexOf(args, World.WorldExportRunner.Flag) is var exportAt and >= 0)
        {
            RunWorldExport(args, exportAt);
            return;
        }
        if (LaunchOptions.TryParse(OS.GetCmdlineUserArgs(), out var options, out _) && (options.Mode != SessionMode.Offline || options.SelfTest))
        {
            Callable.From(() => GetTree().ChangeSceneToFile(WorldScene)).CallDeferred();
            return;
        }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        // The enabled mods' language files: their translations of the menus, and languages they add to Options.
        Loc.UseMods(ModPaths.EnabledDirectories());
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new HomeBackground { Name = "Background" });
        AddChild(new MenuMusic { Name = "Music" });
        var layout = new MarginContainer { Name = "Home" };
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            layout.AddThemeConstantOverride($"margin_{side}", 16);
        AddChild(layout);
        _menu = layout;
        var rows = new VBoxContainer();
        layout.AddChild(rows);
        // The space over the logo gives way first, so the menu still fits a small window at a large UI scale.
        rows.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.25f });
        rows.AddChild(Ui.Logo.Create(84));
        rows.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });
        var center = new CenterContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        rows.AddChild(center);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 48);
        center.AddChild(columns);
        var column = new VBoxContainer { Name = "Buttons", CustomMinimumSize = new Vector2(320, 0) };
        columns.AddChild(column);
        if (SaveFolder?.Latest() is { } latest)
        {
            _continue = AddButton(column, "Continue", () => Continue(latest), Loc.T("Play {0} again", latest.Name));
            _continue.ThemeTypeVariation = "PrimaryButton";
            column.AddChild(new Label
            {
                Name = "ContinueInfo",
                AutoTranslateMode = AutoTranslateModeEnum.Disabled,
                Text = $"{latest.Name}  ·  {HomeScreen.Ago(latest.SavedAt, DateTimeOffset.Now, Loc.T)}",
                ThemeTypeVariation = "SecondaryLabel",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
        AddButton(column, "New Game", () => OpenNewGame(), "Start a new world");
        AddButton(column, "Load Game", () => OpenLoad(), "Play a saved world");
        AddButton(column, "Join Game", () => OpenJoin(), "Join a game on your network or by address");
        AddButton(column, "Options", () => OpenOptions(), "Controls, graphics, audio and more");
        AddButton(column, "Credits", () => OpenCredits(), "Who made the game");
        _quit = AddButton(column, "Quit", () => GetTree().Quit(), "Close the game");
        var toolColumn = new VBoxContainer { Name = "ModTools", CustomMinimumSize = new Vector2(400, 0) };
        columns.AddChild(toolColumn);
        toolColumn.AddChild(new Label { Text = "Mod tools", ThemeTypeVariation = "TitleLabel", HorizontalAlignment = HorizontalAlignment.Center });
        // Two to a row, so the tools never stand taller than the main buttons in a small window.
        var tools = new GridContainer { Name = "ToolButtons", Columns = 2 };
        toolColumn.AddChild(tools);
        AddButton(tools, "Part Editor", () => GetTree().ChangeSceneToFile(PartEditorScene), "Make and edit parts");
        AddButton(tools, "Bot Behavior Editor", () => OpenBotEditor(), "Make bots and the raid table");
        AddButton(tools, "Recipe Editor", () => OpenRecipeEditor(), "Add, copy and override recipes of every mod");
        AddButton(tools, "Sound Editor", () => OpenSoundEditor(), "Edit a mod's sound events without opening a part");
        AddButton(tools, "Test Sandbox", () => OpenSandbox(), "Try parts, bots and Creations");
        AddButton(tools, "Mod Manager", () => OpenModManager(), "Turn mods on and off and package them");
        rows.AddChild(BottomBar());
        _help = new HelpWindow { Name = "Help", Visible = false };
        AddChild(_help);
        // The splash once per run, when the game starts on the menu (not in test scenes that add a menu).
        if (Splash ?? (!_booted && GetTree().CurrentScene == this))
            AddChild(new BootSplash { Name = "Splash" });
        _booted = true;
        Callable.From(() => ((Button?)_continue ?? column.GetChildren().OfType<Button>().First()).GrabFocus()).CallDeferred();
    }

    /// <summary>Whether the boot splash shows; null shows it the first time the game starts on the menu.</summary>
    public bool? Splash { get; set; }

    /// <summary>The Continue button, when a save exists.</summary>
    public Button? ContinueButton => _continue;

    /// <summary>The Quit button.</summary>
    public Button QuitButton => _quit;

    /// <summary>The links at the bottom right.</summary>
    public IReadOnlyList<HomeLink> Links { get; private set; } = [];

    /// <summary>Starts the newest save, or opens Load Game when it no longer reads.</summary>
    public void Continue(SaveSummary latest)
    {
        if (SaveStore.Read(latest.Path, []) is { } save)
        {
            Main.MenuSave = save;
            Go();
        }
        else
            OpenLoad();
    }

    /// <summary>Opens the credits (<c>docs/credits.md</c>).</summary>
    public ToolWindow OpenCredits()
    {
        var window = new ToolWindow { Name = "Credits", Title = "Credits", CustomMinimumSize = new Vector2(560, 420) };
        var text = new RichTextLabel { AutoTranslateMode = AutoTranslateModeEnum.Disabled, BbcodeEnabled = true, FitContent = true, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(520, 320) };
        var file = Path.Combine(HelpWindow.DocsRoot, "credits.md");
        text.Text = File.Exists(file) ? MarkdownBbcode.Convert(File.ReadAllText(file)) : $"{MechanicaInfo.GameName} {MechanicaInfo.Version}";
        text.MetaClicked += meta => OS.ShellOpen(meta.AsString());
        window.Body.AddChild(text);
        window.AddAction("Close", window.RequestClose, primary: true);
        window.CloseRequested += () => Close(window);
        Open(Centered(window));
        return window;
    }

    /// <summary>Esc with Quit focused: asks before quitting (§14). Esc does nothing else on the home screen.</summary>
    public ConfirmDialog? AskQuit() => _quit.HasFocus()
        ? ConfirmDialog.Ask(this, "Quit", Loc.T("Quit {0}?", MechanicaInfo.GameName), "Quit", () => GetTree().Quit(), () => _quit.GrabFocus())
        : null;

    private Control BottomBar()
    {
        var bar = new HBoxContainer { Name = "BottomBar" };
        bar.AddChild(new Label { Name = "Version", AutoTranslateMode = AutoTranslateModeEnum.Disabled, Text = $"{MechanicaInfo.GameName} {MechanicaInfo.Version}", ThemeTypeVariation = "SecondaryLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        Links = Godot.FileAccess.FileExists(LinksPath) ? HomeScreen.ParseLinks(Godot.FileAccess.GetFileAsString(LinksPath)) : [];
        foreach (var link in Links)
            AddButton(bar, link.Label, () => OS.ShellOpen(link.Url), link.Url);
        var help = Loc.Live(AddButton(bar, "?", () => _help.OpenLast("playing.md")), b => b.TooltipText = Loc.T("Handbook ({0})", GameInput.Label(InputActions.Handbook)));
        help.CustomMinimumSize = new Vector2(40, 32);
        return bar;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_help is not null && GameInput.Pressed(@event, InputActions.Handbook))
        {
            _help.OpenLast("playing.md");
            GetViewport().SetInputAsHandled();
        }
        else if (_quit is not null && _menu.Visible && GameInput.Pressed(@event, InputActions.Pause) && ConfirmDialog.Current is null)
        {
            AskQuit();
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
            // Loading the mods takes a moment; the world's own loading screen takes over once it starts.
            LoadingScreen.Transition(this, "Loading mods…", () => GetTree().ChangeSceneToFile(WorldScene));
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

    // --world-export DIR [SEED]: writes the world's manifest and maps (seed 1 when none is given) and quits.
    private void RunWorldExport(string[] args, int at)
    {
        if (at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal))
        {
            GD.PrintErr($"[world-export] {World.WorldExportRunner.Flag} needs a folder.");
            GetTree().Quit(2);
            return;
        }
        var seed = 1L;
        if (at + 2 < args.Length && !args[at + 2].StartsWith("--", StringComparison.Ordinal)
            && !long.TryParse(args[at + 2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out seed))
        {
            GD.PrintErr($"[world-export] '{args[at + 2]}' is not a seed (a whole number).");
            GetTree().Quit(2);
            return;
        }
        GetTree().Quit(World.WorldExportRunner.Run(Path.GetFullPath(args[at + 1]), seed) ? 0 : 1);
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

    /// <summary>Opens Load Game.</summary>
    public Saves.SaveScreen OpenLoad()
    {
        var holder = Centered(new Saves.SaveScreen { Store = SaveFolder ?? new SaveStore(ProjectSettings.GlobalizePath("user://test_saves")) });
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

    /// <summary>Opens the Recipe Editor.</summary>
    public RecipeEditorScreen OpenRecipeEditor()
    {
        var screen = new RecipeEditorScreen { Name = "RecipeEditor" };
        screen.CloseRequested += () => Close(screen);
        Open(screen);
        return screen;
    }

    /// <summary>Opens the Sound Editor.</summary>
    public SoundEditorScreen OpenSoundEditor()
    {
        var screen = new SoundEditorScreen { Name = "SoundEditor" };
        screen.CloseRequested += () => Close(screen);
        Open(screen);
        return screen;
    }

    /// <summary>Opens the Mod Manager.</summary>
    public Control OpenModManager()
    {
        var screen = new ModManagerScreen { Name = "ModManager" };
        screen.CloseRequested += () => Close(screen);
        Open(screen);
        return screen;
    }

    private static CenterContainer Centered(Control screen)
    {
        var holder = new CenterContainer { Name = $"{screen.Name}Holder" };
        holder.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        holder.AddChild(screen);
        return holder;
    }

    /// <summary>Closes every screen opened from the menu.</summary>
    public void CloseAll()
    {
        foreach (var screen in _open.Where(IsInstanceValid))
            Close(screen);
        _open.Clear();
        _help.Hide();
    }

    private void Open(Control screen)
    {
        _menu.Visible = false;
        _open.Add(screen);
        AddChild(screen);
    }

    // Closes a screen, or the centring holder around it.
    private void Close(Control screen)
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        (screen.GetParent() is CenterContainer holder && holder.GetParent() == this ? holder : screen).QueueFree();
        _menu.Visible = true;
        ((Button?)_continue ?? _menu.FindChild("Buttons", true, false)?.GetChildren().OfType<Button>().FirstOrDefault())?.GrabFocus();
    }

    private static Button AddButton(Container parent, string text, Action pressed, string? tooltip = null)
    {
        var button = new Button { Text = text, TooltipText = tooltip ?? "", CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }
}
