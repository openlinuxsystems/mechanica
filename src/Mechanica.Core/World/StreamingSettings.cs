using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.World;

/// <summary>
/// Tuning for world chunk streaming, Creation force-loading and sleep budgets, in SI units. The game
/// loads the values from JSON (balance numbers live in data); the defaults here only fill fields the
/// file leaves out.
/// </summary>
public sealed record StreamingSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Side of a square chunk in metres.</summary>
    public float ChunkSize { get; init; } = 64f;

    /// <summary>Side of the square world in metres, centred on the origin. Chunks outside it never load.</summary>
    public float WorldSize { get; init; } = 4096f;

    /// <summary>Chunks within this many metres (x/z, square) of a player stay loaded.</summary>
    public float PlayerLoadDistance { get; init; } = 128f;

    /// <summary>
    /// A Creation within this many metres (x/z) of a player is attended: it stays loaded and simulated
    /// whatever its activity, and a parked one resumes.
    /// </summary>
    public float PlayerNearDistance { get; init; } = 64f;

    /// <summary>Metres added around a force-loaded Creation's box before taking its chunks.</summary>
    public float CreationBuffer { get; init; } = 16f;

    /// <summary>Seconds a chunk nothing needs stays loaded before it unloads (stops churn at borders).</summary>
    public float UnloadDelay { get; init; } = 5f;

    /// <summary>A Creation is idle while every Body moves slower than this, in m/s.</summary>
    public float SleepLinearSpeed { get; init; } = 0.1f;

    /// <summary>A Creation is idle while every Body turns slower than this, in rad/s.</summary>
    public float SleepAngularSpeed { get; init; } = 0.1f;

    /// <summary>Seconds of idling before a Creation falls asleep and stops force-loading chunks.</summary>
    public float SleepDelay { get; init; } = 2f;

    /// <summary>Most awake Creations kept simulated away from every player; the rest park.</summary>
    public int MaxUnattendedCreations { get; init; } = 16;

    /// <summary>Most chunks that awake unattended Creations may force-load beyond what players already load.</summary>
    public int MaxUnattendedChunks { get; init; } = 64;

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static StreamingSettings FromJson(string json)
    {
        StreamingSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<StreamingSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid streaming settings JSON: {e.Message}", e);
        }

        if (settings is null)
            throw new FormatException("Streaming settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        Positive(ChunkSize, "chunk_size");
        Positive(WorldSize, "world_size");
        NonNegative(PlayerLoadDistance, "player_load_distance");
        NonNegative(PlayerNearDistance, "player_near_distance");
        NonNegative(CreationBuffer, "creation_buffer");
        NonNegative(UnloadDelay, "unload_delay");
        NonNegative(SleepLinearSpeed, "sleep_linear_speed");
        NonNegative(SleepAngularSpeed, "sleep_angular_speed");
        NonNegative(SleepDelay, "sleep_delay");
        if (MaxUnattendedCreations < 0)
            throw new FormatException($"max_unattended_creations must be zero or positive (got {MaxUnattendedCreations}).");
        if (MaxUnattendedChunks < 0)
            throw new FormatException($"max_unattended_chunks must be zero or positive (got {MaxUnattendedChunks}).");
        if (WorldSize < ChunkSize)
            throw new FormatException($"world_size ({WorldSize}) must be at least chunk_size ({ChunkSize}).");
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
