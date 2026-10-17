namespace Mechanica.Core.Building;

/// <summary>
/// The height a Lift's deck is shown at as it goes from level to level. The host moves the Lift's level at once; every peer
/// then eases the deck it shows towards the new height, speeding up and slowing down at <see cref="LiftSettings.RaiseAcceleration"/>
/// up to <see cref="LiftSettings.RaiseSpeed"/>, so the column telescopes smoothly and the same on every peer.
/// </summary>
public sealed class LiftMotion
{
    private const float Epsilon = 1e-4f;

    private readonly LiftSettings _settings;

    /// <summary>Starts at rest at <paramref name="height"/>.</summary>
    public LiftMotion(LiftSettings settings, float height)
    {
        _settings = settings;
        Height = height;
    }

    /// <summary>Deck height shown now, in metres above the Lift's base.</summary>
    public float Height { get; private set; }

    /// <summary>Speed now, in metres per second (positive going up).</summary>
    public float Velocity { get; private set; }

    /// <summary>True while the deck is moving.</summary>
    public bool Moving => Velocity != 0f;

    /// <summary>Puts the deck at <paramref name="height"/>, at rest.</summary>
    public void Reset(float height)
    {
        Height = height;
        Velocity = 0f;
    }

    /// <summary>
    /// Moves the deck <paramref name="seconds"/> towards <paramref name="target"/>. True on the step it comes to rest there
    /// after moving (when the caller plays an end-stop sound, say).
    /// </summary>
    public bool Advance(double seconds, float target)
    {
        if (seconds <= 0 || (!Moving && MathF.Abs(target - Height) < Epsilon))
        {
            if (!Moving)
                Height = MathF.Abs(target - Height) < Epsilon ? target : Height;
            return false;
        }
        var dt = (float)seconds;
        var accel = _settings.RaiseAcceleration;
        var distance = target - Height;
        // The fastest speed that can still stop at the target, capped at the top speed.
        var wanted = MathF.Sign(distance) * MathF.Min(_settings.RaiseSpeed, MathF.Sqrt(2f * accel * MathF.Abs(distance)));
        var change = Math.Clamp(wanted - Velocity, -accel * dt, accel * dt);
        Velocity += change;
        var step = Velocity * dt;
        if (MathF.Abs(distance) < Epsilon || (MathF.Sign(step) == MathF.Sign(distance) && MathF.Abs(step) >= MathF.Abs(distance)))
        {
            Height = target;
            Velocity = 0f;
            return true;
        }
        Height += step;
        return false;
    }
}
