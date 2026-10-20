using System.Numerics;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Bots;

/// <summary>
/// Bots that live in the wild (<see cref="Bot.Wild"/>), run on the host. In Survival, every bot type's
/// <see cref="BotWild.Interval"/> the host tops up each living player's neighbourhood to <see cref="BotWild.Count"/> of
/// that type, at a random spot between <see cref="BotWild.MinDistance"/> and <see cref="BotWild.MaxDistance"/> away in its
/// habitat (open water at least <see cref="BotWild.MinDepth"/> deep, or dry land), with its habitat all round it out to
/// <see cref="BotWild.ClearRadius"/>. A wild bot every player has left farther than <see cref="BotWild.DespawnDistance"/>
/// behind, and that is not fighting, goes away. Wild bots count towards <see cref="BotSettings.MaxBots"/>.
/// </summary>
public sealed class WildSpawns
{
    // Random spots tried per player per top-up, and points checked round each spot.
    private const int Tries = 16;
    private const int RingPoints = 8;

    private readonly BotActions _bots;
    private readonly Bot[] _types;
    private readonly SurvivalActions _survival;
    private readonly float _seaLevel;
    private readonly Func<float, float, float> _heightAt;
    private readonly Random _random;
    private readonly Dictionary<string, double> _nextTry = new(StringComparer.Ordinal);
    private readonly HashSet<int> _wild = [];

    /// <summary>
    /// Wild spawns of <paramref name="types"/> (those with <see cref="Bot.Wild"/>) for the world whose water surface is
    /// <paramref name="seaLevel"/> over ground <paramref name="heightAt"/> (x, z → m); <paramref name="seed"/> picks the spots.
    /// </summary>
    public WildSpawns(IEnumerable<Bot> types, BotActions bots, SurvivalActions survival, float seaLevel, Func<float, float, float> heightAt, int seed)
    {
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
        _wild.RemoveWhere(id => _bots.Find(id) is not { Dead: false });
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
                for (var i = near; i < wild.Count && _bots.Bots.Count < _bots.Settings.MaxBots; i++)
                {
                    if (Spot(player, wild) is not { } feet)
                        break;
                    _wild.Add(_bots.Spawn(type, feet, (float)(_random.NextDouble() * 360)).Id);
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

    private bool Fits(float x, float z, BotWild wild)
    {
        var ground = _heightAt(x, z);
        return wild.Water ? _seaLevel - ground >= wild.MinDepth : ground > _seaLevel + 0.2f;
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
