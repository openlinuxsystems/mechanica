using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Mechanica.Game.Items;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.World;
using FileAccess = Godot.FileAccess;
using RayHit = Mechanica.Core.Survival.RayHit;

namespace Mechanica.Game.Survival;

/// <summary>
/// The survival loop over the network (chunk 20, D10). On the host it is the <see cref="ISurvivalWorld"/>
/// behind <see cref="SurvivalActions"/>: it answers ray casts, ground and water questions from the live
/// scene, teleports players, ticks crops and item piles, turns players' landings into fall damage and
/// Bodies' sudden stops into impact damage.
/// <para>
/// Clients are sent what they show: every player's <see cref="VitalsView"/> when it changes (so dead
/// players are hidden everywhere), every pile of items on the ground, and the <see cref="SoilView"/> of
/// each soil part whose crop or water changed. A peer that becomes ready is sent all of it.
/// </para>
/// </summary>
public partial class SurvivalSync : Node3D, ISurvivalWorld
{
    /// <summary>Where the survival numbers are read from.</summary>
    public const string SettingsPath = "res://data/survival.json";

    // Host ticks between soil checks: 6 at 60 Hz.
    private const int SoilInterval = 6;

    private readonly Dictionary<int, VitalsView> _vitals = [];
    private readonly Dictionary<int, double> _vitalsAt = [];
    private readonly Dictionary<int, ItemDrop> _drops = [];
    private readonly Dictionary<int, DropPile> _dropNodes = [];
    private readonly Dictionary<(int Creation, int Part), SoilView> _soil = [];
    private readonly Dictionary<(int Creation, int Part), CropNode> _cropNodes = [];
    // Host side: the soil revision last sent, per soil part.
    private readonly Dictionary<(int Creation, int Part), int> _soilSent = [];
    // Host side: each Body's velocity last tick, and Bodies pushed since then (not crashes).
    private readonly Dictionary<ulong, Vector3> _lastVelocity = [];
    private readonly HashSet<ulong> _pushed = [];
    private int _tick;

    /// <summary>The session updates travel over. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The Creations this peer shows. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>The streamed world: ground, water and world objects. Set before adding to the tree.</summary>
    public WorldStreamer World { get; init; } = null!;

    /// <summary>Building, for breaking parts and pushing Bodies. Set before adding to the tree.</summary>
    public BuildSync Build { get; init; } = null!;

    /// <summary>Inventories. Set before adding to the tree; register its actions first.</summary>
    public InventorySync Inventory { get; init; } = null!;

    /// <summary>Item and part definitions. Set before adding to the tree.</summary>
    public IPartCatalog Catalog { get; init; } = null!;

    /// <summary>How the world plays when this peer hosts it.</summary>
    public GameMode Mode { get; init; } = GameMode.Survival;

    /// <summary>The numbers; null loads <see cref="SettingsPath"/>.</summary>
    public SurvivalSettings? Settings { get; set; }

    /// <summary>The survival actions, once registered (the host's health, beds and piles live here).</summary>
    public SurvivalActions? Actions { get; private set; }

    /// <summary>Every player's health as last sent, by peer.</summary>
    public IReadOnlyDictionary<int, VitalsView> Vitals => _vitals;

    /// <summary>This peer's own health as last sent, or null before it arrives.</summary>
    public VitalsView? Own => _vitals.GetValueOrDefault(Session.LocalPeer);

    /// <summary>Seconds until this peer's dead player may respawn, counting down since the view arrived.</summary>
    public float OwnRespawnIn => Own is { Dead: true } own
        ? Math.Max(0f, own.RespawnIn - (float)(Now - _vitalsAt.GetValueOrDefault(Session.LocalPeer)))
        : 0f;

    /// <summary>The piles of items this peer shows, by id.</summary>
    public IReadOnlyDictionary<int, ItemDrop> Drops => _drops;

    /// <summary>The soil this peer shows, by Creation and part.</summary>
    public IReadOnlyDictionary<(int Creation, int Part), SoilView> Soil => _soil;

    /// <summary>The nodes showing the piles, by id (tests look at them).</summary>
    public IReadOnlyDictionary<int, DropPile> DropNodes => _dropNodes;

    /// <summary>The nodes showing crops, by Creation and part (tests look at them).</summary>
    public IReadOnlyDictionary<(int Creation, int Part), CropNode> CropNodes => _cropNodes;

    /// <summary>Raised on this peer when a player's health arrives or changes.</summary>
    public event Action<VitalsView>? VitalsChanged;

