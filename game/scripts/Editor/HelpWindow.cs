using Godot;
using Mechanica.Core.Docs;
using Mechanica.Core.Input;
using Mechanica.Core.Ui;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Editor;

/// <summary>
/// In-tool help: shows the Markdown docs from <c>docs/</c> (next to the executable in exported builds,
/// the repo's <c>docs/</c> from the source tree), so the help and the repo docs are the same files.
/// Links to other docs open in place; web links open in the browser. The box above the list searches every
/// page's title and text. Esc closes it, and so does the Handbook key (H or F1) that opened it; each page
/// keeps its scroll position while the window is open or hidden. It is a <see cref="ToolWindow"/> over the middle of the
/// screen (70% of it, never past its edges), like every other window.
/// <para>
/// As the Handbook (UI spec §5, §10) it opens at the page read last (<see cref="OpenLast"/>, kept in the player's
/// settings), and the arrow keys (or Page Up and Page Down) and the buttons under the page turn pages in list order.
/// A line holding only an image (<c>![caption](images/x.svg)</c>, inside <c>docs/</c>) is a diagram, drawn as wide as
/// fits the page and redrawn when the page changes width, so it scales with the window.
/// </para>
/// </summary>
public partial class HelpWindow : ToolWindow
{
    private ItemList _list = null!;
    private RichTextLabel _text = null!;
    private List<string> _files = [];
    private List<string> _all = [];
    private LineEdit _search = null!;
    private readonly Dictionary<string, double> _scroll = [];
    private readonly Dictionary<(string Path, int Width), Texture2D?> _diagrams = [];
    private Button _previous = null!;
    private Button _next = null!;
    private Label _page = null!;
    private float _drawnWidth;

    /// <summary>The docs folder for this run.</summary>
    public static string DocsRoot => Mods.ModPaths.ShippedFolder("docs");

    /// <summary>File name of the doc shown, or null.</summary>
    public string? Current { get; private set; }

    /// <summary>The shown doc as plain text, for tests.</summary>
    public string Text => _text.GetParsedText();

    /// <summary>Diagrams on the page shown now, with the width each was drawn at, for tests.</summary>
    public List<(string Path, int Width)> Diagrams { get; } = [];

    /// <summary>The page counter under the page ("3 / 19").</summary>
    public string PageText => _page.Text;

    public override void _Ready()
    {
        Title = "Handbook";
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
        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(page);
        _text = Loc.Verbatim(new RichTextLabel
        {
            BbcodeEnabled = true,
            SelectionEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        });
        _text.MetaClicked += meta => FollowLink(meta.AsString());
        _text.Resized += Reflow;
        page.AddChild(_text);
        var turn = new HBoxContainer { Name = "Pages", Alignment = BoxContainer.AlignmentMode.Center };
        page.AddChild(turn);
        _previous = new Button { Name = "Previous", Text = "◀ Previous", CustomMinimumSize = new Vector2(96, 32), FocusMode = FocusModeEnum.None };
        _previous.Pressed += () => Turn(-1);
        turn.AddChild(_previous);
        _page = new Label { Name = "Page", CustomMinimumSize = new Vector2(80, 0), HorizontalAlignment = HorizontalAlignment.Center };
        turn.AddChild(_page);
        _next = new Button { Name = "Next", Text = "Next ▶", CustomMinimumSize = new Vector2(96, 32), FocusMode = FocusModeEnum.None };
        _next.Pressed += () => Turn(1);
        turn.AddChild(_next);
        Loc.Live(_previous, b => b.TooltipText = Loc.T("Previous page ({0})", GameInput.Label(InputActions.PagePrevious)));
        Loc.Live(_next, b => b.TooltipText = Loc.T("Next page ({0})", GameInput.Label(InputActions.PageNext)));

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
        UpdatePaging();
    }

    /// <summary>Opens the Handbook at the page read last, or at <paramref name="fallback"/> before any (UI spec §10).</summary>
    public void OpenLast(string fallback)
    {
        var last = GameSettings.Current.Interface.HandbookPage;
        Open(last is not null && File.Exists(Path.Combine(DocsRoot, Path.GetFileName(last))) ? last : fallback);
    }

    /// <summary>Opens the Handbook at the page read last (or <paramref name="fallback"/>), or hides it when it is open.</summary>
    public void ToggleLast(string fallback)
    {
        if (Visible)
            Hide();
        else
            OpenLast(fallback);
    }

