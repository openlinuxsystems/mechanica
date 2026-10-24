using System.Numerics;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Survival;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Scripting.Tests;

/// <summary>
/// The soil part's Lua script (<c>mods/vanilla/scripts/soil.lua</c>, D2): crops grow from the world clock while
/// watered, catch up after their Creation was parked or saved, and are harvested through <c>ActivatePart</c>;
/// plus the generic primitives it uses (<c>soil</c>, <c>player</c>, <c>world.item</c>, <c>can_activate</c>).
/// </summary>
public sealed class SoilScriptTests : IDisposable
{
    private readonly ItemRig _rig;

    public SoilScriptTests()
    {
        _rig = new ItemRig();
        new LogicActions(new BuildSettings(), F.World, _rig.Runtime, new SeatRegistry()).Register(F.Dispatcher);
    }

    private SurvivalFixture F => _rig.F;

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void ACropGrowsOnlyWhileWateredAndIsHarvestedRipe()
    {
        var soil = F.Add(Soil, new Vector3(0, 0, 1));
        _rig.Runtime.Update(1); // the script starts and stamps the soil
        F.Survival.PlantSeed(Me, soil, 1, Seed);

        _rig.Runtime.Update(100);
        Assert.Equal((0f, 0), (State(soil).Grown, View(soil).Stage)); // dry soil: nothing grows

        F.Survival.WaterSoil(Me, soil, 1, 60);
        _rig.Runtime.Update(10); // growth 2: 20 of 40 s, using 10 s of water
        Assert.Equal((20f, 50f, 2), (State(soil).Grown, View(soil).Water, View(soil).Stage));
        Assert.Equal("The Crop is not ripe yet.", F.Refused(Me, LogicActions.ActivatePart, new LogicPartArgs(soil, 1)));
        Assert.Equal(3f, F.Survival.CropValue());

        _rig.Runtime.Update(100); // ripe after 10 more s; the rest of the water stays
        Assert.True(View(soil).Ripe);
        Assert.Equal(40f, View(soil).Water);

        var items = Activate(Me, soil)["items"]!.AsArray().Select(i => (i!["item"]!.GetValue<string>(), i["count"]!.GetValue<int>()));
        Assert.Equal([(Crop, 2), (Seed, 1)], items);
        Assert.Equal((2, 3), (F.Pockets(Me).Count(Crop), F.Pockets(Me).Count(Seed))); // 2 seeds to start, 1 back
        Assert.Null(View(soil).Seed);
        Assert.Equal(40f, View(soil).Water);
        Assert.Equal(0f, F.Survival.CropValue());
        Assert.Equal("Nothing is growing there.", F.Refused(Me, LogicActions.ActivatePart, new LogicPartArgs(soil, 1)));
        Assert.DoesNotContain(_rig.Messages, m => m.Kind != ScriptMessageKind.Print);
    }

    [Fact]
    public void FertilizerMakesACropGrowFasterOnTheSameWaterUntilTheHarvest()
    {
        // Consumables batch C2: fertilizer.lua boosts the soil, soil.lua multiplies the boost into growth.
        var fertilizer = _rig.AddItem("fertilizer", "scripts/fertilizer.lua", new JsonObject { ["boost"] = 15 });
        F.Pockets(Me).Add(fertilizer, 3);
        var slot = F.SlotOf(Me, fertilizer);
        var soil = F.Add(Soil, new Vector3(0, 0, 1));
        _rig.Runtime.Update(1);

        Assert.Equal("Aim at a growing crop to use the Fertilizer.", _rig.Refused(Me, slot));
        Assert.Equal("Plant something there first.", _rig.Refused(Me, slot, on: (soil, 1)));
        F.Survival.PlantSeed(Me, soil, 1, Seed);
        var used = _rig.Used(Me, slot, on: (soil, 1));
        Assert.Equal(15.0, used["boost"]!.GetValue<double>());
        Assert.Equal((15f, 2), (State(soil).Boost, F.Pockets(Me).Count(fertilizer)));
        Assert.Equal(15f, View(soil).Boost);
        Assert.Equal("That crop is already fertilized.", _rig.Refused(Me, slot, on: (soil, 1)));

        _rig.Runtime.Update(5);
        Assert.Equal(0f, State(soil).Grown); // still needs water
        F.Survival.WaterSoil(Me, soil, 1, 60);
        _rig.Runtime.Update(1); // 1 s of water at 2 x 15: 30 of 40 s of growth
        Assert.Equal((30f, 59f, 3), (State(soil).Grown, View(soil).Water, View(soil).Stage));
        _rig.Runtime.Update(5);
        Assert.True(View(soil).Ripe);
        Assert.Equal("That crop is already ripe.", _rig.Refused(Me, slot, on: (soil, 1)));

        Activate(Me, soil);
        Assert.Equal(1f, State(soil).Boost); // the harvest clears it
        Assert.DoesNotContain(_rig.Messages, m => m.Kind != ScriptMessageKind.Print);
    }

