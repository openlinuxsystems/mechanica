using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// Chunk 6: vanilla's structural blocks and wedges. Checks them against CATALOG_IDS.md (ids and sound
/// materials) and the Scrap Mechanic wiki's block list (stats, stacks, recipes), that every wedge inherits its block, that
/// they attach on every face (D7), and that the checked-in part files are exactly what the Part Editor
/// batch produces (D3: authored with the editor, never by hand).
/// </summary>
public class VanillaBlocksTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    // CATALOG_IDS.md "Blocks" and the Scrap Mechanic wiki's block list: block, wedge, sound material, stack size.
    public static TheoryData<string, string, string, int> Catalog => new()
    {
        { "aluminum_block", "aluminum_wedge", "metal", 256 }, { "armored_glass_block", "armored_glass_wedge", "glass", 256 },
        { "barrier_block", "barrier_wedge", "plastic", 500 }, { "brick_block", "brick_wedge", "concrete", 500 },
        { "bubble_plastic_block", "bubble_plastic_wedge", "plastic", 256 }, { "cardboard_block", "cardboard_wedge", "cardboard", 256 },
        { "carpet_block", "carpet_wedge", "fabric", 500 }, { "concrete_block", "concrete_wedge", "concrete", 500 },
        { "concrete_block_2", "concrete_wedge_2", "concrete", 256 }, { "concrete_block_3", "concrete_wedge_3", "concrete", 256 },
        { "concrete_slab_block", "concrete_slab_wedge", "concrete", 256 }, { "cracked_concrete_block", "cracked_concrete_wedge", "concrete", 256 },
        { "diamond_plate_block", "diamond_plate_wedge", "metal", 256 }, { "extruded_metal_block", "extruded_metal_wedge", "metal", 256 },
        { "glass_block", "glass_wedge", "glass", 256 }, { "glass_tile_block", "glass_tile_wedge", "glass", 256 },
        { "hull_floor_block", "hull_floor_wedge", "metal", 256 }, { "hull_plate_block", "hull_plate_wedge", "metal", 500 },
        { "ice_block", "ice_wedge", "glass", 500 }, { "insulation_block", "insulation_wedge", "fabric", 256 },
        { "metal_block", "metal_wedge", "metal", 500 }, { "metal_block_2", "metal_wedge_2", "metal", 500 },
        { "metal_block_3", "metal_wedge_3", "metal", 500 }, { "net_block", "net_wedge", "fabric", 256 },
        { "painted_wall_block", "painted_wall_wedge", "concrete", 256 }, { "path_light_block", "path_light_wedge", "glass", 256 },
        { "plaster_block", "plaster_wedge", "concrete", 256 }, { "plastic_block", "plastic_wedge", "plastic", 500 },
        { "punched_steel_block", "punched_steel_wedge", "metal", 256 }, { "rusted_metal_block", "rusted_metal_wedge", "metal", 256 },
        { "sand_block", "sand_wedge", "concrete", 500 }, { "scrap_metal_block", "scrap_metal_wedge", "scrap", 256 },
        { "scrap_stone_block", "scrap_stone_wedge", "concrete", 500 }, { "scrap_wood_block", "scrap_wood_wedge", "scrap", 500 },
        { "solid_net_block", "solid_net_wedge", "metal", 256 }, { "square_mesh_block", "square_mesh_wedge", "metal", 256 },
        { "striped_net_block", "striped_net_wedge", "metal", 256 }, { "tile_block", "tile_wedge", "tile", 500 },
        { "wood_block", "wood_wedge", "wood", 500 }, { "wood_block_2", "wood_wedge_2", "wood", 500 },
        { "wood_block_3", "wood_wedge_3", "wood", 500 }, { "worn_metal_block", "worn_metal_wedge", "metal", 256 },
        { "worn_tile_block", "worn_tile_wedge", "tile", 256 },
    };

    public static TheoryData<string, string> Pairs
    {
        get
        {
            var pairs = new TheoryData<string, string>();
            foreach (var row in Catalog)
                pairs.Add((string)row[0], (string)row[1]);
            return pairs;
        }
    }

    // The wiki's stat bars: 1.5 kg a weight bar, durability in bars, friction and buoyancy a tenth a bar. Scrap Metal is
    // the one change: the wiki has it flammable, and metal never burns here.
    public static TheoryData<string, float, int, float, float, bool> SpecTable => new()
    {
        { "aluminum_block", 9f, 4, 0.4f, 0.1f, false },
        { "armored_glass_block", 4.5f, 4, 0.3f, 0.5f, false },
        { "barrier_block", 3f, 2, 0.4f, 0.7f, true },
        { "brick_block", 10.5f, 4, 0.4f, 0.5f, false },
        { "bubble_plastic_block", 1.5f, 1, 0.4f, 0.8f, true },
        { "cardboard_block", 1.5f, 1, 0.4f, 0.2f, true },
        { "carpet_block", 4.5f, 2, 0.4f, 0.4f, true },
        { "concrete_block", 10.5f, 5, 0.6f, 0.5f, false },
        { "concrete_block_2", 12f, 6, 0.6f, 0.5f, false },
        { "concrete_block_3", 13.5f, 7, 0.6f, 0.5f, false },
        { "concrete_slab_block", 9f, 4, 0.7f, 0.1f, false },
        { "cracked_concrete_block", 9f, 3, 0.6f, 0.4f, false },
        { "diamond_plate_block", 7.5f, 5, 0.6f, 0.2f, false },
        { "extruded_metal_block", 4.5f, 4, 0.4f, 0.1f, false },
        { "glass_block", 4.5f, 1, 0.3f, 0.5f, false },
        { "glass_tile_block", 4.5f, 1, 0.3f, 0.5f, false },
        { "hull_floor_block", 6f, 4, 0.5f, 0.1f, false },
        { "hull_plate_block", 7.5f, 7, 0.4f, 0.5f, false },
        { "ice_block", 4.5f, 1, 0.1f, 0.5f, false },
        { "insulation_block", 3f, 2, 0.5f, 0.6f, true },
        { "metal_block", 7.5f, 5, 0.4f, 0.4f, false },
        { "metal_block_2", 7.5f, 7, 0.4f, 0.5f, false },
        { "metal_block_3", 7.5f, 8, 0.4f, 0.6f, false },
        { "net_block", 3f, 3, 0.3f, 0.1f, false },
        { "painted_wall_block", 7.5f, 4, 0.6f, 0.1f, false },
        { "path_light_block", 7.5f, 5, 0.5f, 0.2f, false },
        { "plaster_block", 6f, 3, 0.5f, 0.3f, false },
        { "plastic_block", 6f, 2, 0.4f, 0.7f, false },
        { "punched_steel_block", 4.5f, 4, 0.4f, 0.1f, false },
        { "rusted_metal_block", 6f, 2, 0.5f, 0.8f, false },
        { "sand_block", 9f, 1, 0.4f, 0.1f, false },
        { "scrap_metal_block", 7.5f, 3, 0.4f, 0.2f, false },
        { "scrap_stone_block", 9f, 3, 0.6f, 0.5f, false },
        { "scrap_wood_block", 3f, 2, 0.4f, 0.4f, true },
        { "solid_net_block", 4.5f, 5, 0.4f, 0.1f, false },
        { "square_mesh_block", 3f, 3, 0.2f, 0.2f, false },
        { "striped_net_block", 4.5f, 4, 0.4f, 0.1f, false },
        { "tile_block", 10.5f, 4, 0.4f, 0.5f, false },
        { "wood_block", 3f, 3, 0.5f, 0.6f, true },
        { "wood_block_2", 3f, 5, 0.5f, 0.8f, true },
        { "wood_block_3", 3f, 6, 0.5f, 0.9f, true },
        { "worn_metal_block", 6f, 3, 0.5f, 0.1f, false },
        { "worn_tile_block", 10.5f, 3, 0.4f, 0.3f, false },
    };

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no part '{id}'.");

    [Fact]
    public void VanillaLoadsWithoutProblems() =>
        Assert.Empty(Vanilla.Value.Diagnostics);

    [Fact]
    public void BlocksAreExactlyTheCatalogs()
    {
        var expected = Catalog.SelectMany(row => new[] { $"vanilla:{(string)row[0]}", $"vanilla:{(string)row[1]}" }).Order();
        var blocks = Vanilla.Value.Parts.Values.Where(p => p.Definition.Category == PartCategory.Block).Select(p => p.QualifiedId).Order();

        Assert.Equal(expected, blocks);
    }

    [Theory]
    [MemberData(nameof(Catalog))]
    public void BlockIsAOneCellPlaceableWithItsSoundMaterial(string blockId, string wedgeId, string sound, int stack)
    {
        foreach (var part in new[] { Part(blockId), Part(wedgeId) })
        {
            Assert.Equal(PartCategory.Block, part.Category);
            Assert.Equal(GridSize.One, part.Size);
            Assert.Equal(sound, part.SoundMaterial);
            Assert.False(string.IsNullOrWhiteSpace(part.Name));
            Assert.False(string.IsNullOrWhiteSpace(part.Description));
            Assert.Null(part.Sockets);
            Assert.Equal(stack, part.StackSize);
            Assert.NotNull(part.Mesh);
            Assert.True(File.Exists(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", part.Mesh)), $"{part.Id}'s mesh {part.Mesh} exists");
        }
    }

    [Theory]
    [MemberData(nameof(SpecTable))]
    public void BlockUsesTheSpecNumbers(string blockId, float mass, int durability, float friction, float buoyancy, bool flammable)
    {
        var block = Part(blockId);

        Assert.Equal((mass, durability, friction, buoyancy, flammable), (block.Mass, block.Durability, block.Friction, block.Buoyancy, block.Flammable));
    }

    [Theory]
    [MemberData(nameof(Catalog))]
    public void WedgeInheritsItsBlockAtHalfTheMass(string blockId, string wedgeId, string sound, int stack)
    {
        var (block, wedge) = (Part(blockId), Part(wedgeId));

        Assert.Equal((sound, stack), (wedge.SoundMaterial, wedge.StackSize));
        Assert.Equal((block.Durability, block.Friction, block.Buoyancy, block.Flammable), (wedge.Durability, wedge.Friction, wedge.Buoyancy, wedge.Flammable));
        Assert.Equal(DataJsonVisual(block), DataJsonVisual(wedge));
        Assert.Equal(block.Mass / 2, wedge.Mass);
        Assert.Null(block.Collision);
        Assert.Equal(new CollisionDefinition { Shape = CollisionShape.Convex, Mesh = wedge.Mesh }, wedge.Collision);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void BlocksAndWedgesAttachOnEveryFace(string blockId, string wedgeId)
    {
        foreach (var rotation in new[] { GridRotation.Identity, GridRotation.All[5], GridRotation.All[17] })
        {
            // A wedge in the middle with a block on all six sides, its slope included: one rigid Body.
            var creation = new Creation(1, Vanilla.Value);
            Assert.True(creation.TryPlacePart(wedgeId, GridCell.Zero, rotation, out _, out var error), error);
            foreach (var side in Sides)
                Assert.True(creation.TryPlacePart(blockId, side, GridRotation.Identity, out _, out error), error);
            Assert.Single(creation.Bodies);
            Assert.Equal(7, creation.Parts.Count);

            // And the other way round: wedges on every side of a block.
            creation = new Creation(1, Vanilla.Value);
            Assert.True(creation.TryPlacePart(blockId, GridCell.Zero, GridRotation.Identity, out _, out error), error);
            foreach (var side in Sides)
                Assert.True(creation.TryPlacePart(wedgeId, side, rotation, out _, out error), error);
            Assert.Single(creation.Bodies);
        }
    }

    [Theory]
    [InlineData("metal_block", "scrap_metal_block:20", 10)]
    [InlineData("metal_block_2", "metal_block:20,ember:4,water_bucket_full:1", 10)]
    [InlineData("concrete_block_3", "concrete_block_2:10,metal_block_3:2", 10)]
    [InlineData("glass_tile_block", "sand_block:10,ember:1", 10)]
    [InlineData("sand_block", "scrap_stone_block:5", 10)]
    [InlineData("worn_tile_block", "scrap_stone_block:10,beeswax:1", 10)]
    [InlineData("carpet_block", "cotton:5,paint_ammo:1", 10)]
    [InlineData("punched_steel_block", "metal_block:10", 20)]
    [InlineData("cracked_concrete_block", "concrete_block:5", 10)]
    [InlineData("concrete_slab_block", "scrap_stone_block:10,water_bucket_full:1", 10)]
    public void BlockRecipesAreTheWikis(string id, string inputs, int count)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        Assert.Empty(book.Diagnostics);
        var recipe = book.Find(id) ?? throw new Xunit.Sdk.XunitException($"no recipe makes {id}");
        Assert.Equal(inputs, string.Join(",", recipe.Inputs.Select(i => $"{i.Item[8..]}:{i.Count}")));
        Assert.Equal("fabricator", string.Join(",", recipe.Stations.Select(s => s[8..])));
        Assert.Equal(($"vanilla:{id}", count), (Assert.Single(recipe.Outputs).Item, Assert.Single(recipe.Outputs).Count));
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void EveryWedgeIsCutFromOneOfItsBlock(string blockId, string wedgeId)
    {
        var wedge = RecipeBook.Load(Vanilla.Value).Find(wedgeId) ?? throw new Xunit.Sdk.XunitException($"no recipe makes {wedgeId}");
        Assert.Equal(($"vanilla:{blockId}", 1), (Assert.Single(wedge.Inputs).Item, Assert.Single(wedge.Inputs).Count));
        Assert.Equal(($"vanilla:{wedgeId}", 1), (Assert.Single(wedge.Outputs).Item, Assert.Single(wedge.Outputs).Count));
    }

    [Theory]
    [InlineData("scrap_metal")]
    [InlineData("scrap_wood")]
    public void TheScrapBlockIsTheScrapResourceAndItsWedgeIsCutFromOne(string material)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        Assert.Null(book.Find($"{material}_block"));
        Assert.False(Vanilla.Value.Parts.ContainsKey($"vanilla:{material}"));
        var wedge = book.Find($"{material}_wedge") ?? throw new Xunit.Sdk.XunitException($"no recipe makes {material}_wedge");
        Assert.Equal(($"vanilla:{material}_block", 1), (Assert.Single(wedge.Inputs).Item, Assert.Single(wedge.Inputs).Count));
        Assert.Equal(($"vanilla:{material}_wedge", 1), (Assert.Single(wedge.Outputs).Item, Assert.Single(wedge.Outputs).Count));
    }

    [Theory]
    [InlineData("structural_blocks.json", 86)]
    [InlineData("survival.json", 25)]
    [InlineData("consumables.json", 9)]
    [InlineData("joints.json", 4)]
    [InlineData("decor.json", 26)]
    [InlineData("parts_building.json", 35)]
    [InlineData("containers_stations.json", 3)]
    [InlineData("containers.json", 18)]
    [InlineData("lights_utility.json", 27)]
    [InlineData("parts_stations.json", 3)]
    [InlineData("parts_farming.json", 1)]
    [InlineData("parts_engine.json", 1)]
    [InlineData("parts_transport.json", 4)]
    [InlineData("parts_turret.json", 1)]
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
