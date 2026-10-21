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
    public void The_vanilla_crash_site_keeps_bots_250_m_away_and_Workshops_hostile_ones_100_m()
    {
        var world = VanillaWorld.Value;
        var zones = BotFreeZones.Of(world).Zones;
        var site = world.Pois.Single(p => p.Poi.QualifiedId == "vanilla:crash_site");

        Assert.Contains(new BotFreeZone(site.Centre, 250f), zones);
        var workshops = world.Pois.Where(p => p.Poi.Prefab.Name == "Workshop").ToList();
        Assert.NotEmpty(workshops);
        Assert.All(workshops, w => Assert.Contains(new BotFreeZone(w.Centre, 100f, HostileOnly: true), zones));
        Assert.All(workshops, w => Assert.Contains(new BotFreeZone(w.Centre, w.Radius), zones));
    }

    [Fact]
    public void Vanilla_Spoolers_are_rare_in_the_wild()
    {
        var book = BotBook.Load(Vanilla.Value);
        var spoolers = book.All.Where(b => b.QualifiedId.StartsWith("vanilla:spooler_", StringComparison.Ordinal) && b.Wild is not null).ToList();

        Assert.NotEmpty(spoolers);
        Assert.All(spoolers, s => Assert.True(s.Wild!.Chance <= 0.1f && s.Wild.Count == 1, $"{s.QualifiedId} is rare"));
    }

    // A wild land bot with the given wild keys round both players at (-100, 0), passive or not.
    private static (BotFixture F, WildSpawns Wild) Wild(string wild, bool passive)
    {
        var f = new BotFixture();
        MoveBoth(f, new Vector3(-100, 1, 0));
        var type = BotBook.From(f.Survival.Catalog, ("test", new BotDefinition
        {
            Id = "roamer", Name = "Roamer", Health = 30, Speed = 2, Passive = passive,
            Wild = System.Text.Json.JsonSerializer.Deserialize<BotWildDefinition>(wild,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower }),
        })).All;
        return (f, new WildSpawns(type, f.Bots, f.Survival.Survival, 0f, (x, z) => 1, seed: 7));
    }

    [Theory]
    [InlineData(true, 4)]
    [InlineData(false, 0)]
    public void A_hostile_free_zone_keeps_out_only_hostile_bots(bool passive, int expected)
    {
        var (f, wild) = Wild("""{ "habitat": "land", "count": 4, "clear_radius": 2 }""", passive);
        f.Bots.BotFree = new BotFreeZones([new BotFreeZone(new Vector2(-100, 0), 250, HostileOnly: true)]);

        wild.Tick();

        Assert.Equal(expected, f.Bots.Bots.Count);
    }

    [Fact]
    public void A_rare_wild_bot_comes_on_only_some_tries()
    {
        var (f, wild) = Wild("""{ "habitat": "land", "count": 1, "clear_radius": 2, "interval": 10, "despawn_distance": 71, "chance": 0.2 }""", passive: false);
        var tries = 0;
        var spawned = 0;
        for (var i = 0; i < 200; i++)
        {
            wild.Tick();
            tries++;
            if (f.Bots.Bots.Count > 0)
            {
                spawned++;
                foreach (var id in f.Bots.Bots.Keys.ToList())
                    f.Bots.Remove(id);
            }
            f.Advance(10);
        }

        // Both players stand together and each gets a try: 1 - 0.8² = 36% of the rounds.
        Assert.InRange(spawned, tries / 4, tries / 2);
    }

    [Fact]
    public void A_wild_chance_outside_0_to_1_is_refused()
    {
        var diagnostics = BotBook.Validate(new BotDefinition
        {
            Id = "roamer", Name = "Roamer", Health = 30, Speed = 2,
            Wild = new BotWildDefinition { Habitat = "land", Count = 1, Chance = 0 },
        }, "test", null, new BotFixture().Survival.Catalog, "test:roamer");

        Assert.Contains(diagnostics, d => d.JsonPath == "$.wild.chance");
    }
}
