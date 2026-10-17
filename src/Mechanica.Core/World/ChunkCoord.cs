namespace Mechanica.Core.World;

/// <summary>
/// A world chunk: a square column of the ground, <see cref="StreamingSettings.ChunkSize"/> metres on a
/// side, covering x in [X·size, (X+1)·size) and z in [Z·size, (Z+1)·size). The world is surface-only, so
/// chunks have no vertical index.
/// </summary>
public readonly record struct ChunkCoord(int X, int Z)
{
    /// <summary>The chunk holding a world position.</summary>
    public static ChunkCoord At(float x, float z, float chunkSize) =>
        new((int)MathF.Floor(x / chunkSize), (int)MathF.Floor(z / chunkSize));

    /// <summary>World x/z of the chunk's minimum corner, in metres.</summary>
    public (float X, float Z) Origin(float chunkSize) => (X * chunkSize, Z * chunkSize);

    /// <summary>Orders by X, then Z, for stable output.</summary>
    public static int Compare(ChunkCoord a, ChunkCoord b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z);

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Z})";
}
