using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Logic;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Scripting;

namespace Mechanica.Game.Logic;

/// <summary>
/// Part lights over the network (chunk 23, D10). On the host the part scripts set each light's level
/// (<c>part.set_light</c>, kept in the runtime's <see cref="LightLevels"/>); the host sends the levels to
/// every client when they change, at most <see cref="SendRate"/> times a second, and to each client that
/// joins. Every peer then shows each Creation's lights at their levels.
/// </summary>
public partial class LightSync : Node
{
    /// <summary>Most light updates the host sends each second (a blinking warning light changes often).</summary>
    public const float SendRate = 20f;

    private readonly LightLevels _client = new();
    private int _sent = -1;
    private double _sinceSend;

    /// <summary>The session. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The Creations this peer shows. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>The host's part scripts. Set before adding to the tree.</summary>
    public ScriptSync Scripts { get; init; } = null!;

    /// <summary>The levels this peer shows: the host's own, or a client's copy of them.</summary>
    public LightLevels Levels => Session.IsHost ? Scripts.Runtime.Lights : _client;

    public override void _Ready()
    {
        // After the scripts, so a light set this step goes out this step.
        ProcessPhysicsPriority = Scripts.ProcessPhysicsPriority + 1;
        Session.PeerReady += SendTo;
        Session.StateChanged += OnStateChanged;
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= SendTo;
        Session.StateChanged -= OnStateChanged;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Session.IsHost)
            return;
        _sinceSend += delta;
        var levels = Scripts.Runtime.Lights;
        if (levels.Revision == _sent || _sinceSend < 1.0 / SendRate)
            return;
        _sent = levels.Revision;
        _sinceSend = 0;
        var json = JsonSerializer.Serialize(levels.All, ActionJson.Options);
        foreach (var peer in Session.ReadyPeers)
            RpcId(peer, MethodName.ClientLights, json);
    }

    public override void _Process(double delta)
    {
        var levels = Levels;
        foreach (var creation in Creations.All)
            Creations.NodeOf(creation.Id)?.ApplyLights(levels);
    }

    private void SendTo(int peer) =>
        RpcId(peer, MethodName.ClientLights, JsonSerializer.Serialize(Scripts.Runtime.Lights.All, ActionJson.Options));

    /// <summary>Host → client: every lit part and its level.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientLights(string json)
    {
        if (Session.IsHost)
            return;
        try
        {
            _client.Load(JsonSerializer.Deserialize<List<LightLevel>>(json, ActionJson.Options) ?? []);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"[lights] bad light levels from the host: {e.Message}");
        }
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client's copy belongs to the session it leaves.
        _client.Load([]);
        _sent = -1;
    }
}
