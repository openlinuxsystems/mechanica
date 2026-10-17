using Godot;
using Mechanica.Core.Net;

namespace Mechanica.Game.Net;

/// <summary>
/// The browser's side of LAN discovery (chunk 24): while in the tree, broadcasts a query to
/// <see cref="NetProtocol.LanPort"/> every <see cref="LanDiscovery.QueryInterval"/> seconds (and to this
/// machine, where broadcasts may not loop back) and lists the hosts that answer.
/// </summary>
public partial class LanBrowser : Node
{
    private readonly LanBrowserList _list = new();
    private PacketPeerUdp? _udp;
    private double _now;
    private double _nextQuery;

    /// <summary>The UDP port hosts listen on.</summary>
    public int Port { get; set; } = NetProtocol.LanPort;

    /// <summary>Sessions heard recently.</summary>
    public IReadOnlyList<LanEntry> Entries => _list.Entries;

    /// <summary>Raised when the list changes.</summary>
    public event Action? Changed;

    public override void _Ready()
    {
        var udp = new PacketPeerUdp();
        if (udp.Bind(0, "*", LanDiscovery.MaxPacketBytes) != Error.Ok)
        {
            GD.PushWarning("[lan] cannot open a socket to look for games");
            return;
        }
        udp.SetBroadcastEnabled(true);
        _udp = udp;
    }

    public override void _ExitTree()
    {
        _udp?.Close();
        _udp = null;
    }

    public override void _Process(double delta)
    {
        if (_udp is null)
            return;
        _now += delta;
        var before = Signature();
        if (_now >= _nextQuery)
        {
            _nextQuery = _now + LanDiscovery.QueryInterval;
            foreach (var to in new[] { "255.255.255.255", "127.0.0.1" })
            {
                _udp.SetDestAddress(to, Port);
                _udp.PutPacket(LanDiscovery.Query);
            }
        }
        while (_udp.GetAvailablePacketCount() > 0)
        {
            var packet = _udp.GetPacket();
            _list.Heard(_udp.GetPacketIP(), packet, _now);
        }
        _list.Expire(_now);
        if (Signature() != before)
            Changed?.Invoke();
    }

    /// <summary>Forgets every host and asks again now.</summary>
    public void Refresh()
    {
        _list.Clear();
        _nextQuery = _now;
        Changed?.Invoke();
    }

    private string Signature() => string.Join("|", _list.Entries.Select(e => $"{e.Endpoint}/{e.Session.Players}"));
}
