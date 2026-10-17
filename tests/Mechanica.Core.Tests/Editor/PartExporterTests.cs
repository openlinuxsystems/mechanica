using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Editor;

public class PartExporterTests
{
    private static readonly byte[] FakeGlb = [0x67, 0x6C, 0x54, 0x46, 2, 0, 0, 0];

    [Fact]
    public void NewBlockExportsIntoANewModThatLoads()
    {
        using var mods = new TestMods();
        var doc = PartDocument.New(Path.Combine(mods.Root, "my_mod"), EditorParts.Block(), "oak_block", "Oak Block");
        doc.Apply("Colour", d => d with { Visual = new VisualDefinition { Color = "#a07040", Roughness = 0.8f } });

        var result = PartExporter.Export(doc, null);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.Equal("my_mod:oak_block", result.QualifiedId);
        Assert.Equal(Path.Combine(mods.Root, "my_mod", "parts", "oak_block.json"), result.PartFile);
        Assert.False(doc.IsDirty);
        Assert.Equal("parts/oak_block.json", doc.RelativeFile);
        var loaded = mods.Load();
        Assert.False(loaded.HasErrors, string.Join("\n", loaded.Diagnostics));
        var part = loaded.Parts["my_mod:oak_block"].Definition;
        Assert.Equal(DataJson.Write(doc.Definition), DataJson.Write(part));
        Assert.Equal(DataJson.Write(part), File.ReadAllText(result.PartFile!));
        Assert.False(File.Exists(Path.Combine(mods.Root, "my_mod", "editor", "parts", "oak_block.json")));
    }

    [Fact]
    public void PrimitiveModelIsBakedAndReferenced()
    {
        using var mods = new TestMods().Mod("m");
        var doc = PartDocument.New(Path.Combine(mods.Root, "m"), EditorParts.Block(), "wedge", "Wedge");
        doc.AddPiece(new ModelPiece { Kind = ModelPieceKind.Wedge, Size = new Vec3(0.25f, 0.25f, 0.25f) });

        var missing = PartExporter.Export(doc, null);
        Assert.False(missing.Succeeded);
        Assert.Contains("needs a baked mesh", Assert.Single(missing.Diagnostics).Message);

        var result = PartExporter.Export(doc, FakeGlb);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.Equal("meshes/wedge.glb", doc.Definition.Mesh);
        Assert.Equal(FakeGlb, File.ReadAllBytes(Path.Combine(mods.Root, "m", "meshes", "wedge.glb")));
        var source = File.ReadAllText(Path.Combine(mods.Root, "m", "editor", "parts", "wedge.json"));
        Assert.Equal(PartSource.Write(doc.Source), source);
        Assert.False(mods.Load().HasErrors);

        var reopened = PartDocument.Open(result.PartFile!, [])!;
        Assert.Equal(ModelPieceKind.Wedge, Assert.Single(reopened.Source.Pieces).Kind);
    }

    [Fact]
    public void SingleImportedMeshIsReferencedDirectly()
    {
        using var mods = new TestMods().Mod("m").File("m", "meshes/wheel.glb", "x");
        var doc = PartDocument.New(Path.Combine(mods.Root, "m"), EditorParts.Block(), "wheel", "Wheel");
        doc.AddPiece(ModelPiece.NewMesh("meshes/wheel.glb"));

        var result = PartExporter.Export(doc, null);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.Equal("meshes/wheel.glb", doc.Definition.Mesh);
    }

    [Fact]
    public void InvalidPartIsNotWrittenAndKeepsTheOldFile()
    {
        using var mods = new TestMods().Mod("m").Part("m", "a.json", TestMods.Block("a"));
        var file = Path.Combine(mods.Root, "m", "parts", "a.json");
        var before = File.ReadAllText(file);
        var doc = PartDocument.Open(file, [])!;
        doc.Apply("Break", d => d with { Mass = 0, Mesh = "meshes/nope.glb" });

        var result = PartExporter.Export(doc, null);

        Assert.False(result.Succeeded);
        Assert.Equal(["$.mass", "$.mesh"], result.Diagnostics.Select(d => d.JsonPath));
        Assert.Equal(before, File.ReadAllText(file));
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void RenamingMovesTheFileInItsFolder()
    {
        using var mods = new TestMods().Mod("m").Part("m", "blocks/a.json", TestMods.Block("a"))
            .File("m", "editor/parts/blocks/a.json", """{ "model": [{ "kind": "box", "size": [0.25, 0.25, 0.25] }] }""");
        var doc = PartDocument.Open(Path.Combine(mods.Root, "m", "parts", "blocks", "a.json"), [])!;
        doc.Apply("Rename", d => d with { Id = "b" });

        var result = PartExporter.Export(doc, FakeGlb);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.Equal("parts/blocks/b.json", doc.RelativeFile);
        Assert.Equal("meshes/blocks/b.glb", doc.Definition.Mesh);
        Assert.False(File.Exists(Path.Combine(mods.Root, "m", "parts", "blocks", "a.json")));
        Assert.False(File.Exists(Path.Combine(mods.Root, "m", "editor", "parts", "blocks", "a.json")));
        Assert.True(File.Exists(Path.Combine(mods.Root, "m", "editor", "parts", "blocks", "b.json")));
        Assert.Equal(["m:b"], mods.Load().Parts.Keys);
    }

    [Fact]
    public void DuplicateIdInTheModIsRefused()
    {
        using var mods = new TestMods().Mod("m").Part("m", "other/taken.json", TestMods.Block("taken"));
        var doc = PartDocument.New(Path.Combine(mods.Root, "m"), EditorParts.Block(), "taken", "Taken");

        var result = PartExporter.Export(doc, null);

        Assert.False(result.Succeeded);
        Assert.Contains("already used by parts/other/taken.json", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public void OverridesExportUnderTheOverriddenId()
    {
        using var mods = new TestMods().Mod("base").Part("base", "a.json", TestMods.Block("a"))
            .Mod("tweak", dependencies: """[{ "id": "base", "version": "*" }]""")
            .File("tweak", "overrides/base/a.json", TestMods.Block("a"));
        var doc = PartDocument.Open(Path.Combine(mods.Root, "tweak", "overrides", "base", "a.json"), [])!;
        doc.Apply("Heavier", d => d with { Mass = 40 });

        var result = PartExporter.Export(doc, null);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.Equal("base:a", result.QualifiedId);
        Assert.Equal(40, mods.Load().Parts["base:a"].Definition.Mass);
    }

    [Fact]
    public void BadModFolderNameIsRefused()
    {
        using var mods = new TestMods();
        var doc = PartDocument.New(Path.Combine(mods.Root, "My Mod"), EditorParts.Block(), "a", "A");

        var result = PartExporter.Export(doc, null);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("not a valid mod id"));
        Assert.False(Directory.Exists(Path.Combine(mods.Root, "My Mod")));
    }

    [Theory]
    [InlineData("parts/a.json", "meshes/a.glb")]
    [InlineData("parts/blocks/a.json", "meshes/blocks/a.glb")]
    [InlineData("overrides/vanilla/a.json", "meshes/overrides/vanilla/a.glb")]
    public void BakedMeshPathMirrorsThePartFile(string partFile, string mesh) =>
        Assert.Equal(mesh, PartExporter.BakedMeshPath(partFile));
}
