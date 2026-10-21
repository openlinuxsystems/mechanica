using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Tests.Data;
using static Mechanica.Core.Tests.Items.ItemParts;

namespace Mechanica.Core.Tests.Items;

public class RecipeDataTests : IDisposable
{
    private const string Station = """
        { "id": "bench", "name": "Bench", "category": "station", "mass": 20, "durability": 4, "flammable": false,
          "size": [2, 2, 2], "friction": 0.5, "buoyancy": 0, "inventory": { "slots": 4 }, "crafting": { "speed": 1.5, "queue_size": 3 } }
        """;

    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private TestMods Content() => _mods.Mod("vanilla")
        .Part("vanilla", "bench.json", Station)
        .Part("vanilla", "chest.json", TestMods.Block("chest", """{ "category": "container", "inventory": { "slots": 20, "accepts": ["scrap_metal"] } }"""))
        .Part("vanilla", "scrap_metal.json", """{ "id": "scrap_metal", "name": "Scrap Metal", "category": "resource", "stack_size": 50 }""")
        .Part("vanilla", "scrap_wood.json", """{ "id": "scrap_wood", "name": "Scrap Wood", "category": "resource", "stack_size": 50 }""");

    [Fact]
    public void ContainerAndStationFieldsLoadAndRoundTrip()
    {
        var content = Content().Load();

        Assert.False(content.HasErrors, string.Join("\n", content.Diagnostics));
        var bench = content.FindDefinition("bench")!;
        Assert.Equal(4, bench.Inventory!.Slots);
        Assert.Equal(1.5f, bench.Crafting!.Speed);
        Assert.Equal(3, bench.Crafting.QueueSize);
        var written = DataJson.Write(bench);
        Assert.Equal(written, DataJson.Write(DataJson.Read<PartDefinition>(written)!));
        Assert.Contains("\"crafting\": {\n    \"speed\": 1.5,\n    \"queue_size\": 3\n  }", written);
    }

    [Theory]
    [InlineData("""{ "inventory": { } }""", "$.inventory.slots", "slots (1 or more) is required.")]
    [InlineData("""{ "inventory": { "slots": 0 } }""", "$.inventory.slots", "slots must be 1 to 256 (got 0).")]
    [InlineData("""{ "inventory": { "slots": 2, "accepts": ["Bad Id"] } }""", "$.inventory.accepts[0]", "'Bad Id' must be an item id or 'mod_id:item_id'.")]
    [InlineData("""{ "inventory": { "slots": 2, "accepts": ["gas", "vanilla:gas"] } }""", "$.inventory.accepts[1]", "'vanilla:gas' is listed twice.")]
    [InlineData("""{ "crafting": { } }""", "$.crafting", "a crafting station needs an inventory for what it makes.")]
    [InlineData("""{ "inventory": { "slots": 1 }, "crafting": { "speed": 0 } }""", "$.crafting.speed", "speed must be greater than 0.")]
    [InlineData("""{ "inventory": { "slots": 1 }, "crafting": { "queue_size": 0 } }""", "$.crafting.queue_size", "queue_size must be 1 to 64.")]
    [InlineData("""{ "inventory": { "slots": 1 }, "crafting": { "link_range": 9 } }""", "$.crafting.link_range", "link_range must be 0 to 8 m.")]
    [InlineData("""{ "upgrades": [ { "cost": [] } ] }""", "$.upgrades[0].set", "set is required: the part keys this level changes.")]
    [InlineData("""{ "upgrades": [ { "set": { "mass": 9 } } ] }""", "$.upgrades[0].set", "an upgrade can change only name, description, durability, mesh, icon, sockets, settings, display, crafting, inventory, logic, properties, not 'mass'.")]
    [InlineData("""{ "upgrades": [ { "cost": [ { "item": "Bad Id", "count": 0 } ], "set": { "properties": { "a": 1 } } } ] }""", "$.upgrades[0].cost[0].item", "'Bad Id' must be an item id or 'mod_id:item_id'.")]
    [InlineData("""{ "upgrades": [ { "cost": [ { "item": "kit", "count": 0 } ], "set": { "properties": { "a": 1 } } } ] }""", "$.upgrades[0].cost[0].count", "count must be 1 or more.")]
    [InlineData("""{ "inventory": { "slots": 4 }, "upgrades": [ { "set": { "inventory": { "slots": 2 } } } ] }""", "$.upgrades[0].set.inventory", "an upgrade cannot take inventory slots away.")]
    [InlineData("""{ "inventory": { "slots": 4 }, "upgrades": [ { "set": { "inventory": { "slots": 0 } } } ] }""", "$.upgrades[0].set.inventory.slots", "slots must be 1 to 256 (got 0).")]
    [InlineData("""{ "inventory": { "slots": 1 }, "crafting": { }, "upgrades": [ { "set": { "crafting": { "speed": -1 } } } ] }""", "$.upgrades[0].set.crafting.speed", "speed must be greater than 0.")]
    public void BadStorageFieldsAreReported(string overrides, string path, string message)
    {
        var content = _mods.Mod("vanilla").Part("vanilla", "box.json", TestMods.Block("box", overrides)).Load();

        Assert.Contains(content.Diagnostics, d => d.JsonPath == path && d.Message == message);
    }

