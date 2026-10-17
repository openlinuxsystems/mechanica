using Godot;
using Mechanica.Core.Docs;

namespace Mechanica.Game.Editor;

/// <summary>
/// In-tool help: shows the Markdown docs from <c>docs/</c> (next to the executable in exported builds,
/// the repo's <c>docs/</c> from the source tree), so the help and the repo docs are the same files.
/// Links to other docs open in place; web links open in the browser.
/// </summary>
public partial class HelpWindow : Window
{
    private ItemList _list = null!;
    private RichTextLabel _text = null!;
    private List<string> _files = [];

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
        _list = new ItemList { CustomMinimumSize = new Vector2(220, 0) };
        _list.ItemSelected += i => Open(_files[(int)i]);
        split.AddChild(_list);
        _text = new RichTextLabel
        {
            BbcodeEnabled = true,
            SelectionEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _text.MetaClicked += meta => FollowLink(meta.AsString());
        split.AddChild(_text);

        _files = Directory.Exists(DocsRoot)
            ? [.. Directory.GetFiles(DocsRoot, "*.md").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
            : [];
        foreach (var file in _files)
            _list.AddItem(TitleOf(file));
    }

    /// <summary>Shows one doc by file name (<c>getting-started.md</c>), and the window.</summary>
    public void Open(string fileName)
    {
        var path = Path.Combine(DocsRoot, Path.GetFileName(fileName.Split('#')[0]));
        _text.Text = File.Exists(path)
            ? MarkdownBbcode.Convert(File.ReadAllText(path))
            : $"No help file '{MarkdownBbcode.Escape(fileName)}' in {MarkdownBbcode.Escape(DocsRoot)}.";
        Current = Path.GetFileName(path);
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
