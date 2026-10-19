using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Net;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.Items;

/// <summary>
/// Inventories and crafting over the network (chunk 18, D10). On the host it is the
/// <see cref="IInventoryWorld"/> behind <see cref="InventoryActions"/> (<c>MoveItems</c>, <c>Craft</c>,
/// <c>CancelCraft</c>, <c>GiveItems</c>, <c>GetInventory</c>) and works every station's queue each physics
/// tick.
/// <para>
/// Inventory contents travel separately from Creation sync, so moving an item never resends a whole
/// Creation. Each peer always sees its own inventory; it asks to <see cref="Open"/> a container or station
/// in reach and is sent that inventory's <see cref="InventoryView"/> whenever it changes, until it calls
/// <see cref="Close"/> or the host closes it (out of reach, picked up, gone). Container contents are still
/// part of the Creation's save JSON, so saves and late joiners carry them.
/// </para>
/// </summary>
public partial class InventorySync : Node, IInventoryWorld
{
    /// <summary>Where the inventory limits are read from.</summary>
    public const string SettingsPath = "res://data/inventory.json";

    // Host ticks between view checks: 6 at 60 Hz sends a changing view (a crafting bar) 10 times a second.
    private const int ViewInterval = 6;

    // Host side: per peer, the inventories it sees and the version it was last sent.
    private readonly Dictionary<int, Dictionary<InventoryRef, InventoryVersion?>> _viewers = [];
    private readonly Dictionary<InventoryRef, InventoryView> _shown = [];
    private readonly Dictionary<(int Creation, int Part), int> _loops = [];
    private IReadOnlyList<OpenInventory> _open = [];
    private int _tick;

    /// <summary>The session updates travel over. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players, for reach. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The Creations this peer shows. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Item and part definitions (the loaded mods). Set before adding to the tree.</summary>
    public IPartCatalog Catalog { get; init; } = null!;

    /// <summary>Recipes of the loaded mods. Set before adding to the tree.</summary>
    public RecipeBook Recipes { get; init; } = RecipeBook.Empty;

    /// <summary>Where stations' crafting sounds play; null plays none.</summary>
    public Sound.SoundSync? Sounds { get; init; }

    /// <summary>The host's part readouts, where stations name the clips they play; null sets none.</summary>
    public Func<Core.Logic.PartReadouts?>? Readouts { get; init; }

    /// <summary>Limits; null loads <see cref="SettingsPath"/>.</summary>
    public InventorySettings? Settings { get; set; }

    /// <summary>The inventory actions (host-side player inventories live here), once registered.</summary>
    public InventoryActions? Items { get; private set; }

    /// <summary>Inventories this peer currently sees, as last sent by the host.</summary>
    public IReadOnlyDictionary<InventoryRef, InventoryView> Shown => _shown;

    /// <summary>This peer's own inventory as last sent, or null before the first view arrives.</summary>
    public InventoryView? Own => _shown.GetValueOrDefault(InventoryRef.OfPlayer(Session.LocalPeer));

    /// <summary>Raised on this peer when a view arrives or changes.</summary>
    public event Action<InventoryView>? ViewChanged;

    /// <summary>Raised on this peer when the host stops showing an inventory, with its reason.</summary>
    public event Action<InventoryRef, string>? ViewClosed;

    /// <summary>
    /// Every station and container any player has open, as the host last told this peer, for the "in use"
    /// marks (UI spec §11).
    /// </summary>
    public IReadOnlyList<OpenInventory> OpenByAnyone => _open;

    /// <summary>Raised on this peer when <see cref="OpenByAnyone"/> changes.</summary>
    public event Action? OpenChanged;

    /// <inheritdoc/>
    public IEnumerable<Creation> AllCreations => Creations.All;

    /// <inheritdoc/>
    public void PlaySound(string sound, string ownerModId, System.Numerics.Vector3 position) =>
        Sounds?.Emit(sound, ownerModId, position.ToGodot());

    /// <inheritdoc/>
    public void Changed(Creation creation) => Creations.Rebuild(creation.Id);

