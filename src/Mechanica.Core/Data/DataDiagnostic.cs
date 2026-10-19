namespace Mechanica.Core.Data;

/// <summary>How serious a <see cref="DataDiagnostic"/> is.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Suspicious but loadable, for example an unknown key that may be a typo.</summary>
    Warning,

    /// <summary>The file or mod cannot be used.</summary>
    Error,
}

/// <summary>
/// One problem found while loading or validating mod data. <see cref="ToString"/> gives a
/// single readable line such as <c>error MD0201: mods/x/parts/a.json(4) $.mass: mass must be greater than 0.</c>
/// </summary>
/// <param name="Severity">Warning or error.</param>
/// <param name="Code">Stable diagnostic code (see <see cref="DiagnosticCodes"/>).</param>
/// <param name="File">File the problem is in, relative to the mods root where possible.</param>
/// <param name="JsonPath">JSON path inside the file (<c>$.sockets[1].id</c>), or null.</param>
/// <param name="Line">1-based line number, when the JSON parser reported one.</param>
/// <param name="Message">Human-readable description.</param>
public sealed record DataDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string File,
    string? JsonPath,
    int? Line,
    string Message)
{
    /// <inheritdoc />
    public override string ToString()
    {
        var severity = Severity == DiagnosticSeverity.Error ? "error" : "warning";
        var location = Line is { } line ? $"{File}({line})" : File;
        var path = JsonPath is null ? "" : $" {JsonPath}";
        return $"{severity} {Code}: {location}{path}: {Message}";
    }
}

/// <summary>Stable codes for <see cref="DataDiagnostic"/>, grouped by area.</summary>
public static class DiagnosticCodes
{
    // 00xx: files and JSON syntax
    public const string FileUnreadable = "MD0001";
    public const string InvalidJson = "MD0002";
    public const string UnknownKey = "MD0003";

    // 01xx: mod manifests, discovery and dependencies
    public const string ManifestMissing = "MD0101";
    public const string ManifestInvalid = "MD0102";
    public const string ModFolderMismatch = "MD0103";
    public const string DuplicateMod = "MD0104";
    public const string MissingDependency = "MD0105";
    public const string DependencyVersion = "MD0106";
    public const string DependencyCycle = "MD0107";
    public const string DependencyFailed = "MD0108";
    public const string UnsupportedFormat = "MD0109";
    public const string ModNotLoaded = "MD0110";
    public const string DependencyDisabled = "MD0111";
    public const string ModIncompatible = "MD0112";
    public const string ProfileInvalid = "MD0113";

    // 02xx: part definitions
    public const string PartInvalid = "MD0201";
    public const string DuplicatePart = "MD0202";
    public const string OverrideUndeclaredMod = "MD0203";
    public const string OverrideUnknownPart = "MD0204";
    public const string OverrideConflict = "MD0205";

    // 03xx: asset paths
    public const string AssetPathInvalid = "MD0301";
    public const string AssetMissing = "MD0302";
    public const string AssetUndeclaredMod = "MD0303";

    // 04xx: saved Creations
    public const string CreationInvalid = "MD0401";
    public const string CreationUnknownPart = "MD0402";
    public const string CreationOverlap = "MD0403";
    public const string CreationConnection = "MD0404";
    public const string CreationDisconnected = "MD0405";

    // 05xx: Part Editor sources and export
    public const string EditorSourceInvalid = "MD0501";
    public const string ExportFailed = "MD0502";
    public const string BatchInvalid = "MD0503";

    // 06xx: world generation and POI prefabs
    public const string WorldGenInvalid = "MD0601";
    public const string WorldGenMissing = "MD0602";
    public const string PoiInvalid = "MD0603";

    // 07xx: recipes and inventories
    public const string RecipeInvalid = "MD0701";
    public const string DuplicateRecipe = "MD0702";
    public const string RecipeOverrideUnknown = "MD0703";
    public const string RecipeOverrideConflict = "MD0704";

    // 08xx: sound events
    public const string SoundInvalid = "MD0801";
    public const string SoundDuplicate = "MD0802";
    public const string SoundOverrideConflict = "MD0803";
    public const string SoundUnknown = "MD0804";

    // 09xx: bots and raids
    public const string BotInvalid = "MD0901";
    public const string DuplicateBot = "MD0902";
    public const string RaidInvalid = "MD0903";
    public const string BotBehaviorUnknownNode = "MD0904";

    // 10xx: saves
    public const string SaveInvalid = "MD1001";

    // 11xx: quests
    public const string QuestInvalid = "MD1101";

    // 12xx: player characters
    public const string CharacterInvalid = "MD1201";
    public const string DuplicateCharacter = "MD1202";

    // 13xx: mod packages (export, install, verify)
    public const string PackageInvalid = "MD1301";
    public const string PackageUnsafePath = "MD1302";
    public const string PackageTooLarge = "MD1303";
    public const string PackageHashMismatch = "MD1304";
    public const string PackageFileList = "MD1305";
    public const string PackageGameVersion = "MD1306";
    public const string PackageDependency = "MD1307";
    public const string PackageConflict = "MD1308";
    public const string PackageExportRefused = "MD1309";
    public const string PackageModified = "MD1310";

    // 14xx: Lua scripts
    public const string ScriptSyntax = "MD1401";

    // 15xx: content overrides, patches and removals (bots, quests, POIs, characters, and remove.json for any kind)
    public const string ContentOverrideUnknown = "MD1501";
    public const string ContentOverrideConflict = "MD1502";
}

/// <summary>Collects diagnostics for one file, filling in the file name.</summary>
internal sealed class DiagnosticSink(string file, List<DataDiagnostic> into)
{
    public string File { get; } = file;

    public int ErrorCount { get; private set; }

    public void Error(string code, string? path, string message, int? line = null)
    {
        ErrorCount++;
        into.Add(new DataDiagnostic(DiagnosticSeverity.Error, code, File, path, line, message));
    }

    public void Warning(string code, string? path, string message, int? line = null) =>
        into.Add(new DataDiagnostic(DiagnosticSeverity.Warning, code, File, path, line, message));
}
