using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Fire;

/// <summary>
/// How fire behaves (D13: flammable parts ignite, fire spreads to adjacent flammable parts, deals damage over
/// time and is put out by water). The game loads them from <c>game/data/fire.json</c> (balance numbers live in
/// data); the defaults only fill fields the file leaves out. A fire's strength runs from 0 (out) to 1; how
/// much water puts one out is the watering item's or part's own data.
/// </summary>
public sealed record FireSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Seconds between the host's fire updates.</summary>
    public float Tick { get; init; } = 0.25f;

    /// <summary>Strength a fire gains per second, up to 1.</summary>
    public float Grow { get; init; } = 0.08f;

    /// <summary>Strength a newly lit part starts burning at.</summary>
    public float Ignite { get; init; } = 0.2f;

    /// <summary>Strength at which a burning part sets its flammable neighbours alight.</summary>
    public float SpreadAt { get; init; } = 0.6f;

    /// <summary>Health per second a full-strength fire takes off the part it burns (less when weaker).</summary>
    public float PartDamage { get; init; } = 4f;

    /// <summary>Health per second a full-strength fire takes off a player standing in it (less when weaker).</summary>
    public float PlayerDamage { get; init; } = 10f;

    /// <summary>How close to a fire (m) a player's feet are when it burns them.</summary>
    public float PlayerRadius { get; init; } = 1.2f;

    /// <summary>How close to a fire in the world (m) a flammable part catches.</summary>
    public float SiteRadius { get; init; } = 1.5f;

    /// <summary>POI marker type where fires burn when a world is made (the crash site's fires).</summary>
    public string SiteMarker { get; init; } = "fire";

    /// <summary>Looping sound event every fire plays (its <c>intensity</c> follows the fire's strength); a bare id is vanilla's.</summary>
    public string Sound { get; init; } = "burn_wood";

    /// <summary>Most fires burning at once; nothing more catches past it.</summary>
    public int MaxFires { get; init; } = 512;

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static FireSettings FromJson(string json)
    {
        FireSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<FireSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid fire settings JSON: {e.Message}", e);
        }
        if (settings is null)
            throw new FormatException("Fire settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        SurvivalSettings.Positive(Tick, "tick");
        SurvivalSettings.NonNegative(Grow, "grow");
        if (!(Ignite is > 0f and <= 1f))
            throw new FormatException($"ignite must be more than 0 and at most 1 (got {Ignite}).");
        if (!(SpreadAt is > 0f and <= 1f))
            throw new FormatException($"spread_at must be more than 0 and at most 1 (got {SpreadAt}).");
        SurvivalSettings.NonNegative(PartDamage, "part_damage");
        SurvivalSettings.NonNegative(PlayerDamage, "player_damage");
        SurvivalSettings.NonNegative(PlayerRadius, "player_radius");
        SurvivalSettings.NonNegative(SiteRadius, "site_radius");
        if (!Data.Identifiers.IsValid(SiteMarker))
            throw new FormatException($"site_marker '{SiteMarker}' is not a marker type.");
        if (!Sound.Split(':').All(Data.Identifiers.IsValid) || Sound.Count(c => c == ':') > 1)
            throw new FormatException($"sound '{Sound}' is not a sound event id.");
        if (MaxFires < 1)
            throw new FormatException($"max_fires must be 1 or more (got {MaxFires}).");
    }
}
