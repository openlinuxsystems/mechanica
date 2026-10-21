using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Quests;
using Mechanica.Core.World;
using Mechanica.Game.Bots;
using Mechanica.Game.Creations;
using Mechanica.Game.Fire;
using Mechanica.Game.Items;
using Mechanica.Game.Logic;
using Mechanica.Game.Net;
using Mechanica.Game.World;

namespace Mechanica.Game.Quests;

/// <summary>
/// Quests over the network (chunk 24). The host runs the <see cref="QuestTracker"/> against the world: players'
/// feet, the generated POIs' markers, inventories, planted soil, seated speed, raids ending and fires put out. Every change
/// of the story's state is sent to every client as JSON, and so are the lines quests say. Each client keeps a
/// tracker over its copy of the state, only to show what Use (E) would do near a quest marker; the actions run on
/// the host.
/// </summary>
public partial class QuestSync : Node, IQuestWorld
{
    /// <summary>How often (s) the host checks objectives the world shows.</summary>
    public const float TickSeconds = 0.25f;

    private readonly Dictionary<string, List<QuestSite>> _sites = [];
    private WorldGenerator? _sitesOf;
    private float _sinceTick;
    private bool _dirty;
    private string _lastRaidPhase = RaidPhases.None;

    /// <summary>The session. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players, for feet. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Creations, for planted soil and speed. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>The world, for its POIs. Set before adding to the tree.</summary>
    public WorldStreamer World { get; init; } = null!;

    /// <summary>Player inventories, for have_item steps and rewards. Set before adding to the tree.</summary>
    public InventorySync Inventory { get; init; } = null!;

    /// <summary>Seats, for drive steps. Optional.</summary>
    public LogicSync? Logic { get; init; }

    /// <summary>Raids, for raid steps. Optional.</summary>
    public BotSync? Bots { get; init; }

    /// <summary>Fire, for extinguish steps. Optional.</summary>
    public FireSync? Fire { get; init; }

    /// <summary>The quests of the enabled mods.</summary>
    public QuestBook Book { get; init; } = QuestBook.Empty;

    /// <summary>The tracker, once the actions are registered: the host's, or a client's view of it.</summary>
    public QuestTracker Tracker { get; private set; } = null!;

    /// <summary>The story's state this peer knows.</summary>
    public QuestState State => Tracker.State;

    /// <summary>Raised on every peer when the state arrives or changes.</summary>
    public event Action? StateChanged;

    /// <summary>Raised on every peer when a quest says something.</summary>
    public event Action<QuestDialogue>? Said;

    /// <summary>Registers the quest actions (every peer registers; only the host runs them).</summary>
    public void RegisterActions(ActionDispatcher dispatcher)
    {
        Tracker = new QuestTracker(Book, this);
        Tracker.Register(dispatcher);
        Tracker.Changed += () => _dirty = true;
        Tracker.Said += OnSaid;
    }

