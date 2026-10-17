using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Remote;
using Mechanica.Game.Net;

namespace Mechanica.Game.Remote;

/// <summary>
/// Record/replay of the host's Action API calls (§1). Owns the host's <see cref="ActionLog"/> and steps
/// it every physics tick. <c>--record file</c> records the whole session, rewrites the file every few
/// seconds while calls come in (so a killed server keeps most of it) and once more when the game quits;
/// <c>--replay file</c> replays a recording once the session is up. External agents control the
/// same log over the AI endpoint (<c>mechanica.record.*</c>, <c>mechanica.replay.*</c>).
/// </summary>
public partial class RecordReplay : Node
{
    /// <summary>Seconds between rewrites of the recording file while recording.</summary>
    public const double FlushSeconds = 5;

    private bool _saved;
    private int _flushedCount;
    private ulong _nextFlush;

    /// <summary>The session, to record and replay only on the host. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The host's Action API. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>The host's log.</summary>
    public ActionLog Log { get; private set; } = null!;

    /// <summary>Where <see cref="SaveRecording"/> writes when the game quits; null records nothing on start.</summary>
    public string? RecordPath { get; private set; }

    public override void _Ready()
    {
        Log = new ActionLog(Actions.Dispatcher, Engine.PhysicsTicksPerSecond);
        Log.ReplayFinished += status =>
            GD.Print($"[replay] finished: {status.Dispatched} actions, {status.Mismatches.Count} differed from the recording");
    }

    public override void _ExitTree()
    {
        SaveRecording();
        Log.Dispose();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            SaveRecording();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Session.IsHost)
            return;
        var tick = Engine.GetPhysicsFrames();
        Log.Tick((long)tick);
        if (RecordPath is null || _saved || tick < _nextFlush)
            return;
        _nextFlush = tick + (ulong)(FlushSeconds * Engine.PhysicsTicksPerSecond);
        if (Log.Snapshot() is { } snapshot && snapshot.Actions.Count != _flushedCount)
        {
            _flushedCount = snapshot.Actions.Count;
            Write(snapshot);
        }
    }

    /// <summary>Starts recording the session to <paramref name="path"/>, rewritten as it grows and when the game quits.</summary>
    public void RecordTo(string path)
    {
        RecordPath = path;
        _saved = false;
        _flushedCount = 0;
        if (!Log.StartRecording())
            GD.PushWarning("[record] a recording was already running; it continues.");
        GD.Print($"[record] recording Action API calls to {OsPath(path)}");
    }

    /// <summary>Writes the recording to <see cref="RecordPath"/> (once). Returns false when there is nothing to write or it failed.</summary>
    public bool SaveRecording()
    {
        if (_saved || RecordPath is null || Log.StopRecording() is not { } recording)
            return false;
        _saved = true;
        if (!Write(recording))
            return false;
        GD.Print($"[record] wrote {recording.Actions.Count} actions to {OsPath(RecordPath)}");
        return true;
    }

    private bool Write(ActionRecording recording)
    {
        var path = OsPath(RecordPath!);
        try
        {
            // Write beside it, then swap, so a crash mid-write never leaves half a file.
            File.WriteAllText(path + ".tmp", recording.ToJson());
            File.Move(path + ".tmp", path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"[record] cannot write {path}: {e.Message}");
            return false;
        }
    }

    /// <summary>Starts replaying the recording in <paramref name="path"/> as an external caller. Returns an error message or null.</summary>
    public string? ReplayFrom(string path, bool timed = true)
    {
        ActionRecording recording;
        try
        {
            recording = ActionRecording.FromJson(File.ReadAllText(OsPath(path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return $"Cannot replay {OsPath(path)}: {e.Message}";
        }
        var total = Log.StartReplay(recording, new ActionCaller(Session.LocalPeer, ActionSource.External), timed);
        GD.Print($"[replay] replaying {total} actions from {OsPath(path)}");
        return null;
    }

    private static string OsPath(string path) =>
        path.StartsWith("user://", StringComparison.Ordinal) || path.StartsWith("res://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : path;
}
