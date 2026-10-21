using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// The parts map's structural batches (8 to 10: beams and frames, pipes and valves, ducts, conduits and cables), all
/// in <c>editor/batches/parts_structural.json</c>: Scrap Mechanic's bars (1.5 kg per weight bar, durability = bars,
/// friction and buoyancy = bars / 10), its recipes or a cheap one where it has none, a real model and icon, and a
/// recipe order in the part's group.
/// </summary>
public class StructuralPartsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    /// <summary>One part's numbers: its size (x, up, depth), the wiki's bars converted, and its recipe.</summary>
    private sealed record Row(string Id, GridSize Size, double Mass, int Durability, double Friction, double Buoyancy,
        bool Flammable, int Stack, float Time, int Order, string[] Inputs);

    private static readonly Row[] All =
    [
        // Batch 8a: beams, frames and supports. Our I-beams, column and pillar keep their sizes.
        new("ibeam_stub", new GridSize(1, 1, 1), 6, 4, 0.3, 0.1, false, 20, 2, 301, ["metal_block:1"]),
        new("ibeam_short", new GridSize(1, 1, 4), 6, 4, 0.3, 0.1, false, 20, 2, 302, ["metal_block:3"]),
        new("ibeam_long", new GridSize(1, 1, 8), 12, 4, 0.3, 0.1, false, 20, 2, 303, ["metal_block:6"]),
        new("ibeam_corner", new GridSize(1, 1, 1), 6, 4, 0.3, 0.1, false, 20, 2, 304, ["metal_block:1"]),
        new("ibeam_end", new GridSize(1, 1, 1), 6, 4, 0.3, 0.1, false, 20, 2, 305, ["metal_block:1"]),
        new("ibeam_holder", new GridSize(1, 2, 1), 6, 4, 0.3, 0.1, false, 20, 2, 306, ["metal_block:1"]),
        new("metal_column", new GridSize(1, 8, 1), 9.6, 5, 0.4, 0.1, false, 20, 5, 316, ["metal_block:3"]),
        new("support_bracket", new GridSize(1, 2, 2), 4.5, 5, 0.4, 0.2, false, 20, 5, 325, ["metal_block:1"]),
        new("support_pillar", new GridSize(2, 8, 2), 10.5, 6, 0.7, 0.1, false, 20, 5, 335, ["metal_block:4"]),
        new("support_truss_large", new GridSize(8, 8, 1), 6, 6, 0.4, 0.1, false, 20, 5, 342, ["metal_block:8"]),
        // Batch 8b: Scrap Mechanic's sizes beside ours, the trusses, stands and the tower mast (all uncraftable there).
        new("u_beam", new GridSize(1, 6, 2), 6, 6, 0.5, 0.1, false, 20, 5, 310, ["metal_block:2"]),
        new("metal_column_short", new GridSize(1, 5, 1), 6, 5, 0.4, 0.1, false, 20, 5, 315, ["metal_block:2"]),
        new("metal_support", new GridSize(2, 4, 2), 10.5, 5, 0.6, 0.1, false, 20, 5, 320, ["metal_block:2"]),
        new("stand_support", new GridSize(2, 1, 2), 7.5, 4, 0.3, 0.1, false, 20, 5, 330, ["metal_block:1"]),
        new("lattice_pillar", new GridSize(5, 5, 5), 10.5, 6, 0.7, 0.1, false, 20, 5, 336, ["metal_block:6"]),
        new("support_pillar_stand", new GridSize(7, 1, 7), 10.5, 6, 0.7, 0.1, false, 5, 5, 337, ["metal_block:4"]),
        new("support_truss_small", new GridSize(4, 4, 1), 6, 6, 0.4, 0.1, false, 20, 5, 340, ["metal_block:2"]),
        new("support_truss", new GridSize(8, 4, 1), 6, 6, 0.4, 0.1, false, 20, 5, 341, ["metal_block:4"]),
        new("tower_pole", new GridSize(2, 8, 2), 3, 4, 0.3, 0.3, false, 20, 5, 350, ["metal_block:2"]),
        new("tower_pole_top", new GridSize(2, 4, 2), 3, 4, 0.3, 0.3, false, 20, 5, 351, ["metal_block:2"]),
        // Batch 9a: small pipes (Scrap Mechanic's Small Pipe family and the Pipe fittings) and the valve.
        new("small_pipe_short", new GridSize(1, 1, 2), 3, 4, 0.3, 0.3, false, 20, 2, 401, ["metal_block:1"]),
        new("small_pipe_long", new GridSize(1, 1, 4), 6, 4, 0.3, 0.3, false, 20, 2, 402, ["metal_block:2"]),
        new("small_pipe_bend", new GridSize(1, 1, 1), 3, 4, 0.3, 0.3, false, 20, 2, 403, ["metal_block:1"]),
        new("small_pipe_corner", new GridSize(1, 1, 1), 3, 4, 0.3, 0.3, false, 20, 2, 404, ["metal_block:1"]),
        new("small_pipe_tee", new GridSize(1, 1, 1), 3, 4, 0.3, 0.3, false, 20, 2, 405, ["metal_block:1"]),
        new("small_pipe_cross", new GridSize(1, 1, 1), 3, 4, 0.3, 0.3, false, 20, 2, 406, ["metal_block:1"]),
        new("small_pipe_four_way_tee", new GridSize(1, 1, 1), 3, 4, 0.3, 0.3, false, 20, 2, 407, ["metal_block:1"]),
        new("small_pipe_five_way", new GridSize(1, 1, 1), 3, 4, 0.3, 0.3, false, 20, 2, 408, ["metal_block:1"]),
        new("small_pipe_six_way", new GridSize(1, 1, 1), 3, 4, 0.3, 0.3, false, 20, 2, 409, ["metal_block:1"]),
        new("valve", new GridSize(1, 1, 1), 7.5, 4, 0.4, 0.1, false, 20, 2, 415, ["metal_block:1"]),
        // Batch 9b: our 2 x 2 Large Pipes (Scrap Mechanic's Pipe family) and the 3 x 3 Main Pipes (its Large Pipes).
        new("large_pipe_short", new GridSize(2, 2, 2), 6, 5, 0.3, 0.3, false, 20, 2, 420, ["metal_block:1"]),
        new("large_pipe_long", new GridSize(2, 2, 4), 12, 5, 0.3, 0.3, false, 20, 2, 421, ["metal_block:3"]),
        new("large_pipe_corner", new GridSize(2, 2, 2), 6, 5, 0.3, 0.3, false, 20, 2, 422, ["metal_block:1"]),
        new("large_pipe_tee", new GridSize(2, 2, 2), 6, 5, 0.3, 0.3, false, 20, 2, 423, ["metal_block:1"]),
        new("main_pipe_short", new GridSize(3, 3, 3), 6, 4, 0.5, 0.3, false, 20, 2, 440, ["metal_block:1"]),
        new("main_pipe_long", new GridSize(3, 3, 6), 12, 4, 0.5, 0.3, false, 20, 2, 441, ["metal_block:2"]),
        new("main_pipe_corner", new GridSize(3, 3, 3), 6, 4, 0.5, 0.3, false, 20, 2, 442, ["metal_block:1"]),
        new("main_pipe_tee", new GridSize(3, 3, 3), 6, 4, 0.5, 0.3, false, 20, 2, 443, ["metal_block:1"]),
        new("main_pipe_extension", new GridSize(3, 3, 3), 7.5, 5, 0.5, 0.2, false, 20, 5, 444, ["metal_block:6"]),
        new("main_pipe_mount", new GridSize(3, 3, 1), 7.5, 5, 0.5, 0.2, false, 20, 5, 445, ["metal_block:4"]),
        new("main_pipe_cap", new GridSize(3, 3, 1), 7.5, 5, 0.5, 0.2, false, 20, 5, 446, ["metal_block:4"]),
        new("main_pipe_compressor", new GridSize(5, 5, 8), 7.5, 5, 0.5, 0.2, false, 20, 10, 450, ["metal_block:20", "component_kit:2"]),
    ];

    public static TheoryData<string> Ids => [.. All.Select(r => r.Id)];

    private static Row For(string id) => All.Single(r => r.Id == id);

    [Theory]
    [MemberData(nameof(Ids))]
    public void TakesTheWikiStats(string id)
    {
        var row = For(id);
        var part = Part(id);

        Assert.Equal(row.Size, part.Size);
        Assert.Equal((row.Mass, row.Durability, row.Friction, row.Buoyancy, row.Flammable, row.Stack),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, part.StackSize!.Value));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void IsCraftedAtTheFabricatorInItsGroup(string id)
    {
        var row = For(id);
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find(id)!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(row.Inputs.Select(i => $"vanilla:{i}"), recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(1, recipe.Outputs.Single().Count);
        Assert.Equal(row.Time, recipe.Time);
        Assert.Equal(row.Order, recipe.Definition.Order);
        Assert.Single(book.All, r => r.Outputs.Any(o => o.Item == $"vanilla:{id}"));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void HasItsOwnModelAndIcon(string id)
    {
        var part = Part(id);
        var vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

        Assert.Equal($"meshes/parts/{id}.glb", part.Mesh);
        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        Assert.True(File.Exists(Path.Combine(vanilla, part.Mesh!)), $"{id}'s model");
        Assert.True(File.Exists(Path.Combine(vanilla, part.Icon!)), $"{id}'s icon");
    }

    [Fact]
    public void EveryRecipeOrderIsUnique()
    {
        Assert.Equal(All.Length, All.Select(r => r.Order).Distinct().Count());
    }

    [Fact]
    public void PartFilesAreThePartEditorBatchOutput()
    {
        const string file = "parts_structural.json";
        var mod = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");
        var templates = Path.Combine(TestMods.RepoRoot, "game", "data", "part_templates");
        var diagnostics = new List<DataDiagnostic>();
        var batch = PartBatch.Read(File.ReadAllText(Path.Combine(mod, "editor", "batches", file)), file, diagnostics);
        var documents = PartBatch.Prepare(batch!, mod, name => File.ReadAllText(Path.Combine(templates, $"{name}.json")),
            file, diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(All.Select(r => r.Id).Order(), batch!.Parts!.SelectMany(e => e.Recipes ?? []).Select(r => r.Id!).Order());
        foreach (var recipe in batch.Parts!.SelectMany(e => e.Recipes ?? []))
            Assert.Equal(DataJson.Write(recipe), File.ReadAllText(Path.Combine(mod, RecipeExporter.TargetFile(recipe.Id!))));
        foreach (var document in documents!)
        {
            Assert.False(document.IsDirty, $"{document.RelativeFile} differs from the batch; re-run it with tools/author-vanilla.sh");
            Assert.Equal(DataJson.Write(document.Definition), File.ReadAllText(Path.Combine(mod, document.RelativeFile!)));
            Assert.Equal(PartSource.Write(document.Source), File.ReadAllText(Path.Combine(mod, PartDocument.SourcePathFor(document.RelativeFile!))));
        }
    }
}
