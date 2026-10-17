using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Scripting;

/// <summary>
/// Limits and timings of the Lua host. The game loads them from JSON (<c>game/data/scripting.json</c>);
/// the defaults here only fill fields the file leaves out.
/// </summary>
public sealed record ScriptSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Most Lua VM instructions one hook call may run before the host stops it and marks the script
    /// failed (a guard against endless loops, not a performance budget).
    /// </summary>
    public int InstructionBudget { get; init; } = 1_000_000;

    /// <summary>Seconds between checks of the loaded script files for changes (hot reload).</summary>
    public float ReloadPollSeconds { get; init; } = 0.5f;

    /// <summary>Longest string <c>string.rep</c> may build, in characters.</summary>
    public int MaxStringLength { get; init; } = 1_000_000;

    /// <summary>Longest ray <c>world.raycast</c> may cast, in m.</summary>
    public float MaxRayLength { get; init; } = 500f;

    /// <summary>Largest sphere <c>world.overlap_sphere</c> may test, radius in m.</summary>
    public float MaxOverlapRadius { get; init; } = 25f;

    /// <summary>Most messages (prints, warnings, errors) the host keeps for the editor console.</summary>
    public int MaxMessages { get; init; } = 500;

    /// <summary>Most sound loops one part may run at once (<c>sound.start</c>).</summary>
    public int MaxLoopsPerPart { get; init; } = 4;

    /// <summary>Most projectiles item scripts may have in flight at once (<c>combat.projectile</c>).</summary>
    public int MaxProjectiles { get; init; } = 128;

    /// <summary>Longest a projectile may fly, in s (and how long it flies when a script doesn't say).</summary>
    public float MaxProjectileLifetime { get; init; } = 10f;

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static ScriptSettings FromJson(string json)
    {
        ScriptSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<ScriptSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid scripting settings JSON: {e.Message}", e);
        }
        if (settings is null)
            throw new FormatException("Scripting settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        if (InstructionBudget < 1000)
            throw new FormatException($"instruction_budget must be at least 1000 (got {InstructionBudget}).");
        if (!float.IsFinite(ReloadPollSeconds) || ReloadPollSeconds <= 0f)
            throw new FormatException($"reload_poll_seconds must be a positive number (got {ReloadPollSeconds}).");
        if (MaxStringLength < 1)
            throw new FormatException($"max_string_length must be positive (got {MaxStringLength}).");
        if (!float.IsFinite(MaxRayLength) || MaxRayLength <= 0f)
            throw new FormatException($"max_ray_length must be a positive number (got {MaxRayLength}).");
        if (!float.IsFinite(MaxOverlapRadius) || MaxOverlapRadius <= 0f)
            throw new FormatException($"max_overlap_radius must be a positive number (got {MaxOverlapRadius}).");
        if (MaxMessages < 1)
            throw new FormatException($"max_messages must be positive (got {MaxMessages}).");
        if (MaxLoopsPerPart < 0)
            throw new FormatException($"max_loops_per_part must not be negative (got {MaxLoopsPerPart}).");
        if (MaxProjectiles < 0)
            throw new FormatException($"max_projectiles must be 0 or more (got {MaxProjectiles}).");
        if (!float.IsFinite(MaxProjectileLifetime) || MaxProjectileLifetime <= 0f)
            throw new FormatException($"max_projectile_lifetime must be a positive number (got {MaxProjectileLifetime}).");
    }
}
