using System.Globalization;

namespace Mechanica.Core.Ui;

/// <summary>
/// The blue industrial palette every menu, HUD element and mod tool uses (UI spec §13 "Visual Language").
/// Colours are <c>#RRGGBB</c> hex strings so Core stays free of Godot; the game turns them into its theme.
/// There is no orange anywhere: <see cref="IsOrange"/> is how tests check that.
/// </summary>
public static class UiPalette
{
    /// <summary>Panel background, dark navy.</summary>
    public const string Navy = "#1A2332";

    /// <summary>Opacity of panel backgrounds.</summary>
    public const float PanelOpacity = 0.85f;

    /// <summary>Full-screen loading and boot background, darker than panels (§15).</summary>
    public const string DeepNavy = "#0D1520";

    /// <summary>Button and input fill, darker than panels so they read as controls.</summary>
    public const string ControlFill = "#121A26";

    /// <summary>Pressed button fill.</summary>
    public const string ControlPressed = "#0B111A";

    /// <summary>Borders and accents.</summary>
    public const string Blue = "#2E86DE";

    /// <summary>Highlights, hover, selection, focus and progress.</summary>
    public const string Cyan = "#00D4FF";

    /// <summary>Primary text.</summary>
    public const string Text = "#FFFFFF";

    /// <summary>Secondary text.</summary>
    public const string TextSecondary = "#B0B8C1";

    /// <summary>Disabled controls and their text.</summary>
    public const string Disabled = "#5A6470";

    /// <summary>Errors, refusals and failed loads (muted red, spec §14–15).</summary>
    public const string Error = "#E5534B";

    /// <summary>Warnings: pale yellow, kept well away from orange.</summary>
    public const string Warning = "#E8E3A0";

    /// <summary>Success and "running" states.</summary>
    public const string Success = "#4CD07D";

    /// <summary>Hover scale for buttons (§13 "Buttons").</summary>
    public const float ButtonHoverScale = 1.05f;

    /// <summary>Seconds of hover before a tooltip shows (§9, §13).</summary>
    public const float TooltipDelaySeconds = 0.25f;

    /// <summary>Smallest click target at 1080p, in pixels (§13 "Layout Rules").</summary>
    public const int MinClickTarget = 32;

    /// <summary>Smallest supported window (§11).</summary>
    public const int MinWindowWidth = 1280, MinWindowHeight = 720;

    /// <summary>The UI scale range offered in Options (§11, §16).</summary>
    public const float MinUiScale = 0.75f, MaxUiScale = 1.5f;

    /// <summary>Every named colour, for tests and the style guide.</summary>
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        [nameof(Navy)] = Navy,
        [nameof(DeepNavy)] = DeepNavy,
        [nameof(ControlFill)] = ControlFill,
        [nameof(ControlPressed)] = ControlPressed,
        [nameof(Blue)] = Blue,
        [nameof(Cyan)] = Cyan,
        [nameof(Text)] = Text,
        [nameof(TextSecondary)] = TextSecondary,
        [nameof(Disabled)] = Disabled,
        [nameof(Error)] = Error,
        [nameof(Warning)] = Warning,
        [nameof(Success)] = Success,
    };

    /// <summary>Parses <c>#RRGGBB</c> into channels from 0 to 1.</summary>
    public static (float R, float G, float B) Parse(string hex)
    {
        var s = hex.StartsWith('#') ? hex[1..] : hex;
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"'{hex}' is not a #RRGGBB colour.");
        return (((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
    }

    /// <summary>
    /// True for colours a player would call orange or amber: hue from 10° to 50° with enough saturation
    /// and brightness to read as a colour rather than brown-grey. The theme rule (§7, §13) is no orange.
    /// </summary>
    public static bool IsOrange(float r, float g, float b)
    {
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var chroma = max - min;
        if (max < 0.35f || chroma / max < 0.45f || max != r)
            return false;
        var hue = 60f * ((g - b) / chroma);
        if (hue < 0)
            hue += 360f;
        return hue is >= 10f and <= 50f;
    }

    /// <summary>Clamps a UI scale to the supported range.</summary>
    public static float ClampUiScale(float scale) => Math.Clamp(scale, MinUiScale, MaxUiScale);
}
