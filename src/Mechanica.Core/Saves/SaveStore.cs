using System.Text;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Saves;

/// <summary>A save file as the load list shows it.</summary>
/// <param name="Path">Full path of the file.</param>
/// <param name="Name">The world's name (the file name when the file does not read).</param>
/// <param name="SavedAt">When it was written.</param>
/// <param name="Mode">Its game mode.</param>
/// <param name="Error">Why the file does not load, or null.</param>
public sealed record SaveSummary(string Path, string Name, DateTimeOffset SavedAt, GameMode Mode, string? Error = null);

/// <summary>
/// The folder of save files (<c>user://saves/</c> in the game): one <c>&lt;slug&gt;.json</c> per world,
/// named after the world. Writes go to a temporary file first and replace the old save in one move, so a
/// crash mid-save never leaves a half-written world.
/// </summary>
public sealed class SaveStore(string directory)
{
    /// <summary>The folder.</summary>
    public string Directory { get; } = directory;

    /// <summary>Every save in the folder, newest first; files that do not read are listed with their error.</summary>
    public IReadOnlyList<SaveSummary> List()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];
        var list = new List<SaveSummary>();
        foreach (var path in System.IO.Directory.GetFiles(Directory, "*.json"))
        {
            var diagnostics = new List<DataDiagnostic>();
            var save = Read(path, diagnostics);
            list.Add(save is null
                ? new SaveSummary(path, System.IO.Path.GetFileNameWithoutExtension(path), File.GetLastWriteTimeUtc(path), GameMode.Survival,
                    diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error)?.Message ?? "unreadable")
                : new SaveSummary(path, save.Name, save.SavedAt, save.Mode));
        }
        return [.. list.OrderByDescending(s => s.SavedAt).ThenBy(s => s.Name, StringComparer.Ordinal)];
    }

    /// <summary>The file a world named <paramref name="name"/> saves to.</summary>
    public string PathFor(string name) => System.IO.Path.Combine(Directory, Slug(name) + ".json");

    /// <summary>Writes <paramref name="save"/> to <see cref="PathFor"/> its name and returns the path.</summary>
    public string Write(SaveGame save)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(save.Name);
        var temp = path + ".tmp";
        DataJson.WriteFile(temp, save.Write());
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>Reads the save at <paramref name="path"/>; null (with diagnostics) when it is missing or bad.</summary>
    public static SaveGame? Read(string path, List<DataDiagnostic> diagnostics)
    {
        var file = System.IO.Path.GetFileName(path);
        if (!File.Exists(path))
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.SaveInvalid, file, null, null, "The save file does not exist."));
            return null;
        }
        return SaveGame.Read(File.ReadAllText(path), file, diagnostics);
    }

    /// <summary>Deletes a save file in this folder. False when it is not one.</summary>
    public bool Delete(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (System.IO.Path.GetDirectoryName(full) != System.IO.Path.GetFullPath(Directory) || !File.Exists(full))
            return false;
        File.Delete(full);
        return true;
    }

    /// <summary>
    /// A file name for a world name: lowercase letters, digits and single dashes, at most 48 characters;
    /// "world" when nothing is left.
    /// </summary>
    public static string Slug(string name)
    {
        var slug = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
            if (slug.Length >= 48)
                break;
        }
        var text = slug.ToString().Trim('-');
        return text.Length == 0 ? "world" : text;
    }
}
