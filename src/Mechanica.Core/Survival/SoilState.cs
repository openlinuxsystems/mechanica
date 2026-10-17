namespace Mechanica.Core.Survival;

/// <summary>
/// What is stored on one soil part (chunk 20): the planted seed, how far it has grown, how much water the
/// soil holds, the growth stage players see, and when growth was last worked out. How a crop grows is the
/// soil part's Lua script's business (D2: <c>soil.lua</c> in vanilla); this only keeps what it decided. It
/// lives on the soil part in its Creation, so it follows the part through splits and welds, is saved with it,
/// is sent to clients (<see cref="SoilView"/>) and is what bots and raids read.
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

    /// <summary>Growth stage, 0 to <see cref="Stages"/> (ripe). Always 0 with nothing planted.</summary>
    public int Stage { get; private set; }

    /// <summary>
    /// World time (seconds, the scripts' <c>world.time()</c> clock, which carries on across saves) up to which
    /// growth has been worked out, or null when it never has been. The soil script uses it to catch up on
    /// the time its Creation was parked.
    /// </summary>
    public double? Updated { get; private set; }

    /// <summary>Changes whenever what a player sees changes: a planting, a harvest, watering, a new stage, the water running out.</summary>
    public int Revision { get; private set; }

    /// <summary>True when nothing is planted and the soil is dry, so there is nothing to keep.</summary>
    public bool IsEmpty => Seed is null && Water <= 0;

    /// <summary>True when the crop can be harvested.</summary>
    public bool Ripe => Seed is not null && Stage >= Stages;

    /// <summary>Plants <paramref name="seed"/> (a qualified id) in empty soil.</summary>
    public void Plant(string seed)
    {
        if (Seed is not null)
            throw new InvalidOperationException("Something is already planted here.");
        Seed = seed;
        Grown = 0;
        Stage = 0;
        Revision++;
    }

    /// <summary>Takes the crop out (a harvest, or a bot destroying it), leaving the water.</summary>
    public void Clear()
    {
        Seed = null;
        Grown = 0;
        Stage = 0;
        Revision++;
    }

    /// <summary>Adds <paramref name="seconds"/> of water, up to <paramref name="capacity"/>.</summary>
    public void AddWater(float seconds, float capacity)
    {
        Water = Math.Min(capacity, Water + Math.Max(0, seconds));
        Revision++;
    }

    /// <summary>
    /// Stores what the soil script worked out. Null leaves a value as it is. <paramref name="grown"/> and
    /// <paramref name="stage"/> only apply with something planted.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is negative or not finite, or the stage is past <see cref="Stages"/>.</exception>
    public void Set(float? grown = null, float? water = null, int? stage = null, double? updated = null)
    {
        if (grown is { } g && !(float.IsFinite(g) && g >= 0))
            throw new ArgumentOutOfRangeException(nameof(grown), "grown must be 0 or more seconds.");
        if (water is { } w && !(float.IsFinite(w) && w >= 0))
            throw new ArgumentOutOfRangeException(nameof(water), "water must be 0 or more seconds.");
        if (stage is { } s && s is < 0 or > Stages)
            throw new ArgumentOutOfRangeException(nameof(stage), $"stage must be 0 to {Stages}.");
        if (updated is { } u && !(double.IsFinite(u) && u >= 0))
            throw new ArgumentOutOfRangeException(nameof(updated), "updated must be 0 or more seconds.");
        var (oldStage, wasWet) = (Stage, Water > 0);
        if (Seed is not null)
        {
            Grown = grown ?? Grown;
            Stage = stage ?? Stage;
        }
        Water = water ?? Water;
        Updated = updated ?? Updated;
        if (Stage != oldStage || wasWet != Water > 0)
            Revision++;
    }

    /// <summary>Sets every value at once, as when a save is loaded.</summary>
    public void Load(string? seed, float grown, float water, int stage = 0, double? updated = null)
    {
        Seed = seed;
        Grown = seed is null ? 0 : Math.Max(0, grown);
        Water = Math.Max(0, water);
        Stage = seed is null ? 0 : Math.Clamp(stage, 0, Stages);
        Updated = updated;
        Revision++;
    }
}
