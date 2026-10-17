namespace Mechanica.Scripting.Editor;

/// <summary>A starting point for a part script.</summary>
/// <param name="Id">File name without <c>.lua</c>.</param>
/// <param name="Title">The template's first comment line.</param>
/// <param name="Source">The script text, with <c>{{name}}</c> still in place.</param>
public sealed record LuaTemplate(string Id, string Title, string Source)
{
    /// <summary>The script for a part named <paramref name="partName"/>.</summary>
    public string For(string partName) => Source.Replace("{{name}}", partName, StringComparison.Ordinal);
}

/// <summary>
/// The Part Editor's script templates: plain <c>.lua</c> files in a folder (the game ships them in
/// <c>game/data/lua_templates/</c>). The first line is a comment naming the template; <c>{{name}}</c>
/// becomes the part's name.
/// </summary>
public static class LuaTemplates
{
    /// <summary>Default script path for a part id, relative to its mod.</summary>
    public static string PathFor(string partId) => $"scripts/{partId}.lua";

    /// <summary>Every template in <paramref name="directory"/>, by id; "empty" first.</summary>
    public static IReadOnlyList<LuaTemplate> Load(string directory) =>
        Directory.Exists(directory)
            ? From(Directory.GetFiles(directory, "*.lua").Select(f => (Path.GetFileName(f), File.ReadAllText(f))))
            : [];

    /// <summary>Templates from file names and texts (the game reads them from <c>res://</c>); "empty" first.</summary>
    public static IReadOnlyList<LuaTemplate> From(IEnumerable<(string FileName, string Text)> files) =>
        [.. files
            .Where(f => f.FileName.EndsWith(".lua", StringComparison.Ordinal))
            .Select(f =>
            {
                var source = f.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
                var id = f.FileName[..^4];
                var first = source.Split('\n', 2)[0];
                return new LuaTemplate(id, first.StartsWith("--", StringComparison.Ordinal) ? first.TrimStart('-').Trim() : id, source);
            })
            .OrderBy(t => t.Id == "empty" ? 0 : 1)
            .ThenBy(t => t.Id, StringComparer.Ordinal)];
}
