using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Data;

/// <summary>
/// The player's mod setup, saved as <c>mods.json</c> in the user data folder (not inside <c>mods/</c>,
/// so reinstalling a mod keeps it): which installed mods are enabled and the preferred load order.
/// The Mod Manager writes it; <see cref="ModLoader.Load(string, ModProfile?)"/> reads it.
/// </summary>
public sealed record ModProfile
{
    /// <summary>The newest profile format this build reads.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Usual file name of a profile.</summary>
    public const string FileName = "mods.json";

    /// <summary>Profile format. Omitted means 1.</summary>
    public int? FormatVersion { get; init; }

    /// <summary>
    /// Mods in preferred load order. Dependencies still load before their dependents, so a mod placed
    /// above one of its dependencies moves just below it. Installed mods missing from the list are
    /// enabled and go after it, by id. Entries for mods that are not installed are kept, so
    /// reinstalling a mod restores its place.
    /// </summary>
    public List<ModProfileEntry>? Mods { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>
    /// Reads a profile. A missing file gives null (every mod enabled, default order). A broken file
    /// is reported in <paramref name="diagnostics"/> and also gives null, so the game still starts.
    /// </summary>
    public static ModProfile? Load(string path, List<DataDiagnostic> diagnostics)
    {
        if (!File.Exists(path))
            return null;
        var sink = new DiagnosticSink(Path.GetFileName(path), diagnostics);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            sink.Error(DiagnosticCodes.FileUnreadable, null, $"could not read file: {e.Message}");
            return null;
        }
        var profile = ModLoader.ParseJson<ModProfile>(json, sink);
        if (profile is null)
            return null;
        Validate(profile, sink);
        return sink.ErrorCount == 0 ? profile : null;
    }

    /// <summary>Writes the profile in canonical form, creating the folder if needed.</summary>
    public void Save(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } dir)
            Directory.CreateDirectory(dir);
        DataJson.WriteFile(path, DataJson.Write(this));
    }

    private static void Validate(ModProfile profile, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.ProfileInvalid;
        if (profile.FormatVersion is { } format && (format < 1 || format > CurrentFormatVersion))
            sink.Error(DiagnosticCodes.UnsupportedFormat, "$.format_version",
                $"format_version {format} is not supported; this build reads 1 to {CurrentFormatVersion}.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < (profile.Mods?.Count ?? 0); i++)
        {
            var entry = profile.Mods![i];
            var path = $"$.mods[{i}]";
            if (!Identifiers.IsValid(entry.Id))
                sink.Error(code, $"{path}.id", entry.Id is null ? "id is required." : $"'{entry.Id}' is not a valid mod id.");
            else if (!seen.Add(entry.Id))
                sink.Error(code, $"{path}.id", $"mod '{entry.Id}' is listed twice.");
            KnownKeys.WarnUnknown(entry.Extra, path, sink, typeof(ModProfileEntry));
        }
        KnownKeys.WarnUnknown(profile.Extra, "$", sink, typeof(ModProfile));
    }
}

/// <summary>One mod in a <see cref="ModProfile"/>.</summary>
public sealed record ModProfileEntry
{
    /// <summary>Mod id.</summary>
    public string? Id { get; init; }

    /// <summary>Whether the mod loads. Omitted means true.</summary>
    public bool? Enabled { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary><see cref="Enabled"/> with its default applied.</summary>
    [JsonIgnore]
    public bool IsEnabled => Enabled ?? true;
}
