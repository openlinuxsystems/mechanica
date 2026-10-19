using System.Numerics;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

public class ChunkStreamerTests
{
    private static readonly StreamingSettings Settings = new()
    {
        ChunkSize = 64f,
        WorldSize = 4096f,
        PlayerLoadDistance = 64f,
        PlayerNearDistance = 32f,
        CreationBuffer = 8f,
        UnloadDelay = 1f,
        SleepDelay = 1f,
        MaxUnattendedCreations = 2,
        MaxUnattendedChunks = 20,
    };

    private static readonly Vector3 Home = new(32, 0, 32);
    private static readonly Vector3[] AtHome = [Home];

    [Fact]
    public void PlayersLoadTheChunksAroundThem()
    {
        var streamer = new ChunkStreamer(Settings);
        var update = streamer.Update(0.1f, AtHome, []);
        Assert.Equal(9, update.Load.Count);
        Assert.All(Square(-1, 1, -1, 1), c => Assert.True(streamer.IsLoaded(c)));
        Assert.Empty(update.Unload);
    }

    [Fact]
    public void AWorldCanBeWiderThanItIsDeep()
    {
        var streamer = new ChunkStreamer(Settings with { WorldSize = 1024f, WorldDepth = 512f });
        Assert.True(streamer.InWorld(new ChunkCoord(7, 3)));
        Assert.True(streamer.InWorld(new ChunkCoord(-8, -4)));
        Assert.False(streamer.InWorld(new ChunkCoord(7, 4)));
        Assert.False(streamer.InWorld(new ChunkCoord(0, -5)));
        Assert.False(streamer.InWorld(new ChunkCoord(8, 0)));
    }

    [Fact]
    public void NothingLoadsPastTheWorldEdge()
    {
        var streamer = new ChunkStreamer(Settings);
        streamer.Update(0.1f, [new Vector3(2040, 0, -2040)], []);
        Assert.Equal(Square(30, 31, -32, -31), streamer.Loaded.Order(Comparer<ChunkCoord>.Create(ChunkCoord.Compare)).ToList());
    }

    [Fact]
    public void ChunksUnloadOnlyAfterTheDelay()
    {
        var streamer = new ChunkStreamer(Settings);
        streamer.Update(0.1f, AtHome, []);
        var away = new[] { new Vector3(1000, 0, 32) };
        Assert.Empty(streamer.Update(0.6f, away, []).Unload);
        var update = streamer.Update(0.6f, away, []);
        Assert.Equal(Square(-1, 1, -1, 1), update.Unload);
        Assert.False(streamer.IsLoaded(new ChunkCoord(0, 0)));
    }

    [Fact]
    public void ComingBackBeforeTheDelayKeepsChunksLoaded()
    {
        var streamer = new ChunkStreamer(Settings);
        streamer.Update(0.1f, AtHome, []);
        streamer.Update(0.9f, [new Vector3(1000, 0, 32)], []);
        streamer.Update(0.1f, AtHome, []);
        Assert.Empty(streamer.Update(0.9f, [new Vector3(1000, 0, 32)], []).Unload);
    }

    [Fact]
    public void LongVehicleCrossingABorderKeepsEveryChunkItTouchesLoaded()
    {
        var streamer = new ChunkStreamer(Settings);
        // 30 m long, driving +x at 20 m/s across the borders at x = 1024 and 1088, nobody near.
        for (var t = 0f; t < 6f; t += 0.1f)
        {
            var x = 990f + 20f * t;
            var vehicle = new WorldBounds(new Vector3(x, 0, 30), new Vector3(x + 30, 2, 33));
            var update = streamer.Update(0.1f, AtHome, [new CreationSample(7, vehicle, 20f, 0f)]);
            Assert.Empty(update.Park);
            Assert.All(streamer.ChunksOf(vehicle.Expand(Settings.CreationBuffer)), c => Assert.True(streamer.IsLoaded(c), $"{c} under the vehicle at x={x}"));
        }
        Assert.False(streamer.IsParked(7));
        Assert.False(streamer.IsLoaded(new ChunkCoord(15, 0)), "the chunk the vehicle left unloads");
    }

