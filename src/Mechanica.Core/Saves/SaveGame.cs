using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Bots;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Fire;
using Mechanica.Core.Net;
using Mechanica.Core.Quests;
using Mechanica.Core.Survival;
using Mechanica.Core.World;

namespace Mechanica.Core.Saves;

/// <summary>One filled inventory slot in a save.</summary>
/// <param name="Slot">Slot index.</param>
/// <param name="Item">Qualified item id.</param>
/// <param name="Count">How many.</param>
public sealed record SavedSlot(int Slot, string Item, int Count);

/// <summary>
/// A player as the host last saw them, keyed by their <see cref="PlayerProfile"/> id: their inventory,
/// health and where they stood.
/// </summary>
/// <param name="Id">Profile id.</param>
/// <param name="Name">Display name when saved.</param>
/// <param name="Inventory">Filled slots.</param>
/// <param name="Health">Health, or null for full (and for a player who was dead).</param>
/// <param name="Position">Feet position in metres, or null to spawn normally.</param>
/// <param name="Yaw">Facing in radians.</param>
/// <param name="Bed">The respawn bed they set, or null.</param>
/// <param name="Character">The qualified id of the character they chose in this world, or null when they have not chosen.</param>
/// <param name="Hotbar">What each of their ten hotbar slots holds (null entries are empty), or null for the default hotbar.</param>
/// <param name="Markers">The places they marked on their Logbook map, or null for none.</param>
/// <param name="ItemScripts">The <c>state</c> of their item scripts by qualified item id, or null for none.</param>
public sealed record SavedPlayer(string Id, string Name, IReadOnlyList<SavedSlot> Inventory, float? Health = null, Vec3? Position = null, float Yaw = 0,
    SavedPart? Bed = null, string? Character = null, IReadOnlyList<string?>? Hotbar = null, IReadOnlyList<Ui.MapMarker>? Markers = null,
    IReadOnlyDictionary<string, JsonObject>? ItemScripts = null);

/// <summary>A part of a Creation, by id.</summary>
/// <param name="Creation">Creation id.</param>
/// <param name="Part">Part id.</param>
public sealed record SavedPart(int Creation, int Part);

/// <summary>A part script's <c>state</c> table.</summary>
/// <param name="Creation">Creation id.</param>
/// <param name="Part">Part id in that Creation.</param>
/// <param name="State">The table as JSON.</param>
public sealed record SavedScriptState(int Creation, int Part, JsonObject State);

/// <summary>
/// A saved world (D8: JSON): the seed and game mode it was made with, the mods it ran, destroyed world
/// objects, every Creation in <see cref="CreationJson"/> form (parts, pose, velocity, settings, inventories,
/// crafting, soil and fire), part script state, fires at POI markers, kept bots, the story, and every player the host
/// has seen by profile id. The host writes it; clients get the loaded world through normal late-join sync.
/// </summary>
public sealed record SaveGame
{
    /// <summary>The format version this build reads and writes.</summary>
    public const int FormatVersion = 1;

    /// <summary>Name shown in the load list.</summary>
    public string Name { get; init; } = "World";

    /// <summary>When it was written.</summary>
    public DateTimeOffset SavedAt { get; init; }

    /// <summary>The game mode the world plays in.</summary>
    public GameMode Mode { get; init; } = GameMode.Survival;

    /// <summary>How hard the world is; missing in older saves (Normal).</summary>
    public Difficulty Difficulty { get; init; } = Difficulty.Normal;

    /// <summary>Whether players can hurt each other; missing (off) in older saves and when off.</summary>
    public bool FriendlyFire { get; init; }

    /// <summary>World generation seed.</summary>
    public long Seed { get; init; }

    /// <summary>
    /// Seconds the world had been simulated (the part scripts' <c>world.time()</c> clock), which carries on from
    /// here when it loads, so times the scripts and soil stored stay meaningful. 0 in a save from before it.
    /// </summary>
    public double Time { get; init; }

    /// <summary>Mods the world ran, in load order.</summary>
    public IReadOnlyList<ModStamp> Mods { get; init; } = [];

