using System.Numerics;
using Mechanica.Core.Net;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.Net;

public class BodySnapshotTests
{
    private static BodyState State(float x) => new(
        new Vector3(x, 2, -3),
        Quaternion.Normalize(new Quaternion(0.1f, x, 0.3f, 0.9f)),
        new Vector3(0, -9.8f, x),
        new Vector3(x, 0, 1));

    private static CreationBodies Run(int id, int bodies, ushort revision = 3) =>
        new(id, revision, 0, [.. Enumerable.Range(0, bodies).Select(i => State(i))]);

    [Fact]
    public void RoundTrips()
    {
        var snapshot = new BodySnapshot(77, [Run(1, 2), Run(9, 1, revision: 65535) with { FirstBody = 4 }]);
        var bytes = snapshot.Encode();

        Assert.Equal(snapshot.Size, bytes.Length);
        var decoded = BodySnapshot.Decode(bytes)!;
        Assert.Equal(77u, decoded.Tick);
        Assert.Equal(2, decoded.Creations.Count);
        Assert.Equal((1, (ushort)3, 0), (decoded.Creations[0].CreationId, decoded.Creations[0].Revision, decoded.Creations[0].FirstBody));
        Assert.Equal((9, (ushort)65535, 4), (decoded.Creations[1].CreationId, decoded.Creations[1].Revision, decoded.Creations[1].FirstBody));
        Assert.Equal(snapshot.Creations[0].Bodies, decoded.Creations[0].Bodies);
    }

    [Fact]
    public void RejectsMalformedPackets()
    {
        var good = new BodySnapshot(5, [Run(1, 1)]).Encode();
        Assert.Null(BodySnapshot.Decode(good.AsSpan(0, good.Length - 1)));
        Assert.Null(BodySnapshot.Decode([.. good, 0]));
        Assert.Null(BodySnapshot.Decode([1, 2, 3]));

        var badId = (byte[])good.Clone();
        BitConverter.GetBytes(0).CopyTo(badId, BodySnapshot.HeaderSize);
        Assert.Null(BodySnapshot.Decode(badId));

        var nan = (byte[])good.Clone();
        BitConverter.GetBytes(float.NaN).CopyTo(nan, BodySnapshot.HeaderSize + BodySnapshot.RunHeaderSize);
        Assert.Null(BodySnapshot.Decode(nan));

        var stretched = (byte[])good.Clone();
        BitConverter.GetBytes(3f).CopyTo(stretched, BodySnapshot.HeaderSize + BodySnapshot.RunHeaderSize + 12);
        Assert.Null(BodySnapshot.Decode(stretched));
    }

    [Fact]
    public void PacksWholeCreationsWhenTheyFit()
    {
        var max = BodySnapshot.HeaderSize + 2 * (BodySnapshot.RunHeaderSize + 3 * BodySnapshot.StateSize);
        var packets = BodySnapshot.Pack(10, [Run(1, 3), Run(2, 3), Run(3, 2)], max);

        Assert.Equal(2, packets.Count);
        Assert.Equal([1, 2], packets[0].Creations.Select(c => c.CreationId));
        Assert.Equal(3, Assert.Single(packets[1].Creations).CreationId);
        Assert.All(packets, p => Assert.True(p.Size <= max && p.Tick == 10));
    }

    [Fact]
    public void CutsCreationsBiggerThanAPacketIntoRuns()
    {
        var max = BodySnapshot.HeaderSize + BodySnapshot.RunHeaderSize + 4 * BodySnapshot.StateSize;
        var big = Run(7, 10);
        var packets = BodySnapshot.Pack(1, [big], max);

        Assert.Equal(3, packets.Count);
        Assert.All(packets, p => Assert.True(p.Encode().Length <= max));
        var runs = packets.SelectMany(p => p.Creations).ToList();
        Assert.Equal([0, 4, 8], runs.Select(r => r.FirstBody));
        Assert.Equal(big.Bodies, runs.SelectMany(r => r.Bodies));
    }

    [Fact]
    public void OneBodyPerPacketAtTheSmallestLimit()
    {
        var packets = BodySnapshot.Pack(1, [Run(1, 2), Run(2, 1)], BodySnapshot.MinPacketBytes);
        Assert.Equal(3, packets.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => BodySnapshot.Pack(1, [Run(1, 1)], BodySnapshot.MinPacketBytes - 1));
        Assert.Empty(BodySnapshot.Pack(1, [], 1024));
    }
}

public class BodyTrackTests
{
    private static BodyState At(float x, float yaw = 0f, float vx = 0f) =>
        new(new Vector3(x, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw), new Vector3(vx, 0, 0), Vector3.Zero);

