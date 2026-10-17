using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mechanica.Core.Reports;

/// <summary>Arguments of <c>reports.note</c>.</summary>
/// <param name="Title">One line, at most 200 characters.</param>
/// <param name="Text">The note (Markdown).</param>
/// <param name="Tags">Tags for filtering: lower case letters, digits, <c>-</c> and <c>_</c>.</param>
/// <param name="Screenshot">True to attach what the host's screen shows.</param>
public sealed record NoteParams(string Title, string Text = "", IReadOnlyList<string>? Tags = null, bool Screenshot = false);

/// <summary>Arguments of <c>reports.recommend</c>.</summary>
/// <param name="Title">One line, at most 200 characters.</param>
/// <param name="Text">What to change and why (Markdown).</param>
/// <param name="Area">The feature, tool, part or mod it is about.</param>
/// <param name="Priority">How much it matters.</param>
/// <param name="Tags">Tags for filtering.</param>
/// <param name="Screenshot">True to attach what the host's screen shows.</param>
public sealed record RecommendParams(string Title, string Text, string? Area = null, RecommendationPriority Priority = RecommendationPriority.Medium,
    IReadOnlyList<string>? Tags = null, bool Screenshot = false);

/// <summary>Arguments of <c>reports.bug</c>.</summary>
/// <param name="Title">One line, at most 200 characters.</param>
/// <param name="Steps">Steps to reproduce, in order.</param>
/// <param name="Expected">What should have happened.</param>
/// <param name="Actual">What happened instead.</param>
/// <param name="Severity">How bad it is.</param>
/// <param name="Text">More detail (Markdown).</param>
/// <param name="Area">The feature, tool, part or mod it is about.</param>
/// <param name="Tags">Tags for filtering.</param>
/// <param name="Screenshot">True (the default) to attach what the host's screen shows; a headless host notes why it has none.</param>
/// <param name="ReplayActions">Most state-changing calls to keep in the replay file, newest last; null keeps every call since the world started.</param>
/// <param name="Messages">Most recent log lines to keep (0 to 200).</param>
public sealed record BugParams(string Title, IReadOnlyList<string> Steps, string Expected, string Actual, BugSeverity Severity = BugSeverity.Medium,
    string Text = "", string? Area = null, IReadOnlyList<string>? Tags = null, bool Screenshot = true, int? ReplayActions = null, int Messages = 50);

/// <summary>Arguments of <c>reports.list</c>.</summary>
/// <param name="Kind">Only this kind.</param>
/// <param name="Session">Only this session (a folder name, or <c>current</c>); every session when left out.</param>
/// <param name="Tag">Only reports with this tag.</param>
/// <param name="Limit">Most reports returned, newest first (1 to 500).</param>
public sealed record ListReportsParams(ReportKind? Kind = null, string? Session = null, string? Tag = null, int Limit = 50);

/// <summary>Arguments of <c>reports.get</c>.</summary>
/// <param name="Id">The report's id.</param>
/// <param name="Replay">True to include a bug's replay slice.</param>
/// <param name="Screenshot">True to include the screenshot as base64 PNG.</param>
public sealed record GetReportParams(string Id, bool Replay = false, bool Screenshot = false);

/// <summary>Arguments of <c>reports.delete</c>.</summary>
/// <param name="Id">The report's id.</param>
public sealed record DeleteReportParams(string Id);

/// <summary>A bad report request; the message says what to fix.</summary>
public sealed class ReportException(string message) : Exception(message);

/// <summary>A PNG picture of the screen, or why there is none.</summary>
/// <param name="Png">PNG bytes, or null.</param>
/// <param name="Error">Why there is no picture (a headless host), or null.</param>
public readonly record struct Screenshot(byte[]? Png, string? Error)
{
    /// <summary>Width and height from the PNG header, or (0, 0).</summary>
    public (int Width, int Height) Size => Png is { Length: >= 24 } png && png[12] == 'I' && png[13] == 'H' && png[14] == 'D' && png[15] == 'R'
        ? (png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19], png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23])
        : (0, 0);
}

