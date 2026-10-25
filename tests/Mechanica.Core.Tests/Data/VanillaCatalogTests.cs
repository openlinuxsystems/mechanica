using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// Chunk 23's checklist: every item and part id in CATALOG_IDS.md is in vanilla with what the spec asks of
/// it (a data def, a script if it is interactive, sound bindings that exist, and a recipe or another way to
/// get it). Blocks and bots have their own tests (<see cref="VanillaBlocksTests"/>, the bot tests). The
/// tools are the tool belt's (C#, D2), not items; <c>produce_crate</c> is left out for now (spec §8).
/// </summary>
public class VanillaCatalogTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    // CATALOG_IDS.md, every section but Tools, Blocks, Bots, POIs, Characters and Quests.
    public static readonly IReadOnlyList<string> Items =
    [
        // Interactive mechanical parts
        "bearing", "gas_engine", "scrap_gas_engine", "electric_engine", "piston", "sport_suspension", "offroad_suspension",
        "drivers_seat", "scrap_drivers_seat", "passenger_seat", "scrap_seat", "controller", "logic_gate", "timer", "sensor",
        "switch", "button", "horn", "thruster", "wing", "rotor", "control_surface", "hover_jet", "saw_blade", "drill",
        "water_cannon", "potato_blaster_mounted", "scrap_wheel", "wheel", "big_wheel", "sticky_wheel", "plasma_drill",
        "explosive_canister_small", "explosive_canister_large",
        // Containers, stations, lights, utility
        "chest", "small_chest", "large_chest", "cargo_chest", "water_container", "potato_ammo_container", "broken_microwave", "ruin_chest",
        "gas_container", "battery", "chemical_container", "fridge", "locker", "file_cabinet", "tool_cabinet",
        "wooden_crate", "metal_crate", "barrel", "fabricator", "portable_fabricator", "refiner", "dispenser", "respawn_bed",
        "radio", "beacon", "headlight", "spotlight", "warehouse_spotlight", "construction_light", "interior_light",
        "warning_light", "shack_light", "fluorescent_light", "packing_lamp", "frame_beam_light", "neon_sign", "protector_sign",
        "alarm_clock", "cash_register", "fancy_bed", "freezer", "anvil", "field_kitchen",
        // Resources, consumables, handheld weapons (produce_crate is left out for now)
        "component_kit", "circuit_board", "glue", "gasoline", "crude_oil", "battery_pack",
        "master_battery", "warehouse_key", "explosive_remnant", "scuttler_head", "carrot_seed", "potato_seed", "tomato_seed",
        "cotton_seed", "pigment_flower_seed", "beetroot_seed", "banana_seed", "chili_seed", "blueberry_seed", "orange_seed", "broccoli_seed",
        "pineapple_seed", "carrot", "potato", "tomato", "cotton", "pigment_flower", "beetroot", "banana", "chili", "blueberry", "orange", "broccoli",
        "pineapple", "corn", "beeswax", "ember", "water_bucket", "chemical_bucket", "glowstick", "popper", "potato_blaster", "potato_scattergun",
        "potato_launcher", "bloom_tea", "grazer_shake", "fertilizer", "grazer_milk", "grazer_steak", "garden_burger", "colony_burger",
        // World / farming parts
        "soil_bag",
        // Decorative props
        "stop_sign", "caution_sign", "danger_sign", "warehouse_key_sign", "direction_arrow_left", "direction_arrow_right",
        "direction_arrow_straight", "outpost_sign", "no_entry_sign", "speed_limit_sign",
        "small_pipe_short", "small_pipe_long", "small_pipe_corner", "small_pipe_tee", "small_pipe_cross", "large_pipe_short",
        "large_pipe_long", "large_pipe_corner", "large_pipe_tee", "ibeam_short", "ibeam_long", "support_pillar",
        "support_bracket", "metal_column", "metal_railing",
        "wooden_table", "metal_table", "chair", "stool", "shelf_unit", "pallet",
        "small_rock", "large_rock", "boulder", "bush", "tree_trunk", "fallen_log", "hay_bale", "grass_tuft", "flower_patch",
        "bumper_front", "bumper_rear", "windshield", "side_window", "exhaust_pipe", "antenna", "roof_rack", "mud_flap",
        "license_plate",
        "traffic_cone", "barrier_tape", "ladder", "stairs_block", "ramp_block", "door_frame", "window_frame",
    ];

    // Nature props come with the world and are placed in Creative; everything else is crafted or dropped.
    private static readonly HashSet<string> WorldOnly =
        ["small_rock", "large_rock", "boulder", "bush", "hay_bale", "grass_tuft", "flower_patch"];

    public static TheoryData<string> Ids => [.. Items];

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Fact]
    public void TheChecklistHasNoDuplicates() =>
        Assert.Equal(Items.Count, Items.Distinct().Count());

    [Theory]
    [MemberData(nameof(Ids))]
    public void CatalogItemIsInVanillaWithAName(string id)
    {
        var part = Part(id);

        Assert.False(string.IsNullOrWhiteSpace(part.Name), $"{id} has a name");
        if (part.IsPlaceable)
            Assert.True(part.Mesh is not null && File.Exists(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", part.Mesh)), $"{id} has a mesh");
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void LightsAndPartsPlayersUseHaveAScript(string id)
    {
        // Bearings, suspensions and wheels are driven by other parts' scripts and need none of their own, and a
        // container's outputs only link its storage to the parts that draw on it (a Water Container to Water Cannons).
        var part = Part(id);
        var storageLinks = part.Category == PartCategory.Container && part.Logic?.Use is null;
        if (part.Category == PartCategory.Light || part.Logic?.Use is not null || (part.Logic?.Outputs > 0 && !storageLinks))
            Assert.True(part.Script is not null && File.Exists(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", part.Script)), $"{id} has a script");
    }

    [Theory]
    [InlineData("sport_suspension", "Sport Suspension")]
    [InlineData("offroad_suspension", "Off-Road Suspension")]
    public void EachSuspensionIsCraftedAtLevel1AndUpgradedToAStifferSpring(string id, string name)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var made = book.All.SelectMany(r => r.Outputs).Select(o => o.Item).Where(i => i.Contains(id, StringComparison.Ordinal));
        Assert.Equal([$"vanilla:{id}"], made.Distinct());
        var written = Part(id);
        Assert.Equal(4, PartUpgrades.MaxLevel(written));
        Assert.Equal([1, 2, 4, 8], written.Upgrades!.Select(u => u.Cost!.Single(c => c.Item == "component_kit").Count!.Value));
        var vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");
        var stiffness = 0f;
        for (var level = 0; level <= 4; level++)
        {
            var part = PartUpgrades.At(written, level);
            Assert.Equal($"{name} {level + 1}", part.Name);
            Assert.True(File.Exists(Path.Combine(vanilla, part.Mesh!)), $"level {level + 1}'s model {part.Mesh}");
            Assert.True(File.Exists(Path.Combine(vanilla, part.Icon!)), $"level {level + 1}'s icon {part.Icon}");
            var spring = part.Sockets!.Single(s => s.Type == SocketType.Spring).Joint!;
            Assert.True(spring.Stiffness > stiffness, $"level {level + 1} is stiffer than the one below");
            stiffness = spring.Stiffness!.Value;
        }
    }

    [Fact]
    public void ThePistonIsCraftedAtLevel1AndUpgradedForRangeSpeedAndForce()
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var made = book.All.SelectMany(r => r.Outputs).Select(o => o.Item).Where(i => i.Contains("piston", StringComparison.Ordinal));
        Assert.Equal(["vanilla:piston"], made.Distinct());
        var written = Part("piston");
        Assert.Equal(4, PartUpgrades.MaxLevel(written));
        Assert.All(written.Upgrades!, u => Assert.Equal(2, u.Cost!.Single(c => c.Item == "component_kit").Count));
        Assert.True(written.Menu?.LockWhenSequenced, "a Controller locks the piston's menu");
        var vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");
        for (var level = 0; level <= 4; level++)
        {
            var part = PartUpgrades.At(written, level);
            Assert.Equal($"Piston {level + 1}", part.Name);
            Assert.True(File.Exists(Path.Combine(vanilla, part.Mesh!)), $"level {level + 1}'s model {part.Mesh}");
            Assert.True(File.Exists(Path.Combine(vanilla, part.Icon!)), $"level {level + 1}'s icon {part.Icon}");
            // The spec's table: range 7 to 15 blocks, speed 2 to 10, force up a step each level.
            var range = 7 + 2 * level;
            Assert.Equal(range * MechanicaInfo.GridSize, part.Sockets!.Single(s => s.Type == SocketType.Linear).Joint!.Max);
            Assert.Equal(range, part.Settings!.Single(s => s.Id == "length").Max);
            Assert.Equal(2 + 2 * level, part.Settings!.Single(s => s.Id == "speed").Max);
            Assert.Equal(1, part.Settings!.Single(s => s.Id == "length").Min);
            Assert.Equal(5000 * (level + 1), part.Properties!["force"]!.GetValue<float>());
        }
        Assert.Contains(new UpgradeChange("Length (blocks)", "7", "9"), PartUpgrades.Changes(written, PartUpgrades.At(written, 1)));
    }

    [Theory]
    [InlineData("gas_engine", "gasoline", 13, new[] { "+2", "+2", "+44%", "+50%" })]
    [InlineData("electric_engine", "battery_pack", 16, new[] { "+2", "+2", "+40%", "+50%" })]
    public void EachEngineMenuIsALeverWithItsFuelAndWhatTheNextLevelGains(string id, string fuel, int steps, string[] gains)
    {
        var written = Part(id);
        var lever = written.Menu!.Lever!;
        Assert.Equal(("gear", "on", fuel), (lever.Setting, lever.Off, lever.Fuel));
        // The lever runs to the last level's top step (the wiki's gears: 13 for the Gas Engine, 16 for the Electric).
        Assert.Equal(steps, PartUpgrades.Stat(PartUpgrades.At(written, PartUpgrades.MaxLevel(written)), "settings.gear"));
        var next = PartUpgrades.At(written, 1);
        Assert.Equal(gains, lever.UpgradeStats!.Select(s => PartUpgrades.Gain(written, next, s.Value, s.Show ?? UpgradeStatShow.Difference)));
        // The Scrap Gas Engine shares the Gas Engine's menu, with no levels to gain.
        if (id == "gas_engine")
            Assert.Equal("fuel_items", Part("scrap_gas_engine").Menu!.Lever!.FuelCount);
    }

    [Fact]
    public void EveryLightGivesLight()
    {
        foreach (var id in Items.Where(id => Part(id).Category == PartCategory.Light))
            Assert.True(Part(id).Light is { Range: > 0 }, $"{id} has a light section");
    }

    [Fact]
    public void EveryBoundSoundIsRegistered()
    {
        var sounds = Mechanica.Core.Sound.SoundRegistry.Load(Vanilla.Value);
        foreach (var id in Items)
        {
            foreach (var (key, sound) in Part(id).Sounds ?? [])
                Assert.True(sounds.Find(sound) is not null, $"{id}'s '{key}' sound {sound} is registered");
        }
    }

    [Fact]
    public void EveryItemHasARecipeOrAnotherWayToGetIt()
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var crafted = book.All.SelectMany(r => r.Outputs).Select(o => o.Item[(o.Item.IndexOf(':') + 1)..]).ToHashSet();
        var dropped = DroppedItems();
        var missing = Items.Where(id => !crafted.Contains(id) && !dropped.Contains(id) && !WorldOnly.Contains(id)).ToList();

        Assert.True(missing.Count == 0, $"no recipe, drop or world source: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryRecipeHasItsOwnPlaceInTheStationList()
    {
        var book = RecipeBook.Load(Vanilla.Value);

        Assert.Empty(book.All.Where(r => r.Definition.Order is null).Select(r => r.QualifiedId));
        Assert.Empty(book.All.GroupBy(r => r.Definition.Order).Where(g => g.Count() > 1).Select(g => g.Key));
        // The hundreds name the group, so a group full up to its next hundred has spilled into the next group.
        Assert.Empty(book.All.Where(r => r.Definition.Order % 100 == 0).Select(r => r.QualifiedId));
    }

    [Fact]
    public void GlueIsMadeFromScrapWoodAndBeeswaxAtTheFabricator()
    {
        // Parts map batch 1: the wiki's recipe, 20 Scrap Wood Block + 2 Beeswax = 1 Glue in 5 s at the Craftbot.
        var glue = RecipeBook.Load(Vanilla.Value).All.Single(r => r.Outputs.Any(o => o.Item == "vanilla:glue"));

        Assert.Equal(["vanilla:fabricator"], glue.Stations);
        Assert.Equal([("vanilla:scrap_wood_block", 20), ("vanilla:beeswax", 2)], glue.Inputs.Select(i => (i.Item, i.Count)));
        Assert.Equal(1, glue.Outputs.Single().Count);
        Assert.Equal(5f, glue.Time);
    }

    [Theory]
    // Parts map batch 1: the crafting materials take the wiki's bars (mass = bars x 1.5 kg, friction and buoyancy =
    // bars / 10); Crude Oil keeps its barrel's 40 kg.
    [InlineData("glue", 6, 2, 0.4, 0.3, true, 10)]
    [InlineData("circuit_board", 6, 2, 0.6, 0.3, false, 20)]
    [InlineData("beeswax", 6, 2, 0.6, 0.3, true, 10)]
    [InlineData("ember", 6, 2, 0.5, 0.3, false, 20)]
    [InlineData("crude_oil", 40, 2, 0.4, 0.3, true, 20)]
    [InlineData("soil_bag", 3, 2, 0.4, 0.7, false, 10)]
    [InlineData("master_battery", 12, 10, 0.2, 0.7, false, 1)]
    [InlineData("warehouse_key", 4.5, 9, 0.3, 0.3, false, 1)]
    public void CraftingMaterialsTakeTheWikiStats(string id, double mass, int durability, double friction, double buoyancy,
        bool flammable, int stack)
    {
        var part = Part(id);

        Assert.Equal((mass, durability, friction, buoyancy, flammable, stack),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, part.StackSize!.Value));
    }

    [Theory]
    [InlineData("glue")]
    [InlineData("circuit_board")]
    [InlineData("beeswax")]
    [InlineData("ember")]
    [InlineData("crude_oil")]
    [InlineData("soil_bag")]
    [InlineData("water_bucket")]
    [InlineData("water_bucket_full")]
    [InlineData("master_battery")]
    [InlineData("warehouse_key")]
    [InlineData("paint_ammo")]
    [InlineData("explosive_remnant")]
    [InlineData("chemical")]
    [InlineData("chemical_bucket")]
    [InlineData("ladder")]
    public void EachCraftingMaterialHasItsOwnModelAndIcon(string id)
    {
        var part = Part(id);
        var mod = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

        Assert.True(part.Mesh is not null && File.Exists(Path.Combine(mod, part.Mesh)), $"{id} has a model");
        Assert.True(part.Icon is not null && File.Exists(Path.Combine(mod, part.Icon)), $"{id} has an icon");
    }

    [Theory]
    // Parts map batch 2 (the scrap tier): the wiki's bars, except that the wheels keep their tuned friction and the Off
    // Road Wheel (SM's Big Wheel) its 20 kg.
    [InlineData("bearing", 6, 1, 0.4, 0.1, false, 10)]
    [InlineData("switch", 3, 3, 0.5, 0.2, false, 10)]
    [InlineData("button", 3, 3, 0.5, 0.2, false, 10)]
    [InlineData("beacon", 4.5, 6, 0.3, 0.2, false, 1)]
    [InlineData("scrap_seat", 4.5, 2, 0.5, 0.2, true, 1)]
    [InlineData("scrap_drivers_seat", 7.5, 2, 0.5, 0.2, true, 1)]
    [InlineData("scrap_gas_engine", 12, 3, 0.5, 0.2, true, 1)]
    [InlineData("scrap_wheel", 4.5, 2, 1.05, 0.4, true, 1)]
    [InlineData("wheel", 10.5, 5, 1.0, 0.6, true, 20)]
    [InlineData("big_wheel", 20, 5, 1.15, 0.6, true, 20)]
    public void ScrapTierPartsTakeTheWikiStats(string id, double mass, int durability, double friction, double buoyancy,
        bool flammable, int stack)
    {
        var part = Part(id);

        Assert.Equal((mass, durability, friction, buoyancy, flammable, stack),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, part.StackSize!.Value));
    }

    [Theory]
    // Parts map batch 2: the wiki's recipes. The scrap tier is made at the Portable Fabricator (SM's Mini Craftbot); parts
    // SM makes at both stations have a scrap recipe there and a Metal Block one at the Fabricator (SM's Craftbot).
    [InlineData("bearing_scrap", "portable_fabricator", 5, "scrap_metal_block:3")]
    [InlineData("bearing", "fabricator", 5, "metal_block:1")]
    [InlineData("switch_scrap", "portable_fabricator", 5, "scrap_metal_block:5", "circuit_board:1")]
    [InlineData("switch", "fabricator", 5, "metal_block:1", "circuit_board:1")]
    [InlineData("button", "fabricator", 15, "metal_block:1", "circuit_board:1")]
    [InlineData("beacon_scrap", "portable_fabricator", 10, "scrap_metal_block:40", "circuit_board:4")]
    [InlineData("beacon", "fabricator", 10, "metal_block:20", "circuit_board:4")]
    [InlineData("scrap_seat", "portable_fabricator", 10, "scrap_wood_block:20")]
    [InlineData("scrap_drivers_seat", "portable_fabricator", 10, "scrap_metal_block:10", "scrap_wood_block:30", "circuit_board:1")]
    [InlineData("scrap_gas_engine", "portable_fabricator", 10, "scrap_metal_block:10", "scrap_wood_block:5", "circuit_board:2")]
    [InlineData("scrap_wheel", "portable_fabricator", 5, "scrap_wood_block:5", "scrap_metal_block:5")]
    [InlineData("wheel", "fabricator", 15, "wood_block:20", "metal_block:5", "beeswax:3")]
    [InlineData("big_wheel", "fabricator", 30, "wood_block:40", "metal_block:10", "beeswax:6")]
    public void ScrapTierPartsTakeTheWikiRecipes(string id, string station, float time, params string[] inputs)
    {
        var recipe = RecipeBook.Load(Vanilla.Value).Find(id)!;

        Assert.Equal([$"vanilla:{station}"], recipe.Stations);
        Assert.Equal(inputs.Select(i => $"vanilla:{i}"), recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(1, recipe.Outputs.Single().Count);
        Assert.Equal(time, recipe.Time);
    }

    [Fact]
    public void TheBeaconHasItsOwnModelIconAndClips()
    {
        // Parts map batch 2: a Salvager Tech model (tools/beacon/build_beacon.py) whose clips follow the script's "pose".
        var part = Part("beacon");
        var mod = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

        Assert.Equal("meshes/parts/beacon.glb", part.Mesh);
        Assert.True(File.Exists(Path.Combine(mod, part.Mesh!)), "the beacon has a model");
        Assert.True(part.Icon is not null && File.Exists(Path.Combine(mod, part.Icon)), "the beacon has an icon");
        Assert.Equal("pose", part.Display?.Clips?.Readout);
        Assert.Contains("part.display(\"pose\"", File.ReadAllText(Path.Combine(mod, "scripts", "beacon.lua")), StringComparison.Ordinal);
    }

    [Theory]
    // Parts map batch 3 (vehicle parts): the wiki's bars. The Piston keeps its durability ladder (4 to 8) and the wiki
    // gives no stack for most, so they keep ours.
    [InlineData("gas_engine", 12, 4, 0.5, 0.2, false, 1)]
    [InlineData("electric_engine", 10.5, 5, 0.7, 0.5, false, 1)]
    [InlineData("sport_suspension", 1.5, 5, 0.5, 0.1, false, 5)]
    [InlineData("offroad_suspension", 1.5, 5, 0.5, 0.1, false, 5)]
    [InlineData("piston", 7.5, 4, 0.7, 0.5, false, 20)]
    [InlineData("drill", 7.5, 7, 0.2, 0.1, false, 5)]
    [InlineData("saw_blade", 7.5, 7, 0.2, 0.1, false, 5)]
    [InlineData("horn", 3, 2, 0.5, 0.5, true, 20)]
    [InlineData("radio", 3, 3, 0.5, 0.6, false, 10)]
    [InlineData("logic_gate", 3, 3, 0.5, 0.2, false, 20)]
    [InlineData("timer", 4.5, 3, 0.5, 0.4, false, 20)]
    public void VehiclePartsTakeTheWikiStats(string id, double mass, int durability, double friction, double buoyancy,
        bool flammable, int stack)
    {
        var part = Part(id);

        Assert.Equal((mass, durability, friction, buoyancy, flammable, stack),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, part.StackSize!.Value));
    }

    [Theory]
    // Parts map batch 3: the wiki's Craftbot recipes, at the Fabricator only.
    [InlineData("gas_engine", 30, "metal_block:20", "component_kit:5", "circuit_board:1", "crude_oil:5")]
    [InlineData("electric_engine", 90, "metal_block_2:20", "battery_pack:5", "circuit_board:10", "component_kit:5")]
    [InlineData("sport_suspension", 10, "metal_block:20", "component_kit:1", "crude_oil:2")]
    [InlineData("offroad_suspension", 10, "metal_block:20", "component_kit:1", "crude_oil:2")]
    [InlineData("piston", 10, "metal_block_2:10", "component_kit:1", "circuit_board:1")]
    [InlineData("drill", 60, "metal_block_2:20", "component_kit:1")]
    [InlineData("saw_blade", 60, "metal_block:40", "component_kit:1")]
    [InlineData("horn", 10, "metal_block:1", "circuit_board:1", "glue:1")]
    [InlineData("radio", 10, "metal_block:5", "circuit_board:1", "glue:1")]
    [InlineData("logic_gate", 20, "metal_block:1", "circuit_board:1", "glue:1")]
    [InlineData("timer", 45, "metal_block:2", "circuit_board:2", "glue:1")]
    public void VehiclePartsTakeTheWikiRecipes(string id, float time, params string[] inputs)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find(id)!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(inputs.Select(i => $"vanilla:{i}"), recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(1, recipe.Outputs.Single().Count);
        Assert.Equal(time, recipe.Time);
        Assert.Single(book.All, r => r.Outputs.Any(o => o.Item == $"vanilla:{id}"));
    }

    [Theory]
    // Parts map batch 4 (seats and logic): the wiki's bars. The Bathtub, Driver's Saddle, Saddle and Toilet are new.
    [InlineData("drivers_seat", 7.5, 3, 0.5, 0.2, true, 10)]
    [InlineData("passenger_seat", 4.5, 3, 0.5, 0.2, true, 10)]
    [InlineData("drivers_saddle", 7.5, 3, 0.7, 0.5, false, 10)]
    [InlineData("saddle", 4.5, 4, 0.7, 0.5, false, 10)]
    [InlineData("toilet", 12, 3, 0.3, 0.4, false, 10)]
    [InlineData("bathtub", 7.5, 4, 0.2, 0.8, false, 10)]
    [InlineData("controller", 4.5, 3, 0.5, 0.2, false, 1)]
    [InlineData("sensor", 4.5, 3, 0.7, 0.5, false, 20)]
    [InlineData("turret_seat", 3, 5, 0.3, 0.2, false, 10)]
    public void SeatsAndLogicPartsTakeTheWikiStats(string id, double mass, int durability, double friction, double buoyancy,
        bool flammable, int stack) => VehiclePartsTakeTheWikiStats(id, mass, durability, friction, buoyancy, flammable, stack);

    [Theory]
    // Parts map batch 4: the wiki's Craftbot recipes, at the Fabricator only.
    [InlineData("drivers_seat", 30, "metal_block:20", "component_kit:2", "circuit_board:1", "cotton:10")]
    [InlineData("passenger_seat", 30, "metal_block:10", "component_kit:1", "circuit_board:1", "cotton:10")]
    [InlineData("drivers_saddle", 30, "metal_block:15", "component_kit:2", "circuit_board:1", "cotton:5")]
    [InlineData("saddle", 40, "metal_block:5", "cotton:5", "circuit_board:2")]
    [InlineData("toilet", 5, "metal_block:5", "wood_block:5")]
    [InlineData("bathtub", 10, "metal_block:10", "paint_ammo:2")]
    [InlineData("controller", 10, "metal_block:5", "component_kit:1", "circuit_board:5", "glue:1")]
    [InlineData("sensor", 30, "metal_block:1", "glass_block:1", "circuit_board:2", "glue:2")]
    [InlineData("turret_seat", 30, "metal_block_2:50", "component_kit:5", "bearing:2")]
    public void SeatsAndLogicPartsTakeTheWikiRecipes(string id, float time, params string[] inputs) =>
        VehiclePartsTakeTheWikiRecipes(id, time, inputs);

    [Theory]
    // Parts map batch 4: crafted at level 1 only and upgraded in place, each level its own model and icon. A seat's
    // levels add links (its hotbar); the Controller's add output links and joint speed; the Sensor's add range.
    [InlineData("drivers_seat", "Driver's Seat", new[] { 1, 2, 3, 5 }, new[] { 6, 8, 12, 16, 20 })]
    [InlineData("passenger_seat", "Passenger Seat", new[] { 1, 1, 1, 1 }, new[] { 2, 4, 6, 8, 10 })]
    [InlineData("drivers_saddle", "Driver's Saddle", new[] { 1, 2, 3, 5 }, new[] { 6, 8, 12, 16, 20 })]
    [InlineData("saddle", "Saddle", new[] { 1, 1, 1, 1 }, new[] { 3, 4, 6, 8, 10 })]
    [InlineData("controller", "Controller", new[] { 1, 2, 3, 4 }, new[] { 1, 2, 4, 8, 10 })]
    [InlineData("sensor", "Sensor", new[] { 1, 1, 1, 3 }, new[] { 10, 10, 10, 10, 10 })]
    [InlineData("turret_seat", "Turret Seat", new[] { 2, 4, 6, 10 }, new[] { 6, 8, 12, 16, 20 })]
    public void EachLeveledSeatAndLogicPartIsCraftedAtLevel1AndUpgradedInPlace(string id, string name, int[] kits, int[] outputs)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        Assert.Equal([$"vanilla:{id}"], book.All.SelectMany(r => r.Outputs).Select(o => o.Item).Where(i => i == $"vanilla:{id}" || i.StartsWith($"vanilla:{id}@", StringComparison.Ordinal)).Distinct());
        var written = Part(id);
        Assert.Equal(4, PartUpgrades.MaxLevel(written));
        Assert.Equal(kits, written.Upgrades!.Select(u => u.Cost!.Single(c => c.Item == "component_kit").Count!.Value));
        var vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");
        var meshes = new HashSet<string>();
        for (var level = 0; level <= 4; level++)
        {
            var part = PartUpgrades.At(written, level);
            Assert.Equal($"{name} {level + 1}", part.Name);
            Assert.Equal($"meshes/parts/{id}_{level + 1}.glb", part.Mesh);
            Assert.Equal($"textures/items/{id}_{level + 1}_icon.png", part.Icon);
            Assert.True(File.Exists(Path.Combine(vanilla, part.Mesh!)), $"level {level + 1}'s model {part.Mesh}");
            Assert.True(File.Exists(Path.Combine(vanilla, part.Icon!)), $"level {level + 1}'s icon {part.Icon}");
            Assert.True(meshes.Add(part.Mesh!), "each level looks different");
            Assert.Equal(outputs[level], part.Logic!.Outputs);
            // The last level's description no longer offers an upgrade.
            Assert.Equal(level < 4, part.Description!.Contains("Upgrade it in place", StringComparison.Ordinal));
            if (id == "controller")
                Assert.Equal(new[] { 2, 3, 5, 7, 9 }[level], part.Settings!.Single(s => s.Id == "frames").MaxSpeed);
            if (id == "sensor")
                Assert.Equal(level + 1, part.Settings!.Single(s => s.Id == "range").Max);
        }
    }

    [Theory]
    // Parts map batch 4: where a rider sits. Passenger seats keep their hands free for the hotbar; saddles are ridden
    // astride (the sit_saddle clips), the driver's with its hands on the handlebars.
    [InlineData("drivers_seat", SeatHands.Wheel, SeatPose.Chair)]
    [InlineData("passenger_seat", SeatHands.Free, SeatPose.Chair)]
    [InlineData("drivers_saddle", SeatHands.Wheel, SeatPose.Saddle)]
    [InlineData("saddle", SeatHands.Free, SeatPose.Saddle)]
    [InlineData("toilet", SeatHands.Free, SeatPose.Chair)]
    [InlineData("bathtub", SeatHands.Free, SeatPose.Chair)]
    [InlineData("turret_seat", SeatHands.Wheel, SeatPose.Chair)]
    public void EachSeatSitsItsRiderTheWayItIsBuilt(string id, SeatHands hands, SeatPose pose)
    {
        var part = Part(id);
        Assert.Equal(hands, part.Seat!.Hands);
        Assert.Equal(pose, part.Seat.Pose ?? SeatPose.Chair);
        Assert.True(part.Logic!.Outputs > 0, $"{id}'s links go on the rider's hotbar");
        Assert.Equal(pose == SeatPose.Saddle, SeatStyle.For(part).Saddle);
    }

    [Fact]
    public void TheTurretSeatTurnsFurtherFasterAndHarderEachLevel()
    {
        // Parts map batch 17d (E8): its aimed socket's limits and the script's aim speed and torque rise with each
        // level; from level 4 it turns all the way round.
        var written = Part("turret_seat");
        var last = (Yaw: 0f, Up: 0f, Down: 0f, Speed: 0.0, Torque: 0.0);
        for (var level = 0; level <= 4; level++)
        {
            var part = PartUpgrades.At(written, level);
            var joint = part.Sockets!.Single(s => s.Type == SocketType.Aimed).Joint!;
            var yaw = joint.Max ?? MathF.PI;
            Assert.Equal(level >= 3, joint.Max is null);
            Assert.Equal(joint.Min is null ? 0 : -joint.Min!.Value, joint.Max ?? 0);
            var speed = part.Properties!["aim_speed"]!.GetValue<double>();
            var torque = part.Properties!["aim_torque"]!.GetValue<double>();
            Assert.True(yaw >= last.Yaw && joint.PitchMax > last.Up && -joint.PitchMin!.Value > last.Down, $"level {level + 1} aims further");
            Assert.True(speed > last.Speed && torque > last.Torque, $"level {level + 1} aims faster");
            last = (yaw, joint.PitchMax!.Value, -joint.PitchMin!.Value, speed, torque);
        }
    }

    [Theory]
    [InlineData("gas_engine")]
    [InlineData("electric_engine")]
    public void EachEngineUpgradesForTheWikisKitsAndGetsTougherEachLevel(string id)
    {
        // Parts map batch 3: 4/6/8/10 Component Kits (28 in all), as the wiki; durability rises a point a level.
        var written = Part(id);

        Assert.Equal([4, 6, 8, 10], written.Upgrades!.Select(u => u.Cost!.Single(c => c.Item == "component_kit").Count!.Value));
        for (var level = 1; level <= PartUpgrades.MaxLevel(written); level++)
            Assert.Equal(PartUpgrades.At(written, level - 1).Durability + 1, PartUpgrades.At(written, level).Durability);
    }

    [Fact]
    public void GasEnginesHaveAPowerSliderAndTheElectricEngineASpeedSlider()
    {
        // Parts map batch 3 (Scrap Mechanic's engines): a gas engine turns its level's fixed speed and its slider sets the
        // power; the Electric Engine pulls its full torque and its slider sets the speed.
        foreach (var id in new[] { "gas_engine", "scrap_gas_engine" })
            Assert.Equal("power", Part(id).Properties!["slider"]!.GetValue<string>());
        Assert.Equal("speed", Part("electric_engine").Properties!["slider"]!.GetValue<string>());
        // A Gas Engine's mileage per gasoline goes 1, 1.5, 2.25, 3 and 5 times level 1's (the wiki's power per unit of fuel).
        var gas = Part("gas_engine");
        var mileage = Enumerable.Range(0, 5).Select(l => PartUpgrades.At(gas, l).Properties!["fuel_time"]!.GetValue<double>() / 40).ToList();
        Assert.Equal([1, 1.5, 2.25, 3, 5], mileage);
    }

    [Theory]
    // Parts map batch 5 (lights): the wiki's bars. The Frame Beam Light, Neon Sign, Packing Lamp, Protector Sign, Shack
    // Light and Fluorescent Light are new; the Construction and Warning Lights are ours and keep their numbers.
    [InlineData("headlight", 3, 3, 0.4, 0.4, false, 1)]
    [InlineData("warehouse_spotlight", 6, 2, 0.4, 0.4, true, 20)]
    [InlineData("interior_light", 7.5, 2, 0.5, 0.3, false, 20)]
    [InlineData("spotlight", 7.5, 4, 0.4, 0.2, false, 20)]
    [InlineData("frame_beam_light", 7.5, 4, 0.4, 0.1, false, 20)]
    [InlineData("neon_sign", 3, 1, 0.5, 0.4, true, 20)]
    [InlineData("packing_lamp", 6, 4, 0.5, 0.4, false, 1)]
    [InlineData("protector_sign", 7.5, 3, 0.7, 0.1, false, 20)]
    [InlineData("shack_light", 3, 2, 0.5, 0.4, true, 1)]
    [InlineData("fluorescent_light", 7.5, 4, 0.4, 0.3, false, 20)]
    public void LightsTakeTheWikiStats(string id, double mass, int durability, double friction, double buoyancy,
        bool flammable, int stack) => VehiclePartsTakeTheWikiStats(id, mass, durability, friction, buoyancy, flammable, stack);

    [Theory]
    // Parts map batch 5: the Headlight takes the wiki's Craftbot recipe. SM cannot craft the warehouse and shack lights,
    // so the Spotlight, Warehouse Spotlight and Interior Light keep ours and the new ones get a cheap recipe.
    [InlineData("headlight", 5, "metal_block:1", "glass_block:1", "circuit_board:1", "glue:1")]
    [InlineData("spotlight", 5, "scrap_metal_block:8", "component_kit:1", "circuit_board:1")]
    [InlineData("warehouse_spotlight", 8, "scrap_metal_block:16", "component_kit:2", "circuit_board:1")]
    [InlineData("interior_light", 3, "scrap_metal_block:4", "component_kit:1")]
    [InlineData("frame_beam_light", 5, "metal_block:2", "glass_block:1")]
    [InlineData("neon_sign", 5, "metal_block:2", "glass_block:2", "paint_ammo:1")]
    [InlineData("packing_lamp", 5, "metal_block:2", "glass_block:1")]
    [InlineData("protector_sign", 5, "metal_block:2", "circuit_board:1")]
    [InlineData("fluorescent_light", 5, "metal_block:2", "glass_block:1")]
    public void LightsTakeTheWikiRecipes(string id, float time, params string[] inputs) =>
        VehiclePartsTakeTheWikiRecipes(id, time, inputs);

    [Theory]
    // Parts map batch 6 (working parts): the wiki's bars. The Plasma Drill, Sticky Wheel, Explosive Canisters, Alarm
    // Clock and Cash Register are new, and the Scuttler Head is now a part (it plays notes).
    [InlineData("thruster", 12, 5, 0.5, 0.2, false, 10)]
    [InlineData("water_cannon", 7.5, 5, 0.4, 0.2, false, 10)]
    [InlineData("potato_blaster_mounted", 10.5, 5, 0.4, 0.2, false, 5)]
    [InlineData("plasma_drill", 4.5, 8, 0.2, 0.3, false, 5)]
    [InlineData("sticky_wheel", 6, 7, 1.1, 0.2, false, 20)]
    [InlineData("explosive_canister_small", 6, 10, 0.3, 0.7, true, 10)]
    [InlineData("explosive_canister_large", 6, 10, 0.3, 0.7, true, 5)]
    [InlineData("alarm_clock", 4.5, 3, 0.4, 0.6, true, 10)]
    [InlineData("cash_register", 7.5, 4, 0.6, 0.2, true, 1)]
    [InlineData("scuttler_head", 6, 4, 0.4, 0.6, false, 5)]
    public void WorkingPartsTakeTheWikiStats(string id, double mass, int durability, double friction, double buoyancy,
        bool flammable, int stack) => VehiclePartsTakeTheWikiStats(id, mass, durability, friction, buoyancy, flammable, stack);

    [Theory]
    // Parts map batch 6: the wiki's Craftbot recipes with our materials (Glow is explosive_remnant, Chili gasoline,
    // Bee Wax beeswax, Glue Clam chemical). The Scuttler Head only drops from Scuttlers.
    [InlineData("thruster", 30, "metal_block_3:50", "component_kit:5", "gas_engine:1", "controller:1")]
    [InlineData("water_cannon", 30, "metal_block:10", "circuit_board:1", "glue:2")]
    [InlineData("potato_blaster_mounted", 15, "metal_block_2:20", "component_kit:1", "circuit_board:4")]
    [InlineData("plasma_drill", 60, "metal_block_2:100", "component_kit:10", "circuit_board:10")]
    [InlineData("sticky_wheel", 30, "metal_block_2:20", "beeswax:10", "chemical:5")]
    [InlineData("explosive_canister_small", 10, "metal_block:5", "explosive_remnant:2", "gasoline:2")]
    [InlineData("explosive_canister_large", 15, "metal_block:10", "explosive_remnant:4", "gasoline:3")]
    [InlineData("alarm_clock", 10, "metal_block:5", "glass_block:10", "circuit_board:2")]
    [InlineData("cash_register", 5, "metal_block:5", "circuit_board:1")]
    public void WorkingPartsTakeTheWikiRecipes(string id, float time, params string[] inputs) =>
        VehiclePartsTakeTheWikiRecipes(id, time, inputs);

    [Fact]
    public void TheThrusterIsCraftedAtLevel1AndUpgradedForPowerStepsAndMileage()
    {
        // Parts map batch 6: five levels for 10 Component Kits each, lighter and tougher each level, one more power step
        // (3 to 7) and better mileage (gasoline lasts 112 s at level 1, 224 s at level 5), each level its own model.
        var written = Part("thruster");
        var vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

        Assert.Equal(4, PartUpgrades.MaxLevel(written));
        Assert.Equal([10, 10, 10, 10], written.Upgrades!.Select(u => u.Cost!.Single(c => c.Item == "component_kit").Count!.Value));
        for (var level = 0; level <= 4; level++)
        {
            var part = PartUpgrades.At(written, level);
            Assert.Equal($"Thruster {level + 1}", part.Name);
            Assert.Equal($"meshes/parts/thruster_{level + 1}.glb", part.Mesh);
            Assert.Equal($"textures/items/thruster_{level + 1}_icon.png", part.Icon);
            Assert.True(File.Exists(Path.Combine(vanilla, part.Mesh!)), $"level {level + 1}'s model {part.Mesh}");
            Assert.True(File.Exists(Path.Combine(vanilla, part.Icon!)), $"level {level + 1}'s icon {part.Icon}");
            Assert.Equal(new[] { 12, 9, 7.5, 6, 4.5 }[level], (double)part.Mass!.Value, 3);
            Assert.Equal(5 + level, (int)part.Durability!.Value);
            Assert.Equal(3 + level, (double)part.Settings!.Single(s => s.Id == "power").Max!.Value);
            Assert.Equal(112 + 28 * level, part.Properties!["fuel_time"]!.GetValue<double>());
            Assert.Equal(level < 4, part.Description!.Contains("Upgrade it in place", StringComparison.Ordinal));
        }
    }

    [Theory]
    // Parts map batch 6: a real model per part (tools/<model>/build_<model>.py), its icon, and the clips its script
    // plays on the readout the part's display names.
    [InlineData("water_cannon", "water_cannon", "fire")]
    [InlineData("potato_blaster_mounted", "potato_blaster_mounted", "fire")]
    [InlineData("plasma_drill", "plasma_drill", "off", "on")]
    [InlineData("sticky_wheel", "sticky_wheel", "off", "on")]
    [InlineData("explosive_canister_small", "explosive_canister", "fuse_loop")]
    [InlineData("explosive_canister_large", "explosive_canister", "fuse_loop")]
    [InlineData("alarm_clock", "alarm_clock", "armed", "off", "ring_loop")]
    [InlineData("cash_register", "cash_register", "close", "open")]
    [InlineData("scuttler_head", "scuttler_head", "note")]
    [InlineData("thruster", "thruster", "off", "on")]
    public void EachWorkingPartHasItsOwnModelIconAndClips(string id, string tool, params string[] clips)
    {
        var part = Part(id);
        var mod = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

        Assert.StartsWith($"meshes/parts/{id}", part.Mesh, StringComparison.Ordinal);
        Assert.StartsWith($"textures/items/{id}", part.Icon, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(mod, part.Mesh!)), $"{id} has a model");
        Assert.True(File.Exists(Path.Combine(mod, part.Icon!)), $"{id} has an icon");
        Assert.True(File.Exists(Path.Combine(TestMods.RepoRoot, "tools", tool, $"build_{tool}.py")), $"{id} has its build script");
        Assert.Equal(clips, GlbAnimations(Path.Combine(mod, part.Mesh!)).Order(StringComparer.Ordinal));
        var readout = part.Display?.Clips?.Readout;
        Assert.NotNull(readout);
        Assert.Contains($"part.display(\"{readout}\"", File.ReadAllText(Path.Combine(mod, part.Script!)), StringComparison.Ordinal);
    }

    [Theory]
    // Parts map batch 7 (beds and stations): the wiki's bars. The Fabricator and Refiner have no stack size (-1).
    [InlineData("respawn_bed", 4.5, 3, 0.4, 0.6, true, 1)]
    [InlineData("fancy_bed", 4.5, 7, 0.4, 0.6, false, 5)]
    [InlineData("freezer", 4.5, 7, 0.6, 0.2, false, 5)]
    [InlineData("anvil", 15, 5, 0.8, 0.1, false, 20)]
    // Consumables batch C4: the wiki gives the Cookbot no stats, so the Field Kitchen takes the Refiner's, stack 1.
    [InlineData("field_kitchen", 10.5, 7, 0.6, 0.1, false, 1)]
    [InlineData("fabricator", 10.5, 7, 0.6, 0.1, false, -1)]
    [InlineData("refiner", 10.5, 7, 0.6, 0.1, false, -1)]
    public void BedsAndStationsTakeTheWikiStats(string id, double mass, int durability, double friction, double buoyancy,
        bool flammable, int stack)
    {
        var part = Part(id);

        Assert.Equal((mass, durability, friction, buoyancy, flammable, stack),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, part.StackSize ?? -1));
    }

    [Theory]
    // Parts map batch 7: the wiki's Craftbot recipes. The Refiner keeps ours (our early path to Gasoline) and the
    // Fabricator has none (it is picked up at the Workshop).
    [InlineData("respawn_bed", 30, "wood_block:50", "cotton:20", "paint_ammo:10")]
    [InlineData("fancy_bed", 30, "metal_block_2:25", "cotton:20", "paint_ammo:10")]
    [InlineData("freezer", 30, "metal_block_2:20", "component_kit:2", "circuit_board:4", "chemical:20")]
    [InlineData("anvil", 10, "metal_block_2:100")]
    [InlineData("field_kitchen", 11, "metal_block_2:40", "circuit_board:10", "component_kit:10")]
    [InlineData("refiner", 15, "scrap_metal_block:40", "component_kit:4", "circuit_board:2")]
    public void BedsAndStationsTakeTheWikiRecipes(string id, float time, params string[] inputs) =>
        VehiclePartsTakeTheWikiRecipes(id, time, inputs);

    [Fact]
    public void TheFabricatorKeepsItsFiveLevelsAndNoRecipe()
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var written = Part("fabricator");

        Assert.DoesNotContain(book.All, r => r.Outputs.Any(o => o.Item.StartsWith("vanilla:fabricator", StringComparison.Ordinal)));
        Assert.Equal(4, PartUpgrades.MaxLevel(written));
        for (var level = 0; level <= 4; level++)
            Assert.Equal($"meshes/parts/fabricator_{level + 1}.glb", PartUpgrades.At(written, level).Mesh);
    }

    [Theory]
    // Parts map batch 7: a real model per part (tools/<part>/build_<part>.py) and icon; the stations play their clips
    // from the crafting queue's readout (no script), the beds and the Anvil have none.
    [InlineData("respawn_bed")]
    [InlineData("fancy_bed")]
    [InlineData("anvil")]
    [InlineData("freezer", "complete", "idle_loop", "start", "work_loop")]
    [InlineData("refiner", "complete", "idle_loop", "start", "work_loop")]
    [InlineData("field_kitchen", "complete", "idle_loop", "start", "work_loop")]
    public void EachBedAndStationHasItsOwnModelIconAndClips(string id, params string[] clips)
    {
        var part = Part(id);
        var mod = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

        Assert.Equal($"meshes/parts/{id}.glb", part.Mesh);
        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        Assert.True(File.Exists(Path.Combine(mod, part.Mesh!)), $"{id} has a model");
        Assert.True(File.Exists(Path.Combine(mod, part.Icon!)), $"{id} has an icon");
        Assert.True(File.Exists(Path.Combine(TestMods.RepoRoot, "tools", id, $"build_{id}.py")), $"{id} has its build script");
        Assert.Equal(clips, GlbAnimations(Path.Combine(mod, part.Mesh!)).Order(StringComparer.Ordinal));
        if (clips.Length == 0)
            return;
        Assert.Equal(("station", "idle_loop"), (part.Display?.Clips?.Readout, part.Display?.Clips?.Idle));
        Assert.Equal(("start", "work_loop", "complete"), (part.Crafting?.Clips?.Start, part.Crafting?.Clips?.Work, part.Crafting?.Clips?.Complete));
    }

    [Theory]
    [InlineData("respawn_bed", true)]
    [InlineData("fancy_bed", false)]
    public void BothBedsAreRespawnPointsAndOnlyTheWoodenOneBurns(string id, bool flammable)
    {
        var part = Part(id);

        Assert.True(part.RespawnPoint);
        Assert.Equal(flammable, part.Flammable);
        Assert.Equal(("bed_use", "bed_set"), (part.Sounds!["use"], part.Sounds!["set"]));
    }

    [Fact]
    public void TheFreezerTurnsAFullBucketInto20IceBlocksAndGivesTheBucketBack()
    {
        // Parts map batch 7: the wiki's Freezer makes 20 Ice Blocks from a Water in about a minute; our Water is the full bucket.
        var book = RecipeBook.Load(Vanilla.Value);
        var freezer = Part("freezer");

        var recipe = Assert.Single(book.ForStation("freezer"));
        Assert.Equal(["vanilla:water_bucket_full:1"], recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(["vanilla:ice_block:20", "vanilla:water_bucket:1"], recipe.Outputs.Select(o => $"{o.Item}:{o.Count}"));
        Assert.Equal(60f, recipe.Time);
        Assert.Equal(PartCategory.Station, freezer.Category);
        Assert.True(freezer.Inventory!.Slots >= 2, "room for the ice and the bucket");
        Assert.DoesNotContain(book.ForStation("fabricator"), r => r.QualifiedId == recipe.QualifiedId);
    }

    [Fact]
    public void TheScuttlerHeadIsTheScuttlersDropAndCannotBeCrafted()
    {
        var book = RecipeBook.Load(Vanilla.Value);

        Assert.DoesNotContain(book.All, r => r.Outputs.Any(o => o.Item == "vanilla:scuttler_head"));
        Assert.Contains("scuttler_head", DroppedItems());
        Assert.True(Part("scuttler_head").IsPlaceable);
    }

    [Fact]
    public void TheShackLightIsMadeFromScrapAtEitherFabricator()
    {
        // Scrap metal is the scrap tier, so the Portable Fabricator makes it as well as the Fabricator: one recipe at both.
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find("shack_light")!;

        Assert.Equal(["vanilla:fabricator", "vanilla:portable_fabricator"], recipe.Stations);
        Assert.Equal(["vanilla:scrap_metal_block:2", "vanilla:glass_block:1"], recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(5f, recipe.Time);
        Assert.Single(book.All, r => r.Outputs.Any(o => o.Item == "vanilla:shack_light"));
    }

    public static TheoryData<string> Lights => [.. Items.Where(id => Part(id).Category == PartCategory.Light && id != "beacon")];

    [Theory]
    [MemberData(nameof(Lights))]
    public void EachLightHasItsOwnModelIconAndOnAndOffClips(string id)
    {
        // Parts map batch 5: a real model per light (tools/<id>/build_<id>.py) and its icon. A light that can go out
        // plays its model's on and off clips from light.lua's "pose" readout; the glowstick is always lit.
        var part = Part(id);
        var mod = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

        Assert.Equal($"meshes/parts/{id}.glb", part.Mesh);
        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        Assert.True(File.Exists(Path.Combine(mod, part.Mesh!)), $"{id} has a model");
        Assert.True(File.Exists(Path.Combine(mod, part.Icon!)), $"{id} has an icon");
        Assert.True(File.Exists(Path.Combine(TestMods.RepoRoot, "tools", id, $"build_{id}.py")), $"{id} has its build script");
        var clips = GlbAnimations(Path.Combine(mod, part.Mesh!));
        if (id == "glowstick")
        {
            Assert.Null(part.Display);
            Assert.Empty(clips);
            return;
        }
        Assert.Equal("pose", part.Display?.Clips?.Readout);
        Assert.Equal(["off", "on"], clips.Order(StringComparer.Ordinal));
        Assert.Contains("part.display(\"pose\"", File.ReadAllText(Path.Combine(mod, part.Script!)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("shack_light")]
    [InlineData("neon_sign")]
    public void TheShackLampsHaveABrightnessSetting(string id)
    {
        var brightness = Part(id).Settings!.Single(s => s.Id == "brightness");

        Assert.Equal((1.0, 10.0, 10.0), ((double)brightness.Min!.Value, (double)brightness.Max!.Value, brightness.Default!.Value.GetDouble()));
    }

    [Fact]
    public void LampsShineWarmWhiteAndNeverOrange()
    {
        // docs/art-direction.md: lamp light is warm white; the Warning Light is signal red, the Neon Sign mint, the Glowstick
        // green and the Beacon cyan. No light is orange (Scrap Mechanic's signature, banned).
        var coloured = new Dictionary<string, string>
        {
            ["warning_light"] = "#ff3d2e", ["neon_sign"] = "#7dffc4", ["glowstick"] = "#60ff70", ["beacon"] = "#38c8ff",
        };
        foreach (var id in Items.Where(id => Part(id).Category == PartCategory.Light))
        {
            var color = Part(id).Light!.Color!;
            if (coloured.TryGetValue(id, out var expected))
            {
                Assert.Equal(expected, color);
                continue;
            }
            var (r, g, b) = (Convert.ToInt32(color[1..3], 16), Convert.ToInt32(color[3..5], 16), Convert.ToInt32(color[5..7], 16));
            Assert.True(r == 255 && g >= 240 && b >= 200 && b < g, $"{id} shines warm white ({color})");
        }
    }

    // The clip names in a .glb's glTF JSON chunk.
    private static List<string> GlbAnimations(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var length = BitConverter.ToInt32(bytes, 12);
        using var doc = System.Text.Json.JsonDocument.Parse(bytes.AsMemory(20, length));
        return doc.RootElement.TryGetProperty("animations", out var animations)
            ? [.. animations.EnumerateArray().Select(a => a.GetProperty("name").GetString()!)]
            : [];
    }

    [Theory]
    [InlineData("fabricator", "vanilla:wheel", "vanilla:big_wheel", "vanilla:sticky_wheel")]
    [InlineData("fabricator", "vanilla:gas_engine", "vanilla:electric_engine")]
    [InlineData("fabricator", "vanilla:switch", "vanilla:button")]
    [InlineData("fabricator", "vanilla:passenger_seat", "vanilla:saddle", "vanilla:drivers_seat", "vanilla:drivers_saddle", "vanilla:turret_seat", "vanilla:controller")]
    [InlineData("fabricator", "vanilla:toilet", "vanilla:bathtub")]
    [InlineData("fabricator", "vanilla:timer", "vanilla:sensor", "vanilla:alarm_clock")]
    [InlineData("fabricator", "vanilla:drill", "vanilla:plasma_drill", "vanilla:saw_blade", "vanilla:water_cannon",
        "vanilla:potato_blaster_mounted", "vanilla:explosive_canister_small", "vanilla:explosive_canister_large")]
    [InlineData("fabricator", "vanilla:shack_light", "vanilla:glowstick", "vanilla:fluorescent_light", "vanilla:packing_lamp",
        "vanilla:interior_light", "vanilla:headlight", "vanilla:spotlight", "vanilla:construction_light", "vanilla:warning_light",
        "vanilla:beacon", "vanilla:warehouse_spotlight", "vanilla:frame_beam_light", "vanilla:neon_sign", "vanilla:protector_sign")]
    [InlineData("fabricator", "vanilla:portable_fabricator", "vanilla:refiner", "vanilla:respawn_bed", "vanilla:fancy_bed", "vanilla:freezer", "vanilla:field_kitchen")]
    [InlineData("field_kitchen", "vanilla:garden_burger", "vanilla:colony_burger")]
    [InlineData("fabricator", "vanilla:bathtub", "vanilla:anvil", "vanilla:cash_register")]
    [InlineData("portable_fabricator", "vanilla:scrap_wood_wedge", "vanilla:scrap_metal_wedge", "vanilla:scrap_seat",
        "vanilla:scrap_drivers_seat", "vanilla:scrap_wheel", "vanilla:scrap_gas_engine", "vanilla:gasoline", "vanilla:bearing_scrap",
        "vanilla:switch_scrap", "vanilla:shack_light", "vanilla:beacon_scrap", "vanilla:connect_tool")]
    public void RelatedRecipesSitTogetherScrapTierFirst(string station, params string[] ids)
    {
        var listed = RecipeBrowser.Ordered(RecipeBook.Load(Vanilla.Value).ForStation(station)).Select(r => r.QualifiedId).ToList();
        var at = listed.IndexOf(ids[0]);

        Assert.Equal(ids, listed.Skip(at).Take(ids.Length));
    }

    [Theory]
    [InlineData("scrap_wheel", "Scrap Wheel", 3, 1)]
    [InlineData("wheel", "Wheel", 3, 1)]
    [InlineData("big_wheel", "Off Road Wheel", 5, 2)]
    public void WheelsHaveTheirNameModelAndIcon(string id, string name, int across, int wide)
    {
        // The Wheel Design Spec: Scrap Wheel and Wheel are 3 x 1 x 3 blocks, the Off Road Wheel (id big_wheel,
        // once the Big Wheel) 5 x 2 x 5; each has its own model and hotbar picture and rolls on a cylinder.
        var part = Part(id);
        var mod = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

        Assert.Equal(name, part.Name);
        Assert.Equal(new GridSize(across, wide, across), part.Size);
        Assert.Equal($"meshes/{id}.glb", part.Mesh);
        Assert.True(part.Icon is not null && File.Exists(Path.Combine(mod, part.Icon)), $"{id} has an icon");
        Assert.Equal(CollisionShape.Cylinder, part.Collision?.Shape);
    }

    [Theory]
    // Parts map batch 17a: the wiki's bars for the Ladder (E2) and the Chemical Bucket (E11).
    [InlineData("ladder", 4.5, 5, 0.4, 0.2, true, 10)]
    [InlineData("chemical_bucket", 7.5, 3, 0.7, 0.5, false, 1)]
    public void Batch17aPartsTakeTheWikiStats(string id, double mass, int durability, double friction, double buoyancy,
        bool flammable, int stack) =>
        BedsAndStationsTakeTheWikiStats(id, mass, durability, friction, buoyancy, flammable, stack);

    [Fact]
    public void TheLadderIsClimbableAndMadeFromMetalAndGlue()
    {
        // Parts map batch 17a (E2): the wiki's 20 Metal Block 2 + 2 Glue in 10 s at the Craftbot, at the Fabricator.
        BedsAndStationsTakeTheWikiRecipes("ladder", 10, "metal_block_2:20", "glue:2");
        var ladder = Part("ladder");
        Assert.True(ladder.Climbable);
        Assert.Equal(new GridSize(2, 8, 1), ladder.Size);
        Assert.Equal("meshes/parts/ladder.glb", ladder.Mesh);
    }

    [Fact]
    public void AChemicalPoolFillsABucketThatPoursIntoTheChemicalContainer()
    {
        // Parts map batch 17a (E11): no recipe; the vanilla chemical pool turns an empty bucket into a Chemical Bucket, which
        // pours Chemical into a container that takes it and goes back to the empty bucket.
        var book = RecipeBook.Load(Vanilla.Value);
        Assert.DoesNotContain(book.All, r => r.Outputs.Any(o => o.Item == "vanilla:chemical_bucket"));
        var world = WorldGenData.Load(Vanilla.Value);
        Assert.Equal("chemical_bucket", world.Definition.Liquids!.Single(l => l.Id == "chemical").Bucket);
        var bucket = Part("chemical_bucket");
        Assert.Equal("scripts/chemical_bucket.lua", bucket.Script);
        Assert.Equal("chemical", bucket.Properties!["item"]!.GetValue<string>());
        Assert.Equal("water_bucket", bucket.Properties["empty"]!.GetValue<string>());
        Assert.Contains("chemical", Part("chemical_container").Inventory!.Accepts!);
    }

    // Items that bots, world objects, harvests and POIs hand out, the Survival starting kit, and the item
    // water (or a pool of another liquid) fills, read from the data files.
    private static HashSet<string> DroppedItems()
    {
        var found = new HashSet<string>();
        var mod = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");
        var files = Directory.GetFiles(mod, "*.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}editor{Path.DirectorySeparatorChar}"))
            .Append(Path.Combine(TestMods.RepoRoot, "game", "data", "survival.json"));
        foreach (var file in files)
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file),
                "\"(?:item|ref|crop|water_item|bucket)\"\\s*:\\s*\"(?:vanilla:)?([a-z0-9_]+)\""))
                found.Add(m.Groups[1].Value);
        }
        return found;
    }
}
