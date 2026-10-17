using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Remote;
using Mechanica.Core.Reports;

namespace Mechanica.Core.Tests.Reports;

public sealed class ReporterTests : IDisposable
{
    private sealed record MoveArgs(int Distance);

    private static readonly ActionCaller Agent = new(1, ActionSource.External);
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 18, 38, 45, TimeSpan.Zero);

    // A 2×3 PNG header (the rest of the file doesn't matter to the store).
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, 2, 0, 0, 0, 3, 8, 6, 0, 0, 0];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mechanica-reports-" + Guid.NewGuid().ToString("N"));
    private readonly ActionDispatcher _dispatcher = new();
    private readonly RecentActivity _activity;
    private readonly ReportStore _store;
    private Screenshot _screen = new(Png, null);
    private DateTimeOffset _now = Start;
    private int _moved;

    public ReporterTests()
    {
        _dispatcher.Register<MoveArgs>("Move", (_, a) => a.Distance > 10 ? "too far" : null, (_, a) =>
        {
            _moved += a.Distance;
            return null;
        });
        _dispatcher.Register<NoArgs>("Look", (_, _) => null, (_, _) => null, readOnly: true);
        _activity = new RecentActivity(_dispatcher, tickRate: 60, maxActions: 3);
        _store = new ReportStore(_root, Start);
    }

    public void Dispose()
    {
        _activity.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private Reporter NewReporter() => new(_store, _activity, () => new ReportContext
    {
        ProtocolVersion = 2,
        ContentFingerprint = "abc",
        Mods = ["vanilla 0.1.0"],
        World = "Test world",
        Seed = 1234,
        Mode = "survival",
        Difficulty = "easy",
        Tick = _activity.Now,
        WorldTime = _activity.Now / 60.0,
        Players = [new ReportPlayer(1, "Host", new Vec3(1, 2, 3))],
    }, () => _screen, () => _now += TimeSpan.FromSeconds(1));

    private static BugParams Bug(string title = "Wheel falls through the ground") =>
        new(title, ["Place a wheel", "Drive forward"], "The car drives", "The wheel sinks", BugSeverity.High, Tags: ["physics", "wheels"]);

    [Fact]
    public void ANoteIsSavedAsJsonAndMarkdownInTheSessionFolder()
    {
        var reporter = NewReporter();
        AiReport? added = null;
        reporter.Added += r => added = r;

        var note = reporter.Note("Grok", new NoteParams("Lift is easy to find", "The Lift sits by the spawn.", ["ux"]));

        Assert.Equal("20261017-183845/note-001", note.Id);
        Assert.Same(note, added);
        Assert.Equal("Grok", note.Author);
        Assert.Equal(1234, note.Context.Seed);
        Assert.Null(note.Screenshot);
        var folder = Path.Combine(_root, "20261017-183845");
        Assert.True(File.Exists(Path.Combine(folder, "note-001.json")));
        Assert.Contains("# Note: Lift is easy to find", File.ReadAllText(Path.Combine(folder, "note-001.md")));
        Assert.Contains("[note-001](note-001.md)", File.ReadAllText(Path.Combine(folder, "index.md")));
        // The file reads back as the same report.
        Assert.Equal(ReportStore.ToJson(note), ReportStore.ToJson(_store.Get(note.Id)!));
        Assert.DoesNotContain(".tmp", string.Join(",", Directory.GetFiles(folder)));
    }

    [Fact]
    public void ABugCarriesTheReplaySliceMessagesAndScreenshot()
    {
        var reporter = NewReporter();
        _activity.Tick(10);
        _dispatcher.Dispatch(Agent, "Move", """{"distance": 1}""");
        _dispatcher.Dispatch(Agent, "Look", "{}");
        _activity.Tick(14);
        _dispatcher.Dispatch(Agent, "Move", """{"distance": 2}""");
        _dispatcher.Dispatch(new ActionCaller(1, ActionSource.Script), "Move", """{"distance": 3}""");
        _activity.Log("lua", "error", "wheel.lua:3: attempt to index nil");

        var bug = reporter.Bug("Grok", Bug());

        Assert.Equal("20261017-183845/bug-001", bug.Id);
        Assert.Equal(BugSeverity.High, bug.Severity);
        Assert.Equal(["Place a wheel", "Drive forward"], bug.Steps!);
        Assert.Equal("bug-001.png", bug.Screenshot);
        Assert.Equal(Png, _store.ReadScreenshot(bug.Id));
        Assert.Equal([new ReportMessage(14, "lua", "error", "wheel.lua:3: attempt to index nil")], bug.Messages!);
        // Queries and script calls are left out, as in a recording; ticks start at 0.
        var replay = _store.ReadReplay(bug.Id)!;
        Assert.Equal([(0L, "Move"), (4L, "Move")], replay.Actions.Select(a => (a.Tick, a.Action)));
        Assert.False(replay.Truncated);
        Assert.Equal((2, false), (bug.ReplayActions, bug.ReplayTruncated));
        var markdown = _store.ReadMarkdown(bug.Id)!;
        Assert.Contains("1. Place a wheel", markdown);
        Assert.Contains("![Screenshot](bug-001.png)", markdown);
        Assert.Contains("godot --path game -- --host --seed 1234 --survival --easy --replay bug-001.replay.json", markdown);
        Assert.Contains("all 2 state-changing calls since the world started", markdown);
        Assert.Contains("[tick 14] lua error: wheel.lua:3: attempt to index nil", markdown);
    }

    [Fact]
    public void TheReplaySliceIsTruncatedOnceOldCallsAreDropped()
    {
        var reporter = NewReporter();
        for (var i = 0; i < 5; i++)
            _dispatcher.Dispatch(Agent, "Move", $$"""{"distance": {{i}}}""");

        var all = reporter.Bug("Grok", Bug());
        var last = reporter.Bug("Grok", Bug() with { ReplayActions = 1 });
        var none = reporter.Bug("Grok", Bug() with { ReplayActions = 0 });

        Assert.Equal((3, true), (all.ReplayActions, all.ReplayTruncated));
        Assert.Equal([2, 3, 4], _store.ReadReplay(all.Id)!.Actions.Select(a => a.Args.GetProperty("distance").GetInt32()));
        Assert.Equal([4], _store.ReadReplay(last.Id)!.Actions.Select(a => a.Args.GetProperty("distance").GetInt32()));
        Assert.Null(none.ReplayFile);
        Assert.Contains("the last 3", _store.ReadMarkdown(all.Id));
    }

    [Fact]
    public void AHeadlessHostSaysWhyThereIsNoScreenshot()
    {
        _screen = new Screenshot(null, "The host is headless.");
        var bug = NewReporter().Bug("Grok", Bug());

        Assert.Null(bug.Screenshot);
        Assert.Equal("The host is headless.", bug.ScreenshotError);
        Assert.Contains("Not taken: The host is headless.", _store.ReadMarkdown(bug.Id));
    }

    [Theory]
    [InlineData("", "title can't be empty.")]
    [InlineData("two\nlines", "title must be one line.")]
    public void BadFieldsAreRefused(string title, string message)
    {
        var e = Assert.Throws<ReportException>(() => NewReporter().Note("Grok", new NoteParams(title)));
        Assert.Equal(message, e.Message);
    }

    [Fact]
    public void BugsAndRecommendationsNeedTheirFields()
    {
        var reporter = NewReporter();

        Assert.Throws<ReportException>(() => reporter.Bug("Grok", Bug() with { Steps = [] }));
        Assert.Throws<ReportException>(() => reporter.Bug("Grok", Bug() with { Actual = " " }));
        Assert.Throws<ReportException>(() => reporter.Bug("Grok", Bug() with { Messages = 201 }));
        Assert.Throws<ReportException>(() => reporter.Bug("Grok", Bug() with { Tags = ["Not OK"] }));
        Assert.Throws<ReportException>(() => reporter.Recommend("Grok", new RecommendParams("Cheaper wheels", "")));
        Assert.Throws<ReportException>(() => reporter.Note("Grok", new NoteParams(new string('x', Reporter.MaxTitle + 1))));
        Assert.Empty(_store.List());
    }

    [Fact]
    public void ListsFilterAndGetIncludesTheExtras()
    {
        var reporter = NewReporter();
        _dispatcher.Dispatch(Agent, "Move", """{"distance": 1}""");
        var note = reporter.Note("Grok", new NoteParams("A note", Tags: ["ux"]));
        var recommendation = reporter.Recommend("Grok", new RecommendParams("Cheaper wheels", "Wheels cost too much.", "vanilla:wheel", RecommendationPriority.High));
        var bug = reporter.Bug("Grok", Bug());

        Assert.Equal([bug.Id, recommendation.Id, note.Id], reporter.List(new ListReportsParams()).Select(r => r.Id));
        Assert.Equal([recommendation.Id], reporter.List(new ListReportsParams(Kind: ReportKind.Recommendation)).Select(r => r.Id));
        Assert.Equal([note.Id], reporter.List(new ListReportsParams(Tag: "ux")).Select(r => r.Id));
        Assert.Equal([bug.Id], reporter.List(new ListReportsParams(Tag: "wheels")).Select(r => r.Id));
        Assert.Equal(2, reporter.List(new ListReportsParams(Session: "current", Limit: 2)).Count);
        Assert.Throws<ReportException>(() => reporter.List(new ListReportsParams(Limit: 0)));
        Assert.Throws<ReportException>(() => reporter.List(new ListReportsParams(Session: "../etc")));

        var full = reporter.Get(new GetReportParams(bug.Id, Replay: true, Screenshot: true));
        Assert.Equal("bug", full.GetProperty("kind").GetString());
        Assert.Equal(1, full.GetProperty("replay").GetProperty("actions").GetArrayLength());
        Assert.Equal(Png, Convert.FromBase64String(full.GetProperty("screenshot_png").GetString()!));
        var plain = reporter.Get(new GetReportParams(note.Id));
        Assert.False(plain.TryGetProperty("replay", out _));
        Assert.Equal("high", reporter.Get(new GetReportParams(recommendation.Id)).GetProperty("priority").GetString());
        Assert.Throws<ReportException>(() => reporter.Get(new GetReportParams("20261017-183845/bug-999")));
    }

    [Fact]
    public void DeletingRemovesEveryFileAndTheIndexLine()
    {
        var reporter = NewReporter();
        var bug = reporter.Bug("Grok", Bug());
        var other = reporter.Note("Grok", new NoteParams("Keep me"));
        var changed = new List<string>();
        _store.Changed += changed.Add;

        reporter.Delete(new DeleteReportParams(bug.Id));

        Assert.Equal([bug.Id], changed);
        Assert.Null(_store.Get(bug.Id));
        Assert.False(File.Exists(_store.PathOf(bug.Id, ".png")));
        Assert.False(File.Exists(_store.PathOf(bug.Id, ".replay.json")));
        var index = File.ReadAllText(Path.Combine(_store.SessionDirectory, "index.md"));
        Assert.DoesNotContain("bug-001", index);
        Assert.Contains("note-001", index);
        Assert.NotNull(_store.Get(other.Id));
        Assert.Throws<ReportException>(() => reporter.Delete(new DeleteReportParams(bug.Id)));
    }

    [Fact]
    public void IdsNeverNameOtherFiles()
    {
        Assert.True(ReportStore.IsId("20261017-183845/bug-001"));
        Assert.True(ReportStore.IsId("20261017-183845-2/recommendation-1234"));
        Assert.False(ReportStore.IsId("../20261017-183845/bug-001"));
        Assert.False(ReportStore.IsId("20261017-183845/../../bug-001"));
        Assert.False(ReportStore.IsId("20261017-183845/save-001"));
        Assert.Null(_store.Get("/etc/passwd"));
        Assert.Throws<ArgumentException>(() => _store.PathOf("x/../y", ".json"));
    }

    [Fact]
    public void ANewSessionInAnUsedFolderGetsItsOwnName()
    {
        NewReporter().Note("Grok", new NoteParams("First run"));
        var second = new ReportStore(_root, Start);

        Assert.Equal("20261017-183845-2", second.Session);
        var note = second.Add(new AiReport { Id = "", Kind = ReportKind.Note, Created = Start, Author = "Claude", Title = "Second run", Context = new ReportContext() });
        Assert.Equal("20261017-183845-2/note-001", note.Id);
        Assert.Equal(["20261017-183845-2", "20261017-183845"], second.Sessions());
        Assert.Equal(2, second.List().Count);
    }

    [Fact]
    public void UnreadableFilesAreSkipped()
    {
        var note = NewReporter().Note("Grok", new NoteParams("Fine"));
        File.WriteAllText(Path.Combine(_store.SessionDirectory, "note-777.json"), "{ not json");
        File.WriteAllText(Path.Combine(_store.SessionDirectory, "notes.json"), "{}");

        Assert.Equal([note.Id], _store.List().Select(r => r.Id));
        Assert.Null(_store.Get(_store.Session + "/note-777"));
    }

    [Fact]
    public void ScreenshotsKnowTheirSize() => Assert.Equal((2, 3), new Screenshot(Png, null).Size);

    [Fact]
    public void ReportJsonUsesSnakeCaseAndSkipsEmptyFields()
    {
        var bug = NewReporter().Bug("Grok", Bug());
        using var json = JsonDocument.Parse(File.ReadAllText(_store.PathOf(bug.Id, ".json")));
        var root = json.RootElement;

        Assert.Equal(1, root.GetProperty("format_version").GetInt32());
        Assert.Equal("high", root.GetProperty("severity").GetString());
        Assert.Equal("bug-001.replay.json", root.GetProperty("replay_file").GetString());
        Assert.Equal("vanilla 0.1.0", root.GetProperty("context").GetProperty("mods")[0].GetString());
        Assert.False(root.TryGetProperty("priority", out _));
        Assert.False(root.TryGetProperty("session", out _));
    }
}
