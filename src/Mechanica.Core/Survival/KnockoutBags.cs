using Mechanica.Core.Creations;
using Mechanica.Core.Ui;

namespace Mechanica.Core.Survival;

/// <summary>A player's knockout bags as compass and map waypoints, so they can find their things again.</summary>
public static class KnockoutBags
{
    /// <summary>The prefix of a knockout bag's waypoint id; the pile's id follows it.</summary>
    public const string WaypointPrefix = "knockout_bag:";

    /// <summary>True when <paramref name="waypoint"/> marks a knockout bag, which the compass draws as a bag rather than a diamond.</summary>
    public static bool IsBag(Waypoint waypoint) => waypoint.Id.StartsWith(WaypointPrefix, StringComparison.Ordinal);

    /// <summary>
    /// A waypoint named <paramref name="name"/> at each knockout bag <paramref name="peer"/> left behind, newest first.
    /// Other piles and other players' bags are left out.
    /// </summary>
    public static IEnumerable<Waypoint> WaypointsFor(IEnumerable<ItemDrop> drops, int peer, string name) => drops
        .Where(d => d.KnockedOut && d.Owner == peer)
        .OrderByDescending(d => d.Id)
        .Select(d => new Waypoint(WaypointPrefix + d.Id, name, d.Position.ToNumerics()));
}