    [Fact]
    public void SoilOnACreationGrowsOnBatteriesAndGivesItsFixedYield()
    {
        // A Growbed-like soil part (E9) built into a Creation with a container: each Scrap (its battery) grows for 10 s.
        F.Catalog.Add("test:bed", F.Catalog.FindDefinition(Soil)! with
        {
            Id = "bed",
            Soil = new SoilDefinition { Capacity = 60, Growth = 2, Yield = 5 },
            Properties = new JsonObject { ["interval"] = 0.5, ["charge_item"] = "scrap", ["charge_time"] = 10 },
        });
        var cart = new Creation(F.World.NextCreationId(), F.Catalog);
        Assert.True(cart.TryPlacePart("test:bed", new GridCell(0, 0, 0), default, out _, out var error), error);
        Assert.True(cart.TryPlacePart(Chest, new GridCell(1, 0, 0), default, out _, out error), error);
        cart.SetAllBodyPoses(new Pose(new Vector3(0, 0, 1), Quaternion.Identity));
        F.World.Added(cart);
        cart.InventoryOf(2)!.Add(Scrap, 1);
        _rig.Runtime.Update(1);
        F.Survival.PlantSeed(Me, cart.Id, 1, Seed);
        F.Survival.WaterSoil(Me, cart.Id, 1, 60);

        _rig.Runtime.Update(100); // one battery: 10 s of growing at speed 2, 20 of 40 s, then it stops
        Assert.Equal((20f, 50f, 2), (State(cart.Id).Grown, View(cart.Id).Water, View(cart.Id).Stage));
        Assert.Equal(0, cart.InventoryOf(2)!.Count(Scrap));
        _rig.Runtime.Update(100);
        Assert.Equal(20f, State(cart.Id).Grown); // no power, no growth, no water used
        Assert.Equal(50f, View(cart.Id).Water);

        cart.InventoryOf(2)!.Add(Scrap, 3);
        _rig.Runtime.Update(100); // ripe on one more battery; the rest stay in the chest
        Assert.True(View(cart.Id).Ripe);
        Assert.Equal(2, cart.InventoryOf(2)!.Count(Scrap));

        var items = Activate(Me, cart.Id)["items"]!.AsArray().Select(i => (i!["item"]!.GetValue<string>(), i["count"]!.GetValue<int>()));
        Assert.Equal([(Crop, 5), (Seed, 1)], items); // the bed's yield, not the seed's 2
        Assert.DoesNotContain(_rig.Messages, m => m.Kind != ScriptMessageKind.Print);
    }

    [Fact]
    public void EmptyWateredSoilDriesOutWatchedThroughItsFourLooks()
    {
        // Full (60 s) soil dries in 100 s with nothing planted, and looks wet from 30 s of water up.
        F.Catalog.Add("test:drying_soil", F.Catalog.FindDefinition(Soil)! with
        {
            Id = "drying_soil", Soil = new SoilDefinition { Capacity = 60, Growth = 2, DryTime = 100, WetLevel = 0.5f },
        });
        var soil = F.Add("test:drying_soil", new Vector3(0, 0, 1));
        _rig.Runtime.Update(1);
        Assert.Equal(SoilWetness.Dry, View(soil).Wetness);

        F.Survival.WaterSoil(Me, soil, 1, 20);
        Assert.Equal(SoilWetness.Watered, View(soil).Wetness);
        F.Survival.WaterSoil(Me, soil, 1, 40);
        Assert.Equal((60f, SoilWetness.Wet), (View(soil).Water, View(soil).Wetness));

        _rig.Runtime.Update(20); // 12 s of water gone
        Assert.Equal((48f, SoilWetness.Wet), (View(soil).Water, View(soil).Wetness));
        var revision = State(soil).Revision;
        _rig.Runtime.Update(35); // 21 more: under the wet level, on its way down
        Assert.Equal(27f, View(soil).Water, 3);
        Assert.Equal(SoilWetness.Drying, View(soil).Wetness);
        Assert.NotEqual(revision, State(soil).Revision); // clients are sent the new look

        _rig.Runtime.Update(100);
        Assert.Equal((0f, SoilWetness.Dry, false), (View(soil).Water, View(soil).Wetness, State(soil).Soaked));
        Assert.True(State(soil).IsEmpty);

        // Planted soil keeps its water for the crop instead of drying.
        F.Survival.WaterSoil(Me, soil, 1, 10);
        F.Survival.PlantSeed(Me, soil, 1, Seed);
        _rig.Runtime.Update(100);
        Assert.Equal(0f, View(soil).Water); // all of it went on growing (speed 2: 20 of 40 s)
        Assert.Equal(20f, State(soil).Grown);
        Assert.DoesNotContain(_rig.Messages, m => m.Kind != ScriptMessageKind.Print);
    }

