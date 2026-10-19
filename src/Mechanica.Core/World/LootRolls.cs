namespace Mechanica.Core.World;

/// <summary>Rolls a breaking object's <see cref="WorldObjectDefinition.Loot"/> from its loot table.</summary>
public static class LootRolls
{
    /// <summary>
    /// Picks <see cref="LootRoll.Picks"/> to <see cref="LootRoll.PicksMax"/> different entries of
    /// <paramref name="table"/>, each with odds in proportion to its weight among those not yet picked, and a count
    /// from its range. Never more picks than the table has entries.
    /// </summary>
    public static List<(string Item, int Count)> Roll(LootRoll roll, LootTableDefinition table, Random rng)
    {
        var left = (table.Items ?? []).Where(e => e.Item is not null && e.Weight > 0f).ToList();
        var most = Math.Max(roll.Picks, roll.PicksMax ?? roll.Picks);
        var picks = Math.Min(left.Count, most > roll.Picks ? rng.Next(roll.Picks, most + 1) : roll.Picks);
        var result = new List<(string, int)>(picks);
        for (var p = 0; p < picks; p++)
        {
            var at = rng.NextSingle() * left.Sum(e => e.Weight);
            var i = 0;
            while (i < left.Count - 1 && (at -= left[i].Weight) >= 0f)
                i++;
            var entry = left[i];
            left.RemoveAt(i);
            result.Add((entry.Item!, entry.CountMax is { } top && top > entry.Count ? rng.Next(entry.Count, top + 1) : entry.Count));
        }
        return result;
    }
}
