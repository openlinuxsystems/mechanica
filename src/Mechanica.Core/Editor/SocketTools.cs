using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Editor;

/// <summary>
/// What happens when a plain block touches one outer face of a part: the connection it would make, if
/// any, and the socket on that face. The Part Editor draws these as the connection preview.
/// </summary>
/// <param name="Cell">The part's own footprint cell, in part-local cells (origin cell at 0, 0, 0).</param>
/// <param name="Direction">Unit grid direction the face points to.</param>
/// <param name="Connection">The connection a block placed there gets, or null when it would not attach.</param>
/// <param name="Socket">Id of the part's socket on that face, if one sits there.</param>
public sealed record FaceContact(GridCell Cell, GridCell Direction, ConnectionType? Connection, string? Socket);

/// <summary>Socket placement helpers and the connection preview for the Part Editor.</summary>
public static class SocketTools
{
    private const string PreviewPart = "editor:part";
    private const string ProbeBlock = "editor:probe";

    // A plain 1-cell block, only used to ask the Creation graph what would attach. Never saved or simulated.
    private static readonly PartDefinition Probe = new()
    {
        Id = "probe",
        Name = "Probe",
        Category = PartCategory.Block,
        Mass = 1,
        Durability = 1,
        Friction = 0,
        Buoyancy = 0,
        Flammable = false,
        Size = GridSize.One,
    };

    /// <summary>Every outer face of a footprint: cells on the surface and the directions they face out of.</summary>
    public static IEnumerable<(GridCell Cell, GridCell Direction)> OuterFaces(GridSize size)
    {
        for (var x = 0; x < size.X; x++)
            for (var y = 0; y < size.Y; y++)
                for (var z = 0; z < size.Z; z++)
                {
                    var cell = new GridCell(x, y, z);
                    foreach (var d in GridCell.Directions)
                    {
                        var n = cell + d;
                        if (n.X < 0 || n.Y < 0 || n.Z < 0 || n.X >= size.X || n.Y >= size.Y || n.Z >= size.Z)
                            yield return (cell, d);
                    }
                }
    }

    /// <summary>The centre of one cell face in metres, relative to the part centre (socket coordinates).</summary>
    public static Vec3 FaceCentre(GridSize size, GridCell cell, GridCell direction)
    {
        var centre = new Vec3((size.X - 1) / 2f, (size.Y - 1) / 2f, (size.Z - 1) / 2f);
        return (cell.AsVector - centre + direction.AsVector * 0.5f) * MechanicaInfo.GridSize;
    }

    /// <summary>
    /// What a plain block would do against each outer face of <paramref name="part"/>, using the same
    /// attachment rules as building (<see cref="Creation.FindAttachments"/>). Empty for parts that are not
    /// placeable or have no size.
    /// </summary>
    public static IReadOnlyList<FaceContact> PreviewConnections(PartDefinition part)
    {
        if (!part.IsPlaceable || part.Size is not { } size || size.X < 1 || size.Y < 1 || size.Z < 1)
            return [];
        var catalog = new PartCatalog().Add(PreviewPart, part).Add(ProbeBlock, Probe);
        var contacts = new List<FaceContact>();
        foreach (var (cell, direction) in OuterFaces(size))
        {
            var creation = new Creation(0, catalog);
            creation.TryPlacePart(PreviewPart, GridCell.Zero, GridRotation.Identity, out _, out _);
            ConnectionType? type = creation.TryPlacePart(ProbeBlock, cell + direction, GridRotation.Identity, out var probe, out _)
                ? creation.Connections.First(c => c.Involves(probe.Id)).Type
                : null;
            var socket = PartGeometry.SocketOnFace(part, GridCell.Zero, GridRotation.Identity, cell, direction)?.Id;
            contacts.Add(new FaceContact(cell, direction, type, socket));
        }
        return contacts;
    }

    /// <summary>
    /// Moves a socket onto the footprint face its normal points at and snaps it along the face to the
    /// nearest quarter cell (0.0625 m), so it lands on a cell centre or edge where the graph can find it.
    /// The normal is snapped to the closest axis direction.
    /// </summary>
    public static (Vec3 Position, Vec3 Normal) SnapToFace(GridSize size, Vec3 position, Vec3 normal)
    {
        var axis = ClosestAxis(normal);
        var half = size.Metres * 0.5f;
        const float step = MechanicaInfo.GridSize / 4;
        float Snap(float v, float h) => Math.Clamp(MathF.Round(v / step) * step, -h, h);
        var snapped = new Vec3(Snap(position.X, half.X), Snap(position.Y, half.Y), Snap(position.Z, half.Z));
        snapped = axis.X != 0 ? snapped with { X = axis.X * half.X }
            : axis.Y != 0 ? snapped with { Y = axis.Y * half.Y }
            : snapped with { Z = axis.Z * half.Z };
        return (snapped, axis.AsVector);
    }

    /// <summary>A socket centred on the whole side of the footprint that faces <paramref name="direction"/>.</summary>
    public static SocketDefinition SideSocket(GridSize size, GridCell direction, string id, SocketType type)
    {
        var half = size.Metres * 0.5f;
        var position = new Vec3(direction.X * half.X, direction.Y * half.Y, direction.Z * half.Z);
        return new SocketDefinition { Id = id, Type = type, Position = position, Normal = direction.AsVector };
    }

    /// <summary>The unit grid direction closest to <paramref name="v"/> (+Y for a zero vector).</summary>
    public static GridCell ClosestAxis(Vec3 v)
    {
        var (ax, ay, az) = (MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));
        if (ax == 0 && ay == 0 && az == 0)
            return new GridCell(0, 1, 0);
        if (ax >= ay && ax >= az)
            return new GridCell(MathF.Sign(v.X), 0, 0);
        return ay >= az ? new GridCell(0, MathF.Sign(v.Y), 0) : new GridCell(0, 0, MathF.Sign(v.Z));
    }

    /// <summary>A socket id not yet used in <paramref name="part"/>, like <c>socket_3</c>.</summary>
    public static string UnusedSocketId(PartDefinition part, string stem = "socket")
    {
        var used = (part.Sockets ?? []).Select(s => s.Id).ToHashSet();
        for (var n = 1; ; n++)
        {
            var id = $"{stem}_{n}";
            if (!used.Contains(id))
                return id;
        }
    }
}
