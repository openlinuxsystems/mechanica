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
        Sockets = part.Sockets?.Select(s => s with { Extra = Sorted(s.Extra) }).ToList(),
        Inventory = part.Inventory is null ? null : part.Inventory with { Extra = Sorted(part.Inventory.Extra) },
        Crafting = part.Crafting is null ? null : part.Crafting with { Extra = Sorted(part.Crafting.Extra) },
    });

    /// <summary>Serializes a recipe in canonical form.</summary>
    public static string Write(Items.RecipeDefinition recipe) => Serialize(recipe with
    {
        Extra = Sorted(recipe.Extra),
        Inputs = recipe.Inputs?.Select(i => i with { Extra = Sorted(i.Extra) }).ToList(),
        Outputs = recipe.Outputs?.Select(i => i with { Extra = Sorted(i.Extra) }).ToList(),
    });

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, WriteOptions) + "\n";

    private static Dictionary<string, JsonElement>? Sorted(Dictionary<string, JsonElement>? extra) =>
        extra?.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>Writes text as UTF-8 without a byte order mark.</summary>
    public static void WriteFile(string path, string json) => File.WriteAllText(path, json, new UTF8Encoding(false));
}
