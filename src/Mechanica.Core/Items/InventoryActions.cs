using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Items;

/// <summary>
/// Names one inventory: a player's own (<c>{ "player": 2 }</c>) or a container or station part
/// (<c>{ "creation": 3, "part": 7 }</c>).
/// </summary>
/// <param name="Player">Peer id of the player whose inventory it is.</param>
/// <param name="Creation">Creation holding the part.</param>
/// <param name="Part">Part id inside that Creation.</param>
public sealed record InventoryRef(int? Player = null, int? Creation = null, int? Part = null)
{
    /// <summary>A player's own inventory.</summary>
    public static InventoryRef OfPlayer(int peer) => new(Player: peer);

    /// <summary>A part's inventory.</summary>
    public static InventoryRef OfPart(int creation, int part) => new(Creation: creation, Part: part);

    /// <summary>True when it names a player's inventory.</summary>
    public bool IsPlayer => Player is not null;

    /// <inheritdoc/>
    public override string ToString() => Player is { } p ? $"player {p}" : $"creation {Creation} part {Part}";
}

/// <summary>Arguments of <c>MoveItems</c>.</summary>
/// <param name="From">Where the items are.</param>
/// <param name="FromSlot">The slot they are in.</param>
/// <param name="To">Where they go (may be the same inventory).</param>
/// <param name="ToSlot">The slot they go to; null puts them wherever they fit.</param>
/// <param name="Count">How many; null moves the whole stack.</param>
public sealed record MoveItemsArgs(InventoryRef From, int FromSlot, InventoryRef To, int? ToSlot = null, int? Count = null);

/// <summary>Arguments of <c>Craft</c>: queue <see cref="Count"/> crafts of a recipe at a station part.</summary>
/// <param name="Creation">Creation holding the station.</param>
/// <param name="Part">The station part.</param>
/// <param name="Recipe">Recipe id (<c>id</c> means vanilla, or <c>mod_id:id</c>).</param>
/// <param name="Count">How many crafts (1 or more).</param>
public sealed record CraftArgs(int Creation, int Part, string Recipe, int Count = 1);

/// <summary>Arguments of <c>CancelCraft</c>: drop an order and get its unfinished crafts' inputs back.</summary>
/// <param name="Creation">Creation holding the station.</param>
/// <param name="Part">The station part.</param>
/// <param name="Job">Index of the order in the station's queue (0 is the one being worked).</param>
public sealed record CancelCraftArgs(int Creation, int Part, int Job);

/// <summary>Arguments of <c>GiveItems</c> (Creative only).</summary>
/// <param name="Item">Item id.</param>
/// <param name="Count">How many (1 or more).</param>
/// <param name="To">Where they go; null means the caller's own inventory.</param>
public sealed record GiveItemsArgs(string Item, int Count, InventoryRef? To = null);

/// <summary>Arguments of <c>GetInventory</c>.</summary>
/// <param name="Inventory">The inventory to read.</param>
public sealed record GetInventoryArgs(InventoryRef Inventory);

/// <summary>A filled slot in an <see cref="InventoryView"/>.</summary>
/// <param name="Slot">Slot index.</param>
/// <param name="Item">Qualified item id.</param>
/// <param name="Count">How many.</param>
public sealed record SlotView(int Slot, string Item, int Count);

/// <summary>An order in an <see cref="InventoryView"/>.</summary>
/// <param name="Recipe">Qualified recipe id.</param>
/// <param name="Count">Crafts still to finish.</param>
/// <param name="Progress">Seconds of work done on the current craft, at speed 1.</param>
/// <param name="Time">Seconds one craft takes at speed 1.</param>
public sealed record CraftJobView(string Recipe, int Count, float Progress, float Time);

