using System.Text.Json;
using Mechanica.Core.Data;

namespace Mechanica.Core.Quests;

/// <summary>Where a quest stands.</summary>
public enum QuestStatus
{
    /// <summary>Being worked on.</summary>
    Active,

    /// <summary>Finished, rewards given.</summary>
    Done,
}

/// <summary>One quest's progress. Quests are shared by everyone in the world (co-op).</summary>
public sealed record QuestProgress
{
    /// <summary>Active or done.</summary>
    public QuestStatus Status { get; init; }

    /// <summary>Index of the current step (the step count once done).</summary>
    public int Step { get; init; }

    /// <summary>Progress on the current step: things counted so far.</summary>
    public int Count { get; init; }

    /// <summary>Metres driven on a drive step.</summary>
    public float Distance { get; init; }

    /// <summary>Markers already used on an interact step (<c>site:marker</c>).</summary>
    public SortedSet<string>? Used { get; init; }

    /// <summary>Choices picked, by step id.</summary>
    public SortedDictionary<string, string>? Choices { get; init; }
}

/// <summary>
/// The story's state in a world: every started quest's progress, the story flags set by choices and
/// rewards, and the POIs revealed on the map. The host owns it; clients get it as JSON whenever it changes,
/// and saves keep it under <c>quests</c>.
/// </summary>
public sealed record QuestState
{
    /// <summary>Started quests by qualified id.</summary>
    public SortedDictionary<string, QuestProgress> Quests { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Story flags (flag → value).</summary>
    public SortedDictionary<string, string> Flags { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Revealed POIs, qualified ids.</summary>
    public SortedSet<string> Revealed { get; init; } = new(StringComparer.Ordinal);

    /// <summary>The progress of a quest, or null when it has not started.</summary>
    public QuestProgress? Of(string quest) => Quests.GetValueOrDefault(quest);

    /// <summary>True when the quest is done.</summary>
    public bool IsDone(string quest) => Of(quest)?.Status == QuestStatus.Done;

    /// <summary>The JSON form (canonical: sorted keys, pretty-printed).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, DataJson.WriteOptions) + "\n";

    /// <summary>The JSON form as an element, for a save.</summary>
    public JsonElement ToElement() => JsonSerializer.SerializeToElement(this, DataJson.WriteOptions);

    /// <summary>Reads the JSON form; null when it is malformed.</summary>
    public static QuestState? FromJson(string json)
    {
        try
        {
            var state = DataJson.Read<QuestState>(json);
            return state is null ? null : Clean(state);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the JSON form from a save; null when it is malformed.</summary>
    public static QuestState? FromElement(JsonElement element) => FromJson(element.GetRawText());

    // Missing collections read as null; replace them with empty ones and drop nonsense.
    private static QuestState Clean(QuestState state) => new()
    {
        Quests = new((state.Quests ?? []).Where(q => q.Value is not null && q.Value.Step >= 0 && q.Value.Count >= 0)
            .ToDictionary(q => q.Key, q => q.Value), StringComparer.Ordinal),
        Flags = new((state.Flags ?? []).Where(f => f.Value is not null).ToDictionary(), StringComparer.Ordinal),
        Revealed = new(state.Revealed ?? [], StringComparer.Ordinal),
    };
}
