using System.Numerics;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Bots;

public sealed class PoiGuardsTests
{
    private static readonly Vector3 PostAt = new(200, 0, 0);

    private static (BotFixture F, PoiGuards Guards) Make(GameMode mode = GameMode.Survival, string bot = BotFixture.Grunt)
    {
        var f = new BotFixture(mode);
        var guards = new PoiGuards([new GuardPost("test:ruin", bot, PostAt, 90)], f.Bots, f.Survival.Survival);
        return (f, guards);
    }

    private static void MoveBoth(BotFixture f, Vector3 at)
    {
        f.Survival.World.Feet[Me.Peer] = at;
        f.Survival.World.Feet[You.Peer] = at;
    }

    [Fact]
    public void A_guard_appears_when_a_player_comes_near_and_leaves_when_everyone_is_gone()
    {
        var (f, guards) = Make();
        guards.Tick();
        Assert.Empty(f.Bots.Bots); // both players are 200 m away
        f.Survival.World.Feet[Me.Peer] = new Vector3(150, 0, 0);
        guards.Tick();
        var bot = Assert.Single(f.Bots.Bots.Values);
        Assert.Equal(BotFixture.Grunt, bot.Type.QualifiedId);
        Assert.Equal(PostAt.X, bot.Position.X);
        Assert.Equal(90, bot.Yaw);
        Assert.Null(bot.Raid);
        Assert.Equal([bot.Id], guards.OnDuty);
        // Still near enough to keep it (within the sleep distance), but not to wake it.
        f.Survival.World.Feet[Me.Peer] = new Vector3(110, 0, 0);
        guards.Tick();
        Assert.Single(f.Bots.Bots);
        f.Survival.World.Feet[Me.Peer] = new Vector3(100, 0, 0);
        guards.Tick();
        Assert.Empty(f.Bots.Bots);
        Assert.Equal([null], guards.OnDuty);
        // Leaving is not dying: it is back as soon as someone returns.
        MoveBoth(f, new Vector3(200, 0, 40));
        guards.Tick();
        Assert.Single(f.Bots.Bots);
    }

    [Fact]
    public void A_killed_guard_comes_back_only_after_the_respawn_time()
    {
        var (f, guards) = Make();
        MoveBoth(f, new Vector3(190, 0, 0));
        guards.Tick();
        var id = Assert.Single(f.Bots.Bots.Keys);
        f.Bots.DamageBot(id, 1000, "test", melee: false);
        guards.Tick();
        f.Advance(5); // the body goes
        guards.Tick();
        Assert.Empty(f.Bots.Bots);
        f.Advance(f.Bots.Settings.GuardRespawn - 10);
        guards.Tick();
        Assert.Empty(f.Bots.Bots);
        f.Advance(10);
        guards.Tick();
        Assert.NotEqual(id, Assert.Single(f.Bots.Bots.Keys));
    }

    [Fact]
    public void Creative_worlds_and_unknown_bots_field_no_guards()
    {
        var (creative, guards) = Make(GameMode.Creative);
        MoveBoth(creative, PostAt);
        guards.Tick();
        Assert.Empty(creative.Bots.Bots);

        var (f, ghosts) = Make(bot: "test:ghost");
        MoveBoth(f, PostAt);
        ghosts.Tick();
        Assert.Empty(f.Bots.Bots);
    }

    [Fact]
    public void Dead_players_wake_no_guards()
    {
        var (f, guards) = Make();
        f.Survival.World.Feet[Me.Peer] = PostAt;
        f.Survival.Survival.Damage(Me.Peer, 1000, "test");
        guards.Tick();
        Assert.Empty(f.Bots.Bots);
    }

    [Fact]
    public void The_vanilla_world_guards_its_pois_with_real_bots()
    {
        var mods = ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"));
        var book = BotBook.Load(mods);
        var world = new WorldGenerator(WorldGenData.Load(mods), 1234, 4096f);
        var posts = PoiGuards.PostsOf(world);
        Assert.NotEmpty(posts);
        Assert.Contains(posts, p => p.Poi == "vanilla:warehouse");
        Assert.All(posts, p => Assert.True(book.FindBot(p.Bot) is not null, $"{p.Poi} posts an unknown bot '{p.Bot}'."));
        // A guard stands on its POI's pad, not buried in the ground.
        Assert.All(posts, p => Assert.True(p.Feet.Y >= world.HeightAt(p.Feet.X, p.Feet.Z) - 0.5f, $"{p.Poi}'s {p.Bot} is under the ground."));
    }
}
