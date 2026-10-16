using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;

namespace Mechanica.Core.Creations;

/// <summary>
/// One of the 24 axis-aligned orientations a part can take on the build grid. Written as the
/// directions the part's local +X and +Y axes point to, for example <c>"+x+y"</c> (identity) or
/// <c>"+z-x"</c>. The default value is the identity.
/// </summary>
[JsonConverter(typeof(GridRotationJsonConverter))]
public readonly record struct GridRotation
{
    private static readonly GridCell[] Axes = [.. GridCell.Directions];
    private static readonly string[] AxisNames = ["+x", "-x", "+y", "-y", "+z", "-z"];

    // Images of local X, Y and Z for each rotation. Index 0 is the identity.
    private static readonly (GridCell X, GridCell Y, GridCell Z)[] Table = BuildTable();

    private readonly byte _index;

    private GridRotation(int index) => _index = (byte)index;

    /// <summary>The identity rotation.</summary>
    public static GridRotation Identity => default;

    /// <summary>All 24 rotations, identity first.</summary>
    public static IReadOnlyList<GridRotation> All { get; } = [.. Enumerable.Range(0, 24).Select(i => new GridRotation(i))];

    /// <summary>Where local +X points.</summary>
    public GridCell XAxis => Table[_index].X;

    /// <summary>Where local +Y points.</summary>
    public GridCell YAxis => Table[_index].Y;

    /// <summary>Where local +Z points.</summary>
    public GridCell ZAxis => Table[_index].Z;

    /// <summary>The rotation that sends local +X to <paramref name="xAxis"/> and +Y to <paramref name="yAxis"/>.</summary>
    /// <exception cref="ArgumentException">The axes are not unit grid directions at right angles.</exception>
    public static GridRotation FromAxes(GridCell xAxis, GridCell yAxis)
    {
        for (var i = 0; i < Table.Length; i++)
        {
            if (Table[i].X == xAxis && Table[i].Y == yAxis)
                return new GridRotation(i);
        }
        throw new ArgumentException($"{xAxis} and {yAxis} are not perpendicular grid directions.");
    }

    /// <summary>Rotates a cell offset.</summary>
    public GridCell Apply(GridCell v)
    {
        var (x, y, z) = Table[_index];
        return new GridCell(
            v.X * x.X + v.Y * y.X + v.Z * z.X,
            v.X * x.Y + v.Y * y.Y + v.Z * z.Y,
            v.X * x.Z + v.Y * y.Z + v.Z * z.Z);
    }

    /// <summary>Rotates a vector.</summary>
    public Vec3 Apply(Vec3 v)
    {
        var (x, y, z) = Table[_index];
        return new Vec3(
            v.X * x.X + v.Y * y.X + v.Z * z.X,
            v.X * x.Y + v.Y * y.Y + v.Z * z.Y,
            v.X * x.Z + v.Y * y.Z + v.Z * z.Z);
    }

    /// <summary>The rotation that applies <paramref name="second"/> after <paramref name="first"/>.</summary>
    public static GridRotation Combine(GridRotation first, GridRotation second) =>
        FromAxes(second.Apply(first.XAxis), second.Apply(first.YAxis));

    /// <summary>The rotation that undoes this one.</summary>
    public GridRotation Inverse()
    {
        var self = this;
        return All.First(r => Combine(self, r) == Identity);
    }

    /// <summary>Parses the <c>"+x+y"</c> form.</summary>
    public static bool TryParse(string? text, out GridRotation rotation)
    {
        rotation = default;
        if (text is not { Length: 4 })
            return false;
        var x = Array.IndexOf(AxisNames, text[..2]);
        var y = Array.IndexOf(AxisNames, text[2..]);
        if (x < 0 || y < 0 || Axes[x] == Axes[y] || Axes[x] == -Axes[y])
            return false;
        rotation = FromAxes(Axes[x], Axes[y]);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => AxisNames[Array.IndexOf(Axes, XAxis)] + AxisNames[Array.IndexOf(Axes, YAxis)];

    private static (GridCell, GridCell, GridCell)[] BuildTable()
    {
        var table = new List<(GridCell, GridCell, GridCell)>(24);
        foreach (var x in Axes)
        {
            foreach (var y in Axes)
            {
                if (y == x || y == -x)
                    continue;
                // Right-handed: Z = X × Y.
                var z = new GridCell(x.Y * y.Z - x.Z * y.Y, x.Z * y.X - x.X * y.Z, x.X * y.Y - x.Y * y.X);
                table.Add((x, y, z));
            }
        }
        return [.. table];
    }

    internal static GridRotation Parse(string text) =>
        TryParse(text, out var r) ? r : throw new JsonException($"'{text}' is not a grid rotation; expected a form like \"+x+y\".");
}

internal sealed class GridRotationJsonConverter : JsonConverter<GridRotation>
{
    public override GridRotation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? GridRotation.Parse(reader.GetString()!)
            : throw new JsonException("Expected a grid rotation string such as \"+x+y\".");

    public override void Write(Utf8JsonWriter writer, GridRotation value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
