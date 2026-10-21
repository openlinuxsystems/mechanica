using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.World;

/// <summary>
/// What a POI <c>part</c> marker's container is filled with (<see cref="PoiMarker.Loot"/>): <see cref="Min"/> to
/// <see cref="Max"/> different entries of <see cref="Table"/>, and again <see cref="RefillSeconds"/> after it is emptied.
/// </summary>
/// <param name="TableId">The loot table's id in <see cref="WorldGenDefinition.LootTables"/>.</param>
/// <param name="Table">The loot table.</param>
/// <param name="Min">Fewest draws (different entries) per fill.</param>
/// <param name="Max">Most draws per fill.</param>
/// <param name="RefillSeconds">World time (s) it must stay empty before it fills again, or null for once only.</param>
/// <param name="Seed">This marker's roll seed, from the world seed and the marker's key: every fill is the same in every run.</param>
public sealed record WorldPartLoot(string TableId, LootTableDefinition Table, int Min, int Max, double? RefillSeconds, uint Seed);

/// <summary>
/// The fill state of a world-owned container with loot (<see cref="WorldPartLoot"/>), saved with its Creation
/// (<c>"world_loot": { "fills": 1, "emptied_at": 5310.5 }</c>).
/// </summary>
public sealed class WorldLootState
{
    /// <summary>How many times it has been filled: 1 after the first fill. Each fill rolls from its own number.</summary>
    public int Fills { get; set; }

    /// <summary>World time (the scripts' clock) it was found empty, waiting to refill; null while it holds something.</summary>
    public double? EmptiedAt { get; set; }
}

/// <summary>
/// Loot in the world's containers (Scrap Mechanic's lockers, file cabinets and ruin chests): the host fills a world-owned
/// container (<see cref="WorldParts"/>) once, when it first places it, from its marker's loot table, with draws picked
/// from the world seed and the marker, so the same world always holds the same finds. A container a player has looted is
/// never filled again, except a refilling one (<see cref="WorldPartLoot.RefillSeconds"/>): once it has stood empty that
/// long it fills again, with a new roll. Saved with the Creation (<see cref="Creation.WorldLoot"/>). Pure logic; the host
/// calls <see cref="Refill"/> now and then.
/// </summary>
public static class WorldLoot
{
    // Salt of the loot stream, so container rolls are not the same as other per-seed rolls.
    private const uint Stream = 0x4C4F4F54u; // "LOOT"

    /// <summary>A marker's draws per fill as <c>(min, max)</c>: its <c>loot_rolls</c>, or one.</summary>
    public static (int Min, int Max) RollsOf(PoiMarker marker) =>
        marker.LootRolls is [var min, var max] && min >= 1 && max >= min ? (min, max) : (1, 1);

    /// <summary>The roll seed of the marker <paramref name="key"/> (<see cref="WorldParts.KeyOf"/>) in the world <paramref name="worldSeed"/>.</summary>
    public static uint SeedOf(long worldSeed, string key) => Noise.Mix(Noise.Salt(worldSeed, Stream) ^ Noise.Hash(key));

    /// <summary>
    /// The loot <paramref name="marker"/> fills its container with in the world <paramref name="world"/>, or null when it
    /// has none or names a table the world does not have.
    /// </summary>
    public static WorldPartLoot? Of(PoiMarker marker, string key, WorldGenDefinition world, long worldSeed)
    {
        if (marker.Loot is not { } id || world.LootTables?.GetValueOrDefault(id) is not { } table)
            return null;
        var (min, max) = RollsOf(marker);
        double? refill = marker.RefillDays is { } days && days > 0f && float.IsFinite(days) ? (double)days * world.DayLength : null;
        return new WorldPartLoot(id, table, min, max, refill, SeedOf(worldSeed, key));
    }

    /// <summary>
    /// The items fill number <paramref name="fill"/> (1 for the first) of <paramref name="loot"/> holds: <c>Min</c> to
    /// <c>Max</c> different entries, odds by weight, each with a count from its range (<see cref="LootRolls.Roll"/>), ids
    /// qualified. The same loot and fill always give the same items.
    /// </summary>
    public static List<ItemStack> Roll(WorldPartLoot loot, int fill)
    {
        var rng = new Random((int)Noise.Mix(loot.Seed ^ ((uint)fill * 0x9E3779B1u)));
        var roll = new LootRoll { Table = loot.TableId, Picks = loot.Min, PicksMax = loot.Max };
        return [.. LootRolls.Roll(roll, loot.Table, rng).Select(r => new ItemStack(Identifiers.Qualify(r.Item), r.Count))];
    }

    /// <summary>
    /// Fills <paramref name="inventory"/> with the next fill of <paramref name="loot"/> (counting it in
    /// <paramref name="state"/>). Items the loaded mods don't have, or the inventory won't take, are left out. Returns what
    /// went in.
    /// </summary>
    public static List<ItemStack> Fill(Inventory inventory, WorldPartLoot loot, WorldLootState state)
    {
        state.Fills++;
        state.EmptiedAt = null;
        var added = new List<ItemStack>();
        foreach (var stack in Roll(loot, state.Fills))
        {
            if (inventory.Catalog.FindDefinition(stack.Item) is null)
                continue;
            var count = inventory.Add(stack.Item, stack.Count);
            if (count > 0)
                added.Add(stack with { Count = count });
        }
        return added;
    }

    /// <summary>
    /// The host's check on a world-owned container with loot that refills (<paramref name="spot"/>'s loot has
    /// <see cref="WorldPartLoot.RefillSeconds"/>) at world time <paramref name="now"/>: it notes when it is found empty, forgets
    /// it when something is put back, and fills it again once it has stood empty for the refill time. Returns true when it
    /// filled it. Containers without refill, players' Creations and parts with no inventory are left alone.
    /// </summary>
    public static bool Refill(Creation creation, WorldPartSpot spot, double now)
    {
        if (spot.Loot is not { RefillSeconds: { } wait } loot || creation.WorldPart != spot.Key || creation.Parts.Count != 1
            || creation.InventoryOf(creation.Parts.First().Id) is not { } inventory)
            return false;
        var state = creation.WorldLoot ??= new WorldLootState();
        if (!inventory.IsEmpty)
        {
            state.EmptiedAt = null;
            return false;
        }
        if (state.EmptiedAt is not { } emptied)
        {
            state.EmptiedAt = now;
            return false;
        }
        if (now < emptied + wait)
            return false;
        Fill(inventory, loot, state);
        return true;
    }
}
