namespace Mechanica.Core.Logic;

/// <summary>One value a part's script shows, as the host sends it to clients.</summary>
/// <param name="Creation">The part's Creation.</param>
/// <param name="Part">The part.</param>
/// <param name="Id">The readout's id (snake_case), as its script and its <c>menu</c> or <c>display</c> data name it.</param>
/// <param name="Number">The value when it is a number (true is 1, false 0).</param>
/// <param name="Text">The value when it is text.</param>
public sealed record PartReadout(int Creation, int Part, string Id, float? Number = null, string? Text = null);

/// <summary>
/// What each part's script shows (UI spec §12): a fuel level, a speed, whether it runs. Scripts set values on the
/// host with <c>part.display(id, value)</c>; the host sends them to every client, and every peer shows them in the
/// part's menu (<see cref="Data.MenuDefinition.Readouts"/>) and on the part itself (<see cref="Data.DisplayDefinition"/>).
/// A part with no value for an id shows nothing for it. <see cref="Revision"/> changes whenever a value does.
/// </summary>
public sealed class PartReadouts
{
    /// <summary>Most values one part may show.</summary>
    public const int MaxPerPart = 16;

    /// <summary>Longest a text value may be.</summary>
    public const int MaxText = 64;

    private readonly SortedDictionary<(int Creation, int Part), SortedDictionary<string, (float? Number, string? Text)>> _values = [];

    /// <summary>Goes up by one on every change.</summary>
    public int Revision { get; private set; }

    /// <summary>A part's number <paramref name="id"/>, or null when it shows none (or shows text).</summary>
    public float? Number(int creation, int part, string id) =>
        _values.TryGetValue((creation, part), out var values) && values.TryGetValue(id, out var value) ? value.Number : null;

    /// <summary>A part's text <paramref name="id"/>, or null when it shows none (or shows a number).</summary>
    public string? Text(int creation, int part, string id) =>
        _values.TryGetValue((creation, part), out var values) && values.TryGetValue(id, out var value) ? value.Text : null;

    /// <summary>True when the part shows something for <paramref name="id"/>.</summary>
    public bool Has(int creation, int part, string id) =>
        _values.TryGetValue((creation, part), out var values) && values.ContainsKey(id);

    /// <summary>
    /// Sets a number (non-finite counts as 0). Null error when it was set; otherwise why not (too many values, a bad id).
    /// </summary>
    public string? Set(int creation, int part, string id, float number) =>
        Put(creation, part, id, (float.IsFinite(number) ? number : 0f, null));

    /// <summary>Sets a text value, cut to <see cref="MaxText"/> characters. Null when it was set, or why not.</summary>
    public string? Set(int creation, int part, string id, string text) =>
        Put(creation, part, id, (null, text.Length > MaxText ? text[..MaxText] : text));

    /// <summary>Takes one value away.</summary>
    public void Clear(int creation, int part, string id)
    {
        if (_values.TryGetValue((creation, part), out var values) && values.Remove(id))
        {
            if (values.Count == 0)
                _values.Remove((creation, part));
            Revision++;
        }
    }

    /// <summary>Drops every value of a part (the part went away).</summary>
    public void Forget(int creation, int part)
    {
        if (_values.Remove((creation, part)))
            Revision++;
    }

    /// <summary>Drops the values of every part of <paramref name="creation"/> that <paramref name="keep"/> says is gone.</summary>
    public void Prune(int creation, Func<int, bool> keep)
    {
        foreach (var key in _values.Keys.Where(k => k.Creation == creation && !keep(k.Part)).ToList())
        {
            _values.Remove(key);
            Revision++;
        }
    }

    /// <summary>Drops every value.</summary>
    public void Clear()
    {
        if (_values.Count == 0)
            return;
        _values.Clear();
        Revision++;
    }

    /// <summary>Every value, in creation, part and id order.</summary>
    public IReadOnlyList<PartReadout> All =>
        [.. _values.SelectMany(p => p.Value.Select(v => new PartReadout(p.Key.Creation, p.Key.Part, v.Key, v.Value.Number, v.Value.Text)))];

    /// <summary>Replaces every value with the host's (a client's copy). Bad entries are skipped.</summary>
    public void Load(IEnumerable<PartReadout> readouts)
    {
        _values.Clear();
        foreach (var readout in readouts)
        {
            if (readout.Number is { } n)
                Put(readout.Creation, readout.Part, readout.Id, (float.IsFinite(n) ? n : 0f, null), count: false);
            else if (readout.Text is { } text)
                Put(readout.Creation, readout.Part, readout.Id, (null, text.Length > MaxText ? text[..MaxText] : text), count: false);
        }
        Revision++;
    }

    private string? Put(int creation, int part, string id, (float? Number, string? Text) value, bool count = true)
    {
        if (!Data.Identifiers.IsValid(id))
            return $"readout id '{id}' must be lowercase snake_case";
        if (!_values.TryGetValue((creation, part), out var values))
            _values[(creation, part)] = values = new SortedDictionary<string, (float?, string?)>(StringComparer.Ordinal);
        if (values.TryGetValue(id, out var old))
        {
            if (old == value)
                return null;
        }
        else if (values.Count >= MaxPerPart)
        {
            return $"a part may show at most {MaxPerPart} values";
        }
        values[id] = value;
        if (count)
            Revision++;
        return null;
    }
}
