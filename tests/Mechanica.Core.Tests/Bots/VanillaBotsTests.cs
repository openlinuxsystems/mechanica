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
        "rakehand_baler", "rakehand_scarecrow", "spooler_blue", "spooler_green", "spooler_yellow", "spooler_red", "thresher",
        "thresher_tiller", "thresher_duster", "grazer", "grazer_calf",
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

    // Bots fire their sounds and forget them (cries, attacks, idles, deaths): a loop among them would play on where
    // the bot was, after it died or left.
    [Fact]
    public void EverySoundABotPlaysIsAOneShot()
    {
        var sounds = SoundRegistry.Load(Vanilla.Value);
        foreach (var bot in Book.Value.All)
        {
            var ids = new[] { "idle", "alert", "attack", "death" }.Select(bot.Sound)
                .Concat(bot.Attacks.Select(a => a.Sound))
                .Concat(bot.Model?.Idles.Select(i => i.Sound).OfType<string>() ?? []);
            if (bot.Script is { } script)
            {
                var path = Path.Combine(ModsRoot, script.Split(':')[0], script.Split(':')[1]);
                ids = ids.Concat(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(path), @"bot\.sound\(""([^""]+)""\)").Select(m => m.Groups[1].Value));
            }
            foreach (var id in ids.Distinct())
            {
                var e = sounds.Find(id, bot.ModId);
                Assert.True(e is not null, $"{bot.QualifiedId}: sound {id}");
                Assert.False(e!.Loop, $"{bot.QualifiedId}: sound {id} is a loop");
            }
        }
        Assert.Contains("rakehand_chase", File.ReadAllText(Path.Combine(ModsRoot, "vanilla", "scripts", "bots", "rakehand.lua")));
    }

    [Fact]
    public void TheBlueScuttlerSwimsAndTheYellowAndRedOnesShowTheirBlasts()
    {
        Assert.Equal(["vanilla:scuttler_blue"], Book.Value.All.Where(b => b.SwimDepth is not null).Select(b => b.QualifiedId));
        Assert.Equal(["vanilla:scuttler_blue"], Book.Value.All.Where(b => b.Wild is { Water: true }).Select(b => b.QualifiedId));
        Assert.Equal(BotEffects.Shockwave, Book.Value.FindBot("scuttler_yellow")!.Model!.Effects["stomp"]);
        Assert.Equal(BotEffects.Explosion, Book.Value.FindBot("scuttler_red")!.Model!.Effects["explode"]);
    }

    [Theory]
    [InlineData("spooler_blue", "tape", "tape_shot")]
    [InlineData("spooler_green", "bubble", "bubble_shot")]
    [InlineData("spooler_yellow", "tape", "rapid_shot")]
    [InlineData("spooler_red", "tape", "heavy_shot")]
    public void EachSpoolerHasItsOwnModelAndShot(string id, string attack, string clip)
    {
        var model = Book.Value.FindBot(id)!.Model!;
        Assert.Equal($"vanilla:meshes/bots/{id}.glb", model.Mesh);
        Assert.Equal([clip], model.Attacks[attack]);
        Assert.Equal(BotEffects.Shot, model.Effects[attack]);
    }

    [Theory]
    [InlineData("thresher", "kick,swipe,pesticide", "track_stomp,sickle_swipe,spray_sweep", null, null)]
    [InlineData("thresher_tiller", "till,stomp", "till_grind,rear_stomp", "stomp", BotEffects.Shockwave)]
    [InlineData("thresher_duster", "canister,pesticide", "mortar_shot,boom_spray", "canister", BotEffects.Shot)]
    public void EachThresherHasItsOwnModelAndAttacks(string id, string attacks, string clips, string? effectAttack, string? effect)
    {
        var bot = Bot(id);
        Assert.Equal((true, "thresher"), (bot.MeleeImmune, bot.Family));
        Assert.Equal(attacks.Split(','), bot.Attacks.Select(a => a.Id));
        var model = bot.Model!;
        Assert.Equal($"vanilla:meshes/bots/{id}.glb", model.Mesh);
        Assert.Equal(clips.Split(','), attacks.Split(',').Select(a => model.Attacks[a].Single()));
        Assert.Equal(effectAttack is null ? [] : [(effectAttack, effect!)], model.Effects.Select(e => (e.Key, e.Value)));
    }

    [Fact]
    public void TheTillerIsSlowAndToughAndTheDusterFastAndRanged()
    {
        var (harvester, tiller, duster) = (Bot("thresher"), Bot("thresher_tiller"), Bot("thresher_duster"));
        Assert.True(tiller.Health > harvester.Health && tiller.Speed < harvester.Speed, "the Tiller is tougher and slower");
        Assert.True(duster.Health < harvester.Health && duster.Speed > harvester.Speed && duster.Sight > harvester.Sight,
            "the Duster is frailer, faster and sees further");
        Assert.Equal(BotAttackKind.Ranged, duster.Attack("canister")!.Kind);
        Assert.Equal(BotAttackKind.Area, tiller.Attack("stomp")!.Kind);
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
        // Thresher raiders are any of the three.
        foreach (var level in table.Levels.Skip(5))
            Assert.Equal(["vanilla:thresher_tiller", "vanilla:thresher_duster"],
                level.Bots.Single(b => b.Bot.QualifiedId == "vanilla:thresher").OneOf.Select(b => b.QualifiedId));
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
        Assert.True(Bot("thresher_tiller").Attack("till")!.ObjectDamage > 0);
        Assert.Contains("\"break_objects\"", Bot("thresher_tiller").Behavior!.ToJsonString());
        Assert.All(Book.Value.All.Where(b => b.QualifiedId is not ("vanilla:thresher" or "vanilla:thresher_tiller")).SelectMany(b => b.Attacks),
            a => Assert.Equal(0f, a.ObjectDamage));
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
    [InlineData("""{ "mesh": "meshes/x.glb", "effects": { "whip": "lightning" } }""", "$.model.effects.whip", "effect 'lightning' must be one of: explosion, shockwave, shot.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "effects": { "death": "shot" } }""", "$.model.effects.death", "a shot follows an attack to what it hit; use it on an attack, not on death.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "variants": ["../y.glb"] }""", "$.model.variants[0]", null)]
    [InlineData("""{ "mesh": "meshes/x.glb", "idles": [{ "sound": "moo" }] }""", "$.model.idles[0].clip", "clip is required.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "idles": [{ "clip": "moo", "sound": " " }] }""", "$.model.idles[0].sound", "sound must name a sound event (or be left out).")]
    [InlineData("""{ "mesh": "meshes/x.glb", "idles": [{ "clip": "moo", "weight": 0 }] }""", "$.model.idles[0].weight", "weight 0 must be above 0.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "kept_nodes": [" "] }""", "$.model.kept_nodes[0]", "a node name is required.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "idle_every": [5] }""", "$.model.idle_every", "idle_every must be [min, max] seconds, 0 < min <= max.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "idle_every": [9, 4] }""", "$.model.idle_every", "idle_every must be [min, max] seconds, 0 < min <= max.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "track": { "yaw": 40 } }""", "$.model.track.nodes", "name one or more nodes that turn, each with its share (0 to 1).")]
    [InlineData("""{ "mesh": "meshes/x.glb", "track": { "nodes": { "Head": 1.5 } } }""", "$.model.track.nodes.Head", "share 1.5 must be above 0 and at most 1.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "track": { "nodes": { "Head": 1 }, "yaw": 200 } }""", "$.model.track.yaw", "yaw 200 must be above 0 and at most 170 degrees.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "track": { "nodes": { "Head": 1 }, "pitch": -5 } }""", "$.model.track.pitch", "pitch -5 must be 0 to 80 degrees.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "track": { "nodes": { "Head": 1 }, "range": 0 } }""", "$.model.track.range", "range 0 must be above 0 and at most 30 m.")]
    [InlineData("""{ "mesh": "meshes/x.glb", "sway": { "Antenna": 4 } }""", "$.model.sway.Antenna", "looseness 4 must be above 0 and at most 3.")]
    public void BadModelsAreReported(string model, string path, string? message)
    {
        var bot = ModelBot(model);

        var diagnostics = BotBook.Validate(bot, "test", null, Vanilla.Value, "test/bots/whipper.json");

        Assert.Contains(diagnostics, d => d.JsonPath == path && (message is null || d.Message == message));
    }

    [Theory]
    [InlineData("""{ "habitat": "land", "count": 2, "biomes": [""] }""", "$.wild.biomes[0]", "a biome id is required.")]
    [InlineData("""{ "habitat": "land", "count": 2, "herd": [3, 2] }""", "$.wild.herd", "herd must be [min, max], 1 <= min <= max.")]
    [InlineData("""{ "habitat": "land", "count": 2, "herd": [0, 2] }""", "$.wild.herd", "herd must be [min, max], 1 <= min <= max.")]
    [InlineData("""{ "habitat": "land", "count": 2, "near": { "radius": 5 } }""", "$.wild.near.objects", "list one or more world object ids.")]
    [InlineData("""{ "habitat": "land", "count": 2, "near": { "objects": ["farm"] } }""", "$.wild.near.radius", "radius (m) is required.")]
    [InlineData("""{ "habitat": "land", "count": 2, "near": { "objects": ["farm"], "radius": 5, "share": 2 } }""", "$.wild.near.share", "share must be from 0 to 1.")]
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

    [Fact]
    public void AModelPicksAVariantPerBotAndKeepsItsIdles()
    {
        var book = BotBook.From(Vanilla.Value, ("test", ModelBot("""{ "mesh": "meshes/a.glb", "variants": ["meshes/b.glb", "meshes/c.glb"], "idles": [{ "clip": "graze", "weight": 3 }, { "clip": "moo", "sound": "moo" }], "idle_every": [2, 4] }""")));

        var model = book.FindBot("test:whipper")!.Model!;

        Assert.Equal(["test:meshes/a.glb", "test:meshes/b.glb", "test:meshes/c.glb", "test:meshes/a.glb"], [model.MeshFor(0), model.MeshFor(1), model.MeshFor(2), model.MeshFor(3)]);
        Assert.Equal(model.MeshFor(-7), model.MeshFor(-7));
        Assert.Equal([new BotIdle("graze", null, 3f), new BotIdle("moo", "moo", 1f)], model.Idles);
        Assert.Equal((2f, 4f), (model.IdleMin, model.IdleMax));
        var plain = BotBook.From(Vanilla.Value, ("test", ModelBot("""{ "mesh": "meshes/a.glb" }"""))).FindBot("test:whipper")!.Model!;
        Assert.Equal(("test:meshes/a.glb", BotBook.DefaultIdleMin, BotBook.DefaultIdleMax), (plain.MeshFor(5), plain.IdleMin, plain.IdleMax));
    }

    [Fact]
    public void AModelsHeadTrackAndSwayKeepTheirNodesInOrderWithDefaults()
    {
        var model = BotBook.From(Vanilla.Value, ("test", ModelBot("""{ "mesh": "meshes/a.glb", "track": { "nodes": { "Neck": 0.6, "Head": 0.4 } }, "sway": { "Antenna": 1.5 } }""")))
            .FindBot("test:whipper")!.Model!;

        var track = model.Track!;
        Assert.Equal(["Neck", "Head"], track.Nodes.Select(n => n.Key));
        Assert.Equal((70f, 30f, 6f), (track.Yaw, track.Pitch, track.Range));
        Assert.Equal(1.5f, model.Sway["Antenna"]);
        var plain = BotBook.From(Vanilla.Value, ("test", ModelBot("""{ "mesh": "meshes/a.glb" }"""))).FindBot("test:whipper")!.Model!;
        Assert.Null(plain.Track);
        Assert.Empty(plain.Sway);
    }

    [Fact]
    public void EveryVanillaBotWatchesPlayersAndTheGrazerSwingsItsEarsAndTail()
    {
        foreach (var bot in Book.Value.All)
            Assert.True(bot.Model?.Track is { Nodes.Count: > 0 }, bot.QualifiedId);
        var grazer = Book.Value.FindBot("vanilla:grazer")!.Model!;
        Assert.Equal(["EarL", "EarR", "Tail1"], grazer.Sway.Keys.Order());
        Assert.Contains(Book.Value.FindBot("vanilla:grazer_calf")!.Model!.Idles, i => i.Clip == "frolic");
    }

    [Fact]
    public void TheGrazerHasThreeCoatsAndGrazesMoosAndFlees()
    {
        var model = Book.Value.FindBot("vanilla:grazer")!.Model!;

        Assert.Equal(["vanilla:meshes/bots/grazer_fawn.glb", "vanilla:meshes/bots/grazer_chocolate.glb"], model.Variants);
        Assert.Contains(model.Idles, i => i.Clip == "graze");
        var moo = Assert.Single(model.Idles, i => i.Sound is not null);
        Assert.Equal(("moo", "grazer_idle"), (moo.Clip, moo.Sound));
        foreach (var mesh in model.Variants.Prepend(model.Mesh))
            Assert.True(File.Exists(Path.Combine(ModsRoot, "vanilla", mesh["vanilla:".Length..])), mesh);
    }

    [Fact]
    public void WildGrazersRoamMeadowsAndFieldsInHerdsRoundFarms()
    {
        var wild = Book.Value.FindBot("vanilla:grazer")!.Wild!;

        Assert.False(wild.Water);
        Assert.Equal(["field", "meadow"], wild.Biomes.Order());
        Assert.True(wild.HerdMin >= 2 && wild.HerdMax <= wild.Count);
        Assert.Contains("scene_farmstead", wild.Near!.Objects);
    }

    [Fact]
    public void GrazersAreTamedWithCarrots()
    {
        var tame = Book.Value.FindBot("vanilla:grazer")!.Tame!;

        Assert.Contains("vanilla:carrot", tame.Foods);
        Assert.True(tame.Feeds >= 1);
        Assert.Equal(["Collar"], Book.Value.FindBot("vanilla:grazer")!.Model!.KeptNodes);
        Assert.Equal("vanilla:lead", tame.Lead);
        Assert.Equal(["vanilla:feeding_trough"], tame.Homes);
        Assert.Equal("LeadRing", Book.Value.FindBot("vanilla:grazer")!.Model!.LeadNode);
    }

    [Fact]
    public void GrazersBreedCalvesThatGrowIntoGrazers()
    {
        var grazer = Book.Value.FindBot("vanilla:grazer")!;
        var breed = grazer.Tame!.Breed!;
        Assert.Equal(("vanilla:tomato", "vanilla:grazer_calf"), (breed.Treat, breed.Calf));
        Assert.True(breed.Herd >= 2 && breed.Seconds > 0 && breed.Cooldown > breed.Seconds);

        var calf = Book.Value.FindBot(breed.Calf)!;
        Assert.Equal("vanilla:grazer", calf.Grow!.Into);
        Assert.Null(calf.Wild);
        Assert.Null(calf.Tame!.Breed);
        Assert.Equal(grazer.Tame.Lead, calf.Tame.Lead);
        Assert.Equal(grazer.Tame.Homes, calf.Tame.Homes);
        // a calf keeps its coat when it grows up: the same number of looks, in the same order
        Assert.Equal(grazer.Model!.Variants.Count, calf.Model!.Variants.Count);
        Assert.Equal(["Collar"], calf.Model.KeptNodes);
        Assert.True(calf.Height < grazer.Height && calf.Health < grazer.Health);
    }

    [Fact]
    public void TameGrazersGiveMilkAfterFiveCarrotsAndAllGrazersDropSteak()
    {
        var grazer = Book.Value.FindBot("vanilla:grazer")!;
        Assert.Equal(new BotProduce("vanilla:grazer_milk", 5, 300, 30), grazer.Tame!.Produce);
        Assert.Contains("vanilla:carrot", grazer.Tame.Foods);
        Assert.Contains("vanilla:feeding_trough", grazer.Tame.Homes);
        var steak = Assert.Single(grazer.Drops);
        Assert.Equal(("vanilla:grazer_steak", 1f, 1, 2), (steak.Item, steak.Chance, steak.Count, steak.CountMax));
        // calves give no milk
        Assert.Null(Book.Value.FindBot("vanilla:grazer_calf")!.Tame!.Produce);
    }

    [Theory]
    [InlineData("""{ "feeds": 2 }""", "$.tame.foods", "list one or more foods it eats.")]
    [InlineData("""{ "foods": ["no_such_food"] }""", "$.tame.foods[0]", "there is no item 'vanilla:no_such_food'.")]
    [InlineData("""{ "foods": ["carrot"], "feeds": 0 }""", "$.tame.feeds", "feeds must be 1 or more.")]
    [InlineData("""{ "foods": ["carrot"], "lead": "no_such_rope" }""", "$.tame.lead", "there is no item 'vanilla:no_such_rope'.")]
    [InlineData("""{ "foods": ["carrot"], "homes": ["no_such_trough"] }""", "$.tame.homes[0]", "there is no part 'vanilla:no_such_trough'.")]
    [InlineData("""{ "foods": ["carrot"], "home_radius": 0 }""", "$.tame.home_radius", "home_radius must be greater than 0.")]
    [InlineData("""{ "foods": ["carrot"], "breed": { "calf": "grazer_calf" } }""", "$.tame.breed.treat", "treat is required.")]
    [InlineData("""{ "foods": ["carrot"], "breed": { "treat": "no_such_treat", "calf": "grazer_calf" } }""", "$.tame.breed.treat", "there is no item 'vanilla:no_such_treat'.")]
    [InlineData("""{ "foods": ["carrot"], "breed": { "treat": "tomato" } }""", "$.tame.breed.calf", "calf is required.")]
    [InlineData("""{ "foods": ["carrot"], "breed": { "treat": "tomato", "calf": "grazer_calf", "herd": 1 } }""", "$.tame.breed.herd", "herd must be 2 or more.")]
    [InlineData("""{ "foods": ["carrot"], "breed": { "treat": "tomato", "calf": "grazer_calf", "seconds": 0 } }""", "$.tame.breed.seconds", "seconds must be greater than 0.")]
    [InlineData("""{ "foods": ["carrot"], "homes": ["feeding_trough"], "produce": { "feeds": 5 } }""", "$.tame.produce.item", "item is required.")]
    [InlineData("""{ "foods": ["carrot"], "homes": ["feeding_trough"], "produce": { "item": "no_such_milk" } }""", "$.tame.produce.item", "there is no item 'vanilla:no_such_milk'.")]
    [InlineData("""{ "foods": ["carrot"], "homes": ["feeding_trough"], "produce": { "item": "grazer_milk", "feeds": 0 } }""", "$.tame.produce.feeds", "feeds must be 1 or more.")]
    [InlineData("""{ "foods": ["carrot"], "homes": ["feeding_trough"], "produce": { "item": "grazer_milk", "eat_every": 0 } }""", "$.tame.produce.eat_every", "eat_every must be greater than 0.")]
    [InlineData("""{ "foods": ["carrot"], "produce": { "item": "grazer_milk" } }""", "$.tame.produce", "a bot that gives produce eats from its home: list its homes.")]
    public void BadTamesAreReported(string tame, string path, string message)
    {
        var bot = ModelBot(null) with
        {
            Tame = System.Text.Json.JsonSerializer.Deserialize<BotTameDefinition>(tame,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower }),
        };

        var diagnostics = BotBook.Validate(bot, "test", null, Vanilla.Value, "test/bots/whipper.json");

        Assert.Contains(diagnostics, d => d.JsonPath == path && d.Message == message);
    }

    [Fact]
    public void BotsNamingMissingBotsAreLeftOut()
    {
        var parent = ModelBot(null) with
        {
            Tame = new BotTameDefinition { Foods = ["carrot"], Breed = new BotBreedDefinition { Treat = "tomato", Calf = "no_such_calf" } },
        };
        var young = ModelBot(null) with { Id = "whelp", Grow = new BotGrowDefinition { Into = "no_such_adult", Seconds = 10 } };

        var error = Assert.Throws<FormatException>(() => BotBook.From(Vanilla.Value, ("test", parent), ("test", young)));

        Assert.Contains("there is no bot 'vanilla:no_such_calf'.", error.Message);
        Assert.Contains("there is no bot 'vanilla:no_such_adult'.", error.Message);
        Assert.NotNull(BotBook.From(Vanilla.Value, ("test", young with { Grow = new BotGrowDefinition { Into = "test:whipper", Seconds = 10 } }),
            ("test", ModelBot(null))).FindBot("test:whelp")!.Grow);
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
