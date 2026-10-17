using Godot;
using Mechanica.Core;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Net;
using Mechanica.Core.World;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Mechanica.Game.Items;
using Mechanica.Game.Mods;
using Mechanica.Game.Motion;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Remote;
using Mechanica.Game.World;

namespace Mechanica.Game;

/// <summary>
/// Game world and session root. It builds the networking nodes (<see cref="Session"/>,
/// <see cref="PlayerSync"/>, <see cref="CreationSync"/>, <see cref="Actions"/>), the streamed world (<see cref="Streamer"/>,
/// <see cref="Creations"/>), building (<see cref="Build"/>, the local player's <see cref="Tools"/>), the
/// motion primitives (<see cref="Motion"/>) and the host's external AI endpoint and record/replay
/// (<see cref="Ai"/>, <see cref="Recorder"/>). It starts offline, hosting or joining from
/// <see cref="Options"/>, and drives whichever player belongs to this peer from the keyboard and mouse.
/// Every peer runs the same scene, so node paths (and with them RPC routes) match everywhere.
/// </summary>
public partial class Main : Node3D
{
    private LocalPlayerInput? _input;

    /// <summary>How to start. Null reads the command line (<c>-- --host 27460</c>, <c>-- --join host:port</c>).</summary>
    public LaunchOptions? Options { get; set; }

    /// <summary>False in automated tests: nothing reads devices, the test sets the player's input itself.</summary>
    public bool ReadDeviceInput { get; set; } = true;

    /// <summary>
    /// Mods this instance runs, checked against the host on join. Null uses the manifest of
    /// <see cref="Mods"/>; tests may set it directly.
    /// </summary>
    public ContentManifest? Content { get; set; }

    /// <summary>
    /// Loaded mods, the part catalog for building. Null loads the enabled mods when the scene starts;
    /// tests may set it (and <see cref="Content"/> then defaults to its manifest).
    /// </summary>
    public ModLoadResult? Mods { get; set; }

    /// <summary>Meshes and shapes for the parts in <see cref="Mods"/>.</summary>
    public PartAssets Assets { get; private set; } = null!;

    /// <summary>
    /// World generation rules. Null loads them from <see cref="Mods"/> when the scene starts; tests may set
    /// <see cref="WorldGenData.Flat"/>.
    /// </summary>
    public WorldGenData? WorldGen { get; set; }

    /// <summary>Crafting recipes. Null loads them from <see cref="Mods"/> when the scene starts; tests may set them.</summary>
    public RecipeBook? Recipes { get; set; }

    /// <summary>Connection state and handshake.</summary>
    public NetSession Session { get; private set; } = null!;

    /// <summary>Player spawning and movement sync.</summary>
    public PlayerSync PlayerSync { get; private set; } = null!;

    /// <summary>Action API entry point for every peer.</summary>
    public ActionGateway Actions { get; private set; } = null!;

    /// <summary>Every Creation in the world, live or parked.</summary>
    public CreationWorld Creations { get; private set; } = null!;

    /// <summary>Creation state sync: interest, full syncs and body snapshots.</summary>
    public CreationSync CreationSync { get; private set; } = null!;

    /// <summary>Chunk streaming: the ground, and which Creations are simulated.</summary>
    public WorldStreamer Streamer { get; private set; } = null!;

    /// <summary>Building actions and their sync to clients.</summary>
    public BuildSync Build { get; private set; } = null!;

    /// <summary>The local player's hand tools, once the player has spawned.</summary>
    public ToolBelt? Tools { get; private set; }

    /// <summary>Inventories, containers and crafting, and their sync to clients.</summary>
    public InventorySync Inventory { get; private set; } = null!;

    /// <summary>Forces, torques and bearing motors for scripts, bots and external agents.</summary>
    public MotionSync Motion { get; private set; } = null!;

    /// <summary>Record/replay of the host's Action API calls.</summary>
    public RecordReplay Recorder { get; private set; } = null!;

    /// <summary>The external AI endpoint (WebSocket JSON-RPC), listening only when asked to.</summary>
    public AiEndpoint Ai { get; private set; } = null!;

    /// <summary>The local player's Hammer, once the player has spawned.</summary>
    public HammerTool? Hammer => Tools?.Hammer;

    /// <summary>This peer's own player, once spawned.</summary>
    public PlayerController? LocalPlayer => PlayerSync.LocalPlayer;

