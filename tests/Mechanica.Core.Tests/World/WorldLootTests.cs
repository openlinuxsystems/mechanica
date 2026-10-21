using System.Numerics;
using System.Text.Json.Nodes;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Saves;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

/// <summary>
/// Loot in the world's containers (POI <c>part</c> markers with <c>loot</c>): filled once when first placed, the same in
/// every run of a world, never again once looted unless the marker refills (<c>refill_days</c>), saved with the
/// container, and checked where the POI is loaded.
/// </summary>
public sealed class WorldLootTests
{
    private const string Locker = "vanilla:locker";
    private const string RuinChest = "vanilla:ruin_chest";

    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static readonly Lazy<WorldGenerator> VanillaWorld = new(() => new WorldGenerator(WorldGenData.Load(Vanilla.Value), 1234, 12032f, 9024f));

    private static readonly Lazy<IReadOnlyList<WorldPartSpot>> VanillaSpots = new(() => WorldParts.SpotsOf(VanillaWorld.Value));

    private static IPartCatalog Catalog => Vanilla.Value;

    private static WorldGenDefinition Rules => VanillaWorld.Value.Data.Definition;

    // A locker spot at the origin with the vanilla locker table, refilling after `days` in-game days (null: never).
    private static WorldPartSpot Spot(string part = Locker, string table = "locker", int min = 1, int max = 4, float? days = null, string key = "vanilla:warehouse#9@3")
    {
        var marker = new PoiMarker { Type = PoiMarker.PartType, Ref = part, Loot = table, LootRolls = [min, max], RefillDays = days };
        return new WorldPartSpot(key, "vanilla:warehouse", part, Vector3.Zero, 0, WorldLoot.Of(marker, key, Rules, 1234));
    }

    private static Inventory Contents(Creation creation) => creation.InventoryOf(creation.Parts.First().Id)!;

    private static List<ItemStack> Items(Creation creation) => [.. Contents(creation).Slots.OfType<ItemStack>()];

    // ---- Filling ----

    [Fact]
    public void AContainerComesFilledFromItsTable()
    {
        var spot = Spot();
        var creation = WorldParts.Build(spot, 1, Catalog, out var problem) ?? throw new InvalidOperationException(problem);

        var items = Items(creation);
        Assert.InRange(items.Count, 1, 4);
        var table = Rules.LootTables!["locker"].Items!;
        foreach (var stack in items)
        {
            var entry = Assert.Single(table, e => Identifiers.Qualify(e.Item!) == stack.Item);
            Assert.InRange(stack.Count, entry.Count, entry.CountMax ?? entry.Count);
        }
        // Different entries, never the same twice.
        Assert.Equal(items.Count, items.Select(s => s.Item).Distinct().Count());
        Assert.Equal(1, creation.WorldLoot!.Fills);
        Assert.Null(creation.WorldLoot.EmptiedAt);
    }

    [Fact]
    public void TheSameWorldAndMarkerAlwaysHoldTheSameFind()
    {
        var a = WorldParts.Build(Spot(), 1, Catalog, out _)!;
        var b = WorldParts.Build(Spot(), 2, Catalog, out _)!;

        Assert.Equal(Items(a), Items(b));
        Assert.Equal(WorldLoot.SeedOf(1234, "vanilla:warehouse#9@3"), Spot().Loot!.Seed);
        Assert.NotEqual(WorldLoot.SeedOf(1234, "vanilla:warehouse#9@3"), WorldLoot.SeedOf(1235, "vanilla:warehouse#9@3"));
        Assert.NotEqual(WorldLoot.SeedOf(1234, "vanilla:warehouse#9@3"), WorldLoot.SeedOf(1234, "vanilla:warehouse#10@3"));
        // Other markers and other fills roll their own finds.
        var finds = Enumerable.Range(0, 40).Select(i => string.Join(",", WorldLoot.Roll(Spot(key: $"vanilla:warehouse#{i}@3").Loot!, 1))).ToHashSet();
        Assert.True(finds.Count > 10, $"{finds.Count} different finds in 40 lockers");
        var loot = Spot().Loot!;
        Assert.Equal(WorldLoot.Roll(loot, 2), WorldLoot.Roll(loot, 2));
        Assert.Contains(Enumerable.Range(2, 20), f => !WorldLoot.Roll(loot, f).SequenceEqual(WorldLoot.Roll(loot, 1)));
    }

