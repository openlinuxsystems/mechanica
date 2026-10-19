using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Survival;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Scripting.Tests;

/// <summary>
/// The scripts of the example mods behind the Items Tutorial and the mod scripts cookbook
/// (<c>examples/mods/example_items</c>, <c>examples/mods/example_modscript</c>), run in the survival test world with
/// the numbers from their own part files.
/// </summary>
public sealed class ExampleModScriptTests : IDisposable
{
    private static readonly Vec3 Ahead = new(0, 0, -1);
    private static readonly string Examples = Path.Combine(ItemRig.VanillaRoot, "..", "..", "examples", "mods");

    private readonly ItemRig _rig = new();

    private SurvivalFixture F => _rig.F;

    public void Dispose() => _rig.Dispose();

    // Copies an example mod's scripts into the test mod and adds its item with the properties from its part file.
    private string AddExampleItem(string mod, string id, PartCategory category)
    {
        CopyScripts(mod);
        var part = JsonNode.Parse(File.ReadAllText(Path.Combine(Examples, mod, "parts", id + ".json")))!.AsObject();
        var script = part["script"]!.GetValue<string>();
        var qualified = $"test:{id}";
        F.Catalog.Add(qualified, new PartDefinition
        {
            Id = id,
            Name = part["name"]!.GetValue<string>(),
            Category = category,
            StackSize = part["stack_size"]!.GetValue<int>(),
            // The test mod has vanilla's scripts too, so vanilla:scripts/food.lua is scripts/food.lua here.
            Script = script.StartsWith("vanilla:", StringComparison.Ordinal) ? script["vanilla:".Length..] : script,
            Properties = part["properties"]?.DeepClone().AsObject(),
        });
        return qualified;
    }

    private void CopyScripts(string mod)
    {
        var root = Path.Combine(Examples, mod);
        foreach (var file in Directory.GetFiles(Path.Combine(root, "scripts"), "*.lua", SearchOption.AllDirectories))
            _rig.Write(Path.GetRelativePath(root, file), File.ReadAllText(file));
    }

    private void Fly()
    {
        for (var i = 0; i < 240 && F.Survival.Projectiles.Any(); i++)
            F.Survival.Tick(1f / 60);
        Assert.Empty(F.Survival.Projectiles);
    }

