using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Remote;

namespace Mechanica.Core.Tests.Remote;

public class ActionLogTests
{
    private sealed record MoveArgs(int Distance);

    private static readonly ActionCaller Player = new(2, ActionSource.Player);
    private static readonly ActionCaller Agent = new(1, ActionSource.External);

    private readonly ActionDispatcher _dispatcher = new();
    private readonly List<(long Tick, ActionCaller Caller, int Distance)> _moves = [];
    private readonly ActionLog _log;
    private long _tick;

    public ActionLogTests()
    {
        _dispatcher.Register<MoveArgs>("Move",
            (_, args) => args.Distance > 10 ? "too far" : null,
            (caller, args) =>
            {
                _moves.Add((_tick, caller, args.Distance));
                return JsonSerializer.SerializeToElement(new { total = _moves.Sum(m => m.Distance) });
            });
        _dispatcher.Register<NoArgs>("Look", (_, _) => null, (_, _) => null, readOnly: true);
        _log = new ActionLog(_dispatcher, tickRate: 60);
    }

    private void Advance(long ticks = 1)
    {
        for (var i = 0; i < ticks; i++)
            _log.Tick(++_tick);
    }

    private ActionRecording RecordSample()
    {
        Advance(5);
        Assert.True(_log.StartRecording());
        Assert.False(_log.StartRecording());
        _dispatcher.Dispatch(Player, "Move", """{ "distance": 1, /* comment */ }""");
        Advance(3);
        _dispatcher.Dispatch(Player, "Look", "{}");
        _dispatcher.Dispatch(Player, "Move", """{ "distance": 50 }""");
        _dispatcher.Dispatch(Player, "Jump", "{}");
        _dispatcher.Dispatch(Player, "Move", "[1]");
        Advance(2);
        _dispatcher.Dispatch(Agent, "Move", """{ "distance": 2 }""");
        return _log.StopRecording()!;
    }

    [Fact]
    public void RecordsStateChangesWithTheirTicks()
    {
        var recording = RecordSample();

        Assert.False(_log.IsRecording);
        Assert.Equal(60, recording.TickRate);
        // Queries, unknown actions and arguments that aren't an object are left out.
        Assert.Equal([(0L, "Move", true), (3L, "Move", false), (5L, "Move", true)],
            recording.Actions.Select(a => (a.Tick, a.Action, a.Result.Ok)));
        Assert.Equal(ActionErrors.Rejected, recording.Actions[1].Result.Code);
        Assert.Equal((2, ActionSource.Player), (recording.Actions[0].Peer, recording.Actions[0].Source));
        Assert.Equal(ActionSource.External, recording.Actions[2].Source);
        Assert.Equal(1, recording.Actions[0].Args.GetProperty("distance").GetInt32());
        Assert.Null(_log.StopRecording());
    }

    [Fact]
    public void RecordingRoundTripsThroughJson()
    {
        var recording = RecordSample();
        var json = recording.ToJson();

        Assert.Contains("\n  \"format_version\": 1,", json);
        Assert.Contains("\"source\": \"player\"", json);
        var read = ActionRecording.FromJson(json);
        Assert.Equal(recording.Actions.Count, read.Actions.Count);
        Assert.Equal(json, read.ToJson());
    }

    [Theory]
    [InlineData("""{ "format_version": 2, "tick_rate": 60, "actions": [] }""", "format 2")]
    [InlineData("""{ "tick_rate": 0, "actions": [] }""", "tick_rate")]
    [InlineData("""{ "tick_rate": 60 }""", "actions")]
    [InlineData("""{ "tick_rate": 60, "actions": [], "extra": 1 }""", "extra")]
    [InlineData("""{ "tick_rate": 60, "actions": [ { "tick": 0, "peer": 1, "source": "player", "action": "Move", "args": [], "result": { "ok": true } } ] }""", "args")]
    [InlineData("""{ "tick_rate": 60, "actions": [ { "tick": 5, "peer": 1, "source": "player", "action": "Move", "args": {}, "result": { "ok": true } }, { "tick": 4, "peer": 1, "source": "player", "action": "Move", "args": {}, "result": { "ok": true } } ] }""", "back in time")]
    [InlineData("""{ "tick_rate": 60, "actions": [ { "tick": 0, "peer": 1, "source": "robot", "action": "Move", "args": {}, "result": { "ok": true } } ] }""", "")]
    public void BadRecordingsAreRefused(string json, string reason)
    {
        var error = Assert.Throws<FormatException>(() => ActionRecording.FromJson(json));
        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public void TimedReplayKeepsTheSpacingAndSkipsFailures()
    {
        var recording = RecordSample();
        _moves.Clear();

        Assert.Equal(2, _log.StartReplay(recording, Agent));
        Assert.True(_log.IsReplaying);
        ReplayStatus? finished = null;
        _log.ReplayFinished += s => finished = s;
        Advance();
        var start = _tick;
        Advance(4);
        Assert.Single(_moves);
        Advance();
        Assert.Equal([(start, Agent, 1), (start + 5, Agent, 2)], _moves);
        Assert.False(_log.IsReplaying);
        Assert.NotNull(finished);
        Assert.Equal(new ReplayStatus(false, 2, 2, []), finished with { Mismatches = [] });
        Assert.Empty(finished.Mismatches);
    }

    [Fact]
    public void UntimedReplayRunsAtOnceAndReportsDifferences()
    {
        var recording = RecordSample();
        // The world already moved, so the running totals come out different.
        _dispatcher.Dispatch(Player, "Move", """{ "distance": 4 }""");

        _log.StartReplay(recording, Agent, timed: false);
        Advance();

        var status = _log.ReplayStatus!;
        Assert.False(status.Running);
        Assert.Equal(2, status.Dispatched);
        Assert.Equal([0, 2], status.Mismatches.Select(m => m.Index));
    }

    [Fact]
    public void StoppedReplayDispatchesNoMore()
    {
        var recording = RecordSample();
        _moves.Clear();
        _log.StartReplay(recording, Agent);
        Advance();
        _log.StopReplay();
        Advance(10);

        Assert.Single(_moves);
        Assert.Equal(new ReplayStatus(false, 1, 2, []), _log.ReplayStatus! with { Mismatches = [] });
    }

    [Fact]
    public void RecordingStopsAtItsLimit()
    {
        using var log = new ActionLog(_dispatcher, 60, maxEntries: 2);
        log.StartRecording();
        for (var i = 0; i < 3; i++)
            _dispatcher.Dispatch(Player, "Move", """{ "distance": 1 }""");

        var recording = log.StopRecording()!;
        Assert.Equal(2, recording.Actions.Count);
        Assert.True(recording.Truncated);
    }

    [Theory]
    [InlineData(true, null, true, null, true)]
    [InlineData(true, "1", true, "1", true)]
    [InlineData(true, "1", true, "2", false)]
    [InlineData(true, null, false, null, false)]
    [InlineData(false, null, false, null, true)]
    public void OutcomesCompareByOkValueAndCode(bool okA, string? valueA, bool okB, string? valueB, bool same)
    {
        static ActionResult Make(bool ok, string? value) => ok
            ? ActionResult.Success(value is null ? null : JsonDocument.Parse(value).RootElement.Clone())
            : ActionResult.Failure(ActionErrors.Rejected, "no");
        Assert.Equal(same, ActionLog.SameOutcome(Make(okA, valueA), Make(okB, valueB)));
    }
}
