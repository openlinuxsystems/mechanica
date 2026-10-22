using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Items;

/// <summary>What kind of thing an intake can take in (<see cref="PickupView"/>).</summary>
public enum PickupKind
{
    /// <summary>A pile of items on the ground.</summary>
    Drop,

    /// <summary>A loose part lying about (a log, a rod, a chunk): its Creation goes and the part comes in as an item.</summary>
    Loose,

    /// <summary>A ripe crop on a soil part: it is harvested (the soil's script says what it gives).</summary>
    Crop,

    /// <summary>Water the intake is in: one full bucket's worth goes into a container that takes it.</summary>
    Water,
}

/// <summary>Arguments of <c>CollectItems</c>: the part whose intake collects, and what (one of <c>GetPickups</c>' entries).</summary>
/// <param name="Creation">The collecting part's Creation.</param>
/// <param name="Part">The collecting part (it has an <c>intake</c>).</param>
/// <param name="Kind">What it collects: <c>drop</c>, <c>loose</c>, <c>crop</c> or <c>water</c>.</param>
/// <param name="Drop">The pile, for <c>drop</c>.</param>
/// <param name="TargetCreation">The loose part's or the soil's Creation, for <c>loose</c> and <c>crop</c>.</param>
/// <param name="TargetPart">The soil part, for <c>crop</c>.</param>
public sealed record CollectItemsArgs(int Creation, int Part, PickupKind Kind, int? Drop = null, int? TargetCreation = null, int? TargetPart = null);

/// <summary>What <c>CollectItems</c> took in.</summary>
/// <param name="Items">Items now in the containers (what did not fit of a harvest dropped where the crop was).</param>
public sealed record CollectItemsResult(IReadOnlyList<ItemStack> Items);

/// <summary>Arguments of <c>EjectItems</c>: the part whose intake puts items out, and what.</summary>
/// <param name="Creation">The ejecting part's Creation.</param>
/// <param name="Part">The ejecting part (it has an <c>intake</c>).</param>
/// <param name="Item">The item to put out; omitted takes the first item the containers piped to it hold.</param>
/// <param name="Count">How many, 1 to the item's stack size.</param>
public sealed record EjectItemsArgs(int Creation, int Part, string? Item = null, int Count = 1);

/// <summary>What <c>EjectItems</c> put out.</summary>
/// <param name="Item">The item.</param>
/// <param name="Count">How many.</param>
/// <param name="Drop">The pile they landed in.</param>
public sealed record EjectItemsResult(string Item, int Count, int Drop);

/// <summary>Arguments of <c>GetNetwork</c> and <c>GetPickups</c>: one part of a Creation.</summary>
/// <param name="Creation">The Creation.</param>
/// <param name="Part">The part.</param>
public sealed record TransportPartArgs(int Creation, int Part);

/// <summary>One part an IO port reaches (<c>GetNetwork</c>), as <see cref="PipeLink"/> with what the part is.</summary>
/// <param name="Port">The asking part's port the link leaves through.</param>
/// <param name="Part">The part reached.</param>
/// <param name="Type">Its qualified type.</param>
/// <param name="RemotePort">Its port.</param>
/// <param name="TakesIn">True when items may go in through that port.</param>
/// <param name="GivesOut">True when items may come out through that port.</param>
/// <param name="Storage">True for a container (an inventory that is not a station).</param>
/// <param name="Station">True for a crafting station.</param>
/// <param name="Pipes">The pipes between, from the asking part's side.</param>
public sealed record PipeLinkView(string Port, int Part, string Type, string RemotePort, bool TakesIn, bool GivesOut, bool Storage, bool Station,
    IReadOnlyList<int> Pipes);

/// <summary>Something an intake reaches (<c>GetPickups</c>), nearest first.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Position">Where it is, in world metres.</param>
/// <param name="Distance">How far from the intake, in m.</param>
/// <param name="Items">What it would give (a crop: one of its crop; the soil's script decides the harvest).</param>
/// <param name="Drop">The pile, for a drop.</param>
/// <param name="Creation">The loose part's or the soil's Creation.</param>
/// <param name="Part">The loose part or the soil part.</param>
public sealed record PickupView(PickupKind Kind, Vec3 Position, float Distance, IReadOnlyList<ItemStack> Items, int? Drop = null,
    int? Creation = null, int? Part = null);

