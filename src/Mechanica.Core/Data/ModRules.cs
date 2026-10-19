using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mechanica.Core.Data;

/// <summary>
/// Game rules a mod may change: the gameplay half of the engine's <c>game/data</c> settings. A mod ships
/// <c>rules/&lt;name&gt;.patch.json</c>, a merge patch (<see cref="ContentLayers.KeyedMergePatch"/>) over the settings
/// file of that name; patches stack in load order, so the last mod wins per key. Keys that keep the engine
/// running (network, performance, simulation step) are locked and stay as the build ships them.
/// </summary>
public static class ModRules
{
    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Folder in a mod that holds rule patches.</summary>
    public const string Folder = "rules";

    /// <summary>
    /// The settings files mods may patch, with the top-level keys of each that stay locked
    /// (every other key is a rule). The same lists are in <c>docs/packaging-guide.md</c> (Game rules).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Files { get; } = new SortedDictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
    {
        // max_drops bounds memory and sync; aim_tolerance is network lag allowance.
        ["survival"] = new HashSet<string>(["max_drops", "aim_tolerance"], StringComparer.Ordinal),
        // tick is the simulation step; max_fires bounds work per tick.
        ["fire"] = new HashSet<string>(["tick", "max_fires"], StringComparer.Ordinal),
        // Only the gameplay sections of building: the paint palette and ammo, the sledgehammer, breaking and the Lift.
        ["building"] = new HashSet<string>(["reach", "max_parts_per_creation", "clearance_scale", "anchor_margin", "placement", "connect", "weld"], StringComparer.Ordinal),
    };

    /// <summary>
    /// <paramref name="engineJson"/> (the build's <c>game/data/&lt;name&gt;.json</c>) with every mod's
    /// <c>rules/&lt;name&gt;.patch.json</c> applied in load order. <paramref name="check"/> parses a result and returns
    /// why it is invalid (null when it is fine); a patch that makes the settings invalid is left out with an error, and
    /// the rules before it stay. Locked keys and unreadable files were reported when the mod loaded (<see cref="CheckFolder"/>).
    /// </summary>
    public static string Apply(string name, string engineJson, ModLoadResult? mods, Func<string, string?> check, List<DataDiagnostic> diagnostics)
    {
        if (!Files.TryGetValue(name, out var locked))
            throw new ArgumentException($"'{name}' is not a rules file.", nameof(name));
        if (mods is null)
            return engineJson;
        JsonNode? current = null;
        foreach (var mod in mods.Mods)
        {
            var path = Path.Combine(mod.Directory, Folder, name + ContentLayers.PatchSuffix);
            if (!File.Exists(path))
                continue;
            var display = $"{mod.Id}/{Folder}/{name}{ContentLayers.PatchSuffix}";
            // A loaded mod's rules already passed CheckFolder; one that fails here changed on disk since.
            if (ReadPatch(path, display, diagnostics) is not { } patch || patch.Any(p => locked.Contains(p.Key)))
                continue;
            current ??= JsonNode.Parse(engineJson, documentOptions: Options);
            var merged = ContentLayers.KeyedMergePatch(current!.DeepClone(), patch);
            var text = merged!.ToJsonString();
            if (check(text) is { } why)
            {
                diagnostics.Add(Error(DiagnosticCodes.RuleInvalid, display, null, $"the {name} rules would be invalid: {why} This mod's {name} rules are left out."));
                continue;
            }
            current = merged;
        }
        return current?.ToJsonString() ?? engineJson;
    }

    /// <summary>
    /// Checks a mod's <c>rules/</c> folder as the mod loads: every file must be a patch of a settings file in
    /// <see cref="Files"/>, hold one JSON object and leave the locked keys alone. Returns the number of errors.
    /// Whether the patched settings are valid is only known where the engine's settings are read (<see cref="Apply"/>).
    /// </summary>
    public static int CheckFolder(string modId, string directory, List<DataDiagnostic> diagnostics)
    {
        var folder = Path.Combine(directory, Folder);
        if (!Directory.Exists(folder))
            return 0;
        var before = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        foreach (var file in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(file);
            var display = $"{modId}/{Folder}/{fileName}";
            var name = fileName.EndsWith(ContentLayers.PatchSuffix, StringComparison.Ordinal) ? fileName[..^ContentLayers.PatchSuffix.Length] : null;
            if (name is null || !Files.TryGetValue(name, out var locked))
            {
                diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Warning, DiagnosticCodes.RuleUnknown, display, null, null,
                    $"rules files are {string.Join(", ", Files.Keys.Select(k => k + ContentLayers.PatchSuffix))}; this one is ignored."));
                continue;
            }
            if (ReadPatch(file, display, diagnostics) is { } patch && patch.Select(p => p.Key).FirstOrDefault(locked.Contains) is { } key)
                diagnostics.Add(Error(DiagnosticCodes.RuleLocked, display, $"$.{key}", $"'{key}' is an engine setting, not a game rule; mods can't change it."));
        }
        return diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error) - before;
    }

    private static JsonObject? ReadPatch(string path, string display, List<DataDiagnostic> diagnostics)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path), documentOptions: Options) is JsonObject patch)
                return patch;
            diagnostics.Add(Error(DiagnosticCodes.InvalidJson, display, null, "the file must hold one JSON object."));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(DiagnosticCodes.InvalidJson, display, null, $"could not read the rules: {e.Message}"));
        }
        return null;
    }

    private static DataDiagnostic Error(string code, string file, string? path, string message) =>
        new(DiagnosticSeverity.Error, code, file, path, null, message);
}
