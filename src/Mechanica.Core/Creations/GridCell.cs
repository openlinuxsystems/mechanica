using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;

namespace Mechanica.Core.Creations;

/// <summary>
/// A cell on a Creation's 0.25 m build grid, written in JSON as <c>[x, y, z]</c>. Cell centres sit
/// at <c>cell * GridSize</c> metres in the Creation's grid frame.
/// </summary>
[JsonConverter(typeof(GridCellJsonConverter))]
public readonly record struct GridCell(int X, int Y, int Z)
{
    public static readonly GridCell Zero = new(0, 0, 0);

    /// <summary>The six face directions, in a fixed order: +x, -x, +y, -y, +z, -z.</summary>
    public static IReadOnlyList<GridCell> Directions { get; } =
        [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];

    public static GridCell operator +(GridCell a, GridCell b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static GridCell operator -(GridCell a, GridCell b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static GridCell operator -(GridCell a) => new(-a.X, -a.Y, -a.Z);

    /// <summary>The cell centre in metres, in the Creation's grid frame.</summary>
    public Vec3 Metres => new(MechanicaInfo.FromGridCell(X), MechanicaInfo.FromGridCell(Y), MechanicaInfo.FromGridCell(Z));

    /// <summary>The cell as a vector in cell units.</summary>
    public Vec3 AsVector => new(X, Y, Z);

    /// <inheritdoc />
    public override string ToString() => $"[{X}, {Y}, {Z}]";
}

internal sealed class GridCellJsonConverter : JsonConverter<GridCell>
{
    public override GridCell Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var v = JsonNumbers.ReadTriple(ref reader, r =>
            r.TryGetInt32(out var i) ? i : throw new JsonException("Expected whole grid cells."), "a grid cell");
        return new GridCell(v[0], v[1], v[2]);
    }

    public override void Write(Utf8JsonWriter writer, GridCell value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString(), skipInputValidation: true);
}
