using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using static Mechanica.Core.Tests.Items.ItemParts;

namespace Mechanica.Core.Tests.Items;

public class CraftingTests
{
    private static readonly ActionCaller Player = new(2, ActionSource.Player);
    private static readonly ActionCaller Other = new(3, ActionSource.Player);

    private readonly PartCatalog _catalog = Catalog();
    private readonly RecipeBook _recipes;
    private readonly FakeWorld _world = new();
    private readonly ActionDispatcher _dispatcher = new();
    private readonly InventoryActions _items;

    public CraftingTests()
    {
        _recipes = Recipes(_catalog);
        _items = new InventoryActions(_catalog, _recipes, new InventorySettings { Reach = 5, PlayerSlots = 4, MaxCraftCount = 5, MaxGiveCount = 100 }, _world);
        _items.Register(_dispatcher);
        _world.Eyes[2] = new Vector3(0, 1.6f, 3);
        _world.Eyes[3] = new Vector3(0, 1.6f, 3);
    }

    // ---- Queue ----

    [Fact]
    public void AQueueFinishesCraftsAtTheStationSpeed()
    {
        var queue = new CraftingQueue();
        var output = new Inventory(_catalog, 2);
        queue.Enqueue(new CraftJob("test:chest", 2));

        Assert.Equal(0, queue.Tick(1.5f, 2, _recipes, output)); // 3 of 4 s
        Assert.Equal(3f, queue.Jobs[0].Progress);
        Assert.Equal(1, queue.Tick(1f, 2, _recipes, output)); // finishes one, 1 s into the next
        Assert.Equal(new CraftJob("test:chest", 1, 1f), queue.Jobs[0]);
        Assert.Equal(1, queue.Tick(10f, 2, _recipes, output));
        Assert.True(queue.IsEmpty);
        Assert.Equal(2, output.Count(Chest));
    }

    [Fact]
    public void ACraftWhoseOutputsDontFitWaitsFinished()
    {
        var queue = new CraftingQueue();
        var output = new Inventory(_catalog, 1);
        output.Add(Scrap, 1);
        queue.Enqueue(new CraftJob("test:blocks", 1));

        Assert.Equal(0, queue.Tick(5f, 1, _recipes, output));
        Assert.True(queue.IsBlocked);
        Assert.Equal(1f, queue.Jobs[0].Progress);
        var revision = queue.Revision;
        queue.Tick(5f, 1, _recipes, output);
        Assert.Equal(revision, queue.Revision); // waiting is not a change

        output.Clear();
        Assert.Equal(1, queue.Tick(0.01f, 1, _recipes, output));
        Assert.Equal(10, output.Count(Block));
        Assert.False(queue.IsBlocked);
    }

    [Fact]
    public void AnOrderForARecipeThatNoLongerExistsIsDropped()
    {
        var queue = new CraftingQueue();
        queue.Enqueue(new CraftJob("gone:thing", 3));
        queue.Enqueue(new CraftJob("test:blocks", 1));

        Assert.Equal(1, queue.Tick(1f, 1, _recipes, new Inventory(_catalog, 1)));
        Assert.True(queue.IsEmpty);
    }

    // ---- Actions ----

