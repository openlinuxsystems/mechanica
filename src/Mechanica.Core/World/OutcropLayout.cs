using System.Numerics;

namespace Mechanica.Core.World;

/// <summary>One block of a rock deposit, in the deposit's own frame (metres, before the deposit's scale and turn).</summary>
/// <param name="Object">Id of the world object the block is.</param>
/// <param name="Mound">Which mound of the deposit it belongs to (0 is the middle one).</param>
/// <param name="Bottom">Middle of its underside: x and z across the deposit, y up from its mound's floor.</param>
/// <param name="Yaw">Its own turn about +Y in radians, on top of the deposit's.</param>
/// <param name="Variant">Which of its object's models it is drawn with (<see cref="WorldObjectPlacement.Variant"/>).</param>
/// <param name="Half">Half its footprint, x and z, in the deposit's frame.</param>
/// <param name="Height">How tall it stands.</param>
public readonly record struct OutcropBlock(string Object, int Mound, Vector3 Bottom, float Yaw, int Variant, Vector2 Half, float Height)
{
    /// <summary>Height of its top above its mound's floor.</summary>
    public float Top => Bottom.Y + Height;
}

/// <summary>
/// Heaps a rock deposit (<see cref="OutcropDefinition"/>): a cluster of mounds, each a pile of chiselled blocks of
/// mixed sizes, largest first so the big ones make the base. Every block stands on the ground or mostly on blocks
/// below it, and no two blocks overlap. Mounds stand apart, inside the deposit's footprint. Pure and seeded: the
/// same salt always gives the same blocks, in the same order (the order is <see cref="WorldObjectId.Piece"/>).
/// </summary>
public static class OutcropLayout
{
    /// <summary>Most blocks one mound may have.</summary>
    public const int MaxBlocks = 64;

    /// <summary>Most mounds one deposit may have.</summary>
    public const int MaxMounds = 12;

    /// <summary>Most a block turns away from the deposit's grid, in radians.</summary>
    public const float Twist = 0.25f;

    // Blocks touching closer than this (m) at their edges do not count as overlapping: chiselled edges leave room.
    private const float Give = 0.06f;

    /// <summary>
    /// The blocks of the deposit seeded by <paramref name="salt"/>. <paramref name="size"/> gives a block object's
    /// collider box (x, y up, z), null when unknown; <paramref name="variants"/> how many models it has (1 or more).
    /// Every block stays within <paramref name="radius"/> metres of the middle; a mound that will not fit is left out.
    /// </summary>
    public static IReadOnlyList<OutcropBlock> Blocks(OutcropDefinition outcrop, Func<string, Vector3?> size, Func<string, int> variants,
        float radius, uint salt)
    {
        var step = 0;
        float Roll() => Noise.Unit(Noise.Hash(salt, step++, 17, 29));
        int Between(int lo, int hi) => lo + Math.Min(hi - lo, (int)(Roll() * (hi - lo + 1)));

        var blocks = new List<OutcropBlock>();
        if (Pick(outcrop.Deposits, d => d.Weight, Roll()) is not { } deposit || deposit.Mounds is not [var minMounds, var maxMounds])
            return blocks;
        var mounds = Between(Math.Clamp(minMounds, 1, MaxMounds), Math.Clamp(maxMounds, Math.Max(1, minMounds), MaxMounds));
        var circles = new List<(Vector2 Centre, float Radius)>();
        for (var m = 0; m < mounds; m++)
        {
            if (Pick(deposit.Sizes, p => p.Weight, Roll())?.Mound is not { } name || outcrop.Mounds?.GetValueOrDefault(name) is not { } mound
                || mound.Blocks is not [var minBlocks, var maxBlocks])
                continue;
            var rich = m > 0 && Roll() < outcrop.RichChance;
            var count = Between(Math.Clamp(minBlocks, 1, MaxBlocks), Math.Clamp(maxBlocks, Math.Max(1, minBlocks), MaxBlocks));
            var kinds = new List<(string Object, Vector3 Size)>();
            for (var b = 0; b < count; b++)
            {
                if (Pick(mound.Kinds, k => k.Weight, Roll())?.Object is not { } id)
                    continue;
                if (rich && outcrop.Rich?.GetValueOrDefault(id) is { } swapped)
                    id = swapped;
                if (size(id) is { X: > 0, Y: > 0, Z: > 0 } s)
                    kinds.Add((id, s));
            }
            var heap = Heap(kinds, Roll);
            if (heap.Count == 0)
                continue;
            var reach = heap.Max(b => new Vector2(b.Bottom.X, b.Bottom.Z).Length() + b.Half.Length());

            // The first mound takes the middle; the others go where they keep clear of every mound so far,
            // reaching further out when the spread is full, but never past the deposit's footprint.
            Vector2? centre = null;
            for (var tries = 0; tries < 40 && centre is null; tries++)
            {
                var angle = Roll() * 2f * MathF.PI;
                var far = m == 0 ? 0f : deposit.Spread * MathF.Sqrt(Roll()) + tries * 0.25f;
                var at = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * far;
                if (far + reach <= radius && circles.All(c => Vector2.Distance(c.Centre, at) >= c.Radius + reach + 0.4f))
                    centre = at;
            }
            if (centre is not { } middle)
                continue;
            circles.Add((middle, reach));
            foreach (var b in heap)
            {
                var v = variants(b.Object);
                blocks.Add(b with { Mound = circles.Count - 1, Bottom = b.Bottom + new Vector3(middle.X, 0, middle.Y), Variant = v > 1 ? Between(0, v - 1) : 0 });
            }
        }
        return blocks;
    }

