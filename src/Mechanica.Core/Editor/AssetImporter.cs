using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Data;

namespace Mechanica.Core.Editor;

/// <summary>
/// Copies a file from anywhere on disk into a mod folder so part data can reference it with a
/// mod-relative path (D6). Meshes go to <c>meshes/</c>, scripts to <c>scripts/</c>, textures to
/// <c>textures/</c> and sounds to <c>sounds/</c>. A <c>.gltf</c> file's external buffers and images are
/// copied with it and its references rewritten. Names are made lowercase snake_case; an existing file
/// with the same bytes is reused, a different one gets a numbered name.
/// </summary>
public static class AssetImporter
{
    /// <summary>The mod folder a kind of asset is imported into.</summary>
    public static string FolderFor(AssetKind kind) => kind switch
    {
        AssetKind.Mesh => "meshes",
        AssetKind.Script => "scripts",
        AssetKind.Texture => "textures",
        AssetKind.Sound => "sounds",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Imports <paramref name="sourceFile"/> into the mod at <paramref name="modDirectory"/> and returns the
    /// mod-relative path to write in data. A file already inside the mod folder is not copied.
    /// </summary>
    public static bool TryImport(string sourceFile, string modDirectory, AssetKind kind,
        [NotNullWhen(true)] out string? relativePath, [NotNullWhen(false)] out string? error)
    {
        relativePath = null;
        var source = Path.GetFullPath(sourceFile);
        var mod = Path.GetFullPath(modDirectory);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (!AssetPath.ExtensionsFor(kind).Contains(extension))
        {
            error = $"'{Path.GetFileName(source)}' is not a {string.Join(" or ", AssetPath.ExtensionsFor(kind))} file.";
            return false;
        }
        if (!File.Exists(source))
        {
            error = $"'{source}' does not exist.";
            return false;
        }

        var inside = Path.GetRelativePath(mod, source).Replace('\\', '/');
        if (!inside.StartsWith("../", StringComparison.Ordinal) && !Path.IsPathRooted(inside)
            && AssetPath.TryParse(inside, Path.GetFileName(mod), kind, out _, out _))
        {
            relativePath = inside;
            error = null;
            return true;
        }

        try
        {
            var folder = FolderFor(kind);
            byte[] bytes;
            if (extension == ".gltf")
            {
                if (!TryRewriteGltf(source, mod, folder, out bytes, out error))
                    return false;
            }
            else
            {
                bytes = File.ReadAllBytes(source);
            }
            relativePath = Place(mod, folder, Path.GetFileName(source), bytes);
            error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            error = $"could not import '{Path.GetFileName(source)}': {e.Message}";
            return false;
        }
    }

    /// <summary>A lowercase snake_case file name with the extension kept, like <c>My Wheel.GLB</c> → <c>my_wheel.glb</c>.</summary>
    public static string CleanFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var stem = new StringBuilder();
        foreach (var c in Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant())
            stem.Append(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) ? c : '_');
        var cleaned = string.Join('_', stem.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length == 0 || !char.IsAsciiLetterLower(cleaned[0]))
            cleaned = "asset_" + cleaned;
        return cleaned.TrimEnd('_') + extension;
    }

    // Copies into folder under a clean name; reuses an identical file, otherwise numbers the name.
    private static string Place(string mod, string folder, string fileName, byte[] bytes)
    {
        var clean = CleanFileName(fileName);
        var stem = Path.GetFileNameWithoutExtension(clean);
        var extension = Path.GetExtension(clean);
        Directory.CreateDirectory(Path.Combine(mod, folder));
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? clean : $"{stem}_{n}{extension}";
            var target = Path.Combine(mod, folder, name);
            if (File.Exists(target) && !File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
                continue;
            if (!File.Exists(target))
                File.WriteAllBytes(target, bytes);
            return $"{folder}/{name}";
        }
    }

    // Copies a .gltf's relative buffer and image files next to it and points the file at the copies.
    private static bool TryRewriteGltf(string source, string mod, string folder, out byte[] bytes, [NotNullWhen(false)] out string? error)
    {
        bytes = [];
        var root = JsonNode.Parse(File.ReadAllText(source)) as JsonObject;
        if (root is null)
        {
            error = $"'{Path.GetFileName(source)}' is not a glTF JSON file.";
            return false;
        }
        var sourceDir = Path.GetDirectoryName(source)!;
        foreach (var list in new[] { "buffers", "images" })
        {
            foreach (var entry in (root[list] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (entry["uri"]?.GetValue<string>() is not { } uri || uri.StartsWith("data:", StringComparison.Ordinal))
                    continue;
                var decoded = Uri.UnescapeDataString(uri);
                var file = Path.GetFullPath(Path.Combine(sourceDir, decoded));
                if (Path.IsPathRooted(decoded) || decoded.Contains("://") || !File.Exists(file))
                {
                    error = $"'{Path.GetFileName(source)}' refers to '{uri}', which was not found next to it.";
                    return false;
                }
                // Dependencies sit in the same folder as the .gltf, so the rewritten uri is a bare file name.
                var placed = Place(mod, folder, Path.GetFileName(file), File.ReadAllBytes(file));
                entry["uri"] = Path.GetFileName(placed);
            }
        }
        bytes = Encoding.UTF8.GetBytes(root.ToJsonString(Indented));
        error = null;
        return true;
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
