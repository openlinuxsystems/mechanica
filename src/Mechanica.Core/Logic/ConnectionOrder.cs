using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Logic;

/// <summary>
/// What the Connect Tool shows on linked parts beyond their nodes, as in Scrap Mechanic: the order number a seat or a
/// controller gives each part it is linked to (the order its script and the controller's frames go through them, part
/// order), and the turn arrow on a bearing that something drives (a seat, a controller or an engine), which right
/// click reverses. Both follow from the part data, so a modded seat, controller or engine gets them too.
/// </summary>
public static class ConnectionOrder
{
    /// <summary>True when <paramref name="source"/> numbers the parts it is linked to: a seat or a controller (frames).</summary>
    public static bool Numbers(PartDefinition source) =>
        source.Logic is not null && (source.Seat is not null
            || source.Settings?.Any(s => s.Type is SettingType.Sequence or SettingType.NumberList) == true);

    /// <summary>True when <paramref name="source"/> turns the bearings it is linked to: a numbering part or an engine (power).</summary>
    public static bool Drives(PartDefinition source) =>
        source.Logic is not null && (Numbers(source) || source.Logic.KindOrDefault == PortKind.Power);

    /// <summary>
    /// The numbers <paramref name="part"/> shows: for each seat or controller linked to it, its place among that
    /// part's links (1 for the first), with the source's part id. Empty for a part no numbering part is linked to.
    /// </summary>
    public static IReadOnlyList<(int Source, int Number)> NumbersOf(Creation creation, int part)
    {
        ArgumentNullException.ThrowIfNull(creation);
        var numbers = new List<(int, int)>();
        foreach (var source in creation.LinksTo(part))
        {
            if (!Numbers(creation.DefinitionOf(source)))
                continue;
            var index = creation.LinksFrom(source).ToList().IndexOf(part);
            if (index >= 0)
                numbers.Add((source, index + 1));
        }
        return numbers;
    }

    /// <summary>True when <paramref name="part"/> can be reversed and something that drives it is linked to it.</summary>
    public static bool ShowsDirection(Creation creation, int part)
    {
        ArgumentNullException.ThrowIfNull(creation);
        return creation.FindPart(part) is not null && creation.DefinitionOf(part).Logic?.Reverse is not null
            && creation.LinksTo(part).Any(source => Drives(creation.DefinitionOf(source)));
    }
}
