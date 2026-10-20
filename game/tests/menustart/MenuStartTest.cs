using Godot;
using Mechanica.Core.Net;
using Mechanica.Core.Survival;
using Mechanica.Game.Menus;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// A new world started from the main menu, the way a player starts one: the world reads the menu's choices and
/// device input, sets up completely without an error, and opens on the crash. Once the opening is skipped the
/// HUD is back, the Sledgehammer is in hand and Esc's menu opens.
/// </summary>
public partial class MenuStartTest : StagedTest
{
    private readonly List<string> _errors = [];
    private ErrorLog _log = null!;
    private Main _main = null!;

    protected override string Tag => "menustart";

    protected override int StageTimeoutFrames => 1800;

    public override void _Ready()
    {
        _log = new ErrorLog(_errors);
        OS.AddLogger(_log);
        Add("a new world from the menu sets up without an error", frame =>
        {
            if (frame == 0)
            {
                Main.MenuOptions = LaunchOptions.Offline with { GameMode = GameMode.Survival };
                Main.MenuWorldName = "Menu Start Test";
                _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
                _main.ShowLoading = false;
                _main.SaveFolder = ProjectSettings.GlobalizePath("user://tests/menustart/saves");
                AddChild(_main);
                Check(_main.Options is { Mode: SessionMode.Offline }, "it takes the menu's options");
                Check(_main.Tools is not null, "the tool belt is set up");
                Check(_main.GetNodeOrNull<PauseMenu>("PauseMenu") is not null, "the pause menu is set up");
                return false;
            }
            if (_main.Loading?.Entered != true)
                return false;
            Check(_errors.Count == 0, $"no errors while starting ({string.Join(" | ", _errors)})");
            return true;
        });
        Add("it opens on the crash", _ =>
        {
            if (_main.Opening is not { Playing: true } opening)
                return false;
            Check(HudRoot.Hidden, "the HUD is hidden while it plays");
            opening.Skip();
            return true;
        });
        Add("after the opening the player can play", _ =>
        {
            if (_main.Opening is not null)
                return false;
            Check(!HudRoot.Hidden, "the HUD is back");
            Check(_main.Tools!.Current == _main.Tools.Sledge, $"the Sledgehammer is in hand ({_main.Tools.Current.Name})");
            var pause = _main.GetNode<PauseMenu>("PauseMenu");
            pause.Open();
            Check(pause.IsOpen, "the pause menu opens");
            Check(_errors.Count == 0, $"no errors while playing ({string.Join(" | ", _errors)})");
            return true;
        });
    }

    public override void _ExitTree()
    {
        OS.RemoveLogger(_log);
        Main.MenuOptions = null;
        Main.MenuWorldName = null;
    }

    // Errors only; mod data warnings and the like are not what this test looks for.
    private sealed partial class ErrorLog(List<string> errors) : Logger
    {
        public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType,
            Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        {
            if (errorType == (int)ErrorType.Warning)
                return;
            lock (errors)
                errors.Add(rationale.Length > 0 ? rationale : code);
        }
    }
}
