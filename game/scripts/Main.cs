using Godot;
using Mechanica.Core;
using Mechanica.Core.Bots;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Net;
using Mechanica.Core.Quests;
using Mechanica.Core.Saves;
using Mechanica.Core.Sound;
using Mechanica.Core.Survival;
using Mechanica.Core.Ui;
using Mechanica.Core.World;
using Mechanica.Game.Bots;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Mechanica.Game.Items;
using Mechanica.Game.Logic;
using Mechanica.Game.Menus;
using Mechanica.Game.Mods;
using Mechanica.Game.Motion;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Fire;
using Mechanica.Game.Quests;
using Mechanica.Game.Remote;
using Mechanica.Game.Saves;
using Mechanica.Game.Scripting;
using Mechanica.Game.Sound;
using Mechanica.Game.Survival;
using Mechanica.Game.World;

namespace Mechanica.Game;

/// <summary>
/// Game world and session root. It builds the networking nodes (<see cref="Session"/>,
/// <see cref="PlayerSync"/>, <see cref="CreationSync"/>, <see cref="Actions"/>), the streamed world (<see cref="Streamer"/>,
/// <see cref="Creations"/>), building (<see cref="Build"/>, the local player's <see cref="Tools"/>), the
/// motion primitives (<see cref="Motion"/>), part scripts (<see cref="Scripts"/>), the logic bus and seats (<see cref="Logic"/>), sound (<see cref="Sounds"/>) and the host's external
/// AI endpoint and record/replay (<see cref="Ai"/>, <see cref="Recorder"/>). It starts offline, hosting or joining from
/// <see cref="Options"/>, and drives whichever player belongs to this peer from the keyboard and mouse.
/// Every peer runs the same scene, so node paths (and with them RPC routes) match everywhere.
/// </summary>
public partial class Main : Node3D
{
    private LocalPlayerInput? _input;

    /// <summary>How to start. Null reads the command line (<c>-- --host 27460</c>, <c>-- --join host:port</c>).</summary>
    public LaunchOptions? Options { get; set; }

    /// <summary>How the main menu's New game or Join game wants the next world started; it overrides the command line.</summary>
    public static LaunchOptions? MenuOptions { get; set; }

    /// <summary>The name the main menu's New game gave the next world.</summary>
    public static string? MenuWorldName { get; set; }

    /// <summary>The character the main menu's New game picked for the host's player.</summary>
    public static string? MenuCharacter { get; set; }

    /// <summary>A saved world the main menu picked to load; it overrides the command line's <c>--load</c>.</summary>
    public static SaveGame? MenuSave { get; set; }

    /// <summary>
    /// The world to load instead of making a new one. Null reads <see cref="MenuSave"/> or the command line's
    /// <c>--load</c>; tests may set it. Its seed and mode replace <see cref="Options"/>'.
    /// </summary>
    public SaveGame? Save { get; set; }

    /// <summary>
    /// This instance's player. Null loads (or makes) <c>user://profile.json</c>, renamed by <c>--name</c>; tests
    /// set fixed profiles so restarted instances are the same players.
    /// </summary>
    public PlayerProfile? Profile { get; set; }

    private Ui.LoadingScreen? _loadingScreen;
    private int _physicsSteps;

    // Host: the world containers that refill (POI part markers with refill_days), by marker key, and physics steps to
    // the next look at them.
    private Dictionary<string, WorldPartSpot> _refillSpots = [];
    private int _refillWait;
    private int _sceneryMost;

    /// <summary>Where the player profile lives.</summary>
    public const string ProfilePath = "user://profile.json";

    /// <summary>The folder blueprints are saved in. Null uses <c>user://blueprints</c>; tests set their own.</summary>
    public string? BlueprintFolder { get; set; }

    /// <summary>The folder worlds are saved in. Null uses <c>user://saves</c>; tests set their own.</summary>
    public string? SaveFolder { get; set; }

    /// <summary>The Lift's blueprint menu, when reading device input.</summary>
    public BlueprintMenu? Blueprints { get; private set; }

    /// <summary>The upgrade dialog (U), or null without device input.</summary>
    public Items.UpgradeDialog? Upgrades { get; private set; }

    /// <summary>False in automated tests: nothing reads devices, the test sets the player's input itself.</summary>
    public bool ReadDeviceInput { get; set; } = true;

    /// <summary>
    /// Whether a new world gets the debug scrap car by the spawn (<see cref="DebugCarSpawner"/>) when this
    /// instance hosts it. Null means only in a debug build, where this is the running scene (the real game, not a test
    /// that adds the world under its own scene), and while <c>game/data/debug_car.json</c> has it enabled.
    /// </summary>
    public bool? DebugCar { get; set; }

    /// <summary>The debug scrap car's id once it has been put down in this world, or null.</summary>
    public int? DebugCarId { get; private set; }

    /// <summary>
    /// Whether the loading screen covers the world while it loads (UI spec §15). Null shows it in the real game
    /// (settings that persist) when reading device input, and not in test scenes.
    /// </summary>
    public bool? ShowLoading { get; set; }

    /// <summary>The loading screen, while it shows.</summary>
    public Ui.LoadingScreen? LoadingScreen => IsInstanceValid(_loadingScreen) ? _loadingScreen : null;

    /// <summary>Where this instance's world load stands; the host reports it to joining clients.</summary>
    public WorldLoading Loading { get; private set; } = null!;

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

    /// <summary>The mods folder the AI endpoint's mod tools work in. Null uses <see cref="ModPaths.ModsRoot"/>; tests set their own.</summary>
    public string? ModsFolder { get; set; }

    /// <summary>The mod profile the AI endpoint's mod tools save. Null uses <see cref="ModPaths.ProfilePath"/>; tests set their own.</summary>
    public string? ModProfileFile { get; set; }

    /// <summary>Meshes and shapes for the parts in <see cref="Mods"/>.</summary>
    public PartAssets Assets { get; private set; } = null!;

    /// <summary>The items of <see cref="Mods"/> with a held model, numbered as players' inputs and snapshots carry them.</summary>
    public HeldItems HeldItems { get; private set; } = HeldItems.Empty;

    /// <summary>
    /// The model of the held item numbered <paramref name="index"/> (<see cref="HeldItems"/>), placed in the hand's frame by
    /// its <c>held</c> offset, rotation and scale; null for none.
    /// </summary>
    public Node3D? HeldModel(ushort index)
    {
        if (HeldItems.IdOf(index) is not { } id || Mods?.Parts.GetValueOrDefault(id) is not { Definition.Held: { } held } part)
            return null;
        var model = held.Mesh is { } mesh ? Assets.InstantiateScene(mesh, part.ModId) : Assets.SceneFor(id, part.Definition);
        if (model is null)
            return null;
        var turn = held.Rotation ?? default;
        var root = new Node3D { Name = "Held" };
        root.Transform = new Transform3D(
            Basis.FromEuler(new Vector3(Mathf.DegToRad(turn.X), Mathf.DegToRad(turn.Y), Mathf.DegToRad(turn.Z)), EulerOrder.Xyz).Scaled(Vector3.One * (held.Scale ?? 1f)),
            held.Offset is { } o ? new Vector3(o.X, o.Y, o.Z) : Vector3.Zero);
        root.AddChild(model);
        return root;
    }

