using Mechanica.Core.Data;

namespace Mechanica.Core.Sound;

/// <summary>
/// Every sound event of the enabled mods (chunk 16), read from each mod's <c>sounds/**/*.json</c> in load
/// order. Ids are qualified (<c>mod_id:sound_id</c>). An event written <c>other_mod:id</c> replaces that
/// mod's event, and the mod lower in the load order wins. A file with errors is left out, and
/// <see cref="Diagnostics"/> says why; the rest still load, because a missing sound never stops a game.
/// Parts that name a sound nobody registered get a warning.
/// </summary>
public sealed class SoundRegistry
{
    /// <summary>Folder inside a mod that holds sound event files (and, by convention, the audio).</summary>
    public const string Folder = "sounds";

    private readonly Dictionary<string, SoundEvent> _events;

    private SoundRegistry(Dictionary<string, SoundEvent> events, IReadOnlyList<DataDiagnostic> diagnostics)
    {
        _events = events;
        Diagnostics = diagnostics;
    }

    /// <summary>A registry with no events, for tests and tools that need none.</summary>
    public static SoundRegistry Empty { get; } = new([], []);

    /// <summary>Every event by qualified id.</summary>
    public IReadOnlyDictionary<string, SoundEvent> Events => _events;

    /// <summary>Qualified ids, sorted.</summary>
    public IEnumerable<string> Ids => _events.Keys.Order(StringComparer.Ordinal);

    /// <summary>Every problem found while loading.</summary>
    public IReadOnlyList<DataDiagnostic> Diagnostics { get; }

