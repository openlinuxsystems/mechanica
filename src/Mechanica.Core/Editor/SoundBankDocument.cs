using Mechanica.Core.Data;
using Mechanica.Core.Sound;

namespace Mechanica.Core.Editor;

/// <summary>What <see cref="SoundBankDocument.Save"/> did.</summary>
/// <param name="Succeeded">True when the file was written.</param>
/// <param name="File">Absolute path of the bank file.</param>
/// <param name="Diagnostics">Every problem found; errors stop the save.</param>
public sealed record SoundBankSaveResult(bool Succeeded, string File, IReadOnlyList<DataDiagnostic> Diagnostics);

/// <summary>
/// One mod's sound bank file (<c>sounds/*.json</c>) open in the Sound Event Binder (chunk 16): its events
/// listed for editing, validated exactly as the loader would (<see cref="SoundRegistry.Validate"/>) and saved
/// in canonical form. A save with errors writes nothing.
/// </summary>
public sealed class SoundBankDocument
{
    private readonly List<SoundEventDefinition> _events;
    private SoundBankFile _bank;
    private bool _dirty;

    private SoundBankDocument(string modDirectory, string relativeFile, SoundBankFile bank)
    {
        ModDirectory = Path.GetFullPath(modDirectory);
        RelativeFile = relativeFile;
        _bank = bank;
        _events = [.. bank.Events ?? []];
    }

    /// <summary>Raised after every change.</summary>
    public event Action? Changed;

    /// <summary>Absolute path of the mod folder.</summary>
    public string ModDirectory { get; }

    /// <summary>The mod's id (its folder name).</summary>
    public string ModId => Path.GetFileName(ModDirectory);

    /// <summary>The bank file, relative to the mod folder.</summary>
    public string RelativeFile { get; }

    /// <summary>Absolute path of the bank file.</summary>
    public string FilePath => Path.Combine(ModDirectory, RelativeFile);

    /// <summary>The events, in file order.</summary>
    public IReadOnlyList<SoundEventDefinition> Events => _events;

    /// <summary>True when there are changes since it was opened or saved.</summary>
    public bool IsDirty => _dirty;

    /// <summary>The bank file the binder edits for a mod when it has none: <c>sounds/&lt;mod_id&gt;.json</c>.</summary>
    public static string DefaultFile(string modId) => $"{SoundRegistry.Folder}/{modId}.json";

    /// <summary>Bank files already in a mod, relative to it and sorted.</summary>
    public static IReadOnlyList<string> Files(string modDirectory)
    {
        var folder = Path.Combine(modDirectory, SoundRegistry.Folder);
        if (!Directory.Exists(folder))
            return [];
        return [.. Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(modDirectory, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Opens <paramref name="relativeFile"/> (default <see cref="DefaultFile"/>) in the mod at
    /// <paramref name="modDirectory"/>; a missing file starts empty. Null, with the reason in
    /// <paramref name="diagnostics"/>, when the file exists but cannot be read as a sound bank or the path
    /// is not a JSON file under <c>sounds/</c>.
    /// </summary>
    public static SoundBankDocument? Open(string modDirectory, string? relativeFile, List<DataDiagnostic> diagnostics)
    {
        var modId = Path.GetFileName(Path.GetFullPath(modDirectory));
        var relative = (relativeFile ?? DefaultFile(modId)).Replace('\\', '/');
        if (!relative.StartsWith(SoundRegistry.Folder + "/", StringComparison.Ordinal) || !relative.EndsWith(".json", StringComparison.Ordinal)
            || relative.Split('/').Contains("..") || Path.IsPathRooted(relative))
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.SoundInvalid, relative, null, null,
                $"sound bank files must be .json files under {SoundRegistry.Folder}/."));
            return null;
        }
        var file = Path.Combine(modDirectory, relative);
        if (!File.Exists(file))
            return new SoundBankDocument(modDirectory, relative, new SoundBankFile { Events = [] });
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.FileUnreadable, relative, null, null, $"could not read file: {e.Message}"));
            return null;
        }
        var bank = ModLoader.ParseJson<SoundBankFile>(text, relative, diagnostics);
        return bank is null ? null : new SoundBankDocument(modDirectory, relative, bank);
    }

    /// <summary>
    /// Adds an event with id <paramref name="id"/> (made unique with a number when taken) playing
    /// <paramref name="clip"/>, and returns its index.
    /// </summary>
    public int Add(string id, string? clip = null)
    {
        var unique = id;
        for (var n = 2; _events.Any(e => e.Id == unique); n++)
            unique = $"{id}_{n}";
        _events.Add(new SoundEventDefinition { Id = unique, Clips = clip is null ? [] : [clip] });
        Touch();
        return _events.Count - 1;
    }

    /// <summary>Replaces the event at <paramref name="index"/>.</summary>
    public void Set(int index, SoundEventDefinition definition)
    {
        if (_events[index] == definition)
            return;
        _events[index] = definition;
        Touch();
    }

    /// <summary>Removes the event at <paramref name="index"/>.</summary>
    public void Remove(int index)
    {
        _events.RemoveAt(index);
        Touch();
    }

    /// <summary>The index of the event with id <paramref name="id"/>, or -1.</summary>
    public int IndexOf(string id) => _events.FindIndex(e => e.Id == id);

    /// <summary>The file as it would be saved.</summary>
    public SoundBankFile Bank => _bank with { Events = [.. _events] };

    /// <summary>Every problem the loader would report for this file.</summary>
    public IReadOnlyList<DataDiagnostic> Validate() => SoundRegistry.Validate(Bank, ModDirectory, $"{ModId}/{RelativeFile}");

    /// <summary>A playable copy of the event at <paramref name="index"/> as it stands, or null while it has errors.</summary>
    public SoundEvent? Preview(int index) => SoundRegistry.Preview(_events[index], ModDirectory, FilePath);

    /// <summary>Validates and writes the file in canonical form, creating <c>sounds/</c> and the mod's manifest when missing.</summary>
    public SoundBankSaveResult Save()
    {
        var diagnostics = Validate();
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new SoundBankSaveResult(false, FilePath, diagnostics);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        PartExporter.EnsureManifest(ModDirectory);
        _bank = Bank;
        DataJson.WriteFile(FilePath, DataJson.Write(_bank));
        _dirty = false;
        Changed?.Invoke();
        return new SoundBankSaveResult(true, FilePath, diagnostics);
    }

    private void Touch()
    {
        _dirty = true;
        Changed?.Invoke();
    }
}
