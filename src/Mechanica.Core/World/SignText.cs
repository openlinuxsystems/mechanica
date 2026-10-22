using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mechanica.Core.World;

/// <summary>
/// Words a POI's part marker writes on a sign (<see cref="PoiMarker.Settings"/>) can point the way to other places in
/// this world: <c>{distance:outpost}</c> becomes how far the nearest POI of that id is from the sign (<c>650 m</c>,
/// <c>1.4 km</c>), <c>{direction:outpost}</c> which way it lies (<c>N</c>, <c>NE</c>, … with north −Z), and
/// <c>{name:outpost}</c> its name. Several ids separated by <c>|</c> take the nearest of any of them (the three
/// Workshops); a bare id is the marker's POI's mod's. When the world has none of them the token is left as <c>?</c>.
/// </summary>
public static partial class SignText
{
    private static readonly string[] Compass = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    [GeneratedRegex(@"\{(distance|direction|name):([a-z0-9_:|]+)\}")]
    private static partial Regex Token();

    /// <summary>True when <paramref name="text"/> holds a token.</summary>
    public static bool HasTokens(string text) => Token().IsMatch(text);

    /// <summary>Every POI id a token in <paramref name="text"/> names (qualified with <paramref name="poi"/>'s mod).</summary>
    public static IEnumerable<string> IdsIn(string text, string poi) =>
        Token().Matches(text).SelectMany(m => m.Groups[2].Value.Split('|')).Select(id => PoiMarker.Qualify(id, poi));

    /// <summary><paramref name="text"/> with its tokens filled in for a sign standing at <paramref name="at"/>.</summary>
    public static string Resolve(string text, Vector3 at, string poi, IReadOnlyList<PoiInstance> pois)
    {
        if (!HasTokens(text))
            return text;
        return Token().Replace(text, m =>
        {
            var ids = m.Groups[2].Value.Split('|').Select(id => PoiMarker.Qualify(id, poi)).ToHashSet(StringComparer.Ordinal);
            var here = new Vector2(at.X, at.Z);
            var nearest = pois.Where(p => ids.Contains(p.Poi.QualifiedId)).MinBy(p => Vector2.DistanceSquared(p.Centre, here));
            if (nearest is null)
                return "?";
            var offset = nearest.Centre - here;
            return m.Groups[1].Value switch
            {
                "distance" => Distance(offset.Length()),
                "direction" => Compass[(int)MathF.Round(Bearing(offset) / 45f) % 8],
                _ => nearest.Poi.Prefab.Name ?? nearest.Poi.QualifiedId,
            };
        });
    }

    /// <summary>
    /// The settings of a sign marker with the string values' tokens filled in (<see cref="Resolve"/>), or the settings as
    /// they are when none holds a token.
    /// </summary>
    public static IReadOnlyDictionary<string, JsonElement>? Resolve(IReadOnlyDictionary<string, JsonElement>? settings, Vector3 at, string poi,
        IReadOnlyList<PoiInstance> pois)
    {
        if (settings is null || !settings.Values.Any(v => v.ValueKind == JsonValueKind.String && HasTokens(v.GetString()!)))
            return settings;
        return settings.ToDictionary(e => e.Key, e => e.Value.ValueKind == JsonValueKind.String
            ? JsonSerializer.SerializeToElement(Resolve(e.Value.GetString()!, at, poi, pois))
            : e.Value);
    }

    /// <summary>
    /// The settings with each token as long as it can come out (<c>99.9 km</c>, <c>NW</c>, a 24-character name), to
    /// check the words still fit their setting wherever the sign stands.
    /// </summary>
    public static IReadOnlyDictionary<string, JsonElement> Longest(IReadOnlyDictionary<string, JsonElement> settings) =>
        settings.ToDictionary(e => e.Key, e => e.Value.ValueKind == JsonValueKind.String && HasTokens(e.Value.GetString()!)
            ? JsonSerializer.SerializeToElement(Token().Replace(e.Value.GetString()!, m => m.Groups[1].Value switch
            {
                "distance" => "99.9 km",
                "direction" => "NW",
                _ => new string('W', MaxName),
            }))
            : e.Value);

    /// <summary>Longest POI name a <c>{name:…}</c> token is checked with.</summary>
    public const int MaxName = 24;

    // Degrees clockwise from north (-Z) seen from above, east (+X) at 90.
    private static float Bearing(Vector2 offset)
    {
        var degrees = MathF.Atan2(offset.X, -offset.Y) * 180f / MathF.PI;
        return degrees < 0 ? degrees + 360f : degrees;
    }

    // To the nearest 50 m under a kilometre, else to a tenth of a kilometre.
    private static string Distance(float metres) => metres < 975f
        ? $"{Math.Max(50, (int)MathF.Round(metres / 50f) * 50)} m"
        : $"{(metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture)} km";
}
