using Godot;
using Mechanica.Game.Mods;

namespace Mechanica.Game.Editor;

/// <summary>
/// The Bot Behavior Editor mode reachable from the main menu (D9): the editor screen plus the Test Sandbox
/// it opens. "Test in Sandbox" exports the bot, hides the editor and shows the sandbox with fresh mods and
/// the bot spawned in front of the player; Back returns to the editor with the bot still open. The sandbox
/// stays alive between tests, so local clients stay connected while you iterate.
/// </summary>
public partial class BotEditorMode : Control
{
    /// <summary>Mods folder the editor and sandbox use. Set before the node enters the tree.</summary>
    public string ModsRoot { get; set; } = ModPaths.ModsRoot;

    /// <summary>The editor screen.</summary>
    public BotEditorScreen Editor { get; private set; } = null!;

    /// <summary>The sandbox, once a bot was tested.</summary>
    public SandboxScreen? Sandbox { get; private set; }

    /// <summary>The editor was closed (X or Esc).</summary>
    public event Action? CloseRequested;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Editor = new BotEditorScreen { Name = "BotEditor", ModsRoot = ModsRoot };
        Editor.TestRequested += Test;
        Editor.CloseRequested += () => CloseRequested?.Invoke();
        AddChild(Editor);
    }

    /// <summary>Shows the sandbox with fresh mods and spawns bot type <paramref name="qualifiedId"/> in front of the player.</summary>
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
        Sandbox.SpawnBot(qualifiedId);
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
