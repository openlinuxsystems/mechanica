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
/// each soil part whose crop or water changed, and every projectile item scripts launch (chunk 20b), which
/// each peer then flies along its arc itself. A peer that becomes ready is sent all of it.
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
    private readonly Dictionary<int, (ProjectileView View, double ShownAt, Node3D Node)> _projectiles = [];
    // This peer's own item counters (a weapon's rounds loaded), by item, with when each arrived.
    private readonly Dictionary<string, (Mechanica.Scripting.ItemCounterView View, double At)> _counters = [];
    private int _tick;

    /// <summary>The session updates travel over. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The Creations this peer shows. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Part models, for items lying on the ground that have one (a bucket put down). Null shows crates.</summary>
    public PartAssets? Assets { get; init; }

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

    /// <summary>How hard the world is when this peer hosts it.</summary>
    public Difficulty Difficulty { get; init; } = Difficulty.Normal;

    /// <summary>Whether players can hurt each other when this peer hosts the world (the host may change it later).</summary>
    public bool FriendlyFire { get; init; }

    /// <summary>The numbers; null loads <see cref="SettingsPath"/>.</summary>
    public SurvivalSettings? Settings { get; set; }

    /// <summary>The survival actions, once registered (the host's health, beds and piles live here).</summary>
    public SurvivalActions? Actions { get; private set; }

    /// <summary>Every player's health as last sent, by peer.</summary>
    public IReadOnlyDictionary<int, VitalsView> Vitals => _vitals;

    /// <summary>This peer's own health as last sent, or null before it arrives.</summary>
    public VitalsView? Own => _vitals.GetValueOrDefault(Session.LocalPeer);

    /// <summary>Waypoints at the knockout bags this peer's player left behind, for the compass and maps.</summary>
    public IEnumerable<Core.Ui.Waypoint> OwnKnockoutBags() =>
        KnockoutBags.WaypointsFor(_drops.Values, Session.LocalPeer, Ui.Loc.T("Knockout bag"));

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

    /// <summary>The projectiles this peer shows in flight, by id (tests look at them).</summary>
    public IReadOnlyDictionary<int, Node3D> ProjectileNodes => _projectiles.ToDictionary(p => p.Key, p => p.Value.Node);

    /// <summary>
    /// What this peer's own item shows on its hotbar slot and the HUD counter (a weapon's rounds loaded, UI spec §10),
    /// as its script last set it, or null.
    /// </summary>
    public Mechanica.Scripting.ItemCounterView? CounterOf(string item) => _counters.TryGetValue(item, out var c) ? c.View : null;

    /// <summary>Seconds this peer's <paramref name="item"/> is still busy for (reloading), counting down since its counter arrived.</summary>
    public float BusyFor(string item) =>
        _counters.TryGetValue(item, out var c) ? Math.Max(0f, c.View.Busy - (float)(Now - c.At)) : 0f;

    /// <summary>
    /// Host: sends <paramref name="peer"/> what their item's counter shows (from <c>item.counter</c>). Only the holder
    /// is told; the host's own player is shown it here.
    /// </summary>
    public void SendCounter(int peer, Mechanica.Scripting.ItemCounterView view)
    {
        if (peer == Session.LocalPeer)
            ShowCounter(Json(view));
        else if (Session.State == NetSession.SessionState.Hosting && Session.ReadyPeers.Contains(peer))
            RpcId(peer, MethodName.ShowCounter, Json(view));
    }

    /// <summary>Raised on this peer when a player's health arrives or changes.</summary>
    public event Action<VitalsView>? VitalsChanged;

    /// <summary>Raised on this peer when a blow rocks a player who lives through it (they flinch, stagger or go down).</summary>
    public event Action<PlayerHitView>? PlayerHit;

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
    }

    /// <summary>
    /// Reads <see cref="SettingsPath"/> with the game rules of <paramref name="mods"/> on top (their
    /// <c>rules/survival.patch.json</c>), failing loudly on bad engine data.
    /// </summary>
    public static SurvivalSettings LoadSettings(Core.Data.ModLoadResult? mods = null) =>
        Mechanica.Game.Mods.GameRules.Load(SettingsPath, "survival", mods, SurvivalSettings.FromJson);

    /// <summary>
    /// Registers the survival actions and makes dead players unable to act (<see cref="ActionDispatcher.Gate"/>).
    /// Call after the inventory actions, and before <see cref="BuildSync.RegisterActions"/> so building is paid for.
    /// </summary>
    public void RegisterActions(ActionDispatcher dispatcher)
    {
        Settings ??= LoadSettings();
        Actions = new SurvivalActions(Catalog, Settings, Inventory.Items!, this) { Mode = Mode, Difficulty = Difficulty, FriendlyFire = FriendlyFire };
        Actions.Register(dispatcher);
        dispatcher.Gate = Actions.Gate;
        Actions.VitalsChanged += OnVitalsChanged;
        Actions.DropChanged += OnDropChanged;
        Actions.DropRemoved += OnDropRemoved;
        Actions.ProjectileLaunched += view => Broadcast(MethodName.ShowProjectile, Json(view));
        Actions.PlayerHit += view => Broadcast(MethodName.ShowHit, Json(view));
        Actions.ProjectileLanded += OnProjectileLanded;
        Build.Costs = Actions;
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateSpeedScales();
        if (!Session.IsHost || Actions is null)
            return;
        Actions.Tick((float)delta);
        if (++_tick % SoilInterval == 0)
            SendSoil();
    }

    public override void _Process(double delta)
    {
        foreach (var (key, crop) in _cropNodes)
            crop.Follow(Creations.NodeOf(key.Creation), key.Part);
        foreach (var (id, (view, shownAt, node)) in _projectiles.ToList())
        {
            // The host ends a flight when it lands; a lost message only lets one fly out its lifetime.
            var t = view.Age + (float)(Now - shownAt);
            if (t > view.Lifetime + 1f)
            {
                HideProjectile(id);
                continue;
            }
            t = Math.Min(t, view.Lifetime);
            node.Position = view.From.ToGodot() + view.Velocity.ToGodot() * t + view.Gravity.ToGodot() * (0.5f * t * t);
        }
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
    public void Knock(int peer, System.Numerics.Vector3 velocity) => Players.HostKnock(peer, velocity.ToGodot());

    /// <inheritdoc/>
    public RayHit? Cast(System.Numerics.Vector3 from, System.Numerics.Vector3 direction, float range, int ignorePlayer)
    {
        var start = from.ToGodot();
        return Players.Players.TryGetValue(ignorePlayer, out var shooter)
            ? CastBetween(start, start + direction.ToGodot().Normalized() * range, [shooter.GetRid()])
            : CastBetween(start, start + direction.ToGodot().Normalized() * range, []);
    }

    /// <summary>What a ray from <paramref name="start"/> to <paramref name="end"/> hits first, skipping <paramref name="exclude"/>; null when clear.</summary>
    public RayHit? CastBetween(Vector3 start, Vector3 end, Godot.Collections.Array<Rid> exclude)
    {
        var query = PhysicsRayQueryParameters3D.Create(start, end, Mechanica.Game.World.PhysicsLayers.Queries);
        query.Exclude = exclude;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return null;
        var point = hit["position"].AsVector3();
        var normal = hit["normal"].AsVector3();
        switch (hit["collider"].AsGodotObject())
        {
            case PlayerController player:
                return new RayHit(point.ToNumerics(), Player: player.PeerId);
            case Bots.BotBody bot:
                return new RayHit(point.ToNumerics(), Bot: bot.BotId);
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
        var query = PhysicsRayQueryParameters3D.Create(start, start + new Vector3(0, -200f, 0), Mechanica.Game.World.PhysicsLayers.Queries);
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
    public PoiPoolPlacement? LiquidAt(System.Numerics.Vector3 point) => World.Generator.LiquidAt(point);

    /// <inheritdoc/>
    public WorldObjectPlacement? FindObject(WorldObjectId id) =>
        World.State.IsDestroyed(id) ? null : World.Generator.Find(id);

    /// <inheritdoc/>
    public LootTableDefinition? FindLootTable(string id) => World.Generator.Data.Definition.LootTables?.GetValueOrDefault(id);

    /// <inheritdoc/>
    public IEnumerable<WorldObjectPlacement> ObjectsNear(System.Numerics.Vector3 point, float radius) =>
        // Widened by the largest footprint: an outcrop's cubes stand away from its ground point, which is what ObjectsIn looks at.
        World.Generator.ObjectsIn(point.X - radius - World.Generator.Data.MaxFootprint, point.Z - radius - World.Generator.Data.MaxFootprint,
                point.X + radius + World.Generator.Data.MaxFootprint, point.Z + radius + World.Generator.Data.MaxFootprint)
            .Where(o => !World.State.IsDestroyed(o.Id) && System.Numerics.Vector3.Distance(o.Position, point) <= radius);

    /// <inheritdoc/>
    public float AddObjectDamage(WorldObjectId id, float damage) => World.State.AddDamage(id, damage);

    /// <inheritdoc/>
    public void DestroyObject(WorldObjectId id) => World.DestroyObject(id);

    /// <inheritdoc/>
    public void PlaySound(string sound, string ownerModId, System.Numerics.Vector3 position) => Build.PlaySound(sound, ownerModId, position);

    /// <inheritdoc/>
    public SledgeResult DamagePart(Creation creation, int partId, float damage) => Build.Tools!.DamagePart(creation, partId, damage);

    /// <inheritdoc/>
    public void Push(Creation creation, int bodyIndex, System.Numerics.Vector3 point, System.Numerics.Vector3 impulse) =>
        Build.Push(creation, bodyIndex, point, impulse);

    /// <inheritdoc/>
    public void RemovePart(Creation creation, int partId) => Build.Tools!.RemovePart(creation, partId);

    /// <inheritdoc/>
    public void Shatter(BreakDefinition breaks, System.Numerics.Vector3 centre, float radius, uint seed) => Build.Tools!.Shatter(breaks, centre, radius, seed);

    /// <inheritdoc/>
    public void Fell(BreakDefinition breaks, System.Numerics.Vector3 foot, float height, System.Numerics.Vector3 direction, float spin, uint seed) =>
        Build.Tools!.Fell(breaks, foot, height, direction, spin, seed);

    /// <inheritdoc/>
    public void ObjectHit(WorldObjectId id, System.Numerics.Vector3 at, bool broke) => World.ObjectHit(id, at.ToGodot(), broke);

    /// <inheritdoc/>
    public void Impact(System.Numerics.Vector3 at, string material) => World.Impact(at.ToGodot(), material);

    // Slowed players move slower: the host from its own state, a client from the vitals it was sent.
    private void UpdateSpeedScales()
    {
        foreach (var (peer, player) in Players.Players)
        {
            if (Session.IsHost && Actions is not null)
                player.SpeedScale = Actions.SpeedScale(peer);
            else if (_vitals.TryGetValue(peer, out var view) && view.SlowFor > Now - _vitalsAt.GetValueOrDefault(peer))
                player.SpeedScale = view.Slow;
            else
                player.SpeedScale = 1f;
        }
    }

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

    private void OnProjectileLanded(ProjectileLanding landing)
    {
        HideProjectile(landing.Id);
        if (Session.State == NetSession.SessionState.Hosting)
        {
            foreach (var peer in Session.ReadyPeers)
                RpcId(peer, MethodName.HideProjectile, landing.Id);
        }
    }

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
        foreach (var projectile in Actions.Projectiles)
            RpcId(peer, MethodName.ShowProjectile, Json(projectile));
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
        _counters.Clear();
        foreach (var id in _drops.Keys.ToList())
            HideDrop(id);
        foreach (var id in _projectiles.Keys.ToList())
            HideProjectile(id);
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

    /// <summary>Host → client: a blow rocked a player.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowHit(string json)
    {
        if (Read<PlayerHitView>(json) is { } view)
            PlayerHit?.Invoke(view);
    }

    /// <summary>Host → the item's holder: what their item's counter shows.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowCounter(string json)
    {
        if (Read<Mechanica.Scripting.ItemCounterView>(json) is { } view)
            _counters[view.Item] = (view, Now);
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
        node.Show(drop, Catalog, Assets, Settings?.KnockoutBag);
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

    /// <summary>Host → client: a projectile was launched; this peer flies it along its arc.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowProjectile(string json)
    {
        if (Read<ProjectileView>(json) is not { } view)
            return;
        HideProjectile(view.Id);
        Node3D node = view.Look == ProjectileView.Water ? WaterEffects.Blob(view.Size ?? WaterEffects.DefaultRadius) : new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f } };
        node.Name = $"Projectile{view.Id}";
        node.Position = view.From.ToGodot();
        _projectiles[view.Id] = (view, Now, node);
        AddChild(node);
    }

    /// <summary>Host → client: a projectile landed.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void HideProjectile(int id)
    {
        if (_projectiles.Remove(id, out var shown))
        {
            if (shown.View.Look == ProjectileView.Water)
                WaterEffects.Splash(this, shown.Node.Position, shown.View.Size ?? WaterEffects.DefaultRadius);
            RemoveChild(shown.Node);
            shown.Node.QueueFree();
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
                gone.Remove(harvested: gone.View?.Ripe == true);
            return;
        }
        if (!_cropNodes.TryGetValue(key, out var crop))
        {
            crop = _cropNodes[key] = new CropNode { Name = $"Crop{view.Creation}_{view.Part}" };
            AddChild(crop);
        }
        crop.Show(view, Catalog, Assets);
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
