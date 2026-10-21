using System.Numerics;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Bots;

/// <summary>Bot-free zones (a POI's <c>bot_free_radius</c>): no wild bot, guard or raider appears inside one.</summary>
public sealed class BotFreeZonesTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    // The vanilla world at the game's size, with the test harness's seed.
    private static readonly Lazy<WorldGenerator> VanillaWorld = new(() => new WorldGenerator(WorldGenData.Load(Vanilla.Value), 1234, 12032f, 9024f));

    private static void MoveBoth(BotFixture f, Vector3 at)
    {
        f.Survival.World.Feet[Me.Peer] = at;
        f.Survival.World.Feet[You.Peer] = at;
    }

    [Fact]
    public void Points_inside_are_pushed_straight_out_past_the_edge()
    {
        var zones = new BotFreeZones([new BotFreeZone(new Vector2(10, 0), 50)]);

        Assert.True(zones.Contains(40, 0));
        Assert.False(zones.Contains(61, 0));
        Assert.Equal(new Vector2(70, 0), zones.PushOut(new Vector2(70, 0), Vector2.Zero));
        var pushed = zones.PushOut(new Vector2(10, 20), Vector2.Zero);
        Assert.Equal(10, pushed.X, 3);
        Assert.Equal(51, pushed.Y, 3);
        // at the very centre: away from the farm's side
        Assert.Equal(-41, zones.PushOut(new Vector2(10, 0), new Vector2(20, 0)).X, 3);
    }

    [Fact]
    public void Wild_bots_never_appear_in_a_zone()
    {
        var f = new BotFixture();
        MoveBoth(f, new Vector3(-100, 1, 0));
        var centre = new Vector2(-100, 0);
        f.Bots.BotFree = new BotFreeZones([new BotFreeZone(centre, 45)]);
        var type = BotBook.From(f.Survival.Catalog, ("test", new BotDefinition
        {
            Id = "grazer", Name = "Grazer", Health = 30, Speed = 2, Passive = true,
            Wild = System.Text.Json.JsonSerializer.Deserialize<BotWildDefinition>("""{ "habitat": "land", "count": 12, "herd": [3, 4], "clear_radius": 2 }""",
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower }),
        })).All;
        var wild = new WildSpawns(type, f.Bots, f.Survival.Survival, 0f, (x, z) => 1, seed: 7);

        for (var i = 0; i < 5; i++)
        {
            wild.Tick();
            f.Advance(30);
        }

        Assert.NotEmpty(f.Bots.Bots);
        Assert.All(f.Bots.Bots.Values, b => Assert.True(Vector2.Distance(new Vector2(b.Position.X, b.Position.Z), centre) >= 45, $"outside the zone ({b.Position})"));
    }

    [Fact]
    public void A_wild_bot_whose_whole_reach_is_a_zone_never_appears()
    {
        var f = new BotFixture();
        MoveBoth(f, new Vector3(40, 1, 0));
        f.Bots.BotFree = new BotFreeZones([new BotFreeZone(new Vector2(40, 0), 250)]);
        var wild = new WildSpawns(f.Book.All.Where(t => t.QualifiedId == BotFixture.Swimmer), f.Bots, f.Survival.Survival, 0f,
            (x, z) => x > 50 ? -5 : 1, seed: 7);

        wild.Tick();

        Assert.Empty(f.Bots.Bots);
    }

    [Fact]
    public void A_guard_post_in_a_zone_never_fields_its_guard()
    {
        var f = new BotFixture();
        f.Bots.BotFree = new BotFreeZones([new BotFreeZone(new Vector2(200, 0), 10)]);
        var guards = new PoiGuards([new GuardPost("test:ruin", BotFixture.Grunt, new Vector3(200, 0, 0), 0),
            new GuardPost("test:ruin", BotFixture.Grunt, new Vector3(200, 0, 30), 0)], f.Bots, f.Survival.Survival);
        MoveBoth(f, new Vector3(200, 0, 10));

        guards.Tick();

        var guard = Assert.Single(f.Bots.Bots.Values);
        Assert.Equal(30, guard.Position.Z);
    }

    [Fact]
    public void Raiders_of_a_farm_in_a_zone_arrive_from_outside_it()
    {
        var f = new BotFixture();
        f.Bots.BotFree = new BotFreeZones([new BotFreeZone(new Vector2(0, 40), 100)]);
        f.Crop(new Vector3(0, 0.125f, 40));
        f.Survival.Ok(BotFixture.Brain, RaidDirector.StartRaid, new StartRaidArgs(2));

        f.Advance(5);

        Assert.NotEmpty(f.Bots.Bots);
        Assert.Equal(f.Director.Current.Bots, f.Bots.Bots.Count);
        Assert.All(f.Bots.Bots.Values, b => Assert.InRange(Vector2.Distance(new Vector2(b.Position.X, b.Position.Z), new Vector2(0, 40)), 100.5f, 101.5f));
    }

    [Fact]
    public void The_vanilla_crash_site_keeps_bots_250_m_away()
    {
        var world = VanillaWorld.Value;
        var site = world.Pois.Single(p => p.Poi.QualifiedId == "vanilla:crash_site");

        var zone = Assert.Single(BotFreeZones.Of(world).Zones);

        Assert.Equal(site.Centre, zone.Centre);
        Assert.Equal(250f, zone.Radius);
    }
}
