namespace Mechanica.Core.Logic;

/// <summary>One part of a Creation, in <see cref="ReadoutChanges"/>.</summary>
/// <param name="Creation">The part's Creation.</param>
/// <param name="Part">The part.</param>
public sealed record ReadoutPart(int Creation, int Part);

/// <summary>
/// What a client's copy of <see cref="PartReadouts"/> needs to catch up with the host (<see cref="PartReadouts.Apply"/>).
/// </summary>
/// <param name="Creations">Creations whose values all go (they left the client's view, or went away).</param>
/// <param name="Parts">Parts whose values are all replaced by the ones in <paramref name="Values"/> (none: it shows nothing now).</param>
/// <param name="Values">The new values of those parts, and every value of a Creation that came into view.</param>
public sealed record ReadoutChanges(IReadOnlyList<int> Creations, IReadOnlyList<ReadoutPart> Parts, IReadOnlyList<PartReadout> Values);

/// <summary>
/// What the host has sent one client of its <see cref="PartReadouts"/>, so each send carries only what changed since
/// the last, and only for the Creations the client sees (its network interest). A Creation coming into view is sent
/// whole; one leaving it is dropped from the client's copy. Unchanged Creations cost one lookup each.
/// </summary>
public sealed class ReadoutFeed
{
    private readonly Dictionary<int, Sent> _sent = [];
    private readonly List<int> _scratch = [];

    private sealed class Sent
    {
        public int Stamp;
        public readonly Dictionary<int, int> Parts = [];
    }

    /// <summary>
    /// What changed in <paramref name="readouts"/> since the last call, for a client that sees
    /// <paramref name="creations"/>; null when nothing did. The feed then counts it as sent.
    /// </summary>
    public ReadoutChanges? Next(PartReadouts readouts, IReadOnlyCollection<int> creations)
    {
        List<int>? cleared = null;
        List<ReadoutPart>? parts = null;
        List<PartReadout>? values = null;

        _scratch.Clear();
        foreach (var creation in _sent.Keys)
        {
            if (!creations.Contains(creation))
                _scratch.Add(creation);
        }
        foreach (var creation in _scratch)
        {
            _sent.Remove(creation);
            (cleared ??= []).Add(creation);
        }

        foreach (var creation in creations)
        {
            var stamp = readouts.StampOf(creation);
            if (!_sent.TryGetValue(creation, out var sent))
            {
                if (stamp == 0)
                    continue;
                _sent[creation] = sent = new Sent();
            }
            else if (sent.Stamp == stamp)
            {
                continue;
            }
            foreach (var (part, partStamp) in readouts.PartsOf(creation))
            {
                if (sent.Parts.TryGetValue(part, out var sentStamp) && sentStamp == partStamp)
                    continue;
                (parts ??= []).Add(new ReadoutPart(creation, part));
                readouts.AddValuesOf(creation, part, values ??= []);
                sent.Parts[part] = partStamp;
            }
            _scratch.Clear();
            foreach (var part in sent.Parts.Keys)
            {
                if (!readouts.Shows(creation, part))
                    _scratch.Add(part);
            }
            foreach (var part in _scratch)
            {
                sent.Parts.Remove(part);
                (parts ??= []).Add(new ReadoutPart(creation, part));
            }
            sent.Stamp = stamp;
            if (sent.Parts.Count == 0)
                _sent.Remove(creation);
        }
        return cleared is null && parts is null ? null : new ReadoutChanges(cleared ?? [], parts ?? [], values ?? []);
    }
}
