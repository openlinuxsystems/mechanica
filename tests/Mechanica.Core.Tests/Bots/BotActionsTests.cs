using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Building;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Survival;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Bots;

public sealed class BotActionsTests
{
    private readonly BotFixture _f = new();

    private SurvivalFixture S => _f.Survival;

    [Fact]
    public void Players_cannot_spawn_or_drive_bots_in_survival()
    {
        Assert.Contains("Creative", S.Refused(Me, BotActions.SpawnBot, new SpawnBotArgs(BotFixture.Grunt, [0, 0, 0])));
        var bot = _f.Spawn(BotFixture.Grunt, new Vector3(5, 0, 0));
        Assert.Contains("cannot drive", S.Refused(Me, BotActions.MoveBot, new MoveBotArgs(bot, new Vec3(1, 0, 0))));
        Assert.Contains("cannot drive", S.Refused(Me, BotActions.BotAttack, new BotAttackArgs(bot, "whip", Player: You.Peer)));
    }

    [Fact]
    public void Players_spawn_and_remove_bots_in_creative()
    {
        var f = new BotFixture(GameMode.Creative);
        var bot = f.Survival.Ok<SpawnBotResult>(Me, BotActions.SpawnBot, new SpawnBotArgs("test:grunt", [1, 0, 1])).Bot;
        Assert.Equal(new Vector3(1, 0, 1), f.Bots.Find(bot)!.Position);
        f.Survival.Ok(Me, BotActions.DespawnBot, new BotArgs(bot));
        Assert.Null(f.Bots.Find(bot));
    }

    [Fact]
    public void Spawning_checks_the_type_and_the_limit()
    {
        Assert.Contains("no bot type", S.Refused(BotFixture.Brain, BotActions.SpawnBot, new SpawnBotArgs("test:nobody", [0, 0, 0])));
        Assert.Contains("finite", S.Refused(BotFixture.Brain, BotActions.SpawnBot, new SpawnBotArgs(BotFixture.Grunt, [0, 0])));
    }

    [Fact]
    public void A_bot_walks_at_most_its_top_speed_and_faces_where_it_goes()
    {
        var bot = _f.Spawn(BotFixture.Grunt, Vector3.Zero);
        Assert.Contains("at most 5", S.Refused(BotFixture.Brain, BotActions.MoveBot, new MoveBotArgs(bot, new Vec3(1, 0, 0), 6)));
        S.Ok(BotFixture.Brain, BotActions.MoveBot, new MoveBotArgs(bot, new Vec3(1, 5, 0)));
        var state = _f.Bots.Find(bot)!;
        Assert.Equal(new Vector3(5, 0, 0), state.Desired);
        Assert.Equal(-90f, state.Yaw, 3);
        S.Ok(BotFixture.Brain, BotActions.MoveBot, new MoveBotArgs(bot));
        Assert.Equal(Vector3.Zero, state.Desired);
    }

