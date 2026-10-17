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
        "spooler_blue", "spooler_green", "spooler_yellow", "spooler_red", "thresher", "grazer",
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
}
