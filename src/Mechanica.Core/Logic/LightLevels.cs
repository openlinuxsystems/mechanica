namespace Mechanica.Core.Logic;

/// <summary>One part's light level, as the host sends it to clients.</summary>
/// <param name="Creation">The part's Creation.</param>
/// <param name="Part">The part (it has a <c>light</c> section).</param>
/// <param name="Level">0 (dark) to 1 (the light's full energy).</param>
public sealed record LightLevel(int Creation, int Part, float Level);

/// <summary>
/// How bright each part's light is (chunk 23). Part scripts set the levels on the host with
/// <c>part.set_light</c>; the host sends them to every client, and every peer draws its lights from them.
/// A part with no level is dark. <see cref="Revision"/> changes whenever a level does.
/// </summary>
public sealed class LightLevels
{
    private readonly SortedDictionary<(int Creation, int Part), float> _levels = [];

    /// <summary>Goes up by one on every change.</summary>
    public int Revision { get; private set; }

    /// <summary>A part's level now: 0 to 1, 0 when it has none.</summary>
    public float Level(int creation, int part) => _levels.GetValueOrDefault((creation, part));

    /// <summary>
    /// Sets a part's level, clamped to 0 to 1 (non-finite counts as 0). Returns true when it changed.
    /// </summary>
    public bool Set(int creation, int part, float level)
    {
        level = float.IsFinite(level) ? Math.Clamp(level, 0f, 1f) : 0f;
        if (Level(creation, part) == level)
            return false;
        if (level == 0f)
            _levels.Remove((creation, part));
        else
            _levels[(creation, part)] = level;
        Revision++;
        return true;
    }

    /// <summary>Drops a part's level (the part went away).</summary>
    public void Forget(int creation, int part)
    {
        if (_levels.Remove((creation, part)))
            Revision++;
    }

    /// <summary>Drops every level.</summary>
    public void Clear()
    {
        if (_levels.Count == 0)
            return;
        _levels.Clear();
        Revision++;
    }

    /// <summary>Every lit part, in creation then part order.</summary>
    public IReadOnlyList<LightLevel> All => [.. _levels.Select(kv => new LightLevel(kv.Key.Creation, kv.Key.Part, kv.Value))];

    /// <summary>Replaces every level with the host's (a client's copy). Bad levels are clamped as in <see cref="Set"/>.</summary>
    public void Load(IEnumerable<LightLevel> levels)
    {
        _levels.Clear();
        foreach (var light in levels)
        {
            var level = float.IsFinite(light.Level) ? Math.Clamp(light.Level, 0f, 1f) : 0f;
            if (level > 0f)
                _levels[(light.Creation, light.Part)] = level;
        }
        Revision++;
    }
}
