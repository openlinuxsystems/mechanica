using Mechanica.Core.Saves;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Tests.Saves;

public sealed class WorldControlTests : IDisposable
{
    private sealed class FakeHost(SaveStore store) : IWorldHost
    {
        public WorldStatus Status { get; set; } = new("Home", 42, GameMode.Creative, Difficulty.Easy, false, null, 12.5, 750, 1, false, 0, false);

        public SaveStore Store => store;

        public List<string> Calls { get; } = [];

        public bool FailSave { get; set; }

        public string? Save(string name)
        {
            Calls.Add($"save {name}");
            return FailSave ? null : store.Write(new SaveGame { Name = name, Seed = Status.Seed, Mode = Status.Mode });
        }

        public void Load(SaveGame save, string path) => Calls.Add($"load {save.Name} {System.IO.Path.GetFileName(path)}");

        public void Start(WorldSpec spec) => Calls.Add($"start {spec}");

        public void SetTimeScale(double scale)
        {
            Calls.Add($"scale {scale}");
            Status = Status with { TimeScale = scale };
        }

        public void SetPaused(bool paused) => Status = Status with { Paused = paused };

        public void SetFriendlyFire(bool on) => Status = Status with { FriendlyFire = on };
    }

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mechanica-worlds-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHost _host;
    private readonly WorldControl _control;

    public WorldControlTests()
    {
        _host = new FakeHost(new SaveStore(_folder));
        _control = new WorldControl(_host, () => 777);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private WorldControlException Refused(Action call) => Assert.Throws<WorldControlException>(call);

    [Fact]
    public void SavesUnderItsOwnNameOrAGivenOne()
    {
        var own = _control.Save(new SaveWorldParams());
        var named = _control.Save(new SaveWorldParams("  Test run 2 "));

        Assert.Equal(("Home", "home.json"), (own.Name, own.File));
        Assert.Equal(("Test run 2", "test-run-2.json"), (named.Name, named.File));
        Assert.True(File.Exists(named.Path));
        Assert.Equal(["Test run 2", "Home"], _control.List().Select(w => w.Name).Order().Reverse());
    }

    [Fact]
    public void BadNamesFailedWritesAndChangesAreRefused()
    {
        Assert.Equal(WorldRefusal.BadParams, Refused(() => _control.Save(new SaveWorldParams(""))).Refusal);
        Assert.Equal(WorldRefusal.BadParams, Refused(() => _control.Save(new SaveWorldParams(new string('x', 65)))).Refusal);
        Assert.Equal(WorldRefusal.BadParams, Refused(() => _control.New(new NewWorldParams("a\nb"))).Refusal);
        _host.FailSave = true;
        Assert.Equal(WorldRefusal.Rejected, Refused(() => _control.Save(new SaveWorldParams())).Refusal);
        _host.Status = _host.Status with { Changing = true };
        Assert.Equal(WorldRefusal.Busy, Refused(() => _control.Save(new SaveWorldParams())).Refusal);
        Assert.Equal(WorldRefusal.Busy, Refused(() => _control.New(new NewWorldParams())).Refusal);
    }

    [Fact]
    public void LoadsBySavedNameOrFileName()
    {
        _control.Save(new SaveWorldParams("Bridge test"));

        Assert.Equal("Bridge test", _control.Load(new LoadWorldParams("Bridge test")).Name);
        Assert.Equal("bridge-test.json", _control.Load(new LoadWorldParams("bridge-test")).File);
        _control.Load(new LoadWorldParams("bridge-test.json"));
        Assert.Equal(["save Bridge test", "load Bridge test bridge-test.json", "load Bridge test bridge-test.json", "load Bridge test bridge-test.json"], _host.Calls);
        Assert.Equal(WorldRefusal.Rejected, Refused(() => _control.Load(new LoadWorldParams("Nowhere"))).Refusal);
    }

    [Fact]
    public void ABrokenSaveIsListedButNotLoaded()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "broken.json"), "{ nope");

        var listed = Assert.Single(_control.List());
        Assert.NotNull(listed.Error);
        var refusal = Refused(() => _control.Load(new LoadWorldParams("broken")));
        Assert.Equal(WorldRefusal.Rejected, refusal.Refusal);
        Assert.Empty(_host.Calls);
    }

    [Fact]
    public void JoinedPlayersMustBeLetGoFirst()
    {
        _control.Save(new SaveWorldParams("Shared"));
        _host.Status = _host.Status with { Clients = 2 };

        var refusal = Refused(() => _control.Load(new LoadWorldParams("Shared")));
        Assert.Contains("disconnect_clients", refusal.Message);
        Refused(() => _control.New(new NewWorldParams()));
        Refused(() => _control.Reset(new ResetWorldParams()));

        _control.Load(new LoadWorldParams("Shared", DisconnectClients: true));
        Assert.Equal("load Shared shared.json", _host.Calls[^1]);
    }

    [Fact]
    public void NewWorldsPickARandomSeedUnlessGivenOne()
    {
        var random = _control.New(new NewWorldParams("Fresh", Mode: GameMode.Creative));
        var fixedSeed = _control.New(new NewWorldParams("Fixed", 1234, GameMode.Survival, Difficulty.Easy, "vanilla:scrapper_male"));

        Assert.Equal(new WorldSpec("Fresh", 777, GameMode.Creative, Difficulty.Normal, null), random);
        Assert.Equal(new WorldSpec("Fixed", 1234, GameMode.Survival, Difficulty.Easy, "vanilla:scrapper_male"), fixedSeed);
        Assert.Equal(WorldRefusal.BadParams, Refused(() => _control.New(new NewWorldParams(Character: "scrapper"))).Refusal);
        Assert.True(_control.New(new NewWorldParams("Brawl", FriendlyFire: true)).FriendlyFire);
    }

    [Fact]
    public void ResetStartsTheSameWorldAfresh()
    {
        Assert.Equal(new WorldSpec("Home", 42, GameMode.Creative, Difficulty.Easy, null), _control.Reset(new ResetWorldParams()));
        Assert.StartsWith("start ", _host.Calls.Single());
    }

    [Fact]
    public void FriendlyFireTogglesAndResetKeepsIt()
    {
        Assert.True(_control.SetFriendlyFire(new FriendlyFireParams(true)).FriendlyFire);
        Assert.True(_control.Reset(new ResetWorldParams()).FriendlyFire);
        Assert.False(_control.SetFriendlyFire(new FriendlyFireParams(false)).FriendlyFire);
        _host.Status = _host.Status with { Changing = true };
        Assert.Equal(WorldRefusal.Busy, Refused(() => _control.SetFriendlyFire(new FriendlyFireParams(true))).Refusal);
    }

    [Fact]
    public void TimeScaleIsBoundedAndPauseToggles()
    {
        Assert.Equal(4, _control.SetTimeScale(new TimeScaleParams(4)).TimeScale);
        Assert.Equal(WorldRefusal.BadParams, Refused(() => _control.SetTimeScale(new TimeScaleParams(0))).Refusal);
        Assert.Equal(WorldRefusal.BadParams, Refused(() => _control.SetTimeScale(new TimeScaleParams(11))).Refusal);
        Assert.Equal(WorldRefusal.BadParams, Refused(() => _control.SetTimeScale(new TimeScaleParams(double.NaN))).Refusal);
        Assert.True(_control.SetPaused(new PauseParams(true)).Paused);
        Assert.False(_control.SetPaused(new PauseParams(false)).Paused);
        Assert.Equal(_host.Status, _control.Info());
    }
}

