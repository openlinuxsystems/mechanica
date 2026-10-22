using System.Text.Json.Nodes;
using Mechanica.Core.Bots;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Quests;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.Data;

// A mod changing vanilla's content through overrides/vanilla/: whole replacements, merge patches and remove.json,
// for every content kind.
public class ContentLayersTests : IDisposable
{
    private static readonly string Vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");
    private readonly TestMods _mods = new TestMods().Mod("tweaks", dependencies: """[{ "id": "vanilla" }]""");

    public void Dispose() => _mods.Dispose();

    private ModLoadResult Load(params string[] extra) =>
        ModLoader.Load([Vanilla, Path.Combine(_mods.Root, "tweaks"), .. extra.Select(e => Path.Combine(_mods.Root, e))]);

    private static void NoErrors(IEnumerable<DataDiagnostic> diagnostics) =>
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

    [Fact]
    public void APartPatchChangesOnlyWhatItNamesAndKeepsAssets()
    {
        var original = Load().Parts["vanilla:wheel"].Definition;
        _mods.File("tweaks", "overrides/vanilla/wheel.patch.json", """{ "name": "Grippy Wheel", "mass": 99 }""");

        var content = Load();

        NoErrors(content.Diagnostics);
        var wheel = content.Parts["vanilla:wheel"].Definition;
        Assert.Equal(("Grippy Wheel", 99f), (wheel.Name, wheel.Mass));
        Assert.Equal(original.Size, wheel.Size);
        Assert.Equal($"vanilla:{original.Mesh}", wheel.Mesh);
    }

    [Fact]
    public void RemovingAPartTakesItsRecipesWithIt()
    {
        _mods.File("tweaks", "overrides/vanilla/remove.json", """{ "parts": ["antenna"], "recipes": ["barrel"] }""");

        var content = Load();
        var recipes = RecipeBook.Load(content);

        NoErrors(content.Diagnostics);
        NoErrors(recipes.Diagnostics);
        Assert.False(content.Parts.ContainsKey("vanilla:antenna"));
        Assert.Contains("vanilla:antenna", content.RemovedParts);
        Assert.Empty(recipes.Making("vanilla:antenna"));
        Assert.Null(recipes.Find("vanilla:barrel"));
        Assert.NotEqual(ModLoader.Load([Vanilla]).Fingerprint, content.Fingerprint);
    }

    [Fact]
    public void ARecipePatchMergesIntoTheRecipe()
    {
        var before = RecipeBook.Load(Load()).Find("vanilla:barrel")!;
        _mods.File("tweaks", "overrides/vanilla/recipes/barrel.patch.json", """{ "time": 42 }""");

        var recipes = RecipeBook.Load(Load());

        NoErrors(recipes.Diagnostics);
        var barrel = recipes.Find("vanilla:barrel")!;
        Assert.Equal(42, barrel.Time);
        Assert.Equal(before.Inputs, barrel.Inputs);
    }

    [Fact]
    public void BotsCanBeReplacedPatchedAndRemoved()
    {
        var grazer = JsonNode.Parse(File.ReadAllText(Path.Combine(Vanilla, "bots", "grazer.json")))!.AsObject();
        grazer["name"] = "Big Grazer";
        _mods.File("tweaks", "overrides/vanilla/bots/grazer.json", grazer.ToJsonString())
            .File("tweaks", "overrides/vanilla/bots/scuttler_red.patch.json", """{ "health": 999 }""")
            .File("tweaks", "overrides/vanilla/remove.json", """{ "bots": ["scuttler_blue"] }""");

        var book = BotBook.Load(Load());

        NoErrors(book.Diagnostics);
        Assert.Equal("Big Grazer", book.FindBot("grazer")!.Name);
        Assert.Equal(999, book.FindBot("scuttler_red")!.Health);
        Assert.Null(book.FindBot("scuttler_blue"));
    }

