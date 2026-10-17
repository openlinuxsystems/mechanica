using System.Text.Json;
using Mechanica.Core.Actions;

namespace Mechanica.Core.Tests.Actions;

public class ActionDispatcherTests
{
    private sealed record MoveArgs(int Distance, string? Label = null);

    private static readonly ActionCaller Client = new(42, ActionSource.Player);

    private readonly ActionDispatcher _dispatcher = new();
    private readonly List<(int Peer, int Distance)> _applied = [];

    public ActionDispatcherTests()
    {
        _dispatcher.Register<MoveArgs>("Move",
            (caller, args) => args.Distance is < 0 or > 10 ? $"distance {args.Distance} is out of range" : null,
            (caller, args) =>
            {
                _applied.Add((caller.Peer, args.Distance));
                return JsonSerializer.SerializeToElement(new { moved = args.Distance });
            });
    }

    [Fact]
    public void ValidCallApplies()
    {
        var result = _dispatcher.Dispatch(Client, "Move", """{ "distance": 3, /* comment */ }""");
        Assert.True(result.Ok, result.ToString());
        Assert.Equal(3, result.Value!.Value.GetProperty("moved").GetInt32());
        Assert.Equal([(42, 3)], _applied);
    }

    [Fact]
    public void TypedDispatchSerializesArgs()
    {
        Assert.True(_dispatcher.Dispatch(Client, "Move", new MoveArgs(4, "x")).Ok);
        Assert.Equal([(42, 4)], _applied);
    }

    [Fact]
    public void RejectedCallDoesNotApply()
    {
        var result = _dispatcher.Dispatch(Client, "Move", """{"distance": 99}""");
        Assert.Equal(ActionErrors.Rejected, result.Code);
        Assert.Contains("out of range", result.Message);
        Assert.Empty(_applied);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("3")]
    [InlineData("""{"distance": "3"}""")]
    [InlineData("""{"distance": 1, "extra": true}""")]
    [InlineData("""{"label": "no distance"}""")]
    [InlineData("""{"distance": 1, "distance": 2}""")]
    public void BadArgsAreRejectedBeforeValidation(string json)
    {
        var result = _dispatcher.Dispatch(Client, "Move", json);
        Assert.Equal(ActionErrors.BadArgs, result.Code);
        Assert.Empty(_applied);
    }

    [Fact]
    public void OversizedArgsAreRejected()
    {
        var json = $$"""{"distance": 1, "label": "{{new string('x', ActionJson.MaxArgsLength)}}"}""";
        Assert.Equal(ActionErrors.BadArgs, _dispatcher.Dispatch(Client, "Move", json).Code);
    }

    [Fact]
    public void DeeplyNestedArgsAreRejected()
    {
        var json = """{"distance": 1, "label": """ + new string('[', 100) + new string(']', 100) + "}";
        Assert.Equal(ActionErrors.BadArgs, _dispatcher.Dispatch(Client, "Move", json).Code);
    }

    [Fact]
    public void UnknownActionIsReported()
    {
        var result = _dispatcher.Dispatch(Client, "Teleport", "{}");
        Assert.Equal(ActionErrors.UnknownAction, result.Code);
        Assert.Equal(ActionErrors.UnknownAction, _dispatcher.Dispatch(Client, null!, "{}").Code);
    }

    [Fact]
    public void ThrowingActionBecomesInternalError()
    {
        _dispatcher.Register<NoArgs>("Boom", (_, _) => null, (_, _) => throw new InvalidOperationException("kaboom"));
        _dispatcher.Register<NoArgs>("BadCheck", (_, _) => throw new NullReferenceException(), (_, _) => null);
        Assert.Equal(ActionErrors.Internal, _dispatcher.Dispatch(Client, "Boom", "{}").Code);
        Assert.Equal(ActionErrors.Internal, _dispatcher.Dispatch(Client, "BadCheck", "{}").Code);
    }

    [Fact]
    public void ValidatorSeesTheCaller()
    {
        _dispatcher.Register<NoArgs>("HostOnly", (caller, _) => caller.Peer == 1 ? null : "host only", (_, _) => null);
        Assert.True(_dispatcher.Dispatch(ActionCaller.HostPlayer, "HostOnly", "{}").Ok);
        Assert.Equal(ActionErrors.Rejected, _dispatcher.Dispatch(Client, "HostOnly", "{}").Code);
    }

    [Fact]
    public void EveryDispatchIsRecorded()
    {
        var records = new List<ActionRecord>();
        _dispatcher.Dispatched += records.Add;
        _dispatcher.Dispatch(Client, "Move", """{"distance": 1}""");
        _dispatcher.Dispatch(Client, "Nope", "{}");
        Assert.Equal(["Move", "Nope"], records.Select(r => r.Action));
        Assert.True(records[0].Result.Ok);
        Assert.False(records[1].Result.Ok);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("Move Part")]
    [InlineData("")]
    [InlineData("Move")]
    public void RegisterRejectsBadOrDuplicateNames(string name) =>
        Assert.Throws<ArgumentException>(() => _dispatcher.Register<NoArgs>(name, (_, _) => null, (_, _) => null));

    [Fact]
    public void NamesAreSorted()
    {
        _dispatcher.Register<NoArgs>("Alpha", (_, _) => null, (_, _) => null);
        Assert.Equal(["Alpha", "Move"], _dispatcher.Names);
        Assert.True(_dispatcher.Has("Alpha"));
    }
}

public class ActionResultTests
{
    [Fact]
    public void RoundTripsThroughJson()
    {
        var failure = ActionResult.Failure(ActionErrors.Rejected, "no");
        Assert.Equal(failure, ActionResult.FromJson(failure.ToJson()));

        var success = ActionResult.Success(JsonSerializer.SerializeToElement(new { a = 1 }));
        var decoded = ActionResult.FromJson(success.ToJson());
        Assert.True(decoded!.Ok);
        Assert.Equal(1, decoded.Value!.Value.GetProperty("a").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"ok": false}""")]
    [InlineData("""{"ok": true, "surprise": 1}""")]
    public void InvalidWireFormIsNull(string json) => Assert.Null(ActionResult.FromJson(json));
}

public class ActionRateLimiterTests
{
    [Fact]
    public void AllowsBurstThenRefills()
    {
        var limiter = new ActionRateLimiter(perSecond: 10, burst: 3);
        Assert.True(limiter.TryAcquire(2, 0));
        Assert.True(limiter.TryAcquire(2, 0));
        Assert.True(limiter.TryAcquire(2, 0));
        Assert.False(limiter.TryAcquire(2, 0));
        Assert.True(limiter.TryAcquire(3, 0));
        Assert.True(limiter.TryAcquire(2, 0.1));
        Assert.False(limiter.TryAcquire(2, 0.1));
    }

    [Fact]
    public void RefillIsCappedAtBurst()
    {
        var limiter = new ActionRateLimiter(perSecond: 10, burst: 2);
        limiter.TryAcquire(2, 0);
        Assert.True(limiter.TryAcquire(2, 100));
        Assert.True(limiter.TryAcquire(2, 100));
        Assert.False(limiter.TryAcquire(2, 100));
        limiter.Forget(2);
        Assert.True(limiter.TryAcquire(2, 100));
    }
}
