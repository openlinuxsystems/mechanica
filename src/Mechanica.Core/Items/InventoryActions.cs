using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Sound;

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

/// <summary>Arguments of <c>UpgradePart</c>: raise a placed part one upgrade level.</summary>
/// <param name="Creation">Creation holding the part.</param>
/// <param name="Part">The part.</param>
public sealed record UpgradePartArgs(int Creation, int Part);

/// <summary>Arguments of <c>NameContainer</c>: name a container that feeds a vehicle gauge (<see cref="VehicleGauges"/>).</summary>
/// <param name="Creation">Creation holding the container.</param>
/// <param name="Part">The container.</param>
/// <param name="Name">Its new name, at most 20 characters; null or blank gives it back its default ("Gas 1").</param>
public sealed record NameContainerArgs(int Creation, int Part, string? Name);

/// <summary>Value of a successful <c>UpgradePart</c>.</summary>
/// <param name="Level">The part's level now.</param>
public sealed record UpgradePartResult(int Level);

/// <summary>Arguments of <c>GiveItems</c> (Creative only).</summary>
/// <param name="Item">Item id.</param>
/// <param name="Count">How many (1 or more).</param>
/// <param name="To">Where they go; null means the caller's own inventory.</param>
public sealed record GiveItemsArgs(string Item, int Count, InventoryRef? To = null);

/// <summary>Arguments of <c>SortInventory</c>: auto-organize an inventory (the inventory screen's Organize button).</summary>
/// <param name="Inventory">The inventory to organize: the caller's own, or a container in reach.</param>
public sealed record SortInventoryArgs(InventoryRef Inventory);

/// <summary>Value of a successful <c>SortInventory</c>.</summary>
/// <param name="Changed">True when anything moved (false when it was already in order).</param>
public sealed record SortInventoryResult(bool Changed);

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
/// <param name="QueueSize">Most orders a station takes at once.</param>
/// <param name="Linked">Containers linked to a station (<see cref="StorageLinks"/>), by part id; null for other inventories.</param>
/// <param name="Level">The part's upgrade level.</param>
/// <param name="MaxLevel">The part's top upgrade level (0 when it has none).</param>
public sealed record InventoryView(
    InventoryRef Inventory,
    int SlotCount,
    IReadOnlyList<SlotView> Items,
    string? Part = null,
    IReadOnlyList<string>? Accepts = null,
    IReadOnlyList<CraftJobView>? Queue = null,
    float? Speed = null,
    bool Blocked = false,
    int? QueueSize = null,
    IReadOnlyList<InventoryRef>? Linked = null,
    int Level = 0,
    int MaxLevel = 0)
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
/// <param name="CreationRevision">Its Creation's revision (links and levels follow the build), or 0 for a player's.</param>
public sealed record InventoryVersion(Inventory Inventory, int Revision, int QueueRevision, int CreationRevision = 0);

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

    /// <summary>Plays a one-shot sound event at a world position (a station starting or finishing a craft).</summary>
    void PlaySound(string sound, string ownerModId, Vector3 position)
    {
    }

    /// <summary>
    /// Starts the loop <paramref name="sound"/> on a part, replacing any loop already set there, or stops it
    /// when <paramref name="sound"/> is null (a station working its queue).
    /// </summary>
    void SetPartLoop(int creation, int partId, string? sound, string ownerModId)
    {
    }

    /// <summary>
    /// Sets the text readout <paramref name="id"/> on a part, or takes it away when <paramref name="text"/> is null (the
    /// clip a station plays as it works; <see cref="CraftingDefinition.Clips"/>).
    /// </summary>
    void SetReadout(int creation, int partId, string id, string? text)
    {
    }

    /// <summary>A part of <paramref name="creation"/> changed in a way every peer must draw again (an upgrade to a new model).</summary>
    void Changed(Creation creation)
    {
    }
}

