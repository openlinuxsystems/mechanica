using Mechanica.Core.Data;

namespace Mechanica.Core.Editor;

/// <summary>
/// One part open in the Part Editor: its <see cref="PartDefinition"/>, its <see cref="PartSource"/>
/// (model pieces), the mod it belongs to and an undo history. Every change goes through
/// <see cref="Apply"/>, which records the previous state, so the UI never mutates data directly.
/// Pure logic; the Godot editor shows it and <see cref="PartExporter"/> writes it.
/// </summary>
public sealed class PartDocument
{
    /// <summary>How many steps <see cref="Undo"/> can go back.</summary>
    public const int HistoryLimit = 200;

    private readonly List<(PartDefinition Definition, PartSource Source, string Label)> _undo = [];
    private readonly List<(PartDefinition Definition, PartSource Source, string Label)> _redo = [];
    private (PartDefinition Definition, PartSource Source)? _saved;

    private PartDocument(string modDirectory, PartDefinition definition, PartSource source, string? relativeFile)
    {
        ModDirectory = Path.GetFullPath(modDirectory);
        Definition = definition;
        Source = source;
        RelativeFile = relativeFile;
    }

    /// <summary>Raised after every change, undo and redo.</summary>
    public event Action? Changed;

    /// <summary>Absolute folder of the mod the part is saved into.</summary>
    public string ModDirectory { get; }

    /// <summary>The mod id (its folder name).</summary>
    public string ModId => Path.GetFileName(ModDirectory);

    /// <summary>The part as it stands.</summary>
    public PartDefinition Definition { get; private set; }

    /// <summary>The model pieces as they stand.</summary>
    public PartSource Source { get; private set; }

    /// <summary>
    /// Where the part file lives, relative to the mod folder with forward slashes (<c>parts/blocks/wood_block.json</c>),
    /// or null for a part that was never exported. Export keeps a part in the file it came from.
    /// </summary>
    public string? RelativeFile { get; internal set; }

    /// <summary>True when there are changes since it was opened or last exported.</summary>
    public bool IsDirty => _saved is not { } s || !ReferenceEquals(s.Definition, Definition) || !ReferenceEquals(s.Source, Source);

    /// <summary>True when <see cref="Undo"/> has something to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True when <see cref="Redo"/> has something to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Label of the step <see cref="Undo"/> would undo.</summary>
    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    /// <summary>Label of the step <see cref="Redo"/> would redo.</summary>
    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    /// <summary>
    /// Starts a new, unsaved part in the mod at <paramref name="modDirectory"/> from <paramref name="template"/>,
    /// with the given id and name.
    /// </summary>
    public static PartDocument New(string modDirectory, PartDefinition template, string id, string name, PartSource? source = null) =>
        new(modDirectory, template with { Id = id, Name = name }, source ?? PartSource.Empty, null);

