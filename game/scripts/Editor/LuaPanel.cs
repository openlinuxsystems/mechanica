using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Ui;
using Mechanica.Game.Ui;
using Mechanica.Scripting;
using Mechanica.Scripting.Editor;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.Editor;

/// <summary>What saving the script did.</summary>
/// <param name="Written">True when the file was written.</param>
/// <param name="Error">The first compile error, or null when the script compiles.</param>
/// <param name="Reload">The sandbox's hot reload, when the sandbox runs this script.</param>
/// <param name="Message">One line for the status bar.</param>
public sealed record LuaSaveResult(bool Written, LuaSyntaxError? Error, ScriptReloadResult? Reload, string Message);

/// <summary>
/// The Part Editor's Script tab (chunk 8): edit the open part's Lua script with syntax highlighting,
/// start one from a template, browse the Lua API (double-click inserts a call) and save. Saving checks the
/// script the way the host compiles it, marks the error line, and hot reloads it into the Test Sandbox when
/// the sandbox runs it. The console shows the sandbox's script output. Compiling and templates live in
/// <see cref="LuaSyntax"/> and <see cref="LuaTemplates"/>; this node only draws them.
/// </summary>
public partial class LuaPanel : VBoxContainer
{
    /// <summary>Where the script templates live.</summary>
    public const string TemplatesPath = "res://data/lua_templates";

    private const int ConsoleLines = 200;
    private static readonly Color ErrorLineColor = UiTokens.Error with { A = 0.3f };

    private Label _path = null!;
    private OptionButton _templates = null!;
    private CodeEdit _code = null!;
    private Label _status = null!;
    private ItemList _api = null!;
    private RichTextLabel _console = null!;
    private string? _savedText;
    private string? _scriptText;
    private ScriptMessage? _lastShown;
    private int _errorLine = -1;

    /// <summary>The editor this panel belongs to. Set before adding to the tree.</summary>
    public PartEditorScreen Editor { get; init; } = null!;

    /// <summary>The sandbox's script runtime, when a sandbox is open; saving hot reloads into it.</summary>
    public Func<ScriptRuntime?> Runtime { get; set; } = () => null;

    /// <summary>The script templates, "empty" first.</summary>
    public IReadOnlyList<LuaTemplate> Templates { get; private set; } = [];

    /// <summary>The script's qualified path (<c>mod:scripts/x.lua</c>), or null when the part has none.</summary>
    public string? ScriptKey { get; private set; }

    /// <summary>The script's file, or null when the part has none.</summary>
    public string? ScriptFile { get; private set; }

    /// <summary>The text in the editor.</summary>
    public string Code
    {
        get => _code.Text;
        set => _code.Text = value;
    }

    /// <summary>True when the editor holds changes that are not saved.</summary>
    public bool IsDirty => ScriptFile is not null && _code.Text != (_savedText ?? "");

    /// <summary>1-based line marked as a compile error, or 0.</summary>
    public int ErrorLine => _errorLine + 1;

    /// <summary>The status line, for tests.</summary>
    public string StatusText => _status.Text;

    /// <summary>The console as plain text, for tests.</summary>
    public string ConsoleText
    {
        get
        {
            RefreshConsole();
            return _console.GetParsedText();
        }
    }

    public override void _Ready()
    {
        Templates = LuaTemplates.From(DirAccess.GetFilesAt(TemplatesPath)
            .Select(f => (f, FileAccess.GetFileAsString($"{TemplatesPath}/{f}"))));

        _path = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_path);
        var buttons = new HFlowContainer();
        AddChild(buttons);
        _templates = Loc.Verbatim(new OptionButton { TooltipText = Loc.T("Script template") });
        foreach (var template in Templates)
            _templates.AddItem(template.Title);
        buttons.AddChild(_templates);
        AddButton(buttons, "New from template", () => AskNewFromTemplate(),
            "Replace the script with the template (the part gets scripts/<id>.lua if it has no script)");
        AddButton(buttons, "Save & reload", () => Save(), "Check and write the script; the Test Sandbox reloads it in place (Ctrl+S)");
        AddButton(buttons, "Revert", () => ConfirmDialog.Discard(this, IsDirty, Load), "Throw away unsaved changes");
        AddButton(buttons, "Lua API Reference", () => Editor.OpenHelp("lua-api.md"), "Every function a part script can call");

        var split = new VSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(split);
        _code = new CodeEdit
        {
            CustomMinimumSize = new Vector2(0, 320),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 3,
            GuttersDrawLineNumbers = true,
            AutoBraceCompletionEnabled = true,
            IndentUseSpaces = true,
            IndentSize = 2,
            SyntaxHighlighter = Highlighter(),
            Editable = false,
        };
        _code.AddCommentDelimiter("--", "", lineOnly: true);
        _code.AddStringDelimiter("\"", "\"");
        _code.AddStringDelimiter("'", "'");
        _code.TextChanged += () => ShowStatus(null);
        _code.GuiInput += OnCodeInput;
        split.AddChild(_code);

