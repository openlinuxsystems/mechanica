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
    /// The joint's position: for a hinge, B's turn about the axis in rad, wrapped to ±π; for an aimed joint its yaw
    /// (<see cref="Aim(JointFrame, Pose, Pose)"/>); for a slider or spring, how far B's side of the anchor has moved along
    /// the axis, in m.
    /// </summary>
    /// <param name="frame">The joint, in the grid frame.</param>
    /// <param name="poseA">World pose of the joint part's Body.</param>
    /// <param name="poseB">World pose of the other Body.</param>
    public static float Position(JointFrame frame, Pose poseA, Pose poseB)
    {
        var relative = poseA.Inverse().Compose(poseB);
        var axis = frame.Axis.ToNumerics();
        if (frame.Type == ConnectionType.Aimed)
            return Aim(frame, relative.Rotation).Yaw;
        if (frame.Type == ConnectionType.Rotational)
        {
            var q = relative.Rotation;
            return Wrap(2f * MathF.Atan2(Vector3.Dot(new Vector3(q.X, q.Y, q.Z), axis), q.W));
        }
        var anchor = frame.Anchor.ToNumerics();
        return Vector3.Dot(relative.Apply(anchor) - anchor, axis);
    }

    /// <summary>
    /// An aimed joint's yaw and pitch (rad) from the poses of its two Bodies: B's turn relative to A taken as a turn of
    /// yaw about the joint's axis, then of pitch about its pitch axis (turned by the yaw). Yaw is wrapped to ±π.
    /// </summary>
    /// <param name="frame">The aimed joint, in the grid frame (<see cref="JointFrame.Aim"/> set).</param>
    /// <param name="poseA">World pose of the joint part's Body.</param>
    /// <param name="poseB">World pose of the Body built on it.</param>
    public static (float Yaw, float Pitch) Aim(JointFrame frame, Pose poseA, Pose poseB) =>
        Aim(frame, poseA.Inverse().Compose(poseB).Rotation);

    /// <summary>An aimed joint's yaw and pitch from B's rotation relative to A (in A's grid frame).</summary>
    public static (float Yaw, float Pitch) Aim(JointFrame frame, Quaternion relative)
    {
        var u = Vector3.Normalize(frame.Axis.ToNumerics());
        var p = Vector3.Normalize((frame.Aim?.PitchAxis ?? new Data.Vec3(1, 0, 0)).ToNumerics());
        // The pitch turns about p itself, so p ends up where the yaw alone took it.
        var turned = Vector3.Transform(p, relative);
        var yaw = MathF.Atan2(Vector3.Dot(u, Vector3.Cross(p, turned)), Vector3.Dot(p, turned));
        var pitchOnly = Quaternion.Normalize(Quaternion.Inverse(Quaternion.CreateFromAxisAngle(u, yaw)) * relative);
        var pitch = Wrap(2f * MathF.Atan2(Vector3.Dot(new Vector3(pitchOnly.X, pitchOnly.Y, pitchOnly.Z), p), pitchOnly.W));
        return (Wrap(yaw), pitch);
    }

    /// <summary>The rotation an aimed joint at (<paramref name="yaw"/>, <paramref name="pitch"/>) gives B relative to A.</summary>
    public static Quaternion AimRotation(JointFrame frame, float yaw, float pitch)
    {
        var u = Vector3.Normalize(frame.Axis.ToNumerics());
        var p = Vector3.Normalize((frame.Aim?.PitchAxis ?? new Data.Vec3(1, 0, 0)).ToNumerics());
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(u, yaw) * Quaternion.CreateFromAxisAngle(p, pitch));
    }

    /// <summary>
    /// B's pose relative to A (in A's grid frame) with an aimed joint at (<paramref name="yaw"/>, <paramref name="pitch"/>):
    /// the turn about the pivot. A Body built on a turret is drawn at <c>poseA.Compose(AimPose(...))</c>.
    /// </summary>
    public static Pose AimPose(JointFrame frame, float yaw, float pitch)
    {
        var rotation = AimRotation(frame, yaw, pitch);
        var pivot = (frame.Aim?.Pivot ?? frame.Anchor).ToNumerics();
        return new Pose(pivot - Vector3.Transform(pivot, rotation), rotation);
    }

    /// <summary>
    /// Where an aimed joint should turn to so the front of its part (<paramref name="front"/>, square to both axes) points
    /// along <paramref name="direction"/>, both in A's grid frame: the yaw and pitch that do it, unclamped.
    /// </summary>
    public static (float Yaw, float Pitch) AimAt(JointFrame frame, Vector3 front, Vector3 direction)
    {
        var u = Vector3.Normalize(frame.Axis.ToNumerics());
        var p = Vector3.Normalize((frame.Aim?.PitchAxis ?? new Data.Vec3(1, 0, 0)).ToNumerics());
        var f = Vector3.Normalize(front);
        var side = Vector3.Cross(u, f);
        var d = Vector3.Normalize(direction);
        var flat = new Vector2(Vector3.Dot(d, f), Vector3.Dot(d, side));
        var yaw = flat.LengthSquared() < 1e-10f ? 0f : MathF.Atan2(flat.Y, flat.X);
        // A positive pitch about p turns the front toward the axis when p × front is the axis; otherwise away from it.
        var sign = Vector3.Dot(Vector3.Cross(p, f), u) >= 0f ? 1f : -1f;
        var pitch = sign * MathF.Atan2(Vector3.Dot(d, u), flat.Length());
        return (Wrap(yaw), pitch);
    }

    /// <summary>Wraps an angle into (-π, π].</summary>
    public static float Wrap(float angle)
    {
        var wrapped = angle - MathF.Tau * MathF.Round(angle / MathF.Tau);
        return wrapped <= -MathF.PI ? wrapped + MathF.Tau : wrapped;
    }
}
