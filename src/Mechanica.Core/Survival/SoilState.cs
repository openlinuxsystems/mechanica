namespace Mechanica.Core.Survival;

/// <summary>How wet a soil part looks (Soil Design Spec): four states players read at a glance.</summary>
public enum SoilWetness
{
    /// <summary>No water: pale, dusty and crumbly.</summary>
    Dry,

    /// <summary>Some water, on its way up to wet: medium brown with damp patches.</summary>
    Watered,

    /// <summary>At or above the soil's wet level: dark, glossy, small puddles.</summary>
    Wet,

    /// <summary>Was wet and is losing its water: fading, lighter, crusting patches.</summary>
    Drying,
}

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

    /// <summary>
    /// True once the soil has been fully wet (its water at or above its wet level) since it was last dry: on the way
    /// down it looks <see cref="SoilWetness.Drying"/>, not <see cref="SoilWetness.Watered"/>.
    /// </summary>
    public bool Soaked { get; private set; }

    /// <summary>
    /// How many times faster than normal the planted crop grows (1 normally): Fertilizer (consumables batch C2) raises it
    /// and the soil's script multiplies it into growth. Planting and harvesting (<see cref="Clear"/>) put it back to 1.
    /// </summary>
    public float Boost { get; private set; } = 1f;

    /// <summary>Most a boost can be (<see cref="Set"/>).</summary>
    public const float MaxBoost = 100f;

    /// <summary>True when nothing is planted and the soil is dry, so there is nothing to keep.</summary>
    public bool IsEmpty => Seed is null && Water <= 0;

    /// <summary>True when the crop can be harvested.</summary>
    public bool Ripe => Seed is not null && Stage >= Stages;

    /// <summary>How the soil looks with <paramref name="wetAt"/> seconds of water counting as fully wet.</summary>
    public SoilWetness Wetness(float wetAt) => WetnessOf(Water, Soaked, wetAt);

    /// <summary>How soil holding <paramref name="water"/> seconds looks, <paramref name="wetAt"/> seconds counting as fully wet.</summary>
    public static SoilWetness WetnessOf(float water, bool soaked, float wetAt) =>
        water <= 0 ? SoilWetness.Dry
        : water >= wetAt ? SoilWetness.Wet
        : soaked ? SoilWetness.Drying
        : SoilWetness.Watered;

    /// <summary>
    /// Seconds of water at which soil looks fully wet: <paramref name="soil"/>'s wet level (0.5 if omitted) of its capacity,
    /// never less than a sliver so a soil with no capacity is never wet.
    /// </summary>
    public static float WetAt(Data.SoilDefinition? soil) => Math.Max(1e-3f, (soil?.WetLevel ?? 0.5f) * (soil?.Capacity ?? 0));

    /// <summary>Plants <paramref name="seed"/> (a qualified id) in empty soil.</summary>
    public void Plant(string seed)
    {
        if (Seed is not null)
            throw new InvalidOperationException("Something is already planted here.");
        Seed = seed;
        Grown = 0;
        Stage = 0;
        Boost = 1f;
        Revision++;
    }

    /// <summary>Takes the crop out (a harvest, or a bot destroying it), leaving the water; the crop's boost goes with it.</summary>
    public void Clear()
    {
        Seed = null;
        Grown = 0;
        Stage = 0;
        Boost = 1f;
        Revision++;
    }

    /// <summary>
    /// Adds <paramref name="seconds"/> of water, up to <paramref name="capacity"/>; reaching <paramref name="wetAt"/>
    /// seconds soaks it (<see cref="Soaked"/>).
    /// </summary>
    public void AddWater(float seconds, float capacity, float wetAt = float.PositiveInfinity)
    {
        Water = Math.Min(capacity, Water + Math.Max(0, seconds));
        Soaked |= Water >= wetAt;
        Revision++;
    }

    /// <summary>
    /// Stores what the soil script worked out. Null leaves a value as it is. <paramref name="grown"/>,
    /// <paramref name="stage"/> and <paramref name="boost"/> (1 to <see cref="MaxBoost"/>) only apply with something planted. With <paramref name="wetAt"/> (seconds of water that
    /// look fully wet) a change of <see cref="Wetness"/> counts as a change players see; without it only wet and dry do.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is negative or not finite, or the stage is past <see cref="Stages"/>.</exception>
    public void Set(float? grown = null, float? water = null, int? stage = null, double? updated = null, float? wetAt = null, float? boost = null)
    {
        if (grown is { } g && !(float.IsFinite(g) && g >= 0))
            throw new ArgumentOutOfRangeException(nameof(grown), "grown must be 0 or more seconds.");
        if (water is { } w && !(float.IsFinite(w) && w >= 0))
            throw new ArgumentOutOfRangeException(nameof(water), "water must be 0 or more seconds.");
        if (stage is { } s && s is < 0 or > Stages)
            throw new ArgumentOutOfRangeException(nameof(stage), $"stage must be 0 to {Stages}.");
        if (updated is { } u && !(double.IsFinite(u) && u >= 0))
            throw new ArgumentOutOfRangeException(nameof(updated), "updated must be 0 or more seconds.");
        if (boost is { } b && !(float.IsFinite(b) && b is >= 1f and <= MaxBoost))
            throw new ArgumentOutOfRangeException(nameof(boost), $"boost must be from 1 to {MaxBoost:0}.");
        var (oldStage, wasWet, oldLook, oldBoost) = (Stage, Water > 0, Wetness(wetAt ?? float.PositiveInfinity), Boost);
        if (Seed is not null)
        {
            Grown = grown ?? Grown;
            Stage = stage ?? Stage;
            Boost = boost ?? Boost;
        }
        Water = water ?? Water;
        Updated = updated ?? Updated;
        if (Water <= 0)
            Soaked = false;
        else if (wetAt is { } at && Water >= at)
            Soaked = true;
        if (Stage != oldStage || wasWet != Water > 0 || Boost != oldBoost || (wetAt is { } look && Wetness(look) != oldLook))
            Revision++;
    }

    /// <summary>Sets every value at once, as when a save is loaded.</summary>
    public void Load(string? seed, float grown, float water, int stage = 0, double? updated = null, bool soaked = false, float boost = 1f)
    {
        Seed = seed;
        Boost = seed is null || !float.IsFinite(boost) ? 1f : Math.Clamp(boost, 1f, MaxBoost);
        Grown = seed is null ? 0 : Math.Max(0, grown);
        Water = Math.Max(0, water);
        Stage = seed is null ? 0 : Math.Clamp(stage, 0, Stages);
        Updated = updated;
        Soaked = soaked && Water > 0;
        Revision++;
    }
}