/// <summary>
/// Writes AI notes, recommendations and bug reports (AUDIT §4.5): checks what the agent sent, fills in the
/// game's context, the replay slice and recent log lines from <see cref="RecentActivity"/> and an optional
/// screenshot, and saves them in the <see cref="ReportStore"/>. The endpoint's <c>reports.*</c> methods
/// and the AI Reports window both work through it.
/// </summary>
public sealed partial class Reporter(ReportStore store, RecentActivity activity, Func<ReportContext> context, Func<Screenshot>? screenshot = null,
    Func<DateTimeOffset>? clock = null)
{
    /// <summary>Longest title.</summary>
    public const int MaxTitle = 200;

    /// <summary>Longest text field.</summary>
    public const int MaxText = 20_000;

    /// <summary>Most steps or tags.</summary>
    public const int MaxItems = 50;

    /// <summary>Most log lines a bug keeps.</summary>
    public const int MaxMessages = 200;

    /// <summary>Most reports one list returns.</summary>
    public const int MaxList = 500;

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    /// <summary>The store reports go to.</summary>
    public ReportStore Store => store;

    /// <summary>Raised with each new report, for the <c>reports</c> event topic.</summary>
    public event Action<AiReport>? Added;

    /// <summary>Saves a note.</summary>
    /// <exception cref="ReportException">A field is missing or too long.</exception>
    public AiReport Note(string author, NoteParams p) =>
        Add(Base(ReportKind.Note, author, p.Title, p.Text, p.Tags, null), null, p.Screenshot);

    /// <summary>Saves a recommendation.</summary>
    /// <exception cref="ReportException">A field is missing or too long.</exception>
    public AiReport Recommend(string author, RecommendParams p)
    {
        if (string.IsNullOrWhiteSpace(p.Text))
            throw new ReportException("text must say what to change and why.");
        return Add(Base(ReportKind.Recommendation, author, p.Title, p.Text, p.Tags, p.Area) with { Priority = p.Priority }, null, p.Screenshot);
    }

    /// <summary>Saves a bug report with its replay slice and recent log lines.</summary>
    /// <exception cref="ReportException">A field is missing or too long.</exception>
    public AiReport Bug(string author, BugParams p)
    {
        if (p.Steps.Count is 0 or > MaxItems)
            throw new ReportException($"steps needs 1 to {MaxItems} steps to reproduce.");
        for (var i = 0; i < p.Steps.Count; i++)
            CheckLine(p.Steps[i], $"steps[{i}]", MaxText);
        CheckText(p.Expected, "expected", required: true);
        CheckText(p.Actual, "actual", required: true);
        if (p.ReplayActions is < 0)
            throw new ReportException("replay_actions must be 0 or more (or left out for every call).");
        if (p.Messages is < 0 or > MaxMessages)
            throw new ReportException($"messages must be from 0 to {MaxMessages}.");
        var report = Base(ReportKind.Bug, author, p.Title, p.Text, p.Tags, p.Area) with
        {
            Steps = [.. p.Steps.Select(s => s.Trim())],
            Expected = p.Expected.Trim(),
            Actual = p.Actual.Trim(),
            Severity = p.Severity,
            Messages = activity.Messages(p.Messages),
        };
        var replay = p.ReplayActions == 0 ? null : activity.Slice(p.ReplayActions ?? activity.MaxActions);
        return Add(report, replay, p.Screenshot);
    }

    /// <summary>Lists reports, newest first.</summary>
    /// <exception cref="ReportException">The limit or session is out of range.</exception>
    public IReadOnlyList<ReportSummary> List(ListReportsParams p)
    {
        if (p.Limit is < 1 or > MaxList)
            throw new ReportException($"limit must be from 1 to {MaxList}.");
        var session = p.Session == "current" ? store.Session : p.Session;
        if (session is not null && !store.Sessions().Contains(session) && session != store.Session)
            throw new ReportException($"No session named '{Shorten(session)}'.");
        return store.List(new ReportFilter(p.Kind, session, p.Tag, p.Limit));
    }

    /// <summary>A report as JSON, with its replay slice and screenshot when asked for.</summary>
    /// <exception cref="ReportException">There is no such report.</exception>
    public JsonElement Get(GetReportParams p)
    {
        var report = store.Get(p.Id) ?? throw new ReportException($"No report '{Shorten(p.Id)}'.");
        var json = System.Text.Json.Nodes.JsonNode.Parse(ReportStore.ToElement(report).GetRawText())!.AsObject();
        if (p.Replay && store.ReadReplay(p.Id) is { } replay)
            json["replay"] = System.Text.Json.Nodes.JsonNode.Parse(replay.ToElement().GetRawText());
        if (p.Screenshot && store.ReadScreenshot(p.Id) is { } png)
            json["screenshot_png"] = Convert.ToBase64String(png);
        return JsonSerializer.SerializeToElement(json);
    }

    /// <summary>Deletes a report.</summary>
    /// <exception cref="ReportException">There is no such report.</exception>
    public void Delete(DeleteReportParams p)
    {
        if (!store.Delete(p.Id))
            throw new ReportException($"No report '{Shorten(p.Id)}'.");
    }

    /// <summary>What the host's screen shows now.</summary>
    public Screenshot Capture() => screenshot?.Invoke() ?? new Screenshot(null, "This host takes no screenshots.");

    private AiReport Base(ReportKind kind, string author, string title, string text, IReadOnlyList<string>? tags, string? area)
    {
        CheckLine(title, "title", MaxTitle);
        CheckText(text, "text", required: false);
        if (area is not null)
            CheckLine(area, "area", MaxTitle);
        var cleanTags = new List<string>();
        foreach (var tag in tags ?? [])
        {
            if (!TagPattern().IsMatch(tag))
                throw new ReportException($"Tag '{Shorten(tag)}' must be 1 to 32 lower case letters, digits, '-' or '_'.");
            if (!cleanTags.Contains(tag))
                cleanTags.Add(tag);
        }
        if (cleanTags.Count > MaxItems)
            throw new ReportException($"At most {MaxItems} tags.");
        return new AiReport
        {
            Id = "",
            Kind = kind,
            Created = _clock(),
            Author = author,
            Title = title.Trim(),
            Text = text.Trim(),
            Tags = cleanTags,
            Area = area?.Trim(),
            Context = context(),
        };
    }

    private AiReport Add(AiReport report, Remote.ActionRecording? replay, bool wantScreenshot)
    {
        byte[]? png = null;
        if (wantScreenshot)
        {
            var shot = Capture();
            png = shot.Png;
            if (png is null)
                report = report with { ScreenshotError = shot.Error ?? "No picture." };
        }
        try
        {
            report = store.Add(report, replay, png);
        }
        catch (InvalidOperationException e)
        {
            throw new ReportException(e.Message);
        }
        Added?.Invoke(report);
        return report;
    }

    private static void CheckLine(string value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ReportException($"{name} can't be empty.");
        if (value.Length > max)
            throw new ReportException($"{name} is limited to {max} characters.");
        if (value.Any(c => char.IsControl(c)))
            throw new ReportException($"{name} must be one line.");
    }

    private static void CheckText(string value, string name, bool required)
    {
        if (required && string.IsNullOrWhiteSpace(value))
            throw new ReportException($"{name} can't be empty.");
        if (value.Length > MaxText)
            throw new ReportException($"{name} is limited to {MaxText} characters.");
    }

    private static string Shorten(string text) => text.Length <= 64 ? text : text[..64] + "…";

    [GeneratedRegex("^[a-z0-9_-]{1,32}$")]
    private static partial Regex TagPattern();
}
