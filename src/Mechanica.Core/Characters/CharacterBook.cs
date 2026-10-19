using System.Security.Cryptography;
using System.Text;
using Mechanica.Core.Data;

namespace Mechanica.Core.Characters;

/// <summary>A character that loaded cleanly, with its paths qualified and defaults filled in.</summary>
/// <param name="QualifiedId">Game-wide id, <c>mod_id:character_id</c>.</param>
/// <param name="ModId">The mod that defines it.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">One line for the picker (may be empty).</param>
/// <param name="Mesh">The skinned model.</param>
/// <param name="Order">Picker order.</param>
/// <param name="Sounds">
/// Sound event for every name in <see cref="CharacterSounds.Names"/>, as written: a bare id means the character's
/// mod's event when it has one, otherwise vanilla's.
/// </param>
/// <param name="Definition">The data as written.</param>
public sealed record Character(
    string QualifiedId, string ModId, string Name, string Description, AssetPath Mesh, int Order,
    IReadOnlyDictionary<string, string> Sounds, CharacterDefinition Definition)
{
    /// <summary>The event for local sound <paramref name="name"/>, played as <see cref="ModId"/>'s.</summary>
    public string Sound(string name) => Sounds.GetValueOrDefault(name) ?? CharacterSounds.Defaults.GetValueOrDefault(name, name);
}

/// <summary>
/// Every player character of the enabled mods, read from each mod's <c>characters/**/*.json</c> in load order.
/// Each one's mesh must exist and be skinned to every bone of <see cref="CharacterRig"/>. A file with errors
/// is left out and <see cref="Diagnostics"/> says why. Host and clients compare <see cref="Fingerprint"/> on join.
/// </summary>
public sealed class CharacterBook
{
    /// <summary>Where a mod keeps its characters.</summary>
    public const string Folder = "characters";

    private readonly Dictionary<string, Character> _characters;

