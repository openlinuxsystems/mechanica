using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Net;
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
public sealed record SavedPlayer(string Id, string Name, IReadOnlyList<SavedSlot> Inventory, float? Health = null, Vec3? Position = null, float Yaw = 0,
    SavedPart? Bed = null);

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
/// crafting and soil), part script state, and every player the host has seen by profile id. The host
/// writes it; clients get the loaded world through normal late-join sync.
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

    /// <summary>World generation seed.</summary>
    public long Seed { get; init; }

    /// <summary>Mods the world ran, in load order.</summary>
    public IReadOnlyList<ModStamp> Mods { get; init; } = [];

    /// <summary>Content fingerprint the world ran (see <see cref="ContentManifest"/>).</summary>
    public string Fingerprint { get; init; } = "";

    /// <summary>World objects (trees, rocks) that were destroyed.</summary>
    public IReadOnlyList<WorldObjectId> Destroyed { get; init; } = [];

    /// <summary>Each Creation as <see cref="CreationJson"/> text.</summary>
    public IReadOnlyList<string> Creations { get; init; } = [];

    /// <summary>Part script state tables.</summary>
    public IReadOnlyList<SavedScriptState> Scripts { get; init; } = [];

    /// <summary>Players by profile id, sorted by id.</summary>
    public IReadOnlyList<SavedPlayer> Players { get; init; } = [];

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
            Seed = Seed,
            Mods = [.. Mods.Select(m => new ModData { Id = m.Id, Version = m.Version })],
            Fingerprint = Fingerprint,
            Destroyed = Destroyed.Count == 0 ? null : [.. Destroyed.Select(d => d.ToString()).Order(StringComparer.Ordinal)],
            Creations = [.. Creations.Select(c => JsonDocument.Parse(c).RootElement.Clone())],
            Scripts = Scripts.Count == 0 ? null : [.. Scripts.OrderBy(s => s.Creation).ThenBy(s => s.Part)
                .Select(s => new ScriptData { Creation = s.Creation, Part = s.Part, State = JsonSerializer.SerializeToElement(s.State) })],
            Players = [.. Players.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => new PlayerData
            {
                Id = p.Id,
                Name = p.Name,
                Health = p.Health,
                Position = p.Position,
                Yaw = p.Yaw == 0 ? null : p.Yaw,
                Bed = p.Bed is { } bed ? new PartData { Creation = bed.Creation, Part = bed.Part } : null,
                Inventory = p.Inventory.Count == 0 ? null : [.. p.Inventory.OrderBy(s => s.Slot).Select(s => new SlotData { Slot = s.Slot, Item = s.Item, Count = s.Count })],
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

        var destroyed = new List<WorldObjectId>();
        foreach (var (text, i) in (data.Destroyed ?? []).Select((t, i) => (t, i)))
        {
            if (WorldObjectId.TryParse(text, out var id))
                destroyed.Add(id);
            else
                sink.Error(invalid, $"$.destroyed[{i}]", $"'{text}' is not a world object id.");
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
            players.Add(new SavedPlayer(p.Id!, PlayerProfile.CleanName(p.Name), slots, p.Health, p.Position, p.Yaw ?? 0, bed));
        }

        if (sink.ErrorCount > 0)
            return null;
        return new SaveGame
        {
            Name = string.IsNullOrWhiteSpace(data.Name) ? Path.GetFileNameWithoutExtension(file) : data.Name.Trim(),
            SavedAt = data.SavedAt ?? DateTimeOffset.MinValue,
            Mode = data.Mode!.Value,
            Seed = data.Seed!.Value,
            Mods = [.. (data.Mods ?? []).Where(m => m?.Id is not null).Select(m => new ModStamp(m.Id!, m.Version ?? ""))],
            Fingerprint = data.Fingerprint ?? "",
            Destroyed = destroyed,
            Creations = creations,
            Scripts = scripts,
            Players = players,
            Extra = data.Extra,
        };
    }

    private sealed record SaveData
    {
        public int? FormatVersion { get; init; }
        public string? Name { get; init; }
        public DateTimeOffset? SavedAt { get; init; }
        public GameMode? Mode { get; init; }
        public long? Seed { get; init; }
        public List<ModData>? Mods { get; init; }
        public string? Fingerprint { get; init; }
        public List<string>? Destroyed { get; init; }
        public List<JsonElement>? Creations { get; init; }
        public List<ScriptData>? Scripts { get; init; }
        public List<PlayerData>? Players { get; init; }

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
        public List<SlotData>? Inventory { get; init; }
    }

    private sealed record PartData
    {
        public int? Creation { get; init; }
        public int? Part { get; init; }
    }

    private sealed record SlotData
    {
        public int? Slot { get; init; }
        public string? Item { get; init; }
        public int? Count { get; init; }
    }
}
