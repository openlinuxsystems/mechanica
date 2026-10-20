using Mechanica.Core.Data;
using Mechanica.Core.Items;

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
        "water_cannon", "potato_blaster_mounted", "scrap_wheel", "wheel", "big_wheel",
        // Containers, stations, lights, utility
        "chest", "gas_container", "battery", "chemical_container", "fridge", "locker", "file_cabinet", "tool_cabinet",
        "wooden_crate", "metal_crate", "barrel", "fabricator", "portable_fabricator", "refiner", "dispenser", "respawn_bed",
        "radio", "beacon", "headlight", "spotlight", "warehouse_spotlight", "construction_light", "interior_light",
        "warning_light",
        // Resources, consumables, handheld weapons (produce_crate is left out for now)
        "scrap_wood", "component_kit", "circuit_board", "glue", "gasoline", "crude_oil", "battery_pack",
        "master_battery", "warehouse_key", "explosive_remnant", "scuttler_head", "carrot_seed", "potato_seed", "tomato_seed",
        "carrot", "potato", "tomato", "water_bucket", "glowstick", "popper", "potato_blaster", "potato_scattergun",
        "potato_launcher",
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
        // Bearings, suspensions and wheels are driven by other parts' scripts and need none of their own.
        var part = Part(id);
        if (part.Category == PartCategory.Light || part.Logic?.Use is not null || part.Logic?.Outputs > 0)
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
    [InlineData("gas_engine", "gasoline", new[] { "+2", "+2", "+44%", "+50%" })]
    [InlineData("electric_engine", "battery_pack", new[] { "+2", "+2", "+40%", "+50%" })]
    public void EachEngineMenuIsALeverWithItsFuelAndWhatTheNextLevelGains(string id, string fuel, string[] gains)
    {
        var written = Part(id);
        var lever = written.Menu!.Lever!;
        Assert.Equal(("gear", "on", fuel), (lever.Setting, lever.Off, lever.Fuel));
        // The lever runs to the last level's top step.
        Assert.Equal(13, PartUpgrades.Stat(PartUpgrades.At(written, PartUpgrades.MaxLevel(written)), "settings.gear"));
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

    [Theory]
    [InlineData("fabricator", "vanilla:scrap_wheel", "vanilla:wheel", "vanilla:big_wheel")]
    [InlineData("fabricator", "vanilla:scrap_gas_engine", "vanilla:gas_engine", "vanilla:electric_engine")]
    [InlineData("portable_fabricator", "vanilla:scrap_wood_block", "vanilla:scrap_wood_wedge",
        "vanilla:scrap_metal_wedge", "vanilla:scrap_wheel", "vanilla:gasoline", "vanilla:bearing", "vanilla:switch", "vanilla:connect_tool")]
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

    // Items that bots, world objects, harvests and POIs hand out, the Survival starting kit, and the item
    // water fills, read from the data files.
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
                "\"(?:item|ref|crop|water_item)\"\\s*:\\s*\"(?:vanilla:)?([a-z0-9_]+)\""))
                found.Add(m.Groups[1].Value);
        }
        return found;
    }
}
