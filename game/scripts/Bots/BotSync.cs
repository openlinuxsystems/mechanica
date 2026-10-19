using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Scripting;
using Mechanica.Game.Sound;
using Mechanica.Game.Survival;
using Mechanica.Game.World;
using Mechanica.Scripting;
using Mechanica.Scripting.Bots;
using FileAccess = Godot.FileAccess;
using RayHit = Mechanica.Core.Survival.RayHit;

namespace Mechanica.Game.Bots;

/// <summary>
/// Bots and raids over the network (chunk 21, D10). On the host it owns the <see cref="BotActions"/>, the
/// <see cref="RaidDirector"/>, the <see cref="PoiGuards"/> and the Lua <see cref="BotRuntime"/>: brains think,
/// ask the Action API to walk and attack, and this node walks each bot's <see cref="BotBody"/> as asked (with gravity, and the
/// world's ground under it where no terrain is loaded yet). It is the <see cref="IBotWorld"/> and
/// <see cref="IRaidWorld"/> behind them.
/// <para>
/// Clients only show bots: each bot appears and changes by a reliable view, moves by unreliable pose
/// snapshots, and plays its attack when it attacks. The raid in progress is sent to every peer for the HUD, and a
/// peer that becomes ready is sent all of it.
/// </para>
/// </summary>
public partial class BotSync : Node3D, IBotWorld, IRaidWorld
{
    /// <summary>Where the bot engine limits are read from.</summary>
    public const string SettingsPath = "res://data/bots.json";

    // Host ticks between pose snapshots: 3 at 60 Hz, 20 per second.
    private const int PoseInterval = 3;

    // Bots this far under the world's ground (no terrain loaded there) are put back on it, in m.
    private const float BelowGround = 0.3f;

    // A swimmer closes this much of the gap to its swim depth per second, no faster than SwimVertical (m/s).
    private const float SwimFollow = 3f;
    private const float SwimVertical = 2f;

    private readonly Dictionary<int, BotBody> _bodies = [];
    private int _tick;
    private double _sincePoll;
    private double _pollSeconds = 1.0;
    private ModLoadResult _mods = null!;
    private BotBook _book = null!;

    /// <summary>The session updates travel over. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Survival: health, crops and the shared damage API. Set before adding to the tree; register its actions first.</summary>
    public SurvivalSync Survival { get; init; } = null!;

    /// <summary>The streamed world, for the ground. Set before adding to the tree.</summary>
    public WorldStreamer World { get; init; } = null!;

    /// <summary>The host's Action API. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Sound, for bot and raid sounds. Optional.</summary>
    public SoundSync? Sounds { get; init; }

    /// <summary>The loaded mods, whose bot scripts run. Set before adding to the tree; <see cref="SetContent"/> swaps them.</summary>
    public ModLoadResult Mods
    {
        get => _mods;
        init => _mods = value;
    }

    /// <summary>Bot types. Set before adding to the tree; <see cref="SetContent"/> swaps them.</summary>
    public BotBook Book
    {
        get => _book;
        init => _book = value;
    }

    /// <summary>The raid table. Set before adding to the tree.</summary>
    public RaidTable RaidTable { get; init; } = RaidTable.None;

    /// <summary>Engine limits; null loads <see cref="SettingsPath"/>.</summary>
    public BotSettings? Settings { get; set; }

    /// <summary>How fast a knocked-back bot slows to its own walk, in m/s² (<c>sledge.knockback_drag</c>).</summary>
    public float KnockbackDrag { get; init; } = new Core.Building.SledgeSettings().KnockbackDrag;

    /// <summary>The bot actions, once registered (host).</summary>
    public BotActions? Bots { get; private set; }

    /// <summary>The raid director, once registered (host).</summary>
    public RaidDirector? Raids { get; private set; }

    /// <summary>The POIs' guards, once the world is up (host).</summary>
    public PoiGuards? Guards { get; private set; }

    /// <summary>Bot brains, once the node is ready (host).</summary>
    public BotRuntime? Runtime { get; private set; }

