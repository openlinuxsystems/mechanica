using Godot;
using Mechanica.Core.Docs;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Editor;

/// <summary>
/// In-tool help: shows the Markdown docs from <c>docs/</c> (next to the executable in exported builds,
/// the repo's <c>docs/</c> from the source tree), so the help and the repo docs are the same files.
/// Links to other docs open in place; web links open in the browser. The box above the list searches every
/// page's title and text.
/// </summary>
public partial class HelpWindow : Window
{
    private ItemList _list = null!;
    private RichTextLabel _text = null!;
    private List<string> _files = [];
    private List<string> _all = [];
    private LineEdit _search = null!;

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
        Size = new Vector2I(960, 680);
        Exclusive = false;
        CloseRequested += Hide;
        var split = new HSplitContainer();
        split.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(split);
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
        if (!Visible)
            PopupCentered();
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