    [Fact]
    public void AParkedCropCatchesUpWhenItsCreationRunsAgain()
    {
        var parked = F.Add(Soil, new Vector3(0, 0, 1));
        var live = F.Add(Soil, new Vector3(1, 0, 1));
        foreach (var soil in new[] { parked, live })
        {
            F.Survival.PlantSeed(Me, soil, 1, Seed);
            F.Survival.WaterSoil(Me, soil, 1, 15);
        }
        _rig.Runtime.Update(1);

        _rig.Parked.Add(parked);
        for (var i = 0; i < 24; i++)
            _rig.Runtime.Update(0.5f);
        Assert.Equal((0f, 15f), (State(parked).Grown, State(parked).Water)); // nothing ran while parked
        Assert.Equal((24f, 3f, 2), (State(live).Grown, State(live).Water, State(live).Stage));

        _rig.Parked.Remove(parked);
        _rig.Runtime.Update(0.5f);
        _rig.Runtime.Update(0.5f);
        // 13 s worked out in one go: the same crop as one that never parked.
        Assert.Equal((State(live).Grown, State(live).Water, State(live).Stage), (State(parked).Grown, State(parked).Water, State(parked).Stage));
        Assert.Equal((26f, 2f, 2), (State(parked).Grown, State(parked).Water, State(parked).Stage));

        // Parked until long after the water ran out: it grows only for as long as the water lasted.
        _rig.Parked.Add(parked);
        _rig.Runtime.Update(100);
        _rig.Parked.Remove(parked);
        _rig.Runtime.Update(0.5f);
        Assert.Equal((30f, 0f, 3), (State(parked).Grown, State(parked).Water, State(parked).Stage));
    }

    [Fact]
    public void AParkedRipeCropCanBeHarvestedBeforeItRunsAgain()
    {
        var soil = F.Add(Soil, new Vector3(0, 0, 1));
        F.Survival.PlantSeed(Me, soil, 1, Seed);
        F.Survival.WaterSoil(Me, soil, 1, 60);
        _rig.Runtime.Update(1);
        _rig.Parked.Add(soil);
        _rig.Runtime.Update(30);
        Assert.False(View(soil).Ripe); // stored growth waits for the script

        // An agent (no reach needed) harvests it: can_activate and on_activate work growth out first.
        var agent = new ActionCaller(Me.Peer, ActionSource.External);
        Activate(agent, soil);
        Assert.Equal(2, F.Pockets(Me).Count(Crop));
        Assert.Null(View(soil).Seed);
        Assert.Equal(40f, State(soil).Water); // 20 s of growth at speed 2
    }

    [Fact]
    public void EveryHarvestRollsItsSeedsBackFromSeedsToSeedsMax()
    {
        const string ranged = "test:ranged_seed";
        F.Catalog.Add(ranged, new PartDefinition { Id = "ranged_seed", Name = "Ranged Seed", Category = PartCategory.Consumable, StackSize = 50,
            Seed = new SeedDefinition { Crop = Crop, GrowTime = 4, Yield = 1, Seeds = 1, SeedsMax = 3 }, Script = "scripts/seed.lua" });
        var soil = F.Add(Soil, new Vector3(0, 0, 1));
        _rig.Runtime.Update(1);
        var seen = new HashSet<int>();
        for (var harvest = 0; harvest < 40; harvest++)
        {
            F.Pockets(Me).Clear();
            F.Pockets(Me).SetSlot(0, new ItemStack(ranged, 1));
            F.Survival.PlantSeed(Me, soil, 1, ranged);
            F.Survival.WaterSoil(Me, soil, 1, 60);
            _rig.Runtime.Update(5);
            var items = Activate(Me, soil)["items"]!.AsArray();
            var back = items.Single(i => i!["item"]!.GetValue<string>() == ranged)!["count"]!.GetValue<int>();
            Assert.InRange(back, 1, 3);
            seen.Add(back);
        }
        Assert.True(seen.Count > 1, "every harvest gave the same number of seeds");
    }

