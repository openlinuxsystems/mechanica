using System.Numerics;
using System.Text.Json;

namespace Mechanica.Core.Ui;

/// <summary>
/// The in-world HUD's timings and margins (<c>game/data/hud.json</c>, UI spec §6, §9, §11): the hover tooltip's
/// delay and fade, the safe area around edge-anchored elements, how long Use is held to dismiss a hint, and how
/// fast the crosshair pulses over a connection dot.
/// </summary>
public sealed record HudSettings
{
    /// <summary>Seconds of continuous aim at a part before its tooltip appears (§9: 0.25).</summary>
    public double TooltipDelaySeconds { get; init; } = 0.25;

    /// <summary>Seconds the tooltip fades in over (§9: 0.1).</summary>
    public double TooltipFadeSeconds { get; init; } = 0.1;

    /// <summary>Share of the screen's width and height kept clear around edge-anchored HUD elements (§11: 2%).</summary>
    public float SafeArea { get; init; } = 0.02f;

    /// <summary>Seconds Use is held to dismiss a hint (§8: "dismissible by holding E").</summary>
    public double HintDismissSeconds { get; init; } = 0.6;

    /// <summary>Pulses per second of the crosshair over a connection dot.</summary>
    public double CrosshairPulseHz { get; init; } = 2.0;

    /// <summary>Seconds the inventory fades in and out over (§9: 0.15).</summary>
    public double WindowFadeSeconds { get; init; } = 0.15;

    /// <summary>Gap in pixels between the crosshair and the hover tooltip.</summary>
    public float TooltipOffset { get; init; } = 24f;

    /// <summary>What fills all ten pips of each stat in a detail card (UI spec §0 "Stat pips").</summary>
    public StatPipScale StatPips { get; init; } = new();

    /// <summary>A vehicle gauge whose container holds less than this share of what it can hold pulses red (0 to 1).</summary>
    public float GaugeLow { get; init; } = 0.1f;

    /// <summary>Pulses per second of a low vehicle gauge's track.</summary>
    public double GaugePulseHz { get; init; } = 1.0;

    /// <summary>Reads <paramref name="json"/>; throws <see cref="FormatException"/> on bad data.</summary>
    public static HudSettings Parse(string json)
    {
        HudSettings? settings;
        try
        {
            settings = Data.DataJson.Read<HudSettings>(json);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid HUD JSON: {e.Message}", e);
        }
        settings ??= new HudSettings();
        if (settings.TooltipDelaySeconds < 0 || settings.TooltipFadeSeconds < 0 || settings.WindowFadeSeconds < 0 || settings.HintDismissSeconds <= 0
            || settings.CrosshairPulseHz <= 0 || settings.TooltipOffset < 0 || settings.SafeArea is < 0 or >= 0.25f
            || settings.GaugeLow is < 0 or > 1 || settings.GaugePulseHz <= 0)
            throw new FormatException("HUD times and offsets must not be negative, hint_dismiss_seconds, crosshair_pulse_hz and gauge_pulse_hz must be above zero, safe_area must be below 0.25 and gauge_low 0 to 1.");
        if (settings.StatPips is not { MassKg: > 0, Durability: > 0, Friction: > 0, Buoyancy: > 0 })
            throw new FormatException("Every stat_pips scale must be above zero.");
        return settings;
    }

    /// <summary>True when <paramref name="row"/>'s container is low enough that its gauge pulses (<see cref="GaugeLow"/>).</summary>
    public bool IsLow(Items.GaugeRow row) => row.Capacity > 0 && row.Held < row.Capacity * GaugeLow;

    /// <summary>The safe margin in pixels for a screen of <paramref name="screen"/> size: a share of each side.</summary>
    public Vector2 Margin(Vector2 screen) => screen * SafeArea;
}

/// <summary>What the crosshair shows (§8): white by default, blue over a valid place or use, pulsing cyan over a connection dot.</summary>
public enum CrosshairState
{
    /// <summary>Nothing to do here: white.</summary>
    Default,

    /// <summary>A valid placement or something to use: blue.</summary>
    Valid,

    /// <summary>Over a connection dot: pulsing cyan.</summary>
    Connection,
}