    public override void _Ready()
    {
        Session.PeerReady += SendStateTo;
        if (Bots?.Raids is { } raids)
            raids.Changed += OnRaidChanged;
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= SendStateTo;
        if (Bots?.Raids is { } raids)
            raids.Changed -= OnRaidChanged;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Session.IsHost)
            return;
        _sinceTick += (float)delta;
        if (_sinceTick >= TickSeconds)
        {
            Tracker.Tick(_sinceTick);
            _sinceTick = 0;
        }
        if (_dirty)
        {
            _dirty = false;
            var json = State.ToJson();
            if (Session.State == NetSession.SessionState.Hosting)
            {
                foreach (var peer in Session.ReadyPeers)
                    RpcId(peer, MethodName.ClientState, json);
            }
            StateChanged?.Invoke();
        }
    }

    /// <summary>Host: replaces the story's state (a loaded save) and sends it.</summary>
    public void Load(QuestState state) => Tracker.Load(state);

    /// <summary>What Use (E) would do near a quest marker for this peer's player now, or null.</summary>
    public QuestInteractResult? Prompt() => Tracker?.Preview(Session.LocalPeer);

    /// <summary>Choose steps of active quests waiting on a choice, with the quest.</summary>
    public IEnumerable<(Quest Quest, QuestStepInfo Step)> PendingChoices() =>
        Book.All.Select(q => (Quest: q, Step: Tracker.CurrentStep(q.QualifiedId)))
            .Where(c => c.Step is { Kind: QuestObjectiveKind.Choose })
            .Select(c => (c.Quest, c.Step!));

    /// <inheritdoc/>
    public IEnumerable<int> Peers => Players.Players.Keys.Order();

    /// <inheritdoc/>
    public System.Numerics.Vector3? FeetOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) && !player.Dead
            ? new System.Numerics.Vector3(player.GlobalPosition.X, player.GlobalPosition.Y, player.GlobalPosition.Z)
            : null;

    /// <inheritdoc/>
    public IReadOnlyList<QuestSite> SitesOf(string poi)
    {
        if (_sitesOf != World.Generator)
        {
            _sitesOf = World.Generator;
            _sites.Clear();
            foreach (var instance in World.Generator.Pois)
            {
                var site = QuestSite.Of(instance);
                if (!_sites.TryGetValue(site.Poi, out var list))
                    _sites[site.Poi] = list = [];
                list.Add(site);
            }
        }
        return _sites.TryGetValue(poi, out var found) ? found : [];
    }

    /// <summary>
    /// Where the compass points for a player at <paramref name="from"/>: the nearest site of each active quest's
    /// current goal (the first quest's first, so it is the default primary), then every place found so far.
    /// </summary>
    public IReadOnlyList<Mechanica.Core.Ui.Waypoint> Waypoints(System.Numerics.Vector3 from)
    {
        var found = new List<Mechanica.Core.Ui.Waypoint>();
        if (Tracker is null)
            return found;
        foreach (var quest in Book.All)
        {
            if (Tracker.CurrentStep(quest.QualifiedId) is not { Poi: { } poi } step)
                continue;
            // A step that names a marker to use or look by points at the nearest one; any other at the POI.
            List<System.Numerics.Vector3> places = step.Kind is QuestObjectiveKind.Interact or QuestObjectiveKind.HaveItem && step.Marker is { } type
                ? [.. SitesOf(poi).SelectMany(s => s.Markers).Where(m => m.Type == type && (step.Ref is null || m.Ref == step.Ref)).Select(m => m.Position)]
                : [];
            if (places.Count == 0)
                places = [.. SitesOf(poi).Select(s => s.Centre)];
            if (places.Count > 0)
                found.Add(new($"quest:{quest.QualifiedId}", quest.Name, places.MinBy(p => System.Numerics.Vector3.DistanceSquared(p, from))));
        }
        foreach (var poi in State.Revealed)
        {
            var name = World.WorldGen.Pois.FirstOrDefault(p => p.QualifiedId == poi)?.Prefab.Name ?? poi;
            foreach (var site in SitesOf(poi))
            {
                if (!found.Any(w => w.Position == site.Centre))
                    found.Add(new($"poi:{poi}:{site.Index}", name, site.Centre));
            }
        }
        return found;
    }

    /// <inheritdoc/>
    public int CountItem(int peer, string item) => Inventory.Items?.PlayerInventory(peer).Count(item) ?? 0;

    /// <inheritdoc/>
    public int GiveItem(int peer, string item, int count) => Inventory.Items?.PlayerInventory(peer).Add(item, count) ?? 0;

    /// <inheritdoc/>
    public bool TakeItem(int peer, string item, int count) =>
        Inventory.Items?.PlayerInventory(peer) is { } inventory && inventory.Count(item) >= count && inventory.Remove(item, count) == count;

    /// <inheritdoc/>
    public string ItemName(string item) => Inventory.Catalog?.FindDefinition(item)?.Name ?? item;

    /// <inheritdoc/>
    public int PlantedCrops() => Creations.All.Sum(c => c.ActiveSoil.Count(s => s.Soil.Seed is not null));

    /// <inheritdoc/>
    public float? SeatedSpeed(int peer) =>
        Logic?.Seats.SeatOf(peer) is { } seat ? Creations.Sample(seat.Creation).LinearSpeed : null;

    /// <inheritdoc/>
    public int FiresOut(string poi, string marker) => Fire?.Actions?.SitesOut(poi, marker) ?? 0;

    private void OnRaidChanged(RaidView raid)
    {
        if (Session.IsHost && raid.Phase == RaidPhases.None && _lastRaidPhase != RaidPhases.None)
            Tracker.RaidEnded();
        _lastRaidPhase = raid.Phase;
    }

    private void OnSaid(QuestDialogue dialogue)
    {
        Said?.Invoke(dialogue);
        if (Session.State != NetSession.SessionState.Hosting)
            return;
        var json = JsonSerializer.Serialize(dialogue, ActionJson.Options);
        foreach (var peer in Session.ReadyPeers)
            RpcId(peer, MethodName.ClientSaid, json);
    }

    private void SendStateTo(int peer)
    {
        if (Session.IsHost)
            RpcId(peer, MethodName.ClientState, State.ToJson());
    }

    /// <summary>Host → client: the story's state.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientState(string json)
    {
        if (Session.IsHost)
            return;
        if (QuestState.FromJson(json) is not { } state)
        {
            GD.PushWarning("[quests] bad quest state from the host");
            return;
        }
        Tracker.Load(state);
        StateChanged?.Invoke();
    }

    /// <summary>Host → client: lines a quest says.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientSaid(string json)
    {
        if (Session.IsHost)
            return;
        try
        {
            if (JsonSerializer.Deserialize<QuestDialogue>(json, ActionJson.Options) is { } dialogue)
                Said?.Invoke(dialogue);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"[quests] bad dialogue from the host: {e.Message}");
        }
    }
}
