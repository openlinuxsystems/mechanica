namespace Mechanica.Core.Items;

/// <summary>The search fields of the inventory and Fabricator screens (UI spec §3, §8).</summary>
public static class ItemSearch
{
    /// <summary>True when every word of <paramref name="query"/> is in one of <paramref name="texts"/> (any case); an empty query matches everything.</summary>
    public static bool Matches(string? query, params IEnumerable<string?> texts)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return true;
        var text = string.Join(' ', texts.OfType<string>());
        return words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>True when <paramref name="query"/> has something to search for.</summary>
    public static bool Active(string? query) => !string.IsNullOrWhiteSpace(query);
}
