using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Items;

/// <summary>The inventory tab every vanilla item shows under (Scrap Mechanic's tabs: Tools, Blocks, Interactive, Parts, Consumable).</summary>
public class VanillaTabsTests
{
    private static readonly ModLoadResult Vanilla = ModLoader.Load([Path.Combine(TestMods.RepoRoot, "mods", "vanilla")]);

    private static InventoryTab? TabOf(string id) =>
        InventoryTabs.Of(Vanilla.FindDefinition($"vanilla:{id}") ?? throw new ArgumentException($"no part {id}"), HotbarTools.IsTool($"vanilla:{id}"));

    [Fact]
    public void EveryItemHasATab()
    {
        var none = Vanilla.Parts.Values.Where(p => p.Definition.IsItem && InventoryTabs.Of(p.Definition) is null).Select(p => p.Definition.Id).ToList();
        Assert.True(none.Count == 0, $"under no tab: {string.Join(", ", none)}");
    }

    [Fact]
    public void CreativeListsEachToolAndItemOnce()
    {
        var list = HotbarTools.CreativeList(Vanilla);
        var twice = list.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(twice.Count == 0, $"listed twice: {string.Join(", ", twice)}");
        Assert.All(HotbarTools.All, tool => Assert.Contains(tool, list));
        Assert.Contains("vanilla:scrap_wood_block", list);
    }

    [Theory]
    [InlineData(InventoryTab.Tools, "connect_tool", "paint_tool", "weld_tool", "potato_blaster", "potato_scattergun", "potato_launcher")]
    [InlineData(InventoryTab.Blocks, "scrap_wood_block", "metal_wedge", "glass_block", "path_light_block")]
    [InlineData(InventoryTab.Interactive, "bearing", "gas_engine", "drivers_seat", "logic_gate", "wheel", "sport_suspension", "chest",
        "gas_container", "battery", "fabricator", "refiner", "headlight", "respawn_bed", "potato_blaster_mounted")]
    [InlineData(InventoryTab.Parts, "component_kit", "circuit_board", "crude_oil", "wood_log", "stone_chunk",
        "metal_rod", "stop_sign", "small_pipe_short", "wooden_table", "stairs_block")]
    [InlineData(InventoryTab.Consumable, "carrot", "potato_seed", "gasoline", "battery_pack", "chemical", "paint_ammo", "glowstick", "popper",
        "soil_bag", "water_bucket", "water_bucket_full", "bloom_tea", "grazer_shake", "fertilizer", "grazer_milk", "grazer_steak")]
    public void ItemsAreUnderTheirScrapMechanicTab(InventoryTab tab, params string[] ids)
    {
        foreach (var id in ids)
            Assert.True(TabOf(id) == tab, $"{id} is under {TabOf(id)}, not {tab}");
    }
}
