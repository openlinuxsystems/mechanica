using System.Numerics;

namespace Mechanica.Core.Net;

/// <summary>
/// Recent host states of one remote Body, sampled at the client's render tick
/// (<see cref="InterpolationClock"/>) so motion looks smooth whatever arrives. Between two states the
/// position and velocities interpolate linearly and the rotation along the shorter arc. Past the newest
/// state the Body keeps moving on its last velocity for up to a limit, then holds, so a few lost
/// snapshots in a row do not freeze it; before the oldest state it holds the oldest.
/// </summary>
public sealed class BodyTrack
{
    /// <summary>States kept.</summary>
    public const int Capacity = 32;

    private readonly List<(uint Tick, BodyState State)> _states = [];

    /// <summary>Creates a track for a Body whose centre of mass sits at <paramref name="centreOfMass"/> in its grid frame.</summary>
    public BodyTrack(Vector3 centreOfMass = default) => CentreOfMass = centreOfMass;

    /// <summary>Centre of mass in the Body's grid frame; extrapolation turns the Body about it.</summary>
    public Vector3 CentreOfMass { get; }

    /// <summary>States held.</summary>
    public int Count => _states.Count;

    /// <summary>Tick of the newest state, or null when empty.</summary>
    public uint? NewestTick => _states.Count > 0 ? _states[^1].Tick : null;

    /// <summary>Adds a state. Duplicate ticks are ignored, out-of-order ones are inserted in place.</summary>
    public void Add(uint tick, BodyState state)
    {
        var index = _states.FindLastIndex(s => s.Tick <= tick);
        if (index >= 0 && _states[index].Tick == tick)
            return;
        _states.Insert(index + 1, (tick, state));
        if (_states.Count > Capacity)
            _states.RemoveAt(0);
    }

    /// <summary>Drops every state.</summary>
    public void Clear() => _states.Clear();

    /// <summary>The state at a fractional host tick, or null when empty.</summary>
    /// <param name="tick">Host tick to show.</param>
    /// <param name="tickSeconds">Length of one host tick.</param>
    /// <param name="maxExtrapolationTicks">How far past the newest state to keep moving.</param>
    public BodyState? Sample(double tick, float tickSeconds, double maxExtrapolationTicks)
    {
        if (_states.Count == 0)
            return null;
        if (tick <= _states[0].Tick)
            return _states[0].State;
        if (tick >= _states[^1].Tick)
        {
            var ahead = Math.Min(tick - _states[^1].Tick, maxExtrapolationTicks);
            return Extrapolate(_states[^1].State, CentreOfMass, (float)(ahead * tickSeconds));
        }

        var after = _states.FindIndex(s => s.Tick > tick);
        var (t0, a) = _states[after - 1];
        var (t1, b) = _states[after];
        return Interpolate(a, b, (float)((tick - t0) / (t1 - t0)));
    }

    /// <summary>The state a fraction <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static BodyState Interpolate(BodyState a, BodyState b, float t) => new(
        Vector3.Lerp(a.Position, b.Position, t),
        Quaternion.Slerp(a.Rotation, b.Rotation, t),
        Vector3.Lerp(a.LinearVelocity, b.LinearVelocity, t),
        Vector3.Lerp(a.AngularVelocity, b.AngularVelocity, t));

    /// <summary>
    /// Moves a state on by its own velocities for <paramref name="seconds"/>: the centre of mass
    /// (<paramref name="centreOfMass"/>, grid frame) travels at the linear velocity and the Body turns about it.
    /// </summary>
    public static BodyState Extrapolate(BodyState state, Vector3 centreOfMass, float seconds)
    {
        if (seconds <= 0f)
            return state;
        var angle = state.AngularVelocity.Length() * seconds;
        if (angle <= 1e-6f)
            return state with { Position = state.Position + state.LinearVelocity * seconds };
        var rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(state.AngularVelocity), angle) * state.Rotation);
        var centre = state.Position + Vector3.Transform(centreOfMass, state.Rotation) + state.LinearVelocity * seconds;
        return state with { Position = centre - Vector3.Transform(centreOfMass, rotation), Rotation = rotation };
    }
}
