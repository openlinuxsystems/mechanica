using System.Numerics;
using Mechanica.Core.Bots;

namespace Mechanica.Scripting.Tests.Bots;

public sealed class BotRuntimeTests
{
    private const string Raider = "test:raider";

    private static BotDefinition RaiderDefinition(string? script = null, string? tree = null) => new()
    {
        Id = "raider", Name = "Raider", Health = 60, Speed = 4, RaidCost = 1,
        Attacks = [new BotAttackDefinition { Id = "whip", Kind = BotAttackKind.Melee, Damage = 15, Range = 1.5f, Interval = 1 }],
        Script = script,
        Behavior = BotRig.Tree(tree ?? """
            { "type": "selector", "children": [
              { "type": "sequence", "children": [
                { "type": "find_player", "range": 6 },
                { "type": "chase", "attack": "whip" },
                { "type": "attack", "attack": "whip" } ] },
              { "type": "sequence", "children": [
                { "type": "find_crop" },
                { "type": "chase", "attack": "whip" },
                { "type": "attack", "attack": "whip" } ] },
              { "type": "wander", "radius": 3 } ] }
            """),
    };

    [Fact]
    public void A_bot_walks_to_a_crop_and_destroys_it()
    {
        using var rig = new BotRig(RaiderDefinition());
        var soil = rig.Plant(new Vector3(10, 0.125f, 0));
        var bot = rig.Spawn(Raider, Vector3.Zero);
        rig.Step(1);
        Assert.True(rig.Bots.Find(bot)!.Position.X > 2, $"walking toward the crop ({rig.Bots.Find(bot)!.Position})");
        rig.Step(4);
        Assert.Empty(rig.Errors);
        Assert.Null(rig.Creations[soil].SoilOf(1)!.Seed);
    }

    [Fact]
    public void A_bot_prefers_a_nearby_player_and_hurts_them()
    {
        using var rig = new BotRig(RaiderDefinition());
        rig.Plant(new Vector3(-20, 0.125f, 0));
        rig.AddPlayer(2, new Vector3(4, 0, 0));
        rig.Spawn(Raider, Vector3.Zero);
        rig.Step(3);
        Assert.Empty(rig.Errors);
        Assert.True(rig.Survival.View(2)!.Health < 100);
    }

    [Fact]
    public void Without_anything_to_do_a_bot_wanders_near_home()
    {
        using var rig = new BotRig(RaiderDefinition());
        var bot = rig.Spawn(Raider, new Vector3(5, 0, 5));
        rig.Step(6);
        Assert.Empty(rig.Errors);
        var state = rig.Bots.Find(bot)!;
        Assert.NotEqual(new Vector3(5, 0, 5), state.Position);
        Assert.InRange(Vector3.Distance(state.Position, state.Home), 0, 4f);
    }

    [Fact]
    public void Scripts_add_hooks_and_custom_leaves()
    {
        using var rig = new BotRig(RaiderDefinition("scripts/raider.lua", """{ "type": "sequence", "children": [ { "type": "shout" }, { "type": "stop" } ] }"""));
        rig.Write("scripts/raider.lua", """
            function on_spawn() state.shouts = 0 log("spawned with", bot.max_health(), "health") end
            function shout(node, dt) state.shouts = state.shouts + 1 return "success" end
            function on_damaged(amount) log("ouch", amount) end
            function on_death() log("died after", state.shouts, "shouts") end
            """);
        var bot = rig.Spawn(Raider, Vector3.Zero);
        rig.Step(0.5f);
        rig.Bots.DamageBot(bot, 20, "test", melee: false);
        rig.Step(0.1f);
        rig.Bots.DamageBot(bot, 50, "test", melee: false);
        rig.Step(0.1f);
        Assert.Empty(rig.Errors);
        Assert.Contains("bot 1: spawned with 60 health", rig.Prints);
        Assert.Contains("bot 1: ouch 20", rig.Prints);
        Assert.Contains(rig.Prints, p => p.StartsWith("bot 1: died after", StringComparison.Ordinal) && !p.EndsWith(" 0 shouts", StringComparison.Ordinal));
    }

    [Fact]
    public void An_on_think_hook_replaces_the_tree_and_can_still_run_it()
    {
        using var rig = new BotRig(RaiderDefinition("scripts/raider.lua"));
        rig.Write("scripts/raider.lua", """
            function on_think(dt)
              state.thinks = (state.thinks or 0) + 1
              return behavior.run(tree, dt)
            end
            """);
        rig.Plant(new Vector3(6, 0.125f, 0));
        var bot = rig.Spawn(Raider, Vector3.Zero);
        rig.Step(1);
        Assert.Empty(rig.Errors);
        Assert.Equal("running", rig.Runtime.Brains.Single().Status);
        Assert.True(rig.Bots.Find(bot)!.Desired.X > 0);
    }

    [Fact]
    public void A_failing_script_stops_its_bot_and_a_fixed_one_restarts_it()
    {
        using var rig = new BotRig(RaiderDefinition("scripts/raider.lua", """{ "type": "broken" }"""));
        rig.Write("scripts/raider.lua", "function on_spawn() end\n");
        rig.Plant(new Vector3(6, 0.125f, 0));
        var bot = rig.Spawn(Raider, Vector3.Zero);
        rig.Step(0.3f);
        Assert.Contains(rig.Errors, e => e.Text.Contains("unknown behaviour node 'broken'", StringComparison.Ordinal));
        Assert.NotNull(rig.Runtime.Brains.Single().Fault);
        Assert.Equal(Vector3.Zero, rig.Bots.Find(bot)!.Desired);

        rig.Write("scripts/raider.lua", "function broken(node, dt) bot.move(vec3(1, 0, 0)) return 'running' end\n");
        rig.Runtime.CheckForChanges();
        rig.Step(0.3f);
        Assert.Null(rig.Runtime.Brains.Single().Fault);
        Assert.Equal(new Vector3(4, 0, 0), rig.Bots.Find(bot)!.Desired);
    }

    [Fact]
    public void Refused_actions_come_back_to_the_script_and_warn_once()
    {
        using var rig = new BotRig(RaiderDefinition("scripts/raider.lua", """{ "type": "try" }"""));
        rig.Write("scripts/raider.lua", """
            function try()
              local ok, why = bot.attack("whip", { player = 9 })
              state.why = why
              return ok
            end
            function on_death() log(state.why) end
            """);
        var bot = rig.Spawn(Raider, Vector3.Zero);
        rig.Step(1);
        rig.Bots.DamageBot(bot, 100, "test", melee: false);
        rig.Step(0.1f);
        Assert.Contains("bot 1: The target is not there.", rig.Prints);
        Assert.Single(rig.Messages, m => m.Kind == ScriptMessageKind.Warning);
    }

    [Fact]
    public void The_library_mirrors_the_built_in_node_list()
    {
        using var rig = new BotRig(RaiderDefinition("scripts/raider.lua", """{ "type": "list" }"""));
        rig.Write("scripts/raider.lua", """
            function list()
              local names = {}
              for name in pairs(behavior.leaves) do names[#names + 1] = name end
              table.sort(names)
              state.names = table.concat(names, ",")
              log(state.names)
              return "success"
            end
            """);
        rig.Spawn(Raider, Vector3.Zero);
        rig.Step(0.2f);
        var expected = string.Join(",", BotBehavior.Nodes.Select(n => n.Type).Order(StringComparer.Ordinal));
        Assert.Contains($"bot 1: {expected}", rig.Prints);
    }
}