    /// <summary>Content fingerprint the world ran (see <see cref="ContentManifest"/>).</summary>
    public string Fingerprint { get; init; } = "";

    /// <summary>World objects (trees, rocks) that were destroyed.</summary>
    public IReadOnlyList<WorldObjectId> Destroyed { get; init; } = [];

    /// <summary>Destroyed objects that will stand again (an oil geyser), with the world time (<see cref="Time"/>'s clock) when.</summary>
    public IReadOnlyDictionary<WorldObjectId, double> Regrow { get; init; } = new Dictionary<WorldObjectId, double>();

    /// <summary>Each Creation as <see cref="CreationJson"/> text.</summary>
    public IReadOnlyList<string> Creations { get; init; } = [];

    /// <summary>Part script state tables.</summary>
    public IReadOnlyList<SavedScriptState> Scripts { get; init; } = [];

    /// <summary>Mod scripts' <c>state</c> (<c>mod.json</c> <c>scripts</c>), by qualified script path.</summary>
    public IReadOnlyDictionary<string, JsonObject> ModScripts { get; init; } = new Dictionary<string, JsonObject>();

    /// <summary>Players by profile id, sorted by id.</summary>
    public IReadOnlyList<SavedPlayer> Players { get; init; } = [];

    /// <summary>The story: quest progress, flags and revealed POIs. Null in a save from before quests.</summary>
    public QuestState? Quests { get; init; }

    /// <summary>
    /// Fires at POI markers (the crash site's), out ones at strength 0; null in a save from before fire, whose
    /// sites are lit afresh. Burning parts are saved on their parts in <see cref="Creations"/>.
    /// </summary>
    public IReadOnlyList<SavedSiteFire>? Fires { get; init; }

    /// <summary>Kept bots (domesticated animals), in the order they were saved. Wild bots, guards and raiders are not saved.</summary>
    public IReadOnlyList<SavedBot> Bots { get; init; } = [];

    /// <summary>Keys this build does not know, kept as they are.</summary>
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>What has to change for <paramref name="current"/> to match the mods this world ran; empty when it matches.</summary>
    public IReadOnlyList<string> ContentDifferences(ContentManifest current) =>
        ContentManifest.Differences(new ContentManifest(Mods, Fingerprint), current);

    /// <summary>The player saved under <paramref name="profileId"/>, or null.</summary>
    public SavedPlayer? Player(string profileId) => Players.FirstOrDefault(p => p.Id == profileId);

    /// <summary>
    /// Reads every Creation against <paramref name="catalog"/>. A Creation that no longer reads (a part whose
    /// mod is gone) is left out with its errors in <paramref name="diagnostics"/>; the rest still load.
    /// </summary>
    public IReadOnlyList<Creation> ReadCreations(IPartCatalog catalog, string file, List<DataDiagnostic> diagnostics)
    {
        var creations = new List<Creation>();
        for (var i = 0; i < Creations.Count; i++)
        {
            if (CreationJson.Read(Creations[i], catalog, $"{file} creations[{i}]", diagnostics) is { } creation)
                creations.Add(creation);
        }
        return creations;
    }

