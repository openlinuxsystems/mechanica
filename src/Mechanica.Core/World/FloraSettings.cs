using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Settings;

namespace Mechanica.Core.World;

/// <summary>What one <see cref="FoliageLevel"/> does to a kind of flora, as shares of the High level.</summary>
public sealed record FoliageLevelSettings
{
    /// <summary>Plant count, as a share of the biomes' densities.</summary>
    public float Density { get; init; } = 1f;

    /// <summary>How far plants are drawn, as a share of the kind's distance.</summary>
    public float Distance { get; init; } = 1f;

    /// <summary>Wind sway, as a share of the full sway (0 keeps plants still).</summary>
    public float Wind { get; init; } = 1f;
}

/// <summary>
/// Tuning for drawing decorative flora (<c>game/data/flora.json</c>): what each Foliage density level does, how far each
/// kind is drawn, when plants switch to their far mesh and fade out, and the wind. Engine settings: they ship with the
/// build and are not moddable; what grows where is mod data (<see cref="FloraRule"/>).
/// </summary>
public sealed record FloraSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The Low level.</summary>
    public FoliageLevelSettings Low { get; init; } = new() { Density = 0.3f, Distance = 0.55f, Wind = 0f };

    /// <summary>The Medium level.</summary>
    public FoliageLevelSettings Medium { get; init; } = new() { Density = 0.6f, Distance = 0.8f, Wind = 0.6f };

    /// <summary>The High level.</summary>
    public FoliageLevelSettings High { get; init; } = new();

    /// <summary>The Very High level.</summary>
    public FoliageLevelSettings VeryHigh { get; init; } = new() { Density = 1.6f, Distance = 1.35f };

    /// <summary>Metres grass is drawn at the High level.</summary>
    public float GrassDistance { get; init; } = 55f;

    /// <summary>Metres ground cover is drawn at the High level.</summary>
    public float GroundCoverDistance { get; init; } = 35f;

    /// <summary>Metres bushes are drawn at the High level.</summary>
    public float BushDistance { get; init; } = 140f;

    /// <summary>Share of the draw distance past which a chunk's plants use their far mesh (half the blades, coarser lumps).</summary>
    public float LodShare { get; init; } = 0.45f;

    /// <summary>Share of the draw distance, at the far end, over which plants thin out and sink away instead of popping.</summary>
    public float FadeShare { get; init; } = 0.4f;

    /// <summary>How far a grass tip sways in the wind, in metres per metre of height.</summary>
    public float WindStrength { get; init; } = 0.12f;

    /// <summary>Wind sway speed in radians per second.</summary>
    public float WindSpeed { get; init; } = 1.7f;

    /// <summary>Wind direction <c>[x, z]</c>; normalised on use.</summary>
    public List<float> WindDirection { get; init; } = [1f, 0.35f];

    /// <summary>Bushes sway this share of grass's sway.</summary>
    public float BushSway { get; init; } = 0.35f;

    /// <summary>Bushes sway at this share of grass's speed.</summary>
    public float BushSpeed { get; init; } = 0.6f;

    /// <summary>Metres round a player, bot or vehicle within which plants bend away from it.</summary>
    public float PushRadius { get; init; } = 1.1f;

    /// <summary>How far plants bend away from a pusher, in metres per metre of height at its centre.</summary>
    public float PushStrength { get; init; } = 0.55f;

    /// <summary>Seconds per frame spent building chunks' flora, nearest first.</summary>
    public float BuildBudget { get; init; } = 0.003f;

    /// <summary>The settings of a level; null for <see cref="FoliageLevel.Off"/>.</summary>
    public FoliageLevelSettings? Level(FoliageLevel level) => level switch
    {
        FoliageLevel.Low => Low,
        FoliageLevel.Medium => Medium,
        FoliageLevel.High => High,
        FoliageLevel.VeryHigh => VeryHigh,
        _ => null,
    };

    /// <summary>The densest level's density share: chunks grow this many plants, and lower levels draw a subset.</summary>
    public float MaxDensity => new[] { Low, Medium, High, VeryHigh }.Max(l => l.Density);

    /// <summary>Metres a kind of flora is drawn at the High level.</summary>
    public float Distance(FloraKind kind) => kind switch
    {
        FloraKind.Grass => GrassDistance,
        FloraKind.GroundCover => GroundCoverDistance,
        _ => BushDistance,
    };

    /// <summary>Metres a kind is drawn at a level (0 when off).</summary>
    public float Distance(FloraKind kind, FoliageLevel level) => Distance(kind) * (Level(level)?.Distance ?? 0f);

    /// <summary>
    /// The share (0..1) of a chunk's plants, grown at <see cref="MaxDensity"/>, that a level draws: plants whose
    /// <see cref="FloraInstance.Rank"/> is below it.
    /// </summary>
    public float Keep(FoliageLevel level) => Level(level) is { } l ? Math.Clamp(l.Density / MaxDensity, 0f, 1f) : 0f;

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static FloraSettings FromJson(string json)
    {
        FloraSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<FloraSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid flora settings JSON: {e.Message}", e);
        }
        if (settings is null)
            throw new FormatException("Flora settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        foreach (var (name, level) in new[] { ("low", Low), ("medium", Medium), ("high", High), ("very_high", VeryHigh) })
        {
            if (level is null)
                throw new FormatException($"{name} is missing.");
            Range(level.Density, 0f, 10f, $"{name}.density");
            Range(level.Distance, 0f, 10f, $"{name}.distance");
            Range(level.Wind, 0f, 10f, $"{name}.wind");
        }
        if (!(MaxDensity > 0f))
            throw new FormatException("at least one level needs a density above 0.");
        Range(GrassDistance, 0f, 1000f, "grass_distance");
        Range(GroundCoverDistance, 0f, 1000f, "ground_cover_distance");
        Range(BushDistance, 0f, 1000f, "bush_distance");
        Range(LodShare, 0f, 1f, "lod_share");
        Range(FadeShare, 0f, 1f, "fade_share");
        Range(WindStrength, 0f, 10f, "wind_strength");
        Range(WindSpeed, 0f, 100f, "wind_speed");
        Range(BushSway, 0f, 10f, "bush_sway");
        Range(BushSpeed, 0f, 10f, "bush_speed");
        Range(PushRadius, 0f, 100f, "push_radius");
        Range(PushStrength, 0f, 10f, "push_strength");
        Range(BuildBudget, 0f, 1f, "build_budget");
        if (WindDirection is not [var x, var z] || !float.IsFinite(x) || !float.IsFinite(z) || x * x + z * z < 1e-6f)
            throw new FormatException("wind_direction must be [x, z] and not zero.");
    }

    private static void Range(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
            throw new FormatException($"{name} must be {min} to {max} (got {value}).");
    }
}
