using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// The parts map's batch 16 (Scrap Mechanic parity, after the Crops step): the Beehive and the potted plants, each with the
/// wiki's stats (1.5 kg per weight bar, durability = bars, friction and buoyancy = bars / 10), its grid size, a built model
/// and icon, and the wiki's Craftbot recipe at the Fabricator, every one taking a Pigment Flower. The Beehive is a station
/// that works on its own: a Pigment Flower becomes a Beeswax about every 3 minutes, through its two-way port.
/// </summary>
public class GardenPartsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("beehive", 4.5, 7, 0.6, 0.2, false, 5, 3, 4, 3)]
    [InlineData("big_pot", 6, 2, 0.5, 0.2, true, 20, 3, 3, 3)]
    [InlineData("potted_plant", 6, 1, 0.5, 0.2, true, 20, 2, 4, 2)]
    [InlineData("potted_plant_small", 6, 1, 0.5, 0.2, true, 20, 1, 2, 1)]
    [InlineData("potted_seedling", 6, 2, 0.6, 0.1, true, 20, 2, 2, 2)]
    [InlineData("potted_vine", 6, 1, 0.5, 0.2, true, 20, 2, 3, 2)]
    [InlineData("potted_cactus_bloom", 3, 2, 0.6, 0.1, true, 20, 1, 2, 1)]
    [InlineData("potted_blue_flower", 3, 2, 0.6, 0.1, true, 20, 1, 2, 1)]
    [InlineData("potted_cactus", 3, 2, 0.6, 0.1, true, 20, 1, 2, 1)]
    public void GardenPartsTakeTheWikiStatsAndTheirSize(string id, double mass, int durability, double friction, double buoyancy,
        bool flammable, int stack, int x, int y, int z)
    {
        var part = Part(id);

        Assert.Equal((mass, durability, friction, buoyancy, flammable, stack),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, part.StackSize!.Value));
        Assert.Equal(new GridSize(x, y, z), part.Size);
        var vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");
        Assert.Equal($"meshes/parts/{id}.glb", part.Mesh);
        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        Assert.True(File.Exists(Path.Combine(vanilla, part.Mesh!)), $"{id} has its model");
        Assert.True(File.Exists(Path.Combine(vanilla, part.Icon!)), $"{id} has its icon");
    }

    [Theory]
    // The Beehive is a station (Farming, 1900); the pots are Farm and shack decor (3000), the Big Pot first.
    [InlineData("beehive", 30, 1950, "wood_block_2:50", "component_kit:1", "circuit_board:4", "soil_bag:2")]
    [InlineData("big_pot", 5, 3060, "pigment_flower:1", "scrap_stone_block:10")]
    [InlineData("potted_plant", 5, 3061, "pigment_flower:1", "scrap_stone_block:5")]
    [InlineData("potted_plant_small", 5, 3062, "pigment_flower:1", "scrap_stone_block:5")]
    [InlineData("potted_seedling", 5, 3063, "pigment_flower:1", "scrap_stone_block:5")]
    [InlineData("potted_vine", 5, 3064, "pigment_flower:1", "scrap_stone_block:5", "wood_block:2")]
    [InlineData("potted_cactus_bloom", 5, 3065, "pigment_flower:1", "metal_block:1")]
    [InlineData("potted_blue_flower", 5, 3066, "pigment_flower:1", "metal_block:1")]
    [InlineData("potted_cactus", 5, 3067, "pigment_flower:1", "metal_block:1")]
    public void GardenPartsAreMadeAtTheFabricatorInTheirGroup(string id, float time, int order, params string[] inputs)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find(id)!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(inputs.Select(i => $"vanilla:{i}"), recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal($"vanilla:{id}:1", $"{recipe.Outputs.Single().Item}:{recipe.Outputs.Single().Count}");
        Assert.Equal(time, recipe.Time);
        Assert.Equal(order, recipe.Definition.Order);
        Assert.Single(book.All, r => r.Outputs.Any(o => o.Item == $"vanilla:{id}"));
    }

    [Fact]
    public void EveryRecipeOrderIsUsedOnce()
    {
        var book = RecipeBook.Load(Vanilla.Value);

        var shared = book.All.Where(r => r.Definition.Order is not null).GroupBy(r => r.Definition.Order)
            .Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(", ", g.Select(r => r.QualifiedId))}");
        Assert.Empty(shared);
    }

    [Fact]
    public void TheBeehiveTurnsAPigmentFlowerIntoBeeswaxOnItsOwnThroughItsPort()
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var hive = Part("beehive");

        var recipe = Assert.Single(book.ForStation("beehive"));
        Assert.Equal("vanilla:beeswax_beehive", recipe.QualifiedId);
        Assert.Equal(["vanilla:pigment_flower:1"], recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(["vanilla:beeswax:1"], recipe.Outputs.Select(o => $"{o.Item}:{o.Count}"));
        Assert.InRange(recipe.Time, 150f, 210f); // about one every 3 minutes
        Assert.Equal(1951, recipe.Definition.Order);
        Assert.Equal(PartCategory.Station, hive.Category);
        Assert.True(hive.Crafting!.Auto, "it works without an order");
        Assert.Equal(["pigment_flower", "beeswax"], hive.Inventory!.Accepts);
        var port = Assert.Single(hive.IoPorts!);
        Assert.Equal(ItemPortMode.Both, port.Mode ?? ItemPortMode.Both);
        // The standard port placement (docs/io-ports.md): level with the Freezer's on the right side.
        Assert.Equal((Part("freezer").IoPorts!.Single().Position, Part("freezer").IoPorts!.Single().Normal), (port.Position, port.Normal));
        Assert.DoesNotContain(book.ForStation("fabricator"), r => r.QualifiedId == recipe.QualifiedId);
        Assert.Contains("Beehive", Part("beeswax").Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePartFilesAreTheBatchOutput() =>
        new VanillaBlocksTests().PartFilesAreThePartEditorBatchOutput("parts_garden.json", 9);
}
