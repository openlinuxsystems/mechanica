using System.Numerics;
using Mechanica.Core.Characters;
using Mechanica.Core.Net;
using Mechanica.Core.Player;

namespace Mechanica.Core.Tests.Net;

public class InputPacketTests
{
    private static readonly SequencedInput[] Sample =
    [
        new(7, new PlayerInput(0.5f, -1f, 3f, -0.2f, true, false)),
        new(8, new PlayerInput(0f, 1f, -3f, 0.4f, false, true)),
        new(9, new PlayerInput(0f, 0f, 0f, 0f, false, false, Crawl: true, CharacterProp.Sledgehammer, CharacterGesture.SledgeSwing, 200)),
        new(10, new PlayerInput(0f, 0f, 0f, 0f, false, false, Prop: CharacterProp.Sledgehammer, Guard: true)),
    ];

    [Fact]
    public void OnlyTheSledgehammerCanGuard()
    {
        var settings = new PlayerMovementSettings();
        Assert.True(new PlayerInput(0, 0, 0, 0, false, false, Prop: CharacterProp.Sledgehammer, Guard: true).Sanitized(settings).Guard);
        Assert.False(new PlayerInput(0, 0, 0, 0, false, false, Prop: CharacterProp.LiftRemote, Guard: true).Sanitized(settings).Guard);
    }

    [Fact]
    public void RoundTrips() => Assert.Equal(Sample, InputPacket.Decode(InputPacket.Encode(Sample)));

    [Fact]
    public void KeepsNonFiniteValuesForTheHostToSanitize()
    {
        var input = new SequencedInput(1, new PlayerInput(float.NaN, float.PositiveInfinity, 0, 0, false, false));
        var decoded = Assert.Single(InputPacket.Decode(InputPacket.Encode([input]))!);
        Assert.True(float.IsNaN(decoded.Input.MoveX));
        Assert.Equal(0f, decoded.Input.Sanitized(new PlayerMovementSettings()).MoveX);
    }

    [Fact]
    public void RejectsEmptyOrOversizedEncode()
    {
        Assert.Throws<ArgumentException>(() => InputPacket.Encode([]));
        Assert.Throws<ArgumentException>(() => InputPacket.Encode(Enumerable.Repeat(Sample[0], InputPacket.MaxInputs + 1).ToArray()));
    }

    [Fact]
    public void RejectsMalformedPackets()
    {
        var good = InputPacket.Encode(Sample);
        Assert.Null(InputPacket.Decode([]));
        Assert.Null(InputPacket.Decode(good[..^1]));
        Assert.Null(InputPacket.Decode([.. good, 0]));

        var zeroCount = (byte[])good.Clone();
        zeroCount[0] = 0;
        Assert.Null(InputPacket.Decode(zeroCount));

        var hugeCount = (byte[])good.Clone();
        hugeCount[0] = 200;
        Assert.Null(InputPacket.Decode(hugeCount));

        var badFlags = (byte[])good.Clone();
        badFlags[21] = 0x80;
        Assert.Null(InputPacket.Decode(badFlags));
    }
}

public class WorldSnapshotTests
{
    private static readonly WorldSnapshot Sample = new(1234,
    [
        new PlayerState(1, 0, new Vector3(1, 2, 3), new Vector3(-1, 0, 0.5f), 1.5f, -0.25f),
        new PlayerState(987654, 42, new Vector3(-4, 0, 9), Vector3.Zero, -3f, 1f),
        new PlayerState(5, 7, Vector3.One, Vector3.UnitY, 0f, 0f, BodyFlags.Swimming | BodyFlags.Crawl, CharacterProp.WaterBucket, CharacterGesture.Pour, 255),
        new PlayerState(6, 8, Vector3.One, Vector3.Zero, 0f, 0f, BodyFlags.OnFloor | BodyFlags.Guard, CharacterProp.Sledgehammer),
    ]);

    [Fact]
    public void RoundTrips()
    {
        var decoded = WorldSnapshot.Decode(Sample.Encode());
        Assert.NotNull(decoded);
        Assert.Equal(Sample.Tick, decoded.Tick);
        Assert.Equal(Sample.Players, decoded.Players);
    }

    [Fact]
    public void EmptySnapshotRoundTrips() =>
        Assert.Empty(WorldSnapshot.Decode(new WorldSnapshot(5, []).Encode())!.Players);

    [Fact]
    public void RejectsMalformedSnapshots()
    {
        var good = Sample.Encode();
        Assert.Null(WorldSnapshot.Decode(good[..4]));
        Assert.Null(WorldSnapshot.Decode(good[..^1]));
        Assert.Null(WorldSnapshot.Decode([.. good, 1]));
    }

    [Fact]
    public void RejectsNonFiniteStatesAndBadPeers()
    {
        Assert.Null(WorldSnapshot.Decode(new WorldSnapshot(1, [Sample.Players[0] with { Yaw = float.NaN }]).Encode()));
        Assert.Null(WorldSnapshot.Decode(new WorldSnapshot(1, [Sample.Players[0] with { Position = new Vector3(float.PositiveInfinity, 0, 0) }]).Encode()));
        Assert.Null(WorldSnapshot.Decode(new WorldSnapshot(1, [Sample.Players[0] with { Peer = 0 }]).Encode()));
    }

    [Fact]
    public void RejectsUnknownBodyFlags()
    {
        var bytes = Sample.Encode();
        bytes[^4] = 0x80;
        Assert.Null(WorldSnapshot.Decode(bytes));
    }
}
