using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Motion;

/// <summary>
/// Limits for the motion primitives of the Action API (<c>ApplyForce</c>, <c>SetBearingTarget</c>, <c>SetPistonTarget</c>), in
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

    /// <summary>Farthest an <c>ApplyForce</c> point (<c>at</c>) may be from the Body's centre of mass, in m.</summary>
    public float MaxLeverArm { get; init; } = 50f;

    /// <summary>Fastest bearing motor target, in rad/s.</summary>
    public float MaxBearingSpeed { get; init; } = 60f;

    /// <summary>Strongest bearing motor torque, in N·m.</summary>
    public float MaxBearingTorque { get; init; } = 2000f;

    /// <summary>Bearing motor torque when a call gives none, in N·m.</summary>
    public float DefaultBearingTorque { get; init; } = 200f;

    /// <summary>Top bearing speed toward an angle when a call gives none, in rad/s.</summary>
    public float DefaultBearingSpeed { get; init; } = 3f;

    /// <summary>Fastest piston motor speed, in m/s.</summary>
    public float MaxPistonSpeed { get; init; } = 10f;

    /// <summary>Strongest piston motor force, in N.</summary>
    public float MaxPistonForce { get; init; } = 50000f;

    /// <summary>Piston speed when a call gives none, in m/s.</summary>
    public float DefaultPistonSpeed { get; init; } = 1f;

    /// <summary>Piston force when a call gives none, in N.</summary>
    public float DefaultPistonForce { get; init; } = 2000f;

    /// <summary>
    /// How hard a motor with a target (a bearing angle, a piston length) closes in on it: the speed it asks
    /// for is this times the distance left, per s, up to its top speed.
    /// </summary>
    public float ServoGain { get; init; } = 10f;

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
        Positive(MaxLeverArm, "max_lever_arm");
        Positive(MaxBearingSpeed, "max_bearing_speed");
        Positive(MaxBearingTorque, "max_bearing_torque");
        Positive(DefaultBearingTorque, "default_bearing_torque");
        Positive(DefaultBearingSpeed, "default_bearing_speed");
        Positive(MaxPistonSpeed, "max_piston_speed");
        Positive(MaxPistonForce, "max_piston_force");
        Positive(DefaultPistonSpeed, "default_piston_speed");
        Positive(DefaultPistonForce, "default_piston_force");
        Positive(ServoGain, "servo_gain");
        if (DefaultBearingSpeed > MaxBearingSpeed)
            throw new FormatException($"default_bearing_speed ({DefaultBearingSpeed}) is above max_bearing_speed ({MaxBearingSpeed}).");
        if (DefaultPistonSpeed > MaxPistonSpeed)
            throw new FormatException($"default_piston_speed ({DefaultPistonSpeed}) is above max_piston_speed ({MaxPistonSpeed}).");
        if (DefaultPistonForce > MaxPistonForce)
            throw new FormatException($"default_piston_force ({DefaultPistonForce}) is above max_piston_force ({MaxPistonForce}).");
        if (DefaultBearingTorque > MaxBearingTorque)
            throw new FormatException($"default_bearing_torque ({DefaultBearingTorque}) is above max_bearing_torque ({MaxBearingTorque}).");
    }

    private static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new FormatException($"{name} must be a positive number (got {value}).");
    }
}
