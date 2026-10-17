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
/// <see cref="PartReadouts"/>: menu readouts and the machine feedback of UI spec §12). The host sends them to every
/// client when they change, at most <see cref="SendRate"/> times a second, and to each client that joins. Every peer
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
    private (int Lights, int Animations, int Readouts) _sent = (-1, -1, -1);
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
        var runtime = Scripts.Runtime;
        // The host shows its own animations on the display clock, like a client.
        if (runtime.Animations.Revision != _shownAnimations)
        {
            _shownAnimations = runtime.Animations.Revision;
            _animations.Load(runtime.Animations.All(runtime.Time), Now);
        }
        _sinceSend += delta;
        var revisions = (runtime.Lights.Revision, runtime.Animations.Revision, runtime.Readouts.Revision);
        if (revisions == _sent || _sinceSend < 1.0 / SendRate)
            return;
        _sent = revisions;
        _sinceSend = 0;
        var (lights, animations, readouts) = Payload();
        foreach (var peer in Session.ReadyPeers)
            RpcId(peer, MethodName.ClientDisplay, lights, animations, readouts);
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

    private (string Lights, string Animations, string Readouts) Payload()
    {
        var runtime = Scripts.Runtime;
        return (JsonSerializer.Serialize(runtime.Lights.All, ActionJson.Options),
            JsonSerializer.Serialize(runtime.Animations.All(runtime.Time), ActionJson.Options),
            JsonSerializer.Serialize(runtime.Readouts.All, ActionJson.Options));
    }

    private void SendTo(int peer)
    {
        var (lights, animations, readouts) = Payload();
        RpcId(peer, MethodName.ClientDisplay, lights, animations, readouts);
    }

    /// <summary>Host → client: every lit part and its level, every animated part as it is now, and what every part shows.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientDisplay(string lights, string animations, string readouts)
    {
        if (Session.IsHost)
            return;
        try
        {
            _lights.Load(JsonSerializer.Deserialize<List<LightLevel>>(lights, ActionJson.Options) ?? []);
            _animations.Load(JsonSerializer.Deserialize<List<PartAnimation>>(animations, ActionJson.Options) ?? [], Now);
            _readouts.Load(JsonSerializer.Deserialize<List<PartReadout>>(readouts, ActionJson.Options) ?? []);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"[display] bad part lights or animations from the host: {e.Message}");
        }
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client's copy belongs to the session it leaves.
        _lights.Load([]);
        _animations.Load([], Now);
        _readouts.Load([]);
        _sent = (-1, -1, -1);
        _shownAnimations = -1;
    }
}
