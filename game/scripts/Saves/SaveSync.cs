using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Net;
using Mechanica.Core.Saves;
using Mechanica.Core.Settings;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Creations;
using Mechanica.Game.Items;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Fire;
using Mechanica.Game.Quests;
using Mechanica.Game.Scripting;
using Mechanica.Game.Settings;
using Mechanica.Game.Survival;
using Mechanica.Game.World;

namespace Mechanica.Game.Saves;

/// <summary>
/// Saving and loading worlds on the host (chunk 24). <see cref="Capture"/> gathers the world into a
/// <see cref="SaveGame"/>: seed, mode, destroyed objects, every Creation, part script state and every
/// player the host has seen, keyed by <see cref="PlayerProfile"/> id. Loading happens when the world
/// starts: <see cref="Main"/> seeds the world from the save and calls <see cref="Apply"/>, and clients receive
/// the loaded world through the normal late-join sync. A player who joins (or rejoins, or is the host)
/// gets their saved inventory, health, bed and position back; one who leaves is remembered for the rest of
/// the session and in the next save.
/// </summary>
public partial class SaveSync : Node
{
    /// <summary>The connection, for profiles and who is host.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players, for positions and arrivals.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Every Creation in the world.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Meshes and shapes for loaded Creations.</summary>
    public PartAssets Assets { get; init; } = null!;

    /// <summary>The world, for its seed and destroyed objects.</summary>
    public WorldStreamer World { get; init; } = null!;

    /// <summary>Player inventories.</summary>
    public InventorySync Inventory { get; init; } = null!;

    /// <summary>Health and beds.</summary>
    public SurvivalSync Survival { get; init; } = null!;

    /// <summary>Part scripts, for their state tables.</summary>
    public ScriptSync Scripts { get; init; } = null!;

    /// <summary>Who plays as which character. Optional.</summary>
    public Core.Characters.CharacterActions? Characters { get; init; }

    /// <summary>Every player's hotbar, saved under their profile. Optional.</summary>
    public Core.Items.HotbarActions? Hotbars { get; init; }

    /// <summary>Every player's Logbook map markers, saved with them; null leaves them out.</summary>
    public Core.Ui.MapMarkers? Markers { get; init; }

    /// <summary>Quests, for the story's state. Optional.</summary>
    public QuestSync? Quests { get; init; }

    /// <summary>Fire, for the fires at POI markers. Optional.</summary>
    public FireSync? Fire { get; init; }

    /// <summary>The part catalog Creations and inventories are read against.</summary>
    public IPartCatalog Catalog { get; init; } = null!;

    /// <summary>The content this world runs, recorded in saves.</summary>
    public Func<ContentManifest> Content { get; init; } = () => ContentManifest.Empty;

    /// <summary>Where saves go. Null uses <c>user://saves</c>.</summary>
    public SaveStore? Store { get; set; }

    /// <summary>Everyone the host has seen this world.</summary>
    public PlayerRoster Roster { get; private set; } = new();

    /// <summary>The world's name: the loaded save's, or what it was last saved as.</summary>
    public string WorldName { get; set; } = "World";

    /// <summary>
    /// Saves on the interval chosen in Options (Gameplay, Auto-save) while hosting. Off in test scenes,
    /// which never write to the player's save folder on their own.
    /// </summary>
    public bool AutoSave { get; set; } = GameSettings.Persist;

    private readonly AutoSaveClock _autoSave = new();

    /// <summary>Raised on the host after a save is written, with its path.</summary>
    public event Action<string>? Saved;

    /// <summary>The save folder in the user data folder.</summary>
    public static SaveStore UserStore => new(ProjectSettings.GlobalizePath("user://saves"));

    /// <summary>The player's save folder, or null in test scenes (which never read the player's saves).</summary>
    public static SaveStore? UserStoreIfPersisted => GameSettings.Persist ? UserStore : null;

