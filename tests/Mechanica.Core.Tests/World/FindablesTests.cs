using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

/// <summary>
/// Real parts to find in the vanilla world: wrecks break down into the parts a vehicle like that had, roadside signs and
/// cones give themselves, small wrecks give wheels, seats and engines, the containers and Bloom Pods hold parts, and POIs
/// are furnished with parts players can take.
/// </summary>
public sealed class FindablesTests
{
    private static readonly Lazy<ModLoadResult> Mods = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static readonly Lazy<WorldGenData> Data = new(() => WorldGenData.Load(Mods.Value));

    private static WorldObjectDefinition Object(string id) => Data.Value.Objects[id];

    private static IEnumerable<string> Table(string id) => Data.Value.Definition.LootTables![id].Items!.Select(i => i.Item!);

    [Theory]
    [InlineData("car_wreck", "wreck_car")]
    [InlineData("pickup_wreck", "wreck_car")]
    [InlineData("truck_wreck", "wreck_truck")]
    [InlineData("bus_wreck", "wreck_truck")]
    [InlineData("tractor_wreck", "wreck_tractor")]
    [InlineData("excavator_wreck", "wreck_machine")]
    [InlineData("helicopter_wreck", "wreck_air")]
    [InlineData("motorbike_wreck", "wreck_small")]
    public void WrecksBreakDownIntoScrapAndParts(string wreck, string table)
    {
        var definition = Object(wreck);
        Assert.NotNull(definition.Health);
        Assert.True(definition.TakesDamageFrom("sledge"));
        Assert.Contains(definition.Drops!, d => d.Item == "scrap_metal_block");
        Assert.Equal(table, definition.Loot!.Table);
    }

    [Fact]
    public void WreckTablesHoldWhatADerelictIsMissing()
    {
        Assert.Contains("wheel", Table("wreck_car"));
        Assert.Contains("gas_engine", Table("wreck_truck"));
        Assert.Contains("big_wheel", Table("wreck_tractor"));
        Assert.Contains("piston", Table("wreck_machine"));
        Assert.Contains("electric_engine", Table("wreck_air"));
        Assert.Contains("scrap_wheel", Table("wreck_small"));
    }

    [Theory]
    [InlineData("stop_sign", "stop_sign")]
    [InlineData("caution_sign", "caution_sign")]
    [InlineData("speed_limit_sign", "speed_limit_sign")]
    [InlineData("traffic_cone", "traffic_cone")]
    [InlineData("road_cone_cluster", "traffic_cone")]
    [InlineData("engine_block", "scrap_gas_engine")]
    [InlineData("car_seat", "scrap_seat")]
    [InlineData("tire_stack", "scrap_wheel")]
    [InlineData("mine_cart", "scrap_wheel")]
    public void PropsGiveTheRealPart(string prop, string part) =>
        Assert.Contains(Object(prop).Drops!, d => d.Item == part);

    [Theory]
    [InlineData("crate", "bearing")]
    [InlineData("ruin_chest", "scrap_wheel")]
    [InlineData("warehouse_crate", "gas_engine")]
    [InlineData("epic", "wheel")]
    [InlineData("legendary", "electric_engine")]
    public void ContainersAndBloomPodsHoldParts(string table, string part) => Assert.Contains(part, Table(table));

    private static List<PoiMarker> Markers(string poi) => Data.Value.Pois.Single(p => p.Prefab.Id == poi).Prefab.Markers!;

    [Theory]
    [InlineData("farmbot_scrapyard", "beware_bots_sign")]
    [InlineData("farmbot_scrapyard", "scrap_gas_engine")]
    [InlineData("poi_41", "battery")]
    [InlineData("poi_50", "wheel")]
    [InlineData("camp", "scrap_seat")]
    [InlineData("roadside_kiosk", "sale_sign")]
    [InlineData("warehouse", "construction_light")]
    [InlineData("outpost", "respawn_bed")]
    [InlineData("outpost", "outpost_sign")]
    public void PoisAreFurnishedWithPartsToTake(string poi, string part) =>
        Assert.Contains(Markers(poi), m => m.Type == PoiMarker.PartType && m.Ref == part);

    [Theory]
    [InlineData("warehouse_small")]
    [InlineData("warehouse")]
    [InlineData("warehouse_large")]
    public void EveryWarehouseHasItsKeyInALocker(string poi)
    {
        Assert.Contains(Markers(poi), m => m is { Type: PoiMarker.PartType, Ref: "locker", Loot: "warehouse_office" });
        Assert.Equal(["warehouse_key"], Table("warehouse_office"));
    }

    [Fact]
    public void NoPoiMarkerNamesAnItemThatNeverAppears() =>
        Assert.DoesNotContain(Data.Value.Pois.SelectMany(p => p.Prefab.Markers ?? []),
            m => m.Type == "item" && m.Ref is "respawn_bed" or "outpost_sign" or "warehouse_key" or "warehouse_key_sign" or "scrap_metal_block" or "component_kit");

    [Fact]
    public void EveryDropAndLootEntryIsAPartTheModsHave()
    {
        var items = Data.Value.Objects.Values.SelectMany(o => o.Drops ?? []).Select(d => d.Item!)
            .Concat(Data.Value.Definition.LootTables!.Values.SelectMany(t => t.Items!).Select(i => i.Item!));
        Assert.All(items, item => Assert.NotNull(Mods.Value.FindDefinition(item)));
    }
}