/// <summary>The <c>world.*</c> methods over the endpoint, and connections carried over to a rebuilt endpoint.</summary>
public sealed class WorldModuleTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mechanica-world-rpc-" + Guid.NewGuid().ToString("N"));
    private readonly Mechanica.Core.Actions.ActionDispatcher _dispatcher = new();
    private readonly Mechanica.Core.Remote.ActionLog _log;
    private readonly Mechanica.Core.Remote.RemoteEvents _events = new();
    private readonly Mechanica.Core.Remote.EndpointAccess _access =
        new Mechanica.Core.Remote.EndpointAccess().AddToken("watch", Mechanica.Core.Remote.AgentScope.Observe).AddToken("root", Mechanica.Core.Remote.EndpointAccess.All.ToArray());
    private readonly List<string> _calls = [];
    private WorldStatus _status = new("Home", 42, GameMode.Creative, Difficulty.Normal, false, null, 0, 0, 1, false, 1, false);

    private sealed class Host(WorldModuleTests test) : IWorldHost
    {
        public WorldStatus Status => test._status;

        public SaveStore Store { get; } = new(test._folder);

        public string? Save(string name) => Store.Write(new SaveGame { Name = name });

        public void Load(SaveGame save, string path) => test._calls.Add("load " + save.Name);

        public void Start(WorldSpec spec) => test._calls.Add("start " + spec.Name);

        public void SetTimeScale(double scale) => test._status = test._status with { TimeScale = scale };

        public void SetPaused(bool paused) => test._status = test._status with { Paused = paused };

        public void SetFriendlyFire(bool on) => test._status = test._status with { FriendlyFire = on };
    }

    public WorldModuleTests() => _log = new Mechanica.Core.Remote.ActionLog(_dispatcher, 60);

    public void Dispose()
    {
        _log.Dispose();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private Mechanica.Core.Remote.JsonRpcEndpoint Endpoint()
    {
        var endpoint = new Mechanica.Core.Remote.JsonRpcEndpoint(_dispatcher, _log, 1, _access, _events);
        endpoint.AddModule(new WorldModule(new WorldControl(new Host(this), () => 5)));
        return endpoint;
    }

    private static System.Text.Json.JsonElement Send(Mechanica.Core.Remote.JsonRpcEndpoint endpoint, Mechanica.Core.Remote.RemoteConnection connection, string method, string parameters = "{}")
    {
        var text = endpoint.Handle(connection, $$"""{ "jsonrpc": "2.0", "id": 1, "method": "{{method}}", "params": {{parameters}} }""", 0);
        return System.Text.Json.JsonDocument.Parse(text!).RootElement.Clone();
    }

    private static int? Code(System.Text.Json.JsonElement response) =>
        response.TryGetProperty("error", out var e) ? e.GetProperty("code").GetInt32() : null;

    private static Mechanica.Core.Remote.RemoteConnection Auth(Mechanica.Core.Remote.JsonRpcEndpoint endpoint, string token)
    {
        var connection = endpoint.Connect();
        Assert.Null(Code(Send(endpoint, connection, "mechanica.auth", $$"""{ "token": "{{token}}" }""")));
        return connection;
    }

    [Fact]
    public void WorldMethodsNeedAdminAndMapRefusals()
    {
        var endpoint = Endpoint();
        var watcher = Auth(endpoint, "watch");
        var root = Auth(endpoint, "root");

        Assert.Equal(Mechanica.Core.Remote.JsonRpcEndpoint.Codes.Forbidden, Code(Send(endpoint, watcher, "world.info")));
        Assert.Equal("Home", Send(endpoint, root, "world.info").GetProperty("result").GetProperty("name").GetString());
        Assert.Equal("ai-test.json", Send(endpoint, root, "world.save", """{ "name": "AI test" }""").GetProperty("result").GetProperty("file").GetString());
        Assert.Equal("AI test", Send(endpoint, root, "world.list").GetProperty("result")[0].GetProperty("name").GetString());
        // One player is joined, so loading needs disconnect_clients.
        Assert.Equal(Mechanica.Core.Remote.JsonRpcEndpoint.Codes.Rejected, Code(Send(endpoint, root, "world.load", """{ "name": "AI test" }""")));
        Assert.Null(Code(Send(endpoint, root, "world.load", """{ "name": "AI test", "disconnect_clients": true }""")));
        Assert.Equal(Mechanica.Core.Remote.JsonRpcEndpoint.Codes.InvalidParams, Code(Send(endpoint, root, "world.time_scale", """{ "scale": 50 }""")));
        Assert.Equal(2, Send(endpoint, root, "world.time_scale", """{ "scale": 2 }""").GetProperty("result").GetProperty("time_scale").GetDouble());
        Assert.True(Send(endpoint, root, "world.pause", """{ "paused": true }""").GetProperty("result").GetProperty("paused").GetBoolean());
        Assert.False(Send(endpoint, root, "world.info").GetProperty("result").GetProperty("friendly_fire").GetBoolean());
        Assert.Equal(Mechanica.Core.Remote.JsonRpcEndpoint.Codes.Forbidden, Code(Send(endpoint, watcher, "world.friendly_fire", """{ "on": true }""")));
        Assert.True(Send(endpoint, root, "world.friendly_fire", """{ "on": true }""").GetProperty("result").GetProperty("friendly_fire").GetBoolean());
        var spec = Send(endpoint, root, "world.new", """{ "name": "Flat", "mode": "creative", "disconnect_clients": true }""").GetProperty("result");
        Assert.Equal(5, spec.GetProperty("seed").GetInt64());
        _status = _status with { Changing = true };
        Assert.Equal(Mechanica.Core.Remote.JsonRpcEndpoint.Codes.Unavailable, Code(Send(endpoint, root, "world.reset", """{ "disconnect_clients": true }""")));
        Assert.Equal(["load AI test", "start Flat"], _calls);
    }

    [Fact]
    public void ARebuiltEndpointAdoptsOpenConnections()
    {
        var first = Endpoint();
        var watcher = Auth(first, "watch");
        Send(first, watcher, "mechanica.subscribe", """{ "topics": ["world"] }""");

        var second = Endpoint();
        second.Adopt(watcher);

        Assert.True(watcher.Authenticated);
        Assert.Contains(Mechanica.Core.Remote.AgentScope.Observe, watcher.Scopes);
        Assert.Null(Code(Send(second, watcher, "mechanica.describe")));
        _events.Publish("world", new { kind = "loaded" });
        Assert.Single(second.DrainEvents(watcher));
        // New connections never reuse an adopted id.
        Assert.True(second.Connect().Id > watcher.Id);
    }
}