    private void NoErrors() => Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);

    [Fact]
    public void TheRationBarHealsAndCountsWhatThePlayerAte()
    {
        var bar = AddExampleItem("example_items", "ration_bar", PartCategory.Consumable);
        F.Pockets(Me).Add(bar, 3);
        var slot = F.SlotOf(Me, bar);
        Assert.Equal("You are already at full health.", _rig.Refused(Me, slot));

        F.Survival.Damage(Me.Peer, 60, "test");
        var first = _rig.Used(Me, slot);
        var second = _rig.Used(Me, slot);

        Assert.Equal((65.0, 1.0), (first["health"]!.GetValue<double>(), first["eaten"]!.GetValue<double>()));
        Assert.Equal((90.0, 2.0), (second["health"]!.GetValue<double>(), second["eaten"]!.GetValue<double>()));
        Assert.Equal(1, F.Pockets(Me).Count(bar));
        NoErrors();
    }

    [Fact]
    public void ThePeaShooterFiresPeasThatHitWhereTheyLand_AndReloads()
    {
        const string pea = "test:dried_pea";
        F.Catalog.Add(pea, new PartDefinition { Id = "dried_pea", Name = "Dried Pea", Category = PartCategory.Resource, StackSize = 100 });
        var gun = AddExampleItem("example_items", "pea_shooter", PartCategory.Weapon);
        var counters = new List<ItemCounterView>();
        _rig.Runtime.ItemCounterChanged += (_, view) => counters.Add(view);
        F.Pockets(Me).Add(gun, 1);
        F.Pockets(Me).Add(pea, 14);
        var slot = F.SlotOf(Me, gun);
        F.Ok(Me, ScriptRuntime.EquipItem, new { slot });
        Assert.Equal(new ItemCounterView(gun, 12, 12, pea), counters[^1]);
        // Anything the flight crosses below y = 1 is the other player.
        F.World.Ray = (start, direction) => start.Y + direction.Y * F.World.LastRange < 1 ? new RayHit(start with { Y = 1 }, Player: You.Peer) : null;

        _rig.Used(Me, slot, Ahead);
        Fly();

        Assert.Equal(92f, F.Survival.View(You.Peer)!.Health);
        Assert.Equal(13, F.Pockets(Me).Count(pea));
        Assert.Equal(new ItemCounterView(gun, 11, 12, pea), counters[^1]);
        Assert.Equal("The Pea Shooter is not ready yet.", _rig.Refused(Me, slot, Ahead));
        _rig.Runtime.Update(1);
        _rig.Used(Me, slot, Ahead);
        Fly();
        F.Ok(Me, ScriptRuntime.ReloadItem, new { slot });
        _rig.Runtime.Update(0.5f);
        Assert.Equal("The Pea Shooter is still reloading.", _rig.Refused(Me, slot, Ahead));
        NoErrors();
    }

    [Fact]
    public void TheFizzBombIsThrownAndBurstsWhereItLands()
    {
        var bomb = AddExampleItem("example_items", "fizz_bomb", PartCategory.Weapon);
        F.Pockets(Me).Add(bomb, 2);
        F.World.Feet[You.Peer] = new Vector3(0, 0, -2.5f); // near where it comes down, about 6 m ahead
        F.World.Ray = (start, direction) => start.Y + direction.Y * F.World.LastRange < 1 ? new RayHit(start with { Y = 1 }) : null;

        _rig.Used(Me, F.SlotOf(Me, bomb), Ahead);
        Assert.Equal(1, F.Pockets(Me).Count(bomb));
        Assert.Equal(100f, F.Survival.View(You.Peer)!.Health); // nothing is hurt until it lands
        Fly();

        Assert.InRange(F.Survival.View(You.Peer)!.Health, 1f, 99f);
        Assert.Equal(100f, F.Survival.View(Me.Peer)!.Health); // out of the burst
        NoErrors();
    }

    [Fact]
    public void TheWelcomeModScriptGreetsNewPlayersOnceAndHandsOutSnacks()
    {
        var mix = AddExampleItem("example_modscript", "trail_mix", PartCategory.Consumable);
        F.Dispatcher.Register<NoArgs>("GetPlayers", (_, _) => null,
            (_, _) => JsonSerializer.SerializeToElement(new[] { new { peer = Me.Peer, name = "Ada", dead = false } }), readOnly: true);
        _rig.ModScripts.Add("scripts/welcome.lua");
        _rig.Runtime.Update(0.1f);

        _rig.Runtime.RaiseEvent("players", JsonSerializer.SerializeToElement(new { kind = "joined", peer = Me.Peer, name = "Ada" }));
        _rig.Runtime.Update(0.1f);
        Assert.True(3 == F.Pockets(Me).Count(mix), string.Join("\n", _rig.Messages.Select(m => m.Text)));
        _rig.Runtime.RaiseEvent("players", JsonSerializer.SerializeToElement(new { kind = "joined", peer = Me.Peer, name = "Ada" }));
        _rig.Runtime.Update(0.1f);
        Assert.Equal(3, F.Pockets(Me).Count(mix)); // greeted once

        _rig.Runtime.Update(600);
        _rig.Runtime.Update(0.1f);
        Assert.Equal(4, F.Pockets(Me).Count(mix));
        var state = _rig.Runtime.ExportModStates()["test:scripts/welcome.lua"];
        Assert.True(state["greeted"]!["Ada"]!.GetValue<bool>());
        Assert.Equal(1.0, state["snacks"]!.GetValue<double>());
        NoErrors();
    }
}
