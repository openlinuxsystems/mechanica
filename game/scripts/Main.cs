using Godot;
using Mechanica.Core;
using Mechanica.Core.Data;
using Mechanica.Core.Net;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Mechanica.Game.Mods;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.World;

namespace Mechanica.Game;

/// <summary>
/// Flat test world and session root. It builds the networking nodes (<see cref="Session"/>,
/// <see cref="PlayerSync"/>, <see cref="Actions"/>), the streamed world (<see cref="Streamer"/>,
/// <see cref="Creations"/>) and building (<see cref="Build"/>, the local player's <see cref="Tools"/>),
/// starts offline, hosting or joining from <see cref="Options"/>, and drives whichever player belongs
/// to this peer from the keyboard and mouse.
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
    /// Mods this instance runs, checked against the host on join. Null loads the enabled mods
    /// (<see cref="ModPaths.LoadContent"/>) when the scene starts; tests may set it directly.
    /// </summary>
    public ContentManifest? Content { get; set; }

    /// <summary>
    /// Loaded mods, the part catalog for building. Null loads the enabled mods when the scene starts;
    /// tests may set it (and <see cref="Content"/> then defaults to its manifest).
    /// </summary>
    public ModLoadResult? Mods { get; set; }

    /// <summary>Connection state and handshake.</summary>
    public NetSession Session { get; private set; } = null!;

    /// <summary>Player spawning and movement sync.</summary>
    public PlayerSync PlayerSync { get; private set; } = null!;

    /// <summary>Action API entry point for every peer.</summary>
    public ActionGateway Actions { get; private set; } = null!;

    /// <summary>Every Creation in the world, live or parked.</summary>
    public CreationWorld Creations { get; private set; } = null!;

    /// <summary>Chunk streaming: the ground, and which Creations are simulated.</summary>
    public WorldStreamer Streamer { get; private set; } = null!;

    /// <summary>Building actions and their sync to clients.</summary>
    public BuildSync Build { get; private set; } = null!;

    /// <summary>The local player's hand tools, once the player has spawned.</summary>
    public ToolBelt? Tools { get; private set; }

    /// <summary>The local player's Hammer, once the player has spawned.</summary>
    public HammerTool? Hammer => Tools?.Hammer;

    /// <summary>This peer's own player, once spawned.</summary>
    public PlayerController? LocalPlayer => PlayerSync.LocalPlayer;

    public override void _Ready()
    {
        GD.Print($"{MechanicaInfo.GameName} started (physics: {ProjectSettings.GetSetting("physics/3d/physics_engine")})");
        Options ??= ReadCommandLine();
        var mods = Mods ??= LoadMods();
        var content = Content ??= ContentManifest.From(mods);

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
        };
        AddChild(Streamer);
        PlayerSync.PlayerSpawned += OnPlayerSpawned;
        PlayerSync.PlayerDespawning += OnPlayerDespawning;
        AddChild(PlayerSync);
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
            Assets = new PartAssets(mods),
        };
        AddChild(Build);
        Build.RegisterActions(Actions.Dispatcher);
        // The offline player spawned with PlayerSync, before building existed.
        if (LocalPlayer is { } local)
            AddTools(local);
        Session.StateChanged += state => SetPropsSimulated(state is NetSession.SessionState.Offline or NetSession.SessionState.Hosting);
        if (ReadDeviceInput)
            AddChild(new ConnectPanel { Name = "ConnectPanel", Session = Session });

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
            GD.PushError($"[net] {Session.LastMessage}");
    }

    private static ModLoadResult LoadMods()
    {
        var content = ModPaths.LoadContent();
        foreach (var diagnostic in content.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            GD.PushWarning($"[mods] {diagnostic}");
        return content;
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
    /// Loose rigid props only simulate where the world is simulated (the host). On clients they are
    /// frozen until state sync (chunk 4b) drives them.
    /// </summary>
    private void SetPropsSimulated(bool simulated)
    {
        foreach (var body in GetChildren().OfType<RigidBody3D>())
            body.Freeze = !simulated;
    }
}
