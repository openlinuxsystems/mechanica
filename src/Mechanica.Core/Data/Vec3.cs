using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Data;

/// <summary>A 3D vector in metres (or a direction), written in JSON as <c>[x, y, z]</c>.</summary>
[JsonConverter(typeof(Vec3JsonConverter))]
public readonly record struct Vec3(float X, float Y, float Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    /// <summary>Euclidean length.</summary>
    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);

    /// <inheritdoc />
    public override string ToString() => JsonNumbers.Array(X, Y, Z);
}

/// <summary>A size in whole build-grid cells (0.25 m each), written as <c>[x, y, z]</c>.</summary>
[JsonConverter(typeof(GridSizeJsonConverter))]
public readonly record struct GridSize(int X, int Y, int Z)
{
    public static readonly GridSize One = new(1, 1, 1);

    /// <summary>The size in metres.</summary>
    public Vec3 Metres => new(
        MechanicaInfo.FromGridCell(X), MechanicaInfo.FromGridCell(Y), MechanicaInfo.FromGridCell(Z));

    /// <inheritdoc />
    public override string ToString() => $"[{X}, {Y}, {Z}]";
}

internal static class JsonNumbers
{
    /// <summary>Shortest round-trippable, culture-invariant float text.</summary>
    public static string Format(float value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Array(float x, float y, float z) => $"[{Format(x)}, {Format(y)}, {Format(z)}]";

    public static T[] ReadTriple<T>(ref Utf8JsonReader reader, Func<Utf8JsonReader, T> read, string what)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected {what} as an array of 3 numbers.");
        var values = new List<T>(3);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number)
                throw new JsonException($"Expected {what} as an array of 3 numbers.");
            values.Add(read(reader));
        }
        if (values.Count != 3)
            throw new JsonException($"Expected {what} as an array of 3 numbers, got {values.Count}.");
        return [.. values];
    }
}

internal sealed class Vec3JsonConverter : JsonConverter<Vec3>
{
    public override Vec3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var v = JsonNumbers.ReadTriple(ref reader, r => r.GetSingle(), "a vector");
        return new Vec3(v[0], v[1], v[2]);
    }

    // Written on one line so part files stay readable.
    public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options) =>
        writer.WriteRawValue(JsonNumbers.Array(value.X, value.Y, value.Z), skipInputValidation: true);
}

internal sealed class GridSizeJsonConverter : JsonConverter<GridSize>
{
    public override GridSize Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var v = JsonNumbers.ReadTriple(ref reader, r =>
            r.TryGetInt32(out var i) ? i : throw new JsonException("Expected whole grid cells."), "a grid size");
        return new GridSize(v[0], v[1], v[2]);
    }

    public override void Write(Utf8JsonWriter writer, GridSize value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString(), skipInputValidation: true);
}
