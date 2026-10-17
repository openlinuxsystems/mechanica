using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Survival;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Bots;

public sealed class RaidTests
{
    [Fact]
    public void Levels_and_budgets_follow_the_table_and_the_player_count()
    {
        var f = new BotFixture();
        var table = f.Raids;
        Assert.Null(table.LevelFor(2.9f));
        Assert.Equal(1, table.LevelFor(3)!.Level);
        Assert.Equal(2, table.LevelFor(100)!.Level);
        Assert.Equal([1f, 1.3f, 1.6f, 2f], [.. Enumerable.Range(1, 4).Select(table.MultiplierFor)]);
        Assert.Equal(2f, table.MultiplierFor(9));
        var level1 = table.Levels[0];
        Assert.Equal([4, 5, 6, 8], [.. Enumerable.Range(1, 4).Select(p => table.BudgetFor(level1, 3, p))]);
        Assert.Equal(Enumerable.Repeat(BotFixture.Grunt, 8), table.Plan(3, 4)!.Bots);
    }

    [Fact]
    public void A_bigger_budget_sends_more_of_the_same_mix()
    {
        var f = new BotFixture();
        var level2 = f.Raids.Levels[1];
        Assert.Equal(8, level2.MixCost);
        Assert.Equal([BotFixture.Grunt, BotFixture.Grunt, BotFixture.Shooter, BotFixture.Shooter, BotFixture.Shooter], RaidTable.Compose(level2, 8));
        // 13: the mix (8), then a grunt, a shooter, a grunt and a grunt; the last shooter no longer fits.
        var bigger = RaidTable.Compose(level2, 13);
        Assert.Equal(5, bigger.Count(b => b == BotFixture.Grunt));
        Assert.Equal(4, bigger.Count(b => b == BotFixture.Shooter));
    }

    [Fact]
    public void The_open_ended_level_grows_with_crop_value()
    {
        var f = new BotFixture(raids: new RaidTableDefinition
        {
            Levels = [new RaidLevelDefinition { Level = 7, MinValue = 401, Budget = 36, BudgetPerValue = 0.1f, Bots = [new RaidBotDefinition { Bot = BotFixture.Tank, Count = 2 }] }],
        });
        var level = f.Raids.Levels.Single();
        Assert.Equal(36, f.Raids.BudgetFor(level, 401, 1));
        Assert.Equal(46, f.Raids.BudgetFor(level, 501, 1));
        Assert.Equal(92, f.Raids.BudgetFor(level, 501, 4));
        Assert.Equal(18, f.Raids.Plan(501, 4)!.Bots.Count);
    }

    [Fact]
    public void Bad_tables_are_refused()
    {
        var f = new BotFixture();
        string Errors(RaidTableDefinition t) => string.Join("\n", RaidTable.Validate(t, f.Book, "raids").Select(d => d.ToString()));
        Assert.Contains("more than the budget", Errors(new RaidTableDefinition { Levels = [Level(1, 0, 3, (BotFixture.Grunt, 4))] }));
        Assert.Contains("must rise", Errors(new RaidTableDefinition { Levels = [Level(2, 10, 4, (BotFixture.Grunt, 1)), Level(1, 20, 4, (BotFixture.Grunt, 1))] }));
        Assert.Contains("cannot raid", Errors(new RaidTableDefinition { Levels = [Level(1, 0, 4, (BotFixture.Critter, 1))] }));
        Assert.Contains("no bot", Errors(new RaidTableDefinition { Levels = [Level(1, 0, 4, ("test:ghost", 1))] }));
        Assert.Contains("greater than 0", Errors(new RaidTableDefinition { PlayerMultipliers = [1, 0] }));
    }

