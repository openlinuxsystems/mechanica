using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Net;

/// <summary>
/// Tuning for Creation state sync (chunk 4b), in SI units. The game loads the values from JSON (tuning
/// lives in data); the defaults here only fill fields the file leaves out.
/// </summary>
public sealed record SyncSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>A Creation whose box comes within this many metres (x/z) of a player is sent to that player's peer.</summary>
    public float InterestDistance { get; init; } = 128f;

    /// <summary>Extra metres a Creation may drift past <see cref="InterestDistance"/> before the peer drops it.</summary>
    public float InterestHysteresis { get; init; } = 16f;

    /// <summary>Seconds between snapshots of a Creation whose Bodies are all asleep (awake ones go every snapshot).</summary>
    public float SleepingSnapshotInterval { get; init; } = 0.5f;

    /// <summary>Largest body snapshot packet in bytes; bigger updates are split (<see cref="BodySnapshot.Pack"/>).</summary>
    public int MaxPacketBytes { get; init; } = 1024;

    /// <summary>Seconds a client keeps a Body moving on its last velocity when snapshots stop arriving.</summary>
    public float MaxExtrapolation { get; init; } = 0.1f;

    /// <summary>
    /// Fewest seconds between two full syncs of one Creation to a peer that already has it: a run of quick changes (a
    /// slider dragged, paint sprayed, parts placed one after another) goes as one, at most this late.
    /// </summary>
    public float FullSyncInterval { get; init; } = 0.1f;

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static SyncSettings FromJson(string json)
    {
        SyncSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<SyncSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid sync settings JSON: {e.Message}", e);
        }

        if (settings is null)
            throw new FormatException("Sync settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        NonNegative(InterestDistance, "interest_distance");
        NonNegative(InterestHysteresis, "interest_hysteresis");
        NonNegative(SleepingSnapshotInterval, "sleeping_snapshot_interval");
        NonNegative(MaxExtrapolation, "max_extrapolation");
        NonNegative(FullSyncInterval, "full_sync_interval");
        if (MaxPacketBytes is < BodySnapshot.MinPacketBytes or > 65_000)
            throw new FormatException($"max_packet_bytes must be between {BodySnapshot.MinPacketBytes} and 65000 (got {MaxPacketBytes}).");
    }

    private static void NonNegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f)
            throw new FormatException($"{name} must be zero or positive (got {value}).");
    }
}
