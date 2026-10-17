using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;

namespace Mechanica.Core.Creations;

/// <summary>
/// A rigid transform: rotate by <see cref="Rotation"/>, then move by <see cref="Position"/> (metres).
/// A Body's pose maps its Creation's grid frame to the world, so a point at grid-frame metres
/// <c>p</c> on that Body sits at <c>pose.Apply(p)</c>.
/// </summary>
/// <param name="Position">Translation in metres.</param>
/// <param name="Rotation">Unit quaternion.</param>
public readonly record struct Pose(Vector3 Position, Quaternion Rotation)
{
    /// <summary>No rotation or translation.</summary>
    public static Pose Identity => new(Vector3.Zero, Quaternion.Identity);

    /// <summary>The pose of a grid rotation followed by a whole-cell offset.</summary>
    public static Pose FromGrid(GridRotation rotation, GridCell offset) =>
        new(offset.Metres.ToNumerics(), ToQuaternion(rotation));

    /// <summary>Transforms a point.</summary>
    public Vector3 Apply(Vector3 point) => Vector3.Transform(point, Rotation) + Position;

    /// <summary>The pose that applies <paramref name="inner"/> first, then this one.</summary>
    public Pose Compose(Pose inner) => new(Apply(inner.Position), Quaternion.Normalize(Rotation * inner.Rotation));

    /// <summary>The pose that undoes this one.</summary>
    public Pose Inverse()
    {
        var inverse = Quaternion.Inverse(Rotation);
        return new Pose(Vector3.Transform(-Position, inverse), inverse);
    }

    /// <summary>The quaternion of a grid rotation.</summary>
    public static Quaternion ToQuaternion(GridRotation rotation)
    {
        var (x, y, z) = (rotation.XAxis.AsVector, rotation.YAxis.AsVector, rotation.ZAxis.AsVector);
        // Columns are the images of the local axes; System.Numerics matrices are row-vector, so they go in rows.
        var m = new Matrix4x4(
            x.X, x.Y, x.Z, 0,
            y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0,
            0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }
}

/// <summary>Conversions between Core's JSON vector and System.Numerics.</summary>
public static class Vec3Numerics
{
    /// <summary>As a System.Numerics vector.</summary>
    public static Vector3 ToNumerics(this Vec3 v) => new(v.X, v.Y, v.Z);

    /// <summary>As a JSON vector.</summary>
    public static Vec3 ToVec3(this Vector3 v) => new(v.X, v.Y, v.Z);
}

/// <summary>Writes a quaternion as <c>[x, y, z, w]</c> on one line.</summary>
internal sealed class QuaternionJsonConverter : JsonConverter<Quaternion>
{
    public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        const string expected = "Expected a rotation as an array of 4 numbers [x, y, z, w].";
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException(expected);
        var values = new List<float>(4);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number)
                throw new JsonException(expected);
            values.Add(reader.GetSingle());
        }
        return values.Count == 4 ? new Quaternion(values[0], values[1], values[2], values[3]) : throw new JsonException(expected);
    }

    public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options) =>
        writer.WriteRawValue(
            $"[{JsonNumbers.Format(value.X)}, {JsonNumbers.Format(value.Y)}, {JsonNumbers.Format(value.Z)}, {JsonNumbers.Format(value.W)}]",
            skipInputValidation: true);
}
