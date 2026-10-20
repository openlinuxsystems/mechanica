using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Perception;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Perception;

public class PerceptionActionsTests
{
    private static readonly ActionCaller Agent = new(1, ActionSource.External);
    private static readonly ActionCaller Guest = new(2, ActionSource.Player);

    private static readonly Lazy<WorldGenerator> Vanilla = new(() =>
        new WorldGenerator(WorldGenData.Load(ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"))), 1234, 4096f));

    private readonly FakeWorld _world = new();
    private readonly ActionDispatcher _dispatcher = new();

    public PerceptionActionsTests()
    {
        new PerceptionActions(_world).Register(_dispatcher);
        _world.PlayerList.Add(Player(1, "Host", new Vec3(0, 0, 0)));
        _world.PlayerList.Add(Player(2, "Guest", new Vec3(10, 0, 0)) with
        {
            Seat = new SeatView(7, 3), Held = CharacterProp.Sledgehammer, Lift = new LiftState(2, new Vec3(12, 0, 0), 4),
        });
    }

    private static PlayerView Player(int peer, string name, Vec3 feet) => new(peer, name, null, feet, feet + new Vec3(0, 1.6f, 0),
        new Vec3(0, 0, -1), Vec3.Zero, true, false, false, null, null, null, null, 100, 100, false);

    private ActionResult Call(ActionCaller caller, string action, string args = "{}") => _dispatcher.Dispatch(caller, action, args);

    private static T Read<T>(ActionResult result)
    {
        Assert.True(result.Ok, result.ToString());
        return result.Value!.Value.Deserialize<T>(DataJson.ReadOptions)!;
    }

    [Fact]
    public void EveryQueryIsReadOnly()
    {
        foreach (var name in new[]
                 {
                     PerceptionActions.GetPlayers, PerceptionActions.GetPlayer, PerceptionActions.Raycast, PerceptionActions.SampleTerrain,
                     PerceptionActions.GetObjects, PerceptionActions.GetDrops, PerceptionActions.GetPois, PerceptionActions.GetWorld,
                 })
            Assert.True(_dispatcher.IsReadOnly(name), name);
    }

    [Fact]
    public void GetPlayersListsEveryPlayerWithSeatHeldToolAndLift()
    {
        var result = Call(Agent, PerceptionActions.GetPlayers);
        var players = Read<List<PlayerView>>(result);

        Assert.Equal([1, 2], players.Select(p => p.Peer));
        var guest = players[1];
        Assert.Equal((new SeatView(7, 3), CharacterProp.Sledgehammer, 4), (guest.Seat, guest.Held, guest.Lift!.Level));
        var json = result.Value!.Value[1];
        Assert.Equal("sledgehammer", json.GetProperty("held").GetString());
        Assert.Equal(0f, json.GetProperty("yaw").GetSingle(), 4);
        Assert.Equal(0f, json.GetProperty("pitch").GetSingle(), 4);
    }

    [Fact]
    public void YawAndPitchFollowTheLook()
    {
        var left = Player(1, "x", Vec3.Zero) with { Look = new Vec3(-1, 0, 0) };
        Assert.Equal(MathF.PI / 2, left.Yaw, 4);
        var down = Player(1, "x", Vec3.Zero) with { Look = new Vec3(0, -1, 0) };
        Assert.Equal(-MathF.PI / 2, down.Pitch, 4);
    }

    [Fact]
    public void GetPlayerDefaultsToTheCaller()
    {
        Assert.Equal("Guest", Read<PlayerView>(Call(Guest, PerceptionActions.GetPlayer)).Name);
        Assert.Equal("Host", Read<PlayerView>(Call(Guest, PerceptionActions.GetPlayer, """{ "peer": 1 }""")).Name);
        Assert.Contains("no player 9", Call(Agent, PerceptionActions.GetPlayer, """{ "peer": 9 }""").Message);
        Assert.Contains("pass peer", Call(new ActionCaller(5, ActionSource.External), PerceptionActions.GetPlayer).Message);
    }

    [Fact]
    public void RaycastFromTheCallersEyesSkipsTheirBody()
    {
        _world.Hit = new RayHit(new Vector3(0, 1.6f, -5), Player: 2);

        var hit = Read<RaycastResult>(Call(Agent, PerceptionActions.Raycast));

        Assert.Equal((true, "player", 2, 5f), (hit.Hit, hit.Kind, hit.Player, hit.Distance!.Value));
        var (from, direction, range, ignore) = Assert.Single(_world.Casts);
        Assert.Equal((new Vector3(0, 1.6f, 0), new Vector3(0, 0, -1), PerceptionActions.DefaultRayRange, 1), (from, direction, range, ignore));
    }

    [Fact]
    public void RaycastNamesThePartAndObjectItHits()
    {
        var creation = new Creation(4, Catalog());
        var part = creation.Place(Block, GridCell.Zero).Id;
        _world.Creations[4] = creation;
        _world.Hit = new RayHit(new Vector3(0, 0, -3), Creation: 4, Part: part);

        var hit = Read<RaycastResult>(Call(Agent, PerceptionActions.Raycast, """{ "from": [0, 0, 0], "direction": [0, 0, -2], "range": 10 }"""));

        Assert.Equal(("part", 4, part, Block), (hit.Kind, hit.Creation!.Value, hit.Part!.Value, hit.PartType));
        Assert.Equal((Vector3.Zero, new Vector3(0, 0, -1), 0), (_world.Casts[0].From, _world.Casts[0].Direction, _world.Casts[0].Ignore));

        _world.Generator = Vanilla.Value;
        var tree = Vanilla.Value.ObjectsIn(-200, -200, 200, 200).First();
        _world.Hit = new RayHit(tree.Position, Object: tree.Id);
        var objectHit = Read<RaycastResult>(Call(Agent, PerceptionActions.Raycast));
        Assert.Equal(("object", tree.Id.ToString(), tree.Definition.Id), (objectHit.Kind, objectHit.Object, objectHit.ObjectType));

        _world.Hit = null;
        Assert.False(Read<RaycastResult>(Call(Agent, PerceptionActions.Raycast)).Hit);
    }

    [Theory]
    [InlineData("""{ "range": 0 }""", "range must be")]
    [InlineData("""{ "range": 5000 }""", "at most 1000")]
    [InlineData("""{ "from": [0, 0, 0], "direction": [0, 0, 0] }""", "not zero")]
    public void RaycastValidates(string args, string reason)
    {
        var result = Call(Agent, PerceptionActions.Raycast, args);
        Assert.Equal(ActionErrors.Rejected, result.Code);
        Assert.Contains(reason, result.Message);
    }

    [Fact]
    public void RaycastWithoutAPlayerNeedsAStartAndDirection()
    {
        var stranger = new ActionCaller(5, ActionSource.External);
        Assert.Contains("pass from and direction", Call(stranger, PerceptionActions.Raycast).Message);
        Assert.True(Call(stranger, PerceptionActions.Raycast, """{ "from": [0, 5, 0], "direction": [0, -1, 0] }""").Ok);
    }

    [Fact]
    public void SampleTerrainReadsPointsAndGrids()
    {
        _world.Generator = new WorldGenerator(WorldGenData.Flat, 1, 512f);

        var points = Read<TerrainSamples>(Call(Agent, PerceptionActions.SampleTerrain, """{ "points": [[0, 0], [3.5, -2]] }"""));
        Assert.Equal([(0f, 0f), (3.5f, -2f)], points.Samples.Select(s => (s.X, s.Z)));
        Assert.All(points.Samples, s => Assert.Equal(("flat", 0f, 0f), (s.Biome, s.Height, s.WaterDepth)));

        var grid = Read<TerrainSamples>(Call(Agent, PerceptionActions.SampleTerrain, """{ "min": [0, 0], "max": [2, 1], "step": 1 }"""));
        Assert.Equal([(0f, 0f), (1f, 0f), (2f, 0f), (0f, 1f), (1f, 1f), (2f, 1f)], grid.Samples.Select(s => (s.X, s.Z)));
    }

    [Fact]
    public void SampleTerrainFindsWaterAndPois()
    {
        var generator = _world.Generator = Vanilla.Value;
        var poi = generator.Pois[0];

        var samples = Read<TerrainSamples>(Call(Agent, PerceptionActions.SampleTerrain,
            $$"""{ "points": [[{{poi.Centre.X}}, {{poi.Centre.Y}}], [0, {{-generator.WorldSize / 2 + 1}}]] }"""));

        Assert.Equal(generator.SeaLevel, samples.SeaLevel);
        Assert.Equal(poi.Poi.QualifiedId, samples.Samples[0].Poi);
        Assert.Equal(generator.HeightAt(poi.Centre.X, poi.Centre.Y), samples.Samples[0].Height);
        // The world's edge is sea.
        Assert.True(samples.Samples[1].WaterDepth > 0f);
    }

    [Theory]
    [InlineData("{}", "either points")]
    [InlineData("""{ "points": [[0, 0]], "min": [0, 0], "max": [1, 1] }""", "either points")]
    [InlineData("""{ "points": [[0]] }""", "points[0] must be [x, z]")]
    [InlineData("""{ "min": [0, 0], "max": [100, 100], "step": 1 }""", "the most is 4096")]
    [InlineData("""{ "min": [5, 0], "max": [0, 0] }""", "not be less")]
    [InlineData("""{ "min": [0, 0], "max": [1, 1], "step": 0 }""", "step must be")]
    public void SampleTerrainValidates(string args, string reason)
    {
        _world.Generator = new WorldGenerator(WorldGenData.Flat, 1, 512f);
        Assert.Contains(reason, Call(Agent, PerceptionActions.SampleTerrain, args).Message);
    }

    [Fact]
    public void WorldQueriesWaitForTheWorld()
    {
        Assert.Contains("isn't made", Call(Agent, PerceptionActions.GetWorld).Message);
        Assert.Contains("isn't made", Call(Agent, PerceptionActions.GetObjects).Message);
        Assert.Contains("isn't made", Call(Agent, PerceptionActions.SampleTerrain, """{ "points": [[0, 0]] }""").Message);
    }

    [Fact]
    public void GetObjectsListsStandingObjectsNearestFirstWithHealth()
    {
        var generator = _world.Generator = Vanilla.Value;
        var state = _world.State = new WorldState(generator.Seed);
        // The commonest breakable kind here, so the typed query below still finds one nearby after it goes.
        var near = generator.ObjectsIn(-300, -300, 300, 300).Where(o => o.Definition.Health is not null).ToList();
        var tree = near.GroupBy(o => o.Definition).OrderByDescending(g => g.Count()).First().First();
        _world.PlayerList[0] = _world.PlayerList[0] with { Position = tree.Position.ToVec3() };
        state.AddDamage(tree.Id, 1f);

        var objects = Read<List<WorldObjectView>>(Call(Agent, PerceptionActions.GetObjects, """{ "radius": 40 }"""));

        Assert.Equal(tree.Id.ToString(), objects[0].Id);
        Assert.Equal(0f, objects[0].Distance, 3);
        Assert.Equal(tree.Definition.Health - 1f, objects[0].Health);
        Assert.True(objects.Zip(objects.Skip(1)).All(p => p.First.Distance <= p.Second.Distance));
        Assert.All(objects, o => Assert.True(o.Distance <= 40f));

        state.Destroy(tree.Id);
        var after = Read<List<WorldObjectView>>(Call(Agent, PerceptionActions.GetObjects, """{ "radius": 40 }"""));
        Assert.DoesNotContain(after, o => o.Id == tree.Id.ToString());

        var typed = Read<List<WorldObjectView>>(Call(Agent, PerceptionActions.GetObjects, $$"""{ "radius": 200, "type": "{{tree.Definition.Id}}", "limit": 3 }"""));
        Assert.InRange(typed.Count, 1, 3);
        Assert.All(typed, o => Assert.Equal(tree.Definition.Id, o.Type));
    }

    [Fact]
    public void GetObjectsValidates()
    {
        _world.Generator = Vanilla.Value;
        Assert.Contains("at most 256", Call(Agent, PerceptionActions.GetObjects, """{ "radius": 300 }""").Message);
        Assert.Contains("limit must be", Call(Agent, PerceptionActions.GetObjects, """{ "limit": 0 }""").Message);
        Assert.Contains("pass center", Call(new ActionCaller(5, ActionSource.External), PerceptionActions.GetObjects).Message);
    }

    [Fact]
    public void GetDropsListsPilesWithExpiry()
    {
        _world.Now = 100;
        _world.DropMap[1] = new ItemDrop(1, new Vec3(20, 0, 0), [new ItemStack("vanilla:scrap_metal", 3)], 130);
        _world.DropMap[2] = new ItemDrop(2, new Vec3(2, 0, 0), [new ItemStack("vanilla:scrap_wood", 1)], 0, Placed: true);

        var all = Read<List<DropView>>(Call(Agent, PerceptionActions.GetDrops));
        Assert.Equal([1, 2], all.Select(d => d.Id));
        Assert.Equal(30, all[0].ExpiresIn);
        Assert.Null(all[1].ExpiresIn);
        Assert.Null(all[0].Distance);

        var near = Read<List<DropView>>(Call(Agent, PerceptionActions.GetDrops, """{ "radius": 5 }"""));
        Assert.Equal(2, Assert.Single(near).Id);
        Assert.Equal(2f, near[0].Distance);

        var around = Read<List<DropView>>(Call(Agent, PerceptionActions.GetDrops, """{ "center": [19, 0, 0] }"""));
        Assert.Equal([1, 2], around.Select(d => d.Id));
    }

    [Fact]
    public void PlayersSeeOnlyRevealedPois()
    {
        var generator = _world.Generator = Vanilla.Value;
        var shown = generator.Pois[0];
        _world.Revealed.Add(shown.Poi.QualifiedId);

        var revealed = Read<List<PoiView>>(Call(Guest, PerceptionActions.GetPois));
        Assert.NotEmpty(revealed);
        Assert.All(revealed, p => Assert.Equal((shown.Poi.QualifiedId, true), (p.Id, p.Revealed)));
        var view = revealed.Single(p => p.Index == shown.Index);
        Assert.Equal(new Vec3(shown.Centre.X, shown.PadHeight, shown.Centre.Y), view.Centre);
        Assert.Equal((shown.Poi.Prefab.Markers ?? []).Count, view.Markers.Count);

        Assert.Contains("revealed", Call(Guest, PerceptionActions.GetPois, """{ "all": true }""").Message);
        var all = Read<List<PoiView>>(Call(Agent, PerceptionActions.GetPois, """{ "all": true }"""));
        Assert.Equal(generator.Pois.Count, all.Count);
        Assert.Contains(all, p => !p.Revealed);
    }

    [Fact]
    public void GetWorldDescribesTheSessionAndClock()
    {
        var generator = _world.Generator = Vanilla.Value;
        (_world.Now, _world.Tick) = (12.5, 750);

        var result = Call(Agent, PerceptionActions.GetWorld);
        var world = Read<WorldView>(result);

        Assert.Equal((1234L, "1234", GameMode.Survival, Difficulty.Normal), (world.Seed, world.SeedText, world.Mode, world.Difficulty));
        Assert.Equal((12.5, 750L, 60, 4096f), (world.Time, world.Tick, world.TickRate, world.WorldSize));
        Assert.Equal([new PeerView(1, "Host"), new PeerView(2, "Guest")], world.Players);
        Assert.Equal(generator.Pois.Count, world.Pois);
        Assert.Equal("survival", result.Value!.Value.GetProperty("mode").GetString());
    }

    private sealed class FakeWorld : IPerceptionWorld
    {
        public double Now { get; set; }

        public long Tick { get; set; }

        public int TickRate => 60;

        public GameMode Mode => GameMode.Survival;

        public Difficulty Difficulty => Difficulty.Normal;
        public bool FriendlyFire => false;

        public WorldGenerator? Generator { get; set; }

        public WorldState? State { get; set; }

        public List<PlayerView> PlayerList { get; } = [];

        public IReadOnlyList<PlayerView> Players => PlayerList;

        public SortedDictionary<int, ItemDrop> DropMap { get; } = [];

        public IReadOnlyDictionary<int, ItemDrop> Drops => DropMap;

        public HashSet<string> Revealed { get; } = [];

        public IReadOnlyCollection<string> RevealedPois => Revealed;

        public Dictionary<int, Creation> Creations { get; } = [];

        public int CreationCount => Creations.Count;

        public RayHit? Hit { get; set; }

        public List<(Vector3 From, Vector3 Direction, float Range, int Ignore)> Casts { get; } = [];

        public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

        public RayHit? Cast(Vector3 from, Vector3 direction, float range, int ignorePlayer)
        {
            Casts.Add((from, direction, range, ignorePlayer));
            return Hit;
        }
    }
}
