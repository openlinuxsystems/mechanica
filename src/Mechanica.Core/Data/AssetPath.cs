using System.Diagnostics.CodeAnalysis;

namespace Mechanica.Core.Data;

/// <summary>The kinds of asset a mod can reference, each with its allowed file extensions (D6).</summary>
public enum AssetKind
{
    /// <summary>glTF meshes: <c>.glb</c>, <c>.gltf</c>.</summary>
    Mesh,

    /// <summary>Lua scripts: <c>.lua</c>.</summary>
    Script,

    /// <summary>Textures and icons: <c>.png</c>.</summary>
    Texture,

    /// <summary>Audio: <c>.ogg</c>, <c>.wav</c>.</summary>
    Sound,
}

/// <summary>
/// A sandboxed reference to a file in a mod: <c>meshes/bearing.glb</c> (this mod) or
/// <c>other_mod:meshes/x.glb</c> (another mod). Parsing rejects absolute paths, <c>..</c>,
/// backslashes, URLs/<c>res://</c> and wrong extensions, so a resolved path can never leave its mod folder.
/// </summary>
/// <param name="ModId">Mod the file belongs to.</param>
/// <param name="RelativePath">Forward-slash path inside that mod's folder.</param>
public sealed record AssetPath(string ModId, string RelativePath)
{
    private static readonly Dictionary<AssetKind, string[]> Extensions = new()
    {
        [AssetKind.Mesh] = [".glb", ".gltf"],
        [AssetKind.Script] = [".lua"],
        [AssetKind.Texture] = [".png"],
        [AssetKind.Sound] = [".ogg", ".wav"],
    };

    /// <summary>Allowed extensions for <paramref name="kind"/>.</summary>
    public static IReadOnlyList<string> ExtensionsFor(AssetKind kind) => Extensions[kind];

    /// <summary>
    /// Parses <paramref name="text"/> as written in mod data, owned by <paramref name="currentModId"/>
    /// unless it names another mod.
    /// </summary>
    public static bool TryParse(string? text, string currentModId, AssetKind kind,
        [NotNullWhen(true)] out AssetPath? path, [NotNullWhen(false)] out string? error)
    {
        path = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "asset path is empty.";
            return false;
        }
        if (text.Contains("://"))
        {
            error = $"'{text}' is a URL or engine path; use a path relative to the mod root, like \"meshes/part.glb\".";
            return false;
        }
        var modId = currentModId;
        var relative = text;
        var colon = text.IndexOf(':');
        if (colon >= 0)
        {
            modId = text[..colon];
            relative = text[(colon + 1)..];
            if (!Identifiers.IsValid(modId))
            {
                error = $"'{text}' is not a valid asset path; another mod's file is written \"mod_id:path\" and absolute paths are not allowed.";
                return false;
            }
        }
        if (relative.Contains('\\'))
        {
            error = $"'{text}' uses backslashes; use forward slashes.";
            return false;
        }
        if (relative.StartsWith('/') || relative.StartsWith('~'))
        {
            error = $"'{text}' is absolute; paths must be relative to the mod root.";
            return false;
        }
        var segments = relative.Split('/');
        if (segments.Any(s => s is "" or "." or ".."))
        {
            error = $"'{text}' contains an empty, '.' or '..' segment; paths cannot leave the mod folder.";
            return false;
        }
        var extension = Path.GetExtension(relative).ToLowerInvariant();
        if (!Extensions[kind].Contains(extension))
        {
            error = $"'{text}' must be a {string.Join(" or ", Extensions[kind])} file for a {kind.ToString().ToLowerInvariant()}.";
            return false;
        }
        path = new AssetPath(modId, relative);
        return true;
    }

    /// <summary>Full file system path, given the folder of <see cref="ModId"/>.</summary>
    public string Resolve(string modDirectory) =>
        Path.Combine(modDirectory, RelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <inheritdoc />
    public override string ToString() => $"{ModId}:{RelativePath}";
}

/// <summary>Rules for mod, part and socket ids.</summary>
public static class Identifiers
{
    /// <summary>True for lowercase snake_case: a letter, then letters, digits or underscores.</summary>
    public static bool IsValid([NotNullWhen(true)] string? id) =>
        !string.IsNullOrEmpty(id) && char.IsAsciiLetterLower(id[0])
        && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

    /// <summary>
    /// Qualifies <paramref name="id"/> with a mod id: <c>bearing</c> becomes <c>vanilla:bearing</c>
    /// (a bare id means vanilla, per CATALOG_IDS.md), and <c>mod:x</c> stays as it is.
    /// </summary>
    public static string Qualify(string id, string defaultModId = VanillaModId) =>
        id.Contains(':') ? id : $"{defaultModId}:{id}";

    /// <summary>Id of the built-in content mod.</summary>
    public const string VanillaModId = "vanilla";
}
