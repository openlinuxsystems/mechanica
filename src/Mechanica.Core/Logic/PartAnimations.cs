namespace Mechanica.Core.Logic;

/// <summary>One part's animation, as the host sends it to clients.</summary>
/// <param name="Creation">The part's Creation.</param>
/// <param name="Part">The part (it has an <c>animation</c> section).</param>
/// <param name="Angle">How far its model is turned about its animation axis now, in rad (within ±π).</param>
/// <param name="Speed">How fast it turns on from there, in rad/s.</param>
public sealed record PartAnimation(int Creation, int Part, float Angle, float Speed);

/// <summary>
/// How far each animated part's model is turned (chunk 23): a rotor's blades spinning, a flap tilted, a saw
/// turning. Part scripts set an angle and a speed on the host with <c>part.animate</c>; the model keeps
/// turning at that speed until the next change, so a steady spin costs nothing to send. Times are seconds on
/// whichever clock the owner passes (the host's script clock, or a client's own). A part with no entry is
/// unturned. <see cref="Revision"/> changes whenever an animation does.
/// </summary>
public sealed class PartAnimations
{
    private readonly SortedDictionary<(int Creation, int Part), (float Angle, float Speed, double Since)> _animations = [];

    /// <summary>Goes up by one on every change.</summary>
    public int Revision { get; private set; }

    /// <summary>A part's angle at <paramref name="now"/>, in rad within ±π (0 when it has none).</summary>
    public float AngleAt(int creation, int part, double now) =>
        _animations.TryGetValue((creation, part), out var a) ? Wrap(a.Angle + a.Speed * (now - a.Since)) : 0f;

    /// <summary>A part's speed, in rad/s (0 when it has none).</summary>
    public float Speed(int creation, int part) => _animations.TryGetValue((creation, part), out var a) ? a.Speed : 0f;

    /// <summary>
    /// Turns a part to <paramref name="angle"/> rad at <paramref name="now"/> (null keeps the angle it has
    /// reached), turning on at <paramref name="speed"/> rad/s. Non-finite numbers count as 0. Returns true
    /// when it changed.
    /// </summary>
    public bool Set(int creation, int part, float? angle, float speed, double now)
    {
        var at = angle is { } given ? (float.IsFinite(given) ? Wrap(given) : 0f) : AngleAt(creation, part, now);
        speed = float.IsFinite(speed) ? speed : 0f;
        if (_animations.TryGetValue((creation, part), out var old) && old.Speed == speed
            && MathF.Abs(Wrap(at - Wrap(old.Angle + old.Speed * (now - old.Since)))) < 1e-5f)
            return false;
        if (at == 0f && speed == 0f)
        {
            if (!_animations.Remove((creation, part)))
                return false;
        }
        else
        {
            _animations[(creation, part)] = (at, speed, now);
        }
        Revision++;
        return true;
    }

    /// <summary>Drops a part's animation (the part went away).</summary>
    public void Forget(int creation, int part)
    {
        if (_animations.Remove((creation, part)))
            Revision++;
    }

    /// <summary>Drops every animation.</summary>
    public void Clear()
    {
        if (_animations.Count == 0)
            return;
        _animations.Clear();
        Revision++;
    }

    /// <summary>Every animated part as it is at <paramref name="now"/>, in creation then part order.</summary>
    public IReadOnlyList<PartAnimation> All(double now) =>
        [.. _animations.Select(kv => new PartAnimation(kv.Key.Creation, kv.Key.Part, AngleAt(kv.Key.Creation, kv.Key.Part, now), kv.Value.Speed))];

    /// <summary>Replaces every animation with <paramref name="animations"/>, as they are at <paramref name="now"/> on this clock.</summary>
    public void Load(IEnumerable<PartAnimation> animations, double now)
    {
        _animations.Clear();
        foreach (var a in animations)
        {
            var angle = float.IsFinite(a.Angle) ? Wrap(a.Angle) : 0f;
            var speed = float.IsFinite(a.Speed) ? a.Speed : 0f;
            if (angle != 0f || speed != 0f)
                _animations[(a.Creation, a.Part)] = (angle, speed, now);
        }
        Revision++;
    }

    private static float Wrap(double angle) => (float)Math.IEEERemainder(angle, 2 * Math.PI);
}