    private CharacterBook(IEnumerable<Character> characters, IReadOnlyList<DataDiagnostic> diagnostics)
    {
        _characters = characters.ToDictionary(c => c.QualifiedId, StringComparer.Ordinal);
        // On a tie, vanilla's characters come first, so a mod's id never silently changes the default character.
        All = [.. _characters.Values.OrderBy(c => c.Order).ThenBy(c => c.ModId != Identifiers.VanillaModId).ThenBy(c => c.QualifiedId, StringComparer.Ordinal)];
        Diagnostics = diagnostics;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var character in All)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(character.QualifiedId));
            hash.AppendData(Encoding.UTF8.GetBytes(DataJson.Write(character.Definition)));
        }
        Fingerprint = Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>A book with no characters.</summary>
    public static CharacterBook Empty { get; } = new([], []);

    /// <summary>Every character in picker order.</summary>
    public IReadOnlyList<Character> All { get; }

    /// <summary>The character a player gets before choosing: the first in picker order, or null when there are none.</summary>
    public Character? Default => All.Count > 0 ? All[0] : null;

    /// <summary>Every problem found while loading.</summary>
    public IReadOnlyList<DataDiagnostic> Diagnostics { get; }

    /// <summary>SHA-256 (hex) of every character's id and canonical form.</summary>
    public string Fingerprint { get; }

    /// <summary>The character with this id (a bare id means vanilla), or null.</summary>
    public Character? Find(string? id) => id is null ? null : _characters.GetValueOrDefault(Identifiers.Qualify(id));

    /// <summary>
    /// Loads the characters of the mods in <paramref name="content"/>, with other mods' overrides, patches and removals
    /// (<c>overrides/&lt;mod&gt;/characters/</c>, see <see cref="ContentLayers"/>) applied in load order.
    /// </summary>
    public static CharacterBook Load(ModLoadResult content)
    {
        var diagnostics = new List<DataDiagnostic>();
        var characters = new Dictionary<string, Character>(StringComparer.Ordinal);
        var directories = content.Mods.ToDictionary(m => m.Id, m => m.Directory, StringComparer.Ordinal);
        var mods = content.Mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var entry in ContentLayers.Collect(content, Folder, ContentLayers.IdMember, diagnostics,
            new LayerCodes(DiagnosticCodes.CharacterInvalid, DiagnosticCodes.DuplicateCharacter)))
        {
            var sink = new DiagnosticSink(entry.DisplayFile, diagnostics);
            if (ModLoader.ParseJson<CharacterDefinition>(entry.Json, sink) is not { } definition)
                continue;
            // Files may come from the owner, its dependencies, or the mod that changed the entry and its dependencies.
            var allowed = new HashSet<string>(StringComparer.Ordinal) { entry.OwnerModId, entry.SourceModId };
            foreach (var id in new[] { entry.OwnerModId, entry.SourceModId })
                allowed.UnionWith((mods[id].Manifest.Dependencies ?? []).Select(d => d.Id).OfType<string>());
            var character = Check(definition, entry.OwnerModId, id => allowed.Contains(id) ? directories.GetValueOrDefault(id) : null, sink);
            if (character is not null && sink.ErrorCount == 0)
                characters.Add(character.QualifiedId, character);
        }
        return new CharacterBook(characters.Values, diagnostics);
    }

    /// <summary>
    /// Checks one character as the loader would, as if it were in mod <paramref name="modId"/> at
    /// <paramref name="modDirectory"/> (null skips the mesh checks).
    /// </summary>
    public static IReadOnlyList<DataDiagnostic> Validate(CharacterDefinition character, string modId, string? modDirectory, string displayFile)
    {
        var diagnostics = new List<DataDiagnostic>();
        Check(character, modId, id => id == modId ? modDirectory : null, new DiagnosticSink(displayFile, diagnostics), checkFiles: modDirectory is not null);
        return diagnostics;
    }

    private static Character? Check(CharacterDefinition c, string modId, Func<string, string?> modDirectory, DiagnosticSink sink, bool checkFiles = true)
    {
        const string code = DiagnosticCodes.CharacterInvalid;
        var errors = sink.ErrorCount;
        if (c.Id is null)
            sink.Error(code, "$.id", "id is required.");
        else if (!Identifiers.IsValid(c.Id))
            sink.Error(code, "$.id", $"id '{c.Id}' must be lowercase snake_case.");
        if (string.IsNullOrWhiteSpace(c.Name))
            sink.Error(code, "$.name", "name is required.");

        AssetPath? mesh = null;
        if (c.Mesh is null)
            sink.Error(code, "$.mesh", "mesh is required: a model skinned to the shared character skeleton.");
        else if (!AssetPath.TryParse(c.Mesh, modId, AssetKind.Mesh, out mesh, out var pathError))
            sink.Error(DiagnosticCodes.AssetPathInvalid, "$.mesh", pathError);
        else if (checkFiles)
            CheckMesh(mesh, modDirectory(mesh.ModId), sink);

        var sounds = CharacterSounds.Defaults.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        foreach (var (name, sound) in c.Sounds ?? [])
        {
            if (!CharacterSounds.Defaults.ContainsKey(name))
                sink.Error(code, $"$.sounds.{name}", $"'{name}' is not a character sound; use one of {string.Join(", ", CharacterSounds.Names)}.");
            else if (!IsEventId(sound))
                sink.Error(code, $"$.sounds.{name}", $"'{sound}' is not a sound event id.");
            else
                sounds[name] = sound;
        }
        KnownKeys.WarnUnknown(c.Extra, "$", sink, typeof(CharacterDefinition));

        if (sink.ErrorCount > errors || mesh is null)
            return null;
        return new Character($"{modId}:{c.Id}", modId, c.Name!.Trim(), c.Description?.Trim() ?? "", mesh, c.Order ?? 0, sounds, c);
    }

    private static void CheckMesh(AssetPath mesh, string? directory, DiagnosticSink sink)
    {
        if (directory is null)
        {
            sink.Error(DiagnosticCodes.AssetUndeclaredMod, "$.mesh", $"mesh '{mesh}' is in mod '{mesh.ModId}', which must be loaded and listed in this mod's dependencies.");
            return;
        }
        var file = mesh.Resolve(directory);
        if (!File.Exists(file))
        {
            sink.Error(DiagnosticCodes.AssetMissing, "$.mesh", $"mesh '{mesh}' does not exist.");
            return;
        }
        GltfSummary summary;
        try
        {
            summary = GltfSummary.Read(file);
        }
        catch (Exception e) when (e is FormatException or IOException)
        {
            sink.Error(DiagnosticCodes.CharacterInvalid, "$.mesh", $"mesh '{mesh}' cannot be read: {e.Message}");
            return;
        }
        if (summary.SkinnedMeshes == 0)
            sink.Error(DiagnosticCodes.CharacterInvalid, "$.mesh", $"mesh '{mesh}' has no skinned mesh.");
        var missing = summary.MissingBones().ToList();
        if (missing.Count > 0)
            sink.Error(DiagnosticCodes.CharacterInvalid, "$.mesh",
                $"mesh '{mesh}' is not skinned to the character skeleton: it has no bone {string.Join(", ", missing.Select(b => $"'{b}'"))}.");
    }

    // A bare event id, or mod:id.
    private static bool IsEventId(string? id)
    {
        if (id is null)
            return false;
        var colon = id.IndexOf(':');
        return colon < 0 ? Identifiers.IsValid(id) : Identifiers.IsValid(id[..colon]) && Identifiers.IsValid(id[(colon + 1)..]);
    }
}