/// <summary>
/// Inventories and crafting in the Action API (chunk 18), validated on the host like every other action:
/// <list type="bullet">
/// <item><c>MoveItems</c>: moves items between the caller's own inventory and containers in reach, or within one.</item>
/// <item><c>Craft</c>: takes a recipe's inputs from the station's linked storage (<see cref="StorageLinks"/>), then the
/// caller's inventory, and queues it at a station in reach. The station works its queue over time (<see cref="Tick"/>)
/// and puts what it makes in its linked storage, then its own inventory.</item>
/// <item><c>CancelCraft</c>: drops an order and returns the inputs of its unfinished crafts to the caller, then the
/// linked storage.</item>
/// <item><c>UpgradePart</c>: raises a part one upgrade level (<see cref="PartUpgrades"/>), taking the level's cost from
/// the caller's inventory unless <see cref="InventorySettings.FreeUpgrades"/> (Creative).</item>
/// <item><c>NameContainer</c>: names a container in reach that feeds a vehicle gauge (a Gas Container's "Main tank"), as
/// the seated players' gauges and its window show it.</item>
/// <item><c>SortInventory</c>: auto-organizes the caller's own inventory or a container in reach: merges stacks and sorts
/// them by inventory tab, then name (<see cref="Inventory.Sort"/>).</item>
/// <item><c>GiveItems</c>: makes items from nothing, only while <see cref="InventorySettings.AllowGive"/> (Creative).</item>
/// <item><c>GetInventory</c>: reads an inventory the caller may use (a query).</item>
/// </list>
/// Each player's own inventory lives here, keyed by peer; container and station inventories live in
/// their Creation's parts, so they move, split, weld and save with them. No caller can touch another
/// player's inventory. A container linked to a station in reach counts as in reach.
/// </summary>
public sealed class InventoryActions(IPartCatalog catalog, RecipeBook recipes, InventorySettings settings, IInventoryWorld world)
{
    // Stations whose work loop is playing.
    private HashSet<(int Creation, int Part)> _working = [];

    // Makes every clip readout a station writes new, so the same clip plays again.
    private int _clipCount;

    // Seconds since idle stations that work on their own last looked for inputs (they look twice a second).
    private float _autoClock = AutoInterval;

    private const float AutoInterval = 0.5f;

    /// <summary>Action name for moving items.</summary>
    public const string MoveItems = "MoveItems";

    /// <summary>Action name for queueing a craft.</summary>
    public const string Craft = "Craft";

    /// <summary>Action name for cancelling an order.</summary>
    public const string CancelCraft = "CancelCraft";

    /// <summary>Action name for upgrading a part.</summary>
    public const string UpgradePart = "UpgradePart";

    /// <summary>Action name for naming a container.</summary>
    public const string NameContainer = "NameContainer";

