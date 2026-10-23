using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.World;

/// <summary>
/// The light and air at one hour of the day (<see cref="SkySettings.Keys"/>). Between two keys every value blends
/// linearly, so a day is a strip of colours the way a painter would lay it out: golden mornings, a clear noon, orange
/// evenings and deep blue nights. Colours are <c>#rrggbb</c> (sRGB).
/// </summary>
public sealed record SkyKey
{
    /// <summary>The hour this key holds, 0 (midnight) up to 24.</summary>
    public float Hour { get; init; }

    /// <summary>Colour of the sky straight up.</summary>
    public string SkyTop { get; init; } = "#4f84c4";

    /// <summary>Colour of the sky at the horizon; distant land fades into it (<see cref="SkySettings.HazeStart"/>).</summary>
    public string SkyHorizon { get; init; } = "#b7cde0";

    /// <summary>Colour of the light from the sun (by day) or the moon (by night).</summary>
    public string Light { get; init; } = "#fff2dc";

    /// <summary>Strength of that light (0 to 3).</summary>
    public float LightEnergy { get; init; } = 1f;

    /// <summary>Colour of the light that fills shadows (from the sky all round).</summary>
    public string Ambient { get; init; } = "#9fb4cc";

    /// <summary>Strength of that fill light (0 to 2).</summary>
    public float AmbientEnergy { get; init; } = 0.6f;

    /// <summary>How visible the stars are (0 to 1).</summary>
    public float Stars { get; init; }

    /// <summary>Brightness of the picture as a whole: 1 leaves it, below 1 darkens it (0.2 to 2).</summary>
    public float Exposure { get; init; } = 1f;
}

/// <summary>The blended light and air at one moment: <see cref="SkySettings.At"/>. Colours are sRGB, 0 to 1.</summary>
public readonly record struct SkyState(
    (float R, float G, float B) SkyTop,
    (float R, float G, float B) SkyHorizon,
    (float R, float G, float B) Light,
    float LightEnergy,
    (float R, float G, float B) Ambient,
    float AmbientEnergy,
    float Stars,
    float Exposure,
    System.Numerics.Vector3 LightDirection,
    bool Moon);

