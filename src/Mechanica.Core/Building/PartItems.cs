using Mechanica.Core.Data;

namespace Mechanica.Core.Building;

/// <summary>
/// Which item a part is placed from and which part an item sets down on the ground. Most parts are their own item; a
/// part with <see cref="PartDefinition.PlacedFrom"/> (a soil mound) is placed from, and picked up as, another item (a
/// Soil Bag), and an item with <see cref="PartDefinition.GroundPart"/> sets that part down on the ground. A bare id
/// in either belongs to the mod of the part that names it.
/// </summary>
public static class PartItems
{
    /// <summary>The qualified item placing <paramref name="part"/> uses up and removing it gives back.</summary>
    public static string ItemFor(IPartCatalog catalog, string part)
    {
        var qualified = Identifiers.Qualify(part);
        return catalog.FindDefinition(qualified)?.PlacedFrom is { } from ? Identifiers.Qualify(from, OwnerOf(qualified)) : qualified;
    }

    /// <summary>The qualified part <paramref name="item"/> sets down on the ground, or null when it sets down itself.</summary>
    public static string? GroundPartFor(IPartCatalog catalog, string item)
    {
        var qualified = Identifiers.Qualify(item);
        return catalog.FindDefinition(qualified)?.GroundPart is { } ground ? Identifiers.Qualify(ground, OwnerOf(qualified)) : null;
    }

    private static string OwnerOf(string qualified) => qualified[..qualified.IndexOf(':')];
}
