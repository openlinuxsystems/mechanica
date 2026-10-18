using System.Text.Json;
using Mechanica.Core.Data;

namespace Mechanica.Core.Items;

/// <summary>
/// The recipes the local player has seen at each kind of station, for the Fabricator's "NEW" tags (UI spec §8): a
/// recipe the station runs that was not there the last time one of its kind was opened is new until the window closes.
/// The first opening of a kind tags nothing, so a fresh save doesn't call every recipe new. Saved per player as JSON.
/// </summary>
public sealed class SeenRecipes
{
    private readonly SortedDictionary<string, SortedSet<string>> _seen = new(StringComparer.Ordinal);

    /// <summary>
    /// Records that <paramref name="station"/> (a part id) was opened listing <paramref name="recipes"/> (qualified ids)
    /// and returns the ones it hadn't listed before.
    /// </summary>
    public IReadOnlySet<string> Open(string station, IEnumerable<string> recipes)
    {
        var listed = recipes.ToList();
        if (!_seen.TryGetValue(station, out var seen))
        {
            _seen[station] = new SortedSet<string>(listed, StringComparer.Ordinal);
            return new HashSet<string>();
        }
        var fresh = listed.Where(r => !seen.Contains(r)).ToHashSet(StringComparer.Ordinal);
        seen.UnionWith(fresh);
        return fresh;
    }

    /// <summary>The stations recorded, with the recipes seen at each.</summary>
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Stations =>
        _seen.ToDictionary(p => p.Key, p => (IReadOnlyCollection<string>)p.Value, StringComparer.Ordinal);

    /// <summary>Pretty-printed JSON with stable key order.</summary>
    public string ToJson() => JsonSerializer.Serialize(_seen.ToDictionary(p => p.Key, p => p.Value.ToList()), DataJson.WriteOptions) + "\n";

    /// <summary>Reads what <see cref="ToJson"/> wrote; bad or missing data gives an empty record.</summary>
    public static SeenRecipes Parse(string? json)
    {
        var result = new SeenRecipes();
        if (string.IsNullOrWhiteSpace(json))
            return result;
        try
        {
            foreach (var (station, recipes) in DataJson.Read<Dictionary<string, List<string>>>(json) ?? [])
                result._seen[station] = new SortedSet<string>(recipes, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // A damaged file only costs the NEW tags.
        }
        return result;
    }
}
