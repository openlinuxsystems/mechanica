using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Mechanica.Core;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Input;
using Mechanica.Core.Ui;
using Mechanica.Game.Creations;
using Mechanica.Game.Mods;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.Editor;

/// <summary>
/// The in-game Part Editor (chunk 5, D9). Make a new part from a template or open one from a mod;
/// build its model from imported glTF meshes and primitive/CSG pieces; set its footprint, physical and
/// visual properties, colour variants, collision shape, sockets and sounds (<see cref="SoundBinderPanel"/>); watch the 3D preview with collision,
/// socket and connection overlays; and export it into its mod folder in one click. "Test in Sandbox"
/// exports and spawns it in the Test Sandbox.
/// <para>
/// All data logic lives in Core (<see cref="PartDocument"/>, <see cref="PartExporter"/>,
/// <see cref="SocketTools"/>, <see cref="AssetImporter"/>); this node only draws it and forwards edits.
/// </para>
/// </summary>
public partial class PartEditorScreen : Control
{
    /// <summary>Where the part templates live (part JSON files, one per starting point).</summary>
    public const string TemplatesPath = "res://data/part_templates";

    private const float PositionStep = MechanicaInfo.GridSize / 8;

    private PartAssets _assets = null!;
    private PartPreview _preview = null!;
    private SubViewport _viewport = null!;
    private Label _title = null!;
    private Button _export = null!;
    private Button _test = null!;
    private Label _hint = null!;
    private RichTextLabel _problems = null!;
    private TabContainer _tabs = null!;
    private PropertyGrid _partGrid = null!;
    private PropertyGrid _pieceGrid = null!;
    private PropertyGrid _collisionGrid = null!;
    private PropertyGrid _socketGrid = null!;
    private PropertyGrid _lookGrid = null!;
    private PropertyGrid _behaviourGrid = null!;
    private SoundBinderPanel _sounds = null!;
    private ItemList _pieces = null!;
    private ItemList _sockets = null!;
    private Button _undo = null!;
    private Button _redo = null!;
    private FileDialog _openDialog = null!;
    private FileDialog _importDialog = null!;
    private FileDialog _batchDialog = null!;
    private ConfirmationDialog _newDialog = null!;
    private AcceptDialog _message = null!;
    private HelpWindow _help = null!;
    private Action<string>? _importTarget;
    private bool _quiet;
    private int _selectedPiece = -1;
    private int _selectedSocket = -1;
    private int _selectedVariant = -1;

    /// <summary>Raised after a successful export when the user asked to test the part; carries its qualified id.</summary>
    public event Action<string>? TestRequested;

    /// <summary>Raised when the user leaves the editor.</summary>
    public event Action? CloseRequested;

    /// <summary>Mods folder parts are opened from and saved to. Set before the node enters the tree.</summary>
    public string ModsRoot { get; set; } = ModPaths.ModsRoot;

    /// <summary>The part being edited, or null before one is opened.</summary>
    public PartDocument? Document { get; private set; }

    /// <summary>The Recipes tab: recipes in the part's mod that make the part.</summary>
    public RecipePanel Recipes { get; private set; } = null!;

    /// <summary>The Script tab (Lua editor).</summary>
    public LuaPanel Lua { get; private set; } = null!;

    /// <summary>The 3D preview.</summary>
    public PartPreview Preview => _preview;

    /// <summary>The problems list as plain text, for tests.</summary>
    public string ProblemsText => _problems.GetParsedText();

    /// <summary>The Sounds tab: the Sound Event Binder.</summary>
    public SoundBinderPanel Sounds => _sounds;

    /// <summary>The selected model piece, or -1.</summary>
    public int SelectedPiece => _selectedPiece;

    /// <summary>The selected socket, or -1.</summary>
    public int SelectedSocket => _selectedSocket;

    /// <summary>Template names (file names without <c>.json</c>), sorted.</summary>
    public static IReadOnlyList<string> TemplateNames =>
        [.. DirAccess.GetFilesAt(TemplatesPath).Where(f => f.EndsWith(".json", StringComparison.Ordinal)).Select(f => f[..^5]).Order(StringComparer.Ordinal)];

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Window = new ToolWindow { Name = "Window", Title = "Part Editor" };
        Window.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Window.CloseGuard = Discard;
        Window.CloseRequested += () => CloseRequested?.Invoke();
        AddChild(Window);
        var column = Window.Body;
        _test = Window.AddAction("Test in Sandbox", () => _ = TestInSandboxAsync(), tooltip: "Export, then spawn the part in the Test Sandbox");
        _export = Window.AddAction("Export", () => _ = ExportAsync(), primary: true, tooltip: "Validate and write the part into its mod folder (Ctrl+S)");

        var toolbar = new HBoxContainer();
        column.AddChild(toolbar);
        AddButton(toolbar, "New…", () => Discard(() => _newDialog.PopupCentered(new Vector2I(420, 220))), "Start a part from a template");
        AddButton(toolbar, "Open…", () => Discard(() => _openDialog.PopupCentered(new Vector2I(900, 600))), "Open a part file from a mod");
        AddButton(toolbar, "Run batch…", () => _batchDialog.PopupCentered(new Vector2I(900, 600)),
            "Make or update many parts from a batch file (editor/batches/*.json in a mod)");
        toolbar.AddChild(new VSeparator());
        _undo = AddButton(toolbar, "Undo", () => Document?.Undo(), "Ctrl+Z");
        _redo = AddButton(toolbar, "Redo", () => Document?.Redo(), "Ctrl+Shift+Z");
        toolbar.AddChild(new VSeparator());
        AddButton(toolbar, "Help", () => OpenHelp("getting-started.md"), "Getting Started, the Part Data Schema Reference and the Lua API Reference");
        _title = Loc.Verbatim(new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        toolbar.AddChild(_title);

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [440] };
        column.AddChild(split);
        _tabs = new TabContainer { CustomMinimumSize = new Vector2(420, 0) };
        split.AddChild(_tabs);
        BuildTabs();