    [Fact]
    public void DrawsStayInTheirRange()
    {
        var counts = Enumerable.Range(0, 200).Select(i => WorldLoot.Roll(Spot(min: 2, max: 3, key: $"k#{i}@0").Loot!, 1).Count).ToHashSet();
        Assert.Equal([2, 3], counts.Order());
        // Without loot_rolls, one draw.
        var one = WorldLoot.Of(new PoiMarker { Type = PoiMarker.PartType, Ref = Locker, Loot = "locker" }, "k#0@0", Rules, 1)!;
        Assert.Equal((1, 1), (one.Min, one.Max));
        Assert.Single(WorldLoot.Roll(one, 1));
    }

    [Fact]
    public void ASmallContainerTakesWhatFits()
    {
        // A Broken Microwave has one slot: whatever more is drawn is left out.
        var spot = Spot("vanilla:broken_microwave", "ruin_chest", 6, 6);
        var creation = WorldParts.Build(spot, 1, Catalog, out _)!;

        Assert.Single(Items(creation));
    }

    [Fact]
    public void AMarkerWithoutLootOrWithAnUnknownTableStartsEmpty()
    {
        Assert.Null(WorldLoot.Of(new PoiMarker { Type = PoiMarker.PartType, Ref = Locker }, "k#0@0", Rules, 1));
        Assert.Null(WorldLoot.Of(new PoiMarker { Type = PoiMarker.PartType, Ref = Locker, Loot = "nope" }, "k#0@0", Rules, 1));
        var creation = WorldParts.Build(new WorldPartSpot("k#0@0", "vanilla:warehouse", Locker, Vector3.Zero, 0), 1, Catalog, out _)!;
        Assert.True(Contents(creation).IsEmpty);
        Assert.Null(creation.WorldLoot);
    }

    [Fact]
    public void RefillTimeIsInGameDaysOfTheRulesDayLength()
    {
        Assert.Equal(1440f, Rules.DayLength);
        Assert.Equal(5 * 1440.0, Spot(days: 5).Loot!.RefillSeconds);
        Assert.Null(Spot().Loot!.RefillSeconds);
        var shortDays = Rules with { DayLength = 60f };
        var marker = new PoiMarker { Type = PoiMarker.PartType, Ref = RuinChest, Loot = "ruin_chest", RefillDays = 2.5f };
        Assert.Equal(150.0, WorldLoot.Of(marker, "k#0@0", shortDays, 1)!.RefillSeconds);
    }

    // ---- Refilling ----

    [Fact]
    public void ARefillingContainerFillsAgainOnceItHasStoodEmptyLongEnough()
    {
        var spot = Spot(RuinChest, "ruin_chest", 1, 6, days: 5);
        var creation = WorldParts.Build(spot, 1, Catalog, out _)!;
        var wait = spot.Loot!.RefillSeconds!.Value;

        // Full: nothing happens.
        Assert.False(WorldLoot.Refill(creation, spot, 10));
        Assert.Null(creation.WorldLoot!.EmptiedAt);
        // A player empties it: the host notes when it found it empty and waits the refill time from then.
        Contents(creation).Clear();
        Assert.False(WorldLoot.Refill(creation, spot, 100));
        Assert.Equal(100, creation.WorldLoot.EmptiedAt);
        Assert.False(WorldLoot.Refill(creation, spot, 100 + wait - 1));
        Assert.True(Contents(creation).IsEmpty);

        Assert.True(WorldLoot.Refill(creation, spot, 100 + wait));
        Assert.False(Contents(creation).IsEmpty);
        Assert.Equal(2, creation.WorldLoot.Fills);
        Assert.Null(creation.WorldLoot.EmptiedAt);
        Assert.Equal(WorldLoot.Roll(spot.Loot, 2).Where(s => Catalog.FindDefinition(s.Item) is not null).Take(6), Items(creation));
        // And again later.
        Assert.False(WorldLoot.Refill(creation, spot, 100 + wait + 1));
    }

