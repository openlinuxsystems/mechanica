namespace Mechanica.Core.Building;

/// <summary>
/// How the Weld Tool's moving parts follow its <c>weld</c> clip (<see cref="Characters.CharacterGesture.Weld"/>): the
/// clamping arms close round the tip as the tool is pushed in, hold while the weld registers and open again, the tip
/// spins up and down with them, and the blue ring glows brighter. Every peer works this out from the clip time it
/// already plays, so it costs no traffic.
/// </summary>
public static class WeldMotion
{
    /// <summary>Seconds the <c>weld</c> clip lasts (<c>WELD_SECONDS</c> in <c>tools/characters/build_characters.py</c>).</summary>
    public const float Seconds = 0.6f;

    /// <summary>When the arms start to close, are shut, start to open and are open again, in clip seconds.</summary>
    public static readonly (float Close, float Shut, float Open, float Done) Timing = (0.06f, 0.16f, 0.42f, 0.56f);

    /// <summary>Glow of the blue ring while the tool is out and idle.</summary>
    public const float IdleGlow = 0.7f;

    /// <summary>The glow it adds with the arms shut.</summary>
    public const float WeldGlow = 3.5f;

    /// <summary>Tip turns per second with the arms shut.</summary>
    public const float TipTurnsPerSecond = 6f;

    /// <summary>How far the arms are closed, 0 (open, as held) to 1 (shut round the tip), <paramref name="time"/> seconds into the clip.</summary>
    public static float Closed(float time)
    {
        var (close, shut, open, done) = Timing;
        if (time <= close || time >= done)
            return 0f;
        if (time < shut)
            return Ease((time - close) / (shut - close));
        if (time <= open)
            return 1f;
        return 1f - Ease((time - open) / (done - open));
    }

    /// <summary>How bright the blue ring is for a given <see cref="Closed"/> amount.</summary>
    public static float Glow(float closed) => IdleGlow + WeldGlow * closed;

    private static float Ease(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }
}
