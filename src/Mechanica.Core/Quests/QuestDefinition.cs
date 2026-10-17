using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Quests;

/// <summary>
/// A quest as data (<c>quests/*.json</c> in a mod, D8): steps done in order, each with an objective, the
/// lines said when it begins and, for a story branch, the choices the players pick from; then rewards.
/// The file name is the quest id. Quests start on their own once every quest in <see cref="Requires"/> is
/// done, so a mod adds to the story by naming the quest its own follows.
/// </summary>
public sealed record QuestDefinition
{
    /// <summary>Optional; must match the file name when given.</summary>
    public string? Id { get; init; }

    /// <summary>Display name.</summary>
    public string? Name { get; init; }

    /// <summary>One line for the quest log.</summary>
    public string? Summary { get; init; }

    /// <summary>Quests (this mod's id, or <c>mod:id</c>) that must be done first. Empty starts with the world.</summary>
    public List<string>? Requires { get; init; }

    /// <summary>The steps, in order.</summary>
    public List<QuestStep>? Steps { get; init; }

    /// <summary>What finishing gives.</summary>
    public QuestRewards? Rewards { get; init; }

    /// <summary>Lines said when the quest is done.</summary>
    public List<DialogueLine>? Outro { get; init; }

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>One step of a quest.</summary>
public sealed record QuestStep
{
    /// <summary>Step id, unique in the quest (lowercase snake_case).</summary>
    public string? Id { get; init; }

    /// <summary>What the quest log tells the players to do.</summary>
    public string? Text { get; init; }

    /// <summary>What finishes the step.</summary>
    public QuestObjective? Objective { get; init; }

    /// <summary>Lines said when the step begins (and again when a player talks to its <c>npc</c> marker).</summary>
    public List<DialogueLine>? Dialogue { get; init; }

    /// <summary>For a <c>choose</c> objective: the options, at least two.</summary>
    public List<QuestChoice>? Choices { get; init; }

    /// <summary>Unknown keys.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// What finishes a step. <see cref="Type"/> picks the rule and which other keys it reads:
/// <list type="bullet">
/// <item><c>reach</c>: a player comes within <see cref="Radius"/> (default: the POI's own radius) of a <see cref="Poi"/>.</item>
/// <item><c>interact</c>: players use <see cref="Count"/> different <see cref="Marker"/> markers (optionally with <see cref="Ref"/>) of a <see cref="Poi"/>.</item>
/// <item><c>have_item</c>: a player holds <see cref="Count"/> of <see cref="Item"/>.</item>
/// <item><c>place</c>: players place <see cref="Count"/> of <see cref="Part"/> (any part when absent).</item>
/// <item><c>craft</c>: players order <see cref="Count"/> crafts of <see cref="Recipe"/> (any when absent).</item>
/// <item><c>drive</c>: a seated player's Creation travels <see cref="Distance"/> metres.</item>
/// <item><c>crops</c>: <see cref="Count"/> crops are planted in the world at once.</item>
/// <item><c>raid</c>: <see cref="Count"/> raids end.</item>
/// <item><c>choose</c>: a player picks one of the step's <see cref="QuestStep.Choices"/>.</item>
/// </list>
/// </summary>
public sealed record QuestObjective
{
    /// <summary>The rule (see the type summary).</summary>
    public string? Type { get; init; }

    /// <summary>POI id (this mod's, or <c>mod:id</c>) for <c>reach</c> and <c>interact</c>.</summary>
    public string? Poi { get; init; }

    /// <summary>Marker type for <c>interact</c> (<c>fire</c>, <c>item</c>, <c>npc</c>, ...).</summary>
    public string? Marker { get; init; }

    /// <summary>Marker ref an <c>interact</c> marker must have, or null for any.</summary>
    public string? Ref { get; init; }

    /// <summary>Item id for <c>have_item</c>.</summary>
    public string? Item { get; init; }

    /// <summary>Part id for <c>place</c>.</summary>
    public string? Part { get; init; }

    /// <summary>Recipe id for <c>craft</c>.</summary>
    public string? Recipe { get; init; }

    /// <summary>How many (default 1).</summary>
    public int? Count { get; init; }

    /// <summary>Metres for <c>reach</c>.</summary>
    public float? Radius { get; init; }

    /// <summary>Metres for <c>drive</c>.</summary>
    public float? Distance { get; init; }

    /// <summary>Unknown keys.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>An option of a <c>choose</c> step.</summary>
public sealed record QuestChoice
{
    /// <summary>Choice id, unique in the step.</summary>
    public string? Id { get; init; }

    /// <summary>What the button says.</summary>
    public string? Text { get; init; }

    /// <summary>Story flags it sets (flag → value), read by later quests, mods and the ending.</summary>
    public SortedDictionary<string, string>? Flags { get; init; }

    /// <summary>Lines said once it is picked.</summary>
    public List<DialogueLine>? Dialogue { get; init; }

    /// <summary>Unknown keys.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>What a finished quest gives.</summary>
public sealed record QuestRewards
{
    /// <summary>Items every player in the session gets.</summary>
    public List<ItemReward>? Items { get; init; }

    /// <summary>POIs (this mod's id or <c>mod:id</c>) whose nearest site is marked for every player.</summary>
    public List<string>? Reveal { get; init; }

    /// <summary>Story flags set (flag → value).</summary>
    public SortedDictionary<string, string>? Flags { get; init; }

    /// <summary>Unknown keys.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>An item reward.</summary>
public sealed record ItemReward
{
    /// <summary>Catalog item id.</summary>
    public string? Item { get; init; }

    /// <summary>How many (default 1).</summary>
    public int? Count { get; init; }

    /// <summary>Unknown keys.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>One line of dialogue.</summary>
public sealed record DialogueLine
{
    /// <summary>Who says it (<c>Logbook</c>, <c>Grazer Envoy</c>, ...).</summary>
    public string? Speaker { get; init; }

    /// <summary>The line.</summary>
    public string? Text { get; init; }

    /// <summary>Unknown keys.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
