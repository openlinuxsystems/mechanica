using System.Security.Cryptography;
using System.Text;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.World;

namespace Mechanica.Core.Quests;

/// <summary>What finishes a quest step.</summary>
public enum QuestObjectiveKind
{
    /// <summary>A player comes near a POI.</summary>
    Reach,

    /// <summary>Players use markers of a POI.</summary>
    Interact,

    /// <summary>A player holds items.</summary>
    HaveItem,

    /// <summary>Players place parts.</summary>
    Place,

    /// <summary>Players order crafts.</summary>
    Craft,

    /// <summary>A seated player's Creation travels.</summary>
    Drive,

    /// <summary>Crops are planted.</summary>
    Crops,

    /// <summary>Raids end.</summary>
    Raid,

    /// <summary>A player picks a choice.</summary>
    Choose,

    /// <summary>Players put out fires at a POI's markers.</summary>
    Extinguish,
}

/// <summary>A quest step with every id qualified and every default filled in.</summary>
/// <param name="Id">Step id.</param>
/// <param name="Text">Quest log text.</param>
/// <param name="Kind">The objective rule.</param>
/// <param name="Poi">Qualified POI id, for reach, interact and extinguish.</param>
/// <param name="Marker">Marker type, for interact and extinguish.</param>
/// <param name="Ref">Marker ref, for interact, or null for any.</param>
/// <param name="Target">Qualified item, part or recipe id, or null for any.</param>
/// <param name="Count">How many.</param>
/// <param name="Radius">Reach radius in m, or null for the POI's.</param>
/// <param name="Distance">Metres to drive.</param>
/// <param name="Dialogue">Lines said when it begins.</param>
/// <param name="Choices">Options of a choose step.</param>
public sealed record QuestStepInfo(
    string Id, string Text, QuestObjectiveKind Kind, string? Poi, string? Marker, string? Ref, string? Target, int Count,
    float? Radius, float Distance, IReadOnlyList<DialogueLine> Dialogue, IReadOnlyList<QuestChoice> Choices);

/// <summary>A quest that loaded cleanly.</summary>
/// <param name="QualifiedId"><c>mod_id:quest_id</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Summary">Quest log line.</param>
/// <param name="Requires">Qualified quests that must be done first.</param>
/// <param name="Steps">The steps, in order.</param>
/// <param name="Items">Item rewards (qualified id and count).</param>
/// <param name="Reveal">Qualified POIs revealed on completion.</param>
/// <param name="Flags">Flags set on completion.</param>
/// <param name="Outro">Lines said on completion.</param>
/// <param name="Definition">The data as written.</param>
public sealed record Quest(
    string QualifiedId, string Name, string Summary, IReadOnlyList<string> Requires, IReadOnlyList<QuestStepInfo> Steps,
    IReadOnlyList<ItemAmount> Items, IReadOnlyList<string> Reveal, IReadOnlyDictionary<string, string> Flags,
    IReadOnlyList<DialogueLine> Outro, QuestDefinition Definition);

/// <summary>
/// Every quest of the enabled mods (chunk 24), read from each mod's <c>quests/*.json</c> in load order and
/// checked against the loaded items, recipes and POIs. A file with errors is left out and
/// <see cref="Diagnostics"/> says why. Host and clients compare <see cref="Fingerprint"/> on join.
/// </summary>
public sealed class QuestBook
{
    /// <summary>Where a mod keeps its quests.</summary>
    public const string Folder = "quests";

    /// <summary>Objective type names in data, by kind.</summary>
    public static readonly IReadOnlyDictionary<string, QuestObjectiveKind> Kinds = new Dictionary<string, QuestObjectiveKind>
    {
        ["reach"] = QuestObjectiveKind.Reach,
        ["interact"] = QuestObjectiveKind.Interact,
        ["have_item"] = QuestObjectiveKind.HaveItem,
        ["place"] = QuestObjectiveKind.Place,
        ["craft"] = QuestObjectiveKind.Craft,
        ["drive"] = QuestObjectiveKind.Drive,
        ["crops"] = QuestObjectiveKind.Crops,
        ["raid"] = QuestObjectiveKind.Raid,
        ["choose"] = QuestObjectiveKind.Choose,
        ["extinguish"] = QuestObjectiveKind.Extinguish,
    };