    [Fact]
    public void QuestsLoadFromSubfoldersAndCanBePatched()
    {
        var side = JsonNode.Parse(File.ReadAllText(Path.Combine(Vanilla, "quests", "first_farm.json")))!.AsObject();
        side.Remove("rewards");
        side.Remove("requires");
        _mods.File("tweaks", "quests/side/fishing.json", side.ToJsonString())
            .File("tweaks", "overrides/vanilla/quests/first_farm.patch.json", """{ "name": "Grow Something" }""");
        var content = Load();

        var quests = QuestBook.Load(content, RecipeBook.Load(content), WorldGenData.Load(content));

        NoErrors(quests.Diagnostics);
        Assert.Equal("Grow Something", quests.Find("first_farm")!.Name);
        Assert.NotNull(quests.Find("tweaks:fishing"));
    }

    [Fact]
    public void CharactersPoisAndDerelictsCanBePatched()
    {
        _mods.File("tweaks", "overrides/vanilla/characters/scrapper_male.patch.json", """{ "name": "Mechanic" }""")
            .File("tweaks", "overrides/vanilla/pois/camp.patch.json", """{ "name": "Big Camp" }""")
            .File("tweaks", "overrides/vanilla/derelicts/go_kart.patch.json", """{ "missing": [] }""");
        var content = Load();

        var characters = CharacterBook.Load(content);
        var world = WorldGenData.Load(content);

        NoErrors(characters.Diagnostics);
        NoErrors(world.Diagnostics);
        Assert.Equal("Mechanic", characters.Find("scrapper_male")!.Name);
        Assert.Equal("vanilla:scrapper_male", characters.Default!.QualifiedId);
        Assert.Equal("Big Camp", world.Pois.Single(p => p.QualifiedId == "vanilla:camp").Prefab.Name);
        Assert.Empty(world.Derelicts["vanilla:go_kart"].Definition.Missing!);
    }

    [Fact]
    public void ChangingSomethingThatIsNotThereIsAnError()
    {
        _mods.File("tweaks", "overrides/vanilla/bots/nobody.patch.json", """{ "health": 1 }""")
            .File("tweaks", "overrides/vanilla/remove.json", """{ "bots": ["ghost"], "spaceships": ["x"] }""");

        var book = BotBook.Load(Load());

        Assert.Contains(book.Diagnostics, d => d.Code == DiagnosticCodes.ContentOverrideUnknown && d.Severity == DiagnosticSeverity.Error && d.Message.Contains("nobody"));
        Assert.Contains(book.Diagnostics, d => d.Code == DiagnosticCodes.ContentOverrideUnknown && d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("ghost"));
        Assert.Contains(Load().Diagnostics, d => d.Code == DiagnosticCodes.UnknownKey && d.Message.Contains("spaceships"));
    }

    [Fact]
    public void TwoUnrelatedModsChangingOneEntryWarnAndTheLaterWins()
    {
        _mods.File("tweaks", "overrides/vanilla/bots/grazer.patch.json", """{ "health": 10 }""")
            .Mod("zmore", dependencies: """[{ "id": "vanilla" }]""")
            .File("zmore", "overrides/vanilla/bots/grazer.patch.json", """{ "health": 20 }""");

        var book = BotBook.Load(Load("zmore"));

        Assert.Equal(20, book.FindBot("grazer")!.Health);
        Assert.Contains(book.Diagnostics, d => d.Code == DiagnosticCodes.ContentOverrideConflict);
    }

    [Fact]
    public void MergePatchFollowsRfc7386()
    {
        var target = JsonNode.Parse("""{ "a": 1, "b": { "c": 2, "d": 3 }, "e": [1, 2] }""");
        var result = ContentLayers.MergePatch(target, JsonNode.Parse("""{ "a": null, "b": { "c": 5 }, "e": [9], "f": "new" }"""));
        Assert.Equal("""{"b":{"c":5,"d":3},"e":[9],"f":"new"}""", result!.ToJsonString());
    }

