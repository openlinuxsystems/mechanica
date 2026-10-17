using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Ui;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Quests;

/// <summary>
/// The local player's Logbook map markers (UI spec §13). The host keeps every player's markers (<see cref="MapMarkers"/>,
/// saved with the world) and tells a player whenever theirs change, including when a returning player's saved markers
/// come back. Adding and removing go through <c>AddMapMarker</c> and <c>RemoveMapMarker</c>; the host's answer carries
/// the markers now.
/// </summary>
public partial class MapMarkerSync : Node
{
    private IReadOnlyList<MapMarker> _own = [];

    /// <summary>The session. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Requests go here. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>The host's record of every player's markers. Set before adding to the tree.</summary>
    public MapMarkers Markers { get; init; } = null!;

    /// <summary>The local player's markers, oldest first.</summary>
    public IReadOnlyList<MapMarker> Own => _own;

    /// <summary>Raised when the local player's markers change.</summary>
    public event Action? OwnChanged;

    /// <summary>Registers the marker actions. Call on every peer before the session starts.</summary>
    public void RegisterActions(ActionDispatcher dispatcher) => Markers.Register(dispatcher);

    public override void _Ready()
    {
        Markers.Changed += OnChanged;
        Players.PlayerSpawned += OnSpawned;
        Players.PlayerDespawning += OnDespawning;
        Session.StateChanged += OnStateChanged;
    }

    public override void _ExitTree()
    {
        Markers.Changed -= OnChanged;
        Players.PlayerSpawned -= OnSpawned;
        Players.PlayerDespawning -= OnDespawning;
        Session.StateChanged -= OnStateChanged;
    }

    /// <summary>Marks a place at world <paramref name="at"/>, named <paramref name="name"/> or numbered.</summary>
    public void Add(Vector3 at, string? name = null, Action<ActionResult>? done = null) =>
        Actions.Request(MapMarkers.AddMapMarker, new AddMapMarkerArgs(at.X, at.Y, at.Z, name), result => Answered(result, done));

    /// <summary>Removes the marker <paramref name="id"/>.</summary>
    public void Remove(string id, Action<ActionResult>? done = null) =>
        Actions.Request(MapMarkers.RemoveMapMarker, new RemoveMapMarkerArgs(id), result => Answered(result, done));

    private void Answered(ActionResult result, Action<ActionResult>? done)
    {
        if (result is { Ok: true, Value: { } value } && value.Deserialize<MapMarkerList>(ActionJson.Options) is { } list)
            SetOwn(list.Markers);
        done?.Invoke(result);
    }

    private void OnChanged(int peer, IReadOnlyList<MapMarker> markers)
    {
        if (peer == Session.LocalPeer)
            SetOwn(markers);
        else if (Session.State == NetSession.SessionState.Hosting && Session.ReadyPeers.Contains(peer))
            RpcId(peer, MethodName.ClientMarkers, Pack(markers));
    }

    private void OnSpawned(PlayerController player)
    {
        if (player.PeerId == Session.LocalPeer)
        {
            // The host's copy, saved markers included, once every arrival handler has run.
            Callable.From(Fetch).CallDeferred();
            return;
        }
        if (Session.IsHost)
            Callable.From(() => Welcome(player.PeerId)).CallDeferred();
    }

    private void Fetch()
    {
        // The instance may have left the tree (a session stopped) before the deferred call ran.
        if (!IsInsideTree())
            return;
        Actions.Request(MapMarkers.GetMapMarkers, new NoArgs(), result => Answered(result, null));
    }

    // Host → newcomer: its markers, saved ones included.
    private void Welcome(int peer)
    {
        if (Session.State == NetSession.SessionState.Hosting && Players.Players.ContainsKey(peer))
            RpcId(peer, MethodName.ClientMarkers, Pack(Markers.MarkersOf(peer)));
    }

    private void OnDespawning(PlayerController player)
    {
        if (Session.IsHost && player.PeerId != Session.LocalPeer)
            Markers.Forget(player.PeerId);
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        if (state == NetSession.SessionState.Connecting)
            SetOwn([]);
    }

    /// <summary>Host → client: the client's own markers, as JSON.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientMarkers(string json)
    {
        try
        {
            if (JsonSerializer.Deserialize<MapMarkerList>(json, ActionJson.Options) is { } list)
                SetOwn(list.Markers);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"[markers] bad marker list from the host: {e.Message}");
        }
    }

    private static string Pack(IReadOnlyList<MapMarker> markers) => JsonSerializer.Serialize(new MapMarkerList(markers), ActionJson.Options);

    private void SetOwn(IReadOnlyList<MapMarker> markers)
    {
        if (markers.SequenceEqual(_own))
            return;
        _own = [.. markers];
        OwnChanged?.Invoke();
    }
}
