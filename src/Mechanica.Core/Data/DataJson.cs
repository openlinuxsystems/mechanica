using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Data;

/// <summary>
/// The one place that configures System.Text.Json for mod data (D8). Reading accepts comments and
/// trailing commas. Writing is pretty-printed with 2-space indents, LF line endings, a trailing
/// newline and a stable key order (schema order, then unknown keys sorted), so
/// <c>Write(Read(Write(x))) == Write(x)</c> and diffs stay small.
/// </summary>
public static class DataJson
{
    /// <summary>Options for reading data files.</summary>
    public static JsonSerializerOptions ReadOptions { get; } = CreateOptions(write: false);

    /// <summary>Options for writing data files.</summary>
    public static JsonSerializerOptions WriteOptions { get; } = CreateOptions(write: true);

    private static JsonSerializerOptions CreateOptions(bool write)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = null,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            AllowDuplicateProperties = false,
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.Strict,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = write,
            IndentSize = 2,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Deserializes <paramref name="json"/>. Throws <see cref="JsonException"/> on malformed input.</summary>
    public static T? Read<T>(string json) => JsonSerializer.Deserialize<T>(json, ReadOptions);

    /// <summary>Serializes a manifest in canonical form.</summary>
    public static string Write(ModManifest manifest) => Serialize(manifest with
    {
        Extra = Sorted(manifest.Extra),
        Dependencies = manifest.Dependencies?.Select(d => d with { Extra = Sorted(d.Extra) }).ToList(),
        Conflicts = manifest.Conflicts?.Select(d => d with { Extra = Sorted(d.Extra) }).ToList(),
    });

    /// <summary>Serializes a mod profile in canonical form.</summary>
    public static string Write(ModProfile profile) => Serialize(profile with
    {
        Extra = Sorted(profile.Extra),
        Mods = profile.Mods?.Select(m => m with { Extra = Sorted(m.Extra) }).ToList(),
    });

    /// <summary>Serializes a part definition in canonical form.</summary>
    public static string Write(PartDefinition part) => Serialize(part with
    {
        Extra = Sorted(part.Extra),
        Visual = part.Visual is null ? null : part.Visual with
        {
            Extra = Sorted(part.Visual.Extra),
            Variants = part.Visual.Variants?.Select(v => v with { Extra = Sorted(v.Extra) }).ToList(),
        },
        Collision = part.Collision is null ? null : part.Collision with { Extra = Sorted(part.Collision.Extra) },
        Sockets = part.Sockets?.Select(s => s with { Extra = Sorted(s.Extra), Joint = s.Joint is null ? null : s.Joint with { Extra = Sorted(s.Joint.Extra) } }).ToList(),
        Inventory = part.Inventory is null ? null : part.Inventory with { Extra = Sorted(part.Inventory.Extra) },
        Crafting = part.Crafting is null ? null : part.Crafting with { Extra = Sorted(part.Crafting.Extra) },
        Logic = part.Logic is null ? null : part.Logic with { Extra = Sorted(part.Logic.Extra) },
        Settings = part.Settings?.Select(s => s with { Extra = Sorted(s.Extra) }).ToList(),
        Seat = part.Seat is null ? null : part.Seat with { Extra = Sorted(part.Seat.Extra) },
    });

    /// <summary>Serializes a recipe in canonical form.</summary>
    public static string Write(Items.RecipeDefinition recipe) => Serialize(recipe with
    {
        Extra = Sorted(recipe.Extra),
        Inputs = recipe.Inputs?.Select(i => i with { Extra = Sorted(i.Extra) }).ToList(),
        Outputs = recipe.Outputs?.Select(i => i with { Extra = Sorted(i.Extra) }).ToList(),
    });

    /// <summary>Serializes a sound bank file in canonical form.</summary>
    public static string Write(Sound.SoundBankFile bank) => Serialize(bank with
    {
        Extra = Sorted(bank.Extra),
        Events = bank.Events?.Select(e => e with
        {
            Extra = Sorted(e.Extra),
            Parameters = e.Parameters?.Select(p => p with { Extra = Sorted(p.Extra) }).ToList(),
        }).ToList(),
    });

    /// <summary>Serializes a bot definition in canonical form.</summary>
    public static string Write(Bots.BotDefinition bot) => Serialize(bot with
    {
        Extra = Sorted(bot.Extra),
        Size = bot.Size is null ? null : bot.Size with { Extra = Sorted(bot.Size.Extra) },
        Attacks = bot.Attacks?.Select(a => a with
        {
            Extra = Sorted(a.Extra),
            DamageOverTime = a.DamageOverTime is null ? null : a.DamageOverTime with { Extra = Sorted(a.DamageOverTime.Extra) },
            Slow = a.Slow is null ? null : a.Slow with { Extra = Sorted(a.Slow.Extra) },
        }).ToList(),
        Drops = bot.Drops?.Select(d => d with { Extra = Sorted(d.Extra) }).ToList(),
    });

    /// <summary>Serializes a raid table in canonical form.</summary>
    public static string Write(Bots.RaidTableDefinition raids) => Serialize(raids with
    {
        Extra = Sorted(raids.Extra),
        Levels = raids.Levels?.Select(l => l with
        {
            Extra = Sorted(l.Extra),
            Bots = l.Bots?.Select(b => b with { Extra = Sorted(b.Extra) }).ToList(),
        }).ToList(),
    });

    /// <summary>Serializes a POI prefab in canonical form.</summary>
    public static string Write(World.PoiPrefab poi) => Serialize(poi with
    {
        Extra = Sorted(poi.Extra),
        Placement = poi.Placement is null ? null : poi.Placement with { Extra = Sorted(poi.Placement.Extra) },
        Terrain = poi.Terrain?.Select(t => t with { Extra = Sorted(t.Extra) }).ToList(),
        Pieces = poi.Pieces?.Select(p => p with { Extra = Sorted(p.Extra) }).ToList(),
        Markers = poi.Markers?.Select(m => m with { Extra = Sorted(m.Extra) }).ToList(),
    });

    /// <summary>Serializes a quest in canonical form.</summary>
    public static string Write(Quests.QuestDefinition quest) => Serialize(quest with
    {
        Extra = Sorted(quest.Extra),
        Steps = quest.Steps?.Select(s => s with
        {
            Extra = Sorted(s.Extra),
            Objective = s.Objective is null ? null : s.Objective with { Extra = Sorted(s.Objective.Extra) },
            Dialogue = s.Dialogue?.Select(l => l with { Extra = Sorted(l.Extra) }).ToList(),
            Choices = s.Choices?.Select(c => c with
            {
                Extra = Sorted(c.Extra),
                Dialogue = c.Dialogue?.Select(l => l with { Extra = Sorted(l.Extra) }).ToList(),
            }).ToList(),
        }).ToList(),
        Rewards = quest.Rewards is null ? null : quest.Rewards with
        {
            Extra = Sorted(quest.Rewards.Extra),
            Items = quest.Rewards.Items?.Select(i => i with { Extra = Sorted(i.Extra) }).ToList(),
        },
        Outro = quest.Outro?.Select(l => l with { Extra = Sorted(l.Extra) }).ToList(),
    });

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, WriteOptions) + "\n";

    private static Dictionary<string, JsonElement>? Sorted(Dictionary<string, JsonElement>? extra) =>
        extra?.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>Writes text as UTF-8 without a byte order mark.</summary>
    public static void WriteFile(string path, string json) => File.WriteAllText(path, json, new UTF8Encoding(false));
}
