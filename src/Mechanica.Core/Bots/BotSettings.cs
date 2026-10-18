using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Bots;

/// <summary>
/// Engine limits and timings for bots (chunk 21). The game loads them from <c>game/data/bots.json</c>;
/// the defaults only fill fields the file leaves out. A bot's own numbers live in its mod's
/// <c>bots/*.json</c>, and raid tables in <c>raids/raids.json</c>.
/// </summary>
public sealed record BotSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Most bots in the world at once; spawning past it is refused.</summary>
    public int MaxBots { get; init; } = 128;

    /// <summary>Seconds between two thinks of one bot (its Lua <c>on_think</c>).</summary>
    public float ThinkInterval { get; init; } = 0.1f;

    /// <summary>Seconds a dead bot's body stays before it is gone.</summary>
    public float CorpseSeconds { get; init; } = 3f;

    /// <summary>Extra reach the host allows an attack, in m (positions move between a think and its attack).</summary>
    public float RangeTolerance { get; init; } = 0.4f;

    /// <summary>A player's body radius, for attack reach, in m.</summary>
    public float PlayerRadius { get; init; } = 0.4f;

    /// <summary>How far from the world centre a bot may be spawned, in m.</summary>
    public float MaxSpawnDistance { get; init; } = 2048f;

    /// <summary>A POI's guards (its <c>bot</c> markers) appear when a living player comes this close, in m.</summary>
    public float GuardWakeDistance { get; init; } = 64f;

    /// <summary>A POI's guards leave when no living player is this close, in m; at least <see cref="GuardWakeDistance"/>.</summary>
    public float GuardSleepDistance { get; init; } = 96f;

    /// <summary>Seconds after a guard dies before its post can field another.</summary>
    public float GuardRespawn { get; init; } = 1200f;

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static BotSettings FromJson(string json)
    {
        BotSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<BotSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid bot settings JSON: {e.Message}", e);
        }
        if (settings is null)
            throw new FormatException("Bot settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        if (MaxBots < 1)
            throw new FormatException($"max_bots must be 1 or more (got {MaxBots}).");
        Positive(ThinkInterval, "think_interval");
        NonNegative(CorpseSeconds, "corpse_seconds");
        NonNegative(RangeTolerance, "range_tolerance");
        NonNegative(PlayerRadius, "player_radius");
        Positive(MaxSpawnDistance, "max_spawn_distance");
        Positive(GuardWakeDistance, "guard_wake_distance");
        Positive(GuardSleepDistance, "guard_sleep_distance");
        NonNegative(GuardRespawn, "guard_respawn");
        if (GuardSleepDistance < GuardWakeDistance)
            throw new FormatException($"guard_sleep_distance must be at least guard_wake_distance (got {GuardSleepDistance} < {GuardWakeDistance}).");
    }

    private static void Positive(float value, string name)
    {
        if (!(value > 0f) || !float.IsFinite(value))
            throw new FormatException($"{name} must be greater than 0 (got {value}).");
    }

    private static void NonNegative(float value, string name)
    {
        if (!(value >= 0f) || !float.IsFinite(value))
            throw new FormatException($"{name} must be 0 or more (got {value}).");
    }
}
