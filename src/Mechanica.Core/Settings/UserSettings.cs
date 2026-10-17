using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Ui;

namespace Mechanica.Core.Settings;

/// <summary>
/// One player's own settings (UI spec §16), saved as <c>settings.json</c> in the user data folder. They
/// stay on this machine: controls, camera, graphics, audio and interface never affect the world, so a
/// client's settings never reach the host. Missing values take their defaults, and a broken file is
/// ignored so the game still starts.
/// </summary>
public sealed record UserSettings
{
    /// <summary>Usual file name.</summary>
    public const string FileName = "settings.json";

    /// <summary>Key bindings that differ from the defaults: action id to <c>[primary, alternate]</c>.</summary>
    public SortedDictionary<string, List<string>>? Bindings { get; init; }

    /// <summary>Radians of look per pixel of mouse motion.</summary>
    public float MouseSensitivity { get; init; } = DefaultMouseSensitivity;

    /// <summary>Inverts vertical look.</summary>
    public bool InvertY { get; init; }

    /// <summary>Camera field of view in degrees.</summary>
    public float FieldOfView { get; init; } = DefaultFieldOfView;

    /// <summary>UI scale from 0.75 to 1.5 (§11).</summary>
    public float UiScale { get; init; } = 1f;

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>Default look speed.</summary>
    public const float DefaultMouseSensitivity = 0.0025f;

    /// <summary>Default field of view.</summary>
    public const float DefaultFieldOfView = 75f;

    /// <summary>Smallest and largest field of view offered.</summary>
    public const float MinFieldOfView = 60f, MaxFieldOfView = 110f;

    /// <summary>The key bindings as a <see cref="Keymap"/>.</summary>
    public Keymap Keymap() => Input.Keymap.FromOverrides(Bindings?.ToDictionary(p => p.Key, p => p.Value));

    /// <summary>A copy with <paramref name="keymap"/>'s changes as the bindings.</summary>
    public UserSettings WithKeymap(Keymap keymap) => this with { Bindings = keymap.Overrides() is { Count: > 0 } o ? o : null };

    /// <summary>A copy with every value in range.</summary>
    public UserSettings Clamped() => this with
    {
        MouseSensitivity = float.IsFinite(MouseSensitivity) ? Math.Clamp(MouseSensitivity, 0.0002f, 0.02f) : DefaultMouseSensitivity,
        FieldOfView = float.IsFinite(FieldOfView) ? Math.Clamp(FieldOfView, MinFieldOfView, MaxFieldOfView) : DefaultFieldOfView,
        UiScale = float.IsFinite(UiScale) ? UiPalette.ClampUiScale(UiScale) : 1f,
    };

    /// <summary>Reads <paramref name="json"/>; null when it isn't valid settings.</summary>
    public static UserSettings? Parse(string json)
    {
        try
        {
            return DataJson.Read<UserSettings>(json)?.Clamped();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the file at <paramref name="path"/>; defaults when it is missing or broken.</summary>
    public static UserSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) ?? new UserSettings() : new UserSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }

    /// <summary>The canonical JSON form.</summary>
    public string ToJson() => JsonSerializer.Serialize(this with { Extra = Extra is null ? null : new(Extra.OrderBy(p => p.Key, StringComparer.Ordinal)) }, DataJson.WriteOptions) + "\n";

    /// <summary>Writes the file, creating its folder.</summary>
    public void Save(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } dir)
            Directory.CreateDirectory(dir);
        DataJson.WriteFile(path, ToJson());
    }
}