/// <summary>
/// Item transport (Parts map E1), run on the host: Vacuum Pumps and Resource Collectors take things in through their
/// intake and push items along their pipe network (<see cref="TransportNetwork"/>). When and what is the parts' Lua
/// scripts' business (D2); these actions are the generic, validated primitives they call. <c>CollectItems</c> takes a
/// pile, a loose part, a ripe crop or water within the intake's reach into the part's own inventory (a collector) or the
/// containers its ports send to; <c>EjectItems</c> takes items from the containers piped to it and throws them out of the
/// intake as a pile. Only part scripts collect and eject; <c>GetNetwork</c> and <c>GetPickups</c> read for anyone.
/// Everything that changes goes through the world's ordinary sync (piles, parts, soil, inventories), so clients see it.
/// </summary>
public sealed class TransportActions(IPartCatalog catalog, SurvivalActions survival)
{
    /// <summary>Action name for taking one thing in through a part's intake (<see cref="CollectItemsArgs"/>); part scripts only.</summary>
    public const string CollectItems = "CollectItems";

    /// <summary>Action name for putting items out of a part's intake (<see cref="EjectItemsArgs"/>); part scripts only.</summary>
    public const string EjectItems = "EjectItems";

    /// <summary>Action name for reading what a part's IO ports reach through Vacuum Pipes.</summary>
    public const string GetNetwork = "GetNetwork";

    /// <summary>Action name for reading what a part's intake reaches: piles, loose parts, ripe crops and water.</summary>
    public const string GetPickups = "GetPickups";

    // Loose parts are found by their stale pose first, with this much slack (m), then measured exactly.
    private const float CoarseSlack = 4f;

    private ISurvivalWorld World => survival.World;

    /// <summary>
    /// Harvests a ripe crop for a machine (the soil part's script, <c>on_collect</c>): what it gives, or null when it gives
    /// nothing. The script runtime sets it; without it crops are not collected.
    /// </summary>
    public Func<Creation, int, IReadOnlyList<ItemStack>?>? HarvestCrop { get; set; }

