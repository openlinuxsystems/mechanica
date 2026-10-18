namespace Mechanica.Core.World;

/// <summary>One cube of a rock outcrop, on the outcrop's own grid of whole cubes.</summary>
/// <param name="X">Column across.</param>
/// <param name="Y">Layer up from the ground (0 stands on it).</param>
/// <param name="Z">Column along.</param>
/// <param name="Turn">Quarter turns of the cube's look about +Y (0 to 3), so neighbours do not match.</param>
public readonly record struct OutcropCube(int X, int Y, int Z, int Turn);

/// <summary>
/// Stacks a rock outcrop (<see cref="OutcropDefinition"/>): a connected footprint of columns grown out from the
/// middle, the middle columns standing tallest and the edges stepping down, as in Scrap Mechanic's cubic boulders.
/// Pure and seeded: the same salt always gives the same cubes, in the same order (the order is
/// <see cref="WorldObjectId.Piece"/>).
/// </summary>
public static class OutcropLayout
{
    /// <summary>Most cubes one outcrop may have.</summary>
    public const int MaxCubes = 64;

    /// <summary>Most cubes one column may stand.</summary>
    public const int MaxHeight = 8;

    /// <summary>The cubes of the outcrop seeded by <paramref name="salt"/>: columns bottom up, the middle column first.</summary>
    public static IReadOnlyList<OutcropCube> Cubes(OutcropDefinition outcrop, uint salt)
    {
        var step = 0;
        float Roll() => Noise.Unit(Noise.Hash(salt, step++, 17, 29));
        int Between(int lo, int hi) => lo + Math.Min(hi - lo, (int)(Roll() * (hi - lo + 1)));

        var (minCubes, maxCubes) = outcrop.Cubes is [var a, var b] ? (Math.Max(1, a), Math.Clamp(b, Math.Max(1, a), MaxCubes)) : (6, 20);
        var (minHeight, maxHeight) = outcrop.Height is [var c, var d] ? (Math.Max(1, c), Math.Clamp(d, Math.Max(1, c), MaxHeight)) : (1, 4);
        var count = Between(minCubes, maxCubes);

        // Footprint: enough columns that the cubes stand about halfway up the height range, grown one
        // neighbour at a time from the middle so the outcrop is one connected mass.
        var meanHeight = (minHeight + maxHeight) / 2f;
        var columnCount = Math.Clamp((int)MathF.Round(count / meanHeight), 1, count);
        var columns = new List<(int X, int Z)> { (0, 0) };
        var taken = new HashSet<(int, int)> { (0, 0) };
        while (columns.Count < columnCount)
        {
            var (x, z) = columns[Math.Min(columns.Count - 1, (int)(Roll() * columns.Count))];
            var (dx, dz) = Between(0, 3) switch { 0 => (1, 0), 1 => (-1, 0), 2 => (0, 1), _ => (0, -1) };
            var next = (x + dx, z + dz);
            // Keep it compact: a blob, not a snake.
            if (MathF.Sqrt(next.Item1 * next.Item1 + next.Item2 * next.Item2) > MathF.Sqrt(columnCount) * 0.8f + 0.5f || !taken.Add(next))
                continue;
            columns.Add(next);
        }

        // Heights: tallest in the middle, stepping down to the edge, with a little noise.
        var heights = new int[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            var rank = columns.Count == 1 ? 0f : (float)i / (columns.Count - 1);
            var h = maxHeight - (maxHeight - minHeight) * rank + (Roll() - 0.5f) * 1.2f;
            heights[i] = Math.Clamp((int)MathF.Round(h), minHeight, maxHeight);
        }
        // Then add or take away cubes until there are exactly `count`, at the edge first when taking and the
        // middle first when adding, as long as the height range allows.
        var total = heights.Sum();
        for (var guard = 0; total != count && guard < 1000; guard++)
        {
            var i = total > count
                ? columns.Count - 1 - Math.Min(columns.Count - 1, (int)(Roll() * Roll() * columns.Count))
                : Math.Min(columns.Count - 1, (int)(Roll() * Roll() * columns.Count));
            if (total > count && heights[i] > minHeight)
            {
                heights[i]--;
                total--;
            }
            else if (total < count && heights[i] < maxHeight)
            {
                heights[i]++;
                total++;
            }
        }

        var cubes = new List<OutcropCube>(total);
        for (var i = 0; i < columns.Count; i++)
        {
            for (var y = 0; y < heights[i]; y++)
                cubes.Add(new OutcropCube(columns[i].X, y, columns[i].Z, Between(0, 3)));
        }
        return cubes;
    }
}
