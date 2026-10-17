using Mechanica.Core.Creations;

namespace Mechanica.Core.Logic;

/// <summary>
/// The host's logic and power bus (chunk 10). Every part may put out one number (its output, 0 when it has
/// set none); a part's input is the sum of the outputs of the parts linked to it (<see cref="Creation.Links"/>).
/// What the numbers mean is up to the parts' scripts (D2): a switch puts out 1 while on, a seat its throttle,
/// an engine its power. <see cref="Step"/> runs once per physics step and reports every input that changed, so
/// a signal takes one step per link to travel.
/// </summary>
public sealed class SignalBus
{
    private readonly Dictionary<(int Creation, int Part), float> _outputs = [];
    private readonly Dictionary<(int Creation, int Part), float> _inputs = [];

    /// <summary>A part's output now (0 when it has set none).</summary>
    public float Output(int creation, int part) => _outputs.GetValueOrDefault((creation, part));

    /// <summary>Sets a part's output. Non-finite values count as 0.</summary>
    public void SetOutput(int creation, int part, float value)
    {
        if (!float.IsFinite(value) || value == 0f)
            _outputs.Remove((creation, part));
        else
            _outputs[(creation, part)] = value;
    }

    /// <summary>The input a part was last given by <see cref="Step"/> (0 before any).</summary>
    public float Input(int creation, int part) => _inputs.GetValueOrDefault((creation, part));

    /// <summary>What feeds a part now: each linked source and its output, by source id.</summary>
    public IReadOnlyList<(int Part, float Value)> Inputs(Creation creation, int part) =>
        [.. creation.LinksTo(part).Select(from => (from, Output(creation.Id, from)))];

    /// <summary>Every non-zero output of one Creation, by part id.</summary>
    public IReadOnlyDictionary<int, float> OutputsOf(int creation) =>
        _outputs.Where(kv => kv.Key.Creation == creation).OrderBy(kv => kv.Key.Part).ToDictionary(kv => kv.Key.Part, kv => kv.Value);

    /// <summary>
    /// Sums every part's input from its sources' outputs and returns the inputs that changed since the last
    /// step (including back to 0 when a link or source went away), in creation then part order.
    /// </summary>
    public IReadOnlyList<(int Creation, int Part, float Input)> Step(IEnumerable<Creation> creations)
    {
        var changed = new List<(int, int, float)>();
        var present = new HashSet<int>();
        foreach (var creation in creations.OrderBy(c => c.Id))
        {
            present.Add(creation.Id);
            var sums = new SortedDictionary<int, float>();
            foreach (var link in creation.Links)
                sums[link.To] = sums.GetValueOrDefault(link.To) + Output(creation.Id, link.From);
            foreach (var (part, _) in _inputs.Where(kv => kv.Key.Creation == creation.Id).Select(kv => (kv.Key.Part, kv.Value)).ToList())
                sums.TryAdd(part, 0f);
            foreach (var (part, sum) in sums)
            {
                var key = (creation.Id, part);
                if (_inputs.GetValueOrDefault(key) == sum)
                    continue;
                if (sum == 0f)
                    _inputs.Remove(key);
                else
                    _inputs[key] = sum;
                if (creation.FindPart(part) is not null)
                    changed.Add((creation.Id, part, sum));
            }
        }
        foreach (var key in _inputs.Keys.Where(k => !present.Contains(k.Creation)).ToList())
            _inputs.Remove(key);
        return changed;
    }

    /// <summary>Forgets a part's output and input (it was removed, or its script stopped).</summary>
    public void Forget(int creation, int part)
    {
        _outputs.Remove((creation, part));
        _inputs.Remove((creation, part));
    }

    /// <summary>Forgets the input a part was last given, so the next <see cref="Step"/> reports it again (its script restarted).</summary>
    public void ResetInput(int creation, int part) => _inputs.Remove((creation, part));

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        _outputs.Clear();
        _inputs.Clear();
    }
}