    public override void _Ready()
    {
        GD.Print($"{MechanicaInfo.GameName} started (physics: {ProjectSettings.GetSetting("physics/3d/physics_engine")})");
        Options ??= ReadCommandLine();
        var mods = Mods ??= LoadMods();
        var world = WorldGen ??= LoadWorldGen(mods);
        var recipes = Recipes ??= LoadRecipes(mods);
        // World data and recipes are part of the content check on join: every peer regenerates the world
        // from the former and shows the latter in its station screens.
        var content = Content ??= ContentManifest.From(mods) with { Fingerprint = $"{mods.Fingerprint}+{world.Fingerprint}+{recipes.Fingerprint}" };
        Assets = new PartAssets(mods);

        Session = new NetSession { Name = "Session", Content = content };
        AddChild(Session);
        var players = new Node3D { Name = "Players" };
        AddChild(players);
        PlayerSync = new PlayerSync
        {
            Name = "PlayerSync",
            Session = Session,
            PlayersRoot = players,
            SpawnPoint = () => GetNodeOrNull<Node3D>("SpawnPoint")?.GlobalTransform ?? Transform3D.Identity,
        };
        Creations = new CreationWorld { Name = "Creations" };
        AddChild(Creations);
        // Before the players, so the spawn has ground on the first frame.
        Streamer = new WorldStreamer
        {
            Name = "World",
            Session = Session,
            PlayerSync = PlayerSync,
            Creations = Creations,
            SpawnPoint = () => GetNodeOrNull<Node3D>("SpawnPoint")?.GlobalPosition ?? Vector3.Zero,
            WorldGen = WorldGen!,
            Seed = Options.Seed ?? (long)(((ulong)GD.Randi() << 32) | GD.Randi()),
        };
        AddChild(Streamer);
        PlaceSpawnPoint();
        Streamer.WorldReplaced += PlaceSpawnPoint;
        PlayerSync.PlayerSpawned += OnPlayerSpawned;
        PlayerSync.PlayerDespawning += OnPlayerDespawning;
        AddChild(PlayerSync);
        CreationSync = new CreationSync
        {
            Name = "CreationSync",
            Session = Session,
            PlayerSync = PlayerSync,
            Creations = Creations,
            Catalog = new LoadedParts(this),
            Assets = Assets,
        };
        AddChild(CreationSync);
        Actions = new ActionGateway { Name = "Actions", Session = Session };
        AddChild(Actions);
        PlayerSync.RegisterActions(Actions.Dispatcher);
        Build = new BuildSync
        {
            Name = "Build",
            Session = Session,
            Players = PlayerSync,
            Creations = Creations,
            Catalog = mods,
            Assets = Assets,
        };
        AddChild(Build);
        Build.RegisterActions(Actions.Dispatcher);
        Inventory = new InventorySync
        {
            Name = "Inventory",
            Session = Session,
            Players = PlayerSync,
            Creations = Creations,
            Catalog = mods,
            Recipes = recipes,
        };
        AddChild(Inventory);
        Inventory.RegisterActions(Actions.Dispatcher);
        Motion = new MotionSync { Name = "Motion", Session = Session, Creations = Creations };
        AddChild(Motion);
        Motion.RegisterActions(Actions.Dispatcher);
        Recorder = new RecordReplay { Name = "Recorder", Session = Session, Actions = Actions };
        AddChild(Recorder);
        Ai = new AiEndpoint { Name = "Ai", Session = Session, Actions = Actions, Log = Recorder };
        AddChild(Ai);
        // The offline player spawned with PlayerSync, before building existed.
        if (LocalPlayer is { } local)
            AddTools(local);
        Session.StateChanged += state => SetPropsSimulated(state is NetSession.SessionState.Offline or NetSession.SessionState.Hosting);
        if (ReadDeviceInput)
        {
            AddChild(new ConnectPanel { Name = "ConnectPanel", Session = Session });
            AddChild(new InventoryScreen { Name = "InventoryScreen", Inventory = Inventory, Actions = Actions, Players = PlayerSync, Catalog = mods });
        }

        Start(Options);
    }

