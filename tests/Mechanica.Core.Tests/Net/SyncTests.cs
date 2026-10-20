using System.Numerics;
using Mechanica.Core.Net;
using Mechanica.Core.Player;

namespace Mechanica.Core.Tests.Net;

public class InputQueueTests
{
    private static SequencedInput In(uint seq, float moveZ = 1f, bool jump = false) =>
        new(seq, new PlayerInput(0, moveZ, 0.5f, 0.1f, jump, false));

    [Fact]
    public void HandsOutOneInputPerTickInOrder()
    {
        var queue = new InputQueue();
        queue.Receive([In(2, 0.2f), In(1, 0.1f)]);
        Assert.Equal(0.1f, Assert.Single(queue.NextTick()).MoveZ);
        Assert.Equal(1u, queue.LastApplied);
        Assert.Equal(0.2f, Assert.Single(queue.NextTick()).MoveZ);
        Assert.Equal(2u, queue.LastApplied);
    }

    [Fact]
    public void IgnoresDuplicatesFromRedundantPackets()
    {
        var queue = new InputQueue();
        queue.Receive([In(1), In(2)]);
        queue.Receive([In(1), In(2), In(3)]);
        Assert.Equal(3, queue.Count);
    }

    [Fact]
    public void DropsOldestWhenAClientSendsTooFast()
    {
        var queue = new InputQueue();
        queue.Receive(Enumerable.Range(1, 20).Select(i => In((uint)i)));
        Assert.Equal(InputQueue.MaxBuffered, queue.Count);
        Assert.Equal(20 - InputQueue.MaxBuffered, queue.Dropped);
        Assert.Equal(2, queue.NextTick().Count);
        Assert.Equal(20u - InputQueue.MaxBuffered + 2, queue.LastApplied);
    }

    [Fact]
    public void NeverRunsMoreThanTwoInputsPerTick()
    {
        var queue = new InputQueue();
        queue.Receive(Enumerable.Range(1, 8).Select(i => In((uint)i)));
        Assert.Equal([2, 2, 2, 1, 1, 0], Enumerable.Range(0, 6).Select(_ => queue.NextTick().Count));
        Assert.Equal(8u, queue.LastApplied);
    }

    [Fact]
    public void IgnoresImplausibleSequenceJumps()
    {
        var queue = new InputQueue();
        queue.Receive([In(uint.MaxValue - 1)]);
        Assert.Equal(0, queue.Count);
        queue.Receive([In(1), In(2 + InputQueue.MaxSequenceJump)]);
        Assert.Equal(1, queue.Count);
        queue.Receive([In(1 + InputQueue.MaxSequenceJump)]);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void WaitsForInputThenStandsStill()
    {
        var queue = new InputQueue();
        queue.Receive([In(1, jump: true)]);
        Assert.True(Assert.Single(queue.NextTick()).Jump);
        for (var i = 0; i < InputQueue.MaxStallTicks; i++)
            Assert.Empty(queue.NextTick());
        var still = Assert.Single(queue.NextTick());
        Assert.Equal(0f, still.MoveZ);
        Assert.False(still.Jump);
        Assert.Equal(0.5f, still.Yaw);
        Assert.Equal(1u, queue.LastApplied);
        Assert.Equal(InputQueue.MaxStallTicks + 1, queue.Stalled);
    }

    [Fact]
    public void JitterDoesNotLoseOrInventInputs()
    {
        // Packets arrive in bursts of two every other tick: every input still runs exactly once.
        var queue = new InputQueue();
        var ran = new List<float>();
        uint seq = 0;
        for (var tick = 0; tick < 100; tick++)
        {
            if (tick % 2 == 1)
                queue.Receive([In(++seq, seq), In(++seq, seq)]);
            ran.AddRange(queue.NextTick().Select(i => i.MoveZ));
        }
        ran.AddRange(queue.NextTick().Select(i => i.MoveZ)); // the host runs one tick behind
        Assert.Equal(1, queue.Stalled); // only tick 0, before the first packet
        Assert.Equal(Enumerable.Range(1, (int)seq).Select(i => (float)i), ran);
    }
}

public class PredictionHistoryTests
{
    private static PredictionHistory.Entry E(uint seq, float z) =>
        new(seq, PlayerInput.None, new Vector3(0, 0, z), Vector3.Zero);

    [Fact]
    public void MatchingAckNeedsNoCorrectionAndDropsAckedEntries()
    {
        var history = new PredictionHistory();
        for (uint i = 1; i <= 5; i++)
            history.Record(E(i, i));
        Assert.False(history.Reconcile(3, new Vector3(0, 0, 3.01f)));
        Assert.Equal([4u, 5u], history.Pending.Select(e => e.Sequence));
    }

    [Fact]
    public void MismatchNeedsCorrection()
    {
        var history = new PredictionHistory(tolerance: 0.05f);
        history.Record(E(1, 1));
        history.Record(E(2, 2));
        Assert.True(history.Reconcile(1, new Vector3(0, 0, 1.5f)));
        history.Overwrite(2, new Vector3(0, 0, 2.5f), Vector3.One);
        Assert.Equal(new Vector3(0, 0, 2.5f), Assert.Single(history.Pending).Position);
    }

