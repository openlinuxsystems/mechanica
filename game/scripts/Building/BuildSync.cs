using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Building;

/// <summary>
/// Building over the network (D10). On the host it is the <see cref="IToolWorld"/> behind the Hammer's
/// <c>PlacePart</c>/<c>RemovePart</c> and the other hand tools' actions (<see cref="ToolActions"/>): it
/// answers reach and clearance questions from the live scene and applies each change to the host's
/// <see cref="CreationWorld"/>. It also shows every player's Lift and sends Lift changes to every
/// client, and all Lifts to a peer when it becomes ready.
/// It also runs <c>SpawnPart</c> and <c>ClearCreations</c> (<see cref="CreationActions"/>), which the Test
/// Sandbox uses, and is the part catalog for the building actions, so <see cref="SetContent"/> can swap mods.
/// The actions' sounds go to <see cref="Sounds"/>, which sends them to the clients in earshot.
/// <para>
/// Clients get the result from <see cref="CreationSync"/>: a changed graph bumps
/// <see cref="Creation.Revision"/>, so every peer near the Creation is sent it whole again (save JSON,
/// graph plus Body poses and velocities), and a removed one is forgotten. Late joiners and interest
/// management come with it. Paint and a Lift picking up or letting go bump the revision too; a held
/// Creation's moves reach clients as ordinary snapshots.
/// </para>
/// </summary>
public partial class BuildSync : Node, IToolWorld, IPartCatalog
{
    /// <summary>Where the building limits are read from.</summary>
    public const string SettingsPath = "res://data/building.json";

    private readonly Dictionary<int, LiftBody> _lifts = [];
    private Node3D _liftRoot = null!;
    private ActionDispatcher? _dispatcher;
    private int _nextCreationId = 1;
    private bool _wasConnected;

    /// <summary>The session updates travel over. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players, for reach. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The Creations this peer shows. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Part definitions (the loaded mods). Set before adding to the tree; <see cref="SetContent"/> swaps it.</summary>
    public ModLoadResult Catalog { get; set; } = null!;

    /// <summary>Meshes and shapes for the parts. Set before adding to the tree.</summary>
    public PartAssets Assets { get; init; } = null!;

    /// <summary>Where the building and tool actions' sounds play (host side); null plays none.</summary>
    public Sound.SoundSync? Sounds { get; init; }

    /// <summary>Building limits; null loads <see cref="SettingsPath"/>.</summary>
    public BuildSettings? Settings { get; set; }

    /// <summary>What building costs (Survival pays with items). Set before <see cref="RegisterActions"/>.</summary>
    public IBuildCosts? Costs { get; set; }

    /// <summary>Raised on the host just before a part breaks off, so what it holds can fall out.</summary>
    public event Action<Creation, int>? PartBreaking;

    /// <summary>Raised on the host when a tool, a weapon or a script pushes a Body (so it is not taken for a crash).</summary>
    public event Action<CreationBody>? BodyPushed;

    /// <summary>The tool actions, once registered (host-side Lift state lives here).</summary>
    public ToolActions? Tools { get; private set; }

    /// <summary>The Lifts this peer shows, by owner peer.</summary>
    public IReadOnlyDictionary<int, LiftBody> Lifts => _lifts;

    /// <inheritdoc/>
    public double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready()
    {
        Settings ??= LoadSettings();
        _liftRoot = new Node3D { Name = "Lifts" };
        AddChild(_liftRoot);
        Session.PeerReady += SendLiftsTo;
        Session.PeerLeft += OnPeerLeft;
        Session.StateChanged += OnStateChanged;
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= SendLiftsTo;
        Session.PeerLeft -= OnPeerLeft;
        Session.StateChanged -= OnStateChanged;
    }

    /// <summary>
    /// Uses <paramref name="mods"/> from now on, as the Test Sandbox does after the Part Editor exports.
    /// Creations already in the world keep the parts they were built with.
    /// </summary>
    public void SetContent(ModLoadResult mods) => Catalog = mods;

    /// <inheritdoc/>
    public PartDefinition? FindDefinition(string id) => Catalog.FindDefinition(id);

