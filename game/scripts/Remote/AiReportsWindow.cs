using System.Text;
using Godot;
using Mechanica.Core.Reports;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Remote;

/// <summary>
/// The AI Reports window (AUDIT §4.5), opened from the in-game menu: the notes, recommendations and bug
/// reports AI agents wrote through the endpoint, newest first, filtered by kind and session. Picking one
/// shows it in full with its screenshot. Refresh reads the folder again (another game, such as a headless
/// host, may be writing to it), Open folder shows the files, Copy puts the report's Markdown on the
/// clipboard, and Delete asks, then removes the report and its files.
/// </summary>
public partial class AiReportsWindow : ToolWindow
{
    /// <summary>The kind filter's choices, in order.</summary>
    public static readonly (string Label, ReportKind? Kind)[] Kinds =
        [("All reports", null), ("Notes", ReportKind.Note), ("Recommendations", ReportKind.Recommendation), ("Bugs", ReportKind.Bug)];

    private OptionButton _kind = null!;
    private OptionButton _session = null!;
    private ItemList _list = null!;
    private RichTextLabel _detail = null!;
    private TextureRect _picture = null!;
    private Label _status = null!;
    private Button _open = null!;
    private Button _copy = null!;
    private Button _delete = null!;
    private IReadOnlyList<ReportSummary> _shown = [];
    private IReadOnlyList<string> _sessions = [];

    /// <summary>The reports folder. Set before adding to the tree.</summary>
    public ReportStore Store { get; init; } = null!;

    /// <summary>Opens a folder in the system's file manager; tests replace it.</summary>
    public Action<string> OpenFolder { get; init; } = path => OS.ShellOpen(path);

    /// <summary>Puts text on the clipboard; tests replace it.</summary>
    public Action<string> Copy { get; init; } = DisplayServer.ClipboardSet;

    /// <summary>The reports listed now, newest first.</summary>
    public IReadOnlyList<ReportSummary> Listed => _shown;

    /// <summary>The report shown in full, or null.</summary>
    public AiReport? Selected { get; private set; }

    /// <summary>The detail pane's text, for tests.</summary>
    public string DetailText => _detail.GetParsedText();

    /// <summary>The status line, for tests.</summary>
    public string Status => _status.Text;

    /// <summary>True when the selected report's screenshot is showing.</summary>
    public bool ShowsPicture => _picture.Visible && _picture.Texture is not null;

