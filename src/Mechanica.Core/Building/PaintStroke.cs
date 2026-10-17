namespace Mechanica.Core.Building;

/// <summary>
/// One drag of the Paint Tool (UI spec §2): the parts the crosshair passes over while the button is held, each once,
/// at most <see cref="Limit"/> of them. The tool takes what is new every so often (<see cref="TakePending"/>) and
/// sends one <c>Paint</c> per Creation, so a long drag shows its colour as it goes.
/// </summary>
/// <param name="limit">Most parts one drag covers.</param>
public sealed class PaintStroke(int limit)
{
    private readonly HashSet<(int Creation, int Part)> _seen = [];
    private readonly List<(int Creation, int Part)> _pending = [];

    /// <summary>Most parts the stroke covers.</summary>
    public int Limit { get; } = Math.Max(1, limit);

    /// <summary>How many parts the stroke has covered.</summary>
    public int Count => _seen.Count;

    /// <summary>True once the stroke has covered <see cref="Limit"/> parts.</summary>
    public bool Full => _seen.Count >= Limit;

    /// <summary>Adds a part passed over. False when it was already covered or the stroke is full.</summary>
    public bool Add(int creation, int part)
    {
        if (Full || !_seen.Add((creation, part)))
            return false;
        _pending.Add((creation, part));
        return true;
    }

    /// <summary>The parts added since the last call, grouped by Creation in the order first reached.</summary>
    public IReadOnlyList<(int Creation, IReadOnlyList<int> Parts)> TakePending()
    {
        var batches = _pending.GroupBy(p => p.Creation).Select(g => (g.Key, (IReadOnlyList<int>)[.. g.Select(p => p.Part)])).ToList();
        _pending.Clear();
        return batches;
    }
}
