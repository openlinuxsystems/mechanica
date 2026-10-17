using System.Text;
using Mechanica.Core.Data;

namespace Mechanica.Core.ModTools;

/// <summary>One file in a mod folder, as <see cref="ModFiles.List"/> reports it.</summary>
/// <param name="Path">Forward-slash path inside the mod.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Modified">Last write time, UTC, ISO 8601.</param>
public sealed record ModFileInfo(string Path, long Size, string Modified);

/// <summary>A file's content: text for text formats, base64 for the rest.</summary>
/// <param name="Path">Forward-slash path inside the mod.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Text">UTF-8 content of a text file (<see cref="ModFiles.IsText"/>), else null.</param>
/// <param name="Base64">Base64 content of a binary file, else null.</param>
public sealed record ModFileContent(string Path, long Size, string? Text, string? Base64);

/// <summary>
/// Sandboxed file access inside the mod folders of one <c>mods/</c> root, for the mod tools an AI drives.
/// Paths are relative to a mod folder and checked like data paths (D6): no absolute paths, <c>..</c>,
/// backslashes, hidden names or symbolic links, and only the file types a mod holds. Vanilla is read-only
/// outside <c>editor/</c>: its content is authored through the batch files there.
/// </summary>
public sealed class ModFiles(string modsRoot)
{
    /// <summary>Largest file read or written, in bytes (its base64 still fits one endpoint message).</summary>
    public const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>Most files <see cref="List"/> returns.</summary>
    public const int MaxListed = 5000;

    /// <summary>Folder of vanilla that stays writable: the Part Editor and Bot Behavior Editor batches.</summary>
    public const string VanillaEditorFolder = "editor";

    private static readonly string[] TextExtensions = [".json", ".lua", ".gltf", ".md", ".txt"];
    private static readonly string[] BinaryExtensions = [".glb", ".bin", ".png", ".ogg", ".wav"];

    /// <summary>The <c>mods/</c> folder.</summary>
    public string ModsRoot { get; } = Path.GetFullPath(modsRoot);

    /// <summary>File types a mod may hold, text first.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [.. TextExtensions, .. BinaryExtensions];

    /// <summary>True for files read and written as UTF-8 text.</summary>
    public static bool IsText(string path) => TextExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    /// <summary>The folder of an existing mod. Throws when the id is invalid, the folder is missing or a link.</summary>
    public string ModDirectory(string modId)
    {
        if (!Identifiers.IsValid(modId))
            throw new ModToolException($"'{modId}' is not a valid mod id (lowercase letters, digits and _).");
        var dir = Path.Combine(ModsRoot, modId);
        if (!Directory.Exists(dir))
            throw new ModToolException($"There is no mod folder '{modId}'. Create it with mods.create.");
        if (new DirectoryInfo(dir).LinkTarget is not null)
            throw new ModToolException($"The mod folder '{modId}' is a link; mod tools only work in real folders.");
        return dir;
    }

