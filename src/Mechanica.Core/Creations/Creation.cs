using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Creations;

/// <summary>
/// A Creation: placed parts on one 0.25 m grid, joined by <see cref="Connection"/> edges. Rigidly
/// connected parts form <see cref="Bodies"/>; joint edges become <see cref="Joints"/> between them (D7).
/// It is the unit of loading, persistence and network authority (<see cref="AuthorityPeer"/>, D5).
/// <para>
/// A non-empty Creation is always connected: placing must attach to an existing part, and
/// <see cref="RemovePart"/> splits off pieces that lose their link. Pure logic; the host owns it
/// and the Action API calls into it.
/// </para>
/// </summary>
public sealed class Creation
{
    /// <summary>Godot's multiplayer peer id for the host.</summary>
    public const int HostPeer = 1;

    private readonly SortedDictionary<int, PartInstance> _parts = [];
    private readonly Dictionary<int, PartDefinition> _definitions = [];
    private readonly Dictionary<GridCell, int> _occupancy = [];
    private readonly EdgeSet<Connection> _connections = new(Connection.Order);
    private readonly EdgeSet<LogicLink> _links = new(LogicLink.Order);
    // LinksFrom and LinksTo, built from _links at _linksVersion.
    private Dictionary<int, int[]>? _linksFrom;
    private Dictionary<int, int[]>? _linksTo;
    private int _linksVersion = -1;
    // World pose of each part's Body, stored on every member so poses survive splits and removals.
    private readonly Dictionary<int, Pose> _poses = [];
    // Health each part has lost (Sledgehammer hits); parts at full health have no entry.
    private readonly Dictionary<int, float> _damage = [];
    // World velocity of each part's Body, stored the same way as poses.
    private readonly Dictionary<int, BodyVelocity> _velocities = [];
    // Item storage and crafting orders of container and station parts, made when first asked for.
    private readonly Dictionary<int, Inventory> _inventories = [];
    private readonly Dictionary<int, CraftingQueue> _crafting = [];
    private readonly Dictionary<int, SoilState> _soil = [];
    // How strongly each burning part burns (0 to 1); parts not on fire have no entry.
    private readonly Dictionary<int, float> _fire = [];
    // Parts that were placed resting on the world (terrain, rocks): their Bodies stay where they are.
    private readonly HashSet<int> _anchored = [];
    private Layout? _layout;
    private bool _onLift;
    private int _authorityPeer;
    private int _nextPartId = 1;

    /// <summary>Creates an empty Creation.</summary>
    public Creation(int id, IPartCatalog catalog, int authorityPeer = HostPeer)
    {
        Id = id;
        Catalog = catalog;
        AuthorityPeer = authorityPeer;
    }

    /// <summary>World-unique id.</summary>
    public int Id { get; }

    /// <summary>Where part definitions come from.</summary>
    public IPartCatalog Catalog { get; }

    /// <summary>Peer whose simulation is authoritative for this Creation. Host-only in v1 (D5).</summary>
    public int AuthorityPeer
    {
        get => _authorityPeer;
        set => _authorityPeer = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Peer ids start at 1.");
    }

    /// <summary>Keys this build does not know, kept from a loaded save.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>
    /// The POI <c>part</c> marker this Creation stands at (a <see cref="World.WorldParts"/> key, such as
    /// <c>vanilla:outpost#0@12</c>), or null for one players made. Such a Creation is world-owned: anyone can use it, nobody
    /// can change, move or damage it (<see cref="WorldOwned"/>). Pieces split off never carry it.
    /// </summary>
    public string? WorldPart { get; init; }

    /// <summary>
    /// For a world-owned container its marker fills with loot (<see cref="World.WorldLoot"/>): how often it has been filled
    /// and since when it has stood empty, waiting to refill. Null for every other Creation. Saved with it.
    /// </summary>
    public World.WorldLootState? WorldLoot { get; set; }

    /// <summary>True when the world owns this Creation (<see cref="WorldPart"/>).</summary>
    public bool IsWorldOwned => WorldPart is not null;

    /// <summary>
    /// True while a Lift holds the Creation: its Bodies stay where the Lift put them instead of simulating.
    /// Pieces that split off do not inherit it, so they drop.
    /// </summary>
    public bool OnLift
    {
        get => _onLift;
        set
        {
            if (_onLift == value)
                return;
            _onLift = value;
            // Peers need the whole Creation again to know it is held.
            Revision++;
        }
    }

    /// <summary>
    /// True when any part is anchored to the world (<see cref="IsAnchored(int)"/>): built on the ground, so at
    /// least one of its Bodies stays put instead of simulating, as in Scrap Mechanic.
    /// </summary>
    public bool IsGrounded => _anchored.Count > 0;

    /// <summary>Anchored part ids, ascending.</summary>
    public IReadOnlyList<int> AnchoredParts => [.. _anchored.Order()];

    /// <summary>True when part <paramref name="partId"/> rests on the world and holds its Body still.</summary>
    public bool IsAnchored(int partId) => _anchored.Contains(partId);

    /// <summary>True when the Body at <paramref name="bodyIndex"/> holds an anchored part, so it does not simulate.</summary>
    public bool IsBodyAnchored(int bodyIndex) =>
        _anchored.Count > 0 && Bodies[bodyIndex].PartIds.Any(_anchored.Contains);

    /// <summary>
    /// Anchors part <paramref name="partId"/> to the world or lets it go. Removing the part, or splitting it off,
    /// takes its anchor with it, so a piece left with none drops.
    /// </summary>
    public void SetAnchored(int partId, bool anchored)
    {
        if (!_parts.ContainsKey(partId))
            throw new ArgumentException($"Creation {Id} has no part {partId}.", nameof(partId));
        if (anchored ? _anchored.Add(partId) : _anchored.Remove(partId))
            Revision++;
    }