        var lower = new TabContainer { CustomMinimumSize = new Vector2(0, 160), SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddChild(lower);
        // The API list is reference text, in English like the handbook.
        _api = Loc.Verbatim(new ItemList { Name = "API", TooltipText = Loc.T("Double-click to insert") });
        foreach (var entry in LuaApi.Entries)
        {
            var index = _api.AddItem($"{entry.Signature}  —  {entry.Summary}");
            _api.SetItemTooltip(index, $"{entry.Group}: {entry.Summary}");
        }
        _api.ItemActivated += i => InsertApi((int)i);
        lower.AddChild(_api);
        _console = Loc.Verbatim(new RichTextLabel { Name = "Console", BbcodeEnabled = true, SelectionEnabled = true, ScrollFollowing = true });
        lower.AddChild(_console);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_status);
        Reload();
    }

    public override void _Process(double delta)
    {
        if (IsVisibleInTree())
            RefreshConsole();
    }

    /// <summary>Follows the open part: loads its script when the part or its script path changed.</summary>
    public void Reload()
    {
        var (key, file) = Resolve();
        if (key == ScriptKey && file == ScriptFile && _scriptText is not null)
            return;
        ScriptKey = key;
        ScriptFile = file;
        Load();
    }

    /// <summary>
    /// Replaces the editor's text with a template for the open part. A part without a script gets
    /// <c>scripts/&lt;part id&gt;.lua</c>. Nothing is written until <see cref="Save"/>. Returns an error, or null.
    /// </summary>
    public string? NewFromTemplate(string templateId)
    {
        if (Editor.Document is not { } doc)
            return "Open or create a part first.";
        if (Templates.FirstOrDefault(t => t.Id == templateId) is not { } template)
            return Loc.T("No script template '{0}'.", templateId);
        if (doc.Definition.Script is null)
            doc.Apply("Add script", d => d with { Script = LuaTemplates.PathFor(d.Id ?? "script") });
        Reload();
        if (ScriptFile is null)
            return Loc.T("The script path '{0}' can't be used.", doc.Definition.Script);
        _code.Text = template.For(doc.Definition.Name ?? doc.Definition.Id ?? "part");
        return null;
    }

    /// <summary>
    /// Writes the script and checks it. A compile error marks its line (the file is still written, and a
    /// running sandbox keeps the old code). When it compiles and the Test Sandbox runs it, it hot reloads there.
    /// </summary>
    public LuaSaveResult Save()
    {
        if (ScriptFile is null || ScriptKey is null)
            return Finish(new LuaSaveResult(false, null, null, "The part has no script: pick a template and press New from template."));
        var text = _code.Text;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScriptFile)!);
            File.WriteAllText(ScriptFile, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Finish(new LuaSaveResult(false, null, null, Loc.T("Could not write {0}: {1}", ScriptKey, e.Message)));
        }
        _savedText = text;
        var error = LuaSyntax.Check(text);
        MarkError(error);
        if (error is not null)
            return Finish(new LuaSaveResult(true, error, null, Loc.T("Saved {0} with an error on {1}. The sandbox keeps the old code.", ScriptKey, error)));
        if (Runtime() is not { } runtime || !runtime.Scripts.Contains(ScriptKey))
            return Finish(new LuaSaveResult(true, null, null, Loc.T("Saved {0}. Export and Test in Sandbox to run it.", ScriptKey)));
        var reload = runtime.Reload(ScriptKey);
        return Finish(new LuaSaveResult(true, null, reload, reload.Ok
            ? Loc.T(reload.Instances == 1 ? "Saved {0}; reloaded on {1} part in the sandbox." : "Saved {0}; reloaded on {1} parts in the sandbox.", ScriptKey, reload.Instances)
            : Loc.T("Saved {0}, but the sandbox refused it: {1}", ScriptKey, reload.Error)));
    }

    /// <summary>Inserts an API entry's call at the caret (a hook becomes an empty function).</summary>
    public void InsertApi(int index)
    {
        if (index < 0 || index >= LuaApi.Entries.Count || !_code.Editable)
            return;
        var entry = LuaApi.Entries[index];
        _code.InsertTextAtCaret(entry.Group == "Hooks" ? $"{entry.Signature}\n  \nend\n" : entry.Signature);
        _code.GrabFocus();
    }

    /// <summary>
    /// Puts the selected template in the editor, asking first when that replaces code already there.
    /// Returns the question, or null when the editor was empty and the template went straight in.
    /// </summary>
    public ConfirmDialog? AskNewFromTemplate()
    {
        var template = Templates[_templates.Selected];
        void Replace() => ShowStatus(NewFromTemplate(template.Id));
        if (!string.IsNullOrWhiteSpace(_code.Text))
            return ConfirmDialog.Ask(this, "Replace script", Loc.T("Replace the code in the editor with the {0} template?", template.Title), "Replace", Replace);
        Replace();
        return null;
    }

    private void Load()
    {
        _savedText = ScriptFile is not null && File.Exists(ScriptFile) ? File.ReadAllText(ScriptFile) : null;
        _scriptText = _savedText ?? "";
        _code.Editable = ScriptFile is not null;
        _code.Text = _scriptText;
        _code.ClearUndoHistory();
        MarkError(null);
        _path.Text = Editor.Document is null
            ? "No part open."
            : ScriptKey is null
                ? "This part has no script. Pick a template and press New from template, or set Script on the Behaviour tab."
                : _savedText is null ? Loc.T("{0}  (new file, not saved yet)", ScriptKey) : ScriptKey;
        ShowStatus(null);
    }

    private (string? Key, string? File) Resolve()
    {
        if (Editor.Document is not { Definition.Script: { } script } doc
            || !AssetPath.TryParse(script, doc.ModId, AssetKind.Script, out var asset, out _))
            return (null, null);
        var modDirectory = asset.ModId == doc.ModId ? doc.ModDirectory : Path.Combine(Path.GetDirectoryName(doc.ModDirectory)!, asset.ModId);
        return (asset.ToString(), asset.Resolve(modDirectory));
    }

    private void MarkError(LuaSyntaxError? error)
    {
        if (_errorLine >= 0 && _errorLine < _code.GetLineCount())
            _code.SetLineBackgroundColor(_errorLine, new Color(0, 0, 0, 0));
        _errorLine = error is null ? -1 : Math.Clamp(error.Line - 1, 0, _code.GetLineCount() - 1);
        if (_errorLine < 0)
            return;
        _code.SetLineBackgroundColor(_errorLine, ErrorLineColor);
        _code.SetCaretLine(_errorLine);
        _code.SetCaretColumn(error!.Column);
    }

    private LuaSaveResult Finish(LuaSaveResult result)
    {
        ShowStatus(result.Message);
        return result;
    }

    private void ShowStatus(string? message)
    {
        var saves = Loc.T("Ctrl+S saves and reloads the script.");
        _status.Text = message ?? (ScriptFile is null ? "" : IsDirty ? $"{Loc.T("Unsaved changes.")} {saves}" : saves);
    }

    private void RefreshConsole()
    {
        var messages = Runtime()?.Messages ?? [];
        var last = messages.Count > 0 ? messages[^1] : null;
        if (ReferenceEquals(last, _lastShown))
            return;
        _lastShown = last;
        _console.Text = messages.Count == 0
            ? Loc.T("Script output from the Test Sandbox shows here.")
            : string.Join("\n", messages.TakeLast(ConsoleLines).Select(Line));
    }

    private string Line(ScriptMessage m)
    {
        var color = m.Kind switch { ScriptMessageKind.Error => UiPalette.Error, ScriptMessageKind.Warning => UiPalette.Warning, _ => UiPalette.TextSecondary };
        var where = m.Part is { } part ? " " + Loc.T("part {0}", part) : "";
        var text = $"[color={color}]{Escape(m.Script + where)}:[/color] {Escape(m.Text)}";
        // The open part's own script stands out.
        return m.Script == ScriptKey ? $"[b]{text}[/b]" : text;
    }

    private void OnCodeInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.S } key && (key.CtrlPressed || key.MetaPressed))
        {
            Save();
            _code.AcceptEvent();
        }
    }

    private static CodeHighlighter Highlighter()
    {
        var highlighter = new CodeHighlighter
        {
            NumberColor = new Color(UiPalette.SyntaxNumber),
            SymbolColor = new Color(UiPalette.SyntaxSymbol),
            FunctionColor = new Color(UiPalette.SyntaxFunction),
            MemberVariableColor = new Color(UiPalette.SyntaxMember),
        };
        foreach (var keyword in LuaApi.Keywords)
            highlighter.AddKeywordColor(keyword, new Color(UiPalette.SyntaxKeyword));
        foreach (var library in LuaApi.Libraries)
            highlighter.AddKeywordColor(library, new Color(UiPalette.SyntaxLibrary));
        foreach (var name in LuaApi.Entries.Select(e => e.Name.Split('.')[0]).Distinct())
            highlighter.AddKeywordColor(name, new Color(name.StartsWith("on_", StringComparison.Ordinal) || name == "get_outputs" ? UiPalette.SyntaxCallback : UiPalette.SyntaxApi));
        foreach (var member in LuaApi.Entries.Where(e => e.Name.Contains('.')).Select(e => e.Name.Split('.')[1]).Distinct())
            highlighter.AddMemberKeywordColor(member, new Color(UiPalette.SyntaxApi));
        var comment = new Color(UiPalette.SyntaxComment);
        var text = new Color(UiPalette.SyntaxString);
        highlighter.AddColorRegion("--[[", "]]", comment);
        highlighter.AddColorRegion("--", "", comment, lineOnly: true);
        highlighter.AddColorRegion("[[", "]]", text);
        highlighter.AddColorRegion("\"", "\"", text);
        highlighter.AddColorRegion("'", "'", text);
        return highlighter;
    }

    private static Button AddButton(Container parent, string text, Action pressed, string tooltip = "")
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
