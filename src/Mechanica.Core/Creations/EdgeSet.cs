namespace Mechanica.Core.Creations;

/// <summary>
/// A Creation's edges (connections or logic links): a set that keeps its sorted view until it changes, so the
/// lists scripts, sync and the HUD read every tick are not sorted and copied on every read.
/// </summary>
internal sealed class EdgeSet<T>(IComparer<T> order) : IEnumerable<T>
    where T : notnull
{
    private readonly HashSet<T> _items = [];
    private T[]? _sorted;

    /// <summary>Counts changes, so views built from the set know when to rebuild.</summary>
    public int Version { get; private set; }

    public int Count => _items.Count;

    /// <summary>The edges in order, rebuilt only after a change.</summary>
    public IReadOnlyList<T> Sorted => _sorted ??= [.. _items.Order(order)];

    public bool Contains(T item) => _items.Contains(item);

    public bool Add(T item) => _items.Add(item) && Touch();

    public bool Remove(T item) => _items.Remove(item) && Touch();

    public void UnionWith(IEnumerable<T> items)
    {
        var count = _items.Count;
        _items.UnionWith(items);
        if (_items.Count != count)
            Touch();
    }

    public int RemoveWhere(Predicate<T> match)
    {
        var removed = _items.RemoveWhere(match);
        if (removed > 0)
            Touch();
        return removed;
    }

    public HashSet<T>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private bool Touch()
    {
        _sorted = null;
        Version++;
        return true;
    }
}
