using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Editor;

public class PartBatchTests
{
    private static readonly byte[] FakeGlb = [0x67, 0x6C, 0x54, 0x46, 2, 0, 0, 0];

    private const string BlockAndWedge = """
        {
          "format_version": 1,
          "parts": [
            {
              "file": "parts/blocks/oak_block.json",
              "template": "block",
              "set": { "name": "Oak Block", "mass": 4, "flammable": true, "visual": { "color": "#a07040" } },
              "model": [{ "kind": "box", "size": [0.25, 0.25, 0.25] }]
            },
            {
              "file": "parts/blocks/oak_wedge.json",
              "base": "oak_block",
              "set": { "name": "Oak Wedge", "mass": 2 },
              "model": [{ "kind": "wedge", "size": [0.25, 0.25, 0.25] }],
              "collision_from_model": true
            }
          ]
        }
        """;

    private static string? Templates(string name) =>
        name == "block" ? TestMods.Block("new_block", """{ "flammable": false, "visual": { "roughness": 0.8, "metallic": 0 } }""") : null;

    private static (IReadOnlyList<PartDocument>? Documents, List<DataDiagnostic> Diagnostics) Prepare(string modDirectory, string json)
    {
        var diagnostics = new List<DataDiagnostic>();
        var batch = PartBatch.Read(json, "batch.json", diagnostics);
        Assert.NotNull(batch);
        return (PartBatch.Prepare(batch, modDirectory, Templates, "batch.json", diagnostics), diagnostics);
    }

    private static void ExportAll(IEnumerable<PartDocument> documents)
    {
        foreach (var document in documents)
        {
            var result = PartExporter.Export(document, document.Source.NeedsBake ? FakeGlb : null);
            Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        }
    }

    [Fact]
    public void BlockAndWedgeAreMadeFromTemplateAndBase()
    {
        using var mods = new TestMods().Mod("m");
        var (documents, diagnostics) = Prepare(Path.Combine(mods.Root, "m"), BlockAndWedge);

        Assert.Empty(diagnostics);
        Assert.Equal(2, documents!.Count);
        var (block, wedge) = (documents[0], documents[1]);
        Assert.Equal("parts/blocks/oak_block.json", block.RelativeFile);
        Assert.Equal(("oak_block", "Oak Block", 4f, true), (block.Definition.Id, block.Definition.Name, block.Definition.Mass, block.Definition.Flammable));
        Assert.Equal("#a07040", block.Definition.Visual!.Color);
        Assert.Equal(0.8f, block.Definition.Visual.Roughness);
        Assert.Equal("meshes/blocks/oak_block.glb", block.Definition.Mesh);

        // The wedge inherits the block's finished definition, then changes only what it sets.
        Assert.Equal(DataJson.Write(block.Definition with { Id = "oak_wedge", Name = "Oak Wedge", Mass = 2, Mesh = "meshes/blocks/oak_wedge.glb" }),
            DataJson.Write(wedge.Definition with { Collision = null }));
        Assert.Equal(new CollisionDefinition { Shape = CollisionShape.Convex, Mesh = "meshes/blocks/oak_wedge.glb" }, wedge.Definition.Collision);
        Assert.Equal(ModelPieceKind.Wedge, Assert.Single(wedge.Source.Pieces).Kind);
    }

    [Fact]
    public void ExportedBatchLoadsAndKeepsItsFolder()
    {
        using var mods = new TestMods().Mod("m");
        var (documents, _) = Prepare(Path.Combine(mods.Root, "m"), BlockAndWedge);

        ExportAll(documents!);

        var loaded = mods.Load();
        Assert.False(loaded.HasErrors, string.Join("\n", loaded.Diagnostics));
        Assert.Equal(["m:oak_block", "m:oak_wedge"], loaded.Parts.Keys.Order());
        Assert.Equal("meshes/blocks/oak_wedge.glb", loaded.Parts["m:oak_wedge"].Definition.Mesh);
        Assert.True(File.Exists(Path.Combine(mods.Root, "m", "editor", "parts", "blocks", "oak_wedge.json")));
    }

    [Fact]
    public void RunningAgainEditsTheExistingParts()
    {
        using var mods = new TestMods().Mod("m");
        var mod = Path.Combine(mods.Root, "m");
        ExportAll(Prepare(mod, BlockAndWedge).Documents!);

        var (documents, diagnostics) = Prepare(mod, BlockAndWedge.Replace("\"mass\": 4", "\"mass\": 5"));
        Assert.Empty(diagnostics);
        Assert.True(documents![0].CanUndo && documents[0].IsDirty, "the change is an undoable edit of the opened part");
        Assert.False(documents[1].IsDirty, "an unchanged part opens clean");
        ExportAll(documents);

        var loaded = mods.Load();
        Assert.False(loaded.HasErrors, string.Join("\n", loaded.Diagnostics));
        Assert.Equal(5f, loaded.Parts["m:oak_block"].Definition.Mass);
        Assert.Equal(2, loaded.Parts.Count);
    }

