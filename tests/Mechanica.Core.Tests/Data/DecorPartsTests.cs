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
    [InlineData("hay_bale", 3, 1, 0.4, 0.2, true, 20, 4, 2, 2)]
    [InlineData("wooden_crate", 6, 2, 0.6, 0.5, true, -1, 3, 3, 3)]
    [InlineData("duck_statuette", 3, 4, 0.5, 0.8, true, 1, 2, 2, 2)]
    [InlineData("produce_box_mixed", 6, 1, 0.5, 0.2, true, 20, 4, 4, 4)]
    [InlineData("produce_box_carrot", 6, 1, 0.5, 0.2, true, 20, 4, 4, 4)]
    [InlineData("produce_box_tomato", 6, 1, 0.5, 0.2, true, 20, 4, 4, 4)]
    [InlineData("grass_planter", 6, 2, 0.4, 0.2, true, 20, 4, 1, 4)]
    [InlineData("mannequin_boot", 3, 7, 0.7, 0.1, true, 20, 2, 2, 3)]
    [InlineData("mannequin_hand", 3, 7, 0.7, 0.1, true, 20, 1, 2, 1)]
    [InlineData("plant_container_open", 6, 1, 0.6, 0.1, true, 20, 3, 3, 3)]
    [InlineData("plant_container", 6, 1, 0.6, 0.1, true, 20, 3, 3, 3)]
    [InlineData("shack_awning", 3, 1, 0.5, 0.4, true, 1, 4, 2, 3)]
    [InlineData("stacked_crates", 6, 2, 0.4, 0.7, false, 20, 3, 4, 3)]
    [InlineData("vending_machine", 6, 4, 0.4, 0.6, true, 1, 4, 7, 3)]
    [InlineData("grazer_head_topper", 7.5, 6, 0.4, 0.6, false, 1, 4, 3, 3)]
    [InlineData("produce_box_potato", 6, 1, 0.5, 0.2, true, 20, 4, 4, 4)]
    [InlineData("air_conditioner", 6, 6, 0.6, 0.6, true, 20, 4, 3, 3)]
    [InlineData("juice_drum", 7.5, 3, 0.3, 0.6, false, 20, 2, 3, 2)]
    [InlineData("broken_concrete_large", 7.5, 4, 0.2, 0.1, false, 20, 4, 3, 2)]
    [InlineData("broken_concrete_medium", 7.5, 4, 0.2, 0.1, false, 20, 3, 3, 2)]
    [InlineData("crane_body", 10.5, 6, 0.5, 0.1, false, 20, 4, 4, 4)]
    [InlineData("crane_hook", 10.5, 6, 0.5, 0.1, false, 20, 2, 4, 2)]
    [InlineData("fuse_box", 6, 6, 0.4, 0.3, false, 20, 2, 3, 1)]
    [InlineData("coil_corner", 9, 6, 0.6, 0.3, false, 20, 3, 3, 3)]
    [InlineData("coil_segment", 9, 6, 0.6, 0.2, false, 20, 3, 3, 2)]
    [InlineData("tank_large", 3, 4, 0.4, 0.4, false, 20, 7, 5, 7)]
    [InlineData("hull_door", 10.5, 5, 0.6, 0.1, false, 20, 5, 10, 1)]
    [InlineData("tank_medium", 3, 3, 0.4, 0.5, false, 20, 5, 4, 5)]
    [InlineData("nut", 3, 3, 0.6, 0.1, false, 20, 3, 3, 3)]
    [InlineData("reflector_antenna", 3, 2, 0.3, 0.3, false, 20, 3, 6, 2)]
    [InlineData("dish_antenna", 3, 4, 0.4, 0.3, false, 20, 4, 4, 2)]
    [InlineData("reflector_dish", 3, 4, 0.4, 0.3, false, 20, 4, 4, 3)]
    [InlineData("screw", 3, 3, 0.6, 0.1, false, 20, 3, 3, 3)]
    [InlineData("tank_small", 3, 2, 0.4, 0.6, false, 20, 3, 4, 3)]
    [InlineData("steel_pallet", 6, 5, 0.6, 0.1, false, 20, 5, 1, 5)]
    [InlineData("vent_grid", 10.5, 2, 0.6, 0.1, false, 20, 5, 3, 1)]
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
    [InlineData("duck_statuette", 10, 3040, "wood_block:1", "paint_ammo:1", "beeswax:3", "glue:1")]
    [InlineData("produce_box_mixed", 2, 3016, "scrap_wood_block:4")]
    [InlineData("produce_box_carrot", 2, 3010, "scrap_wood_block:4")]
    [InlineData("produce_box_tomato", 2, 3014, "scrap_wood_block:4")]
    [InlineData("grass_planter", 2, 3020, "scrap_wood_block:5", "soil_bag:1")]
    [InlineData("mannequin_boot", 10, 3044, "cotton:1", "wood_block:2", "paint_ammo:1", "beeswax:1")]
    [InlineData("mannequin_hand", 10, 3042, "wood_block:2", "paint_ammo:1", "beeswax:3")]
    [InlineData("plant_container_open", 2, 3024, "scrap_wood_block:5", "soil_bag:1")]
    [InlineData("plant_container", 2, 3022, "scrap_wood_block:5", "soil_bag:1")]
    [InlineData("shack_awning", 2, 3050, "scrap_metal_block:3", "scrap_wood_block:2")]
    [InlineData("stacked_crates", 2, 3018, "plastic_block:4")]
    [InlineData("vending_machine", 5, 3030, "metal_block:10", "glass_block:2")]
    [InlineData("grazer_head_topper", 5, 3032, "cotton:5", "paint_ammo:2")]
    [InlineData("produce_box_potato", 2, 3012, "scrap_wood_block:4")]
    [InlineData("air_conditioner", 5, 3126, "metal_block:10", "circuit_board:3")]
    [InlineData("juice_drum", 5, 3116, "metal_block:4")]
    [InlineData("broken_concrete_large", 5, 3142, "metal_block:6", "concrete_block:12")]
    [InlineData("broken_concrete_medium", 5, 3140, "metal_block:4", "concrete_block:8")]
    [InlineData("crane_body", 10, 3130, "metal_block:20")]
    [InlineData("crane_hook", 5, 3132, "metal_block:5")]
    [InlineData("fuse_box", 5, 3124, "metal_block:2", "circuit_board:1")]
    [InlineData("coil_corner", 5, 3122, "metal_block:6", "circuit_board:1")]
    [InlineData("coil_segment", 5, 3120, "metal_block:6", "circuit_board:1")]
    [InlineData("tank_large", 5, 3114, "metal_block:12")]
    [InlineData("hull_door", 10, 3150, "metal_block_2:10")]
    [InlineData("tank_medium", 5, 3112, "metal_block:8")]
    [InlineData("nut", 2, 3160, "scrap_metal_block:1")]
    [InlineData("reflector_antenna", 5, 3184, "metal_block:2")]
    [InlineData("dish_antenna", 5, 3180, "metal_block:4", "circuit_board:1")]
    [InlineData("reflector_dish", 5, 3182, "metal_block:4")]
    [InlineData("screw", 2, 3162, "scrap_metal_block:1")]
    [InlineData("tank_small", 5, 3110, "metal_block:4")]
    [InlineData("steel_pallet", 5, 3170, "metal_block:4")]
    [InlineData("vent_grid", 5, 3128, "metal_block:3")]
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

    [Theory]
    [InlineData("hay_bale", "wooden_crate")]
    public void WorldOnlyDecorHasNoRecipe(params string[] ids)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        Assert.All(ids, id => Assert.DoesNotContain(book.All, r => r.Outputs.Any(o => o.Item == $"vanilla:{id}")));
    }

    [Fact]
    public void ThePartFilesAreTheBatchOutput() =>
        new VanillaBlocksTests().PartFilesAreThePartEditorBatchOutput("parts_decor.json", 52);
}
