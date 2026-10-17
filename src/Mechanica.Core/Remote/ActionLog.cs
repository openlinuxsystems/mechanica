using System.Text.Json;
using Mechanica.Core.Actions;

namespace Mechanica.Core.Remote;

/// <summary>A replayed call whose outcome differs from the recording.</summary>
/// <param name="Index">Entry index in the recording.</param>
/// <param name="Action">Action name.</param>
/// <param name="Expected">What the recording says the host answered.</param>
/// <param name="Actual">What the host answered on replay.</param>
public sealed record ReplayMismatch(int Index, string Action, ActionResult Expected, ActionResult Actual);

/// <summary>Progress of a replay.</summary>
/// <param name="Running">True until every entry has been dispatched or the replay was stopped.</param>
/// <param name="Dispatched">Entries dispatched so far.</param>
/// <param name="Total">Entries the replay will dispatch.</param>
/// <param name="Mismatches">Entries whose outcome differed from the recording.</param>
public sealed record ReplayStatus(bool Running, int Dispatched, int Total, IReadOnlyList<ReplayMismatch> Mismatches);

/// <summary>
/// Record and replay of Action API calls on the host. While recording, every state-changing call the
/// dispatcher handles (from any player, bot or external agent) is kept with its physics tick. Read-only
/// queries are skipped, and so are calls from part scripts: the scripts run again during a replay and
/// make those calls themselves. A replay dispatches a recording's calls again through the same
/// validated dispatcher, as the caller who asked for the replay, and on the same tick spacing unless
/// it is untimed. Calls that failed when recorded are skipped. The game calls <see cref="Tick"/> once
/// per physics step.
/// </summary>
public sealed class ActionLog : IDisposable
{
    private readonly ActionDispatcher _dispatcher;
    private readonly List<RecordedAction> _recorded = [];
    private long _now;
    private long? _recordStart;
    private bool _truncated;
    private Replay? _replay;

    private sealed class Replay(IReadOnlyList<(int Index, RecordedAction Entry)> entries, ActionCaller caller, bool timed)
    {
        public IReadOnlyList<(int Index, RecordedAction Entry)> Entries { get; } = entries;
        public ActionCaller Caller { get; } = caller;
        public bool Timed { get; } = timed;
        public long? Start { get; set; }
        public int Next { get; set; }
        public bool Stopped { get; set; }
        public List<ReplayMismatch> Mismatches { get; } = [];
    }

    /// <summary>Creates a log that watches <paramref name="dispatcher"/>.</summary>
    /// <param name="dispatcher">The host's dispatcher.</param>
    /// <param name="tickRate">Physics ticks per second, stored in recordings.</param>
    /// <param name="maxEntries">Most calls one recording keeps.</param>
    public ActionLog(ActionDispatcher dispatcher, int tickRate, int maxEntries = 100_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        _dispatcher = dispatcher;
        TickRate = tickRate;
        MaxEntries = maxEntries;
        _dispatcher.Dispatched += OnDispatched;
    }

    /// <summary>Physics ticks per second.</summary>
    public int TickRate { get; }

    /// <summary>Most calls one recording keeps.</summary>
    public int MaxEntries { get; }

    /// <summary>The physics tick last passed to <see cref="Tick"/>.</summary>
    public long CurrentTick => _now;

    /// <summary>True while recording.</summary>
    public bool IsRecording => _recordStart is not null;

    /// <summary>True while a replay is dispatching.</summary>
    public bool IsReplaying => _replay is { Stopped: false } r && r.Next < r.Entries.Count;

    /// <summary>Raised once when a replay has dispatched its last entry.</summary>
    public event Action<ReplayStatus>? ReplayFinished;

    /// <summary>Starts a new recording; false when one is already running.</summary>
    public bool StartRecording()
    {
        if (IsRecording)
            return false;
        _recorded.Clear();
        _truncated = false;
        _recordStart = _now;
        return true;
    }