    [Fact]
    public void AThirstyCropDrinksItsThirstInWaterEverySecondItGrows()
    {
        // Crops map K1: seed.thirst is seconds of water a second of growth uses (default 1).
        const string thirsty = "test:thirsty_seed";
        F.Catalog.Add(thirsty, new PartDefinition { Id = "thirsty_seed", Name = "Thirsty Seed", Category = PartCategory.Consumable, StackSize = 50,
            Seed = new SeedDefinition { Crop = Crop, GrowTime = 40, Yield = 1, Thirst = 2 }, Script = "scripts/seed.lua" });
        var soil = F.Add(Soil, new Vector3(0, 0, 1));
        _rig.Runtime.Update(1);
        F.Pockets(Me).SetSlot(0, new ItemStack(thirsty, 1));
        F.Survival.PlantSeed(Me, soil, 1, thirsty);

        F.Survival.WaterSoil(Me, soil, 1, 20);
        _rig.Runtime.Update(100); // 20 s of water lasts 10 s: 20 of 40 s of growth at speed 2
        Assert.Equal((20f, 0f, 2), (State(soil).Grown, View(soil).Water, View(soil).Stage));

        F.Survival.WaterSoil(Me, soil, 1, 60);
        _rig.Runtime.Update(100); // 10 more s ripens it on 20 s of water
        Assert.True(View(soil).Ripe);
        Assert.Equal(40f, View(soil).Water);
        Assert.DoesNotContain(_rig.Messages, m => m.Kind != ScriptMessageKind.Print);
    }

    [Fact]
    public void AHarvestNeedsRoom()
    {
        var soil = F.Add(Soil, new Vector3(0, 0, 1));
        F.Survival.PlantSeed(Me, soil, 1, Seed);
        F.Survival.WaterSoil(Me, soil, 1, 60);
        _rig.Runtime.Update(1);
        _rig.Runtime.Update(30);
        Assert.True(View(soil).Ripe);
        foreach (var slot in Enumerable.Range(0, 4))
            F.Pockets(Me).SetSlot(slot, new ItemStack(Scrap, 50));

        Assert.Equal("Make room in your inventory first.", F.Refused(Me, LogicActions.ActivatePart, new LogicPartArgs(soil, 1)));
        Assert.Equal(Seed, View(soil).Seed);
    }

    [Fact]
    public void TheWorldClockAndTheSoilStampCarryOverASave()
    {
        var soil = F.Add(Soil, new Vector3(0, 0, 1));
        F.Survival.PlantSeed(Me, soil, 1, Seed);
        F.Survival.WaterSoil(Me, soil, 1, 60);
        _rig.Runtime.Update(1);
        _rig.Parked.Add(soil);
        _rig.Runtime.Update(5); // parked when saved: 5 s of growth not worked out yet
        var json = CreationJson.Write(F.World.FindCreation(soil)!);
        Assert.Contains("\"updated\": 1", json);

        using var loaded = new ItemRig();
        var diagnostics = new List<DataDiagnostic>();
        loaded.F.World.Added(CreationJson.Read(json, loaded.F.Catalog, "save", diagnostics)!);
        Assert.Empty(diagnostics);
        loaded.Runtime.Time = _rig.Runtime.Time; // SaveSync carries the clock (SaveGame.Time)
        loaded.Runtime.Update(1);

        var state = loaded.F.World.FindCreation(soil)!.SoilOf(1)!;
        Assert.Equal((12f, 54f, 1), (state.Grown, state.Water, state.Stage)); // 6 s at speed 2
    }