/// <summary>
/// What one inventory holds, as <c>GetInventory</c> returns it and as clients are sent it: the filled
/// slots, and for a station its orders.
/// </summary>
/// <param name="Inventory">Which inventory.</param>
/// <param name="SlotCount">Number of slots.</param>
/// <param name="Items">Filled slots in slot order.</param>
/// <param name="Part">Qualified part id of a container or station; null for a player's inventory.</param>
/// <param name="Accepts">Item ids it takes, or null for any.</param>
/// <param name="Queue">A station's orders (null for other inventories).</param>
/// <param name="Speed">A station's crafting speed.</param>
/// <param name="Blocked">True when a station's finished craft is waiting for room.</param>
public sealed record InventoryView(
    InventoryRef Inventory,
    int SlotCount,
    IReadOnlyList<SlotView> Items,
    string? Part = null,
    IReadOnlyList<string>? Accepts = null,
    IReadOnlyList<CraftJobView>? Queue = null,
    float? Speed = null,
    bool Blocked = false)
{
    /// <summary>The stack in <paramref name="slot"/>, or null.</summary>
    public SlotView? At(int slot) => Items.FirstOrDefault(i => i.Slot == slot);

    /// <summary>How many of <paramref name="item"/> it holds.</summary>
    public int Count(string item) => Items.Where(i => i.Item == Identifiers.Qualify(item)).Sum(i => i.Count);
}

/// <summary>Changes whenever an <see cref="InventoryView"/> would (compared by value; the inventory by reference).</summary>
/// <param name="Inventory">The inventory object.</param>
/// <param name="Revision">Its revision.</param>
/// <param name="QueueRevision">Its station queue's revision, or 0.</param>
public sealed record InventoryVersion(Inventory Inventory, int Revision, int QueueRevision);

/// <summary>Value of a successful <c>MoveItems</c>.</summary>
/// <param name="Moved">How many items moved.</param>
public sealed record MoveItemsResult(int Moved);

/// <summary>Value of a successful <c>Craft</c>.</summary>
/// <param name="Queued">Crafts queued.</param>
/// <param name="Position">Index of the new order in the queue.</param>
public sealed record CraftResult(int Queued, int Position);

/// <summary>Value of a successful <c>CancelCraft</c>.</summary>
/// <param name="Refunded">Items returned to the caller.</param>
public sealed record CancelCraftResult(IReadOnlyList<ItemAmount> Refunded);

/// <summary>Value of a successful <c>GiveItems</c>.</summary>
/// <param name="Given">How many items were made.</param>
public sealed record GiveItemsResult(int Given);

/// <summary>What the inventory actions need from the world. The host's game layer implements it.</summary>
public interface IInventoryWorld
{
    /// <summary>The Creation with this id, with its Body poses current, or null.</summary>
    Creation? FindCreation(int id);

    /// <summary>World position of a peer's player's eyes, or null when it has no player.</summary>
    Vector3? EyeOf(int peer);

    /// <summary>Every Creation in the world, live or parked.</summary>
    IEnumerable<Creation> AllCreations { get; }
}

/// <summary>
/// Inventories and crafting in the Action API (chunk 18), validated on the host like every other action:
/// <list type="bullet">
/// <item><c>MoveItems</c>: moves items between the caller's own inventory and containers in reach, or within one.</item>
/// <item><c>Craft</c>: takes a recipe's inputs from the caller's inventory and queues it at a station in reach.
/// The station works its queue over time (<see cref="Tick"/>) and puts what it makes in its own inventory.</item>
/// <item><c>CancelCraft</c>: drops an order and returns the inputs of its unfinished crafts.</item>
/// <item><c>GiveItems</c>: makes items from nothing, only while <see cref="InventorySettings.AllowGive"/> (Creative).</item>
/// <item><c>GetInventory</c>: reads an inventory the caller may use (a query).</item>
/// </list>
/// Each player's own inventory lives here, keyed by peer; container and station inventories live in
/// their Creation's parts, so they move, split, weld and save with them. No caller can touch another
/// player's inventory.
/// </summary>
public sealed class InventoryActions(IPartCatalog catalog, RecipeBook recipes, InventorySettings settings, IInventoryWorld world)
{
    /// <summary>Action name for moving items.</summary>
    public const string MoveItems = "MoveItems";

    /// <summary>Action name for queueing a craft.</summary>
    public const string Craft = "Craft";

