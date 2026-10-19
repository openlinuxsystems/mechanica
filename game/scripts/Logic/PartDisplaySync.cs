using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Logic;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Scripting;

namespace Mechanica.Game.Logic;

/// <summary>
/// How parts look as their scripts drive them, over the network (chunk 23, D10): lights
/// (<c>part.set_light</c>, the runtime's <see cref="LightLevels"/>), turning models
/// (<c>part.animate</c>, its <see cref="PartAnimations"/>) and what scripts show (<c>part.display</c>, its
/// <see cref="PartReadouts"/>: menu readouts and the machine feedback of UI spec §12). The host sends lights and
/// animations to every client when they change, at most <see cref="SendRate"/> times a second, and to each client that
/// joins. Readouts change far more often, so each client gets only the ones that changed, and only for the Creations
/// it sees (<see cref="InterestOf"/>, through a <see cref="ReadoutFeed"/> per client). Every peer
/// shows each Creation's lights at their levels, turns each animated model, carrying a spin on smoothly between
/// updates on its own clock, and draws each part's display (<see cref="CreationNode.ApplyDisplays"/>).
/// </summary>
public partial class PartDisplaySync : Node
{
    /// <summary>Most updates the host sends each second (a blinking warning light changes often).</summary>
    public const float SendRate = 20f;

    private readonly LightLevels _lights = new();
    private readonly PartAnimations _animations = new();
    private readonly PartReadouts _readouts = new();
    private readonly Dictionary<int, ReadoutFeed> _feeds = [];
    private (int Lights, int Animations) _sent = (-1, -1);
    private int _shownAnimations = -1;
    private double _sinceSend;

    /// <summary>The session. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The Creations this peer shows. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>The host's part scripts. Set before adding to the tree.</summary>
    public ScriptSync Scripts { get; init; } = null!;

    /// <summary>The light levels this peer shows: the host's own, or a client's copy of them.</summary>
    public LightLevels Lights => Session.IsHost ? Scripts.Runtime.Lights : _lights;

    /// <summary>What part scripts show on this peer: the host's own, or a client's copy.</summary>
    public PartReadouts Readouts => Session.IsHost ? Scripts.Runtime.Readouts : _readouts;

    /// <summary>
    /// Host side: the Creations a client sees (its network interest), whose readouts it gets. Set before adding to the tree.
    /// </summary>
    public Func<int, IReadOnlyCollection<int>> InterestOf { get; init; } = null!;

    /// <summary>True while the local player has the Connect Tool out (bars show, status icons hide); null never.</summary>
    public Func<bool>? ConnectToolOut { get; init; }

    /// <summary>The animations this peer shows, on its own clock (<see cref="Now"/>).</summary>
    public PartAnimations Animations => _animations;

    /// <summary>This peer's display clock, in seconds.</summary>
    public static double Now => Time.GetTicksUsec() / 1e6;

    public override void _Ready()
    {
        // After the scripts, so what they set this step goes out this step.
        ProcessPhysicsPriority = Scripts.ProcessPhysicsPriority + 1;
        Session.PeerReady += SendTo;
        Session.PeerLeft += OnPeerLeft;
        Session.StateChanged += OnStateChanged;
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= SendTo;
        Session.PeerLeft -= OnPeerLeft;
        Session.StateChanged -= OnStateChanged;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Session.IsHost)
            return;
        var runtime = Scripts.Runtime;
        // The host shows its own animations on the display clock, like a client.
        if (runtime.Animations.Revision != _shownAnimations)
        {
            _shownAnimations = runtime.Animations.Revision;
            _animations.Load(runtime.Animations.All(runtime.Time), Now);
        }
        // The first change after a quiet spell goes at once; the rate only spaces out changes that follow it.
        _sinceSend += delta;
        if (_sinceSend < 1.0 / SendRate)
            return;
        var sent = false;
        var revisions = (runtime.Lights.Revision, runtime.Animations.Revision);
        if (revisions != _sent)
        {
            _sent = revisions;
            var (lights, animations) = Payload();
            foreach (var peer in Session.ReadyPeers)
                RpcId(peer, MethodName.ClientDisplay, lights, animations);
            sent = true;
        }
        foreach (var peer in Session.ReadyPeers)
        {
            if (!_feeds.TryGetValue(peer, out var feed))
                _feeds[peer] = feed = new ReadoutFeed();
            if (feed.Next(runtime.Readouts, InterestOf(peer)) is { } changes)
            {
                RpcId(peer, MethodName.ClientReadouts, JsonSerializer.Serialize(changes, ActionJson.Options));
                sent = true;
            }
        }
        if (sent)
            _sinceSend = 0;
    }

    public override void _Process(double delta)
    {
        var (lights, readouts, now) = (Lights, Readouts, Now);
        var connecting = ConnectToolOut?.Invoke() == true;
        foreach (var creation in Creations.All)
        {
            if (Creations.NodeOf(creation.Id) is { } node)
            {
                node.ApplyLights(lights);
                node.ApplyAnimations(_animations, now);
                node.ApplyDisplays(readouts, connecting, delta);
            }
        }
    }

    private (string Lights, string Animations) Payload()
    {
        var runtime = Scripts.Runtime;
        return (JsonSerializer.Serialize(runtime.Lights.All, ActionJson.Options),
            JsonSerializer.Serialize(runtime.Animations.All(runtime.Time), ActionJson.Options));
    }

    private void SendTo(int peer)
    {
        var (lights, animations) = Payload();
        RpcId(peer, MethodName.ClientDisplay, lights, animations);
        // A peer that joins starts from nothing; its feed sends what it sees on the next send.
        _feeds[peer] = new ReadoutFeed();
    }

    /// <summary>Host → client: every lit part and its level, and every animated part as it is now.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientDisplay(string lights, string animations)
    {
        if (Session.IsHost)
            return;
        try
        {
            _lights.Load(JsonSerializer.Deserialize<List<LightLevel>>(lights, ActionJson.Options) ?? []);
            _animations.Load(JsonSerializer.Deserialize<List<PartAnimation>>(animations, ActionJson.Options) ?? [], Now);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"[display] bad part lights or animations from the host: {e.Message}");
        }
    }

    /// <summary>Host → client: what parts show, changed since the last update, for the Creations this client sees.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientReadouts(string changes)
    {
        if (Session.IsHost)
            return;
        try
        {
            if (JsonSerializer.Deserialize<ReadoutChanges>(changes, ActionJson.Options) is { } update)
                _readouts.Apply(update);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"[display] bad part readouts from the host: {e.Message}");
        }
    }

    private void OnPeerLeft(int peer) => _feeds.Remove(peer);

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client's copy belongs to the session it leaves.
        _lights.Load([]);
        _animations.Load([], Now);
        _readouts.Load([]);
        _feeds.Clear();
        _sent = (-1, -1);
        _shownAnimations = -1;
    }
}
