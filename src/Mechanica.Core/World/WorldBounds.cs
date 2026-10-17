using System.Numerics;
using Mechanica.Core.Creations;

namespace Mechanica.Core.World;

/// <summary>An axis-aligned box in world metres. <see cref="Empty"/> contains nothing and is the identity of <see cref="Union"/>.</summary>
public readonly record struct WorldBounds(Vector3 Min, Vector3 Max)
{
    /// <summary>The box that contains nothing.</summary>
    public static WorldBounds Empty => new(new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity));

    /// <summary>True when the box contains no point.</summary>
    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

    /// <summary>A box of half-size <paramref name="extent"/> around <paramref name="centre"/>.</summary>
    public static WorldBounds Around(Vector3 centre, Vector3 extent) => new(centre - extent, centre + extent);

    /// <summary>The smallest box holding both.</summary>
    public WorldBounds Union(WorldBounds other) =>
        IsEmpty ? other : other.IsEmpty ? this : new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    /// <summary>The smallest box holding this one and a point.</summary>
    public WorldBounds Include(Vector3 point) => IsEmpty ? new(point, point) : new(Vector3.Min(Min, point), Vector3.Max(Max, point));

    /// <summary>Grown by <paramref name="margin"/> metres on every side.</summary>
    public WorldBounds Expand(float margin) => IsEmpty ? this : new(Min - new Vector3(margin), Max + new Vector3(margin));

    /// <summary>The box holding this one after a rigid transform (all eight corners moved).</summary>
    public WorldBounds Transform(Pose pose)
    {
        if (IsEmpty)
            return this;
        var result = Empty;
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? Min.X : Max.X, (i & 2) == 0 ? Min.Y : Max.Y, (i & 4) == 0 ? Min.Z : Max.Z);
            result = result.Include(pose.Apply(corner));
        }
        return result;
    }

    /// <summary>Horizontal (x/z) distance from the box to a point; 0 when the point is above or below it.</summary>
    public float HorizontalDistanceTo(Vector3 point)
    {
        if (IsEmpty)
            return float.PositiveInfinity;
        var dx = MathF.Max(0f, MathF.Max(Min.X - point.X, point.X - Max.X));
        var dz = MathF.Max(0f, MathF.Max(Min.Z - point.Z, point.Z - Max.Z));
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Every chunk whose ground square overlaps the box's x/z footprint, in <see cref="ChunkCoord.Compare"/> order.</summary>
    public IEnumerable<ChunkCoord> Chunks(float chunkSize)
    {
        if (IsEmpty)
            yield break;
        var low = ChunkCoord.At(Min.X, Min.Z, chunkSize);
        var high = ChunkCoord.At(Max.X, Max.Z, chunkSize);
        for (var x = low.X; x <= high.X; x++)
            for (var z = low.Z; z <= high.Z; z++)
                yield return new ChunkCoord(x, z);
    }
}
