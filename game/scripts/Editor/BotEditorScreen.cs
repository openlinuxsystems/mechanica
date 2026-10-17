using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Game.Mods;
using Mechanica.Scripting.Editor;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.Editor;

/// <summary>
/// The Bot Behavior Editor (chunk 21, D9), an in-game mode from the main menu. Pick a mod, open one of its
/// bots or start one from a template, then edit it in four tabs:
/// <list type="bullet">
/// <item><b>Bot</b>: stats, size, colour, attacks and drops.</item>
/// <item><b>Behavior</b>: the behaviour tree. Add nodes from the palette of built-in nodes (or a leaf from the
/// bot's script), move and remove them, and set each node's parameters.</item>
/// <item><b>Script</b>: the bot's own Lua (hooks and custom leaves), syntax-checked before it is saved.</item>
/// <item><b>Raids</b>: the mod's raid table, with a preview of what a crop value and player count send.</item>
/// </list>
/// Export checks the bot as loading would and writes it with <see cref="BotExporter"/>. Help opens the Bot
/// Tutorial. Every edit goes through <see cref="BotDocument"/>, so it can be undone.
/// </summary>
public partial class BotEditorScreen : Control
{
    /// <summary>Where the bot templates live.</summary>
    public const string TemplatesPath = "res://data/bot_templates";

    /// <summary>The help page Help opens.</summary>
    public const string HelpFile = "bot-tutorial.md";

    private OptionButton _mods = null!;
    private OptionButton _templates = null!;
    private LineEdit _newId = null!;
    private ItemList _bots = null!;
    private TabContainer _tabs = null!;
    private PropertyGrid _botGrid = null!;
    private Tree _tree = null!;
    private OptionButton _palette = null!;
    private Label _nodeHelp = null!;
    private PropertyGrid _nodeGrid = null!;
    private CodeEdit _script = null!;
    private PropertyGrid _raidGrid = null!;
    private Label _raidPreview = null!;
    private RichTextLabel _status = null!;
    private HelpWindow _help = null!;
    private RaidTableDefinition? _raids;
    private IReadOnlyList<int> _selected = [];

    /// <summary>Mods folder the editor works in. Set before the node enters the tree.</summary>
    public string ModsRoot { get; set; } = ModPaths.ModsRoot;

    /// <summary>The bot open now, or null.</summary>
    public BotDocument? Document { get; private set; }

    /// <summary>Folder of the mod picked in the mod list.</summary>
    public string? ModDirectory { get; private set; }

    /// <summary>The templates, by name.</summary>
    public IReadOnlyDictionary<string, string> Templates { get; private set; } = new Dictionary<string, string>();

    /// <summary>The status line's text: the last export, save or problem.</summary>
    public string StatusText => _status.GetParsedText();

    /// <summary>The tree path of the selected node.</summary>
    public IReadOnlyList<int> Selected => _selected;

    /// <summary>The help window (the Bot Tutorial).</summary>
    public HelpWindow Help => _help;

    /// <summary>The raid table being edited.</summary>
    public RaidTableDefinition? Raids => _raids;

    /// <summary>The script text in the Script tab.</summary>
    public string ScriptText
    {
        get => _script.Text;
        set => _script.Text = value;
    }

    /// <summary>The Back button was pressed.</summary>
    public event Action? CloseRequested;

