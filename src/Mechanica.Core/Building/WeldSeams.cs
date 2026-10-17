using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Building;

/// <summary>One face where two welded pieces touch.</summary>
/// <param name="Cell">The cell on the first piece's side.</param>
/// <param name="Normal">The face's direction, out of that cell into the other piece.</param>
public readonly record struct SeamFace(GridCell Cell, GridCell Normal)
{
    /// <summary>The middle of the face, in metres in the grid frame.</summary>
    public Vec3 CentreMetres => (Cell.AsVector + Normal.AsVector * 0.5f) * MechanicaInfo.GridSize;
}

/// <summary>
/// Where the Weld Tool's seam goes (UI spec §8): the faces at which the cells of one piece touch the cells of the
/// other, in one grid. The game draws a blue highlight on each after a weld, and the weld plane square on the first.
/// </summary>
public static class WeldSeams
{
    /// <summary>Every face where a cell of <paramref name="a"/> touches one of <paramref name="b"/>, in a stable order.</summary>
    public static IReadOnlyList<SeamFace> Between(IEnumerable<GridCell> a, IEnumerable<GridCell> b)
    {
        var other = b.ToHashSet();
        var faces = new List<SeamFace>();
        foreach (var cell in a.Distinct().OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z))
        {
            if (other.Contains(cell))
                continue;
            foreach (var direction in GridCell.Directions)
            {
                if (other.Contains(cell + direction))
                    faces.Add(new SeamFace(cell, direction));
            }
        }
        return faces;
    }

    /// <summary>The faces where two parts of one Creation touch.</summary>
    public static IReadOnlyList<SeamFace> Between(Creation creation, int a, int b) => Between(creation.CellsOf(a), creation.CellsOf(b));
}
