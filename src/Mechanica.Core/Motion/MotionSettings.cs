using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Motion;

/// <summary>
/// Limits for the motion primitives of the Action API (<c>ApplyForce</c>, <c>SetBearingTarget</c>), in
/// SI units. The game loads the values from JSON (balance numbers live in data); the defaults here only
/// fill fields the file leaves out.
/// </summary>
public sealed record MotionSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Largest force one <c>ApplyForce</c> call may push with, in N.</summary>
    public float MaxForce { get; init; } = 20000f;

    /// <summary>Largest torque one <c>ApplyForce</c> call may twist with, in N·m.</summary>
    public float MaxTorque { get; init; } = 10000f;

    /// <summary>Longest time one <c>ApplyForce</c> call keeps pushing, in s.</summary>
    public float MaxDuration { get; init; } = 5f;

    /// <summary>Fastest bearing motor target, in rad/s.</summary>
    public float MaxBearingSpeed { get; init; } = 60f;

    /// <summary>Strongest bearing motor torque, in N·m.</summary>
    public float MaxBearingTorque { get; init; } = 2000f;

    /// <summary>Bearing motor torque when a call gives none, in N·m.</summary>
    public float DefaultBearingTorque { get; init; } = 200f;

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static MotionSettings FromJson(string json)
    {
        MotionSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<MotionSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid motion settings JSON: {e.Message}", e);
        }
        if (settings is null)
            throw new FormatException("Motion settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        Positive(MaxForce, "max_force");
        Positive(MaxTorque, "max_torque");
        Positive(MaxDuration, "max_duration");
        Positive(MaxBearingSpeed, "max_bearing_speed");
        Positive(MaxBearingTorque, "max_bearing_torque");
        Positive(DefaultBearingTorque, "default_bearing_torque");
        if (DefaultBearingTorque > MaxBearingTorque)
            throw new FormatException($"default_bearing_torque ({DefaultBearingTorque}) is above max_bearing_torque ({MaxBearingTorque}).");
    }

    private static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new FormatException($"{name} must be a positive number (got {value}).");
    }
}
