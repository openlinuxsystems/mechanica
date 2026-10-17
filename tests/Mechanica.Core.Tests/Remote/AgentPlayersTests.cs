using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Player;
using Mechanica.Core.Remote;
using Mechanica.Core.Saves;
using Codes = Mechanica.Core.Remote.JsonRpcEndpoint.Codes;

namespace Mechanica.Core.Tests.Remote;

/// <summary>AI agents joining as players: bookkeeping, <c>SetPlayerInput</c> and <c>mechanica.join</c>/<c>leave</c>.</summary>
public class AgentPlayersTests
{
    private static readonly AgentSettings Settings = new() { MaxAgents = 2, MaxInputTicks = 10 };

    private sealed class Host(AgentPlayers agents) : IAgentHost
    {
        public List<string> Left { get; } = [];

        public AgentPlayer? Join(string connectionId, string name, out string? error) => agents.Join(connectionId, name, _ => false, out error);

        public int? PeerOf(string connectionId) => agents.OfConnection(connectionId)?.Peer;

        public void Leave(string connectionId)
        {
            Left.Add(connectionId);
            if (agents.OfConnection(connectionId) is { } agent)
                agents.Leave(agent.Peer);
        }
    }

    [Fact]
    public void AgentsGetFreePeersStableValidProfilesAndAMarkedName()
    {
        var agents = new AgentPlayers(Settings);
        var a = agents.Join("1", "Grok", peer => peer == AgentPlayers.FirstPeer, out var error);
        Assert.Null(error);
        Assert.NotNull(a);
        Assert.Equal(AgentPlayers.FirstPeer + 1, a.Peer);
        Assert.Equal("Grok (AI)", a.Profile.Name);
        Assert.True(PlayerProfile.IsValidId(a.Profile.Id));
        Assert.Equal(AgentPlayers.ProfileId("grok"), a.Profile.Id);
        Assert.True(agents.IsAgent(a.Peer));
        Assert.Same(a, agents.OfConnection("1"));

        var b = agents.Join("2", "A name far longer than twenty four", _ => false, out _);
        Assert.NotNull(b);
        Assert.True(b.Profile.Name.Length <= PlayerProfile.MaxNameLength);
        Assert.EndsWith(" (AI)", b.Profile.Name);
        Assert.NotEqual(a.Peer, b.Peer);
    }

    [Fact]
    public void JoinsAreRefusedForBlankOrTakenNamesBusyConnectionsAndTheLimit()
    {
        var agents = new AgentPlayers(Settings);
        Assert.Null(agents.Join("1", "  ", _ => false, out var blank));
        Assert.NotNull(blank);
        Assert.NotNull(agents.Join("1", "Grok", _ => false, out _));
        Assert.Null(agents.Join("1", "Other", _ => false, out var busy));
        Assert.Contains("already plays", busy);
        Assert.Null(agents.Join("2", "GROK", _ => false, out var taken));
        Assert.Contains("already playing", taken);
        Assert.NotNull(agents.Join("2", "Claude", _ => false, out _));
        Assert.Null(agents.Join("3", "Third", _ => false, out var full));
        Assert.Contains("At most 2", full);

        Assert.True(agents.Leave(agents.OfConnection("1")!.Peer));
        Assert.NotNull(agents.Join("3", "Grok", _ => false, out _));
    }

    [Fact]
    public void HeldInputRunsForItsTicksThenTheAgentStandsStillLookingTheSameWay()
    {
        var agents = new AgentPlayers(Settings);
        var dispatcher = new ActionDispatcher();
        agents.Register(dispatcher);
        var agent = agents.Join("1", "Grok", _ => false, out _)!;
        var caller = new ActionCaller(agent.Peer, ActionSource.Player);

        Assert.True(dispatcher.Dispatch(caller, AgentPlayers.SetPlayerInput, """{ "move_z": 1, "yaw": 1.5, "sprint": true, "ticks": 2 }""").Ok);
        Assert.Equal(2, agents.TicksLeft(agent.Peer));
        var last = PlayerInput.None with { Pitch = 0.25f, Prop = Mechanica.Core.Characters.CharacterProp.Sledgehammer };
        var first = agents.NextInput(agent.Peer, last);
        Assert.Equal(1f, first.MoveZ);
        Assert.Equal(1.5f, first.Yaw);
        Assert.Equal(0.25f, first.Pitch);
        Assert.True(first.Sprint);
        Assert.Equal(Mechanica.Core.Characters.CharacterProp.Sledgehammer, first.Prop);
        Assert.Equal(1f, agents.NextInput(agent.Peer, first).MoveZ);
        var idle = agents.NextInput(agent.Peer, first);
        Assert.Equal(0f, idle.MoveZ);
        Assert.False(idle.Sprint);
        Assert.Equal(1.5f, idle.Yaw);
        Assert.Equal(0, agents.TicksLeft(agent.Peer));

        Assert.True(dispatcher.Dispatch(caller, AgentPlayers.SetPlayerInput, """{ "pitch": -0.5 }""").Ok);
        var turned = agents.NextInput(agent.Peer, idle);
        Assert.Equal(1.5f, turned.Yaw);
        Assert.Equal(-0.5f, turned.Pitch);
    }

    [Theory]
    [InlineData("{ \"ticks\": 0 }")]
    [InlineData("{ \"ticks\": 11 }")]
    public void BadInputIsRejected(string args)
    {
        var agents = new AgentPlayers(Settings);
        var dispatcher = new ActionDispatcher();
        agents.Register(dispatcher);
        var agent = agents.Join("1", "Grok", _ => false, out _)!;
        var result = dispatcher.Dispatch(new ActionCaller(agent.Peer, ActionSource.Player), AgentPlayers.SetPlayerInput, args);
        Assert.Equal(ActionErrors.Rejected, result.Code);
    }