    [Fact]
    public void PlaneOverACornerLoadsAllFourChunks()
    {
        var streamer = new ChunkStreamer(Settings);
        var plane = WorldBounds.Around(new Vector3(1024, 150, -1024), new Vector3(4, 1, 4));
        streamer.Update(0.1f, AtHome, [new CreationSample(3, plane, 40f, 0.5f)]);
        Assert.All(Square(15, 16, -17, -16), c => Assert.True(streamer.IsLoaded(c)));
    }

    [Fact]
    public void IdleUnattendedCreationSleepsParksBeforeItsGroundGoesAndResumesWhenAPlayerComes()
    {
        var streamer = new ChunkStreamer(Settings);
        var parked = WorldBounds.Around(new Vector3(1000, 1, 32), new Vector3(2, 1, 2));
        var still = new CreationSample(5, parked, 0f, 0f);
        Assert.Empty(streamer.Update(0.5f, AtHome, [still]).FellAsleep);
        var update = streamer.Update(0.5f, AtHome, [still]);
        Assert.Equal([5], update.FellAsleep);
        Assert.Empty(update.Park);

        // Asleep, it stopped force-loading; it parks in the update its chunks unload.
        update = streamer.Update(0.5f, AtHome, [still]);
        Assert.Equal([5], update.Park);
        Assert.Contains(new ChunkCoord(15, 0), update.Unload);
        Assert.True(streamer.IsParked(5));

        // Moving samples are ignored while parked; a player walking up brings ground and Creation back.
        Assert.Empty(streamer.Update(0.5f, AtHome, [still with { LinearSpeed = 9f }]).Resume);
        update = streamer.Update(0.1f, [new Vector3(990, 0, 32)], [still]);
        Assert.Contains(new ChunkCoord(15, 0), update.Load);
        Assert.Equal([5], update.Resume);
        Assert.False(streamer.IsParked(5));
    }

    [Fact]
    public void AttendedCreationNeverParks()
    {
        var streamer = new ChunkStreamer(Settings);
        var box = WorldBounds.Around(Home + new Vector3(20, 0, 0), Vector3.One);
        for (var i = 0; i < 20; i++)
            Assert.Empty(streamer.Update(0.5f, AtHome, [new CreationSample(1, box, 0f, 0f)]).Park);
        Assert.True(streamer.IsAsleep(1));
        Assert.False(streamer.IsParked(1));
    }

    [Fact]
    public void AsleepCreationOnGroundPlayersKeepLoadedStaysLive()
    {
        var streamer = new ChunkStreamer(Settings);
        // Beyond near distance but inside a player-loaded chunk.
        var box = WorldBounds.Around(new Vector3(80, 0, 32), Vector3.One);
        for (var i = 0; i < 20; i++)
            Assert.Empty(streamer.Update(0.5f, AtHome, [new CreationSample(1, box, 0f, 0f)]).Park);
    }

    [Fact]
    public void HoldAwakeKeepsAStillCreationRunning()
    {
        var streamer = new ChunkStreamer(Settings);
        var box = WorldBounds.Around(new Vector3(1000, 0, 32), Vector3.One);
        for (var i = 0; i < 20; i++)
        {
            var update = streamer.Update(0.5f, AtHome, [new CreationSample(1, box, 0f, 0f, HoldAwake: true)]);
            Assert.Empty(update.Park);
            Assert.Empty(update.FellAsleep);
        }
        Assert.True(streamer.IsLoaded(new ChunkCoord(15, 0)));
    }