/// <summary>The crosshair's colour for a state.</summary>
public static class Crosshair
{
    /// <summary>
    /// The colour (hex) and opacity for <paramref name="state"/> at <paramref name="time"/> seconds: white, blue, or
    /// cyan whose opacity swings between 0.55 and 1 at <paramref name="pulseHz"/>. The colour changes the frame
    /// the state does (§9: instant).
    /// </summary>
    public static (string Hex, float Alpha) Look(CrosshairState state, double time, double pulseHz) => state switch
    {
        CrosshairState.Valid => (UiPalette.Blue, 1f),
        CrosshairState.Connection => (UiPalette.Cyan, (float)(0.775 + 0.225 * Math.Cos(time * pulseHz * 2 * Math.PI))),
        _ => (UiPalette.Text, 1f),
    };
}

/// <summary>
/// Hover timing for the world tooltip (§9): it appears after <see cref="HudSettings.TooltipDelaySeconds"/> of
/// continuous aim at the same target, fades in over <see cref="HudSettings.TooltipFadeSeconds"/>, and is gone the
/// moment the target changes or is lost.
/// </summary>
public sealed class HoverTimer(HudSettings settings)
{
    private double _held;

    /// <summary>What is aimed at now, or null.</summary>
    public object? Target { get; private set; }

    /// <summary>The tooltip's opacity now, 0 to 1.</summary>
    public float Opacity
    {
        get
        {
            if (Target is null || _held < settings.TooltipDelaySeconds)
                return 0f;
            var fade = settings.TooltipFadeSeconds;
            return fade <= 0 ? 1f : (float)Math.Min(1, (_held - settings.TooltipDelaySeconds) / fade);
        }
    }

    /// <summary>True while the tooltip shows at all.</summary>
    public bool Showing => Opacity > 0f;

    /// <summary>Moves time on by <paramref name="delta"/> seconds with <paramref name="target"/> aimed at (null for none).</summary>
    public void Tick(object? target, double delta)
    {
        if (!Equals(target, Target))
        {
            Target = target;
            _held = 0;
            return;
        }
        if (target is not null)
            _held += delta;
    }
}

/// <summary>A held key that completes after a time (§8: hold E to dismiss a hint); letting go or losing focus cancels it.</summary>
public sealed class HoldTimer(double seconds)
{
    /// <summary>Seconds held so far.</summary>
    public double Held { get; private set; }

    /// <summary>How far the hold is, 0 to 1, for a progress ring.</summary>
    public float Fraction => (float)Math.Clamp(Held / seconds, 0, 1);

    /// <summary>
    /// Moves time on by <paramref name="delta"/> with the key <paramref name="down"/> or not. True on the one tick the hold
    /// completes; it then starts again from zero only after the key is let go.
    /// </summary>
    public bool Tick(bool down, double delta)
    {
        if (!down)
        {
            Held = 0;
            return false;
        }
        if (Held >= seconds)
            return false;
        Held += delta;
        return Held >= seconds;
    }

    /// <summary>Drops the hold, as when the window loses focus (§11).</summary>
    public void Cancel() => Held = 0;
}

/// <summary>Where a tooltip goes so it stays fully on screen (§11).</summary>
public static class TooltipPlacement
{
    /// <summary>
    /// The top-left corner for a <paramref name="size"/> tooltip placed below and right of <paramref name="anchor"/> by
    /// <paramref name="offset"/>, flipped to the other side of the anchor when it would leave the screen and then
    /// clamped inside the screen less <paramref name="margin"/>.
    /// </summary>
    public static Vector2 Place(Vector2 anchor, Vector2 size, Vector2 screen, Vector2 margin, float offset)
    {
        var x = anchor.X + offset;
        if (x + size.X > screen.X - margin.X)
            x = anchor.X - offset - size.X;
        var y = anchor.Y + offset;
        if (y + size.Y > screen.Y - margin.Y)
            y = anchor.Y - offset - size.Y;
        return new Vector2(
            Math.Clamp(x, margin.X, Math.Max(margin.X, screen.X - margin.X - size.X)),
            Math.Clamp(y, margin.Y, Math.Max(margin.Y, screen.Y - margin.Y - size.Y)));
    }
}
