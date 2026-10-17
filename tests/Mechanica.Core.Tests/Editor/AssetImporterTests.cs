using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Editor;

public class AssetImporterTests
{
    [Fact]
    public void CopiesUnderACleanNameAndReusesIdenticalFiles()
    {
        using var mods = new TestMods().Mod("m");
        var outside = Path.Combine(Path.GetDirectoryName(mods.Root)!, "downloads");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "Big Wheel (v2).GLB"), "one");

        Assert.True(AssetImporter.TryImport(Path.Combine(outside, "Big Wheel (v2).GLB"), Path.Combine(mods.Root, "m"), AssetKind.Mesh, out var first, out var error), error);
        Assert.True(AssetImporter.TryImport(Path.Combine(outside, "Big Wheel (v2).GLB"), Path.Combine(mods.Root, "m"), AssetKind.Mesh, out var again, out _));
        File.WriteAllText(Path.Combine(outside, "Big Wheel (v2).GLB"), "two");
        Assert.True(AssetImporter.TryImport(Path.Combine(outside, "Big Wheel (v2).GLB"), Path.Combine(mods.Root, "m"), AssetKind.Mesh, out var changed, out _));

        Assert.Equal("meshes/big_wheel_v2.glb", first);
        Assert.Equal(first, again);
        Assert.Equal("meshes/big_wheel_v2_2.glb", changed);
        Assert.Equal("two", File.ReadAllText(Path.Combine(mods.Root, "m", "meshes", "big_wheel_v2_2.glb")));
    }

    [Fact]
    public void FilesAlreadyInTheModAreNotCopied()
    {
        using var mods = new TestMods().Mod("m").File("m", "meshes/sub/a.glb", "x");

        Assert.True(AssetImporter.TryImport(Path.Combine(mods.Root, "m", "meshes", "sub", "a.glb"), Path.Combine(mods.Root, "m"), AssetKind.Mesh, out var path, out _));

        Assert.Equal("meshes/sub/a.glb", path);
    }

    [Fact]
    public void GltfBringsItsBuffersAndImages()
    {
        using var mods = new TestMods().Mod("m");
        var outside = Path.Combine(Path.GetDirectoryName(mods.Root)!, "art");
        Directory.CreateDirectory(Path.Combine(outside, "tex"));
        File.WriteAllText(Path.Combine(outside, "Crate.gltf"),
            """{ "asset": { "version": "2.0" }, "buffers": [{ "uri": "Crate Data.bin", "byteLength": 3 }, { "uri": "data:application/octet-stream;base64,AAAA", "byteLength": 3 }], "images": [{ "uri": "tex/Wood%20Grain.png" }] }""");
        File.WriteAllText(Path.Combine(outside, "Crate Data.bin"), "abc");
        File.WriteAllText(Path.Combine(outside, "tex", "Wood Grain.png"), "png");

        Assert.True(AssetImporter.TryImport(Path.Combine(outside, "Crate.gltf"), Path.Combine(mods.Root, "m"), AssetKind.Mesh, out var path, out var error), error);

        Assert.Equal("meshes/crate.gltf", path);
        var gltf = File.ReadAllText(Path.Combine(mods.Root, "m", "meshes", "crate.gltf"));
        Assert.Contains("\"crate_data.bin\"", gltf);
        Assert.Contains("\"wood_grain.png\"", gltf);
        Assert.Contains("data:application/octet-stream;base64,AAAA", gltf);
        Assert.Equal("abc", File.ReadAllText(Path.Combine(mods.Root, "m", "meshes", "crate_data.bin")));
        Assert.Equal("png", File.ReadAllText(Path.Combine(mods.Root, "m", "meshes", "wood_grain.png")));
    }

    [Fact]
    public void GltfWithMissingBufferIsRefused()
    {
        using var mods = new TestMods().Mod("m");
        var file = Path.Combine(Path.GetDirectoryName(mods.Root)!, "a.gltf");
        File.WriteAllText(file, """{ "buffers": [{ "uri": "gone.bin" }] }""");

        Assert.False(AssetImporter.TryImport(file, Path.Combine(mods.Root, "m"), AssetKind.Mesh, out _, out var error));
        Assert.Contains("'gone.bin'", error);
    }

    [Fact]
    public void WrongExtensionIsRefused()
    {
        using var mods = new TestMods().Mod("m");
        var file = Path.Combine(Path.GetDirectoryName(mods.Root)!, "a.obj");
        File.WriteAllText(file, "x");

        Assert.False(AssetImporter.TryImport(file, Path.Combine(mods.Root, "m"), AssetKind.Mesh, out _, out var error));
        Assert.Contains(".glb or .gltf", error);
    }

    [Theory]
    [InlineData("Wheel.GLB", "wheel.glb")]
    [InlineData("my--part  v2.gltf", "my_part_v2.gltf")]
    [InlineData("123.png", "asset_123.png")]
    [InlineData("___.ogg", "asset.ogg")]
    public void FileNamesBecomeSnakeCase(string name, string expected) => Assert.Equal(expected, AssetImporter.CleanFileName(name));
}
