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
/// Building over the network (D10). On the host it is the <see cref="IBuildWorld"/> behind the
/// <c>PlacePart</c>/<c>RemovePart</c> actions: it answers reach and clearance questions from the live
/// scene and applies each graph change to the host's <see cref="CreationWorld"/>.
/// It also runs <c>SpawnPart</c> and <c>ClearCreations</c> (<see cref="CreationActions"/>), which the Test
/// Sandbox uses, and is the part catalog for all four actions, so <see cref="SetContent"/> can swap mods.
/// <para>
/// Clients get the result from <see cref="CreationSync"/>: a changed graph bumps
/// <see cref="Creation.Revision"/>, so every peer near the Creation is sent it whole again (save JSON,
/// graph plus Body poses and velocities), and a removed one is forgotten. Late joiners and interest
/// management come with it.
/// </para>
/// </summary>
public partial class BuildSync : Node, IBuildWorld, IPartCatalog
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

    /// <summary>Part definitions (the loaded mods). Set before adding to the tree; <see cref="SetContent"/> swaps it.</summary>
    public ModLoadResult Catalog { get; set; } = null!;

    /// <summary>Meshes and shapes for the parts. Set before adding to the tree.</summary>
    public PartAssets Assets { get; init; } = null!;

    /// <summary>Building limits; null loads <see cref="SettingsPath"/>.</summary>
    public BuildSettings? Settings { get; set; }

    public override void _Ready()
    {
        Settings ??= LoadSettings();
        Session.StateChanged += OnStateChanged;
    }

    public override void _ExitTree() => Session.StateChanged -= OnStateChanged;

    /// <summary>
    /// Uses <paramref name="mods"/> from now on, as the Test Sandbox does after the Part Editor exports.
    /// Creations already in the world keep the parts they were built with.
    /// </summary>
    public void SetContent(ModLoadResult mods) => Catalog = mods;

    /// <inheritdoc/>
    public PartDefinition? FindDefinition(string id) => Catalog.FindDefinition(id);

    /// <summary>
    /// Registers <c>PlacePart</c>, <c>RemovePart</c>, <c>SpawnPart</c> (<see cref="SpawnPartArgs"/>; returns
    /// the new Creation's id) and <c>ClearCreations</c> (no arguments).
    /// </summary>
    public void RegisterActions(ActionDispatcher dispatcher)
    {
        Settings ??= LoadSettings();
        new BuildActions(this, Settings, this).Register(dispatcher);
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
    public void Added(Creation creation) => Creations.Add(creation, Assets);

    /// <inheritdoc/>
    public void Changed(Creation creation) => Creations.Rebuild(creation.Id);

    /// <inheritdoc/>
    public void Removed(int creationId) => Creations.Remove(creationId);

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client shows the host's world only while connected to it; the host keeps its own.
        if (state == NetSession.SessionState.Connecting || (state == NetSession.SessionState.Offline && _wasConnected))
            Creations.Clear();
        _wasConnected = state == NetSession.SessionState.Connected;
    }
}
