using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;

namespace Mechanica.Game.Items;

/// <summary>
/// The vehicle gauges over the network: the host reads the containers of the Creation each seated player sits on
/// (<see cref="VehicleGauges.Read"/>) ten times a second and sends that player its rows when they change, and nothing
/// again until they do. Standing up sends an empty list. Clients keep their own rows in <see cref="Rows"/> for the HUD
/// (<see cref="Ui.VehicleGaugesHud"/>); container contents never travel with the Creation, so they come from here.
/// </summary>
public partial class GaugeSync : Node
{
    // Physics ticks between reads: 6 at 60 Hz is 10 times a second.
    private const int ReadInterval = 6;

    private readonly Dictionary<int, string> _sent = [];
    private int _tick;

    /// <summary>The session updates travel over. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The Creations this peer shows. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Who sits where. Set before adding to the tree.</summary>
    public Func<SeatRegistry?> Seats { get; init; } = () => null;

    /// <summary>This peer's gauges as the host last sent them: empty while the player is not seated.</summary>
    public IReadOnlyList<GaugeRow> Rows { get; private set; } = [];

    /// <summary>Raised on this peer when <see cref="Rows"/> changes.</summary>
    public event Action? Changed;

    public override void _Ready() => Session.PeerLeft += OnPeerLeft;

    public override void _ExitTree() => Session.PeerLeft -= OnPeerLeft;

    private void OnPeerLeft(int peer) => _sent.Remove(peer);

    public override void _PhysicsProcess(double delta)
    {
        if (!Session.IsHost || Seats() is not { } seats || ++_tick % ReadInterval != 0)
            return;
        var seated = new HashSet<int>();
        foreach (var seat in seats.All)
        {
            seated.Add(seat.Peer);
            var rows = Creations.Find(seat.Creation) is { } creation ? VehicleGauges.Read(creation) : [];
            Send(seat.Peer, JsonSerializer.Serialize(rows, ActionJson.Options));
        }
        foreach (var peer in _sent.Keys.Where(p => !seated.Contains(p)).ToList())
        {
            Send(peer, "[]");
            _sent.Remove(peer);
        }
    }

    // Sends a peer its rows unless it already has them (a peer the host has sent nothing has none).
    private void Send(int peer, string json)
    {
        if (_sent.TryGetValue(peer, out var last) ? last == json : json == "[]")
            return;
        _sent[peer] = json;
        if (peer == Session.LocalPeer)
            ShowRows(json);
        else if (Session.State == NetSession.SessionState.Hosting && Session.IsReady(peer))
            RpcId(peer, MethodName.ShowRows, json);
    }

    /// <summary>Host → client: the gauges of the Creation the player sits on.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowRows(string json)
    {
        try
        {
            Rows = JsonSerializer.Deserialize<List<GaugeRow>>(json, ActionJson.Options) ?? [];
        }
        catch (JsonException e)
        {
            GD.PushWarning($"[items] unreadable gauges from the host: {e.Message}");
            return;
        }
        Changed?.Invoke();
    }
}