    /// <summary>Lets go of every anchor (a Lift picks the Creation up), so it simulates once it is free.</summary>
    public void ClearAnchors()
    {
        if (_anchored.Count == 0)
            return;
        _anchored.Clear();
        Revision++;
    }

    /// <summary>
    /// Counts graph changes (parts, edges, splits, merges), paint, anchor and <see cref="OnLift"/> changes. State
    /// sync compares it to know when a peer needs the whole Creation again; poses, velocities and part
    /// damage do not change it.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>Parts by ascending id.</summary>
    public IReadOnlyCollection<PartInstance> Parts => _parts.Values;

    /// <summary>Edges in a stable order (by A, B, type, sockets).</summary>
    public IReadOnlyList<Connection> Connections => _connections.Sorted;

    /// <summary>Logic links (Connect Tool, chunk 10), by source then target.</summary>
    public IReadOnlyList<LogicLink> Links => _links.Sorted;

    /// <summary>The parts <paramref name="partId"/>'s output feeds, ascending.</summary>
    public IReadOnlyList<int> LinksFrom(int partId)
    {
        IndexLinks();
        return _linksFrom!.GetValueOrDefault(partId) ?? [];
    }

    /// <summary>The parts feeding <paramref name="partId"/>'s inputs, ascending.</summary>
    public IReadOnlyList<int> LinksTo(int partId)
    {
        IndexLinks();
        return _linksTo!.GetValueOrDefault(partId) ?? [];
    }

    private void IndexLinks()
    {
        if (_linksVersion == _links.Version && _linksFrom is not null)
            return;
        _linksFrom = _links.GroupBy(l => l.From).ToDictionary(g => g.Key, g => g.Select(l => l.To).Order().ToArray());
        _linksTo = _links.GroupBy(l => l.To).ToDictionary(g => g.Key, g => g.Select(l => l.From).Order().ToArray());
        _linksVersion = _links.Version;
    }

    /// <summary>True when no parts are left.</summary>
    public bool IsEmpty => _parts.Count == 0;

    /// <summary>Rigid bodies, ordered by lowest part id.</summary>
    public IReadOnlyList<Body> Bodies => GetLayout().Bodies;

    /// <summary>Joint edges resolved to bodies, in <see cref="Connections"/> order.</summary>
    public IReadOnlyList<JointLink> Joints => GetLayout().Joints;

    /// <summary>
    /// The first joint (in <see cref="Joints"/> order) whose <see cref="Connection.A"/> side is <paramref name="partId"/>
    /// (the part's own bearing, piston or suspension), or null.
    /// </summary>
    public JointLink? JointOf(int partId) => GetLayout().JointOfPart.GetValueOrDefault(partId);

    /// <summary>The part with this id, or null.</summary>
    public PartInstance? FindPart(int partId) => _parts.GetValueOrDefault(partId);

    /// <summary>The definition a placed part uses.</summary>
    public PartDefinition DefinitionOf(int partId) => _definitions[partId];

    /// <summary>The part covering <paramref name="cell"/>, or null.</summary>
    public PartInstance? PartAt(GridCell cell) => _occupancy.TryGetValue(cell, out var id) ? _parts[id] : null;

    /// <summary>The cells a placed part covers.</summary>
    public IEnumerable<GridCell> CellsOf(int partId)
    {
        var part = _parts[partId];
        return PartGeometry.Cells(_definitions[partId], part.Cell, part.Rotation);
    }

    /// <summary>Index into <see cref="Bodies"/> of the body holding <paramref name="partId"/>.</summary>
    public int BodyIndexOf(int partId) => GetLayout().BodyOfPart[partId];

    /// <summary>
    /// World pose of a Body's grid frame (<see cref="Pose"/>). A Body whose parts carry different poses
    /// (after a merge) uses its lowest part's; a Body with none stored is at <see cref="Pose.Identity"/>.
    /// </summary>
    public Pose BodyPose(int bodyIndex) => StoredPose(Bodies[bodyIndex]) ?? Pose.Identity;

    /// <summary>True when the Body has a stored pose (from a save, a spawn or the simulation).</summary>
    public bool HasBodyPose(int bodyIndex) => StoredPose(Bodies[bodyIndex]) is not null;

    /// <summary>Sets a Body's world pose; the game writes simulated poses back with this before saving.</summary>
    public void SetBodyPose(int bodyIndex, Pose pose)
    {
        foreach (var id in Bodies[bodyIndex].PartIds)
            _poses[id] = pose;
    }

    /// <summary>
    /// World linear and angular velocity of a Body (from a save, a late-join sync or the simulation).
    /// Uses the same lowest-part rule as <see cref="BodyPose"/>; a Body with none stored is at rest.
    /// </summary>
    public BodyVelocity GetBodyVelocity(int bodyIndex) =>
        First(Bodies[bodyIndex], _velocities) ?? BodyVelocity.Zero;

    /// <summary>Sets a Body's world velocity; the game writes simulated velocities back with this before saving.</summary>
    public void SetBodyVelocity(int bodyIndex, BodyVelocity velocity)
    {
        foreach (var id in Bodies[bodyIndex].PartIds)
            _velocities[id] = velocity;
    }

    /// <summary>
    /// Sets the pose stored on one part, leaving its Body-mates alone. The runtime uses it after a graph
    /// change to hand each part the pose of the Body it was in, so the lowest-part rule settles merged Bodies.
    /// </summary>
    public void SetPartPose(int partId, Pose pose) =>
        _poses[_parts.ContainsKey(partId) ? partId : throw new ArgumentException($"Creation {Id} has no part {partId}.", nameof(partId))] = pose;

    /// <summary>Puts every Body at <paramref name="pose"/>, as when a Creation is spawned or reset.</summary>
    public void SetAllBodyPoses(Pose pose)
    {
        foreach (var id in _parts.Keys)
            _poses[id] = pose;
    }

    /// <summary>Health a part has lost; 0 when it is undamaged.</summary>
    public float PartDamage(int partId) => _damage.GetValueOrDefault(partId);

