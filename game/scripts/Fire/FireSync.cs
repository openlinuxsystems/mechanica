using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Fire;
using Mechanica.Core.Quests;
using Mechanica.Core.World;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Scripting;
using Mechanica.Game.Sound;
using Mechanica.Game.Survival;
using Mechanica.Game.World;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.Fire;

/// <summary>
/// Fire over the network (D13, D10). On the host it runs <see cref="FireActions"/>: it lights the world's
/// POI fires once the world is up, ticks every fire, and answers which parts a fire reaches from the live
/// physics scene (<see cref="IFireWorld"/>). Every peer is sent each fire that starts, changes strength
/// visibly or goes out, and shows it as a <see cref="FireNode"/>; the host also runs each fire's burning sound
/// as a loop every peer hears. A peer that becomes ready is sent every fire.
/// </summary>
public partial class FireSync : Node3D, IFireWorld
{
    /// <summary>Where the fire numbers are read from.</summary>
    public const string SettingsPath = "res://data/fire.json";

    // Strength steps clients are sent; finer changes are not worth a message.
    private const float Step = 0.05f;

    private readonly Dictionary<string, FireView> _fires = [];
    private readonly Dictionary<string, FireNode> _nodes = [];
    // Host side: the strength step last sent and the sound loop, per fire.
    private readonly Dictionary<string, int> _sent = [];
    private readonly Dictionary<string, int> _loops = [];
    private WorldGenerator? _litFor;
    private float _sinceSend;

    /// <summary>The session updates travel over. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The Creations this peer shows. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>The survival loop: players to burn, parts to damage. Set before adding to the tree; register its actions first.</summary>
    public SurvivalSync Survival { get; init; } = null!;

    /// <summary>The streamed world, for its POIs. Set before adding to the tree.</summary>
    public WorldStreamer World { get; init; } = null!;

    /// <summary>Part scripts, for the sphere test that finds the parts a fire reaches. Set before adding to the tree.</summary>
    public ScriptSync Scripts { get; init; } = null!;

    /// <summary>Sounds, for each fire's burning loop. Optional.</summary>
    public SoundSync? Sounds { get; init; }

    /// <summary>The numbers; null loads <see cref="SettingsPath"/>.</summary>
    public FireSettings? Settings { get; set; }

    /// <summary>The host's fire, once the actions are registered.</summary>
    public FireActions? Actions { get; private set; }

    /// <summary>The fires this peer shows, by id.</summary>
    public IReadOnlyDictionary<string, FireView> Fires => _fires;

    /// <summary>The nodes showing them, by id (tests look at them).</summary>
    public IReadOnlyDictionary<string, FireNode> Nodes => _nodes;

    /// <summary>The burning sound loop of each fire the host runs, by id (tests look at them).</summary>
    public IReadOnlyDictionary<string, int> Loops => _loops;

    /// <summary>
    /// Reads <see cref="SettingsPath"/> with the game rules of <paramref name="mods"/> on top (their
    /// <c>rules/fire.patch.json</c>), failing loudly on bad engine data.
    /// </summary>
    public static FireSettings LoadSettings(Core.Data.ModLoadResult? mods = null) =>
        Mechanica.Game.Mods.GameRules.Load(SettingsPath, "fire", mods, FireSettings.FromJson);

    /// <summary>Makes the host's fire and registers <c>GetFires</c>. Call after the survival actions.</summary>
    public void RegisterActions(ActionDispatcher dispatcher)
    {
        Settings ??= LoadSettings();
        Actions = new FireActions(Settings, Survival.Actions!, this);
        Actions.Register(dispatcher);
    }

