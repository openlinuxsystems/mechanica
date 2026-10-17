using System.Text.Json;
using Mechanica.Core.Data;

namespace Mechanica.Core.World;

/// <summary>
/// What makes a generated world this world: its seed, and the scattered objects destroyed since it was
/// generated. The host owns it and sends it to every joining client; everything else regenerates from it.
/// Saved as JSON (<c>{ "seed": 42, "destroyed": ["3.10.-4"] }</c>), ids sorted.
/// </summary>
public sealed class WorldState
{
    private readonly HashSet<WorldObjectId> _destroyed = [];
    private readonly Dictionary<WorldObjectId, float> _damage = [];

    /// <summary>A fresh world.</summary>
    public WorldState(long seed) => Seed = seed;

    /// <summary>World seed.</summary>
    public long Seed { get; }

    /// <summary>Destroyed objects.</summary>
    public IReadOnlyCollection<WorldObjectId> Destroyed => _destroyed;

    /// <summary>True when the object has been destroyed.</summary>
    public bool IsDestroyed(WorldObjectId id) => _destroyed.Contains(id);

    /// <summary>Marks an object destroyed. Returns false if it already was.</summary>
    public bool Destroy(WorldObjectId id)
    {
        _damage.Remove(id);
        return _destroyed.Add(id);
    }

    /// <summary>Health an object has lost to hits (chunk 20). Not saved: a damaged tree heals on reload.</summary>
    public float DamageOf(WorldObjectId id) => _damage.GetValueOrDefault(id);

    /// <summary>Adds <paramref name="damage"/> to what an object has lost and returns the new total.</summary>
    public float AddDamage(WorldObjectId id, float damage) => _damage[id] = DamageOf(id) + Math.Max(0, damage);

    /// <summary>Writes the state as JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(new Dto { Seed = Seed, Destroyed = [.. _destroyed.Select(i => i.ToString()).Order(StringComparer.Ordinal)] }, DataJson.WriteOptions) + "\n";

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
        return state;
    }

    private sealed record Dto
    {
        public long? Seed { get; init; }

        public List<string>? Destroyed { get; init; }
    }
}
