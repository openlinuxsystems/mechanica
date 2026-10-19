using System.Text.Json;
using Mechanica.Core.Data;

namespace Mechanica.Core.World;

/// <summary>
/// What makes a generated world this world: its seed, and the scattered objects destroyed since it was
/// generated, with when each one that regrows (<see cref="WorldObjectDefinition.Regrow"/>) stands again. The host
/// owns it and sends it to every joining client; everything else regenerates from it. Saved as JSON
/// (<c>{ "seed": 42, "destroyed": ["3.10.-4"], "regrow": { "poi.7.2": 5400 } }</c>), ids sorted.
/// </summary>
public sealed class WorldState
{
    private readonly HashSet<WorldObjectId> _destroyed = [];
    private readonly Dictionary<WorldObjectId, float> _damage = [];
    private readonly Dictionary<WorldObjectId, double> _regrow = [];

    /// <summary>A fresh world.</summary>
    public WorldState(long seed) => Seed = seed;

    /// <summary>World seed.</summary>
    public long Seed { get; }

    /// <summary>Destroyed objects.</summary>
    public IReadOnlyCollection<WorldObjectId> Destroyed => _destroyed;

    /// <summary>True when the object has been destroyed.</summary>
    public bool IsDestroyed(WorldObjectId id) => _destroyed.Contains(id);

    /// <summary>
    /// Marks an object destroyed; with <paramref name="regrowAt"/> it stands again at that world time (the scripts' clock,
    /// which saves carry on). Returns false if it already was destroyed.
    /// </summary>
    public bool Destroy(WorldObjectId id, double? regrowAt = null)
    {
        _damage.Remove(id);
        if (!_destroyed.Add(id))
            return false;
        if (regrowAt is { } at)
            _regrow[id] = at;
        return true;
    }

    /// <summary>Destroyed objects that will stand again, with the world time when.</summary>
    public IReadOnlyDictionary<WorldObjectId, double> Regrowing => _regrow;

    /// <summary>Destroyed objects whose regrow time has come by <paramref name="now"/>, in id order.</summary>
    public IReadOnlyList<WorldObjectId> RegrownBy(double now) =>
        [.. _regrow.Where(r => r.Value <= now).Select(r => r.Key).OrderBy(i => i.ToString(), StringComparer.Ordinal)];

    /// <summary>Stands a destroyed object again, as good as new. Returns false if it was not destroyed.</summary>
    public bool Restore(WorldObjectId id)
    {
        _regrow.Remove(id);
        _damage.Remove(id);
        return _destroyed.Remove(id);
    }

    /// <summary>Health an object has lost to hits (chunk 20). Not saved: a damaged tree heals on reload.</summary>
    public float DamageOf(WorldObjectId id) => _damage.GetValueOrDefault(id);

    /// <summary>Adds <paramref name="damage"/> to what an object has lost and returns the new total.</summary>
    public float AddDamage(WorldObjectId id, float damage) => _damage[id] = DamageOf(id) + Math.Max(0, damage);

    /// <summary>Writes the state as JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(new Dto
    {
        Seed = Seed,
        Destroyed = [.. _destroyed.Select(i => i.ToString()).Order(StringComparer.Ordinal)],
        Regrow = _regrow.Count == 0 ? null : new SortedDictionary<string, double>(_regrow.ToDictionary(r => r.Key.ToString(), r => r.Value), StringComparer.Ordinal),
    }, DataJson.WriteOptions) + "\n";

    /// <summary>Reads a state written by <see cref="ToJson"/>.</summary>
    /// <exception cref="FormatException">The JSON is malformed or holds a bad id.</exception>
    public static WorldState FromJson(string json)
    {
        Dto? dto;
        try
        {
            dto = DataJson.Read<Dto>(json);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid world state JSON: {e.Message}", e);
        }
        if (dto?.Seed is not { } seed)
            throw new FormatException("World state JSON has no seed.");
        var state = new WorldState(seed);
        foreach (var text in dto.Destroyed ?? [])
        {
            if (!WorldObjectId.TryParse(text, out var id))
                throw new FormatException($"'{text}' is not a world object id.");
            state.Destroy(id);
        }
        foreach (var (text, at) in dto.Regrow ?? [])
        {
            if (!WorldObjectId.TryParse(text, out var id) || !state.IsDestroyed(id))
                throw new FormatException($"regrow names '{text}', which is not a destroyed world object.");
            if (!double.IsFinite(at))
                throw new FormatException($"regrow time of '{text}' must be a number.");
            state._regrow[id] = at;
        }
        return state;
    }

    private sealed record Dto
    {
        public long? Seed { get; init; }

        public List<string>? Destroyed { get; init; }

        public SortedDictionary<string, double>? Regrow { get; init; }
    }
}