    [Fact]
    public void WorldgenPatchesFromSeveralModsStack()
    {
        var rock = JsonNode.Parse(File.ReadAllText(Path.Combine(Vanilla, "world", "worldgen.json")),
            documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!["objects"]!
            .AsArray().Single(o => (string?)o!["id"] == "small_rock")!.AsObject();
        rock["id"] = "blue_rock";
        _mods.File("tweaks", "world/worldgen.patch.json", new JsonObject
        {
            ["objects"] = new JsonArray(rock.DeepClone()),
            ["biomes"] = JsonNode.Parse("""[{ "id": "meadow", "scatter": [{ "object": "blue_rock", "spacing": 20, "chance": 0.5 }, { "object": "bush", "$remove": true }] }]"""),
        }.ToJsonString())
            .Mod("zmore", dependencies: """[{ "id": "vanilla" }]""")
            .File("zmore", "world/worldgen.patch.json", """{ "biomes": [{ "id": "meadow", "name": "Blue Meadow" }] }""");

        var world = WorldGenData.Load(Load("zmore"));

        NoErrors(world.Diagnostics);
        Assert.True(world.Objects.ContainsKey("blue_rock"));
        var meadow = world.Definition.Biomes!.Single(b => b.Id == "meadow");
        Assert.Equal("Blue Meadow", meadow.Name);
        Assert.Contains(meadow.Scatter!, r => r.Object == "blue_rock");
        Assert.DoesNotContain(meadow.Scatter!, r => r.Object == "bush");
        Assert.Contains(meadow.Scatter!, r => r.Object == "birch_tree");
        Assert.Equal(9, world.Definition.Biomes!.Count);
    }

    [Fact]
    public void ABrokenWorldgenPatchIsLeftOut()
    {
        _mods.File("tweaks", "world/worldgen.patch.json", """{ "biomes": [{ "id": "meadow", "scatter": [{ "object": "no_such_thing", "spacing": 20, "chance": 0.5 }] }] }""");

        var world = WorldGenData.Load(Load());

        Assert.Contains(world.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.File == "tweaks/world/worldgen.patch.json");
        Assert.Equal(9, world.Definition.Biomes!.Count); // vanilla's world, unpatched
    }

    [Fact]
    public void RaidPatchesAddBotsToALevel()
    {
        _mods.File("tweaks", "bots/rust_bug.json", File.ReadAllText(Path.Combine(Vanilla, "bots", "scuttler_green.json"))
            .Replace("\"scuttler_green\"", "\"rust_bug\"").Replace("\"meshes/", "\"vanilla:meshes/"))
            .File("tweaks", "raids/raids.patch.json", """{ "levels": [{ "level": 1, "budget": 6, "bots": [{ "bot": "rust_bug", "count": 2 }] }] }""");
        var content = Load();

        var raids = RaidTable.Load(content, BotBook.Load(content));

        NoErrors(raids.Diagnostics);
        var level = raids.Levels.Single(l => l.Level == 1);
        Assert.Equal(6, level.Budget);
        Assert.Contains(level.Bots, b => b.Bot.QualifiedId == "tweaks:rust_bug");
        Assert.Contains(level.Bots, b => b.Bot.QualifiedId == "vanilla:scuttler_green");
    }

    [Fact]
    public void KeyedPatchesMergeListsById()
    {
        var target = JsonNode.Parse("""{ "list": [{ "id": "a", "n": 1 }, { "id": "b", "n": 2 }], "plain": [1, 2] }""");
        var result = ContentLayers.KeyedMergePatch(target, JsonNode.Parse("""{ "list": [{ "id": "b", "n": 5 }, { "id": "a", "$remove": true }, { "id": "c" }], "plain": [3] }"""));
        Assert.Equal("""{"list":[{"id":"b","n":5},{"id":"c"}],"plain":[3]}""", result!.ToJsonString());
    }
}
