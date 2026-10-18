namespace Mechanica.Core.Characters;

/// <summary>
/// How the Paint Tool's tank moves with the arms: the keys in <see cref="PaintToolSettings.Tank"/>, eased the same way as
/// <c>tools/characters/build_characters.py</c> (<c>paint_tank_motion</c>), so the tank the game draws stays in the left
/// hand the reload was posed with.
/// </summary>
public static class PaintToolRig
{
    /// <summary>
    /// The tank's twist (degrees about its axis) and pull (metres back out of its collar) <paramref name="seconds"/> into
    /// <paramref name="clip"/> (a first-person <c>_fp</c> clip moves it as its third-person one does), or null when the
    /// clip does not move the tank.
    /// </summary>
    public static (float Twist, float Pull)? Tank(PaintToolSettings settings, string clip, float seconds)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.EndsWith(CharacterRig.FirstPersonSuffix, StringComparison.Ordinal))
            clip = clip[..^CharacterRig.FirstPersonSuffix.Length];
        if (!settings.Tank.TryGetValue(clip, out var keys) || keys.Length == 0)
            return null;
        if (seconds <= keys[0][0])
            return (keys[0][1], keys[0][2]);
        for (var i = 1; i < keys.Length; i++)
        {
            if (seconds <= keys[i][0])
            {
                var t = Math.Clamp((seconds - keys[i - 1][0]) / MathF.Max(keys[i][0] - keys[i - 1][0], 1e-6f), 0f, 1f);
                var u = t * t * (3f - 2f * t);
                return (keys[i - 1][1] + (keys[i][1] - keys[i - 1][1]) * u, keys[i - 1][2] + (keys[i][2] - keys[i - 1][2]) * u);
            }
        }
        return (keys[^1][1], keys[^1][2]);
    }
}
