using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Game;

/// <summary>Conversions between Core's System.Numerics and JSON types and Godot's. Both use Y up and metres.</summary>
public static class VectorConversions
{
    public static System.Numerics.Vector3 ToNumerics(this Godot.Vector3 v) => new(v.X, v.Y, v.Z);

    public static Godot.Vector3 ToGodot(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public static System.Numerics.Vector2 ToNumerics(this Godot.Vector2 v) => new(v.X, v.Y);

    public static Godot.Vector2 ToGodot(this System.Numerics.Vector2 v) => new(v.X, v.Y);

    public static Godot.Vector3 ToGodot(this Vec3 v) => new(v.X, v.Y, v.Z);

    public static Godot.Vector3 ToGodot(this GridCell v) => new(v.X, v.Y, v.Z);

    public static Godot.Quaternion ToGodot(this System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    public static System.Numerics.Quaternion ToNumerics(this Godot.Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    /// <summary>The basis whose columns are the images of the local axes.</summary>
    public static Godot.Basis ToBasis(this GridRotation r) => new(r.XAxis.ToGodot(), r.YAxis.ToGodot(), r.ZAxis.ToGodot());

    public static Godot.Transform3D ToTransform(this Pose pose) =>
        new(new Godot.Basis(pose.Rotation.ToGodot()), pose.Position.ToGodot());

    /// <summary>The pose of a rigid transform; any scale or shear is dropped.</summary>
    public static Pose ToPose(this Godot.Transform3D t) =>
        new(t.Origin.ToNumerics(), t.Basis.Orthonormalized().GetRotationQuaternion().Normalized().ToNumerics());
}
