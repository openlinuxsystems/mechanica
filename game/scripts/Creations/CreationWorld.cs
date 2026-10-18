using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.World;

namespace Mechanica.Game.Creations;

/// <summary>
/// Every Creation in the world, live or parked. A live Creation has a <see cref="CreationNode"/> in the
/// physics world; a parked one (chunk 12, <see cref="ChunkStreamer"/>) has only its Core graph, with its
/// Body poses written back and its Body velocities kept here, so it resumes exactly as it stopped.
/// The host changes the graphs through the Action API; clients mirror the Creations near them
/// (<see cref="Net.CreationSync"/>).
/// </summary>
public partial class CreationWorld : Node3D
{
    private readonly SortedDictionary<int, Entry> _entries = [];

    private sealed class Entry(Creation creation, PartAssets assets)
    {
        public Creation Creation { get; } = creation;
        public PartAssets Assets { get; } = assets;
        public CreationNode? Node { get; set; }
        public List<(Vector3 Linear, Vector3 Angular)> Velocities { get; } = [];
    }

    /// <summary>Raised after a Creation is removed, with its id.</summary>
    public event Action<int>? Removed;

    /// <summary>Every Creation, by ascending id.</summary>
    public IEnumerable<Creation> All => _entries.Values.Select(e => e.Creation);

    /// <summary>The node of every live Creation, by ascending id.</summary>
    public IEnumerable<CreationNode> Nodes => _entries.Values.Select(e => e.Node).OfType<CreationNode>();

    /// <summary>How many Creations there are, live or parked.</summary>
    public int Count => _entries.Count;

    /// <summary>The Creation with this id, or null.</summary>
    public Creation? Find(int id) => _entries.GetValueOrDefault(id)?.Creation;

    /// <summary>The live node of a Creation, or null when it is parked or unknown.</summary>
    public CreationNode? NodeOf(int id) => _entries.GetValueOrDefault(id)?.Node;

    /// <summary>True when the Creation exists and is parked.</summary>
    public bool IsParked(int id) => _entries.TryGetValue(id, out var e) && e.Node is null;

    /// <summary>Adds a Creation and spawns it live at its stored Body poses.</summary>
    public CreationNode Add(Creation creation, PartAssets assets)
    {
        if (_entries.ContainsKey(creation.Id))
            throw new ArgumentException($"Creation {creation.Id} is already in the world.", nameof(creation));
        var entry = _entries[creation.Id] = new Entry(creation, assets);
        return Spawn(entry);
    }

    /// <summary>Adds a Creation, or swaps out the one with the same id (a client mirroring the host).</summary>
    public CreationNode Replace(Creation creation, PartAssets assets)
    {
        Remove(creation.Id);
        return Add(creation, assets);
    }

    /// <summary>
    /// Shows a graph change: a live Creation rebuilds its bodies in place, a parked one spawns from the
    /// new graph when it resumes.
    /// </summary>
    public void Rebuild(int id)
    {
        if (_entries.GetValueOrDefault(id) is not { } entry)
            return;
        if (entry.Node is { } node)
            node.Rebuild();
        else
            entry.Velocities.Clear();
    }

    /// <summary>
    /// Shows a Creation moved without a graph change: a live one jumps to its stored poses at rest, a
    /// parked one forgets its velocities.
    /// </summary>
    public void Moved(int id)
    {
        if (_entries.GetValueOrDefault(id) is not { } entry)
            return;
        if (entry.Node is { } node)
            node.Teleport();
        else
            entry.Velocities.Clear();
    }

    /// <summary>Removes every Creation.</summary>
    public void Clear()
    {
        foreach (var id in _entries.Keys.ToList())
            Remove(id);
    }

    /// <summary>Removes a Creation, live or parked.</summary>
    public void Remove(int id)
    {
        if (!_entries.Remove(id, out var entry))
            return;
        Despawn(entry);
        Removed?.Invoke(id);
    }

    /// <summary>Takes a live Creation out of the physics world, keeping its poses and velocities.</summary>
    public void Park(int id)
    {
        if (_entries.GetValueOrDefault(id) is not { Node: { } node } entry)
            return;
        node.SyncToCreation();
        entry.Velocities.Clear();
        entry.Velocities.AddRange(node.Bodies.Select(b => (b.LinearVelocity, b.AngularVelocity)));
        Despawn(entry);
    }

    /// <summary>Puts a parked Creation back where it was parked, moving as it was.</summary>
    public void Resume(int id)
    {
        if (_entries.GetValueOrDefault(id) is not { Node: null } entry)
            return;
        var node = Spawn(entry);
        if (entry.Velocities.Count == node.Bodies.Count)
        {
            for (var i = 0; i < node.Bodies.Count; i++)
                (node.Bodies[i].LinearVelocity, node.Bodies[i].AngularVelocity) = entry.Velocities[i];
        }
        entry.Velocities.Clear();
    }

    /// <summary>Puts every Body of a live, simulated Creation to sleep.</summary>
    public void Sleep(int id)
    {
        if (NodeOf(id) is { IsSimulated: true } node)
        {
            foreach (var body in node.Bodies)
                body.Sleeping = true;
        }
    }

    /// <summary>
    /// What the streamer needs about a Creation now: its world box from the live bodies and their fastest
    /// motion. A parked Creation reports an empty box and no motion; the streamer keeps its parked box.
    /// </summary>
    public CreationSample Sample(int id)
    {
        if (NodeOf(id) is not { Bodies.Count: > 0 } node)
            return new CreationSample(id, WorldBounds.Empty, 0f, 0f);
        var poses = node.Bodies.Select(b => b.GlobalTransform.ToPose()).ToList();
        var linear = node.Bodies.Max(b => b.LinearVelocity.Length());
        var angular = node.Bodies.Max(b => b.AngularVelocity.Length());
        return new CreationSample(id, CreationBounds.World(node.LocalBounds, poses), linear, angular);
    }

    private CreationNode Spawn(Entry entry)
    {
        var node = new CreationNode { Creation = entry.Creation, Assets = entry.Assets };
        AddChild(node);
        return entry.Node = node;
    }

    private void Despawn(Entry entry)
    {
        if (entry.Node is not { } node)
            return;
        RemoveChild(node);
        node.QueueFree();
        entry.Node = null;
    }
}