    [Fact]
    public void OnlyAnAgentsOwnPlayerMaySteer()
    {
        var agents = new AgentPlayers(Settings);
        var dispatcher = new ActionDispatcher();
        agents.Register(dispatcher);
        var agent = agents.Join("1", "Grok", _ => false, out _)!;
        Assert.Equal(ActionErrors.Rejected, dispatcher.Dispatch(new ActionCaller(1, ActionSource.Player), AgentPlayers.SetPlayerInput, "{}").Code);
        Assert.Equal(ActionErrors.Rejected, dispatcher.Dispatch(new ActionCaller(agent.Peer, ActionSource.External), AgentPlayers.SetPlayerInput, "{}").Code);
    }

    [Fact]
    public void SettingsLoadAndRefuseBadValues()
    {
        var settings = AgentSettings.FromJson("""{ "max_agents": 4, /* c */ "max_input_ticks": 120, }""");
        Assert.Equal(4, settings.MaxAgents);
        Assert.Equal(120, settings.MaxInputTicks);
        Assert.Equal(" (AI)", settings.NameSuffix);
        Assert.Throws<FormatException>(() => AgentSettings.FromJson("""{ "max_input_ticks": 0 }"""));
        Assert.Throws<FormatException>(() => AgentSettings.FromJson("""{ "nonsense": 1 }"""));
        var shipped = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "game", "data", "agents.json");
        Assert.True(AgentSettings.FromJson(File.ReadAllText(shipped)).MaxAgents > 0);
    }

    [Fact]
    public void JoiningMakesTheConnectionActAsItsPlayerUntilItLeavesOrDisconnects()
    {
        var dispatcher = new ActionDispatcher();
        var seen = new List<ActionCaller>();
        dispatcher.Register<NoArgs>("Poke", (_, _) => null, (caller, _) =>
        {
            seen.Add(caller);
            return null;
        });
        var agents = new AgentPlayers(Settings);
        agents.Register(dispatcher);
        var endpoint = new JsonRpcEndpoint(dispatcher, new ActionLog(dispatcher, 60), 1, new EndpointAccess(), null);
        var connection = endpoint.Connect();

        Assert.Equal(Codes.Unavailable, Code(Send(endpoint, connection, "mechanica.join", """{ "name": "Grok" }""")));
        var host = new Host(agents);
        endpoint.Agents = host;
        Assert.Equal(Codes.Rejected, Code(Send(endpoint, connection, AgentPlayers.SetPlayerInput, "{}")));

        var joined = Send(endpoint, connection, "mechanica.join", """{ "name": "Grok" }""").GetProperty("result");
        var peer = joined.GetProperty("peer").GetInt32();
        Assert.Equal("Grok (AI)", joined.GetProperty("name").GetString());
        Assert.Equal(AgentPlayers.ProfileId("Grok"), joined.GetProperty("profile_id").GetString());
        Assert.Equal(Codes.Rejected, Code(Send(endpoint, connection, "mechanica.join", """{ "name": "Again" }""")));
        Send(endpoint, connection, "Poke");
        Assert.Equal(new ActionCaller(peer, ActionSource.Player), seen[^1]);
        Assert.Equal(peer, Send(endpoint, connection, "mechanica.info").GetProperty("result").GetProperty("peer").GetInt32());
        Assert.Null(Code(Send(endpoint, connection, AgentPlayers.SetPlayerInput, """{ "move_z": 1, "ticks": 5 }""")));
        Assert.Equal(5, agents.TicksLeft(peer));

        var other = endpoint.Connect();
        Assert.Equal(Codes.Rejected, Code(Send(endpoint, other, "mechanica.join", """{ "name": "grok" }""")));

        Assert.Null(Code(Send(endpoint, connection, "mechanica.leave")));
        Assert.False(agents.IsAgent(peer));
        Send(endpoint, connection, "Poke");
        Assert.Equal(new ActionCaller(1, ActionSource.External), seen[^1]);
        Assert.Equal(Codes.Unavailable, Code(Send(endpoint, connection, "mechanica.leave")));

        Assert.Null(Code(Send(endpoint, other, "mechanica.join", """{ "name": "grok" }""")));
        var removed = agents.OfConnection("2")!.Peer;
        agents.Leave(removed);
        Assert.Equal(1, Send(endpoint, other, "mechanica.info").GetProperty("result").GetProperty("peer").GetInt32());
        Assert.Null(Code(Send(endpoint, other, "mechanica.join", """{ "name": "grok" }""")));
        endpoint.Disconnect(other);
        Assert.Empty(agents.All);
        Assert.Equal(2, host.Left.Count);
    }

    [Fact]
    public void JoiningNeedsThePlayScope()
    {
        var dispatcher = new ActionDispatcher();
        var endpoint = new JsonRpcEndpoint(dispatcher, new ActionLog(dispatcher, 60), 1,
            new EndpointAccess().AddToken("look", AgentScope.Observe), null) { Agents = new Host(new AgentPlayers(Settings)) };
        var connection = endpoint.Connect();
        Send(endpoint, connection, "mechanica.auth", """{ "token": "look" }""");
        Assert.Equal(Codes.Forbidden, Code(Send(endpoint, connection, "mechanica.join", """{ "name": "Grok" }""")));
    }

    private static JsonElement Send(JsonRpcEndpoint endpoint, RemoteConnection connection, string method, string parameters = "{}")
    {
        var text = endpoint.Handle(connection, $$"""{ "jsonrpc": "2.0", "id": 1, "method": "{{method}}", "params": {{parameters}} }""", 0);
        Assert.NotNull(text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static int? Code(JsonElement response) =>
        response.TryGetProperty("error", out var e) ? e.GetProperty("code").GetInt32() : null;
}