    [Fact]
    public void A_melee_attack_needs_reach_and_waits_for_its_interval()
    {
        // You stand at (0, 0, 6).
        var bot = _f.Spawn(BotFixture.Grunt, new Vector3(0, 0, 10));
        Assert.Contains("reaches 1.5", S.Refused(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "whip", Player: You.Peer)));
        _f.Bots.Moved(bot, new Vector3(0, 0, 7.5f), false);
        var hits = S.Ok<BotAttackResult>(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "whip", Player: You.Peer)).Hits;
        Assert.Equal(("player", 15f, (int?)You.Peer), (hits.Single().Kind, hits.Single().Damage, hits.Single().Player));
        Assert.Equal(85, S.Survival.View(You.Peer)!.Health);
        Assert.Contains("not ready", S.Refused(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "whip", Player: You.Peer)));
        _f.Advance(1);
        S.Ok(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "whip", Player: You.Peer));
        Assert.Equal(70, S.Survival.View(You.Peer)!.Health);
    }

    [Fact]
    public void An_attack_names_one_target()
    {
        var bot = _f.Spawn(BotFixture.Grunt, new Vector3(0, 0, 7.5f));
        Assert.Contains("one target", S.Refused(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "whip")));
        Assert.Contains("no attack", S.Refused(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "bite", Player: You.Peer)));
        Assert.Contains("cannot attack itself", S.Refused(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "whip", Target: bot)));
    }

    [Fact]
    public void Hitting_soil_with_a_crop_destroys_the_crop()
    {
        var soil = _f.Crop(new Vector3(20, 0.125f, 0));
        var destroyed = new List<(int, int, int)>();
        _f.Bots.CropDestroyed += (c, p, b) => destroyed.Add((c, p, b));
        var bot = _f.Spawn(BotFixture.Grunt, new Vector3(21, 0, 0));
        Assert.Single(_f.Bots.Crops());
        Assert.Equal(3, S.Survival.CropValue());
        var hit = S.Ok<BotAttackResult>(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "whip", Creation: soil, Part: 1)).Hits.Single();
        Assert.Equal("crop", hit.Kind);
        Assert.Null(S.World.Creations[soil].SoilOf(1)!.Seed);
        Assert.Equal([(soil, 1, bot)], destroyed);
        Assert.Empty(_f.Bots.Crops());
        Assert.Equal(0, S.Survival.CropValue());
        // The bare soil takes part damage from the next hit.
        _f.Advance(1);
        Assert.Equal("part", S.Ok<BotAttackResult>(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "whip", Creation: soil, Part: 1)).Hits.Single().Kind);
        Assert.Equal(15, S.World.Creations[soil].PartDamage(1));
    }

    [Fact]
    public void A_ranged_attack_needs_a_clear_line_and_leaves_lasting_damage()
    {
        var bot = _f.Spawn(BotFixture.Shooter, new Vector3(0, 0, 20));
        _f.Ray = (_, _, _) => new RayHit(new Vector3(0, 1, 15));
        Assert.Contains("in the way", S.Refused(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "tape", Player: You.Peer)));
        _f.Ray = (_, _, _) => new RayHit(new Vector3(0, 1, 6.4f), Player: You.Peer);
        S.Ok(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "tape", Player: You.Peer));
        Assert.Equal(75, S.Survival.View(You.Peer)!.Health);
        _f.Advance(1);
        Assert.Equal(70, S.Survival.View(You.Peer)!.Health, 3);
        _f.Advance(1);
        Assert.Equal(65, S.Survival.View(You.Peer)!.Health, 3);
        _f.Advance(1);
        Assert.Equal(65, S.Survival.View(You.Peer)!.Health, 3);
    }

    [Fact]
    public void An_area_attack_hits_everything_round_the_bot_and_can_kill_it()
    {
        var soil = _f.Crop(new Vector3(1, 0.125f, 6));
        // Me at (0,0,3) is out of the blast; You at (0,0,6) is right beside it.
        var bot = _f.Spawn(BotFixture.Bomber, new Vector3(0, 0, 7.5f));
        var hits = S.Ok<BotAttackResult>(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "boom")).Hits;
        Assert.Contains(hits, h => h is { Kind: "player", Player: 3 } && h.Damage > 20);
        Assert.DoesNotContain(hits, h => h.Player == Me.Peer);
        Assert.Contains(hits, h => h is { Kind: "crop", Creation: var c } && c == soil);
        Assert.True(_f.Bots.Find(bot)!.Dead);
        Assert.Contains("takes no target", S.Refused(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(_f.Spawn(BotFixture.Bomber, Vector3.Zero), "boom", Player: Me.Peer)));
    }

    [Fact]
    public void A_dead_bot_drops_its_items_and_its_body_goes()
    {
        var bot = _f.Spawn(BotFixture.Grunt, new Vector3(4, 0, 0));
        var removed = new List<int>();
        _f.Bots.Removed += removed.Add;
        Assert.Equal(20, _f.Bots.DamageBot(bot, 40, "test", melee: false));
        Assert.Equal(0, _f.Bots.DamageBot(bot, 40, "test", melee: false));
        Assert.True(_f.Bots.Find(bot)!.Dead);
        var drop = S.Survival.Drops.Values.Single();
        Assert.Equal(new ItemStack(Scrap, 2), drop.Items.Single());
        Assert.Contains("dead", S.Refused(BotFixture.Brain, BotActions.MoveBot, new MoveBotArgs(bot, new Vec3(1, 0, 0))));
        _f.Advance(3);
        Assert.Equal([bot], removed);
        Assert.Null(_f.Bots.Find(bot));
    }

    [Fact]
    public void Weapons_and_splash_hurt_bots()
    {
        var bot = _f.Spawn(BotFixture.Grunt, new Vector3(0, 0, -5));
        S.Pockets(Me).Add(Blaster, 1);
        S.Pockets(Me).Add(Crop, 3);
        S.World.Ray = (_, _) => new RayHit(new Vector3(0, 0.6f, -5), Bot: bot);
        var hit = S.Ok<FireResult>(Me, SurvivalActions.Fire, new FireArgs(S.SlotOf(Me, Blaster), new Vec3(0, 0, -1))).Hits.Single();
        Assert.Equal(("bot", (int?)bot, 20f), (hit.Kind, hit.Bot, hit.Damage));
        Assert.Equal(40, _f.Bots.Find(bot)!.Health);
        var hits = S.Survival.Splash(Me, new Vector3(0, 0.6f, -4), 4, 40, 0);
        Assert.Contains(hits, h => h.Kind == "bot" && h.Bot == bot);
        Assert.True(_f.Bots.Find(bot)!.Health < 40);
    }

    [Fact]
    public void The_sledgehammer_hits_bots_but_not_melee_immune_ones()
    {
        var grunt = _f.Spawn(BotFixture.Grunt, new Vector3(0, 0, 1.5f));
        var tank = _f.Spawn(BotFixture.Tank, new Vector3(1, 0, 2));
        var result = S.Ok<SledgeResult>(Me, ToolActions.Sledge, new SledgeArgs(Bot: grunt));
        Assert.Equal(45, result.Health);
        S.World.Now += 5;
        Assert.Equal(1500, S.Ok<SledgeResult>(Me, ToolActions.Sledge, new SledgeArgs(Bot: tank)).Health);
        Assert.Contains("no bot", S.Refused(Me, ToolActions.Sledge, new SledgeArgs(Bot: 99)));
    }

    [Fact]
    public void Queries_list_the_bots()
    {
        var bot = _f.Spawn(BotFixture.Critter, new Vector3(2, 0, 2));
        var views = S.Ok<List<BotView>>(Me, BotActions.GetBots, new NoArgs());
        Assert.Equal(new BotView(bot, BotFixture.Critter, new Vec3(2, 0, 2), 0, 30, 30, false, null), views.Single());
        Assert.Equal(bot, S.Ok<BotView>(Me, BotActions.GetBot, new BotArgs(bot)).Id);
    }
}