    /// <summary>Serializes the save in canonical form.</summary>
    public string Write()
    {
        var data = new SaveData
        {
            FormatVersion = FormatVersion,
            Name = Name,
            SavedAt = SavedAt,
            Mode = Mode,
            Difficulty = Difficulty == Difficulty.Normal ? null : Difficulty,
            FriendlyFire = FriendlyFire ? true : null,
            Seed = Seed,
            Time = Time > 0 ? Time : null,
            Mods = [.. Mods.Select(m => new ModData { Id = m.Id, Version = m.Version })],
            Fingerprint = Fingerprint,
            Destroyed = Destroyed.Count == 0 ? null : [.. Destroyed.Select(d => d.ToString()).Order(StringComparer.Ordinal)],
            Regrow = Regrow.Count == 0 ? null : new SortedDictionary<string, double>(Regrow.ToDictionary(r => r.Key.ToString(), r => r.Value), StringComparer.Ordinal),
            Creations = [.. Creations.Select(DataJson.Element)],
            Scripts = Scripts.Count == 0 ? null : [.. Scripts.OrderBy(s => s.Creation).ThenBy(s => s.Part)
                .Select(s => new ScriptData { Creation = s.Creation, Part = s.Part, State = JsonSerializer.SerializeToElement(s.State) })],
            ModScripts = ModScripts.Count == 0 ? null
                : new SortedDictionary<string, JsonElement>(ModScripts.ToDictionary(m => m.Key, m => JsonSerializer.SerializeToElement(m.Value)), StringComparer.Ordinal),
            Players = [.. Players.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => new PlayerData
            {
                Id = p.Id,
                Name = p.Name,
                Health = p.Health,
                Position = p.Position,
                Yaw = p.Yaw == 0 ? null : p.Yaw,
                Bed = p.Bed is { } bed ? new PartData { Creation = bed.Creation, Part = bed.Part } : null,
                Character = p.Character,
                Hotbar = p.Hotbar is { } bar ? [.. bar] : null,
                Markers = p.Markers is { Count: > 0 } markers ? [.. markers.Select(m => new MarkerData { Id = m.Id, Name = m.Name, X = m.X, Y = m.Y, Z = m.Z })] : null,
                Inventory = p.Inventory.Count == 0 ? null : [.. p.Inventory.OrderBy(s => s.Slot).Select(s => new SlotData { Slot = s.Slot, Item = s.Item, Count = s.Count })],
                ItemScripts = p.ItemScripts is { Count: > 0 } items
                    ? new SortedDictionary<string, JsonElement>(items.ToDictionary(i => i.Key, i => JsonSerializer.SerializeToElement(i.Value)), StringComparer.Ordinal)
                    : null,
            })],
            Quests = Quests?.ToElement(),
            Fires = Fires is null ? null : [.. Fires.OrderBy(f => f.Site).ThenBy(f => f.Marker)
                .Select(f => new FireData { Site = f.Site, Marker = f.Marker, Strength = f.Strength })],
            Bots = Bots.Count == 0 ? null : [.. Bots.Select(b => new BotData
            {
                Type = b.Type, Position = b.Position, Yaw = b.Yaw, Home = b.Home, Health = b.Health, Look = b.Look, BreedIn = b.BreedIn,
                DueIn = b.DueIn, DueLook = b.DueIn is null ? null : b.DueLook, GrowsIn = b.GrowsIn, Meals = b.Meals == 0 ? null : b.Meals,
                ProduceIn = b.ProduceIn,
            })],
            Extra = Extra?.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
        };
        return JsonSerializer.Serialize(data, DataJson.WriteOptions) + "\n";
    }

    /// <summary>
    /// Parses and checks a save. Problems go to <paramref name="diagnostics"/> with <paramref name="file"/> as their
    /// location; returns null on any error. Creations are only checked to be objects here; <see cref="ReadCreations"/>
    /// reads them against the loaded parts.
    /// </summary>
    public static SaveGame? Read(string json, string file, List<DataDiagnostic> diagnostics)
    {
        var sink = new DiagnosticSink(file, diagnostics);
        var data = ModLoader.ParseJson<SaveData>(json, sink);
        if (data is null)
            return null;
        const string invalid = DiagnosticCodes.SaveInvalid;
        if (data.FormatVersion is null)
            sink.Error(invalid, "$.format_version", "format_version is required.");
        else if (data.FormatVersion != FormatVersion)
            sink.Error(DiagnosticCodes.UnsupportedFormat, "$.format_version",
                $"format_version {data.FormatVersion} is not supported; this build reads {FormatVersion}.");
        if (data.Seed is null)
            sink.Error(invalid, "$.seed", "seed is required.");
        if (data.Mode is null)
            sink.Error(invalid, "$.mode", "mode is required.");
        if (data.Time is { } time && !(double.IsFinite(time) && time >= 0))
            sink.Error(invalid, "$.time", "time must be 0 or more seconds.");

        var destroyed = new List<WorldObjectId>();
        foreach (var (text, i) in (data.Destroyed ?? []).Select((t, i) => (t, i)))
        {
            if (WorldObjectId.TryParse(text, out var id))
                destroyed.Add(id);
            else
                sink.Error(invalid, $"$.destroyed[{i}]", $"'{text}' is not a world object id.");
        }
        var regrow = new Dictionary<WorldObjectId, double>();
        foreach (var (text, at) in data.Regrow ?? [])
        {
            if (!WorldObjectId.TryParse(text, out var id) || !destroyed.Contains(id))
                sink.Error(invalid, $"$.regrow.{text}", $"'{text}' is not a destroyed world object.");
            else if (!double.IsFinite(at))
                sink.Error(invalid, $"$.regrow.{text}", "the regrow time must be a number.");
            else
                regrow[id] = at;
        }

        var creations = new List<string>();
        var creationIds = new HashSet<int>();
        foreach (var (element, i) in (data.Creations ?? []).Select((e, i) => (e, i)))
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                sink.Error(invalid, $"$.creations[{i}]", "a creation must be an object.");
                continue;
            }
            if (element.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var creationId) && !creationIds.Add(creationId))
                sink.Error(invalid, $"$.creations[{i}].id", $"creation id {creationId} is used twice.");
            creations.Add(element.GetRawText());
        }

        var scripts = new List<SavedScriptState>();
        foreach (var (s, i) in (data.Scripts ?? []).Select((s, i) => (s, i)))
        {
            if (s?.Creation is not { } creation || s.Part is not { } part || s.State is not { ValueKind: JsonValueKind.Object } state)
                sink.Error(invalid, $"$.scripts[{i}]", "a script state needs creation, part and a state object.");
            else
                scripts.Add(new SavedScriptState(creation, part, JsonNode.Parse(state.GetRawText())!.AsObject()));
        }

        var modScripts = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var (key, state) in data.ModScripts ?? [])
        {
            if (state.ValueKind != JsonValueKind.Object)
                sink.Error(invalid, $"$.mod_scripts.{key}", "a mod script's state must be an object.");
            else
                modScripts[key] = JsonNode.Parse(state.GetRawText())!.AsObject();
        }

        var players = new List<SavedPlayer>();
        var playerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (p, i) in (data.Players ?? []).Select((p, i) => (p, i)))
        {
            var path = $"$.players[{i}]";
            if (p is null || !PlayerProfile.IsValidId(p.Id))
            {
                sink.Error(invalid, $"{path}.id", "a player needs a profile id (32 lowercase hex characters).");
                continue;
            }
            if (!playerIds.Add(p.Id!))
                sink.Error(invalid, $"{path}.id", $"player {p.Id} is listed twice.");
            if (p.Health is { } health && (!float.IsFinite(health) || health < 0))
                sink.Error(invalid, $"{path}.health", "health must be 0 or more.");
            if (p.Position is { } at && !(float.IsFinite(at.X) && float.IsFinite(at.Y) && float.IsFinite(at.Z)))
                sink.Error(invalid, $"{path}.position", "position must be finite.");
            var slots = new List<SavedSlot>();
            var used = new HashSet<int>();
            foreach (var (slot, j) in (p.Inventory ?? []).Select((s, j) => (s, j)))
            {
                if (slot?.Slot is not { } index || slot.Item is null || slot.Count is not { } count || index < 0 || count < 1)
                    sink.Error(invalid, $"{path}.inventory[{j}]", "a slot needs slot (0 or more), item and count (1 or more).");
                else if (!used.Add(index))
                    sink.Error(invalid, $"{path}.inventory[{j}].slot", $"slot {index} is listed twice.");
                else
                    slots.Add(new SavedSlot(index, slot.Item, count));
            }
            SavedPart? bed = null;
            if (p.Bed is { } b)
            {
                if (b.Creation is { } bc && b.Part is { } bp)
                    bed = new SavedPart(bc, bp);
                else
                    sink.Error(invalid, $"{path}.bed", "a bed needs creation and part.");
            }
            if (p.Character is { } character && !IsQualifiedId(character))
                sink.Error(invalid, $"{path}.character", $"'{character}' is not a character id (mod_id:character_id).");
            if (p.Hotbar is { Count: > Items.HotbarActions.SlotCount })
                sink.Error(invalid, $"{path}.hotbar", $"a hotbar has at most {Items.HotbarActions.SlotCount} slots.");
            var markers = new List<Ui.MapMarker>();
            foreach (var (marker, j) in (p.Markers ?? []).Select((m, j) => (m, j)))
            {
                if (marker is not { Id: { } markerId, X: var x, Y: var y, Z: var z } || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                    sink.Error(invalid, $"{path}.markers[{j}]", "a marker needs an id and a finite x, y and z.");
                else
                    markers.Add(new Ui.MapMarker(markerId, marker.Name ?? "", x, y, z));
            }
            var itemScripts = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (var (item, state) in p.ItemScripts ?? [])
            {
                if (state.ValueKind != JsonValueKind.Object || !IsQualifiedId(item))
                    sink.Error(invalid, $"{path}.item_scripts.{item}", "an item script's state is an object under a qualified item id (mod_id:item_id).");
                else
                    itemScripts[item] = JsonNode.Parse(state.GetRawText())!.AsObject();
            }
            players.Add(new SavedPlayer(p.Id!, PlayerProfile.CleanName(p.Name), slots, p.Health, p.Position, p.Yaw ?? 0, bed, p.Character, p.Hotbar,
                markers.Count == 0 ? null : markers, itemScripts.Count == 0 ? null : itemScripts));
        }

        QuestState? quests = null;
        if (data.Quests is { ValueKind: not JsonValueKind.Null } questElement)
        {
            quests = questElement.ValueKind == JsonValueKind.Object ? QuestState.FromElement(questElement) : null;
            if (quests is null)
                sink.Error(invalid, "$.quests", "quests must be an object with quests, flags and revealed.");
        }

        List<SavedSiteFire>? fires = null;
        if (data.Fires is { } fireData)
        {
            fires = [];
            foreach (var (f, i) in fireData.Select((f, i) => (f, i)))
            {
                if (f?.Site is not { } site || f.Marker is not { } marker || f.Strength is not { } strength)
                    sink.Error(invalid, $"$.fires[{i}]", "a fire needs site, marker and strength.");
                else if (!(float.IsFinite(strength) && strength is >= 0 and <= 1))
                    sink.Error(invalid, $"$.fires[{i}].strength", "strength must be 0 to 1.");
                else
                    fires.Add(new SavedSiteFire(site, marker, strength));
            }
        }

        var bots = new List<SavedBot>();
        foreach (var (b, i) in (data.Bots ?? []).Select((b, i) => (b, i)))
        {
            if (b is not { Type: { } type, Position: { } at, Health: { } health } || !IsQualifiedId(type))
                sink.Error(invalid, $"$.bots[{i}]", "a bot needs a type (mod_id:bot_id), a position and health.");
            else if (!Finite(at) || b.Home is { } home && !Finite(home) || !float.IsFinite(b.Yaw ?? 0) || !(health > 0 && float.IsFinite(health)))
                sink.Error(invalid, $"$.bots[{i}]", "a bot's position, home and yaw must be finite and its health above 0.");
            else if (new[] { b.BreedIn, b.DueIn, b.GrowsIn, b.ProduceIn }.Any(t => t is { } s && !(s >= 0 && float.IsFinite(s))) || b.Meals is < 0)
                sink.Error(invalid, $"$.bots[{i}]", "a bot's times left and meals must be 0 or more.");
            else
                bots.Add(new SavedBot(type, at, b.Yaw ?? 0, b.Home ?? at, health, b.Look, b.BreedIn, b.DueIn, b.DueLook ?? 0, b.GrowsIn, b.Meals ?? 0,
                    b.ProduceIn));
        }

        if (sink.ErrorCount > 0)
            return null;
        return new SaveGame
        {
            Name = string.IsNullOrWhiteSpace(data.Name) ? Path.GetFileNameWithoutExtension(file) : data.Name.Trim(),
            SavedAt = data.SavedAt ?? DateTimeOffset.MinValue,
            Mode = data.Mode!.Value,
            Difficulty = data.Difficulty ?? Difficulty.Normal,
            FriendlyFire = data.FriendlyFire ?? false,
            Seed = data.Seed!.Value,
            Time = data.Time ?? 0,
            Mods = [.. (data.Mods ?? []).Where(m => m?.Id is not null).Select(m => new ModStamp(m.Id!, m.Version ?? ""))],
            Fingerprint = data.Fingerprint ?? "",
            Destroyed = destroyed,
            Regrow = regrow,
            Creations = creations,
            Scripts = scripts,
            ModScripts = modScripts,
            Players = players,
            Quests = quests,
            Fires = fires,
            Bots = bots,
            Extra = data.Extra,
        };
    }

    private static bool Finite(Vec3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool IsQualifiedId(string id)
    {
        var colon = id.IndexOf(':');
        return colon > 0 && Identifiers.IsValid(id[..colon]) && Identifiers.IsValid(id[(colon + 1)..]);
    }

    private sealed record SaveData
    {
        public int? FormatVersion { get; init; }
        public string? Name { get; init; }
        public DateTimeOffset? SavedAt { get; init; }
        public GameMode? Mode { get; init; }
        public Difficulty? Difficulty { get; init; }
        public bool? FriendlyFire { get; init; }
        public long? Seed { get; init; }
        public double? Time { get; init; }
        public List<ModData>? Mods { get; init; }
        public string? Fingerprint { get; init; }
        public List<string>? Destroyed { get; init; }
        public SortedDictionary<string, double>? Regrow { get; init; }
        public List<JsonElement>? Creations { get; init; }
        public List<ScriptData>? Scripts { get; init; }
        public SortedDictionary<string, JsonElement>? ModScripts { get; init; }
        public List<PlayerData>? Players { get; init; }
        public JsonElement? Quests { get; init; }
        public List<FireData>? Fires { get; init; }
        public List<BotData>? Bots { get; init; }

        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; init; }
    }

    private sealed record ModData
    {
        public string? Id { get; init; }
        public string? Version { get; init; }
    }

    private sealed record ScriptData
    {
        public int? Creation { get; init; }
        public int? Part { get; init; }
        public JsonElement? State { get; init; }
    }

    private sealed record PlayerData
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public float? Health { get; init; }
        public Vec3? Position { get; init; }
        public float? Yaw { get; init; }
        public PartData? Bed { get; init; }
        public string? Character { get; init; }
        public List<string?>? Hotbar { get; init; }
        public List<MarkerData?>? Markers { get; init; }
        public List<SlotData>? Inventory { get; init; }
        public SortedDictionary<string, JsonElement>? ItemScripts { get; init; }
    }

    private sealed record MarkerData
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public float X { get; init; } = float.NaN;
        public float Y { get; init; } = float.NaN;
        public float Z { get; init; } = float.NaN;
    }

    private sealed record PartData
    {
        public int? Creation { get; init; }
        public int? Part { get; init; }
    }

    private sealed record FireData
    {
        public int? Site { get; init; }
        public int? Marker { get; init; }
        public float? Strength { get; init; }
    }

    private sealed record BotData
    {
        public string? Type { get; init; }
        public Vec3? Position { get; init; }
        public float? Yaw { get; init; }
        public Vec3? Home { get; init; }
        public float? Health { get; init; }
        public int? Look { get; init; }
        public float? BreedIn { get; init; }
        public float? DueIn { get; init; }
        public int? DueLook { get; init; }
        public float? GrowsIn { get; init; }
        public int? Meals { get; init; }
        public float? ProduceIn { get; init; }
    }

    private sealed record SlotData
    {
        public int? Slot { get; init; }
        public string? Item { get; init; }
        public int? Count { get; init; }
    }
}
