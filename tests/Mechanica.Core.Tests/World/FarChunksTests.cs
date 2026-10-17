using System.Numerics;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

public class FarChunksTests
{
    private static bool Everywhere(ChunkCoord _) => true;

    [Fact]
    public void ScenerySkipsTheSimulatedGroundAndComesNearestFirst()
    {
        var loaded = new HashSet<ChunkCoord> { new(0, 0) };
        var wanted = FarChunks.Wanted(new Vector3(32, 0, 32), 100, 64, Everywhere, loaded);
        Assert.DoesNotContain(new ChunkCoord(0, 0), wanted);
        Assert.Contains(new ChunkCoord(1, 0), wanted);
        Assert.Contains(new ChunkCoord(2, 0), wanted); // its near edge is 96 m away
        Assert.DoesNotContain(new ChunkCoord(3, 0), wanted); // 160 m
        var distances = wanted.Select(c => FarChunks.Distance(c, new Vector3(32, 0, 32), 64)).ToList();
        Assert.Equal(distances.Order(), distances);
    }

    [Fact]
    public void FartherViewDrawsMoreAndTheWorldEdgeStopsIt()
    {
        var near = FarChunks.Wanted(Vector3.Zero, 128, 64, Everywhere, new HashSet<ChunkCoord>());
        var far = FarChunks.Wanted(Vector3.Zero, 512, 64, Everywhere, new HashSet<ChunkCoord>());
        Assert.True(far.Count > near.Count * 4);
        var edged = FarChunks.Wanted(Vector3.Zero, 512, 64, c => c.X >= 0, new HashSet<ChunkCoord>());
        Assert.All(edged, c => Assert.True(c.X >= 0));
    }

    [Fact]
    public void DistanceIsZeroInsideAChunk() =>
        Assert.Equal(0, FarChunks.Distance(new ChunkCoord(1, 1), new Vector3(70, 5, 100), 64));
}
