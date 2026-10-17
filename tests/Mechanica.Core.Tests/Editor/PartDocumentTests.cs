using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Editor;

public class PartDocumentTests
{
    [Fact]
    public void NewDocumentTakesIdAndNameAndIsDirty()
    {
        using var mods = new TestMods();

        var doc = PartDocument.New(Path.Combine(mods.Root, "my_mod"), EditorParts.Block(), "oak_block", "Oak Block");

        Assert.Equal("oak_block", doc.Definition.Id);
        Assert.Equal("Oak Block", doc.Definition.Name);
        Assert.Equal("my_mod", doc.ModId);
        Assert.Null(doc.RelativeFile);
        Assert.True(doc.IsDirty);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void UndoAndRedoWalkTheHistory()
    {
        var doc = PartDocument.New("m", EditorParts.Block(), "a", "A");
        var changes = 0;
        doc.Changed += () => changes++;

        doc.Apply("Set mass", d => d with { Mass = 10 });
        doc.Apply("Set friction", d => d with { Friction = 0.9f });

        Assert.Equal("Set friction", doc.UndoLabel);
        Assert.True(doc.Undo());
        Assert.Equal(10, doc.Definition.Mass);
        Assert.Equal(0.5f, doc.Definition.Friction);
        Assert.True(doc.Undo());
        Assert.Equal(4, doc.Definition.Mass);
        Assert.False(doc.Undo());
        Assert.Equal("Set mass", doc.RedoLabel);
        Assert.True(doc.Redo());
        Assert.Equal(10, doc.Definition.Mass);
        Assert.Equal(5, changes);
    }

    [Fact]
    public void NewEditClearsRedoAndEqualEditsAreIgnored()
    {
        var doc = PartDocument.New("m", EditorParts.Block(), "a", "A");
        doc.Apply("Set mass", d => d with { Mass = 10 });
        doc.Undo();

        doc.Apply("Same", d => d with { });
        Assert.True(doc.CanRedo);
        doc.Apply("Set mass", d => d with { Mass = 7 });

        Assert.False(doc.CanRedo);
        Assert.Equal(7, doc.Definition.Mass);
    }

    [Fact]
    public void HistoryIsCapped()
    {
        var doc = PartDocument.New("m", EditorParts.Block(), "a", "A");
        for (var i = 1; i <= PartDocument.HistoryLimit + 10; i++)
            doc.Apply("Set mass", d => d with { Mass = i });

        var undone = 0;
        while (doc.Undo())
            undone++;

        Assert.Equal(PartDocument.HistoryLimit, undone);
        Assert.Equal(10, doc.Definition.Mass);
    }

    [Fact]
    public void SocketPieceAndVariantEditsAreSingleSteps()
    {
        var doc = PartDocument.New("m", EditorParts.Block(), "a", "A");

        doc.AddSocket(new SocketDefinition { Id = "s", Type = SocketType.Structural, Position = Vec3.Zero, Normal = new Vec3(0, 1, 0) });
        doc.SetSocket(0, doc.Definition.Sockets![0] with { Id = "top" });
        doc.AddPiece(ModelPiece.NewBox(new Vec3(1, 1, 1)));
        doc.AddPiece(ModelPiece.NewMesh("meshes/a.glb"));
        doc.MovePiece(1, -1);
        doc.AddVariant(new ColorVariant { Id = "red", Name = "Red", Color = "#ff0000" });

        Assert.Equal("top", Assert.Single(doc.Definition.Sockets!).Id);
        Assert.Equal([ModelPieceKind.Mesh, ModelPieceKind.Box], doc.Source.Pieces.Select(p => p.Kind!.Value));
        Assert.Equal("red", Assert.Single(doc.Definition.Visual!.Variants!).Id);

        doc.RemoveVariant(0);
        doc.RemovePiece(0);
        doc.RemoveSocket(0);

        Assert.Null(doc.Definition.Visual!.Variants);
        Assert.Equal(ModelPieceKind.Box, Assert.Single(doc.Source.Pieces).Kind);
        Assert.Null(doc.Definition.Sockets);
        for (var i = 0; i < 9; i++)
            Assert.True(doc.Undo());
        Assert.False(doc.Undo());
    }

    [Fact]
    public void OpenReadsPartAndShowsItsMeshAsOnePiece()
    {
        var file = Path.Combine(TestMods.FixtureRoot, "sample", "parts", "bearing.json");
        var diagnostics = new List<DataDiagnostic>();

        var doc = PartDocument.Open(file, diagnostics)!;

        Assert.Empty(diagnostics);
        Assert.Equal("sample", doc.ModId);
        Assert.Equal("parts/bearing.json", doc.RelativeFile);
        Assert.Equal("bearing", doc.Definition.Id);
        var piece = Assert.Single(doc.Source.Pieces);
        Assert.Equal("meshes/bearing.gltf", piece.Mesh);
        Assert.False(doc.Source.NeedsBake);
        Assert.False(doc.IsDirty);
        Assert.DoesNotContain(doc.Validate(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void OpenReadsTheEditorSourceWhenThereIsOne()
    {
        using var mods = new TestMods().Mod("m").Part("m", "blocks/b.json", TestMods.Block("b"))
            .File("m", "editor/parts/blocks/b.json", """{ "model": [{ "kind": "box", "size": [0.25, 0.25, 0.25] }] }""");

        var doc = PartDocument.Open(Path.Combine(mods.Root, "m", "parts", "blocks", "b.json"), [])!;

        Assert.Equal("parts/blocks/b.json", doc.RelativeFile);
        Assert.Equal(ModelPieceKind.Box, Assert.Single(doc.Source.Pieces).Kind);
    }

    [Fact]
    public void OpenOutsideAModFails()
    {
        var file = Path.Combine(Path.GetTempPath(), $"loose-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, TestMods.Block());
        var diagnostics = new List<DataDiagnostic>();
        try
        {
            Assert.Null(PartDocument.Open(file, diagnostics));
            Assert.Contains("not inside a mod folder", Assert.Single(diagnostics).Message);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void OpenReportsBadJson()
    {
        using var mods = new TestMods().Mod("m").Part("m", "b.json", "{ \"id\": ");
        var diagnostics = new List<DataDiagnostic>();

        Assert.Null(PartDocument.Open(Path.Combine(mods.Root, "m", "parts", "b.json"), diagnostics));
        Assert.Equal(DiagnosticCodes.InvalidJson, Assert.Single(diagnostics).Code);
    }

    [Fact]
    public void ValidateReportsPartAndSourceProblems()
    {
        using var mods = new TestMods().Mod("m");
        var doc = PartDocument.New(Path.Combine(mods.Root, "m"), EditorParts.Block() with { Mass = -1 }, "a", "A");
        doc.AddPiece(new ModelPiece { Kind = ModelPieceKind.Sphere });

        var paths = doc.Validate().Select(d => (d.File, d.JsonPath)).ToList();

        Assert.Equal([("parts/a.json", "$.mass"), ("editor/parts/a.json", "$.model[0].radius")], paths);
    }
}