    public override void _Ready()
    {
        Session.PeerReady += SendAllTo;
        Session.StateChanged += OnStateChanged;
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= SendAllTo;
        Session.StateChanged -= OnStateChanged;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Session.IsHost || Actions is null)
            return;
        if (_litFor != World.Generator)
        {
            _litFor = World.Generator;
            Actions.LightSites(World.Generator.Pois.Select(QuestSite.Of));
        }
        Actions.Tick((float)delta);
        _sinceSend += (float)delta;
        if (_sinceSend >= Settings!.Tick)
        {
            _sinceSend = 0;
            Send();
        }
    }

    public override void _Process(double delta)
    {
        foreach (var (id, node) in _nodes)
        {
            if (_fires.TryGetValue(id, out var view) && view is { Creation: { } creation, Part: { } part })
                node.Follow(Creations.NodeOf(creation), part);
        }
    }

    /// <inheritdoc/>
    public IEnumerable<(int Creation, int Part)> PartsNear(System.Numerics.Vector3 point, float radius) =>
        Scripts.OverlapSphere(point, Math.Max(0.01f, radius), null)
            .Where(o => o.Creation is not null && o.Part is not null)
            .Select(o => (o.Creation!.Value, o.Part!.Value));

    /// <summary>Host: sends now what changed since the last send (tests call it to skip the wait).</summary>
    public void Send()
    {
        if (Actions is null)
            return;
        var live = new HashSet<string>();
        foreach (var view in Actions.Fires())
        {
            if (view.Strength <= 0)
                continue;
            live.Add(view.Id);
            var step = (int)MathF.Ceiling(view.Strength / Step);
            if (_sent.TryGetValue(view.Id, out var sent) && sent == step)
                continue;
            _sent[view.Id] = step;
            Broadcast(MethodName.ShowFire, JsonSerializer.Serialize(view, ActionJson.Options));
            Sound(view);
        }
        foreach (var gone in _sent.Keys.Where(k => !live.Contains(k)).ToList())
        {
            _sent.Remove(gone);
            if (_loops.Remove(gone, out var loop))
                Sounds?.StopLoop(loop);
            HideFire(gone);
            if (Session.State == NetSession.SessionState.Hosting)
            {
                foreach (var peer in Session.ReadyPeers)
                    RpcId(peer, MethodName.HideFire, gone);
            }
        }
    }

    // Starts a fire's burning loop, or turns it up or down with the fire.
    private void Sound(FireView view)
    {
        if (Sounds is null)
            return;
        if (_loops.TryGetValue(view.Id, out var loop))
        {
            Sounds.SetLoopParameter(loop, "intensity", view.Strength);
            return;
        }
        var anchor = view is { Creation: { } creation, Part: { } part }
            ? SoundAnchor.OnPart(creation, part)
            : new SoundAnchor(view.Position.ToGodot());
        var handle = Sounds.StartLoop(Settings!.Sound, Identifiers.VanillaModId, anchor, new Dictionary<string, float> { ["intensity"] = view.Strength });
        if (handle != 0)
            _loops[view.Id] = handle;
    }

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
        if (!Session.IsHost || Actions is null)
            return;
        foreach (var view in Actions.Fires().Where(f => f.Strength > 0))
            RpcId(peer, MethodName.ShowFire, JsonSerializer.Serialize(view, ActionJson.Options));
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client shows only what its host sends.
        if (state != NetSession.SessionState.Connecting)
            return;
        foreach (var id in _nodes.Keys.ToList())
            HideFire(id);
    }

    /// <summary>Host → client: a fire started or changed.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowFire(string json)
    {
        FireView? view;
        try
        {
            view = JsonSerializer.Deserialize<FireView>(json, ActionJson.Options);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"[fire] unreadable fire from the host: {e.Message}");
            return;
        }
        if (view is null)
            return;
        _fires[view.Id] = view;
        if (!_nodes.TryGetValue(view.Id, out var node))
        {
            node = _nodes[view.Id] = new FireNode { Name = "Fire_" + view.Id.Replace(':', '_') };
            AddChild(node);
        }
        node.Show(view);
        if (view is { Creation: { } creation, Part: { } part })
            node.Follow(Creations.NodeOf(creation), part);
    }

    /// <summary>Host → client: a fire went out.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void HideFire(string id)
    {
        _fires.Remove(id);
        if (_nodes.Remove(id, out var node))
        {
            RemoveChild(node);
            node.QueueFree();
        }
    }
}
