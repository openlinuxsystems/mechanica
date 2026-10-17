using Godot;
using Mechanica.Core.Net;

namespace Mechanica.Game.Net;

/// <summary>
/// The host's side of LAN discovery (chunk 24): while hosting, listens on <see cref="NetProtocol.LanPort"/>
/// and answers each browser's query with the session (<see cref="LanSession"/>). It never sends unasked.
/// Only one host per machine can listen on the port; another one still hosts, but is not listed.
/// </summary>
public partial class LanBeacon : Node
{
    private PacketPeerUdp? _udp;

    /// <summary>The session to describe. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>What to say about the session now; null says nothing.</summary>
    public Func<LanSession?> Describe { get; init; } = () => null;

    /// <summary>The UDP port to listen on.</summary>
    public int Port { get; set; } = NetProtocol.LanPort;

    /// <summary>True while listening.</summary>
    public bool Listening => _udp is not null;

    /// <summary>Queries answered, for tests.</summary>
    public int Answered { get; private set; }

    public override void _Ready()
    {
        Session.StateChanged += OnStateChanged;
        OnStateChanged(Session.State);
    }

    public override void _ExitTree()
    {
        Session.StateChanged -= OnStateChanged;
        Stop();
    }

    public override void _Process(double delta)
    {
        if (_udp is null)
            return;
        while (_udp.GetAvailablePacketCount() > 0)
        {
            var packet = _udp.GetPacket();
            var address = _udp.GetPacketIP();
            var port = _udp.GetPacketPort();
            if (!LanDiscovery.IsQuery(packet) || Describe() is not { } session)
                continue;
            _udp.SetDestAddress(address, port);
            if (_udp.PutPacket(LanDiscovery.Encode(session)) == Error.Ok)
                Answered++;
        }
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        if (state == NetSession.SessionState.Hosting)
            Start();
        else
            Stop();
    }

    private void Start()
    {
        if (_udp is not null)
            return;
        var udp = new PacketPeerUdp();
        var error = udp.Bind(Port, "*", LanDiscovery.MaxPacketBytes);
        if (error != Error.Ok)
        {
            GD.Print($"[lan] not listed on the LAN: port {Port} is in use ({error})");
            return;
        }
        _udp = udp;
        GD.Print($"[lan] listed on the LAN (port {Port})");
    }

    private void Stop()
    {
        _udp?.Close();
        _udp = null;
    }
}
