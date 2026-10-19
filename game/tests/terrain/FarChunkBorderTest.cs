namespace Mechanica.Game.Tests;

/// <summary>
/// <see cref="ChunkBorderTest"/> near the far corner of the world (about 5.9 km out on both axes), where 32-bit
/// positions are least precise: carts must cross the borders as smoothly, and a resting cart must not shake.
/// </summary>
public partial class FarChunkBorderTest : ChunkBorderTest
{
    protected override string Tag => "far-chunk-border";

    protected override int Corner => 93;
}