    [Fact]
    public void InterpolatesPositionAndRotation()
    {
        var track = new BodyTrack();
        track.Add(10, At(0, 0f));
        track.Add(14, At(4, 1f));

        var mid = track.Sample(11, 1 / 60f, 0)!.Value;
        Assert.Equal(1f, mid.Position.X, 4);
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.25f);
        Assert.True(MathF.Abs(Quaternion.Dot(expected, mid.Rotation)) > 0.99999f);
    }

    [Fact]
    public void ToleratesGapsAndOutOfOrderStates()
    {
        var track = new BodyTrack();
        track.Add(20, At(20));
        track.Add(10, At(10));
        track.Add(10, At(99));
        Assert.Equal(2, track.Count);
        Assert.Equal(15f, track.Sample(15, 1 / 60f, 0)!.Value.Position.X, 4);
        Assert.Equal(10f, track.Sample(5, 1 / 60f, 0)!.Value.Position.X, 4);
    }

    [Fact]
    public void ExtrapolatesBrieflyThenHolds()
    {
        var track = new BodyTrack();
        track.Add(10, At(0, vx: 6f));
        // 3 ticks of 0.1 s at 6 m/s, capped at 5 ticks.
        Assert.Equal(1.8f, track.Sample(13, 0.1f, 5)!.Value.Position.X, 4);
        Assert.Equal(3f, track.Sample(40, 0.1f, 5)!.Value.Position.X, 4);
    }

    [Fact]
    public void ExtrapolationTurnsAboutTheCentreOfMass()
    {
        var spinning = new BodyState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, new Vector3(0, MathF.PI, 0));
        // Half a turn about a centre of mass 1 m along +x leaves the grid origin 2 m along +x, facing back.
        var turned = BodyTrack.Extrapolate(spinning, new Vector3(1, 0, 0), 1f);
        Assert.True(Vector3.Distance(new Vector3(2, 0, 0), turned.Position) < 1e-4f, $"{turned.Position}");
        Assert.True(Vector3.Distance(-Vector3.UnitX, Vector3.Transform(Vector3.UnitX, turned.Rotation)) < 1e-4f);
    }

    [Fact]
    public void KeepsABoundedHistory()
    {
        var track = new BodyTrack();
        for (uint t = 0; t < 100; t++)
            track.Add(t, At(t));
        Assert.Equal(BodyTrack.Capacity, track.Count);
        Assert.Equal(99u, track.NewestTick);
        Assert.Null(new BodyTrack().Sample(1, 0.1f, 1));
    }
}

public class CreationInterestTests
{
    private static readonly SyncSettings Settings = new() { InterestDistance = 100f, InterestHysteresis = 10f };

    private static InterestCandidate At(int id, float x) => new(id, WorldBounds.Around(new Vector3(x, 0, 0), Vector3.One));

    [Fact]
    public void SendsNearbyCreationsWithHysteresis()
    {
        var interest = new CreationInterest(Settings);
        var change = interest.Update(2, Vector3.Zero, [At(1, 50), At(2, 105), At(3, 500)]);
        Assert.Equal([1], change.Entered);
        Assert.Empty(change.Left);

        // Creation 1 drifts past the edge but inside the hysteresis band; creation 2 is still too far.
        change = interest.Update(2, Vector3.Zero, [At(1, 108), At(2, 108)]);
        Assert.Same(InterestChange.None, change);
        Assert.Equal([1], interest.Of(2));

        change = interest.Update(2, Vector3.Zero, [At(1, 120)]);
        Assert.Equal([1], change.Left);
        Assert.Empty(interest.Of(2));
    }

    [Fact]
    public void DropsCreationsThatStopBeingCandidates()
    {
        // A parked Creation, or one the peer now simulates itself, is no longer offered.
        var interest = new CreationInterest(Settings);
        interest.Update(2, Vector3.Zero, [At(1, 0), At(2, 0)]);
        var change = interest.Update(2, Vector3.Zero, [At(2, 0)]);
        Assert.Equal([1], change.Left);
    }

    [Fact]
    public void PeersWithoutAPlayerReceiveNothing()
    {
        var interest = new CreationInterest(Settings);
        Assert.Same(InterestChange.None, interest.Update(3, null, [At(1, 0)]));
        interest.Update(3, Vector3.Zero, [At(1, 0)]);
        Assert.Equal([1], interest.Update(3, null, [At(1, 0)]).Left);
    }

    [Fact]
    public void ForgetsPeersAndCreations()
    {
        var interest = new CreationInterest(Settings);
        interest.Update(2, Vector3.Zero, [At(1, 0), At(2, 0)]);
        interest.Update(3, Vector3.Zero, [At(1, 0)]);
        interest.ForgetCreation(1);
        Assert.Equal([2], interest.Of(2));
        Assert.Empty(interest.Of(3));
        interest.Forget(2);
        Assert.False(interest.Contains(2, 2));
        // A forgotten Creation that is still offered enters again.
        Assert.Equal([1], interest.Update(3, Vector3.Zero, [At(1, 0)]).Entered);
    }

    [Fact]
    public void UsesHorizontalDistance()
    {
        var interest = new CreationInterest(Settings);
        var high = new InterestCandidate(1, WorldBounds.Around(new Vector3(0, 400, 0), Vector3.One));
        Assert.Equal([1], interest.Update(2, Vector3.Zero, [high]).Entered);
    }
}

public class SyncSettingsTests
{
    [Fact]
    public void ReadsJsonWithCommentsAndDefaults()
    {
        var settings = SyncSettings.FromJson("""
            {
              // tuning
              "interest_distance": 64,
              "max_packet_bytes": 512,
            }
            """);
        Assert.Equal(64f, settings.InterestDistance);
        Assert.Equal(512, settings.MaxPacketBytes);
        Assert.Equal(new SyncSettings().InterestHysteresis, settings.InterestHysteresis);
    }

    [Theory]
    [InlineData("""{ "interest_distance": -1 }""", "interest_distance")]
    [InlineData("""{ "max_packet_bytes": 10 }""", "max_packet_bytes")]
    [InlineData("""{ "max_extrapolation": -0.5 }""", "max_extrapolation")]
    [InlineData("""{ "unknown": 1 }""", "Invalid sync settings")]
    [InlineData("null", "null")]
    public void RejectsBadValues(string json, string message)
    {
        var error = Assert.Throws<FormatException>(() => SyncSettings.FromJson(json));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void ShippedSettingsAreValid()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mechanica.sln")))
            dir = dir.Parent;
        var settings = SyncSettings.FromJson(File.ReadAllText(Path.Combine(dir!.FullName, "game", "data", "sync.json")));
        Assert.True(settings.InterestDistance > 0);
    }
}
