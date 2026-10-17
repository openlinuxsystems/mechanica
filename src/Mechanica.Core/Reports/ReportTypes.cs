using System.Text.Json.Serialization;
using Mechanica.Core.Data;

namespace Mechanica.Core.Reports;

/// <summary>What a report is.</summary>
public enum ReportKind
{
    /// <summary>Something the agent noticed or wants to remember.</summary>
    Note,

    /// <summary>A suggested change to the game or a mod (balance, controls, content).</summary>
    Recommendation,

    /// <summary>Something that went wrong, with how to make it happen again.</summary>
    Bug,
}

/// <summary>How bad a bug is.</summary>
public enum BugSeverity
{
    /// <summary>Cosmetic, or a small annoyance with an easy way around it.</summary>
    Low,

    /// <summary>A feature misbehaves but play goes on.</summary>
    Medium,

    /// <summary>A feature does not work, or progress is blocked.</summary>
    High,

    /// <summary>A crash, lost save, desync or anything that breaks the session.</summary>
    Critical,
}

/// <summary>How much a recommendation matters to the agent that made it.</summary>
public enum RecommendationPriority
{
    /// <summary>Nice to have.</summary>
    Low,

    /// <summary>Worth doing.</summary>
    Medium,

    /// <summary>Should be done soon.</summary>
    High,
}

/// <summary>A player when the report was written.</summary>
/// <param name="Peer">Peer id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Position">Feet position in metres, when spawned.</param>
public sealed record ReportPlayer(int Peer, string Name, Vec3? Position = null);

/// <summary>A log line kept with a report: a part script's print or error, a bot's message, a host error.</summary>
/// <param name="Tick">Physics tick it was logged on.</param>
/// <param name="Source">Where it came from: <c>lua</c>, <c>bot</c>, <c>host</c> or <c>actions</c>.</param>
/// <param name="Level"><c>info</c>, <c>warning</c> or <c>error</c>.</param>
/// <param name="Text">The line.</param>
public sealed record ReportMessage(long Tick, string Source, string Level, string Text);

/// <summary>
/// The game when a report was written, filled in by the host so the agent need not: version, mods, world
/// and who was playing. Lets a reader set up the same game again.
/// </summary>
public sealed record ReportContext
{
    /// <summary>Game version (<see cref="MechanicaInfo.Version"/>).</summary>
    public string GameVersion { get; init; } = MechanicaInfo.Version;

    /// <summary>The AI endpoint's protocol version.</summary>
    public int ProtocolVersion { get; init; }

    /// <summary>The content fingerprint joining clients are checked against.</summary>
    public string ContentFingerprint { get; init; } = "";

    /// <summary>Loaded mods, as <c>id version</c>, in load order.</summary>
    public IReadOnlyList<string> Mods { get; init; } = [];

    /// <summary>The world's name.</summary>
    public string World { get; init; } = "";

    /// <summary>The save the world was loaded from, or null for a new world.</summary>
    public string? LoadedFrom { get; init; }

    /// <summary>World seed.</summary>
    public long Seed { get; init; }

    /// <summary>Game mode, lower case (<c>creative</c>, <c>survival</c>).</summary>
    public string Mode { get; init; } = "";

    /// <summary>Difficulty, lower case.</summary>
    public string Difficulty { get; init; } = "";

    /// <summary>Whether players could hurt each other.</summary>
    public bool FriendlyFire { get; init; }

    /// <summary>Physics tick when written.</summary>
    public long Tick { get; init; }

    /// <summary>Seconds the world has run.</summary>
    public double WorldTime { get; init; }

    /// <summary>True when the host has no display (no screenshots).</summary>
    public bool Headless { get; init; }

    /// <summary>Players in the session.</summary>
    public IReadOnlyList<ReportPlayer> Players { get; init; } = [];
}