    /// <summary>The marker type an <c>extinguish</c> objective counts when it names none.</summary>
    public const string FireMarker = "fire";

    private readonly Dictionary<string, Quest> _quests;

    private QuestBook(IEnumerable<Quest> quests, IReadOnlyList<DataDiagnostic> diagnostics)
    {
        _quests = quests.ToDictionary(q => q.QualifiedId, StringComparer.Ordinal);
        All = [.. _quests.Values.OrderBy(q => q.QualifiedId, StringComparer.Ordinal)];
        Diagnostics = diagnostics;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var quest in All)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(quest.QualifiedId));
            hash.AppendData(Encoding.UTF8.GetBytes(DataJson.Write(quest.Definition)));
        }
        Fingerprint = Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>A book with no quests.</summary>
    public static QuestBook Empty { get; } = new([], []);

    /// <summary>Every quest, ordered by qualified id.</summary>
    public IReadOnlyList<Quest> All { get; }

    /// <summary>Every problem found while loading.</summary>
    public IReadOnlyList<DataDiagnostic> Diagnostics { get; }

    /// <summary>SHA-256 (hex) of every quest's id and canonical form.</summary>
    public string Fingerprint { get; }

    /// <summary>The quest with this id (a bare id means vanilla), or null.</summary>
    public Quest? Find(string id) => _quests.GetValueOrDefault(Identifiers.Qualify(id));

    /// <summary>Loads the quests of the mods in <paramref name="content"/>, checked against its items, <paramref name="recipes"/> and <paramref name="world"/>'s POIs.</summary>
    public static QuestBook Load(ModLoadResult content, RecipeBook recipes, WorldGenData world)
    {
        var diagnostics = new List<DataDiagnostic>();
        var found = new List<(Quest Quest, DiagnosticSink Sink)>();
        foreach (var mod in content.Mods)
        {
            var folder = Path.Combine(mod.Directory, Folder);
            if (!Directory.Exists(folder))
                continue;
            foreach (var path in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
            {
                var sink = new DiagnosticSink($"{mod.Id}/{Folder}/{Path.GetFileName(path)}", diagnostics);
                if (ModLoader.ParseJson<QuestDefinition>(File.ReadAllText(path), sink) is not { } definition)
                    continue;
                var id = Path.GetFileNameWithoutExtension(path);
                if (Check(definition, id, mod.Id, content, recipes, world, sink) is { } quest && sink.ErrorCount == 0)
                    found.Add((quest, sink));
            }
        }
        return new QuestBook(Link(found), diagnostics);
    }

    /// <summary>Checks one quest as the loader would, as if it were <paramref name="id"/> in mod <paramref name="modId"/>; the exporter checks with this before it writes.</summary>
    public static IReadOnlyList<DataDiagnostic> Validate(QuestDefinition quest, string id, string modId, IPartCatalog catalog, RecipeBook recipes, WorldGenData world, string displayFile)
    {
        var diagnostics = new List<DataDiagnostic>();
        Check(quest, id, modId, catalog, recipes, world, new DiagnosticSink(displayFile, diagnostics));
        return diagnostics;
    }

    // Drops quests whose requirements are missing or form a loop, with an error on the quest.
    private static IEnumerable<Quest> Link(List<(Quest Quest, DiagnosticSink Sink)> found)
    {
        var byId = new Dictionary<string, (Quest Quest, DiagnosticSink Sink)>(StringComparer.Ordinal);
        foreach (var entry in found)
        {
            if (!byId.TryAdd(entry.Quest.QualifiedId, entry))
                entry.Sink.Error(DiagnosticCodes.QuestInvalid, "$.id", $"quest '{entry.Quest.QualifiedId}' is defined twice.");
        }
        var good = new HashSet<string>(StringComparer.Ordinal);
        var bad = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in byId.Keys.Order(StringComparer.Ordinal))
            Resolve(id, []);
        return byId.Values.Where(e => good.Contains(e.Quest.QualifiedId)).Select(e => e.Quest);

        bool Resolve(string id, HashSet<string> path)
        {
            if (good.Contains(id))
                return true;
            if (bad.Contains(id) || !byId.TryGetValue(id, out var entry))
                return false;
            if (!path.Add(id))
            {
                entry.Sink.Error(DiagnosticCodes.QuestInvalid, "$.requires", $"quest '{id}' requires itself through {string.Join(" → ", path)}.");
                bad.Add(id);
                return false;
            }
            foreach (var need in entry.Quest.Requires)
            {
                if (!byId.ContainsKey(need))
                    entry.Sink.Error(DiagnosticCodes.QuestInvalid, "$.requires", $"required quest '{need}' does not exist.");
                if (!Resolve(need, path))
                {
                    bad.Add(id);
                    path.Remove(id);
                    return false;
                }
            }
            path.Remove(id);
            good.Add(id);
            return true;
        }
    }

    private static Quest? Check(QuestDefinition q, string id, string modId, IPartCatalog catalog, RecipeBook recipes, WorldGenData world, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.QuestInvalid;
        KnownKeys.WarnUnknown(q.Extra, "$", sink, typeof(QuestDefinition));
        if (!Identifiers.IsValid(id))
            sink.Error(code, "$", $"file name '{id}' must be a lowercase snake_case quest id.");
        if (q.Id is not null && q.Id != id)
            sink.Error(code, "$.id", $"id '{q.Id}' must match the file name '{id}'.");
        if (string.IsNullOrWhiteSpace(q.Name))
            sink.Error(code, "$.name", "name is required.");
        var requires = new List<string>();
        foreach (var (need, i) in (q.Requires ?? []).Select((r, i) => (r, i)))
        {
            if (!QualifiedOk(need))
                sink.Error(code, $"$.requires[{i}]", $"'{need}' is not a quest id.");
            else
                requires.Add(Identifiers.Qualify(need, modId));
        }

        var steps = new List<QuestStepInfo>();
        if (q.Steps is not { Count: > 0 })
            sink.Error(code, "$.steps", "a quest needs at least one step.");
        for (var i = 0; i < (q.Steps?.Count ?? 0); i++)
        {
            if (CheckStep(q.Steps![i], $"$.steps[{i}]", modId, catalog, recipes, world, sink) is not { } step)
                continue;
            if (steps.Any(s => s.Id == step.Id))
                sink.Error(code, $"$.steps[{i}].id", $"step '{step.Id}' is listed twice.");
            steps.Add(step);
        }

        var items = new List<ItemAmount>();
        var reveal = new List<string>();
        var flags = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (q.Rewards is { } rewards)
        {
            KnownKeys.WarnUnknown(rewards.Extra, "$.rewards", sink, typeof(QuestRewards));
            foreach (var (reward, i) in (rewards.Items ?? []).Select((r, i) => (r, i)))
            {
                var path = $"$.rewards.items[{i}]";
                KnownKeys.WarnUnknown(reward.Extra, path, sink, typeof(ItemReward));
                if (reward.Item is null || catalog.FindDefinition(Identifiers.Qualify(reward.Item, modId)) is null)
                    sink.Error(code, $"{path}.item", $"item '{reward.Item}' is not in the loaded mods.");
                else if (reward.Count is < 1)
                    sink.Error(code, $"{path}.count", "count must be 1 or more.");
                else
                    items.Add(new ItemAmount(Identifiers.Qualify(reward.Item, modId), reward.Count ?? 1));
            }
            foreach (var (poi, i) in (rewards.Reveal ?? []).Select((p, i) => (p, i)))
            {
                if (FindPoi(world, poi, modId) is { } found)
                    reveal.Add(found);
                else
                    sink.Error(code, $"$.rewards.reveal[{i}]", $"POI '{poi}' is not in the world data.");
            }
            CheckFlags(rewards.Flags, "$.rewards.flags", flags, sink);
        }
        var outro = CheckLines(q.Outro, "$.outro", sink);
        return new Quest(Identifiers.Qualify(id, modId), q.Name?.Trim() ?? id, q.Summary?.Trim() ?? "", requires, steps, items, reveal, flags, outro,
            q with { Id = null });
    }

    private static QuestStepInfo? CheckStep(QuestStep s, string path, string modId, IPartCatalog catalog, RecipeBook recipes, WorldGenData world, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.QuestInvalid;
        var errors = sink.ErrorCount;
        KnownKeys.WarnUnknown(s.Extra, path, sink, typeof(QuestStep));
        if (!Identifiers.IsValid(s.Id))
            sink.Error(code, $"{path}.id", $"step id '{s.Id}' must be lowercase snake_case.");
        if (string.IsNullOrWhiteSpace(s.Text))
            sink.Error(code, $"{path}.text", "text is required.");
        var dialogue = CheckLines(s.Dialogue, $"{path}.dialogue", sink);
        if (s.Objective is not { } o)
        {
            sink.Error(code, $"{path}.objective", "objective is required.");
            return null;
        }
        var at = $"{path}.objective";
        KnownKeys.WarnUnknown(o.Extra, at, sink, typeof(QuestObjective));
        if (o.Type is null || !Kinds.TryGetValue(o.Type, out var kind))
        {
            sink.Error(code, $"{at}.type", $"type '{o.Type}' must be one of {string.Join(", ", Kinds.Keys)}.");
            return null;
        }
        if (o.Count is < 1)
            sink.Error(code, $"{at}.count", "count must be 1 or more.");
        string? poi = null;
        string? target = null;
        switch (kind)
        {
            case QuestObjectiveKind.Reach or QuestObjectiveKind.Interact or QuestObjectiveKind.Extinguish:
                poi = FindPoi(world, o.Poi, modId);
                if (poi is null)
                    sink.Error(code, $"{at}.poi", $"POI '{o.Poi}' is not in the world data.");
                if (o.Radius is { } radius && !(radius > 0 && radius <= 2000))
                    sink.Error(code, $"{at}.radius", "radius must be more than 0 and at most 2000 m.");
                if (kind is QuestObjectiveKind.Interact or QuestObjectiveKind.Extinguish)
                {
                    var marker = kind == QuestObjectiveKind.Extinguish ? o.Marker ?? FireMarker : o.Marker;
                    if (!Identifiers.IsValid(marker))
                        sink.Error(code, $"{at}.marker", $"an {o.Type} objective needs a marker type.");
                    else if (poi is not null && world.Pois.First(p => p.QualifiedId == poi).Prefab.Markers?.Count(m => m.Type == marker && (o.Ref is null || m.Ref == o.Ref)) is var n && n < (o.Count ?? 1))
                        sink.Error(code, $"{at}.count", $"the POI has {n} '{marker}' markers{(o.Ref is null ? "" : $" with ref '{o.Ref}'")}, fewer than count.");
                }
                break;
            case QuestObjectiveKind.HaveItem:
                if (o.Item is null || catalog.FindDefinition(Identifiers.Qualify(o.Item, modId)) is null)
                    sink.Error(code, $"{at}.item", $"item '{o.Item}' is not in the loaded mods.");
                else
                    target = Identifiers.Qualify(o.Item, modId);
                break;
            case QuestObjectiveKind.Place:
                if (o.Part is not null && catalog.FindDefinition(Identifiers.Qualify(o.Part, modId)) is null)
                    sink.Error(code, $"{at}.part", $"part '{o.Part}' is not in the loaded mods.");
                else if (o.Part is not null)
                    target = Identifiers.Qualify(o.Part, modId);
                break;
            case QuestObjectiveKind.Craft:
                if (o.Recipe is not null && recipes.Find(Identifiers.Qualify(o.Recipe, modId)) is null)
                    sink.Error(code, $"{at}.recipe", $"recipe '{o.Recipe}' is not in the loaded mods.");
                else if (o.Recipe is not null)
                    target = Identifiers.Qualify(o.Recipe, modId);
                break;
            case QuestObjectiveKind.Drive:
                if (o.Distance is not ({ } d and > 0 and <= 100_000))
                    sink.Error(code, $"{at}.distance", "a drive objective needs a distance from 0 to 100000 m.");
                break;
        }
        var choices = new List<QuestChoice>();
        if (kind == QuestObjectiveKind.Choose)
        {
            if (s.Choices is not { Count: >= 2 })
                sink.Error(code, $"{path}.choices", "a choose step needs at least two choices.");
            foreach (var (choice, i) in (s.Choices ?? []).Select((c, i) => (c, i)))
            {
                var cp = $"{path}.choices[{i}]";
                KnownKeys.WarnUnknown(choice.Extra, cp, sink, typeof(QuestChoice));
                if (!Identifiers.IsValid(choice.Id))
                    sink.Error(code, $"{cp}.id", $"choice id '{choice.Id}' must be lowercase snake_case.");
                else if (choices.Any(c => c.Id == choice.Id))
                    sink.Error(code, $"{cp}.id", $"choice '{choice.Id}' is listed twice.");
                if (string.IsNullOrWhiteSpace(choice.Text))
                    sink.Error(code, $"{cp}.text", "text is required.");
                CheckFlags(choice.Flags, $"{cp}.flags", [], sink);
                CheckLines(choice.Dialogue, $"{cp}.dialogue", sink);
                choices.Add(choice);
            }
        }
        else if (s.Choices is { Count: > 0 })
        {
            sink.Error(code, $"{path}.choices", "only a choose step has choices.");
        }
        if (sink.ErrorCount > errors)
            return null;
        var stepMarker = kind == QuestObjectiveKind.Extinguish ? o.Marker ?? FireMarker : o.Marker;
        return new QuestStepInfo(s.Id!, s.Text!.Trim(), kind, poi, stepMarker, o.Ref, target, o.Count ?? 1, o.Radius, o.Distance ?? 0, dialogue, choices);
    }

    private static List<DialogueLine> CheckLines(List<DialogueLine>? lines, string path, DiagnosticSink sink)
    {
        foreach (var (line, i) in (lines ?? []).Select((l, i) => (l, i)))
        {
            KnownKeys.WarnUnknown(line.Extra, $"{path}[{i}]", sink, typeof(DialogueLine));
            if (string.IsNullOrWhiteSpace(line.Text))
                sink.Error(DiagnosticCodes.QuestInvalid, $"{path}[{i}].text", "a line needs text.");
        }
        return [.. (lines ?? []).Select(l => l with { Extra = null })];
    }

    private static void CheckFlags(SortedDictionary<string, string>? flags, string path, SortedDictionary<string, string> into, DiagnosticSink sink)
    {
        foreach (var (flag, value) in flags ?? [])
        {
            if (!QualifiedOk(flag))
                sink.Error(DiagnosticCodes.QuestInvalid, $"{path}.{flag}", $"flag '{flag}' must be snake_case, optionally mod:flag.");
            else
                into[flag] = value;
        }
    }

    private static string? FindPoi(WorldGenData world, string? id, string modId) =>
        id is null ? null : world.Pois.FirstOrDefault(p => p.QualifiedId == Identifiers.Qualify(id, modId))?.QualifiedId;

    private static bool QualifiedOk(string? id) =>
        id is not null && id.Split(':') is var parts && parts.Length <= 2 && parts.All(Identifiers.IsValid);
}