    [Fact]
    public void SomethingPutBackStopsTheWait()
    {
        var spot = Spot(RuinChest, "ruin_chest", 1, 6, days: 5);
        var creation = WorldParts.Build(spot, 1, Catalog, out _)!;
        var wait = spot.Loot!.RefillSeconds!.Value;
        Contents(creation).Clear();
        WorldLoot.Refill(creation, spot, 0);

        Contents(creation).Add("vanilla:carrot", 1);
        Assert.False(WorldLoot.Refill(creation, spot, wait / 2));
        Assert.Null(creation.WorldLoot!.EmptiedAt);
        Assert.False(WorldLoot.Refill(creation, spot, wait * 2));
        Assert.Equal(1, Contents(creation).Count("vanilla:carrot"));
        Assert.Equal(1, creation.WorldLoot.Fills);
        // Emptied again, the wait starts over.
        Contents(creation).Clear();
        Assert.False(WorldLoot.Refill(creation, spot, wait * 2));
        Assert.False(WorldLoot.Refill(creation, spot, wait * 3 - 1));
        Assert.True(WorldLoot.Refill(creation, spot, wait * 3));
    }

    [Fact]
    public void AContainerWithoutRefillDaysIsNeverFilledAgain()
    {
        var spot = Spot();
        var creation = WorldParts.Build(spot, 1, Catalog, out _)!;
        Contents(creation).Clear();

        foreach (var now in new[] { 0.0, 1e3, 1e6, 1e9 })
            Assert.False(WorldLoot.Refill(creation, spot, now));
        Assert.True(Contents(creation).IsEmpty);
        Assert.Equal(1, creation.WorldLoot!.Fills);
    }

    [Fact]
    public void RefillLeavesOtherCreationsAlone()
    {
        var spot = Spot(RuinChest, "ruin_chest", days: 1);
        var own = new Creation(5, Catalog);
        own.TryPlacePart(RuinChest, GridCell.Zero, GridRotation.Identity, out _, out _);
        var elsewhere = WorldParts.Build(spot with { Key = "vanilla:warehouse#1@1" }, 6, Catalog, out _)!;
        Contents(elsewhere).Clear();

        for (var now = 0.0; now < 1e5; now += 1e4)
        {
            Assert.False(WorldLoot.Refill(own, spot, now));
            Assert.False(WorldLoot.Refill(elsewhere, spot, now));
        }
        Assert.True(Contents(own).IsEmpty);
        Assert.Null(own.WorldLoot);
        Assert.True(Contents(elsewhere).IsEmpty);
    }

    // ---- Saves ----

