using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Survival;
using Mechanica.Core.World;
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
    public void A_swimmer_swims_in_water_and_clients_are_told_when_it_starts_and_stops()
    {
        var swimmer = _f.Spawn(BotFixture.Swimmer, new Vector3(0, 0, 20));
        var walker = _f.Spawn(BotFixture.Grunt, new Vector3(4, 0, 20));
        var changed = new List<(int Bot, bool Swimming)>();
        _f.Bots.Changed += bot => changed.Add((bot.Id, bot.View.Swimming));

        _f.Bots.Moved(swimmer, new Vector3(0, -1, 20), true);
        _f.Bots.Moved(swimmer, new Vector3(0, -0.9f, 20), true);
        _f.Bots.Moved(walker, new Vector3(4, -1, 20), true);

        Assert.True(_f.Bots.Bots[swimmer].Swimming);
        Assert.Equal(6f, _f.Bots.Bots[swimmer].TopSpeed);
        Assert.False(_f.Bots.Bots[walker].Swimming);
        Assert.Equal([(swimmer, true)], changed);
        _f.Bots.Moved(swimmer, new Vector3(0, 0, 21), false);
        Assert.Equal([(swimmer, true), (swimmer, false)], changed);
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
    public void Attacking_or_being_hurt_engages_a_bot_until_it_calms_down()
    {
        // You stand at (0, 0, 6).
        var bot = _f.Spawn(BotFixture.Grunt, new Vector3(0, 0, 7.5f));
        var state = _f.Bots.Find(bot)!;
        var changes = 0;
        _f.Bots.Changed += b => changes += b.Id == bot ? 1 : 0;
        Assert.False(state.View.Engaged);

        S.Ok(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "whip", Player: You.Peer));
        Assert.True(state.View.Engaged);
        Assert.Equal(1, changes);
        // Staying engaged sends nothing new; the engagement just lasts longer.
        _f.Advance(3);
        _f.Bots.Engage(bot);
        _f.Advance(3);
        Assert.True(state.Engaged);
        Assert.Equal(1, changes);
        _f.Advance(1.5);
        Assert.False(state.View.Engaged);
        Assert.Equal(2, changes);

        // A hit engages it, in the same change that carries its new health.
        _f.Bots.DamageBot(bot, 10, "test", melee: false);
        Assert.True(state.Engaged);
        Assert.Equal(3, changes);
        // A dead bot is never engaged.
        _f.Bots.DamageBot(bot, 100, "test", melee: false);
        Assert.False(state.View.Engaged);
        _f.Bots.Engage(bot);
        Assert.False(state.Engaged);
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
    public void Bots_never_hurt_a_world_owned_creation()
    {
        var soil = _f.Crop(new Vector3(20, 0.125f, 0));
        var owned = S.World.Creations[soil] = Creations.WorldOwnedCreations.Owned(S.World.Creations[soil]);
        var bot = _f.Spawn(BotFixture.Grunt, new Vector3(21, 0, 0));
        Assert.Contains("belongs to the world", S.Refused(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "whip", Creation: soil, Part: 1)));

        // A blast beside it passes it by.
        var bomber = _f.Spawn(BotFixture.Bomber, new Vector3(20, 0, 1));
        var hits = S.Ok<BotAttackResult>(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bomber, "boom")).Hits;
        Assert.DoesNotContain(hits, h => h.Creation == soil);
        Assert.NotNull(owned.SoilOf(1)!.Seed);
        Assert.Equal(0, owned.PartDamage(1));
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
        S.World.Ray = (_, _) => new RayHit(new Vector3(0, 0.6f, -5), Bot: bot);
        var hit = S.Survival.Hitscan(Me, new Vector3(0, 1.6f, 0), new Vector3(0, 0, -1), 30, 20, 0)!;
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
        Assert.Equal(40, result.Health);
        // The hit knocks it back along the swing, with a little lift, and the push slows away.
        var knock = _f.Bots.Bots[grunt].Knock;
        Assert.Equal(new SledgeSettings().MaxKnockbackSpeed, new Vector3(knock.X, 0, knock.Z).Length(), 3);
        var away = _f.Bots.Bots[grunt].Position - S.World.Feet[Me.Peer];
        Assert.True(knock.Y > 0 && Vector3.Dot(knock with { Y = 0 }, away with { Y = 0 }) > 0, $"up and away ({knock})");
        Assert.Equal(knock, _f.Bots.Bots[grunt].TakeKnock(8f, 0.25f));
        Assert.Equal(2f, _f.Bots.Bots[grunt].Knock.Length(), 3);
        Assert.Equal(0f, _f.Bots.Bots[grunt].Knock.Y);
        _f.Bots.Bots[grunt].TakeKnock(8f, 1f);
        Assert.Equal(Vector3.Zero, _f.Bots.Bots[grunt].Knock);
        S.World.Now += 5;
        Assert.Equal(1500, S.Ok<SledgeResult>(Me, ToolActions.Sledge, new SledgeArgs(Bot: tank)).Health);
        Assert.Equal(Vector3.Zero, _f.Bots.Bots[tank].Knock);
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

    [Fact]
    public void A_kept_bot_is_saved_and_comes_back_as_it_was()
    {
        var bot = _f.Spawn(BotFixture.Critter, new Vector3(2, 0, 2));
        _f.Spawn(BotFixture.Grunt, new Vector3(9, 0, 9));
        Assert.Contains("only in Creative", S.Refused(Me, BotActions.KeepBot, new KeepBotArgs(bot)));
        Assert.Contains("no bot", S.Refused(BotFixture.Brain, BotActions.KeepBot, new KeepBotArgs(99)));

        S.Ok(BotFixture.Brain, BotActions.KeepBot, new KeepBotArgs(bot));
        _f.Bots.DamageBot(bot, 10, "test", melee: false);
        _f.Bots.Moved(bot, new Vector3(5, 1, 6), inWater: false);
        var saved = Assert.Single(_f.Bots.SaveKept()); // the grunt is not kept, so it is not saved
        Assert.True(S.Ok<BotView>(Me, BotActions.GetBot, new BotArgs(bot)).Kept);

        var again = new BotFixture();
        Assert.True(again.Bots.Restore(saved));
        var back = Assert.Single(again.Bots.Bots.Values);
        Assert.Equal((BotFixture.Critter, new Vector3(5, 1, 6), new Vector3(2, 0, 2), 20f, true),
            (back.Type.QualifiedId, back.Position, back.Home, back.Health, back.Kept));
        Assert.False(again.Bots.Restore(saved with { Type = "gone:critter" }));

        S.Ok(BotFixture.Brain, BotActions.KeepBot, new KeepBotArgs(bot, Kept: false));
        Assert.Empty(_f.Bots.SaveKept());
    }

    [Fact]
    public void Feeding_a_bot_its_food_tames_it()
    {
        var critter = _f.Spawn(BotFixture.Critter, new Vector3(1, 0, 1));
        var grunt = _f.Spawn(BotFixture.Grunt, new Vector3(-1, 0, 1));
        var far = _f.Spawn(BotFixture.Critter, new Vector3(30, 0, 0));
        Assert.Contains("You have no", S.Refused(Me, BotActions.FeedBot, new FeedBotArgs(critter, SurvivalFixture.Crop)));
        S.Pockets(Me).Add(SurvivalFixture.Crop, 5);
        S.Pockets(Me).Add(SurvivalFixture.Seed, 5);
        Assert.Contains("cannot be tamed", S.Refused(Me, BotActions.FeedBot, new FeedBotArgs(grunt, SurvivalFixture.Crop)));
        Assert.Contains("does not eat", S.Refused(Me, BotActions.FeedBot, new FeedBotArgs(critter, SurvivalFixture.Seed)));
        Assert.Contains("away", S.Refused(Me, BotActions.FeedBot, new FeedBotArgs(far, SurvivalFixture.Crop)));

        var first = S.Ok<FeedBotResult>(Me, BotActions.FeedBot, new FeedBotArgs(critter, SurvivalFixture.Crop));
        Assert.Equal(new FeedBotResult(1, 2, false), first);
        Assert.Equal(1, S.Ok<BotView>(Me, BotActions.GetBot, new BotArgs(critter)).Fed);
        var second = S.Ok<FeedBotResult>(Me, BotActions.FeedBot, new FeedBotArgs(critter, "test:crop"));
        Assert.True(second.Kept);
        Assert.True(_f.Bots.Find(critter)!.Kept);
        Assert.Equal(3, S.Pockets(Me).Count(SurvivalFixture.Crop));
        Assert.Contains("already tame", S.Refused(Me, BotActions.FeedBot, new FeedBotArgs(critter, SurvivalFixture.Crop)));

        var scared = _f.Spawn(BotFixture.Critter, new Vector3(1, 0, -1));
        _f.Bots.Engage(scared);
        Assert.Contains("too scared", S.Refused(Me, BotActions.FeedBot, new FeedBotArgs(scared, SurvivalFixture.Crop)));
    }

    [Fact]
    public void Players_lead_tame_animals_and_let_them_go()
    {
        // You stand at (0, 0, 6), I at (0, 0, 3).
        var critter = _f.Spawn(BotFixture.Critter, new Vector3(1, 0, 3));
        var grunt = _f.Spawn(BotFixture.Grunt, new Vector3(-1, 0, 3));
        Assert.Contains("cannot be led", S.Refused(Me, BotActions.LeadBot, new BotArgs(grunt)));
        Assert.Contains("not tame", S.Refused(Me, BotActions.LeadBot, new BotArgs(critter)));
        _f.Bots.Keep(critter);
        Assert.Contains("Craft a Connect Tool", S.Refused(Me, BotActions.LeadBot, new BotArgs(critter)));
        S.Pockets(Me).Add(SurvivalFixture.Connector, 1);
        S.Pockets(You).Add(SurvivalFixture.Connector, 1);
        Assert.Contains("Only players", S.Refused(BotFixture.Brain, BotActions.LeadBot, new BotArgs(critter)));

        Assert.True(S.Ok<LeadBotResult>(Me, BotActions.LeadBot, new BotArgs(critter)).Led);
        Assert.Equal(Me.Peer, S.Ok<BotView>(Me, BotActions.GetBot, new BotArgs(critter)).LedBy);
        Assert.Equal(new Vector3(0, 0, 3), _f.Bots.LeaderFeet(_f.Bots.Find(critter)!));
        // Anyone may take it over; leading it again lets it go, and it makes its home where it stands.
        Assert.True(S.Ok<LeadBotResult>(You, BotActions.LeadBot, new BotArgs(critter)).Led);
        Assert.Equal(You.Peer, _f.Bots.Find(critter)!.LedBy);
        _f.Bots.Moved(critter, new Vector3(2, 0, 4), false);
        Assert.False(S.Ok<LeadBotResult>(You, BotActions.LeadBot, new BotArgs(critter)).Led);
        Assert.Null(_f.Bots.Find(critter)!.LedBy);
        Assert.Equal(new Vector3(2, 0, 4), _f.Bots.Find(critter)!.Home);

        // It slips its Lead when its player gets too far away, or leaves.
        S.Ok<LeadBotResult>(Me, BotActions.LeadBot, new BotArgs(critter));
        S.World.Feet[Me.Peer] = new Vector3(40, 0, 3);
        _f.Advance(0.1);
        Assert.Null(_f.Bots.Find(critter)!.LedBy);
        S.World.Feet[Me.Peer] = new Vector3(0, 0, 3);
        S.Ok<LeadBotResult>(Me, BotActions.LeadBot, new BotArgs(critter));
        S.World.Feet.Remove(Me.Peer);
        _f.Advance(0.1);
        Assert.Null(_f.Bots.Find(critter)!.LedBy);
    }

    [Fact]
    public void Tame_animals_fed_a_treat_pair_up_and_have_young_that_grow_up()
    {
        // You stand at (0, 0, 6), I at (0, 0, 3).
        var mum = _f.Spawn(BotFixture.Critter, new Vector3(1, 0, 3));
        var dad = _f.Spawn(BotFixture.Critter, new Vector3(-1, 0, 3));
        var wild = _f.Spawn(BotFixture.Critter, new Vector3(0, 0, 4));
        _f.Bots.Keep(mum);
        _f.Bots.Keep(dad);
        S.Pockets(Me).Add(SurvivalFixture.Seed, 5);
        S.Pockets(Me).Add(SurvivalFixture.Crop, 5);
        Assert.Contains("does not eat", S.Refused(Me, BotActions.FeedBot, new FeedBotArgs(wild, SurvivalFixture.Seed)));
        Assert.Contains("already tame", S.Refused(Me, BotActions.FeedBot, new FeedBotArgs(mum, SurvivalFixture.Crop)));

        // One in the mood alone waits for a partner, and gives up after love_seconds.
        Assert.Equal(new FeedBotResult(0, 2, true, true), S.Ok<FeedBotResult>(Me, BotActions.FeedBot, new FeedBotArgs(mum, SurvivalFixture.Seed)));
        Assert.True(S.Ok<BotView>(Me, BotActions.GetBot, new BotArgs(mum)).InLove);
        Assert.Contains("already looking", S.Refused(Me, BotActions.FeedBot, new FeedBotArgs(mum, SurvivalFixture.Seed)));
        _f.Advance(31);
        Assert.False(_f.Bots.Find(mum)!.InLove);

        // Two in the mood pair up: one is expecting, neither can pair again for a while.
        S.Ok<FeedBotResult>(Me, BotActions.FeedBot, new FeedBotArgs(mum, SurvivalFixture.Seed));
        S.Ok<FeedBotResult>(Me, BotActions.FeedBot, new FeedBotArgs(dad, SurvivalFixture.Seed));
        _f.Advance(0.1);
        var mother = _f.Bots.Find(mum)!.DueAt is not null ? mum : dad;
        var father = mother == mum ? dad : mum;
        Assert.Null(_f.Bots.Find(father)!.DueAt);
        Assert.False(_f.Bots.Find(mum)!.InLove || _f.Bots.Find(dad)!.InLove);
        Assert.Contains("expecting", S.Refused(Me, BotActions.FeedBot, new FeedBotArgs(mother, SurvivalFixture.Seed)));
        Assert.Contains("not ready", S.Refused(Me, BotActions.FeedBot, new FeedBotArgs(father, SurvivalFixture.Seed)));

        // The young is born tame by its mother, wanders round her and grows up.
        _f.Advance(10);
        var kit = Assert.Single(_f.Bots.Bots.Values, b => b.Type.QualifiedId == BotFixture.Kit);
        Assert.Equal((true, mother, _f.Bots.Find(mother)!.Home), (kit.Kept, kit.Mother, kit.Home));
        _f.Bots.Moved(mother, new Vector3(4, 0, 2), false);
        _f.Advance(0.1);
        Assert.Equal(new Vector3(4, 0, 2), kit.Home);
        var saved = Assert.Single(_f.Bots.SaveKept(), s => s.Type == BotFixture.Kit);
        Assert.InRange(saved.GrowsIn!.Value, 49f, 50f);
        Assert.InRange(Assert.Single(_f.Bots.SaveKept(), s => s.Type == BotFixture.Critter && s.BreedIn is not null && s.Position.X > 3).BreedIn!.Value, 89f, 90f);
        var again = new BotFixture();
        Assert.True(again.Bots.Restore(saved));
        Assert.Equal(again.Now + saved.GrowsIn.Value, again.Bots.Bots.Values.Single().GrowsAt!.Value, 3);
        _f.Advance(50);
        Assert.Null(_f.Bots.Find(kit.Id));
        Assert.Equal(4, _f.Bots.Bots.Values.Count(b => b.Type.QualifiedId == BotFixture.Critter));
        Assert.Equal(3, _f.Bots.Bots.Values.Count(b => b.Kept));
    }

    [Fact]
    public void A_full_herd_does_not_breed()
    {
        var herd = Enumerable.Range(0, 4).Select(i => _f.Spawn(BotFixture.Critter, new Vector3(i * 0.5f, 0, 3))).ToList();
        herd.ForEach(b => _f.Bots.Keep(b));
        S.Pockets(Me).Add(SurvivalFixture.Seed, 5);
        S.Ok<FeedBotResult>(Me, BotActions.FeedBot, new FeedBotArgs(herd[0], SurvivalFixture.Seed));
        S.Ok<FeedBotResult>(Me, BotActions.FeedBot, new FeedBotArgs(herd[1], SurvivalFixture.Seed));
        _f.Advance(0.1);
        Assert.All(herd, b => Assert.Null(_f.Bots.Find(b)!.DueAt));
        _f.Bots.Keep(herd[3], false);
        _f.Advance(0.1);
        Assert.Single(herd, b => _f.Bots.Find(b)!.DueAt is not null);
    }

    [Fact]
    public void Tame_animals_move_in_with_a_home_part_nearby()
    {
        var critter = _f.Spawn(BotFixture.Critter, new Vector3(1, 0, 3));
        var wild = _f.Spawn(BotFixture.Critter, new Vector3(1, 0, 4));
        _f.Bots.Keep(critter);
        _f.Advance(3);
        Assert.Equal(new Vector3(1, 0, 3), _f.Bots.Find(critter)!.Home);
        var far = S.Add(SurvivalFixture.Chest, new Vector3(30, 0, 3));
        var near = S.Add(SurvivalFixture.Chest, new Vector3(6, 0, 3));
        _f.Advance(3);
        var home = _f.Bots.Find(critter)!.Home;
        var trough = SurvivalFixture.FakeWorld.Centre(S.World.Creations[near], 1);
        Assert.Equal((trough.X, trough.Z), (home.X, home.Z));
        Assert.NotEqual(far, near);
        // Wild ones don't, and nor does one on a Lead.
        Assert.Equal(new Vector3(1, 0, 4), _f.Bots.Find(wild)!.Home);
        S.Pockets(Me).Add(SurvivalFixture.Connector, 1);
        S.Ok<LeadBotResult>(Me, BotActions.LeadBot, new BotArgs(critter));
        var nearer = S.Add(SurvivalFixture.Chest, new Vector3(1, 0, 1));
        _f.Advance(3);
        Assert.Equal(home, _f.Bots.Find(critter)!.Home);
        // Let go, it moves in with the nearest.
        S.Ok<LeadBotResult>(Me, BotActions.LeadBot, new BotArgs(critter));
        _f.Advance(3);
        trough = SurvivalFixture.FakeWorld.Centre(S.World.Creations[nearer], 1);
        Assert.Equal((trough.X, trough.Z), (_f.Bots.Find(critter)!.Home.X, _f.Bots.Find(critter)!.Home.Z));
    }

    [Fact]
    public void Tame_animals_eat_from_their_home_and_give_produce_a_player_collects()
    {
        // I stand at (0, 0, 3). The critter gives one scrap after 3 meals, one every 5 s, and the next no sooner than 50 s after.
        var critter = _f.Spawn(BotFixture.Critter, new Vector3(1, 0, 3));
        var kit = _f.Spawn(BotFixture.Kit, new Vector3(1, 0, 4));
        var wild = _f.Spawn(BotFixture.Critter, new Vector3(-1, 0, 3));
        var grunt = _f.Spawn(BotFixture.Grunt, new Vector3(-1, 0, 4));
        _f.Bots.Keep(critter);
        _f.Bots.Keep(kit);
        var trough = S.Add(SurvivalFixture.Chest, new Vector3(2, 0, 3));
        var food = S.World.Creations[trough].InventoryOf(1)!;
        food.Add(SurvivalFixture.Crop, 4);
        Assert.Contains("0 of 3 meals", S.Refused(Me, BotActions.CollectBot, new BotArgs(critter)));
        Assert.Contains("gives nothing", S.Refused(Me, BotActions.CollectBot, new BotArgs(kit)));
        Assert.Contains("gives nothing", S.Refused(Me, BotActions.CollectBot, new BotArgs(grunt)));
        Assert.Contains("not tame", S.Refused(Me, BotActions.CollectBot, new BotArgs(wild)));

        // It moves in, eats one meal at once and then one every 5 s; the kit (no produce) and the wild one eat nothing.
        _f.Advance(3);
        Assert.Equal((1, 3), (_f.Bots.Find(critter)!.Meals, food.Count(SurvivalFixture.Crop)));
        _f.Advance(2);
        Assert.Equal(1, _f.Bots.Find(critter)!.Meals);
        _f.Advance(3);
        _f.Advance(5);
        var view = S.Ok<BotView>(Me, BotActions.GetBot, new BotArgs(critter));
        Assert.Equal((3, true), (view.Meals, view.Ready));
        Assert.Equal(1, food.Count(SurvivalFixture.Crop));
        Assert.Equal((0, false), (_f.Bots.Find(kit)!.Meals, _f.Bots.Find(kit)!.Ready));
        _f.Advance(5);
        Assert.Equal(1, food.Count(SurvivalFixture.Crop)); // full: it stops eating until it is collected

        Assert.Contains("Only players", S.Refused(BotFixture.Brain, BotActions.CollectBot, new BotArgs(critter)));
        Assert.Equal(new CollectBotResult(SurvivalFixture.Scrap, 1), S.Ok<CollectBotResult>(Me, BotActions.CollectBot, new BotArgs(critter)));
        Assert.Equal(1, S.Pockets(Me).Count(SurvivalFixture.Scrap));
        Assert.Equal((0, false), (_f.Bots.Find(critter)!.Meals, _f.Bots.Find(critter)!.Ready));
        Assert.Contains("nothing to give", S.Refused(Me, BotActions.CollectBot, new BotArgs(critter)));

        // It eats again, stops when the home is empty, and has the next only once the cooldown is over.
        _f.Advance(5);
        Assert.Equal((1, 0), (_f.Bots.Find(critter)!.Meals, food.Count(SurvivalFixture.Crop)));
        _f.Advance(5);
        Assert.Equal(1, _f.Bots.Find(critter)!.Meals);
        food.Add(SurvivalFixture.Crop, 5);
        _f.Advance(5);
        _f.Advance(5);
        var bot = _f.Bots.Find(critter)!;
        Assert.Equal((3, false), (bot.Meals, bot.Ready));
        Assert.Contains("nothing to give yet", S.Refused(Me, BotActions.CollectBot, new BotArgs(critter)));

        // Meals and the cooldown left save with it.
        var saved = Assert.Single(_f.Bots.SaveKept(), s => s.Type == BotFixture.Critter);
        Assert.Equal(3, saved.Meals);
        var left = bot.ProduceAt - _f.Now;
        Assert.Equal(left, saved.ProduceIn!.Value, 3);
        var again = new BotFixture();
        Assert.True(again.Bots.Restore(saved));
        var back = again.Bots.Bots.Values.Single();
        Assert.Equal((3, false), (back.Meals, back.Ready));
        again.Advance(left + 0.1);
        Assert.True(back.Ready);

        _f.Advance(left + 0.1);
        Assert.True(_f.Bots.Find(critter)!.Ready);
        // On a Lead it does not eat; it keeps what it has ready.
        S.Ok<CollectBotResult>(Me, BotActions.CollectBot, new BotArgs(critter));
        S.Pockets(Me).Add(SurvivalFixture.Connector, 1);
        S.Ok<LeadBotResult>(Me, BotActions.LeadBot, new BotArgs(critter));
        _f.Advance(10);
        Assert.Equal(0, _f.Bots.Find(critter)!.Meals);
    }

    [Fact]
    public void A_full_inventory_cannot_collect_produce()
    {
        var critter = _f.Spawn(BotFixture.Critter, new Vector3(1, 0, 3));
        _f.Bots.Keep(critter);
        var trough = S.Add(SurvivalFixture.Chest, new Vector3(2, 0, 3));
        S.World.Creations[trough].InventoryOf(1)!.Add(SurvivalFixture.Crop, 3);
        for (var i = 0; i < 3; i++)
        {
            _f.Bots.Hurry(critter);
            _f.Advance(2);
        }
        Assert.True(_f.Bots.Find(critter)!.Ready);
        var pockets = S.Pockets(Me);
        for (var k = 0; k < 100 && pockets.SpaceFor(SurvivalFixture.Scrap) > 0; k++)
            pockets.Add(SurvivalFixture.Block, 1000);
        Assert.Contains("Make room", S.Refused(Me, BotActions.CollectBot, new BotArgs(critter)));
    }

    [Fact]
    public void Bubble_wrap_slows_a_player_for_a_while()
    {
        // You stand at (0, 0, 6).
        var bot = _f.Spawn(BotFixture.Spitter, new Vector3(0, 0, 8));
        Assert.Equal(1f, S.Survival.SpeedScale(You.Peer));
        S.Ok(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "wrap", Player: You.Peer));
        Assert.Equal(0.5f, S.Survival.SpeedScale(You.Peer));
        Assert.Equal(1f, S.Survival.SpeedScale(Me.Peer));
        var view = S.Survival.View(You.Peer)!;
        Assert.Equal((0.5f, 3f), (view.Slow, view.SlowFor));
        _f.Advance(2);
        Assert.Equal(0.5f, S.Survival.SpeedScale(You.Peer));
        // A second hit keeps it going from now.
        S.Ok(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "wrap", Player: You.Peer));
        _f.Advance(2);
        Assert.Equal(0.5f, S.Survival.SpeedScale(You.Peer));
        _f.Advance(1.5);
        Assert.Equal(1f, S.Survival.SpeedScale(You.Peer));
        Assert.Equal((1f, 0f), (S.Survival.View(You.Peer)!.Slow, S.Survival.View(You.Peer)!.SlowFor));
    }

    [Fact]
    public void A_stronger_slow_wins_and_death_or_creative_clear_it()
    {
        S.Survival.Slow(You.Peer, 0.5f, 10);
        S.Survival.Slow(You.Peer, 0.8f, 2);
        Assert.Equal(0.5f, S.Survival.SpeedScale(You.Peer));
        Assert.Equal(10f, S.Survival.View(You.Peer)!.SlowFor);
        S.Survival.Damage(You.Peer, 1000, "test");
        Assert.Equal(1f, S.Survival.SpeedScale(You.Peer));

        var creative = new BotFixture(GameMode.Creative);
        creative.Survival.Survival.Slow(You.Peer, 0.5f, 10);
        Assert.Equal(1f, creative.Survival.Survival.SpeedScale(You.Peer));
    }

    [Fact]
    public void A_bot_breaks_a_world_object_with_an_attack_that_has_object_damage()
    {
        var tree = Tree(new WorldObjectId(0, 0, 1), new Vector3(10, 0, 0), health: 30);
        var bot = _f.Spawn(BotFixture.Smasher, new Vector3(7, 0, 0));
        Assert.Equal([tree.ToString()], _f.Bots.ObjectsNear(_f.Bots.Find(bot)!.Centre, 5).Select(o => o.Id));
        Assert.Contains("cannot break world objects",
            S.Refused(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "poke", Object: tree.ToString())));
        var hit = S.Ok<BotAttackResult>(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "kick", Object: tree.ToString())).Hits.Single();
        Assert.Equal(("object", 20f, tree.ToString()), (hit.Kind, hit.Damage, hit.Object));
        Assert.False(S.World.State.IsDestroyed(tree));
        _f.Advance(1);
        S.Ok(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "kick", Object: tree.ToString()));
        Assert.True(S.World.State.IsDestroyed(tree));
        // Its drops lie where it stood.
        Assert.Contains(S.Survival.Drops.Values, d => d.Items.Any(i => i.Item == Scrap));
        Assert.Contains("not there", S.Refused(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(_f.Spawn(BotFixture.Smasher, new Vector3(7, 0, 0)), "kick", Object: tree.ToString())));
    }

    [Fact]
    public void An_area_attack_with_object_damage_breaks_objects_round_the_bot()
    {
        var near = Tree(new WorldObjectId(0, 0, 1), new Vector3(2, 0, 0), health: 20); // 1.8 m off: 22 damage
        var far = Tree(new WorldObjectId(0, 0, 2), new Vector3(9, 0, 0), health: 30);
        var bot = _f.Spawn(BotFixture.Smasher, new Vector3(0, 0, 0));
        var hits = S.Ok<BotAttackResult>(BotFixture.Brain, BotActions.BotAttack, new BotAttackArgs(bot, "stomp")).Hits;
        Assert.Contains(hits, h => h.Kind == "object" && h.Object == near.ToString());
        Assert.True(S.World.State.IsDestroyed(near));
        Assert.False(S.World.State.IsDestroyed(far));
    }

    [Fact]
    public void Spinning_parts_are_the_far_side_of_a_turning_bearing()
    {
        var creation = new Creation(S.World.NextCreationId(), Creations.TestParts.Catalog());
        Creations.TestParts.Place(creation, Creations.TestParts.Block, new GridCell(0, 0, 0));
        Creations.TestParts.Place(creation, Creations.TestParts.Bearing, new GridCell(0, 1, 0));
        var rotor = Creations.TestParts.Place(creation, Creations.TestParts.Block, new GridCell(0, 2, 0)).Id;
        creation.SetAllBodyPoses(new Pose(new Vector3(3, 0, 0), Quaternion.Identity));
        S.World.Added(creation);
        var link = Assert.Single(creation.Joints);
        var at = new Vector3(0, 0.5f, 0);
        Assert.Empty(_f.Bots.SpinningNear(at, 6, 4));

        creation.SetBodyVelocity(link.BodyB, new BodyVelocity(Vector3.Zero, new Vector3(0, 10, 0)));
        var spinning = Assert.Single(_f.Bots.SpinningNear(at, 6, 4));
        Assert.Equal((creation.Id, rotor), (spinning.Creation, spinning.Part));
        Assert.Equal(10f, spinning.Speed, 3);
        Assert.Empty(_f.Bots.SpinningNear(at, 6, 12));
        Assert.Empty(_f.Bots.SpinningNear(at, 1, 4));
        // Asked for one part type, only a part of that type on the turning side counts.
        Assert.Equal(rotor, Assert.Single(_f.Bots.SpinningNear(at, 6, 4, Creations.TestParts.Block)).Part);
        Assert.Empty(_f.Bots.SpinningNear(at, 6, 4, "vanilla:saw_blade"));
        // Several types, separated by commas: any of them counts.
        Assert.Equal(rotor, Assert.Single(_f.Bots.SpinningNear(at, 6, 4, $"vanilla:saw_blade, {Creations.TestParts.Block}")).Part);
        Assert.Empty(_f.Bots.SpinningNear(at, 6, 4, "vanilla:saw_blade,vanilla:drill"));

        // Turning together (the whole Creation tumbling) is not spinning.
        creation.SetBodyVelocity(link.BodyA, new BodyVelocity(Vector3.Zero, new Vector3(0, 10, 0)));
        Assert.Empty(_f.Bots.SpinningNear(at, 6, 4));
    }

    private WorldObjectId Tree(WorldObjectId id, Vector3 at, float health)
    {
        S.World.Objects[id] = new WorldObjectPlacement(id, new WorldObjectDefinition
        {
            Id = "tree", Health = health, Collider = new ShapeDefinition { Radius = 0.3f, At = new Vec3(0, 1, 0) },
            Drops = [new ObjectDrop { Item = Scrap, Count = 2 }],
        }, at, 0, 1);
        return id;
    }
}
