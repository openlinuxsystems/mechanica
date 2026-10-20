using System.Numerics;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Bots;

/// <summary>
/// Bots that live in the wild (<see cref="Bot.Wild"/>), run on the host. In Survival, every bot type's
/// <see cref="BotWild.Interval"/> the host tops up each living player's neighbourhood to <see cref="BotWild.Count"/> of
/// that type, at a random spot between <see cref="BotWild.MinDistance"/> and <see cref="BotWild.MaxDistance"/> away in its
/// habitat (open water at least <see cref="BotWild.MinDepth"/> deep, or dry land), with its habitat all round it out to
/// <see cref="BotWild.ClearRadius"/>, and in one of its <see cref="BotWild.Biomes"/> when it names any. A herd
/// (<see cref="BotWild.HerdMin"/> to <see cref="BotWild.HerdMax"/>) appears together a few metres apart, and a share of
/// them appear round one of its <see cref="BotWild.Near"/> objects in reach. A wild bot every player has left farther than <see cref="BotWild.DespawnDistance"/>
/// behind, and that is not fighting, goes away; a kept one (<see cref="BotState.Kept"/>) is no longer wild and stays. Wild bots count towards <see cref="BotSettings.MaxBots"/>.
/// </summary>
public sealed class WildSpawns
{
    // Random spots tried per player per top-up, and points checked round each spot.
    private const int Tries = 16;
    private const int RingPoints = 8;
    // How far from the first a herd's others appear, m.
    private const float HerdGap = 1.5f;
    private const float HerdSpread = 5f;

    private readonly BotActions _bots;
    private readonly Bot[] _types;
    private readonly SurvivalActions _survival;
    private readonly float _seaLevel;
    private readonly Func<float, float, float> _heightAt;
    private readonly Func<float, float, string?>? _biomeAt;
    private readonly Func<Vector2, float, IEnumerable<(string Object, Vector2 At)>>? _objectsNear;
    private readonly Random _random;
    private readonly Dictionary<string, double> _nextTry = new(StringComparer.Ordinal);
    private readonly HashSet<int> _wild = [];

    /// <summary>
    /// Wild spawns of <paramref name="types"/> (those with <see cref="Bot.Wild"/>) for the world whose water surface is
    /// <paramref name="seaLevel"/> over ground <paramref name="heightAt"/> (x, z → m); <paramref name="seed"/> picks the spots.
    /// <paramref name="biomeAt"/> (x, z → biome id) and <paramref name="objectsNear"/> (centre, radius → world object or scene
    /// ids with their x/z) serve <see cref="BotWild.Biomes"/> and <see cref="BotWild.Near"/>; without them a bot that names
    /// biomes never appears and one with near objects appears anywhere.
    /// </summary>
    public WildSpawns(IEnumerable<Bot> types, BotActions bots, SurvivalActions survival, float seaLevel, Func<float, float, float> heightAt, int seed,
        Func<float, float, string?>? biomeAt = null, Func<Vector2, float, IEnumerable<(string Object, Vector2 At)>>? objectsNear = null)
    {
        _biomeAt = biomeAt;
        _objectsNear = objectsNear;
        _types = [.. types.Where(t => t.Wild is not null)];
        _bots = bots;
        _survival = survival;
        _seaLevel = seaLevel;
        _heightAt = heightAt;
        _random = new Random(seed);
    }

    /// <summary>The wild bots alive now.</summary>
    public IReadOnlyCollection<int> Bots => _wild;

