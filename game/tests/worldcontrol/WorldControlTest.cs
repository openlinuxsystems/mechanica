using System.Text.Json;
using Godot;
using Mechanica.Core.Building;
using Mechanica.Core.Data;
using Mechanica.Core.Remote;
using Mechanica.Game.Remote;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Session control for AI agents (AUDIT §5 PR 5), host plus a client. An admin agent saves and lists worlds,
/// runs the world faster and pauses it (the endpoint keeps answering), loads the save (refused while a player
/// is joined, then with <c>disconnect_clients</c>), starts a new world and resets it. Its connection stays
/// open across every world change, the <c>world</c> topic says when each new world is ready, and a player
/// joins the new world.
/// </summary>
public partial class WorldControlTest : StagedTest
{
    private const string Token = "world-test-token";

    private readonly string _saves = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mechanica-world-control-" + Guid.NewGuid().ToString("N"));
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Main _oldMain = null!;
    private readonly RpcTestClient _agent = new();
    private readonly Dictionary<string, long> _ids = [];
    private int _loaded;

    protected override string Tag => "worldcontrol";

    protected override int StageTimeoutFrames => 1200;

    public override void _Ready()
    {
        _mods = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/remote/mods"));
        _harness = new NetTestHarness { Name = "Harness", SaveFolder = _saves };
        AddChild(_harness);
        _host = _harness.StartHost(mods: _mods);
        _client = _harness.StartClient("Client", mods: _mods);

        Add("the client joins and the host opens the endpoint", _ =>
        {
            if (_host.Main.PlayerSync.Players.Count != 2 || _client.Main.PlayerSync.Players.Count != 2)
                return false;
            var access = new EndpointAccess().AddToken(Token, [.. EndpointAccess.All]);
            Check(_host.Main.Ai.Listen(0, "127.0.0.1", access) is null, $"the host listens on port {_host.Main.Ai.Port}");
            Check(_agent.Connect(_host.Main.Ai.Port) == Error.Ok, "the agent starts connecting");
            return true;
        });
        Add("the agent connects as admin", frame =>
        {
            _agent.Poll();
            if (!_agent.IsOpen)
                return false;
            if (!_ids.ContainsKey("auth"))
            {
                _ids["auth"] = _agent.Call("mechanica.auth", new { token = Token });
                _ids["sub"] = _agent.Call("mechanica.subscribe", new { topics = new[] { "world" } });
            }
            return Answer("sub") is not null;
        });
        Add("world.info, world.save and world.list", frame =>
        {
            if (frame == 0)
            {
                _ids["info"] = _agent.Call("world.info");
                _ids["save"] = _agent.Call("world.save", new { name = "AI test" });
                _ids["list"] = _agent.Call("world.list");
            }
            _agent.Poll();
            if (Answer("list") is not { } list)
                return false;
            var info = Result("info");
            Check(info.GetProperty("seed").GetInt64() == _harness.Seed && info.GetProperty("clients").GetInt32() == 1, $"info ({info})");
            Check(System.IO.File.Exists(Result("save").GetProperty("path").GetString()), $"the save is written ({Answer("save")})");
            Check(RpcTestClient.Result(list).EnumerateArray().Any(s => s.GetProperty("name").GetString() == "AI test"), $"the save is listed ({list})");
            Check(_agent.Events.Any(e => Topic(e) == "world" && Data(e).GetProperty("kind").GetString() == "saved"), "the world topic says it saved");
            return true;
        });
        Add("time scale and pause; the endpoint answers while paused", frame =>
        {
            if (frame == 0)
            {
                _ids["fast"] = _agent.Call("world.time_scale", new { scale = 2.0 });
                _ids["tooFast"] = _agent.Call("world.time_scale", new { scale = 100.0 });
                _ids["pause"] = _agent.Call("world.pause", new { paused = true });
                _ids["pausedInfo"] = _agent.Call("world.info");
            }
            _agent.Poll();
            if (Answer("pausedInfo") is null)
                return false;
            Check(Engine.TimeScale == 2 && Result("fast").GetProperty("time_scale").GetDouble() == 2, "the world runs twice as fast");
            Check(RpcTestClient.ErrorCode(Answer("tooFast")!.Value) == JsonRpcEndpoint.Codes.InvalidParams, "a time scale out of range is refused");
            Check(GetTree().Paused && Result("pausedInfo").GetProperty("paused").GetBoolean(), "the world is paused and still answers");
            return true;
        });
        Add("the world resumes at real time", frame =>
        {
            if (frame == 0)
            {
                _ids["resume"] = _agent.Call("world.pause", new { paused = false });
                _ids["real"] = _agent.Call("world.time_scale", new { scale = 1.0 });
            }
            _agent.Poll();
            if (Answer("real") is null)
                return false;
            Check(!GetTree().Paused && Engine.TimeScale == 1, "running again at real time");
            return true;
        });
        Add("loading is refused while a player is joined", frame =>
        {
            if (frame == 0)
                _ids["refused"] = _agent.Call("world.load", new { name = "AI test" });
            _agent.Poll();
            if (Answer("refused") is not { } refused)
                return false;
            Check(RpcTestClient.ErrorCode(refused) == JsonRpcEndpoint.Codes.Rejected, $"refused ({refused})");
            return true;
        });
        Add("the agent loads the save and stays connected", frame =>
        {
            if (frame == 0)
            {
                _oldMain = _host.Main;
                _ids["place"] = _agent.Call(BuildActions.PlacePart, new { part = "drive_test:block", cell = new[] { 0, 0, 0 }, position = new[] { 2, 0.75, 12 } });
                _ids["load"] = _agent.Call("world.load", new { name = "AI test", disconnect_clients = true });
            }
            if (!WaitLoaded())
                return false;
            Check(RpcTestClient.ErrorCode(Answer("place")!.Value) is null, $"a block was placed after the save ({Answer("place")})");
            Check(RpcTestClient.ErrorCode(Answer("load")!.Value) is null, $"the load was accepted ({Answer("load")})");
            _host = _harness.Current(_host);
            Check(_host.Main != _oldMain, "the host runs a new world");
            Check(_agent.IsOpen, "the agent's connection carried over");
            Check(_client.Main.Session.State != Net.NetSession.SessionState.Connected, "the client was disconnected");
            return true;
        });
        Add("the loaded world answers", frame =>
        {
            if (frame == 0)
            {
                _ids["loadedInfo"] = _agent.Call("world.info");
            }
            _agent.Poll();
            if (Answer("loadedInfo") is null)
                return false;
            var info = Result("loadedInfo");
            Check(info.GetProperty("name").GetString() == "AI test" && info.GetProperty("loaded_from").GetString() is { Length: > 0 }, $"info ({info})");
            Check(_host.Main.Session.IsHost && _host.Main.Session.Port == _harness.Port, "the new world hosts on the same port");
            Check(CreationCount() == 0, "the save was taken before the block, so it has none");
            return true;
        });
        Add("a player joins the loaded world", frame =>
        {
            if (frame == 0)
            {
                _harness.Stop(_client);
                _client = _harness.StartClient("Client", mods: _mods);
            }
            return _host.Main.PlayerSync.Players.Count == 2 && _client.Main.PlayerSync.Players.Count == 2;
        });
        Add("the agent starts a new world", frame =>
        {
            if (frame == 0)
                _ids["new"] = _agent.Call("world.new", new { name = "Fresh", seed = 99, mode = "creative", disconnect_clients = true });
            if (!WaitLoaded())
                return false;
            Check(RpcTestClient.ErrorCode(Answer("new")!.Value) is null, $"accepted ({Answer("new")})");
            _host = _harness.Current(_host);
            Check(_host.Main.Streamer.Seed == 99 && _host.Main.Saves.WorldName == "Fresh", $"seed {_host.Main.Streamer.Seed}, '{_host.Main.Saves.WorldName}'");
            return true;
        });
        Add("the agent builds", frame =>
        {
            if (frame == 0)
                _ids["place2"] = _agent.Call(BuildActions.PlacePart, new { part = "drive_test:block", cell = new[] { 0, 0, 0 }, position = new[] { 2, 0.75, 12 } });
            _agent.Poll();
            if (Answer("place2") is not { } placed)
                return false;
            Check(RpcTestClient.ErrorCode(placed) is null && CreationCount() == 1, $"placed ({placed})");
            return true;
        });
        Add("the agent resets the world", frame =>
        {
            if (frame == 0)
                _ids["reset"] = _agent.Call("world.reset", new { disconnect_clients = true });
            if (!WaitLoaded())
                return false;
            Check(RpcTestClient.ErrorCode(Answer("reset")!.Value) is null, $"accepted ({Answer("reset")})");
            _host = _harness.Current(_host);
            Check(_host.Main.Streamer.Seed == 99 && _host.Main.Saves.WorldName == "Fresh", "the same world, afresh");
            Check(CreationCount() == 0, $"nothing is built ({CreationCount()})");
            Check(_agent.IsOpen, "still connected");
            System.IO.Directory.Delete(_saves, recursive: true);
            return true;
        });
    }

    // True once the world topic says another world has loaded.
    private bool WaitLoaded()
    {
        _agent.Poll();
        var loaded = _agent.Events.Count(e => Topic(e) == "world" && Data(e).GetProperty("kind").GetString() == "loaded");
        if (loaded == _loaded)
            return false;
        _loaded = loaded;
        return true;
    }

    private int CreationCount() => _host.Main.Creations.Count;

    private static string? Topic(JsonElement e) => e.GetProperty("params").GetProperty("topic").GetString();

    private static JsonElement Data(JsonElement e) => e.GetProperty("params").GetProperty("data");

    private JsonElement? Answer(string name) => _agent.Response(_ids[name]);

    private JsonElement Result(string name) => RpcTestClient.Result(Answer(name)!.Value);
}