    /// <summary>Turns <paramref name="step"/> pages on through the listed pages (negative goes back), stopping at the ends.</summary>
    public void Turn(int step)
    {
        if (Handbook.Turn(_files, Current, step) is { } page && page != Current)
            Open(page);
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
        Current = Path.GetFileName(path);
        Render(path, fileName);
        if (File.Exists(path) && GameSettings.Current.Interface.HandbookPage != Current)
            GameSettings.Remember(GameSettings.Current with { Interface = GameSettings.Current.Interface with { HandbookPage = Current } });
        if (!_files.Contains(Current))
            Search("");
        var index = _files.IndexOf(Current);
        if (index >= 0)
            _list.Select(index);
        UpdatePaging();
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
        if (!Visible || GameInput.Typing(GetViewport()))
            return;
        if (GameInput.Pressed(@event, InputActions.Handbook))
        {
            GetViewport().SetInputAsHandled();
            Hide();
        }
        else if (Topmost == this && (GameInput.Pressed(@event, InputActions.PagePrevious) || GameInput.Pressed(@event, InputActions.PageNext)))
        {
            GetViewport().SetInputAsHandled();
            Turn(GameInput.Pressed(@event, InputActions.PageNext) ? 1 : -1);
        }
    }

    // The page as text and diagrams, each diagram as wide as fits (never wider than drawn).
    private void Render(string path, string asked)
    {
        _text.Clear();
        Diagrams.Clear();
        _drawnWidth = _text.Size.X;
        if (!File.Exists(path))
        {
            _text.AppendText(Loc.T("No help file '{0}' in {1}.", MarkdownBbcode.Escape(asked), MarkdownBbcode.Escape(DocsRoot)));
            return;
        }
        var fit = Math.Max(64, (int)_text.Size.X - 32);
        foreach (var block in Handbook.Split(File.ReadAllText(path)))
        {
            if (block.Markdown is { } markdown)
            {
                _text.AppendText(MarkdownBbcode.Convert(markdown));
                continue;
            }
            if (Diagram(block.Image!, fit) is not { } texture)
            {
                _text.AppendText($"[i]{MarkdownBbcode.Escape(block.Caption ?? block.Image!)}[/i]\n");
                continue;
            }
            _text.PushParagraph(HorizontalAlignment.Center);
            _text.AddImage(texture, texture.GetWidth(), texture.GetHeight());
            _text.Pop();
            Diagrams.Add((block.Image!, texture.GetWidth()));
            if (!string.IsNullOrWhiteSpace(block.Caption))
                _text.AppendText($"[center][i]{MarkdownBbcode.Escape(block.Caption)}[/i][/center]\n");
            _text.AppendText("\n");
        }
    }

    // A diagram from docs/, drawn at most fit pixels wide: an SVG is drawn at that size, other images scaled down.
    private Texture2D? Diagram(string relative, int fit)
    {
        var path = Path.GetFullPath(Path.Combine(DocsRoot, relative));
        if (!path.StartsWith(Path.GetFullPath(DocsRoot) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path))
            return null;
        if (_diagrams.TryGetValue((path, fit), out var cached))
            return cached;
        var image = new Image();
        Error error;
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var svg = File.ReadAllText(path);
            error = image.LoadSvgFromString(svg);
            if (error == Error.Ok && image.GetWidth() > fit)
                error = image.LoadSvgFromString(svg, (float)fit / image.GetWidth());
        }
        else
        {
            error = image.Load(path);
            if (error == Error.Ok && image.GetWidth() > fit)
                image.Resize(fit, Math.Max(1, image.GetHeight() * fit / image.GetWidth()), Image.Interpolation.Lanczos);
        }
        var texture = error == Error.Ok ? ImageTexture.CreateFromImage(image) : null;
        _diagrams[(path, fit)] = texture;
        return texture;
    }

    // A page with diagrams is drawn again when the page changes width, keeping its place.
    private void Reflow()
    {
        if (Current is null || Diagrams.Count == 0 || Mathf.Abs(_text.Size.X - _drawnWidth) < 8)
            return;
        var at = _text.GetVScrollBar().Value;
        Render(Path.Combine(DocsRoot, Current), Current);
        Callable.From(() => _text.GetVScrollBar().Value = at).CallDeferred();
    }

    private void UpdatePaging()
    {
        _page.Text = Handbook.Position(_files, Current);
        var index = Current is null ? -1 : _files.IndexOf(Current);
        _previous.Disabled = index <= 0;
        _next.Disabled = index < 0 || index >= _files.Count - 1;
    }

    private void FollowLink(string link)
    {
        if (link.Contains("://"))
            OS.ShellOpen(link);
        else if (link.Split('#')[0] is var file && file.Contains('/'))
            OpenOutside(file);
        else if (file.EndsWith(".md", StringComparison.Ordinal))
            Open(link);
    }

    /// <summary>
    /// The Handbook lists only the top-level pages, so a link into a sub-folder (<c>ai/reference.md</c>)
    /// opens the shipped file in the system's viewer instead of a page that isn't there.
    /// </summary>
    private static void OpenOutside(string relative)
    {
        var root = Path.GetFullPath(DocsRoot);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(path))
            OS.ShellOpen(path);
    }

    private string TitleOf(string file)
    {
        var first = File.ReadLines(Path.Combine(DocsRoot, file)).FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));
        return first?[2..].Trim() ?? file;
    }
}