    public override void _Ready()
    {
        Title = "AI Reports";
        Name = "AiReports";
        CustomMinimumSize = new Vector2(920, 580);

        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 8);
        _kind = new OptionButton { Name = "Kind", TooltipText = "Which reports to list" };
        foreach (var (label, _) in Kinds)
            _kind.AddItem(label);
        _kind.ItemSelected += _ => Refresh();
        filters.AddChild(_kind);
        _session = Loc.Verbatim(new OptionButton { Name = "Session", TooltipText = Loc.T("Which game session to list"), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _session.ItemSelected += _ => Refresh();
        filters.AddChild(_session);
        var refresh = new Button { Name = "Refresh", Text = "Refresh", TooltipText = "Read the reports folder again" };
        refresh.Pressed += () => Refresh(rereadSessions: true);
        filters.AddChild(refresh);
        Body.AddChild(filters);

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 420) };
        Body.AddChild(split);
        _list = Loc.Verbatim(new ItemList { Name = "List", CustomMinimumSize = new Vector2(330, 0), SizeFlagsVertical = SizeFlags.ExpandFill });
        _list.ItemSelected += index => Show((int)index);
        split.AddChild(_list);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(right);
        _detail = Loc.Verbatim(new RichTextLabel
        {
            Name = "Detail", BbcodeEnabled = true, SelectionEnabled = true, ContextMenuEnabled = true,
            SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(520, 260),
        });
        right.AddChild(_detail);
        _picture = new TextureRect
        {
            Name = "Screenshot", Visible = false, CustomMinimumSize = new Vector2(0, 150),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        right.AddChild(_picture);
        _status = new Label { Name = "Status", ThemeTypeVariation = "SecondaryLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Body.AddChild(_status);

        _open = AddAction("Open folder", OpenSelectedFolder, tooltip: "Show the report's files");
        _copy = AddAction("Copy", CopySelected, tooltip: "Copy the report as Markdown");
        _delete = AddAction("Delete", () => DeleteSelected(), tooltip: "Delete the report and its files");
        _delete.ThemeTypeVariation = "DangerButton";
        Store.Changed += OnChanged;
        Refresh(rereadSessions: true);
    }

    public override void _ExitTree()
    {
        Store.Changed -= OnChanged;
        base._ExitTree();
    }

    /// <summary>Picks the kind filter (an index into <see cref="Kinds"/>).</summary>
    public void FilterKind(int index)
    {
        _kind.Select(index);
        Refresh();
    }

    /// <summary>Picks the session filter: null for every session.</summary>
    public void FilterSession(string? session)
    {
        var index = session is null ? 0 : _sessions.ToList().IndexOf(session) + 1;
        _session.Select(Math.Max(0, index));
        Refresh();
    }

    /// <summary>Lists the reports again, keeping the selection when it is still there.</summary>
    public void Refresh(bool rereadSessions = false)
    {
        if (rereadSessions)
            FillSessions();
        var keep = Selected?.Id;
        var session = _session.Selected <= 0 ? null : _sessions[_session.Selected - 1];
        _shown = Store.List(new ReportFilter(Kinds[Math.Max(0, _kind.Selected)].Kind, session));
        _list.Clear();
        foreach (var report in _shown)
        {
            var index = _list.AddItem(Line(report));
            _list.SetItemTooltip(index, report.Id);
            _list.SetItemCustomFgColor(index, report.Kind == ReportKind.Bug && report.Severity is BugSeverity.High or BugSeverity.Critical ? UiTokens.Error : UiTokens.Text);
        }
        var again = keep is null ? -1 : _shown.ToList().FindIndex(r => r.Id == keep);
        if (again >= 0)
        {
            _list.Select(again);
            Show(again);
        }
        else
            Show(-1);
        _status.Text = _shown.Count == 0 ? Loc.T("No reports yet. AI agents write them with reports.note, reports.recommend and reports.bug.") : Loc.T("{0} reports.", _shown.Count);
    }

    /// <summary>Shows the listed report at <paramref name="index"/> in full; -1 clears the pane.</summary>
    public void Show(int index)
    {
        Selected = index >= 0 && index < _shown.Count ? Store.Get(_shown[index].Id) : null;
        _open.Disabled = _copy.Disabled = _delete.Disabled = Selected is null;
        _picture.Texture = null;
        _picture.Visible = false;
        if (Selected is not { } report)
        {
            _detail.Text = _shown.Count == 0 ? "" : Loc.T("Pick a report to read it.");
            return;
        }
        if (_list.GetSelectedItems() is not [var selected] || selected != index)
            _list.Select(index);
        _detail.Text = Render(report);
        if (Store.ReadScreenshot(report.Id) is { } png)
        {
            using var image = new Image();
            if (image.LoadPngFromBuffer(png) == Error.Ok)
            {
                _picture.Texture = ImageTexture.CreateFromImage(image);
                _picture.Visible = true;
            }
        }
    }

    /// <summary>Opens the selected report's session folder.</summary>
    public void OpenSelectedFolder()
    {
        if (Selected is not { } report)
            return;
        OpenFolder(System.IO.Path.GetDirectoryName(Store.PathOf(report.Id, ".json"))!);
    }

    /// <summary>Copies the selected report's Markdown.</summary>
    public void CopySelected()
    {
        if (Selected is not { } report)
            return;
        Copy(Store.ReadMarkdown(report.Id) ?? ReportMarkdown.Render(report));
        _status.Text = Loc.T("Copied {0} as Markdown.", report.Id);
    }

    /// <summary>Asks, then deletes the selected report.</summary>
    public ConfirmDialog? DeleteSelected()
    {
        if (Selected is not { } report)
            return null;
        return ConfirmDialog.Ask(this, "Delete report", Loc.T("Delete {0}? This can't be undone.", report.Title), "Delete", () =>
        {
            var deleted = Store.Delete(report.Id);
            Refresh(rereadSessions: true);
            _status.Text = deleted ? Loc.T("Deleted {0}.", report.Id) : Loc.T("Could not delete {0}.", report.Id);
        });
    }

    private void OnChanged(string id)
    {
        if (IsInsideTree())
            Refresh(rereadSessions: true);
    }

    private void FillSessions()
    {
        var current = _session.Selected <= 0 ? null : _sessions[_session.Selected - 1];
        _sessions = Store.Sessions();
        _session.Clear();
        _session.AddItem(Loc.T("All sessions"));
        foreach (var session in _sessions)
            _session.AddItem(session == Store.Session ? Loc.T("{0} (this game)", session) : session);
        var index = current is null ? 0 : _sessions.ToList().IndexOf(current) + 1;
        _session.Select(Math.Max(0, index));
    }

    private static string Line(ReportSummary report)
    {
        var level = report.Severity is { } s ? Loc.T(s.ToString()) : report.Priority is { } p ? Loc.T(p.ToString()) : null;
        var kind = Loc.T(ReportMarkdown.KindName(report.Kind));
        return level is null ? $"{kind}: {report.Title}" : $"{kind} ({level}): {report.Title}";
    }

    // The report as rich text; the agent's own words are escaped so they show as written.
    private static string Render(AiReport r)
    {
        var text = new StringBuilder();
        text.Append("[font_size=20][b]").Append(Escape(r.Title)).AppendLine("[/b][/font_size]");
        var facts = new List<string> { Loc.T(ReportMarkdown.KindName(r.Kind)) };
        if (r.Severity is { } severity)
            facts.Add(Loc.T("severity {0}", Loc.T(severity.ToString()).ToLowerInvariant()));
        if (r.Priority is { } priority)
            facts.Add(Loc.T("priority {0}", Loc.T(priority.ToString()).ToLowerInvariant()));
        facts.Add(Loc.T("by {0}", r.Author));
        facts.Add(r.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        facts.Add(Loc.T("tick {0}", r.Context.Tick));
        text.Append("[color=#").Append(UiTokens.TextSecondary.ToHtml(false)).Append(']').Append(Escape(string.Join("  ·  ", facts))).AppendLine("[/color]");
        text.Append("[color=#").Append(UiTokens.TextSecondary.ToHtml(false)).Append(']').Append(Escape(r.Id)).AppendLine("[/color]");
        if (r.Area is { } area)
            text.Append(Escape(Loc.T("Area: {0}", area))).AppendLine();
        if (r.Tags.Count > 0)
            text.Append(Escape(Loc.T("Tags: {0}", string.Join(", ", r.Tags)))).AppendLine();
        text.AppendLine();
        if (r.Text.Length > 0)
            text.AppendLine(Escape(r.Text)).AppendLine();
        if (r.Steps is { Count: > 0 } steps)
        {
            Heading(text, Loc.T("Steps to reproduce"));
            for (var i = 0; i < steps.Count; i++)
                text.Append(i + 1).Append(". ").AppendLine(Escape(steps[i]));
            text.AppendLine();
        }
        if (r.Expected is { } expected)
            Heading(text, Loc.T("Expected")).AppendLine(Escape(expected)).AppendLine();
        if (r.Actual is { } actual)
            Heading(text, Loc.T("Actual")).AppendLine(Escape(actual)).AppendLine();
        if (r.ScreenshotError is { } why)
            Heading(text, Loc.T("Screenshot")).AppendLine(Escape(Loc.T("Not taken: {0}", why))).AppendLine();
        if (r.ReplayFile is { } replay)
        {
            Heading(text, Loc.T("Replay"));
            text.AppendLine(Escape(Loc.T(r.ReplayTruncated == true ? "{0} holds the last {1} calls before this report." : "{0} holds all {1} calls since the world started.",
                replay, r.ReplayActions ?? 0)));
            text.Append("[code]").Append(Escape($"godot --path game -- {ReportMarkdown.ReplayFlags(r.Context)}--replay {replay}")).AppendLine("[/code]").AppendLine();
        }
        if (r.Messages is { Count: > 0 } messages)
        {
            Heading(text, Loc.T("Recent messages"));
            text.Append("[code]");
            foreach (var m in messages)
                text.Append(Escape($"[tick {m.Tick}] {m.Source} {m.Level}: {m.Text}")).Append('\n');
            text.AppendLine("[/code]").AppendLine();
        }
        var c = r.Context;
        Heading(text, Loc.T("Game"));
        text.AppendLine(Escape(Loc.T("Version {0}, world {1}, seed {2}, {3} {4}", c.GameVersion, c.World, c.Seed, c.Mode, c.Difficulty)));
        text.AppendLine(Escape(Loc.T("Mods: {0}", c.Mods.Count > 0 ? string.Join(", ", c.Mods) : Loc.T("none"))));
        text.AppendLine(Escape(Loc.T("Players: {0}", c.Players.Count > 0 ? string.Join(", ", c.Players.Select(p => p.Name)) : Loc.T("none"))));
        return text.ToString();
    }

    private static StringBuilder Heading(StringBuilder text, string heading) =>
        text.Append("[b][color=#").Append(UiTokens.Cyan.ToHtml(false)).Append(']').Append(Escape(heading)).AppendLine("[/color][/b]");

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