/// <summary>
/// One note, recommendation or bug report. Saved as <c>&lt;file&gt;.json</c> and <c>&lt;file&gt;.md</c> in its
/// session's folder; a bug also keeps the last calls before it as a replayable recording
/// (<c>&lt;file&gt;.replay.json</c>) and, when asked, a screenshot (<c>&lt;file&gt;.png</c>).
/// </summary>
public sealed record AiReport
{
    /// <summary>The format this code writes and reads.</summary>
    public const int CurrentFormat = 1;

    /// <summary>File format version.</summary>
    public int FormatVersion { get; init; } = CurrentFormat;

    /// <summary><c>&lt;session&gt;/&lt;file&gt;</c>, unique across sessions, e.g. <c>20261017-183845/bug-002</c>.</summary>
    public required string Id { get; init; }

    /// <summary>What it is.</summary>
    public required ReportKind Kind { get; init; }

    /// <summary>When it was written (UTC).</summary>
    public required DateTimeOffset Created { get; init; }

    /// <summary>Who wrote it: the agent's name, or <c>agent &lt;connection&gt;</c>.</summary>
    public required string Author { get; init; }

    /// <summary>One line.</summary>
    public required string Title { get; init; }

    /// <summary>The note or recommendation itself, or a bug's description. May be Markdown.</summary>
    public string Text { get; init; } = "";

    /// <summary>Tags for filtering (lower case letters, digits, <c>-</c> and <c>_</c>).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>A recommendation's or bug's area: a feature, tool, part or mod id.</summary>
    public string? Area { get; init; }

    /// <summary>A recommendation's priority.</summary>
    public RecommendationPriority? Priority { get; init; }

    /// <summary>A bug's steps to reproduce.</summary>
    public IReadOnlyList<string>? Steps { get; init; }

    /// <summary>What a bug's reporter expected.</summary>
    public string? Expected { get; init; }

    /// <summary>What happened instead.</summary>
    public string? Actual { get; init; }

    /// <summary>A bug's severity.</summary>
    public BugSeverity? Severity { get; init; }

    /// <summary>The game when it was written.</summary>
    public required ReportContext Context { get; init; }

    /// <summary>Recent log lines (bugs).</summary>
    public IReadOnlyList<ReportMessage>? Messages { get; init; }

    /// <summary>Calls kept in the replay file (bugs).</summary>
    public int? ReplayActions { get; init; }

    /// <summary>True when the replay file lacks the session's earliest calls, so it may not reproduce the bug from a fresh world.</summary>
    public bool? ReplayTruncated { get; init; }

    /// <summary>The replay file's name in the session folder, or null.</summary>
    public string? ReplayFile { get; init; }

    /// <summary>The screenshot's file name in the session folder, or null.</summary>
    public string? Screenshot { get; init; }

    /// <summary>Why a screenshot was asked for but not taken (a headless host), or null.</summary>
    public string? ScreenshotError { get; init; }

    /// <summary>The session folder's name.</summary>
    [JsonIgnore]
    public string Session => Id[..Id.IndexOf('/')];

    /// <summary>The report's file name without extension.</summary>
    [JsonIgnore]
    public string File => Id[(Id.IndexOf('/') + 1)..];
}

/// <summary>A report as lists show it.</summary>
/// <param name="Id">Report id.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Created">When.</param>
/// <param name="Author">Who.</param>
/// <param name="Title">One line.</param>
/// <param name="Tags">Its tags.</param>
/// <param name="Severity">A bug's severity.</param>
/// <param name="Priority">A recommendation's priority.</param>
/// <param name="HasScreenshot">True when a screenshot is attached.</param>
public sealed record ReportSummary(string Id, ReportKind Kind, DateTimeOffset Created, string Author, string Title, IReadOnlyList<string> Tags,
    BugSeverity? Severity, RecommendationPriority? Priority, bool HasScreenshot)
{
    /// <summary>The summary of <paramref name="report"/>.</summary>
    public static ReportSummary Of(AiReport report) =>
        new(report.Id, report.Kind, report.Created, report.Author, report.Title, report.Tags, report.Severity, report.Priority, report.Screenshot is not null);
}