    /// <summary>The recording so far, without stopping it. Null when not recording.</summary>
    public ActionRecording? Snapshot() =>
        IsRecording ? new ActionRecording { TickRate = TickRate, Truncated = _truncated, Actions = [.. _recorded] } : null;

    /// <summary>Stops recording and returns what was recorded, or null when not recording.</summary>
    public ActionRecording? StopRecording()
    {
        var recording = Snapshot();
        _recordStart = null;
        _recorded.Clear();
        return recording;
    }

    /// <summary>
    /// Starts replaying <paramref name="recording"/> as <paramref name="caller"/>, replacing any replay in
    /// progress. A timed replay keeps the recorded tick spacing, starting on the next <see cref="Tick"/>;
    /// an untimed one dispatches everything on that tick. Returns how many entries it will dispatch.
    /// </summary>
    public int StartReplay(ActionRecording recording, ActionCaller caller, bool timed = true)
    {
        var entries = recording.Actions.Select((entry, index) => (index, entry))
            .Where(e => e.entry.Result.Ok && !_dispatcher.IsReadOnly(e.entry.Action))
            .ToList();
        _replay = new Replay(entries, caller, timed);
        return entries.Count;
    }

    /// <summary>Stops the replay in progress, if any.</summary>
    public void StopReplay()
    {
        if (_replay is not null)
            _replay.Stopped = true;
    }

    /// <summary>Progress of the latest replay, or null when none ever started.</summary>
    public ReplayStatus? ReplayStatus => _replay is { } r ? Status(r) : null;

    /// <summary>Advances to physics tick <paramref name="tick"/> and dispatches replay entries now due.</summary>
    public void Tick(long tick)
    {
        _now = tick;
        if (_replay is not { Stopped: false } replay || replay.Next >= replay.Entries.Count)
            return;
        replay.Start ??= tick;
        var first = replay.Entries[0].Entry.Tick;
        while (replay.Next < replay.Entries.Count)
        {
            var (index, entry) = replay.Entries[replay.Next];
            if (replay.Timed && tick - replay.Start.Value < entry.Tick - first)
                break;
            replay.Next++;
            var actual = _dispatcher.Dispatch(replay.Caller, entry.Action, entry.Args.GetRawText());
            if (!SameOutcome(entry.Result, actual))
                replay.Mismatches.Add(new ReplayMismatch(index, entry.Action, entry.Result, actual));
            if (replay.Stopped)
                return;
        }
        if (replay.Next >= replay.Entries.Count)
            ReplayFinished?.Invoke(Status(replay));
    }

    /// <inheritdoc/>
    public void Dispose() => _dispatcher.Dispatched -= OnDispatched;

    /// <summary>True when a replayed result matches the recorded one: both succeeded with equal values, or both failed with the same code.</summary>
    public static bool SameOutcome(ActionResult expected, ActionResult actual)
    {
        if (expected.Ok != actual.Ok)
            return false;
        if (!expected.Ok)
            return expected.Code == actual.Code;
        return (expected.Value, actual.Value) switch
        {
            (null, null) => true,
            ({ } a, { } b) => JsonElement.DeepEquals(a, b),
            _ => false,
        };
    }

    private void OnDispatched(ActionRecord record)
    {
        if (_recordStart is not { } start || record.Caller.Source == ActionSource.Script
            || _dispatcher.IsReadOnly(record.Action) || !_dispatcher.Has(record.Action))
            return;
        if (_recorded.Count >= MaxEntries)
        {
            _truncated = true;
            return;
        }
        JsonElement args;
        try
        {
            using var document = JsonDocument.Parse(record.ArgsJson, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                MaxDepth = ActionJson.Options.MaxDepth,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return;
            args = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Unparseable arguments never applied anything, so there is nothing to replay.
            return;
        }
        _recorded.Add(new RecordedAction(_now - start, record.Caller.Peer, record.Caller.Source, record.Action, args, record.Result));
    }

    private static ReplayStatus Status(Replay replay) =>
        new(!replay.Stopped && replay.Next < replay.Entries.Count, replay.Next, replay.Entries.Count, [.. replay.Mismatches]);
}