    /// <summary>
    /// How a bite of the held item numbered <paramref name="index"/> (<see cref="HeldItems"/>) shows: its colour and its
    /// <c>eat</c> sound, or for a drink (<see cref="Core.Survival.FoodUse"/>) its <c>drink</c> sound and no juice.
    /// </summary>
    public HeldBite? HeldBite(ushort index)
    {
        if (HeldItems.IdOf(index) is not { } id || Mods?.Parts.GetValueOrDefault(id) is not { } part)
            return null;
        var juice = (part.Definition.Held?.Juice ?? part.Definition.Visual?.Color) is { } color && Color.HtmlIsValid(color) ? Color.FromHtml(color) : new Color(0.8f, 0.25f, 0.2f);
        var drink = Core.Survival.FoodUse.Of(part.Definition).Drink;
        var sound = (drink ? Core.Sound.PartSounds.For(part.Definition, "drink") : null) ?? Core.Sound.PartSounds.For(part.Definition, "eat");
        return new HeldBite(juice, sound, part.ModId, drink);
    }

    /// <summary>
    /// World generation rules. Null loads them from <see cref="Mods"/> when the scene starts; tests may set
    /// <see cref="WorldGenData.Flat"/>.
    /// </summary>
    public WorldGenData? WorldGen { get; set; }

    /// <summary>
    /// Sound events. Null loads them from <see cref="Mods"/> when the scene starts; tests may set them.
    /// </summary>
    public SoundRegistry? SoundEvents { get; set; }

    /// <summary>Bot types. Null loads them from <see cref="Mods"/> when the scene starts; tests may set them.</summary>
    public BotBook? BotTypes { get; set; }

    /// <summary>The raid table. Null loads it from <see cref="Mods"/> when the scene starts; tests may set it.</summary>
    public RaidTable? RaidTable { get; set; }

    /// <summary>Player characters. Null loads them from <see cref="Mods"/> when the scene starts; tests may set them.</summary>
    public CharacterBook? CharacterBook { get; set; }

    /// <summary>Who plays as which character, and drawing them.</summary>
    public CharacterSync Characters { get; private set; } = null!;

    /// <summary>Every player's hotbar slots, and the local player's (UI spec §3).</summary>
    public HotbarSync Hotbar { get; private set; } = null!;

    /// <summary>The local player's Logbook map markers.</summary>
    public MapMarkerSync MapMarkers { get; private set; } = null!;

    /// <summary>The Logbook map's numbers, from <c>res://data/logbook.json</c>.</summary>
    public static Core.Ui.LogbookSettings LogbookSettings => _logbookSettings ??=
        Godot.FileAccess.FileExists(LogbookDataPath) ? Core.Ui.LogbookSettings.Parse(Godot.FileAccess.GetFileAsString(LogbookDataPath)) : new Core.Ui.LogbookSettings();

    private const string LogbookDataPath = "res://data/logbook.json";
    private static Core.Ui.LogbookSettings? _logbookSettings;

    /// <summary>Item pictures for the hotbar and inventory; null without a display.</summary>
    public Ui.ItemIcons? Icons { get; private set; }

    /// <summary>Quests from the enabled mods. Null loads them on ready.</summary>
    public QuestBook? QuestBook { get; set; }

    /// <summary>Lists this world on the LAN while hosting (chunk 24).</summary>
    public LanBeacon Lan { get; private set; } = null!;

    /// <summary>The UDP port the LAN beacon listens on; tests use their own.</summary>
    public int LanPort { get; set; } = NetProtocol.LanPort;

    /// <summary>Fire and its sync to clients (D13).</summary>
    public FireSync Fire { get; private set; } = null!;

    /// <summary>Quests and their sync to clients (chunk 24).</summary>
    public QuestSync Quests { get; private set; } = null!;

    /// <summary>Bots, raids and their sync to clients (chunk 21).</summary>
    public BotSync Bots { get; private set; } = null!;

    /// <summary>Sound playback and its sync to clients.</summary>
    public SoundSync Sounds { get; private set; } = null!;

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

    /// <summary>The joint debug overlay, hidden until something shows it (the Test Sandbox).</summary>
    public JointOverlay JointOverlay { get; private set; } = null!;

    /// <summary>Chunk streaming: the ground, and which Creations are simulated.</summary>
    public WorldStreamer Streamer { get; private set; } = null!;

    /// <summary>Grass, ground cover and bushes drawn round the camera.</summary>
    public FloraField Flora { get; private set; } = null!;

    /// <summary>The look of lakes and the sea, and the tint under them.</summary>
    public WaterSurface Water { get; private set; } = null!;

    /// <summary>Building actions and their sync to clients.</summary>
    public BuildSync Build { get; private set; } = null!;

    /// <summary>The local player's hand tools, once the player has spawned.</summary>
    public ToolBelt? Tools { get; private set; }

    /// <summary>Text chat between players.</summary>
    public Mechanica.Game.Chat.ChatSync Chat { get; private set; } = null!;

    /// <summary>The vehicle gauges sent to seated players.</summary>
    public GaugeSync Gauges { get; private set; } = null!;

    /// <summary>Inventories, containers and crafting, and their sync to clients.</summary>
    public InventorySync Inventory { get; private set; } = null!;

    /// <summary>Health, food, farming, weapons, items on the ground and respawn (chunk 20), and their sync to clients.</summary>
    public SurvivalSync Survival { get; private set; } = null!;

    /// <summary>How soil mounds look: wet or dry, and spilled out of a bag.</summary>
    public SoilLooks SoilLooks { get; private set; } = null!;

    /// <summary>Forces, torques and bearing motors for scripts, bots and external agents.</summary>
    public MotionSync Motion { get; private set; } = null!;

    /// <summary>The host's Lua part scripts.</summary>
    public ScriptSync Scripts { get; private set; } = null!;

    /// <summary>The day and night cycle: the sun, moon, sky and haze over the world's clock.</summary>
    public DayNight DayNight { get; private set; } = null!;

    /// <summary>The logic and power bus, links, part settings and seats (chunk 10).</summary>
    public LogicSync Logic { get; private set; } = null!;

    /// <summary>Part lights and animations: the host's, sent to every client (chunk 23).</summary>
    public PartDisplaySync Display { get; private set; } = null!;

    /// <summary>The Logic Visualizer (F7, and while the Connect Tool is in hand).</summary>
    public LogicOverlay LogicOverlay { get; private set; } = null!;

    /// <summary>The Connect Tool's lights in every player's hand.</summary>
    public ConnectToolLights ConnectLights { get; private set; } = null!;

    /// <summary>The Weld Tool's arms, tip and glow in every player's hand, and the sparks of every weld.</summary>
    public WeldEffects WeldEffects { get; private set; } = null!;

    /// <summary>Every player's Paint Tool colour, tank and mist.</summary>
    public PaintToolLook PaintLook { get; private set; } = null!;

    /// <summary>Sprays from held items (the Fire Extinguisher's foam) on every peer.</summary>
    public HeldSprays Sprays { get; private set; } = null!;

    /// <summary>The aero force overlay (F9): the pushes part scripts make, as arrows.</summary>
    public ForceOverlay ForceOverlay { get; private set; } = null!;

    /// <summary>The Creation Inspector (F8).</summary>
    public CreationInspector Inspector { get; private set; } = null!;