    public override void _Ready()
    {
        Store ??= UserStore;
        // After SurvivalSync's own handler, so starting items are replaced rather than added to.
        Players.PlayerSpawned += OnPlayerSpawned;
        Players.PlayerLeaving += OnPlayerLeaving;
        foreach (var player in Players.Players.Values)
            OnPlayerSpawned(player);
    }

    public override void _Process(double delta)
    {
        if (AutoSave && Session.IsHost && _autoSave.Tick(delta, GameSettings.Current.Gameplay.AutoSaveMinutes))
            SaveNow();
    }

    public override void _ExitTree()
    {
        Players.PlayerSpawned -= OnPlayerSpawned;
        Players.PlayerLeaving -= OnPlayerLeaving;
    }

    /// <summary>
    /// Host: puts a loaded save's Creations, script state and players into the world. Problems (a part whose
    /// mod is gone) are returned; the rest of the world still loads.
    /// </summary>
    public IReadOnlyList<DataDiagnostic> Apply(SaveGame save)
    {
        var diagnostics = new List<DataDiagnostic>();
        WorldName = save.Name;
        // The scripts' clock carries on from the save, so the times they stored (a soil's last growth) still hold.
        Scripts.Runtime.Time = save.Time;
        foreach (var creation in save.ReadCreations(Catalog, save.Name, diagnostics))
        {
            if (Creations.Find(creation.Id) is null)
                Creations.Add(creation, Assets);
        }
        foreach (var script in save.Scripts)
        {
            if (Creations.Find(script.Creation) is not null && !Scripts.Runtime.ImportState(script.Creation, script.Part, script.State))
                diagnostics.Add(Warning(save.Name, $"creation {script.Creation} part {script.Part} no longer runs a script; its state was dropped."));
        }
        foreach (var (script, state) in save.ModScripts)
        {
            if (!Scripts.Runtime.ImportModState(script, state))
                diagnostics.Add(Warning(save.Name, $"no mod runs the script {script} any more; its state was dropped."));
        }
        if (save.Quests is { } quests && Quests is not null)
            Quests.Load(quests);
        if (save.Fires is { } fires)
            Fire?.Actions?.LoadSites(fires);
        Roster = new PlayerRoster(save.Players);
        // Players already in the world (the host's own) get their saved selves back now.
        foreach (var player in Players.Players.Values)
            Arrive(player);
        return diagnostics;
    }

    /// <summary>Host: the world as it is now, named <paramref name="name"/> (or <see cref="WorldName"/>).</summary>
    public SaveGame Capture(string? name = null)
    {
        foreach (var player in Players.Players.Values)
            Remember(player);
        var content = Content();
        var scripts = new List<SavedScriptState>();
        var runtime = Scripts.Runtime;
        foreach (var creation in Creations.All)
        {
            foreach (var part in creation.Parts)
            {
                if (runtime.ExportState(creation.Id, part.Id) is System.Text.Json.Nodes.JsonObject state && state.Count > 0)
                    scripts.Add(new SavedScriptState(creation.Id, part.Id, state));
            }
        }
        return new SaveGame
        {
            Name = string.IsNullOrWhiteSpace(name) ? WorldName : name.Trim(),
            SavedAt = DateTimeOffset.UtcNow,
            Mode = Survival.Mode,
            Difficulty = Survival.Actions?.Difficulty ?? Survival.Difficulty,
            FriendlyFire = Survival.Actions?.FriendlyFire ?? Survival.FriendlyFire,
            Seed = World.Seed,
            Time = runtime.Time,
            Mods = content.Mods,
            Fingerprint = content.Fingerprint,
            Destroyed = [.. World.State.Destroyed],
            Regrow = World.State.Regrowing.ToDictionary(),
            Creations = [.. Creations.All.OrderBy(c => c.Id).Select(c => { Creations.NodeOf(c.Id)?.SyncToCreation(); return Core.Creations.CreationJson.Write(c); })],
            Scripts = scripts,
            ModScripts = runtime.ExportModStates(),
            Players = [.. Roster.All],
            Quests = Quests?.State,
            Fires = Fire?.Actions?.SaveSites(),
        };
    }