    [Fact]
    public void SetMergesObjectsAndNullRemovesKeys()
    {
        using var mods = new TestMods().Mod("m");
        var (documents, diagnostics) = Prepare(Path.Combine(mods.Root, "m"), """
            { "parts": [
              { "file": "parts/a.json", "template": "block", "set": { "name": "A", "visual": { "color": "#102030", "opacity": 0.5 } } },
              { "file": "parts/b.json", "base": "a", "set": { "name": "B", "visual": { "roughness": null, "emission": "#ffffff" } } }
            ] }
            """);

        Assert.Empty(diagnostics);
        var visual = documents![1].Definition.Visual!;
        Assert.Equal(("#102030", 0.5f, "#ffffff", (float?)null, 0f), (visual.Color, visual.Opacity, visual.Emission, visual.Roughness, visual.Metallic));
    }

    [Theory]
    [InlineData("""{ "parts": [] }""", "$.parts", "at least one part")]
    [InlineData("""{ "format_version": 2, "parts": [{ "file": "parts/a.json", "template": "block" }] }""", "$.format_version", "not supported")]
    [InlineData("""{ "parts": [{ "template": "block" }] }""", "$.parts[0].file", "file is required")]
    [InlineData("""{ "parts": [{ "file": "meshes/a.json", "template": "block" }] }""", "$.parts[0].file", "under parts/")]
    [InlineData("""{ "parts": [{ "file": "parts/../a.json", "template": "block" }] }""", "$.parts[0].file", "no '..'")]
    [InlineData("""{ "parts": [{ "file": "parts/Big-Block.json", "template": "block" }] }""", "$.parts[0].file", "snake_case")]
    [InlineData("""{ "parts": [{ "file": "parts/a.json" }] }""", "$.parts[0]", "exactly one of template")]
    [InlineData("""{ "parts": [{ "file": "parts/a.json", "template": "block", "base": "b" }] }""", "$.parts[0]", "exactly one of template")]
    [InlineData("""{ "parts": [{ "file": "parts/a.json", "template": "gadget" }] }""", "$.parts[0].template", "no template 'gadget'")]
    [InlineData("""{ "parts": [{ "file": "parts/a.json", "base": "b" }] }""", "$.parts[0].base", "no part 'b'")]
    [InlineData("""{ "parts": [{ "file": "parts/a.json", "template": "block" }, { "file": "parts/x/a.json", "template": "block" }] }""", "$.parts[1].file", "already made")]
    [InlineData("""{ "parts": [{ "file": "parts/a.json", "template": "block", "set": { "id": "b" } }] }""", "$.parts[0].set.id", "file name")]
    [InlineData("""{ "parts": [{ "file": "parts/a.json", "template": "block", "set": { "mass": "heavy" } }] }""", "$.parts[0].set", "not a valid part")]
    [InlineData("""{ "parts": [{ "file": "parts/a.json", "template": "block", "collision_from_model": true }] }""", "$.parts[0].collision_from_model", "needs model pieces")]
    [InlineData("""{ "parts": [{ "file": "parts/a.json", "template": "block", "model": [{ "kind": "box" }] }] }""", "$.model[0].size", "greater than 0")]
    public void BadBatchesGiveClearErrors(string json, string path, string messagePart)
    {
        using var mods = new TestMods().Mod("m");
        var (documents, diagnostics) = Prepare(Path.Combine(mods.Root, "m"), json);

        Assert.Null(documents);
        var error = Assert.Single(diagnostics, d => d.JsonPath == path && d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(messagePart, error.Message);
    }

    [Fact]
    public void UnknownKeysWarnWithASuggestion()
    {
        using var mods = new TestMods().Mod("m");
        var (documents, diagnostics) = Prepare(Path.Combine(mods.Root, "m"),
            """{ "parts": [{ "file": "parts/a.json", "template": "block", "set": { "name": "A" }, "modle": [] }] }""");

        Assert.NotNull(documents);
        var warning = Assert.Single(diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownKey, "$.parts[0].modle"), (warning.Code, warning.JsonPath));
        Assert.Contains("Did you mean 'model'?", warning.Message);
    }
}
