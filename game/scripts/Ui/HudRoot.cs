using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Ui;
using Mechanica.Game.Player;

namespace Mechanica.Game.Ui;

/// <summary>
/// A HUD layer that may stay up while the HUD is hidden, such as the chat while the player types in it or the
/// quest log while it is open.
/// </summary>
public interface IHudLayer
{
    /// <summary>True while this layer stays visible with the HUD hidden.</summary>
    bool KeepWhenHidden { get; }

    /// <summary>True while this layer stays visible under an open window (see <see cref="HudRoot.Covered"/>).</summary>
    bool KeepWhenCovered => KeepWhenHidden;
}

/// <summary>
/// The root of the in-world HUD (UI spec §1, §6, §11). Hide GUI (Alt+Z) hides every layer in the <see cref="Group"/>
/// group (crosshair, tool bar, compass, tips, chat, quest log, names) and shows them again; menus and windows are
/// not HUD and stay. Edge-anchored HUD elements keep <see cref="Margin"/> clear of the screen edges (2% from
/// <c>game/data/hud.json</c>). While a window is open over the world (<see cref="Covered"/>), the same layers step
/// aside so nothing of the HUD shows through or over the window; a layer tagged <see cref="KeepWhenCoveredMeta"/>
/// (the tool bar, which takes drops from the inventory) or an <see cref="IHudLayer"/> that asks stays. When the
/// window loses focus, <see cref="FocusLost"/> tells hold actions to cancel.
/// </summary>
public partial class HudRoot : Node
{
    /// <summary>The scene group of HUD canvas layers that Hide GUI hides.</summary>
    public const string Group = "hud";

    /// <summary>Where the HUD timings live.</summary>
    public const string DataPath = "res://data/hud.json";

    private static HudSettings? _settings;

    /// <summary>Meta that keeps a plain HUD layer up under an open window.</summary>
    public const string KeepWhenCoveredMeta = "hud_keep_when_covered";

    /// <summary>True while Hide GUI has the HUD hidden.</summary>
    public static bool Hidden { get; set; }

    /// <summary>True while a window is open over the world (as of this frame's <see cref="Covered"/>).</summary>
    public static bool IsCovered { get; private set; }

    /// <summary>True while a window is open over the world: the inventory, the menu, the logbook or any tool window.</summary>
    public Func<bool> Covered { get; set; } = () => ToolWindow.Topmost is not null;

    /// <summary>Raised when the game window loses focus, so held actions cancel (§11).</summary>
    public static event Action? FocusLost;

    /// <summary>The HUD timings and margins, read once from <see cref="DataPath"/>.</summary>
    public static HudSettings Settings => _settings ??=
        Godot.FileAccess.FileExists(DataPath) ? HudSettings.Parse(Godot.FileAccess.GetFileAsString(DataPath)) : new HudSettings();

    /// <summary>The safe margin, in pixels of <paramref name="viewport"/>'s visible area, on each side.</summary>
    public static Vector2 Margin(Viewport viewport)
    {
        var size = viewport.GetVisibleRect().Size;
        var margin = Settings.Margin(new System.Numerics.Vector2(size.X, size.Y));
        return new Vector2(margin.X, margin.Y);
    }

    /// <summary>False in automated tests: Alt+Z does nothing.</summary>
    public bool ReadDeviceInput { get; init; } = true;

    public override void _EnterTree() => Hidden = IsCovered = false;

    public override void _ExitTree() => Hidden = IsCovered = false;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!ReadDeviceInput || !GameInput.Pressed(@event, InputActions.HideGui))
            return;
        Toggle();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Hides the HUD, or shows it again.</summary>
    public void Toggle()
    {
        Hidden = !Hidden;
        Apply();
    }

    public override void _Process(double delta) => Apply();

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
            LoseFocus();
    }

    /// <summary>Cancels held actions as when the window loses focus.</summary>
    public static void LoseFocus() => FocusLost?.Invoke();

    private void Apply()
    {
        IsCovered = Covered();
        foreach (var node in GetTree().GetNodesInGroup(Group))
        {
            var show = (!Hidden || node is IHudLayer { KeepWhenHidden: true })
                && (!IsCovered || (node is IHudLayer hud ? hud.KeepWhenCovered : node.HasMeta(KeepWhenCoveredMeta)));
            switch (node)
            {
                case CanvasLayer layer when layer.Visible != show:
                    layer.Visible = show;
                    break;
                case Node3D spatial when spatial.Visible != show:
                    spatial.Visible = show;
                    break;
            }
        }
    }
}

/// <summary>A screen-space progress ring for timed holds (§6, §11): a thin track with a blue arc that fills clockwise.</summary>
public partial class ProgressRing : Control
{
    private float _value;

    /// <summary>How full the ring is, 0 to 1.</summary>
    public float Value
    {
        get => _value;
        set
        {
            value = Mathf.Clamp(value, 0f, 1f);
            if (Mathf.IsEqualApprox(value, _value))
                return;
            _value = value;
            QueueRedraw();
        }
    }

    /// <summary>Ring thickness in pixels.</summary>
    public float Thickness { get; set; } = 3f;

    public ProgressRing()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(28, 28);
    }

    public override void _Draw()
    {
        var centre = Size / 2;
        var radius = Mathf.Min(Size.X, Size.Y) / 2 - Thickness;
        DrawArc(centre, radius, 0, Mathf.Tau, 48, UiTokens.ControlFill, Thickness, true);
        if (_value > 0)
            DrawArc(centre, radius, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * _value, Mathf.Max(4, (int)(48 * _value)), UiTokens.Blue, Thickness, true);
    }
}
