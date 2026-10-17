using Godot;
using FileAccess = Godot.FileAccess;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Building;

/// <summary>
/// Building over the network (D10). On the host it is the <see cref="IBuildWorld"/> behind the
/// <c>PlacePart</c>/<c>RemovePart</c> actions: it answers reach and clearance questions from the live
/// scene, rebuilds Creation nodes after each graph change and sends the changed Creation to every
/// client. A late joiner gets every Creation when it becomes ready. Clients only mirror what arrives.
/// <para>
/// Each update carries the whole Creation as save JSON (graph plus Body poses), which keeps clients
/// exact without a delta format. Moving bodies between changes is physics state sync's job (chunk 4b).
/// </para>
/// </summary>
public partial class BuildSync : Node, IBuildWorld
{
    /// <summary>Where the building limits are read from.</summary>
    public const string SettingsPath = "res://data/building.json";

    private int _nextCreationId = 1;
    private bool _wasConnected;

    /// <summary>The session updates travel over. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players, for reach. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The Creations this peer shows. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Part definitions (the loaded mods). Set before adding to the tree.</summary>
    public ModLoadResult Catalog { get; init; } = null!;

    /// <summary>Meshes and shapes for the parts. Set before adding to the tree.</summary>
    public PartAssets Assets { get; init; } = null!;

    /// <summary>Building limits; null loads <see cref="SettingsPath"/>.</summary>
    public BuildSettings? Settings { get; set; }

    public override void _Ready()
    {
        Settings ??= LoadSettings();
        Session.PeerReady += SendAllTo;
        Session.StateChanged += OnStateChanged;
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= SendAllTo;
        Session.StateChanged -= OnStateChanged;
    }

    /// <summary>Registers <c>PlacePart</c> and <c>RemovePart</c>.</summary>
    public void RegisterActions(Core.Actions.ActionDispatcher dispatcher)
    {
        Settings ??= LoadSettings();
        new BuildActions(Catalog, Settings, this).Register(dispatcher);
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
        // Validation measures reach from where the bodies are now, not where they were last saved.
        Creations.NodeOf(id)?.SyncToCreation();
        return Creations.Find(id);
    }

    /// <inheritdoc/>
    public System.Numerics.Vector3? EyeOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) ? player.Head.GlobalPosition.ToNumerics() : null;

    /// <inheritdoc/>
    public int NextCreationId() => _nextCreationId++;

    /// <inheritdoc/>
    public string? CheckClear(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose)
    {
        var size = (definition.Size ?? GridSize.One).Metres.ToGodot() * Settings!.ClearanceScale;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = size },
            Transform = pose.ToTransform()
                * new Transform3D(rotation.ToBasis(), PartGeometry.CentreMetres(definition, cell, rotation).ToGodot()),
            CollideWithBodies = true,
        };
        if (target is not null && Creations.NodeOf(target.Id) is { } own)
            query.Exclude = [.. own.Bodies.Select(b => b.GetRid())];
        var hits = GetViewport().FindWorld3D().DirectSpaceState.IntersectShape(query, 1);
        if (hits.Count == 0)
            return null;
        return hits[0]["collider"].AsGodotObject() switch
        {
            PlayerController => "A player is in the way.",
            CreationBody body when body.GetParent() is CreationNode node => $"Creation {node.Creation.Id} is in the way.",
            _ => "Something is in the way.",
        };
    }

    /// <inheritdoc/>
    public void Added(Creation creation)
    {
        Creations.Add(creation, Assets);
        Broadcast(creation);
    }

    /// <inheritdoc/>
    public void Changed(Creation creation)
    {
        Creations.NodeOf(creation.Id)!.Rebuild();
        Broadcast(creation);
    }

    /// <inheritdoc/>
    public void Removed(int creationId)
    {
        Creations.Remove(creationId);
        foreach (var peer in Session.ReadyPeers)
            RpcId(peer, MethodName.ClientRemoveCreation, creationId);
    }

    /// <summary>Host → client: a Creation as it is now (save JSON).</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientSetCreation(string json)
    {
        if (Session.IsHost)
            return;
        var diagnostics = new List<DataDiagnostic>();
        if (CreationJson.Read(json, Catalog, "host", diagnostics) is not { } creation)
        {
            GD.PushError($"[build] the host sent a Creation this client cannot read: {string.Join("; ", diagnostics)}");
            return;
        }
        Creations.Replace(creation, Assets);
    }

    /// <summary>Host → client: a Creation is gone.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientRemoveCreation(int id)
    {
        if (!Session.IsHost)
            Creations.Remove(id);
    }

    private void Broadcast(Creation creation)
    {
        if (Session.ReadyPeers.Count == 0)
            return;
        var json = Write(creation);
        foreach (var peer in Session.ReadyPeers)
            RpcId(peer, MethodName.ClientSetCreation, json);
    }

    private void SendAllTo(int peer)
    {
        foreach (var creation in Creations.All)
            RpcId(peer, MethodName.ClientSetCreation, Write(creation));
    }

    private string Write(Creation creation)
    {
        Creations.NodeOf(creation.Id)?.SyncToCreation();
        return CreationJson.Write(creation);
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client shows the host's world only while connected to it; the host keeps its own.
        if (state == NetSession.SessionState.Connecting || (state == NetSession.SessionState.Offline && _wasConnected))
            Creations.Clear();
        _wasConnected = state == NetSession.SessionState.Connected;
    }
}
