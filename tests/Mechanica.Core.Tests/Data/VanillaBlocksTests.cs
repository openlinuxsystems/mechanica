using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// Chunk 6: vanilla's structural blocks and wedges. Checks them against CATALOG_IDS.md (ids and sound
/// materials) and the spec's block table (§11.2 and §21.2), that every wedge inherits its block, that
/// they attach on every face (D7), and that the checked-in part files are exactly what the Part Editor
/// batch produces (D3: authored with the editor, never by hand).
/// </summary>
public class VanillaBlocksTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    // CATALOG_IDS.md "Blocks": material → sound material.
    public static TheoryData<string, string> Catalog => new()
    {
        { "wood", "wood" }, { "metal", "metal" }, { "concrete", "concrete" }, { "glass", "glass" },
        { "scrap_wood", "scrap" }, { "scrap_metal", "scrap" }, { "scrap_stone", "concrete" }, { "barrier", "plastic" }, { "tile", "tile" },
        { "brick", "concrete" }, { "net", "fabric" }, { "cardboard", "cardboard" }, { "hull_plate", "metal" },
        { "path_light", "glass" }, { "armored_glass", "glass" }, { "aluminum", "metal" }, { "rusted_metal", "metal" },
        { "insulation", "fabric" }, { "plaster", "concrete" }, { "carpet", "fabric" }, { "diamond_plate", "metal" },
    };

    public static TheoryData<string> Materials => [.. Catalog.Select(row => (string)row[0])];

    // Spec §11.2 (mass, durability, friction, buoyancy, flammable); the other blocks have no numbers in the spec.
    public static TheoryData<string, float, int, float, float, bool> SpecTable => new()
    {
        { "wood", 4, 3, 0.5f, 0.6f, true },
        { "metal", 8, 6, 0.4f, 0.1f, false },
        { "concrete", 10, 8, 0.6f, 0.0f, false },
        { "glass", 3, 1, 0.3f, 0.4f, false },
        { "scrap_wood", 3, 2, 0.5f, 0.5f, true },
        { "scrap_metal", 6, 4, 0.4f, 0.1f, false },
        { "barrier", 5, 4, 0.5f, 0.3f, true },
        { "tile", 7, 4, 0.4f, 0.2f, false },
    };

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no part '{id}'.");

    [Fact]
    public void VanillaLoadsWithoutProblems() =>
        Assert.Empty(Vanilla.Value.Diagnostics);

    [Fact]
    public void BlocksAreExactlyTheCatalogs()
    {
        var expected = Catalog.SelectMany(row => new[] { $"vanilla:{(string)row[0]}_block", $"vanilla:{(string)row[0]}_wedge" }).Order();
        var blocks = Vanilla.Value.Parts.Values.Where(p => p.Definition.Category == PartCategory.Block).Select(p => p.QualifiedId).Order();

        Assert.Equal(expected, blocks);
    }

    [Theory]
    [MemberData(nameof(Catalog))]
    public void BlockIsAOneCellPlaceableWithItsSoundMaterial(string material, string sound)
    {
        foreach (var part in new[] { Part($"{material}_block"), Part($"{material}_wedge") })
        {
            Assert.Equal(PartCategory.Block, part.Category);
            Assert.Equal(GridSize.One, part.Size);
            Assert.Equal(sound, part.SoundMaterial);
            Assert.False(string.IsNullOrWhiteSpace(part.Name));
            Assert.False(string.IsNullOrWhiteSpace(part.Description));
            Assert.Null(part.Sockets);
            Assert.Equal(100, part.StackSize);
            Assert.NotNull(part.Mesh);
            Assert.True(File.Exists(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", part.Mesh)), $"{part.Id}'s mesh {part.Mesh} exists");
        }
    }

    [Theory]
    [MemberData(nameof(SpecTable))]
    public void BlockUsesTheSpecNumbers(string material, float mass, int durability, float friction, float buoyancy, bool flammable)
    {
        var block = Part($"{material}_block");

        Assert.Equal((mass, durability, friction, buoyancy, flammable), (block.Mass, block.Durability, block.Friction, block.Buoyancy, block.Flammable));
    }

    [Theory]
    [MemberData(nameof(Catalog))]
    public void WedgeInheritsItsBlockAtHalfTheMass(string material, string sound)
    {
        var (block, wedge) = (Part($"{material}_block"), Part($"{material}_wedge"));

        Assert.Equal(sound, wedge.SoundMaterial);
        Assert.Equal((block.Durability, block.Friction, block.Buoyancy, block.Flammable), (wedge.Durability, wedge.Friction, wedge.Buoyancy, wedge.Flammable));
        Assert.Equal(DataJsonVisual(block), DataJsonVisual(wedge));
        Assert.Equal(block.Mass / 2, wedge.Mass);
        Assert.Null(block.Collision);
        Assert.Equal(new CollisionDefinition { Shape = CollisionShape.Convex, Mesh = wedge.Mesh }, wedge.Collision);
    }

    [Theory]
    [MemberData(nameof(Materials))]
    public void BlocksAndWedgesAttachOnEveryFace(string material)
    {
        foreach (var rotation in new[] { GridRotation.Identity, GridRotation.All[5], GridRotation.All[17] })
        {
            // A wedge in the middle with a block on all six sides, its slope included: one rigid Body.
            var creation = new Creation(1, Vanilla.Value);
            Assert.True(creation.TryPlacePart($"{material}_wedge", GridCell.Zero, rotation, out _, out var error), error);
            foreach (var side in Sides)
                Assert.True(creation.TryPlacePart($"{material}_block", side, GridRotation.Identity, out _, out error), error);
            Assert.Single(creation.Bodies);
            Assert.Equal(7, creation.Parts.Count);

            // And the other way round: wedges on every side of a block.
            creation = new Creation(1, Vanilla.Value);
            Assert.True(creation.TryPlacePart($"{material}_block", GridCell.Zero, GridRotation.Identity, out _, out error), error);
            foreach (var side in Sides)
                Assert.True(creation.TryPlacePart($"{material}_wedge", side, rotation, out _, out error), error);
            Assert.Single(creation.Bodies);
        }
    }

    [Theory]
    [InlineData("scrap_wood", "scrap_wood:1", "fabricator,portable_fabricator")]
    [InlineData("scrap_metal", "scrap_metal:1", "fabricator,portable_fabricator")]
    [InlineData("metal", "scrap_metal:2,component_kit:1", "fabricator")]
    public void SpecBlockRecipesMakeFourOfTheBlockOrWedge(string material, string inputs, string stations)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        Assert.Empty(book.Diagnostics);
        foreach (var id in new[] { $"{material}_block", $"{material}_wedge" })
        {
            var recipe = book.Find(id) ?? throw new Xunit.Sdk.XunitException($"no recipe makes {id}");
            Assert.Equal(inputs, string.Join(",", recipe.Inputs.Select(i => $"{i.Item[8..]}:{i.Count}")));
            Assert.Equal(stations, string.Join(",", recipe.Stations.Select(s => s[8..])));
            var output = Assert.Single(recipe.Outputs);
            Assert.Equal(($"vanilla:{id}", 4), (output.Item, output.Count));
        }
    }

    [Theory]
    [InlineData("structural_blocks.json", 42)]
    [InlineData("survival.json", 24)]
    [InlineData("joints.json", 4)]
    [InlineData("lights_utility.json", 15)]
    [InlineData("decor.json", 56)]
    [InlineData("containers_stations.json", 10)]
    public void PartFilesAreThePartEditorBatchOutput(string file, int parts)
    {
        var mod = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");
        var templates = Path.Combine(TestMods.RepoRoot, "game", "data", "part_templates");
        var diagnostics = new List<DataDiagnostic>();
        var batchFile = Path.Combine(mod, "editor", "batches", file);
        var batch = PartBatch.Read(File.ReadAllText(batchFile), file, diagnostics);
        var documents = PartBatch.Prepare(batch!, mod, name => File.ReadAllText(Path.Combine(templates, $"{name}.json")),
            file, diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(parts, documents!.Count);
        foreach (var recipe in batch!.Parts!.SelectMany(e => e.Recipes ?? []))
            Assert.Equal(DataJson.Write(recipe), File.ReadAllText(Path.Combine(mod, RecipeExporter.TargetFile(recipe.Id!))));
        foreach (var document in documents)
        {
            Assert.False(document.IsDirty, $"{document.RelativeFile} differs from the batch; re-run it with tools/author-vanilla.sh");
            Assert.Equal(DataJson.Write(document.Definition), File.ReadAllText(Path.Combine(mod, document.RelativeFile!)));
            var source = Path.Combine(mod, PartDocument.SourcePathFor(document.RelativeFile!));
            // Items without a model of their own have no editor source.
            if (document.Source.Pieces.Count > 0 || File.Exists(source))
                Assert.Equal(PartSource.Write(document.Source), File.ReadAllText(source));
        }
    }

    private static readonly GridCell[] Sides =
        [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];

    private static string DataJsonVisual(PartDefinition part) =>
        DataJson.Write(new PartDefinition { Visual = part.Visual });
}
