using Mechanica.Core.Net;
using Mechanica.Core.Saves;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Bots;
using Mechanica.Core.Tests.Data;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Survival;

public class DifficultyTests
{
    private static SurvivalSettings Shipped() =>
        SurvivalSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "survival.json")));

    [Fact]
    public void EasyIsGentlerThanNormalInTheShippedData()
    {
        var settings = Shipped();
        var easy = settings.For(Difficulty.Easy);
        Assert.True(easy.BotDamage < 1 && easy.RaidBudget < 1 && easy.RaidCooldown > 1);
        Assert.Equal(new DifficultySettings(), settings.For(Difficulty.Normal));
    }

    [Fact]
    public void EasyScalesBotDamageOnly()
    {
        var f = new SurvivalFixture(Shipped() with { StartingItems = [] });
        f.Survival.Difficulty = Difficulty.Easy;
        Assert.Equal(80f, f.Survival.Damage(Me.Peer, 40, "bot"));
        Assert.Equal(40f, f.Survival.Damage(Me.Peer, 40, "fall"));
        Assert.Equal(Difficulty.Easy, f.Survival.View(Me.Peer)!.Difficulty);
    }

    [Fact]
    public void EasyRaidsAreSmaller()
    {
        var table = new BotFixture().Raids;
        var normal = table.Plan(501, 4)!;
        var easy = table.Plan(501, 4, budgetScale: 0.6f)!;
        Assert.Equal(Math.Round(normal.Budget * 0.6, MidpointRounding.AwayFromZero), easy.Budget);
        Assert.True(easy.Bots.Count < normal.Bots.Count);
        Assert.Equal(1, table.Plan(3, 1, budgetScale: 0.01f)!.Budget);
    }

    [Theory]
    [InlineData("""{ "difficulties": { "hard": { "bot_damage": 2 } } }""", "hard")]
    [InlineData("""{ "difficulties": { "easy": { "bot_damage": 0 } } }""", "difficulties.easy.bot_damage")]
    public void BadDifficultiesAreRefused(string json, string named)
    {
        var e = Assert.Throws<FormatException>(() => SurvivalSettings.FromJson(json));
        Assert.Contains(named, e.Message);
    }

    [Fact]
    public void TheCommandLinePicksEasy()
    {
        Assert.True(LaunchOptions.TryParse(["--easy"], out var easy, out _));
        Assert.Equal(Difficulty.Easy, easy.Difficulty);
        Assert.True(LaunchOptions.TryParse([], out var normal, out _));
        Assert.Equal(Difficulty.Normal, normal.Difficulty);
    }

    [Fact]
    public void AWorldKeepsItsDifficultyAndOlderSavesAreNormal()
    {
        var save = new SaveGame { Name = "w", Seed = 1, Difficulty = Difficulty.Easy };
        var read = SaveGame.Read(save.Write(), "w.json", [])!;
        Assert.Equal(Difficulty.Easy, read.Difficulty);
        var plain = new SaveGame { Name = "w", Seed = 1 }.Write();
        Assert.DoesNotContain("difficulty", plain);
        Assert.Equal(Difficulty.Normal, SaveGame.Read(plain, "w.json", [])!.Difficulty);
    }
}
