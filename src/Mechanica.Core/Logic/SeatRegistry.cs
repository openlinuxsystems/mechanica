namespace Mechanica.Core.Logic;

/// <summary>One player in one seat.</summary>
/// <param name="Peer">The player.</param>
/// <param name="Creation">The seat's Creation.</param>
/// <param name="Part">The seat part.</param>
public readonly record struct SeatAssignment(int Peer, int Creation, int Part);

/// <summary>
/// Who sits where (chunk 10). The host owns it and changes it only through <c>EnterSeat</c> and <c>LeaveSeat</c>
/// (<see cref="LogicActions"/>); clients get a copy. A player sits in at most one seat and a seat holds at most
/// one player.
/// </summary>
public sealed class SeatRegistry
{
    private readonly SortedDictionary<int, SeatAssignment> _byPeer = [];
    private readonly Dictionary<(int Creation, int Part), int> _bySeat = [];

    /// <summary>Goes up on every change, so a host can tell when to send the seats again.</summary>
    public int Revision { get; private set; }

    /// <summary>Every seated player, by peer.</summary>
    public IReadOnlyList<SeatAssignment> All => [.. _byPeer.Values];

    /// <summary>The player in a seat, or null when it is free.</summary>
    public int? Occupant(int creation, int part) => _bySeat.TryGetValue((creation, part), out var peer) ? peer : null;

    /// <summary>Where a player sits, or null.</summary>
    public SeatAssignment? SeatOf(int peer) => _byPeer.TryGetValue(peer, out var seat) ? seat : null;

    /// <summary>Seats a player, moving them out of any other seat. False when someone else sits there.</summary>
    public bool Sit(int peer, int creation, int part)
    {
        if (Occupant(creation, part) is { } other)
            return other == peer;
        Leave(peer);
        var seat = new SeatAssignment(peer, creation, part);
        _byPeer[peer] = seat;
        _bySeat[(creation, part)] = peer;
        Revision++;
        return true;
    }

    /// <summary>Stands a player up. Returns the seat they left, or null when they sat nowhere.</summary>
    public SeatAssignment? Leave(int peer)
    {
        if (!_byPeer.Remove(peer, out var seat))
            return null;
        _bySeat.Remove((seat.Creation, seat.Part));
        Revision++;
        return seat;
    }

    /// <summary>Replaces everything with <paramref name="seats"/> (a client taking the host's copy).</summary>
    public void Load(IEnumerable<SeatAssignment> seats)
    {
        _byPeer.Clear();
        _bySeat.Clear();
        foreach (var seat in seats)
        {
            if (_byPeer.ContainsKey(seat.Peer) || _bySeat.ContainsKey((seat.Creation, seat.Part)))
                continue;
            _byPeer[seat.Peer] = seat;
            _bySeat[(seat.Creation, seat.Part)] = seat.Peer;
        }
        Revision++;
    }
}
