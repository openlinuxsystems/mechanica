using System.Numerics;
using Mechanica.Core.Bots;
using Mechanica.Core.Survival;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Bots;

public sealed class WildSpawnsTests
{
    // Dry land west of x = 50, a 5 m deep sea east of it.
    private static float HeightAt(float x, float z) => x > 50 ? -5 : 1;

    private static (BotFixture F, WildSpawns Wild) Make(GameMode mode = GameMode.Survival)
    {
        var f = new BotFixture(mode);
        MoveBoth(f, new Vector3(40, 1, 0));
        return (f, new WildSpawns(f.Book.All, f.Bots, f.Survival.Survival, 0f, HeightAt, seed: 7));
    }

    private static void MoveBoth(BotFixture f, Vector3 at)
    {
        f.Survival.World.Feet[Me.Peer] = at;
        f.Survival.World.Feet[You.Peer] = at;
    }

    [Fact]
    public void Swimmers_appear_in_deep_open_water_near_the_players()
    {
        var (f, wild) = Make();

        wild.Tick();

        var bots = f.Bots.Bots.Values.ToList();
        Assert.Equal(2, bots.Count); // two near each player, and both players stand together
        Assert.All(bots, b => Assert.Equal(BotFixture.Swimmer, b.Type.QualifiedId));
        Assert.All(bots, b => Assert.True(b.Position.X > 55, $"in the sea with open water round it ({b.Position})"));
        Assert.All(bots, b => Assert.InRange(new Vector2(b.Position.X - 40, b.Position.Z).Length(), 25, 60));
        Assert.All(bots, b => Assert.Equal(-4.95f, b.Position.Y, 3));
        Assert.Equal(bots.Select(b => b.Id).Order(), wild.Bots.Order());
    }

    [Fact]
    public void A_killed_swimmer_is_replaced_only_after_the_interval()
    {
        var (f, wild) = Make();
        wild.Tick();
        var victim = f.Bots.Bots.Keys.First();
        f.Bots.DamageBot(victim, 1000, "test", melee: true);

        wild.Tick();
        Assert.Single(wild.Bots);
        f.Advance(10);
        wild.Tick();
        Assert.Equal(2, wild.Bots.Count);
        Assert.DoesNotContain(victim, wild.Bots);
    }

    [Fact]
    public void Swimmers_go_when_every_player_has_left_them_behind()
    {
        var (f, wild) = Make();
        wild.Tick();
        var first = wild.Bots.ToList();

        MoveBoth(f, new Vector3(-400, 1, 0)); // far inland: no water near enough for more
        f.Advance(10);
        wild.Tick();

        Assert.All(first, id => Assert.Null(f.Bots.Find(id)));
        Assert.Empty(wild.Bots);
    }

    [Fact]
    public void Nothing_lives_in_the_wild_in_Creative()
    {
        var (f, wild) = Make(GameMode.Creative);

        wild.Tick();

        Assert.Empty(f.Bots.Bots);
    }
}
