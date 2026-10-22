using Mechanica.Core.Data;

namespace Mechanica.Core.Creations;

/// <summary>
/// Grid geometry of a placed part: its footprint cells, centre and socket frames. Positions are in
/// cell units in the Creation's grid frame unless a name says metres.
/// </summary>
public static class PartGeometry
{
    private const float Epsilon = 1e-3f;

    /// <summary>The cells a part covers when its local origin cell is at <paramref name="origin"/>.</summary>
    public static IEnumerable<GridCell> Cells(PartDefinition definition, GridCell origin, GridRotation rotation)
    {
        var size = definition.Size ?? GridSize.One;
        for (var x = 0; x < size.X; x++)
            for (var y = 0; y < size.Y; y++)
                for (var z = 0; z < size.Z; z++)
                    yield return origin + rotation.Apply(new GridCell(x, y, z));
    }

    /// <summary>Footprint centre in cell units.</summary>
    public static Vec3 Centre(PartDefinition definition, GridCell origin, GridRotation rotation) =>
        origin.AsVector + rotation.Apply(LocalCentre(definition));

    /// <summary>Footprint centre in metres.</summary>
    public static Vec3 CentreMetres(PartDefinition definition, GridCell origin, GridRotation rotation) =>
        Centre(definition, origin, rotation) * MechanicaInfo.GridSize;

    /// <summary>
    /// Centre of mass in metres in the Creation's grid frame: the footprint centre moved by the part's
    /// <see cref="PartDefinition.CentreOfMass"/> override, turned with the part.
    /// </summary>
    public static Vec3 CentreOfMassMetres(PartDefinition definition, GridCell origin, GridRotation rotation) =>
        CentreMetres(definition, origin, rotation) + rotation.Apply(definition.CentreOfMass ?? Vec3.Zero);

    /// <summary>
    /// The direction a socket's joint turns about or slides along, in the Creation's grid frame: the
    /// joint's <see cref="JointDefinition.Axis"/>, or the socket normal when it has none.
    /// </summary>
    public static Vec3 JointAxis(SocketDefinition socket, GridRotation rotation) =>
        rotation.Apply(socket.Joint?.Axis ?? socket.Normal ?? Vec3.Zero);

    /// <summary>
    /// An aimed socket's pivot and pitch in the Creation's grid frame (<see cref="JointDefinition.Pivot"/>, or the socket's
    /// point when it has none; the pitch axis defaults to the part's +X).
    /// </summary>
    public static AimFrame AimOf(PartDefinition definition, SocketDefinition socket, GridCell origin, GridRotation rotation)
    {
        var joint = socket.Joint;
        var pivot = joint?.Pivot is { } p
            ? CentreMetres(definition, origin, rotation) + rotation.Apply(p)
            : SocketFrame(definition, socket, origin, rotation).Position;
        return new AimFrame(pivot, rotation.Apply(joint?.PitchAxis ?? new Vec3(1, 0, 0)), joint?.PitchMin ?? 0f, joint?.PitchMax ?? 0f);
    }

    /// <summary>A socket's position (metres) and outward normal in the Creation's grid frame.</summary>
    public static (Vec3 Position, Vec3 Normal) SocketFrame(PartDefinition definition, SocketDefinition socket,
        GridCell origin, GridRotation rotation)
    {
        var position = SocketPoint(definition, socket, origin, rotation) * MechanicaInfo.GridSize;
        return (position, rotation.Apply(socket.Normal ?? Vec3.Zero));
    }

    /// <summary>
    /// The socket that sits on the face of <paramref name="cell"/> facing <paramref name="direction"/>,
    /// if any. A socket sits on a face when its rotated normal equals the direction and its point lies on
    /// that face square (edges included, so a socket centred on a 2-cell face covers both cells).
    /// </summary>
    public static SocketDefinition? SocketOnFace(PartDefinition definition, GridCell origin, GridRotation rotation,
        GridCell cell, GridCell direction)
    {
        foreach (var socket in definition.Sockets ?? [])
        {
            if (socket.Normal is not { } normal || !SameDirection(rotation.Apply(normal), direction))
                continue;
            var offset = SocketPoint(definition, socket, origin, rotation) - cell.AsVector;
            var along = Dot(offset, direction.AsVector);
            var tangent = offset - direction.AsVector * along;
            if (MathF.Abs(along - 0.5f) < Epsilon && MathF.Abs(tangent.X) <= 0.5f + Epsilon
                && MathF.Abs(tangent.Y) <= 0.5f + Epsilon && MathF.Abs(tangent.Z) <= 0.5f + Epsilon)
                return socket;
        }
        return null;
    }

    private static Vec3 LocalCentre(PartDefinition definition)
    {
        var size = definition.Size ?? GridSize.One;
        return new Vec3((size.X - 1) / 2f, (size.Y - 1) / 2f, (size.Z - 1) / 2f);
    }

    private static Vec3 SocketPoint(PartDefinition definition, SocketDefinition socket, GridCell origin, GridRotation rotation) =>
        origin.AsVector + rotation.Apply(LocalCentre(definition) + (socket.Position ?? Vec3.Zero) * (1f / MechanicaInfo.GridSize));

    private static bool SameDirection(Vec3 normal, GridCell direction) =>
        (normal - direction.AsVector).Length < Epsilon;

    private static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