    /// <summary>
    /// Every file under <paramref name="folder"/> (the whole mod when null), by path. Links and hidden names are skipped.
    /// </summary>
    public IReadOnlyList<ModFileInfo> List(string modId, string? folder = null)
    {
        var root = ModDirectory(modId);
        var start = folder is null or "" ? root : Resolve(root, folder, file: false);
        if (!Directory.Exists(start))
            return [];
        var files = new List<ModFileInfo>();
        var pending = new Stack<string>([start]);
        while (pending.TryPop(out var dir))
        {
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if (entry.Name.StartsWith('.') || entry.LinkTarget is not null)
                    continue;
                if (entry is DirectoryInfo sub)
                    pending.Push(sub.FullName);
                else if (entry is FileInfo file)
                    files.Add(new ModFileInfo(Relative(root, file.FullName), file.Length, file.LastWriteTimeUtc.ToString("O")));
            }
            if (files.Count > MaxListed)
                throw new ModToolException($"More than {MaxListed} files; list a folder instead.");
        }
        return [.. files.OrderBy(f => f.Path, StringComparer.Ordinal)];
    }

    /// <summary>Reads a file: text for text formats, base64 for the rest.</summary>
    public ModFileContent Read(string modId, string path)
    {
        var full = Resolve(ModDirectory(modId), path, file: true);
        if (!File.Exists(full))
            throw new ModToolException($"{modId}/{path} does not exist.");
        var info = new FileInfo(full);
        if (info.Length > MaxFileBytes)
            throw new ModToolException($"{modId}/{path} is larger than {MaxFileBytes} bytes.");
        var bytes = File.ReadAllBytes(full);
        return IsText(path)
            ? new ModFileContent(path, bytes.Length, new UTF8Encoding(false).GetString(bytes), null)
            : new ModFileContent(path, bytes.Length, null, Convert.ToBase64String(bytes));
    }

    /// <summary>
    /// Writes a file, creating its folders. Text formats take <paramref name="text"/>, the rest
    /// <paramref name="base64"/>; exactly one must be given. Returns the bytes written.
    /// </summary>
    public long Write(string modId, string path, string? text, string? base64)
    {
        var root = ModDirectory(modId);
        var full = Resolve(root, path, file: true);
        CheckWritable(modId, path);
        if ((text is null) == (base64 is null))
            throw new ModToolException("Give exactly one of text and base64.");
        if (IsText(path) != (text is not null))
            throw new ModToolException(IsText(path) ? $"{path} is a text file; send it as text." : $"{path} is a binary file; send it as base64.");
        byte[] bytes;
        if (text is not null)
            bytes = new UTF8Encoding(false).GetBytes(text);
        else
        {
            try
            {
                bytes = Convert.FromBase64String(base64!);
            }
            catch (FormatException)
            {
                throw new ModToolException("base64 is not valid base64.");
            }
        }
        if (bytes.Length > MaxFileBytes)
            throw new ModToolException($"Files are limited to {MaxFileBytes} bytes.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return bytes.Length;
    }

    /// <summary>Deletes a file, then any folders it leaves empty (never the mod folder itself).</summary>
    public void Delete(string modId, string path)
    {
        var root = ModDirectory(modId);
        var full = Resolve(root, path, file: true);
        CheckWritable(modId, path);
        if (!File.Exists(full))
            throw new ModToolException($"{modId}/{path} does not exist.");
        if (path == ModLoader.ManifestFileName)
            throw new ModToolException("mod.json can't be deleted; it is what makes the folder a mod.");
        File.Delete(full);
        for (var dir = Path.GetDirectoryName(full)!; dir != root && !Directory.EnumerateFileSystemEntries(dir).Any(); dir = Path.GetDirectoryName(dir)!)
            Directory.Delete(dir);
    }

    /// <summary>
    /// The absolute path of <paramref name="path"/> inside <paramref name="modDirectory"/>, after the sandbox
    /// checks. A <paramref name="file"/> path must have one of the <see cref="Extensions"/>.
    /// </summary>
    public static string Resolve(string modDirectory, string path, bool file)
    {
        if (string.IsNullOrEmpty(path))
            throw new ModToolException("path is empty.");
        if (path.Contains('\\'))
            throw new ModToolException($"'{path}' uses backslashes; use forward slashes.");
        if (path.StartsWith('/') || path.StartsWith('~') || path.Contains(':') || Path.IsPathRooted(path))
            throw new ModToolException($"'{path}' is not relative to the mod folder.");
        var segments = path.Split('/');
        if (segments.Any(s => s is "" or "." or ".." || s.StartsWith('.') || s.Any(char.IsControl)))
            throw new ModToolException($"'{path}' has an empty, '.', '..' or hidden segment; paths stay inside the mod folder.");
        if (file && !Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            throw new ModToolException($"'{path}' is not a mod file type ({string.Join(", ", Extensions)}).");
        var root = Path.GetFullPath(modDirectory);
        var full = Path.GetFullPath(Path.Combine(root, path));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ModToolException($"'{path}' leaves the mod folder.");
        // No link anywhere on the way, so the real file is inside the mod folder too.
        for (var at = full; at != root; at = Path.GetDirectoryName(at)!)
        {
            FileSystemInfo entry = Directory.Exists(at) ? new DirectoryInfo(at) : new FileInfo(at);
            if (entry.Exists && entry.LinkTarget is not null)
                throw new ModToolException($"'{path}' goes through a link; mod tools only work in real folders.");
        }
        return full;
    }

    private static void CheckWritable(string modId, string path)
    {
        if (modId == Identifiers.VanillaModId && !path.StartsWith(VanillaEditorFolder + "/", StringComparison.Ordinal))
            throw new ModToolException(
                "Vanilla is authored through its editor batches: edit a batch under vanilla/editor/batches/ and run it again.");
    }

    private static string Relative(string root, string full) => Path.GetRelativePath(root, full).Replace('\\', '/');
}