    /// <summary>The bots this peer shows, by id.</summary>
    public IReadOnlyDictionary<int, BotBody> Bodies => _bodies;

    /// <summary>The raid as last sent.</summary>
    public RaidView Raid { get; private set; } = RaidView.None;

    /// <summary>Crops bots destroyed on this host, for tests.</summary>
    public int CropsDestroyed { get; private set; }

    /// <summary>Raised on this peer when the raid view arrives or changes.</summary>
    public event Action<RaidView>? RaidChanged;

    /// <inheritdoc/>
    public double Now => Survival.Now;

    /// <summary>Reads <see cref="SettingsPath"/>, failing loudly on bad data.</summary>
    public static BotSettings LoadSettings()
    {
        using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {SettingsPath}: {FileAccess.GetOpenError()}");
        return BotSettings.FromJson(file.GetAsText());
    }

    /// <summary>
    /// Registers the bot and raid actions and hands bots to the shared damage API. Call after
    /// <see cref="SurvivalSync.RegisterActions"/>.
    /// </summary>
    public void RegisterActions(ActionDispatcher dispatcher, int seed)
    {
        Settings ??= LoadSettings();
        var survival = Survival.Actions ?? throw new InvalidOperationException("Register the survival actions first.");
        Bots = new BotActions(new CurrentBook(this), Settings, survival, Survival, this) { Seed = seed };
        Bots.Register(dispatcher);
        survival.Bots = Bots;
        Raids = new RaidDirector(RaidTable, Bots, survival, this);
        Raids.Register(dispatcher);
        Bots.Spawned += OnSpawned;
        Bots.Changed += OnChanged;
        Bots.Removed += OnRemoved;
        Bots.Attacked += OnAttacked;
        Bots.CropDestroyed += (_, _, _) => CropsDestroyed++;
        Raids.Changed += OnRaidChanged;
    }

    /// <summary>
    /// Switches to newly loaded mods and their bot types, as the Test Sandbox does after an editor exports:
    /// bots spawned from now on use the new types and scripts.
    /// </summary>
    public void SetContent(ModLoadResult mods, BotBook book)
    {
        _mods = mods;
        _book = book;
    }

    // The current Book and Mods, so the bot actions and brains keep working after SetContent swaps them.
    private sealed class CurrentBook(BotSync sync) : IBotCatalog
    {
        public Bot? FindBot(string id) => sync.Book.FindBot(id);
    }

    private sealed class CurrentScripts(BotSync sync) : IBotScriptSource
    {
        public ScriptFile? FindScript(Bot bot) => new ModBotScriptSource(sync.Mods).FindScript(bot);
    }

