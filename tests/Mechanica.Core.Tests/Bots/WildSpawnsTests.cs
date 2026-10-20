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
        return (f, new WildSpawns(f.Book.All.Where(t => t.QualifiedId == BotFixture.Swimmer), f.Bots, f.Survival.Survival, 0f, HeightAt, seed: 7));
    }

    // A land animal with the given wild keys (JSON), and the wild spawns of just that type.
    private static (BotFixture F, WildSpawns Wild) Herd(string wild, Func<float, float, string?>? biomeAt = null,
        Func<Vector2, float, IEnumerable<(string Object, Vector2 At)>>? objectsNear = null)
    {
        var f = new BotFixture();
        MoveBoth(f, new Vector3(-100, 1, 0));
        var type = BotBook.From(f.Survival.Catalog, ("test", new BotDefinition
        {
            Id = "grazer", Name = "Grazer", Health = 30, Speed = 2, Passive = true,
            Wild = System.Text.Json.JsonSerializer.Deserialize<BotWildDefinition>(wild,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower }),
        })).All;
        return (f, new WildSpawns(type, f.Bots, f.Survival.Survival, 0f, HeightAt, seed: 7, biomeAt, objectsNear));
    }

    private static string? NorthIsMeadow(float x, float z) => z > 0 ? "meadow" : "desert";

    [Fact]
    public void A_herd_appears_together_up_to_the_count()
    {
        var (f, wild) = Herd("""{ "habitat": "land", "count": 3, "herd": [3, 3], "clear_radius": 2 }""");

        wild.Tick();

        var bots = f.Bots.Bots.Values.ToList();
        Assert.Equal(3, bots.Count);
        Assert.All(bots, a => Assert.All(bots, b => Assert.True(Vector3.Distance(a.Position, b.Position) <= 10.1f, "the herd stays together")));
    }

    [Fact]
    public void No_herd_smaller_than_its_least_fills_up_the_count()
    {
        var (f, wild) = Herd("""{ "habitat": "land", "count": 4, "herd": [3, 3], "clear_radius": 2 }""");

        for (var i = 0; i < 5; i++)
        {
            wild.Tick();
            f.Advance(10);
        }

        Assert.Equal(3, f.Bots.Bots.Count);
    }

    [Theory]
    [InlineData(4, 2, 4)]
    [InlineData(6, 2, 4)]
    [InlineData(5, 2, 3)]
    public void Herds_fill_the_count_with_none_left_alone(int count, int min, int max)
    {
        var (f, wild) = Herd($$"""{ "habitat": "land", "count": {{count}}, "herd": [{{min}}, {{max}}], "clear_radius": 2 }""");

        for (var i = 0; i < 5; i++)
        {
            wild.Tick();
            f.Advance(10);
        }

        Assert.Equal(count, f.Bots.Bots.Count);
    }

    [Fact]
    public void A_bot_with_biomes_appears_only_in_them()
    {
        var (f, wild) = Herd("""{ "habitat": "land", "count": 6, "biomes": ["meadow"], "clear_radius": 2 }""", NorthIsMeadow);

        wild.Tick();

        Assert.Equal(6, f.Bots.Bots.Count);
        Assert.All(f.Bots.Bots.Values, b => Assert.True(b.Position.Z > 0, $"in the meadow ({b.Position})"));
    }

    [Fact]
    public void A_bot_with_biomes_never_appears_where_biomes_are_unknown()
    {
        var (f, wild) = Herd("""{ "habitat": "land", "count": 2, "biomes": ["meadow"] }""");

        wild.Tick();

        Assert.Empty(f.Bots.Bots);
    }

    [Fact]
    public void A_bot_appears_round_its_near_objects()
    {
        var farm = new Vector2(-100, 50);
        IEnumerable<(string, Vector2)> Objects(Vector2 at, float radius) => [("barn", new Vector2(-150, -40)), ("farm", farm)];
        var (f, wild) = Herd("""{ "habitat": "land", "count": 4, "clear_radius": 2, "near": { "objects": ["farm"], "radius": 10, "share": 1 } }""",
            objectsNear: Objects);

        wild.Tick();

        Assert.Equal(4, f.Bots.Bots.Count);
        Assert.All(f.Bots.Bots.Values, b => Assert.InRange(Vector2.Distance(new Vector2(b.Position.X, b.Position.Z), farm), 3.9f, 10.1f));
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
    public void A_kept_swimmer_stays_when_the_players_leave_and_more_wild_ones_come()
    {
        var (f, wild) = Make();
        wild.Tick();
        var kept = wild.Bots.First();
        f.Bots.Keep(kept);

        MoveBoth(f, new Vector3(-400, 1, 0));
        f.Advance(10);
        wild.Tick();

        Assert.True(f.Bots.Find(kept) is { Dead: false, Kept: true }, "the kept one is still there");
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