    /// <summary>
    /// Host: writes the world to the save folder as <paramref name="name"/> (or <see cref="WorldName"/>) and
    /// returns the file, or null with a log line when this instance is not the host or the write fails.
    /// </summary>
    public string? SaveNow(string? name = null)
    {
        if (!Session.IsHost)
            return null;
        var save = Capture(name);
        try
        {
            var path = Store!.Write(save);
            WorldName = save.Name;
            _autoSave.Reset();
            SaveThumbnail(SaveStore.ThumbnailFor(path));
            GD.Print($"[save] saved '{save.Name}' to {path} ({save.Creations.Count} creations, {save.Players.Count} players)");
            Saved?.Invoke(path);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"[save] could not save '{save.Name}': {e.Message}");
            return null;
        }
    }

    // A small picture of the screen for the load list; none without a display.
    private void SaveThumbnail(string file)
    {
        if (DisplayServer.GetName() == "headless" || GetViewport()?.GetTexture()?.GetImage() is not { } image || image.IsEmpty())
            return;
        image.Resize((int)SaveScreen.ThumbnailSize.X * 2, (int)SaveScreen.ThumbnailSize.Y * 2, Image.Interpolation.Bilinear);
        if (image.SavePng(file) != Error.Ok)
            GD.PushWarning($"[save] could not write the picture {file}");
    }

    private void OnPlayerSpawned(PlayerController player)
    {
        if (Session.IsHost)
            Arrive(player);
    }

    private void OnPlayerLeaving(PlayerController player)
    {
        if (Session.IsHost)
            Remember(player);
    }

    // Gives a player their saved inventory, health, bed and position.
    private void Arrive(PlayerController player)
    {
        if (Session.ProfileOf(player.PeerId) is not { } profile || Roster.Find(profile.Id) is not { } saved)
            return;
        if (saved.Character is not null)
            Characters?.Restore(player.PeerId, saved.Character);
        Hotbars?.Restore(player.PeerId, saved.Hotbar);
        Markers?.Restore(player.PeerId, saved.Markers);
        if (Inventory.Items is { } items)
        {
            foreach (var problem in PlayerRoster.Restore(items.PlayerInventory(player.PeerId), saved.Inventory))
                GD.PushWarning($"[save] {profile.Name}'s inventory: {problem}");
        }
        Survival.Actions?.Restore(player.PeerId, saved.Health, saved.Bed is { } bed ? (bed.Creation, bed.Part) : null);
        if (saved.Position is { } at)
            Players.HostTeleport(player.PeerId, new Vector3(at.X, at.Y, at.Z), saved.Yaw);
        GD.Print($"[save] {profile.Name} is back");
    }

    // Writes a player's current self into the roster.
    private void Remember(PlayerController player)
    {
        if (Session.ProfileOf(player.PeerId) is not { } profile)
            return;
        var inventory = Inventory.Items?.PlayerInventory(player.PeerId);
        var actions = Survival.Actions;
        var dead = actions?.IsDead(player.PeerId) == true;
        var at = player.GlobalPosition;
        var bed = actions?.RespawnBedOf(player.PeerId);
        Roster.Store(new SavedPlayer(
            profile.Id,
            profile.Name,
            inventory is null ? [] : PlayerRoster.SlotsOf(inventory),
            dead ? null : actions?.HealthOf(player.PeerId),
            // A dead player comes back at their spawn or bed, not where they fell.
            dead ? null : new Vec3(at.X, at.Y, at.Z),
            player.Rotation.Y,
            bed is { } b ? new SavedPart(b.Creation, b.Part) : null,
            Characters?.ChosenBy(player.PeerId),
            Hotbars?.SavedOf(player.PeerId),
            Markers?.SavedOf(player.PeerId)));
    }

    private static DataDiagnostic Warning(string file, string message) =>
        new(DiagnosticSeverity.Warning, DiagnosticCodes.SaveInvalid, file, null, null, message);
}
