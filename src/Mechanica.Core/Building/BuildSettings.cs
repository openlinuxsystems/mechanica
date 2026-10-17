using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;

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

    /// <summary>The Hammer's timings and drags.</summary>
    public HammerSettings Hammer { get; init; } = new();

    /// <summary>The Lift.</summary>
    public LiftSettings Lift { get; init; } = new();

    /// <summary>The Paint Tool.</summary>
    public PaintSettings Paint { get; init; } = new();

    /// <summary>The Sledgehammer.</summary>
    public SledgeSettings Sledge { get; init; } = new();

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
        Hammer.Validate();
        Lift.Validate();
        Paint.Validate();
        Sledge.Validate();
    }

    internal static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new FormatException($"{name} must be a positive number (got {value}).");
    }
}

/// <summary>
/// The Hammer's timings (UI spec §8, §9): holding the left button still for <see cref="AlignHoldSeconds"/> enters
/// alignment mode, moving for <see cref="DragSeconds"/> while held starts a drag, and holding the right button on a
/// part removes it after <see cref="RemoveSeconds"/>, or the time its <c>sound_material</c> has in
/// <see cref="RemoveSecondsByMaterial"/>. A drag covers at most <see cref="DragMaxPerAxis"/> parts along each side.
/// </summary>
public sealed record HammerSettings
{
    // One shared default, so settings that keep it compare equal.
    private static readonly IReadOnlyDictionary<string, double> NoMaterials = new Dictionary<string, double>();

    /// <summary>Seconds the left button is held still before the ghost locks for a drag (§9: 0.15).</summary>
    public double AlignHoldSeconds { get; init; } = 0.15;

    /// <summary>Seconds of movement with a button held before it is a drag (§9: 0.1).</summary>
    public double DragSeconds { get; init; } = 0.1;

    /// <summary>Most parts a drag places or removes along each side of its rectangle (§10: 16).</summary>
    public int DragMaxPerAxis { get; init; } = 16;

    /// <summary>Seconds of holding the right button to remove a part whose material has no time of its own (§9: 0.6).</summary>
    public double RemoveSeconds { get; init; } = 0.6;

    /// <summary>Removal seconds by <c>sound_material</c> (§9: wood 0.4, metal 0.7, concrete 0.9).</summary>
    public IReadOnlyDictionary<string, double> RemoveSecondsByMaterial { get; init; } = NoMaterials;

    /// <summary>Seconds a refused placement's ghost takes to fade out red (§9).</summary>
    public double RollbackSeconds { get; init; } = 0.4;

    /// <summary>Most parts one drag sends: the whole rectangle.</summary>
    public int MaxPartsPerAction => DragMaxPerAxis * DragMaxPerAxis;

    /// <summary>Seconds to remove a part of <paramref name="material"/> (null: none named).</summary>
    public double RemoveSecondsFor(string? material) =>
        material is not null && RemoveSecondsByMaterial.TryGetValue(material, out var seconds) ? seconds : RemoveSeconds;

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        if (!double.IsFinite(AlignHoldSeconds) || AlignHoldSeconds < 0)
            throw new FormatException($"hammer.align_hold_seconds must be 0 or more (got {AlignHoldSeconds}).");
        if (!double.IsFinite(DragSeconds) || DragSeconds < 0)
            throw new FormatException($"hammer.drag_seconds must be 0 or more (got {DragSeconds}).");
        if (DragMaxPerAxis is < 1 or > 64)
            throw new FormatException($"hammer.drag_max_per_axis must be 1 to 64 (got {DragMaxPerAxis}).");
        if (!double.IsFinite(RemoveSeconds) || RemoveSeconds < 0)
            throw new FormatException($"hammer.remove_seconds must be 0 or more (got {RemoveSeconds}).");
        if (RemoveSecondsByMaterial.FirstOrDefault(p => !double.IsFinite(p.Value) || p.Value < 0) is { Key: { } bad } entry)
            throw new FormatException($"hammer.remove_seconds_by_material.{bad} must be 0 or more (got {entry.Value}).");
        if (!double.IsFinite(RollbackSeconds) || RollbackSeconds < 0)
            throw new FormatException($"hammer.rollback_seconds must be 0 or more (got {RollbackSeconds}).");
    }
}

