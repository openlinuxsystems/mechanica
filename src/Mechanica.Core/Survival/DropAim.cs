using System.Numerics;
using Mechanica.Core.Creations;

namespace Mechanica.Core.Survival;

/// <summary>
/// Which pile of items the crosshair is on (UI spec §9: right click picks a loose item up). Piles have no collider,
/// so each is a sphere of <see cref="Radius"/> round its middle.
/// </summary>
public static class DropAim
{
    /// <summary>How far round a pile's middle the aim ray may pass and still be on it, in metres.</summary>
    public const float Radius = 0.3f;

    /// <summary>Height of a pile's middle above its ground point, in metres.</summary>
    public const float Middle = 0.15f;

    /// <summary>
    /// The nearest pile the ray from <paramref name="from"/> along <paramref name="direction"/> passes through within
    /// <paramref name="maxDistance"/> metres (where something solid blocks the view, or the reach), or null.
    /// </summary>
    public static ItemDrop? Aimed(IEnumerable<ItemDrop> drops, Vector3 from, Vector3 direction, float maxDistance)
    {
        var dir = Vector3.Normalize(direction);
        ItemDrop? best = null;
        var bestT = float.MaxValue;
        foreach (var drop in drops)
        {
            var centre = drop.Position.ToNumerics() + new Vector3(0, Middle, 0);
            var t = Vector3.Dot(centre - from, dir);
            if (t < 0 || t > maxDistance + Radius || t >= bestT)
                continue;
            if (Vector3.DistanceSquared(from + dir * t, centre) <= Radius * Radius)
                (best, bestT) = (drop, t);
        }
        return best;
    }
}
