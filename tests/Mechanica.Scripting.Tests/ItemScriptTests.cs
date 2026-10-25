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
    public void ADrinkHealsAndTheNextWaitsUntilItIsDrunk()
    {
        // Consumables batch C2: eat_seconds holds the next one back (less a little slack); drink says it is a drink.
        var tea = _rig.AddItem("tea", "scripts/food.lua", new JsonObject { ["health"] = 20, ["drink"] = true, ["eat_seconds"] = 1 });
        var bread = _rig.AddItem("bread", "scripts/food.lua", new JsonObject { ["health"] = 5, ["eat_seconds"] = 1 });
        F.Pockets(Me).Add(tea, 3);
        F.Pockets(Me).Add(bread, 2);
        F.Survival.Damage(Me.Peer, 60, "test");

        var drunk = _rig.Used(Me, F.SlotOf(Me, tea));
        Assert.Equal((60.0, true), (drunk["health"]!.GetValue<double>(), drunk["drink"]!.GetValue<bool>()));
        Assert.Equal("You are still drinking.", _rig.Refused(Me, F.SlotOf(Me, tea)));
        // Each food keeps its own clock.
        Assert.False(_rig.Used(Me, F.SlotOf(Me, bread))["drink"]!.GetValue<bool>());
        Assert.Equal("You are still eating.", _rig.Refused(Me, F.SlotOf(Me, bread)));

        _rig.Runtime.Update(0.85f); // past 0.8 s: the host allows for jitter
        Assert.Equal(85.0, _rig.Used(Me, F.SlotOf(Me, tea))["health"]!.GetValue<double>());
        Assert.Equal(1, F.Pockets(Me).Count(tea));
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    [Fact]
    public void TheDeadCannotEat()
    {
        F.Pockets(Me).Add(Crop, 1);
        F.Survival.Damage(Me.Peer, 1000, "test");
        Assert.Equal("You are dead. Respawn first.", _rig.Refused(Me, 0));
    }

    [Fact]
    public void AFoodWithNegativeHealthHurtsEvenAtFullHealth()
    {
        // Crops batch K3: the Chili. food.lua takes it through holder.hurt, the normal damage path.
        var chili = _rig.AddItem("chili", "scripts/food.lua", new JsonObject { ["health"] = -50, ["eat_seconds"] = 1 });
        F.Pockets(Me).Add(chili, 3);
        var hurt = new List<(int Peer, float Amount, string Cause)>();
        F.Survival.Hurt += (peer, amount, cause) => hurt.Add((peer, amount, cause));

        Assert.Equal(50.0, _rig.Used(Me, F.SlotOf(Me, chili))["health"]!.GetValue<double>());
        Assert.Equal([(Me.Peer, 50f, "food")], hurt);
        Assert.Equal("You are still eating.", _rig.Refused(Me, F.SlotOf(Me, chili)));
        _rig.Runtime.Update(0.85f);
        // At 50 health or less the second one kills, as any other blow.
        Assert.Equal(0.0, _rig.Used(Me, F.SlotOf(Me, chili))["health"]!.GetValue<double>());
        Assert.True(F.Survival.IsDead(Me.Peer));
        Assert.Equal(0, F.Pockets(Me).Count(chili)); // the last one went into the knockout bag with the rest of the pockets
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    [Fact]
    public void HolderHurtChecksItsArgumentsAndOnlyActsInOnUse()
    {
        _rig.Write("scripts/thorn.lua", """
            function on_use(target)
              local ok, why = pcall(holder.hurt, -1)
              local ok2, why2 = pcall(holder.hurt, 5, "Not A Word")
              return { left = holder.hurt(10, "thorn"), negative = why, cause = why2 }
            end
            """);
        _rig.Write("scripts/sneak.lua", """
            function can_use(target) holder.hurt(10) end
            function on_use(target) end
            """);
        var thorn = _rig.AddItem("thorn", "scripts/thorn.lua");
        var sneak = _rig.AddItem("sneak", "scripts/sneak.lua");
        F.Pockets(Me).Add(thorn, 1);
        F.Pockets(Me).Add(sneak, 1);
        var causes = new List<string>();
        F.Survival.Hurt += (_, _, cause) => causes.Add(cause);

        var used = _rig.Used(Me, F.SlotOf(Me, thorn));
        Assert.Equal(90.0, used["left"]!.GetValue<double>());
        Assert.Contains("amount must be 0 or more", used["negative"]!.GetValue<string>());
        Assert.Contains("cause must be a short lowercase word", used["cause"]!.GetValue<string>());
        Assert.Equal(["thorn"], causes);

        Assert.Contains("holder.hurt changes the world", _rig.Refused(Me, F.SlotOf(Me, sneak)));
        Assert.Equal(90f, F.Survival.HealthOf(Me.Peer));
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
    public void WithFriendlyFireOffOrInCreativeShotsDoNotHurtPlayers()
    {
        foreach (var fixture in new[] { new SurvivalFixture(friendlyFire: false), new SurvivalFixture(mode: GameMode.Creative) })
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
    public void AHeldItemUpdatesAndHearsAiming()
    {
        _rig.Write("scripts/charm.lua", """
            state.log = state.log or {}
            function on_update(dt) state.held = (state.held or 0) + 1; holder.heal(1) end
            function on_aim(on) table.insert(state.log, on and "aim" or "rest") end
            function on_use(target) return { held = state.held, log = state.log } end
            """);
        var charm = _rig.AddItem("charm", "scripts/charm.lua");
        F.Pockets(Me).Add(charm, 1);
        F.Pockets(Me).Add(Crop, 1);
        var slot = F.SlotOf(Me, charm);
        F.Survival.Damage(Me.Peer, 10, "test");

        _rig.Runtime.Update(1f / 60); // not in hand yet
        F.Ok(Me, ScriptRuntime.EquipItem, new { slot });
        _rig.Runtime.Update(1f / 60);
        _rig.Runtime.Update(1f / 60);
        F.Ok(Me, ScriptRuntime.AimItem, new { on = true });
        F.Ok(Me, ScriptRuntime.AimItem, new { on = true }); // still aiming: no second call
        F.Ok(Me, ScriptRuntime.AimItem, new { on = false });
        F.Ok(Me, ScriptRuntime.EquipItem, new { });
        _rig.Runtime.Update(1f / 60); // put away

        var seen = _rig.Used(Me, slot);
        Assert.Equal(2.0, seen["held"]!.GetValue<double>());
        Assert.Equal(["aim", "rest"], seen["log"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(92f, F.Survival.HealthOf(Me.Peer));
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
    public void ModScriptsLaunchProjectilesAndSplash()
    {
        _rig.Write("scripts/mortar.lua", """
            function on_create()
              state.id = combat.projectile(vec3(0, 5, 0), vec3(0, 0, -10), { gravity = vec3(0, -10, 0), data = { radius = 3 } })
            end
            function on_hit(landing)
              state.hits = #combat.splash(landing.position, landing.data.radius, 50)
            end
            """);
        _rig.ModScripts.Add("scripts/mortar.lua");
        F.World.Feet[You.Peer] = new Vector3(0, 0, -10);
        F.World.Ray = (start, direction) => start.Y + direction.Y * F.World.LastRange < 0 ? new RayHit(new Vector3(start.X, 0, start.Z)) : null;

        _rig.Runtime.Update(1f / 60);
        var id = _rig.Runtime.ExportModStates()["test:scripts/mortar.lua"]["id"]!.GetValue<double>();
        Assert.Contains(F.Survival.Projectiles, p => p.Id == (int)id && p.Owner == 0); // a script's shot spares no player
        for (var i = 0; i < 120 && F.Survival.Projectiles.Any(); i++)
            F.Survival.Tick(1f / 60);

        Assert.Empty(F.Survival.Projectiles);
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
        Assert.True(_rig.Runtime.ExportModStates()["test:scripts/mortar.lua"]["hits"]!.GetValue<double>() >= 1);
        Assert.True(F.Survival.View(You.Peer)!.Health < 100f);
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

    [Fact]
    public void EachPlayersItemStateIsSavedAndComesBack()
    {
        _rig.Write("scripts/counter.lua", "n_at_setup = state.n; function on_use(target) state.n = (state.n or 0) + 1; return state.n end");
        var counter = _rig.AddItem("counter", "scripts/counter.lua");
        F.Pockets(Me).Add(counter, 1);
        F.Pockets(You).Add(counter, 1);
        var mine = F.SlotOf(Me, counter);
        _rig.Used(Me, mine);
        _rig.Used(Me, mine);
        Assert.Empty(_rig.Runtime.ExportItemStates(You.Peer));
        var saved = _rig.Runtime.ExportItemStates(Me.Peer);
        Assert.Equal(2, saved[counter]["n"]!.GetValue<double>());

        // The player comes back as another peer: the state waits for their first use, and is there before setup.
        _rig.Runtime.ImportItemStates(You.Peer, saved);
        Assert.Equal(2, _rig.Runtime.ExportItemStates(You.Peer)[counter]["n"]!.GetValue<double>());
        Assert.Equal(3, _rig.Used(You, F.SlotOf(You, counter)).GetValue<int>());

        // A script already running takes it at once; forgetting a peer drops what was waiting.
        _rig.Runtime.ImportItemStates(Me.Peer, new Dictionary<string, JsonObject> { [counter] = new() { ["n"] = 40 } });
        Assert.Equal(41, _rig.Used(Me, mine).GetValue<int>());
        _rig.Runtime.ImportItemStates(99, saved);
        _rig.Runtime.ForgetItemStates(99);
        Assert.Empty(_rig.Runtime.ExportItemStates(99));
    }

    // ---- Farming (mods/vanilla/scripts/seed.lua and watering.lua) ----

    [Fact]
    public void ASeedIsPlantedAndWateredFromTheHand()
    {
        var soil = F.Add(Soil, new Vector3(0, 0, 1));
        var on = (soil, 1);
        var seed = F.SlotOf(Me, Seed);

        Assert.Equal("Aim at soil to plant the Seed.", _rig.Refused(Me, seed));
        Assert.Equal("creation and part go together.", F.Dispatch(Me, ScriptRuntime.UseItem, new { slot = seed, creation = soil }).Message);
        Assert.Equal("There is no part 7 in creation " + soil + ".", _rig.Refused(Me, seed, on: (soil, 7)));

        var planted = _rig.Used(Me, seed, on: on);
        Assert.Equal((Seed, Crop, 0, false, 60.0), (planted["seed"]!.GetValue<string>(), planted["crop"]!.GetValue<string>(),
            planted["stage"]!.GetValue<int>(), planted["ripe"]!.GetValue<bool>(), planted["capacity"]!.GetValue<double>()));
        Assert.Equal(1, F.Pockets(Me).Count(Seed));
        Assert.Equal("Something is already growing there.", _rig.Refused(Me, seed, on: on));

        F.Survival.WaterSoil(Me, soil, 1, 60);

        _rig.Runtime.Update(1); // the soil's script starts and stamps the soil
        _rig.Runtime.Update(20); // growth 2: ripe after 20 s
        var view = F.Survival.SoilViewOf(F.World.FindCreation(soil)!, 1);
        Assert.True(view.Ripe);
        Assert.Equal(40f, view.Water); // a second of water per second of growing
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    // Me stands at (0, 0, 3) with eyes 1.6 m up: thrown straight ahead, the water lands on the ground about 6 m out,
    // past the 5 m reach (where it lands is the host's to say, so it still acts there).
    private static readonly Vector3 Landing = new(0, 0, -3.05f);

    // Flies every projectile in flight until it lands on the ground (y = 0).
    private void Fly()
    {
        F.World.Ray = (from, direction) => direction.Y < 0 && from.Y / -direction.Y <= F.World.LastRange ? new RayHit(from + direction * (from.Y / -direction.Y)) : null;
        for (var i = 0; i < 240 && F.Survival.Projectiles.Any(); i++)
            F.Survival.Tick(1f / 60);
        Assert.Empty(F.Survival.Projectiles);
    }

    [Fact]
    public void AThrownBucketWatersTheSoilWhereItLandsAndComesBackEmpty()
    {
        var soil = F.Add(Soil, Landing);
        var dry = F.Add(Soil, new Vector3(0, 0, 1));
        F.Pockets(Me).Add(Bucket, 1);
        var slot = F.SlotOf(Me, Bucket);

        var thrown = _rig.Used(Me, slot, Ahead);
        Assert.True(thrown["thrown"]!.GetValue<int>() > 0);
        var flight = Assert.Single(F.Survival.Projectiles);
        Assert.Equal(ProjectileView.Water, flight.Look);
        // The bucket empties as the water leaves it, and stays in the same slot.
        Assert.Equal(EmptyBucket, F.Pockets(Me)[slot]!.Item);
        Fly();

        Assert.Equal(30f, F.World.FindCreation(soil)!.SoilOf(1)!.Water);
        Assert.Equal(0f, F.World.FindCreation(dry)!.SoilOf(1)!.Water);
        Assert.Equal("Aim at water to fill the bucket.", _rig.Refused(Me, slot, Ahead));
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    // ---- The Chemical Bucket (mods/vanilla/scripts/chemical_bucket.lua, Parts map E11) ----

    // A tank that takes only Scrap (as the Chemical Container takes only Chemical), and a bucket of Scrap that pours 5. It pours
    // into any container that takes its item (a chest takes anything); the tank's limit is what refuses it.
    private (string Bucket, int Tank) ChemicalRig(Vector3 tankAt)
    {
        const string tank = "test:tank";
        F.Catalog.Add(tank, F.Catalog.FindDefinition(Chest)! with
        {
            Id = "tank", Name = "Tank", Inventory = new Mechanica.Core.Data.InventoryDefinition { Slots = 1, Accepts = [Scrap] },
        });
        var bucket = _rig.AddItem("goo_bucket", "scripts/chemical_bucket.lua", new JsonObject
        {
            ["item"] = "scrap", ["count"] = 5, ["empty"] = "empty_bucket", ["range"] = 8, ["throw_speed"] = 7, ["throw_up"] = 0.35, ["splash"] = 1.2,
        });
        F.Pockets(Me).Add(bucket, 1);
        return (bucket, F.Add(tank, tankAt));
    }

    [Fact]
    public void AChemicalBucketPoursIntoAContainerThatTakesItAndComesBackEmpty()
    {
        var (bucket, tank) = ChemicalRig(new Vector3(0, 0, 1));
        var slot = F.SlotOf(Me, bucket);
        var contents = F.Inventories.Find(Mechanica.Core.Items.InventoryRef.OfPart(tank, 1))!;

        Assert.Equal(5, _rig.Used(Me, slot, Ahead, on: (tank, 1))["poured"]!.GetValue<int>());
        Assert.Equal(5, contents.Count(Scrap));
        Assert.Equal(EmptyBucket, F.Pockets(Me)[slot]!.Item);
        Assert.Empty(F.Survival.Projectiles);

        // A tank with less room than a bucketful is refused before anything is poured.
        contents.Add(Scrap, 43);
        F.Pockets(Me).SetSlot(slot, new Mechanica.Core.Items.ItemStack(bucket, 1));
        Assert.Equal("There is no room for it there.", _rig.Refused(Me, slot, Ahead, on: (tank, 1)));
        Assert.Equal(48, contents.Count(Scrap));
        Assert.Equal(bucket, F.Pockets(Me)[slot]!.Item);
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    [Fact]
    public void AChemicalBucketAimedAtNothingThatTakesItThrowsTheChemical()
    {
        var (bucket, tank) = ChemicalRig(new Vector3(0, 0, 1));
        var slot = F.SlotOf(Me, bucket);

        // Aimed past the tank, it is thrown, green.
        var thrown = _rig.Used(Me, slot, Ahead);
        Assert.True(thrown["thrown"]?.GetValue<int>() > 0, thrown.ToJsonString());
        Assert.Equal(ProjectileView.Chemical, Assert.Single(F.Survival.Projectiles).Look);
        Assert.Equal(EmptyBucket, F.Pockets(Me)[slot]!.Item);
        Assert.Equal(0, F.Inventories.Find(Mechanica.Core.Items.InventoryRef.OfPart(tank, 1))!.Count(Scrap));
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    [Fact]
    public void AThrownBucketLandsOnWhatItIsAimedAt()
    {
        var aimed = F.Add(Soil, new Vector3(0, 0, 0.5f));
        var beyond = F.Add(Soil, Landing);
        F.Pockets(Me).Add(Bucket, 1);
        F.World.Ray = (_, _) => new RayHit(new Vector3(0, 0, 0.5f), Creation: aimed, Part: 1);

        _rig.Used(Me, F.SlotOf(Me, Bucket), Ahead);
        Fly();

        Assert.Equal(30f, F.World.FindCreation(aimed)!.SoilOf(1)!.Water);
        Assert.Equal(0f, F.World.FindCreation(beyond)!.SoilOf(1)!.Water);
    }

    [Fact]
    public void AThrownBucketPutsOutTheFireItLandsOn()
    {
        F.Fire.LightSites([new Mechanica.Core.Quests.QuestSite(0, "test:camp", Vector3.Zero, 10,
            [new Mechanica.Core.Quests.QuestMarker(0, "fire", null, Landing), new Mechanica.Core.Quests.QuestMarker(1, "fire", null, new Vector3(0, 0, -12))])]);
        F.Pockets(Me).Add(Bucket, 1);

        _rig.Used(Me, F.SlotOf(Me, Bucket), Ahead);
        Fly();

        Assert.Equal([0f, 1f], F.Fire.Fires().Select(f => f.Strength));
        Assert.Equal(1, F.Fire.SitesOut("test:camp", "fire"));
        Assert.Equal(0, F.Pockets(Me).Count(Bucket));
        Assert.Equal(1, F.Pockets(Me).Count(EmptyBucket));
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    // ---- The Fire Extinguisher (mods/vanilla/scripts/extinguisher.lua, Consumables batch C2a) and held Use (C8) ----

    private const float Step = 1f / 60;

    private string Extinguisher() => _rig.AddItem("extinguisher", "scripts/extinguisher.lua", new JsonObject
    {
        ["spray_seconds"] = 2, ["range"] = 5, ["angle"] = 25, ["pad"] = 0.6, ["douse"] = 1.5, ["sound_interval"] = 0.4,
    });

    // Me looks along -z from (0, 1.6, 3): a fire 3 m ahead is in the cone, one 5 m to the side and one 15 m ahead are not.
    private void LightThreeFires() => F.Fire.LightSites([new Mechanica.Core.Quests.QuestSite(0, "test:camp", Vector3.Zero, 20,
        [new Mechanica.Core.Quests.QuestMarker(0, "fire", null, Vector3.Zero), new Mechanica.Core.Quests.QuestMarker(1, "fire", null, new Vector3(5, 0, 0)),
            new Mechanica.Core.Quests.QuestMarker(2, "fire", null, new Vector3(0, 0, -12))])]);

    private void Steps(int count)
    {
        for (var i = 0; i < count; i++)
            _rig.Runtime.Update(Step);
    }

    [Fact]
    public void HoldingUseSpraysOutTheFiresInTheConeAndUsesTheExtinguishersUp()
    {
        LightThreeFires();
        var extinguisher = Extinguisher();
        F.Pockets(Me).Add(extinguisher, 2);
        var slot = F.SlotOf(Me, extinguisher);

        Assert.True(F.Dispatch(Me, ScriptRuntime.UseItem, new { slot, hold = true }).Ok);
        Assert.True(_rig.Runtime.IsUsing(Me.Peer));
        Steps(30); // half a second: 0.75 of the fire ahead gone
        var strengths = F.Fire.Fires().Select(f => f.Strength).ToList();
        Assert.Equal(0.25f, strengths[0], 0.02f);
        Assert.Equal([1f, 1f], strengths[1..]);
        Steps(30);
        Assert.Equal([0f, 1f, 1f], F.Fire.Fires().Select(f => f.Strength));
        Assert.Equal(1, F.Fire.SitesOut("test:camp", "fire"));
        Assert.Equal(2, F.Pockets(Me).Count(extinguisher));
        Steps(61); // two seconds of spraying use one up; the next carries on
        Assert.Equal(1, F.Pockets(Me).Count(extinguisher));

        F.Ok(Me, ScriptRuntime.ReleaseItem, new { });
        Assert.False(_rig.Runtime.IsUsing(Me.Peer));
        Steps(240); // let go: no more is used
        Assert.Equal(1, F.Pockets(Me).Count(extinguisher));

        Assert.True(F.Dispatch(Me, ScriptRuntime.UseItem, new { slot, hold = true }).Ok);
        Steps(121);
        Assert.Equal(0, F.Pockets(Me).Count(extinguisher));
        Steps(10); // nothing left to spray: no error
        Assert.Equal($"Slot {slot} is empty.", _rig.Refused(Me, slot));
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    [Fact]
    public void AUseWithoutHoldDoesNotSpray()
    {
        LightThreeFires();
        var extinguisher = Extinguisher();
        F.Pockets(Me).Add(extinguisher, 1);

        Assert.True(_rig.Used(Me, F.SlotOf(Me, extinguisher))["spraying"]!.GetValue<bool>());
        Assert.False(_rig.Runtime.IsUsing(Me.Peer));
        Steps(180);
        Assert.Equal([1f, 1f, 1f], F.Fire.Fires().Select(f => f.Strength));
        Assert.Equal(1, F.Pockets(Me).Count(extinguisher));
    }

    [Fact]
    public void AHeldUseEndsOnReleaseOrWhenTheItemLeavesTheHand()
    {
        _rig.Write("scripts/hose.lua", """
            state.log = state.log or {}
            function on_use(target) table.insert(state.log, "use") end
            function on_use_release() table.insert(state.log, "release") end
            function on_update(dt) if holder.using() then state.held = (state.held or 0) + 1 end end
            function can_use(target) end
            """);
        var hose = _rig.AddItem("hose", "scripts/hose.lua");
        F.Pockets(Me).Add(hose, 1);
        F.Pockets(Me).Add(Crop, 1);
        var slot = F.SlotOf(Me, hose);

        F.Ok(Me, ScriptRuntime.ReleaseItem, new { }); // not holding: nothing to let go
        Assert.True(F.Dispatch(Me, ScriptRuntime.UseItem, new { slot, hold = true }).Ok);
        Steps(3);
        F.Ok(Me, ScriptRuntime.ReleaseItem, new { slot });
        F.Ok(Me, ScriptRuntime.ReleaseItem, new { }); // twice: one on_use_release
        Steps(3);
        Assert.True(F.Dispatch(Me, ScriptRuntime.UseItem, new { slot, hold = true }).Ok);
        Steps(2);
        F.Ok(Me, ScriptRuntime.EquipItem, new { slot = F.SlotOf(Me, Crop) }); // another item in hand lets go
        Assert.False(_rig.Runtime.IsUsing(Me.Peer));

        _rig.Write("scripts/hose.lua", """
            function on_use(target) return { log = state.log, held = state.held } end
            """);
        Assert.Single(_rig.Runtime.CheckForChanges()); // hot reload: the state stays
        var seen = _rig.Used(Me, slot);
        Assert.Equal(["use", "release", "use", "release"], seen["log"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(5.0, seen["held"]!.GetValue<double>());

        Assert.True(F.Dispatch(Me, ScriptRuntime.UseItem, new { slot, hold = true }).Ok);
        Assert.True(_rig.Runtime.IsUsing(Me.Peer));
        F.Survival.Damage(Me.Peer, 1000, "test"); // dying lets go too
        Assert.False(_rig.Runtime.IsUsing(Me.Peer));
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    [Fact]
    public void ReplacingOneOfAStackMakesRoomForTheNewItem()
    {
        _rig.Write("scripts/swap.lua", "function on_use(target) return item.replace(\"crop\") end");
        var swap = _rig.AddItem("swap", "scripts/swap.lua");
        F.Pockets(Me).Add(swap, 2);
        var slot = F.SlotOf(Me, swap);
        Assert.True(_rig.Used(Me, slot).GetValue<bool>());
        Assert.Equal((1, 1), (F.Pockets(Me).Count(swap), F.Pockets(Me).Count(Crop)));
        Assert.True(_rig.Used(Me, slot).GetValue<bool>());
        Assert.Equal((0, 2), (F.Pockets(Me).Count(swap), F.Pockets(Me).Count(Crop)));
        Assert.Equal(Crop, F.Pockets(Me)[slot]!.Item);
    }

    [Fact]
    public void FireFunctionsChangeTheWorldOnlyInOnUse()
    {
        var plank = F.Add(Plank, new Vector3(0, 0, 1));
        _rig.Write("scripts/torch.lua", """
            function can_use(target)
              if target.part then fire.ignite(target.creation, target.part) end
            end
            function on_use(target)
              local ok, why = fire.ignite(target.creation, target.part)
              return { ok = ok, why = why, strength = fire.strength(target.creation, target.part) }
            end
            """);
        F.Pockets(Me).Add(_rig.AddItem("torch", "scripts/torch.lua"), 1);
        var torch = F.SlotOf(Me, "test:torch");

        Assert.Contains("changes the world", _rig.Refused(Me, torch, on: (plank, 1)));
        _rig.Write("scripts/torch.lua", """
            function on_use(target)
              local ok, why = fire.ignite(target.creation, target.part)
              return { ok = ok, why = why, strength = fire.strength(target.creation, target.part) }
            end
            """);
        _rig.Runtime.CheckForChanges();
        var lit = _rig.Used(Me, torch, on: (plank, 1));
        Assert.True(lit["ok"]!.GetValue<bool>());
        Assert.Equal(0.2, lit["strength"]!.GetValue<double>(), 3);
        var block = F.Add(Block, new Vector3(0, 0, 2));
        Assert.Equal("The Block does not burn.", _rig.Used(Me, torch, on: (block, 1))["why"]!.GetValue<string>());
    }

    [Fact]
    public void FarmingFromTheHandNeedsSoilInReach()
    {
        var far = F.Add(Soil, new Vector3(0, 0, 30));
        var block = F.Add(Block, new Vector3(0, 0, 1));
        Assert.StartsWith("That is ", _rig.Refused(Me, F.SlotOf(Me, Seed), on: (far, 1)));
        Assert.Equal("The Block is not soil.", _rig.Refused(Me, F.SlotOf(Me, Seed), on: (block, 1)));
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
