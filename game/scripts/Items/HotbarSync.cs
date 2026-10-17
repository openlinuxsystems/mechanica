using System.Text.Json;
using Godot;
using Mechanica.Game.Player;
using Mechanica.Core.Actions;
using Mechanica.Core.Items;
using Mechanica.Game.Net;

namespace Mechanica.Game.Items;

/// <summary>
/// The local player's hotbar (UI spec §3). The host keeps every player's slots (<see cref="HotbarActions"/>, saved
/// with the world) and tells a player whenever theirs change, including when a returning player's saved slots
/// come back. Changing a slot goes through <c>SetHotbarSlot</c>; the host's answer carries the slots now.
/// </summary>
public partial class HotbarSync : Node
{
    private IReadOnlyList<string?> _own = HotbarActions.Default;

    /// <summary>The session. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Requests go here. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>The host's record of every hotbar. Set before adding to the tree.</summary>
    public HotbarActions Bars { get; init; } = null!;

    /// <summary>What the local player's slots hold (qualified ids, null for empty).</summary>
    public IReadOnlyList<string?> Own => _own;

    /// <summary>Raised when the local player's slots change.</summary>
    public event Action? OwnChanged;

    /// <summary>Registers the hotbar actions. Call on every peer before the session starts.</summary>
    public void RegisterActions(ActionDispatcher dispatcher) => Bars.Register(dispatcher);

    public override void _Ready()
    {
        Bars.Changed += OnChanged;
        Players.PlayerSpawned += OnSpawned;
        Players.PlayerDespawning += OnDespawning;
        Session.StateChanged += OnStateChanged;
    }

    public override void _ExitTree()
    {
        Bars.Changed -= OnChanged;
        Players.PlayerSpawned -= OnSpawned;
        Players.PlayerDespawning -= OnDespawning;
        Session.StateChanged -= OnStateChanged;
    }

    /// <summary>Puts <paramref name="entry"/> (a tool, an item id or null for empty) in slot <paramref name="slot"/> (0 to 9).</summary>
    public void Set(int slot, string? entry, Action<ActionResult>? done = null) =>
        Actions.Request(HotbarActions.SetHotbarSlot, new SetHotbarSlotArgs(slot, entry), result =>
        {
            if (result is { Ok: true, Value: { } value } && value.Deserialize<HotbarSlots>(ActionJson.Options) is { } view)
                SetOwn(view.Slots);
            done?.Invoke(result);
        });

    /// <summary>Swaps what two slots hold.</summary>
    public void Swap(int a, int b, Action<ActionResult>? done = null)
    {
        if (a == b)
            return;
        var (first, second) = (_own[a], _own[b]);
        Set(a, second, _ => Set(b, first, done));
    }

    private void OnChanged(int peer, IReadOnlyList<string?> slots)
    {
        if (peer == Session.LocalPeer)
            SetOwn(slots);
        else if (Session.State == NetSession.SessionState.Hosting && Session.ReadyPeers.Contains(peer))
            RpcId(peer, MethodName.ClientHotbar, Pack(slots));
    }

    private void OnSpawned(PlayerController player)
    {
        if (player.PeerId == Session.LocalPeer)
        {
            // The host's copy, saved slots included, once every arrival handler has run.
            Callable.From(Fetch).CallDeferred();
            return;
        }
        if (Session.IsHost)
            Callable.From(() => Welcome(player.PeerId)).CallDeferred();
    }

    private void Fetch() =>
        Actions.Request(HotbarActions.GetHotbar, new NoArgs(), result =>
        {
            if (result is { Ok: true, Value: { } value } && value.Deserialize<HotbarSlots>(ActionJson.Options) is { } view)
                SetOwn(view.Slots);
        });

    // Host → newcomer: its slots, saved ones included.
    private void Welcome(int peer)
    {
        if (Session.State == NetSession.SessionState.Hosting && Players.Players.ContainsKey(peer))
            RpcId(peer, MethodName.ClientHotbar, Pack(Bars.SlotsOf(peer)));
    }

    private void OnDespawning(PlayerController player)
    {
        if (Session.IsHost && player.PeerId != Session.LocalPeer)
            Bars.Forget(player.PeerId);
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        if (state == NetSession.SessionState.Connecting)
            SetOwn(HotbarActions.Default);
    }

    /// <summary>Host → client: the client's own slots (empty strings are empty slots).</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientHotbar(string[] slots) => SetOwn([.. slots.Take(HotbarActions.SlotCount).Select(s => s.Length == 0 ? null : s)]);

    private static string[] Pack(IReadOnlyList<string?> slots) => [.. slots.Select(s => s ?? "")];

    private void SetOwn(IReadOnlyList<string?> slots)
    {
        var padded = Enumerable.Range(0, HotbarActions.SlotCount).Select(i => i < slots.Count ? slots[i] : null).ToList();
        if (padded.SequenceEqual(_own))
            return;
        _own = padded;
        OwnChanged?.Invoke();
    }
}