    /// <summary>Starts the session <paramref name="options"/> describe. Errors are logged and leave the game offline.</summary>
    public void Start(LaunchOptions options)
    {
        var error = options.Mode switch
        {
            SessionMode.Host => Session.Host(options.Port, options.MaxPlayers),
            SessionMode.Join => Session.Join(options.Endpoint!.Value),
            _ => Error.Ok,
        };
        if (error != Error.Ok)
        {
            GD.PushError($"[net] {Session.LastMessage}");
            return;
        }
        if (!Session.IsHost)
            return;
        if (options.RecordPath is { } record)
            Recorder.RecordTo(record);
        if (options.ReplayPath is { } replay && Recorder.ReplayFrom(replay) is { } replayError)
            GD.PushError($"[replay] {replayError}");
        if (options.AiPort is { } port)
        {
            var token = options.AiToken ?? OS.GetEnvironment("MECHANICA_AI_TOKEN");
            if (Ai.Listen(port, options.AiBind, token) is { } aiError)
                GD.PushError($"[ai] {aiError}");
        }
    }

    /// <summary>
    /// Switches to newly loaded mods, as the Test Sandbox does after the Part Editor exports. Clients
    /// already connected stay; the next client to join is checked against the new mods.
    /// </summary>
    public void ReloadMods(ModLoadResult mods)
    {
        Mods = mods;
        Content = ContentManifest.From(mods);
        Session.Content = Content;
        Assets.SetContent(mods);
        Build.SetContent(mods);
        Hammer?.ReloadParts();
    }

    // The current Mods, so readers keep working after ReloadMods swaps them.
    private sealed class LoadedParts(Main main) : IPartCatalog
    {
        public PartDefinition? FindDefinition(string id) => main.Mods!.FindDefinition(id);
    }

    private static ModLoadResult LoadMods()
    {
        var content = ModPaths.LoadContent();
        foreach (var diagnostic in content.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            GD.PushWarning($"[mods] {diagnostic}");
        return content;
    }

    private static RecipeBook LoadRecipes(ModLoadResult mods)
    {
        var recipes = RecipeBook.Load(mods);
        foreach (var diagnostic in recipes.Diagnostics)
            GD.PushWarning($"[recipes] {diagnostic}");
        return recipes;
    }

    private static WorldGenData LoadWorldGen(ModLoadResult mods)
    {
        var world = WorldGenData.Load(mods);
        foreach (var diagnostic in world.Diagnostics)
            GD.PushWarning($"[world] {diagnostic}");
        return world;
    }

    // Players start at the world's spawn (the crash site's spawn marker in vanilla). A world without
    // one keeps the scene's spawn point, moved onto the ground.
    private void PlaceSpawnPoint()
    {
        if (GetNodeOrNull<Node3D>("SpawnPoint") is not { } spawn)
            return;
        var at = spawn.GlobalPosition;
        spawn.GlobalPosition = Streamer.Generator.Spawn?.ToGodot() ?? new Vector3(at.X, Streamer.Generator.HeightAt(at.X, at.Z), at.Z);
    }

    private static LaunchOptions ReadCommandLine()
    {
        if (LaunchOptions.TryParse(OS.GetCmdlineUserArgs(), out var options, out var error))
            return options;
        GD.PushError($"[net] {error} Starting offline.");
        return LaunchOptions.Offline;
    }

    private void OnPlayerSpawned(PlayerController player)
    {
        if (player != LocalPlayer)
            return;
        player.MakeCameraCurrent();
        AddTools(player);
        if (!ReadDeviceInput)
            return;
        _input = new LocalPlayerInput { Name = "LocalPlayerInput", Target = player };
        AddChild(_input);
    }

    private void AddTools(PlayerController player)
    {
        if (Build is null || Tools is not null)
            return;
        Tools = new ToolBelt { Name = "Tools", Player = player, Actions = Actions, Build = Build, ReadDeviceInput = ReadDeviceInput };
        AddChild(Tools);
    }

    private void OnPlayerDespawning(PlayerController player)
    {
        if (Tools?.Player == player)
        {
            RemoveChild(Tools);
            Tools.QueueFree();
            Tools = null;
        }
        if (_input?.Target != player)
            return;
        RemoveChild(_input);
        _input.QueueFree();
        _input = null;
    }

    /// <summary>
    /// Loose rigid props only simulate where the world is simulated (the host). They are test-world
    /// fixtures, not Creations, so state sync does not cover them and they stay frozen on clients.
    /// </summary>
    private void SetPropsSimulated(bool simulated)
    {
        foreach (var body in GetChildren().OfType<RigidBody3D>())
            body.Freeze = !simulated;
    }
}
