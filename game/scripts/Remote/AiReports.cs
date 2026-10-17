using System.Collections.Concurrent;
using Godot;
using Mechanica.Core;
using Mechanica.Core.Remote;
using Mechanica.Core.Reports;
using Mechanica.Game.Net;
using Mechanica.Scripting;
using Mechanica.Scripting.Bots;

namespace Mechanica.Game.Remote;

/// <summary>
/// AI notes, recommendations and bug reports on this game (AUDIT §4.5). Owns the <see cref="ReportStore"/>
/// (<c>user://ai_reports/</c>, one folder per run of the game, across the worlds it loads) and, on the host, the <see cref="Reporter"/> the AI
/// endpoint's <c>reports.*</c> methods write through. It keeps <see cref="RecentActivity"/> going every
/// physics tick and feeds it part script and bot messages and the host's own errors and warnings, so a bug
/// report carries what led up to it. Screenshots are what this window shows.
/// </summary>
public partial class AiReports : Node
{
    private readonly ConcurrentQueue<(string Level, string Text)> _hostLog = new();
    private HostLogger? _logger;
    private ScriptRuntime? _scripts;
    private BotRuntime? _bots;

    /// <summary>The session. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The host's Action API. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>The game's state for each report's context. Set before adding to the tree.</summary>
    public Func<ReportContext> Context { get; init; } = () => new ReportContext();

    /// <summary>Part scripts, whose messages reports keep; optional.</summary>
    public Func<ScriptRuntime?> Scripts { get; init; } = () => null;

    /// <summary>Bot scripts, whose messages reports keep; optional.</summary>
    public Func<BotRuntime?> Bots { get; init; } = () => null;

    /// <summary>The reports folder. Null uses <see cref="UserFolder"/>; tests set their own.</summary>
    public string? Folder { get; init; }

    /// <summary>The reports folder in the game's user data.</summary>
    public static string UserFolder => ProjectSettings.GlobalizePath("user://ai_reports");

    /// <summary>The reports folder and this run's session in it.</summary>
    public ReportStore Store { get; private set; } = null!;

    /// <summary>What happened lately on the host.</summary>
    public RecentActivity Activity { get; private set; } = null!;

    /// <summary>Writes reports.</summary>
    public Reporter Reporter { get; private set; } = null!;

    // One session folder per run of the game, kept when an agent loads another world.
    private static ReportStore? _runStore;

    public override void _Ready()
    {
        var root = Folder ?? UserFolder;
        Store = _runStore is { } run && run.Root == root ? run : _runStore = new ReportStore(root, DateTimeOffset.UtcNow);
        Activity = new RecentActivity(Actions.Dispatcher, Engine.PhysicsTicksPerSecond);
        Reporter = new Reporter(Store, Activity, () => Context() with { Tick = Activity.Now }, Capture);
        _logger = new HostLogger(_hostLog);
        OS.AddLogger(_logger);
    }

    public override void _ExitTree()
    {
        if (_logger is not null)
            OS.RemoveLogger(_logger);
        _logger = null;
        Hook(null, null);
        Activity.Dispose();
    }

    public override void _PhysicsProcess(double delta)
    {
        Activity.Tick((long)Engine.GetPhysicsFrames());
        Hook(Scripts(), Bots());
        while (_hostLog.TryDequeue(out var line))
            Activity.Log("host", line.Level, line.Text);
    }

    /// <summary>
    /// What this game's screen shows now, as PNG. A headless game, or one whose screen has not drawn yet,
    /// has no picture and says why.
    /// </summary>
    public Core.Reports.Screenshot Capture()
    {
        if (DisplayServer.GetName() == "headless")
            return new Core.Reports.Screenshot(null, "The host runs headless, so it has no screen to capture.");
        using var image = GetViewport()?.GetTexture()?.GetImage();
        if (image is null || image.IsEmpty())
            return new Core.Reports.Screenshot(null, "The screen has not been drawn yet.");
        return new Core.Reports.Screenshot(image.SavePngToBuffer(), null);
    }

    // Follows the current script and bot runtimes (a mod reload or a new session replaces them).
    private void Hook(ScriptRuntime? scripts, BotRuntime? bots)
    {
        if (scripts != _scripts)
        {
            if (_scripts is not null)
                _scripts.Message -= OnScript;
            _scripts = scripts;
            if (_scripts is not null)
                _scripts.Message += OnScript;
        }
        if (bots != _bots)
        {
            if (_bots is not null)
                _bots.Message -= OnBot;
            _bots = bots;
            if (_bots is not null)
                _bots.Message += OnBot;
        }
    }

    private void OnScript(ScriptMessage message) => Activity.Log("lua", Level(message.Kind), message.ToString());

    private void OnBot(ScriptMessage message) => Activity.Log("bot", Level(message.Kind), message.ToString());

    private static string Level(ScriptMessageKind kind) => kind switch
    {
        ScriptMessageKind.Error => "error",
        ScriptMessageKind.Warning => "warning",
        _ => "info",
    };

    // The host's own errors and warnings (GD.PushError, engine errors). Godot may call it from any thread,
    // so lines wait in a queue for the next physics tick.
    private sealed partial class HostLogger(ConcurrentQueue<(string Level, string Text)> queue) : Logger
    {
        public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType,
            Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        {
            if (queue.Count < 1000)
                queue.Enqueue((errorType == (int)ErrorType.Warning ? "warning" : "error", rationale.Length > 0 ? rationale : code));
        }
    }
}
