using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Remote;

namespace Mechanica.Core.Reports;

/// <summary>
/// What happened lately on the host, always kept so a bug report can carry it: the last state-changing
/// Action API calls (the same calls <see cref="ActionLog"/> records, so the slice replays like a
/// recording) and the last log lines. The game calls <see cref="Tick"/> once per physics step and
/// <see cref="Log"/> for each script, bot or host message.
/// </summary>
public sealed class RecentActivity : IDisposable
{
    private static readonly JsonDocumentOptions ArgsOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 64,
    };

    private readonly ActionDispatcher _dispatcher;
    private readonly Queue<RecordedAction> _actions = new();
    private readonly Queue<ReportMessage> _messages = new();
    private long _now;
    private bool _dropped;

    /// <summary>Watches <paramref name="dispatcher"/>.</summary>
    /// <param name="dispatcher">The host's dispatcher.</param>
    /// <param name="tickRate">Physics ticks per second, stored in replay slices.</param>
    /// <param name="maxActions">Most calls kept.</param>
    /// <param name="maxMessages">Most log lines kept.</param>
    public RecentActivity(ActionDispatcher dispatcher, int tickRate, int maxActions = 20_000, int maxMessages = 200)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxActions, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMessages, 1);
        _dispatcher = dispatcher;
        TickRate = tickRate;
        MaxActions = maxActions;
        MaxMessages = maxMessages;
        _dispatcher.Dispatched += OnDispatched;
    }

    /// <summary>Physics ticks per second.</summary>
    public int TickRate { get; }

    /// <summary>Most calls kept.</summary>
    public int MaxActions { get; }

    /// <summary>Most log lines kept.</summary>
    public int MaxMessages { get; }

    /// <summary>The current physics tick.</summary>
    public long Now => _now;

    /// <summary>Calls kept now.</summary>
    public int ActionCount => _actions.Count;

    /// <summary>Advances to physics tick <paramref name="tick"/>.</summary>
    public void Tick(long tick) => _now = tick;

    /// <summary>Keeps a log line, dropping the oldest past <see cref="MaxMessages"/>.</summary>
    public void Log(string source, string level, string text)
    {
        _messages.Enqueue(new ReportMessage(_now, source, level, text.Length <= 2000 ? text : text[..2000] + "…"));
        while (_messages.Count > MaxMessages)
            _messages.Dequeue();
    }

    /// <summary>The last <paramref name="count"/> log lines, oldest first.</summary>
    public IReadOnlyList<ReportMessage> Messages(int count) => [.. _messages.Skip(Math.Max(0, _messages.Count - count))];

    /// <summary>
    /// The last <paramref name="count"/> calls as a recording that starts at tick 0, in the format
    /// <c>--replay</c> and <c>mechanica.replay.start</c> read. It is <see cref="ActionRecording.Truncated"/>
    /// when calls since the world started are missing from it.
    /// </summary>
    public ActionRecording Slice(int count)
    {
        var entries = _actions.Skip(Math.Max(0, _actions.Count - count)).ToList();
        var start = entries.Count > 0 ? entries[0].Tick : 0;
        return new ActionRecording
        {
            TickRate = TickRate,
            Truncated = _dropped || _actions.Count > entries.Count,
            Actions = [.. entries.Select(e => e with { Tick = e.Tick - start })],
        };
    }

    /// <inheritdoc/>
    public void Dispose() => _dispatcher.Dispatched -= OnDispatched;

    private void OnDispatched(ActionRecord record)
    {
        // As ActionLog: part scripts re-run on replay and make their own calls, and queries change nothing.
        if (record.Caller.Source == ActionSource.Script || _dispatcher.IsReadOnly(record.Action) || !_dispatcher.Has(record.Action))
            return;
        JsonElement args;
        try
        {
            using var document = JsonDocument.Parse(record.ArgsJson, ArgsOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return;
            args = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }
        _actions.Enqueue(new RecordedAction(_now, record.Caller.Peer, record.Caller.Source, record.Action, args, record.Result));
        while (_actions.Count > MaxActions)
        {
            _actions.Dequeue();
            _dropped = true;
        }
        if (!record.Result.Ok && record.Result.Code == ActionErrors.Internal)
            Log("actions", "error", $"{record.Action} from peer {record.Caller.Peer} failed: {record.Result.Message}");
    }
}
