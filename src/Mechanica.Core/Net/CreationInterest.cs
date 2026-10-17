using System.Numerics;
using Mechanica.Core.World;

namespace Mechanica.Core.Net;

/// <summary>A Creation a peer could be told about, with its current world box.</summary>
/// <param name="Id">Creation id.</param>
/// <param name="Bounds">World box of its live Bodies.</param>
public readonly record struct InterestCandidate(int Id, WorldBounds Bounds);

/// <summary>What changed in one peer's interest set.</summary>
/// <param name="Entered">Creations the peer must now be sent in full, ascending.</param>
/// <param name="Left">Creations the peer must drop, ascending.</param>
public sealed record InterestChange(IReadOnlyList<int> Entered, IReadOnlyList<int> Left)
{
    /// <summary>Nothing changed.</summary>
    public static InterestChange None { get; } = new([], []);
}

/// <summary>
/// Host-side interest management (D10): which Creations each peer receives. A Creation enters a peer's
/// set when its box comes within <see cref="SyncSettings.InterestDistance"/> (x/z) of the peer's player
/// and leaves once it is more than <see cref="SyncSettings.InterestHysteresis"/> further, so one sitting on
/// the edge does not flicker. Only candidates count: the caller leaves out parked Creations (chunk 12) and
/// the ones the peer is itself the authority for (D5), so those leave the set too.
/// </summary>
public sealed class CreationInterest(SyncSettings settings)
{
    private readonly Dictionary<int, SortedSet<int>> _sets = [];

    /// <summary>The tuning in use.</summary>
    public SyncSettings Settings { get; } = settings;

    /// <summary>Creations a peer currently receives, ascending.</summary>
    public IReadOnlyCollection<int> Of(int peer) => _sets.TryGetValue(peer, out var set) ? set : [];

    /// <summary>True when the peer receives the Creation.</summary>
    public bool Contains(int peer, int creationId) => _sets.TryGetValue(peer, out var set) && set.Contains(creationId);

    /// <summary>
    /// Re-evaluates one peer. <paramref name="player"/> is its player's position, or null while it has
    /// none (then it receives nothing).
    /// </summary>
    public InterestChange Update(int peer, Vector3? player, IEnumerable<InterestCandidate> candidates)
    {
        if (!_sets.TryGetValue(peer, out var set))
            _sets[peer] = set = [];
        var keep = new SortedSet<int>();
        if (player is { } position)
        {
            foreach (var candidate in candidates)
            {
                var limit = Settings.InterestDistance + (set.Contains(candidate.Id) ? Settings.InterestHysteresis : 0f);
                if (candidate.Bounds.HorizontalDistanceTo(position) <= limit)
                    keep.Add(candidate.Id);
            }
        }
        var entered = keep.Where(id => !set.Contains(id)).ToList();
        var left = set.Where(id => !keep.Contains(id)).ToList();
        _sets[peer] = keep;
        return entered.Count + left.Count == 0 ? InterestChange.None : new InterestChange(entered, left);
    }

    /// <summary>Forgets a peer that left.</summary>
    public void Forget(int peer) => _sets.Remove(peer);

    /// <summary>Drops a Creation from every peer's set without reporting it (it was destroyed, and peers are told separately).</summary>
    public void ForgetCreation(int creationId)
    {
        foreach (var set in _sets.Values)
            set.Remove(creationId);
    }

    /// <summary>Forgets everything.</summary>
    public void Clear() => _sets.Clear();
}
