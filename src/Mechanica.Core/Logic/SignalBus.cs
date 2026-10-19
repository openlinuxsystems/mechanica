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
    // How many non-zero outputs each Creation has.
    private readonly Dictionary<int, int> _outputCounts = [];
    private readonly Dictionary<(int Creation, int Part), float> _inputs = [];
    // The parts of each Creation with a non-zero input, so a step only visits Creations with logic.
    private readonly Dictionary<int, HashSet<int>> _given = [];
    // Step's scratch, kept between steps.
    private readonly Dictionary<int, float> _sums = [];
    private readonly List<int> _parts = [];
    private readonly HashSet<int> _present = [];

    /// <summary>A part's output now (0 when it has set none).</summary>
    public float Output(int creation, int part) => _outputs.GetValueOrDefault((creation, part));

    /// <summary>Sets a part's output. Non-finite values count as 0.</summary>
    public void SetOutput(int creation, int part, float value)
    {
        if (!float.IsFinite(value) || value == 0f)
            RemoveOutput((creation, part));
        else if (_outputs.TryAdd((creation, part), value))
            _outputCounts[creation] = _outputCounts.GetValueOrDefault(creation) + 1;
        else
            _outputs[(creation, part)] = value;
    }

    /// <summary>
    /// True while any part of a Creation puts out or is given a non-zero signal: its logic is doing something (a
    /// switch left on, a sensor seeing, an engine powered), so the world keeps it running even when it stands still.
    /// </summary>
    public bool IsActive(int creation) => _outputCounts.ContainsKey(creation) || _given.ContainsKey(creation);

    private void RemoveOutput((int Creation, int Part) key)
    {
        if (!_outputs.Remove(key))
            return;
        if (_outputCounts[key.Creation] == 1)
            _outputCounts.Remove(key.Creation);
        else
            _outputCounts[key.Creation]--;
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
        _present.Clear();
        foreach (var creation in creations.OrderBy(c => c.Id))
        {
            _present.Add(creation.Id);
            var given = _given.GetValueOrDefault(creation.Id);
            // Most Creations have no logic at all: nothing to sum and nothing to send back to 0.
            if (creation.Links.Count == 0 && given is null)
                continue;
            _sums.Clear();
            foreach (var link in creation.Links)
                _sums[link.To] = _sums.GetValueOrDefault(link.To) + Output(creation.Id, link.From);
            if (given is not null)
            {
                foreach (var part in given)
                    _sums.TryAdd(part, 0f);
            }
            _parts.Clear();
            _parts.AddRange(_sums.Keys);
            _parts.Sort();
            foreach (var part in _parts)
            {
                var sum = _sums[part];
                var key = (creation.Id, part);
                if (_inputs.GetValueOrDefault(key) == sum)
                    continue;
                if (sum == 0f)
                    RemoveInput(key);
                else
                    SetInput(key, sum);
                if (creation.FindPart(part) is not null)
                    changed.Add((creation.Id, part, sum));
            }
        }
        foreach (var creation in _given.Keys.Where(c => !_present.Contains(c)).ToList())
        {
            foreach (var part in _given[creation])
                _inputs.Remove((creation, part));
            _given.Remove(creation);
        }
        return changed;
    }

    private void SetInput((int Creation, int Part) key, float value)
    {
        _inputs[key] = value;
        if (!_given.TryGetValue(key.Creation, out var parts))
            _given[key.Creation] = parts = [];
        parts.Add(key.Part);
    }

    private void RemoveInput((int Creation, int Part) key)
    {
        if (!_inputs.Remove(key) || !_given.TryGetValue(key.Creation, out var parts))
            return;
        parts.Remove(key.Part);
        if (parts.Count == 0)
            _given.Remove(key.Creation);
    }

    /// <summary>Forgets a part's output and input (it was removed, or its script stopped).</summary>
    public void Forget(int creation, int part)
    {
        RemoveOutput((creation, part));
        RemoveInput((creation, part));
    }

    /// <summary>Forgets the input a part was last given, so the next <see cref="Step"/> reports it again (its script restarted).</summary>
    public void ResetInput(int creation, int part) => RemoveInput((creation, part));

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        _outputs.Clear();
        _outputCounts.Clear();
        _inputs.Clear();
        _given.Clear();
    }
}