    [Fact]
    public void TheFillAndTheWaitAreSavedWithTheContainer()
    {
        var spot = Spot(RuinChest, "ruin_chest", 1, 6, days: 5);
        var creation = WorldParts.Build(spot, 1, Catalog, out _)!;
        Contents(creation).Clear();
        WorldLoot.Refill(creation, spot, 321.5);

        var json = CreationJson.Write(creation);
        Assert.Equal(1, JsonNode.Parse(json)!["world_loot"]!["fills"]!.GetValue<int>());
        Assert.Equal(321.5, JsonNode.Parse(json)!["world_loot"]!["emptied_at"]!.GetValue<double>());
        var diagnostics = new List<DataDiagnostic>();
        var back = CreationJson.Read(json, Catalog, "save", diagnostics)!;
        Assert.Empty(diagnostics);
        Assert.Equal(1, back.WorldLoot!.Fills);
        Assert.Equal(321.5, back.WorldLoot.EmptiedAt);
        // The wait carries on from the save.
        Assert.True(WorldLoot.Refill(back, spot, 321.5 + spot.Loot!.RefillSeconds!.Value));
        Assert.Equal(2, back.WorldLoot.Fills);

        // Players' Creations have none, and a blueprint never carries it.
        Assert.Null(JsonNode.Parse(CreationJson.Write(new Creation(4, Catalog).With("vanilla:wood_block")))!["world_loot"]);
        Assert.DoesNotContain("world_loot", Blueprint.Write(creation, "Free loot", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void ALootedContainerIsNotFilledAgainWhenTheWorldLoads()
    {
        var spot = Spot();
        var made = Assert.Single(WorldParts.Reconcile([spot], [], Catalog, () => 1).Add);
        Assert.False(Contents(made).IsEmpty);
        Contents(made).Clear();

        var save = new SaveGame { Seed = 1234, Creations = [CreationJson.Write(made)] };
        var diagnostics = new List<DataDiagnostic>();
        var loaded = SaveGame.Read(save.Write(), "save", diagnostics)!.ReadCreations(Catalog, "save", diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var plan = WorldParts.Reconcile([spot], loaded, Catalog, () => 100);
        Assert.Empty(plan.Add);
        Assert.Empty(plan.Remove);
        Assert.True(Contents(Assert.Single(loaded)).IsEmpty);
        // A container a player picked up places no other, full or not.
        var taken = WorldParts.ObjectOf(spot.Key)!.Value;
        Assert.Empty(WorldParts.Reconcile([spot], [], Catalog, () => 100, id => id == taken).Add);
    }

    [Fact]
    public void ASavedWorldLootStateIsChecked()
    {
        var creation = WorldParts.Build(Spot(), 1, Catalog, out _)!;
        var good = JsonNode.Parse(CreationJson.Write(creation))!.AsObject();

        var negative = good.DeepClone().AsObject();
        negative["world_loot"]!["fills"] = -1;
        var stray = good.DeepClone().AsObject();
        stray.Remove("world_part");
        foreach (var (json, path) in new[] { (negative, "$.world_loot.fills"), (stray, "$.world_loot") })
        {
            var diagnostics = new List<DataDiagnostic>();
            Assert.Null(CreationJson.Read(json.ToJsonString(), Catalog, "save", diagnostics));
            Assert.Contains(diagnostics, d => d.JsonPath == path && d.Severity == DiagnosticSeverity.Error);
        }
    }

    // ---- Vanilla data ----

    [Fact]
    public void VanillaContainersHaveLootFromTablesOfRealItems()
    {
        var looted = VanillaSpots.Value.Where(s => s.Loot is not null).ToList();

        Assert.NotEmpty(looted);
        foreach (var table in new[] { "locker", "file_cabinet", "microwave", "ruin_chest", "crate", "warehouse_crate" })
            Assert.Contains(looted, s => s.Loot!.TableId == table);
        foreach (var (id, table) in Rules.LootTables!)
        {
            foreach (var entry in table.Items!)
                Assert.True(Catalog.FindDefinition(Identifiers.Qualify(entry.Item!)) is not null, $"{id}: no item '{entry.Item}'");
        }
        foreach (var spot in looted)
        {
            var creation = WorldParts.Build(spot, 1, Catalog, out var problem) ?? throw new InvalidOperationException(problem);
            Assert.False(Contents(creation).IsEmpty, $"{spot.Key} ({spot.Part}) is empty");
        }
        // Every container marker that was a design note now places its container; ruin chests refill after 5 days.
        Assert.DoesNotContain(VanillaWorld.Value.MarkersOfType("item"), m => m.Marker.Ref is "wooden_crate" or "metal_crate" or "chest" or "dispenser");
        Assert.All(looted.Where(s => s.Part == RuinChest), s => Assert.Equal(5 * Rules.DayLength, s.Loot!.RefillSeconds));
        Assert.All(looted.Where(s => s.Part != RuinChest), s => Assert.Null(s.Loot!.RefillSeconds));
        Assert.Contains(looted, s => s.Poi == "vanilla:crash_site" && s.Part == Locker);
        Assert.Contains(looted, s => s.Poi == "vanilla:warehouse" && s.Part == "vanilla:file_cabinet");
        Assert.Contains(looted, s => s.Poi == "vanilla:forest_ruin" && s.Part == RuinChest);
    }

    // ---- Validation ----

    [Fact]
    public void LootOnAMarkerIsCheckedWhereThePoiLoads()
    {
        using var mods = new TestMods().Mod("vanilla")
            .File("vanilla", WorldGenData.DefinitionFile, """
                { "biomes": [{ "id": "a" }],
                  "loot_tables": { "junk": { "items": [{ "item": "bolt" }, { "item": "nut" }] } } }
                """)
            .Part("vanilla", "bolt.json", TestMods.Block("bolt"))
            .Part("vanilla", "nut.json", TestMods.Block("nut"))
            .Part("vanilla", "crate.json", TestMods.Block("crate", """{ "inventory": { "slots": 2 } }"""))
            .File("vanilla", "pois/yard.json", """
                { "id": "yard", "radius": 20, "placement": { "fixed": [100, 100] },
                  "markers": [
                    { "type": "part", "ref": "crate", "loot": "junk", "loot_rolls": [1, 2], "refill_days": 5 },
                    { "type": "part", "ref": "crate", "loot": "gold" },
                    { "type": "part", "ref": "bolt", "loot": "junk" },
                    { "type": "part", "ref": "crate", "loot": "junk", "loot_rolls": [3, 1] },
                    { "type": "part", "ref": "crate", "loot": "junk", "loot_rolls": [0, 1] },
                    { "type": "part", "ref": "crate", "loot": "junk", "loot_rolls": [1] },
                    { "type": "part", "ref": "crate", "loot_rolls": [1, 1] },
                    { "type": "part", "ref": "crate", "refill_days": 2 },
                    { "type": "part", "ref": "crate", "loot": "junk", "refill_days": 0 },
                    { "type": "item", "ref": "crate", "loot": "junk" },
                    { "type": "part", "ref": "crate", "loot": "other_mod:junk" },
                    { "type": "part", "ref": "crate", "loot": "junk", "loot_rolls": [1, 3] }
                  ] }
                """);
        var data = WorldGenData.Load(mods.Load());

        var errors = data.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.JsonPath).ToList();
        Assert.Equal(
        [
            "$.markers[1].loot", "$.markers[2].loot", "$.markers[3].loot_rolls", "$.markers[4].loot_rolls", "$.markers[5].loot_rolls",
            "$.markers[6].loot_rolls", "$.markers[7].refill_days", "$.markers[8].refill_days", "$.markers[9].loot", "$.markers[10].loot",
        ], errors);
        Assert.Contains("'gold' is not one of the loot_tables", data.Diagnostics.Single(d => d.JsonPath == "$.markers[1].loot").Message);
        Assert.Contains("no inventory", data.Diagnostics.Single(d => d.JsonPath == "$.markers[2].loot").Message);
        // Asking for more draws than the table has entries or the crate has slots is only a warning.
        Assert.Contains(data.Diagnostics, d => d.JsonPath == "$.markers[11].loot_rolls" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Empty(data.Pois);
    }

    [Fact]
    public void AGoodLootMarkerLoadsAndFillsItsContainer()
    {
        using var mods = new TestMods().Mod("vanilla")
            .File("vanilla", WorldGenData.DefinitionFile, """
                { "biomes": [{ "id": "a" }], "day_length": 600,
                  "loot_tables": { "junk": { "items": [{ "item": "bolt", "count": 2, "count_max": 5 }, { "item": "nut", "count": 3 }] } } }
                """)
            .Part("vanilla", "bolt.json", TestMods.Block("bolt", """{ "stack_size": 10 }"""))
            .Part("vanilla", "nut.json", TestMods.Block("nut", """{ "stack_size": 10 }"""))
            .Part("vanilla", "crate.json", TestMods.Block("crate", """{ "inventory": { "slots": 2 } }"""))
            .File("vanilla", "pois/yard.json", """
                { "id": "yard", "radius": 20, "placement": { "fixed": [100, 100] },
                  "markers": [{ "type": "part", "ref": "crate", "at": [1, 0, 2], "loot": "junk", "loot_rolls": [2, 2], "refill_days": 0.5 }] }
                """);
        var loaded = mods.Load();
        var data = WorldGenData.Load(loaded);
        Assert.Empty(data.Diagnostics);

        var spot = Assert.Single(WorldParts.SpotsOf(new WorldGenerator(data, 7, 4096f)));
        Assert.Equal(300.0, spot.Loot!.RefillSeconds);
        var crate = Assert.Single(WorldParts.Reconcile([spot], [], loaded, () => 1).Add);
        var items = Items(crate).OrderBy(s => s.Item, StringComparer.Ordinal).ToList();
        Assert.Equal(["vanilla:bolt", "vanilla:nut"], items.Select(s => s.Item));
        Assert.InRange(items[0].Count, 2, 5);
        Assert.Equal(3, items[1].Count);
    }

    [Fact]
    public void ADayLengthMustBePositive()
    {
        using var mods = new TestMods().Mod("vanilla")
            .File("vanilla", WorldGenData.DefinitionFile, """{ "biomes": [{ "id": "a" }], "day_length": 0 }""");
        var data = WorldGenData.Load(mods.Load());

        Assert.Contains(data.Diagnostics, d => d.JsonPath == "$.day_length" && d.Severity == DiagnosticSeverity.Error);
    }
}
