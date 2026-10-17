using Godot;
using Mechanica.Core.Settings;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Settings;

/// <summary>
/// Autoload holding this player's <see cref="UserSettings"/> (<c>user://settings.json</c>): it loads them
/// at start, applies the key bindings, UI scale, graphics and audio, and saves and re-applies them when
/// Options changes something. Test scenes (<c>res://tests/…</c>) start from the defaults and never touch the file, so a
/// developer's own bindings can't change a test.
/// </summary>
public partial class GameSettings : Node
{
    /// <summary>Where the settings live.</summary>
    public const string SettingsPath = "user://" + UserSettings.FileName;

    private static UserSettings _current = new();
    private static bool _persist;

    /// <summary>The graphics settings whose window size, mode and VSync are in effect.</summary>
    public static GraphicsSettings WindowSettings { get; private set; } = new();

    /// <summary>False in test scenes, which leave the player's files alone.</summary>
    public static bool Persist => _persist;

    /// <summary>The settings in use.</summary>
    public static UserSettings Current => _current;

    /// <summary>The colour-blind filter over everything.</summary>
    public static ColorFilter? Filter { get; private set; }

    /// <summary>Raised after settings change.</summary>
    public static event Action? Changed;

    public override void _EnterTree()
    {
        _persist = !OS.GetCmdlineArgs().Any(a => a.StartsWith("res://tests/", StringComparison.Ordinal));
        GetTree().NodeAdded += DisplayApplier.Configure;
        Filter = new ColorFilter { Name = "ColorFilter" };
        AddChild(Filter);
        Apply(_persist ? UserSettings.Load(ProjectSettings.GlobalizePath(SettingsPath)) : new UserSettings());
        ApplyWindow();
    }

    public override void _ExitTree() => GetTree().NodeAdded -= DisplayApplier.Configure;

    /// <summary>Uses, applies and (outside tests) saves <paramref name="settings"/>.</summary>
    public static void Save(UserSettings settings)
    {
        Apply(settings);
        if (_persist)
            _current.Save(ProjectSettings.GlobalizePath(SettingsPath));
    }

    /// <summary>
    /// Uses and applies <paramref name="settings"/> without saving: bindings, UI scale, quality and audio
    /// at once. The window itself (size, display mode, VSync) changes only through <see cref="ApplyWindow"/>,
    /// which Options calls behind its Apply button and confirm.
    /// </summary>
    public static void Apply(UserSettings settings)
    {
        _current = settings.Clamped();
        GameInput.Apply(_current.Keymap());
        UiRoot.UiScale = _current.UiScale;
        if (Engine.GetMainLoop() is SceneTree tree)
            DisplayApplier.ApplyQuality(tree, _current.Graphics);
        DisplayApplier.ApplyAudio(_current.Audio);
        Filter?.Apply(_current.Interface.ColorBlind);
        Changed?.Invoke();
    }

    /// <summary>Applies the window settings of the settings in use.</summary>
    public static void ApplyWindow()
    {
        WindowSettings = _current.Graphics;
        DisplayApplier.ApplyWindow(WindowSettings);
    }
}