    [Fact]
    public void Crops_set_off_a_raid_that_warns_arrives_and_ends()
    {
        var f = new BotFixture();
        var views = new List<RaidView>();
        f.Director.Changed += views.Add;
        f.Advance(0);
        f.Advance(11);
        Assert.Empty(views); // nothing planted
        var soil = f.Crop(new Vector3(50, 0.125f, 0));
        f.Advance(5);
        Assert.Empty(views); // not time for a check yet
        f.Advance(5);
        var raid = Assert.Single(views);
        Assert.Equal((RaidPhases.Warning, 1, 5, 2), (raid.Phase, raid.Level, raid.Budget, raid.Players));
        Assert.Equal(new Vec3(50, 0.125f, 0), raid.Target);
        Assert.Empty(f.Bots.Bots);

        f.Advance(5);
        Assert.Equal(RaidPhases.Attacking, f.Director.Current.Phase);
        Assert.Equal(5, f.Bots.Bots.Count);
        foreach (var bot in f.Bots.Bots.Values)
        {
            Assert.Equal(raid.Id, bot.Raid);
            Assert.Equal(30, Vector3.Distance(bot.Position with { Y = 0 }, new Vector3(50, 0, 0)), 2);
        }

        foreach (var id in f.Bots.Bots.Keys.ToList())
            f.Bots.DamageBot(id, 100, "test", melee: false);
        f.Advance(0.1);
        Assert.Equal(RaidPhases.None, f.Director.Current.Phase);
        Assert.Equal(RaidPhases.None, views[^1].Phase);
        f.Advance(50);
        Assert.Equal(RaidPhases.None, f.Director.Current.Phase); // cooling down
        f.Advance(60);
        Assert.Equal(RaidPhases.Warning, f.Director.Current.Phase);
        Assert.NotNull(f.Survival.World.Creations[soil]);
    }

    [Fact]
    public void Raiders_still_standing_leave_when_the_raid_times_out()
    {
        var f = new BotFixture();
        f.Crop(new Vector3(0, 0.125f, 40));
        f.Survival.Ok(BotFixture.Brain, RaidDirector.StartRaid, new StartRaidArgs(2));
        Assert.Equal(2, f.Director.Current.Level);
        f.Advance(5);
        Assert.Equal(f.Director.Current.Bots, f.Bots.Bots.Count);
        f.Advance(61);
        Assert.Equal(RaidPhases.None, f.Director.Current.Phase);
        Assert.Empty(f.Bots.Bots);
    }

    [Fact]
    public void Starting_a_raid_by_hand_is_checked()
    {
        var f = new BotFixture();
        Assert.Contains("Creative", f.Survival.Refused(Me, RaidDirector.StartRaid, new StartRaidArgs()));
        Assert.Contains("Nothing is planted", f.Survival.Refused(BotFixture.Brain, RaidDirector.StartRaid, new StartRaidArgs()));
        f.Crop(new Vector3(0, 0.125f, 40));
        Assert.Contains("no raid level 9", f.Survival.Refused(BotFixture.Brain, RaidDirector.StartRaid, new StartRaidArgs(9)));
        var started = f.Survival.Ok<RaidView>(BotFixture.Brain, RaidDirector.StartRaid, new StartRaidArgs());
        Assert.Equal(1, started.Level);
        Assert.Contains("already on", f.Survival.Refused(BotFixture.Brain, RaidDirector.StartRaid, new StartRaidArgs()));
        Assert.Equal(started with { Composition = [] }, f.Survival.Ok<RaidView>(Me, RaidDirector.GetRaid, new NoArgs()) with { Composition = [] });
    }

    [Fact]
    public void Creative_worlds_have_no_raids_unless_started_by_hand()
    {
        var f = new BotFixture(GameMode.Creative);
        f.Crop(new Vector3(0, 0.125f, 40));
        f.Advance(0);
        f.Advance(30);
        Assert.Equal(RaidPhases.None, f.Director.Current.Phase);
        f.Survival.Ok(Me, RaidDirector.StartRaid, new StartRaidArgs());
        Assert.Equal(RaidPhases.Warning, f.Director.Current.Phase);
    }

    private static RaidLevelDefinition Level(int level, float min, int budget, params (string Bot, int Count)[] bots) => new()
    {
        Level = level, MinValue = min, Budget = budget,
        Bots = [.. bots.Select(b => new RaidBotDefinition { Bot = b.Bot, Count = b.Count })],
    };
}
