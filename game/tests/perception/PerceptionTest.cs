using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Motion;
using Mechanica.Core.Perception;
using Mechanica.Core.Survival;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// AI perception and catalog queries (AI support PR 3), host plus a client in a Survival world with the vanilla
/// mods. An agent on the host's endpoint reads the world (seed, mode, clock, players), both players' poses, casts
/// rays at the client's body and at the ground, samples the terrain under the host, finds trees and rocks, item
/// piles and POIs, reads every catalog, and builds a block to see its cell, rotation and health in
/// <c>GetCreation</c>. The client's player can ask the same queries through the Action API, but sees only revealed
/// POIs.
/// </summary>
public partial class PerceptionTest : StagedTest
{
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private readonly RpcTestClient _agent = new();
    private readonly Dictionary<string, long> _ids = [];
    private string _block = "";
    private int _creation;
    private ActionResult? _clientPlayers;
    private ActionResult? _clientPois;

    protected override string Tag => "perception";

    protected override int StageTimeoutFrames => 900;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins and the agent connects", frame =>
        {
            if (frame == 0)
                return false;
            if (_host.Main.PlayerSync.Players.Count != 2 || _client.Main.PlayerSync.Players.Count != 2)
                return false;
            if (!_host.Main.Ai.IsListening)
            {
                Check(_host.Main.Ai.Listen(0) is null, "the host listens");
                Check(_agent.Connect(_host.Main.Ai.Port) == Error.Ok, "the agent starts connecting");
            }
            _agent.Poll();
            return _agent.IsOpen;
        });
        Wait("the players land", 60);
        Add("GetWorld describes the session", _ =>
        {
            if (Ask("world", PerceptionActions.GetWorld) is not { } world)
                return false;
            var host = _host.Main.Streamer.Generator;
            Check(world.GetProperty("seed").GetInt64() == _harness.Seed && world.GetProperty("seed_text").GetString() == "1234", $"seed 1234 ({world})");
            Check(world.GetProperty("mode").GetString() == "survival", "Survival");
            Check(world.GetProperty("tick").GetInt64() > 0 && world.GetProperty("tick_rate").GetInt32() == Engine.PhysicsTicksPerSecond, "the tick and its rate");
            Check(world.GetProperty("sea_level").GetSingle() == host.SeaLevel && world.GetProperty("pois").GetInt32() == host.Pois.Count, "sea level and POIs");
            Check(world.GetProperty("players").GetArrayLength() == 2, "two players");
            return true;
        });
        Add("GetPlayers and GetPlayer see both players where the host has them", _ =>
        {
            var (playersAnswer, clientAnswer) = (Ask("players", PerceptionActions.GetPlayers), Answer("client", new GetPlayerArgs(_client.Peer), PerceptionActions.GetPlayer));
            if (playersAnswer is not { } players || clientAnswer is not { } client)
                return false;
            var list = players.Deserialize<List<PlayerView>>(DataJson.ReadOptions)!;
            Check(list.Select(p => p.Peer).SequenceEqual([1, _client.Peer]), $"peers 1 and {_client.Peer}");
            var view = client.Deserialize<PlayerView>(DataJson.ReadOptions)!;
            var body = _host.Main.PlayerSync.Players[_client.Peer];
            Check(view.Name == "Client", $"named Client ({view.Name})");
            Check(new Vector3(view.Position.X, view.Position.Y, view.Position.Z).DistanceTo(body.GlobalPosition) < 0.01f, "at its feet");
            Check(view.Health is > 0 && view.MaxHealth is > 0 && !view.Dead, $"alive with health ({view.Health}/{view.MaxHealth})");
            Check(view.Eye.Y > view.Position.Y, "eyes above the feet");
            return true;
        });
        Add("Raycast hits the client's body from above and the ground beside it", _ =>
        {
            var feet = _host.Main.PlayerSync.Players[_client.Peer].GlobalPosition;
            var down = new Vec3(0, -1, 0);
            var (rayAnswer, groundAnswer) = (Answer("ray", new RaycastArgs(new Vec3(feet.X, feet.Y + 4f, feet.Z), down, 10f), PerceptionActions.Raycast),
                Answer("ground", new RaycastArgs(new Vec3(feet.X + 3f, feet.Y + 4f, feet.Z + 3f), down, 50f), PerceptionActions.Raycast));
            if (rayAnswer is not { } ray || groundAnswer is not { } ground)
                return false;
            Check(ray.GetProperty("kind").GetString() == "player" && ray.GetProperty("player").GetInt32() == _client.Peer, $"the client's body ({ray})");
            Check(ground.GetProperty("hit").GetBoolean() && ground.GetProperty("distance").GetSingle() is > 1f and < 12f, $"the ground ({ground})");
            return true;
        });
        Add("SampleTerrain matches the ground under the host", _ =>
        {
            var feet = _host.Main.PlayerSync.Players[1].GlobalPosition;
            if (Answer("terrain", new SampleTerrainArgs([[feet.X, feet.Z]]), PerceptionActions.SampleTerrain) is not { } terrain)
                return false;
            var sample = terrain.GetProperty("samples")[0];
            var height = sample.GetProperty("height").GetSingle();
            Check(Mathf.Abs(height - _host.Main.Streamer.Generator.HeightAt(feet.X, feet.Z)) < 1e-3f, $"the generator's height ({height})");
            Check(!string.IsNullOrEmpty(sample.GetProperty("biome").GetString()), "a biome");
            return true;
        });
        Add("GetObjects finds standing world objects", _ =>
        {
            if (Answer("objects", new GetObjectsArgs(Radius: 256f, Limit: 5), PerceptionActions.GetObjects) is not { } objects)
                return false;
            Check(objects.GetArrayLength() > 0, $"objects near the host ({objects.GetArrayLength()})");
            var first = objects[0];
            Check(Mechanica.Core.World.WorldObjectId.TryParse(first.GetProperty("id").GetString(), out var id)
                && _host.Main.Streamer.Generator.Find(id) is not null, $"a real object id ({first})");
            return true;
        });
        Add("GetDrops finds a pile on the ground", frame =>
        {
            if (frame == 0)
            {
                var feet = _host.Main.PlayerSync.Players[1].GlobalPosition.ToNumerics();
                Check(_host.Main.Survival.Actions!.AddDrop(feet, [new ItemStack("vanilla:scrap_metal", 2)]) is not null, "a pile dropped at the host's feet");
            }
            if (Answer("drops", new GetDropsArgs(Radius: 5f), PerceptionActions.GetDrops) is not { } drops)
                return false;
            Check(drops.GetArrayLength() == 1 && drops[0].GetProperty("items")[0].GetProperty("item").GetString() == "vanilla:scrap_metal", $"the pile ({drops})");
            Check(drops[0].GetProperty("expires_in").GetDouble() > 0, "it expires later");
            return true;
        });
        Add("GetPois lists every POI for the agent, the client only revealed ones", frame =>
        {
            if (frame == 0)
            {
                _client.Main.Actions.Request(PerceptionActions.GetPois, new GetPoisArgs(true), r => _clientPois = r);
                _client.Main.Actions.Request(PerceptionActions.GetPlayers, new NoArgs(), r => _clientPlayers = r);
            }
            if (Answer("pois", new GetPoisArgs(true), PerceptionActions.GetPois) is not { } pois || _clientPois is null || _clientPlayers is null)
                return false;
            Check(pois.GetArrayLength() == _host.Main.Streamer.Generator.Pois.Count && pois.GetArrayLength() > 0, $"all {pois.GetArrayLength()} POIs");
            Check(_clientPois is { Ok: false, Code: ActionErrors.Rejected }, $"the client's player can't see unrevealed POIs ({_clientPois})");
            Check(_clientPlayers is { Ok: true } && _clientPlayers.Value!.Value.GetArrayLength() == 2, $"the client's player can read the players ({_clientPlayers})");
            return true;
        });
        Add("the catalogs list parts, items, recipes, bots, quests and characters", _ =>
        {
            var answers = new[]
            {
                Ask("parts", CatalogActions.ListParts), Ask("items", CatalogActions.ListItems), Ask("recipes", CatalogActions.ListRecipes),
                Ask("bots", CatalogActions.ListBotTypes), Ask("quests", CatalogActions.GetQuestBook), Ask("characters", CatalogActions.ListCharacters),
            };
            if (answers.Any(a => a is null))
                return false;
            var (parts, items, recipes, bots, quests, characters) = (answers[0]!.Value, answers[1]!.Value, answers[2]!.Value, answers[3]!.Value, answers[4]!.Value, answers[5]!.Value);
            Check(items.GetArrayLength() == _host.Main.Mods!.Parts.Count && parts.GetArrayLength() < items.GetArrayLength(), "every item, and the buildable parts");
            _block = parts.EnumerateArray().First(p => p.GetProperty("category").GetString() == "block").GetProperty("id").GetString()!;
            Check(recipes.GetArrayLength() > 0 && bots.GetArrayLength() > 0 && quests.GetArrayLength() > 0 && characters.GetArrayLength() > 0,
                $"recipes {recipes.GetArrayLength()}, bots {bots.GetArrayLength()}, quests {quests.GetArrayLength()}, characters {characters.GetArrayLength()}");
            return true;
        });
        Add("GetPart gives a definition", _ =>
        {
            if (Answer("part", new GetPartArgs(_block), CatalogActions.GetPart) is not { } part)
                return false;
            Check(part.GetProperty("id").GetString() == _block && part.GetProperty("definition").GetProperty("category").GetString() == "block", $"{_block} ({part.GetProperty("id")})");
            return true;
        });
        Add("GetCreation shows a placed block's cell, rotation and health", frame =>
        {
            if (frame == 0)
            {
                var feet = _host.Main.PlayerSync.Players[1].GlobalPosition;
                _ids["place"] = _agent.Call(BuildActions.PlacePart,
                    new PlacePartArgs(_block, new GridCell(0, 0, 0), GridRotation.All[5], Position: new Vec3(feet.X + 4f, feet.Y + 2f, feet.Z)));
            }
            _agent.Poll();
            if (_creation == 0)
            {
                if (Response("place") is not { } placed)
                    return false;
                Check(RpcTestClient.ErrorCode(placed) is null, $"PlacePart accepted ({placed})");
                _creation = RpcTestClient.Result(placed).GetProperty("creation").GetInt32();
                _ids["describe"] = _agent.Call(MotionActions.GetCreation, new GetCreationArgs(_creation));
                return false;
            }
            if (Response("describe") is not { } describe)
                return false;
            var state = RpcTestClient.Result(describe).Deserialize<CreationState>(ActionJson.Options)!;
            var part = state.PartStates.Single();
            Check(part.Part == _block && part.Cell == GridCell.Zero && part.Rotation == GridRotation.All[5], $"the block as placed ({part})");
            Check(part.MaxHealth is > 0 && part.Health == part.MaxHealth && part.Damage == 0, $"at full health ({part.Health}/{part.MaxHealth})");
            return true;
        });
    }

    // Sends a no-argument query once and returns its result once answered.
    private JsonElement? Ask(string name, string action) => Answer(name, new NoArgs(), action);

    // Sends a query once and returns its result once answered; an error fails the test.
    private JsonElement? Answer(string name, object args, string action)
    {
        if (!_ids.ContainsKey(name))
            _ids[name] = _agent.Call(action, args);
        _agent.Poll();
        if (Response(name) is not { } response)
            return null;
        if (RpcTestClient.ErrorCode(response) is not null)
        {
            Check(false, $"{action} answered ({response})");
            return null;
        }
        return RpcTestClient.Result(response);
    }

    private JsonElement? Response(string name) => _ids.TryGetValue(name, out var id) ? _agent.Response(id) : null;
}
