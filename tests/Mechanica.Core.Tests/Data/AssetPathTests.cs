using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Data;

public class AssetPathTests
{
    [Fact]
    public void RelativePathBelongsToCurrentMod()
    {
        Assert.True(AssetPath.TryParse("meshes/bearing.glb", "vanilla", AssetKind.Mesh, out var path, out _));
        Assert.Equal(new AssetPath("vanilla", "meshes/bearing.glb"), path);
    }

    [Fact]
    public void PrefixedPathBelongsToOtherMod()
    {
        Assert.True(AssetPath.TryParse("other_mod:sounds/x.ogg", "vanilla", AssetKind.Sound, out var path, out _));
        Assert.Equal(new AssetPath("other_mod", "sounds/x.ogg"), path);
        Assert.Equal(Path.Combine("root", "sounds", "x.ogg"), path.Resolve("root"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("meshes//a.glb")]
    [InlineData("./a.glb")]
    [InlineData("~/a.glb")]
    [InlineData("other:../a.glb")]
    [InlineData("Other:a.glb")]
    [InlineData("file:///a.glb")]
    [InlineData("meshes/a.GLTF.txt")]
    public void UnsafeOrWrongPathsAreRejected(string text)
    {
        Assert.False(AssetPath.TryParse(text, "m", AssetKind.Mesh, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void ExtensionMatchIsCaseInsensitive() =>
        Assert.True(AssetPath.TryParse("textures/A.PNG", "m", AssetKind.Texture, out _, out _));

    [Theory]
    [InlineData("bearing", "vanilla:bearing")]
    [InlineData("my_mod:bearing", "my_mod:bearing")]
    public void QualifyDefaultsToVanilla(string id, string expected) => Assert.Equal(expected, Identifiers.Qualify(id));
}
