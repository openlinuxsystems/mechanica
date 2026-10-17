namespace Mechanica.Core.Survival;

/// <summary>
/// What grows on one soil part (chunk 20): the planted seed, how far it has grown, and how much water
/// the soil holds. A crop grows only while the soil has water, using it up second for second, and is
/// ripe once it has grown for its seed's <c>grow_time</c>. It lives on the soil part in its Creation, so it
/// follows the part through splits and welds and is saved with it.
/// </summary>
public sealed class SoilState
{
    /// <summary>Growth stages a crop shows on its way to ripe (stage <see cref="Stages"/> is ripe).</summary>
    public const int Stages = 4;

    /// <summary>Qualified id of the planted seed, or null when nothing is planted.</summary>
    public string? Seed { get; private set; }

    /// <summary>Seconds of growth done.</summary>
    public float Grown { get; private set; }

    /// <summary>Seconds of water left in the soil.</summary>
    public float Water { get; private set; }

    /// <summary>Changes whenever what a player sees changes: a planting, a harvest, watering, a new stage, the water running out.</summary>
    public int Revision { get; private set; }

    /// <summary>True when nothing is planted and the soil is dry, so there is nothing to keep.</summary>
    public bool IsEmpty => Seed is null && Water <= 0;

    /// <summary>Growth stage 0 to <see cref="Stages"/> for a crop that takes <paramref name="growTime"/> seconds.</summary>
    public int StageFor(float growTime) =>
        Seed is null ? 0 : growTime <= 0 ? Stages : Math.Clamp((int)(Grown / growTime * Stages), 0, Stages);

    /// <summary>Plants <paramref name="seed"/> (a qualified id) in empty soil.</summary>
    public void Plant(string seed)
    {
        if (Seed is not null)
            throw new InvalidOperationException("Something is already planted here.");
        Seed = seed;
        Grown = 0;
        Revision++;
    }

    /// <summary>Takes the crop out (a harvest), leaving the water.</summary>
    public void Clear()
    {
        Seed = null;
        Grown = 0;
        Revision++;
    }

    /// <summary>Adds <paramref name="seconds"/> of water, up to <paramref name="capacity"/>.</summary>
    public void AddWater(float seconds, float capacity)
    {
        Water = Math.Min(capacity, Water + Math.Max(0, seconds));
        Revision++;
    }

    /// <summary>Sets every value at once, as when a save is loaded.</summary>
    public void Load(string? seed, float grown, float water)
    {
        Seed = seed;
        Grown = seed is null ? 0 : Math.Max(0, grown);
        Water = Math.Max(0, water);
        Revision++;
    }

    /// <summary>
    /// Grows the crop for <paramref name="seconds"/> at <paramref name="speed"/>, as far as the water
    /// lasts and no further than <paramref name="growTime"/>. Soil with nothing growing keeps its water.
    /// </summary>
    public void Tick(float seconds, float speed, float growTime)
    {
        if (Seed is null || Water <= 0 || Grown >= growTime || seconds <= 0)
            return;
        var stage = StageFor(growTime);
        // Water is used only for the growing still to do.
        var used = Math.Min(Math.Min(seconds, Water), speed > 0 ? (growTime - Grown) / speed : seconds);
        Water -= used;
        Grown = Math.Min(growTime, Grown + used * speed);
        if (Water <= 0 || StageFor(growTime) != stage)
            Revision++;
    }
}
