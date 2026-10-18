using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Building;

/// <summary>
/// Grid maths behind building by hand: which cell and face a ray hit, where a part goes against that face,
/// and how it is oriented. Positions are metres in a Creation's grid frame unless a name says otherwise.
/// <para>
/// Orientation works like Scrap Mechanic: the part's local +Y points out of the face it is placed on
/// and its local +Z points at the builder; the player then turns it about the face normal
/// (<see cref="Turn"/>) or tips it over (<see cref="Tip"/>). Those two together reach all 24
/// orientations, which is what wedges need.
/// </para>
/// </summary>
public static class BuildAim
{
    /// <summary>A quarter turn about +Y (local +X goes to -Z).</summary>
    public static GridRotation QuarterTurnY { get; } = GridRotation.FromAxes(new GridCell(0, 0, -1), new GridCell(0, 1, 0));

    /// <summary>A quarter turn about +X (local +Y goes to +Z).</summary>
    public static GridRotation QuarterTurnX { get; } = GridRotation.FromAxes(new GridCell(1, 0, 0), new GridCell(0, 0, 1));

    // How far behind the hit point to look for the hit cell, in metres: surfaces sit on cell borders.
    private const float Inset = 0.01f;

    /// <summary>The grid direction closest to <paramref name="v"/> (ties go to the first in <see cref="GridCell.Directions"/>).</summary>
    public static GridCell NearestAxis(Vector3 v) =>
        GridCell.Directions.MaxBy(d => Vector3.Dot(d.AsVector.ToNumerics(), v));

    /// <summary>The cell holding a metres position.</summary>
    public static GridCell CellAt(Vector3 metres) =>
        new(MechanicaInfo.ToGridCell(metres.X), MechanicaInfo.ToGridCell(metres.Y), MechanicaInfo.ToGridCell(metres.Z));

    /// <summary>
    /// The cell a ray hit and the face it entered through, from the hit point and surface normal in
    /// the grid frame. A part placed there goes in <c>Cell + Face</c>.
    /// </summary>
    public static (GridCell Cell, GridCell Face) FromHit(Vector3 point, Vector3 normal)
    {
        var face = NearestAxis(normal);
        return (CellAt(point - face.AsVector.ToNumerics() * Inset), face);
    }

    /// <summary>
    /// The origin cell that puts a part's footprint against a face: the footprint lies wholly on the
    /// <paramref name="face"/> side and its contact layer covers <paramref name="target"/> as centrally
    /// as the footprint allows. For a one-cell part this is <paramref name="target"/> itself.
    /// </summary>
    public static GridCell Anchor(PartDefinition definition, GridRotation rotation, GridCell target, GridCell face) =>
        Anchor(PartGeometry.Cells(definition, GridCell.Zero, rotation).ToList(), PartGeometry.Centre(definition, GridCell.Zero, rotation).ToNumerics(), target, face);

    /// <summary>
    /// <see cref="Anchor(PartDefinition, GridRotation, GridCell, GridCell)"/> for a whole set of cells, such
    /// as a Creation the Weld Tool moves: the offset that puts <paramref name="cells"/> against the face,
    /// their contact layer covering <paramref name="target"/> as centrally as it can.
    /// </summary>
    public static GridCell Anchor(IReadOnlyCollection<GridCell> cells, GridCell target, GridCell face)
    {
        var centre = cells.Aggregate(Vector3.Zero, (sum, c) => sum + c.AsVector.ToNumerics()) / cells.Count;
        return Anchor(cells, centre, target, face);
    }

    private static GridCell Anchor(IReadOnlyCollection<GridCell> cells, Vector3 centre, GridCell target, GridCell face)
    {
        var normal = face.AsVector.ToNumerics();
        var lowest = cells.Min(c => Dot(c, face));
        var contact = cells
            .Where(c => Dot(c, face) == lowest)
            .OrderBy(c =>
            {
                var offset = c.AsVector.ToNumerics() - centre;
                return (offset - normal * Vector3.Dot(offset, normal)).LengthSquared();
            })
            .ThenBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z)
            .First();
        return target - contact;
    }

    /// <summary>
    /// The placement rotation for a part on <paramref name="face"/>: local +Y along the face normal,
    /// local +Z along the grid axis across the face closest to <paramref name="towardViewer"/>, then
    /// the player's own <paramref name="adjust"/> (built with <see cref="Turn"/> and <see cref="Tip"/>)
    /// applied before that.
    /// </summary>
    public static GridRotation Orient(GridCell face, Vector3 towardViewer, GridRotation adjust)
    {
        var across = GridCell.Directions.Where(d => Dot(d, face) == 0).ToList();
        var z = across.MaxBy(d => Vector3.Dot(d.AsVector.ToNumerics(), towardViewer));
        var x = Cross(face, z);
        return GridRotation.Combine(adjust, GridRotation.FromAxes(x, face));
    }

    /// <summary>The adjustment turned a further quarter turn about the face normal.</summary>
    public static GridRotation Turn(GridRotation adjust) => GridRotation.Combine(adjust, QuarterTurnY);

    /// <summary>The adjustment tipped a further quarter turn over the sideways axis.</summary>
    public static GridRotation Tip(GridRotation adjust) => GridRotation.Combine(adjust, QuarterTurnX);

    /// <summary>A world position snapped to the 0.25 m grid on every axis.</summary>
    public static Vector3 Snap(Vector3 world) => CellAt(world).Metres.ToNumerics();

    private static int Dot(GridCell a, GridCell b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static GridCell Cross(GridCell a, GridCell b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
