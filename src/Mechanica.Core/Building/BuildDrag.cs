using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Building;

/// <summary>
/// The area a Hammer drag covers (UI spec §2, §8, §10): a rectangle of cells in one grid plane, from the cell the
/// drag started on towards the cell under the aim, stepping by the part's size and at most
/// <see cref="HammerSettings.DragMaxPerAxis"/> parts along each side. The plane is named by its normal axis
/// (0 = X, 1 = Y, 2 = Z); Q steps it through the three.
/// </summary>
public static class BuildDrag
{
    /// <summary>The plane after <paramref name="normalAxis"/>: X, then Y, then Z, then X again.</summary>
    public static int NextPlane(int normalAxis) => (normalAxis + 1) % 3;

    /// <summary>The plane's name for the HUD: the two axes it spans.</summary>
    public static string PlaneName(int normalAxis) => normalAxis switch
    {
        0 => "YZ",
        1 => "XZ",
        _ => "XY",
    };

    /// <summary>The normal axis of the face a part goes on (its largest component).</summary>
    public static int AxisOf(GridCell face) =>
        Math.Abs(face.X) >= Math.Abs(face.Y) && Math.Abs(face.X) >= Math.Abs(face.Z) ? 0 : Math.Abs(face.Y) >= Math.Abs(face.Z) ? 1 : 2;

    /// <summary>
    /// The cells from <paramref name="start"/> towards <paramref name="end"/> in the plane through
    /// <paramref name="start"/> with normal <paramref name="normalAxis"/>, <paramref name="step"/> apart along each
    /// axis, at most <paramref name="maxPerAxis"/> along each side. Ordered by distance from the start, so each
    /// cell after the first touches one before it.
    /// </summary>
    public static IReadOnlyList<GridCell> Cells(GridCell start, GridCell end, int normalAxis, GridSize step, int maxPerAxis)
    {
        var size = new[] { Math.Max(1, step.X), Math.Max(1, step.Y), Math.Max(1, step.Z) };
        var from = new[] { start.X, start.Y, start.Z };
        var to = new[] { end.X, end.Y, end.Z };
        var counts = new int[3];
        var signs = new int[3];
        for (var axis = 0; axis < 3; axis++)
        {
            if (axis == normalAxis)
            {
                counts[axis] = 1;
                continue;
            }
            var delta = to[axis] - from[axis];
            signs[axis] = Math.Sign(delta);
            // A part reaches the end cell once any of its cells covers it.
            var steps = delta >= 0 ? delta / size[axis] : -((-delta) / size[axis]);
            counts[axis] = Math.Min(Math.Abs(steps) + 1, Math.Max(1, maxPerAxis));
        }
        var cells = new List<(int Distance, GridCell Cell)>(counts[0] * counts[1] * counts[2]);
        for (var i = 0; i < counts[0]; i++)
            for (var j = 0; j < counts[1]; j++)
                for (var k = 0; k < counts[2]; k++)
                {
                    var cell = new GridCell(from[0] + signs[0] * i * size[0], from[1] + signs[1] * j * size[1], from[2] + signs[2] * k * size[2]);
                    cells.Add((i + j + k, cell));
                }
        return [.. cells.OrderBy(c => c.Distance).ThenBy(c => c.Cell.X).ThenBy(c => c.Cell.Y).ThenBy(c => c.Cell.Z).Select(c => c.Cell)];
    }

    /// <summary>
    /// The cell where a ray from <paramref name="origin"/> along <paramref name="direction"/> (both in grid units of
    /// the frame) crosses the plane through the centre of <paramref name="start"/> with normal
    /// <paramref name="normalAxis"/>; null when the ray runs along the plane or points away from it.
    /// </summary>
    public static GridCell? OnPlane(Vector3 origin, Vector3 direction, GridCell start, int normalAxis)
    {
        var centre = new Vector3(start.X + 0.5f, start.Y + 0.5f, start.Z + 0.5f);
        var along = Component(direction, normalAxis);
        if (MathF.Abs(along) < 1e-4f)
            return null;
        var t = (Component(centre, normalAxis) - Component(origin, normalAxis)) / along;
        if (t <= 0)
            return null;
        var point = origin + direction * t;
        var cell = new GridCell((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y), (int)MathF.Floor(point.Z));
        return normalAxis switch
        {
            0 => cell with { X = start.X },
            1 => cell with { Y = start.Y },
            _ => cell with { Z = start.Z },
        };
    }

    /// <summary>How far apart parts sit in a drag: the part's footprint along each grid axis, turned as placed.</summary>
    public static GridSize StepOf(PartDefinition definition, GridRotation rotation)
    {
        var cells = PartGeometry.Cells(definition, GridCell.Zero, rotation).ToList();
        return new GridSize(cells.Max(c => c.X) - cells.Min(c => c.X) + 1, cells.Max(c => c.Y) - cells.Min(c => c.Y) + 1, cells.Max(c => c.Z) - cells.Min(c => c.Z) + 1);
    }

    /// <summary>
    /// The cell, level with <paramref name="cell"/>, whose <paramref name="face"/> side the ray from
    /// <paramref name="origin"/> along <paramref name="direction"/> (grid units) crosses: where the aim meets the
    /// surface a drag started on. Null when the ray runs along that surface or points away from it.
    /// </summary>
    public static GridCell? OnFace(Vector3 origin, Vector3 direction, GridCell cell, GridCell face)
    {
        var axis = AxisOf(face);
        var outward = Component(face.AsVector.ToNumerics(), axis) > 0 ? 1 : 0;
        var along = Component(direction, axis);
        if (MathF.Abs(along) < 1e-4f)
            return null;
        var t = (Component(cell.AsVector.ToNumerics(), axis) + outward - Component(origin, axis)) / along;
        if (t <= 0)
            return null;
        var point = origin + direction * t;
        var at = new GridCell((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y), (int)MathF.Floor(point.Z));
        return axis switch
        {
            0 => at with { X = cell.X },
            1 => at with { Y = cell.Y },
            _ => at with { Z = cell.Z },
        };
    }

    /// <summary>The four directions a drag can grow in the plane with normal <paramref name="normalAxis"/>.</summary>
    public static IReadOnlyList<GridCell> Directions(int normalAxis) => normalAxis switch
    {
        0 => [new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)],
        1 => [new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1)],
        _ => [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0)],
    };

    private static float Component(Vector3 v, int axis) => axis switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z,
    };
}