/// <summary>
/// The Lift: a square platform a player puts on the ground, which holds one Creation still on top and
/// raises or lowers it in steps. It has no stamina cost (D12).
/// </summary>
public sealed record LiftSettings
{
    /// <summary>Width and depth of the platform, in metres.</summary>
    public float PlatformSize { get; init; } = 2f;

    /// <summary>Height of the platform's top above the ground at level 0, in metres.</summary>
    public float BaseHeight { get; init; } = 0.25f;

    /// <summary>How far one level raises the platform, in metres.</summary>
    public float LevelHeight { get; init; } = 0.25f;

    /// <summary>Highest level.</summary>
    public int MaxLevel { get; init; } = 16;

    /// <summary>Height of the platform's top above its base at <paramref name="level"/>, in metres.</summary>
    public float TopAt(int level) => BaseHeight + level * LevelHeight;

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        BuildSettings.Positive(PlatformSize, "lift.platform_size");
        BuildSettings.Positive(BaseHeight, "lift.base_height");
        BuildSettings.Positive(LevelHeight, "lift.level_height");
        if (MaxLevel < 0)
            throw new FormatException($"lift.max_level must be 0 or more (got {MaxLevel}).");
    }
}

/// <summary>The Paint Tool.</summary>
public sealed record PaintSettings
{
    // One shared default, so settings that keep it compare equal.
    private static readonly IReadOnlyList<string> DefaultPalette = ["#e8e8e8", "#3a3a3a", "#d03030", "#e8a020", "#f0d830", "#40a040", "#3070d0", "#8040b0"];

    /// <summary>Colours the tool offers, <c>"#rrggbb"</c>, in display order. The host accepts any colour.</summary>
    public IReadOnlyList<string> Palette { get; init; } = DefaultPalette;

    /// <summary>Most parts one Paint action may colour.</summary>
    public int MaxPartsPerAction { get; init; } = 256;

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        if (Palette.Count == 0)
            throw new FormatException("paint.palette needs at least one colour.");
        if (Palette.FirstOrDefault(c => !HexColor.TryParse(c, out _)) is { } bad)
            throw new FormatException($"paint.palette has \"{bad}\", which is not \"#rrggbb\".");
        if (MaxPartsPerAction < 1)
            throw new FormatException($"paint.max_parts_per_action must be at least 1 (got {MaxPartsPerAction}).");
    }
}

/// <summary>
/// The Sledgehammer. A part's health is its <c>durability</c> times <see cref="HealthPerDurability"/>
/// (D13); each hit takes <see cref="Damage"/> and pushes the Body, and a part at 0 health breaks off.
/// Parts without a durability cannot be broken. Hits on players and world objects take
/// <see cref="PlayerDamage"/> and <see cref="ObjectDamage"/> instead.
/// </summary>
public sealed record SledgeSettings
{
    /// <summary>Health one hit takes from a part.</summary>
    public float Damage { get; init; } = 10f;

    /// <summary>Health per point of a part's durability.</summary>
    public float HealthPerDurability { get; init; } = 10f;

    /// <summary>Shortest time between one player's hits, in seconds.</summary>
    public float SwingSeconds { get; init; } = 0.5f;

    /// <summary>Impulse a hit gives the Body it lands on, in N·s, along the swing.</summary>
    public float Impulse { get; init; } = 20f;

    /// <summary>Health one hit takes from a player (Survival, chunk 20).</summary>
    public float PlayerDamage { get; init; } = 15f;

    /// <summary>Health one hit takes from a tree, rock or other world object that can break (chunk 20).</summary>
    public float ObjectDamage { get; init; } = 10f;

    /// <summary>Full health of a part with <paramref name="durability"/>, or null when it cannot break.</summary>
    public float? HealthOf(int? durability) => durability is > 0 ? durability.Value * HealthPerDurability : null;

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        BuildSettings.Positive(Damage, "sledge.damage");
        BuildSettings.Positive(HealthPerDurability, "sledge.health_per_durability");
        if (!float.IsFinite(SwingSeconds) || SwingSeconds < 0f)
            throw new FormatException($"sledge.swing_seconds must be 0 or more (got {SwingSeconds}).");
        if (!float.IsFinite(Impulse) || Impulse < 0f)
            throw new FormatException($"sledge.impulse must be 0 or more (got {Impulse}).");
        BuildSettings.Positive(PlayerDamage, "sledge.player_damage");
        BuildSettings.Positive(ObjectDamage, "sledge.object_damage");
    }
}
