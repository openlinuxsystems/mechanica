using System.Numerics;

namespace Mechanica.Core.Characters;

/// <summary>
/// Two-bone reach (a leg: hip, knee, ankle) for planting the feet on uneven ground. It bends the middle joint so the
/// chain is as long as the way to the target, in the plane the joint already bends in, then swings the whole chain
/// from its root to point at the target. A target out of reach is reached for as far as the chain goes.
/// </summary>
public static class LegReach
{
    // Never quite straight, so the joint always knows which way it bends.
    private const float Slack = 0.999f;

    /// <summary>
    /// Works out the turns that take <paramref name="end"/> to <paramref name="target"/>: <c>Mid</c> turns the lower bone
    /// about the middle joint, then <c>Root</c> turns the whole chain about the root (both in the space the points are in).
    /// <paramref name="pole"/> is the way the joint bends when the chain lies straight.
    /// </summary>
    public static (Quaternion Root, Quaternion Mid) Solve(Vector3 root, Vector3 mid, Vector3 end, Vector3 target, Vector3 pole)
    {
        var upper = mid - root;
        var lower = end - mid;
        var a = upper.Length();
        var b = lower.Length();
        if (a < 1e-5f || b < 1e-5f)
            return (Quaternion.Identity, Quaternion.Identity);
        var reach = Math.Clamp((target - root).Length(), MathF.Abs(a - b) + 1e-4f, (a + b) * Slack);
        // The joint's angle now and the one that makes the chain that long (law of cosines).
        var now = Angle(-upper, lower);
        var want = MathF.Acos(Math.Clamp((a * a + b * b - reach * reach) / (2f * a * b), -1f, 1f));
        var axis = Vector3.Cross(-upper, lower);
        if (axis.LengthSquared() < 1e-10f)
            axis = Vector3.Cross(upper, pole);
        if (axis.LengthSquared() < 1e-10f)
            return (Quaternion.Identity, Quaternion.Identity);
        // Turning the lower bone about this axis opens the joint.
        var mid1 = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), want - now);
        var end1 = mid + Vector3.Transform(lower, mid1);
        var root1 = Between(end1 - root, target - root);
        return (root1, mid1);
    }

    /// <summary>
    /// The shortest turn that takes direction <paramref name="from"/> to direction <paramref name="to"/>
    /// (identity for a zero vector).
    /// </summary>
    public static Quaternion Between(Vector3 from, Vector3 to)
    {
        if (from.LengthSquared() < 1e-12f || to.LengthSquared() < 1e-12f)
            return Quaternion.Identity;
        var f = Vector3.Normalize(from);
        var t = Vector3.Normalize(to);
        var dot = Vector3.Dot(f, t);
        if (dot > 0.999999f)
            return Quaternion.Identity;
        if (dot < -0.999999f)
        {
            // Opposite: half a turn about any axis square to it.
            var side = Vector3.Cross(f, Vector3.UnitX);
            if (side.LengthSquared() < 1e-6f)
                side = Vector3.Cross(f, Vector3.UnitY);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(side), MathF.PI);
        }
        var axis = Vector3.Cross(f, t);
        return Quaternion.Normalize(new Quaternion(axis, 1f + dot));
    }

    private static float Angle(Vector3 u, Vector3 v) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(u), Vector3.Normalize(v)), -1f, 1f));
}

/// <summary>
/// Where the feet go on uneven ground, eased so they never pop: each foot rises or drops by the height of the ground
/// under it against the floor the body stands on, and the hips drop by the most a foot has to go down (so the lower foot
/// can reach). Feet only follow the ground while they are on it; a foot in the air keeps its clip's height.
/// </summary>
public sealed class FootPlanting
{
    private float _left, _right, _hips, _weight;

    /// <summary>How far each foot most goes up or down, in metres (a step steeper than this is left to the stride).</summary>
    public float MaxOffset { get; init; } = 0.35f;

    /// <summary>How fast the offsets follow the ground: the time they take to get most of the way there, in seconds.</summary>
    public float Ease { get; init; } = 0.06f;

    /// <summary>The left foot's height change, in metres (up is positive).</summary>
    public float Left => _left * _weight;

    /// <summary>The right foot's height change, in metres.</summary>
    public float Right => _right * _weight;

    /// <summary>How far the hips drop, in metres (zero or negative).</summary>
    public float Hips => _hips * _weight;

    /// <summary>How much of the planting applies, 0 (none: in the air, seated, swimming) to 1.</summary>
    public float Weight => _weight;

    /// <summary>
    /// Moves on by <paramref name="delta"/> seconds toward the ground under each foot (<paramref name="left"/> and
    /// <paramref name="right"/>: its height against the body's floor, or null where there is none in reach) while
    /// <paramref name="planted"/> (standing or walking on the ground).
    /// </summary>
    public void Update(float? left, float? right, bool planted, float delta)
    {
        if (!(delta > 0f))
            return;
        var k = 1f - MathF.Exp(-delta / MathF.Max(Ease, 1e-3f));
        _weight += ((planted ? 1f : 0f) - _weight) * MathF.Min(1f, k * 0.6f);
        var l = Math.Clamp(Finite(left), -MaxOffset, MaxOffset);
        var r = Math.Clamp(Finite(right), -MaxOffset, MaxOffset);
        _left += (l - _left) * k;
        _right += (r - _right) * k;
        _hips += (MathF.Min(0f, MathF.Min(l, r)) - _hips) * k;
        if (!float.IsFinite(_left + _right + _hips + _weight))
            _left = _right = _hips = _weight = 0f;
    }

    private static float Finite(float? v) => v is { } x && float.IsFinite(x) ? x : 0f;
}
