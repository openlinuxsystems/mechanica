namespace Mechanica.Core.Items;

/// <summary>
/// Several inventories used as one, in order: a station's linked storage and the crafting player's own inventory
/// (UI spec §4). Taking empties the first inventory before the next; adding fills the first before the next.
/// </summary>
public static class ItemPool
{
    /// <summary>How many of <paramref name="item"/> the pool holds.</summary>
    public static int Count(IEnumerable<Inventory> pool, string item) => pool.Sum(i => i.Count(item));

    /// <summary>True when the pool holds every amount.</summary>
    public static bool HasAll(IReadOnlyList<Inventory> pool, IEnumerable<ItemAmount> amounts) =>
        amounts.All(a => Count(pool, a.Item) >= a.Count);

    /// <summary>Takes up to <paramref name="count"/> of <paramref name="item"/>, first inventory first; returns how many it took.</summary>
    public static int Remove(IEnumerable<Inventory> pool, string item, int count)
    {
        var taken = 0;
        foreach (var inventory in pool)
        {
            if (taken >= count)
                break;
            taken += inventory.Remove(item, count - taken);
        }
        return taken;
    }

    /// <summary>True when every amount fits at once.</summary>
    public static bool CanAddAll(IReadOnlyList<Inventory> pool, IEnumerable<ItemAmount> amounts) =>
        Add(pool.Select(i => i.Clone()).ToList(), amounts);

    /// <summary>Adds every amount, or nothing when they don't all fit. Returns true when added.</summary>
    public static bool TryAddAll(IReadOnlyList<Inventory> pool, IReadOnlyCollection<ItemAmount> amounts) =>
        CanAddAll(pool, amounts) && Add(pool, amounts);

    private static bool Add(IReadOnlyList<Inventory> pool, IEnumerable<ItemAmount> amounts)
    {
        foreach (var amount in amounts)
        {
            var left = amount.Count;
            foreach (var inventory in pool)
            {
                if (left == 0)
                    break;
                left -= inventory.Add(amount.Item, left);
            }
            if (left > 0)
                return false;
        }
        return true;
    }
}
