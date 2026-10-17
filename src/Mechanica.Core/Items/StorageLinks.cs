using System.Numerics;
using Mechanica.Core.Creations;

namespace Mechanica.Core.Items;

/// <summary>
/// Which containers a crafting station is linked to (UI spec §4): containers (parts with an inventory that are not
/// stations) on the same Creation whose box is within the station's <see cref="Data.CraftingDefinition.LinkRange"/>,
/// measured surface to surface in metres. Crafting takes inputs from linked containers and puts outputs in them.
/// Links follow the build: move a chest away and it is no longer linked.
/// </summary>
public static class StorageLinks
{
    // A little slack so containers exactly at the range, on the 0.25 m grid, still count.
    private const float Slack = 1e-3f;

    /// <summary>Containers linked to <paramref name="station"/>, by part id; empty when it is not a station or links nothing.</summary>
    public static IReadOnlyList<int> Of(Creation creation, int station)
    {
        if (creation.FindPart(station) is null || creation.DefinitionOf(station).Crafting?.LinkRange is not (> 0 and var range))
            return [];
        var box = Box(creation, station);
        return [.. creation.Parts.Where(p => p.Id != station && IsContainer(creation, p.Id) && Gap(box, Box(creation, p.Id)) <= range + Slack).Select(p => p.Id)];
    }

    /// <summary>Stations <paramref name="container"/> is linked to, by part id.</summary>
    public static IEnumerable<int> StationsOf(Creation creation, int container) =>
        IsContainer(creation, container)
            ? creation.Parts.Where(p => creation.DefinitionOf(p.Id).Crafting is not null && Of(creation, p.Id).Contains(container)).Select(p => p.Id)
            : [];

    /// <summary>True for a part with an inventory that is not a crafting station.</summary>
    public static bool IsContainer(Creation creation, int part) =>
        creation.DefinitionOf(part) is { Inventory: not null, Crafting: null };

    // Grid-space box of a part's cells, in metres.
    private static (Vector3 Min, Vector3 Max) Box(Creation creation, int part)
    {
        var cells = creation.CellsOf(part).ToList();
        var min = new Vector3(cells.Min(c => c.X), cells.Min(c => c.Y), cells.Min(c => c.Z));
        var max = new Vector3(cells.Max(c => c.X), cells.Max(c => c.Y), cells.Max(c => c.Z)) + Vector3.One;
        return (min * MechanicaInfo.GridSize, max * MechanicaInfo.GridSize);
    }

    private static float Gap((Vector3 Min, Vector3 Max) a, (Vector3 Min, Vector3 Max) b) =>
        Vector3.Max(Vector3.Zero, Vector3.Max(a.Min - b.Max, b.Min - a.Max)).Length();
}