    [Fact]
    public void CraftingTakesTheInputsAndTheStationMakesTheOutput()
    {
        var (creation, bench) = Station();
        Give(Player, Scrap, 5);
        Give(Player, Wood, 2);

        var result = Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe = "test:chest", count = 2 });

        Assert.True(result.Ok, result.ToString());
        var pockets = _items.PlayerInventory(2);
        Assert.Equal(1, pockets.Count(Scrap));
        Assert.Equal(0, pockets.Count(Wood));
        _items.Tick(2f); // speed 2: one 4 s chest
        Assert.Equal(1, View(Player, InventoryRef.OfPart(creation, bench)).Count(Chest));
        _items.Tick(2f);
        var view = View(Player, InventoryRef.OfPart(creation, bench));
        Assert.Equal(2, view.Count(Chest));
        Assert.Empty(view.Queue!);
        Assert.Equal(2f, view.Speed);
    }

    [Fact]
    public void AStationPlaysItsStartWorkAndCompleteSounds()
    {
        var (creation, bench) = Station();
        Give(Player, Scrap, 4);
        Give(Player, Wood, 2);
        Assert.True(Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe = "test:chest", count = 2 }).Ok);
        _world.Sounds.Clear();

        _items.Tick(1f);
        Assert.Equal(["bench_start@test", $"loop bench_work@test {creation}/{bench}"], _world.Sounds);
        _world.Sounds.Clear();
        _items.Tick(1f); // the first chest
        _items.Tick(1f);
        Assert.Equal(["bench_done@test"], _world.Sounds);
        _world.Sounds.Clear();
        _items.Tick(1f); // the second, and the queue is empty
        Assert.Equal(["bench_done@test", $"stop loop {creation}/{bench}"], _world.Sounds);
        _world.Sounds.Clear();
        _items.Tick(1f);
        Assert.Empty(_world.Sounds);
    }

    [Fact]
    public void AStationNamesItsStartWorkAndCompleteClips()
    {
        var (creation, bench) = Station();
        Give(Player, Scrap, 4);
        Give(Player, Wood, 2);
        Assert.True(Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe = "test:chest", count = 2 }).Ok);

        _items.Tick(1f);
        Assert.Equal([$"{creation}/{bench} station=open>work_loop#1"], _world.Readouts);
        _items.Tick(1f); // the first chest: still working, no new clip
        _items.Tick(1f);
        Assert.Single(_world.Readouts);
        _items.Tick(1f); // the second, and the queue is empty
        Assert.Equal($"{creation}/{bench} station=close#2", _world.Readouts[^1]);

        Give(Player, Scrap, 1);
        Assert.True(Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe = "test:blocks", count = 1 }).Ok);
        _items.Tick(0.1f);
        Assert.Equal($"{creation}/{bench} station=open>work_loop#3", _world.Readouts[^1]); // a new value plays it again
    }

    [Fact]
    public void AStationWithAFullOutputStopsItsWorkLoop()
    {
        var (creation, bench) = Station();
        Give(Player, Scrap, 3);
        Assert.True(Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe = "test:blocks", count = 3 }).Ok);
        _world.Sounds.Clear();

        _items.Tick(0.25f);
        _items.Tick(10f); // two stacks of 10 fill both slots, the third craft waits
        Assert.Equal(["bench_start@test", $"loop bench_work@test {creation}/{bench}", "bench_done@test", $"stop loop {creation}/{bench}"], _world.Sounds);
    }

    [Fact]
    public void TheCrafterTakesTheOutputWithMoveItems()
    {
        var (creation, bench) = Station();
        Give(Player, Scrap, 1);
        Assert.True(Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe = "test:blocks" }).Ok);
        _items.Tick(1f);

        var result = Dispatch(Player, InventoryActions.MoveItems, new
        {
            from = new { creation, part = bench },
            from_slot = 0,
            to = new { player = 2 },
        });

        Assert.True(result.Ok, result.ToString());
        Assert.Equal(10, result.Value!.Value.GetProperty("moved").GetInt32());
        Assert.Equal(10, _items.PlayerInventory(2).Count(Block));
    }

    [Theory]
    [InlineData("test:nope", 1, "There is no recipe 'test:nope'.")]
    [InlineData("test:blocks", 6, "count must be 1 to 5 (got 6).")]
    [InlineData("test:chest", 1, "You need 2 Scrap and have 1.")]
    public void BadCraftsAreRefused(string recipe, int count, string error)
    {
        var (creation, bench) = Station();
        Give(Player, Scrap, 1);
        Give(Player, Wood, 1);

        var result = Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe, count });

        Assert.Equal(ActionErrors.Rejected, result.Code);
        Assert.Equal(error, result.Message);
        Assert.Equal(1, _items.PlayerInventory(2).Count(Scrap));
    }

    [Fact]
    public void ARecipeOnlyRunsAtItsStations()
    {
        var creation = Add(Chest);

        var result = Dispatch(Player, InventoryActions.Craft, new { creation, part = 1, recipe = "test:blocks" });

        Assert.Equal("Chest is not a crafting station.", result.Message);
    }

    [Fact]
    public void TheQueueHasALimit()
    {
        var (creation, bench) = Station();
        Give(Player, Scrap, 3);
        Assert.True(Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe = "test:blocks" }).Ok);
        Assert.True(Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe = "test:blocks" }).Ok);

        var result = Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe = "test:blocks" });

        Assert.Equal("The Bench queue is full (2).", result.Message);
    }

    [Fact]
    public void StationsOutOfReachCannotBeUsed()
    {
        var (creation, bench) = Station();
        _world.Eyes[2] = new Vector3(0, 1.6f, 30);
        Give(Player, Scrap, 1);

        var result = Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe = "test:blocks" });

        Assert.StartsWith("That is 29.", result.Message);
        Assert.False(Dispatch(Player, InventoryActions.GetInventory, new { inventory = new { creation, part = bench } }).Ok);
    }

    [Fact]
    public void CancellingReturnsTheInputsOfUnfinishedCrafts()
    {
        var (creation, bench) = Station();
        Give(Player, Scrap, 4);
        Give(Player, Wood, 2);
        Assert.True(Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe = "test:chest", count = 2 }).Ok);
        _items.Tick(2.5f); // one done, the second started

        var result = Dispatch(Player, InventoryActions.CancelCraft, new { creation, part = bench, job = 0 });

        Assert.True(result.Ok, result.ToString());
        Assert.Equal(2, _items.PlayerInventory(2).Count(Scrap));
        Assert.Equal(1, _items.PlayerInventory(2).Count(Wood));
        Assert.Equal(1, View(Player, InventoryRef.OfPart(creation, bench)).Count(Chest));
        Assert.Equal("Nothing is queued.", Dispatch(Player, InventoryActions.CancelCraft, new { creation, part = bench, job = 0 }).Message);
    }

    [Fact]
    public void NobodyTouchesAnotherPlayersInventory()
    {
        Give(Player, Scrap, 5);

        var result = Dispatch(Other, InventoryActions.MoveItems, new { from = new { player = 2 }, from_slot = 0, to = new { player = 3 } });

        Assert.Equal("You can only use your own inventory.", result.Message);
        Assert.False(Dispatch(Other, InventoryActions.GetInventory, new { inventory = new { player = 2 } }).Ok);
    }

    [Fact]
    public void ItemsGoIntoAndOutOfAChest()
    {
        var creation = Add(Chest);
        Give(Player, Scrap, 60);

        Assert.True(Dispatch(Player, InventoryActions.MoveItems, new { from = new { player = 2 }, from_slot = 0, to = new { creation, part = 1 }, to_slot = 3 }).Ok);
        Assert.True(Dispatch(Other, InventoryActions.MoveItems, new { from = new { creation, part = 1 }, from_slot = 3, to = new { player = 3 }, count = 20 }).Ok);

        Assert.Equal(new SlotView(3, Scrap, 30), View(Player, InventoryRef.OfPart(creation, 1)).At(3));
        Assert.Equal(20, _items.PlayerInventory(3).Count(Scrap));
    }

    [Fact]
    public void SortInventoryMergesStacksAndSortsByTabThenName()
    {
        var inventory = _items.PlayerInventory(2);
        inventory.SetSlot(0, new ItemStack(Wood, 10));
        inventory.SetSlot(1, new ItemStack(Block, 1));
        inventory.SetSlot(3, new ItemStack(Wood, 45));

        var result = Dispatch(Player, InventoryActions.SortInventory, new { inventory = new { player = 2 } });

        Assert.True(result.Ok, result.Message);
        // Blocks come before Resources; the two Wood stacks merge into a full one and the rest.
        Assert.Equal([new ItemStack(Block, 1), new ItemStack(Wood, 50), new ItemStack(Wood, 5), null], inventory.Slots);
        Assert.False(Dispatch(Player, InventoryActions.SortInventory, new { inventory = new { player = 2 } }).Value!.Value.GetProperty("changed").GetBoolean());
    }

    [Fact]
    public void SortInventoryOrganizesAChestInReachButNotAnotherPlayers()
    {
        var creation = Add(Chest);
        var chest = _world.Creations[creation].InventoryOf(1)!;
        chest.SetSlot(3, new ItemStack(Scrap, 5));

        Assert.True(Dispatch(Player, InventoryActions.SortInventory, new { inventory = new { creation, part = 1 } }).Ok);
        Assert.Equal(new ItemStack(Scrap, 5), chest[0]);
        Assert.Equal("You can only use your own inventory.", Dispatch(Other, InventoryActions.SortInventory, new { inventory = new { player = 2 } }).Message);
    }

    [Fact]
    public void ATankOnlyTakesFuel()
    {
        var creation = Add(Tank);
        Give(Player, Scrap, 1);

        var result = Dispatch(Player, InventoryActions.MoveItems, new { from = new { player = 2 }, from_slot = 0, to = new { creation, part = 1 } });

        Assert.Equal("That inventory does not take Scrap.", result.Message);
        Assert.Equal([Fuel], View(Player, InventoryRef.OfPart(creation, 1)).Accepts!);
    }

    [Fact]
    public void GivingOnlyWorksInCreative()
    {
        var items = new InventoryActions(_catalog, _recipes, new InventorySettings { AllowGive = false }, _world);
        var dispatcher = new ActionDispatcher();
        items.Register(dispatcher);

        var result = dispatcher.Dispatch(Player, InventoryActions.GiveItems, JsonSerializer.Serialize(new { item = Scrap, count = 1 }));

        Assert.Equal("Items can only be made from nothing in Creative.", result.Message);
        Assert.Equal("Unknown item 'test:nope'.", Dispatch(Player, InventoryActions.GiveItems, new { item = "test:nope", count = 1 }).Message);
        Assert.Equal("count must be 1 to 100 (got 101).", Dispatch(Player, InventoryActions.GiveItems, new { item = Scrap, count = 101 }).Message);
    }

    [Fact]
    public void AContainerHoldingItemsCannotBePickedUp()
    {
        var creation = Add(Chest);
        var build = new BuildActions(_catalog, new BuildSettings { Reach = 5 }, _world);
        build.Register(_dispatcher);
        _world.Creations[creation].InventoryOf(1)!.Add(Scrap, 1);

        var result = Dispatch(Player, BuildActions.RemovePart, new { creation, part = 1 });

        Assert.Equal("Empty the Chest before picking it up.", result.Message);
        _world.Creations[creation].InventoryOf(1)!.Clear();
        Assert.True(Dispatch(Player, BuildActions.RemovePart, new { creation, part = 1 }).Ok);
    }

    [Fact]
    public void TheVersionChangesWithTheContents()
    {
        var (creation, bench) = Station();
        var where = InventoryRef.OfPart(creation, bench);
        var before = _items.Version(where);
        Give(Player, Scrap, 1);
        Assert.Equal(before, _items.Version(where));

        Dispatch(Player, InventoryActions.Craft, new { creation, part = bench, recipe = "test:blocks" });
        var queued = _items.Version(where);
        Assert.NotEqual(before, queued);
        _items.Tick(0.1f);
        Assert.NotEqual(queued, _items.Version(where));
        Assert.Null(_items.Version(InventoryRef.OfPart(99, 1)));
    }

    // ---- Linked storage ----

    [Fact]
    public void ContainersWithinTheLinkRangeAreLinked()
    {
        var (creation, forge, near, mid, far) = Workshop();
        var built = _world.Creations[creation];

        Assert.Equal([near, mid], StorageLinks.Of(built, forge));
        Assert.Equal([forge], StorageLinks.StationsOf(built, mid));
        Assert.Empty(StorageLinks.StationsOf(built, far));
        Assert.Empty(StorageLinks.Of(built, near)); // a chest links nothing
        var view = View(Player, InventoryRef.OfPart(creation, forge));
        Assert.Equal([InventoryRef.OfPart(creation, near), InventoryRef.OfPart(creation, mid)], view.Linked);
        Assert.Equal(2, view.QueueSize);
    }

    [Fact]
    public void CraftingTakesFromLinkedStorageFirstAndPutsTheOutputThere()
    {
        var (creation, forge, near, _, _) = Workshop();
        var chest = _world.Creations[creation].InventoryOf(near)!;
        chest.Add(Scrap, 1);
        Give(Player, Scrap, 5);
        Give(Player, Wood, 1);

        Assert.True(Dispatch(Player, InventoryActions.Craft, new { creation, part = forge, recipe = "test:chest" }).Ok);

        Assert.Equal(0, chest.Count(Scrap));
        Assert.Equal(4, _items.PlayerInventory(2).Count(Scrap));
        _items.Tick(4f);
        Assert.Equal(1, chest.Count(Chest));
        Assert.Equal(0, View(Player, InventoryRef.OfPart(creation, forge)).Count(Chest));
    }

    [Fact]
    public void LinkedStorageCountsTowardsARecipe()
    {
        var (creation, forge, near, mid, _) = Workshop();
        _world.Creations[creation].InventoryOf(near)!.Add(Scrap, 1);
        _world.Creations[creation].InventoryOf(mid)!.Add(Scrap, 1);

        var refused = Dispatch(Player, InventoryActions.Craft, new { creation, part = forge, recipe = "test:chest" });
        Assert.Equal("You need 1 Wood and have 0.", refused.Message);
        _world.Creations[creation].InventoryOf(mid)!.Add(Wood, 1);
        Assert.True(Dispatch(Player, InventoryActions.Craft, new { creation, part = forge, recipe = "test:chest" }).Ok);
        Assert.Empty(_items.PlayerInventory(2).Slots.OfType<ItemStack>());
    }

    [Fact]
    public void ACancelledOrderReturnsToThePlayerThenTheLinkedStorage()
    {
        var (creation, forge, near, _, _) = Workshop();
        Give(Player, Scrap, 4);
        Give(Player, Wood, 2);
        Assert.True(Dispatch(Player, InventoryActions.Craft, new { creation, part = forge, recipe = "test:chest", count = 2 }).Ok);
        // Fill three of the four pockets so only part of the refund fits there.
        Give(Player, Key, 1);
        Give(Player, Key, 1);
        Give(Player, Key, 1);

        Assert.True(Dispatch(Player, InventoryActions.CancelCraft, new { creation, part = forge, job = 0 }).Ok);

        Assert.Equal(4, _items.PlayerInventory(2).Count(Scrap));
        Assert.Equal(2, _world.Creations[creation].InventoryOf(near)!.Count(Wood));
    }

    [Fact]
    public void AContainerLinkedToAStationInReachCanBeUsedFromFurther()
    {
        var (creation, forge, _, mid, far) = Workshop();
        _world.Eyes[2] = new Vector3(-4.6f, 0.125f, 0.125f);

        Assert.True(Dispatch(Player, InventoryActions.GetInventory, new { inventory = new { creation, part = forge } }).Ok);
        Assert.True(Dispatch(Player, InventoryActions.GetInventory, new { inventory = new { creation, part = mid } }).Ok);
        Assert.StartsWith("That is ", Dispatch(Player, InventoryActions.GetInventory, new { inventory = new { creation, part = far } }).Message);
    }

    // ---- Upgrades ----

    [Fact]
    public void UpgradingInSurvivalTakesTheCostAndRaisesTheLevel()
    {
        var items = Survival(out var dispatcher);
        var (creation, forge) = (Add(Forge), 1);
        _world.Creations[creation].InventoryOf(forge)!.Add(Scrap, 1);
        var upgrade = new UpgradePartArgs(creation, forge);

        Assert.Equal("You need 2 Kit and have 0.", dispatcher.Dispatch(Player, InventoryActions.UpgradePart, upgrade).Message);
        items.PlayerInventory(2).Add(Kit, 5);
        Assert.Equal([new ItemAmount(Kit, 2)], items.UpgradeCost(_world.Creations[creation], forge));
        Assert.True(dispatcher.Dispatch(Player, InventoryActions.UpgradePart, upgrade).Ok);

        var view = items.View(InventoryRef.OfPart(creation, forge))!;
        Assert.Equal((1, 2, 3, 4f), (view.Level, view.MaxLevel, view.SlotCount, view.Speed!.Value));
        Assert.Equal(1, view.Count(Scrap)); // the slots grew; what they held stayed
        Assert.Equal(3, items.PlayerInventory(2).Count(Kit));
        Assert.True(dispatcher.Dispatch(Player, InventoryActions.UpgradePart, upgrade).Ok);
        Assert.Equal(3, items.View(InventoryRef.OfPart(creation, forge))!.QueueSize);
        Assert.Equal(4f, items.View(InventoryRef.OfPart(creation, forge))!.Speed); // levels add up
        Assert.Equal("The Forge is already at its top level (2).", dispatcher.Dispatch(Player, InventoryActions.UpgradePart, upgrade).Message);
        Assert.Empty(items.UpgradeCost(_world.Creations[creation], forge));
    }

    [Fact]
    public void AWorldOwnedStationCraftsLikeAnyButIsNeverUpgraded()
    {
        var items = Survival(out var dispatcher);
        var (bench, forge) = (Add(Bench), Add(Forge));
        _world.Creations[bench] = Creations.WorldOwnedCreations.Owned(_world.Creations[bench]);
        _world.Creations[forge] = Creations.WorldOwnedCreations.Owned(_world.Creations[forge]);
        items.PlayerInventory(2).Add(Scrap, 1);
        items.PlayerInventory(2).Add(Kit, 5);

        // Using it: queue a craft, let it finish and take the output.
        Assert.True(dispatcher.Dispatch(Player, InventoryActions.Craft, new CraftArgs(bench, 1, "test:blocks")).Ok);
        items.Tick(1f);
        var take = dispatcher.Dispatch(Player, InventoryActions.MoveItems, new MoveItemsArgs(InventoryRef.OfPart(bench, 1), 0, InventoryRef.OfPlayer(2)));
        Assert.True(take.Ok, take.ToString());
        Assert.Equal(10, items.PlayerInventory(2).Count(Block));

        // Changing it: no.
        var upgrade = dispatcher.Dispatch(Player, InventoryActions.UpgradePart, new UpgradePartArgs(forge, 1));
        Assert.Equal("The Forge belongs to the world: it can't be upgraded.", upgrade.Message);
        Assert.Equal(0, _world.Creations[forge].LevelOf(1));
        Assert.Equal(5, items.PlayerInventory(2).Count(Kit));
    }

    [Fact]
    public void UpgradesAreFreeInCreativeAndNeedAnUpgradablePartInReach()
    {
        var (creation, forge) = (Add(Forge), 1);
        var (benchCreation, bench) = Station();

        Assert.Empty(_items.UpgradeCost(_world.Creations[creation], forge));
        Assert.True(Dispatch(Player, InventoryActions.UpgradePart, new { creation, part = forge }).Ok);
        Assert.Equal("The Bench can't be upgraded.", Dispatch(Player, InventoryActions.UpgradePart, new { creation = benchCreation, part = bench }).Message);
        _world.Eyes[2] = new Vector3(0, 1.6f, 30);
        Assert.StartsWith("That is ", Dispatch(Player, InventoryActions.UpgradePart, new { creation, part = forge }).Message);
    }

    [Fact]
    public void ALevelIsSavedAndLoadedButNotKeptInABlueprint()
    {
        var (creation, forge) = (Add(Forge), 1);
        var built = _world.Creations[creation];
        Assert.Equal(5f, built.Bodies[0].Mass);
        Assert.True(built.SetLevel(forge, 2));
        Assert.Equal(3f, built.Bodies[0].Mass);

        var loaded = CreationJson.Read(CreationJson.Write(built), _catalog, "save.json", []);

        Assert.Equal(2, loaded!.LevelOf(forge));
        Assert.Equal(3, loaded.DefinitionOf(forge).Crafting!.QueueSize);
        Assert.Equal(3, loaded.InventoryOf(forge)!.SlotCount);
        var blueprint = Blueprint.Read(Blueprint.Write(built, "forge", DateTimeOffset.UnixEpoch), _catalog, 9, "forge.json", []);
        Assert.Equal(0, blueprint!.LevelOf(forge));
        Assert.Equal(2, blueprint.InventoryOf(forge)!.SlotCount);
    }

    [Fact]
    public void LevelsMergeInOrderOnTopOfThePart()
    {
        var forge = _catalog.FindDefinition(Forge)!;

        Assert.Same(forge, PartUpgrades.At(forge, 0));
        Assert.Equal((4f, 2, 3), (PartUpgrades.At(forge, 1).Crafting!.Speed!.Value, PartUpgrades.At(forge, 1).Crafting!.QueueSize!.Value, PartUpgrades.At(forge, 1).Inventory!.Slots!.Value));
        Assert.Equal((4f, 3, 0.5f), (PartUpgrades.At(forge, 2).Crafting!.Speed!.Value, PartUpgrades.At(forge, 2).Crafting!.QueueSize!.Value, PartUpgrades.At(forge, 2).Crafting!.LinkRange!.Value));
        Assert.Same(PartUpgrades.At(forge, 2), PartUpgrades.At(forge, 9));
        var bad = forge with { Upgrades = [new UpgradeDefinition { Set = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse("""{ "size": [2, 2, 2] }""")! }] };
        Assert.Null(PartUpgrades.Apply(bad, 1, out var error));
        Assert.Equal("an upgrade can change only name, description, mass, durability, mesh, icon, sockets, settings, display, crafting, inventory, logic, properties, not 'size'.", error);
    }

    [Fact]
    public void ALevelCanRetuneASpringButNotMoveOrRenameASocket()
    {
        static SocketDefinition Spring(float stiffness, float x = 0.125f, string id = "mount") => new()
        {
            Id = id, Type = SocketType.Spring, Position = new Vec3(x, 0, 0), Normal = new Vec3(1, 0, 0),
            Joint = new JointDefinition { Axis = new Vec3(0, 1, 0), Min = -0.08f, Max = 0.08f, Stiffness = stiffness, Damping = 1500 },
        };
        static UpgradeDefinition Level(SocketDefinition socket) => new()
        {
            Set = new System.Text.Json.Nodes.JsonObject { ["sockets"] = System.Text.Json.JsonSerializer.SerializeToNode(new[] { socket }, DataJson.WriteOptions) },
        };
        var spring = new PartDefinition { Id = "spring", Sockets = [Spring(25000)] };

        var stiffer = PartUpgrades.Apply(spring with { Upgrades = [Level(Spring(40000))] }, 1, out var error);
        Assert.Null(error);
        Assert.Equal(40000f, stiffer!.Sockets![0].Joint!.Stiffness);
        Assert.Contains(new UpgradeChange("Stiffness", "25000 N/m", "40000 N/m"), PartUpgrades.Changes(spring, stiffer));
        foreach (var moved in new[] { Spring(40000, x: 0.25f), Spring(40000, id: "wheel") })
        {
            Assert.Null(PartUpgrades.Apply(spring with { Upgrades = [Level(moved)] }, 1, out error));
            Assert.Equal("an upgrade can change only a socket's joint (a suspension's spring), not which sockets the part has or where they are.", error);
        }
    }

    [Theory]
    [InlineData("vanilla:gas_engine", "vanilla:gas_engine", 0)]
    [InlineData("gas_engine@2", "gas_engine", 2)]
    [InlineData("mod:engine@12", "mod:engine", 12)]
    [InlineData("gas_engine@0", "gas_engine", -1)]
    [InlineData("gas_engine@02", "gas_engine", -1)]
    [InlineData("gas_engine@", "gas_engine", -1)]
    [InlineData("gas_engine@x", "gas_engine", -1)]
    [InlineData("gas_engine@-1", "gas_engine", -1)]
    [InlineData("gas_engine@100", "gas_engine", -1)]
    public void AnUpgradedPartsItemNamesItsLevel(string item, string part, int level)
    {
        Assert.Equal((part, level), PartUpgrades.SplitItem(item));
        if (level >= 0)
            Assert.Equal(item, PartUpgrades.ItemKey(part, level));
    }

    [Fact]
    public void AnUpgradeSaysWhatItChanges()
    {
        var forge = _catalog.FindDefinition(Forge)!;
        var powered = forge with { Properties = new System.Text.Json.Nodes.JsonObject { ["max_power"] = 100 } };
        var stronger = powered with { Properties = new System.Text.Json.Nodes.JsonObject { ["max_power"] = 150, ["fuel_use"] = 0.5 } };

        Assert.Equal([new UpgradeChange("Speed", "1×", "4×"), new UpgradeChange("Slots", "2", "3")], PartUpgrades.Changes(forge, PartUpgrades.At(forge, 1)));
        Assert.Equal([new UpgradeChange("Queue", "2", "3"), new UpgradeChange("Mass", "5 kg", "3 kg")], PartUpgrades.Changes(PartUpgrades.At(forge, 1), PartUpgrades.At(forge, 2)));
        Assert.Equal([new UpgradeChange("max power", "100", "150"), new UpgradeChange("fuel use", "-", "0.5")], PartUpgrades.Changes(powered, stronger));
    }

    // ---- Helpers ----

    // A Forge with a chest touching it, a chest 0.5 m away (linked) and one 1.25 m away (not linked), joined by blocks.
    private (int Creation, int Forge, int Near, int Mid, int Far) Workshop()
    {
        var creation = new Creation(_world.NextCreationId(), _catalog);
        var parts = new[] { Forge, Chest, Block, Chest, Block, Block, Chest };
        for (var x = 0; x < parts.Length; x++)
            Assert.True(creation.TryPlacePart(parts[x], new GridCell(x, 0, 0), default, out _, out var error), error);
        creation.SetAllBodyPoses(Pose.Identity);
        _world.Added(creation);
        return (creation.Id, 1, 2, 4, 7);
    }

    // Survival rules (upgrades cost items) over the same world.
    private InventoryActions Survival(out ActionDispatcher dispatcher)
    {
        var items = new InventoryActions(_catalog, _recipes, new InventorySettings { Reach = 5, PlayerSlots = 4, AllowGive = false, FreeUpgrades = false }, _world);
        dispatcher = new ActionDispatcher();
        items.Register(dispatcher);
        return items;
    }

    private (int Creation, int Part) Station() => (Add(Bench), 1);

    private int Add(string part)
    {
        var creation = new Creation(_world.NextCreationId(), _catalog);
        Assert.True(creation.TryPlacePart(part, new GridCell(0, 0, 0), default, out _, out var error), error);
        creation.SetAllBodyPoses(Pose.Identity);
        _world.Added(creation);
        return creation.Id;
    }

    private void Give(ActionCaller caller, string item, int count) =>
        Assert.True(Dispatch(caller, InventoryActions.GiveItems, new { item, count }).Ok);

    private InventoryView View(ActionCaller caller, InventoryRef where)
    {
        var result = _dispatcher.Dispatch(caller, InventoryActions.GetInventory, new GetInventoryArgs(where));
        Assert.True(result.Ok, result.ToString());
        return result.Value!.Value.Deserialize<InventoryView>(ActionJson.Options)!;
    }

    private ActionResult Dispatch(ActionCaller caller, string action, object args) =>
        _dispatcher.Dispatch(caller, action, JsonSerializer.Serialize(args));

    private sealed class FakeWorld : IInventoryWorld, IBuildWorld
    {
        private int _nextId = 1;

        public Dictionary<int, Creation> Creations { get; } = [];
        public Dictionary<int, Vector3> Eyes { get; } = [];

        public IEnumerable<Creation> AllCreations => Creations.Values;

        public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

        public Vector3? EyeOf(int peer) => Eyes.TryGetValue(peer, out var eye) ? eye : null;

        public List<string> Sounds { get; } = [];

        public void PlaySound(string sound, string ownerModId, Vector3 position) => Sounds.Add($"{sound}@{ownerModId}");

        public void SetPartLoop(int creation, int partId, string? sound, string ownerModId) =>
            Sounds.Add(sound is null ? $"stop loop {creation}/{partId}" : $"loop {sound}@{ownerModId} {creation}/{partId}");

        public List<string> Readouts { get; } = [];

        public void SetReadout(int creation, int partId, string id, string? text) =>
            Readouts.Add($"{creation}/{partId} {id}={text ?? "none"}");

        public int NextCreationId() => _nextId++;

        public string? CheckClear(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose) => null;

        public void Added(Creation creation) => Creations.Add(creation.Id, creation);

        public void Changed(Creation creation)
        {
        }

        public void Removed(int creationId) => Creations.Remove(creationId);
    }
}
