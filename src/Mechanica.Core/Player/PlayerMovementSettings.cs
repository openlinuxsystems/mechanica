using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Player;

/// <summary>
/// Tuning for the on-foot player, in SI units. The game loads the values from JSON
/// (balance numbers live in data); the defaults here only fill fields the file leaves out.
/// </summary>
public sealed record PlayerMovementSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Top walking speed in m/s.</summary>
    public float WalkSpeed { get; init; } = 4f;

    /// <summary>Top sprinting speed in m/s.</summary>
    public float SprintSpeed { get; init; } = 7f;

    /// <summary>Horizontal acceleration on the ground in m/s².</summary>
    public float GroundAcceleration { get; init; } = 40f;

    /// <summary>Horizontal acceleration in the air in m/s² (air control).</summary>
    public float AirAcceleration { get; init; } = 8f;

    /// <summary>Upward speed given by a jump in m/s.</summary>
    public float JumpSpeed { get; init; } = 4.5f;

    /// <summary>Downward acceleration in m/s².</summary>
    public float Gravity { get; init; } = 9.81f;

    /// <summary>Fastest fall speed in m/s.</summary>
    public float MaxFallSpeed { get; init; } = 50f;

    /// <summary>Look limit above and below the horizon in degrees.</summary>
    public float MaxPitchDegrees { get; init; } = 89f;

    /// <summary>Capsule height in metres.</summary>
    public float Height { get; init; } = 1.8f;

    /// <summary>Capsule radius in metres.</summary>
    public float Radius { get; init; } = 0.35f;

    /// <summary>Camera height above the feet in metres.</summary>
    public float EyeHeight { get; init; } = 1.6f;

    /// <summary>Force in N the player applies to a rigid body it walks into.</summary>
    public float PushForce { get; init; } = 600f;

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static PlayerMovementSettings FromJson(string json)
    {
        PlayerMovementSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<PlayerMovementSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid player settings JSON: {e.Message}", e);
        }

        if (settings is null)
            throw new FormatException("Player settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        Positive(WalkSpeed, "walk_speed");
        Positive(SprintSpeed, "sprint_speed");
        Positive(GroundAcceleration, "ground_acceleration");
        NonNegative(AirAcceleration, "air_acceleration");
        NonNegative(JumpSpeed, "jump_speed");
        NonNegative(Gravity, "gravity");
        Positive(MaxFallSpeed, "max_fall_speed");
        Positive(Height, "height");
        Positive(Radius, "radius");
        NonNegative(PushForce, "push_force");
        if (!float.IsFinite(MaxPitchDegrees) || MaxPitchDegrees is <= 0f or >= 90f)
            throw new FormatException($"max_pitch_degrees must be between 0 and 90 (got {MaxPitchDegrees}).");
        if (Height < 2f * Radius)
            throw new FormatException($"height ({Height}) must be at least twice radius ({Radius}).");
        if (!float.IsFinite(EyeHeight) || EyeHeight <= 0f || EyeHeight > Height)
            throw new FormatException($"eye_height must be in (0, height] (got {EyeHeight}).");
    }

    private static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new FormatException($"{name} must be a positive number (got {value}).");
    }

    private static void NonNegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f)
            throw new FormatException($"{name} must be zero or positive (got {value}).");
    }
}
