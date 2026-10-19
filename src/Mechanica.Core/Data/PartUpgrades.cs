using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mechanica.Core.Data;

/// <summary>One line of an upgrade's result: <paramref name="Label"/> goes from <paramref name="From"/> to <paramref name="To"/>.</summary>
/// <param name="Label">What changes, in English UI text (<c>Speed</c>) or a script property's name.</param>
/// <param name="From">The value now, as shown.</param>
/// <param name="To">The value after the upgrade, as shown.</param>
public sealed record UpgradeChange(string Label, string From, string To);

/// <summary>
/// A part's upgrade levels (<see cref="PartDefinition.Upgrades"/>, UI spec §4): level 0 is the part as written, and
/// level <c>n</c> is the part with the <c>set</c> of levels 1 to <c>n</c> merged on top, in order. Results are kept per
/// definition, so asking again is cheap.
/// </summary>
public static class PartUpgrades
{
    /// <summary>Most levels a part may have.</summary>
    public const int MaxLevels = 10;

    private static readonly ConditionalWeakTable<PartDefinition, PartDefinition?[]> Cache = [];

    /// <summary>How many times the part can be upgraded (0 when it has no upgrades).</summary>
    public static int MaxLevel(PartDefinition part) => part.Upgrades?.Count ?? 0;

    /// <summary>Marks an upgraded part's item: <c>vanilla:gas_engine@2</c> is a Gas Engine at level 2 (see <see cref="ItemKey"/>).</summary>
    public const char LevelMark = '@';

    /// <summary>
    /// The item a part at <paramref name="level"/> is carried as. Level 0 is the part's own id; a higher level adds
    /// <see cref="LevelMark"/> and the level (<c>gas_engine@2</c>), so an upgraded part keeps its level in an inventory
    /// and on a hotbar, stacks only with parts at the same level, and places at it again.
    /// </summary>
    public static string ItemKey(string part, int level) => level > 0 ? $"{part}{LevelMark}{level}" : part;

    /// <summary>
    /// The part an item places and the level it places at: <c>gas_engine@2</c> is (<c>gas_engine</c>, 2), any other id
    /// is itself at level 0. A mark not followed by a level from 1 up gives level -1 (no such item).
    /// </summary>
    public static (string Part, int Level) SplitItem(string item)
    {
        var at = item.IndexOf(LevelMark);
        if (at < 0)
            return (item, 0);
        var digits = item.AsSpan(at + 1);
        return (item[..at], digits.Length is > 0 and <= 2 && int.TryParse(digits, System.Globalization.NumberStyles.None, null, out var n) && n > 0 && digits[0] != '0' ? n : -1);
    }

    /// <summary>
    /// The definition of <paramref name="item"/> from <paramref name="find"/> (a catalog's own lookup): an upgraded
    /// part's item (<see cref="ItemKey"/>) is its part at that level, or null when the part has no such level.
    /// </summary>
    public static PartDefinition? FindItem(string item, Func<string, PartDefinition?> find)
    {
        var (part, level) = SplitItem(item);
        if (level == 0)
            return find(item);
        return level > 0 && find(part) is { } written && level <= MaxLevel(written) ? At(written, level) : null;
    }

    /// <summary>
    /// The part at <paramref name="level"/> (0 is <paramref name="part"/> itself). A level past the last is treated as
    /// the last; a level whose data does not parse stops at the level before it (validation reports it).
    /// </summary>
    public static PartDefinition At(PartDefinition part, int level)
    {
        level = Math.Clamp(level, 0, MaxLevel(part));
        if (level == 0)
            return part;
        var levels = Cache.GetValue(part, p => new PartDefinition?[MaxLevel(p) + 1]);
        if (levels[level] is { } known)
            return known;
        var previous = At(part, level - 1);
        return levels[level] = Apply(part, level, out _) ?? previous;
    }

    /// <summary>
    /// Merges levels 1 to <paramref name="level"/> onto <paramref name="part"/>; null, with <paramref name="error"/>
    /// saying why, when a level changes a key it may not or the result is not a part.
    /// </summary>
    public static PartDefinition? Apply(PartDefinition part, int level, out string? error)
    {
        error = null;
        var json = (JsonObject)JsonNode.Parse(DataJson.Write(part))!;
        for (var i = 0; i < level && i < MaxLevel(part); i++)
        {
            if (part.Upgrades![i].Set is not { } set)
                continue;
            if (set.Select(kv => kv.Key).FirstOrDefault(k => !UpgradeDefinition.Changeable.Contains(k)) is { } bad)
            {
                error = $"an upgrade can change only {string.Join(", ", UpgradeDefinition.Changeable)}, not '{bad}'.";
                return null;
            }
            Merge(json, set);
        }
        try
        {
            return DataJson.Read<PartDefinition>(json.ToJsonString()) ?? throw new JsonException("the part is null.");
        }
        catch (JsonException e)
        {
            error = $"the upgraded part does not read: {e.Message}";
            return null;
        }
    }

    /// <summary>
    /// What changes from <paramref name="from"/> to <paramref name="to"/> (two levels of one part), for the upgrade dialog:
    /// crafting speed, queue and link range, slots, logic inputs and outputs, then each changed script property. Labels
    /// are English UI text; values are as shown (<c>1.25×</c>, <c>12</c>, <c>1 m</c>).
    /// </summary>
    public static IReadOnlyList<UpgradeChange> Changes(PartDefinition from, PartDefinition to)
    {
        var changes = new List<UpgradeChange>();
        void Add(string label, object? a, object? b, string suffix = "")
        {
            var (x, y) = (Show(a, suffix), Show(b, suffix));
            if (x != y)
                changes.Add(new UpgradeChange(label, x, y));
        }
        Add("Speed", from.Crafting is null ? null : from.Crafting.Speed ?? 1f, to.Crafting is null ? null : to.Crafting.Speed ?? 1f, "×");
        Add("Queue", from.Crafting is null ? null : from.Crafting.QueueSize ?? 1, to.Crafting is null ? null : to.Crafting.QueueSize ?? 1);
        Add("Link range", from.Crafting?.LinkRange, to.Crafting?.LinkRange, " m");
        Add("Slots", from.Inventory?.Slots, to.Inventory?.Slots);
        Add("Durability", from.Durability, to.Durability);
        Add("Inputs", from.Logic?.Inputs, to.Logic?.Inputs);
        Add("Outputs", from.Logic?.Outputs, to.Logic?.Outputs);
        foreach (var key in (to.Properties?.Select(kv => kv.Key) ?? []).Union(from.Properties?.Select(kv => kv.Key) ?? []))
        {
            var a = from.Properties?[key]?.ToJsonString();
            var b = to.Properties?[key]?.ToJsonString();
            if (a != b)
                changes.Add(new UpgradeChange(key.Replace('_', ' '), a ?? "-", b ?? "-"));
        }
        return changes;
    }

    private static string Show(object? value, string suffix) => value switch
    {
        null => "-",
        float f => f.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + suffix,
        _ => $"{value}{suffix}",
    };

    // Objects merge key by key, null removes a key, other values replace (as a Part Editor batch's "set").
    private static void Merge(JsonObject target, JsonObject changes)
    {
        foreach (var (key, value) in changes)
        {
            if (value is null)
                target.Remove(key);
            else if (value is JsonObject inner && target[key] is JsonObject existing)
                Merge(existing, inner);
            else
                target[key] = value.DeepClone();
        }
    }
}
