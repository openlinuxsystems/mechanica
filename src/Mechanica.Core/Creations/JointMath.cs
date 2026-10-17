using System.Numerics;

namespace Mechanica.Core.Creations;

/// <summary>
/// Reads where a joint is from the poses of the two Bodies it joins. Both Bodies share the Creation's
/// grid frame when they are built, so a joint's position is how far Body B has turned or slid from
/// Body A's grid frame, about or along the joint's axis.
/// </summary>
public static class JointMath
{
    /// <summary>
    /// The joint's position: for a hinge, B's turn about the axis in rad, wrapped to ±π; for a slider or
    /// spring, how far B's side of the anchor has moved along the axis, in m.
    /// </summary>
    /// <param name="frame">The joint, in the grid frame.</param>
    /// <param name="poseA">World pose of the joint part's Body.</param>
    /// <param name="poseB">World pose of the other Body.</param>
    public static float Position(JointFrame frame, Pose poseA, Pose poseB)
    {
        var relative = poseA.Inverse().Compose(poseB);
        var axis = frame.Axis.ToNumerics();
        if (frame.Type == ConnectionType.Rotational)
        {
            var q = relative.Rotation;
            return Wrap(2f * MathF.Atan2(Vector3.Dot(new Vector3(q.X, q.Y, q.Z), axis), q.W));
        }
        var anchor = frame.Anchor.ToNumerics();
        return Vector3.Dot(relative.Apply(anchor) - anchor, axis);
    }

    /// <summary>Wraps an angle into (-π, π].</summary>
    public static float Wrap(float angle)
    {
        var wrapped = angle - MathF.Tau * MathF.Round(angle / MathF.Tau);
        return wrapped <= -MathF.PI ? wrapped + MathF.Tau : wrapped;
    }
}
