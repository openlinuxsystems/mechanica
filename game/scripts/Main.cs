using Godot;
using Mechanica.Core;
using Mechanica.Core.Data;
using Mechanica.Core.Net;
using Mechanica.Game.Mods;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game;

/// <summary>
/// Flat test world and session root. It builds the networking nodes (<see cref="Session"/>,
/// <see cref="PlayerSync"/>, <see cref="Actions"/>), starts offline, hosting or joining from
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
    /// Mods this instance runs, checked against the host on join. Null loads the enabled mods
    /// (<see cref="ModPaths.LoadContent"/>) when the scene starts; tests may set it directly.
    /// </summary>
    public ContentManifest? Content { get; set; }

    /// <summary>Connection state and handshake.</summary>
    public NetSession Session { get; private set; } = null!;

    /// <summary>Player spawning and movement sync.</summary>
    public PlayerSync PlayerSync { get; private set; } = null!;

    /// <summary>Action API entry point for every peer.</summary>
    public ActionGateway Actions { get; private set; } = null!;

    /// <summary>This peer's own player, once spawned.</summary>
    public PlayerController? LocalPlayer => PlayerSync.LocalPlayer;

    public override void _Ready()
    {
        GD.Print($"{MechanicaInfo.GameName} started (physics: {ProjectSettings.GetSetting("physics/3d/physics_engine")})");
        Options ??= ReadCommandLine();
        var content = Content ??= LoadContentManifest();

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
        PlayerSync.PlayerSpawned += OnPlayerSpawned;
        PlayerSync.PlayerDespawning += OnPlayerDespawning;
        AddChild(PlayerSync);
        Actions = new ActionGateway { Name = "Actions", Session = Session };
        AddChild(Actions);
        PlayerSync.RegisterActions(Actions.Dispatcher);
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

    private static ContentManifest LoadContentManifest()
    {
        var content = ModPaths.LoadContent();
        foreach (var diagnostic in content.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            GD.PushWarning($"[mods] {diagnostic}");
        return ContentManifest.From(content);
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
        if (!ReadDeviceInput)
            return;
        _input = new LocalPlayerInput { Name = "LocalPlayerInput", Target = player };
        AddChild(_input);
    }

    private void OnPlayerDespawning(PlayerController player)
    {
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
