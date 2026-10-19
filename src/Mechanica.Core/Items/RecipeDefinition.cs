using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Items;

/// <summary>
/// One crafting recipe, loaded from <c>recipes/**/*.json</c> in a mod (one recipe per file, like parts).
/// A station whose part id is in <see cref="Stations"/> turns <see cref="Inputs"/> into
/// <see cref="Outputs"/> in <see cref="Time"/> seconds (divided by the station's speed). Item and
/// station ids are <c>id</c> (vanilla) or <c>mod_id:id</c>. Keys are snake_case.
/// <code>
/// {
///   "id": "chest",
///   "stations": ["fabricator", "portable_fabricator"],
///   "inputs": [ { "item": "scrap_wood", "count": 4 }, { "item": "scrap_metal", "count": 2 } ],
///   "outputs": [ { "item": "chest", "count": 1 } ],
///   "time": 4
/// }
/// </code>
/// </summary>
public sealed record RecipeDefinition
{
    /// <summary>Id, unique within the mod (lowercase snake_case). Usually the main output's id.</summary>
    public string? Id { get; init; }

    /// <summary>Display name; omitted means the first output's name.</summary>
    public string? Name { get; init; }

    /// <summary>Station part ids that can run it (parts with <c>crafting</c>).</summary>
    public List<string>? Stations { get; init; }

    /// <summary>Items used up, taken from the crafting player's inventory when the job is queued.</summary>
    public List<RecipeItem>? Inputs { get; init; }

    /// <summary>Items made, put in the station's inventory when the job finishes.</summary>
    public List<RecipeItem>? Outputs { get; init; }

    /// <summary>Seconds one craft takes at speed 1 (0 or more).</summary>
    public float? Time { get; init; }

    /// <summary>Keys this build does not know. Kept so a round-trip never loses data.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>An item and how many of it, in a recipe.</summary>
public sealed record RecipeItem
{
    /// <summary>Item id, <c>id</c> or <c>mod_id:id</c>.</summary>
    public string? Item { get; init; }

    /// <summary>How many (1 or more).</summary>
    public int? Count { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