    [Fact]
    public void UnknownAckNeedsCorrection()
    {
        var history = new PredictionHistory(capacity: 2);
        history.Record(E(1, 1));
        history.Record(E(2, 2));
        history.Record(E(3, 3));
        Assert.True(history.Reconcile(1, new Vector3(0, 0, 1)));
        Assert.Equal(2, history.Pending.Count);
    }

    private static PredictionHistory.Entry OnDeck(uint seq, float z, int body = 0) =>
        new(seq, PlayerInput.None, new Vector3(0, 0, z * 10), Vector3.Zero, new PlayerGround(7, (ushort)body, new Vector3(0, 0, z)));

    [Fact]
    public void OnTheSameBodyTheFeetAreCheckedInItsFrame()
    {
        var history = new PredictionHistory(tolerance: 0.05f);
        for (uint i = 1; i <= 4; i++)
            history.Record(OnDeck(i, i));
        // The host's world position is far off (it shows the Body elsewhere), but on the Body the feet agree.
        Assert.False(history.ReconcileOnGround(2, new PlayerGround(7, 0, new Vector3(0, 0, 2.01f)), out var acked));
        Assert.Equal(2u, acked.Sequence);
        Assert.Equal([3u, 4u], history.Pending.Select(e => e.Sequence));

        Assert.True(history.ReconcileOnGround(3, new PlayerGround(7, 0, new Vector3(0, 0, 3.5f)), out _));
        history.Shift(new Vector3(0, 0, 5f), new Vector3(0, 0, 0.5f), new PlayerGround(7, 0, Vector3.Zero));
        var left = Assert.Single(history.Pending);
        Assert.Equal(new Vector3(0, 0, 4.5f), left.Ground!.Value.Feet);
        Assert.Equal(new Vector3(0, 0, 45f), left.Position);
    }

    [Fact]
    public void AnotherBodyOrNoneFallsBackToWorldPositions()
    {
        var history = new PredictionHistory();
        history.Record(OnDeck(1, 1, body: 1));
        history.Record(E(2, 2));
        Assert.Null(history.ReconcileOnGround(1, new PlayerGround(7, 0, Vector3.Zero), out _));
        Assert.Null(history.ReconcileOnGround(2, new PlayerGround(7, 1, Vector3.Zero), out _));
        Assert.Null(history.ReconcileOnGround(9, new PlayerGround(7, 1, Vector3.Zero), out _));
        Assert.Equal(2, history.Pending.Count);
    }

    [Fact]
    public void NewestReturnsTheLastEntriesInOrder()
    {
        var history = new PredictionHistory();
        for (uint i = 1; i <= 6; i++)
            history.Record(E(i, i));
        Assert.Equal([4u, 5u, 6u], history.Newest(3).Select(e => e.Sequence));
        Assert.Equal(6, history.Newest(10).Count);
    }

    [Fact]
    public void SequencesMustIncrease()
    {
        var history = new PredictionHistory();
        history.Record(E(2, 0));
        Assert.Throws<ArgumentException>(() => history.Record(E(2, 0)));
        Assert.Throws<ArgumentException>(() => history.Overwrite(9, Vector3.Zero, Vector3.Zero));
    }
}

public class SnapshotBufferTests
{
    [Fact]
    public void InterpolatesFeetOnTheSameBodyInItsFrame()
    {
        var buffer = new SnapshotBuffer();
        buffer.Add(10, S(0) with { Ground = new PlayerGround(3, 1, new Vector3(1, 0, 0)) });
        buffer.Add(12, S(50) with { Ground = new PlayerGround(3, 1, new Vector3(2, 0, 0)) });
        buffer.Add(14, S(90) with { Ground = new PlayerGround(4, 0, new Vector3(9, 0, 0)) });
        Assert.Equal(new PlayerGround(3, 1, new Vector3(1.5f, 0, 0)), buffer.Sample(11)!.Value.Ground);
        Assert.Equal(new PlayerGround(3, 1, new Vector3(2, 0, 0)), buffer.Sample(12.5)!.Value.Ground);
        Assert.Equal(4, buffer.Sample(13.5)!.Value.Ground!.Value.Creation);
    }

    private static PlayerState S(float x, float yaw = 0f) => new(2, 0, new Vector3(x, 0, 0), Vector3.Zero, yaw, 0);

    [Fact]
    public void InterpolatesBetweenTicks()
    {
        var buffer = new SnapshotBuffer();
        buffer.Add(10, S(0));
        buffer.Add(12, S(2));
        Assert.Equal(1f, buffer.Sample(11)!.Value.Position.X, 5);
        Assert.Equal(0.5f, buffer.Sample(10.5)!.Value.Position.X, 5);
    }

    [Fact]
    public void HoldsEndsAndHandlesEmpty()
    {
        var buffer = new SnapshotBuffer();
        Assert.Null(buffer.Sample(1));
        buffer.Add(10, S(0));
        buffer.Add(12, S(2));
        Assert.Equal(0f, buffer.Sample(3)!.Value.Position.X);
        Assert.Equal(2f, buffer.Sample(30)!.Value.Position.X);
    }

