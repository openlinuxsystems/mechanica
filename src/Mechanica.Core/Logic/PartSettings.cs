using System.Text.Json;
using Mechanica.Core.Data;

namespace Mechanica.Core.Logic;

/// <summary>
/// The values of a placed part's <see cref="PartDefinition.Settings"/>: checking a value a player picked, and
/// filling in defaults. A placed part stores only what was changed (<see cref="Creations.PartInstance.Settings"/>).
/// </summary>
public static class PartSettings
{
    /// <summary>The definition of setting <paramref name="id"/>, or null when the part has none by that id.</summary>
    public static SettingDefinition? Find(PartDefinition part, string id) =>
        part.Settings?.FirstOrDefault(s => s.Id == id);

    /// <summary>The value a part starts with (see <see cref="SettingDefinition.Default"/>).</summary>
    public static JsonElement DefaultOf(SettingDefinition setting) => setting.Default ?? setting.Type switch
    {
        SettingType.Number => Element(setting.Min ?? 0f),
        SettingType.Choice => Element(setting.Options?.FirstOrDefault() ?? ""),
        SettingType.Toggle => Element(false),
        _ => Element(Array.Empty<float>()),
    };

    /// <summary>Null when <paramref name="value"/> fits <paramref name="setting"/>, or why it does not.</summary>
    public static string? Check(SettingDefinition setting, JsonElement value)
    {
        var name = setting.Name ?? setting.Id;
        switch (setting.Type)
        {
            case SettingType.Number:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number) || !float.IsFinite(number))
                    return $"{name} must be a number.";
                return InRange(setting, number) ? null : $"{name} must be between {setting.Min:0.###} and {setting.Max:0.###}.";
            case SettingType.Choice:
                return value.ValueKind == JsonValueKind.String && setting.Options?.Contains(value.GetString()!) == true
                    ? null
                    : $"{name} must be one of {string.Join(", ", setting.Options ?? [])}.";
            case SettingType.Toggle:
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : $"{name} must be true or false.";
            case SettingType.NumberList:
                if (value.ValueKind != JsonValueKind.Array)
                    return $"{name} must be a list of numbers.";
                if (value.GetArrayLength() > (setting.MaxCount ?? 0))
                    return $"{name} holds at most {setting.MaxCount} numbers.";
                foreach (var entry in value.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Number || !entry.TryGetSingle(out var n) || !float.IsFinite(n) || !InRange(setting, n))
                        return $"each of {name} must be a number between {setting.Min:0.###} and {setting.Max:0.###}.";
                }
                return null;
            default:
                return $"{name} has no type.";
        }
    }

    /// <summary>
    /// Null when every entry of <paramref name="values"/> names a setting of <paramref name="part"/> and fits it,
    /// or the first problem.
    /// </summary>
    public static string? Check(PartDefinition part, IReadOnlyDictionary<string, JsonElement> values)
    {
        foreach (var (id, value) in values)
        {
            if (Find(part, id) is not { } setting)
                return $"'{part.Name ?? part.Id}' has no setting '{id}'.";
            if (Check(setting, value) is { } error)
                return error;
        }
        return null;
    }

    /// <summary>The value of setting <paramref name="id"/>: the stored one, else its default. Null when the part has no such setting.</summary>
    public static JsonElement? Value(PartDefinition part, IReadOnlyDictionary<string, JsonElement>? stored, string id)
    {
        if (Find(part, id) is not { } setting)
            return null;
        return stored is not null && stored.TryGetValue(id, out var value) ? value : DefaultOf(setting);
    }

    /// <summary>Every setting's value (stored or default), in the definition's order.</summary>
    public static IReadOnlyList<(string Id, JsonElement Value)> All(PartDefinition part, IReadOnlyDictionary<string, JsonElement>? stored) =>
        [.. (part.Settings ?? []).Select(s => (s.Id!, stored is not null && stored.TryGetValue(s.Id!, out var v) ? v : DefaultOf(s)))];

    /// <summary>
    /// <paramref name="stored"/> with <paramref name="changes"/> applied: a value sets a setting, null puts it
    /// back to its default. Values equal to the default are dropped, so an untouched part stores nothing; the
    /// result is null when nothing is left. Keys come out sorted.
    /// </summary>
    public static IReadOnlyDictionary<string, JsonElement>? Apply(PartDefinition part, IReadOnlyDictionary<string, JsonElement>? stored,
        IReadOnlyDictionary<string, JsonElement?> changes)
    {
        var result = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (id, value) in stored ?? new Dictionary<string, JsonElement>())
            result[id] = value;
        foreach (var (id, value) in changes)
        {
            if (value is not { } set || Find(part, id) is not { } setting || JsonElement.DeepEquals(set, DefaultOf(setting)))
                result.Remove(id);
            else
                result[id] = set.Clone();
        }
        return result.Count == 0 ? null : result;
    }

    private static bool InRange(SettingDefinition setting, float value) =>
        value >= (setting.Min ?? float.NegativeInfinity) && value <= (setting.Max ?? float.PositiveInfinity);

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value);
}
