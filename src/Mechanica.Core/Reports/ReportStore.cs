using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Mechanica.Core.Remote;

namespace Mechanica.Core.Reports;

/// <summary>Which reports a list shows.</summary>
/// <param name="Kind">Only this kind, or every kind.</param>
/// <param name="Session">Only this session's folder, or every session.</param>
/// <param name="Tag">Only reports with this tag.</param>
/// <param name="Limit">Most reports returned, newest first.</param>
public sealed record ReportFilter(ReportKind? Kind = null, string? Session = null, string? Tag = null, int Limit = int.MaxValue);

/// <summary>
/// The folder of AI reports (<c>user://ai_reports/</c> in the game). Each run of the host is a session with
/// its own folder, named after the time it started (<c>20261017-183845</c>), holding every report as
/// <c>&lt;kind&gt;-&lt;n&gt;.json</c> and <c>.md</c>, a bug's <c>.replay.json</c> and <c>.png</c>, and an
/// <c>index.md</c> listing them. JSON is written pretty-printed with a stable key order (D8); Markdown is
/// for people and AIs reading the folder.
/// </summary>
public sealed partial class ReportStore
{
    /// <summary>Most reports one session keeps, so a looping agent can't fill the disk.</summary>
    public const int MaxPerSession = 5000;

    private static readonly JsonSerializerOptions WriteOptions = CreateOptions(indented: true);
    private static readonly JsonSerializerOptions ReadOptions = CreateOptions(indented: false);

    private readonly object _gate = new();
    private readonly Dictionary<ReportKind, int> _next = [];

    /// <summary>Opens the folder <paramref name="root"/> and starts a new session in it.</summary>
    /// <param name="root">The reports folder; made when the first report is written.</param>
    /// <param name="started">When the session started; its folder is named after it (UTC).</param>
    public ReportStore(string root, DateTimeOffset started)
    {
        Root = root;
        var name = started.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var session = name;
        for (var n = 2; System.IO.Directory.Exists(System.IO.Path.Combine(root, session)); n++)
            session = $"{name}-{n}";
        Session = session;
    }

    /// <summary>The reports folder.</summary>
    public string Root { get; }

    /// <summary>This session's folder name.</summary>
    public string Session { get; }

    /// <summary>This session's folder.</summary>
    public string SessionDirectory => System.IO.Path.Combine(Root, Session);

    /// <summary>Raised after a report is added or deleted, with its id.</summary>
    public event Action<string>? Changed;

    /// <summary>True when <paramref name="id"/> has the shape of a report id (and so names no other file).</summary>
    public static bool IsId(string id) => IdPattern().IsMatch(id);

