using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Sound;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Bots;

/// <summary>
/// Chunk 21: vanilla's bots and raid table. Checks them against CATALOG_IDS.md (ids and sound events), the
/// spec's bot numbers (§11.6, §21.2) and raid table (§16), and that the checked-in files are exactly what
/// the Bot Behavior Editor batch produces (D3: authored with the editor, never by hand).
/// </summary>
public class VanillaBotsTests
{
    private const string Batch = "editor/batches/bots.json";

    private static readonly string ModsRoot = Path.Combine(TestMods.RepoRoot, "mods");
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(ModsRoot));
    private static readonly Lazy<BotBook> Book = new(() => BotBook.Load(Vanilla.Value));
    private static readonly Lazy<RaidTable> Raids = new(() => RaidTable.Load(Vanilla.Value, Book.Value));

    // CATALOG_IDS.md "Bots / creatures".
    private static readonly string[] Catalog =
    [
        "scuttler_green", "scuttler_blue", "scuttler_yellow", "scuttler_red", "scuttler_seed", "rakehand",
        "rakehand_baler", "rakehand_scarecrow", "spooler_blue", "spooler_green", "spooler_yellow", "spooler_red", "thresher", "grazer",
    ];

    private static Bot Bot(string id) => Book.Value.FindBot(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no bot '{id}'.");

    [Fact]
    public void BotsAndRaidsLoadWithoutProblems()
    {
        Assert.Empty(Book.Value.Diagnostics);
        Assert.Empty(Raids.Value.Diagnostics);
    }

    [Fact]
    public void BotsAreExactlyTheCatalogs() =>
        Assert.Equal(Catalog.Select(id => $"vanilla:{id}").Order(), Book.Value.All.Select(b => b.QualifiedId).Order());

    [Fact]
    public void EveryBotHasItsFamilysSoundsAndEveryAttackSoundExists()
    {
        var sounds = SoundRegistry.Load(Vanilla.Value);
        foreach (var bot in Book.Value.All)
        {
            foreach (var what in new[] { "idle", "alert", "attack", "death" })
                Assert.True(sounds.Find(bot.Sound(what)) is not null, $"{bot.QualifiedId}: sound {bot.Sound(what)}");
            foreach (var attack in bot.Attacks)
                Assert.True(sounds.Find(attack.Sound) is not null, $"{bot.QualifiedId}.{attack.Id}: sound {attack.Sound}");
        }
        Assert.Equal(["grazer", "rakehand", "scuttler", "spooler", "thresher"], Book.Value.All.Select(b => b.Family).Distinct().Order());
    }

    [Fact]
    public void TheBlueScuttlerSwimsAndTheYellowAndRedOnesShowTheirBlasts()
    {
        Assert.Equal(["vanilla:scuttler_blue"], Book.Value.All.Where(b => b.SwimDepth is not null).Select(b => b.QualifiedId));
        Assert.Equal(["vanilla:scuttler_blue"], Book.Value.All.Where(b => b.Wild is { Water: true }).Select(b => b.QualifiedId));
        Assert.Equal(BotEffects.Shockwave, Book.Value.FindBot("scuttler_yellow")!.Model!.Effects["stomp"]);
        Assert.Equal(BotEffects.Explosion, Book.Value.FindBot("scuttler_red")!.Model!.Effects["explode"]);
    }

    [Fact]
    public void BotsUseTheSpecNumbers()
    {
        // §11.6 and §21.2: health, damage, speed and the main drop's chance.
        Assert.Equal((60f, 15f, 5f), (Bot("scuttler_green").Health, Bot("scuttler_green").Attacks[0].Damage, Bot("scuttler_green").Speed));
        Assert.Equal(0.75f, Bot("scuttler_green").Drops.Single(d => d.Item == "vanilla:circuit_board").Chance);
        Assert.Equal((60f, 20f), (Bot("scuttler_blue").Health, Bot("scuttler_blue").Attacks[0].Damage));
        Assert.True(Bot("scuttler_blue").WaterSpeed > Bot("scuttler_blue").Speed, "the Blue Scuttler is faster in water");
        Assert.Equal((80f, 25f, BotAttackKind.Area), (Bot("scuttler_yellow").Health, Bot("scuttler_yellow").Attacks[0].Damage, Bot("scuttler_yellow").Attacks[0].Kind));
        Assert.Equal((40f, 40f, true), (Bot("scuttler_red").Health, Bot("scuttler_red").Attacks[0].Damage, Bot("scuttler_red").Attacks[0].KillsSelf));
        Assert.Equal((100f, 4f), (Bot("rakehand").Health, Bot("rakehand").Speed));
        Assert.Equal([20f, 30f], Bot("rakehand").Attacks.Select(a => a.Damage));
        Assert.Equal(0.65f, Bot("rakehand").Drops.Single(d => d.Item == "vanilla:component_kit").Chance);
        Assert.Equal((200f, 2.5f, (int?)2), (Bot("rakehand_baler").Health, Bot("rakehand_baler").Speed, Bot("rakehand_baler").RaidCost));
        Assert.Equal([25f, 45f], Bot("rakehand_baler").Attacks.Select(a => a.Damage));
        Assert.Equal((70f, 5.5f), (Bot("rakehand_scarecrow").Health, Bot("rakehand_scarecrow").Speed));
        Assert.Equal([15f, 22f], Bot("rakehand_scarecrow").Attacks.Select(a => a.Damage));
        Assert.Equal((40f, 25f, 3f, BotAttackKind.Ranged), (Bot("spooler_blue").Health, Bot("spooler_blue").Attacks[0].Damage, Bot("spooler_blue").Speed, Bot("spooler_blue").Attacks[0].Kind));
        Assert.Equal((40f, 15f), (Bot("spooler_green").Health, Bot("spooler_green").Attacks[0].Damage));
        var thresher = Bot("thresher");
        Assert.Equal((1500f, 3f, true), (thresher.Health, thresher.Speed, thresher.MeleeImmune));
        Assert.Equal([10f, 35f], thresher.Attacks.Where(a => a.Kind == BotAttackKind.Melee).Select(a => a.Damage));
        Assert.Contains(thresher.Attacks, a => a.DamagePerSecond > 0);
        Assert.Equal(4, thresher.Drops.Single(d => d.Item == "vanilla:component_kit").Count);
        Assert.True(Bot("grazer").Passive && Bot("grazer").Attacks.Count == 0 && Bot("grazer").RaidCost is null);
    }

    [Fact]
    public void RaidTableIsTheSpecsWithPlayerScaling()
    {
        var table = Raids.Value;
        // §16: level, crop value threshold and bot budget.
        Assert.Equal([(1, 10f, 4), (2, 31f, 8), (3, 61f, 12), (4, 101f, 16), (5, 161f, 22), (6, 251f, 30), (7, 401f, 36)],
            table.Levels.Select(l => (l.Level, l.MinValue, l.Budget)));
        Assert.Equal([1f, 1.3f, 1.6f, 2f], [.. Enumerable.Range(1, 4).Select(table.MultiplierFor)]);
        // Each listed mix spends exactly its budget, at the costs the table implies.
        foreach (var level in table.Levels.Take(6))
            Assert.Equal(level.Budget, level.MixCost);
        Assert.Equal(["vanilla:scuttler_green"], table.Levels[0].Bots.Select(b => b.Bot.QualifiedId));
        Assert.Contains(table.Levels[5].Bots, b => b.Bot.QualifiedId == "vanilla:thresher" && b.Count == 2);
        // Rakehand raiders are a random Field Hand or Scarecrow early, and any of the three from level 4.
        Assert.Equal(["vanilla:rakehand_scarecrow"], table.Levels[1].Bots.Single(b => b.Bot.QualifiedId == "vanilla:rakehand").OneOf.Select(b => b.QualifiedId));
        Assert.Equal(["vanilla:rakehand_baler", "vanilla:rakehand_scarecrow"],
            table.Levels[3].Bots.Single(b => b.Bot.QualifiedId == "vanilla:rakehand").OneOf.Select(b => b.QualifiedId));
        var mixed = Enumerable.Range(0, 20).SelectMany(seed => table.Plan(300, 1, seed: seed)!.Bots).ToHashSet();
        Assert.Subset(mixed, new HashSet<string> { "vanilla:rakehand", "vanilla:rakehand_baler", "vanilla:rakehand_scarecrow" });
        Assert.Null(table.Plan(9, 1));
        var four = table.Plan(20, 1)!;
        Assert.Equal((1, 4), (four.Level, four.Bots.Count));
        Assert.Equal(8, table.Plan(20, 4)!.Budget);
        Assert.Equal(7, table.Plan(1000, 1)!.Level);
    }

    [Fact]
    public void TheEditorBatchReproducesTheCheckedInFiles()
    {
        var vanilla = Path.Combine(ModsRoot, "vanilla");
        var root = Path.Combine(Path.GetTempPath(), "mechanica-tests", Guid.NewGuid().ToString("N"), "mods");
        var scratch = Path.Combine(root, "vanilla");
        try
        {
            foreach (var file in Directory.GetFiles(vanilla, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(vanilla, file).Replace('\\', '/');
                if (relative.StartsWith("bots/", StringComparison.Ordinal) || relative == RaidTable.FilePath)
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(scratch, relative))!);
                File.Copy(file, Path.Combine(scratch, relative));
            }
            var diagnostics = new List<DataDiagnostic>();
            var batch = BotBatch.Read(File.ReadAllText(Path.Combine(scratch, Batch)), Batch, diagnostics)!;
            var results = BotBatch.Run(batch, scratch, Template, Batch, diagnostics);

            Assert.Empty(diagnostics);
            Assert.Equal(Catalog.Length + 1, results.Count(r => r.Succeeded));
            var expected = Directory.GetFiles(Path.Combine(vanilla, "bots")).Append(Path.Combine(vanilla, RaidTable.FilePath))
                .Select(f => Path.GetRelativePath(vanilla, f).Replace('\\', '/')).Order().ToList();
            var made = Directory.GetFiles(Path.Combine(scratch, "bots")).Append(Path.Combine(scratch, RaidTable.FilePath))
                .Select(f => Path.GetRelativePath(scratch, f).Replace('\\', '/')).Order().ToList();
            Assert.Equal(expected, made);
            foreach (var file in expected)
                Assert.True(File.ReadAllText(Path.Combine(vanilla, file)) == File.ReadAllText(Path.Combine(scratch, file)),
                    $"{file} differs from the batch's output; edit {Batch} and re-run tools/author-vanilla.sh instead of the file.");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
        }
    }

    // The editor's own templates, as the game reads them from res://data/bot_templates.
    internal static string? Template(string name)
    {
        var path = Path.Combine(TestMods.RepoRoot, "game", "data", "bot_templates", $"{name}.json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    [Fact]
    public void TheScarecrowStandsStillUntilAPlayerComesClose()
    {
        var tree = Bot("rakehand_scarecrow").Behavior!.ToJsonString();
        Assert.Contains("\"find_player\",\"range\":6", tree);
        Assert.Contains("\"clear_target\"", tree);
        Assert.DoesNotContain("\"wander\"", tree);
    }

    [Fact]
    public void TheGreenSpoolerSlowsTheRakehandHesitatesAndTheThresherBreaksObjects()
    {
        var bubble = Bot("spooler_green").Attack("bubble")!;
        Assert.True(bubble.Slows);
        Assert.Equal((0.5f, 3f, 0f), (bubble.SlowFactor, bubble.SlowSeconds, bubble.DamagePerSecond));
        Assert.Contains("\"hesitate\"", Bot("rakehand").Behavior!.ToJsonString());
        // Saw Blade spec: every Scuttler balks at a saw blade spinning at cutting speed, then comes on anyway.
        foreach (var scuttler in new[] { "scuttler_green", "scuttler_blue", "scuttler_yellow", "scuttler_red", "scuttler_seed" })
            Assert.Contains("\"part\":\"vanilla:saw_blade,vanilla:drill\"", Bot(scuttler).Behavior!.ToJsonString());
        var thresher = Bot("thresher");
        Assert.True(thresher.Attack("kick")!.ObjectDamage > 0);
        Assert.Contains("\"break_objects\"", thresher.Behavior!.ToJsonString());
        Assert.All(Book.Value.All.Where(b => b.QualifiedId != "vanilla:thresher").SelectMany(b => b.Attacks), a => Assert.Equal(0f, a.ObjectDamage));
    }

    [Theory]
    [InlineData("""{ "factor": 1, "seconds": 2 }""", "$.attacks[0].slow.factor", "factor must be more than 0 and less than 1 (got 1).")]
    [InlineData("""{ "factor": 0, "seconds": 2 }""", "$.attacks[0].slow.factor", "factor must be more than 0 and less than 1 (got 0).")]
    [InlineData("""{ "seconds": 2 }""", "$.attacks[0].slow.factor", "factor (what speed is multiplied by, between 0 and 1) is required.")]
    [InlineData("""{ "factor": 0.5 }""", "$.attacks[0].slow.seconds", "seconds is required.")]
    public void BadSlowsAreReported(string slow, string path, string message)
    {
        var bot = new BotDefinition
        {
            Id = "spitter", Name = "Spitter", Health = 10, Speed = 1,
            Attacks = [new BotAttackDefinition { Id = "wrap", Kind = BotAttackKind.Melee, Damage = 1, Range = 1, Interval = 1,
                Slow = System.Text.Json.JsonSerializer.Deserialize<BotSlow>(slow, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower }) }],
        };

        var diagnostics = BotBook.Validate(bot, "test", null, Vanilla.Value, "test/bots/spitter.json");

        Assert.Contains(diagnostics, d => d.JsonPath == path && d.Message == message);
    }

    [Fact]
    public void NegativeObjectDamageIsReported()
    {
        var bot = new BotDefinition
        {
            Id = "smasher", Name = "Smasher", Health = 10, Speed = 1,
            Attacks = [new BotAttackDefinition { Id = "kick", Kind = BotAttackKind.Melee, Damage = 1, Range = 1, Interval = 1, ObjectDamage = -1 }],
        };

        var diagnostics = BotBook.Validate(bot, "test", null, Vanilla.Value, "test/bots/smasher.json");

        Assert.Contains(diagnostics, d => d.JsonPath == "$.attacks[0].object_damage" && d.Message == "object_damage must be 0 or more.");
    }

    [Theory]
    [InlineData("""{ "attacks": { "whip": ["a"] } }""", "$.model.mesh", "mesh (a .glb or .gltf in the mod) is required.")]
    [InlineData("""{ "mesh": "../x.glb" }""", "$.model.mesh", null)]
    [InlineData("""{ "mesh": "meshes/x.glb", "attacks": { "bite": ["a"] } }""", "$.model.attacks.bite", "the bot has no attack 'bite'.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "attacks": { "whip": [] } }""", "$.model.attacks.whip", "list one or more clip names.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "eye_calm": "green" }""", "$.model.eye_calm", "eye_calm 'green' must be \"#rrggbb\".")]
    [InlineData("""{ "mesh": "meshes/x.glb", "clip_speeds": { "patrol": 0 } }""", "$.model.clip_speeds.patrol", "clip speed 0 must be above 0 m/s.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "effects": { "bite": "explosion" } }""", "$.model.effects.bite", "the bot has no attack 'bite' (use an attack id or \"death\").")]
    [InlineData("""{ "mesh": "meshes/x.glb", "effects": { "whip": "lightning" } }""", "$.model.effects.whip", "effect 'lightning' must be one of: explosion, shockwave.")]
    public void BadModelsAreReported(string model, string path, string? message)
    {
        var bot = ModelBot(model);

        var diagnostics = BotBook.Validate(bot, "test", null, Vanilla.Value, "test/bots/whipper.json");

        Assert.Contains(diagnostics, d => d.JsonPath == path && (message is null || d.Message == message));
    }

    [Theory]
    [InlineData("""{ "count": 2 }""", "$.wild.habitat", "habitat (water or land) is required.")]
    [InlineData("""{ "habitat": "air", "count": 2 }""", "$.wild.habitat", "habitat 'air' must be water or land.")]
    [InlineData("""{ "habitat": "water" }""", "$.wild.count", "count (how many near each player) is required.")]
    [InlineData("""{ "habitat": "water", "count": 0 }""", "$.wild.count", "count must be 1 or more.")]
    [InlineData("""{ "habitat": "water", "count": 1, "min_distance": 50, "max_distance": 40 }""", "$.wild.max_distance", "max_distance (40) must be more than min_distance (50).")]
    [InlineData("""{ "habitat": "land", "count": 1, "despawn_distance": 60 }""", "$.wild.despawn_distance", "despawn_distance (60) must be more than max_distance (70).")]
    public void BadWildSpawnsAreReported(string wild, string path, string message)
    {
        var bot = ModelBot(null) with
        {
            Wild = System.Text.Json.JsonSerializer.Deserialize<BotWildDefinition>(wild,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower }),
        };

        var diagnostics = BotBook.Validate(bot, "test", null, Vanilla.Value, "test/bots/whipper.json");

        Assert.Contains(diagnostics, d => d.JsonPath == path && d.Message == message);
    }

    [Fact]
    public void AModelFillsInItsClipsAndEyeColours()
    {
        var book = BotBook.From(Vanilla.Value, ("test", ModelBot("""{ "mesh": "meshes/bots/whipper.glb", "attacks": { "whip": ["slam", "sweep"] }, "clip_speeds": { "chase": 2 }, "eye_engaged": "#FF0000", "effects": { "whip": "shockwave", "death": "explosion" } }""")));

        var model = book.FindBot("test:whipper")!.Model!;

        Assert.Equal("test:meshes/bots/whipper.glb", model.Mesh);
        Assert.Equal(["slam", "sweep"], model.Attacks["whip"]);
        Assert.Equal(["poke"], model.Attacks["poke"]);
        Assert.Equal(2f, model.ClipSpeeds["chase"]);
        Assert.Equal((BotBook.DefaultEyeCalm, "#ff0000"), (model.EyeCalm, model.EyeEngaged));
        Assert.Equal((BotEffects.Shockwave, BotEffects.Explosion), (model.Effects["whip"], model.Effects[BotEffects.Death]));
        Assert.Null(BotBook.From(Vanilla.Value, ("test", ModelBot(null))).FindBot("test:whipper")!.Model);
    }

    private static BotDefinition ModelBot(string? model) => new()
    {
        Id = "whipper", Name = "Whipper", Health = 10, Speed = 1,
        Attacks =
        [
            new BotAttackDefinition { Id = "whip", Kind = BotAttackKind.Melee, Damage = 1, Range = 1, Interval = 1 },
            new BotAttackDefinition { Id = "poke", Kind = BotAttackKind.Melee, Damage = 1, Range = 1, Interval = 1 },
        ],
        Model = model is null ? null : System.Text.Json.JsonSerializer.Deserialize<BotModelDefinition>(model,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower }),
    };
}
