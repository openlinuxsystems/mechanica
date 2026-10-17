using Godot;
using Mechanica.Core.Ui;

namespace Mechanica.Game.Ui;

/// <summary>
/// Autoload that sets up the UI for every scene, test scenes included (UI spec §11, §13): the shared
/// <see cref="MechanicaTheme"/> on the root window, the 1280×720 minimum window size, the UI scale, and
/// the 1.05 hover scale on every button as it enters the tree. Layout scaling itself is the project's
/// <c>canvas_items</c> stretch mode with a 1280×720 base, so the UI grows with the window up to 4K.
/// </summary>
public partial class UiRoot : Node
{
    private static float _scale = 1f;

    /// <summary>The UI scale (0.75 to 1.5), applied on top of the window stretch.</summary>
    public static float UiScale
    {
        get => _scale;
        set
        {
            _scale = UiPalette.ClampUiScale(value);
            if (Engine.GetMainLoop() is SceneTree tree)
                tree.Root.ContentScaleFactor = _scale;
        }
    }

    public override void _EnterTree()
    {
        var root = GetTree().Root;
        root.Theme = MechanicaTheme.Theme;
        root.MinSize = new Vector2I(UiPalette.MinWindowWidth, UiPalette.MinWindowHeight);
        root.ContentScaleFactor = _scale;
        GetTree().NodeAdded += OnNodeAdded;
    }

    public override void _ExitTree() => GetTree().NodeAdded -= OnNodeAdded;

    private static void OnNodeAdded(Node node)
    {
        if (node is Button button)
            HoverScale.Attach(button);
    }
}
