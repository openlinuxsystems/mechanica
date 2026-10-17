using System.Text.Json;
using System.Text.RegularExpressions;
using Mechanica.Core.Data;

namespace Mechanica.Core.Ui;

/// <summary>
/// One language's string table (UI spec §16 Interface, Language): <c>game/data/lang/&lt;code&gt;.json</c> in the
/// build, or the same in the player's <c>lang</c> folder, which adds the language to Options. Keys are the
/// English text; <c>{0}</c>, <c>{1}</c>, … are filled in when shown and must survive translation. A key the
/// file leaves out shows in English.
/// </summary>
/// <param name="Code">Locale code: <c>en</c>, <c>fr</c>, <c>pt-BR</c>.</param>
/// <param name="Name">The language's own name, as the dropdown lists it (<c>Français</c>).</param>
/// <param name="Strings">English key to translated text.</param>
public sealed partial record Language(string Code, string Name, IReadOnlyDictionary<string, string> Strings)
{
    /// <summary>The source language every key is written in.</summary>
    public const string English = "en";

    private sealed record LanguageData
    {
        public string? Code { get; init; }
        public string? Name { get; init; }
        public SortedDictionary<string, string>? Strings { get; init; }
    }

    /// <summary>Reads a language file; throws <see cref="FormatException"/> naming what is wrong.</summary>
    public static Language Parse(string json, string file)
    {
        LanguageData? data;
        try
        {
            data = DataJson.Read<LanguageData>(json);
        }
        catch (JsonException e)
        {
            throw new FormatException($"{file}: not valid JSON ({e.Message})", e);
        }
        if (data?.Code is not { } code || !CodePattern().IsMatch(code))
            throw new FormatException($"{file}: code must be a locale code like en, fr or pt-BR.");
        if (string.IsNullOrWhiteSpace(data.Name))
            throw new FormatException($"{file}: name is missing.");
        var strings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in data.Strings ?? [])
        {
            if (string.IsNullOrEmpty(key) || value is null)
                throw new FormatException($"{file}: every string needs a key and text.");
            if (!Placeholders(key).SetEquals(Placeholders(value)))
                throw new FormatException($"{file}: '{value}' must keep the placeholders of '{key}' ({string.Join(" ", Placeholders(key).Order())}).");
            strings[key] = value;
        }
        return new Language(code, data.Name.Trim(), strings);
    }

    /// <summary>The file form: pretty-printed, keys in order.</summary>
    public string ToJson() => JsonSerializer.Serialize(new LanguageData
    {
        Code = Code,
        Name = Name,
        Strings = new SortedDictionary<string, string>(Strings.ToDictionary(), StringComparer.Ordinal),
    }, DataJson.WriteOptions) + "\n";

    /// <summary>The translation of <paramref name="key"/>, or the key itself when this language leaves it out.</summary>
    public string this[string key] => Strings.TryGetValue(key, out var text) && text.Length > 0 ? text : key;

    /// <summary><paramref name="template"/> with <c>{0}</c>, <c>{1}</c>, … replaced by <paramref name="args"/>; other braces stay.</summary>
    public static string Format(string template, IReadOnlyList<object?> args) =>
        args.Count == 0 ? template : Slot().Replace(template, m =>
            int.TryParse(m.Groups[1].ValueSpan, out var i) && i < args.Count ? Convert.ToString(args[i], System.Globalization.CultureInfo.InvariantCulture) ?? "" : m.Value);

    /// <summary>The <c>{0}</c>-style slots in <paramref name="text"/>.</summary>
    public static HashSet<string> Placeholders(string text) => [.. Slot().Matches(text).Select(m => m.Value)];

    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Slot();
}

/// <summary>
/// The pseudo-locale (<c>qps</c>): every string accented, a third longer and bracketed, so a screen run in it
/// shows at a glance any text that skipped the string tables, and which layouts break with longer words.
/// Placeholders and BBCode tags are kept.
/// </summary>
public static partial class PseudoLocale
{
    /// <summary>Its locale code.</summary>
    public const string Code = "qps";

    /// <summary>What every pseudo string starts and ends with.</summary>
    public const char Open = '⟦', Close = '⟧';

    private static readonly Dictionary<char, char> Accents = new()
    {
        ['a'] = 'á', ['e'] = 'é', ['i'] = 'í', ['o'] = 'ó', ['u'] = 'ú', ['c'] = 'ç', ['n'] = 'ñ', ['y'] = 'ý',
        ['A'] = 'Á', ['E'] = 'É', ['I'] = 'Í', ['O'] = 'Ó', ['U'] = 'Ú', ['C'] = 'Ç', ['N'] = 'Ñ', ['Y'] = 'Ý',
    };

    /// <summary>The pseudo form of <paramref name="text"/>.</summary>
    public static string Of(string text)
    {
        var parts = Kept().Split(text);
        var kept = Kept().Matches(text);
        var result = new System.Text.StringBuilder().Append(Open);
        for (var i = 0; i < parts.Length; i++)
        {
            foreach (var c in parts[i])
                result.Append(Accents.GetValueOrDefault(c, c));
            if (i < kept.Count)
                result.Append(kept[i].Value);
        }
        var letters = text.Count(char.IsLetter);
        return result.Append('·', (letters + 2) / 3).Append(Close).ToString();
    }

    /// <summary>True when <paramref name="text"/> went through <see cref="Of"/>.</summary>
    public static bool IsPseudo(string text) => text.Contains(Open);

    /// <summary>The pseudo-locale for every key of <paramref name="english"/>.</summary>
    public static Language For(Language english) =>
        new(Code, "Pseudo", english.Strings.Keys.ToDictionary(k => k, Of, StringComparer.Ordinal));

    [GeneratedRegex(@"\{[^{}]*\}|\[[^\[\]]*\]")]
    private static partial Regex Kept();
}

/// <summary>Finds the keys code asks for: the string literals passed to <c>Loc.T(…)</c>.</summary>
public static partial class LocKeys
{
    /// <summary>The literal keys in <paramref name="source"/>, a C# file.</summary>
    public static IEnumerable<string> In(string source) =>
        Call().Matches(source).Select(m => System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value));

    [GeneratedRegex("""\bLoc\.T\(\s*"((?:[^"\\]|\\.)*)"[\s,)]""")]
    private static partial Regex Call();
}