    /// <summary>
    /// Opens a part file inside a mod (under <c>parts/</c> or <c>overrides/</c>) together with its editor
    /// source when one exists. Returns null and fills <paramref name="diagnostics"/> when the file cannot be read.
    /// </summary>
    public static PartDocument? Open(string partFile, List<DataDiagnostic> diagnostics)
    {
        var file = Path.GetFullPath(partFile);
        var modDirectory = FindModDirectory(file);
        if (modDirectory is null)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.FileUnreadable, partFile, null, null,
                $"'{partFile}' is not inside a mod folder (no {ModLoader.ManifestFileName} above it)."));
            return null;
        }
        var relative = Path.GetRelativePath(modDirectory, file).Replace('\\', '/');
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.FileUnreadable, relative, null, null,
                $"could not read file: {e.Message}"));
            return null;
        }
        var definition = ModLoader.ParseJson<PartDefinition>(text, relative, diagnostics);
        if (definition is null)
            return null;

        var source = PartSource.Empty;
        var sourceFile = Path.Combine(modDirectory, SourcePathFor(relative));
        if (File.Exists(sourceFile))
            source = PartSource.Read(File.ReadAllText(sourceFile), SourcePathFor(relative), diagnostics) ?? PartSource.Empty;
        else if (definition.Mesh is { } mesh)
            // A part made outside the editor: show its mesh as the one piece, so it exports unchanged.
            source = PartSource.Empty with { Model = [ModelPiece.NewMesh(mesh)] };

        var document = new PartDocument(modDirectory, definition, source, relative);
        document._saved = (document.Definition, document.Source);
        return document;
    }

    /// <summary>The editor source path for a part file path, both relative to the mod folder.</summary>
    public static string SourcePathFor(string relativePartFile) => $"{PartSource.Folder}/{relativePartFile.Replace('\\', '/')}";

    /// <summary>The nearest folder at or above <paramref name="path"/>'s folder that holds a <c>mod.json</c>.</summary>
    public static string? FindModDirectory(string path)
    {
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(path)); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, ModLoader.ManifestFileName)))
                return dir;
        }
        return null;
    }

    /// <summary>Applies a change to the definition as one undo step. No-op when it returns an equal value.</summary>
    public void Apply(string label, Func<PartDefinition, PartDefinition> edit) => Apply(label, edit(Definition), Source);

    /// <summary>Applies a change to the model pieces as one undo step.</summary>
    public void ApplySource(string label, Func<PartSource, PartSource> edit) => Apply(label, Definition, edit(Source));

    /// <summary>Replaces both definition and source as one undo step. No-op when neither changed.</summary>
    public void Apply(string label, PartDefinition definition, PartSource source)
    {
        if (Same(definition, Definition) && Same(source, Source))
            return;
        _undo.Add((Definition, Source, label));
        if (_undo.Count > HistoryLimit)
            _undo.RemoveAt(0);
        _redo.Clear();
        (Definition, Source) = (definition, source);
        Changed?.Invoke();
    }

    /// <summary>Goes back one step. Returns false when there is nothing to undo.</summary>
    public bool Undo() => Step(_undo, _redo);

    /// <summary>Goes forward one undone step. Returns false when there is nothing to redo.</summary>
    public bool Redo() => Step(_redo, _undo);

    /// <summary>Checks the part and its model pieces as export would, without writing anything.</summary>
    public IReadOnlyList<DataDiagnostic> Validate()
    {
        var file = RelativeFile ?? $"{ModLoader.PartsFolder}/{Definition.Id ?? "new_part"}.json";
        var diagnostics = new List<DataDiagnostic>(ModLoader.ValidatePart(Definition, ModDirectory, file));
        diagnostics.AddRange(PartSource.Validate(Source, ModId, SourcePathFor(file)));
        return diagnostics;
    }

    // ---- Convenience edits used by the editor UI. Each is one undo step. ----

    /// <summary>Appends a model piece.</summary>
    public void AddPiece(ModelPiece piece) =>
        ApplySource($"Add {piece.Kind?.ToString().ToLowerInvariant()}", s => s with { Model = [.. s.Pieces, piece] });

    /// <summary>Replaces the model piece at <paramref name="index"/>.</summary>
    public void SetPiece(int index, ModelPiece piece, string label = "Edit piece") =>
        ApplySource(label, s => s with { Model = Replace(s.Pieces, index, piece) });

    /// <summary>Removes the model piece at <paramref name="index"/>.</summary>
    public void RemovePiece(int index) =>
        ApplySource("Remove piece", s => s with { Model = [.. s.Pieces.Where((_, i) => i != index)] });

    /// <summary>Moves the model piece at <paramref name="index"/> by <paramref name="offset"/> places in the CSG order.</summary>
    public void MovePiece(int index, int offset)
    {
        var target = index + offset;
        if (target < 0 || target >= Source.Pieces.Count || offset == 0)
            return;
        var list = Source.Pieces.ToList();
        var piece = list[index];
        list.RemoveAt(index);
        list.Insert(target, piece);
        ApplySource("Reorder pieces", s => s with { Model = list });
    }

    /// <summary>Appends a socket.</summary>
    public void AddSocket(SocketDefinition socket) =>
        Apply("Add socket", d => d with { Sockets = [.. d.Sockets ?? [], socket] });

    /// <summary>Replaces the socket at <paramref name="index"/>.</summary>
    public void SetSocket(int index, SocketDefinition socket, string label = "Edit socket") =>
        Apply(label, d => d with { Sockets = Replace(d.Sockets ?? [], index, socket) });

    /// <summary>Removes the socket at <paramref name="index"/>. An empty list is dropped from the data.</summary>
    public void RemoveSocket(int index) => Apply("Remove socket", d =>
    {
        var rest = (d.Sockets ?? []).Where((_, i) => i != index).ToList();
        return d with { Sockets = rest.Count > 0 ? rest : null };
    });

    /// <summary>Appends a colour variant.</summary>
    public void AddVariant(ColorVariant variant) => Apply("Add colour variant", d =>
    {
        var visual = d.Visual ?? new VisualDefinition();
        return d with { Visual = visual with { Variants = [.. visual.Variants ?? [], variant] } };
    });

    /// <summary>Replaces the colour variant at <paramref name="index"/>.</summary>
    public void SetVariant(int index, ColorVariant variant) => Apply("Edit colour variant", d =>
        d with { Visual = d.Visual! with { Variants = Replace(d.Visual.Variants ?? [], index, variant) } });

    /// <summary>Removes the colour variant at <paramref name="index"/>.</summary>
    public void RemoveVariant(int index) => Apply("Remove colour variant", d =>
    {
        var rest = (d.Visual?.Variants ?? []).Where((_, i) => i != index).ToList();
        return d with { Visual = d.Visual! with { Variants = rest.Count > 0 ? rest : null } };
    });

    internal void MarkSaved(PartDefinition definition, PartSource source, string relativeFile)
    {
        // Export may fill in derived data (the baked mesh path) without an undo step.
        (Definition, Source, RelativeFile) = (definition, source, relativeFile);
        _saved = (Definition, Source);
        Changed?.Invoke();
    }

    private bool Step(List<(PartDefinition, PartSource, string)> from, List<(PartDefinition, PartSource, string)> to)
    {
        if (from.Count == 0)
            return false;
        var (definition, source, label) = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add((Definition, Source, label));
        (Definition, Source) = (definition, source);
        Changed?.Invoke();
        return true;
    }

    // Records compare lists by reference, so compare canonical JSON instead.
    private static bool Same(PartDefinition a, PartDefinition b) => ReferenceEquals(a, b) || DataJson.Write(a) == DataJson.Write(b);

    private static bool Same(PartSource a, PartSource b) => ReferenceEquals(a, b) || PartSource.Write(a) == PartSource.Write(b);

    private static List<T> Replace<T>(IReadOnlyList<T> list, int index, T item)
    {
        if (index < 0 || index >= list.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"No item {index}; there are {list.Count}.");
        var copy = list.ToList();
        copy[index] = item;
        return copy;
    }
}
