using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;

namespace Mechanica.Core.Building;

/// <summary>
/// Limits for building by hand (placing and removing parts), in SI units. The game loads the values
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

    /// <summary>
    /// How far (m) past a part's faces the world may be for the part to count as resting on it and be anchored
    /// to the ground (<see cref="IBuildWorld.RestsOnWorld"/>).
    /// </summary>
    public float AnchorMargin { get; init; } = 0.05f;

    /// <summary>Timings and drags for placing and removing parts by hand.</summary>
    public PlacementSettings Placement { get; init; } = new();

    /// <summary>The Connect Tool's nodes, lines, drag and tooltips.</summary>
    public ConnectSettings Connect { get; init; } = new();

    /// <summary>The Weld Tool.</summary>
    public WeldSettings Weld { get; init; } = new();

    /// <summary>The Lift.</summary>
    public LiftSettings Lift { get; init; } = new();

    /// <summary>The Paint Tool.</summary>
    public PaintSettings Paint { get; init; } = new();

    /// <summary>The Sledgehammer.</summary>
    public SledgeSettings Sledge { get; init; } = new();

    /// <summary>How things fall apart into loose parts (a rock cube into chunks, a chunk into rods).</summary>
    public BreakSettings Breaking { get; init; } = new();

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
        if (!float.IsFinite(AnchorMargin) || AnchorMargin is <= 0f or > 0.5f)
            throw new FormatException($"anchor_margin must be in (0, 0.5] m (got {AnchorMargin}).");
        Placement.Validate();
        Connect.Validate();
        Weld.Validate();
        Lift.Validate();
        Paint.Validate();
        Sledge.Validate();
        Breaking.Validate();
    }

    internal static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new FormatException($"{name} must be a positive number (got {value}).");
    }
}

/// <summary>
/// Timings for placing and removing parts by hand (UI spec §8, §9): holding the left button still for <see cref="AlignHoldSeconds"/> enters
/// alignment mode, moving for <see cref="DragSeconds"/> while held starts a drag, and holding the right button on a
/// part removes it after <see cref="RemoveSeconds"/>, or the time its <c>sound_material</c> has in
/// <see cref="RemoveSecondsByMaterial"/>. A drag covers at most <see cref="DragMaxPerAxis"/> parts along each side.
/// </summary>
public sealed record PlacementSettings
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
            throw new FormatException($"placement.align_hold_seconds must be 0 or more (got {AlignHoldSeconds}).");
        if (!double.IsFinite(DragSeconds) || DragSeconds < 0)
            throw new FormatException($"placement.drag_seconds must be 0 or more (got {DragSeconds}).");
        if (DragMaxPerAxis is < 1 or > 64)
            throw new FormatException($"placement.drag_max_per_axis must be 1 to 64 (got {DragMaxPerAxis}).");
        if (!double.IsFinite(RemoveSeconds) || RemoveSeconds < 0)
            throw new FormatException($"placement.remove_seconds must be 0 or more (got {RemoveSeconds}).");
        if (RemoveSecondsByMaterial.FirstOrDefault(p => !double.IsFinite(p.Value) || p.Value < 0) is { Key: { } bad } entry)
            throw new FormatException($"placement.remove_seconds_by_material.{bad} must be 0 or more (got {entry.Value}).");
        if (!double.IsFinite(RollbackSeconds) || RollbackSeconds < 0)
            throw new FormatException($"placement.rollback_seconds must be 0 or more (got {RollbackSeconds}).");
    }
}

/// <summary>
/// How connections look and feel (UI spec §9, §10, §12): node size and pulse, line thickness and chevron spacing, the
/// drag's snap radius, how faint invalid targets get, and how long links flash when a signal changes with the
/// Connect Tool put away. Sizes are in screen pixels at 1080 p.
/// </summary>
public sealed record ConnectSettings
{
    /// <summary>Diameter of a connection node, in px (§12: 12 to 16).</summary>
    public float NodePixels { get; init; } = 14f;

    /// <summary>How much a hovered node grows (§12: 20%).</summary>
    public float HoverScale { get; init; } = 1.2f;

    /// <summary>Seconds of one pulse of an active node (§12: 0.8).</summary>
    public double PulseSeconds { get; init; } = 0.8;

    /// <summary>Thickness of a connection line, in px (§12: 3).</summary>
    public float LineWidth { get; init; } = 3f;

    /// <summary>Pixels between the chevrons along a line (§12: 40).</summary>
    public float ChevronSpacing { get; init; } = 40f;

    /// <summary>Pixels between lines that join the same two parts, so both show (§12).</summary>
    public float LaneGap { get; init; } = 5f;

