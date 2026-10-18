using Mechanica.Core.Data;

namespace Mechanica.Core.Ui;

/// <summary>
/// What fills all ten pips of each stat in a detail card (<c>game/data/hud.json</c> <c>stat_pips</c>). Mass spans
/// grams to tonnes, so its pips follow the logarithm of the mass; the others are linear.
/// </summary>
public sealed record StatPipScale
{
    /// <summary>Mass in kg that fills ten pips (logarithmic).</summary>
    public float MassKg { get; init; } = 900f;

    /// <summary>Durability that fills ten pips.</summary>
    public float Durability { get; init; } = 30f;

    /// <summary>Friction coefficient that fills ten pips.</summary>
    public float Friction { get; init; } = 1f;

    /// <summary>Buoyancy factor that fills ten pips.</summary>
    public float Buoyancy { get; init; } = 1f;
}

/// <summary>One row of a detail card's stats panel: its name, filled pips (0 to <see cref="ItemStats.Pips"/>) and value text.</summary>
public sealed record StatRow(string Name, int Pips, string Value);

/// <summary>A part's physical stats as a detail card shows them (UI spec §0, §8): pip bars with the SI value.</summary>
public static class ItemStats
{
    /// <summary>Pips in a full bar.</summary>
    public const int Pips = 10;

    /// <summary>
    /// Mass, Durability, Friction and Buoyancy rows for the stats <paramref name="part"/> has, in that order; empty
    /// when it has none (resources and consumables hide the panel).
    /// </summary>
    public static IReadOnlyList<StatRow> Rows(PartDefinition part, StatPipScale scale)
    {
        var rows = new List<StatRow>();
        if (part.Mass is { } mass)
            rows.Add(new("Mass", Fill(MathF.Log(1 + mass) / MathF.Log(1 + scale.MassKg), mass), $"{Number(mass)} kg"));
        if (part.Durability is { } durability)
            rows.Add(new("Durability", Fill(durability / scale.Durability, durability), $"{durability}"));
        if (part.Friction is { } friction)
            rows.Add(new("Friction", Fill(friction / scale.Friction, friction), Number(friction)));
        if (part.Buoyancy is { } buoyancy)
            rows.Add(new("Buoyancy", Fill(buoyancy / scale.Buoyancy, buoyancy), Number(buoyancy)));
        return rows;
    }

    /// <summary>The footprint as the card's subtitle shows it, such as <c>0.5×0.5×0.75 m</c>, or null for none.</summary>
    public static string? Size(PartDefinition part) =>
        part.Size is { } size ? $"{Number(size.Metres.X)}×{Number(size.Metres.Y)}×{Number(size.Metres.Z)} m" : null;

    // Anything above zero shows at least one pip, so a light part never reads as weightless.
    private static int Fill(float share, float value) =>
        value <= 0 || !float.IsFinite(share) ? 0 : Math.Clamp((int)MathF.Round(share * Pips), 1, Pips);

    private static string Number(float value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