    /// <summary>"Test in Sandbox" exported the bot; carries its qualified id.</summary>
    public event Action<string>? TestRequested;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new Panel { AnchorRight = 1, AnchorBottom = 1 });
        Templates = LoadTemplates();
        var margin = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 12);
        AddChild(margin);
        var column = new VBoxContainer();
        margin.AddChild(column);
        column.AddChild(BuildToolbar());
        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(split);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
        left.AddChild(new Label { Text = "Bots in this mod" });
        _bots = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        _bots.ItemActivated += i => Open(_bots.GetItemText((int)i));
        _bots.ItemSelected += i => Open(_bots.GetItemText((int)i));
        left.AddChild(_bots);
        split.AddChild(left);
        _tabs = new TabContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(_tabs);
        BuildTabs();
        _status = new RichTextLabel { FitContent = true, BbcodeEnabled = false, CustomMinimumSize = new Vector2(0, 40), ScrollActive = false };
        column.AddChild(_status);
        _help = new HelpWindow { Visible = false };
        AddChild(_help);
        RefreshMods();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, CtrlPressed: true } key || Document is null)
            return;
        if (key.Keycode == Key.Z)
            Document.Undo();
        else if (key.Keycode == Key.Y)
            Document.Redo();
    }

    // ---- Commands (the buttons call these; tests call them directly) ----

    /// <summary>Picks the mod to work in, by folder name.</summary>
    public bool SelectMod(string modId)
    {
        for (var i = 0; i < _mods.ItemCount; i++)
        {
            if (_mods.GetItemText(i) != modId)
                continue;
            _mods.Select(i);
            UseMod(modId);
            return true;
        }
        return false;
    }

    /// <summary>Starts a new bot from <paramref name="template"/> with <paramref name="id"/>. False when the id or template is bad.</summary>
    public bool New(string template, string id)
    {
        if (ModDirectory is null || !Templates.TryGetValue(template, out var json))
            return Fail($"Pick a mod and one of the templates ({string.Join(", ", Templates.Keys)}).");
        if (!Identifiers.IsValid(id))
            return Fail($"'{id}' is not a valid id: use lowercase snake_case, like my_bot.");
        Use(BotDocument.New(ModDirectory, json, id, Humanize(id)));
        _script.Text = "";
        Say($"New bot '{id}' from the {template} template. Export to save it.");
        return true;
    }

    /// <summary>Opens a bot file of the current mod (<c>bots/x.json</c>).</summary>
    public bool Open(string relativeFile)
    {
        if (ModDirectory is null)
            return false;
        var diagnostics = new List<DataDiagnostic>();
        if (BotDocument.Open(ModDirectory, relativeFile, diagnostics) is not { } document)
            return Fail(string.Join("\n", diagnostics));
        Use(document);
        _script.Text = BotExporter.ReadScript(document) ?? "";
        Say($"Opened {relativeFile}.");
        return true;
    }

    /// <summary>Checks and writes the open bot. Returns what happened.</summary>
    public ExportResult? Export()
    {
        if (Document is null)
            return null;
        var result = BotExporter.Export(Document, Catalog());
        Say(result.Succeeded
            ? $"Exported {result.QualifiedId} to {Document.TargetFile}." + Problems(result.Diagnostics)
            : $"Not exported:{Problems(result.Diagnostics)}");
        if (result.Succeeded)
            RefreshBots();
        return result;
    }

    /// <summary>Exports the open bot and, when that worked, asks to try it in the Test Sandbox.</summary>
    public bool TestInSandbox()
    {
        if (Export() is not { Succeeded: true } result)
            return false;
        TestRequested?.Invoke(result.QualifiedId);
        return true;
    }

    /// <summary>Checks the Script tab's Lua and saves it as the bot's script. False on a syntax error.</summary>
    public bool SaveScript()
    {
        if (Document is null)
            return false;
        if (LuaSyntax.Check(_script.Text) is { } error)
        {
            _script.SetCaretLine(Math.Max(0, error.Line - 1));
            return Fail($"Lua error on line {error.Line}: {error.Message}");
        }
        var file = BotExporter.SaveScript(Document, _script.Text);
        Say($"Saved {file}. Export the bot to keep its script setting.");
        return true;
    }

    /// <summary>Selects the tree node at <paramref name="path"/>.</summary>
    public void SelectNode(IReadOnlyList<int> path)
    {
        _selected = Document?.Node(path) is null ? [] : [.. path];
        RefreshTree();
    }

    /// <summary>Adds a node of <paramref name="type"/> under the selected node (or as the root of an empty tree) and selects it.</summary>
    public bool AddNode(string type)
    {
        if (Document?.AddNode(_selected, type) is not { } path)
            return Fail(Document?.Tree is null
                ? "Pick a node type to start the tree."
                : "The selected node can't take another child: leaves take none and decorators one.");
        SelectNode(path);
        return true;
    }

    /// <summary>Removes the selected node and what is under it.</summary>
    public bool RemoveNode()
    {
        if (Document is null || !Document.RemoveNode(_selected))
            return false;
        SelectNode(_selected.Count == 0 ? [] : [.. _selected.Take(_selected.Count - 1)]);
        return true;
    }

    /// <summary>Moves the selected node up (-1) or down (+1) among its siblings.</summary>
    public bool MoveNode(int delta)
    {
        if (Document?.MoveNode(_selected, delta) is not { } path)
            return false;
        SelectNode(path);
        return true;
    }

    /// <summary>Sets a parameter of the selected node; text is read as a number or boolean when it is one.</summary>
    public bool SetParameter(string name, string? text) =>
        Document?.SetParameter(_selected, name, ParseValue(text)) ?? false;

    /// <summary>Replaces the raid table being edited with <paramref name="json"/>. Returns an error, or null.</summary>
    public string? SetRaidsJson(string json)
    {
        var diagnostics = new List<DataDiagnostic>();
        if (ModLoader.ParseJson<RaidTableDefinition>(json, RaidTable.FilePath, diagnostics) is not { } raids)
            return string.Join("\n", diagnostics);
        _raids = raids;
        UpdateRaidPreview(null, 1);
        return null;
    }

    /// <summary>Checks the raid table against the mods' bots and saves it to <c>raids/raids.json</c>.</summary>
    public ExportResult? SaveRaids()
    {
        if (ModDirectory is null || _raids is null)
            return null;
        var result = BotExporter.ExportRaids(ModDirectory, _raids, BotBook.Load(ModLoader.LoadAll(ModsRoot)));
        Say(result.Succeeded ? $"Saved {RaidTable.FilePath}." + Problems(result.Diagnostics) : $"Raid table not saved:{Problems(result.Diagnostics)}");
        return result;
    }

    /// <summary>Shows the Bot Tutorial.</summary>
    public void OpenHelp(string fileName = HelpFile) => _help.Open(fileName);

    // F1 opens the help, as everywhere in the game.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F1 })
        {
            OpenHelp(HelpFile);
            GetViewport().SetInputAsHandled();
        }
    }

    // ---- Building the UI ----

    private Control BuildToolbar()
    {
        var bar = new HBoxContainer();
        bar.AddChild(new Label { Text = "Mod" });
        _mods = new OptionButton { CustomMinimumSize = new Vector2(140, 0) };
        _mods.ItemSelected += i => UseMod(_mods.GetItemText((int)i));
        bar.AddChild(_mods);
        bar.AddChild(new VSeparator());
        _templates = new OptionButton();
        foreach (var name in Templates.Keys)
            _templates.AddItem(name);
        bar.AddChild(_templates);
        _newId = new LineEdit { PlaceholderText = "new_bot_id", CustomMinimumSize = new Vector2(140, 0) };
        bar.AddChild(_newId);
        Button(bar, "New", () => New(_templates.GetItemText(_templates.Selected), _newId.Text.Trim()));
        bar.AddChild(new VSeparator());
        Button(bar, "Undo", () => Document?.Undo());
        Button(bar, "Redo", () => Document?.Redo());
        Button(bar, "Export", () => Export());
        Button(bar, "Test in Sandbox", () => TestInSandbox());
        bar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        Button(bar, "Help", () => OpenHelp());
        Button(bar, "Back", () => CloseRequested?.Invoke());
        return bar;
    }

    private void BuildTabs()
    {
        var botScroll = new ScrollContainer { Name = "Bot" };
        _botGrid = new PropertyGrid();
        botScroll.AddChild(_botGrid);
        _tabs.AddChild(botScroll);

        var behavior = new HSplitContainer { Name = "Behavior" };
        var treeColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _tree = new Tree { SizeFlagsVertical = SizeFlags.ExpandFill, HideRoot = false };
        _tree.ItemSelected += () =>
        {
            if (_tree.GetSelected()?.GetMetadata(0).AsString() is { } meta && meta != PathKey(_selected))
                SelectNode(ParsePath(meta));
        };
        treeColumn.AddChild(_tree);
        var row = new HBoxContainer();
        _palette = new OptionButton();
        foreach (var node in BotBehavior.Nodes)
            _palette.AddItem(node.Type);
        _palette.ItemSelected += _ => ShowNodeHelp(_palette.GetItemText(_palette.Selected));
        row.AddChild(_palette);
        Button(row, "Add", () => AddNode(_palette.GetItemText(_palette.Selected)));
        Button(row, "Up", () => MoveNode(-1));
        Button(row, "Down", () => MoveNode(1));
        Button(row, "Remove", () => RemoveNode());
        treeColumn.AddChild(row);
        _nodeHelp = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 48) };
        treeColumn.AddChild(_nodeHelp);
        behavior.AddChild(treeColumn);
        var nodeScroll = new ScrollContainer { CustomMinimumSize = new Vector2(300, 0) };
        _nodeGrid = new PropertyGrid();
        nodeScroll.AddChild(_nodeGrid);
        behavior.AddChild(nodeScroll);
        _tabs.AddChild(behavior);

        var script = new VBoxContainer { Name = "Script" };
        script.AddChild(new Label
        {
            Text = "Hooks: on_spawn(), on_think(dt) (default: behavior.run(tree, dt)), on_damaged(amount), on_death(). "
                + "A global function here is a leaf the tree can use by name, or through a call node.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _script = new CodeEdit { SizeFlagsVertical = SizeFlags.ExpandFill, GuttersDrawLineNumbers = true };
        script.AddChild(_script);
        var scriptRow = new HBoxContainer();
        Button(scriptRow, "Save script", () => SaveScript());
        Button(scriptRow, "Lua API", () => OpenHelp("lua-api.md"));
        script.AddChild(scriptRow);
        _tabs.AddChild(script);

        var raids = new ScrollContainer { Name = "Raids" };
        var raidColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _raidGrid = new PropertyGrid();
        raidColumn.AddChild(_raidGrid);
        _raidPreview = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        raidColumn.AddChild(_raidPreview);
        raids.AddChild(raidColumn);
        _tabs.AddChild(raids);
    }

    private static void Button(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        parent.AddChild(button);
    }

    // ---- Showing the document ----

    private void RefreshMods()
    {
        _mods.Clear();
        var mods = Directory.Exists(ModsRoot)
            ? Directory.GetDirectories(ModsRoot).Where(d => File.Exists(Path.Combine(d, ModLoader.ManifestFileName)))
                .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList()
            : [];
        foreach (var mod in mods)
            _mods.AddItem(mod);
        if (mods.Count > 0)
            UseMod(mods[0]);
    }

    private void UseMod(string modId)
    {
        ModDirectory = Path.Combine(ModsRoot, modId);
        Document = null;
        _selected = [];
        var diagnostics = new List<DataDiagnostic>();
        _raids = BotExporter.ReadRaids(ModDirectory, diagnostics) ?? new RaidTableDefinition { Levels = [] };
        RefreshBots();
        Refresh();
        RefreshRaids();
        if (diagnostics.Count > 0)
            Say(string.Join("\n", diagnostics));
    }

    private void RefreshBots()
    {
        _bots.Clear();
        if (ModDirectory is null)
            return;
        foreach (var file in BotDocument.Files(ModDirectory))
            _bots.AddItem(file);
    }

    private void Use(BotDocument document)
    {
        if (Document is not null)
            Document.Changed -= Refresh;
        Document = document;
        Document.Changed += Refresh;
        _selected = [];
        Refresh();
    }

    private void Refresh()
    {
        RefreshBotGrid();
        RefreshTree();
    }

    private void RefreshBotGrid()
    {
        _botGrid.Clear();
        if (Document is not { } d)
        {
            _botGrid.AddHeading("Open a bot, or start one from a template.");
            return;
        }
        var j = d.Json;
        _botGrid.AddHeading(d.IsDirty ? $"{d.Id} (not exported)" : d.Id ?? "");
        _botGrid.AddText("Id", Str(j, "id"), v => d.Set("id", v));
        _botGrid.AddText("Name", Str(j, "name"), v => d.Set("name", v));
        _botGrid.AddText("Description", Str(j, "description"), v => d.Set("description", v));
        _botGrid.AddText("Family (sounds)", Str(j, "family"), v => d.Set("family", v), "the id");
        _botGrid.AddNumber("Health", Num(j, "health"), 1, 100000, 1, v => d.Set("health", Value(v)));
        _botGrid.AddNumber("Speed", Num(j, "speed"), 0, 50, 0.1, v => d.Set("speed", Value(v)), suffix: " m/s");
        _botGrid.AddNumber("Water speed", Num(j, "water_speed"), 0, 50, 0.1, v => d.Set("water_speed", Value(v)), optional: true, suffix: " m/s");
        _botGrid.AddNumber("Sight", Num(j, "sight"), 0, 500, 1, v => d.Set("sight", Value(v)), optional: true, fallback: 30, suffix: " m");
        _botGrid.AddNumber("Radius", Num(j["size"], "radius"), 0.05, 10, 0.05, v => SetSize(d, "radius", v), optional: true, fallback: 0.4, suffix: " m");
        _botGrid.AddNumber("Height", Num(j["size"], "height"), 0.1, 20, 0.05, v => SetSize(d, "height", v), optional: true, fallback: 1.2, suffix: " m");
        _botGrid.AddColor("Colour", Str(j, "color"), v => d.Set("color", v));
        _botGrid.AddBool("Melee immune", Bool(j, "melee_immune"), v => d.Set("melee_immune", v), optional: true);
        _botGrid.AddBool("Passive", Bool(j, "passive"), v => d.Set("passive", v), optional: true);
        _botGrid.AddNumber("Raid cost", Num(j, "raid_cost"), 1, 100, 1, v => d.Set("raid_cost", v is { } c ? JsonValue.Create((int)c) : null), optional: true, fallback: 1);
        _botGrid.AddText("Script", Str(j, "script"), v => d.Set("script", v), "none");
        _botGrid.AddHeading("Attacks and drops");
        _botGrid.AddJson("Attacks", Pretty(j["attacks"]), text => SetJson(d, "attacks", text));
        _botGrid.AddJson("Drops", Pretty(j["drops"]), text => SetJson(d, "drops", text));
        _botGrid.AddJson("Properties", Pretty(j["properties"]), text => SetJson(d, "properties", text));
    }

    private void RefreshTree()
    {
        _tree.Clear();
        _nodeGrid.Clear();
        if (Document?.Tree is not { } root)
        {
            _nodeHelp.Text = Document is null ? "" : "The bot has no behaviour tree. Pick a node type and press Add to start one (usually a selector).";
            return;
        }
        if (Document.Node(_selected) is null)
            _selected = [];
        AddTreeItem(null, root, []);
        var node = Document.Node(_selected)!;
        var type = Str(node, "type") ?? "";
        ShowNodeHelp(type);
        _nodeGrid.AddHeading(type);
        var info = BotBehavior.Find(type);
        var names = (info?.Parameters.Select(p => p.Name) ?? [])
            .Concat(node.Select(kv => kv.Key).Where(k => k is not "type" and not "children"))
            .Distinct().ToList();
        foreach (var name in names)
        {
            var parameter = info?.Parameters.FirstOrDefault(p => p.Name == name);
            var field = _nodeGrid.AddText(name, node[name] is { } v ? Text(v) : null, value => SetParameter(name, value),
                parameter?.Default is { Length: > 0 } fallback ? fallback : parameter?.Kind ?? "");
            field.TooltipText = parameter?.Help ?? "";
        }
        var custom = _nodeGrid.AddText("+ parameter", null, value =>
        {
            if (value?.Split('=', 2) is [var key, var text])
                SetParameter(key.Trim(), text.Trim());
        }, "name=value");
        custom.TooltipText = "Adds a parameter your own leaf reads, as name=value.";
    }

    private void AddTreeItem(TreeItem? parent, JsonObject node, IReadOnlyList<int> path)
    {
        var item = _tree.CreateItem(parent);
        var details = string.Join(", ", node.Where(kv => kv.Key is not "type" and not "children").Select(kv => $"{kv.Key}={Text(kv.Value)}"));
        item.SetText(0, details.Length > 0 ? $"{Str(node, "type")} ({details})" : Str(node, "type") ?? "?");
        item.SetMetadata(0, PathKey(path));
        if (PathKey(path) == PathKey(_selected))
            item.Select(0);
        if (node["children"] is JsonArray children)
        {
            for (var i = 0; i < children.Count; i++)
            {
                if (children[i] is JsonObject child)
                    AddTreeItem(item, child, [.. path, i]);
            }
        }
    }

    private void ShowNodeHelp(string type)
    {
        _nodeHelp.Text = BotBehavior.Find(type) is { } info
            ? $"{info.Type} ({info.Shape.ToString().ToLowerInvariant()}): {info.Help}"
            : $"{type}: a leaf from the bot's script (a global function named {type}).";
    }

    private void RefreshRaids()
    {
        _raidGrid.Clear();
        if (_raids is null)
            return;
        _raidGrid.AddHeading($"Raid table ({RaidTable.FilePath}); the last mod in load order that has one wins");
        _raidGrid.AddJson("Table", DataJson.Write(_raids), SetRaidsJson);
        var value = 0.0;
        var players = 1;
        _raidGrid.AddNumber("Preview crop value", value, 0, 100000, 1, v => UpdateRaidPreview(v, null));
        _raidGrid.AddNumber("Preview players", players, 1, 16, 1, v => UpdateRaidPreview(null, (int?)v));
        var save = new Button { Text = "Save raid table" };
        save.Pressed += () => SaveRaids();
        _raidGrid.Row("", save);
        UpdateRaidPreview(value, players);
    }

    private double _previewValue;
    private int _previewPlayers = 1;

    private void UpdateRaidPreview(double? value, int? players)
    {
        _previewValue = value ?? _previewValue;
        _previewPlayers = players ?? _previewPlayers;
        if (_raids is null)
            return;
        var bots = BotBook.Load(ModLoader.LoadAll(ModsRoot));
        var modId = Path.GetFileName(ModDirectory!);
        var errors = RaidTable.Validate(_raids, bots, RaidTable.FilePath, modId).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            _raidPreview.Text = "Fix the table first:\n" + string.Join("\n", errors);
            return;
        }
        var plan = RaidTable.From(_raids, bots, modId).Plan((float)_previewValue, _previewPlayers);
        _raidPreview.Text = plan is null
            ? $"Crops worth {_previewValue:0} set off no raid."
            : $"Level {plan.Level}, budget {plan.Budget} for {_previewPlayers} player(s): "
              + string.Join(", ", plan.Bots.GroupBy(b => b).Select(g => $"{g.Count()}x {g.Key}"));
    }

    // ---- Helpers ----

    private IPartCatalog Catalog() => ModLoader.LoadAll(ModsRoot);

    private static IReadOnlyDictionary<string, string> LoadTemplates()
    {
        var templates = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using var dir = DirAccess.Open(TemplatesPath);
        if (dir is null)
            return templates;
        foreach (var file in dir.GetFiles().Where(f => f.EndsWith(".json", StringComparison.Ordinal)))
            templates[file[..^5]] = FileAccess.GetFileAsString($"{TemplatesPath}/{file}");
        return templates;
    }

    /// <summary>A template's JSON by name, or null (for batches).</summary>
    public static string? Template(string name)
    {
        var path = $"{TemplatesPath}/{name}.json";
        return Identifiers.IsValid(name) && FileAccess.FileExists(path) ? FileAccess.GetFileAsString(path) : null;
    }

    private static void SetSize(BotDocument d, string key, double? value)
    {
        var size = d.Json["size"] is JsonObject s ? (JsonObject)s.DeepClone() : new JsonObject();
        if (value is null)
            size.Remove(key);
        else
            size[key] = JsonValue.Create(value.Value);
        d.Set("size", size.Count == 0 ? null : size);
    }

    private static string? SetJson(BotDocument d, string key, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            d.Set(key, null);
            return null;
        }
        try
        {
            d.Set(key, JsonNode.Parse(text));
            return null;
        }
        catch (JsonException e)
        {
            return e.Message;
        }
    }

    private static JsonNode? Value(double? v) => v is { } x ? JsonValue.Create(x) : null;

    private static JsonNode? ParseValue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return JsonValue.Create(number);
        return text switch { "true" => JsonValue.Create(true), "false" => JsonValue.Create(false), _ => JsonValue.Create(text) };
    }

    private static string? Str(JsonNode? o, string key) => o?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Num(JsonNode? o, string key) => o?[key] is JsonValue v && v.TryGetValue<double>(out var n) ? n : null;

    private static bool? Bool(JsonNode? o, string key) => o?[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static string Text(JsonNode? v) => v is JsonValue value && value.TryGetValue<string>(out var s) ? s : v?.ToJsonString() ?? "";

    private static string Pretty(JsonNode? node) => node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "";

    private static string PathKey(IReadOnlyList<int> path) => string.Join('/', path);

    private static IReadOnlyList<int> ParsePath(string key) => key.Length == 0 ? [] : [.. key.Split('/').Select(int.Parse)];

    private static string Humanize(string id) =>
        string.Join(' ', id.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string Problems(IEnumerable<DataDiagnostic> diagnostics)
    {
        var list = diagnostics.ToList();
        return list.Count == 0 ? "" : "\n" + string.Join("\n", list);
    }

    private void Say(string text) => _status.Text = text;

    private bool Fail(string text)
    {
        Say(text);
        return false;
    }
}
