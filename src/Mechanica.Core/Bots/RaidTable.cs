using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;

namespace Mechanica.Core.Bots;

/// <summary>
/// When raids come and what they send (spec §16), loaded from <c>raids/raids.json</c>. Like world
/// generation, the last mod in load order that ships one wins. Keys are snake_case.
/// <code>
/// {
///   "check_interval": 60,
///   "player_multipliers": [1.0, 1.3, 1.6, 2.0],
///   "levels": [
///     { "level": 1, "min_value": 10, "budget": 4, "bots": [ { "bot": "scuttler_green", "count": 4 } ] }
///   ]
/// }
/// </code>
/// </summary>
public sealed record RaidTableDefinition
{
    /// <summary>Seconds between raid checks.</summary>
    public float? CheckInterval { get; init; }

    /// <summary>Seconds after a raid ends before the next can start.</summary>
    public float? Cooldown { get; init; }

    /// <summary>Seconds between the horn and the raiders' arrival.</summary>
    public float? Warning { get; init; }

    /// <summary>How far from the farm the raiders arrive, in m.</summary>
    public float? SpawnDistance { get; init; }

    /// <summary>Seconds a raid lasts at most; raiders still standing then leave.</summary>
    public float? Duration { get; init; }

    /// <summary>Budget multipliers for 1, 2, 3, 4… players; more players than entries use the last.</summary>
    public List<float>? PlayerMultipliers { get; init; }