    [Fact]
    public void ItemsCannotHaveAnInventory()
    {
        var content = _mods.Mod("vanilla")
            .Part("vanilla", "bag.json", """{ "id": "bag", "name": "Bag", "category": "resource", "inventory": { "slots": 2 } }""").Load();

        Assert.Contains(content.Diagnostics, d => d.JsonPath == "$.inventory" && d.Message.StartsWith("only placeable parts"));
    }

    [Fact]
    public void RecipesLoadFromTheRecipesFolder()
    {
        var content = Content()
            .File("vanilla", "recipes/chest.json", """
                {
                  // Comments and trailing commas are fine.
                  "id": "chest",
                  "stations": ["bench"],
                  "inputs": [ { "item": "scrap_wood", "count": 4 }, { "item": "vanilla:scrap_metal", "count": 2 }, ],
                  "outputs": [ { "item": "chest", "count": 1 } ],
                  "time": 4,
                }
                """)
            .Load();

        var book = RecipeBook.Load(content);

        Assert.False(book.HasErrors, string.Join("\n", book.Diagnostics));
        var recipe = Assert.Single(book.All);
        Assert.Equal("vanilla:chest", recipe.QualifiedId);
        Assert.Equal("Test Block", recipe.Name);
        Assert.Equal(["vanilla:bench"], recipe.Stations);
        Assert.Equal([new ItemAmount("vanilla:scrap_wood", 4), new ItemAmount("vanilla:scrap_metal", 2)], recipe.Inputs);
        Assert.Same(recipe, book.Find("chest"));
        Assert.Same(recipe, Assert.Single(book.ForStation("bench")));
        Assert.Same(recipe, Assert.Single(book.Making("chest")));
        Assert.Empty(book.ForStation("chest"));
    }