    /// <inheritdoc/>
    public double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready()
    {
        Settings ??= LoadSettings();
        Session.PeerReady += SendAllTo;
        Session.StateChanged += OnStateChanged;
        Players.PlayerSpawned += OnPlayerSpawned;
        Players.PlayerDespawning += OnPlayerDespawning;
        Build.PartBreaking += OnPartBreaking;
        Build.BodyPushed += OnBodyPushed;
        // The offline player spawned before this node existed.
        foreach (var player in Players.Players.Values)
            OnPlayerSpawned(player);
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= SendAllTo;
        Session.StateChanged -= OnStateChanged;
        Players.PlayerSpawned -= OnPlayerSpawned;
        Players.PlayerDespawning -= OnPlayerDespawning;
        Build.PartBreaking -= OnPartBreaking;
        Build.BodyPushed -= OnBodyPushed;
    }

    /// <summary>Reads <see cref="SettingsPath"/>, failing loudly on bad data.</summary>
    public static SurvivalSettings LoadSettings()
    {
        using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {SettingsPath}: {FileAccess.GetOpenError()}");
        return SurvivalSettings.FromJson(file.GetAsText());
    }

    /// <summary>
    /// Registers the survival actions and makes dead players unable to act (<see cref="ActionDispatcher.Gate"/>).
    /// Call after the inventory actions, and before <see cref="BuildSync.RegisterActions"/> so building is paid for.
    /// </summary>
    public void RegisterActions(ActionDispatcher dispatcher)
    {
        Settings ??= LoadSettings();
        Actions = new SurvivalActions(Catalog, Settings, Inventory.Items!, this) { Mode = Mode };
        Actions.Register(dispatcher);
        dispatcher.Gate = Actions.Gate;
        Actions.VitalsChanged += OnVitalsChanged;
        Actions.DropChanged += OnDropChanged;
        Actions.DropRemoved += OnDropRemoved;
        Build.Costs = Actions;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Session.IsHost || Actions is null)
            return;
        Actions.Tick((float)delta);
        TrackImpacts();
        if (++_tick % SoilInterval == 0)
            SendSoil();
    }

    public override void _Process(double delta)
    {
        foreach (var (key, crop) in _cropNodes)
            crop.Follow(Creations.NodeOf(key.Creation), key.Part);
    }

    // ---- ISurvivalWorld ----

    /// <inheritdoc/>
    public Creation? FindCreation(int id)
    {
        Creations.NodeOf(id)?.SyncToCreation();
        return Creations.Find(id);
    }

    /// <inheritdoc/>
    public IEnumerable<Creation> AllCreations => Creations.All;

    /// <inheritdoc/>
    public System.Numerics.Vector3? EyeOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) ? player.Head.GlobalPosition.ToNumerics() : null;

    /// <inheritdoc/>
    public System.Numerics.Vector3? FeetOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) ? player.GlobalPosition.ToNumerics() : null;

    /// <inheritdoc/>
    public System.Numerics.Vector3? LookOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) ? (-player.Head.GlobalBasis.Z).Normalized().ToNumerics() : null;

    /// <inheritdoc/>
    public System.Numerics.Vector3 SpawnOf(int peer) => (Players.SpawnSpotOf(peer) ?? Vector3.Zero).ToNumerics();

    /// <inheritdoc/>
    public void Teleport(int peer, System.Numerics.Vector3 feet) => Players.HostTeleport(peer, feet.ToGodot());

    /// <inheritdoc/>
    public RayHit? Cast(System.Numerics.Vector3 from, System.Numerics.Vector3 direction, float range, int ignorePlayer)
    {
        var start = from.ToGodot();
        var query = PhysicsRayQueryParameters3D.Create(start, start + direction.ToGodot().Normalized() * range);
        if (Players.Players.TryGetValue(ignorePlayer, out var shooter))
            query.Exclude = [shooter.GetRid()];
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return null;
        var point = hit["position"].AsVector3();
        var normal = hit["normal"].AsVector3();
        switch (hit["collider"].AsGodotObject())
        {
            case PlayerController player:
                return new RayHit(point.ToNumerics(), Player: player.PeerId);
            case CreationBody body when body.GetParent() is CreationNode node:
                var toGrid = body.GlobalTransform.AffineInverse();
                var (cell, _) = BuildAim.FromHit((toGrid * point).ToNumerics(), (toGrid.Basis * normal).ToNumerics());
                return node.Creation.PartAt(cell) is { } part
                    ? new RayHit(point.ToNumerics(), Creation: node.Creation.Id, Part: part.Id)
                    : new RayHit(point.ToNumerics());
            case StaticBody3D objects when objects.GetParent() is TerrainChunk chunk && objects == chunk.Objects:
                return new RayHit(point.ToNumerics(), Object: chunk.ObjectAt(hit["shape"].AsInt32()));
            default:
                return new RayHit(point.ToNumerics());
        }
    }

    /// <inheritdoc/>
    public System.Numerics.Vector3 GroundBelow(System.Numerics.Vector3 point)
    {
        var start = point.ToGodot() + new Vector3(0, 0.25f, 0);
        var query = PhysicsRayQueryParameters3D.Create(start, start + new Vector3(0, -200f, 0));
        query.Exclude = [.. Players.Players.Values.Select(p => p.GetRid())];
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
            return hit["position"].AsVector3().ToNumerics();
        return new System.Numerics.Vector3(point.X, World.Generator.HeightAt(point.X, point.Z), point.Z);
    }

    /// <inheritdoc/>
    public bool IsWater(System.Numerics.Vector3 point)
    {
        var sea = World.Generator.SeaLevel;
        var floor = World.Generator.HeightAt(point.X, point.Z);
        return floor < sea && point.Y <= sea + 0.25f && point.Y >= floor - 0.25f;
    }

    /// <inheritdoc/>
    public WorldObjectPlacement? FindObject(WorldObjectId id) =>
        World.State.IsDestroyed(id) ? null : World.Generator.Find(id);

    /// <inheritdoc/>
    public IEnumerable<WorldObjectPlacement> ObjectsNear(System.Numerics.Vector3 point, float radius) =>
        World.Generator.ObjectsIn(point.X - radius, point.Z - radius, point.X + radius, point.Z + radius)
            .Where(o => !World.State.IsDestroyed(o.Id) && System.Numerics.Vector3.Distance(o.Position, point) <= radius);

    /// <inheritdoc/>
    public float AddObjectDamage(WorldObjectId id, float damage) => World.State.AddDamage(id, damage);

    /// <inheritdoc/>
    public void DestroyObject(WorldObjectId id) => World.DestroyObject(id);

    /// <inheritdoc/>
    public SledgeResult DamagePart(Creation creation, int partId, float damage) => Build.Tools!.DamagePart(creation, partId, damage);

    /// <inheritdoc/>
    public void Push(Creation creation, int bodyIndex, System.Numerics.Vector3 point, System.Numerics.Vector3 impulse) =>
        Build.Push(creation, bodyIndex, point, impulse);

    // ---- Host: players, impacts, soil ----

    private void OnPlayerSpawned(PlayerController player)
    {
        if (_vitals.TryGetValue(player.PeerId, out var shown))
            player.Dead = shown.Dead;
        if (!Session.IsHost || Actions is null)
            return;
        player.Landed += speed => Actions.Landed(player.PeerId, speed);
        Actions.Join(player.PeerId);
    }

    private void OnPlayerDespawning(PlayerController player)
    {
        if (Session.IsHost)
            Actions?.Leave(player.PeerId);
        _vitals.Remove(player.PeerId);
    }

    // A Body whose velocity changed by more than gravity and steering can in one tick hit something.
    private void TrackImpacts()
    {
        var seen = new HashSet<ulong>();
        foreach (var creation in Creations.All.ToList())
        {
            if (Creations.NodeOf(creation.Id) is not { IsFrozen: false } node)
                continue;
            foreach (var body in node.Bodies.ToList())
            {
                var id = body.GetInstanceId();
                seen.Add(id);
                var velocity = body.LinearVelocity;
                if (_lastVelocity.TryGetValue(id, out var last) && !_pushed.Contains(id)
                    && (velocity - last).Length() >= Settings!.Impact.MinSpeed && GodotObject.IsInstanceValid(body))
                {
                    node.SyncToCreation();
                    Actions!.Impact(creation, body.BodyIndex, (velocity - last).ToNumerics());
                }
                if (GodotObject.IsInstanceValid(body))
                    _lastVelocity[id] = velocity;
            }
        }
        foreach (var gone in _lastVelocity.Keys.Where(k => !seen.Contains(k)).ToList())
            _lastVelocity.Remove(gone);
        _pushed.Clear();
    }

    private void OnBodyPushed(CreationBody body) => _pushed.Add(body.GetInstanceId());

    private void OnPartBreaking(Creation creation, int partId) => Actions?.Spill(creation, partId);

    private void SendSoil()
    {
        var live = new HashSet<(int, int)>();
        foreach (var creation in Creations.All)
        {
            foreach (var (part, soil) in creation.ActiveSoil)
            {
                var key = (creation.Id, part);
                live.Add(key);
                if (_soilSent.TryGetValue(key, out var sent) && sent == soil.Revision)
                    continue;
                _soilSent[key] = soil.Revision;
                Broadcast(MethodName.ShowSoil, Json(Actions!.SoilViewOf(creation, part)));
            }
        }
        // Soil that went empty (harvested and dry) or is gone: show it bare.
        foreach (var key in _soilSent.Keys.Where(k => !live.Contains(k)).ToList())
        {
            _soilSent.Remove(key);
            Broadcast(MethodName.ShowSoil, Json(new SoilView(key.Item1, key.Item2, null, null, 0, 0, 0)));
        }
    }

    private void OnVitalsChanged(int peer)
    {
        if (Actions!.View(peer) is { } view)
            Broadcast(MethodName.ShowVitals, Json(view));
    }

    private void OnDropChanged(ItemDrop drop) => Broadcast(MethodName.ShowDrop, Json(drop));

    private void OnDropRemoved(int id)
    {
        HideDrop(id);
        if (Session.State == NetSession.SessionState.Hosting)
        {
            foreach (var peer in Session.ReadyPeers)
                RpcId(peer, MethodName.HideDrop, id);
        }
    }

    // Shows on this peer, and sends to every ready client.
    private void Broadcast(StringName method, string json)
    {
        Call(method, json);
        if (Session.State != NetSession.SessionState.Hosting)
            return;
        foreach (var peer in Session.ReadyPeers)
            RpcId(peer, method, json);
    }

    private void SendAllTo(int peer)
    {
        if (Actions is null)
            return;
        foreach (var player in Actions.Players)
        {
            if (Actions.View(player) is { } view)
                RpcId(peer, MethodName.ShowVitals, Json(view));
        }
        foreach (var drop in Actions.Drops.Values)
            RpcId(peer, MethodName.ShowDrop, Json(drop));
        foreach (var creation in Creations.All)
        {
            foreach (var (part, _) in creation.ActiveSoil)
                RpcId(peer, MethodName.ShowSoil, Json(Actions.SoilViewOf(creation, part)));
        }
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client shows only what its host sends; the host keeps its own.
        if (state != NetSession.SessionState.Connecting)
            return;
        _vitals.Clear();
        _vitalsAt.Clear();
        foreach (var id in _drops.Keys.ToList())
            HideDrop(id);
        foreach (var key in _soil.Keys.ToList())
            ShowSoil(Json(new SoilView(key.Creation, key.Part, null, null, 0, 0, 0)));
    }

    // ---- Shown on every peer ----

    /// <summary>Host → client: a player's health.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowVitals(string json)
    {
        if (Read<VitalsView>(json) is not { } view)
            return;
        _vitals[view.Peer] = view;
        _vitalsAt[view.Peer] = Now;
        if (Players.Players.TryGetValue(view.Peer, out var player) && player.Dead != view.Dead)
            player.Dead = view.Dead;
        VitalsChanged?.Invoke(view);
    }

    /// <summary>Host → client: a pile of items appeared or changed.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowDrop(string json)
    {
        if (Read<ItemDrop>(json) is not { } drop)
            return;
        _drops[drop.Id] = drop;
        if (!_dropNodes.TryGetValue(drop.Id, out var node))
        {
            node = _dropNodes[drop.Id] = new DropPile { Name = $"Drop{drop.Id}" };
            AddChild(node);
        }
        node.Show(drop, Catalog);
    }

    /// <summary>Host → client: a pile of items is gone.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void HideDrop(int id)
    {
        _drops.Remove(id);
        if (_dropNodes.Remove(id, out var node))
        {
            RemoveChild(node);
            node.QueueFree();
        }
    }

    /// <summary>Host → client: what grows on a soil part.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowSoil(string json)
    {
        if (Read<SoilView>(json) is not { } view)
            return;
        var key = (view.Creation, view.Part);
        if (view.Seed is null && view.Water <= 0)
            _soil.Remove(key);
        else
            _soil[key] = view;
        if (view.Seed is null)
        {
            if (_cropNodes.Remove(key, out var gone))
            {
                RemoveChild(gone);
                gone.QueueFree();
            }
            return;
        }
        if (!_cropNodes.TryGetValue(key, out var crop))
        {
            crop = _cropNodes[key] = new CropNode { Name = $"Crop{view.Creation}_{view.Part}" };
            AddChild(crop);
        }
        crop.Show(view, Catalog);
        crop.Follow(Creations.NodeOf(view.Creation), view.Part);
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, ActionJson.Options);

    private static T? Read<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, ActionJson.Options);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"[survival] unreadable {typeof(T).Name} from the host: {e.Message}");
            return null;
        }
    }
}
