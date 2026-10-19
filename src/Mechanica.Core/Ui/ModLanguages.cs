using Mechanica.Core.Data;

namespace Mechanica.Core.Ui;

/// <summary>
/// Mods' string tables: <c>lang/&lt;code&gt;.json</c> in a mod folder, the same format as the game's language files.
/// Keys are English text: the mod's own (part, item, quest and bot names, descriptions, dialogue) or the game's UI
/// text, so a mod can translate its content, the game, or both, and can add a language the game does not ship.
/// </summary>
public static class ModLanguages
{
    /// <summary>The folder in a mod that holds its language files.</summary>
    public const string Folder = "lang";

    /// <summary>
    /// The language files of <paramref name="modDirectories"/> (in load order), each folder's by file name. A file that
    /// does not read goes to <paramref name="diagnostics"/> as a warning and is left out.
    /// </summary>
    public static IReadOnlyList<Language> Read(IEnumerable<string> modDirectories, List<DataDiagnostic> diagnostics)
    {
        var languages = new List<Language>();
        foreach (var dir in modDirectories)
        {
            var folder = Path.Combine(dir, Folder);
            if (!Directory.Exists(folder))
                continue;
            foreach (var file in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
            {
                var display = $"mods/{Path.GetFileName(dir)}/{Folder}/{Path.GetFileName(file)}";
                try
                {
                    languages.Add(Language.Parse(File.ReadAllText(file), display));
                }
                catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Warning, DiagnosticCodes.FileUnreadable, display, null, null, e.Message));
                }
            }
        }
        return languages;
    }

    /// <summary>
    /// The mods' strings for <paramref name="code"/>: a regional code (<c>pt-BR</c>) takes the base language's
    /// (<c>pt</c>) first and its own over them; within each, later mods win.
    /// </summary>
    public static IReadOnlyDictionary<string, string> For(IReadOnlyList<Language> languages, string code)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        var baseCode = code.Split('-')[0];
        foreach (var layer in new[] { baseCode, code }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var language in languages.Where(l => l.Code.Equals(layer, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var (key, text) in language.Strings.Where(s => s.Value.Length > 0))
                    strings[key] = text;
            }
        }
        return strings;
    }
}