    /// <summary>Spawns and clears wild bots. The host calls it every physics tick.</summary>
    public void Tick()
    {
        // A wild bot that died, went or was kept (tamed) is no longer this one's to count or take away.
        _wild.RemoveWhere(id => _bots.Find(id) is not { Dead: false, Kept: false });
        var survival = _survival.Mode == GameMode.Survival;
        var players = survival ? _bots.LivingPlayers().Select(p => p.Centre).ToList() : [];
        var now = _bots.Now;
        foreach (var type in _types)
        {
            if (type.Wild is not { } wild || now < _nextTry.GetValueOrDefault(type.QualifiedId, double.NegativeInfinity))
                continue;
            _nextTry[type.QualifiedId] = now + wild.Interval;
            var mine = _wild.Select(_bots.Find).OfType<BotState>().Where(b => b.Type == type).ToList();
            foreach (var bot in mine.Where(b => !b.Engaged && !players.Any(p => Flat(p, b.Position) <= wild.DespawnDistance)))
            {
                _wild.Remove(bot.Id);
                _bots.Remove(bot.Id);
            }
            foreach (var player in players)
            {
                var near = _wild.Select(_bots.Find).OfType<BotState>().Count(b => b.Type == type && Flat(player, b.Position) <= wild.DespawnDistance);
                while (near < wild.Count && _bots.Bots.Count < _bots.Settings.MaxBots)
                {
                    // no herd smaller than its least to fill the count up, so none is left alone
                    if (near > 0 && wild.Count - near < wild.HerdMin)
                        break;
                    var first = (wild.Near is { } objects && _random.NextDouble() < objects.Share ? NearSpot(player, wild, objects) : null)
                        ?? Spot(player, wild);
                    if (first is not { } feet)
                        break;
                    var room = wild.Count - near;
                    var herd = Math.Min(_random.Next(wild.HerdMin, wild.HerdMax + 1), room);
                    // nor leave less room than a herd: this one takes it, or leaves room for the least herd
                    if (room - herd is var left && left > 0 && left < wild.HerdMin)
                        herd = room <= wild.HerdMax ? room : Math.Max(wild.HerdMin, room - wild.HerdMin);
                    for (var k = 0; k < herd && _bots.Bots.Count < _bots.Settings.MaxBots; k++)
                    {
                        var at = k == 0 ? feet : Around(new Vector2(feet.X, feet.Z), HerdGap, HerdSpread, wild) ?? feet;
                        _wild.Add(_bots.Spawn(type, at, (float)(_random.NextDouble() * 360)).Id);
                        near++;
                    }
                }
            }
        }
    }

    // A random spot of the habitat round the player, or null when none of the tries is one.
    private Vector3? Spot(Vector3 player, BotWild wild)
    {
        for (var i = 0; i < Tries; i++)
        {
            var angle = _random.NextDouble() * Math.Tau;
            var distance = wild.MinDistance + (float)_random.NextDouble() * (wild.MaxDistance - wild.MinDistance);
            var x = player.X + (float)Math.Cos(angle) * distance;
            var z = player.Z + (float)Math.Sin(angle) * distance;
            if (!Fits(x, z, wild))
                continue;
            var ring = Enumerable.Range(0, RingPoints).Select(k => k * MathF.Tau / RingPoints);
            if (ring.All(a => Fits(x + MathF.Cos(a) * wild.ClearRadius, z + MathF.Sin(a) * wild.ClearRadius, wild)))
                return new Vector3(x, _heightAt(x, z) + 0.05f, z);
        }
        return null;
    }

    // A spot round one of the near objects in reach, at least min_distance from the player, or null.
    private Vector3? NearSpot(Vector3 player, BotWild wild, BotWildNear near)
    {
        if (_objectsNear is null)
            return null;
        var centre = new Vector2(player.X, player.Z);
        var objects = _objectsNear(centre, wild.MaxDistance)
            .Where(o => near.Objects.Contains(o.Object) && Vector2.Distance(o.At, centre) <= wild.MaxDistance).ToList();
        if (objects.Count == 0)
            return null;
        var at = objects[_random.Next(objects.Count)].At;
        return Around(at, near.Radius * 0.4f, near.Radius, wild) is { } feet && Vector2.Distance(new Vector2(feet.X, feet.Z), centre) >= wild.MinDistance ? feet : null;
    }

    // A spot of its habitat between inner and outer m from a point, or null.
    private Vector3? Around(Vector2 at, float inner, float outer, BotWild wild)
    {
        for (var i = 0; i < Tries; i++)
        {
            var angle = _random.NextDouble() * Math.Tau;
            var distance = inner + (float)_random.NextDouble() * (outer - inner);
            var x = at.X + (float)Math.Cos(angle) * distance;
            var z = at.Y + (float)Math.Sin(angle) * distance;
            if (Fits(x, z, wild))
                return new Vector3(x, _heightAt(x, z) + 0.05f, z);
        }
        return null;
    }

    private bool Fits(float x, float z, BotWild wild)
    {
        var ground = _heightAt(x, z);
        if (!(wild.Water ? _seaLevel - ground >= wild.MinDepth : ground > _seaLevel + 0.2f))
            return false;
        return wild.Biomes.Count == 0 || (_biomeAt?.Invoke(x, z) is { } biome && wild.Biomes.Contains(biome));
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