    /// <summary>
    /// Saves <paramref name="report"/> in this session under the next free file name for its kind, with its
    /// replay slice and screenshot, and returns it with its id and file names filled in.
    /// </summary>
    /// <exception cref="InvalidOperationException">The session already holds <see cref="MaxPerSession"/> reports.</exception>
    public AiReport Add(AiReport report, ActionRecording? replay = null, byte[]? screenshot = null)
    {
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(SessionDirectory);
            var existing = System.IO.Directory.GetFiles(SessionDirectory, "*.json").Count(p => !p.EndsWith(".replay.json", StringComparison.Ordinal));
            if (existing >= MaxPerSession)
                throw new InvalidOperationException($"This session already has {MaxPerSession} reports.");
            string file;
            do
                file = $"{Prefix(report.Kind)}-{_next[report.Kind] = _next.GetValueOrDefault(report.Kind) + 1:000}";
            while (File.Exists(PathOf(Session, file, ".json")));
            report = report with
            {
                Id = $"{Session}/{file}",
                ReplayFile = replay is null ? null : file + ".replay.json",
                ReplayActions = replay?.Actions.Count,
                ReplayTruncated = replay?.Truncated,
                Screenshot = screenshot is null ? null : file + ".png",
            };
            if (replay is not null)
                WriteAtomic(PathOf(Session, file, ".replay.json"), Encoding.UTF8.GetBytes(replay.ToJson()));
            if (screenshot is not null)
                WriteAtomic(PathOf(Session, file, ".png"), screenshot);
            WriteAtomic(PathOf(Session, file, ".md"), Encoding.UTF8.GetBytes(ReportMarkdown.Render(report)));
            WriteAtomic(PathOf(Session, file, ".json"), Encoding.UTF8.GetBytes(ToJson(report)));
            WriteIndex(Session);
        }
        Changed?.Invoke(report.Id);
        return report;
    }

    /// <summary>Every session folder, newest first.</summary>
    public IReadOnlyList<string> Sessions()
    {
        if (!System.IO.Directory.Exists(Root))
            return [];
        return [.. System.IO.Directory.GetDirectories(Root).Select(System.IO.Path.GetFileName).OfType<string>()
            .Where(s => SessionPattern().IsMatch(s)).OrderDescending(StringComparer.Ordinal)];
    }

    /// <summary>Reports matching <paramref name="filter"/>, newest first. Files that don't read are skipped.</summary>
    public IReadOnlyList<ReportSummary> List(ReportFilter? filter = null)
    {
        filter ??= new ReportFilter();
        var sessions = filter.Session is { } only ? (SessionPattern().IsMatch(only) ? [only] : []) : Sessions();
        var list = new List<ReportSummary>();
        foreach (var session in sessions)
        {
            var folder = System.IO.Path.Combine(Root, session);
            if (!System.IO.Directory.Exists(folder))
                continue;
            foreach (var path in System.IO.Directory.GetFiles(folder, "*.json"))
            {
                var file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!IsId($"{session}/{file}") || Read(path) is not { } report)
                    continue;
                if ((filter.Kind is { } kind && report.Kind != kind) || (filter.Tag is { } tag && !report.Tags.Contains(tag)))
                    continue;
                list.Add(ReportSummary.Of(report));
            }
        }
        return [.. list.OrderByDescending(r => r.Created).ThenByDescending(r => r.Id, StringComparer.Ordinal).Take(Math.Max(0, filter.Limit))];
    }

    /// <summary>The report <paramref name="id"/>, or null when there is none (or the id is not one).</summary>
    public AiReport? Get(string id) => IsId(id) ? Read(PathOf(id, ".json")) : null;

    /// <summary>The bug's replay slice, or null.</summary>
    public ActionRecording? ReadReplay(string id)
    {
        if (!IsId(id) || !File.Exists(PathOf(id, ".replay.json")))
            return null;
        try
        {
            return ActionRecording.FromJson(File.ReadAllText(PathOf(id, ".replay.json")));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The report's screenshot as PNG bytes, or null.</summary>
    public byte[]? ReadScreenshot(string id) => IsId(id) && File.Exists(PathOf(id, ".png")) ? File.ReadAllBytes(PathOf(id, ".png")) : null;

    /// <summary>The report's Markdown file, or null.</summary>
    public string? ReadMarkdown(string id) => IsId(id) && File.Exists(PathOf(id, ".md")) ? File.ReadAllText(PathOf(id, ".md")) : null;

    /// <summary>Full path of one of a report's files (<c>.json</c>, <c>.md</c>, <c>.png</c>, <c>.replay.json</c>).</summary>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a report id.</exception>
    public string PathOf(string id, string extension)
    {
        if (!IsId(id))
            throw new ArgumentException($"'{id}' is not a report id.", nameof(id));
        var slash = id.IndexOf('/');
        return PathOf(id[..slash], id[(slash + 1)..], extension);
    }

    /// <summary>Deletes a report and its files; false when there was none.</summary>
    public bool Delete(string id)
    {
        if (Get(id) is null)
            return false;
        lock (_gate)
        {
            foreach (var extension in new[] { ".json", ".md", ".png", ".replay.json" })
            {
                var path = PathOf(id, extension);
                if (File.Exists(path))
                    File.Delete(path);
            }
            WriteIndex(id[..id.IndexOf('/')]);
        }
        Changed?.Invoke(id);
        return true;
    }

    /// <summary>The JSON file form.</summary>
    public static string ToJson(AiReport report) => JsonSerializer.Serialize(report, WriteOptions) + "\n";

    /// <summary>The report as a JSON value, for JSON-RPC results.</summary>
    public static JsonElement ToElement(AiReport report) => JsonSerializer.SerializeToElement(report, ReadOptions);

    /// <summary>A summary as a JSON value.</summary>
    public static JsonElement ToElement(IReadOnlyList<ReportSummary> list) => JsonSerializer.SerializeToElement(list, ReadOptions);

    /// <summary>Parses a report file; null when it is not one this build reads.</summary>
    public static AiReport? FromJson(string json)
    {
        try
        {
            var report = JsonSerializer.Deserialize<AiReport>(json, ReadOptions);
            return report is { FormatVersion: AiReport.CurrentFormat } && IsId(report.Id) ? report : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AiReport? Read(string path)
    {
        try
        {
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private string PathOf(string session, string file, string extension) => System.IO.Path.Combine(Root, session, file + extension);

    private void WriteIndex(string session)
    {
        var folder = System.IO.Path.Combine(Root, session);
        if (!System.IO.Directory.Exists(folder))
            return;
        var reports = List(new ReportFilter(Session: session)).Reverse().ToList();
        WriteAtomic(System.IO.Path.Combine(folder, "index.md"), Encoding.UTF8.GetBytes(ReportMarkdown.Index(session, reports)));
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The file name prefix for <paramref name="kind"/>.</summary>
    public static string Prefix(ReportKind kind) => kind switch
    {
        ReportKind.Note => "note",
        ReportKind.Recommendation => "recommendation",
        _ => "bug",
    };

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            RespectNullableAnnotations = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
            MaxDepth = 64,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    [GeneratedRegex(@"^\d{8}-\d{6}(-\d+)?$")]
    private static partial Regex SessionPattern();

    [GeneratedRegex(@"^\d{8}-\d{6}(-\d+)?/(note|recommendation|bug)-\d{3,}$")]
    private static partial Regex IdPattern();
}