    /// <summary>True when any diagnostic is an error.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Finds an event as data names it. <c>mod:id</c> is exact. A bare id means the event of
    /// <paramref name="ownerModId"/> (the mod whose data names it) when it has one, otherwise vanilla's.
    /// </summary>
    public SoundEvent? Find(string? id, string ownerModId = Identifiers.VanillaModId)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        if (id.Contains(':'))
            return _events.GetValueOrDefault(id);
        return _events.GetValueOrDefault($"{ownerModId}:{id}") ?? _events.GetValueOrDefault($"{Identifiers.VanillaModId}:{id}");
    }

    /// <summary>Loads the sound events of every mod in <paramref name="content"/>, in load order, and checks its parts' sounds.</summary>
    public static SoundRegistry Load(ModLoadResult content)
    {
        var diagnostics = new List<DataDiagnostic>();
        var events = new Dictionary<string, SoundEvent>(StringComparer.Ordinal);
        var directories = content.Mods.ToDictionary(m => m.Id, m => m.Directory, StringComparer.Ordinal);
        foreach (var mod in content.Mods)
            LoadMod(mod.Manifest, mod.Directory, id => directories.GetValueOrDefault(id), events, diagnostics);
        var registry = new SoundRegistry(events, diagnostics);
        foreach (var part in content.Parts.Values.OrderBy(p => p.QualifiedId, StringComparer.Ordinal))
        {
            var file = directories.TryGetValue(part.ModId, out var dir)
                ? $"{part.ModId}/{Path.GetRelativePath(dir, part.FilePath).Replace('\\', '/')}"
                : part.FilePath;
            diagnostics.AddRange(registry.CheckPart(part.Definition, part.ModId, file));
        }
        return registry;
    }

    /// <summary>
    /// The shortest id a part of <paramref name="ownerModId"/> can write for the event
    /// <paramref name="qualifiedId"/>: the bare id when <see cref="Find"/> resolves it to that event,
    /// else the qualified id. The Sound Event Binder writes bindings this way.
    /// </summary>
    public string ShortId(string qualifiedId, string ownerModId)
    {
        var colon = qualifiedId.IndexOf(':');
        if (colon < 0)
            return qualifiedId;
        var bare = qualifiedId[(colon + 1)..];
        return Find(bare, ownerModId)?.QualifiedId == qualifiedId ? bare : qualifiedId;
    }

    /// <summary>
    /// A playable copy of one event as the mod at <paramref name="modDirectory"/> would load it, for the
    /// Sound Event Binder's preview of an unsaved event. Null when the event has errors.
    /// </summary>
    public static SoundEvent? Preview(SoundEventDefinition definition, string modDirectory, string filePath)
    {
        var bank = new SoundBankFile { Events = [definition] };
        if (Validate(bank, modDirectory, filePath).Any(d => d.Severity == DiagnosticSeverity.Error))
            return null;
        var dir = Path.GetFullPath(modDirectory);
        var modId = Path.GetFileName(dir);
        var root = Path.GetDirectoryName(dir) ?? dir;
        var deps = DependenciesOf(dir);
        Func<string, string?> directoryOf = id => id == modId ? dir : Path.Combine(root, id);
        var validator = new SoundValidator(new PartValidator(modId, deps, directoryOf), modId, deps);
        var clips = definition.Clips!.Select(c => validator.Resolve(c, directoryOf)).ToList();
        return new SoundEvent(Identifiers.Qualify(definition.Id!, modId), modId, filePath, definition, clips);
    }

    /// <summary>
    /// Reads and validates one sound bank file as if it were in the mod at <paramref name="modDirectory"/>,
    /// without loading anything else. The Sound Event Binder checks a bank with this before saving.
    /// Other mods' clips are looked up as sibling folders.
    /// </summary>
    public static IReadOnlyList<DataDiagnostic> Validate(SoundBankFile bank, string modDirectory, string displayFile)
    {
        var diagnostics = new List<DataDiagnostic>();
        var dir = Path.GetFullPath(modDirectory);
        var modId = Path.GetFileName(dir);
        var root = Path.GetDirectoryName(dir) ?? dir;
        var deps = DependenciesOf(dir);
        var validator = new SoundValidator(new PartValidator(modId, deps, id => id == modId ? dir : Path.Combine(root, id)), modId, deps);
        validator.Validate(bank, new DiagnosticSink(displayFile, diagnostics), new HashSet<string>(StringComparer.Ordinal));
        return diagnostics;
    }

    /// <summary>
    /// Warnings for a part whose sounds name no registered event: its bound <c>sounds</c> and the
    /// <c>place_</c>/<c>break_</c>/<c>impact_</c> events of its <c>sound_material</c> it does not rebind.
    /// </summary>
    /// <param name="part">The part's data.</param>
    /// <param name="ownerModId">Mod whose files define it, which bare sound ids are looked up in first.</param>
    /// <param name="displayFile">File name shown in the diagnostics.</param>
    public IReadOnlyList<DataDiagnostic> CheckPart(PartDefinition part, string ownerModId, string displayFile)
    {
        var diagnostics = new List<DataDiagnostic>();
        foreach (var (key, id, path) in References(part))
        {
            if (Find(id, ownerModId) is null)
                diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Warning, DiagnosticCodes.SoundUnknown, displayFile, path, null,
                    $"{key} sound '{id}' is not a registered sound event, so it plays nothing."));
        }
        return diagnostics;
    }

    // What a part's data asks to hear: bound events, then material events not overridden by a binding.
    private static IEnumerable<(string Key, string Id, string Path)> References(PartDefinition part)
    {
        foreach (var (key, id) in part.Sounds ?? [])
            yield return (key, id, $"$.sounds.{key}");
        if (part.SoundMaterial is not { } material || !Identifiers.IsValid(material))
            yield break;
        foreach (var key in PartSounds.MaterialKeys.Where(k => part.Sounds?.ContainsKey(k) != true))
            yield return (key, PartSounds.MaterialEvent(key, material), "$.sound_material");
    }

    private static void LoadMod(ModManifest manifest, string dir, Func<string, string?> directoryOf,
        Dictionary<string, SoundEvent> events, List<DataDiagnostic> diagnostics)
    {
        var folder = Path.Combine(dir, Folder);
        if (!Directory.Exists(folder))
            return;
        var modId = manifest.Id!;
        var deps = (manifest.Dependencies ?? []).Select(d => d.Id).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var validator = new SoundValidator(new PartValidator(modId, deps, id => id == modId ? dir : directoryOf(id)), modId, deps);
        var ownIds = new HashSet<string>(StringComparer.Ordinal);
        var files = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories)
            .OrderBy(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), StringComparer.Ordinal);
        foreach (var file in files)
        {
            var display = $"{modId}/{Path.GetRelativePath(dir, file).Replace('\\', '/')}";
            var sink = new DiagnosticSink(display, diagnostics);
            SoundBankFile? bank;
            try
            {
                bank = ModLoader.ParseJson<SoundBankFile>(File.ReadAllText(file), sink);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                sink.Error(DiagnosticCodes.FileUnreadable, null, $"could not read file: {e.Message}");
                continue;
            }
            if (bank is null)
                continue;
            var fileIds = new HashSet<string>(ownIds, StringComparer.Ordinal);
            validator.Validate(bank, sink, fileIds);
            for (var i = 0; i < (bank.Events?.Count ?? 0); i++)
            {
                var e = bank.Events![i];
                if (SoundValidator.ReplacedMod(e.Id, modId) is { } other && !events.ContainsKey(e.Id!))
                    sink.Error(DiagnosticCodes.SoundInvalid, $"$.events[{i}].id",
                        $"'{e.Id}' replaces an event that mod '{other}' does not have.");
            }
            if (sink.ErrorCount > 0)
            {
                diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.SoundInvalid, display, null, null,
                    $"none of this file's {bank.Events?.Count ?? 0} sound event(s) were loaded because of the errors above."));
                continue;
            }
            ownIds.UnionWith(fileIds);
            foreach (var e in bank.Events ?? [])
            {
                var id = Identifiers.Qualify(e.Id!, modId);
                if (events.TryGetValue(id, out var previous) && previous.ModId != id[..id.IndexOf(':')] && previous.ModId != modId)
                    diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Warning, DiagnosticCodes.SoundOverrideConflict, display, null, null,
                        $"mods '{previous.ModId}' and '{modId}' both replace sound '{id}'; '{modId}' loads later and wins."));
                var clips = e.Clips!.Select(c => validator.Resolve(c, id2 => id2 == modId ? dir : directoryOf(id2))).ToList();
                events[id] = new SoundEvent(id, modId, file, e, clips);
            }
        }
    }

    private static HashSet<string> DependenciesOf(string modDirectory)
    {
        var deps = new HashSet<string>(StringComparer.Ordinal);
        var manifestPath = Path.Combine(modDirectory, ModLoader.ManifestFileName);
        if (!File.Exists(manifestPath))
            return deps;
        var manifest = ModLoader.ParseJson<ModManifest>(File.ReadAllText(manifestPath), "mod.json", []);
        deps.UnionWith((manifest?.Dependencies ?? []).Select(d => d.Id).OfType<string>());
        return deps;
    }
}