    /// <summary>
    /// Registers <c>PlacePart</c>, <c>RemovePart</c>, the tool actions, <c>SpawnPart</c>
    /// (<see cref="SpawnPartArgs"/>; returns the new Creation's id) and <c>ClearCreations</c> (no arguments).
    /// </summary>
    public void RegisterActions(ActionDispatcher dispatcher)
    {
        Settings ??= LoadSettings();
        new BuildActions(this, Settings, this) { Costs = Costs }.Register(dispatcher);
        Tools = new ToolActions(Settings, this, this) { Costs = Costs };
        Tools.Register(dispatcher);
        _dispatcher = dispatcher;
        dispatcher.Register<SpawnPartArgs>(CreationActions.SpawnPart,
            (_, args) => CreationActions.ValidateSpawn(args, this, Creations.All.Count()),
            (_, args) =>
            {
                var creation = CreationActions.BuildSpawn(args, NextCreationId(), this);
                Added(creation);
                return System.Text.Json.JsonSerializer.SerializeToElement(creation.Id);
            });
        dispatcher.Register<NoArgs>(CreationActions.ClearCreations,
            (_, _) => null,
            (_, _) =>
            {
                foreach (var id in Creations.All.Select(c => c.Id).ToList())
                    Removed(id);
                return null;
            });
    }

    /// <summary>Reads <see cref="SettingsPath"/>, failing loudly on bad data.</summary>
    public static BuildSettings LoadSettings()
    {
        using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {SettingsPath}: {FileAccess.GetOpenError()}");
        return BuildSettings.FromJson(file.GetAsText());
    }

    /// <inheritdoc/>
    public Creation? FindCreation(int id)
    {
        // Validation measures reach from where the bodies are now, not where they were last saved
        // (a parked Creation's poses were written back when it parked).
        Creations.NodeOf(id)?.SyncToCreation();
        return Creations.Find(id);
    }

