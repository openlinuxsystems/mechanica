using System.Text.Json;
using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Remote;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Watch players, host plus a client with an AI agent playing. The agent shares a thought with
/// <c>SetThought</c> and both peers get it. The host watches: its view becomes the agent's camera (AI players
/// come first), the banner names it and shows the thought, clicks cycle to the client and back, zoom goes to
/// the agent's own first-person view, and Watch players gives the host's view back. Keys other than chat and
/// Hide GUI are used up while watching. On the client, watching the agent moves on to the host when the
/// agent leaves, and its thought is gone.
/// </summary>
public partial class SpectateTest : StagedTest
{
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private readonly RpcTestClient _agent = new();
    private readonly Dictionary<string, long> _ids = [];
    private int _peer;

    protected override string Tag => "spectate";

    public override void _Ready()
    {
        GameInput.EnsureRegistered();
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins and an agent connects", frame =>
        {
            if (frame == 0)
                return false;
            if (_host.Main.PlayerSync.Players.Count != 2 || _client.Main.PlayerSync.Players.Count != 2)
                return false;
            if (!_agent.IsOpen && _host.Main.Ai.Port == 0)
            {
                Check(_host.Main.Ai.Listen(0) is null, "the host opens the endpoint");
                Check(_agent.Connect(_host.Main.Ai.Port) == Error.Ok, "the agent starts connecting");
            }
            _agent.Poll();
            return _agent.IsOpen;
        });
        Add("nobody is watched at first", _ =>
        {
            Check(!_host.Main.Spectator.Watching, "the host isn't watching yet");
            return true;
        });
        Add("the agent joins and shares a thought", frame =>
        {
            if (frame == 0)
            {
                _ids["join"] = _agent.Call("mechanica.join", new { name = "Scout" });
                _ids["think"] = _agent.Call(AgentPlayers.SetThought, new SetThoughtArgs("Heading to the wreck to look for scrap"));
            }
            _agent.Poll();
            if (Answer("think") is not { } think || Answer("join") is not { } join)
                return false;
            Check(RpcTestClient.ErrorCode(think) is null, $"SetThought is accepted ({think})");
            _peer = RpcTestClient.Result(join).GetProperty("peer").GetInt32();
            return true;
        });
        Add("both peers know the thought", _ =>
            _client.Main.PlayerSync.Players.TryGetValue(_peer, out var seen) && seen.Thought == "Heading to the wreck to look for scrap"
            && Check(_host.Main.PlayerSync.Players[_peer].Thought == seen.Thought, "the host keeps it too"));
        Add("the host watches the agent first", _ =>
        {
            var spectator = _host.Main.Spectator;
            var agent = _host.Main.PlayerSync.Players[_peer];
            var me = _host.Main.PlayerSync.LocalPlayer!;
            Check(spectator.Candidates[0] == agent && spectator.Candidates.Count == 2, "the AI comes before the client");
            Check(spectator.Watch(), "watching starts");
            Check(spectator.Target == agent, "it follows the agent");
            Check(_host.Viewport.GetCamera3D() == agent.Camera, "the host's view is the agent's camera");
            Check(agent.ThirdPerson && agent.BodyVisible, $"from behind it, its body showing ({agent.CameraDistance} m)");
            Check(me.BodyVisible, "the host's own body shows while it looks elsewhere");
            return true;
        });
        // The banner updates in _Process, which can lag a few physics frames behind.
        Add("the banner names the agent and shows its thought", _ =>
        {
            var text = _host.Main.Spectator.BannerText;
            if (text.Length == 0)
                return false;
            Check(text.Contains("Watching Scout (AI)"), $"the banner names it ({text})");
            Check(text.Contains("Heading to the wreck to look for scrap"), "and shows the thought");
            Check(text.Contains(GameInput.Label(InputActions.Spectate)), "and the key to stop");
            return true;
        });
        Add("clicks cycle, zoom goes first person, and other keys are used up", _ =>
        {
            var spectator = _host.Main.Spectator;
            var agent = _host.Main.PlayerSync.Players[_peer];
            var other = _host.Main.PlayerSync.Players[_client.Peer];
            Check(spectator.HandleWatching(Press(InputActions.Primary)) && spectator.Target == other, "left click watches the client");
            Check(_host.Viewport.GetCamera3D() == other.Camera && !agent.ThirdPerson, "with the client's camera; the agent's is reset");
            Check(spectator.HandleWatching(Press(InputActions.Secondary)) && spectator.Target == agent, "right click goes back to the agent");
            for (var i = 0; i < 20 && agent.CameraDistance > 0f; i++)
                spectator.HandleWatching(Press(InputActions.ZoomIn));
            Check(agent.CameraDistance == 0f && !agent.BodyVisible, "zooming all the way in is the agent's own view");
            Check(spectator.HandleWatching(Press(InputActions.Jump)), "jump is used up");
            Check(spectator.HandleWatching(Press(InputActions.Hotbar(2))), "a hotbar key is used up");
            Check(!spectator.HandleWatching(Press(InputActions.Chat)), "chat still opens");
            Check(spectator.Watching, "still watching");
            return true;
        });
        Add("Watch players again gives the view back", _ =>
        {
            var spectator = _host.Main.Spectator;
            var agent = _host.Main.PlayerSync.Players[_peer];
            var me = _host.Main.PlayerSync.LocalPlayer!;
            Check(spectator.HandleWatching(Press(InputActions.Spectate)) && !spectator.Watching, "it stops");
            Check(_host.Viewport.GetCamera3D() == me.Camera, "the host's own camera is back");
            Check(agent.BodyVisible && !agent.ThirdPerson, "the agent looks like any player again");
            return true;
        });
        Add("the banner goes away", _ => _host.Main.Spectator.BannerText == "");
        Add("on the client, the agent leaving moves the camera to the host", frame =>
        {
            var spectator = _client.Main.Spectator;
            if (frame == 0)
            {
                Check(spectator.Watch(_client.Main.PlayerSync.Players[_peer]), "the client watches the agent");
                _ids["leave"] = _agent.Call("mechanica.leave");
            }
            _agent.Poll();
            if (_client.Main.PlayerSync.Players.ContainsKey(_peer) || frame < 2)
                return false;
            var host = _client.Main.PlayerSync.Players[_host.Peer];
            Check(spectator.Target == host, "it now follows the host");
            Check(_client.Viewport.GetCamera3D() == host.Camera, "with the host's camera");
            spectator.Stop();
            Check(_client.Viewport.GetCamera3D() == _client.Main.PlayerSync.LocalPlayer!.Camera, "and stops cleanly");
            Check(_host.Main.AgentHost.Agents.Thoughts.Count == 0, "the agent's thought is gone");
            _agent.Close();
            return true;
        });
    }

    private static InputEvent Press(string action)
    {
        var chord = GameInput.Keymap.Bindings(action).First();
        var @event = GameInput.ToEvent(chord)!;
        switch (@event)
        {
            case InputEventKey key:
                key.Pressed = true;
                break;
            case InputEventMouseButton button:
                button.Pressed = true;
                break;
        }
        return @event;
    }

    private JsonElement? Answer(string name) => _ids.TryGetValue(name, out var id) ? _agent.Response(id) : null;
}