    // Piles the blocks into one heap around its own middle: largest first, each where it stands lowest and nearest
    // the middle, on the ground or with at least 45% of its underside on blocks whose tops are level with its floor.
    private static List<OutcropBlock> Heap(List<(string Object, Vector3 Size)> kinds, Func<float> roll)
    {
        var order = kinds.OrderByDescending(k => k.Size.X * k.Size.Y * k.Size.Z).ToList();
        var spread = 0.36f * MathF.Sqrt(order.Sum(k => k.Size.X * k.Size.Z));
        var cap = spread + 1.4f;
        var placed = new List<OutcropBlock>();
        foreach (var (id, size) in order)
        {
            var half = new Vector2(size.X / 2f, size.Z / 2f);
            (float Score, Vector2 At, float Floor)? best = null;
            for (var t = 0; t < 24; t++)
            {
                var angle = roll() * 2f * MathF.PI;
                var r = spread * MathF.Sqrt(roll());
                var at = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * r;
                var floor = 0f;
                foreach (var p in placed)
                {
                    if (Overlap(at, half, p) > 0f)
                        floor = MathF.Max(floor, p.Top);
                }
                if (floor + size.Y > cap)
                    continue;
                if (floor > 0f && placed.Where(p => MathF.Abs(p.Top - floor) < 0.08f).Sum(p => Overlap(at, half, p)) < 0.45f * Inner(half))
                    continue;
                var score = -(r + 0.2f * floor) + roll() * 0.4f;
                if (best is null || score > best.Value.Score)
                    best = (score, at, floor);
            }
            if (best is null)
            {
                // Nowhere on the heap: on the ground at its edge, stepping out until it is clear.
                var angle = roll() * 2f * MathF.PI;
                var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var r = spread;
                while (placed.Any(p => Overlap(dir * r, half, p) > 0f))
                    r += 0.25f;
                best = (0f, dir * r, 0f);
            }
            var (_, spot, height) = best.Value;
            placed.Add(new OutcropBlock(id, 0, new Vector3(spot.X, height, spot.Y), (roll() * 2f - 1f) * Twist, 0, half, size.Y));
        }
        return placed;
    }

    // Area (m²) where a block of half size `half` at `at` overlaps block `p`, both shrunk by the give at their edges.
    private static float Overlap(Vector2 at, Vector2 half, OutcropBlock p)
    {
        var ox = MathF.Min(at.X + half.X, p.Bottom.X + p.Half.X) - MathF.Max(at.X - half.X, p.Bottom.X - p.Half.X) - 2f * Give;
        var oz = MathF.Min(at.Y + half.Y, p.Bottom.Z + p.Half.Y) - MathF.Max(at.Y - half.Y, p.Bottom.Z - p.Half.Y) - 2f * Give;
        return ox > 0f && oz > 0f ? ox * oz : 0f;
    }

    // A block's underside area, less the give at its edges.
    private static float Inner(Vector2 half) => MathF.Max(0f, 2f * half.X - 2f * Give) * MathF.Max(0f, 2f * half.Y - 2f * Give);

    // One of the entries, picked by weight with a roll in [0, 1); null when there are none.
    private static T? Pick<T>(List<T>? entries, Func<T, float> weight, float roll) where T : class
    {
        if (entries is not { Count: > 0 })
            return null;
        var total = entries.Sum(e => MathF.Max(0f, weight(e)));
        if (!(total > 0f))
            return null;
        var t = roll * total;
        foreach (var e in entries)
        {
            t -= MathF.Max(0f, weight(e));
            if (t < 0f)
                return e;
        }
        return entries.Last(e => weight(e) > 0f);
    }
}
