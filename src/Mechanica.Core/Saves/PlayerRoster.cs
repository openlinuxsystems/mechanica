using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Saves;

/// <summary>
/// The host's record of every player it has seen, by profile id: what a save stores for them and what they
/// get back when they join again. A player who leaves is stored at once, so rejoining in the same session
/// restores them too.
/// </summary>
public sealed class PlayerRoster(IEnumerable<SavedPlayer>? players = null)
{
    private readonly SortedDictionary<string, SavedPlayer> _players =
        new((players ?? []).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.Last()), StringComparer.Ordinal);

    /// <summary>Every stored player, sorted by id.</summary>
    public IReadOnlyCollection<SavedPlayer> All => _players.Values;

    /// <summary>The player stored under <paramref name="profileId"/>, or null.</summary>
    public SavedPlayer? Find(string profileId) => _players.GetValueOrDefault(profileId);

    /// <summary>Stores (or replaces) a player.</summary>
    public void Store(SavedPlayer player) => _players[player.Id] = player;

    /// <summary>The filled slots of <paramref name="inventory"/>.</summary>
    public static IReadOnlyList<SavedSlot> SlotsOf(Inventory inventory) =>
        [.. inventory.Slots.Select((s, i) => (s, i)).Where(x => x.s is not null).Select(x => new SavedSlot(x.i, x.s!.Item, x.s.Count))];

    /// <summary>
    /// Empties <paramref name="inventory"/> and fills it from <paramref name="slots"/>. Slots that no longer fit
    /// (an item whose mod is gone, a slot past the end, too many for a stack) are skipped and described in
    /// the returned list.
    /// </summary>
    public static IReadOnlyList<string> Restore(Inventory inventory, IEnumerable<SavedSlot> slots)
    {
        var problems = new List<string>();
        inventory.Clear();
        foreach (var slot in slots)
        {
            var stack = new ItemStack(Identifiers.Qualify(slot.Item), slot.Count);
            if (slot.Slot < 0 || slot.Slot >= inventory.SlotCount)
                problems.Add($"slot {slot.Slot} is past the inventory's {inventory.SlotCount} slots");
            else if (inventory.CheckStack(stack) is { } error)
                problems.Add($"slot {slot.Slot}: {error}");
            else
                inventory.SetSlot(slot.Slot, stack);
        }
        return problems;
    }
}
