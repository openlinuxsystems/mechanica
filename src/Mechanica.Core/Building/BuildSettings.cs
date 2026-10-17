using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Building;

/// <summary>
/// Limits for building by hand (Hammer placement and removal), in SI units. The game loads the values
/// from JSON (balance numbers live in data); the defaults here only fill fields the file leaves out.
/// </summary>
public sealed record BuildSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>How far from a player's eyes a part may be placed or removed, in metres.</summary>
    public float Reach { get; init; } = 7.5f;

    /// <summary>Most parts one Creation may hold; placing beyond it is refused.</summary>
    public int MaxPartsPerCreation { get; init; } = 4096;

    /// <summary>
    /// How much of a cell (0 to 1) the clearance check tests for overlap with players, other Creations
    /// and the world, so parts can sit flush against a surface without touching it.
    /// </summary>
    public float ClearanceScale { get; init; } = 0.9f;

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static BuildSettings FromJson(string json)
    {
        BuildSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<BuildSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid build settings JSON: {e.Message}", e);
        }
        if (settings is null)
            throw new FormatException("Build settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        if (!float.IsFinite(Reach) || Reach <= 0f)
            throw new FormatException($"reach must be a positive number (got {Reach}).");
        if (MaxPartsPerCreation < 1)
            throw new FormatException($"max_parts_per_creation must be at least 1 (got {MaxPartsPerCreation}).");
        if (!float.IsFinite(ClearanceScale) || ClearanceScale is <= 0f or > 1f)
            throw new FormatException($"clearance_scale must be in (0, 1] (got {ClearanceScale}).");
    }
}