    /// <inheritdoc/>
    public System.Numerics.Vector3? EyeOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) ? player.Head.GlobalPosition.ToNumerics() : null;

    /// <inheritdoc/>
    public int NextCreationId()
    {
        // Creations can also arrive from elsewhere (a save, a test), so never reuse an id in the world.
        _nextCreationId = Math.Max(_nextCreationId, (Creations.All.LastOrDefault()?.Id ?? 0) + 1);
        return _nextCreationId++;
    }

    /// <inheritdoc/>
    public string? CheckClear(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose) =>
        CheckPartClear(definition, cell, rotation, pose, target is null ? [] : [target.Id], null);

    /// <inheritdoc/>
    public string? CheckPartClear(PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose,
        IReadOnlyCollection<int> ignoreCreations, int? ignoreLiftOf)
    {
        var size = (definition.Size ?? GridSize.One).Metres.ToGodot() * Settings!.ClearanceScale;
        var transform = pose.ToTransform()
            * new Transform3D(rotation.ToBasis(), PartGeometry.CentreMetres(definition, cell, rotation).ToGodot());
        return Overlap(new BoxShape3D { Size = size }, transform, ignoreCreations, ignoreLiftOf);
    }

    /// <inheritdoc/>
    public string? CheckLiftClear(LiftState lift, IReadOnlyCollection<int> ignoreCreations)
    {
        var height = Settings!.Lift.TopAt(lift.Level);
        var size = new Vector3(Settings.Lift.PlatformSize, height, Settings.Lift.PlatformSize) * Settings.ClearanceScale;
        var transform = new Transform3D(Basis.Identity, lift.Position.ToGodot() + new Vector3(0, height / 2, 0));
        return Overlap(new BoxShape3D { Size = size }, transform, ignoreCreations, lift.Owner);
    }

    // What overlaps a shape, apart from the given Creations' bodies and Lift, as a reason; null when nothing does.
    private string? Overlap(Shape3D shape, Transform3D transform, IReadOnlyCollection<int> ignoreCreations, int? ignoreLiftOf)
    {
        var query = new PhysicsShapeQueryParameters3D { Shape = shape, Transform = transform, CollideWithBodies = true };
        var exclude = new Godot.Collections.Array<Rid>();
        foreach (var id in ignoreCreations)
        {
            foreach (var body in Creations.NodeOf(id)?.Bodies ?? [])
                exclude.Add(body.GetRid());
        }
        if (ignoreLiftOf is { } owner && _lifts.TryGetValue(owner, out var own))
            exclude.Add(own.GetRid());
        query.Exclude = exclude;
        var hits = GetViewport().FindWorld3D().DirectSpaceState.IntersectShape(query, 1);
        if (hits.Count == 0)
            return null;
        return hits[0]["collider"].AsGodotObject() switch
        {
            PlayerController => "A player is in the way.",
            CreationBody body when body.GetParent() is CreationNode node => $"Creation {node.Creation.Id} is in the way.",
            LiftBody => "A lift is in the way.",
            _ => "Something is in the way.",
        };
    }

    /// <inheritdoc/>
    public void Moved(Creation creation) => Creations.Moved(creation.Id);

    /// <inheritdoc/>
    public void LiftChanged(int owner, LiftState? lift)
    {
        ShowLift(owner, lift);
        var json = lift is null ? "" : JsonSerializer.Serialize(lift, ActionJson.Options);
        foreach (var peer in Session.ReadyPeers)
            RpcId(peer, MethodName.ClientSetLift, owner, json);
    }

    /// <inheritdoc/>
    public void Push(Creation creation, int bodyIndex, System.Numerics.Vector3 point, System.Numerics.Vector3 impulse)
    {
        if (Creations.NodeOf(creation.Id) is not { IsFrozen: false } node || bodyIndex >= node.Bodies.Count)
            return;
        var body = node.Bodies[bodyIndex];
        body.Sleeping = false;
        BodyPushed?.Invoke(body);
        body.ApplyImpulse(impulse.ToGodot(), point.ToGodot() - body.GlobalPosition);
    }

    /// <inheritdoc/>
    public void Breaking(Creation creation, int partId) => PartBreaking?.Invoke(creation, partId);

    /// <inheritdoc />
    public void PlaySound(string sound, string ownerModId, System.Numerics.Vector3 position) =>
        Sounds?.Emit(sound, ownerModId, position.ToGodot());

    /// <inheritdoc/>
    public void Added(Creation creation) => Creations.Add(creation, Assets);

    /// <inheritdoc/>
    public void Changed(Creation creation) => Creations.Rebuild(creation.Id);

    /// <inheritdoc/>
    public void Removed(int creationId) => Creations.Remove(creationId);

    /// <summary>Host → client: a player's Lift as it is now (Action API JSON), or empty when it is gone.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientSetLift(int owner, string json)
    {
        if (Session.IsHost)
            return;
        LiftState? lift = null;
        if (json.Length > 0)
        {
            try
            {
                lift = JsonSerializer.Deserialize<LiftState>(json, ActionJson.Options);
            }
            catch (JsonException e)
            {
                GD.PushError($"[build] the host sent a lift this client cannot read: {e.Message}");
                return;
            }
        }
        ShowLift(owner, lift);
    }

    private void SendLiftsTo(int peer)
    {
        foreach (var lift in Tools?.Lifts ?? [])
            RpcId(peer, MethodName.ClientSetLift, lift.Owner, JsonSerializer.Serialize(lift, ActionJson.Options));
    }

    private void ShowLift(int owner, LiftState? lift)
    {
        if (lift is null)
        {
            if (_lifts.Remove(owner, out var gone))
            {
                _liftRoot.RemoveChild(gone);
                gone.QueueFree();
            }
            return;
        }
        if (!_lifts.TryGetValue(owner, out var body))
        {
            body = _lifts[owner] = new LiftBody { Name = $"Lift{owner}", Assets = Assets, FindCreation = Creations.NodeOf, Sounds = Sounds };
            _liftRoot.AddChild(body);
        }
        body.Show(lift, Settings!.Lift);
    }

    // A player who leaves takes their Lift with them; what it held drops.
    private void OnPeerLeft(int peer)
    {
        if (Session.IsHost && Tools?.LiftOf(peer) is not null)
            _dispatcher?.Dispatch(new ActionCaller(peer, ActionSource.Player), ToolActions.RemoveLift, "{}");
        Tools?.Forget(peer);
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client shows the host's world only while connected to it; the host keeps its own.
        if (state == NetSession.SessionState.Connecting || (state == NetSession.SessionState.Offline && _wasConnected))
        {
            Creations.Clear();
            foreach (var owner in _lifts.Keys.ToList())
                ShowLift(owner, null);
        }
        _wasConnected = state == NetSession.SessionState.Connected;
    }
}
