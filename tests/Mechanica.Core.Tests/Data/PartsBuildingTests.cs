using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// The parts map's batches 11 and 12 (stairs, railings, windows, windshields and signs; batches 11a, 11b, 12a, 12b so far): each
/// part has the wiki's stats and recipe, its own model and icon (tools/parts_building/build_parts.py) and its place in
/// the Fabricator's list.
/// </summary>
public class PartsBuildingTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));
    private static readonly string Mod = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("ramp_block", 6, 5, 0.5, 0.2, false, 4, 4, 4)]
    [InlineData("metal_railing", 6, 4, 0.5, 0.2, false, 4, 4, 1)]
    [InlineData("windshield", 3, 3, 0.2, 0.3, false, 6, 4, 2)]
    [InlineData("side_window", 3, 3, 0.2, 0.3, false, 4, 3, 1)]
    [InlineData("concave_wedge", 7.5, 4, 0.5, 0.1, false, 1, 1, 1)]
    [InlineData("window_large", 3, 3, 0.2, 0.3, false, 8, 4, 1)]
    [InlineData("windshield_large", 3, 3, 0.2, 0.3, false, 8, 5, 3)]
    [InlineData("window_metal", 10.5, 5, 0.7, 0.1, false, 4, 4, 1)]
    [InlineData("net_fence", 6, 4, 0.6, 0.2, false, 7, 4, 1)]
    [InlineData("window_square", 3, 3, 0.2, 0.3, false, 4, 4, 1)]
    [InlineData("stair_baluster", 6, 4, 0.5, 0.2, false, 1, 4, 1)]
    [InlineData("stair_banister", 6, 4, 0.5, 0.2, false, 1, 4, 1)]
    [InlineData("stair_landing", 6, 5, 0.5, 0.2, false, 8, 1, 8)]
    [InlineData("metal_railing_long", 6, 4, 0.5, 0.2, false, 8, 4, 1)]
    [InlineData("railing_join", 6, 4, 0.5, 0.2, false, 1, 1, 1)]
    [InlineData("stair_step", 6, 5, 0.5, 0.2, false, 6, 1, 1)]
    [InlineData("antenna", 3, 2, 0.3, 0.3, false, 1, 6, 1)]
    [InlineData("direction_arrow_left", 3, 3, 0.4, 0.2, false, 3, 6, 1)]
    [InlineData("direction_arrow_straight", 3, 3, 0.4, 0.2, false, 3, 6, 1)]
    [InlineData("direction_arrow_right", 3, 3, 0.4, 0.2, false, 3, 6, 1)]
    [InlineData("caution_sign", 3, 3, 0.4, 0.2, false, 3, 6, 1)]
    [InlineData("danger_sign", 3, 3, 0.4, 0.2, false, 3, 6, 1)]
    [InlineData("stop_sign", 3, 3, 0.4, 0.2, false, 3, 6, 1)]
    [InlineData("no_entry_sign", 3, 3, 0.4, 0.2, false, 3, 6, 1)]
    [InlineData("traffic_cone", 3, 2, 0.3, 0.6, true, 2, 3, 2)]
    [InlineData("banner_holder", 3, 3, 0.6, 0.5, false, 4, 1, 1)]
    [InlineData("billboard", 6, 3, 0.4, 0.6, false, 8, 8, 2)]
    [InlineData("beware_bots_sign", 3, 3, 0.4, 0.2, false, 3, 6, 1)]
    [InlineData("construction_sign", 3, 3, 0.4, 0.2, false, 3, 6, 1)]
    [InlineData("exit_sign", 3, 3, 0.4, 0.5, false, 3, 1, 1)]
    [InlineData("falling_objects_sign", 3, 3, 0.4, 0.2, false, 3, 6, 1)]
    [InlineData("hard_work_sign", 3, 1, 0.5, 0.4, true, 4, 3, 1)]
    [InlineData("sale_sign", 3, 1, 0.5, 0.4, true, 4, 3, 1)]
    [InlineData("skull_sign", 3, 3, 0.4, 0.2, false, 3, 6, 1)]
    [InlineData("welcome_sign", 3, 3, 0.4, 0.2, false, 6, 3, 1)]
    public void TakesTheWikiStatsAtOurSize(string id, double mass, int durability, double friction, double buoyancy,
        bool flammable, int x, int y, int z)
    {
        var part = Part(id);

        Assert.Equal((mass, durability, friction, buoyancy, flammable, 20),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, part.StackSize!.Value));
        Assert.Equal(new GridSize(x, y, z), part.Size);
    }

    [Theory]
    [InlineData("ramp_block", 2, 210, "scrap_metal_block:8", "scrap_wood_block:8")]
    [InlineData("metal_railing", 2, 240, "scrap_metal_block:8")]
    [InlineData("windshield", 10, 1580, "glass_block:10", "metal_block:5")]
    [InlineData("side_window", 10, 1590, "glass_block:5", "metal_block:5")]
    [InlineData("concave_wedge", 2, 190, "metal_block:1")]
    [InlineData("window_large", 10, 2920, "glass_block:15", "metal_block:10")]
    [InlineData("windshield_large", 10, 1585, "glass_block:20", "metal_block:10")]
    [InlineData("window_metal", 5, 2930, "metal_block:5", "glass_block:2")]
    [InlineData("net_fence", 5, 2950, "net_block:5")]
    [InlineData("window_square", 10, 2910, "glass_block:10", "metal_block:10")]
    [InlineData("stair_baluster", 2, 248, "metal_block:1")]
    [InlineData("stair_banister", 2, 246, "metal_block:1")]
    [InlineData("stair_landing", 5, 214, "metal_block:6", "net_block:2")]
    [InlineData("metal_railing_long", 2, 242, "metal_block:2")]
    [InlineData("railing_join", 2, 244, "metal_block:1")]
    [InlineData("stair_step", 2, 212, "metal_block:1")]
    [InlineData("antenna", 2, 1570, "scrap_metal_block:4")]
    [InlineData("direction_arrow_left", 2, 2160, "scrap_metal_block:4")]
    [InlineData("direction_arrow_straight", 2, 2170, "scrap_metal_block:4")]
    [InlineData("direction_arrow_right", 2, 2180, "scrap_metal_block:4")]
    [InlineData("caution_sign", 2, 2120, "scrap_metal_block:4")]
    [InlineData("danger_sign", 2, 2130, "scrap_metal_block:4")]
    [InlineData("stop_sign", 2, 2110, "scrap_metal_block:4")]
    [InlineData("no_entry_sign", 2, 2140, "scrap_metal_block:4")]
    [InlineData("traffic_cone", 15, 2210, "metal_block:10", "paint_ammo:1")]
    [InlineData("banner_holder", 10, 2250, "metal_block:10")]
    [InlineData("billboard", 10, 2260, "wood_block:10", "paint_ammo:10")]
    [InlineData("beware_bots_sign", 2, 2136, "scrap_metal_block:4")]
    [InlineData("construction_sign", 2, 2123, "scrap_metal_block:4")]
    [InlineData("exit_sign", 2, 2185, "metal_block:2", "glass_block:1")]
    [InlineData("falling_objects_sign", 2, 2126, "scrap_metal_block:4")]
    [InlineData("hard_work_sign", 2, 2193, "scrap_wood_block:4")]
    [InlineData("sale_sign", 2, 2196, "scrap_wood_block:4")]
    [InlineData("skull_sign", 2, 2133, "scrap_metal_block:4")]
    [InlineData("welcome_sign", 2, 2190, "scrap_wood_block:4")]
    public void TakesTheWikiRecipeInItsGroup(string id, float time, int order, params string[] inputs)
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
    [InlineData("ramp_block")]
    [InlineData("metal_railing")]
    [InlineData("windshield")]
    [InlineData("side_window")]
    [InlineData("concave_wedge")]
    [InlineData("window_large")]
    [InlineData("windshield_large")]
    [InlineData("window_metal")]
    [InlineData("net_fence")]
    [InlineData("window_square")]
    [InlineData("stair_baluster")]
    [InlineData("stair_banister")]
    [InlineData("stair_landing")]
    [InlineData("metal_railing_long")]
    [InlineData("railing_join")]
    [InlineData("stair_step")]
    [InlineData("antenna")]
    [InlineData("direction_arrow_left")]
    [InlineData("direction_arrow_straight")]
    [InlineData("direction_arrow_right")]
    [InlineData("caution_sign")]
    [InlineData("danger_sign")]
    [InlineData("stop_sign")]
    [InlineData("no_entry_sign")]
    [InlineData("traffic_cone")]
    [InlineData("banner_holder")]
    [InlineData("billboard")]
    [InlineData("beware_bots_sign")]
    [InlineData("construction_sign")]
    [InlineData("exit_sign")]
    [InlineData("falling_objects_sign")]
    [InlineData("hard_work_sign")]
    [InlineData("sale_sign")]
    [InlineData("skull_sign")]
    [InlineData("welcome_sign")]
    public void HasItsOwnModelAndIcon(string id)
    {
        var part = Part(id);

        Assert.Equal($"meshes/parts/{id}.glb", part.Mesh);
        Assert.True(File.Exists(Path.Combine(Mod, part.Mesh!)), $"{id} has a model");
        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        Assert.True(File.Exists(Path.Combine(Mod, part.Icon!)), $"{id} has an icon");
    }
}