    /// <summary>Action name for cancelling an order.</summary>
    public const string CancelCraft = "CancelCraft";

    /// <summary>Action name for making items (Creative).</summary>
    public const string GiveItems = "GiveItems";

    /// <summary>Action name for reading an inventory.</summary>
    public const string GetInventory = "GetInventory";

    private readonly SortedDictionary<int, Inventory> _players = [];

    /// <summary>The recipes stations run.</summary>
    public RecipeBook Recipes => recipes;

    /// <summary>The limits in use.</summary>
    public InventorySettings Settings => settings;

    /// <summary>Registers every inventory action.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<MoveItemsArgs>(MoveItems, ValidateMove, ApplyMove);
        dispatcher.Register<CraftArgs>(Craft, ValidateCraft, ApplyCraft);
        dispatcher.Register<CancelCraftArgs>(CancelCraft, ValidateCancel, ApplyCancel);
        dispatcher.Register<GiveItemsArgs>(GiveItems, ValidateGive, ApplyGive);
        dispatcher.Register<GetInventoryArgs>(GetInventory, (c, a) => CheckAccess(c, a.Inventory, out _), (c, a) => Value(View(a.Inventory)), readOnly: true);
    }

    /// <summary>A player's own inventory, made empty the first time it is asked for.</summary>
    public Inventory PlayerInventory(int peer)
    {
        if (!_players.TryGetValue(peer, out var inventory))
            _players[peer] = inventory = new Inventory(catalog, settings.PlayerSlots);
        return inventory;
    }

    /// <summary>Forgets a player who left, with their inventory (saving it comes with save/load, chunk 24).</summary>
    public void Forget(int peer) => _players.Remove(peer);

    /// <summary>The inventory <paramref name="where"/> names, or null when there is none.</summary>
    public Inventory? Find(InventoryRef where)
    {
        if (where.Player is { } peer)
            return where.Creation is null && where.Part is null ? PlayerInventory(peer) : null;
        if (where.Creation is not { } id || where.Part is not { } part || world.FindCreation(id) is not { } creation)
            return null;
        return creation.FindPart(part) is null ? null : creation.InventoryOf(part);
    }

    /// <summary>What <paramref name="where"/> holds now, or null when there is no such inventory.</summary>
    public InventoryView? View(InventoryRef where)
    {
        if (Find(where) is not { } inventory)
            return null;
        var items = inventory.Slots.Select((s, i) => (s, i)).Where(x => x.s is not null)
            .Select(x => new SlotView(x.i, x.s!.Item, x.s.Count)).ToList();
        var accepts = inventory.AcceptedItems?.Order(StringComparer.Ordinal).ToList();
        if (where.IsPlayer)
            return new InventoryView(where, inventory.SlotCount, items, Accepts: accepts);
        var creation = world.FindCreation(where.Creation!.Value)!;
        var part = creation.FindPart(where.Part!.Value)!;
        var queue = creation.CraftingOf(part.Id);
        return new InventoryView(where, inventory.SlotCount, items, part.Part, accepts,
            queue?.Jobs.Select(j => new CraftJobView(j.Recipe, j.Count, j.Progress, recipes.Find(j.Recipe)?.Time ?? 0)).ToList(),
            queue is null ? null : SpeedOf(creation.DefinitionOf(part.Id)),
            queue?.IsBlocked ?? false);
    }

    /// <summary>
    /// A token that changes whenever <see cref="View"/> of <paramref name="where"/> would; null when the
    /// inventory is gone. Sync compares it to decide when to send a view again.
    /// </summary>
    public InventoryVersion? Version(InventoryRef where)
    {
        if (Find(where) is not { } inventory)
            return null;
        var queue = where.IsPlayer ? null : world.FindCreation(where.Creation!.Value)!.CraftingOf(where.Part!.Value);
        // The inventory object follows its part, so after a weld or split renames the part it is a new one here.
        return new InventoryVersion(inventory, inventory.Revision, queue?.Revision ?? 0);
    }

    /// <summary>Null when <paramref name="caller"/> may use the inventory <paramref name="where"/> names, or why not.</summary>
    public string? CheckAccess(ActionCaller caller, InventoryRef where, out Inventory? inventory)
    {
        inventory = null;
        if (where.Player is { } peer)
        {
            if (where.Creation is not null || where.Part is not null)
                return "Name either a player or a creation and part, not both.";
            if (peer != caller.Peer)
                return "You can only use your own inventory.";
            inventory = PlayerInventory(peer);
            return null;
        }
        if (where.Creation is not { } id || where.Part is not { } partId)
            return "Name an inventory: a player, or a creation and part.";
        if (world.FindCreation(id) is not { } creation)
            return $"There is no creation {id}.";
        if (creation.FindPart(partId) is not { } part)
            return $"Creation {id} has no part {partId}.";
        if (creation.InventoryOf(partId) is not { } found)
            return $"{creation.DefinitionOf(partId).Name ?? part.Part} has no inventory.";
        if (CheckReach(caller, creation, partId) is { } far)
            return far;
        inventory = found;
        return null;
    }

    /// <summary>Works every station's queue for <paramref name="seconds"/>. The host calls it every physics tick.</summary>
    public void Tick(float seconds)
    {
        foreach (var creation in world.AllCreations)
        {
            foreach (var (part, queue) in creation.ActiveCrafting.ToList())
            {
                if (creation.InventoryOf(part) is { } output)
                    queue.Tick(seconds, SpeedOf(creation.DefinitionOf(part)), recipes, output);
            }
        }
    }

    // ---- MoveItems ----

    private string? ValidateMove(ActionCaller caller, MoveItemsArgs args)
    {
        if (CheckAccess(caller, args.From, out var from) is { } a)
            return a;
        if (CheckAccess(caller, args.To, out var to) is { } b)
            return b;
        return Inventory.CheckMove(from!, args.FromSlot, to!, args.ToSlot, args.Count);
    }

    private JsonElement? ApplyMove(ActionCaller caller, MoveItemsArgs args) =>
        Value(new MoveItemsResult(Inventory.Move(Find(args.From)!, args.FromSlot, Find(args.To)!, args.ToSlot, args.Count)));

    // ---- Craft ----

    private string? ValidateCraft(ActionCaller caller, CraftArgs args)
    {
        if (CheckStation(caller, args.Creation, args.Part, out var creation, out var queue) is { } bad)
            return bad;
        var station = creation!.FindPart(args.Part)!.Part;
        if (recipes.Find(args.Recipe) is not { } recipe)
            return $"There is no recipe '{args.Recipe}'.";
        if (!recipe.RunsAt(station))
            return $"{recipe.Name} is not made at the {creation.DefinitionOf(args.Part).Name ?? station}.";
        if (args.Count < 1 || args.Count > settings.MaxCraftCount)
            return $"count must be 1 to {settings.MaxCraftCount} (got {args.Count}).";
        var limit = creation.DefinitionOf(args.Part).Crafting!.QueueSize ?? 1;
        if (queue!.Jobs.Count >= limit)
            return $"The {creation.DefinitionOf(args.Part).Name ?? station} queue is full ({limit}).";
        var pockets = PlayerInventory(caller.Peer);
        foreach (var input in recipe.InputsFor(args.Count))
        {
            var have = pockets.Count(input.Item);
            if (have < input.Count)
                return $"You need {input.Count} {Name(input.Item)} and have {have}.";
        }
        return null;
    }

    private JsonElement? ApplyCraft(ActionCaller caller, CraftArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        var queue = creation.CraftingOf(args.Part)!;
        var recipe = recipes.Find(args.Recipe)!;
        var pockets = PlayerInventory(caller.Peer);
        foreach (var input in recipe.InputsFor(args.Count))
            pockets.Remove(input.Item, input.Count);
        queue.Enqueue(new CraftJob(recipe.QualifiedId, args.Count));
        return Value(new CraftResult(args.Count, queue.Jobs.Count - 1));
    }

    private string? ValidateCancel(ActionCaller caller, CancelCraftArgs args)
    {
        var bad = CheckStation(caller, args.Creation, args.Part, out _, out var found);
        if (bad is not null || found is not { } queue)
            return bad;
        if (args.Job < 0 || args.Job >= queue.Jobs.Count)
            return queue.IsEmpty ? "Nothing is queued." : $"job must be 0 to {queue.Jobs.Count - 1} (got {args.Job}).";
        var refund = Refund(queue.Jobs[args.Job]);
        return PlayerInventory(caller.Peer).CanAddAll(refund) ? null : "Make room in your inventory for the returned items first.";
    }

    private JsonElement? ApplyCancel(ActionCaller caller, CancelCraftArgs args)
    {
        var queue = world.FindCreation(args.Creation)!.CraftingOf(args.Part)!;
        var refund = Refund(queue.RemoveAt(args.Job));
        PlayerInventory(caller.Peer).TryAddAll(refund);
        return Value(new CancelCraftResult(refund));
    }

    // Inputs of an order's crafts that have not finished; a recipe that no longer exists returns nothing.
    private List<ItemAmount> Refund(CraftJob job) => recipes.Find(job.Recipe) is { } recipe ? [.. recipe.InputsFor(job.Count)] : [];

    private string? CheckStation(ActionCaller caller, int creationId, int partId, out Creation? creation, out CraftingQueue? queue)
    {
        queue = null;
        creation = world.FindCreation(creationId);
        if (creation is null)
            return $"There is no creation {creationId}.";
        if (creation.FindPart(partId) is not { } part)
            return $"Creation {creationId} has no part {partId}.";
        if (creation.CraftingOf(partId) is not { } found || creation.InventoryOf(partId) is null)
            return $"{creation.DefinitionOf(partId).Name ?? part.Part} is not a crafting station.";
        if (CheckReach(caller, creation, partId) is { } far)
            return far;
        queue = found;
        return null;
    }

    // ---- GiveItems ----

    private string? ValidateGive(ActionCaller caller, GiveItemsArgs args)
    {
        if (!settings.AllowGive)
            return "Items can only be made from nothing in Creative.";
        if (catalog.FindDefinition(args.Item) is null)
            return $"Unknown item '{args.Item}'.";
        if (args.Count < 1 || args.Count > settings.MaxGiveCount)
            return $"count must be 1 to {settings.MaxGiveCount} (got {args.Count}).";
        if (CheckAccess(caller, args.To ?? InventoryRef.OfPlayer(caller.Peer), out var to) is { } bad)
            return bad;
        return to!.SpaceFor(args.Item) > 0 ? null : $"There is no room for {Name(args.Item)}.";
    }

    private JsonElement? ApplyGive(ActionCaller caller, GiveItemsArgs args) =>
        Value(new GiveItemsResult(Find(args.To ?? InventoryRef.OfPlayer(caller.Peer))!.Add(args.Item, args.Count)));

    // ---- Shared ----

    private static float SpeedOf(PartDefinition station) => station.Crafting?.Speed ?? 1f;

    private string Name(string item) => catalog.FindDefinition(item)?.Name ?? item;

    private string? CheckReach(ActionCaller caller, Creation creation, int partId)
    {
        if (caller.Source != ActionSource.Player)
            return null;
        if (world.EyeOf(caller.Peer) is not { } eye)
            return "You have no player to reach it with.";
        var part = creation.FindPart(partId)!;
        var pose = creation.BodyPose(creation.BodyIndexOf(partId));
        var definition = creation.DefinitionOf(partId);
        var centre = pose.Apply(PartGeometry.CentreMetres(definition, part.Cell, part.Rotation).ToNumerics());
        // Measure to the part's surface (its bounding sphere), so a big station is usable from any side.
        var radius = (definition.Size ?? GridSize.One).Metres.Length / 2;
        var distance = Math.Max(0, Vector3.Distance(eye, centre) - radius);
        return distance <= settings.Reach ? null : $"That is {distance:F1} m away; you can reach {settings.Reach:F1} m.";
    }

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}
