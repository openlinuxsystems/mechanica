using System.Numerics;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Survival;
using Mechanica.Core.World;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Scripting.Tests;

/// <summary>Held items in Lua (chunk 20b): the vanilla food, potato gun, seed and watering scripts, and the item hooks.</summary>
public sealed class ItemScriptTests : IDisposable
{
    private static readonly Vec3 Ahead = new(0, 0, -1);

    private readonly ItemRig _rig = new();

    private SurvivalFixture F => _rig.F;

    public void Dispose() => _rig.Dispose();

    // ---- Food (mods/vanilla/scripts/food.lua) ----

    [Fact]
    public void EatingHealsAndUsesTheFoodUp()
    {
        F.Pockets(Me).Add(Crop, 2);
        var slot = F.SlotOf(Me, Crop);
        Assert.Equal("You are already at full health.", _rig.Refused(Me, slot));

        F.Survival.Damage(Me.Peer, 50, "test");
        Assert.Equal(80.0, _rig.Used(Me, slot)["health"]!.GetValue<double>());
        Assert.Equal(100.0, _rig.Used(Me, slot)["health"]!.GetValue<double>());
        Assert.Equal(0, F.Pockets(Me).Count(Crop));
        Assert.Equal($"Slot {slot} is empty.", _rig.Refused(Me, slot));
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    [Fact]
    public void TheDeadCannotEat()
    {
        F.Pockets(Me).Add(Crop, 1);
        F.Survival.Damage(Me.Peer, 1000, "test");
        Assert.Equal("You are dead. Respawn first.", _rig.Refused(Me, 0));
    }

    // ---- Weapons (mods/vanilla/scripts/potato_gun.lua) ----

    [Fact]
    public void AShotUsesAmmoAndHurtsThePlayerItHits()
    {
        F.Pockets(Me).Add(Blaster, 1);
        var slot = F.SlotOf(Me, Blaster);
        Assert.Equal("You have no Crop to fire.", _rig.Refused(Me, slot, Ahead));
        F.Pockets(Me).Add(Crop, 2);
        F.World.Ray = (_, _) => new RayHit(new Vector3(0, 1, 0), Player: You.Peer);

        var result = _rig.Used(Me, slot, Ahead);

        var hit = Assert.Single(result["hits"]!.AsArray())!;
        Assert.Equal(("player", 20.0, 3), (hit["kind"]!.GetValue<string>(), hit["damage"]!.GetValue<double>(), hit["player"]!.GetValue<int>()));
        Assert.Equal(1.0, hit["position"]!["y"]!.GetValue<double>());
        Assert.Equal(80f, F.Survival.View(You.Peer)!.Health);
        Assert.Equal(1, F.Pockets(Me).Count(Crop));
        Assert.Equal((new Vector3(0, 1.6f, 3), -Vector3.UnitZ), F.World.Casts[^1]);
        Assert.Equal("The Blaster is not ready yet.", _rig.Refused(Me, slot, Ahead));
        _rig.Runtime.Update(1);
        Assert.Equal("That is not where you are aiming.", _rig.Refused(Me, slot, new Vec3(1, 0, 0)));
        Assert.Equal("direction must be a non-zero vector.", _rig.Refused(Me, slot, new Vec3(0, 0, 0)));
        F.Pockets(Me).Add(Scrap, 1);
        Assert.Equal("Scrap cannot be used.", _rig.Refused(Me, F.SlotOf(Me, Scrap), Ahead));
        Assert.Single(_rig.Used(Me, slot)["hits"]!.AsArray()); // no direction: where they look
    }

    [Fact]
    public void ShotsBreakPartsAndPushTheirBody()
    {
        var block = F.Add(Bed, new Vector3(0, 0, 0)); // durability 3: 30 health
        F.Pockets(Me).Add(Blaster, 1);
        F.Pockets(Me).Add(Crop, 2);
        F.World.Ray = (_, _) => new RayHit(new Vector3(0, 0.1f, 0.2f), Creation: block, Part: 1);
        var slot = F.SlotOf(Me, Blaster);
        F.World.Feet[2] = new Vector3(0, 0, 1);

        Assert.Equal("part", _rig.Used(Me, slot, Ahead)["hits"]![0]!["kind"]!.GetValue<string>());
        Assert.True(F.World.Creations.ContainsKey(block));
        Assert.Equal([new Vector3(0, 0, -5)], F.World.Pushes);
        _rig.Runtime.Update(1);
        _rig.Used(Me, slot, Ahead);
        Assert.False(F.World.Creations.ContainsKey(block));
    }

    [Fact]
    public void AScattergunFiresItsPelletsRoundTheCone()
    {
        F.Pockets(Me).Add(Scattergun, 1);
        F.World.Ray = (_, direction) => new RayHit(direction * 5, Player: You.Peer);

        var hits = _rig.Used(Me, F.SlotOf(Me, Scattergun), Ahead)["hits"]!.AsArray();

        Assert.Equal(5, F.World.Casts.Count);
        Assert.Equal(SurvivalActions.Pellets(-Vector3.UnitZ, 5, 10), F.World.Casts.Select(c => c.Direction), new Near(1e-5f));
        Assert.Equal(5, hits.Count);
        Assert.Equal(75f, F.Survival.View(You.Peer)!.Health);
    }

    [Fact]
    public void SplashHurtsEverythingNearbyLessFurtherOut()
    {
        F.Pockets(Me).Add(Bomb, 2);
        F.World.Feet[3] = new Vector3(0, 0, -6f); // centre at (0, 0.8, -6): 2 m from the blast
        var block = F.Add(Block, new Vector3(-0.125f, -0.125f, -8.125f)); // centre 0 0 -8: 2.15 m off
        var tree = new WorldObjectId(0, 1, 1);
        F.World.Objects[tree] = new WorldObjectPlacement(tree, new WorldObjectDefinition { Id = "tree", Health = 30, Drops = [new ObjectDrop { Item = Scrap, Count = 3 }] },
            new Vector3(0, 0, -8), 0, 1);
        F.World.Ray = (_, _) => new RayHit(new Vector3(0, 0.8f, -8));

        var hits = _rig.Used(Me, F.SlotOf(Me, Bomb), new Vec3(0, -0.1f, -1))["hits"]!.AsArray();

        Assert.Equal(1, F.Pockets(Me).Count(Bomb)); // a thrown weapon is its own ammo
        Assert.Equal(80f, F.Survival.View(You.Peer)!.Health, 2); // 40 × (1 - 2/4)
        Assert.Equal(100f, F.Survival.View(Me.Peer)!.Health); // 8 m away
        Assert.Contains(hits, h => h!["kind"]!.GetValue<string>() == "part" && h["creation"]!.GetValue<int>() == block);
        Assert.True(F.World.State.IsDestroyed(tree)); // 0.8 m off: 32 damage
    }

    [Fact]
    public void ASplashWithNothingHitGoesOffAtTheEndOfItsRange()
    {
        F.Pockets(Me).Add(Bomb, 1);
        F.World.Feet[3] = new Vector3(0, -0.2f, -7f); // centre (0, 0.6, -7): 1 m below the end of the range (10 m)
        _rig.Used(Me, F.SlotOf(Me, Bomb), Ahead);
        Assert.Equal(70f, F.Survival.View(You.Peer)!.Health, 2); // 40 × (1 - 1/4)
    }

    [Fact]
    public void WithPvpOffOrInCreativeShotsDoNotHurtPlayers()
    {
        foreach (var fixture in new[] { new SurvivalFixture(new SurvivalSettings { Pvp = false }), new SurvivalFixture(mode: GameMode.Creative) })
        {
            using var rig = new ItemRig(fixture);
            fixture.Pockets(Me).Add(Blaster, 1);
            fixture.Pockets(Me).Add(Crop, 1);
            fixture.World.Ray = (_, _) => new RayHit(Vector3.Zero, Player: You.Peer);
            var hit = rig.Used(Me, fixture.SlotOf(Me, Blaster), Ahead)["hits"]![0]!;
            Assert.Equal(0.0, hit["damage"]!.GetValue<double>());
            Assert.Equal(100f, fixture.Survival.View(You.Peer)!.Health);
        }
    }

    // ---- Hooks and the runtime ----

    [Fact]
    public void EquippingCallsTheHooksAndDeathEmptiesTheHand()
    {
        _rig.Write("scripts/wand.lua", """
            state.log = state.log or {}
            function on_equip() table.insert(state.log, "equip " .. item.slot()) end
            function on_unequip() table.insert(state.log, "unequip") end
            function on_use(target) table.insert(state.log, "use"); return state.log end
            """);
        var wand = _rig.AddItem("wand", "scripts/wand.lua");
        F.Pockets(Me).Add(wand, 1);
        F.Pockets(Me).Add(Crop, 1);
        var slot = F.SlotOf(Me, wand);

        F.Ok(Me, ScriptRuntime.EquipItem, new { slot });
        F.Ok(Me, ScriptRuntime.EquipItem, new { slot }); // the same again changes nothing
        Assert.Equal((wand, slot), _rig.Runtime.Held[Me.Peer]);
        F.Ok(Me, ScriptRuntime.EquipItem, new { });
        Assert.False(_rig.Runtime.Held.ContainsKey(Me.Peer));
        var log = _rig.Used(Me, slot); // using equips it first
        Assert.Equal([$"equip {slot}", "unequip", $"equip {slot}", "use"], log.AsArray().Select(n => n!.GetValue<string>()));

        F.Survival.Damage(Me.Peer, 1000, "test");
        Assert.False(_rig.Runtime.Held.ContainsKey(Me.Peer));
        Assert.Single(_rig.Runtime.ItemInstances);
        F.Survival.Leave(Me.Peer);
        _rig.Runtime.Update(1f / 60);
        Assert.Empty(_rig.Runtime.ItemInstances);
    }

    [Fact]
    public void CanUseMayNotChangeTheWorld()
    {
        _rig.Write("scripts/cheat.lua", """
            function can_use(target) holder.heal(10) end
            function on_use(target) end
            """);
        var cheat = _rig.AddItem("cheat", "scripts/cheat.lua");
        F.Pockets(Me).Add(cheat, 1);
        F.Survival.Damage(Me.Peer, 50, "test");

        var reason = _rig.Refused(Me, F.SlotOf(Me, cheat));

        Assert.StartsWith("The Cheat's script is stopped:", reason);
        Assert.Contains("holder.heal changes the world", reason);
        Assert.Equal(50f, F.Survival.HealthOf(Me.Peer));
        Assert.Contains(_rig.Runtime.ItemInstances, i => i.Fault is not null);
    }

    [Fact]
    public void CanUseRefusesWithItsReason()
    {
        _rig.Write("scripts/picky.lua", """
            function can_use(target) if target.direction.y < 0 then return "Aim higher." end end
            function on_use(target) return { from = target.from } end
            """);
        var picky = _rig.AddItem("picky", "scripts/picky.lua");
        F.Pockets(Me).Add(picky, 1);
        F.World.Looks[Me.Peer] = Vector3.Normalize(new Vector3(0, -0.1f, -1));
        Assert.Equal("Aim higher.", _rig.Refused(Me, F.SlotOf(Me, picky)));
        F.World.Looks[Me.Peer] = -Vector3.UnitZ;
        Assert.Equal(1.6, _rig.Used(Me, F.SlotOf(Me, picky))["from"]!["y"]!.GetValue<double>(), 4);
    }

    [Fact]
    public void ProjectilesLandAndCallOnHit()
    {
        _rig.Write("scripts/lobber.lua", """
            function on_use(target)
              return combat.projectile(target.from, target.direction * 10, { gravity = vec3(0, -10, 0), data = { power = 25 } })
            end
            function on_hit(landing)
              state.landed = landing.position
              if landing.hit then combat.hit(landing.hit, landing.data.power) end
            end
            """);
        var lobber = _rig.AddItem("lobber", "scripts/lobber.lua");
        F.Pockets(Me).Add(lobber, 1);
        // Anything the flight crosses below y = 1 is the other player.
        F.World.Ray = (start, direction) => start.Y + direction.Y * F.World.LastRange < 1 ? new RayHit(start with { Y = 1 }, Player: You.Peer) : null;

        var id = _rig.Used(Me, F.SlotOf(Me, lobber), Ahead).GetValue<int>();
        Assert.Contains(F.Survival.Projectiles, p => p.Id == id && p.Owner == Me.Peer);
        for (var i = 0; i < 120 && F.Survival.Projectiles.Any(); i++)
            F.Survival.Tick(1f / 60);

        Assert.Empty(F.Survival.Projectiles);
        Assert.Equal(75f, F.Survival.View(You.Peer)!.Health);
    }

    [Fact]
    public void ALobbedShotFliesThenBurstsWhereItLandsOrWhenItsFuseRunsOut()
    {
        // The Popper's way (D14): thrown at 10 m/s, bursting after 0.5 s in the air (about 5 m ahead).
        var popper = _rig.AddItem("popper", Gun, new JsonObject { ["damage"] = 40, ["interval"] = 1, ["speed"] = 10, ["fuse"] = 0.5,
            ["ammo"] = "popper", ["radius"] = 3 }, PartCategory.Weapon);
        F.Pockets(Me).Add(popper, 2);
        F.World.Feet[3] = new Vector3(0, 0, -2); // under where it bursts

        var thrown = _rig.Used(Me, F.SlotOf(Me, popper), Ahead)["projectiles"]!.AsArray();
        var id = Assert.Single(thrown)!.GetValue<int>();
        Assert.Equal(1, F.Pockets(Me).Count(popper));
        Assert.Equal(100f, F.Survival.View(You.Peer)!.Health); // nothing is hurt until it lands
        F.Survival.Tick(0.25f);
        Assert.Contains(F.Survival.Projectiles, p => p.Id == id);
        F.Survival.Tick(0.25f);

        Assert.Empty(F.Survival.Projectiles);
        Assert.InRange(F.Survival.View(You.Peer)!.Health, 1f, 99f);
        Assert.Equal(100f, F.Survival.View(Me.Peer)!.Health); // 5 m back: out of the burst
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    [Fact]
    public void ProjectilesAreCappedAndTheirLifetimeChecked()
    {
        using var rig = new ItemRig(settings: new ScriptSettings { MaxProjectiles = 1, MaxProjectileLifetime = 2 });
        rig.Write("scripts/spam.lua", """
            function on_use(target)
              local first = combat.projectile(target.from, target.direction, { gravity = vec3(0, 0, 0) })
              local second, why = combat.projectile(target.from, target.direction)
              return { first = first, second = second, why = why, bad = pcall(combat.projectile, target.from, target.direction, { lifetime = 3 }) }
            end
            """);
        var spam = rig.AddItem("spam", "scripts/spam.lua");
        rig.F.Pockets(Me).Add(spam, 1);
        var result = rig.Used(Me, rig.F.SlotOf(Me, spam));
        Assert.Equal(1, result["first"]!.GetValue<int>());
        Assert.False(result["second"]!.GetValue<bool>());
        Assert.Contains("already 1 projectiles", result["why"]!.GetValue<string>());
        Assert.False(result["bad"]!.GetValue<bool>());
    }

    [Fact]
    public void PlacedPartsScriptsAreNotHeldItems()
    {
        F.Catalog.Add("test:gadget", new PartDefinition
        {
            Id = "gadget", Name = "Gadget", Category = PartCategory.Interactive, Mass = 1, Size = GridSize.One, StackSize = 5,
            Script = "scripts/food.lua",
        });
        F.Pockets(Me).Add("test:gadget", 1);
        Assert.Equal("Gadget cannot be used.", _rig.Refused(Me, F.SlotOf(Me, "test:gadget")));
    }

    [Fact]
    public void ItemLibrariesOnlyWorkInItemScripts()
    {
        _rig.Write("scripts/probe.lua", """
            function on_use(target)
              return { part = pcall(part.id), heal = holder.health(), id = item.id(), name = item.name("crop"), count = item.count(),
                       carried = inventory.count("crop"), pellets = #combat.pellets(target.direction, 3, 5) }
            end
            """);
        var probe = _rig.AddItem("probe", "scripts/probe.lua", new JsonObject());
        F.Pockets(Me).Add(probe, 2);
        F.Pockets(Me).Add(Crop, 4);
        var result = _rig.Used(Me, F.SlotOf(Me, probe));
        Assert.False(result["part"]!.GetValue<bool>());
        Assert.Equal(100.0, result["heal"]!.GetValue<double>());
        Assert.Equal((probe, "Crop", 2, 4, 3), (result["id"]!.GetValue<string>(), result["name"]!.GetValue<string>(),
            result["count"]!.GetValue<int>(), result["carried"]!.GetValue<int>(), result["pellets"]!.GetValue<int>()));
    }

    [Fact]
    public void HotReloadKeepsEachPlayersItemState()
    {
        _rig.Write("scripts/counter.lua", "function on_use(target) state.n = (state.n or 0) + 1; return state.n end");
        var counter = _rig.AddItem("counter", "scripts/counter.lua");
        F.Pockets(Me).Add(counter, 1);
        F.Pockets(You).Add(counter, 1);
        var mine = F.SlotOf(Me, counter);
        Assert.Equal(1, _rig.Used(Me, mine).GetValue<int>());
        Assert.Equal(2, _rig.Used(Me, mine).GetValue<int>());
        Assert.Equal(1, _rig.Used(You, F.SlotOf(You, counter)).GetValue<int>());

        _rig.Write("scripts/counter.lua", "function on_use(target) state.n = state.n + 10; return state.n end");
        var reload = Assert.Single(_rig.Runtime.CheckForChanges());
        Assert.True(reload.Ok, reload.Error);
        Assert.Equal(2, reload.Instances);
        Assert.Equal(12, _rig.Used(Me, mine).GetValue<int>());
    }

    // ---- Farming (mods/vanilla/scripts/seed.lua and watering.lua) ----

    [Fact]
    public void ASeedIsPlantedAndWateredFromTheHand()
    {
        var soil = F.Add(Soil, new Vector3(0, 0, 1));
        var on = (soil, 1);
        F.Pockets(Me).Add(Bucket, 3);
        var seed = F.SlotOf(Me, Seed);
        var bucket = F.SlotOf(Me, Bucket);

        Assert.Equal("Aim at soil to plant the Seed.", _rig.Refused(Me, seed));
        Assert.Equal("Aim at soil to water it.", _rig.Refused(Me, bucket));
        Assert.Equal("creation and part go together.", F.Dispatch(Me, ScriptRuntime.UseItem, new { slot = seed, creation = soil }).Message);
        Assert.Equal("There is no part 7 in creation " + soil + ".", _rig.Refused(Me, seed, on: (soil, 7)));

        var planted = _rig.Used(Me, seed, on: on);
        Assert.Equal((Seed, Crop, 0, false, 60.0), (planted["seed"]!.GetValue<string>(), planted["crop"]!.GetValue<string>(),
            planted["stage"]!.GetValue<int>(), planted["ripe"]!.GetValue<bool>(), planted["capacity"]!.GetValue<double>()));
        Assert.Equal(1, F.Pockets(Me).Count(Seed));
        Assert.Equal("Something is already growing there.", _rig.Refused(Me, seed, on: on));

        Assert.Equal(30.0, _rig.Used(Me, bucket, on: on)["water"]!.GetValue<double>());
        Assert.Equal(60.0, _rig.Used(Me, bucket, on: on)["water"]!.GetValue<double>());
        Assert.Equal("The soil is already full of water.", _rig.Refused(Me, bucket, on: on));
        Assert.Equal(1, F.Pockets(Me).Count(Bucket));

        _rig.Runtime.Update(1); // the soil's script starts and stamps the soil
        _rig.Runtime.Update(20); // growth 2: ripe after 20 s
        var view = F.Survival.SoilViewOf(F.World.FindCreation(soil)!, 1);
        Assert.True(view.Ripe);
        Assert.Equal(40f, view.Water); // a second of water per second of growing
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    [Fact]
    public void FarmingFromTheHandNeedsSoilInReach()
    {
        var far = F.Add(Soil, new Vector3(0, 0, 30));
        var block = F.Add(Block, new Vector3(0, 0, 1));
        F.Pockets(Me).Add(Bucket, 1);
        Assert.StartsWith("That is ", _rig.Refused(Me, F.SlotOf(Me, Seed), on: (far, 1)));
        Assert.Equal("The Block is not soil.", _rig.Refused(Me, F.SlotOf(Me, Bucket), on: (block, 1)));
        Assert.Equal(2, F.Pockets(Me).Count(Seed));
    }

    [Fact]
    public void FarmFunctionsActOnlyInOnUse()
    {
        var soil = F.Add(Soil, new Vector3(0, 0, 1));
        _rig.Write("scripts/sneaky.lua", """
            function can_use(target)
              farm.water(target.creation, target.part, 10)
            end
            """);
        var sneaky = _rig.AddItem("sneaky", "scripts/sneaky.lua");
        F.Pockets(Me).Add(sneaky, 1);
        Assert.Contains("farm.water changes the world", _rig.Refused(Me, F.SlotOf(Me, sneaky), on: (soil, 1)));
        Assert.Equal(0f, F.World.FindCreation(soil)!.SoilOf(1)!.Water);
    }

    private sealed class Near(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector3 v) => 0;
    }
}