    [Theory]
    [InlineData("""{ "id": "x", "stations": ["chest"], "inputs": [{ "item": "scrap_wood", "count": 1 }], "outputs": [{ "item": "chest", "count": 1 }], "time": 1 }""",
        "$.stations[0]", "'vanilla:chest' is not a crafting station (it has no \"crafting\").")]
    [InlineData("""{ "id": "x", "stations": ["bench"], "inputs": [{ "item": "gold", "count": 1 }], "outputs": [{ "item": "chest", "count": 1 }], "time": 1 }""",
        "$.inputs[0].item", "there is no item 'vanilla:gold'.")]
    [InlineData("""{ "id": "x", "stations": ["bench"], "inputs": [{ "item": "scrap_wood", "count": 0 }], "outputs": [{ "item": "chest", "count": 1 }], "time": 1 }""",
        "$.inputs[0].count", "count must be 1 or more (got 0).")]
    [InlineData("""{ "id": "x", "stations": ["bench"], "inputs": [{ "item": "scrap_wood", "count": 1 }, { "item": "scrap_wood", "count": 1 }], "outputs": [{ "item": "chest", "count": 1 }], "time": 1 }""",
        "$.inputs[1].item", "'vanilla:scrap_wood' is listed twice; add the counts together.")]
    [InlineData("""{ "id": "x", "stations": ["bench"], "inputs": [{ "item": "scrap_wood", "count": 1 }], "outputs": [], "time": 1 }""",
        "$.outputs", "outputs needs at least one item.")]
    [InlineData("""{ "id": "x", "stations": [], "inputs": [{ "item": "scrap_wood", "count": 1 }], "outputs": [{ "item": "chest", "count": 1 }], "time": 1 }""",
        "$.stations", "stations (part ids of crafting stations) needs at least one entry.")]
    [InlineData("""{ "id": "x", "stations": ["bench"], "inputs": [{ "item": "scrap_wood", "count": 1 }], "outputs": [{ "item": "chest", "count": 1 }] }""",
        "$.time", "time (seconds, 0 or more) is required.")]
    [InlineData("""{ "id": "X", "stations": ["bench"], "inputs": [{ "item": "scrap_wood", "count": 1 }], "outputs": [{ "item": "chest", "count": 1 }], "time": 1 }""",
        "$.id", "id 'X' must be lowercase snake_case (a letter, then letters, digits or '_').")]
    public void BadRecipesAreReportedAndLeftOut(string json, string path, string message)
    {
        var book = RecipeBook.Load(Content().File("vanilla", "recipes/x.json", json).Load());

        Assert.Empty(book.All);
        Assert.Contains(book.Diagnostics, d => d.JsonPath == path && d.Message == message && d.File == "vanilla/recipes/x.json");
    }

