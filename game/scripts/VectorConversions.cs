namespace Mechanica.Game;

/// <summary>Conversions between Core's System.Numerics vectors and Godot's.</summary>
public static class VectorConversions
{
    public static System.Numerics.Vector3 ToNumerics(this Godot.Vector3 v) => new(v.X, v.Y, v.Z);

    public static Godot.Vector3 ToGodot(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
