using Mechanica.Core.ModTools;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.ModTools;

public class ModFilesTests
{
    [Fact]
    public void WritesReadsListsAndDeletesInsideAMod()
    {
        using var mods = new TestMods().Mod("m");
        var files = new ModFiles(mods.Root);

        Assert.Equal(13, files.Write("m", "parts/blocks/a.json", "{ \"id\": \"a\" }", null));
        files.Write("m", "meshes/a.glb", null, Convert.ToBase64String([1, 2, 3]));

        Assert.Equal(["meshes/a.glb", "mod.json", "parts/blocks/a.json"], files.List("m").Select(f => f.Path));
        Assert.Equal(["parts/blocks/a.json"], files.List("m", "parts").Select(f => f.Path));
        Assert.Equal("{ \"id\": \"a\" }", files.Read("m", "parts/blocks/a.json").Text);
        var mesh = files.Read("m", "meshes/a.glb");
        Assert.Null(mesh.Text);
        Assert.Equal([1, 2, 3], Convert.FromBase64String(mesh.Base64!));

        files.Delete("m", "parts/blocks/a.json");

        Assert.False(Directory.Exists(Path.Combine(mods.Root, "m", "parts")));
        Assert.True(Directory.Exists(Path.Combine(mods.Root, "m")));
    }

    [Theory]
    [InlineData("../other/mod.json")]
    [InlineData("parts/../../x.json")]
    [InlineData("/etc/passwd.json")]
    [InlineData("parts\\a.json")]
    [InlineData("C:/a.json")]
    [InlineData("~/a.json")]
    [InlineData(".git/config.json")]
    [InlineData("parts//a.json")]
    [InlineData("parts/a.exe")]
    [InlineData("parts/a.cs")]
    public void RefusesPathsThatLeaveTheModOrAreNotModFiles(string path)
    {
        using var mods = new TestMods().Mod("m");
        var files = new ModFiles(mods.Root);

        Assert.Throws<ModToolException>(() => files.Write("m", path, "x", null));
        Assert.Throws<ModToolException>(() => files.Read("m", path));
    }

    [Fact]
    public void RefusesUnknownModsAndBadIds()
    {
        using var mods = new TestMods().Mod("m");
        var files = new ModFiles(mods.Root);

        Assert.Contains("mods.create", Assert.Throws<ModToolException>(() => files.List("nope")).Message);
        Assert.Throws<ModToolException>(() => files.List(".."));
        Assert.Throws<ModToolException>(() => files.List("Bad-Id"));
    }

    [Fact]
    public void RefusesLinksOutOfTheMod()
    {
        using var mods = new TestMods().Mod("m");
        var outside = Path.Combine(Path.GetDirectoryName(mods.Root)!, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        Directory.CreateSymbolicLink(Path.Combine(mods.Root, "m", "parts"), outside);
        var files = new ModFiles(mods.Root);

        Assert.Throws<ModToolException>(() => files.Read("m", "parts/secret.txt"));
        Assert.Throws<ModToolException>(() => files.Write("m", "parts/new.json", "{}", null));
        Assert.DoesNotContain(files.List("m"), f => f.Path.StartsWith("parts/", StringComparison.Ordinal));
    }

    [Fact]
    public void TextAndBinaryFilesTakeTheirOwnEncoding()
    {
        using var mods = new TestMods().Mod("m");
        var files = new ModFiles(mods.Root);

        Assert.Throws<ModToolException>(() => files.Write("m", "parts/a.json", null, "AAAA"));
        Assert.Throws<ModToolException>(() => files.Write("m", "textures/a.png", "text", null));
        Assert.Throws<ModToolException>(() => files.Write("m", "textures/a.png", null, "not base64!"));
        Assert.Throws<ModToolException>(() => files.Write("m", "parts/a.json", "{}", "AAAA"));
    }

    [Fact]
    public void VanillaIsReadOnlyOutsideItsEditorBatches()
    {
        using var mods = new TestMods().Mod("vanilla");
        var files = new ModFiles(mods.Root);

        Assert.Contains("batch", Assert.Throws<ModToolException>(() => files.Write("vanilla", "parts/a.json", "{}", null)).Message);
        files.Write("vanilla", "editor/batches/blocks.json", "{}", null);

        Assert.Equal("{}", files.Read("vanilla", "editor/batches/blocks.json").Text);
        Assert.Throws<ModToolException>(() => files.Delete("vanilla", "mod.json"));
    }

    [Fact]
    public void ModJsonCannotBeDeleted()
    {
        using var mods = new TestMods().Mod("m");

        Assert.Throws<ModToolException>(() => new ModFiles(mods.Root).Delete("m", "mod.json"));
    }

    [Fact]
    public void RefusesFilesOverTheLimit()
    {
        using var mods = new TestMods().Mod("m");

        Assert.Throws<ModToolException>(() => new ModFiles(mods.Root).Write("m", "notes.txt", new string('x', ModFiles.MaxFileBytes + 1), null));
    }
}
