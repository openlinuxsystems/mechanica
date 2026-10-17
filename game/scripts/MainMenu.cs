using Godot;
using Mechanica.Core;
using Mechanica.Core.Net;
using Mechanica.Game.Editor;
using Mechanica.Game.Mods;

namespace Mechanica.Game;

/// <summary>
/// Start screen: play the test world, or open the mod tools that ship in the game (D9): the Part
/// Editor, the Test Sandbox and the Mod Manager. Launching with <c>--host</c> or <c>--join</c> skips
/// straight to the world. Menus proper come in chunk 24.
/// </summary>
public partial class MainMenu : Control
{
    /// <summary>The game world scene.</summary>
    public const string WorldScene = "res://scenes/Main.tscn";

    /// <summary>The Part Editor scene.</summary>
    public const string PartEditorScene = "res://scenes/PartEditor.tscn";

    private Control _menu = null!;

    public override void _Ready()
    {
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
        AddButton(column, "Play test world", () => GetTree().ChangeSceneToFile(WorldScene));
        column.AddChild(new HSeparator());
        column.AddChild(new Label { Text = "Mod tools", HorizontalAlignment = HorizontalAlignment.Center });
        AddButton(column, "Part Editor", () => GetTree().ChangeSceneToFile(PartEditorScene));
        AddButton(column, "Test Sandbox", OpenSandbox);
        AddButton(column, "Mod Manager", OpenModManager);
        column.AddChild(new HSeparator());
        AddButton(column, "Quit", () => GetTree().Quit());
    }

    private void OpenSandbox()
    {
        var sandbox = new SandboxScreen { Name = "Sandbox" };
        sandbox.BackRequested += () => Close(sandbox);
        Open(sandbox);
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
