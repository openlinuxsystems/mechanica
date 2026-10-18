using Mechanica.Core.Net;
using Mechanica.Core.Reports;
using Mechanica.Core.Saves;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Survival;

public class FriendlyFireTests
{
    [Fact]
    public void ItIsOffUnlessTheHostTurnsItOn()
    {
        var settings = SurvivalSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "survival.json")));
        var f = new SurvivalFixture(settings);
        Assert.False(new SurvivalActions(f.Catalog, settings, f.Inventories, f.World).FriendlyFire);
        Assert.False(new NewWorldParams().FriendlyFire);
        Assert.True(LaunchOptions.TryParse([], out var plain, out _));
        Assert.False(plain.FriendlyFire);
        Assert.True(LaunchOptions.TryParse(["--friendly-fire"], out var on, out _));
        Assert.True(on.FriendlyFire);
    }

    [Fact]
    public void AWorldKeepsItAndOlderSavesHaveItOff()
    {
        var save = new SaveGame { Name = "w", Seed = 1, FriendlyFire = true };
        Assert.True(SaveGame.Read(save.Write(), "w.json", [])!.FriendlyFire);
        var plain = new SaveGame { Name = "w", Seed = 1 }.Write();
        Assert.DoesNotContain("friendly_fire", plain);
        Assert.False(SaveGame.Read(plain, "w.json", [])!.FriendlyFire);
    }

    [Fact]
    public void AReportsReplayFlagsKeepIt()
    {
        Assert.Contains("--friendly-fire ", ReportMarkdown.ReplayFlags(new ReportContext { Seed = 1, Mode = "survival", FriendlyFire = true }));
        Assert.DoesNotContain("--friendly-fire", ReportMarkdown.ReplayFlags(new ReportContext { Seed = 1, Mode = "survival" }));
    }
}
