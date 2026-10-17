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

    // ---- Helpers ----

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

        public int NextCreationId() => _nextId++;

        public string? CheckClear(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose) => null;

        public void Added(Creation creation) => Creations.Add(creation.Id, creation);

        public void Changed(Creation creation)
        {
        }

        public void Removed(int creationId) => Creations.Remove(creationId);
    }
}
