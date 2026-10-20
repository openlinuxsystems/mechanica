using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Items;
using Mechanica.Core.Quests;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.Quests;

public class QuestTests
{
    private const string Batch = "editor/batches/story.json";
    private static readonly string ModsRoot = Path.Combine(TestMods.RepoRoot, "mods");
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(ModsRoot));
    private static readonly Lazy<WorldGenData> World = new(() => WorldGenData.Load(Vanilla.Value));
    private static readonly Lazy<RecipeBook> Recipes = new(() => RecipeBook.Load(Vanilla.Value));
    private static readonly Lazy<QuestBook> Book = new(() => QuestBook.Load(Vanilla.Value, Recipes.Value, World.Value));

    private readonly FakeWorld _world = new();

    [Fact]
    public void VanillaQuestsLoadCleanlyAsOneChain()
    {
        var book = Book.Value;

        Assert.Empty(book.Diagnostics);
        Assert.Equal(["vanilla:data_core_location", "vanilla:faction_contact", "vanilla:final_decision", "vanilla:first_farm",
            "vanilla:first_vehicle", "vanilla:getting_started", "vanilla:the_farmer", "vanilla:workshop_outpost"], book.All.Select(q => q.QualifiedId));
        Assert.Equal(["vanilla:getting_started"], book.All.Where(q => q.Requires.Count == 0).Select(q => q.QualifiedId));
        Assert.Equal(64, book.Fingerprint.Length);
    }

    [Fact]
    public void TheStoryBatchReproducesTheCheckedInFiles()
    {
        var vanilla = Path.Combine(ModsRoot, "vanilla");
        var root = Path.Combine(Path.GetTempPath(), "mechanica-tests", Guid.NewGuid().ToString("N"), "mods");
        var scratch = Path.Combine(root, "vanilla");
        var diagnostics = new List<DataDiagnostic>();
        var batch = StoryBatch.Read(File.ReadAllText(Path.Combine(vanilla, Batch)), Batch, diagnostics)!;
        var written = batch.Pois!.Select(p => p.File!).Concat(batch.Quests!.Select(q => q.File!)).ToList();
        try
        {
            foreach (var file in Directory.GetFiles(vanilla, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(vanilla, file).Replace('\\', '/');
                if (written.Contains(relative))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(scratch, relative))!);
                File.Copy(file, Path.Combine(scratch, relative));
            }
            var results = StoryBatch.Run(batch, scratch, Batch, diagnostics);

            Assert.Empty(diagnostics);
            Assert.Equal(written.Count, results.Count(r => r.Succeeded));
            Assert.Equal(Directory.GetFiles(Path.Combine(vanilla, QuestBook.Folder)).Length, batch.Quests!.Count);
            foreach (var file in written)
                Assert.True(File.ReadAllText(Path.Combine(vanilla, file)) == File.ReadAllText(Path.Combine(scratch, file)),
                    $"{file} differs from the batch's output; edit {Batch} and re-run tools/author-vanilla.sh instead of the file.");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
        }
    }

    [Fact]
    public void TheAiCoreFacilityStandsOnLand()
    {
        var generator = new WorldGenerator(World.Value, 1234, 4096f);
        var core = Assert.Single(generator.Pois, p => p.Poi.QualifiedId == "vanilla:ai_core_facility");

        Assert.True(core.PadHeight > generator.SeaLevel, $"pad {core.PadHeight} is below the sea ({generator.SeaLevel})");
        Assert.Contains(generator.Pois, p => p.Poi.QualifiedId == "vanilla:faction_outpost");
    }

    [Fact]
    public void TheStoryPlaysThroughFromCrashSiteToEnding()
    {
        var tracker = Tracker(out var dispatcher, out var said);
        _world.Site("vanilla:crash_site", Vector3.Zero, 80, ("fire", null, new(3, 0, 13)), ("fire", null, new(-6, 0, 29)),
            ("fire", null, new(7, 0, 27)), ("item", "master_battery", new(-2, 0.5f, 12)), ("item", "logbook", new(2, 0.5f, 12)),
            ("npc", "farmer", new(-45, 0, -40)));
        _world.Site("vanilla:outpost", new(600, 0, 0), 40);
        _world.Site("vanilla:faction_outpost", new(0, 0, 900), 36, ("npc", "faction_envoy", new(0, 0, 897)));
        _world.Site("vanilla:ai_core_facility", new(-900, 0, 1100), 70, ("core", null, new(-900, 0, 1098)));

        tracker.Tick(0.1f);
        Assert.Equal(QuestStatus.Active, tracker.State.Of("vanilla:getting_started")!.Status);
        Assert.Contains(said, d => d.Lines[0].Speaker == "Logbook");
        Assert.Null(tracker.State.Of("vanilla:workshop_outpost"));

        // The fires count as they go out (water puts them out; FireActions tells the world how many are out).
        _world.Feet[2] = new Vector3(3, 0, 10);
        Assert.Equal(ActionErrors.Rejected, Interact(dispatcher, 2).Code); // E does nothing to a fire
        _world.Out["vanilla:crash_site"] = 1;
        tracker.Tick(0.1f);
        Assert.Equal(1, tracker.State.Of("vanilla:getting_started")!.Count);
        Assert.Equal(0, tracker.State.Of("vanilla:getting_started")!.Step);
        _world.Out["vanilla:crash_site"] = 3;
        tracker.Tick(0.1f);
        Assert.Equal(1, tracker.State.Of("vanilla:getting_started")!.Step);
        _world.Feet[2] = new Vector3(100, 0, 100);
        Assert.Equal(ActionErrors.Rejected, Interact(dispatcher, 2).Code); // out of reach does nothing
        _world.Feet[2] = new Vector3(1, 0, 12); // nearer the logbook, but only the battery counts
        Assert.True(Interact(dispatcher, 2).Ok);
        Assert.True(tracker.State.IsDone("vanilla:getting_started"));
        Assert.Equal(2, _world.Given[(2, "vanilla:component_kit")]);
        Assert.Equal(2, _world.Given[(3, "vanilla:component_kit")]);
        Assert.Contains(said, d => d.Lines.Any(l => l.Text!.StartsWith("Power restored", StringComparison.Ordinal)));

        // The Farmer: talking to him at his stall gives food, seed and scrap and reveals the Workshops.
        Assert.Equal(QuestStatus.Active, tracker.State.Of("vanilla:the_farmer")!.Status);
        Assert.DoesNotContain("vanilla:outpost", tracker.State.Revealed);
        _world.Feet[2] = new Vector3(-45, 0, -42);
        said.Clear();
        Assert.True(Interact(dispatcher, 2).Ok);
        Assert.True(tracker.State.IsDone("vanilla:the_farmer"));
        Assert.Equal(3, _world.Given[(3, "vanilla:carrot")]);
        Assert.Equal(10, _world.Given[(2, "vanilla:scrap_metal")]);
        Assert.Contains("vanilla:outpost", tracker.State.Revealed);
        Assert.Contains(said, d => d.Lines.Any(l => l.Speaker == "Farmer" && l.Text!.Contains("Workshops", StringComparison.Ordinal)));

        // Workshop: reach it, then order a craft.
        Assert.NotNull(tracker.State.Of("vanilla:workshop_outpost"));
        _world.Feet[3] = new Vector3(590, 0, 5);
        tracker.Tick(0.1f);
        Assert.Equal(1, tracker.State.Of("vanilla:workshop_outpost")!.Step);
        Assert.False(dispatcher.Dispatch(new ActionCaller(2, ActionSource.Player), InventoryActions.Craft, "{\"creation\":1,\"part\":1,\"recipe\":\"x\"}").Ok);
        Assert.Equal(1, tracker.State.Of("vanilla:workshop_outpost")!.Step); // failed calls don't count
        dispatcher.Dispatch(new ActionCaller(2, ActionSource.Player), InventoryActions.Craft, "{\"creation\":1,\"part\":1,\"recipe\":\"vanilla:chest\"}");
        Assert.True(tracker.State.IsDone("vanilla:workshop_outpost"));
        Assert.Equal("yes", tracker.State.Flags["fabricator_unlocked"]);

        // First Vehicle: distance only counts while seated and moving.
        tracker.Tick(1f);
        _world.Speed[2] = 10f;
        for (var i = 0; i < 9; i++)
            tracker.Tick(1f);
        Assert.Equal(90f, tracker.State.Of("vanilla:first_vehicle")!.Distance, 0.01f);
        tracker.Tick(1f);
        Assert.True(tracker.State.IsDone("vanilla:first_vehicle"));
        _world.Speed.Clear();

        // First Farm: four crops, then a raid ends.
        _world.Crops = 3;
        tracker.Tick(0.1f);
        Assert.Equal(3, tracker.State.Of("vanilla:first_farm")!.Count);
        _world.Crops = 4;
        tracker.Tick(0.1f);
        tracker.RaidEnded();
        Assert.True(tracker.State.IsDone("vanilla:first_farm"));
        Assert.Equal(1, _world.Given[(2, "vanilla:popper")]);

        // Faction Contact: reach, talk, choose.
        _world.Feet[2] = new Vector3(0, 0, 899);
        tracker.Tick(0.1f);
        said.Clear();
        Assert.True(Interact(dispatcher, 2).Ok);
        Assert.Equal(2, tracker.State.Of("vanilla:faction_contact")!.Step);
        Assert.True(Interact(dispatcher, 2).Ok, "talking to the envoy again says the step's lines");
        Assert.Contains(said, d => d.Lines[0].Speaker == "Faction Envoy");
        Assert.False(dispatcher.Dispatch(new ActionCaller(2, ActionSource.Player), QuestTracker.ChooseQuestOption, "{\"quest\":\"faction_contact\",\"choice\":\"maybe\"}").Ok);
        Assert.True(dispatcher.Dispatch(new ActionCaller(2, ActionSource.Player), QuestTracker.ChooseQuestOption, "{\"quest\":\"faction_contact\",\"choice\":\"ally\"}").Ok);
        Assert.Equal("ally", tracker.State.Flags["faction_stance"]);
        Assert.Equal("ally", tracker.State.Of("vanilla:faction_contact")!.Choices!["decide"]);

        // Data Core Location reveals the facility; Final Decision ends the story.
        _world.Items[(3, "vanilla:component_kit")] = 10;
        tracker.Tick(0.1f);
        Assert.Contains("vanilla:ai_core_facility", tracker.State.Revealed);
        _world.Feet[2] = new Vector3(-900, 0, 1099);
        tracker.Tick(0.1f);
        Assert.True(Interact(dispatcher, 2).Ok);
        Assert.True(tracker.Choose("final_decision", "evolve"));
        Assert.Equal("evolved", tracker.State.Flags["ending"]);
        Assert.All(Book.Value.All, q => Assert.True(tracker.State.IsDone(q.QualifiedId), q.QualifiedId));
    }

    [Fact]
    public void StateRoundTripsAndReloadsIntoATracker()
    {
        var tracker = Tracker(out _, out _);
        tracker.Tick(0.1f);
        tracker.State.Flags["x"] = "y";
        tracker.State.Revealed.Add("vanilla:outpost");

        var json = tracker.State.ToJson();
        var read = QuestState.FromJson(json)!;

        Assert.Equal(json, read.ToJson());
        Assert.Equal(QuestStatus.Active, read.Of("vanilla:getting_started")!.Status);
        Assert.Null(QuestState.FromJson("{"));
        Assert.Empty(QuestState.FromJson("{}")!.Quests);
    }

    [Theory]
    [InlineData("""{ "name": "Q", "steps": [] }""", "at least one step")]
    [InlineData("""{ "name": "Q", "steps": [{ "id": "a", "text": "t", "objective": { "type": "fly" } }] }""", "must be one of")]
    [InlineData("""{ "name": "Q", "steps": [{ "id": "a", "text": "t", "objective": { "type": "reach", "poi": "moon" } }] }""", "not in the world data")]
    [InlineData("""{ "name": "Q", "steps": [{ "id": "a", "text": "t", "objective": { "type": "interact", "poi": "crash_site", "marker": "fire", "count": 9 } }] }""", "fewer than count")]
    [InlineData("""{ "name": "Q", "steps": [{ "id": "a", "text": "t", "objective": { "type": "extinguish", "poi": "crash_site", "count": 4 } }] }""", "fewer than count")]
    [InlineData("""{ "name": "Q", "steps": [{ "id": "a", "text": "t", "objective": { "type": "extinguish", "poi": "moon" } }] }""", "not in the world data")]
    [InlineData("""{ "name": "Q", "steps": [{ "id": "a", "text": "t", "objective": { "type": "choose" }, "choices": [{ "id": "x", "text": "x" }] }] }""", "at least two choices")]
    [InlineData("""{ "name": "Q", "steps": [{ "id": "a", "text": "t", "objective": { "type": "raid" }, "choices": [{ "id": "x", "text": "x" }] }] }""", "only a choose step")]
    [InlineData("""{ "name": "Q", "steps": [{ "id": "a", "text": "t", "objective": { "type": "have_item", "item": "nothing" } }] }""", "not in the loaded mods")]
    [InlineData("""{ "name": "Q", "steps": [{ "id": "a", "text": "t", "objective": { "type": "drive" } }] }""", "needs a distance")]
    [InlineData("""{ "name": "Q", "steps": [{ "id": "a", "text": "t", "objective": { "type": "raid" } }], "rewards": { "reveal": ["moon"] } }""", "not in the world data")]
    public void BadQuestsGiveClearErrors(string json, string message)
    {
        var quest = DataJson.Read<QuestDefinition>(json)!;

        var diagnostics = QuestBook.Validate(quest, "q", "vanilla", Vanilla.Value, Recipes.Value, World.Value, "q.json");

        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains(message));
    }

    [Fact]
    public void MissingAndLoopingRequirementsAreLeftOut()
    {
        using var mods = new TestMods();
        mods.Mod("story");
        var dir = Path.Combine(mods.Root, "story", QuestBook.Folder);
        Directory.CreateDirectory(dir);
        const string step = """[{ "id": "a", "text": "t", "objective": { "type": "raid" } }]""";
        File.WriteAllText(Path.Combine(dir, "a.json"), $$"""{ "name": "A", "requires": ["b"], "steps": {{step}} }""");
        File.WriteAllText(Path.Combine(dir, "b.json"), $$"""{ "name": "B", "requires": ["a"], "steps": {{step}} }""");
        File.WriteAllText(Path.Combine(dir, "c.json"), $$"""{ "name": "C", "requires": ["gone"], "steps": {{step}} }""");
        File.WriteAllText(Path.Combine(dir, "d.json"), $$"""{ "name": "D", "steps": {{step}} }""");

        var content = ModLoader.LoadAll(mods.Root);
        var book = QuestBook.Load(content, RecipeBook.Load(content), WorldGenData.Flat);

        Assert.Equal(["story:d"], book.All.Select(q => q.QualifiedId));
        Assert.Contains(book.Diagnostics, d => d.Message.Contains("requires itself"));
        Assert.Contains(book.Diagnostics, d => d.Message.Contains("'story:gone' does not exist"));
    }

    private QuestTracker Tracker(out ActionDispatcher dispatcher, out List<QuestDialogue> said)
    {
        var tracker = new QuestTracker(Book.Value, _world);
        dispatcher = new ActionDispatcher();
        tracker.Register(dispatcher);
        // A stand-in Craft that succeeds for known recipes, so the tracker sees real dispatches.
        dispatcher.Register<CraftArgs>(InventoryActions.Craft, (_, a) => Recipes.Value.Find(a.Recipe) is null ? "no recipe" : null, (_, _) => null);
        var lines = new List<QuestDialogue>();
        tracker.Said += lines.Add;
        said = lines;
        _world.Feet[2] = new Vector3(0, 0, 4);
        _world.Feet[3] = new Vector3(1, 0, 4);
        return tracker;
    }

    private static ActionResult Interact(ActionDispatcher dispatcher, int peer) =>
        dispatcher.Dispatch(new ActionCaller(peer, ActionSource.Player), QuestTracker.QuestInteract, "{}");

    private sealed class FakeWorld : IQuestWorld
    {
        private readonly List<QuestSite> _sites = [];

        public Dictionary<int, Vector3> Feet { get; } = [];
        public Dictionary<(int, string), int> Items { get; } = [];
        public Dictionary<(int, string), int> Given { get; } = [];
        public Dictionary<int, float> Speed { get; } = [];
        public int Crops { get; set; }
        public Dictionary<string, int> Out { get; } = [];

        public IEnumerable<int> Peers => Feet.Keys.Order();

        public QuestSite Site(string poi, Vector3 centre, float radius, params (string Type, string? Ref, Vector3 At)[] markers)
        {
            var site = new QuestSite(_sites.Count(s => s.Poi == poi), poi, centre, radius,
                [.. markers.Select((m, i) => new QuestMarker(i, m.Type, m.Ref, m.At))]);
            _sites.Add(site);
            return site;
        }

        public Vector3? FeetOf(int peer) => Feet.TryGetValue(peer, out var f) ? f : null;

        public IReadOnlyList<QuestSite> SitesOf(string poi) => [.. _sites.Where(s => s.Poi == poi)];

        public int CountItem(int peer, string item) => Items.GetValueOrDefault((peer, item));

        public int GiveItem(int peer, string item, int count)
        {
            Given[(peer, item)] = Given.GetValueOrDefault((peer, item)) + count;
            return count;
        }

        public int PlantedCrops() => Crops;

        public float? SeatedSpeed(int peer) => Speed.TryGetValue(peer, out var s) ? s : null;

        public int FiresOut(string poi, string marker) => marker == "fire" ? Out.GetValueOrDefault(poi) : 0;
    }
}