    public override void _Ready()
    {
        Settings ??= LoadSettings();
        if (Bots is not null)
        {
            var scripting = ScriptSync.LoadSettings();
            _pollSeconds = scripting.ReloadPollSeconds;
            Runtime = new BotRuntime(scripting, Actions.Dispatcher, Bots, Raids, this, new CurrentScripts(this));
            Runtime.Message += Log;
            Runtime.Alerted += id => PlayBotSound(id, "alert");
            Runtime.Sounded += (id, sound) => { if (Bots.Find(id) is { } bot) Emit(sound, bot.Type.ModId, bot.Centre.ToGodot()); };
        }
        Session.PeerReady += SendAllTo;
        Session.StateChanged += OnStateChanged;
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= SendAllTo;
        Session.StateChanged -= OnStateChanged;
        Runtime?.Clear();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Session.IsHost || Bots is null)
            return;
        var dt = (float)delta;
        _sincePoll += delta;
        if (Runtime is not null && _sincePoll >= _pollSeconds)
        {
            _sincePoll = 0;
            Runtime.CheckForChanges();
        }
        Runtime?.Update(dt);
        Walk(dt);
        Bots.Tick(dt);
        Raids!.Tick();
        // The world's POIs are placed when the streamer is ready, which may come after this node registers.
        if (Guards is null && World.Generator is { } world)
            Guards = new PoiGuards(PoiGuards.PostsOf(world), Bots, Survival.Actions!);
        Guards?.Tick();
        if (++_tick % PoseInterval == 0)
            SendPoses();
    }

    public override void _Process(double delta)
    {
        if (Session.IsHost)
            return;
        foreach (var body in _bodies.Values)
            body.Interpolate((float)delta);
    }

    // ---- IBotWorld, IRaidWorld ----

    /// <inheritdoc/>
    public RayHit? Cast(System.Numerics.Vector3 from, System.Numerics.Vector3 to, int ignoreBot) =>
        Survival.CastBetween(from.ToGodot(), to.ToGodot(), _bodies.TryGetValue(ignoreBot, out var body) ? [body.GetRid()] : []);

    /// <inheritdoc/>
    public System.Numerics.Vector3 GroundBelow(System.Numerics.Vector3 point) => Survival.GroundBelow(point);

    // ---- Host ----

    // Every living bot walks where its brain asked, falls, and stays on the world's ground.
    private void Walk(float dt)
    {
        var gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        foreach (var bot in Bots!.Bots.Values)
        {
            if (!_bodies.TryGetValue(bot.Id, out var body))
                continue;
            if (bot.Dead)
            {
                body.Velocity = Vector3.Zero;
                continue;
            }
            if (bot.Swimming)
                body.Fall = Swim(bot, body.GlobalPosition);
            else
                body.Fall = body.IsOnFloor() ? 0f : body.Fall - gravity * dt;
            // A Sledgehammer's knockback rides on top of the walk: its lift once, its push slowing away.
            var knock = bot.TakeKnock(KnockbackDrag, dt);
            body.Fall += knock.Y;
            body.Velocity = new Vector3(bot.Desired.X + knock.X, body.Fall, bot.Desired.Z + knock.Z);
            body.Rotation = new Vector3(0, Mathf.DegToRad(bot.Yaw), 0);
            body.MoveAndSlide();
            var feet = body.GlobalPosition;
            var ground = World.Generator.HeightAt(feet.X, feet.Z);
            if (feet.Y < ground - BelowGround)
            {
                body.GlobalPosition = feet = new Vector3(feet.X, ground, feet.Z);
                body.Fall = 0;
            }
            Bots.Moved(bot.Id, feet.ToNumerics(), Survival.IsWater((feet + new Vector3(0, bot.Type.Height * 0.5f, 0)).ToNumerics()));
        }
    }

    // A swimmer rises or sinks towards its swim depth below the surface, never below the bottom: the vertical speed for it.
    private float Swim(BotState bot, Vector3 feet)
    {
        var floor = World.Generator.HeightAt(feet.X, feet.Z);
        var target = MathF.Max(floor, World.Generator.SeaLevel - bot.Type.SwimDepth!.Value - bot.Type.Height / 2);
        return Mathf.Clamp((target - feet.Y) * SwimFollow, -SwimVertical, SwimVertical);
    }

    private void OnSpawned(BotState bot)
    {
        Broadcast(MethodName.ShowBot, Json(bot.View));
        PlayBotSound(bot.Id, "idle");
    }

    private void OnChanged(BotState bot)
    {
        var wasDead = _bodies.TryGetValue(bot.Id, out var body) && body.View is { Dead: true };
        Broadcast(MethodName.ShowBot, Json(bot.View));
        if (bot.Dead && !wasDead)
            PlayBotSound(bot.Id, "death");
    }

    private void OnRemoved(int id)
    {
        HideBot(id);
        if (Session.State != NetSession.SessionState.Hosting)
            return;
        foreach (var peer in Session.ReadyPeers)
            RpcId(peer, MethodName.HideBot, id);
    }

    private void OnAttacked(BotAttackEvent attack)
    {
        Broadcast(MethodName.BotAttacked, Json(attack));
        if (Bots!.Find(attack.Bot) is { } bot)
            Emit(attack.Sound, bot.Type.ModId, bot.Centre.ToGodot());
    }

    private void OnRaidChanged(RaidView raid)
    {
        if (raid.Phase == RaidPhases.Warning)
            Emit("raid_horn", Identifiers.VanillaModId, raid.Target.ToGodot());
        Broadcast(MethodName.ShowRaid, Json(raid));
    }

    private void PlayBotSound(int id, string what)
    {
        if (Bots?.Find(id) is { } bot)
            Emit(bot.Type.Sound(what), bot.Type.ModId, bot.Centre.ToGodot());
    }

    private void Emit(string sound, string modId, Vector3 at) => Sounds?.Emit(sound, modId, at);

    // Every bot's pose, packed [id, x, y, z, yaw] per bot, unreliably: a lost one is replaced by the next.
    private void SendPoses()
    {
        if (Session.State != NetSession.SessionState.Hosting || _bodies.Count == 0 || !Session.ReadyPeers.Any())
            return;
        var poses = new float[Bots!.Bots.Count * 5];
        var i = 0;
        foreach (var bot in Bots.Bots.Values)
        {
            poses[i++] = bot.Id;
            poses[i++] = bot.Position.X;
            poses[i++] = bot.Position.Y;
            poses[i++] = bot.Position.Z;
            poses[i++] = bot.Yaw;
        }
        foreach (var peer in Session.ReadyPeers)
            RpcId(peer, MethodName.ShowPoses, poses);
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
        if (Bots is null)
            return;
        foreach (var bot in Bots.Bots.Values)
            RpcId(peer, MethodName.ShowBot, Json(bot.View));
        RpcId(peer, MethodName.ShowRaid, Json(Raids!.Current));
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client shows only what its host sends, and runs no brains.
        if (state != NetSession.SessionState.Connecting)
            return;
        Runtime?.Clear();
        if (Bots is not null)
        {
            foreach (var id in Bots.Bots.Keys.ToList())
                Bots.Remove(id);
        }
        foreach (var id in _bodies.Keys.ToList())
            HideBot(id);
        ShowRaid(Json(RaidView.None));
    }

    private static void Log(ScriptMessage message)
    {
        if (message.Kind == ScriptMessageKind.Print)
            GD.Print($"[bots] {message}");
        else
            GD.PushWarning($"[bots] {message}");
    }

    // ---- Shown on every peer ----

    /// <summary>Host → client: a bot appeared or changed (health, death, raid).</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowBot(string json)
    {
        if (Read<BotView>(json) is not { } view || Book.FindBot(view.Type) is not { } type)
            return;
        if (!_bodies.TryGetValue(view.Id, out var body))
        {
            body = _bodies[view.Id] = new BotBody { Name = $"Bot{view.Id}", BotId = view.Id, Type = type, Assets = Survival.Assets };
            AddChild(body);
            body.GlobalPosition = view.Position.ToGodot();
            body.Rotation = new Vector3(0, Mathf.DegToRad(view.Yaw), 0);
        }
        body.Show(view, follow: !Session.IsHost);
    }

    /// <summary>Host → client: a bot is gone.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void HideBot(int id)
    {
        if (!_bodies.Remove(id, out var body))
            return;
        RemoveChild(body);
        body.QueueFree();
    }

    /// <summary>Host → client: where every bot is.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void ShowPoses(float[] poses)
    {
        for (var i = 0; i + 4 < poses.Length; i += 5)
        {
            if (_bodies.TryGetValue((int)poses[i], out var body))
                body.Follow(new Vector3(poses[i + 1], poses[i + 2], poses[i + 3]), poses[i + 4]);
        }
    }

    /// <summary>Host → client: a bot attacked.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BotAttacked(string json)
    {
        if (Read<BotAttackEvent>(json) is { } attack && _bodies.TryGetValue(attack.Bot, out var body))
            body.Attacked(attack.Attack);
    }

    /// <summary>Host → client: the raid in progress.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowRaid(string json)
    {
        if (Read<RaidView>(json) is not { } raid)
            return;
        Raid = raid;
        RaidChanged?.Invoke(raid);
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
            GD.PushWarning($"[bots] unreadable {typeof(T).Name} from the host: {e.Message}");
            return null;
        }
    }
}
