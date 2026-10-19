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
/// A part with no value for an id shows nothing for it. <see cref="Revision"/> changes whenever a value does, and
/// each Creation and part carries the revision it last changed at (<see cref="StampOf"/>), so the host sends each
/// client only what changed near it (<see cref="ReadoutFeed"/>).
/// </summary>
public sealed class PartReadouts
{
    /// <summary>Most values one part may show.</summary>
    public const int MaxPerPart = 16;

    /// <summary>Longest a text value may be.</summary>
    public const int MaxText = 64;

    private readonly Dictionary<int, CreationValues> _creations = [];

    private sealed class CreationValues
    {
        public int Stamp;
        public readonly SortedDictionary<int, PartValues> Parts = [];
    }

    private sealed class PartValues
    {
        public int Stamp;
        public readonly SortedDictionary<string, (float? Number, string? Text)> Values = new(StringComparer.Ordinal);
    }

    /// <summary>Goes up by one on every change.</summary>
    public int Revision { get; private set; }

    /// <summary>A part's number <paramref name="id"/>, or null when it shows none (or shows text).</summary>
    public float? Number(int creation, int part, string id) =>
        Find(creation, part) is { } values && values.Values.TryGetValue(id, out var value) ? value.Number : null;

    /// <summary>A part's text <paramref name="id"/>, or null when it shows none (or shows a number).</summary>
    public string? Text(int creation, int part, string id) =>
        Find(creation, part) is { } values && values.Values.TryGetValue(id, out var value) ? value.Text : null;

    /// <summary>True when the part shows something for <paramref name="id"/>.</summary>
    public bool Has(int creation, int part, string id) =>
        Find(creation, part) is { } values && values.Values.ContainsKey(id);

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
        if (!_creations.TryGetValue(creation, out var values) || !values.Parts.TryGetValue(part, out var partValues)
            || !partValues.Values.Remove(id))
            return;
        Revision++;
        values.Stamp = partValues.Stamp = Revision;
        if (partValues.Values.Count == 0)
            RemovePart(creation, values, part);
    }

    /// <summary>Drops every value of a part (the part went away).</summary>
    public void Forget(int creation, int part)
    {
        if (!_creations.TryGetValue(creation, out var values) || !values.Parts.ContainsKey(part))
            return;
        Revision++;
        values.Stamp = Revision;
        RemovePart(creation, values, part);
    }

    /// <summary>Drops the values of every part of <paramref name="creation"/> that <paramref name="keep"/> says is gone.</summary>
    public void Prune(int creation, Func<int, bool> keep)
    {
        if (!_creations.TryGetValue(creation, out var values))
            return;
        foreach (var part in values.Parts.Keys.Where(p => !keep(p)).ToList())
        {
            Revision++;
            values.Stamp = Revision;
            RemovePart(creation, values, part);
        }
    }

    /// <summary>Drops every value.</summary>
    public void Clear()
    {
        if (_creations.Count == 0)
            return;
        _creations.Clear();
        Revision++;
    }

    /// <summary>Every value, in creation, part and id order.</summary>
    public IReadOnlyList<PartReadout> All
    {
        get
        {
            var all = new List<PartReadout>();
            foreach (var creation in _creations.Keys.Order())
            {
                foreach (var part in _creations[creation].Parts.Keys)
                    AddValuesOf(creation, part, all);
            }
            return all;
        }
    }

    /// <summary>The revision a Creation's values last changed at; 0 when it shows nothing.</summary>
    public int StampOf(int creation) => _creations.TryGetValue(creation, out var values) ? values.Stamp : 0;

    /// <summary>The parts of a Creation that show something, in id order, with the revision each last changed at.</summary>
    public IEnumerable<(int Part, int Stamp)> PartsOf(int creation)
    {
        if (!_creations.TryGetValue(creation, out var values))
            yield break;
        foreach (var (part, partValues) in values.Parts)
            yield return (part, partValues.Stamp);
    }

    /// <summary>True when the part shows anything.</summary>
    public bool Shows(int creation, int part) => Find(creation, part) is not null;

    /// <summary>Adds every value of one part, in id order, to <paramref name="into"/>.</summary>
    public void AddValuesOf(int creation, int part, List<PartReadout> into)
    {
        if (Find(creation, part) is not { } values)
            return;
        foreach (var (id, value) in values.Values)
            into.Add(new PartReadout(creation, part, id, value.Number, value.Text));
    }

    /// <summary>Replaces every value with the host's (a client's copy). Bad entries are skipped.</summary>
    public void Load(IEnumerable<PartReadout> readouts)
    {
        _creations.Clear();
        PutAll(readouts);
        Revision++;
    }

    /// <summary>
    /// Brings a client's copy up to date with what the host sent (<see cref="ReadoutFeed"/>): the listed Creations
    /// and parts lose every value they had, then take the values sent. Bad entries are skipped.
    /// </summary>
    public void Apply(ReadoutChanges changes)
    {
        foreach (var creation in changes.Creations)
            _creations.Remove(creation);
        foreach (var part in changes.Parts)
        {
            if (_creations.TryGetValue(part.Creation, out var values))
                RemovePart(part.Creation, values, part.Part);
        }
        PutAll(changes.Values);
        Revision++;
    }

    private PartValues? Find(int creation, int part) =>
        _creations.TryGetValue(creation, out var values) && values.Parts.TryGetValue(part, out var partValues) ? partValues : null;

    private void RemovePart(int creation, CreationValues values, int part)
    {
        values.Parts.Remove(part);
        if (values.Parts.Count == 0)
            _creations.Remove(creation);
    }

    private void PutAll(IEnumerable<PartReadout> readouts)
    {
        foreach (var readout in readouts)
        {
            if (readout.Number is { } n)
                Put(readout.Creation, readout.Part, readout.Id, (float.IsFinite(n) ? n : 0f, null), count: false);
            else if (readout.Text is { } text)
                Put(readout.Creation, readout.Part, readout.Id, (null, text.Length > MaxText ? text[..MaxText] : text), count: false);
        }
    }

    private string? Put(int creation, int part, string id, (float? Number, string? Text) value, bool count = true)
    {
        if (!Data.Identifiers.IsValid(id))
            return $"readout id '{id}' must be lowercase snake_case";
        var partValues = Find(creation, part);
        if (partValues is not null && partValues.Values.TryGetValue(id, out var old))
        {
            if (old == value)
                return null;
        }
        else if (partValues is not null && partValues.Values.Count >= MaxPerPart)
        {
            return $"a part may show at most {MaxPerPart} values";
        }
        if (partValues is null)
        {
            if (!_creations.TryGetValue(creation, out var values))
                _creations[creation] = values = new CreationValues();
            values.Parts[part] = partValues = new PartValues();
        }
        partValues.Values[id] = value;
        if (count)
        {
            Revision++;
            partValues.Stamp = _creations[creation].Stamp = Revision;
        }
        return null;
    }
}