        var right = new VSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(right);
        var view = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3 };
        right.AddChild(view);
        var container = new SubViewportContainer { Stretch = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        container.GuiInput += OnViewInput;
        view.AddChild(container);
        _viewport = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X };
        container.AddChild(_viewport);
        _preview = new PartPreview { Name = "Preview" };
        _viewport.AddChild(_preview);
        var overlays = new HBoxContainer();
        view.AddChild(overlays);
        AddToggle(overlays, "Collision", true, on =>
        {
            _preview.ShowCollision = on;
            Refresh(rebuildInspector: false);
        });
        AddToggle(overlays, "Sockets", true, on =>
        {
            _preview.ShowSockets = on;
            Refresh(rebuildInspector: false);
        });
        AddToggle(overlays, "Connections", true, on =>
        {
            _preview.ShowConnections = on;
            Refresh(rebuildInspector: false);
        });
        AddToggle(overlays, "Joints", true, on =>
        {
            _preview.ShowJoints = on;
            Refresh(rebuildInspector: false);
        });
        _hint = new Label
        {
            Text = "Right-drag to orbit, wheel to zoom. Connections: green rigid, blue hinge, teal slider, violet spring, red none. Joints show their axis and range.",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Right,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ThemeTypeVariation = "SecondaryLabel",
        };
        overlays.AddChild(_hint);
        _problems = Loc.Verbatim(new RichTextLabel { BbcodeEnabled = true, SelectionEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 90) });
        right.AddChild(_problems);

        BuildDialogs();
        _assets = new PartAssets(id => Path.Combine(ModsRoot, id));
        Refresh(rebuildInspector: true);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || !IsVisibleInTree())
            return;
        var ctrl = key.CtrlPressed || key.MetaPressed;
        if (ctrl && key.Keycode == Key.Z)
            _ = key.ShiftPressed ? Document?.Redo() : Document?.Undo();
        else if (ctrl && key.Keycode == Key.Y)
            Document?.Redo();
        else if (ctrl && key.Keycode == Key.S)
            _ = ExportAsync();
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>The editor's window: its X and Esc close the editor (asking first about unsaved changes).</summary>
    public ToolWindow Window { get; private set; } = null!;

    /// <summary>True when the part, its script, its recipes or its mod's sound bank has changes not written yet.</summary>
    public bool HasUnsavedChanges => Document?.IsDirty == true || Lua.IsDirty || Recipes.IsDirty || Sounds.Bank?.IsDirty == true;

    /// <summary>Runs <paramref name="then"/>, asking first when it would throw away unsaved changes.</summary>
    public void Discard(Action then) => ConfirmDialog.Discard(this, HasUnsavedChanges, then);

    // ---- Public operations (buttons call these; tests drive them directly) ----

    /// <summary>Starts a new part from a template in <c>ModsRoot/&lt;modId&gt;</c>. Returns an error, or null.</summary>
    public string? NewPart(string modId, string partId, string name, string template)
    {
        if (!Identifiers.IsValid(modId))
            return Loc.T("Mod id '{0}' must be lowercase snake_case.", modId);
        if (!Identifiers.IsValid(partId))
            return Loc.T("Part id '{0}' must be lowercase snake_case.", partId);
        var text = FileAccess.GetFileAsString($"{TemplatesPath}/{template}.json");
        var diagnostics = new List<DataDiagnostic>();
        var definition = ModLoader.ParseJson<PartDefinition>(text, $"{template}.json", diagnostics);
        if (definition is null)
            return Loc.T("Template '{0}' could not be read: {1}", template, string.Join("; ", diagnostics));
        SetDocument(PartDocument.New(Path.Combine(ModsRoot, modId), definition, partId, string.IsNullOrWhiteSpace(name) ? partId : name));
        return null;
    }

    /// <summary>Opens a part file. Returns an error, or null.</summary>
    public string? OpenPart(string file)
    {
        var diagnostics = new List<DataDiagnostic>();
        var document = PartDocument.Open(file, diagnostics);
        if (document is null)
            return string.Join("\n", diagnostics);
        SetDocument(document);
        return null;
    }

    /// <summary>Imports a glTF file into the part's mod and adds it as a model piece. Returns an error, or null.</summary>
    public string? ImportMesh(string file)
    {
        if (Document is null)
            return "Open or create a part first.";
        if (!AssetImporter.TryImport(file, Document.ModDirectory, AssetKind.Mesh, out var path, out var error))
            return error;
        _assets.ClearCache();
        Document.AddPiece(ModelPiece.NewMesh(path));
        SelectPiece(Document.Source.Pieces.Count - 1);
        return null;
    }

    /// <summary>Selects a model piece and shows its fields.</summary>
    public void SelectPiece(int index)
    {
        _selectedPiece = Document is not null && index >= 0 && index < Document.Source.Pieces.Count ? index : -1;
        Refresh(rebuildInspector: true);
    }

    /// <summary>Selects a socket and shows its fields.</summary>
    public void SelectSocket(int index)
    {
        _selectedSocket = Document is not null && index >= 0 && index < (Document.Definition.Sockets?.Count ?? 0) ? index : -1;
        Refresh(rebuildInspector: true);
    }

    /// <summary>
    /// Validates and writes the part into its mod. A model that needs baking is evaluated first (CSG
    /// takes two frames). Problems are listed in the problems pane either way.
    /// </summary>
    public async Task<ExportResult?> ExportAsync()
    {
        if (Document is not { } document)
            return null;
        var baked = document.Source.NeedsBake
            ? await ModelBuilder.BakeAsync(_viewport, document.Source, path => _assets.LoadMeshes(path, document.ModId))
            : null;
        var result = PartExporter.Export(document, baked);
        _assets.ClearCache();
        ShowProblems(result.Diagnostics, result.Succeeded ? Loc.T("Exported {0} to {1}.", result.QualifiedId, Display(result.PartFile!)) : Loc.T("Export failed; nothing was written."));
        return result;
    }

    /// <summary>
    /// Runs a batch file (<see cref="PartBatch"/>) into the mod that holds it, then opens the last part
    /// it made. Problems are listed in the problems pane.
    /// </summary>
    public async Task<BatchRunResult> RunBatchAsync(string file)
    {
        var result = await PartBatchRunner.RunAsync(_viewport, file);
        if (result.Exports.LastOrDefault(e => e.Succeeded) is { PartFile: { } last })
            OpenPart(last);
        var made = result.Exports.Count(e => e.Succeeded);
        ShowProblems(result.Diagnostics, result.Succeeded
            ? Loc.T("Batch {0} exported {1} parts and recipes.", Path.GetFileName(file), made)
            : Loc.T("Batch {0} failed; {1} of {2} parts and recipes exported.", Path.GetFileName(file), made, result.Exports.Count));
        return result;
    }

    /// <summary>Exports, then asks to test the part in the sandbox.</summary>
    public async Task TestInSandboxAsync()
    {
        if (await ExportAsync() is { Succeeded: true } result)
            TestRequested?.Invoke(result.QualifiedId);
    }

    /// <summary>Shows one of the docs (<c>lua-api.md</c>) in the help window.</summary>
    public void OpenHelp(string fileName) => _help.Open(fileName);

    // The Handbook key (H or F1) opens and closes the help, as everywhere in the game.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (GameInput.Pressed(@event, InputActions.Handbook) && IsVisibleInTree())
        {
            _help.Toggle("getting-started.md");
            GetViewport().SetInputAsHandled();
        }
    }

    // ---- Building the UI ----

    private void BuildTabs()
    {
        _partGrid = AddTab("Part");

        var model = AddTabBox("Model");
        _pieces = Loc.Verbatim(new ItemList { CustomMinimumSize = new Vector2(0, 140) });
        _pieces.ItemSelected += i => SelectPiece((int)i);
        model.AddChild(_pieces);
        var add = new HFlowContainer();
        model.AddChild(add);
        AddButton(add, "+ Box", () => AddPiece(ModelPiece.NewBox(Footprint())));
        AddButton(add, "+ Wedge", () => AddPiece(new ModelPiece { Kind = ModelPieceKind.Wedge, Size = Footprint() }));
        AddButton(add, "+ Cylinder", () => AddPiece(new ModelPiece { Kind = ModelPieceKind.Cylinder, Radius = MinHalf(), Height = Footprint().Y }));
        AddButton(add, "+ Sphere", () => AddPiece(new ModelPiece { Kind = ModelPieceKind.Sphere, Radius = MinHalf() }));
        AddButton(add, "Import mesh…", () => Import(AssetKind.Mesh, path => ShowError(ImportMesh(path))));
        var order = new HBoxContainer();
        model.AddChild(order);
        AddButton(order, "Up", () => MovePiece(-1));
        AddButton(order, "Down", () => MovePiece(1));
        AddButton(order, "Duplicate", () =>
        {
            if (Document is not null && _selectedPiece >= 0)
                AddPiece(Document.Source.Pieces[_selectedPiece]);
        });
        AddButton(order, "Remove", () =>
        {
            if (Document is null || _selectedPiece < 0)
                return;
            var index = _selectedPiece;
            _selectedPiece = -1;
            Document.RemovePiece(index);
        });
        _pieceGrid = new PropertyGrid();
        model.AddChild(_pieceGrid);

        _collisionGrid = AddTab("Collision");

        var sockets = AddTabBox("Sockets");
        _sockets = Loc.Verbatim(new ItemList { CustomMinimumSize = new Vector2(0, 120) });
        _sockets.ItemSelected += i => SelectSocket((int)i);
        sockets.AddChild(_sockets);
        var socketButtons = new HFlowContainer();
        sockets.AddChild(socketButtons);
        var side = Loc.Verbatim(new OptionButton());
        foreach (var name in new[] { "+X", "-X", "+Y", "-Y", "+Z", "-Z" })
            side.AddItem(name);
        side.Selected = 2;
        AddButton(socketButtons, "+ Socket on side", () => AddSideSocket(Core.Creations.GridCell.Directions[side.Selected]));
        socketButtons.AddChild(side);
        AddButton(socketButtons, "Snap to face", SnapSelectedSocket);
        AddButton(socketButtons, "Remove", () =>
        {
            if (Document is null || _selectedSocket < 0)
                return;
            var index = _selectedSocket;
            _selectedSocket = -1;
            Document.RemoveSocket(index);
        });
        _socketGrid = new PropertyGrid();
        sockets.AddChild(_socketGrid);

        _lookGrid = AddTab("Look");
        _behaviourGrid = AddTab("Behaviour");
        Recipes = new RecipePanel { Name = "RecipePanel", Editor = this, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddTabBox("Recipes").AddChild(Recipes);
        // The script editor fills its tab instead of scrolling with it.
        Lua = new LuaPanel { Name = "Script", Editor = this };
        _tabs.AddChild(Lua);

        _sounds = new SoundBinderPanel { Name = "Binder", ModsRoot = () => ModsRoot, Import = Import, ShowError = ShowError };
        AddTabBox("Sounds").AddChild(_sounds);
        _sounds.RegistryChanged += () => Refresh(rebuildInspector: false);
    }

    private void BuildDialogs()
    {
        _message = new AcceptDialog { Title = "Part Editor" };
        AddChild(_message);
        _help = new HelpWindow { Visible = false };
        AddChild(_help);

        _openDialog = new FileDialog
        {
            Title = "Open part",
            ModeOverridesTitle = false,
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Filters = ["*.json ; Part files"],
            UseNativeDialog = false,
        };
        _openDialog.FileSelected += path => ShowError(OpenPart(path));
        AddChild(_openDialog);

        _batchDialog = new FileDialog
        {
            Title = "Run batch",
            ModeOverridesTitle = false,
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Filters = ["*.json ; Batch files"],
            CurrentDir = ModsRoot,
            UseNativeDialog = false,
        };
        _batchDialog.FileSelected += path => ConfirmDialog.Ask(this, "Run batch",
            Loc.T("Run {0}? It writes every part and recipe it lists into its mod, replacing the files already there.", Path.GetFileName(path)),
            "Run batch", () => Discard(() => _ = RunBatchAsync(path)));
        AddChild(_batchDialog);

        _importDialog = new FileDialog
        {
            Title = "Import",
            ModeOverridesTitle = false,
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            UseNativeDialog = false,
        };
        _importDialog.FileSelected += path => _importTarget?.Invoke(path);
        AddChild(_importDialog);

        _newDialog = new ConfirmationDialog { Title = "New part" };
        var grid = new GridContainer { Columns = 2 };
        _newDialog.AddChild(grid);
        var mod = new LineEdit { Text = "my_mod", CustomMinimumSize = new Vector2(240, 0), TooltipText = "Folder name in the mods folder; created if missing" };
        var id = new LineEdit { Text = "new_part" };
        var name = new LineEdit { Text = "New Part" };
        var template = Loc.Verbatim(new OptionButton());
        foreach (var t in TemplateNames)
            template.AddItem(t);
        foreach (var (label, control) in new (string, Control)[] { ("Mod", mod), ("Part id", id), ("Name", name), ("Template", template) })
        {
            grid.AddChild(new Label { Text = label });
            grid.AddChild(control);
        }
        _newDialog.Confirmed += () =>
        {
            var (modId, partId) = (mod.Text.Trim(), id.Text.Trim());
            void Start() => ShowError(NewPart(modId, partId, name.Text.Trim(), template.GetItemText(template.Selected)));
            if (Identifiers.IsValid(modId) && PartExporter.ExistingPartFile(Path.Combine(ModsRoot, modId), partId) is { } existing)
                ConfirmDialog.Ask(this, "Part exists", Loc.T("{0} already has {1}. Start a new part anyway? Exporting it replaces that file.", modId, existing), "Start new", Start);
            else
                Start();
        };
        AddChild(_newDialog);
    }

    // ---- Refreshing from the document ----

    private void SetDocument(PartDocument document)
    {
        if (Document is not null)
            Document.Changed -= OnDocumentChanged;
        Document = document;
        Document.Changed += OnDocumentChanged;
        _selectedPiece = document.Source.Pieces.Count > 0 ? 0 : -1;
        _selectedSocket = (document.Definition.Sockets?.Count ?? 0) > 0 ? 0 : -1;
        _selectedVariant = -1;
        _assets = new PartAssets(modId => Path.Combine(Path.GetDirectoryName(document.ModDirectory)!, modId));
        _openDialog.CurrentDir = document.ModDirectory;
        _preview.Frame(document.Definition.Size ?? GridSize.One);
        _sounds.SetPart(document);
        Refresh(rebuildInspector: true);
        Recipes.Reload();
        Lua.Reload();
    }

    private void OnDocumentChanged()
    {
        if (Document is null)
            return;
        // Undo can remove the selected item.
        if (_selectedPiece >= Document.Source.Pieces.Count)
            _selectedPiece = Document.Source.Pieces.Count - 1;
        if (_selectedSocket >= (Document.Definition.Sockets?.Count ?? 0))
            _selectedSocket = (Document.Definition.Sockets?.Count ?? 0) - 1;
        Refresh(rebuildInspector: !_quiet);
        Lua.Reload();
    }

    // An edit from a field: the field already shows the new value, so the inspector is not rebuilt under it.
    private void Edit(Action edit)
    {
        _quiet = true;
        try
        {
            edit();
        }
        finally
        {
            _quiet = false;
        }
    }

    private void Refresh(bool rebuildInspector)
    {
        var doc = Document;
        _undo.Disabled = doc?.CanUndo != true;
        _redo.Disabled = doc?.CanRedo != true;
        _export.Disabled = _test.Disabled = doc is null;
        _undo.TooltipText = doc?.UndoLabel is { } u ? Loc.T("Undo {0} (Ctrl+Z)", u) : "Ctrl+Z";
        _redo.TooltipText = doc?.RedoLabel is { } r ? Loc.T("Redo {0} (Ctrl+Shift+Z)", r) : "Ctrl+Shift+Z";
        _title.Text = doc is null
            ? Loc.T("No part open: New or Open")
            : doc.IsDirty
                ? Loc.T("{0}  ({1})  • unsaved", $"{doc.ModId}:{doc.Definition.Id}", doc.RelativeFile ?? Loc.T("not exported yet"))
                : $"{doc.ModId}:{doc.Definition.Id}  ({doc.RelativeFile ?? Loc.T("not exported yet")})";
        if (doc is null)
        {
            _problems.Text = Loc.T("Start with New (from a template) or Open (a part file in a mod). Press Help for the Getting Started guide.");
            return;
        }

        _preview.Show(doc.Definition, doc.Source, _assets, doc.ModId, _selectedSocket);
        ShowProblems([.. doc.Validate(), .. _sounds.Registry.CheckPart(doc.Definition, doc.ModId, doc.RelativeFile ?? PartExporter.TargetFile(doc))], null);
        RefreshLists();
        if (rebuildInspector)
        {
            RebuildInspector();
            _sounds.RebuildBindings();
        }
    }

    private void RefreshLists()
    {
        var doc = Document!;
        _pieces.Clear();
        for (var i = 0; i < doc.Source.Pieces.Count; i++)
        {
            var p = doc.Source.Pieces[i];
            var op = (p.Operation ?? CsgOperation.Union) switch { CsgOperation.Subtract => "− ", CsgOperation.Intersect => "∩ ", _ => "+ " };
            _pieces.AddItem($"{i + 1}. {op}{p.Kind?.ToString().ToLowerInvariant()}{(p.Mesh is { } m ? $" ({m})" : "")}");
        }
        if (_selectedPiece >= 0)
            _pieces.Select(_selectedPiece);
        _sockets.Clear();
        foreach (var s in doc.Definition.Sockets ?? [])
            _sockets.AddItem($"{s.Id} ({s.Type?.ToString().ToLowerInvariant()})");
        if (_selectedSocket >= 0)
            _sockets.Select(_selectedSocket);
    }

    private void RebuildInspector()
    {
        foreach (var grid in new[] { _partGrid, _pieceGrid, _collisionGrid, _socketGrid, _lookGrid, _behaviourGrid })
            grid.Clear();
        var doc = Document!;
        var d = doc.Definition;

        // Part: identity, physics and footprint.
        _partGrid.AddHeading("Identity");
        _partGrid.AddText("Id", d.Id, v => Set("Rename", x => x with { Id = v }), "snake_case");
        _partGrid.AddText("Name", d.Name, v => Set("Set name", x => x with { Name = v }));
        _partGrid.AddText("Description", d.Description, v => Set("Set description", x => x with { Description = v }));
        _partGrid.AddEnum<PartCategory>("Category", d.Category, v => Set("Set category", x => x with { Category = v }));
        _partGrid.AddHeading("Physics (SI units)");
        _partGrid.AddNumber("Mass", d.Mass, 0.001, 1e6, 0.1, v => Set("Set mass", x => x with { Mass = (float?)v }), optional: !d.IsPlaceable, fallback: 1, suffix: "kg");
        _partGrid.AddNumber("Durability", d.Durability, 1, 100, 1, v => Set("Set durability", x => x with { Durability = (int?)v }), optional: !d.IsPlaceable, fallback: 1);
        _partGrid.AddNumber("Friction", d.Friction, 0, 10, 0.05, v => Set("Set friction", x => x with { Friction = (float?)v }), optional: !d.IsPlaceable, fallback: 0.5);
        _partGrid.AddNumber("Buoyancy", d.Buoyancy, 0, 100, 0.05, v => Set("Set buoyancy", x => x with { Buoyancy = (float?)v }), optional: !d.IsPlaceable, fallback: 0.5);
        _partGrid.AddBool("Flammable", d.Flammable, v => Set("Set flammable", x => x with { Flammable = v }), optional: !d.IsPlaceable);
        _partGrid.AddNumber("Stack size", d.StackSize, 1, 1e6, 1, v => Set("Set stack size", x => x with { StackSize = (int?)v }), optional: true, fallback: 1);
        _partGrid.AddText("Sound material", d.SoundMaterial, v => Set("Set sound material", x => x with { SoundMaterial = v }), "wood, metal, concrete…");
        if (d.IsPlaceable)
            _partGrid.AddVector("Centre of mass (m)", d.CentreOfMass ?? Vec3.Zero, PositionStep / 2,
                v => Set("Move centre of mass", x => x with { CentreOfMass = v == Vec3.Zero ? null : v }), -32, 32);
        if (d.IsPlaceable)
        {
            _partGrid.AddHeading("Footprint (0.25 m cells)");
            _partGrid.AddGridSize("Size", d.Size ?? GridSize.One, v => Set("Resize", x => x with { Size = v }));
        }
        AddAssetField(_partGrid, "Icon", d.Icon, AssetKind.Texture, v => Set("Set icon", x => x with { Icon = v }));
        if (d.IsPlaceable)
            AddStorageFields(d);
        AddSurvivalFields(d);
        if (d.IsPlaceable)
        {
            AddLogicFields(d);
            AddLightFields(d);
            AddAnimationFields(d);
        }

        // Model: the selected piece.
        if (_selectedPiece >= 0)
        {
            var piece = doc.Source.Pieces[_selectedPiece];
            var index = _selectedPiece;
            void SetPiece(string label, Func<ModelPiece, ModelPiece> change) =>
                Edit(() => doc.SetPiece(index, change(doc.Source.Pieces[index]), label));
            _pieceGrid.AddHeading(Loc.T("Piece {0}: {1}", index + 1, piece.Kind?.ToString().ToLowerInvariant()));
            _pieceGrid.AddEnum<CsgOperation>("Operation", piece.Operation ?? CsgOperation.Union,
                v => SetPiece("Set operation", p => p with { Operation = v == CsgOperation.Union ? null : v }));
            switch (piece.Kind)
            {
                case ModelPieceKind.Mesh:
                    AddAssetField(_pieceGrid, "Mesh", piece.Mesh, AssetKind.Mesh, v => SetPiece("Set mesh", p => p with { Mesh = v }));
                    break;
                case ModelPieceKind.Box or ModelPieceKind.Wedge:
                    _pieceGrid.AddVector("Size (m)", piece.Size ?? Footprint(), PositionStep, v => SetPiece("Resize piece", p => p with { Size = v }), 0.001, 64);
                    break;
                case ModelPieceKind.Cylinder:
                    _pieceGrid.AddNumber("Radius", piece.Radius, 0.001, 64, PositionStep / 2, v => SetPiece("Set radius", p => p with { Radius = (float?)v }), suffix: "m");
                    _pieceGrid.AddNumber("Height", piece.Height, 0.001, 64, PositionStep, v => SetPiece("Set height", p => p with { Height = (float?)v }), suffix: "m");
                    break;
                case ModelPieceKind.Sphere:
                    _pieceGrid.AddNumber("Radius", piece.Radius, 0.001, 64, PositionStep / 2, v => SetPiece("Set radius", p => p with { Radius = (float?)v }), suffix: "m");
                    break;
            }
            _pieceGrid.AddHeading("Transform");
            _pieceGrid.AddVector("Position (m)", piece.Position ?? Vec3.Zero, PositionStep,
                v => SetPiece("Move piece", p => p with { Position = v == Vec3.Zero ? null : v }));
            _pieceGrid.AddVector("Rotation (°)", piece.Rotation ?? Vec3.Zero, 15,
                v => SetPiece("Rotate piece", p => p with { Rotation = v == Vec3.Zero ? null : v }), -360, 360);
            _pieceGrid.AddNumber("Scale", piece.Scale ?? 1, 0.01, 100, 0.05,
                v => SetPiece("Scale piece", p => p with { Scale = v is 1 or null ? null : (float?)v }));
        }

        // Collision.
        var c = d.Collision;
        _collisionGrid.AddEnum<CollisionShape>("Shape", c?.Shape, v => Set("Set collision shape",
            x => x with { Collision = v is null ? null : DefaultCollision(v.Value, x) }), optional: true);
        _collisionGrid.AddChild(new Label { Text = c is null ? "Not set: a box filling the footprint." : "", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _collisionGrid.AddChild(new Control());
        if (c is not null)
        {
            void SetCollision(string label, Func<CollisionDefinition, CollisionDefinition> change) =>
                Set(label, x => x with { Collision = change(x.Collision!) });
            if (c.Shape == CollisionShape.Box)
                _collisionGrid.AddVector("Size (m)", c.Size ?? Footprint(), PositionStep, v => SetCollision("Resize collision", x => x with { Size = v }), 0.001, 64);
            if (c.Shape is CollisionShape.Sphere or CollisionShape.Cylinder or CollisionShape.Capsule)
                _collisionGrid.AddNumber("Radius", c.Radius, 0.001, 64, PositionStep / 2, v => SetCollision("Set collision radius", x => x with { Radius = (float?)v }), suffix: "m");
            if (c.Shape is CollisionShape.Cylinder or CollisionShape.Capsule)
                _collisionGrid.AddNumber("Height", c.Height, 0.001, 64, PositionStep, v => SetCollision("Set collision height", x => x with { Height = (float?)v }), suffix: "m");
            if (c.Shape == CollisionShape.Convex)
                AddAssetField(_collisionGrid, "Mesh", c.Mesh, AssetKind.Mesh, v => SetCollision("Set collision mesh", x => x with { Mesh = v }));
            _collisionGrid.AddVector("Offset (m)", c.Offset ?? Vec3.Zero, PositionStep,
                v => SetCollision("Move collision", x => x with { Offset = v == Vec3.Zero ? null : v }));
        }

        // Sockets: the selected one.
        if (_selectedSocket >= 0)
        {
            var socket = d.Sockets![_selectedSocket];
            var index = _selectedSocket;
            void SetSocket(string label, Func<SocketDefinition, SocketDefinition> change) =>
                Edit(() => doc.SetSocket(index, change(doc.Definition.Sockets![index]), label));
            _socketGrid.AddText("Id", socket.Id, v => SetSocket("Rename socket", s => s with { Id = v }));
            _socketGrid.AddEnum<SocketType>("Type", socket.Type, v => SetSocket("Set socket type", s => s with { Type = v }));
            _socketGrid.AddVector("Position (m)", socket.Position ?? Vec3.Zero, PositionStep / 2, v => SetSocket("Move socket", s => s with { Position = v }));
            _socketGrid.AddVector("Normal", socket.Normal ?? new Vec3(0, 1, 0), 1, v => SetSocket("Turn socket", s => s with { Normal = v }), -1, 1);
            if (socket.Type is SocketType.Rotational or SocketType.Linear or SocketType.Spring)
                AddJointFields(socket, change => SetSocket("Edit joint", s => s with { Joint = Normalize(change(s.Joint ?? new JointDefinition())) }));
        }

        // Look: colour, surface, variants.
        var look = d.Visual ?? new VisualDefinition();
        void SetLook(string label, Func<VisualDefinition, VisualDefinition> change) =>
            Set(label, x => x with { Visual = Normalize(change(x.Visual ?? new VisualDefinition())) });
        _lookGrid.AddColor("Colour", look.Color, v => SetLook("Set colour", x => x with { Color = v }));
        _lookGrid.AddNumber("Roughness", look.Roughness, 0, 1, 0.05, v => SetLook("Set roughness", x => x with { Roughness = (float?)v }), optional: true, fallback: 0.8);
        _lookGrid.AddNumber("Metallic", look.Metallic, 0, 1, 0.05, v => SetLook("Set metallic", x => x with { Metallic = (float?)v }), optional: true);
        _lookGrid.AddNumber("Opacity", look.Opacity, 0, 1, 0.05, v => SetLook("Set opacity", x => x with { Opacity = (float?)v }), optional: true, fallback: 1);
        _lookGrid.AddColor("Glow", look.Emission, v => SetLook("Set glow", x => x with { Emission = v }));
        AddAssetField(_lookGrid, "Texture", look.Texture, AssetKind.Texture, v => SetLook("Set texture", x => x with { Texture = v }));
        _lookGrid.AddBool("Paintable", look.Paintable, v => SetLook("Set paintable", x => x with { Paintable = v }), optional: true);
        _lookGrid.AddHeading("Colour variants");
        var variants = look.Variants ?? [];
        for (var i = 0; i < variants.Count; i++)
        {
            var index = i;
            var variant = variants[i];
            void SetVariant(Func<ColorVariant, ColorVariant> change) =>
                Edit(() => doc.SetVariant(index, change(doc.Definition.Visual!.Variants![index])));
            _lookGrid.AddText(Loc.T("Variant {0} id", i + 1), variant.Id, v => SetVariant(x => x with { Id = v }));
            _lookGrid.AddText(Loc.T("Variant {0} name", i + 1), variant.Name, v => SetVariant(x => x with { Name = v }));
            _lookGrid.AddColor(Loc.T("Variant {0} colour", i + 1), variant.Color, v => SetVariant(x => x with { Color = v }));
            var remove = new Button { Text = Loc.T("Remove variant {0}", i + 1) };
            remove.Pressed += () => doc.RemoveVariant(index);
            _lookGrid.Row("", remove);
        }
        var addVariant = new Button { Text = "+ Variant" };
        addVariant.Pressed += () => doc.AddVariant(new ColorVariant
        {
            Id = $"variant_{variants.Count + 1}",
            Name = $"Variant {variants.Count + 1}",
            Color = look.Color ?? "#ffffff",
        });
        _lookGrid.Row("", addVariant);

        // Behaviour: script, sounds and free-form properties.
        AddAssetField(_behaviourGrid, "Script", d.Script, AssetKind.Script, v => Set("Set script", x => x with { Script = v }));
        _behaviourGrid.Row("Sounds", new Label
        {
            Text = d.Sounds is { Count: > 0 } bound ? Loc.T("{0}: bound in the Sounds tab", string.Join(", ", bound.Keys.Order(StringComparer.Ordinal))) : "None bound: see the Sounds tab",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _behaviourGrid.AddJson("Properties", d.Properties?.ToJsonString(DataJson.WriteOptions) ?? "{}", text =>
        {
            try
            {
                var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (node is not JsonObject properties)
                    return "Properties must be a JSON object.";
                Set("Set properties", x => x with { Properties = properties.Count > 0 ? properties : null });
                return null;
            }
            catch (JsonException e)
            {
                return e.Message;
            }
        });
    }

    // Inventory and crafting station fields (chunk 18). Turning either on or off rebuilds the inspector.
    private void AddStorageFields(PartDefinition d)
    {
        _partGrid.AddHeading("Storage and crafting");
        _partGrid.AddNumber("Inventory slots", d.Inventory?.Slots, 1, 256, 1, v =>
        {
            if ((v is null) != (Document?.Definition.Inventory is null))
                Document?.Apply(v is null ? "Remove inventory" : "Add inventory", x => x with { Inventory = v is null ? null : new InventoryDefinition { Slots = (int)v } });
            else if (v is not null)
                Set("Set inventory slots", x => x with { Inventory = x.Inventory! with { Slots = (int)v } });
        }, optional: true, fallback: 10);
        if (d.Inventory is { } inventory)
        {
            _partGrid.AddText("Accepts", inventory.Accepts is { } accepts ? string.Join(", ", accepts) : null,
                v => Set("Set accepted items", x => x with { Inventory = x.Inventory! with { Accepts = SplitIds(v) } }), "any item, or ids: gasoline, crude_oil");
        }
        _partGrid.AddBool("Crafting station", d.Crafting is not null,
            v => Document?.Apply(v == true ? "Make crafting station" : "Remove crafting", x => x with { Crafting = v == true ? x.Crafting ?? new CraftingDefinition() : null }));
        if (d.Crafting is { } crafting)
        {
            _partGrid.AddNumber("Crafting speed", crafting.Speed, 0.01, 100, 0.05,
                v => Set("Set crafting speed", x => x with { Crafting = x.Crafting! with { Speed = (float?)v } }), optional: true, fallback: 1, suffix: "×");
            _partGrid.AddNumber("Queue size", crafting.QueueSize, 1, 64, 1,
                v => Set("Set queue size", x => x with { Crafting = x.Crafting! with { QueueSize = (int?)v } }), optional: true, fallback: 1);
        }
    }

    // Survival fields (chunk 20): seeds and water on items; soil and beds on parts. Weapons and food are
    // item scripts (Behaviour: Script and Properties). Turning a section on or off rebuilds the inspector.
    private void AddSurvivalFields(PartDefinition d)
    {
        _partGrid.AddHeading("Survival");
        void Toggle(string what, bool on, Func<PartDefinition, PartDefinition> add, Func<PartDefinition, PartDefinition> remove) =>
            Document?.Apply(on ? $"Add {what}" : $"Remove {what}", x => on ? add(x) : remove(x));
        if (d.IsPlaceable)
        {
            _partGrid.AddBool("Soil", d.Soil is not null, v => Toggle("soil", v == true,
                x => x with { Soil = x.Soil ?? new SoilDefinition { Capacity = 300 } }, x => x with { Soil = null }));
            if (d.Soil is { } soil)
            {
                _partGrid.AddNumber("Water capacity", soil.Capacity, 1, 1e5, 10,
                    v => Set("Set water capacity", x => x with { Soil = x.Soil! with { Capacity = (float?)v } }), optional: true, suffix: "s");
                _partGrid.AddNumber("Growth speed", soil.Growth, 0.01, 100, 0.05,
                    v => Set("Set growth speed", x => x with { Soil = x.Soil! with { Growth = (float?)v } }), optional: true, fallback: 1, suffix: "×");
            }
            _partGrid.AddBool("Respawn point", d.RespawnPoint, v => Set("Set respawn point", x => x with { RespawnPoint = v == true ? true : null }), optional: true);
            return;
        }
        _partGrid.AddBool("Seed", d.Seed is not null, v => Toggle("seed", v == true,
            x => x with { Seed = x.Seed ?? new SeedDefinition { GrowTime = 240, Yield = 1 } }, x => x with { Seed = null }));
        if (d.Seed is { } seed)
        {
            void SetSeed(string label, Func<SeedDefinition, SeedDefinition> change) => Set(label, x => x with { Seed = change(x.Seed!) });
            _partGrid.AddText("Grows into", seed.Crop, v => SetSeed("Set crop", x => x with { Crop = v }), "item id: carrot");
            _partGrid.AddNumber("Grow time", seed.GrowTime, 1, 1e6, 10, v => SetSeed("Set grow time", x => x with { GrowTime = (float?)v }), suffix: "s");
            _partGrid.AddNumber("Yield", seed.Yield, 1, 1000, 1, v => SetSeed("Set yield", x => x with { Yield = (int?)v }), optional: true, fallback: 1);
            _partGrid.AddNumber("Seeds back", seed.Seeds, 0, 1000, 1, v => SetSeed("Set seeds back", x => x with { Seeds = (int?)v }), optional: true);
            _partGrid.AddNumber("Raid value", seed.Value, 0, 1000, 0.5, v => SetSeed("Set raid value", x => x with { Value = (float?)v }), optional: true, fallback: 1);
        }
    }

    // Logic and power fields (chunk 10): Connect Tool links in and out, a seat, and the settings players
    // change on each placed part (edited as JSON, like Properties).
    private void AddLogicFields(PartDefinition d)
    {
        _partGrid.AddHeading("Logic and seats");
        void SetLogic(string label, Func<LogicDefinition, LogicDefinition> change) => Set(label, x =>
        {
            var logic = change(x.Logic ?? new LogicDefinition());
            return x with { Logic = logic.Inputs is null && logic.Outputs is null && logic.Use is null ? null : logic };
        });
        _partGrid.AddNumber("Input links", d.Logic?.Inputs, 0, LogicDefinition.MaxLinks, 1,
            v => SetLogic("Set input links", l => l with { Inputs = (int?)v }), optional: true, fallback: 1);
        _partGrid.AddNumber("Output links", d.Logic?.Outputs, 0, LogicDefinition.MaxLinks, 1,
            v => SetLogic("Set output links", l => l with { Outputs = (int?)v }), optional: true, fallback: 10);
        _partGrid.AddText(Loc.T("Use ({0})", GameInput.Label(InputActions.Interact)), d.Logic?.Use, v => SetLogic("Set use", l => l with { Use = string.IsNullOrWhiteSpace(v) ? null : v }), "toggle, press…");
        _partGrid.AddBool("Seat", d.Seat is not null, v => Document?.Apply(v == true ? "Make seat" : "Remove seat",
            x => x with { Seat = v == true ? x.Seat ?? new SeatDefinition() : null }));
        if (d.Seat is { } seat)
        {
            _partGrid.AddNumber("Seat height", seat.Offset?.Y, -2, 2, 0.05,
                v => Set("Set seat height", x => x with { Seat = x.Seat! with { Offset = v is null ? null : new Vec3(0, (float)v, 0) } }), optional: true, suffix: "m");
        }
        _partGrid.AddJson("Settings", d.Settings is null ? "[]" : JsonSerializer.Serialize(d.Settings, DataJson.WriteOptions), text =>
        {
            try
            {
                var settings = JsonSerializer.Deserialize<List<SettingDefinition>>(text, DataJson.ReadOptions);
                Set("Set settings", x => x with { Settings = settings is { Count: > 0 } ? settings : null });
                return null;
            }
            catch (JsonException e)
            {
                return e.Message;
            }
        });
    }

    // Light fields (chunk 23): what the part's light looks like at full level. Its script turns it on.
    private void AddLightFields(PartDefinition d)
    {
        _partGrid.AddHeading("Light");
        _partGrid.AddBool("Gives light", d.Light is not null, v => Document?.Apply(v == true ? "Add light" : "Remove light",
            x => x with { Light = v == true ? x.Light ?? new LightDefinition { Range = 6 } : null }));
        if (d.Light is not { } light)
            return;
        void SetLight(string label, Func<LightDefinition, LightDefinition> change) => Set(label, x => x with { Light = change(x.Light!) });
        _partGrid.AddEnum("Kind", light.Kind, v => SetLight("Set light kind", l => l with { Kind = v }), optional: true);
        _partGrid.AddColor("Colour", light.Color, v => SetLight("Set light colour", l => l with { Color = v }));
        _partGrid.AddNumber("Energy", light.Energy, 0, 100, 0.1, v => SetLight("Set light energy", l => l with { Energy = (float?)v }), optional: true, fallback: 1);
        _partGrid.AddNumber("Range", light.Range, 0.1, 500, 0.5, v => SetLight("Set light range", l => l with { Range = (float?)v }), suffix: "m");
        if (light.Kind == LightKind.Spot)
            _partGrid.AddNumber("Spot angle", light.Angle, 1, 89, 1, v => SetLight("Set spot angle", l => l with { Angle = (float?)v }), optional: true, fallback: 30, suffix: "°");
        _partGrid.AddVector("Offset", light.Offset ?? Vec3.Zero, 0.05, v => SetLight("Move light", l => l with { Offset = v == Vec3.Zero ? null : v }), -2, 2);
        if (light.Kind == LightKind.Spot)
            _partGrid.AddVector("Direction", light.Direction ?? new Vec3(0, 0, -1), 0.1, v => SetLight("Aim light", l => l with { Direction = v }), -1, 1);
    }

    // Animation fields (chunk 23): the axis the part's script turns its model about with part.animate.
    private void AddAnimationFields(PartDefinition d)
    {
        _partGrid.AddHeading("Animation");
        _partGrid.AddBool("Animated", d.Animation is not null, v => Document?.Apply(v == true ? "Animate" : "Stop animating",
            x => x with { Animation = v == true ? x.Animation ?? new AnimationDefinition() : null }));
        if (d.Animation is not { } animation)
            return;
        void SetAnimation(string label, Func<AnimationDefinition, AnimationDefinition> change) => Set(label, x => x with { Animation = change(x.Animation!) });
        _partGrid.AddVector("Axis", animation.Axis ?? new Vec3(0, 1, 0), 0.1, v => SetAnimation("Set animation axis", a => a with { Axis = v }), -1, 1);
        _partGrid.AddVector("Pivot", animation.Pivot ?? Vec3.Zero, 0.05, v => SetAnimation("Move animation pivot", a => a with { Pivot = v == Vec3.Zero ? null : v }), -2, 2);
    }

    private static List<string>? SplitIds(string? text) =>
        text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } ids ? [.. ids] : null;

    // ---- Edits ----

    private void Set(string label, Func<PartDefinition, PartDefinition> change) => Edit(() => Document?.Apply(label, change));

    private void AddPiece(ModelPiece piece)
    {
        if (Document is null)
            return;
        Document.AddPiece(piece);
        SelectPiece(Document.Source.Pieces.Count - 1);
    }

    private void MovePiece(int offset)
    {
        if (Document is null || _selectedPiece < 0)
            return;
        var target = Math.Clamp(_selectedPiece + offset, 0, Document.Source.Pieces.Count - 1);
        Document.MovePiece(_selectedPiece, offset);
        SelectPiece(target);
    }

    private void AddSideSocket(Core.Creations.GridCell direction)
    {
        if (Document is null)
            return;
        var size = Document.Definition.Size ?? GridSize.One;
        Document.AddSocket(SocketTools.SideSocket(size, direction, SocketTools.UnusedSocketId(Document.Definition), SocketType.Structural));
        SelectSocket(Document.Definition.Sockets!.Count - 1);
    }

    private void SnapSelectedSocket()
    {
        if (Document is null || _selectedSocket < 0)
            return;
        var socket = Document.Definition.Sockets![_selectedSocket];
        var (position, normal) = SocketTools.SnapToFace(Document.Definition.Size ?? GridSize.One, socket.Position ?? Vec3.Zero, socket.Normal ?? new Vec3(0, 1, 0));
        Document.SetSocket(_selectedSocket, socket with { Position = position, Normal = normal }, "Snap socket");
    }

    private void AddAssetField(PropertyGrid grid, string label, string? value, AssetKind kind, Action<string?> set)
    {
        var box = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var edit = Loc.Verbatim(new LineEdit { Text = value ?? "", PlaceholderText = $"{AssetImporter.FolderFor(kind)}/…", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        void Commit()
        {
            var text = edit.Text.Trim();
            if (text != (value ?? ""))
                set(text.Length == 0 ? null : text);
        }
        edit.TextSubmitted += _ => Commit();
        edit.FocusExited += Commit;
        box.AddChild(edit);
        var import = new Button { Text = "Import…" };
        import.Pressed += () => Import(kind, path =>
        {
            if (Document is null)
                return;
            if (AssetImporter.TryImport(path, Document.ModDirectory, kind, out var relative, out var error))
            {
                _assets.ClearCache();
                set(relative);
            }
            else
            {
                ShowError(error);
            }
        });
        box.AddChild(import);
        grid.Row(label, box);
    }

    private void Import(AssetKind kind, Action<string> then)
    {
        _importTarget = then;
        _importDialog.Filters = [$"{string.Join(", ", AssetPath.ExtensionsFor(kind).Select(e => "*" + e))} ; {kind}"];
        _importDialog.PopupCentered(new Vector2I(900, 600));
    }

    private Vec3 Footprint() => (Document?.Definition.Size ?? GridSize.One).Metres;

    private float MinHalf()
    {
        var f = Footprint();
        return MathF.Min(f.X, f.Z) / 2;
    }

    private static CollisionDefinition DefaultCollision(CollisionShape shape, PartDefinition part)
    {
        var size = (part.Size ?? GridSize.One).Metres;
        var radius = MathF.Min(size.X, size.Z) / 2;
        return shape switch
        {
            CollisionShape.Box => new CollisionDefinition { Shape = shape, Size = size },
            CollisionShape.Sphere => new CollisionDefinition { Shape = shape, Radius = MathF.Min(radius, size.Y / 2) },
            CollisionShape.Capsule => new CollisionDefinition { Shape = shape, Radius = MathF.Min(radius, size.Y / 2), Height = size.Y },
            CollisionShape.Convex => new CollisionDefinition { Shape = shape, Mesh = part.Mesh },
            _ => new CollisionDefinition { Shape = shape, Radius = radius, Height = size.Y },
        };
    }

    // An empty visual block is dropped from the data instead of writing "visual": {}.
    // The joint section of a rotational, linear or spring socket: axis, travel and (for springs) rate and damping.
    private void AddJointFields(SocketDefinition socket, Action<Func<JointDefinition, JointDefinition>> set)
    {
        var joint = socket.Joint ?? new JointDefinition();
        var rotational = socket.Type == SocketType.Rotational;
        var normal = socket.Normal ?? new Vec3(0, 1, 0);
        _socketGrid.AddHeading(socket.Type switch
        {
            SocketType.Rotational => "Joint: hinge (no limits spins freely)",
            SocketType.Linear => "Joint: slider (piston)",
            _ => "Joint: spring (suspension)",
        });
        _socketGrid.AddVector("Axis", joint.Axis ?? normal, 1, v => set(j => j with { Axis = v == normal ? null : v }), -1, 1);
        var (limit, unit, step) = rotational ? (Math.PI, "rad", 0.05) : ((double)JointDefinition.MaxTravel, "m", PositionStep / 5);
        _socketGrid.AddNumber("Min", joint.Min, -limit, 0, step, v => set(j => j with { Min = (float?)v }), optional: true, suffix: unit);
        _socketGrid.AddNumber("Max", joint.Max, 0, limit, step, v => set(j => j with { Max = (float?)v }), optional: true, suffix: unit);
        if (socket.Type == SocketType.Spring)
        {
            _socketGrid.AddNumber("Stiffness", joint.Stiffness, 0.001, 1e7, 100, v => set(j => j with { Stiffness = (float?)v }), optional: true, fallback: 5000, suffix: "N/m");
            _socketGrid.AddNumber("Damping", joint.Damping, 0, 1e7, 10, v => set(j => j with { Damping = (float?)v }), optional: true, fallback: 300, suffix: "N·s/m");
        }
        if (!rotational)
            _socketGrid.AddNumber("Shaft radius", joint.ShaftRadius, 0.001, 1, 0.005, v => set(j => j with { ShaftRadius = (float?)v }), optional: true, fallback: 0.04, suffix: "m");
    }

    private static JointDefinition? Normalize(JointDefinition joint) =>
        joint is { Axis: null, Min: null, Max: null, Stiffness: null, Damping: null, ShaftRadius: null, Extra: null or { Count: 0 } } ? null : joint;

    private static VisualDefinition? Normalize(VisualDefinition visual) =>
        visual is { Color: null, Roughness: null, Metallic: null, Opacity: null, Emission: null, Texture: null, Paintable: null, Variants: null or { Count: 0 }, Extra: null } ? null : visual;

    // ---- Output ----

    /// <summary>Lists problems in the problems pane under an optional headline.</summary>
    internal void ShowProblems(IReadOnlyList<DataDiagnostic> diagnostics, string? headline)
    {
        var lines = new List<string>();
        if (headline is not null)
            lines.Add($"[b]{Escape(headline)}[/b]");
        foreach (var d in diagnostics)
        {
            var color = d.Severity == DiagnosticSeverity.Error ? UiPalette.Error : UiPalette.Warning;
            lines.Add($"[color={color}]{(d.Severity == DiagnosticSeverity.Error ? Loc.T("Error") : Loc.T("Warning"))}[/color] {Escape(d.JsonPath ?? d.File)}: {Escape(d.Message)}");
        }
        if (diagnostics.Count == 0 && headline is null)
            lines.Add($"[color={UiPalette.Success}]{Loc.T("No problems.")}[/color] {Loc.T("Export writes the part into its mod.")}");
        _problems.Text = string.Join("\n", lines);
    }

    private void ShowError(string? error)
    {
        if (error is null)
            return;
        _message.DialogText = error;
        _message.PopupCentered();
    }

    private void OnViewInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion when (motion.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0:
                _preview.OrbitBy(-motion.Relative.X * 0.01f, -motion.Relative.Y * 0.01f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _preview.ZoomBy(0.9f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _preview.ZoomBy(1.1f);
                break;
            default:
                return;
        }
        AcceptEvent();
    }

    private string Display(string file) => Path.GetRelativePath(Path.GetDirectoryName(ModsRoot) ?? ModsRoot, file).Replace('\\', '/');

    private PropertyGrid AddTab(string title)
    {
        var box = AddTabBox(title);
        var grid = new PropertyGrid();
        box.AddChild(grid);
        return grid;
    }

    private VBoxContainer AddTabBox(string title)
    {
        var scroll = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(box);
        _tabs.AddChild(scroll);
        return box;
    }

    private static Button AddButton(Container parent, string text, Action pressed, string tooltip = "")
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private static void AddToggle(Container parent, string text, bool on, Action<bool> toggled)
    {
        var check = new CheckBox { Text = text, ButtonPressed = on };
        check.Toggled += value => toggled(value);
        parent.AddChild(check);
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
