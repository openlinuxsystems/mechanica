using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// The parts audit's fix batches: the chunk-era parts put on the bars scale every mapped part uses (1.5 kg per weight
/// bar, durability = bars, friction and buoyancy = bars / 10), one flat mass for every length of a family, and the
/// chunk-era recipes tidied to match their siblings, and the Refiner recipe for Circuit Boards.
/// </summary>
public class PartsAuditTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    /// <summary>Audit A2 (problems #4, #5, #6, #7, #8, #9, #12): stats on the bars scale.</summary>
    [Theory]
    // #4 vehicle body
    [InlineData("bumper_front", 7.5, 5, 0.4, 0.2, false)]
    [InlineData("bumper_rear", 7.5, 5, 0.4, 0.2, false)]
    [InlineData("mud_flap", 1.5, 2, 0.7, 0.3, true)]
    [InlineData("exhaust_pipe", 3, 3, 0.3, 0.3, false)]
    [InlineData("license_plate", 1.5, 2, 0.4, 0.3, false)]
    [InlineData("roof_rack", 6, 4, 0.4, 0.1, false)]
    // #5 wooden furniture and frames: Wood Block's bars
    [InlineData("chair", 3, 3, 0.5, 0.6, true)]
    [InlineData("stool", 3, 3, 0.5, 0.6, true)]
    [InlineData("wooden_table", 6, 3, 0.5, 0.6, true)]
    [InlineData("pallet", 6, 3, 0.5, 0.6, true)]
    [InlineData("door_frame", 6, 3, 0.5, 0.6, true)]
    [InlineData("window_frame", 4.5, 3, 0.5, 0.6, true)]
    // #6 the Stairs Block weighs what the Ramp Block does
    [InlineData("stairs_block", 6, 5, 0.5, 0.2, false)]
    // #7 the sign family
    [InlineData("outpost_sign", 3, 3, 0.4, 0.2, false)]
    [InlineData("warehouse_key_sign", 3, 3, 0.4, 0.2, false)]
    [InlineData("speed_limit_sign", 3, 3, 0.4, 0.2, false)]
    // #8 and #9 crafting materials
    [InlineData("component_kit", 6, 2, 0.5, 0.3, false)]
    [InlineData("explosive_remnant", 6, 2, 0.4, 0.3, true)]
    // #12 chunk-era containers: the container family's 5/6/4/6 bars
    [InlineData("barrel", 7.5, 6, 0.4, 0.6, false)]
    [InlineData("tool_cabinet", 7.5, 6, 0.4, 0.6, false)]
    [InlineData("dispenser", 7.5, 6, 0.4, 0.6, false)]
    [InlineData("metal_crate", 7.5, 6, 0.4, 0.6, false)]
    [InlineData("feeding_trough", 7.5, 6, 0.4, 0.6, true)]
    public void ChunkEraPartsAreOnTheBarsScale(string id, double mass, int durability, double friction, double buoyancy, bool flammable)
    {
        var part = Part(id);

        Assert.Equal((mass, durability, friction, buoyancy, flammable),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value));
    }

    /// <summary>Audit #9: the Explosive Remnant keeps Glow's stack of 10.</summary>
    [Fact]
    public void TheExplosiveRemnantStacksToTen() => Assert.Equal(10, Part("explosive_remnant").StackSize);

    /// <summary>Audit #10: the Water Buckets say they do not burn, like every other consumable.</summary>
    [Theory]
    [InlineData("water_bucket")]
    [InlineData("water_bucket_full")]
    public void WaterBucketsAreNotFlammable(string id) => Assert.False(Part(id).Flammable);

    /// <summary>Audit #11: the 4x3x3 container family all stack to 5, as the Gas Container does.</summary>
    [Theory]
    [InlineData("gas_container")]
    [InlineData("battery")]
    [InlineData("chemical_container")]
    [InlineData("water_container")]
    [InlineData("potato_ammo_container")]
    public void TheContainerFamilyStacksToFive(string id) => Assert.Equal(5, Part(id).StackSize);

    /// <summary>Audit #17: weight bars are a density, so every length of a family weighs the same.</summary>
    [Theory]
    [InlineData(6, "ibeam_stub", "ibeam_short", "ibeam_long")]
    [InlineData(6, "metal_column_short", "metal_column")]
    [InlineData(3, "small_pipe_short", "small_pipe_long")]
    [InlineData(6, "large_pipe_short", "large_pipe_long")]
    [InlineData(6, "main_pipe_short", "main_pipe_long")]
    public void EveryLengthOfAFamilyWeighsTheSame(double mass, params string[] ids) =>
        Assert.All(ids, id => Assert.Equal(mass, Math.Round((double)Part(id).Mass!.Value, 3)));

    /// <summary>Audit #8: the Component Kit's description names what it still makes.</summary>
    [Fact]
    public void TheComponentKitSaysWhatItMakes()
    {
        var description = Part("component_kit").Description!;
        Assert.DoesNotContain("metal blocks", description);
        Assert.Contains("engines, seats, suspensions, logic parts and upgrades", description);
    }

    /// <summary>
    /// Audit A3 (problems #6, #13, #14, #15): the chunk-era light recipes match the batch 5 lights, the Stairs Block
    /// sits after the Ramp Block, the short railing costs less than the long one and the scrap stone wedge is made at
    /// both Fabricators like its siblings.
    /// </summary>
    [Theory]
    [InlineData("interior_light", 5, 1620, "metal_block:2", "glass_block:1")]
    [InlineData("warning_light", 5, 1660, "metal_block:2", "glass_block:1")]
    [InlineData("construction_light", 5, 1650, "metal_block:4", "glass_block:1")]
    [InlineData("spotlight", 5, 1640, "metal_block:2", "glass_block:1", "circuit_board:1")]
    [InlineData("warehouse_spotlight", 10, 1680, "metal_block:6", "glass_block:2", "circuit_board:1")]
    [InlineData("stairs_block", 2, 211, "scrap_metal_block:8", "scrap_wood_block:8")]
    [InlineData("metal_railing", 2, 240, "metal_block:1")]
    public void TidiedRecipes(string id, float time, int order, params string[] inputs)
    {
        var recipe = RecipeBook.Load(Vanilla.Value).Find(id)!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(inputs.Select(i => $"vanilla:{i}"), recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(time, recipe.Time);
        Assert.Equal(order, recipe.Definition.Order);
    }

    /// <summary>Audit #14: every scrap wedge is made at both Fabricators.</summary>
    [Theory]
    [InlineData("scrap_wood_wedge")]
    [InlineData("scrap_metal_wedge")]
    [InlineData("scrap_stone_wedge")]
    public void ScrapWedgesAreMadeAtBothFabricators(string id) =>
        Assert.Equal(["vanilla:fabricator", "vanilla:portable_fabricator"], RecipeBook.Load(Vanilla.Value).Find(id)!.Stations);

    /// <summary>Audit #15: the short railing never costs more metal than the long one.</summary>
    [Fact]
    public void TheShortRailingCostsLessThanTheLongOne()
    {
        var book = RecipeBook.Load(Vanilla.Value);
        int Metal(string id) => book.Find(id)!.Inputs.Where(i => i.Item == "vanilla:metal_block").Sum(i => i.Count);

        Assert.True(Metal("metal_railing") < Metal("metal_railing_long"));
        Assert.DoesNotContain(book.Find("metal_railing")!.Inputs, i => i.Item == "vanilla:scrap_metal_block");
    }

    [Fact]
    public void ARefinerMakesCircuitBoardsFromAMetalRodAndGlass()
    {
        // Audit #18 (Chase chose the Refiner recipe): 54 recipes need Circuit Boards, so loot and bot drops alone
        // are a choke point under the abundance rule.
        var recipe = RecipeBook.Load(Vanilla.Value).Find("circuit_board")!;

        Assert.Equal(["vanilla:refiner"], recipe.Stations);
        Assert.Equal(["vanilla:metal_rod:1", "vanilla:glass_block:2"], recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(("vanilla:circuit_board", 2), (recipe.Outputs.Single().Item, recipe.Outputs.Single().Count));
        Assert.Equal(10f, recipe.Time);
        Assert.Equal(2650, recipe.Definition.Order);
    }
}