    [Fact]
    public void PartScriptsGiveItemsAndReadItemData()
    {
        F.Catalog.Add("test:gadget", new PartDefinition
        {
            Id = "gadget", Name = "Gadget", Category = PartCategory.Decor, Mass = 1, Durability = 3, Flammable = false,
            Size = GridSize.One, StackSize = 10, Script = "scripts/gadget.lua",
        });
        _rig.Write("scripts/gadget.lua", """
            function can_activate(peer)
              if state.sneaky then soil.clear() end
              local ok, why = player.can_give(peer, {{item = "crop", count = 40}})
              if not ok then return why end
            end

            function on_activate(peer)
              local seed = world.item("seed", "seed")
              return {
                gave = player.give(peer, {{item = "crop", count = 2}, {item = "test:seed"}}),
                too_much = select(2, player.give(peer, {{item = "crop", count = 200}})),
                nobody = select(2, player.give(1675662328, {{item = "crop"}})),
                crop = seed.crop, grow_time = seed.grow_time,
                name = world.item("crop", "name"), missing = world.item("nothing") == nil,
              }
            end
            """);
        var gadget = F.Add("test:gadget", new Vector3(0, 0, 1));

        var result = Activate(Me, gadget);
        Assert.True(result["gave"]!.GetValue<bool>());
        Assert.Equal("there is no room for it", result["too_much"]!.GetValue<string>()); // all or nothing
        Assert.Equal("there is no player 1675662328", result["nobody"]!.GetValue<string>()); // large peer ids stay exact
        Assert.Equal((Crop, 40.0), (result["crop"]!.GetValue<string>(), result["grow_time"]!.GetValue<double>())); // as in the data
        Assert.Equal("Crop", result["name"]!.GetValue<string>());
        Assert.True(result["missing"]!.GetValue<bool>());
        Assert.Equal((2, 3), (F.Pockets(Me).Count(Crop), F.Pockets(Me).Count(Seed)));

        F.Pockets(Me).Add(Scrap, 150); // fills the two free slots: 40 crop no longer fit
        Assert.Equal("there is no room for it", F.Refused(Me, LogicActions.ActivatePart, new LogicPartArgs(gadget, 1)));

        // can_activate only reads.
        Assert.True(_rig.Runtime.ImportState(gadget, 1, new JsonObject { ["sneaky"] = true }));
        Assert.Contains("soil.clear changes the world", F.Refused(Me, LogicActions.ActivatePart, new LogicPartArgs(gadget, 1)));
    }

    [Fact]
    public void TheSoilLibraryOnlyStoresSoundValues()
    {
        _rig.Write("scripts/soil.lua", """
            function on_activate(peer)
              local ok, why = pcall(soil.set, {stage = 9})
              local ok2, why2 = pcall(soil.set, {water = -1})
              local ok3 = pcall(soil.set, {stage = 1.5})
              soil.set({water = 1000, updated = 12345.25})
              local s = soil.get()
              return {why = why, why2 = why2, ok3 = ok3, water = s.water, updated = s.updated, stages = s.stages}
            end
            """);
        F.Catalog.Add("test:block2", F.Catalog.FindDefinition(Block)! with { Script = "scripts/soil.lua" });
        var soil = F.Add(Soil, new Vector3(0, 0, 1));

        var result = Activate(Me, soil);
        Assert.Contains("stage must be 0 to 4", result["why"]!.GetValue<string>());
        Assert.Contains("water must be 0 or more", result["why2"]!.GetValue<string>());
        Assert.False(result["ok3"]!.GetValue<bool>());
        Assert.Equal((60.0, 12345.25, 4.0), (result["water"]!.GetValue<double>(), result["updated"]!.GetValue<double>(), result["stages"]!.GetValue<double>())); // kept to the capacity

        var block = F.Add("test:block2", new Vector3(1, 0, 1));
        Assert.Contains("is not soil", F.Dispatch(Me, LogicActions.ActivatePart, new LogicPartArgs(block, 1)).Message);
    }

    private JsonNode Activate(ActionCaller caller, int creation)
    {
        var result = F.Dispatch(caller, LogicActions.ActivatePart, new LogicPartArgs(creation, 1));
        Assert.True(result.Ok, result.ToString());
        return JsonNode.Parse(result.Value!.Value.GetRawText())!;
    }

    private SoilState State(int creation) => F.World.FindCreation(creation)!.SoilOf(1)!;

    private SoilView View(int creation) => F.Survival.SoilViewOf(F.World.FindCreation(creation)!, 1);
}
