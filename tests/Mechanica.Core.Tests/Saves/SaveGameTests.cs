using System.Numerics;
using System.Text.Json.Nodes;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Net;
using Mechanica.Core.Quests;
using Mechanica.Core.Saves;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using static Mechanica.Core.Tests.Items.ItemParts;

namespace Mechanica.Core.Tests.Saves;

public class SaveGameTests
{
    private readonly PartCatalog _catalog = Catalog();
    private static readonly string Ada = PlayerProfile.Fixed("Ada").Id;

    private SaveGame Sample()
    {
        var chest = new Creation(3, _catalog);
        Assert.True(chest.TryPlacePart(Chest, GridCell.Zero, GridRotation.Identity, out _, out var error), error);
        chest.InventoryOf(1)!.Add(Scrap, 4);
        chest.SetAllBodyPoses(new Pose(new Vector3(1, 2, 3), Quaternion.Identity));
        return new SaveGame
        {
            Name = "My World",
            SavedAt = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero),
            Mode = GameMode.Creative,
            Seed = -42,
            Time = 3600.25,
            Mods = [new ModStamp("test", "1.0.0")],
            Fingerprint = "abc",
            Destroyed = [new WorldObjectId(1, 2, 3), WorldObjectId.ForPoi(7, 2)],
            Regrow = new Dictionary<WorldObjectId, double> { [WorldObjectId.ForPoi(7, 2)] = 7200.5 },
            Creations = [CreationJson.Write(chest)],
            Scripts = [new SavedScriptState(3, 1, new JsonObject { ["count"] = 2 })],
            ModScripts = new Dictionary<string, JsonObject> { ["test:scripts/rules.lua"] = new() { ["visits"] = 3 } },
            Players = [new SavedPlayer(Ada, "Ada", [new SavedSlot(2, Wood, 5)], 80f, new Vec3(4, 0, 5), 1.5f, new SavedPart(3, 1), "vanilla:scrapper_female",
                ["vanilla:sledgehammer", null, "test:chest"], [new Mechanica.Core.Ui.MapMarker("marker:2", "Scrap pile", 12.5f, 3f, -40f)])],
        };
    }

    [Fact]
    public void ASaveRoundTripsLosslessly()
    {
        var json = Sample().Write();
        var diagnostics = new List<DataDiagnostic>();
        var read = SaveGame.Read(json, "my-world.json", diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(read);
        Assert.Equal(json, read.Write());
        Assert.Equal("My World", read.Name);
        Assert.Equal(GameMode.Creative, read.Mode);
        Assert.Equal(-42, read.Seed);
        Assert.Equal(3600.25, read.Time); // the scripts' clock carries on from here
        Assert.Equal(3, read.ModScripts["test:scripts/rules.lua"]["visits"]!.GetValue<int>());
        Assert.Equal([new WorldObjectId(1, 2, 3), WorldObjectId.ForPoi(7, 2)], read.Destroyed);
        Assert.Equal(7200.5, read.Regrow[WorldObjectId.ForPoi(7, 2)]); // the geyser wells up again then
        var player = read.Player(Ada)!;
        Assert.Equal(new SavedSlot(2, Wood, 5), Assert.Single(player.Inventory));
        Assert.Equal((80f, new Vec3(4, 0, 5), 1.5f, new SavedPart(3, 1)), (player.Health, player.Position, player.Yaw, player.Bed));
        Assert.Equal("vanilla:scrapper_female", player.Character);
        Assert.Equal(["vanilla:sledgehammer", null, "test:chest"], player.Hotbar!);
        Assert.Equal(new Mechanica.Core.Ui.MapMarker("marker:2", "Scrap pile", 12.5f, 3f, -40f), Assert.Single(player.Markers!));
        Assert.Equal(2, read.Scripts[0].State["count"]!.GetValue<int>());

        var creation = Assert.Single(read.ReadCreations(_catalog, "my-world.json", diagnostics));
        Assert.Empty(diagnostics);
        Assert.Equal(4, creation.InventoryOf(1)!.Count(Scrap));
        Assert.Equal(new Vector3(1, 2, 3), creation.BodyPose(0).Position);
    }

    [Fact]
    public void ACreationThatNoLongerReadsIsLeftOutAndTheRestLoad()
    {
        var save = Sample();
        var broken = save with { Creations = [.. save.Creations, save.Creations[0].Replace("\"id\": 3", "\"id\": 4").Replace(Chest, "gone:part")] };
        var diagnostics = new List<DataDiagnostic>();

        var creations = broken.ReadCreations(_catalog, "w.json", diagnostics);

        Assert.Equal([3], creations.Select(c => c.Id));
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.File.Contains("creations[1]"));
    }

    [Theory]
    [InlineData("""{ "seed": 1, "mode": "survival" }""", "format_version is required")]
    [InlineData("""{ "format_version": 9, "seed": 1, "mode": "survival" }""", "not supported")]
    [InlineData("""{ "format_version": 1, "mode": "survival" }""", "seed is required")]
    [InlineData("""{ "format_version": 1, "seed": 1, "mode": "survival", "players": [{ "id": "nope" }] }""", "profile id")]
    [InlineData("""{ "format_version": 1, "seed": 1, "mode": "survival", "destroyed": ["x"] }""", "not a world object id")]
    [InlineData("""{ "format_version": 1, "seed": 1, "mode": "survival", "creations": [1] }""", "must be an object")]
    [InlineData("""{ "format_version": 1, "seed": 1, "mode": "survival", "time": -1 }""", "time must be 0 or more")]
    [InlineData("""{ "format_version": 1, "seed": 1, "mode": "survival", "creations": [{ "id": 1 }, { "id": 1 }] }""", "used twice")]
    public void BadSavesGiveClearErrors(string json, string message)
    {
        var diagnostics = new List<DataDiagnostic>();

        Assert.Null(SaveGame.Read(json, "bad.json", diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains(message) && d.File == "bad.json");
    }

    [Fact]
    public void UnknownKeysAreKept()
    {
        var json = """{ "format_version": 1, "seed": 1, "mode": "survival", "weather": { "a": 1 } }""";
        var save = SaveGame.Read(json, "w.json", [])!;

        Assert.Contains("\"weather\"", save.Write());
        Assert.Equal(0, save.Time); // a save from before the clock was kept starts it at 0
    }

    [Fact]
    public void TheStorySurvivesASave()
    {
        var story = new QuestState();
        story.Quests["vanilla:getting_started"] = new QuestProgress { Status = QuestStatus.Active, Step = 1, Count = 2, Used = ["0:1", "0:2"] };
        story.Flags["faction_stance"] = "ally";
        story.Revealed.Add("vanilla:ai_core_facility");
        var json = (Sample() with { Quests = story }).Write();
        var read = SaveGame.Read(json, "w.json", [])!;

        Assert.Equal(json, read.Write());
        Assert.Equal("ally", read.Quests!.Flags["faction_stance"]);
        Assert.Equal(["0:1", "0:2"], read.Quests.Of("vanilla:getting_started")!.Used!);
        Assert.Contains("vanilla:ai_core_facility", read.Quests.Revealed);
    }

    [Fact]
    public void ASaveWithoutAStoryHasNone() =>
        Assert.Null(SaveGame.Read(Sample().Write(), "w.json", [])!.Quests);

    [Fact]
    public void AMalformedStoryIsAnError()
    {
        var diagnostics = new List<DataDiagnostic>();
        var json = """{ "format_version": 1, "seed": 1, "mode": "survival", "quests": [1, 2] }""";

        Assert.Null(SaveGame.Read(json, "w.json", diagnostics));
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.SaveInvalid && d.JsonPath == "$.quests");
    }

    [Fact]
    public void ContentDifferencesNameTheMissingMod()
    {
        var differences = Sample().ContentDifferences(new ContentManifest([], ""));

        Assert.Equal(["missing test 1.0.0"], differences);
    }

    [Fact]
    public void TheStoreWritesListsAndDeletesSaves()
    {
        var store = new SaveStore(Path.Combine(Path.GetTempPath(), "mechanica-tests", Guid.NewGuid().ToString("N")));
        var path = store.Write(Sample());
        store.Write(Sample() with { Name = "Older", SavedAt = DateTimeOffset.UnixEpoch });
        var junk = Path.Combine(store.Directory, "junk.json");
        File.WriteAllText(junk, "{");
        // An unreadable file lists by its write time; pin it between the two saves so the order never follows the clock.
        File.SetLastWriteTimeUtc(junk, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("my-world.json", Path.GetFileName(path));
        var listed = store.List();
        Assert.Equal(["My World", "junk", "Older"], listed.Select(s => s.Name));
        Assert.NotNull(listed[1].Error);
        Assert.False(store.Delete("/etc/passwd"));
        Assert.True(store.Delete(path));
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void TheListCarriesPlayTimeAndPictureAndContinuePicksTheNewestGoodSave()
    {
        var store = new SaveStore(Path.Combine(Path.GetTempPath(), "mechanica-tests", Guid.NewGuid().ToString("N")));
        var path = store.Write(Sample() with { Name = "Kept", Time = 3 * 3600 + 5 * 60, SavedAt = DateTimeOffset.UnixEpoch });
        File.WriteAllBytes(SaveStore.ThumbnailFor(path), [1, 2, 3]);
        File.WriteAllText(Path.Combine(store.Directory, "broken.json"), "{");
        File.SetLastWriteTimeUtc(Path.Combine(store.Directory, "broken.json"), DateTime.UtcNow);

        var latest = store.Latest()!;
        Assert.Equal("Kept", latest.Name);
        Assert.Equal("3 h 05 min", latest.PlayTime);
        Assert.Equal(SaveStore.ThumbnailFor(path), latest.Thumbnail);
        Assert.True(store.Delete(path));
        Assert.False(File.Exists(SaveStore.ThumbnailFor(path)));
        Assert.Null(store.Latest());
    }

    [Theory]
    [InlineData(0, "under a minute")]
    [InlineData(45 * 60 + 30, "45 min")]
    [InlineData(3600, "1 h 00 min")]
    public void PlayTimeReadsNaturally(double seconds, string text) => Assert.Equal(text, SaveSummary.FormatPlayTime(seconds));

    [Theory]
    [InlineData("My World", "my-world")]
    [InlineData("  ../../etc  ", "etc")]
    [InlineData("!!!", "world")]
    [InlineData("Ünïcode 2", "n-code-2")]
    public void SlugsAreSafeFileNames(string name, string slug) => Assert.Equal(slug, SaveStore.Slug(name));

    [Fact]
    public void TheRosterRestoresWhatStillFits()
    {
        var inventory = new Inventory(_catalog, 4);
        inventory.Add(Scrap, 1);

        var problems = PlayerRoster.Restore(inventory, [new SavedSlot(0, Wood, 3), new SavedSlot(9, Wood, 1), new SavedSlot(1, "gone:item", 1)]);

        Assert.Equal(2, problems.Count);
        Assert.Equal(3, inventory.Count(Wood));
        Assert.Equal(0, inventory.Count(Scrap));
        Assert.Equal([new SavedSlot(0, Wood, 3)], PlayerRoster.SlotsOf(inventory));
    }

    [Fact]
    public void ProfilesReadBackAndBadOnesAreRejected()
    {
        var profile = PlayerProfile.New("  Ada\u0007 Lovelace with a very long name indeed ");

        Assert.True(PlayerProfile.IsValidId(profile.Id));
        Assert.Equal(PlayerProfile.MaxNameLength, profile.Name.Length);
        Assert.Equal(profile, PlayerProfile.FromJson(profile.ToJson()));
        Assert.Null(PlayerProfile.FromJson("""{ "id": "x", "name": "a" }"""));
        Assert.Null(PlayerProfile.FromJson("{"));
        Assert.Equal(PlayerProfile.Fixed("Ada"), PlayerProfile.Fixed("Ada"));
        Assert.Equal("Player", PlayerProfile.CleanName(" "));
    }
}
