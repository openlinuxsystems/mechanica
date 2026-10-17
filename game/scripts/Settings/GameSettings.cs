using Godot;
using Mechanica.Core.Settings;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Settings;

/// <summary>
/// Autoload holding this player's <see cref="UserSettings"/> (<c>user://settings.json</c>): it loads them
/// at start, applies the key bindings and UI scale, and saves and re-applies them when Options changes
/// something. Test scenes (<c>res://tests/…</c>) start from the defaults and never touch the file, so a
/// developer's own bindings can't change a test.
/// </summary>
public partial class GameSettings : Node
{
    /// <summary>Where the settings live.</summary>
    public const string SettingsPath = "user://" + UserSettings.FileName;

    private static UserSettings _current = new();
    private static bool _persist;

    /// <summary>The settings in use.</summary>
    public static UserSettings Current => _current;

    /// <summary>Raised after settings change.</summary>
    public static event Action? Changed;

    public override void _EnterTree()
    {
        _persist = !OS.GetCmdlineArgs().Any(a => a.StartsWith("res://tests/", StringComparison.Ordinal));
        Apply(_persist ? UserSettings.Load(ProjectSettings.GlobalizePath(SettingsPath)) : new UserSettings());
    }

    /// <summary>Uses, applies and (outside tests) saves <paramref name="settings"/>.</summary>
    public static void Save(UserSettings settings)
    {
        Apply(settings);
        if (_persist)
            _current.Save(ProjectSettings.GlobalizePath(SettingsPath));
    }

    /// <summary>Uses and applies <paramref name="settings"/> without saving.</summary>
    public static void Apply(UserSettings settings)
    {
        _current = settings.Clamped();
        GameInput.Apply(_current.Keymap());
        UiRoot.UiScale = _current.UiScale;
        Changed?.Invoke();
    }
}