    /// <summary>A drag snaps to the nearest valid node this close to the aim, in m (§10: 0.5).</summary>
    public float SnapMetres { get; init; } = 0.5f;

    /// <summary>Opacity of targets a drag cannot end on (§9: 30%).</summary>
    public float InvalidAlpha { get; init; } = 0.3f;

    /// <summary>Seconds a link shows when its signal changes while the Connect Tool is put away (§12: a brief flash).</summary>
    public double FlashSeconds { get; init; } = 0.6;

    /// <summary>How far from the player connections are drawn, in m.</summary>
    public float Range { get; init; } = 40f;

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        if (!float.IsFinite(NodePixels) || NodePixels is < 4f or > 64f)
            throw new FormatException($"connect.node_pixels must be 4 to 64 (got {NodePixels}).");
        if (!float.IsFinite(HoverScale) || HoverScale is < 1f or > 3f)
            throw new FormatException($"connect.hover_scale must be 1 to 3 (got {HoverScale}).");
        foreach (var (value, name) in new[] { (LineWidth, "line_width"), (ChevronSpacing, "chevron_spacing"), (SnapMetres, "snap_metres"), (Range, "range") })
            BuildSettings.Positive(value, $"connect.{name}");
        if (!float.IsFinite(LaneGap) || LaneGap < 0f)
            throw new FormatException($"connect.lane_gap must be 0 or more (got {LaneGap}).");
        if (!float.IsFinite(InvalidAlpha) || InvalidAlpha is < 0f or > 1f)
            throw new FormatException($"connect.invalid_alpha must be 0 to 1 (got {InvalidAlpha}).");
        foreach (var (value, name) in new[] { (PulseSeconds, "pulse_seconds"), (FlashSeconds, "flash_seconds") })
        {
            if (!double.IsFinite(value) || value < 0)
                throw new FormatException($"connect.{name} must be 0 or more (got {value}).");
        }
    }
}

/// <summary>The Weld Tool (UI spec §8): how long the blue seam stays on the faces a weld joined.</summary>
public sealed record WeldSettings
{
    /// <summary>Seconds the seam highlight takes to fade after a weld.</summary>
    public double SeamSeconds { get; init; } = 1.5;

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        if (!double.IsFinite(SeamSeconds) || SeamSeconds < 0)
            throw new FormatException($"weld.seam_seconds must be 0 or more (got {SeamSeconds}).");
    }
}

/// <summary>
/// The Lift: a square platform a player puts on the ground, which holds one Creation still on top and
/// raises or lowers it in steps. It has no stamina cost (D12). Holding right click for
/// <see cref="RemoveHoldSeconds"/> takes it away. Its blueprint menu (UI spec §5, §9) lists at most
/// <see cref="MaxBlueprints"/> saves, and a blueprint sent to the host is at most <see cref="BlueprintMaxBytes"/> long.
/// </summary>
public sealed record LiftSettings
{
    /// <summary>Width and depth of the platform, in metres.</summary>
    public float PlatformSize { get; init; } = 1f;

    /// <summary>Height of the platform's top above the ground at level 0, in metres.</summary>
    public float BaseHeight { get; init; } = 0.5f;

    /// <summary>How far one level raises the platform, in metres.</summary>
    public float LevelHeight { get; init; } = 0.25f;

    /// <summary>Highest level.</summary>
    public int MaxLevel { get; init; } = 20;

    /// <summary>
    /// The Lift's model (<c>mod:path</c> to a glTF file). Its <c>deploy</c> clip plays when the Lift is put down and its
    /// <c>extend</c> clip is scrubbed by deck height (time in seconds = metres); see <c>tools/lift/build_lift.py</c>.
    /// </summary>
    public string Model { get; init; } = "vanilla:meshes/lift/lift.glb";

    /// <summary>Fastest the shown platform moves between levels, in metres per second.</summary>
    public float RaiseSpeed { get; init; } = 0.9f;

    /// <summary>How quickly the shown platform speeds up and slows down, in metres per second squared.</summary>
    public float RaiseAcceleration { get; init; } = 4f;

    /// <summary>Sound event each peer plays when the platform comes to rest at level 0 or the highest level.</summary>
    public string EndStopSound { get; init; } = "impact_metal";

    /// <summary>Seconds right click is held on the Lift before it is taken away.</summary>
    public double RemoveHoldSeconds { get; init; } = 0.5;

    /// <summary>When true, a Creation whose storage holds items cannot be picked up (UI spec §9 edge case).</summary>
    public bool RefuseStoredItems { get; init; } = true;

    /// <summary>Most blueprints the menu lists, newest first.</summary>
    public int MaxBlueprints { get; init; } = 50;