/// <summary>
/// The day and night cycle (<c>game/data/sky.json</c>): colours for the sky, haze, sunlight and shadow fill at keyed
/// hours, how the sun and moon cross the sky, how far off the haze starts and the colour grade. The world's clock (the
/// host's, shared with every client) picks the hour; everything here is cosmetic and drawn on each client. Engine
/// settings: they ship with the build and are not moddable.
/// </summary>
public sealed record SkySettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The keyed hours, in order; the last blends round into the first across midnight. At least two.</summary>
    public IReadOnlyList<SkyKey> Keys { get; init; } = [new() { Hour = 0 }, new() { Hour = 12 }];

    /// <summary>The hour the sun rises; it sets at <see cref="Sunset"/> and the moon has the sky in between.</summary>
    public float Sunrise { get; init; } = 6f;

    /// <summary>The hour the sun sets.</summary>
    public float Sunset { get; init; } = 18f;

    /// <summary>How high the sun stands at noon, in degrees above the horizon (10 to 90).</summary>
    public float NoonElevation { get; init; } = 62f;

    /// <summary>How high the moon stands at midnight, in degrees.</summary>
    public float MidnightElevation { get; init; } = 48f;

    /// <summary>Compass bearing the sun rises at, in degrees clockwise from north (−z); it sets opposite.</summary>
    public float SunriseBearing { get; init; } = 75f;

    /// <summary>
    /// Degrees above the horizon below which the light fades out, so the sun never shines from under the ground; the
    /// moon takes over past the horizon. The light's colour and strength still come from the keys.
    /// </summary>
    public float HorizonFade { get; init; } = 6f;

    /// <summary>
    /// Metres from the camera where haze starts. Past it land fades into <see cref="SkyKey.SkyHorizon"/> until it is gone
    /// at the view distance, so hills a few hundred metres off turn blue.
    /// </summary>
    public float HazeStart { get; init; } = 60f;

    /// <summary>
    /// How fast the haze thickens with distance: 1 rises evenly, below 1 thickens sooner, above 1 holds back (0.3 to 3).
    /// </summary>
    public float HazeCurve { get; init; } = 1f;

    /// <summary>The colour grade over the whole picture.</summary>
    public SkyGrade Grade { get; init; } = new();

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static SkySettings FromJson(string json)
    {
        SkySettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<SkySettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid sky settings JSON: {e.Message}", e);
        }
        if (settings is null)
            throw new FormatException("Sky settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first bad value.</summary>
    public void Validate()
    {
        if (Keys is not { Count: >= 2 })
            throw new FormatException("keys needs at least two hours.");
        for (var i = 0; i < Keys.Count; i++)
        {
            var key = Keys[i] ?? throw new FormatException($"keys[{i}] is null.");
            var at = $"keys[{i}]";
            Range(key.Hour, 0f, 24f, $"{at}.hour");
            if (i > 0 && key.Hour <= Keys[i - 1].Hour)
                throw new FormatException($"{at}.hour ({key.Hour}) must come after the hour before it ({Keys[i - 1].Hour}).");
            foreach (var (name, hex) in new[] { ("sky_top", key.SkyTop), ("sky_horizon", key.SkyHorizon), ("light", key.Light), ("ambient", key.Ambient) })
                Color(hex, $"{at}.{name}");
            Range(key.LightEnergy, 0f, 3f, $"{at}.light_energy");
            Range(key.AmbientEnergy, 0f, 2f, $"{at}.ambient_energy");
            Range(key.Stars, 0f, 1f, $"{at}.stars");
            Range(key.Exposure, 0.2f, 2f, $"{at}.exposure");
        }
        if (Keys[^1].Hour - Keys[0].Hour >= 24f)
            throw new FormatException("the first and last keys must be less than 24 hours apart.");
        Range(Sunrise, 0f, 24f, "sunrise");
        Range(Sunset, 0f, 24f, "sunset");
        if (Sunset <= Sunrise)
            throw new FormatException($"sunset ({Sunset}) must come after sunrise ({Sunrise}).");
        Range(NoonElevation, 10f, 90f, "noon_elevation");
        Range(MidnightElevation, 10f, 90f, "midnight_elevation");
        Range(SunriseBearing, 0f, 360f, "sunrise_bearing");
        Range(HorizonFade, 0f, 30f, "horizon_fade");
        Range(HazeStart, 0f, 2000f, "haze_start");
        Range(HazeCurve, 0.3f, 3f, "haze_curve");
        if (Grade is null)
            throw new FormatException("grade is missing.");
        Range(Grade.Contrast, 0.5f, 1.5f, "grade.contrast");
        Range(Grade.Saturation, 0.5f, 1.5f, "grade.saturation");
        Range(Grade.Brightness, 0.5f, 1.5f, "grade.brightness");
        Range(Grade.WhitePoint, 1f, 16f, "grade.white_point");
        if (Grade.Tonemap is not ("filmic" or "aces" or "agx"))
            throw new FormatException($"grade.tonemap must be filmic, aces or agx (got '{Grade.Tonemap}').");
    }

    /// <summary>The light and air at <paramref name="hour"/> (0 to 24; wraps).</summary>
    public SkyState At(double hour)
    {
        var h = (float)(((hour % 24) + 24) % 24);
        // The key at or before the hour and the one after it, wrapping round midnight.
        var next = 0;
        while (next < Keys.Count && Keys[next].Hour <= h)
            next++;
        var a = Keys[(next - 1 + Keys.Count) % Keys.Count];
        var b = Keys[next % Keys.Count];
        var span = b.Hour - a.Hour;
        if (span <= 0)
            span += 24f;
        var into = h - a.Hour;
        if (into < 0)
            into += 24f;
        var t = Math.Clamp(into / span, 0f, 1f);
        var (direction, moon, height) = LightAt(h);
        var fade = HorizonFade <= 0 ? 1f : Math.Clamp(height / HorizonFade, 0f, 1f);
        return new SkyState(
            Mix(a.SkyTop, b.SkyTop, t),
            Mix(a.SkyHorizon, b.SkyHorizon, t),
            Mix(a.Light, b.Light, t),
            Lerp(a.LightEnergy, b.LightEnergy, t) * fade,
            Mix(a.Ambient, b.Ambient, t),
            Lerp(a.AmbientEnergy, b.AmbientEnergy, t),
            Lerp(a.Stars, b.Stars, t),
            Lerp(a.Exposure, b.Exposure, t),
            direction,
            moon);
    }

    /// <summary>
    /// Where the light comes from at <paramref name="hour"/>: a unit vector pointing from the sun (or moon) toward the
    /// world (y down when it is overhead), whether it is the moon, and its height above the horizon in degrees.
    /// </summary>
    public (System.Numerics.Vector3 Direction, bool Moon, float Elevation) LightAt(double hour)
    {
        var h = (float)(((hour % 24) + 24) % 24);
        var day = Sunset - Sunrise;
        var night = 24f - day;
        bool moon;
        float along;
        if (h >= Sunrise && h <= Sunset)
        {
            moon = false;
            along = (h - Sunrise) / day;
        }
        else
        {
            moon = true;
            var since = h > Sunset ? h - Sunset : h + 24f - Sunset;
            along = since / night;
        }
        // Up and over in an arc: 0 at the eastern horizon, 1 at the western one.
        var peak = (moon ? MidnightElevation : NoonElevation) * MathF.PI / 180f;
        var elevation = MathF.Sin(along * MathF.PI) * peak;
        var bearing = (SunriseBearing + along * 180f) * MathF.PI / 180f;
        // Bearing clockwise from north (−z): north is (0, 0, −1), east is (1, 0, 0).
        var toward = new System.Numerics.Vector3(MathF.Sin(bearing) * MathF.Cos(elevation), MathF.Sin(elevation), -MathF.Cos(bearing) * MathF.Cos(elevation));
        return (-toward, moon, elevation * 180f / MathF.PI);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static (float R, float G, float B) Mix(string a, string b, float t)
    {
        var x = WaterSettings.ParseColor(a);
        var y = WaterSettings.ParseColor(b);
        return (Lerp(x.R, y.R, t), Lerp(x.G, y.G, t), Lerp(x.B, y.B, t));
    }

    private static void Color(string hex, string name)
    {
        try
        {
            WaterSettings.ParseColor(hex);
        }
        catch (FormatException e)
        {
            throw new FormatException($"{name}: {e.Message}", e);
        }
    }

    private static void Range(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
            throw new FormatException($"{name} must be {min} to {max} (got {value}).");
    }
}

/// <summary>The colour grade over the whole picture (<see cref="SkySettings.Grade"/>).</summary>
public sealed record SkyGrade
{
    /// <summary>The tone curve: <c>filmic</c>, <c>aces</c> or <c>agx</c>.</summary>
    public string Tonemap { get; init; } = "filmic";

    /// <summary>The brightest scene value the curve keeps detail in (1 to 16).</summary>
    public float WhitePoint { get; init; } = 6f;

    /// <summary>Contrast (0.5 to 1.5, 1 leaves it).</summary>
    public float Contrast { get; init; } = 1f;

    /// <summary>Saturation (0.5 to 1.5, 1 leaves it): below 1 calms neon greens.</summary>
    public float Saturation { get; init; } = 1f;

    /// <summary>Brightness (0.5 to 1.5, 1 leaves it).</summary>
    public float Brightness { get; init; } = 1f;
}
