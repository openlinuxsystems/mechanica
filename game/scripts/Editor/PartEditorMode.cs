using Godot;
using Mechanica.Game.Mods;

namespace Mechanica.Game.Editor;

/// <summary>
/// The Part Editor mode reachable from the main menu (D9): the editor screen plus the Test Sandbox it
/// opens. "Test in Sandbox" hides the editor and shows the sandbox with the exported part spawned; Back
/// returns to the editor with the part still open. The sandbox stays alive between tests, so local
/// clients stay connected while you iterate.
/// </summary>
public partial class PartEditorMode : Node
{
    /// <summary>Scene to return to when the editor closes.</summary>
    public const string MenuScene = "res://scenes/MainMenu.tscn";

    /// <summary>Mods folder the editor and sandbox use. Set before the node enters the tree.</summary>
    public string ModsRoot { get; set; } = ModPaths.ModsRoot;

    /// <summary>The editor screen.</summary>
    public PartEditorScreen Editor { get; private set; } = null!;

    /// <summary>The sandbox, once a part was tested.</summary>
    public SandboxScreen? Sandbox { get; private set; }

    public override void _Ready()
    {
        Editor = new PartEditorScreen { Name = "PartEditor", ModsRoot = ModsRoot };
        Editor.TestRequested += Test;
        Editor.CloseRequested += () => GetTree().ChangeSceneToFile(MenuScene);
        AddChild(Editor);
        // Saving a script hot reloads it into the sandbox's host, which runs every script (D5).
        Editor.Lua.Runtime = () => Sandbox?.Host.Main.Scripts.Runtime;
    }

    /// <summary>Shows the sandbox with fresh mods and spawns <paramref name="qualifiedId"/> in front of the player.</summary>
    public void Test(string qualifiedId)
    {
        if (Sandbox is null)
        {
            Sandbox = new SandboxScreen
            {
                Name = "Sandbox",
                LoadContent = () => Core.Data.ModLoader.LoadWithProfile(ModsRoot, ModPaths.ProfilePath),
            };
            Sandbox.BackRequested += ShowEditor;
            AddChild(Sandbox);
        }
        else
        {
            SetShown(Sandbox, true);
            Sandbox.ReloadContent();
        }
        SetShown(Editor, false);
        // The host's player spawns on its first frame; spawn once it exists.
        Callable.From(() => SpawnWhenReady(qualifiedId, 0)).CallDeferred();
    }

    private void SpawnWhenReady(string qualifiedId, int attempt)
    {
        if (Sandbox is null)
            return;
        if (Sandbox.Active.Main.LocalPlayer is null && attempt < 120)
        {
            GetTree().CreateTimer(0.05).Timeout += () => SpawnWhenReady(qualifiedId, attempt + 1);
            return;
        }
        Sandbox.Spawn(qualifiedId);
    }

    private void ShowEditor()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (Sandbox is not null)
            SetShown(Sandbox, false);
        SetShown(Editor, true);
    }

    private static void SetShown(Control control, bool shown)
    {
        control.Visible = shown;
        control.ProcessMode = shown ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
    }
}
