using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Items;

/// <summary>
/// One row of the vehicle gauges: a container that feeds a <see cref="GaugeKind"/>, or every one of a kind summed.
/// </summary>
/// <param name="Kind">What it holds.</param>
/// <param name="Part">The container's part id; 0 for a row that sums a kind.</param>
/// <param name="Number">Its default number within its kind (the 2 of "Gas 2"), counted in part id order; 0 for a sum.</param>
/// <param name="Held">How many of its item it holds.</param>
/// <param name="Capacity">How many it could hold: <see cref="Held"/> plus the room left.</param>
/// <param name="Name">The name a player gave it, or null for its default.</param>
public sealed record GaugeRow(GaugeKind Kind, int Part, int Number, int Held, int Capacity, string? Name = null);

/// <summary>
/// The HUD gauges a seated player sees: how full each container on their Creation is that feeds a gauge
/// (<see cref="InventoryDefinition.Gauge"/>). The host reads them and sends each seated player theirs; the HUD shows them
/// one row per container or one per kind (<see cref="Ui.VehicleGaugeMode"/>). Also the rules for naming a container.
/// </summary>
public static class VehicleGauges
{
    /// <summary>The most characters a part's name has.</summary>
    public const int MaxNameLength = 20;

    /// <summary>
    /// Every gauge on <paramref name="creation"/>: kinds in <see cref="GaugeKind"/> order, then containers in part id order
    /// (the order they were placed), so renaming one never moves it.
    /// </summary>
    public static IReadOnlyList<GaugeRow> Read(Creation creation)
    {
        var rows = new List<GaugeRow>();
        var numbers = new Dictionary<GaugeKind, int>();
        foreach (var part in creation.Parts.OrderBy(p => p.Id))
        {
            if (creation.DefinitionOf(part.Id).Inventory is not { Gauge: { } kind, Accepts: [var first, ..] }
                || creation.InventoryOf(part.Id) is not { } inventory)
                continue;
            var number = numbers[kind] = numbers.GetValueOrDefault(kind) + 1;
            var item = Identifiers.Qualify(first);
            var held = inventory.Count(item);
            rows.Add(new GaugeRow(kind, part.Id, number, held, held + inventory.SpaceFor(item), part.Name));
        }
        return [.. rows.OrderBy(r => r.Kind).ThenBy(r => r.Part)];
    }

    /// <summary>
    /// The gauge part <paramref name="partId"/> feeds and its default number within its kind (the 2 of "Gas 2"), as
    /// <see cref="Read"/> numbers it; null when it feeds none.
    /// </summary>
    public static (GaugeKind Kind, int Number)? NumberOf(Creation creation, int partId)
    {
        if (creation.FindPart(partId) is null || creation.DefinitionOf(partId).Inventory is not { Gauge: { } kind, Accepts.Count: > 0 })
            return null;
        var number = creation.Parts.Count(p => p.Id <= partId && creation.DefinitionOf(p.Id).Inventory is { Gauge: { } k, Accepts.Count: > 0 } && k == kind);
        return (kind, number);
    }

    /// <summary><paramref name="rows"/> summed into one row per kind, with no part, number or name, in kind order.</summary>
    public static IReadOnlyList<GaugeRow> PerKind(IEnumerable<GaugeRow> rows) =>
        [.. rows.GroupBy(r => r.Kind).OrderBy(g => g.Key)
            .Select(g => new GaugeRow(g.Key, 0, 0, g.Sum(r => r.Held), g.Sum(r => r.Capacity)))];

    /// <summary>
    /// A part name as it is stored: trimmed, without control characters, at most <see cref="MaxNameLength"/> characters;
    /// null when nothing is left (the part shows its default name).
    /// </summary>
    public static string? CleanName(string? name)
    {
        if (name is null)
            return null;
        var cleaned = new string([.. name.Where(c => !char.IsControl(c))]).Trim();
        if (cleaned.Length > MaxNameLength)
            cleaned = cleaned[..MaxNameLength].TrimEnd();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>Null when <paramref name="name"/> can be stored as it is, or why not.</summary>
    public static string? CheckName(string name) =>
        name.Length > MaxNameLength ? $"A name has at most {MaxNameLength} characters (got {name.Length})."
        : name.Any(char.IsControl) ? "A name can't hold control characters."
        : null;
}
