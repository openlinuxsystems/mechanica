using Godot;
using Mechanica.Core.Creations;

namespace Mechanica.Game.Creations;

/// <summary>
/// Every Creation in the world, each with its <see cref="CreationNode"/> as a child. The host changes
/// the graphs (through the Action API) and the clients mirror them (<see cref="Building.BuildSync"/>).
/// </summary>
public partial class CreationWorld : Node3D
{
    private readonly SortedDictionary<int, (Creation Creation, CreationNode Node)> _entries = [];

    /// <summary>Raised after a Creation is removed, with its id.</summary>
    public event Action<int>? Removed;

    /// <summary>Every Creation, by ascending id.</summary>
    public IEnumerable<Creation> All => _entries.Values.Select(e => e.Creation);

    /// <summary>How many Creations there are.</summary>
    public int Count => _entries.Count;

    /// <summary>The Creation with this id, or null.</summary>
    public Creation? Find(int id) => _entries.TryGetValue(id, out var e) ? e.Creation : null;

    /// <summary>The node of a Creation, or null.</summary>
    public CreationNode? NodeOf(int id) => _entries.TryGetValue(id, out var e) ? e.Node : null;

    /// <summary>Adds a Creation and spawns it at its stored Body poses.</summary>
    public CreationNode Add(Creation creation, PartAssets assets)
    {
        if (_entries.ContainsKey(creation.Id))
            throw new ArgumentException($"Creation {creation.Id} is already in the world.", nameof(creation));
        var node = new CreationNode { Creation = creation, Assets = assets };
        _entries[creation.Id] = (creation, node);
        AddChild(node);
        return node;
    }

    /// <summary>Adds a Creation, or swaps out the one with the same id (a client mirroring the host).</summary>
    public CreationNode Replace(Creation creation, PartAssets assets)
    {
        if (_entries.Remove(creation.Id, out var old))
            Despawn(old.Node);
        return Add(creation, assets);
    }

    /// <summary>Removes a Creation.</summary>
    public void Remove(int id)
    {
        if (!_entries.Remove(id, out var entry))
            return;
        Despawn(entry.Node);
        Removed?.Invoke(id);
    }

    /// <summary>Removes every Creation.</summary>
    public void Clear()
    {
        foreach (var id in _entries.Keys.ToList())
            Remove(id);
    }

    private void Despawn(CreationNode node)
    {
        RemoveChild(node);
        node.QueueFree();
    }
}
