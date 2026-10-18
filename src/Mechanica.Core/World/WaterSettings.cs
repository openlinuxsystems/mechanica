using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Settings;

namespace Mechanica.Core.World;

/// <summary>What one Water quality level (Options, Graphics) draws.</summary>
public sealed record WaterTierSettings
{
    /// <summary>Ripple normal map layers blended together, 1 to 3.</summary>
    public int Layers { get; init; } = 2;

    /// <summary>Vertex wave height, as a share of <see cref="WaterSettings.WaveAmplitude"/> (0 keeps the surface flat).</summary>
    public float Waves { get; init; } = 1f;

    /// <summary>Shore foam, as a share of <see cref="WaterSettings.FoamIntensity"/> (0 draws none).</summary>
    public float Foam { get; init; } = 1f;

    /// <summary>
    /// Reads the depth of the scene under the water, so it darkens with depth and foams at the shore. Off, the water is
    /// one colour at one opacity, which costs least.
    /// </summary>
    public bool Depth { get; init; } = true;
}

/// <summary>
/// How lakes and the sea look and move (<c>game/data/water.json</c>): one shared surface with scrolling ripple normal
/// maps, slow low waves, a Fresnel edge, darkening with depth and soft foam where it meets the ground or a part, plus
/// the tint seen from under it. Each Water quality level (<see cref="Quality.Low"/> to <see cref="Quality.High"/>) picks
/// how many ripple layers, how much wave and foam. The surface is cosmetic and client-side only. Engine settings: they
/// ship with the build and are not moddable.
/// </summary>
public sealed record WaterSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The Low level.</summary>
    public WaterTierSettings Low { get; init; } = new() { Layers = 1, Waves = 0f, Foam = 0f, Depth = false };

    /// <summary>The Medium level.</summary>
    public WaterTierSettings Medium { get; init; } = new() { Layers = 2, Waves = 0.5f, Foam = 0.6f };

    /// <summary>The High level.</summary>
    public WaterTierSettings High { get; init; } = new() { Layers = 3 };

    /// <summary>Colour of shallow water and of the whole surface on Low, <c>#rrggbb</c>.</summary>
    public string ShallowColor { get; init; } = "#22B2DC";

    /// <summary>Colour water darkens to at <see cref="DepthFade"/> metres deep, <c>#rrggbb</c>.</summary>
    public string DeepColor { get; init; } = "#0A7DB4";

    /// <summary>Colour seen when the camera is under the surface, <c>#rrggbb</c>.</summary>
    public string UnderwaterColor { get; init; } = "#0A6E8C";

    /// <summary>Metres of water at which the colour is fully <see cref="DeepColor"/> and nearly opaque.</summary>
    public float DepthFade { get; init; } = 3f;

    /// <summary>How much of what is under shallow water shows through (0.4 to 0.85).</summary>
    public float Transparency { get; init; } = 0.65f;

    /// <summary>Ripple intensity: the ripple normal maps' strength (0.2 to 1).</summary>
    public float NormalStrength { get; init; } = 0.5f;

    /// <summary>Metres across one tile of the primary ripple layer; the others are smaller.</summary>
    public float RippleSize { get; init; } = 8f;

    /// <summary>Primary ripple layer drift, metres per second (0.05 to 0.3).</summary>
    public float ScrollPrimary { get; init; } = 0.12f;

    /// <summary>Secondary (and third) ripple layer drift, metres per second (0.03 to 0.2).</summary>
    public float ScrollSecondary { get; init; } = 0.07f;

    /// <summary>Wave height in metres at High (0.03 to 0.2).</summary>
    public float WaveAmplitude { get; init; } = 0.08f;

    /// <summary>Waves per metre (0.05 to 0.4): 0.15 is a wave every 6.7 m.</summary>
    public float WaveFrequency { get; init; } = 0.15f;

    /// <summary>How fast waves travel, metres per second.</summary>
    public float WaveSpeed { get; init; } = 0.25f;

    /// <summary>Edge foam strength (0 to 1).</summary>
    public float FoamIntensity { get; init; } = 0.4f;

    /// <summary>Metres of water depth, at the edge, over which foam fades out.</summary>
    public float FoamWidth { get; init; } = 0.35f;

    /// <summary>Surface roughness: low keeps the sun's glints small and bright.</summary>
    public float Roughness { get; init; } = 0.05f;

    /// <summary>Metres from the camera where ripples and waves start to calm, so far water stays smooth and cheap.</summary>
    public float LodStart { get; init; } = 48f;

    /// <summary>Metres from the camera past which waves are flat and ripples are at <see cref="LodRipples"/>.</summary>
    public float LodEnd { get; init; } = 112f;

    /// <summary>Share of the ripple strength left past <see cref="LodEnd"/>.</summary>
    public float LodRipples { get; init; } = 0.35f;

    /// <summary>Metres between wave vertices on the near water mesh.</summary>
    public float MeshSpacing { get; init; } = 1f;

    /// <summary>The settings of a quality level; Ultra and Custom draw High.</summary>
    public WaterTierSettings Tier(Quality quality) => quality switch
    {
        Quality.Low => Low,
        Quality.Medium => Medium,
        _ => High,
    };

    /// <summary>Parses a <c>#rrggbb</c> colour to sRGB channels from 0 to 1.</summary>
    /// <exception cref="FormatException">Not <c>#rrggbb</c>.</exception>
    public static (float R, float G, float B) ParseColor(string hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new FormatException($"'{hex}' is not a #rrggbb colour.");
        return (((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static WaterSettings FromJson(string json)
    {
        WaterSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<WaterSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid water settings JSON: {e.Message}", e);
        }
        if (settings is null)
            throw new FormatException("Water settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        foreach (var (name, tier) in new[] { ("low", Low), ("medium", Medium), ("high", High) })
        {
            if (tier is null)
                throw new FormatException($"{name} is missing.");
            if (tier.Layers is < 1 or > 3)
                throw new FormatException($"{name}.layers must be 1 to 3 (got {tier.Layers}).");
            Range(tier.Waves, 0f, 1f, $"{name}.waves");
            Range(tier.Foam, 0f, 1f, $"{name}.foam");
        }
        foreach (var (name, hex) in new[] { ("shallow_color", ShallowColor), ("deep_color", DeepColor), ("underwater_color", UnderwaterColor) })
        {
            try
            {
                ParseColor(hex);
            }
            catch (FormatException e)
            {
                throw new FormatException($"{name}: {e.Message}", e);
            }
        }
        Range(DepthFade, 0.1f, 100f, "depth_fade");
        Range(Transparency, 0.4f, 0.85f, "transparency");
        Range(NormalStrength, 0.2f, 1f, "normal_strength");
        Range(RippleSize, 0.5f, 100f, "ripple_size");
        Range(ScrollPrimary, 0.05f, 0.3f, "scroll_primary");
        Range(ScrollSecondary, 0.03f, 0.2f, "scroll_secondary");
        Range(WaveAmplitude, 0.03f, 0.2f, "wave_amplitude");
        Range(WaveFrequency, 0.05f, 0.4f, "wave_frequency");
        Range(WaveSpeed, 0f, 2f, "wave_speed");
        Range(FoamIntensity, 0f, 1f, "foam_intensity");
        Range(FoamWidth, 0.01f, 5f, "foam_width");
        Range(Roughness, 0f, 1f, "roughness");
        Range(LodStart, 0f, 1000f, "lod_start");
        Range(LodEnd, 0f, 1000f, "lod_end");
        if (LodEnd <= LodStart)
            throw new FormatException($"lod_end ({LodEnd}) must be past lod_start ({LodStart}).");
        Range(LodRipples, 0f, 1f, "lod_ripples");
        Range(MeshSpacing, 0.25f, 16f, "mesh_spacing");
    }

    private static void Range(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
            throw new FormatException($"{name} must be {min} to {max} (got {value}).");
    }
}
