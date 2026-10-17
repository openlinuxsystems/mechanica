using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;

namespace Mechanica.Core.Packages;

/// <summary>
/// The contents of <c>&lt;mod_id&gt;/package.json</c> inside a mod package (<c>&lt;id&gt;-&lt;version&gt;.zip</c>):
/// the mod's identity, what it needs to run, and a SHA-256 for every file so an installer or a mod
/// store can prove the files are the ones the author exported. See docs/packaging-guide.md §9.
/// </summary>
public sealed record PackageManifest
{
    /// <summary>The newest package format this build reads and writes.</summary>
    public const int CurrentFormat = 1;

    /// <summary>Package format version. Omitted means 1.</summary>
    public int? PackageFormat { get; init; }

    /// <summary>Mod id, equal to the archive's top folder and the mod's <c>mod.json</c> id.</summary>
    public string? Id { get; init; }

    /// <summary>Display name, copied from <c>mod.json</c>.</summary>
    public string? Name { get; init; }

    /// <summary>Semantic version, equal to the mod's <c>mod.json</c> version.</summary>
    public string? Version { get; init; }

    /// <summary>One-paragraph description, copied from <c>mod.json</c>.</summary>
    public string? Description { get; init; }

    /// <summary>Author names, copied from <c>mod.json</c>.</summary>
    public List<string>? Authors { get; init; }

    /// <summary>Mods (with version ranges) that must be installed first, copied from <c>mod.json</c>.</summary>
    public List<ModDependency>? Dependencies { get; init; }

    /// <summary>Mods this one cannot run with, copied from <c>mod.json</c>.</summary>
    public List<ModDependency>? Conflicts { get; init; }

    /// <summary>The oldest game version that can install the package (the version that exported it).</summary>
    public string? MinGameVersion { get; init; }

    /// <summary>The oldest AI endpoint protocol version the package was exported with.</summary>
    public int? MinProtocolVersion { get; init; }

    /// <summary>Every file of the mod except <c>package.json</c>, sorted by path.</summary>
    public List<PackageFile>? Files { get; init; }

    /// <summary>SHA-256 of the whole package: see <see cref="ComputePackageHash"/>.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Empty. Reserved for a mod store to sign <see cref="Sha256"/>; this build does not check it.</summary>
    public string? Signature { get; init; }

    /// <summary>Keys this build does not know. Kept so a round-trip never loses data.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>The parsed <see cref="Version"/>, or null when it is missing or invalid.</summary>
    [JsonIgnore]
    public SemVersion? ParsedVersion => SemVersion.TryParse(Version, out var v) ? v : null;

    /// <summary>
    /// The package hash: SHA-256 (lowercase hex) of the UTF-8 text <c>"&lt;sha256&gt;  &lt;path&gt;\n"</c> for every file,
    /// sorted by path (ordinal). That text is exactly what <c>sha256sum</c> prints inside the mod folder, so the
    /// hash can be checked by hand.
    /// </summary>
    public static string ComputePackageHash(IEnumerable<PackageFile> files)
    {
        var text = new StringBuilder();
        foreach (var file in files.OrderBy(f => f.Path, StringComparer.Ordinal))
            text.Append(file.Sha256).Append("  ").Append(file.Path).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}

/// <summary>One file in <see cref="PackageManifest.Files"/>.</summary>
public sealed record PackageFile
{
    /// <summary>Path relative to the mod folder, with <c>/</c> separators.</summary>
    public string? Path { get; init; }

    /// <summary>Size in bytes.</summary>
    public long? Size { get; init; }

    /// <summary>SHA-256 of the contents, lowercase hex.</summary>
    public string? Sha256 { get; init; }
}
