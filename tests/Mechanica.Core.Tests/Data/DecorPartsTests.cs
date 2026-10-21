using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// The parts map's furniture and decor batches (13a-15b, Scrap Mechanic parity): each part has the wiki's stats
/// (1.5 kg per weight bar, durability = bars, friction and buoyancy = bars / 10), its grid size, a built model and
/// icon, and the wiki's recipe or a cheap Fabricator one, with an order in its group (docs/part-data-schema.md).
/// </summary>
public class DecorPartsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("shelf_unit", 6, 4, 0.3, 0.1, false, 20, 4, 8, 2)]
    [InlineData("metal_table", 6, 5, 0.4, 0.1, false, 20, 6, 3, 4)]
    [InlineData("stall_door", 6, 4, 0.4, 0.2, false, 20, 4, 7, 1)]
    [InlineData("calendar", 1.5, 2, 0.4, 0.4, true, 1, 2, 3, 1)]
    [InlineData("cubicle_wall", 4.5, 3, 0.3, 0.4, false, 20, 8, 8, 1)]
    [InlineData("mattress", 3, 1, 0.5, 0.2, true, 20, 4, 1, 8)]
    [InlineData("mug", 3, 2, 0.3, 0.3, true, 20, 1, 1, 1)]
    [InlineData("office_chair_base", 4.5, 4, 0.4, 0.1, false, 20, 3, 1, 3)]
    [InlineData("office_table", 6, 5, 0.4, 0.1, false, 20, 8, 1, 4)]
    [InlineData("pillow", 3, 1, 0.4, 0.2, true, 20, 3, 1, 2)]
    [InlineData("shelf_pillar", 6, 4, 0.4, 0.1, false, 20, 1, 3, 1)]
    [InlineData("shelf_support", 6, 4, 0.4, 0.1, false, 20, 1, 1, 1)]
    [InlineData("sink", 6, 3, 0.4, 0.3, false, 20, 3, 3, 2)]
    [InlineData("table_support", 3, 3, 0.3, 0.3, false, 20, 1, 3, 1)]
    [InlineData("shelf_support_tall", 6, 4, 0.4, 0.1, false, 20, 1, 4, 1)]
    [InlineData("toilet_paper", 1.5, 1, 0.3, 0.2, true, 1, 1, 1, 1)]
    public void DecorPartsTakeTheWikiStatsAndTheirSize(string id, double mass, int durability, double friction, double buoyancy,
        bool flammable, int stack, int x, int y, int z)
    {
        var part = Part(id);

        Assert.Equal((mass, durability, friction, buoyancy, flammable, stack),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, stack < 0 ? stack : part.StackSize!.Value));
        Assert.Equal(new GridSize(x, y, z), part.Size);
        var vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");
        Assert.Equal($"meshes/parts/{id}.glb", part.Mesh);
        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        Assert.True(File.Exists(Path.Combine(vanilla, part.Mesh!)), $"{id} has its model");
        Assert.True(File.Exists(Path.Combine(vanilla, part.Icon!)), $"{id} has its icon");
    }

    [Theory]
    [InlineData("shelf_unit", 2, 2050, "scrap_metal_block:16")]
    [InlineData("metal_table", 2, 2040, "scrap_metal_block:20")]
    [InlineData("stall_door", 5, 2064, "metal_block:4")]
    [InlineData("calendar", 2, 2082, "scrap_wood_block:1")]
    [InlineData("cubicle_wall", 5, 2048, "metal_block:4", "cotton:2")]
    [InlineData("mattress", 5, 2070, "cotton:5")]
    [InlineData("mug", 2, 2080, "metal_block:1")]
    [InlineData("office_chair_base", 10, 2046, "metal_block:3")]
    [InlineData("office_table", 5, 2042, "metal_block:4")]
    [InlineData("pillow", 2, 2072, "cotton:2")]
    [InlineData("shelf_pillar", 2, 2052, "metal_block:1")]
    [InlineData("shelf_support", 2, 2054, "metal_block:1")]
    [InlineData("sink", 5, 2066, "metal_block:5")]
    [InlineData("table_support", 2, 2044, "metal_block:1")]
    [InlineData("shelf_support_tall", 2, 2056, "metal_block:1")]
    [InlineData("toilet_paper", 2, 2084, "scrap_wood_block:1")]
    public void DecorPartsAreMadeAtTheFabricatorInTheirGroup(string id, float time, int order, params string[] inputs)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find(id)!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(inputs.Select(i => $"vanilla:{i}"), recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(1, recipe.Outputs.Single().Count);
        Assert.Equal(time, recipe.Time);
        Assert.Equal(order, recipe.Definition.Order);
        Assert.Single(book.All, r => r.Outputs.Any(o => o.Item == $"vanilla:{id}"));
    }

    [Fact]
    public void ThePartFilesAreTheBatchOutput() =>
        new VanillaBlocksTests().PartFilesAreThePartEditorBatchOutput("parts_decor.json", 16);
}