    [Fact]
    public void OverTheCreationBudgetTheFarthestParksUntilRoomFrees()
    {
        var streamer = new ChunkStreamer(Settings);
        CreationSample At(int id, float x, float speed) => new(id, WorldBounds.Around(new Vector3(x, 0, 32), Vector3.One), speed, 0f);

        var update = streamer.Update(0.1f, AtHome, [At(1, 500, 5), At(2, 700, 5), At(3, 900, 5)]);
        Assert.Equal([3], update.Park);
        Assert.False(streamer.IsLoaded(new ChunkCoord(14, 0)));

        // Creation 1 stops: moving ones go first, so 3 takes its slot and 1 parks where it stands, then dozes off.
        update = streamer.Update(0.6f, AtHome, [At(1, 500, 0), At(2, 700, 5), At(3, 900, 5)]);
        Assert.Equal([1], update.Park);
        Assert.Equal([3], update.Resume);
        Assert.True(streamer.IsLoaded(new ChunkCoord(14, 0)));
        streamer.Update(0.6f, AtHome, [At(1, 500, 0), At(2, 700, 5), At(3, 900, 5)]);
        Assert.True(streamer.IsAsleep(1));
        Assert.True(streamer.IsParked(1));
    }

    [Fact]
    public void ACreationHeldAwakeKeepsItsGroundWhileItWaitsStill()
    {
        // A cart waiting at a far station on a timer: still, but its logic is busy, so it never sleeps or parks.
        var streamer = new ChunkStreamer(Settings);
        var cart = new CreationSample(1, WorldBounds.Around(new Vector3(1000, 0, 32), Vector3.One), 0f, 0f, HoldAwake: true);
        for (var i = 0; i < 40; i++)
        {
            var update = streamer.Update(0.1f, AtHome, [cart]);
            Assert.Empty(update.Park);
            Assert.Empty(update.FellAsleep);
        }
        Assert.True(streamer.IsLoaded(new ChunkCoord(15, 0)));
        Assert.Equal(cart.Bounds, streamer.BoundsOf(1));
        Assert.True(streamer.IsTracked(1));
        Assert.False(streamer.IsTracked(2));
    }

    [Fact]
    public void OverTheBudgetAMovingCreationRunsBeforeNearerOnesOnlyHeldAwake()
    {
        var streamer = new ChunkStreamer(Settings);
        CreationSample Held(int id, float x) => new(id, WorldBounds.Around(new Vector3(x, 0, 32), Vector3.One), 0f, 0f, HoldAwake: true);
        var moving = new CreationSample(3, WorldBounds.Around(new Vector3(900, 0, 32), Vector3.One), 5f, 0f);
        var update = streamer.Update(0.1f, AtHome, [Held(1, 500), Held(2, 700), moving]);
        Assert.Equal([2], update.Park);
        Assert.False(streamer.IsParked(3));
    }

    [Fact]
    public void OverTheChunkBudgetABigCreationParksWhileASmallOneRuns()
    {
        var streamer = new ChunkStreamer(Settings with { MaxUnattendedChunks = 4 });
        var big = new CreationSample(1, new WorldBounds(new Vector3(500, 0, 0), new Vector3(700, 5, 10)), 5f, 0f);
        var small = new CreationSample(2, WorldBounds.Around(new Vector3(900, 0, 32), Vector3.One), 5f, 0f);
        var update = streamer.Update(0.1f, AtHome, [big, small]);
        Assert.Equal([1], update.Park);
        Assert.False(streamer.IsParked(2));
    }

    [Fact]
    public void CreationsRegisteredParkedResumeWhenAttendedAndForgottenOnesAreDropped()
    {
        var streamer = new ChunkStreamer(Settings);
        var box = WorldBounds.Around(new Vector3(1000, 0, 32), Vector3.One);
        streamer.RegisterParked(9, box);
        Assert.Empty(streamer.Update(0.1f, AtHome, []).Resume);
        Assert.Equal([9], streamer.Update(0.1f, [new Vector3(1000, 0, 40)], []).Resume);

        streamer.Forget(9);
        Assert.False(streamer.IsParked(9) || streamer.IsAsleep(9));
    }

    private static List<ChunkCoord> Square(int x0, int x1, int z0, int z1) =>
        [.. from x in Enumerable.Range(x0, x1 - x0 + 1) from z in Enumerable.Range(z0, z1 - z0 + 1) select new ChunkCoord(x, z)];
}
