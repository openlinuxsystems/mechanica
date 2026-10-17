using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Items;

/// <summary>
/// Limits for inventories and crafting, in SI units. The game loads the values from JSON (balance
/// numbers live in data); the defaults here only fill fields the file leaves out.
/// </summary>
public sealed record InventorySettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>How far from a player's eyes a container or station may be used, in metres.</summary>
    public float Reach { get; init; } = 7.5f;

    /// <summary>Slots in each player's own inventory.</summary>
    public int PlayerSlots { get; init; } = 30;

    /// <summary>Most crafts one order may ask for.</summary>
    public int MaxCraftCount { get; init; } = 99;

    /// <summary>
    /// Whether <c>GiveItems</c> may make items from nothing (Creative, D13). Survival (chunk 20) turns it off.
    /// </summary>
    public bool AllowGive { get; init; } = true;

    /// <summary>Most items one <c>GiveItems</c> call may make.</summary>
    public int MaxGiveCount { get; init; } = 1000;

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static InventorySettings FromJson(string json)
    {
        InventorySettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<InventorySettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid inventory settings JSON: {e.Message}", e);
        }
        if (settings is null)
            throw new FormatException("Inventory settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> when a value is out of range.</summary>
    public void Validate()
    {
        if (!(Reach > 0) || !float.IsFinite(Reach))
            throw new FormatException($"reach must be greater than 0 (got {Reach}).");
        if (PlayerSlots is < 1 or > 256)
            throw new FormatException($"player_slots must be 1 to 256 (got {PlayerSlots}).");
        if (MaxCraftCount < 1)
            throw new FormatException($"max_craft_count must be 1 or more (got {MaxCraftCount}).");
        if (MaxGiveCount < 1)
            throw new FormatException($"max_give_count must be 1 or more (got {MaxGiveCount}).");
    }
}
