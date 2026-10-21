using System.Numerics;
using Mechanica.Core.World;

namespace Mechanica.Core.Bots;

/// <summary>
/// A disc of the world where no bot appears (round a POI with <see cref="PoiPrefab.BotFreeRadius"/>), or no hostile one
/// (<see cref="PoiPrefab.HostileFreeRadius"/>).
/// </summary>
/// <param name="Centre">World <c>[x, z]</c> of its centre.</param>
/// <param name="Radius">Its radius, in m.</param>
/// <param name="HostileOnly">Only hostile bots keep out; passive ones (<see cref="Bot.Passive"/>, a Grazer) may appear.</param>
public sealed record BotFreeZone(Vector2 Centre, float Radius, bool HostileOnly = false);

/// <summary>
/// The places where bots never appear (the crash site, so a new player starts in peace), or no hostile ones (round a
/// Workshop). Wild spawns, POI guards and raiders all keep out of them; a bot spawned by hand (<c>SpawnBot</c>) may still go there, and bots may walk in.
/// Distances are horizontal.
/// </summary>
public sealed class BotFreeZones(IEnumerable<BotFreeZone> zones)
{
    // How far past a zone's edge a pushed-out raider appears, m.
    private const float Margin = 1f;

    private readonly BotFreeZone[] _zones = [.. zones];

    /// <summary>No zones: bots may appear anywhere.</summary>
    public static BotFreeZones None { get; } = new([]);

    /// <summary>The zones.</summary>
    public IReadOnlyList<BotFreeZone> Zones => _zones;

    /// <summary>The zones round every POI <paramref name="world"/> placed whose prefab gives a bot-free or hostile-free radius.</summary>
    public static BotFreeZones Of(WorldGenerator world) =>
        new(world.Pois.SelectMany(p => (IEnumerable<BotFreeZone>)
        [
            .. p.Poi.Prefab.BotFreeRadius is > 0f and var all ? [new BotFreeZone(p.Centre, all)] : Array.Empty<BotFreeZone>(),
            .. p.Poi.Prefab.HostileFreeRadius is > 0f and var hostile ? [new BotFreeZone(p.Centre, hostile, HostileOnly: true)] : Array.Empty<BotFreeZone>(),
        ]));

    /// <summary>Whether x/z lies in a zone that keeps out a bot that is <paramref name="hostile"/> (or not).</summary>
    public bool Contains(float x, float z, bool hostile = true) =>
        _zones.Any(zone => (hostile || !zone.HostileOnly) && Vector2.DistanceSquared(new Vector2(x, z), zone.Centre) < zone.Radius * zone.Radius);

    /// <summary>
    /// For a raider (hostile): <paramref name="at"/> when it is outside every zone; otherwise the point pushed straight out from the centre of the
    /// zone holding it to just past its edge (away from <paramref name="towards"/>'s side when it is at the very centre),
    /// again until it is in none.
    /// </summary>
    public Vector2 PushOut(Vector2 at, Vector2 towards)
    {
        // Overlapping zones can push a point from one into another; a few rounds settle it.
        for (var round = 0; round <= _zones.Length; round++)
        {
            var zone = _zones.FirstOrDefault(z => Vector2.DistanceSquared(at, z.Centre) < z.Radius * z.Radius);
            if (zone is null)
                return at;
            var away = at - zone.Centre;
            if (away.LengthSquared() < 1e-6f)
                away = zone.Centre - towards;
            if (away.LengthSquared() < 1e-6f)
                away = Vector2.UnitX;
            at = zone.Centre + Vector2.Normalize(away) * (zone.Radius + Margin);
        }
        return at;
    }
}
