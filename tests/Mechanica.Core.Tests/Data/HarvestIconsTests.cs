using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// The parts audit's batch D7 (tools/harvest_icons/): every harvest piece (logs, rods, boulders, chunks) has a rendered
/// 256 x 256 icon, and the Pine Rod has its own heartwood model in place of the Wood Rod's.
/// </summary>
public class HarvestIconsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static string VanillaDir => Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("wood_log")]
    [InlineData("birch_log")]
    [InlineData("pine_log")]
    [InlineData("wood_rod")]
    [InlineData("pine_rod")]
    [InlineData("stone_rod")]
    [InlineData("metal_rod")]
    [InlineData("stone_boulder")]
    [InlineData("metal_boulder")]
    [InlineData("stone_chunk")]
    [InlineData("metal_chunk")]
    public void EachHarvestPieceHasAnIcon(string id)
    {
        var part = Part(id);

        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        var path = Path.Combine(VanillaDir, part.Icon!);
        Assert.True(File.Exists(path), $"{id} has its icon");
        var (width, height) = PngSize(path);
        Assert.Equal((256, 256), (width, height));
    }

    [Fact]
    public void ThePineRodHasItsOwnMesh()
    {
        var pine = Part("pine_rod");
        var wood = Part("wood_rod");

        Assert.Equal("meshes/items/pine_rod.glb", pine.Mesh);
        Assert.NotEqual(wood.Mesh, pine.Mesh);
        var path = Path.Combine(VanillaDir, pine.Mesh!);
        Assert.True(File.Exists(path), "the Pine Rod has its model");
        Assert.NotEqual(File.ReadAllBytes(Path.Combine(VanillaDir, wood.Mesh!)), File.ReadAllBytes(path));
        // Its colours are in the model now, so the part no longer tints the mesh.
        Assert.Equal("#ffffff", pine.Visual?.Color);
        Assert.Equal(wood.Size, pine.Size);
    }

    // Width and height from a PNG's IHDR chunk (big-endian, bytes 16..23).
    private static (int Width, int Height) PngSize(string path)
    {
        var head = File.ReadAllBytes(path).AsSpan(0, 24);
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[16..]),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[20..]));
    }
}
