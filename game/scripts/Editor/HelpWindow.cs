using Godot;
using Mechanica.Core.Docs;
using Mechanica.Core.Input;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Editor;

/// <summary>
/// In-tool help: shows the Markdown docs from <c>docs/</c> (next to the executable in exported builds,
/// the repo's <c>docs/</c> from the source tree), so the help and the repo docs are the same files.
/// Links to other docs open in place; web links open in the browser. The box above the list searches every
/// page's title and text. Esc closes it, and so does the Handbook key (H or F1) that opened it; each page
/// keeps its scroll position while the window is open or hidden. It is a <see cref="ToolWindow"/> over the middle of the
/// screen (70% of it, never past its edges), like every other window.
/// </summary>
public partial class HelpWindow : ToolWindow
{
    private ItemList _list = null!;
    private RichTextLabel _text = null!;
    private List<string> _files = [];
    private List<string> _all = [];
    private LineEdit _search = null!;
    private readonly Dictionary<string, double> _scroll = [];

    /// <summary>The docs folder for this run.</summary>
    public static string DocsRoot => OS.HasFeature("template")
        ? Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "docs")
        : Path.GetFullPath(ProjectSettings.GlobalizePath("res://../docs"));

    /// <summary>File name of the doc shown, or null.</summary>
    public string? Current { get; private set; }

    /// <summary>The shown doc as plain text, for tests.</summary>
    public string Text => _text.GetParsedText();

    public override void _Ready()
    {
        Title = "Help";
        // The middle 70% of the screen, as the pop-up window it replaces was.
        AnchorLeft = AnchorTop = 0.15f;
        AnchorRight = AnchorBottom = 0.85f;
        OffsetLeft = OffsetTop = OffsetRight = OffsetBottom = 0;
        CloseRequested += Hide;
        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        Body.AddChild(split);
        var side = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
        split.AddChild(side);
        _search = new LineEdit { Name = "Search", PlaceholderText = "Search help", ClearButtonEnabled = true };
        _search.TextChanged += Search;
        side.AddChild(_search);
        // The handbook pages are English for now; their titles and text show as written.
        _list = Loc.Verbatim(new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        _list.ItemSelected += i => Open(_files[(int)i]);
        side.AddChild(_list);
        _text = Loc.Verbatim(new RichTextLabel
        {
            BbcodeEnabled = true,
            SelectionEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        _text.MetaClicked += meta => FollowLink(meta.AsString());
        split.AddChild(_text);

        _all = Directory.Exists(DocsRoot)
            ? [.. Directory.GetFiles(DocsRoot, "*.md").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
            : [];
        Search("");
    }

    /// <summary>Pages the list shows now, by file name.</summary>
    public IReadOnlyList<string> Listed => _files;

    /// <summary>Lists the pages whose title or text holds every word of <paramref name="query"/>; empty lists them all.</summary>
    public void Search(string query)
    {
        if (_search.Text != query)
            _search.Text = query;
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _files = [.. _all.Where(file => words.Length == 0 || Matches(file, words))];
        _list.Clear();
        foreach (var file in _files)
            _list.AddItem(TitleOf(file));
        if (Current is not null && _files.IndexOf(Current) is var index and >= 0)
            _list.Select(index);
    }

    private static bool Matches(string file, string[] words)
    {
        var text = File.ReadAllText(Path.Combine(DocsRoot, file));
        return words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Shows one doc by file name (<c>getting-started.md</c>), and the window.</summary>
    public void Open(string fileName)
    {
        var path = Path.Combine(DocsRoot, Path.GetFileName(fileName.Split('#')[0]));
        if (Current is not null)
            _scroll[Current] = _text.GetVScrollBar().Value;
        _text.Text = File.Exists(path)
            ? MarkdownBbcode.Convert(File.ReadAllText(path))
            : Loc.T("No help file '{0}' in {1}.", MarkdownBbcode.Escape(fileName), MarkdownBbcode.Escape(DocsRoot));
        Current = Path.GetFileName(path);
        if (!_files.Contains(Current))
            Search("");
        var index = _files.IndexOf(Current);
        if (index >= 0)
            _list.Select(index);
        _text.ScrollToLine(0);
        // Back at the place the reader left this page.
        if (_scroll.TryGetValue(Current, out var at))
            Callable.From(() => _text.GetVScrollBar().Value = at).CallDeferred();
        if (!Visible)
            Show();
        // Over whatever else is open beside it.
        GetParent()?.MoveChild(this, -1);
    }

    /// <summary>Shows <paramref name="fileName"/>, or hides the window when it is already open.</summary>
    public void Toggle(string fileName)
    {
        if (Visible)
            Hide();
        else
            Open(fileName);
    }

    /// <summary>The page's scroll position, for tests.</summary>
    public double Scroll
    {
        get => _text.GetVScrollBar().Value;
        set => _text.GetVScrollBar().Value = value;
    }

    public override void _Input(InputEvent @event)
    {
        // Esc is the window's own; the Handbook key that opened it closes it too.
        base._Input(@event);
        if (Visible && GameInput.Pressed(@event, InputActions.Handbook) && !GameInput.Typing(GetViewport()))
        {
            GetViewport().SetInputAsHandled();
            Hide();
        }
    }

    private void FollowLink(string link)
    {
        if (link.Contains("://"))
            OS.ShellOpen(link);
        else if (link.Split('#')[0].EndsWith(".md", StringComparison.Ordinal))
            Open(link);
    }

    private string TitleOf(string file)
    {
        var first = File.ReadLines(Path.Combine(DocsRoot, file)).FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));
        return first?[2..].Trim() ?? file;
    }
}
