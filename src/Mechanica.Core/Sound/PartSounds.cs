using Mechanica.Core.Data;

namespace Mechanica.Core.Sound;

/// <summary>
/// Which sound event a part plays for a moment in its life. A part's <c>sounds</c> map binds local event
/// names to sound ids; the names below are the ones the game itself plays, and a part's Lua script may
/// play any other name it binds. Without a binding, <see cref="Place"/>, <see cref="Break"/> and
/// <see cref="Impact"/> fall back to the events of the part's <c>sound_material</c>
/// (<c>place_wood</c>, <c>break_wood</c>, <c>impact_wood</c>, per CATALOG_IDS.md).
/// </summary>
public static class PartSounds
{
    /// <summary>Played where the part is placed.</summary>
    public const string Place = "place";

    /// <summary>Played where the part is taken off by hand.</summary>
    public const string Remove = "remove";

    /// <summary>Played where the part breaks (a Sledgehammer, later damage).</summary>
    public const string Break = "break";

    /// <summary>Played where the part is struck without breaking.</summary>
    public const string Impact = "impact";

    /// <summary>Played where a station begins working its crafting queue.</summary>
    public const string Start = "start";

    /// <summary>Loop on a station while it works its crafting queue.</summary>
    public const string Work = "work";

    /// <summary>Played where a station finishes a craft.</summary>
    public const string Complete = "complete";

    /// <summary>The names that fall back to the part's sound material.</summary>
    public static IReadOnlyList<string> MaterialKeys { get; } = [Place, Break, Impact];

    /// <summary>The sound event of material <paramref name="material"/> for <paramref name="key"/>: <c>place_wood</c>.</summary>
    public static string MaterialEvent(string key, string material) => $"{key}_{material}";

    /// <summary>
    /// The sound id a part plays for <paramref name="key"/>: its binding, else its material's event for a
    /// material key, else <paramref name="fallback"/>. Ids come back as data writes them; resolve them with
    /// <see cref="SoundRegistry.Find"/> and the part's mod.
    /// </summary>
    public static string? For(PartDefinition part, string key, string? fallback = null)
    {
        if (part.Sounds?.GetValueOrDefault(key) is { } bound)
            return bound;
        if (part.SoundMaterial is { } material && MaterialKeys.Contains(key))
            return MaterialEvent(key, material);
        return fallback;
    }

    /// <summary>
    /// Local event names the Sound Event Binder offers for a part of <paramref name="category"/>, besides
    /// the ones it already binds: what the game plays for every placeable part, and what scripts of that
    /// kind of part usually play.
    /// </summary>
    public static IReadOnlyList<string> SuggestedKeys(PartCategory? category) => category switch
    {
        PartCategory.Block or PartCategory.Decor => [Place, Remove, Break, Impact],
        PartCategory.Interactive => [Place, Remove, Break, Impact, "start", "loop", "stop"],
        PartCategory.Container => [Place, Remove, Break, Impact, "open", "close"],
        PartCategory.Station => [Place, Remove, Break, Impact, Start, Work, Complete],
        PartCategory.Light => [Place, Remove, Break, Impact, "on", "off"],
        PartCategory.Tool or PartCategory.Weapon => ["use", "fire", "reload"],
        PartCategory.Resource or PartCategory.Consumable => ["pickup", "drop", "use"],
        _ => [],
    };
}