    /// <summary>Raid levels, by rising crop value.</summary>
    public List<RaidLevelDefinition>? Levels { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>One row of the raid table.</summary>
public sealed record RaidLevelDefinition
{
    /// <summary>Level number, 1 and up.</summary>
    public int? Level { get; init; }

    /// <summary>Total planted crop value at which this level starts.</summary>
    public float? MinValue { get; init; }

    /// <summary>Bot budget for one player, spent at each bot's <c>raid_cost</c>.</summary>
    public int? Budget { get; init; }

    /// <summary>Extra budget per point of crop value above <see cref="MinValue"/> (for the open-ended top level).</summary>
    public float? BudgetPerValue { get; init; }

    /// <summary>The bots sent at this level's budget; a bigger budget sends more of the same mix.</summary>
    public List<RaidBotDefinition>? Bots { get; init; }

    /// <summary>Designer's note, shown in the editor.</summary>
    public string? Notes { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A bot type and how many, in a raid level.</summary>
public sealed record RaidBotDefinition
{
    /// <summary>Bot id: <c>mod_id:id</c>, or a bare <c>id</c> for the table's own mod.</summary>
    public string? Bot { get; init; }

    /// <summary>How many.</summary>
    public int? Count { get; init; }

    /// <summary>
    /// Other bot types each of these raiders may be instead: every raider in the entry is picked at random from
    /// <see cref="Bot"/> and these, and costs <see cref="Bot"/>'s <c>raid_cost</c> whichever it turns out to be.
    /// </summary>
    public List<string>? OneOf { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A raid level with its bots resolved.</summary>
/// <param name="Level">Level number.</param>
/// <param name="MinValue">Crop value at which it starts.</param>
/// <param name="Budget">Budget for one player.</param>
/// <param name="BudgetPerValue">Extra budget per point of value above <paramref name="MinValue"/>.</param>
/// <param name="Bots">The mix, with each type resolved.</param>
public sealed record RaidLevel(int Level, float MinValue, int Budget, float BudgetPerValue, IReadOnlyList<RaidSlot> Bots)
{
    /// <summary>What the listed mix costs.</summary>
    public int MixCost => Bots.Sum(b => b.Cost * b.Count);
}

/// <summary>One entry of a raid level, resolved.</summary>
/// <param name="Bot">The bot type; its <c>raid_cost</c> is what each raider in the entry costs.</param>
/// <param name="Count">How many.</param>
/// <param name="OneOf">The other types each raider may be instead (empty when it is always <paramref name="Bot"/>).</param>
public sealed record RaidSlot(Bot Bot, int Count, IReadOnlyList<Bot> OneOf)
{
    /// <summary>What each raider in the entry costs.</summary>
    public int Cost => Bot.RaidCost!.Value;

    /// <summary>The type and the count.</summary>
    public void Deconstruct(out Bot bot, out int count) => (bot, count) = (Bot, Count);

    /// <summary>The type the raider at <paramref name="roll"/> (any whole number) turns out to be.</summary>
    public Bot Pick(uint roll) => OneOf.Count == 0 ? Bot : roll % (uint)(OneOf.Count + 1) is var i && i == 0 ? Bot : OneOf[(int)i - 1];
}

/// <summary>
/// The raid table in use, checked against the bot types: every bot in a level must exist and have a
/// <c>raid_cost</c>, and levels must rise in value. <see cref="Plan"/> turns a crop value and a player count
/// into a level, a budget and the bots to send.
/// </summary>
public sealed class RaidTable
{
    /// <summary>Where a mod keeps its raid table.</summary>
    public const string FilePath = "raids/raids.json";

    /// <summary>Most raiders one raid sends, whatever the budget.</summary>
    public const int MaxRaiders = 96;

    private RaidTable(RaidTableDefinition definition, IReadOnlyList<RaidLevel> levels, string? source, IReadOnlyList<DataDiagnostic> diagnostics)
    {
        Definition = definition;
        Levels = levels;
        Source = source;
        Diagnostics = diagnostics;
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DataJson.Write(definition))));
    }

    /// <summary>No raids at all.</summary>
    public static RaidTable None { get; } = new(new RaidTableDefinition(), [], null, []);

    /// <summary>The table as written.</summary>
    public RaidTableDefinition Definition { get; }

    /// <summary>Levels in rising order.</summary>
    public IReadOnlyList<RaidLevel> Levels { get; }

    /// <summary>Mod file it came from (<c>mod/raids/raids.json</c>), or null.</summary>
    public string? Source { get; }

    /// <summary>Every problem found while loading.</summary>
    public IReadOnlyList<DataDiagnostic> Diagnostics { get; }

    /// <summary>True when any diagnostic is an error.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>SHA-256 (hex) of the canonical table.</summary>
    public string Fingerprint { get; }

    /// <summary>Seconds between raid checks.</summary>
    public float CheckInterval => Definition.CheckInterval ?? 60f;

    /// <summary>Seconds after a raid before the next.</summary>
    public float Cooldown => Definition.Cooldown ?? 600f;

    /// <summary>Seconds of warning.</summary>
    public float Warning => Definition.Warning ?? 10f;

    /// <summary>Distance from the farm the raiders arrive at, m.</summary>
    public float SpawnDistance => Definition.SpawnDistance ?? 35f;

    /// <summary>Longest a raid lasts, s.</summary>
    public float Duration => Definition.Duration ?? 300f;

    /// <summary>
    /// Loads the raid table of the last mod in <paramref name="content"/> that has one, with every later mod's
    /// <c>raids/raids.patch.json</c> merged in (levels by <c>level</c>, their bots by <c>bot</c>; see
    /// <see cref="ContentLayers.KeyedMergePatch"/>). Bare bot ids in a patch mean the patching mod's bots.
    /// </summary>
    public static RaidTable Load(ModLoadResult content, IBotCatalog bots)
    {
        var diagnostics = new List<DataDiagnostic>();
        var baseMod = "";
        var layered = ContentLayers.LayerFile(content, FilePath, diagnostics, QualifyBots, (json, sink) =>
        {
            // A whole file's bare bot ids are its own mod's; a patch's own are already qualified, so what is left
            // bare came from the whole file it patches.
            var patch = sink.File.EndsWith(ContentLayers.PatchSuffix, StringComparison.Ordinal);
            var owner = patch ? baseMod : sink.File[..sink.File.IndexOf('/')];
            if (ModLoader.ParseJson<RaidTableDefinition>(json.ToJsonString(), sink) is not { } read)
                return false;
            var errors = sink.ErrorCount;
            Check(read, bots, owner, sink);
            if (sink.ErrorCount > errors)
                return false;
            if (!patch)
                baseMod = owner;
            return true;
        });
        if (layered is not { } table)
            return diagnostics.Count == 0 ? None : new RaidTable(new RaidTableDefinition(), [], null, diagnostics);
        var final = new DiagnosticSink(table.DisplayFile, diagnostics);
        var definition = ModLoader.ParseJson<RaidTableDefinition>(table.Json.ToJsonString(), final)!;
        var levels = Check(definition, bots, table.BaseModId, final);
        return new RaidTable(definition, final.ErrorCount == 0 ? levels : [], table.DisplayFile, diagnostics);
    }

    // levels[].bots[].bot written bare in a patch names the patching mod's bot.
    private static void QualifyBots(JsonObject patch, string modId)
    {
        foreach (var level in (patch["levels"] as JsonArray ?? []).OfType<JsonObject>())
        {
            foreach (var bot in (level["bots"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (bot["bot"] is JsonValue v && v.TryGetValue<string>(out var id) && !id.Contains(':'))
                    bot["bot"] = $"{modId}:{id}";
                if (bot["one_of"] is JsonArray others)
                {
                    for (var i = 0; i < others.Count; i++)
                    {
                        if (others[i] is JsonValue o && o.TryGetValue<string>(out var other) && !other.Contains(':'))
                            others[i] = $"{modId}:{other}";
                    }
                }
            }
        }
    }

    /// <summary>Builds a table in code (tests, tools). Throws on any error.</summary>
    /// <exception cref="FormatException">The table has errors.</exception>
    public static RaidTable From(RaidTableDefinition definition, IBotCatalog bots, string modId = Identifiers.VanillaModId)
    {
        var diagnostics = new List<DataDiagnostic>();
        var levels = Check(definition, bots, modId, new DiagnosticSink("raids", diagnostics));
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            throw new FormatException(string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return new RaidTable(definition, levels, null, diagnostics);
    }

    /// <summary>
    /// Checks a raid table as the loader would, as if it were in mod <paramref name="modId"/> (bare bot ids
    /// mean that mod's). The Bot Behavior Editor checks with this before it saves.
    /// </summary>
    public static IReadOnlyList<DataDiagnostic> Validate(RaidTableDefinition definition, IBotCatalog bots, string displayFile,
        string modId = Identifiers.VanillaModId)
    {
        var diagnostics = new List<DataDiagnostic>();
        Check(definition, bots, modId, new DiagnosticSink(displayFile, diagnostics));
        return diagnostics;
    }

    private static List<RaidLevel> Check(RaidTableDefinition t, IBotCatalog bots, string modId, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.RaidInvalid;
        KnownKeys.WarnUnknown(t.Extra, "$", sink, typeof(RaidTableDefinition));
        Positive(t.CheckInterval, "$.check_interval", sink);
        NonNegative(t.Cooldown, "$.cooldown", sink);
        NonNegative(t.Warning, "$.warning", sink);
        Positive(t.SpawnDistance, "$.spawn_distance", sink);
        Positive(t.Duration, "$.duration", sink);
        if (t.PlayerMultipliers is { } multipliers)
        {
            if (multipliers.Count == 0)
                sink.Error(code, "$.player_multipliers", "player_multipliers needs at least one entry (for 1 player).");
            for (var i = 0; i < multipliers.Count; i++)
            {
                if (!(multipliers[i] > 0 && float.IsFinite(multipliers[i])))
                    sink.Error(code, $"$.player_multipliers[{i}]", "each multiplier must be greater than 0.");
            }
        }
        var levels = new List<RaidLevel>();
        for (var i = 0; i < (t.Levels?.Count ?? 0); i++)
        {
            var row = t.Levels![i];
            var path = $"$.levels[{i}]";
            var errors = sink.ErrorCount;
            KnownKeys.WarnUnknown(row.Extra, path, sink, typeof(RaidLevelDefinition));
            if (row.Level is not { } number || number < 1)
                sink.Error(code, $"{path}.level", "level (1 or more) is required.");
            else if (levels.Count > 0 && number <= levels[^1].Level)
                sink.Error(code, $"{path}.level", "levels must rise from one row to the next.");
            if (row.MinValue is not { } min || !(min >= 0 && float.IsFinite(min)))
                sink.Error(code, $"{path}.min_value", "min_value (0 or more) is required.");
            else if (levels.Count > 0 && min <= levels[^1].MinValue)
                sink.Error(code, $"{path}.min_value", "min_value must rise from one level to the next.");
            if (row.Budget is not { } budget || budget < 1)
                sink.Error(code, $"{path}.budget", "budget (1 or more) is required.");
            NonNegative(row.BudgetPerValue, $"{path}.budget_per_value", sink);
            var mix = new List<RaidSlot>();
            if (row.Bots is not { Count: > 0 })
                sink.Error(code, $"{path}.bots", "bots needs at least one entry.");
            for (var j = 0; j < (row.Bots?.Count ?? 0); j++)
            {
                var entry = row.Bots![j];
                var at = $"{path}.bots[{j}]";
                KnownKeys.WarnUnknown(entry.Extra, at, sink, typeof(RaidBotDefinition));
                if (entry.Bot is null || bots.FindBot(Identifiers.Qualify(entry.Bot, modId)) is not { } bot)
                    sink.Error(code, $"{at}.bot", entry.Bot is null ? "bot is required." : $"there is no bot '{entry.Bot}'.");
                else if (bot.RaidCost is null)
                    sink.Error(code, $"{at}.bot", $"'{bot.QualifiedId}' has no raid_cost, so it cannot raid.");
                else if (mix.Any(m => m.Bot == bot))
                    sink.Error(code, $"{at}.bot", $"'{bot.QualifiedId}' is listed twice; add the counts together.");
                else if (entry.Count is not { } count || count < 1)
                    sink.Error(code, $"{at}.count", "count (1 or more) is required.");
                else if (OneOf(entry, bot, bots, modId, at, sink) is { } others)
                    mix.Add(new RaidSlot(bot, count, others));
            }
            if (sink.ErrorCount > errors)
                continue;
            var level = new RaidLevel(row.Level!.Value, row.MinValue!.Value, row.Budget!.Value, row.BudgetPerValue ?? 0, mix);
            if (level.MixCost > level.Budget)
                sink.Error(code, $"{path}.bots", $"the bots cost {level.MixCost}, more than the budget of {level.Budget}.");
            else
                levels.Add(level);
        }
        return levels;
    }

    // The entry's one_of types: each must exist, have a raid_cost and be listed once (and not be the entry's own bot).
    private static List<Bot>? OneOf(RaidBotDefinition entry, Bot bot, IBotCatalog bots, string modId, string at, DiagnosticSink sink)
    {
        var others = new List<Bot>();
        var errors = sink.ErrorCount;
        for (var k = 0; k < (entry.OneOf?.Count ?? 0); k++)
        {
            var id = entry.OneOf![k];
            var path = $"{at}.one_of[{k}]";
            if (string.IsNullOrEmpty(id) || bots.FindBot(Identifiers.Qualify(id, modId)) is not { } other)
                sink.Error(DiagnosticCodes.RaidInvalid, path, $"there is no bot '{id}'.");
            else if (other.RaidCost is null)
                sink.Error(DiagnosticCodes.RaidInvalid, path, $"'{other.QualifiedId}' has no raid_cost, so it cannot raid.");
            else if (other == bot || others.Contains(other))
                sink.Error(DiagnosticCodes.RaidInvalid, path, $"'{other.QualifiedId}' is already one of this entry's bots.");
            else
                others.Add(other);
        }
        return sink.ErrorCount > errors ? null : others;
    }

    private static void Positive(float? value, string path, DiagnosticSink sink)
    {
        if (value is { } v && !(v > 0 && float.IsFinite(v)))
            sink.Error(DiagnosticCodes.RaidInvalid, path, $"{path[2..]} must be greater than 0.");
    }

    private static void NonNegative(float? value, string path, DiagnosticSink sink)
    {
        if (value is { } v && !(v >= 0 && float.IsFinite(v)))
            sink.Error(DiagnosticCodes.RaidInvalid, path, $"{path[(path.LastIndexOf('.') + 1)..]} must be 0 or more.");
    }

    /// <summary>The level a crop value sets off, or null when it is below the first.</summary>
    public RaidLevel? LevelFor(float cropValue) => Levels.LastOrDefault(l => cropValue >= l.MinValue);

    /// <summary>The budget multiplier for <paramref name="players"/> players (1 for none or no table).</summary>
    public float MultiplierFor(int players)
    {
        var multipliers = Definition.PlayerMultipliers ?? [1f, 1.3f, 1.6f, 2f];
        return players < 1 || multipliers.Count == 0 ? multipliers.FirstOrDefault(1f) : multipliers[Math.Min(players, multipliers.Count) - 1];
    }

    /// <summary>
    /// The budget of <paramref name="level"/> for a crop value and player count: the level's budget plus
    /// <see cref="RaidLevel.BudgetPerValue"/> for each point above its start, times the player multiplier,
    /// rounded to the nearest whole point.
    /// </summary>
    public int BudgetFor(RaidLevel level, float cropValue, int players) =>
        (int)MathF.Round((level.Budget + Math.Max(0, cropValue - level.MinValue) * level.BudgetPerValue) * MultiplierFor(players), MidpointRounding.AwayFromZero);

    /// <summary>
    /// The bots a raid sends for <paramref name="budget"/>: the level's listed mix, then one more of each
    /// listed type in turn while the budget still covers it (so a bigger budget sends more of the same mix),
    /// up to <see cref="MaxRaiders"/>. An entry with <c>one_of</c> picks each raider's type from
    /// <paramref name="seed"/> (the same seed sends the same raid). Bot type ids in send order.
    /// </summary>
    public static IReadOnlyList<string> Compose(RaidLevel level, int budget, int seed = 0)
    {
        var sent = new List<string>();
        var left = budget;
        void Send(RaidSlot slot)
        {
            sent.Add(slot.Pick(Roll(seed, sent.Count)).QualifiedId);
            left -= slot.Cost;
        }
        foreach (var slot in level.Bots)
        {
            for (var i = 0; i < slot.Count && left >= slot.Cost && sent.Count < MaxRaiders; i++)
                Send(slot);
        }
        var added = true;
        while (added && sent.Count < MaxRaiders)
        {
            added = false;
            foreach (var slot in level.Bots)
            {
                if (left < slot.Cost || sent.Count >= MaxRaiders)
                    continue;
                Send(slot);
                added = true;
            }
        }
        return sent;
    }

    // A well-mixed whole number for raider `index` of the raid seeded `seed` (no shared random state, so plans replay).
    private static uint Roll(int seed, int index)
    {
        var x = (uint)seed * 0x9E3779B9u ^ (uint)index * 0x85EBCA6Bu;
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        return x ^ (x >> 16);
    }

    /// <summary>
    /// The raid a crop value and player count set off, or null when the value is below the first level.
    /// <paramref name="budgetScale"/> is the difficulty's raid budget multiplier (at least 1 point is left);
    /// <paramref name="seed"/> picks the types of <c>one_of</c> entries (see <see cref="Compose"/>).
    /// </summary>
    public RaidPlan? Plan(float cropValue, int players, int? forceLevel = null, float budgetScale = 1f, int seed = 0)
    {
        var level = forceLevel is { } n ? Levels.FirstOrDefault(l => l.Level == n) : LevelFor(cropValue);
        if (level is null)
            return null;
        var budget = BudgetFor(level, Math.Max(cropValue, level.MinValue), players);
        if (budgetScale != 1f)
            budget = Math.Max(1, (int)MathF.Round(budget * budgetScale, MidpointRounding.AwayFromZero));
        return new RaidPlan(level.Level, budget, Compose(level, budget, seed));
    }
}

/// <summary>A planned raid.</summary>
/// <param name="Level">Its level.</param>
/// <param name="Budget">Its budget after player scaling.</param>
/// <param name="Bots">Bot type ids to send, in order.</param>
public sealed record RaidPlan(int Level, int Budget, IReadOnlyList<string> Bots);