    /// <summary>Registers the actions.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<CollectItemsArgs>(CollectItems, ValidateCollect, ApplyCollect);
        dispatcher.Register<EjectItemsArgs>(EjectItems, ValidateEject, ApplyEject);
        dispatcher.Register<TransportPartArgs>(GetNetwork, ValidatePart,
            (_, args) => Value(Network(World.FindCreation(args.Creation)!, args.Part)), readOnly: true);
        dispatcher.Register<TransportPartArgs>(GetPickups, ValidateIntake,
            (_, args) => Value(Pickups(World.FindCreation(args.Creation)!, args.Part)), readOnly: true);
    }

    /// <summary>What <paramref name="part"/>'s IO ports reach, with what each part is.</summary>
    public static IReadOnlyList<PipeLinkView> Network(Creation creation, int part) =>
        [.. TransportNetwork.Reach(creation, part).Select(l => new PipeLinkView(l.Port, l.Part, Identifiers.Qualify(creation.FindPart(l.Part)!.Part),
            l.RemotePort, l.TakesIn, l.GivesOut, StorageLinks.IsContainer(creation, l.Part), creation.DefinitionOf(l.Part).Crafting is not null, l.Pipes))];

    /// <summary>The intake's middle and the way it faces, in world space; null when the part has no intake.</summary>
    public static (Vector3 Position, Vector3 Normal, IntakeDefinition Intake)? IntakeOf(Creation creation, int part)
    {
        if (creation.FindPart(part) is not { } instance || creation.DefinitionOf(part).Intake is not { } intake)
            return null;
        var definition = creation.DefinitionOf(part);
        var pose = creation.BodyPose(creation.BodyIndexOf(part));
        var local = PartGeometry.CentreMetres(definition, instance.Cell, instance.Rotation) + instance.Rotation.Apply(intake.Position ?? Vec3.Zero);
        var normal = Vector3.Transform(instance.Rotation.Apply(intake.Normal ?? new Vec3(0, 0, 1)).ToNumerics(), pose.Rotation);
        return (pose.Apply(local.ToNumerics()), normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitY, intake);
    }

    /// <summary>
    /// Everything <paramref name="part"/>'s intake reaches, nearest first: piles (not a knocked-out player's bag), loose parts
    /// of other Creations, ripe crops and the water it is in. Empty when it has no intake.
    /// </summary>
    public IReadOnlyList<PickupView> Pickups(Creation creation, int part)
    {
        if (IntakeOf(creation, part) is not var (at, _, intake))
            return [];
        var reach = intake.Reach ?? 0f;
        var found = new List<PickupView>();
        foreach (var drop in survival.Drops.Values)
        {
            var d = Vector3.Distance(drop.Position.ToNumerics(), at);
            if (!drop.KnockedOut && d <= reach)
                found.Add(new PickupView(PickupKind.Drop, drop.Position, d, drop.Items, Drop: drop.Id));
        }
        foreach (var candidate in World.AllCreations.ToList())
        {
            if (candidate.Id == creation.Id || candidate.IsEmpty)
                continue;
            var loose = IsLooseCreation(candidate);
            if (!loose && !candidate.ActiveSoil.Any(s => s.Soil.Ripe))
                continue;
            // A cheap look at the stale pose first: most Creations are nowhere near.
            if (candidate.Parts.All(p => Vector3.Distance(Centre(candidate, p.Id), at) > reach + CoarseSlack)
                || World.FindCreation(candidate.Id) is not { } other)
                continue;
            if (loose)
            {
                var near = other.Parts.Select(p => (p.Id, Centre: Centre(other, p.Id))).MinBy(p => Vector3.Distance(p.Centre, at));
                var d = Vector3.Distance(near.Centre, at);
                if (d <= reach)
                    found.Add(new PickupView(PickupKind.Loose, near.Centre.ToVec3(), d,
                        [.. other.Parts.Select(p => new ItemStack(Identifiers.Qualify(p.Part), 1))], Creation: other.Id, Part: near.Id));
                continue;
            }
            foreach (var (soilPart, soil) in other.ActiveSoil.Where(s => s.Soil.Ripe).ToList())
            {
                var centre = Centre(other, soilPart);
                var d = Vector3.Distance(centre, at);
                if (d <= reach && CropOf(soil) is { } crop)
                    found.Add(new PickupView(PickupKind.Crop, centre.ToVec3(), d, [new ItemStack(crop, 1)], Creation: other.Id, Part: soilPart));
            }
        }
        // Soil on the part's own Creation (a pump over a Growbed on the same cart).
        foreach (var (soilPart, soil) in creation.ActiveSoil.Where(s => s.Soil.Ripe).ToList())
        {
            var centre = Centre(creation, soilPart);
            var d = Vector3.Distance(centre, at);
            if (d <= reach && CropOf(soil) is { } crop)
                found.Add(new PickupView(PickupKind.Crop, centre.ToVec3(), d, [new ItemStack(crop, 1)], Creation: creation.Id, Part: soilPart));
        }
        if (World.IsWater(at) && catalog.FindDefinition(survival.Settings.WaterItem) is not null)
            found.Add(new PickupView(PickupKind.Water, at.ToVec3(), 0f, [new ItemStack(Identifiers.Qualify(survival.Settings.WaterItem), 1)]));
        return [.. found.OrderBy(f => f.Distance).ThenBy(f => f.Kind).ThenBy(f => f.Drop ?? 0).ThenBy(f => f.Creation ?? 0).ThenBy(f => f.Part ?? 0)];
    }

    /// <summary>
    /// The inventories what <paramref name="part"/> collects goes into, in order: its own when it has one (a Resource
    /// Collector), else the containers its ports send to.
    /// </summary>
    public static IReadOnlyList<Inventory> Destinations(Creation creation, int part) =>
        creation.InventoryOf(part) is { } own ? [own] : [.. TransportNetwork.Into(creation, part).Select(creation.InventoryOf).OfType<Inventory>()];

    /// <summary>
    /// The inventories what <paramref name="part"/> ejects comes from, in order: its own when it has one, then the containers
    /// piped to send to it.
    /// </summary>
    public static IReadOnlyList<Inventory> Sources(Creation creation, int part) =>
        [.. new[] { creation.InventoryOf(part) }.OfType<Inventory>(),
            .. TransportNetwork.From(creation, part).Select(creation.InventoryOf).OfType<Inventory>()];

    // ---- CollectItems ----

    private string? ValidateCollect(ActionCaller caller, CollectItemsArgs args)
    {
        if (caller.Source != ActionSource.Script)
            return "Only a part's script collects with its intake (a Vacuum Pump, a Resource Collector).";
        if (ValidateIntake(caller, new TransportPartArgs(args.Creation, args.Part)) is { } bad)
            return bad;
        var creation = World.FindCreation(args.Creation)!;
        var into = Destinations(creation, args.Part);
        if (into.Count == 0)
            return "Nothing is piped to it to take things in.";
        var target = Find(creation, args);
        if (target is null)
            return args.Kind switch
            {
                PickupKind.Drop => $"There is no pile {args.Drop} in reach.",
                PickupKind.Loose => $"There is no loose part of creation {args.TargetCreation} in reach.",
                PickupKind.Crop => $"There is no ripe crop on part {args.TargetPart} of creation {args.TargetCreation} in reach.",
                _ => "It is not in water.",
            };
        switch (args.Kind)
        {
            case PickupKind.Loose:
                return ItemPool.CanAddAll(into, target.Items.Select(s => new ItemAmount(s.Item, s.Count))) ? null : "There is no room for it.";
            case PickupKind.Crop:
                if (HarvestCrop is null)
                    return "Crops cannot be collected here.";
                break;
            case PickupKind.Water:
                // Water goes only into what is made for it (a Water Container), never into a chest.
                var water = target.Items[0].Item;
                return into.Any(i => i.AcceptedItems?.Contains(water) == true && i.SpaceFor(water) > 0) ? null : "Nothing piped to it holds water.";
        }
        return target.Items.Any(s => into.Any(i => i.SpaceFor(s.Item) > 0)) ? null : "There is no room for it.";
    }

    private JsonElement? ApplyCollect(ActionCaller caller, CollectItemsArgs args)
    {
        var creation = World.FindCreation(args.Creation)!;
        var into = Destinations(creation, args.Part);
        var target = Find(creation, args)!;
        IReadOnlyList<ItemStack> moved;
        switch (args.Kind)
        {
            case PickupKind.Drop:
                moved = survival.TakeDrop(args.Drop!.Value, into);
                break;
            case PickupKind.Loose:
                var loose = World.FindCreation(args.TargetCreation!.Value)!;
                ItemPool.TryAddAll(into, [.. target.Items.Select(s => new ItemAmount(s.Item, s.Count))]);
                foreach (var id in loose.Parts.Select(p => p.Id).ToList())
                {
                    if (World.FindCreation(loose.Id)?.FindPart(id) is not null)
                        World.RemovePart(loose, id);
                }
                survival.PlayPickupSound(target.Items[0].Item, target.Position.ToNumerics());
                moved = target.Items;
                break;
            case PickupKind.Crop:
                var soil = World.FindCreation(args.TargetCreation!.Value)!;
                var harvest = HarvestCrop!(soil, args.TargetPart!.Value) ?? [];
                moved = Put(into, harvest, out var spilt);
                if (spilt.Count > 0)
                    survival.AddDrop(target.Position.ToNumerics(), spilt);
                if (harvest.Count > 0)
                    survival.PlayPickupSound(harvest[0].Item, target.Position.ToNumerics());
                break;
            default:
                var water = target.Items[0].Item;
                var tank = into.First(i => i.AcceptedItems?.Contains(water) == true && i.SpaceFor(water) > 0);
                tank.Add(water, 1);
                moved = [new ItemStack(water, 1)];
                break;
        }
        return Value(new CollectItemsResult(moved));
    }

    // What the action names, still within the intake's reach; null when it is gone or out of reach.
    private PickupView? Find(Creation creation, CollectItemsArgs args)
    {
        var pickups = Pickups(creation, args.Part);
        return args.Kind switch
        {
            PickupKind.Drop => pickups.FirstOrDefault(p => p.Kind == PickupKind.Drop && p.Drop == args.Drop),
            PickupKind.Loose => pickups.FirstOrDefault(p => p.Kind == PickupKind.Loose && p.Creation == args.TargetCreation),
            PickupKind.Crop => pickups.FirstOrDefault(p => p.Kind == PickupKind.Crop && p.Creation == args.TargetCreation && p.Part == args.TargetPart),
            _ => pickups.FirstOrDefault(p => p.Kind == PickupKind.Water),
        };
    }

    // Adds what fits of each stack into the inventories in order: what went in, and what did not fit.
    private static List<ItemStack> Put(IReadOnlyList<Inventory> into, IReadOnlyList<ItemStack> items, out List<ItemStack> left)
    {
        var moved = new List<ItemStack>();
        left = [];
        foreach (var stack in items)
        {
            var added = 0;
            foreach (var inventory in into)
            {
                if (added == stack.Count)
                    break;
                added += inventory.Add(stack.Item, stack.Count - added);
            }
            if (added > 0)
                moved.Add(stack with { Count = added });
            if (added < stack.Count)
                left.Add(stack with { Count = stack.Count - added });
        }
        return moved;
    }

    // ---- EjectItems ----

    private string? ValidateEject(ActionCaller caller, EjectItemsArgs args)
    {
        if (caller.Source != ActionSource.Script)
            return "Only a part's script ejects items from its intake (a Vacuum Pump).";
        if (ValidateIntake(caller, new TransportPartArgs(args.Creation, args.Part)) is { } bad)
            return bad;
        var creation = World.FindCreation(args.Creation)!;
        var from = Sources(creation, args.Part);
        if (from.Count == 0)
            return "Nothing is piped to it to put out.";
        if (args.Item is { } item)
        {
            if (catalog.FindDefinition(item) is not { } definition)
                return $"Unknown item '{item}'.";
            var stack = Math.Max(1, definition.StackSize ?? 1);
            if (args.Count < 1 || args.Count > stack)
                return $"count must be 1 to {stack} (got {args.Count}).";
            return ItemPool.Count(from, item) > 0 ? null : $"Nothing piped to it holds {definition.Name ?? item}.";
        }
        if (args.Count < 1)
            return $"count must be 1 or more (got {args.Count}).";
        return FirstItem(from) is null ? "The containers piped to it are empty." : null;
    }

    private JsonElement? ApplyEject(ActionCaller caller, EjectItemsArgs args)
    {
        var creation = World.FindCreation(args.Creation)!;
        var from = Sources(creation, args.Part);
        var item = Identifiers.Qualify(args.Item ?? FirstItem(from)!);
        var count = Math.Min(args.Count, Math.Max(1, catalog.FindDefinition(item)?.StackSize ?? 1));
        var taken = ItemPool.Remove(from, item, count);
        var (at, normal, intake) = IntakeOf(creation, args.Part)!.Value;
        var lands = at + normal * (intake.Throw ?? (intake.Reach ?? 0f) / 2f);
        var drop = survival.AddDrop(lands, [new ItemStack(item, taken)], tossedFrom: at);
        return Value(new EjectItemsResult(item, taken, drop?.Id ?? 0));
    }

    // The first item the inventories hold, in order, slot by slot.
    private static string? FirstItem(IReadOnlyList<Inventory> from) =>
        from.SelectMany(i => i.Slots).FirstOrDefault(s => s is not null)?.Item;

    // ---- Shared ----

    private string? ValidatePart(ActionCaller caller, TransportPartArgs args)
    {
        if (World.FindCreation(args.Creation) is not { } creation)
            return $"There is no creation {args.Creation}.";
        return creation.FindPart(args.Part) is null ? $"Creation {args.Creation} has no part {args.Part}." : null;
    }

    private string? ValidateIntake(ActionCaller caller, TransportPartArgs args)
    {
        if (ValidatePart(caller, args) is { } bad)
            return bad;
        var definition = World.FindCreation(args.Creation)!.DefinitionOf(args.Part);
        return definition.Intake is null ? $"The {definition.Name ?? "part"} has no intake." : null;
    }

    // A Creation made only of loose parts (a log, a rod): what an intake takes in whole.
    private static bool IsLooseCreation(Creation creation) =>
        !creation.IsWorldOwned && !creation.OnLift && creation.Parts.All(p => creation.DefinitionOf(p.Id) is { Loose: true, IsItem: true });

    // The crop a ripe soil part grows (its seed's data), qualified; null when the seed is unknown.
    private string? CropOf(SoilState soil) =>
        soil.Seed is { } seed && catalog.FindDefinition(seed)?.Seed?.Crop is { } crop
            ? crop.Contains(':') ? crop : $"{seed.Split(':')[0]}:{crop}"
            : null;

    private static Vector3 Centre(Creation creation, int partId)
    {
        var part = creation.FindPart(partId)!;
        return creation.BodyPose(creation.BodyIndexOf(partId))
            .Apply(PartGeometry.CentreMetres(creation.DefinitionOf(partId), part.Cell, part.Rotation).ToNumerics());
    }

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}