    /// <summary>Sets the health a part has lost (0 or less clears it).</summary>
    public void SetPartDamage(int partId, float damage)
    {
        if (!_parts.ContainsKey(partId))
            throw new ArgumentException($"Creation {Id} has no part {partId}.", nameof(partId));
        if (damage > 0)
            _damage[partId] = damage;
        else
            _damage.Remove(partId);
    }

    /// <summary>How strongly a part burns, 0 (not on fire) to 1. Fire follows the part through splits and welds.</summary>
    public float FireOf(int partId) => _fire.GetValueOrDefault(partId);

    /// <summary>Sets how strongly a part burns (clamped to 1; 0 or less puts it out).</summary>
    public void SetFire(int partId, float strength)
    {
        if (!_parts.ContainsKey(partId))
            throw new ArgumentException($"Creation {Id} has no part {partId}.", nameof(partId));
        if (strength > 0 && float.IsFinite(strength))
            _fire[partId] = Math.Min(1f, strength);
        else
            _fire.Remove(partId);
    }

    /// <summary>The parts on fire and how strongly, by part id.</summary>
    public IEnumerable<(int PartId, float Strength)> Burning => _fire.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value));

    /// <summary>
    /// The item storage of a part whose definition has an <c>inventory</c>, or null for other parts. Its
    /// contents follow the part through splits and welds and are saved with the Creation.
    /// </summary>
    public Inventory? InventoryOf(int partId)
    {
        if (_inventories.TryGetValue(partId, out var inventory))
            return inventory;
        if (!_definitions.TryGetValue(partId, out var definition) || Inventory.For(Catalog, definition) is not { } made)
            return null;
        _inventories[partId] = made;
        return made;
    }

    /// <summary>The crafting orders of a part whose definition has <c>crafting</c>, or null for other parts.</summary>
    public CraftingQueue? CraftingOf(int partId)
    {
        if (_crafting.TryGetValue(partId, out var queue))
            return queue;
        if (!_definitions.TryGetValue(partId, out var definition) || definition.Crafting is null)
            return null;
        return _crafting[partId] = new CraftingQueue();
    }

    /// <summary>
    /// What grows on a part whose definition has <c>soil</c>, or null for other parts. Like an inventory,
    /// it follows the part through splits and welds and is saved with the Creation.
    /// </summary>
    public SoilState? SoilOf(int partId)
    {
        if (_soil.TryGetValue(partId, out var soil))
            return soil;
        if (!_definitions.TryGetValue(partId, out var definition) || definition.Soil is null)
            return null;
        return _soil[partId] = new SoilState();
    }

    /// <summary>Soil parts with something planted or some water, by part id.</summary>
    public IEnumerable<(int PartId, SoilState Soil)> ActiveSoil =>
        // Ticked for every Creation every step, and most have no soil.
        _soil.Count == 0 ? [] : _soil.Where(kv => !kv.Value.IsEmpty).OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value));

    /// <summary>True when a part holds items or has crafting orders.</summary>
    public bool HoldsItems(int partId) =>
        _inventories.GetValueOrDefault(partId) is { IsEmpty: false } || _crafting.GetValueOrDefault(partId) is { IsEmpty: false };

    /// <summary>
    /// True when the part may be picked up, or lifted, with what it holds (<see cref="InventoryDefinition.CarryContents"/>):
    /// it has no crafting orders and its definition allows it.
    /// </summary>
    public bool CarriesContents(int partId) =>
        DefinitionOf(partId).Inventory?.CarryContents == true && _crafting.GetValueOrDefault(partId) is not { IsEmpty: false };

    /// <summary>Stations with orders, by part id.</summary>
    public IEnumerable<(int PartId, CraftingQueue Queue)> ActiveCrafting =>
        // Ticked for every Creation every step, and most have no station.
        _crafting.Count == 0 ? [] : _crafting.Where(kv => !kv.Value.IsEmpty).OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value));

    /// <summary>Paints a part (Paint Tool): <paramref name="color"/> is <c>"#rrggbb"</c>, or null for its own colour.</summary>
    /// <returns>True when the colour changed.</returns>
    public bool SetPartColor(int partId, string? color)
    {
        var part = _parts.TryGetValue(partId, out var p) ? p : throw new ArgumentException($"Creation {Id} has no part {partId}.", nameof(partId));
        if (color is not null && !HexColor.TryParse(color, out _))
            throw new ArgumentException($"'{color}' is not a #rrggbb colour.", nameof(color));
        color = color?.ToLowerInvariant();
        if (part.Color == color)
            return false;
        _parts[partId] = part with { Color = color };
        Revision++;
        return true;
    }

    /// <summary>
    /// Null when a link from <paramref name="from"/> to <paramref name="to"/> could be added, or why not: both
    /// parts exist and differ, <paramref name="from"/> has a free output and <paramref name="to"/> a free input
    /// (<see cref="PartDefinition.Logic"/>), and the link is not there yet.
    /// </summary>
    public string? CheckLink(int from, int to)
    {
        if (!_parts.ContainsKey(from))
            return $"creation {Id} has no part {from}.";
        if (!_parts.ContainsKey(to))
            return $"creation {Id} has no part {to}.";
        if (from == to)
            return "a part cannot be linked to itself.";
        if (_links.Contains(new LogicLink(from, to)))
            return $"part {from} already feeds part {to}.";
        var source = _definitions[from];
        var target = _definitions[to];
        var outputs = source.Logic?.Outputs ?? 0;
        var inputs = target.Logic?.Inputs ?? 0;
        if (outputs == 0)
            return $"{source.Name ?? source.Id} has no output to link from.";
        if (inputs == 0)
            return $"{target.Name ?? target.Id} takes no input.";
        if (!target.Logic!.Takes(source.Logic!.KindOrDefault))
            return $"{target.Name ?? target.Id} takes no {source.Logic.KindOrDefault.ToString().ToLowerInvariant()} links.";
        if (_links.Count(l => l.From == from) >= outputs)
            return $"{source.Name ?? source.Id} already has all {outputs} of its output links.";
        if (_links.Count(l => l.To == to) >= inputs)
            return $"{target.Name ?? target.Id} already has all {inputs} of its input links.";
        return null;
    }

    /// <summary>
    /// The link joining <paramref name="a"/> and <paramref name="b"/> that could be added, whichever way round it
    /// goes (the Connect Tool links two parts dragged either way, a Gas Container to a Gas Engine or back): a to b
    /// when <see cref="CheckLink"/> allows it, else b to a, else null.
    /// </summary>
    public LogicLink? LinkBetween(int a, int b) =>
        CheckLink(a, b) is null ? new LogicLink(a, b) : CheckLink(b, a) is null ? new LogicLink(b, a) : null;

    /// <summary>The link already joining <paramref name="a"/> and <paramref name="b"/> either way round, a to b first, or null.</summary>
    public LogicLink? LinkedBetween(int a, int b) =>
        _links.Contains(new LogicLink(a, b)) ? new LogicLink(a, b) : _links.Contains(new LogicLink(b, a)) ? new LogicLink(b, a) : null;

    /// <summary>
    /// What a Connect Tool drag from <paramref name="from"/> to <paramref name="to"/> does: takes away the link from
    /// one to the other if it is there, else adds the one <see cref="LinkBetween"/> finds, else takes away the link
    /// back the other way. Two parts that cannot be linked either way get the link as dragged, which the host refuses.
    /// </summary>
    public (LogicLink Link, bool Remove) DragLink(int from, int to)
    {
        var dragged = new LogicLink(from, to);
        if (_links.Contains(dragged))
            return (dragged, true);
        if (LinkBetween(from, to) is { } link)
            return (link, false);
        return _links.Contains(new LogicLink(to, from)) ? (new LogicLink(to, from), true) : (dragged, false);
    }

    /// <summary>Adds a logic link after <see cref="CheckLink"/>. Bumps <see cref="Revision"/>.</summary>
    public bool TryLink(int from, int to, [NotNullWhen(false)] out string? error)
    {
        error = CheckLink(from, to);
        if (error is not null)
            return false;
        _links.Add(new LogicLink(from, to));
        Revision++;
        return true;
    }

    /// <summary>Removes a logic link. False when there was none.</summary>
    public bool Unlink(int from, int to)
    {
        if (!_links.Remove(new LogicLink(from, to)))
            return false;
        Revision++;
        return true;
    }

    /// <summary>
    /// Replaces a part's changed settings (<see cref="PartInstance.Settings"/>; null means all defaults). The
    /// caller checks them against the definition (<see cref="Logic.PartSettings"/>). Bumps <see cref="Revision"/>.
    /// </summary>
    /// <returns>True when they changed.</returns>
    public bool SetPartSettings(int partId, IReadOnlyDictionary<string, JsonElement>? settings)
    {
        var part = _parts.TryGetValue(partId, out var p) ? p : throw new ArgumentException($"Creation {Id} has no part {partId}.", nameof(partId));
        if (settings is { Count: 0 })
            settings = null;
        if (SameSettings(part.Settings, settings))
            return false;
        _parts[partId] = part with { Settings = settings };
        Revision++;
        return true;
    }

    /// <summary>A part's upgrade level (<see cref="PartInstance.Level"/>).</summary>
    public int LevelOf(int partId) => _parts[partId].Level;

    /// <summary>
    /// Sets a part's upgrade level (UI spec §4): its definition becomes the part's at that level
    /// (<see cref="PartUpgrades.At"/>) and its inventory grows to the new slot count. The caller checks the level and
    /// takes the cost. Bumps <see cref="Revision"/>.
    /// </summary>
    /// <returns>True when the level changed.</returns>
    public bool SetLevel(int partId, int level)
    {
        var part = _parts.TryGetValue(partId, out var p) ? p : throw new ArgumentException($"Creation {Id} has no part {partId}.", nameof(partId));
        var written = Catalog.FindDefinition(part.Part) ?? throw new InvalidOperationException($"'{part.Part}' is no longer in the catalog.");
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, PartUpgrades.MaxLevel(written));
        if (part.Level == level)
            return false;
        _parts[partId] = part with { Level = level };
        var definition = _definitions[partId] = PartUpgrades.At(written, level);
        if (_inventories.TryGetValue(partId, out var inventory) && definition.Inventory?.Slots is { } slots)
            inventory.Grow(slots);
        Revision++;
        return true;
    }

    private static bool SameSettings(IReadOnlyDictionary<string, JsonElement>? a, IReadOnlyDictionary<string, JsonElement>? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var other) && JsonElement.DeepEquals(kv.Value, other));
    }

    /// <summary>
    /// Places a part and attaches it to every touching part whose sockets allow it (see
    /// <see cref="FindAttachments"/>). Fails without changing anything when the definition is unknown or
    /// not placeable, a cell is taken, or the Creation already has parts and the new one attaches to none.
    /// </summary>
    public bool TryPlacePart(string partId, GridCell cell, GridRotation rotation,
        [NotNullWhen(true)] out PartInstance? placed, [NotNullWhen(false)] out string? error)
    {
        placed = null;
        var qualified = Identifiers.Qualify(partId);
        if (!TryResolve(qualified, out var definition, out error))
            return false;
        foreach (var c in PartGeometry.Cells(definition, cell, rotation))
        {
            if (_occupancy.TryGetValue(c, out var other))
            {
                error = $"cell {c} is already taken by part {other}.";
                return false;
            }
        }

        var instance = new PartInstance(_nextPartId, qualified, cell, rotation);
        Insert(instance, definition);
        if (_parts.Count > 1)
        {
            var attachments = FindAttachments(instance.Id);
            if (attachments.Count == 0)
            {
                Uninsert(instance.Id);
                error = $"'{qualified}' at {cell} does not attach to any part of creation {Id}.";
                return false;
            }
            _connections.UnionWith(attachments);
            // A new part joins its neighbours' frame; any attached neighbour will do, they share a grid.
            if (attachments.Select(c => c.A == instance.Id ? c.B : c.A).FirstOrDefault(_poses.ContainsKey) is var n and > 0)
            {
                _poses[instance.Id] = _poses[n];
                if (_velocities.TryGetValue(n, out var velocity))
                    _velocities[instance.Id] = velocity;
            }
        }
        _nextPartId++;
        Changed();
        placed = instance;
        return true;
    }

    /// <summary>
    /// Checks a placement without making it: the same rules as <see cref="TryPlacePart"/>, reporting
    /// the existing parts the new one would attach to (empty when the Creation is empty).
    /// </summary>
    public bool CanPlacePart(string partId, GridCell cell, GridRotation rotation,
        [NotNullWhen(true)] out IReadOnlyList<int>? attachedTo, [NotNullWhen(false)] out string? error)
    {
        attachedTo = null;
        var qualified = Identifiers.Qualify(partId);
        if (!TryResolve(qualified, out var definition, out error))
            return false;
        foreach (var c in PartGeometry.Cells(definition, cell, rotation))
        {
            if (_occupancy.TryGetValue(c, out var other))
            {
                error = $"cell {c} is already taken by part {other}.";
                return false;
            }
        }
        if (_parts.Count == 0)
        {
            attachedTo = [];
            return true;
        }
        // Insert, look and take out again: the id is not consumed and no edge is kept.
        Insert(new PartInstance(_nextPartId, qualified, cell, rotation), definition);
        var neighbours = FindAttachments(_nextPartId).Select(c => c.A == _nextPartId ? c.B : c.A).Distinct().Order().ToList();
        Uninsert(_nextPartId);
        if (neighbours.Count == 0)
        {
            error = $"'{qualified}' at {cell} does not attach to any part of creation {Id}.";
            return false;
        }
        attachedTo = neighbours;
        return true;
    }

    /// <summary>
    /// The edges <paramref name="partId"/> would form with the parts touching it, by face contact:
    /// <list type="bullet">
    /// <item>Blocks, and parts with no sockets, attach structurally on every face, as in Scrap Mechanic (D7).
    /// Any other part attaches only where a socket sits on the touching face.</item>
    /// <item>Structural meets structural gives a rigid edge; a joint socket meets a structural face gives a
    /// joint edge of that socket's type; two joint sockets never connect.</item>
    /// <item>With <paramref name="weld"/>, every face contact is rigid regardless of sockets (Weld Tool, D7).</item>
    /// </list>
    /// Existing edges are included; <paramref name="onlyWith"/> limits which neighbours count.
    /// </summary>
    public IReadOnlyList<Connection> FindAttachments(int partId, bool weld = false, Func<int, bool>? onlyWith = null)
    {
        var part = _parts[partId];
        var definition = _definitions[partId];
        var found = new HashSet<Connection>();
        foreach (var cell in PartGeometry.Cells(definition, part.Cell, part.Rotation))
        {
            foreach (var direction in GridCell.Directions)
            {
                var neighbourCell = cell + direction;
                if (!_occupancy.TryGetValue(neighbourCell, out var otherId) || otherId == partId)
                    continue;
                if (onlyWith is not null && !onlyWith(otherId))
                    continue;
                if (weld)
                {
                    found.Add(Connection.Rigid(partId, otherId));
                    continue;
                }
                var mine = Face(partId, cell, direction);
                var theirs = Face(otherId, neighbourCell, -direction);
                if (Combine(partId, mine, otherId, theirs) is { } connection)
                    found.Add(connection);
            }
        }
        return [.. found.Order(Connection.Order)];
    }

    /// <summary>
    /// Adds an edge after checking it: both parts exist and differ, they touch (a joint edge's socket
    /// face must touch part B), and named sockets exist with a matching type. Adding an existing edge is a no-op.
    /// </summary>
    public bool TryConnect(Connection connection, [NotNullWhen(false)] out string? error)
    {
        if (!CheckConnection(connection, out error))
            return false;
        if (_connections.Add(connection))
            Changed();
        return true;
    }

    /// <summary>
    /// Removes a part and its edges, then splits off every piece that no longer links to the rest
    /// (<see cref="SplitDisconnected"/>). Removing the last part leaves the Creation empty, and the caller
    /// destroys it.
    /// </summary>
    public RemovalResult RemovePart(int partId, Func<int> allocateCreationId)
    {
        var removed = _parts.TryGetValue(partId, out var part)
            ? part
            : throw new ArgumentException($"Creation {Id} has no part {partId}.", nameof(partId));
        Delete(partId);
        Changed();
        return new RemovalResult(removed, SplitDisconnected(allocateCreationId));
    }

    /// <summary>
    /// Splits the graph into connected pieces (counting every edge type). This Creation keeps the piece
    /// with the most parts (ties go to the one with the lowest part id); each other piece moves to a new
    /// Creation with an id from <paramref name="allocateCreationId"/> and the same authority peer. Part
    /// ids are kept. Returns the new Creations, ordered by lowest part id.
    /// </summary>
    public IReadOnlyList<Creation> SplitDisconnected(Func<int> allocateCreationId)
    {
        var pieces = Components(includeJoints: true);
        if (pieces.Count <= 1)
            return [];
        var keep = pieces.MaxBy(p => p.Count)!;
        var split = new List<Creation>();
        foreach (var piece in pieces)
        {
            if (piece == keep)
                continue;
            var creation = new Creation(allocateCreationId(), Catalog, AuthorityPeer) { _nextPartId = _nextPartId };
            var members = piece.ToHashSet();
            foreach (var id in piece)
            {
                creation.Insert(_parts[id], _definitions[id]);
                if (_poses.TryGetValue(id, out var pose))
                    creation._poses[id] = pose;
                if (_damage.TryGetValue(id, out var damage))
                    creation._damage[id] = damage;
                if (_velocities.TryGetValue(id, out var velocity))
                    creation._velocities[id] = velocity;
                if (_inventories.TryGetValue(id, out var inventory))
                    creation._inventories[id] = inventory;
                if (_crafting.TryGetValue(id, out var queue))
                    creation._crafting[id] = queue;
                if (_soil.TryGetValue(id, out var soil))
                    creation._soil[id] = soil;
                if (_fire.TryGetValue(id, out var fire))
                    creation._fire[id] = fire;
                if (_anchored.Contains(id))
                    creation._anchored.Add(id);
            }
            foreach (var connection in _connections.Where(c => members.Contains(c.A)).ToList())
            {
                creation._connections.Add(connection);
                _connections.Remove(connection);
            }
            // A link between two pieces is dropped: links stay inside one Creation.
            foreach (var link in _links.Where(l => members.Contains(l.From)).ToList())
            {
                if (members.Contains(link.To))
                    creation._links.Add(link);
                _links.Remove(link);
            }
            foreach (var id in piece)
                Delete(id);
            split.Add(creation);
        }
        Changed();
        return split;
    }

    /// <summary>
    /// Moves every part of <paramref name="other"/> into this Creation (placing, or welding two Creations, D7).
    /// <paramref name="other"/>'s grid is mapped onto this one by <paramref name="rotation"/> then
    /// <paramref name="offset"/>. Parts get new ids (returned as old → new); <paramref name="other"/>'s
    /// edges are kept, and touching faces between the two attach as <see cref="FindAttachments"/> describes.
    /// Body poses carry over, re-expressed in this grid, so a Body of <paramref name="other"/> stays where
    /// it was in the world; once it joins one of this Creation's Bodies, that Body's pose wins. Fails
    /// without changing anything on overlap or when nothing attaches. <paramref name="other"/> is left
    /// unchanged for the caller to discard.
    /// </summary>
    public bool TryMergeFrom(Creation other, GridRotation rotation, GridCell offset, bool weld,
        [NotNullWhen(true)] out IReadOnlyDictionary<int, int>? idMap, [NotNullWhen(false)] out string? error)
    {
        idMap = null;
        if (!Merge(other, rotation, offset, weld, commit: true, out var map, out _, out error))
            return false;
        idMap = map;
        return true;
    }

    /// <summary>
    /// Checks <see cref="TryMergeFrom"/> without changing either Creation, reporting this Creation's parts
    /// that <paramref name="other"/> would attach to.
    /// </summary>
    public bool CanMergeFrom(Creation other, GridRotation rotation, GridCell offset, bool weld,
        [NotNullWhen(true)] out IReadOnlyList<int>? attachedTo, [NotNullWhen(false)] out string? error)
    {
        attachedTo = null;
        if (!Merge(other, rotation, offset, weld, commit: false, out _, out var touched, out error))
            return false;
        attachedTo = touched;
        return true;
    }

    /// <summary>
    /// Checks <see cref="TryWeldParts"/> without changing anything: both parts exist, sit in different
    /// Bodies, and touch face to face.
    /// </summary>
    public bool CanWeldParts(int a, int b, [NotNullWhen(false)] out string? error)
    {
        error = !_parts.ContainsKey(a) ? $"creation {Id} has no part {a}."
            : !_parts.ContainsKey(b) ? $"creation {Id} has no part {b}."
            : a == b ? $"part {a} cannot be welded to itself."
            : BodyIndexOf(a) == BodyIndexOf(b) ? $"parts {a} and {b} are already one rigid body."
            : !Touches(a, b) ? $"parts {a} and {b} do not touch."
            : null;
        return error is null;
    }

    /// <summary>
    /// Welds two touching parts of different Bodies (Weld Tool, D7): adds a rigid edge between them, so
    /// their Bodies merge into one and any joint between those Bodies locks. The merged Body takes the
    /// pose of its lowest part, as after any merge.
    /// </summary>
    public bool TryWeldParts(int a, int b, [NotNullWhen(false)] out string? error)
    {
        if (!CanWeldParts(a, b, out error))
            return false;
        _connections.Add(Connection.Rigid(a, b));
        Changed();
        return true;
    }

    private bool Merge(Creation other, GridRotation rotation, GridCell offset, bool weld, bool commit,
        out Dictionary<int, int> map, out IReadOnlyList<int> attachedTo, [NotNullWhen(false)] out string? error)
    {
        map = [];
        attachedTo = [];
        if (other == this || other.IsEmpty)
        {
            error = "nothing to merge.";
            return false;
        }
        var moved = other.Parts
            .Select(p => p with { Cell = rotation.Apply(p.Cell) + offset, Rotation = GridRotation.Combine(p.Rotation, rotation) })
            .ToList();
        foreach (var part in moved)
        {
            foreach (var c in PartGeometry.Cells(other._definitions[part.Id], part.Cell, part.Rotation))
            {
                if (_occupancy.TryGetValue(c, out var taken))
                {
                    error = $"part {part.Id} of creation {other.Id} would overlap part {taken} at {c}.";
                    return false;
                }
            }
        }

        var firstId = _nextPartId;
        foreach (var part in moved)
        {
            map[part.Id] = _nextPartId;
            Insert(part with { Id = _nextPartId++ }, other._definitions[part.Id]);
        }
        var added = map.Values.ToHashSet();
        var attachments = added
            .SelectMany(id => FindAttachments(id, weld, n => !added.Contains(n)))
            .ToHashSet();
        if (attachments.Count == 0 || !commit)
        {
            attachedTo = [.. attachments.Select(c => added.Contains(c.A) ? c.B : c.A).Distinct().Order()];
            foreach (var id in added)
                Uninsert(id);
            _nextPartId = firstId;
            error = attachments.Count == 0 ? $"creation {other.Id} does not attach to creation {Id}." : null;
            return error is null;
        }
        // other's grid point p sits at mapping(p) here, so a pose for this grid is otherPose ∘ mapping⁻¹.
        var unmap = Pose.FromGrid(rotation, offset).Inverse();
        foreach (var (oldId, newId) in map)
        {
            if (other._poses.TryGetValue(oldId, out var pose))
                _poses[newId] = pose.Compose(unmap);
            if (other._damage.TryGetValue(oldId, out var damage))
                _damage[newId] = damage;
            // Velocities are in world space, so they carry over unchanged.
            if (other._velocities.TryGetValue(oldId, out var velocity))
                _velocities[newId] = velocity;
            if (other._inventories.TryGetValue(oldId, out var inventory))
                _inventories[newId] = inventory;
            if (other._crafting.TryGetValue(oldId, out var queue))
                _crafting[newId] = queue;
            if (other._soil.TryGetValue(oldId, out var soil))
                _soil[newId] = soil;
            if (other._fire.TryGetValue(oldId, out var fire))
                _fire[newId] = fire;
        }
        foreach (var c in other._connections)
            _connections.Add(c.Type == ConnectionType.Structural
                ? Connection.Rigid(map[c.A], map[c.B])
                : c with { A = map[c.A], B = map[c.B] });
        _connections.UnionWith(attachments);
        foreach (var link in other._links)
            _links.Add(new LogicLink(map[link.From], map[link.To]));
        Changed();
        error = null;
        return true;
    }

    /// <summary>A socket's position (metres) and outward normal in this Creation's grid frame, for joint anchors.</summary>
    public bool TryGetSocketFrame(int partId, string socketId, out Vec3 position, out Vec3 normal)
    {
        position = normal = Vec3.Zero;
        if (!_parts.TryGetValue(partId, out var part)
            || _definitions[partId].Sockets?.FirstOrDefault(s => s.Id == socketId) is not { } socket)
            return false;
        (position, normal) = PartGeometry.SocketFrame(_definitions[partId], socket, part.Cell, part.Rotation);
        return true;
    }

    /// <summary>
    /// Where and how a joint edge moves, in this Creation's grid frame: its anchor on the joint socket, its
    /// axis and the limits and spring from the socket's <see cref="JointDefinition"/>. False for a
    /// structural edge or a socket the part no longer has.
    /// </summary>
    public bool TryGetJointFrame(Connection connection, [NotNullWhen(true)] out JointFrame? frame)
    {
        frame = null;
        if (connection.Type == ConnectionType.Structural || connection.ASocket is null
            || !_parts.TryGetValue(connection.A, out var part)
            || _definitions[connection.A].Sockets?.FirstOrDefault(s => s.Id == connection.ASocket) is not { } socket)
            return false;
        var definition = _definitions[connection.A];
        var (anchor, normal) = PartGeometry.SocketFrame(definition, socket, part.Cell, part.Rotation);
        var joint = socket.Joint;
        frame = new JointFrame(connection.Type, anchor, PartGeometry.JointAxis(socket, part.Rotation), normal,
            connection.Type == ConnectionType.Linear ? joint?.Min ?? 0f : joint?.Min, joint?.Max,
            joint?.Stiffness, joint?.Damping, joint?.ShaftRadius);
        return true;
    }

    /// <summary>Adds a part from a save without attaching it. Callers check overlap first.</summary>
    internal void Load(PartInstance part, PartDefinition definition)
    {
        Insert(part, definition);
        _nextPartId = Math.Max(_nextPartId, part.Id + 1);
        Changed();
    }

    /// <summary>Connected pieces in order of lowest part id, each sorted ascending.</summary>
    internal List<List<int>> Components(bool includeJoints)
    {
        var adjacency = _parts.Keys.ToDictionary(id => id, _ => new List<int>());
        foreach (var c in _connections)
        {
            if (!includeJoints && c.Type != ConnectionType.Structural)
                continue;
            adjacency[c.A].Add(c.B);
            adjacency[c.B].Add(c.A);
        }
        var seen = new HashSet<int>();
        var pieces = new List<List<int>>();
        foreach (var start in _parts.Keys)
        {
            if (!seen.Add(start))
                continue;
            var piece = new List<int>();
            var queue = new Queue<int>([start]);
            while (queue.TryDequeue(out var id))
            {
                piece.Add(id);
                foreach (var next in adjacency[id])
                    if (seen.Add(next))
                        queue.Enqueue(next);
            }
            piece.Sort();
            pieces.Add(piece);
        }
        return pieces;
    }

    private void Changed()
    {
        _layout = null;
        Revision++;
    }

    private Pose? StoredPose(Body body) => First(body, _poses);

    // The value stored for the Body's first part that has one. Read for every force and every body pose, so a plain loop.
    private static T? First<T>(Body body, Dictionary<int, T> byPart)
        where T : struct
    {
        foreach (var id in body.PartIds)
        {
            if (byPart.TryGetValue(id, out var value))
                return value;
        }
        return null;
    }

    private bool TryResolve(string qualified, [NotNullWhen(true)] out PartDefinition? definition, [NotNullWhen(false)] out string? error)
    {
        definition = Catalog.FindDefinition(qualified);
        error = definition switch
        {
            null => $"unknown part '{qualified}'.",
            { IsPlaceable: false } => $"'{qualified}' is a {definition.Category?.ToString().ToLowerInvariant()} and cannot be placed.",
            _ => null,
        };
        return error is null;
    }

    private void Insert(PartInstance part, PartDefinition definition)
    {
        _parts.Add(part.Id, part);
        _definitions.Add(part.Id, definition);
        foreach (var c in PartGeometry.Cells(definition, part.Cell, part.Rotation))
            _occupancy.Add(c, part.Id);
    }

    // Takes out a part Insert just put in, before it got any edges or state: the build ghost and the weld preview
    // do this every frame, and Delete's edge scans are O(edges) per part.
    private void Uninsert(int partId)
    {
        foreach (var c in CellsOf(partId))
            _occupancy.Remove(c);
        _parts.Remove(partId);
        _definitions.Remove(partId);
    }

    private void Delete(int partId)
    {
        foreach (var c in CellsOf(partId))
            _occupancy.Remove(c);
        _parts.Remove(partId);
        _definitions.Remove(partId);
        _poses.Remove(partId);
        _damage.Remove(partId);
        _velocities.Remove(partId);
        _inventories.Remove(partId);
        _crafting.Remove(partId);
        _soil.Remove(partId);
        _fire.Remove(partId);
        _anchored.Remove(partId);
        _connections.RemoveWhere(c => c.Involves(partId));
        _links.RemoveWhere(l => l.From == partId || l.To == partId);
    }

    // How a part offers the face of `cell` that faces `direction`: null type means it does not attach there.
    private (SocketType? Type, string? Socket) Face(int partId, GridCell cell, GridCell direction)
    {
        var part = _parts[partId];
        var definition = _definitions[partId];
        if (definition.Category == PartCategory.Block || definition.Sockets is not { Count: > 0 })
            return (SocketType.Structural, null);
        var socket = PartGeometry.SocketOnFace(definition, part.Cell, part.Rotation, cell, direction);
        return (socket?.Type, socket?.Id);
    }

    private static Connection? Combine(int a, (SocketType? Type, string? Socket) faceA, int b, (SocketType? Type, string? Socket) faceB) =>
        (faceA.Type, faceB.Type) switch
        {
            (null, _) or (_, null) => null,
            (SocketType.Structural, SocketType.Structural) => Connection.Rigid(a, b),
            (var joint, SocketType.Structural) => Connection.Joint(ToConnection(joint.Value), a, faceA.Socket!, b, faceB.Socket),
            (SocketType.Structural, var joint) => Connection.Joint(ToConnection(joint.Value), b, faceB.Socket!, a, faceA.Socket),
            _ => null,
        };

    private static ConnectionType ToConnection(SocketType type) => type switch
    {
        SocketType.Rotational => ConnectionType.Rotational,
        SocketType.Linear => ConnectionType.Linear,
        SocketType.Spring => ConnectionType.Spring,
        _ => ConnectionType.Structural,
    };

    private bool CheckConnection(Connection c, [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (!_parts.ContainsKey(c.A) || !_parts.ContainsKey(c.B))
            error = $"connection {c.A} → {c.B} names a part that is not in creation {Id}.";
        else if (c.A == c.B)
            error = $"part {c.A} cannot connect to itself.";
        else if (c.Type == ConnectionType.Structural)
        {
            if (c.A > c.B || c.ASocket is not null || c.BSocket is not null)
                error = $"structural connection {c.A} → {c.B} must be written with a < b and no sockets.";
            else if (!Touches(c.A, c.B))
                error = $"parts {c.A} and {c.B} do not touch.";
        }
        else if (c.ASocket is null || _definitions[c.A].Sockets?.FirstOrDefault(s => s.Id == c.ASocket) is not { } socket)
            error = $"part {c.A} has no socket '{c.ASocket}'.";
        else if (ToConnection(socket.Type ?? SocketType.Structural) != c.Type)
            error = $"socket '{c.ASocket}' on part {c.A} is {socket.Type?.ToString().ToLowerInvariant()}, not {c.Type.ToString().ToLowerInvariant()}.";
        else if (c.BSocket is not null && _definitions[c.B].Sockets?.Any(s => s.Id == c.BSocket && s.Type == SocketType.Structural) != true)
            error = $"part {c.B} has no structural socket '{c.BSocket}'.";
        else if (!SocketTouches(c.A, socket, c.B))
            error = $"socket '{c.ASocket}' on part {c.A} does not face part {c.B}.";
        return error is null;
    }

    private bool Touches(int a, int b) =>
        CellsOf(a).Any(cell => GridCell.Directions.Any(d => _occupancy.TryGetValue(cell + d, out var n) && n == b));

    private bool SocketTouches(int owner, SocketDefinition socket, int other)
    {
        var part = _parts[owner];
        var definition = _definitions[owner];
        return CellsOf(owner).Any(cell => GridCell.Directions.Any(d =>
            _occupancy.TryGetValue(cell + d, out var n) && n == other
            && PartGeometry.SocketOnFace(definition, part.Cell, part.Rotation, cell, d) == socket));
    }

    private Layout GetLayout() => _layout ??= BuildLayout();

    private Layout BuildLayout()
    {
        var bodies = new List<Body>();
        var bodyOfPart = new Dictionary<int, int>();
        foreach (var piece in Components(includeJoints: false))
        {
            var mass = 0f;
            var weighted = Vec3.Zero;
            var centres = Vec3.Zero;
            foreach (var id in piece)
            {
                var part = _parts[id];
                var centre = PartGeometry.CentreOfMassMetres(_definitions[id], part.Cell, part.Rotation);
                var m = _definitions[id].Mass ?? 0f;
                mass += m;
                weighted += centre * m;
                centres += centre;
                bodyOfPart[id] = bodies.Count;
            }
            var com = mass > 0 ? weighted * (1f / mass) : centres * (1f / piece.Count);
            bodies.Add(new Body(bodies.Count, piece, mass, com));
        }
        var joints = Connections
            .Where(c => c.Type != ConnectionType.Structural)
            .Select(c => new JointLink(c, bodyOfPart[c.A], bodyOfPart[c.B]))
            .ToList();
        var jointOfPart = new Dictionary<int, JointLink>();
        foreach (var joint in joints)
            jointOfPart.TryAdd(joint.Connection.A, joint);
        return new Layout(bodies, joints, bodyOfPart, jointOfPart);
    }

    private sealed record Layout(IReadOnlyList<Body> Bodies, IReadOnlyList<JointLink> Joints, IReadOnlyDictionary<int, int> BodyOfPart,
        IReadOnlyDictionary<int, JointLink> JointOfPart);
}