    /// <inheritdoc/>
    public void SetReadout(int creation, int partId, string id, string? text)
    {
        if (Readouts?.Invoke() is not { } readouts)
            return;
        if (text is null)
            readouts.Clear(creation, partId, id);
        else
            readouts.Set(creation, partId, id, text);
    }

    /// <inheritdoc/>
    public void SetPartLoop(int creation, int partId, string? sound, string ownerModId)
    {
        if (Sounds is null)
            return;
        if (_loops.Remove((creation, partId), out var playing))
            Sounds.StopLoop(playing);
        if (sound is not null && Sounds.StartLoop(sound, ownerModId, Sound.SoundAnchor.OnPart(creation, partId)) is > 0 and var handle)
            _loops[(creation, partId)] = handle;
    }

    public override void _Ready()
    {
        Settings ??= LoadSettings();
        Session.PeerReady += OnPeerReady;
        Session.PeerLeft += OnPeerLeft;
        Session.StateChanged += OnStateChanged;
        WatchOwn(Session.LocalPeer);
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= OnPeerReady;
        Session.PeerLeft -= OnPeerLeft;
        Session.StateChanged -= OnStateChanged;
    }

    /// <summary>Registers the inventory actions.</summary>
    public void RegisterActions(ActionDispatcher dispatcher)
    {
        Settings ??= LoadSettings();
        Items = new InventoryActions(Catalog, Recipes, Settings, this);
        Items.Register(dispatcher);
    }

    /// <summary>Reads <see cref="SettingsPath"/>, failing loudly on bad data.</summary>
    public static InventorySettings LoadSettings()
    {
        using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {SettingsPath}: {FileAccess.GetOpenError()}");
        return InventorySettings.FromJson(file.GetAsText());
    }

    /// <inheritdoc/>
    public Creation? FindCreation(int id)
    {
        Creations.NodeOf(id)?.SyncToCreation();
        return Creations.Find(id);
    }

