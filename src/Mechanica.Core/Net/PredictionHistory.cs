using System.Numerics;
using Mechanica.Core.Player;

namespace Mechanica.Core.Net;

/// <summary>
/// Client-side record of the local player's own inputs and where each one left the player. When a
/// host snapshot acknowledges input <c>n</c>, <see cref="Reconcile"/> compares the host's position with
/// the prediction after <c>n</c>; on a mismatch the game resets to the host state and replays the
/// inputs the host has not processed yet.
/// </summary>
public sealed class PredictionHistory
{
    /// <summary>A predicted tick.</summary>
    /// <param name="Sequence">Input sequence.</param>
    /// <param name="Input">The input simulated.</param>
    /// <param name="Position">Predicted position after the tick.</param>
    /// <param name="Velocity">Predicted velocity after the tick.</param>
    /// <param name="Ground">The Creation Body the player stood on after the tick, or null.</param>
    public readonly record struct Entry(uint Sequence, PlayerInput Input, Vector3 Position, Vector3 Velocity, PlayerGround? Ground = null);

    private readonly List<Entry> _entries = [];

    /// <summary>Creates a history.</summary>
    /// <param name="capacity">Ticks kept; older entries fall off.</param>
    /// <param name="tolerance">Position error in metres that is accepted without a correction.</param>
    public PredictionHistory(int capacity = 120, float tolerance = 0.05f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        Tolerance = tolerance;
    }

    /// <summary>Ticks kept.</summary>
    public int Capacity { get; }

    /// <summary>Position error accepted without a correction, in metres.</summary>
    public float Tolerance { get; }

    /// <summary>Unacknowledged entries, oldest first.</summary>
    public IReadOnlyList<Entry> Pending => _entries;

    /// <summary>Records a tick the local player just simulated.</summary>
    public void Record(Entry entry)
    {
        if (_entries.Count > 0 && entry.Sequence <= _entries[^1].Sequence)
            throw new ArgumentException("Sequences must increase.", nameof(entry));
        _entries.Add(entry);
        if (_entries.Count > Capacity)
            _entries.RemoveAt(0);
    }

    /// <summary>The newest <paramref name="count"/> entries, oldest first (what the next input packet carries).</summary>
    public IReadOnlyList<Entry> Newest(int count) => _entries[Math.Max(0, _entries.Count - count)..];

    /// <summary>
    /// Drops entries up to <paramref name="ackSequence"/> and says whether the prediction must be corrected.
    /// True when the acknowledged entry's position is more than <see cref="Tolerance"/> from
    /// <paramref name="hostPosition"/>, or when the entry is no longer known. After a correction the
    /// caller replays <see cref="Pending"/> and calls <see cref="Overwrite"/> for each.
    /// </summary>
    public bool Reconcile(uint ackSequence, Vector3 hostPosition)
    {
        var index = _entries.FindIndex(e => e.Sequence == ackSequence);
        var predicted = index >= 0 ? _entries[index].Position : (Vector3?)null;
        _entries.RemoveAll(e => e.Sequence <= ackSequence);
        return predicted is not { } p || Vector3.Distance(p, hostPosition) > Tolerance;
    }

    /// <summary>
    /// Checks a prediction on a Creation in that Body's frame (<see cref="PlayerGround"/>). Returns null, dropping
    /// nothing, when the acknowledged entry is unknown or was not on the same Body: the caller then uses
    /// <see cref="Reconcile"/>. Otherwise drops entries up to <paramref name="ackSequence"/> and says whether the
    /// feet in the Body's frame are more than <see cref="Tolerance"/> from the host's; <paramref name="acked"/> is
    /// the acknowledged entry. After a correction the caller moves the player by the difference and calls
    /// <see cref="Shift"/>, without replaying: replayed steps on a moving Body would each carry the player
    /// along with it again.
    /// </summary>
    public bool? ReconcileOnGround(uint ackSequence, PlayerGround hostGround, out Entry acked)
    {
        var index = _entries.FindIndex(e => e.Sequence == ackSequence);
        if (index < 0 || !hostGround.SameBody(_entries[index].Ground))
        {
            acked = default;
            return null;
        }
        acked = _entries[index];
        _entries.RemoveRange(0, index + 1);
        return Vector3.Distance(acked.Ground!.Value.Feet, hostGround.Feet) > Tolerance;
    }

    /// <summary>
    /// Moves every pending prediction by <paramref name="world"/> metres, and by <paramref name="local"/> in the
    /// frame of entries standing on <paramref name="ground"/>'s Body (after <see cref="ReconcileOnGround"/>).
    /// </summary>
    public void Shift(Vector3 world, Vector3 local, PlayerGround ground)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            _entries[i] = entry with
            {
                Position = entry.Position + world,
                Ground = entry.Ground is { } g && ground.SameBody(g) ? g with { Feet = g.Feet + local } : entry.Ground,
            };
        }
    }

    /// <summary>Replaces the predicted result of a replayed tick.</summary>
    public void Overwrite(uint sequence, Vector3 position, Vector3 velocity, PlayerGround? ground = null)
    {
        var index = _entries.FindIndex(e => e.Sequence == sequence);
        if (index < 0)
            throw new ArgumentException($"No entry for sequence {sequence}.", nameof(sequence));
        _entries[index] = _entries[index] with { Position = position, Velocity = velocity, Ground = ground };
    }

    /// <summary>Forgets everything (after a teleport or reconnect).</summary>
    public void Clear() => _entries.Clear();
}