    /// <summary>The part settings panel the Connect Tool opens; null without device input.</summary>
    public PartSettingsPanel? PartSettings { get; private set; }

    /// <summary>The local player's camera in a seat (Ctrl switches interior and chase), when reading device input.</summary>
    public SeatCamera? SeatCamera { get; private set; }

    /// <summary>Watch players: the camera that follows another player (an AI agent, say).</summary>
    public Spectator Spectator { get; private set; } = null!;

    /// <summary>The opening cinematic while it plays (<see cref="PlayOpening"/>), else null.</summary>
    public OpeningCinematic? Opening { get; private set; }

    // True on the host of a world just started from the menu, until the opening cinematic starts with the world.
    private bool _openingWanted;

    /// <summary>The seat's creation hotbar of linked switches and buttons, when reading device input.</summary>
    public CreationHotbar? CreationHotbar { get; private set; }

    /// <summary>Saving and loading the world, and players' saved selves.</summary>
    public SaveSync Saves { get; private set; } = null!;

    /// <summary>Record/replay of the host's Action API calls.</summary>
    public RecordReplay Recorder { get; private set; } = null!;

    /// <summary>The external AI endpoint (WebSocket JSON-RPC), listening only when asked to.</summary>
    public AiEndpoint Ai { get; private set; } = null!;

    /// <summary>The AI agents that joined the host's world as players through <see cref="Ai"/>.</summary>
    public AgentHost AgentHost { get; private set; } = null!;

    /// <summary>Saving, loading and starting worlds for AI agents (the <c>world.*</c> methods).</summary>
    public WorldSession WorldSession { get; private set; } = null!;

    /// <summary>
    /// Replaces this world with the next one, as <see cref="MenuOptions"/> and <see cref="MenuSave"/> describe it.
    /// Null reloads the world scene; tests that host several worlds in one scene set their own.
    /// </summary>
    public Action? ReplaceWorld { get; set; }

    /// <summary>AI notes, recommendations and bug reports, and what led up to them.</summary>
    public AiReports Reports { get; private set; } = null!;

    /// <summary>
    /// The AI reports folder. Null uses <c>user://ai_reports</c> in the real game and a folder under the system's
    /// temporary folder in test scenes, which never write to the player's own reports.
    /// </summary>
    public string? ReportsFolder { get; set; }

    /// <summary>The local player's building by hand, once the player has spawned.</summary>
    public BuildTool? Builder => Tools?.Builder;

    /// <summary>This peer's own player, once spawned.</summary>
    public PlayerController? LocalPlayer => PlayerSync.LocalPlayer;