    /// <summary>Action name for auto-organizing an inventory.</summary>
    public const string SortInventory = "SortInventory";

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
        dispatcher.Register<SortInventoryArgs>(SortInventory, (c, a) => CheckAccess(c, a.Inventory, out _), ApplySort);
        dispatcher.Register<GiveItemsArgs>(GiveItems, ValidateGive, ApplyGive);
        dispatcher.Register<UpgradePartArgs>(UpgradePart, ValidateUpgrade, ApplyUpgrade);
        dispatcher.Register<NameContainerArgs>(NameContainer, ValidateName, ApplyName);
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
        var definition = creation.DefinitionOf(part.Id);
        var queue = creation.CraftingOf(part.Id);
        return new InventoryView(where, inventory.SlotCount, items, part.Part, accepts,
            queue?.Jobs.Select(j => new CraftJobView(j.Recipe, j.Count, j.Progress, recipes.Find(j.Recipe)?.Time ?? 0)).ToList(),
            queue is null ? null : SpeedOf(definition),
            queue?.IsBlocked ?? false,
            queue is null ? null : definition.Crafting!.QueueSize ?? 1,
            queue is null ? null : [.. StorageLinks.Of(creation, part.Id).Select(id => InventoryRef.OfPart(creation.Id, id))],
            part.Level,
            PartUpgrades.MaxLevel(creation.Catalog.FindDefinition(part.Part) ?? definition));
    }

    /// <summary>
    /// A token that changes whenever <see cref="View"/> of <paramref name="where"/> would; null when the
    /// inventory is gone. Sync compares it to decide when to send a view again.
    /// </summary>
    public InventoryVersion? Version(InventoryRef where)
    {
        if (Find(where) is not { } inventory)
            return null;
        var creation = where.IsPlayer ? null : world.FindCreation(where.Creation!.Value)!;
        var queue = creation?.CraftingOf(where.Part!.Value);
        // The inventory object follows its part, so after a weld or split renames the part it is a new one here.
        return new InventoryVersion(inventory, inventory.Revision, queue?.Revision ?? 0, creation?.Revision ?? 0);
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
        if (CheckReach(caller, creation, partId) is { } far
            && !StorageLinks.StationsOf(creation, partId).Any(station => CheckReach(caller, creation, station) is null))
            return far;
        inventory = found;
        return null;
    }

    /// <summary>
    /// Works every station's queue for <paramref name="seconds"/>. The host calls it every physics tick. A
    /// station plays its <c>start</c> sound when it begins working, its <c>work</c> loop while it works and
    /// its <c>complete</c> sound when crafts finish (<see cref="PartSounds"/>); the loop stops when the
    /// queue empties or the output is full.
    /// </summary>
    public void Tick(float seconds)
    {
        var working = new HashSet<(int Creation, int Part)>();
        _autoClock += seconds;
        var lookForInputs = _autoClock >= AutoInterval;
        if (lookForInputs)
            _autoClock = 0;
        foreach (var creation in world.AllCreations)
        {
            if (lookForInputs)
            {
                foreach (var part in creation.AutoStations)
                    StartOnItsOwn(creation, part);
            }
            foreach (var (part, queue) in creation.ActiveCrafting.ToList())
            {
                if (creation.InventoryOf(part) is not { } output)
                    continue;
                var definition = creation.DefinitionOf(part);
                var finished = queue.Tick(seconds, SpeedOf(definition), recipes, [.. Outputs(creation, part), output]);
                var owner = BuildActions.OwnerOf(creation.FindPart(part)!.Part);
                if (finished > 0 && PartSounds.For(definition, PartSounds.Complete) is { } complete)
                    world.PlaySound(complete, owner, BuildActions.PartCentre(creation, part));
                if (queue.IsEmpty || queue.IsBlocked)
                    continue;
                working.Add((creation.Id, part));
                if (_working.Contains((creation.Id, part)))
                    continue;
                if (PartSounds.For(definition, PartSounds.Start) is { } start)
                    world.PlaySound(start, owner, BuildActions.PartCentre(creation, part));
                if (PartSounds.For(definition, PartSounds.Work) is { } work)
                    world.SetPartLoop(creation.Id, part, work, owner);
                if (definition.Crafting?.Clips is { } clips && StartClips(clips) is { } begin)
                    world.SetReadout(creation.Id, part, CraftingDefinition.ClipReadout, $"{begin}#{++_clipCount}");
            }
        }
        foreach (var (creation, part) in _working.Where(w => !working.Contains(w)))
        {
            world.SetPartLoop(creation, part, null, Identifiers.VanillaModId);
            if (world.FindCreation(creation) is { } found && found.FindPart(part) is not null
                && found.DefinitionOf(part).Crafting?.Clips is { } clips)
                world.SetReadout(creation, part, CraftingDefinition.ClipReadout, clips.Complete is { } done ? $"{done}#{++_clipCount}" : null);
        }
        _working = working;
    }

    /// <summary>
    /// Starts one craft at an idle station that works on its own (<see cref="CraftingDefinition.Auto"/>): the first of its
    /// recipes, by order, whose inputs are in its near storage, the containers piped to its input ports or its own slots.
    /// </summary>
    private void StartOnItsOwn(Creation creation, int part)
    {
        if (creation.CraftingOf(part) is not { IsEmpty: true } queue || creation.InventoryOf(part) is not { } own
            || creation.FindPart(part) is not { } placed)
            return;
        var pool = new List<Inventory>(Inputs(creation, part)) { own };
        var recipe = recipes.ForStation(placed.Part).OrderBy(r => r.Definition.Order ?? int.MaxValue)
            .FirstOrDefault(r => ItemPool.HasAll(pool, r.Inputs));
        if (recipe is null)
            return;
        foreach (var input in recipe.Inputs)
            ItemPool.Remove(pool, input.Item, input.Count);
        queue.Enqueue(new CraftJob(recipe.QualifiedId, 1));
    }

    // What a station plays when it begins working: its start clip, then its work clip (the readout's "a>b").
    private static string? StartClips(StationClipsDefinition clips) => (clips.Start, clips.Work) switch
    {
        ({ } start, { } work) => $"{start}>{work}",
        ({ } start, null) => start,
        (null, var work) => work,
    };

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
        var pool = Supplies(caller, creation, args.Part);
        foreach (var input in recipe.InputsFor(args.Count))
        {
            var have = ItemPool.Count(pool, input.Item);
            if (have < input.Count)
                return $"You need {input.Count} {Name(input.Item)} and have {have}.";
        }
        return null;
    }

    /// <summary>
    /// Where a craft at a station takes its inputs from, in order: the station's near storage and the containers piped to its
    /// input ports, then the caller's own inventory (a script or the host has none).
    /// </summary>
    public IReadOnlyList<Inventory> Supplies(ActionCaller caller, Creation creation, int station) =>
        [.. Inputs(creation, station), PlayerInventory(caller.Peer)];

    // The inventories a station takes inputs from: its near containers and those piped to its input ports.
    private static IEnumerable<Inventory> Inputs(Creation creation, int station) =>
        StorageLinks.InputsOf(creation, station).Select(creation.InventoryOf).OfType<Inventory>();

    // The inventories a station's crafts go to before its own: its near containers and those piped to its output ports.
    private static IEnumerable<Inventory> Outputs(Creation creation, int station) =>
        StorageLinks.OutputsOf(creation, station).Select(creation.InventoryOf).OfType<Inventory>();

    private JsonElement? ApplyCraft(ActionCaller caller, CraftArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        var queue = creation.CraftingOf(args.Part)!;
        var recipe = recipes.Find(args.Recipe)!;
        var pool = Supplies(caller, creation, args.Part);
        foreach (var input in recipe.InputsFor(args.Count))
            ItemPool.Remove(pool, input.Item, input.Count);
        queue.Enqueue(new CraftJob(recipe.QualifiedId, args.Count));
        return Value(new CraftResult(args.Count, queue.Jobs.Count - 1));
    }

    private string? ValidateCancel(ActionCaller caller, CancelCraftArgs args)
    {
        var bad = CheckStation(caller, args.Creation, args.Part, out var creation, out var found);
        if (bad is not null || found is not { } queue)
            return bad;
        if (args.Job < 0 || args.Job >= queue.Jobs.Count)
            return queue.IsEmpty ? "Nothing is queued." : $"job must be 0 to {queue.Jobs.Count - 1} (got {args.Job}).";
        var refund = Refund(queue.Jobs[args.Job]);
        return ItemPool.CanAddAll(Returns(caller, creation!, args.Part), refund) ? null : "Make room in your inventory for the returned items first.";
    }

    private JsonElement? ApplyCancel(ActionCaller caller, CancelCraftArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        var queue = creation.CraftingOf(args.Part)!;
        var refund = Refund(queue.RemoveAt(args.Job));
        ItemPool.TryAddAll(Returns(caller, creation, args.Part), refund);
        return Value(new CancelCraftResult(refund));
    }

    // Where a cancelled order's inputs go: the caller's own inventory, then the station's linked storage.
    private IReadOnlyList<Inventory> Returns(ActionCaller caller, Creation creation, int station) =>
        [PlayerInventory(caller.Peer), .. Inputs(creation, station)];

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

    // ---- UpgradePart ----

    /// <summary>
    /// What raising the part one level costs (empty when it is free or at its top level), as qualified amounts.
    /// </summary>
    public IReadOnlyList<ItemAmount> UpgradeCost(Creation creation, int partId)
    {
        if (settings.FreeUpgrades || creation.FindPart(partId) is not { } part || catalog.FindDefinition(part.Part) is not { } written
            || part.Level >= PartUpgrades.MaxLevel(written))
            return [];
        return [.. (written.Upgrades![part.Level].Cost ?? []).Select(c => new ItemAmount(Identifiers.Qualify(c.Item!), c.Count ?? 1))];
    }

    private string? ValidateUpgrade(ActionCaller caller, UpgradePartArgs args)
    {
        if (world.FindCreation(args.Creation) is not { } creation)
            return $"There is no creation {args.Creation}.";
        if (creation.FindPart(args.Part) is not { } part)
            return $"Creation {args.Creation} has no part {args.Part}.";
        if (WorldOwned.Refusal(creation, "upgraded") is { } owned)
            return owned;
        var name = creation.DefinitionOf(args.Part).Name ?? part.Part;
        var top = PartUpgrades.MaxLevel(catalog.FindDefinition(part.Part) ?? creation.DefinitionOf(args.Part));
        if (top == 0)
            return $"The {name} can't be upgraded.";
        if (part.Level >= top)
            return $"The {name} is already at its top level ({top}).";
        if (CheckReach(caller, creation, args.Part) is { } far)
            return far;
        var pockets = PlayerInventory(caller.Peer);
        foreach (var cost in UpgradeCost(creation, args.Part))
        {
            var have = pockets.Count(cost.Item);
            if (have < cost.Count)
                return $"You need {cost.Count} {Name(cost.Item)} and have {have}.";
        }
        return null;
    }

    private JsonElement? ApplyUpgrade(ActionCaller caller, UpgradePartArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        var pockets = PlayerInventory(caller.Peer);
        foreach (var cost in UpgradeCost(creation, args.Part))
            pockets.Remove(cost.Item, cost.Count);
        creation.SetLevel(args.Part, creation.LevelOf(args.Part) + 1);
        world.Changed(creation);
        return Value(new UpgradePartResult(creation.LevelOf(args.Part)));
    }

    // ---- NameContainer ----

    private string? ValidateName(ActionCaller caller, NameContainerArgs args)
    {
        if (CheckAccess(caller, InventoryRef.OfPart(args.Creation, args.Part), out _) is { } bad)
            return bad;
        var creation = world.FindCreation(args.Creation)!;
        if (WorldOwned.Refusal(creation, "renamed") is { } owned)
            return owned;
        if (creation.DefinitionOf(args.Part).Inventory?.Gauge is null)
            return $"The {creation.DefinitionOf(args.Part).Name ?? creation.FindPart(args.Part)!.Part} has no gauge to name.";
        return args.Name is { } name ? VehicleGauges.CheckName(name.Trim()) : null;
    }

    private JsonElement? ApplyName(ActionCaller caller, NameContainerArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        if (creation.SetPartName(args.Part, args.Name))
            world.Changed(creation);
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

    // ---- SortInventory ----

    private JsonElement? ApplySort(ActionCaller caller, SortInventoryArgs args) =>
        Value(new SortInventoryResult(Find(args.Inventory)!.Sort(SortKey, SortOrder)));

    /// <summary>
    /// Where <see cref="SortInventory"/> puts an item: by inventory tab in tab order (items under no tab last), then by
    /// name.
    /// </summary>
    public (int Tab, string Name) SortKey(string item)
    {
        var definition = catalog.FindDefinition(item);
        var tab = InventoryTabs.Of(definition, HotbarTools.IsTool(item));
        return (tab is { } t ? (int)t : int.MaxValue, definition?.Name ?? item);
    }

    private static readonly IComparer<(int Tab, string Name)> SortOrder = Comparer<(int Tab, string Name)>.Create((a, b) =>
        a.Tab != b.Tab ? a.Tab.CompareTo(b.Tab) : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));

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
