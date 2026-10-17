using Mechanica.Core.Data;

namespace Mechanica.Core.Items;

/// <summary>Some of one item in one slot.</summary>
/// <param name="Item">Qualified item id, <c>mod_id:item_id</c>.</param>
/// <param name="Count">How many, 1 to the item's stack size.</param>
public sealed record ItemStack(string Item, int Count);

/// <summary>An item id and a count, as recipes and gifts name them.</summary>
/// <param name="Item">Qualified item id.</param>
/// <param name="Count">How many (1 or more).</param>
public readonly record struct ItemAmount(string Item, int Count);

/// <summary>
/// A fixed number of item slots: a player's pockets, a chest, a station's output. Each slot holds one
/// <see cref="ItemStack"/> of at most the item's <see cref="PartDefinition.StackSize"/>. An inventory may
/// only accept some items (a gas container takes gasoline). Adding fills matching stacks first, then
/// empty slots, in slot order. <see cref="Revision"/> goes up on every change so sync can tell when to
/// send it again. Not thread-safe; the host owns every inventory.
/// </summary>
public sealed class Inventory
{
    private readonly ItemStack?[] _slots;
    private readonly HashSet<string>? _accepts;

    /// <summary>Creates an empty inventory.</summary>
    /// <param name="catalog">Item definitions, for stack sizes.</param>
    /// <param name="slots">Slot count (1 or more).</param>
    /// <param name="accepts">Item ids it takes (bare ids mean vanilla); null takes anything.</param>
    public Inventory(IPartCatalog catalog, int slots, IEnumerable<string>? accepts = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slots, 1);
        Catalog = catalog;
        _slots = new ItemStack?[slots];
        _accepts = accepts?.Select(a => Identifiers.Qualify(a)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Creates the inventory a part's definition describes, or null when it has none.</summary>
    public static Inventory? For(IPartCatalog catalog, PartDefinition definition) =>
        definition.Inventory is { Slots: { } slots } inventory ? new Inventory(catalog, slots, inventory.Accepts) : null;

    /// <summary>Where stack sizes come from.</summary>
    public IPartCatalog Catalog { get; }

    /// <summary>Number of slots.</summary>
    public int SlotCount => _slots.Length;

    /// <summary>Item ids it takes, or null for any item.</summary>
    public IReadOnlySet<string>? AcceptedItems => _accepts;

    /// <summary>Goes up by one on every change.</summary>
    public int Revision { get; private set; }

    /// <summary>The stack in a slot, or null when it is empty.</summary>
    public ItemStack? this[int slot] => _slots[slot];

    /// <summary>Every slot in order, empty ones as null.</summary>
    public IReadOnlyList<ItemStack?> Slots => _slots;

    /// <summary>True when every slot is empty.</summary>
    public bool IsEmpty => _slots.All(s => s is null);

    /// <summary>The most of <paramref name="item"/> one slot holds (its definition's stack size; unknown items 1).</summary>
    public int StackSizeOf(string item) => Math.Max(1, Catalog.FindDefinition(item)?.StackSize ?? 1);

    /// <summary>True when the slots take <paramref name="item"/> and the item exists.</summary>
    public bool Accepts(string item) =>
        Catalog.FindDefinition(item) is not null && (_accepts is null || _accepts.Contains(Identifiers.Qualify(item)));

    /// <summary>How many of <paramref name="item"/> the inventory holds.</summary>
    public int Count(string item)
    {
        item = Identifiers.Qualify(item);
        return _slots.Where(s => s?.Item == item).Sum(s => s!.Count);
    }

    /// <summary>How many more of <paramref name="item"/> would fit.</summary>
    public int SpaceFor(string item)
    {
        if (!Accepts(item))
            return 0;
        item = Identifiers.Qualify(item);
        var max = StackSizeOf(item);
        return _slots.Sum(s => s is null ? max : s.Item == item ? Math.Max(0, max - s.Count) : 0);
    }

    /// <summary>True when every amount fits at once.</summary>
    public bool CanAddAll(IEnumerable<ItemAmount> amounts)
    {
        var copy = Clone();
        return amounts.All(a => copy.Add(a.Item, a.Count) == a.Count);
    }

    /// <summary>Adds as many of <paramref name="item"/> as fit, up to <paramref name="count"/>, and returns how many it added.</summary>
    public int Add(string item, int count)
    {
        if (count <= 0 || !Accepts(item))
            return 0;
        item = Identifiers.Qualify(item);
        var max = StackSizeOf(item);
        var left = count;
        for (var i = 0; i < _slots.Length && left > 0; i++)
        {
            if (_slots[i] is { } s && s.Item == item && s.Count < max)
            {
                var n = Math.Min(left, max - s.Count);
                _slots[i] = s with { Count = s.Count + n };
                left -= n;
            }
        }
        for (var i = 0; i < _slots.Length && left > 0; i++)
        {
            if (_slots[i] is null)
            {
                var n = Math.Min(left, max);
                _slots[i] = new ItemStack(item, n);
                left -= n;
            }
        }
        if (left != count)
            Revision++;
        return count - left;
    }

    /// <summary>Adds every amount, or nothing when they don't all fit. Returns true when added.</summary>
    public bool TryAddAll(IReadOnlyCollection<ItemAmount> amounts)
    {
        if (!CanAddAll(amounts))
            return false;
        foreach (var a in amounts)
            Add(a.Item, a.Count);
        return true;
    }

    /// <summary>Takes up to <paramref name="count"/> of <paramref name="item"/>, last slots first, and returns how many it took.</summary>
    public int Remove(string item, int count)
    {
        item = Identifiers.Qualify(item);
        var left = count;
        for (var i = _slots.Length - 1; i >= 0 && left > 0; i--)
        {
            if (_slots[i] is { } s && s.Item == item)
            {
                var n = Math.Min(left, s.Count);
                _slots[i] = n == s.Count ? null : s with { Count = s.Count - n };
                left -= n;
            }
        }
        if (left != count)
            Revision++;
        return count - Math.Max(left, 0);
    }

    /// <summary>True when the inventory holds every amount (amounts of one item add up).</summary>
    public bool HasAll(IEnumerable<ItemAmount> amounts) =>
        amounts.GroupBy(a => Identifiers.Qualify(a.Item)).All(g => Count(g.Key) >= g.Sum(a => a.Count));

    /// <summary>
    /// Puts a stack straight into a slot (loading saves, sync views). Throws when the slot is out of range,
    /// the item is not accepted or the count is outside 1 to its stack size.
    /// </summary>
    public void SetSlot(int slot, ItemStack? stack)
    {
        if (slot < 0 || slot >= _slots.Length)
            throw new ArgumentOutOfRangeException(nameof(slot), $"slot must be 0 to {_slots.Length - 1}.");
        if (CheckStack(stack) is { } error)
            throw new ArgumentException(error, nameof(stack));
        stack = stack is null ? null : stack with { Item = Identifiers.Qualify(stack.Item) };
        if (_slots[slot] == stack)
            return;
        _slots[slot] = stack;
        Revision++;
    }

    /// <summary>Null when <paramref name="stack"/> could sit in a slot here, or why not.</summary>
    public string? CheckStack(ItemStack? stack)
    {
        if (stack is null)
            return null;
        if (Catalog.FindDefinition(stack.Item) is null)
            return $"unknown item '{stack.Item}'.";
        if (!Accepts(stack.Item))
            return $"this inventory does not take '{stack.Item}'.";
        var max = StackSizeOf(stack.Item);
        return stack.Count < 1 || stack.Count > max ? $"count must be 1 to {max} for '{stack.Item}' (got {stack.Count})." : null;
    }

    /// <summary>Empties every slot.</summary>
    public void Clear()
    {
        if (IsEmpty)
            return;
        Array.Fill(_slots, null);
        Revision++;
    }

    /// <summary>A copy with the same slots (its revision starts at 0).</summary>
    public Inventory Clone()
    {
        var copy = new Inventory(Catalog, _slots.Length, _accepts);
        _slots.CopyTo(copy._slots, 0);
        return copy;
    }

    /// <summary>
    /// Null when <see cref="Move"/> would succeed, or why not. Moving <paramref name="count"/> (null: the whole
    /// stack) from <paramref name="fromSlot"/> to <paramref name="toSlot"/> merges into a stack of the same
    /// item, fills an empty slot, or swaps two different whole stacks. Without <paramref name="toSlot"/> the
    /// items go wherever they fit (as many as fit, at least one).
    /// </summary>
    public static string? CheckMove(Inventory from, int fromSlot, Inventory to, int? toSlot, int? count) =>
        Plan(from, fromSlot, to, toSlot, count, out _);

    /// <summary>Moves items as <see cref="CheckMove"/> describes and returns how many moved.</summary>
    /// <exception cref="InvalidOperationException">The move is not possible.</exception>
    public static int Move(Inventory from, int fromSlot, Inventory to, int? toSlot, int? count)
    {
        if (Plan(from, fromSlot, to, toSlot, count, out var n) is { } error)
            throw new InvalidOperationException(error);
        var stack = from._slots[fromSlot]!;
        if (toSlot is not { } target)
        {
            from.Take(fromSlot, n);
            to.Add(stack.Item, n);
            return n;
        }
        var other = to._slots[target];
        if (other is not null && other.Item != stack.Item)
        {
            // Swap two whole stacks.
            from._slots[fromSlot] = other;
            to._slots[target] = stack;
            from.Revision++;
            if (to != from)
                to.Revision++;
            return n;
        }
        from.Take(fromSlot, n);
        to._slots[target] = new ItemStack(stack.Item, (to._slots[target]?.Count ?? 0) + n);
        to.Revision++;
        return n;
    }

    private void Take(int slot, int n)
    {
        var s = _slots[slot]!;
        _slots[slot] = n == s.Count ? null : s with { Count = s.Count - n };
        Revision++;
    }

    private static string? Plan(Inventory from, int fromSlot, Inventory to, int? toSlot, int? count, out int moved)
    {
        moved = 0;
        if (fromSlot < 0 || fromSlot >= from.SlotCount)
            return $"from_slot must be 0 to {from.SlotCount - 1} (got {fromSlot}).";
        if (toSlot is { } t && (t < 0 || t >= to.SlotCount))
            return $"to_slot must be 0 to {to.SlotCount - 1} (got {t}).";
        if (from._slots[fromSlot] is not { } stack)
            return $"Slot {fromSlot} is empty.";
        var n = count ?? stack.Count;
        if (n < 1 || n > stack.Count)
            return $"count must be 1 to {stack.Count} (got {n}).";
        if (from == to && toSlot == fromSlot)
            return "An item cannot move onto its own slot.";
        if (!to.Accepts(stack.Item))
            return $"That inventory does not take {Name(to, stack.Item)}.";
        var max = to.StackSizeOf(stack.Item);
        if (toSlot is not { } target)
        {
            // Measure the space as if the items had already left (moving within one inventory).
            var space = from == to ? Without(from, fromSlot, n).SpaceFor(stack.Item) : to.SpaceFor(stack.Item);
            moved = Math.Min(n, space);
            return moved > 0 ? null : $"There is no room for {Name(to, stack.Item)}.";
        }
        var other = to._slots[target];
        if (other is null || other.Item == stack.Item)
        {
            var room = max - (other?.Count ?? 0);
            if (room < n)
                return room <= 0 ? $"Slot {target} is full." : $"Only {room} more fit in slot {target}.";
            moved = n;
            return null;
        }
        if (n != stack.Count)
            return $"Slot {target} holds something else; move the whole stack to swap.";
        if (!from.Accepts(other.Item))
            return $"Swapping would put {Name(from, other.Item)} where it is not allowed.";
        moved = n;
        return null;
    }

    private static Inventory Without(Inventory inventory, int slot, int n)
    {
        var copy = inventory.Clone();
        copy.Take(slot, n);
        return copy;
    }

    private static string Name(Inventory inventory, string item) => inventory.Catalog.FindDefinition(item)?.Name ?? item;
}