    /// <inheritdoc/>
    public System.Numerics.Vector3? EyeOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) ? player.Head.GlobalPosition.ToNumerics() : null;

    /// <summary>Asks to see a container or station. The view arrives through <see cref="ViewChanged"/>, or <see cref="ViewClosed"/> says why not.</summary>
    public void Open(InventoryRef where)
    {
        var json = JsonSerializer.Serialize(where, ActionJson.Options);
        if (Session.IsHost)
            HostOpen(Session.LocalPeer, json);
        else if (Session.State == NetSession.SessionState.Connected)
            RpcId(1, MethodName.RequestOpen, json);
    }

    /// <summary>Stops seeing a container or station.</summary>
    public void Close(InventoryRef where)
    {
        _shown.Remove(where);
        var json = JsonSerializer.Serialize(where, ActionJson.Options);
        if (Session.IsHost)
            HostClose(Session.LocalPeer, json);
        else if (Session.State == NetSession.SessionState.Connected)
            RpcId(1, MethodName.RequestClose, json);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Session.IsHost || Items is null)
            return;
        Items.Tick((float)delta);
        if (++_tick % ViewInterval == 0)
        {
            SendViews();
            SendOpen();
        }
    }

    private void SendOpen()
    {
        var open = InUseMarks.Open(_viewers.Select(v => new KeyValuePair<int, IEnumerable<InventoryRef>>(v.Key, v.Value.Keys)));
        if (open.SequenceEqual(_open))
            return;
        _open = open;
        OpenChanged?.Invoke();
        if (Session.State != NetSession.SessionState.Hosting)
            return;
        var json = JsonSerializer.Serialize(open, ActionJson.Options);
        foreach (var peer in Session.ReadyPeers)
            RpcId(peer, MethodName.ClientOpen, json);
    }

    private void SendViews()
    {
        foreach (var (peer, views) in _viewers)
        {
            foreach (var (where, sent) in views.ToList())
            {
                var caller = new ActionCaller(peer, ActionSource.Player);
                if (!where.IsPlayer && Items!.CheckAccess(caller, where, out _) is { } lost)
                {
                    views.Remove(where);
                    Send(peer, MethodName.ShowClosed, Json(where), lost);
                    continue;
                }
                var version = Items!.Version(where);
                if (version is null || version == sent)
                    continue;
                views[where] = version;
                Send(peer, MethodName.ShowView, JsonSerializer.Serialize(Items.View(where), ActionJson.Options), "");
            }
        }
    }

    private void Send(int peer, StringName method, string json, string reason)
    {
        if (peer == Session.LocalPeer)
        {
            if (method == MethodName.ShowView)
                ShowView(json, reason);
            else
                ShowClosed(json, reason);
            return;
        }
        if (Session.State == NetSession.SessionState.Hosting && Session.IsReady(peer))
            RpcId(peer, method, json, reason);
    }

    private void HostOpen(int peer, string json)
    {
        if (Items is null || Read(json) is not { } where || where.IsPlayer)
            return;
        if (Items.CheckAccess(new ActionCaller(peer, ActionSource.Player), where, out _) is { } refused)
        {
            Send(peer, MethodName.ShowClosed, json, refused);
            return;
        }
        if (_viewers.TryGetValue(peer, out var views))
            views[where] = null; // send it on the next check
    }

    private void HostClose(int peer, string json)
    {
        if (Read(json) is { IsPlayer: false } where && _viewers.TryGetValue(peer, out var views))
            views.Remove(where);
    }

    /// <summary>Client → host: show me this inventory.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestOpen(string json)
    {
        var peer = Multiplayer.GetRemoteSenderId();
        if (Session.State == NetSession.SessionState.Hosting && Session.IsReady(peer))
            HostOpen(peer, json);
    }

    /// <summary>Client → host: stop showing me this inventory.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestClose(string json)
    {
        if (Session.State == NetSession.SessionState.Hosting)
            HostClose(Multiplayer.GetRemoteSenderId(), json);
    }

    /// <summary>Host → client: what an inventory holds now.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowView(string json, string unused)
    {
        InventoryView? view;
        try
        {
            view = JsonSerializer.Deserialize<InventoryView>(json, ActionJson.Options);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"[items] unreadable view from the host: {e.Message}");
            return;
        }
        if (view is null)
            return;
        _shown[view.Inventory] = view;
        ViewChanged?.Invoke(view);
    }

    /// <summary>Host → client: an inventory is no longer shown, and why.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowClosed(string json, string reason)
    {
        if (Read(json) is not { } where)
            return;
        _shown.Remove(where);
        ViewClosed?.Invoke(where, reason);
    }

    /// <summary>Host → client: who has which station or container open.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientOpen(string json)
    {
        if (Session.State != NetSession.SessionState.Connected)
            return;
        try
        {
            _open = JsonSerializer.Deserialize<List<OpenInventory>>(json, ActionJson.Options) ?? [];
        }
        catch (JsonException e)
        {
            GD.PushWarning($"[items] unreadable in-use list from the host: {e.Message}");
            return;
        }
        OpenChanged?.Invoke();
    }

    private void WatchOwn(int peer)
    {
        if (!_viewers.ContainsKey(peer))
            _viewers[peer] = new() { [InventoryRef.OfPlayer(peer)] = null };
    }

    private void OnPeerReady(int peer)
    {
        WatchOwn(peer);
        if (Session.State == NetSession.SessionState.Hosting && _open.Count > 0)
            RpcId(peer, MethodName.ClientOpen, JsonSerializer.Serialize(_open, ActionJson.Options));
    }

    private void OnPeerLeft(int peer)
    {
        _viewers.Remove(peer);
        Items?.Forget(peer);
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A new session: a client's peer id changes and the host forgets everyone it showed things to.
        _shown.Clear();
        _viewers.Clear();
        _open = [];
        OpenChanged?.Invoke();
        if (Session.IsHost)
            WatchOwn(Session.LocalPeer);
    }

    private static string Json(InventoryRef where) => JsonSerializer.Serialize(where, ActionJson.Options);

    private static InventoryRef? Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<InventoryRef>(json, ActionJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
