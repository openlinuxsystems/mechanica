using System.Numerics;

namespace Mechanica.Core.Net;

/// <summary>
/// Recent host states of one remote player, sampled between snapshots so movement looks smooth at
/// any snapshot rate. Positions and pitch interpolate linearly, yaw along the shorter arc, and feet on
/// the same Creation Body (<see cref="PlayerGround"/>) in that Body's frame. Sampling past the newest
/// state holds it (no extrapolation).
/// </summary>
public sealed class SnapshotBuffer
{
    /// <summary>States kept.</summary>
    public const int Capacity = 32;

    private readonly List<(uint Tick, PlayerState State)> _states = [];

    /// <summary>States held.</summary>
    public int Count => _states.Count;

    /// <summary>Adds a state. Duplicate ticks are ignored, out-of-order ones are inserted in place.</summary>
    public void Add(uint tick, PlayerState state)
    {
        var index = _states.FindLastIndex(s => s.Tick <= tick);
        if (index >= 0 && _states[index].Tick == tick)
            return;
        _states.Insert(index + 1, (tick, state));
        if (_states.Count > Capacity)
            _states.RemoveAt(0);
    }

    /// <summary>The state at a fractional host tick, or null when empty.</summary>
    public PlayerState? Sample(double tick)
    {
        if (_states.Count == 0)
            return null;
        if (tick <= _states[0].Tick)
            return _states[0].State;
        if (tick >= _states[^1].Tick)
            return _states[^1].State;

        var after = _states.FindIndex(s => s.Tick > tick);
        var (t0, a) = _states[after - 1];
        var (t1, b) = _states[after];
        var t = (float)((tick - t0) / (t1 - t0));
        return a with
        {
            AckSequence = b.AckSequence,
            Position = Vector3.Lerp(a.Position, b.Position, t),
            Velocity = Vector3.Lerp(a.Velocity, b.Velocity, t),
            Yaw = LerpAngle(a.Yaw, b.Yaw, t),
            Pitch = a.Pitch + (b.Pitch - a.Pitch) * t,
            Ground = a.Ground is { } ground && ground.SameBody(b.Ground)
                ? ground with { Feet = Vector3.Lerp(ground.Feet, b.Ground!.Value.Feet, t) }
                : t < 0.5f ? a.Ground : b.Ground,
        };
    }

    /// <summary>Interpolates between two angles in radians along the shorter arc; the result is in (-π, π].</summary>
    public static float LerpAngle(float from, float to, float t)
    {
        var difference = MathF.IEEERemainder(to - from, 2f * MathF.PI);
        var result = MathF.IEEERemainder(from + difference * t, 2f * MathF.PI);
        return result <= -MathF.PI ? result + 2f * MathF.PI : result;
    }
}

/// <summary>
/// Client estimate of which host tick to show remote players and Creations at: the host's tick, as the
/// newest snapshots tell it, minus a fixed delay. The estimate advances one per local physics tick and
/// jumps ahead when a snapshot shows the host further on; late snapshots only nudge it back. The tick
/// shown follows the estimate at between <c>1 - </c><see cref="MaxSkew"/> and <c>1 + </c><see cref="MaxSkew"/>
/// ticks per tick, so a burst or a gap in arrivals never makes motion speed up or stall from one tick to
/// the next; drift beyond <see cref="ResetTicks"/> snaps it.
/// </summary>
public sealed class InterpolationClock
{
    /// <summary>Largest change in how fast the shown tick moves, in ticks per tick (5%: barely seen).</summary>
    public const double MaxSkew = 0.05;

    // How much of the gap to the estimate the shown tick closes per tick, before the MaxSkew limit.
    private const double Gain = 0.1;

    // How much of the way a late snapshot pulls the estimate back.
    private const double LatePull = 0.05;

    private double _target;

    /// <summary>Creates a clock.</summary>
    /// <param name="delayTicks">Ticks behind the newest snapshot (covers snapshot spacing plus jitter).</param>
    /// <param name="resetTicks">Drift beyond this snaps the clock to the target.</param>
    public InterpolationClock(double delayTicks = 2.5 * NetProtocol.SnapshotInterval, double resetTicks = 30)
    {
        DelayTicks = delayTicks;
        ResetTicks = resetTicks;
    }

    /// <summary>Ticks behind the newest snapshot.</summary>
    public double DelayTicks { get; }

    /// <summary>Drift that snaps the clock.</summary>
    public double ResetTicks { get; }

    /// <summary>Host tick to sample at, or null before the first snapshot.</summary>
    public double? RenderTick { get; private set; }

    /// <summary>Moves the clock forward by one local physics tick.</summary>
    public void Advance()
    {
        if (RenderTick is not { } tick)
            return;
        _target++;
        tick++;
        RenderTick = tick + Math.Clamp((_target - tick) * Gain, -MaxSkew, MaxSkew);
    }

    /// <summary>Forgets the host's clock, as before the first snapshot.</summary>
    public void Reset() => RenderTick = null;

    /// <summary>Feeds the tick of a snapshot that just arrived.</summary>
    public void OnSnapshot(uint hostTick)
    {
        var target = hostTick - DelayTicks;
        if (RenderTick is not { } tick || Math.Abs(target - tick) > ResetTicks)
            RenderTick = _target = target;
        else if (target > _target)
            _target = target;
        else
            _target += (target - _target) * LatePull;
    }
}
