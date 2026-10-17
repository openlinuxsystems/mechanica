using Godot;
using Mechanica.Core;
using Mechanica.Core.Net;
using Mechanica.Game.Editor;
using Mechanica.Game.Mods;

namespace Mechanica.Game;

/// <summary>
/// Start screen: play the test world, or open the mod tools that ship in the game (D9): the Part
/// Editor, the Bot Behavior Editor, the Test Sandbox and the Mod Manager. Launching with <c>--host</c> or <c>--join</c> skips
/// straight to the world. <c>--part-batch file</c> runs a Part Editor batch, prints its problems and
/// quits (0 when everything exported); a Bot Behavior Editor batch (one with <c>bots</c>) runs the same way, which is how vanilla's blocks are re-made headless. Menus proper
/// come in chunk 24.
/// </summary>
public partial class MainMenu : Control
{
    /// <summary>The game world scene.</summary>
    public const string WorldScene = "res://scenes/Main.tscn";

    /// <summary>The Part Editor scene.</summary>
    public const string PartEditorScene = "res://scenes/PartEditor.tscn";

    private Control _menu = null!;

    /// <summary>Command-line flag that runs a Part Editor batch file and quits.</summary>
    public const string PartBatchFlag = "--part-batch";

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
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new Panel { AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        center.AddChild(column);
        _menu = column;
        column.AddChild(new Label { Text = MechanicaInfo.GameName, ThemeTypeVariation = "HeaderLarge", HorizontalAlignment = HorizontalAlignment.Center });
        AddButton(column, "Play survival", () => Play(Core.Survival.GameMode.Survival));
        AddButton(column, "Play creative", () => Play(Core.Survival.GameMode.Creative));
        AddButton(column, "Load game", OpenLoad);
        column.AddChild(new HSeparator());
        column.AddChild(new Label { Text = "Mod tools", HorizontalAlignment = HorizontalAlignment.Center });
        AddButton(column, "Part Editor", () => GetTree().ChangeSceneToFile(PartEditorScene));
        AddButton(column, "Bot Behavior Editor", OpenBotEditor);
        AddButton(column, "Test Sandbox", OpenSandbox);
        AddButton(column, "Mod Manager", OpenModManager);
        column.AddChild(new HSeparator());
        AddButton(column, "Quit", () => GetTree().Quit());
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

    private void Play(Core.Survival.GameMode mode)
    {
        Main.MenuGameMode = mode;
        GetTree().ChangeSceneToFile(WorldScene);
    }

    private void OpenLoad()
    {
        var holder = new CenterContainer { Name = "Load" };
        holder.SetAnchorsPreset(LayoutPreset.FullRect);
        var screen = new Saves.SaveScreen();
        screen.CloseRequested += () => Close(holder);
        screen.LoadRequested += save =>
        {
            Main.MenuSave = save;
            GetTree().ChangeSceneToFile(WorldScene);
        };
        holder.AddChild(screen);
        Open(holder);
    }

    private void OpenSandbox()
    {
        var sandbox = new SandboxScreen { Name = "Sandbox" };
        sandbox.BackRequested += () => Close(sandbox);
        Open(sandbox);
    }

    private void OpenBotEditor()
    {
        var editor = new BotEditorScreen { Name = "BotEditor" };
        editor.CloseRequested += () => Close(editor);
        Open(editor);
    }

    private void OpenModManager()
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
    }

    private void Open(Control screen)
    {
        _menu.GetParent<Control>().Visible = false;
        AddChild(screen);
    }

    private void Close(Control screen)
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        screen.QueueFree();
        _menu.GetParent<Control>().Visible = true;
    }

    private static void AddButton(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        parent.AddChild(button);
    }
}
