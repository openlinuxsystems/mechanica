using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Items;
using Mechanica.Core.Remote;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// AI agents as players, host plus a client (D10). An agent connects to the host's endpoint and joins with
/// <c>mechanica.join</c>: it gets a player of its own, marked as an AI, that the client sees. It walks with
/// <c>SetPlayerInput</c>, and its actions act as that player (items land in its own inventory). A second
/// connection can't take its name, and the human client can't steer it. After <c>mechanica.leave</c> its
/// player is gone on both peers; joining again under the same name brings back its saved inventory and
/// position. Closing the socket while playing removes the player too.
/// </summary>
public partial class AgentsTest : StagedTest
{
    private const string Scrap = "vanilla:scrap_metal";
    private const int WalkTicks = 60;

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private readonly RpcTestClient _agent = new();
    private readonly RpcTestClient _rival = new();
    private readonly Dictionary<string, long> _ids = [];
    private int _peer;
    private string _profileId = "";
    private Vector3 _start;
    private Vector3 _walked;
    private ActionResult? _clientResult;

    protected override string Tag => "agents";

    protected override int StageTimeoutFrames => 600;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins and the host opens the endpoint", _ =>
        {
            if (_host.Main.PlayerSync.Players.Count != 2 || _client.Main.PlayerSync.Players.Count != 2)
                return false;
            Check(_host.Main.Ai.Listen(0) is null && _host.Main.Ai.Port > 0, $"the host listens on port {_host.Main.Ai.Port}");
            Check(_agent.Connect(_host.Main.Ai.Port) == Error.Ok, "the agent starts connecting");
            Check(_rival.Connect(_host.Main.Ai.Port) == Error.Ok, "a second agent starts connecting");
            return true;
        });
        Add("the agents connect", _ => Pump() && _agent.IsOpen && _rival.IsOpen);
        Add("the agent joins as a player of its own", frame =>
        {
            if (frame == 0)
            {
                _ids["join"] = _agent.Call("mechanica.join", new { name = "Scout" });
                _ids["info"] = _agent.Call("mechanica.info");
            }
            if (!Pump() || Answer("info") is not { } info)
                return false;
            var joined = Answer("join")!.Value;
            if (!Check(RpcTestClient.ErrorCode(joined) is null, $"mechanica.join is accepted ({joined})"))
                return true;
            var result = RpcTestClient.Result(joined);
            _peer = result.GetProperty("peer").GetInt32();
            _profileId = result.GetProperty("profile_id").GetString()!;
            Check(result.GetProperty("name").GetString() == "Scout (AI)", "its name is marked as an AI");
            Check(_profileId == AgentPlayers.ProfileId("Scout"), "its profile id follows its name");
            Check(RpcTestClient.Result(info).GetProperty("peer").GetInt32() == _peer
                && RpcTestClient.Result(info).GetProperty("source").GetString() == "player", "its calls now act as that player");
            Check(_host.Main.PlayerSync.Players.ContainsKey(_peer), "the host spawned its player");
            _start = _host.Main.PlayerSync.Players[_peer].GlobalPosition;
            return true;
        });
        Add("the client sees the agent's player", _ =>
            _client.Main.PlayerSync.Players.TryGetValue(_peer, out var seen)
            && Check(seen.DisplayName == "Scout (AI)", $"the client shows its name ({seen.DisplayName})"));
        Add("the agent walks, gives itself items, and others can't use its name or steer it", frame =>
        {
            if (frame == 0)
            {
                _ids["walk"] = _agent.Call(AgentPlayers.SetPlayerInput, new SetPlayerInputArgs(MoveZ: 1, Yaw: 0, Ticks: WalkTicks));
                _ids["give"] = _agent.Call(InventoryActions.GiveItems, new GiveItemsArgs(Scrap, 5));
                _ids["rival"] = _rival.Call("mechanica.join", new { name = "scout" });
                _ids["rival-steer"] = _rival.Call(AgentPlayers.SetPlayerInput, new SetPlayerInputArgs(MoveZ: 1));
                _client.Main.Actions.Request(AgentPlayers.SetPlayerInput, new SetPlayerInputArgs(MoveZ: 1), r => _clientResult = r);
            }
            Pump();
            if (frame < WalkTicks + 10 || Answer("rival-steer", _rival) is null || _clientResult is null)
                return false;
            Check(RpcTestClient.ErrorCode(Answer("walk")!.Value) is null, "SetPlayerInput is accepted");
            Check(RpcTestClient.ErrorCode(Answer("give")!.Value) is null, "GiveItems is accepted");
            Check(_host.Main.Inventory.Items!.PlayerInventory(_peer).Count(Scrap) == 5, "the items are in the agent's own inventory");
            Check(_host.Main.Inventory.Items!.PlayerInventory(_host.Peer).Count(Scrap) == 0, "not the host's");
            Check(RpcTestClient.ErrorCode(Answer("rival", _rival)!.Value) == JsonRpcEndpoint.Codes.Rejected, "another connection can't join under the same name");
            Check(RpcTestClient.ErrorCode(Answer("rival-steer", _rival)!.Value) == JsonRpcEndpoint.Codes.Rejected, "a connection that isn't playing can't steer");
            Check(_clientResult is { Ok: false }, $"the human client can't steer ({_clientResult?.Message})");
            _walked = _host.Main.PlayerSync.Players[_peer].GlobalPosition;
            var moved = new Vector2(_walked.X - _start.X, _walked.Z - _start.Z);
            Check(moved.Length() > 1.5f && moved.Y < -1f, $"it walked forward, down -Z ({_start} to {_walked})");
            Check(_host.Main.AgentHost.Agents.TicksLeft(_peer) == 0, "and stopped when its input ran out");
            return true;
        });
        Add("the client sees where the agent walked", _ =>
            _client.Main.PlayerSync.Players[_peer].GlobalPosition.DistanceTo(_walked) < 0.5f);
        Add("the agent leaves and its player goes on both peers", frame =>
        {
            if (frame == 0)
                _ids["leave"] = _agent.Call("mechanica.leave");
            if (!Pump() || Answer("leave") is null || _client.Main.PlayerSync.Players.ContainsKey(_peer))
                return false;
            Check(RpcTestClient.ErrorCode(Answer("leave")!.Value) is null, "mechanica.leave is accepted");
            Check(!_host.Main.PlayerSync.Players.ContainsKey(_peer) && _host.Main.AgentHost.Agents.All.Count == 0, "the host removed it");
            Check(_host.Main.Session.ProfileOf(_peer) is null, "its profile is forgotten until it comes back");
            return true;
        });
        Add("joining again under the same name brings its things back", frame =>
        {
            if (frame == 0)
                _ids["rejoin"] = _agent.Call("mechanica.join", new { name = "SCOUT" });
            if (!Pump() || Answer("rejoin") is not { } rejoined)
                return false;
            if (!Check(RpcTestClient.ErrorCode(rejoined) is null, $"the rejoin is accepted ({rejoined})"))
                return true;
            _peer = RpcTestClient.Result(rejoined).GetProperty("peer").GetInt32();
            Check(RpcTestClient.Result(rejoined).GetProperty("profile_id").GetString() == _profileId, "the same profile, whatever the case of the name");
            Check(_host.Main.Inventory.Items!.PlayerInventory(_peer).Count(Scrap) == 5, "its inventory is restored");
            var at = _host.Main.PlayerSync.Players[_peer].GlobalPosition;
            Check(at.DistanceTo(_walked) < 0.5f, $"it is back where it left ({at} vs {_walked})");
            return true;
        });
        Add("closing the socket while playing removes the player", frame =>
        {
            if (frame == 0)
                _agent.Close();
            Pump();
            if (_host.Main.PlayerSync.Players.ContainsKey(_peer) || _client.Main.PlayerSync.Players.ContainsKey(_peer))
                return false;
            Check(_host.Main.AgentHost.Agents.All.Count == 0, "the agent is gone");
            _rival.Close();
            return true;
        });
        Add("the endpoint drops closed connections", _ =>
        {
            Pump();
            return _host.Main.Ai.ConnectionCount == 0;
        });
    }

    private bool Pump()
    {
        _agent.Poll();
        _rival.Poll();
        return true;
    }

    private JsonElement? Answer(string name, RpcTestClient? client = null) =>
        _ids.TryGetValue(name, out var id) ? (client ?? _agent).Response(id) : null;
}