    [Fact]
    public void UnknownRecipeKeysAreWarnedAboutAndKept()
    {
        var json = """{ "id": "x", "stations": ["bench"], "inputs": [{ "item": "scrap_wood", "count": 1 }], "outputs": [{ "item": "chest", "count": 1 }], "time": 1, "tiem": 2 }""";

        var book = RecipeBook.Load(Content().File("vanilla", "recipes/x.json", json).Load());

        var warning = Assert.Single(book.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("Did you mean 'time'?", warning.Message);
        Assert.Contains("\"tiem\": 2", DataJson.Write(book.Find("x")!.Definition));
    }

    [Fact]
    public void ARecipeIdUsedTwiceInOneModIsAnError()
    {
        var json = """{ "id": "x", "stations": ["bench"], "inputs": [{ "item": "scrap_wood", "count": 1 }], "outputs": [{ "item": "chest", "count": 1 }], "time": 1 }""";

        var book = RecipeBook.Load(Content().File("vanilla", "recipes/a.json", json).File("vanilla", "recipes/b.json", json).Load());

        Assert.Single(book.All);
        Assert.Contains(book.Diagnostics, d => d.Code == DiagnosticCodes.DuplicateRecipe && d.File == "vanilla/recipes/b.json");
    }

    [Fact]
    public void TheFingerprintFollowsTheRecipes()
    {
        var json = """{ "id": "x", "stations": ["bench"], "inputs": [{ "item": "scrap_wood", "count": 1 }], "outputs": [{ "item": "chest", "count": 1 }], "time": 1 }""";
        var one = RecipeBook.Load(Content().File("vanilla", "recipes/x.json", json).Load()).Fingerprint;
        var two = RecipeBook.Load(Content().File("vanilla", "recipes/x.json", json.Replace("\"time\": 1", "\"time\": 2")).Load()).Fingerprint;

        Assert.NotEqual(one, two);
        Assert.NotEqual(RecipeBook.Empty.Fingerprint, one);
    }

    // ---- Saved Creations ----

    [Fact]
    public void InventoriesAndOrdersAreSavedWithTheCreation()
    {
        var catalog = Catalog();
        var creation = new Creation(4, catalog);
        Assert.True(creation.TryPlacePart(Bench, new GridCell(0, 0, 0), default, out _, out _));
        Assert.True(creation.TryPlacePart(Chest, new GridCell(1, 0, 0), default, out _, out _));
        creation.InventoryOf(1)!.SetSlot(1, new ItemStack(Block, 7));
        creation.CraftingOf(1)!.Enqueue(new CraftJob("test:chest", 2, 1.5f));
        creation.InventoryOf(2)!.Add(Scrap, 60);

        var json = CreationJson.Write(creation);
        var diagnostics = new List<DataDiagnostic>();
        var read = CreationJson.Read(json, catalog, "saved.json", diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(json, CreationJson.Write(read!));
        Assert.Contains("\"inventory\": [\n        {\n          \"slot\": 1,", json);
        Assert.Equal(new ItemStack(Block, 7), read!.InventoryOf(1)![1]);
        Assert.Equal(new CraftJob("test:chest", 2, 1.5f), read.CraftingOf(1)!.Jobs[0]);
        Assert.Equal(60, read.InventoryOf(2)!.Count(Scrap));
        Assert.DoesNotContain("inventory", CreationJson.Write(Single(catalog, Chest)));
    }

    [Theory]
    [InlineData("""[{ "slot": 9, "item": "test:scrap", "count": 1 }]""", "slot must be 0 to 3 (got 9).")]
    [InlineData("""[{ "slot": 0, "item": "test:scrap", "count": 51 }]""", "count must be 1 to 50 for 'test:scrap' (got 51).")]
    [InlineData("""[{ "slot": 0, "item": "test:gold", "count": 1 }]""", "unknown item 'test:gold'.")]
    [InlineData("""[{ "slot": 0, "item": "test:scrap", "count": 1 }, { "slot": 0, "item": "test:scrap", "count": 1 }]""", "slot 0 is listed twice.")]
    public void BadSavedInventoriesAreRejected(string slots, string message)
    {
        var catalog = Catalog();
        var json = $$"""{ "format_version": 1, "id": 1, "parts": [ { "id": 1, "part": "test:chest", "cell": [0, 0, 0], "inventory": {{slots}} } ] }""";
        var diagnostics = new List<DataDiagnostic>();

        Assert.Null(CreationJson.Read(json, catalog, "bad.json", diagnostics));
        Assert.Contains(diagnostics, d => d.Message == message);
    }

    [Fact]
    public void StorageFollowsItsPartThroughSplitsAndWelds()
    {
        var catalog = Catalog();
        var creation = new Creation(1, catalog);
        Assert.True(creation.TryPlacePart(Chest, new GridCell(0, 0, 0), default, out _, out _));
        Assert.True(creation.TryPlacePart(Block, new GridCell(1, 0, 0), default, out _, out _));
        Assert.True(creation.TryPlacePart(Chest, new GridCell(2, 0, 0), default, out _, out _));
        creation.InventoryOf(3)!.Add(Scrap, 5);

        var split = creation.RemovePart(2, () => 7).SplitOff;

        var piece = Assert.Single(split);
        Assert.Equal(5, piece.InventoryOf(3)!.Count(Scrap));
        Assert.Null(creation.InventoryOf(3));

        var target = Single(catalog, Block);
        Assert.True(target.TryMergeFrom(piece, default, new GridCell(-1, 0, 0), weld: true, out var map, out var error), error);
        Assert.Equal(5, target.InventoryOf(map[3])!.Count(Scrap));
        Assert.True(target.HoldsItems(map[3]));
    }

    private static Creation Single(IPartCatalog catalog, string part)
    {
        var creation = new Creation(9, catalog);
        Assert.True(creation.TryPlacePart(part, new GridCell(0, 0, 0), default, out _, out _));
        return creation;
    }

    [Fact]
    public void ThePortableFabricatorMakesOnlyItsShortList()
    {
        var content = ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"));
        var book = RecipeBook.Load(content);

        var made = book.All.Where(r => r.RunsAt("portable_fabricator")).Select(r => r.QualifiedId[8..]).Order(StringComparer.Ordinal);
        // Scrap Mechanic's Mini Craftbot list (the scrap tier) plus our gasoline, wedges and Connect Tool. Parts SM makes at
        // both stations have a scrap recipe here (`_scrap`) and a Metal Block one at the Fabricator.
        Assert.Equal(["beacon_scrap", "bearing_scrap", "connect_tool", "gasoline", "scrap_drivers_seat", "scrap_gas_engine",
            "scrap_metal_wedge", "scrap_seat", "scrap_wheel", "scrap_wood_wedge", "switch_scrap"], made);
        foreach (var scrap in new[] { "scrap_seat", "scrap_drivers_seat", "scrap_gas_engine", "scrap_wheel" })
            Assert.False(book.Find(scrap)!.RunsAt("fabricator"), $"{scrap} is made only at the Portable Fabricator");
        foreach (var both in new[] { "bearing", "switch", "beacon" })
        {
            var metal = book.Find(both)!;
            Assert.True(metal.RunsAt("fabricator") && !metal.RunsAt("portable_fabricator"), both);
            Assert.Equal(metal.Outputs, book.Find(both + "_scrap")!.Outputs);
        }
        var self = book.Find("portable_fabricator")!;
        Assert.True(self.RunsAt("fabricator") && !self.RunsAt("portable_fabricator"));
    }

    [Fact]
    public void VanillaRecipesLoadAndCraftAChest()
    {
        var content = ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"));
        var book = RecipeBook.Load(content);

        Assert.Empty(book.Diagnostics);
        var chest = book.Find("chest")!;
        Assert.True(chest.RunsAt("fabricator") && !chest.RunsAt("portable_fabricator"));
        var gasoline = book.Find("gasoline")!;
        Assert.True(gasoline.RunsAt("refiner") && gasoline.RunsAt("fabricator") && gasoline.RunsAt("portable_fabricator"));

        var player = new Inventory(content, 30);
        player.Add("vanilla:scrap_wood_block", 60);
        player.Add("vanilla:scrap_metal_block", 10);
        Assert.True(player.HasAll(chest.InputsFor(1)));
        var station = Inventory.For(content, content.FindDefinition("vanilla:fabricator")!)!;
        var queue = new CraftingQueue();
        queue.Enqueue(new CraftJob(chest.QualifiedId, 1));
        queue.Tick(chest.Time, 1, book, station);
        Assert.Equal(1, station.Count("vanilla:chest"));
    }

    private static string ChestRecipe(int wood, string station = "bench") =>
        $$"""{ "id": "chest", "stations": ["{{station}}"], "inputs": [ { "item": "scrap_wood", "count": {{wood}} } ], "outputs": [ { "item": "chest", "count": 1 } ], "time": 4 }""";

    private TestMods WithChestRecipe() => Content().File("vanilla", "recipes/chest.json", ChestRecipe(4));

    [Fact]
    public void AModOverridesAnotherModsRecipeKeepingItsId()
    {
        var before = RecipeBook.Load(WithChestRecipe().Load()).Fingerprint;
        var content = _mods
            .Mod("cheap_chests", dependencies: """[{ "id": "vanilla" }]""")
            .File("cheap_chests", "overrides/vanilla/recipes/chest.json", ChestRecipe(1))
            .Load();

        var book = RecipeBook.Load(content);

        Assert.False(content.HasErrors, string.Join("\n", content.Diagnostics));
        Assert.Empty(book.Diagnostics);
        var chest = Assert.Single(book.All);
        Assert.Equal("vanilla:chest", chest.QualifiedId);
        Assert.Equal(1, chest.Inputs.Single().Count);
        // A recipe override is not a part override.
        Assert.Empty(content.Mods.Single(m => m.Id == "cheap_chests").Overrides);
        Assert.NotEqual(before, book.Fingerprint);
    }

    [Fact]
    public void ARecipeOverrideNeedsTheTargetAsADependency()
    {
        var content = WithChestRecipe()
            .Mod("sneaky")
            .File("sneaky", "overrides/vanilla/recipes/chest.json", ChestRecipe(1))
            .Load();

        var book = RecipeBook.Load(content);

        Assert.Contains(content.Diagnostics, d => d.Code == DiagnosticCodes.OverrideUndeclaredMod);
        Assert.Equal(4, book.Find("chest")!.Inputs.Single().Count);
    }

    [Fact]
    public void OverridingAnUnknownRecipeIsAnErrorAndTheRestLoad()
    {
        var content = WithChestRecipe()
            .Mod("typo", dependencies: """[{ "id": "vanilla" }]""")
            .File("typo", "overrides/vanilla/recipes/chets.json", ChestRecipe(1).Replace("\"id\": \"chest\"", "\"id\": \"chets\""))
            .Load();

        var book = RecipeBook.Load(content);

        var error = Assert.Single(book.Diagnostics);
        Assert.Equal(DiagnosticCodes.RecipeOverrideUnknown, error.Code);
        Assert.Equal("typo/overrides/vanilla/recipes/chets.json", error.File);
        Assert.Equal("mod 'vanilla' has no recipe 'chets' to override.", error.Message);
        Assert.Equal(4, book.Find("chest")!.Inputs.Single().Count);
    }

    [Fact]
    public void ARecipeOverrideIsCheckedLikeARecipe()
    {
        var content = WithChestRecipe()
            .Mod("bad", dependencies: """[{ "id": "vanilla" }]""")
            .File("bad", "overrides/vanilla/recipes/chest.json", ChestRecipe(1, station: "chest"))
            .Load();

        var book = RecipeBook.Load(content);

        Assert.Contains(book.Diagnostics, d => d.Code == DiagnosticCodes.RecipeInvalid && d.JsonPath == "$.stations[0]");
        Assert.Equal(4, book.Find("chest")!.Inputs.Single().Count);
    }

    [Fact]
    public void TwoUnrelatedRecipeOverridesWarnAndTheLaterOneWins()
    {
        var mods = WithChestRecipe()
            .Mod("aaa", dependencies: """[{ "id": "vanilla" }]""")
            .File("aaa", "overrides/vanilla/recipes/chest.json", ChestRecipe(1))
            .Mod("bbb", dependencies: """[{ "id": "vanilla" }]""")
            .File("bbb", "overrides/vanilla/recipes/chest.json", ChestRecipe(2));

        var byId = RecipeBook.Load(mods.Load());
        var swapped = RecipeBook.Load(mods.Load(TestMods.Profile("vanilla", "bbb", "aaa")));

        var warning = Assert.Single(byId.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(DiagnosticCodes.RecipeOverrideConflict, warning.Code);
        Assert.False(byId.HasErrors);
        Assert.Equal(2, byId.Find("chest")!.Inputs.Single().Count);
        Assert.Equal(1, swapped.Find("chest")!.Inputs.Single().Count);
    }

    [Fact]
    public void ARecipeOverrideBuildingOnADependencysOverrideDoesNotWarn()
    {
        var content = WithChestRecipe()
            .Mod("zzz", dependencies: """[{ "id": "vanilla" }]""")
            .File("zzz", "overrides/vanilla/recipes/chest.json", ChestRecipe(1))
            .Mod("patch", dependencies: """[{ "id": "vanilla" }, { "id": "zzz" }]""")
            .File("patch", "overrides/vanilla/recipes/chest.json", ChestRecipe(3))
            .Load(TestMods.Profile("patch"));

        var book = RecipeBook.Load(content);

        Assert.Empty(book.Diagnostics);
        Assert.Equal(3, book.Find("chest")!.Inputs.Single().Count);
    }
}