    [Fact]
    public void InsertsOutOfOrderAndIgnoresDuplicates()
    {
        var buffer = new SnapshotBuffer();
        buffer.Add(14, S(4));
        buffer.Add(10, S(0));
        buffer.Add(12, S(2));
        buffer.Add(12, S(99));
        Assert.Equal(3, buffer.Count);
        Assert.Equal(3f, buffer.Sample(13)!.Value.Position.X, 5);
    }

    [Fact]
    public void KeepsAtMostCapacity()
    {
        var buffer = new SnapshotBuffer();
        for (uint i = 0; i < 100; i++)
            buffer.Add(i, S(i));
        Assert.Equal(SnapshotBuffer.Capacity, buffer.Count);
    }

    [Fact]
    public void YawTakesTheShortArc()
    {
        var yaw = SnapshotBuffer.LerpAngle(3f, -3f, 0.5f);
        Assert.True(MathF.Abs(MathF.Abs(yaw) - MathF.PI) < 1e-4f, $"yaw={yaw}");
        Assert.Equal(0.5f, SnapshotBuffer.LerpAngle(0f, 1f, 0.5f), 5);
    }
}

public class InterpolationClockTests
{
    [Fact]
    public void StartsAtFirstSnapshotMinusDelay()
    {
        var clock = new InterpolationClock(delayTicks: 5);
        Assert.Null(clock.RenderTick);
        clock.Advance();
        Assert.Null(clock.RenderTick);
        clock.OnSnapshot(100);
        Assert.Equal(95, clock.RenderTick);
    }

    [Fact]
    public void FollowsSteadySnapshotsWithoutDrift()
    {
        var clock = new InterpolationClock(delayTicks: 5);
        uint host = 100;
        clock.OnSnapshot(host);
        for (var i = 0; i < 600; i++)
        {
            host++;
            clock.Advance();
            if (host % 2 == 0)
                clock.OnSnapshot(host);
        }
        Assert.InRange(clock.RenderTick!.Value, host - 6, host - 4);
    }

    [Fact]
    public void BurstsAndGapsNeverChangeTheShownSpeedMuch()
    {
        var clock = new InterpolationClock(delayTicks: 5);
        var random = new Random(7);
        uint host = 100;
        clock.OnSnapshot(host);
        var pending = new List<uint>();
        var shown = clock.RenderTick!.Value;
        for (var i = 0; i < 2000; i++)
        {
            host++;
            if (host % 2 == 0)
                pending.Add(host);
            clock.Advance();
            // Snapshots arrive in bursts: each tick, none to all of the waiting ones.
            var arrive = random.Next(pending.Count + 1);
            foreach (var tick in pending.Take(arrive))
                clock.OnSnapshot(tick);
            pending.RemoveRange(0, arrive);
            var step = clock.RenderTick!.Value - shown;
            shown = clock.RenderTick.Value;
            Assert.InRange(step, 1 - InterpolationClock.MaxSkew - 1e-9, 1 + InterpolationClock.MaxSkew + 1e-9);
        }
        Assert.InRange(shown, host - 8, host - 4);
    }

    [Fact]
    public void LateSnapshotsDoNotHoldItBack()
    {
        var clock = new InterpolationClock(delayTicks: 5);
        clock.OnSnapshot(100);
        for (var i = 0; i < 200; i++)
        {
            clock.Advance();
            clock.OnSnapshot(100 + (uint)i);
        }
        // Every snapshot arrives a tick after it was sent; the clock stays about the delay behind the newest.
        Assert.InRange(clock.RenderTick!.Value, 299 - 6, 299 - 4);
    }

    [Fact]
    public void SnapsAfterLargeJump()
    {
        var clock = new InterpolationClock(delayTicks: 5, resetTicks: 30);
        clock.OnSnapshot(100);
        clock.OnSnapshot(1000);
        Assert.Equal(995, clock.RenderTick);
    }
}

public class PlayerSlotsTests
{
    [Fact]
    public void ClaimsLowestFreeSlotAndRefusesWhenFull()
    {
        var slots = new PlayerSlots(3);
        Assert.Equal(0, slots.Claim(1));
        Assert.Equal(1, slots.Claim(50));
        Assert.Equal(1, slots.Claim(50));
        Assert.Equal(2, slots.Claim(60));
        Assert.Null(slots.Claim(70));
        slots.Release(50);
        Assert.Equal(2, slots.Count);
        Assert.Equal(1, slots.Claim(70));
        Assert.Null(slots.SlotOf(50));
    }

    [Fact]
    public void SpawnOffsetsAreDistinct()
    {
        var offsets = Enumerable.Range(0, 8).Select(PlayerSlots.SpawnOffset).ToList();
        Assert.Equal(Vector3.Zero, offsets[0]);
        Assert.Equal(offsets.Count, offsets.Distinct().Count());
        Assert.All(offsets, o => Assert.Equal(0f, o.Y));
    }
}