    /// <summary>Longest blueprint, in characters of JSON, that <c>LoadBlueprint</c> accepts.</summary>
    public int BlueprintMaxBytes { get; init; } = 1024 * 1024;

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
        BuildSettings.Positive(RaiseSpeed, "lift.raise_speed");
        BuildSettings.Positive(RaiseAcceleration, "lift.raise_acceleration");
        if (!double.IsFinite(RemoveHoldSeconds) || RemoveHoldSeconds < 0)
            throw new FormatException($"lift.remove_hold_seconds must be 0 or more (got {RemoveHoldSeconds}).");
        if (MaxBlueprints < 1)
            throw new FormatException($"lift.max_blueprints must be at least 1 (got {MaxBlueprints}).");
        if (BlueprintMaxBytes < 1024)
            throw new FormatException($"lift.blueprint_max_bytes must be at least 1024 (got {BlueprintMaxBytes}).");
    }
}

/// <summary>The Paint Tool.</summary>
public sealed record PaintSettings
{
    // One shared default, so settings that keep it compare equal.
    private static readonly IReadOnlyList<string> DefaultPalette = ["#e8e8e8", "#3a3a3a", "#d03030", "#e8a020", "#f0d830", "#40a040", "#3070d0", "#8040b0"];

    /// <summary>Colours the tool offers, <c>"#rrggbb"</c>, in display order. The host accepts any colour.</summary>
    public IReadOnlyList<string> Palette { get; init; } = DefaultPalette;

    /// <summary>Most parts one Paint action may colour, and one drag may cover.</summary>
    public int MaxPartsPerAction { get; init; } = 256;

    /// <summary>While dragging, how often the parts passed over are sent to the host, in seconds.</summary>
    public double DragFlushSeconds { get; init; } = 0.1;

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        if (Palette.Count == 0)
            throw new FormatException("paint.palette needs at least one colour.");
        if (Palette.FirstOrDefault(c => !HexColor.TryParse(c, out _)) is { } bad)
            throw new FormatException($"paint.palette has \"{bad}\", which is not \"#rrggbb\".");
        if (MaxPartsPerAction < 1)
            throw new FormatException($"paint.max_parts_per_action must be at least 1 (got {MaxPartsPerAction}).");
        if (!double.IsFinite(DragFlushSeconds) || DragFlushSeconds < 0)
            throw new FormatException($"paint.drag_flush_seconds must be 0 or more (got {DragFlushSeconds}).");
    }
}

/// <summary>
/// The Sledgehammer, every player's main tool (Chase's sledgehammer spec). A part's health is its <c>durability</c> times
/// <see cref="HealthPerDurability"/> (D13); each hit takes <see cref="Damage"/>. A swing takes <see cref="SwingSeconds"/>
/// and lands <see cref="StrikeSeconds"/> after the click (at it, as shipped), on what is within <see cref="Reach"/> of the head.
/// Every hit pushes what it lands on along the swing with <see cref="Impulse"/>: a Body by mass × Δv, a player or bot as
/// a body of <see cref="CharacterMass"/>, never faster than <see cref="MaxKnockbackSpeed"/>. Players and bots take
/// <see cref="CombatDamage"/>; trees, rocks and other world objects take their own <c>sledge_damage</c>, or
/// <see cref="ObjectDamage"/> when they have none. Parts without a durability cannot be broken. Holding right click in
/// Creative raises a block pose that does nothing else.
/// </summary>
public sealed record SledgeSettings
{
    /// <summary>Health one hit takes from a part.</summary>
    public float Damage { get; init; } = 10f;

    /// <summary>Health per point of a part's durability.</summary>
    public float HealthPerDurability { get; init; } = 10f;

    /// <summary>Shortest time between one player's hits, in seconds: one whole swing.</summary>
    public float SwingSeconds { get; init; } = 0.6f;

    /// <summary>Seconds from the click to the hit (0: the hit lands at the click, and the animation catches up).</summary>
    public float StrikeSeconds { get; init; }

    /// <summary>How far a swing reaches, in metres.</summary>
    public float Reach { get; init; } = 3.0f;

    /// <summary>Impulse a hit gives what it lands on, in N·s, along the swing.</summary>
    public float Impulse { get; init; } = 5000f;

    /// <summary>
    /// Fastest a hit sets anything moving, in m/s: a light Body gets less than <see cref="Impulse"/>, so a hit knocks a
    /// heavy Creation about without throwing a single block across the map.
    /// </summary>
    public float MaxKnockbackSpeed { get; init; } = 4.0f;

    /// <summary>The mass a player or bot counts as when a hit knocks it back, in kg.</summary>
    public float CharacterMass { get; init; } = 80f;

