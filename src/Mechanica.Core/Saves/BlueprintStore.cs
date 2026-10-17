using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Saves;

/// <summary>A blueprint file as the Lift's blueprint menu lists it.</summary>
/// <param name="Path">Full path of the file.</param>
/// <param name="Name">The name it was saved under (the file name when the file does not read).</param>
/// <param name="SavedAt">When it was saved.</param>
/// <param name="Parts">How many parts it has.</param>
/// <param name="Thumbnail">The picture beside it, or null.</param>
/// <param name="Error">Why the file does not read, or null.</param>
public sealed record BlueprintSummary(string Path, string Name, DateTimeOffset SavedAt, int Parts, string? Thumbnail = null, string? Error = null);

/// <summary>
/// The folder of blueprints (<c>user://blueprints/</c> in the game, UI spec §5): one <c>&lt;slug&gt;.json</c> per
/// blueprint (<see cref="Blueprint"/>), named after it, with an optional <c>&lt;slug&gt;.png</c> picture beside it.
/// Saving under a name that is already there replaces it. Writes go to a temporary file first.
/// </summary>
/// <param name="directory">The folder.</param>
/// <param name="limit">Most blueprints <see cref="List"/> returns.</param>
public sealed class BlueprintStore(string directory, int limit = 50)
{
    /// <summary>The folder.</summary>
    public string Directory { get; } = directory;

    /// <summary>The newest <paramref name="limit"/> blueprints, newest first; files that do not read are listed with their error.</summary>
    public IReadOnlyList<BlueprintSummary> List()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];
        var list = new List<BlueprintSummary>();
        foreach (var path in System.IO.Directory.GetFiles(Directory, "*.json"))
        {
            var diagnostics = new List<DataDiagnostic>();
            var info = Blueprint.Peek(File.ReadAllText(path), System.IO.Path.GetFileName(path), diagnostics);
            var thumbnail = File.Exists(ThumbnailFor(path)) ? ThumbnailFor(path) : null;
            list.Add(info is null
                ? new BlueprintSummary(path, System.IO.Path.GetFileNameWithoutExtension(path), File.GetLastWriteTimeUtc(path), 0, thumbnail,
                    diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error)?.Message ?? "unreadable")
                : new BlueprintSummary(path, info.Name, info.SavedAt == DateTimeOffset.MinValue ? File.GetLastWriteTimeUtc(path) : info.SavedAt, info.Parts, thumbnail));
        }
        return [.. list.OrderByDescending(b => b.SavedAt).ThenBy(b => b.Name, StringComparer.Ordinal).Take(limit)];
    }

    /// <summary>The file a blueprint named <paramref name="name"/> saves to.</summary>
    public string PathFor(string name) => System.IO.Path.Combine(Directory, SaveStore.Slug(Blueprint.CleanName(name)) + ".json");

    /// <summary>The picture file beside the blueprint at <paramref name="path"/>.</summary>
    public static string ThumbnailFor(string path) => System.IO.Path.ChangeExtension(path, ".png");

    /// <summary>True when a blueprint named <paramref name="name"/> is already saved (saving replaces it).</summary>
    public bool Exists(string name) => File.Exists(PathFor(name));

    /// <summary>Saves <paramref name="creation"/> as <paramref name="name"/> and returns the path.</summary>
    public string Write(Creation creation, string name, DateTimeOffset savedAt)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(name);
        var temp = path + ".tmp";
        File.WriteAllText(temp, Blueprint.Write(creation, name, savedAt));
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>The text of the blueprint at <paramref name="path"/>, or null when it is not a file in this folder.</summary>
    public string? Read(string path) => InFolder(path) is { } full ? File.ReadAllText(full) : null;

    /// <summary>Deletes a blueprint and its picture. False when it is not one in this folder.</summary>
    public bool Delete(string path)
    {
        if (InFolder(path) is not { } full)
            return false;
        File.Delete(full);
        if (File.Exists(ThumbnailFor(full)))
            File.Delete(ThumbnailFor(full));
        return true;
    }

    private string? InFolder(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        return System.IO.Path.GetDirectoryName(full) == System.IO.Path.GetFullPath(Directory) && File.Exists(full) ? full : null;
    }
}
