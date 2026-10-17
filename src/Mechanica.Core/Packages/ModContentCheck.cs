using Mechanica.Core.Bots;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Quests;
using Mechanica.Core.Sound;
using Mechanica.Core.World;

namespace Mechanica.Core.Packages;

/// <summary>
/// Checks one installed mod the way the game would load it: the mod plus the mods it depends on, with
/// every content domain (parts, recipes, sounds, bots, raids, world generation, quests, characters).
/// Returns only the problems in the mod's own files, so a broken unrelated mod never blocks an export.
/// </summary>
public static class ModContentCheck
{
    /// <summary>Every diagnostic for the mod <paramref name="modId"/> inside <paramref name="modsRoot"/>.</summary>
    public static IReadOnlyList<DataDiagnostic> Validate(string modsRoot, string modId)
    {
        var scan = ModLoader.Scan(modsRoot);
        var installed = scan.Mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        if (!installed.ContainsKey(modId))
        {
            // Not a valid mod at all: show why (its manifest problems) or that it is missing.
            var own = scan.Diagnostics.Where(d => Belongs(d, modsRoot, modId)).ToList();
            if (own.Count == 0)
                own.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.PackageExportRefused,
                    $"{modId}/{ModLoader.ManifestFileName}", null, null, $"mod '{modId}' is not installed."));
            return own;
        }

        // Enable the mod and everything it needs, nothing else (vanilla always loads).
        var needed = new List<string> { modId };
        for (var i = 0; i < needed.Count; i++)
        {
            if (installed.TryGetValue(needed[i], out var mod))
                needed.AddRange(mod.DependencyIds.Where(d => !needed.Contains(d)));
        }
        var profile = new ModProfile
        {
            FormatVersion = ModProfile.CurrentFormatVersion,
            Mods = [.. installed.Keys.Select(id => new ModProfileEntry { Id = id, Enabled = needed.Contains(id) })],
        };
        var content = ModLoader.Load(modsRoot, profile);
        var diagnostics = new List<DataDiagnostic>(content.Diagnostics);
        var recipes = RecipeBook.Load(content);
        var world = WorldGenData.Load(content);
        var bots = BotBook.Load(content);
        diagnostics.AddRange(recipes.Diagnostics);
        diagnostics.AddRange(world.Diagnostics);
        diagnostics.AddRange(bots.Diagnostics);
        diagnostics.AddRange(RaidTable.Load(content, bots).Diagnostics);
        diagnostics.AddRange(SoundRegistry.Load(content).Diagnostics);
        diagnostics.AddRange(QuestBook.Load(content, recipes, world).Diagnostics);
        diagnostics.AddRange(CharacterBook.Load(content).Diagnostics);

        var result = diagnostics.Where(d => Belongs(d, modsRoot, modId)).Distinct().ToList();
        if (content.Mods.All(m => m.Id != modId) && !result.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            var why = diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains($"'{modId}'"));
            result.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.ModNotLoaded,
                $"{modId}/{ModLoader.ManifestFileName}", null, null,
                why is null ? $"mod '{modId}' did not load." : $"mod '{modId}' did not load: {why.Message}"));
        }
        return result;
    }

    // Domains name files either "mods/<id>/..." (relative to the mods root's parent) or "<id>/...".
    private static bool Belongs(DataDiagnostic d, string modsRoot, string modId)
    {
        var root = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(modsRoot)));
        var file = d.File.Replace('\\', '/');
        return file.StartsWith($"{root}/{modId}/", StringComparison.Ordinal) || file.StartsWith($"{modId}/", StringComparison.Ordinal);
    }
}
