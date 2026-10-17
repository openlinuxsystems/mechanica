using System.Numerics;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;

namespace Mechanica.Scripting.Tests.Bots;

/// <summary>The Bot Tutorial's Sentry Drone runs as written: its custom <c>patrol</c> leaf and hooks work.</summary>
public sealed class ExampleBotTests
{
    private static readonly string Example = Path.Combine(RepoRoot(), "examples", "mods", "example_bots");

    [Fact]
    public void The_sentry_drone_patrols_then_shoots_an_intruder()
    {
        var definition = DataJson.Read<BotDefinition>(File.ReadAllText(Path.Combine(Example, "bots", "sentry_drone.json")))!;
        // The rig has no vanilla items, so the drops stay out.
        using var rig = new BotRig(definition with { Drops = [] });
        rig.Write(definition.Script!, File.ReadAllText(Path.Combine(Example, definition.Script!)));
        var drone = rig.Spawn("test:sentry_drone", Vector3.Zero);
        rig.Step(2);
        Assert.Empty(rig.Errors);
        Assert.Contains(rig.Prints, p => p.Contains("on patrol", StringComparison.Ordinal));
        Assert.True(rig.Bots.Find(drone)!.Position.X > 2, $"walking to the first waypoint ({rig.Bots.Find(drone)!.Position})");

        rig.AddPlayer(2, rig.Bots.Find(drone)!.Position + new Vector3(0, 0, 8));
        rig.Step(3);
        Assert.Empty(rig.Errors);
        Assert.True(rig.Survival.View(2)!.Health < 100, "the drone shot the intruder");

        // A hit it survives calls on_damaged; the one that kills it calls only on_death.
        rig.Bots.DamageBot(drone, 10, "test", melee: false);
        rig.Step(0.1f);
        rig.Bots.DamageBot(drone, 1000, "test", melee: false);
        rig.Step(0.1f);
        Assert.Contains(rig.Prints, p => p.Contains("destroyed after 1 hits", StringComparison.Ordinal));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mechanica.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Mechanica.sln not found above the test output.");
    }
}