    /// <summary>Upward speed a knocked-back player or bot gets on top of the push, in m/s, so it leaves the ground.</summary>
    public float KnockbackLift { get; init; } = 2.5f;

    /// <summary>How fast a knocked-back bot slows to its own walk again, in m/s².</summary>
    public float KnockbackDrag { get; init; } = 8f;

    /// <summary>How far the swinging player's camera jolts when a hit lands, in metres (0: no shake).</summary>
    public float ShakeMetres { get; init; } = 0.03f;

    /// <summary>How long that jolt takes to settle, in seconds.</summary>
    public float ShakeSeconds { get; init; } = 0.2f;

    /// <summary>The impulse a hit gives a Body of <paramref name="mass"/> kg, in N·s.</summary>
    public float ImpulseFor(float mass) => float.IsFinite(mass) && mass > 0f ? MathF.Min(Impulse, mass * MaxKnockbackSpeed) : 0f;

    /// <summary>The speed a hit along <paramref name="direction"/> gives a player or bot, lift included, in m/s.</summary>
    public Vector3 KnockbackFor(Vector3 direction)
    {
        var flat = new Vector3(direction.X, 0f, direction.Z);
        if (!(flat.LengthSquared() > 1e-8f) || !float.IsFinite(flat.LengthSquared()))
            return Vector3.Zero;
        var speed = ImpulseFor(CharacterMass) / CharacterMass;
        return Vector3.Normalize(flat) * speed + new Vector3(0f, speed > 0f ? KnockbackLift : 0f, 0f);
    }

    /// <summary>Health one hit takes from a player or bot (combat damage).</summary>
    public float CombatDamage { get; init; } = 20f;

    /// <summary>Health one hit takes from a tree, rock or other world object that has no <c>sledge_damage</c> of its own.</summary>
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
        if (!float.IsFinite(StrikeSeconds) || StrikeSeconds < 0f || StrikeSeconds > SwingSeconds)
            throw new FormatException($"sledge.strike_seconds must be 0 to sledge.swing_seconds (got {StrikeSeconds}).");
        BuildSettings.Positive(Reach, "sledge.reach");
        if (!float.IsFinite(Impulse) || Impulse < 0f)
            throw new FormatException($"sledge.impulse must be 0 or more (got {Impulse}).");
        if (!float.IsFinite(MaxKnockbackSpeed) || MaxKnockbackSpeed < 0f)
            throw new FormatException($"sledge.max_knockback_speed must be 0 or more (got {MaxKnockbackSpeed}).");
        BuildSettings.Positive(CharacterMass, "sledge.character_mass");
        if (!float.IsFinite(KnockbackLift) || KnockbackLift < 0f)
            throw new FormatException($"sledge.knockback_lift must be 0 or more (got {KnockbackLift}).");
        BuildSettings.Positive(KnockbackDrag, "sledge.knockback_drag");
        if (!float.IsFinite(ShakeMetres) || ShakeMetres is < 0f or > 0.5f)
            throw new FormatException($"sledge.shake_metres must be 0 to 0.5 (got {ShakeMetres}).");
        BuildSettings.Positive(ShakeSeconds, "sledge.shake_seconds");
        BuildSettings.Positive(CombatDamage, "sledge.combat_damage");
        BuildSettings.Positive(ObjectDamage, "sledge.object_damage");
    }
}

/// <summary>
/// How things fall apart into loose parts (<see cref="World.BreakDefinition"/>): each piece is thrown out from the middle
/// at <see cref="SpreadSpeed"/> and up at <see cref="LiftSpeed"/>, so the pieces tumble apart rather than stack.
/// </summary>
public sealed record BreakSettings
{
    /// <summary>Outward speed each piece starts with, m/s.</summary>
    public float SpreadSpeed { get; init; } = 1.5f;

    /// <summary>Upward speed each piece starts with, m/s.</summary>
    public float LiftSpeed { get; init; } = 1.5f;

    /// <summary>Metres left between a felled tree's log segments stacked up its trunk, and under the lowest.</summary>
    public float FellGap { get; init; } = 0.05f;

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        if (!float.IsFinite(SpreadSpeed) || SpreadSpeed is < 0f or > 20f)
            throw new FormatException($"breaking.spread_speed must be 0 to 20 (got {SpreadSpeed}).");
        if (!float.IsFinite(LiftSpeed) || LiftSpeed is < 0f or > 20f)
            throw new FormatException($"breaking.lift_speed must be 0 to 20 (got {LiftSpeed}).");
        if (!float.IsFinite(FellGap) || FellGap is < 0f or > 1f)
            throw new FormatException($"breaking.fell_gap must be 0 to 1 (got {FellGap}).");
    }
}