    public override void _Ready()
    {
        GD.Print($"{MechanicaInfo.GameName} started (physics: {ProjectSettings.GetSetting("physics/3d/physics_engine")})");
        Options ??= TakeMenuOptions() ?? ReadCommandLine();
        Loading = new WorldLoading(Ui.LoadingScreen.LoadSettings());
        if (ReadDeviceInput && (ShowLoading ?? Settings.GameSettings.Persist))
        {
            _loadingScreen = new Ui.LoadingScreen
            {
                Name = "LoadingScreen",
                Loading = Loading,
                World = DescribeWorld,
                Detail = () => Session is not null && !Session.IsHost && Session.HostName.Length > 0 && Session.HostStage < LoadStage.Entering
                    ? Ui.Loc.T("Host: {0}  ·  {1}", Session.HostName, Ui.Loc.T(WorldLoading.Text(Session.HostStage)))
                    : null,
            };
            AddChild(_loadingScreen);
            _loadingScreen.Entering += () =>
            {
                if (_openingWanted)
                    PlayOpening();
            };
        }
        var mods = Mods ??= LoadMods();
        var save = Save ??= TakeMenuSave() ?? ReadSave(Options.LoadPath);
        if (save is null && Options.LoadPath is { } unreadable && Options.Mode != SessionMode.Join)
            Loading.Fail(Ui.Loc.T("The save {0} could not be read.", System.IO.Path.GetFileName(unreadable)));
        if (save is not null && Options.Mode != SessionMode.Join)
            Options = Options with { Seed = save.Seed, GameMode = save.Mode, Difficulty = save.Difficulty, FriendlyFire = save.FriendlyFire };
        else
            Save = save = null;
        var profile = Profile ??= LoadProfile(Options.PlayerName);
        var world = WorldGen ??= LoadWorldGen(mods);
        var recipes = Recipes ??= LoadRecipes(mods);
        var bots = BotTypes ??= LoadBots(mods);
        var raids = RaidTable ??= LoadRaids(mods, bots);
        var quests = QuestBook ??= LoadQuests(mods, recipes, world);
        var characters = CharacterBook ??= LoadCharacters(mods);
        // World data, recipes, bots, raids and quests are part of the content check on join: every peer regenerates
        // the world from the first, shows the recipes in its station screens and the bots the host sends.
        var content = Content ??= Core.ModTools.GameContent.ManifestOf(mods, world, recipes, bots, raids, quests, characters);
        Assets = new PartAssets(mods);
        HeldItems = HeldItemsOf(mods);
        Ui.Loc.UseMods(mods.Mods.Select(m => m.Directory));

        Session = new NetSession { Name = "Session", Content = content, Profile = profile };
        AddChild(Session);
        var players = new Node3D { Name = "Players" };
        AddChild(players);
        PlayerSync = new PlayerSync
        {
            Name = "PlayerSync",
            Session = Session,
            PlayersRoot = players,
            // Every peer generates the same world, so each one knows where the sea is.
            WaterLevel = at => Streamer.Generator.WaterLevelAt(at.X, at.Z),
            SpawnPoint = () => GetNodeOrNull<Node3D>("SpawnPoint")?.GlobalTransform ?? Transform3D.Identity,
        };
        Creations = new CreationWorld { Name = "Creations" };
        AddChild(Creations);
        PlayerSync.Creations = Creations;
        JointOverlay = new JointOverlay { Creations = Creations };
        AddChild(JointOverlay);
        Sounds = new SoundSync { Name = "Sounds", Session = Session, Players = PlayerSync, Creations = Creations, Registry = SoundEvents ??= LoadSounds(mods) };
        AddChild(Sounds);
        AddChild(new Building.SledgeImpacts { Name = "SledgeImpacts", Sounds = Sounds });
        // Before the world, so the chunks' water meshes use its spacing.
        Water = new WaterSurface { Name = "Water", Level = at => Streamer.Generator.WaterLevelAt(at.X, at.Z) };
        AddChild(Water);
        // Before the players, so the spawn has ground on the first frame.
        Streamer = new WorldStreamer
        {
            Name = "World",
            Session = Session,
            PlayerSync = PlayerSync,
            Creations = Creations,
            HoldsAwake = id => Scripts?.Runtime.Signals.IsActive(id) == true,
            QuestDone = id => Quests?.Tracker?.State.IsDone(id) == true,
            SpawnPoint = () => GetNodeOrNull<Node3D>("SpawnPoint")?.GlobalPosition ?? Vector3.Zero,
            WorldGen = WorldGen!,
            Seed = Options.Seed ?? (long)(((ulong)GD.Randi() << 32) | GD.Randi()),
            InitialState = save is null ? null : StateOf(save),
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
        Build = new BuildSync
        {
            Name = "Build",
            Session = Session,
            Players = PlayerSync,
            Creations = Creations,
            Catalog = mods,
            Assets = Assets,
            Sounds = Sounds,
            // The engine's numbers with the mods' game rules on top (paint, sledge, breaking, Lift).
            Settings = BuildSync.LoadSettings(mods),
        };
        Inventory = new InventorySync
        {
            Name = "Inventory",
            Session = Session,
            Sounds = Sounds,
            Readouts = () => Scripts?.Runtime.Readouts,
            Players = PlayerSync,
            Creations = Creations,
            Catalog = mods,
            Recipes = recipes,
            // Making items from nothing is Creative's (D13).
            Settings = InventorySync.LoadSettings() with { AllowGive = Options.GameMode == GameMode.Creative, FreeUpgrades = Options.GameMode == GameMode.Creative },
        };
        Inventory.RegisterActions(Actions.Dispatcher);
        Survival = new SurvivalSync
        {
            Name = "Survival",
            Session = Session,
            Players = PlayerSync,
            Creations = Creations,
            World = Streamer,
            Build = Build,
            Inventory = Inventory,
            Catalog = new LoadedParts(this),
            Assets = Assets,
            Mode = Options.GameMode,
            Difficulty = Options.Difficulty,
            FriendlyFire = Options.FriendlyFire,
            Settings = SurvivalSync.LoadSettings(mods),
        };
        // Before building's actions, so building is paid for in Survival.
        Survival.RegisterActions(Actions.Dispatcher);
        AddChild(Build);
        Build.RegisterActions(Actions.Dispatcher);
        Build.Tools!.Targets = Survival.Actions;
        AddChild(Inventory);
        AddChild(Survival);
        AddChild(new RefineEffects { Name = "RefineEffects", Players = PlayerSync, Sounds = Sounds, Survival = Survival });
        SoilLooks = new SoilLooks { Name = "SoilLooks", Creations = Creations, Survival = Survival, Sounds = Sounds, Catalog = Survival.Catalog };
        AddChild(SoilLooks);
        AddChild(ConnectLights = new ConnectToolLights { Name = "ConnectToolLights", Players = PlayerSync });
        AddChild(WeldEffects = new WeldEffects { Name = "WeldEffects", Players = PlayerSync, Sounds = Sounds });
        Bots = new BotSync
        {
            Name = "Bots",
            Session = Session,
            Players = PlayerSync,
            Survival = Survival,
            World = Streamer,
            KnockbackDrag = Build.Settings!.Sledge.KnockbackDrag,
            Actions = Actions,
            Sounds = Sounds,
            Mods = mods,
            Book = bots,
            RaidTable = raids,
        };
        Bots.RegisterActions(Actions.Dispatcher, unchecked((int)Streamer.Seed));
        AddChild(Bots);
        Flora = new FloraField { Name = "Flora", World = Streamer, Pushers = FloraPushers };
        AddChild(Flora);
        Motion = new MotionSync { Name = "Motion", Session = Session, Creations = Creations };
        AddChild(Motion);
        Motion.RegisterActions(Actions.Dispatcher);
        Scripts = new ScriptSync { Name = "Scripts", Session = Session, Creations = Creations, Motion = Motion, Actions = Actions, Sounds = Sounds, Mods = mods, Survival = Survival };
        AddChild(Scripts);
        Streamer.Clock = () => Scripts.Runtime.Time;
        Fire = new FireSync
        {
            Name = "Fire", Session = Session, Creations = Creations, Survival = Survival, World = Streamer, Scripts = Scripts, Sounds = Sounds,
            Settings = FireSync.LoadSettings(mods),
        };
        Fire.RegisterActions(Actions.Dispatcher);
        Scripts.Runtime.Fire = Fire.Actions;
        Scripts.Runtime.DayLength = () => Streamer.Generator?.Data.Definition.DayLength ?? Mechanica.Core.World.WorldGenDefinition.DefaultDayLength;
        AddChild(DayNight = new DayNight { Name = "DayNight", Session = Session, Clock = () => Scripts.Runtime.Time, DayLength = Scripts.Runtime.DayLength });
        AddChild(Fire);
        Logic = new LogicSync
        {
            Name = "Logic", Session = Session, Players = PlayerSync, Creations = Creations, Build = Build, Scripts = Scripts,
            Creative = Options.GameMode == GameMode.Creative,
        };
        AddChild(Logic);
        Logic.RegisterActions(Actions.Dispatcher);
        Gauges = new GaugeSync { Name = "Gauges", Session = Session, Creations = Creations, Seats = () => Logic.Seats };
        AddChild(Gauges);
        Display = new PartDisplaySync { Name = "Display", Session = Session, Creations = Creations, Scripts = Scripts, InterestOf = peer => CreationSync.InterestOf(peer), ConnectToolOut = () => Tools?.Connector.Active == true };
        AddChild(Display);
        LogicOverlay = new LogicOverlay { Creations = Creations, Players = PlayerSync, Actions = Actions, Build = Build, ReadDeviceInput = ReadDeviceInput, Connector = () => Tools?.Connector };
        AddChild(LogicOverlay);
        ForceOverlay = new ForceOverlay { Creations = Creations, Players = PlayerSync, Actions = Actions, ReadDeviceInput = ReadDeviceInput };
        AddChild(ForceOverlay);
        Inspector = new CreationInspector { Name = "Inspector", Players = PlayerSync, Actions = Actions, ReadDeviceInput = ReadDeviceInput };
        AddChild(Inspector);
        Spectator = new Spectator { Name = "Spectator", Players = PlayerSync, ReadDeviceInput = ReadDeviceInput };
        AddChild(Spectator);
        Quests = new QuestSync
        {
            Name = "Quests",
            Session = Session,
            Players = PlayerSync,
            Creations = Creations,
            World = Streamer,
            Inventory = Inventory,
            Logic = Logic,
            Bots = Bots,
            Fire = Fire,
            Book = quests,
        };
        Quests.RegisterActions(Actions.Dispatcher);
        AddChild(Quests);
        Characters = new CharacterSync
        {
            Name = "Characters",
            Session = Session,
            Players = PlayerSync,
            Assets = new CharacterAssets(mods) { HeldModel = HeldModel, HeldBite = HeldBite },
            Sounds = Sounds,
            Actions = Actions,
            Choices = new CharacterActions(characters),
            Survival = Survival,
            InitialChoice = save is null ? TakeMenuCharacter() : null,
            ReadDeviceInput = ReadDeviceInput,
        };
        Characters.RegisterActions(Actions.Dispatcher);
        Streamer.Npc = Characters.Npc;
        AddChild(PaintLook = new PaintToolLook
        {
            Name = "PaintToolLook", Players = PlayerSync, Palette = Build.Settings!.Paint.Palette, Settings = Characters.Assets.Settings.PaintTool,
        });
        AddChild(Sprays = new HeldSprays
        {
            Name = "HeldSprays", Players = PlayerSync,
            SprayOf = index => HeldItems.IdOf(index) is { } id ? Mods?.Parts.GetValueOrDefault(id)?.Definition.Held?.Spray : null,
        });
        Hotbar = new HotbarSync { Name = "Hotbar", Session = Session, Players = PlayerSync, Actions = Actions, Bars = new HotbarActions(mods) };
        Hotbar.RegisterActions(Actions.Dispatcher);
        AddChild(Hotbar);
        MapMarkers = new MapMarkerSync
        {
            Name = "MapMarkers", Session = Session, Players = PlayerSync, Actions = Actions,
            Markers = new Core.Ui.MapMarkers(LogbookSettings, Streamer.Settings!.WorldSize, Streamer.Settings.Depth),
        };
        MapMarkers.RegisterActions(Actions.Dispatcher);
        AddChild(MapMarkers);
        var toolModels = new Ui.ToolIconModels(Characters.Assets, Assets, Build.Settings!.Lift);
        Survival.ToolModel = toolModels.For;
        if (ReadDeviceInput)
        {
            Icons = new Ui.ItemIcons { Name = "ItemIcons", Catalog = mods, Assets = Assets, ToolModel = toolModels.For };
            AddChild(Icons);
        }
        Chat = new Mechanica.Game.Chat.ChatSync { Name = "Chat", Session = Session };
        Chat.RegisterActions(Actions.Dispatcher);
        AddChild(Chat);
        // Read-only perception and catalog queries for agents, scripts and tools (AI support PR 3).
        var perception = new PerceptionWorld(this);
        new Core.Perception.PerceptionActions(perception).Register(Actions.Dispatcher);
        new Core.Perception.CatalogActions(perception).Register(Actions.Dispatcher);
        Motion.Actions.Sledge = Build.Settings!.Sledge;
        Saves = new SaveSync
        {
            Store = SaveFolder is null ? null : new SaveStore(SaveFolder),
            Characters = Characters.Choices,
            Hotbars = Hotbar.Bars,
            Markers = MapMarkers.Markers,
            Quests = Quests,
            Fire = Fire,
            Bots = Bots,
            Name = "Saves",
            Session = Session,
            Players = PlayerSync,
            Creations = Creations,
            Assets = Assets,
            World = Streamer,
            Inventory = Inventory,
            Survival = Survival,
            Scripts = Scripts,
            Catalog = new LoadedParts(this),
            Content = () => Content!,
        };
        AddChild(Saves);
        AddChild(Characters);
        var newWorld = false;
        if (TakeMenuWorldName() is { } worldName && save is null)
        {
            Saves.WorldName = worldName;
            newWorld = true;
        }
        Lan = new LanBeacon { Name = "Lan", Session = Session, Port = LanPort, Describe = DescribeForLan };
        AddChild(Lan);
        if (save is not null)
        {
            foreach (var diagnostic in save.ContentDifferences(content))
                GD.PushWarning($"[save] '{save.Name}' was saved with different mods: {diagnostic}");
            foreach (var diagnostic in Saves.Apply(save))
                GD.PushWarning($"[save] {diagnostic}");
            GD.Print($"[save] loaded '{save.Name}' ({Creations.Count} creations)");
        }
        if (Options.Mode != SessionMode.Join)
            PlaceWorldParts();
        Recorder = new RecordReplay { Name = "Recorder", Session = Session, Actions = Actions };
        AddChild(Recorder);
        AgentHost = new AgentHost(Session, PlayerSync, Actions.Dispatcher);
        Reports = new AiReports
        {
            Name = "Reports", Session = Session, Actions = Actions, Folder = ReportsFolder ?? (Settings.GameSettings.Persist ? null : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mechanica-test-ai-reports", $"{System.Environment.ProcessId}-{GetInstanceId()}")), Context = DescribeForReport,
            Scripts = () => Scripts.Runtime, Bots = () => Bots.Runtime,
        };
        AddChild(Reports);
        // An agent that loaded this world keeps its endpoint and connection.
        if (AiEndpoint.TakeCarried() is { } carried)
        {
            carried.Session = Session;
            carried.Actions = Actions;
            carried.Log = Recorder;
            carried.Agents = AgentHost;
            Ai = carried;
        }
        else
            Ai = new AiEndpoint { Session = Session, Actions = Actions, Log = Recorder, Agents = AgentHost };
        Ai.Name = "Ai";
        AddChild(Ai);
        // A new world from the menu opens on the crash (an agent starting one carries on without it).
        _openingWanted = newWorld && ReadDeviceInput && Options.Mode != SessionMode.Join && !Ai.IsCarried;
        WorldSession = new WorldSession(this);
        ReplaceWorld ??= () => GetTree().ChangeSceneToFile(MainMenu.WorldScene);
        RemoteEventSources.Connect(this, Ai.Events);
        var modsRoot = ModsFolder ?? ModPaths.ModsRoot;
        var profilePath = ModProfileFile ?? ModPaths.ProfilePath;
        var modTools = new Mechanica.Scripting.Editor.ModToolService(
            new Core.ModTools.ModWorkspace(modsRoot, profilePath, Mechanica.Scripting.Editor.LuaSyntax.Problem),
            new ModToolsHost(this, modsRoot, profilePath), Scripts.Settings!);
        Ai.Modules.Add(new Mechanica.Scripting.Editor.ModToolsModule(modTools, Ai.Events));
        Ai.Modules.Add(new Core.Packages.PackageRpcModule(ModPaths.ModsRoot, ModPaths.PackagesRoot, Ai.Events));
        Ai.Modules.Add(new Core.ModTools.EditorModule(new Core.ModTools.EditorService(modTools.Workspace, new EditorHost(this))));
        Ai.Modules.Add(new Core.Reports.ReportsModule(Reports.Reporter, Ai.Events));
        Ai.Modules.Add(new WorldModule(new WorldControl(WorldSession)));
        // The offline player spawned with PlayerSync, before building existed.
        if (LocalPlayer is { } local)
        {
            AddTools(local);
            SpawnDebugCar();
        }
        Session.StateChanged += state => SetPropsSimulated(state is NetSession.SessionState.Offline or NetSession.SessionState.Hosting);
        if (ReadDeviceInput)
        {
            InventoryScreen? inventory = null;
            Upgrades = new UpgradeDialog
            {
                Name = "UpgradeDialog", Inventory = Inventory, Actions = Actions, Creations = Creations, Catalog = mods, Icons = Icons,
                Aimed = () => inventory?.AimedPart(), OtherScreenOpen = () => inventory?.IsOpen == true,
            };
            inventory = new InventoryScreen
            {
                Name = "InventoryScreen", Inventory = Inventory, Actions = Actions, Players = PlayerSync, Catalog = mods, Hotbar = Hotbar, Icons = Icons, Sounds = Sounds,
                Mode = () => Survival.Own?.Mode, Creations = Creations, Upgrades = Upgrades,
                AllItems = () => HotbarTools.CreativeList(Build.Catalog),
            };
            AddChild(inventory);
            AddChild(Upgrades);
            PartSettings = new PartSettingsPanel
            {
                Name = "PartSettings", Actions = Actions, Creations = Creations, Readouts = () => Display.Readouts, Signals = LogicOverlay, Assets = Assets,
                Inventory = Inventory, Icons = Icons, Creative = () => Survival.Own?.Mode == GameMode.Creative,
            };
            var hudRoot = new Ui.HudRoot { Name = "HudRoot" };
            AddChild(hudRoot);
            Ui.WorldHud? worldHud = null;
            var survivalHud = new SurvivalHud
            {
                Name = "SurvivalHud", Survival = Survival, Actions = Actions, Players = PlayerSync, Catalog = mods, Logic = Logic, Quests = Quests,
                Containers = inventory, Upgrades = Upgrades, PartSettings = PartSettings, TooltipShowing = () => worldHud?.TooltipShowing == true,
                ToolInteraction = () => Tools?.Current.Interaction,
            };
            AddChild(survivalHud);
            worldHud = new Ui.WorldHud
            {
                Name = "WorldHud", Players = PlayerSync, Actions = Actions, Build = Build, Tool = () => Tools?.Current, Sounds = Sounds,
                Interaction = () => survivalHud.Interaction()?.Label, HeldProgress = () => survivalHud.RefineProgress,
            };
            AddChild(worldHud);
            var questLog = new QuestHud
            {
                Name = "QuestHud", Quests = Quests, Actions = Actions, Players = PlayerSync,
                Markers = MapMarkers, MapSettings = LogbookSettings, World = () => Streamer.Generator,
                Holding = () => Opening is { Playing: true },
            };
            AddChild(questLog);
            var maps = new MapHud
            {
                Name = "MapHud", Quests = Quests, Players = PlayerSync, Markers = MapMarkers, Settings = LogbookSettings, World = () => Streamer.Generator,
                KnockoutBags = Survival.OwnKnockoutBags,
            };
            AddChild(maps);
            AddChild(new Ui.TutorialHints
            {
                Name = "TutorialHints",
                Waiting = () => LoadingScreen is not null,
                Tool = () => LocalPlayer is null ? null : Tools?.Current.Name,
                Screens = [("inventory", () => inventory.IsOpen), ("logbook", () => questLog.IsOpen)],
            });
            // F1 opens the page for what the player is doing.
            var pause = new PauseMenu
            {
                Name = "PauseMenu", Session = Session, Saves = Saves, Lan = Lan, Reports = Reports.Store,
                HelpTopic = () => questLog.IsOpen ? "quests.md" : inventory.IsOpen ? "survival.md" : "playing.md",
                World = () => Survival.Own is { } own
                    ? new WorldRules(own.Mode, own.Difficulty, own.FriendlyFire,
                        Session.IsHost ? on => Actions.Request(Core.Survival.SurvivalActions.SetFriendlyFire, new Core.Survival.SetFriendlyFireArgs(on)) : null)
                    : null,
            };
            AddChild(pause);
            hudRoot.Covered = () => inventory.IsOpen || pause.IsOpen || questLog.IsOpen || maps.IsOpen || Ui.ToolWindow.Topmost is not null || Characters.Window is not null;
            AddChild(PartSettings);
            SeatCamera = new SeatCamera { Logic = Logic, Players = PlayerSync, Turn = yaw => _input?.Turn(yaw) };
            AddChild(SeatCamera);
            CreationHotbar = new CreationHotbar
            {
                Logic = Logic, Players = PlayerSync, Creations = Creations, Actions = Actions, Signals = LogicOverlay, Icons = Icons, Belt = () => Tools,
            };
            AddChild(CreationHotbar);
            var snapshots = new Ui.CreationSnapshots { Name = "CreationSnapshots", Assets = Assets };
            AddChild(snapshots);
            Blueprints = new BlueprintMenu
            {
                Name = "BlueprintMenu", Snapshots = snapshots, Lift = () => Tools?.Lift,
                Store = new Core.Saves.BlueprintStore(BlueprintFolder ?? ProjectSettings.GlobalizePath("user://blueprints"), Build.Settings!.Lift.MaxBlueprints),
            };
            AddChild(Blueprints);
            AddChild(new RaidBanner { Name = "RaidBanner", Bots = Bots });
            AddChild(new Ui.Nameplates { Name = "Nameplates", Players = PlayerSync });
            AddChild(new Ui.InUseIndicators { Name = "InUse", Session = Session, Players = PlayerSync, Inventory = Inventory, Logic = Logic, Build = Build, Creations = Creations });
            var gauges = new Ui.VehicleGaugesHud { Name = "VehicleGauges", Gauges = Gauges };
            AddChild(gauges);
            AddChild(new Mechanica.Game.Chat.ChatPanel { Name = "ChatPanel", Chat = Chat, Actions = Actions, Below = () => gauges.Top });
            var compass = new Ui.CompassHud
            {
                Name = "Compass", Players = PlayerSync,
                Waypoints = () => LocalPlayer is { } me
                    ? [.. Quests.Waypoints(me.GlobalPosition.ToNumerics()), .. Survival.OwnKnockoutBags(), .. MapMarkers.Own.Select(m => m.ToWaypoint())]
                    : [],
                Covered = () => Ui.HudRoot.IsCovered,
            };
            AddChild(compass);
            questLog.Compass = compass;
            maps.Compass = compass;
        }

        if (_loadingScreen is not null)
            _loadingScreen.Sounds = SoundEvents;
        Start(Options);
    }

    public override void _Process(double delta)
    {
        UpdateLoading();
        if (_openingWanted && Loading?.Entered == true && LoadingScreen is null)
            PlayOpening();
        if (Loading?.Entered == true && Remote.WorldSession.Changing)
            WorldSession.Entered();
        if (Options?.SelfTest == true)
            SelfTest.Update(this);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Loading?.Stage == LoadStage.Physics)
            _physicsSteps++;
        RefillWorldLoot();
    }

    // Host, about once a second: a world container that refills fills again once it has stood empty for its refill time
    // (WorldLoot.Refill); peers with it open see the new contents.
    private void RefillWorldLoot()
    {
        if (_refillSpots.Count == 0 || --_refillWait > 0 || Scripts?.Runtime is not { } runtime)
            return;
        _refillWait = Engine.PhysicsTicksPerSecond;
        foreach (var creation in Creations.All)
        {
            if (creation.WorldPart is { } key && _refillSpots.TryGetValue(key, out var spot) && WorldLoot.Refill(creation, spot, runtime.Time))
                GD.Print($"[world] {key} refilled from loot table '{spot.Loot!.TableId}'");
        }
    }

    // Moves the load on from what the world actually has (§15): a client first reaches the host and waits for
    // its world, then every peer builds the ground and chunks around the spawn, the scenery in view, runs physics
    // on it and spawns its player. The host passes its stage on to joining clients.
    private void UpdateLoading()
    {
        if (Loading is null || Loading.Entered || Loading.Error is not null)
            return;
        if (Options?.Mode == SessionMode.Join && Session.State == NetSession.SessionState.Offline)
            Loading.Fail(Session.LastMessage.Length > 0 ? Session.LastMessage : Ui.Loc.T("Could not reach the host."));
        else if (Session.State == NetSession.SessionState.Connecting)
            Loading.Report(LoadStage.Connecting);
        else if (!Streamer.HasWorld || (!Session.IsHost && Session.HostStage < LoadStage.Entering))
            Loading.Report(LoadStage.WaitingForHost);
        else if (Streamer.Chunks.Count == 0)
            Loading.Report(LoadStage.Terrain);
        else if (Streamer.PendingChunks > 0)
        {
            Loading.Report(LoadStage.Terrain, 1);
            Loading.Report(LoadStage.Chunks, Streamer.Chunks.Count / (double)(Streamer.Chunks.Count + Streamer.PendingChunks));
        }
        else if (Streamer.SceneryMissing > 0)
        {
            _sceneryMost = Math.Max(_sceneryMost, Streamer.SceneryMissing);
            Loading.Report(LoadStage.Structures, 1 - Streamer.SceneryMissing / (double)_sceneryMost);
        }
        else if (_physicsSteps < Loading.Settings.PhysicsSteps)
            Loading.Report(LoadStage.Physics, _physicsSteps / (double)Loading.Settings.PhysicsSteps);
        else if (LocalPlayer is null)
            Loading.Report(LoadStage.Player);
        else if (Loading.Stage < LoadStage.Finalizing)
            Loading.Report(LoadStage.Finalizing);
        else
            Loading.Report(LoadStage.Entering);
        if (Session.IsHost)
            Session.ReportStage(Loading.Stage);
    }

    // Bottom right of the loading screen: the world's name and mode once known.
    private string? DescribeWorld()
    {
        if (Session is null || Saves is null)
            return null;
        if (Session.IsHost)
            return $"{Saves.WorldName}  ·  {Ui.Loc.T((Options?.GameMode ?? GameMode.Creative).ToString())}";
        var mode = Survival.Own?.Mode;
        return Session.HostName.Length == 0 ? null
            : mode is { } m ? Ui.Loc.T("{0}'s world  ·  {1}", Session.HostName, Ui.Loc.T(m.ToString())) : Ui.Loc.T("{0}'s world", Session.HostName);
    }

    /// <summary>Starts the session <paramref name="options"/> describe. Errors are logged and leave the game offline.</summary>
    public void Start(LaunchOptions options)
    {
        // A host without --password follows Options, so a changed password counts from the next join.
        Session.Password = options.Password ?? "";
        if (options.Mode == SessionMode.Host && options.Password is null)
            Session.PasswordFrom = () => Settings.GameSettings.Current.Gameplay.ServerPassword;
        var error = options.Mode switch
        {
            SessionMode.Host => Session.Host(options.Port, options.MaxPlayers),
            SessionMode.Join => Session.Join(options.Endpoint!.Value),
            _ => Error.Ok,
        };
        if (error != Error.Ok)
        {
            GD.PushError($"[net] {Session.LastMessage}");
            if (Ai.IsCarried)
                Ai.Stop();
            return;
        }
        if (!Session.IsHost)
        {
            if (Ai.IsCarried)
                Ai.Stop();
            return;
        }
        if (options.RecordPath is { } record)
            Recorder.RecordTo(record);
        if (options.ReplayPath is { } replay && Recorder.ReplayFrom(replay) is { } replayError)
            GD.PushError($"[replay] {replayError}");
        if (Ai.IsCarried)
        {
            if (Ai.Resume() is { } resumeError)
                GD.PushError($"[ai] {resumeError}");
        }
        else if (options.AiPort is { } port)
        {
            if (Ai.Listen(port, options.AiBind, options.AiAccess(OS.GetEnvironment)) is { } aiError)
                GD.PushError($"[ai] {aiError}");
        }
    }

    /// <summary>
    /// Switches to newly loaded mods, as the Test Sandbox does after the Part Editor exports: parts, sounds,
    /// part scripts and bot types. World generation, recipes, raids, quests and characters stay as the world
    /// started with them until a new world. Clients already connected stay; the next client to join is checked
    /// against the content now running.
    /// </summary>
    private static HeldItems HeldItemsOf(ModLoadResult mods) =>
        new(mods.Parts.Select(p => KeyValuePair.Create(p.Key, p.Value.Definition)));

    public void ReloadMods(ModLoadResult mods)
    {
        Mods = mods;
        BotTypes = LoadBots(mods);
        Content = Core.ModTools.GameContent.ManifestOf(mods, WorldGen!, Recipes!, BotTypes, RaidTable!, QuestBook!, CharacterBook!);
        Session.Content = Content;
        Assets.SetContent(mods);
        HeldItems = HeldItemsOf(mods);
        Ui.Loc.UseMods(mods.Mods.Select(m => m.Directory));
        Build.SetContent(mods);
        SoundEvents = LoadSounds(mods);
        Sounds.SetRegistry(SoundEvents);
        Scripts.SetContent(mods);
        Bots.SetContent(mods, BotTypes);
        Builder?.ReloadParts();
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

    private static SoundRegistry LoadSounds(ModLoadResult mods)
    {
        var sounds = SoundRegistry.Load(mods);
        foreach (var diagnostic in sounds.Diagnostics)
            GD.PushWarning($"[sound] {diagnostic}");
        return sounds;
    }

    private static BotBook LoadBots(ModLoadResult mods)
    {
        var bots = BotBook.Load(mods);
        foreach (var diagnostic in bots.Diagnostics)
            GD.PushWarning($"[bots] {diagnostic}");
        return bots;
    }

    private static RaidTable LoadRaids(ModLoadResult mods, BotBook bots)
    {
        var raids = RaidTable.Load(mods, bots);
        foreach (var diagnostic in raids.Diagnostics)
            GD.PushWarning($"[raids] {diagnostic}");
        return raids;
    }

    private static QuestBook LoadQuests(ModLoadResult mods, RecipeBook recipes, WorldGenData world)
    {
        var quests = QuestBook.Load(mods, recipes, world);
        foreach (var diagnostic in quests.Diagnostics)
            GD.PushWarning($"[quests] {diagnostic}");
        return quests;
    }

    private static CharacterBook LoadCharacters(ModLoadResult mods)
    {
        var characters = Core.Characters.CharacterBook.Load(mods);
        foreach (var diagnostic in characters.Diagnostics)
            GD.PushWarning($"[characters] {diagnostic}");
        return characters;
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
    // Players, bots and the bodies of live Creations: the plants round them bend out of the way.
    private IEnumerable<Vector3> FloraPushers() =>
        PlayerSync.Players.Values.Select(p => p.GlobalPosition)
            .Concat(Bots.Bodies.Values.Select(b => b.GlobalPosition))
            .Concat(Creations.All.Select(c => Creations.NodeOf(c.Id)).OfType<CreationNode>().SelectMany(n => n.Bodies).Select(b => b.GlobalPosition));

    private void PlaceSpawnPoint()
    {
        if (GetNodeOrNull<Node3D>("SpawnPoint") is not { } spawn)
            return;
        var at = spawn.GlobalPosition;
        spawn.GlobalPosition = Streamer.Generator.Spawn?.ToGodot() ?? new Vector3(at.X, Streamer.Generator.HeightAt(at.X, at.Z), at.Z);
    }

    private static SaveGame? TakeMenuSave()
    {
        var save = MenuSave;
        MenuSave = null;
        return save;
    }

    private static SaveGame? ReadSave(string? path)
    {
        if (path is null)
            return null;
        var diagnostics = new List<DataDiagnostic>();
        var save = SaveStore.Read(path, diagnostics);
        foreach (var diagnostic in diagnostics)
            GD.PushError($"[save] {diagnostic}");
        return save;
    }

    private static WorldState StateOf(SaveGame save)
    {
        var state = new WorldState(save.Seed);
        foreach (var id in save.Destroyed)
            state.Destroy(id, save.Regrow.TryGetValue(id, out var at) ? at : null);
        return state;
    }

    private static PlayerProfile LoadProfile(string? name) => UserProfile(name);

    /// <summary>
    /// Reads (or makes) the player profile at <paramref name="path"/> (default <see cref="ProfilePath"/>),
    /// renamed to <paramref name="name"/> and written back when that changes it.
    /// </summary>
    public static PlayerProfile UserProfile(string? name, string? path = null)
    {
        path ??= ProjectSettings.GlobalizePath(ProfilePath);
        var profile = PlayerProfile.LoadOrCreate(path, OS.GetEnvironment("USER") is { Length: > 0 } user ? user : "Player");
        if (name is not null && PlayerProfile.CleanName(name) != profile.Name)
        {
            profile = profile with { Name = PlayerProfile.CleanName(name) };
            PlayerProfile.Save(path, profile);
        }
        return profile;
    }

    private static LaunchOptions? TakeMenuOptions()
    {
        var options = MenuOptions;
        MenuOptions = null;
        return options;
    }

    private static string? TakeMenuCharacter()
    {
        var character = MenuCharacter;
        MenuCharacter = null;
        return character;
    }

    private static string? TakeMenuWorldName()
    {
        var name = MenuWorldName;
        MenuWorldName = null;
        return name;
    }

    // The game as an AI report records it.
    private Core.Reports.ReportContext DescribeForReport() => new()
    {
        ProtocolVersion = Core.Remote.JsonRpcEndpoint.ProtocolVersion,
        ContentFingerprint = Content?.Fingerprint ?? "",
        Mods = [.. (Content?.Mods ?? []).Select(m => $"{m.Id} {m.Version}")],
        World = Saves.WorldName,
        LoadedFrom = Save is null ? null : Options?.LoadPath ?? (Saves.Store ?? SaveSync.UserStore).PathFor(Save.Name),
        Seed = Streamer.Seed,
        Mode = Options!.GameMode.ToString().ToLowerInvariant(),
        Difficulty = Options.Difficulty.ToString().ToLowerInvariant(),
        FriendlyFire = Survival.Actions?.FriendlyFire ?? Options.FriendlyFire,
        WorldTime = Scripts.Runtime.Time,
        Headless = DisplayServer.GetName() == "headless",
        Players = [.. PlayerSync.Players.Values.OrderBy(p => p.PeerId).Select(p => new Core.Reports.ReportPlayer(p.PeerId, p.DisplayName, p.GlobalPosition.ToNumerics().ToVec3()))],
    };

    // What the LAN beacon says about this world while hosting.
    private LanSession? DescribeForLan() => Session.State != NetSession.SessionState.Hosting ? null : new LanSession(
        Saves.WorldName,
        Session.Profile.Name,
        Session.Port,
        PlayerSync.Players.Count,
        Session.MaxPlayers,
        Options!.GameMode.ToString().ToLowerInvariant(),
        NetProtocol.Version,
        [.. Content!.Mods.Select(m => $"{m.Id} {m.Version}")],
        Session.Password.Length > 0);

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
        SpawnDebugCar();
        if (!ReadDeviceInput)
            return;
        _input = new LocalPlayerInput { Name = "LocalPlayerInput", Target = player, Suspended = () => Spectator.Watching || Opening is { Playing: true } };
        AddChild(_input);
    }

    /// <summary>
    /// Plays the opening cinematic (<see cref="OpeningCinematic"/>) for the local player, as on the first start of a new
    /// world; null when there is no player yet or it doesn't start at the crash site.
    /// </summary>
    public OpeningCinematic? PlayOpening()
    {
        _openingWanted = false;
        if (Opening is not null || LocalPlayer is not { } player || !Streamer.HasWorld)
            return null;
        var settings = OpeningCinematic.LoadSettings();
        if (OpeningCinematic.SceneFor(Streamer.Generator, settings, player.GlobalPosition) is not { } scene)
            return null;
        Opening = new OpeningCinematic
        {
            Name = "Opening", World = Streamer, Player = player, Scene = scene, Fire = Fire, Sounds = Sounds, Input = () => _input,
            ReadDeviceInput = ReadDeviceInput,
        };
        Opening.Finished += () => Opening = null;
        AddChild(Opening);
        return Opening;
    }

    // Host, once the world (and any save) is in: the world's own parts (POI part markers, such as a Workshop's Fabricator
    // or a warehouse's lockers) are matched to their markers, so each marker has exactly one; a new container comes filled
    // with its marker's loot. New ones start parked like a still Creation from a
    // save, so the streamer brings each in with the ground under it when a player comes near.
    private void PlaceWorldParts()
    {
        // A part a player picked up is theirs: its marker is saved as taken, with the destroyed objects, and places no other.
        Build.WorldPartTaken += key =>
        {
            if (WorldParts.ObjectOf(key) is { } id)
                Streamer.State.Destroy(id);
        };
        var spots = WorldParts.SpotsOf(Streamer.Generator);
        _refillSpots = spots.Where(s => s.Loot?.RefillSeconds is not null).ToDictionary(s => s.Key, StringComparer.Ordinal);
        var plan = WorldParts.Reconcile([.. spots, .. Derelicts.SpotsOf(Streamer.Generator)], Creations.All.ToList(), new LoadedParts(this), Build.NextCreationId,
            Streamer.State.IsDestroyed, Streamer.Generator.Seed);
        // A derelict is placed once: its marker is saved as used, like a taken world part's.
        foreach (var key in plan.Used ?? [])
        {
            if (WorldParts.ObjectOf(key) is { } used)
                Streamer.State.Destroy(used);
        }
        foreach (var id in plan.Remove)
            Creations.Remove(id);
        foreach (var id in plan.Moved)
            Creations.Moved(id);
        foreach (var creation in plan.Add)
            Creations.AddParked(creation, Assets);
        foreach (var problem in plan.Problems)
            GD.PushWarning($"[world] a part marker places nothing: {problem}");
        if (plan.Add.Count + plan.Remove.Count + plan.Moved.Count > 0)
            GD.Print($"[world] world parts: {plan.Add.Count} placed ({plan.Used?.Count ?? 0} derelicts), {plan.Moved.Count} put back, {plan.Remove.Count} removed");
    }

    // Once, on the host of a new world, when the debug car is wanted (see DebugCar).
    private void SpawnDebugCar()
    {
        if (DebugCarId is not null || Save is not null || Build is null || Options?.Mode == SessionMode.Join || !Session.IsHost)
            return;
        var settings = DebugCarSpawner.LoadSettings();
        if (!(DebugCar ?? (OS.IsDebugBuild() && GetTree().CurrentScene == this && settings.Enabled)))
            return;
        var spawn = GetNodeOrNull<Node3D>("SpawnPoint")?.GlobalPosition ?? Vector3.Zero;
        DebugCarId = DebugCarSpawner.Spawn(this, spawn, settings).Id;
    }

    private void AddTools(PlayerController player)
    {
        if (Build is null || Survival is null || Tools is not null)
            return;
        Tools = new ToolBelt
        {
            Name = "Tools",
            Player = player,
            Actions = Actions,
            Build = Build,
            Inventory = Inventory,
            Survival = Survival,
            Sounds = Sounds,
            Characters = Characters?.Assets.Settings,
            HeldNumbers = () => HeldItems,
            Hotbar = Hotbar,
            Icons = Icons,
            ReadDeviceInput = ReadDeviceInput,
        };
        AddChild(Tools);
        Tools.Connector.SettingsRequested += (creation, part) => PartSettings?.Open(creation, part);
        Tools.Connector.SignalOf = LogicOverlay.SignalOf;
        Tools.Lift.OpenBlueprints = () => Blueprints?.Open();
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
