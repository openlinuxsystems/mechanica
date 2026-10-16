using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Data;

/// <summary>
/// The contents of <c>mods/&lt;mod_id&gt;/mod.json</c>: identity, version and dependencies of a mod
/// (decision D6).
/// </summary>
public sealed record ModManifest
{
    /// <summary>The newest data format this build reads. Bump it when a migration is added.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Data format of the mod's files. Omitted means 1.</summary>
    public int? FormatVersion { get; init; }

    /// <summary>Mod id: lowercase snake_case, equal to the folder name. Prefixes qualified ids (<c>id:part</c>).</summary>
    public string? Id { get; init; }

    /// <summary>Display name.</summary>
    public string? Name { get; init; }

    /// <summary>Semantic version of the mod, for example <c>"1.2.0"</c>.</summary>
    public string? Version { get; init; }

    /// <summary>Optional one-paragraph description.</summary>
    public string? Description { get; init; }

    /// <summary>Optional author names.</summary>
    public List<string>? Authors { get; init; }

    /// <summary>Mods that must load before this one.</summary>
    public List<ModDependency>? Dependencies { get; init; }

    /// <summary>
    /// Mods that cannot be enabled together with this one (optionally only some versions). When one is
    /// enabled, this mod is not loaded and the Mod Manager reports the conflict.
    /// </summary>
    public List<ModDependency>? Conflicts { get; init; }

    /// <summary>Keys this build does not know. Kept so a round-trip never loses data.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>The parsed <see cref="Version"/>, or null when it is missing or invalid.</summary>
    [JsonIgnore]
    public SemVersion? ParsedVersion => SemVersion.TryParse(Version, out var v) ? v : null;
}

/// <summary>A mod reference in <see cref="ModManifest.Dependencies"/> or <see cref="ModManifest.Conflicts"/>.</summary>
public sealed record ModDependency
{
    /// <summary>Id of the referenced mod.</summary>
    public string? Id { get; init; }

    /// <summary>Versions it applies to, as a <see cref="VersionRange"/> expression. Omitted means any version.</summary>
    public string? Version { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>The parsed <see cref="Version"/> range (any version when omitted), or null when invalid.</summary>
    [JsonIgnore]
    public VersionRange? ParsedVersion => Version is null ? VersionRange.Any
        : VersionRange.TryParse(Version, out var r) ? r : null;
}
