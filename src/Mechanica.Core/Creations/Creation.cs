using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Mechanica.Core.Data;

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
    private readonly HashSet<Connection> _connections = [];
    // World pose of each part's Body, stored on every member so poses survive splits and removals.
    private readonly Dictionary<int, Pose> _poses = [];
    private Layout? _layout;
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

    /// <summary>Parts by ascending id.</summary>
    public IReadOnlyCollection<PartInstance> Parts => _parts.Values;

    /// <summary>Edges in a stable order (by A, B, type, sockets).</summary>
    public IReadOnlyList<Connection> Connections => [.. _connections.Order(Connection.Order)];

    /// <summary>True when no parts are left.</summary>
    public bool IsEmpty => _parts.Count == 0;

    /// <summary>Rigid bodies, ordered by lowest part id.</summary>
    public IReadOnlyList<Body> Bodies => GetLayout().Bodies;

    /// <summary>Joint edges resolved to bodies, in <see cref="Connections"/> order.</summary>
    public IReadOnlyList<JointLink> Joints => GetLayout().Joints;

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
                Delete(instance.Id);
                error = $"'{qualified}' at {cell} does not attach to any part of creation {Id}.";
                return false;
            }
            _connections.UnionWith(attachments);
            // A new part joins its neighbours' frame; any attached neighbour will do, they share a grid.
            if (attachments.Select(c => c.A == instance.Id ? c.B : c.A).FirstOrDefault(_poses.ContainsKey) is var n and > 0)
                _poses[instance.Id] = _poses[n];
        }
        _nextPartId++;
        _layout = null;
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
        Delete(_nextPartId);
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
            _layout = null;
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
        _layout = null;
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
            }
            foreach (var connection in _connections.Where(c => members.Contains(c.A)).ToList())
            {
                creation._connections.Add(connection);
                _connections.Remove(connection);
            }
            foreach (var id in piece)
                Delete(id);
            split.Add(creation);
        }
        _layout = null;
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

        var map = new Dictionary<int, int>();
        foreach (var part in moved)
        {
            map[part.Id] = _nextPartId;
            Insert(part with { Id = _nextPartId++ }, other._definitions[part.Id]);
        }
        var added = map.Values.ToHashSet();
        var attachments = added
            .SelectMany(id => FindAttachments(id, weld, n => !added.Contains(n)))
            .ToHashSet();
        if (attachments.Count == 0)
        {
            foreach (var id in added)
                Delete(id);
            _nextPartId -= added.Count;
            error = $"creation {other.Id} does not attach to creation {Id}.";
            return false;
        }
        // other's grid point p sits at mapping(p) here, so a pose for this grid is otherPose ∘ mapping⁻¹.
        var unmap = Pose.FromGrid(rotation, offset).Inverse();
        foreach (var (oldId, newId) in map)
        {
            if (other._poses.TryGetValue(oldId, out var pose))
                _poses[newId] = pose.Compose(unmap);
        }
        foreach (var c in other._connections)
            _connections.Add(c.Type == ConnectionType.Structural
                ? Connection.Rigid(map[c.A], map[c.B])
                : c with { A = map[c.A], B = map[c.B] });
        _connections.UnionWith(attachments);
        _layout = null;
        idMap = map;
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

    /// <summary>Adds a part from a save without attaching it. Callers check overlap first.</summary>
    internal void Load(PartInstance part, PartDefinition definition)
    {
        Insert(part, definition);
        _nextPartId = Math.Max(_nextPartId, part.Id + 1);
        _layout = null;
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

    private Pose? StoredPose(Body body) =>
        body.PartIds.FirstOrDefault(_poses.ContainsKey) is var id and > 0 ? _poses[id] : null;

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

    private void Delete(int partId)
    {
        foreach (var c in CellsOf(partId))
            _occupancy.Remove(c);
        _parts.Remove(partId);
        _definitions.Remove(partId);
        _poses.Remove(partId);
        _connections.RemoveWhere(c => c.Involves(partId));
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
                var centre = PartGeometry.CentreMetres(_definitions[id], part.Cell, part.Rotation);
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
        return new Layout(bodies, joints, bodyOfPart);
    }

    private sealed record Layout(IReadOnlyList<Body> Bodies, IReadOnlyList<JointLink> Joints, IReadOnlyDictionary<int, int> BodyOfPart);
}
