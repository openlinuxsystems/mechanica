using System.Numerics;

namespace Mechanica.Core.World;

/// <summary>
/// The scenery past the simulated ground (UI spec §16 Graphics, view distance). Chunks the
/// <see cref="ChunkStreamer"/> loads have colliders and run physics; past them, out to the player's view
/// distance, chunks are drawn only: ground and scattered objects with no colliders, built a few at a time
/// nearest first. View distance is a local graphics setting, so it never changes what the host simulates.
/// </summary>
public static class FarChunks
{
    /// <summary>Fewest metres of view distance (the simulated ground alone).</summary>
    public const float MinViewDistance = 128f;

    /// <summary>
    /// The chunks to draw as scenery for a viewer at <paramref name="viewer"/>: every chunk in the world
    /// whose nearest point is within <paramref name="viewDistance"/> (x/z) and that is not in
    /// <paramref name="loaded"/>, nearest first.
    /// </summary>
    public static IReadOnlyList<ChunkCoord> Wanted(Vector3 viewer, float viewDistance, float chunkSize, Func<ChunkCoord, bool> inWorld,
        IReadOnlySet<ChunkCoord> loaded)
    {
        var centre = ChunkCoord.At(viewer.X, viewer.Z, chunkSize);
        var reach = (int)MathF.Ceiling(viewDistance / chunkSize) + 1;
        var found = new List<(ChunkCoord Chunk, float Distance)>();
        for (var dz = -reach; dz <= reach; dz++)
        {
            for (var dx = -reach; dx <= reach; dx++)
            {
                var chunk = new ChunkCoord(centre.X + dx, centre.Z + dz);
                if (loaded.Contains(chunk) || !inWorld(chunk))
                    continue;
                var distance = Distance(chunk, viewer, chunkSize);
                if (distance <= viewDistance)
                    found.Add((chunk, distance));
            }
        }
        return [.. found.OrderBy(f => f.Distance).ThenBy(f => f.Chunk.X).ThenBy(f => f.Chunk.Z).Select(f => f.Chunk)];
    }

    /// <summary>Metres (x/z) from <paramref name="viewer"/> to the nearest point of <paramref name="chunk"/>; 0 inside it.</summary>
    public static float Distance(ChunkCoord chunk, Vector3 viewer, float chunkSize)
    {
        var (x, z) = chunk.Origin(chunkSize);
        var dx = MathF.Max(0, MathF.Max(x - viewer.X, viewer.X - (x + chunkSize)));
        var dz = MathF.Max(0, MathF.Max(z - viewer.Z, viewer.Z - (z + chunkSize)));
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
