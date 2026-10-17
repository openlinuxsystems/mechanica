using Mechanica.Core.Items;
using Mechanica.Core.Logic;

namespace Mechanica.Core.Net;

/// <summary>What an "in use" mark sits on.</summary>
public enum InUseKind
{
    /// <summary>A station or container another player has open.</summary>
    Open,

    /// <summary>A seat another player sits in.</summary>
    Seat,

    /// <summary>Another player's Lift.</summary>
    Lift,
}

/// <summary>
/// One "in use" mark (UI spec §11 Multiplayer UI indicators): an object other players are using right now.
/// </summary>
/// <param name="Kind">What the mark sits on.</param>
/// <param name="Creation">The part's Creation; 0 for a Lift.</param>
/// <param name="Part">The station, container or seat part; for a Lift, its owner's peer.</param>
/// <param name="Peers">The players using it, lowest peer first.</param>
public sealed record InUseMark(InUseKind Kind, int Creation, int Part, IReadOnlyList<int> Peers);

/// <summary>A player who has a station or container open, as the host tells every peer.</summary>
/// <param name="Peer">The player.</param>
/// <param name="Creation">The station's or container's Creation.</param>
/// <param name="Part">The station or container part.</param>
public readonly record struct OpenInventory(int Peer, int Creation, int Part);

/// <summary>
/// Works out the "in use" marks one peer draws from what everyone has open, who sits where and whose Lift
/// is out. The peer's own use never marks anything: the mark tells a player that <i>someone else</i> is there.
/// </summary>
public static class InUseMarks
{
    /// <summary>
    /// The open stations and containers in <paramref name="viewers"/> (peer → inventories it sees), player
    /// inventories left out, in a stable order so a host can tell when the list changed.
    /// </summary>
    public static IReadOnlyList<OpenInventory> Open(IEnumerable<KeyValuePair<int, IEnumerable<InventoryRef>>> viewers) =>
        [.. viewers
            .SelectMany(v => v.Value.Where(w => !w.IsPlayer && w.Creation is not null && w.Part is not null)
                .Select(w => new OpenInventory(v.Key, w.Creation!.Value, w.Part!.Value)))
            .Distinct()
            .OrderBy(o => o.Creation).ThenBy(o => o.Part).ThenBy(o => o.Peer)];

    /// <summary>The marks <paramref name="localPeer"/> draws, one per used object, in a stable order.</summary>
    public static IReadOnlyList<InUseMark> For(int localPeer, IEnumerable<OpenInventory> open,
        IEnumerable<SeatAssignment> seats, IEnumerable<int> liftOwners)
    {
        var marks = new List<InUseMark>();
        marks.AddRange(open
            .Where(o => o.Peer != localPeer)
            .GroupBy(o => (o.Creation, o.Part))
            .OrderBy(g => g.Key.Creation).ThenBy(g => g.Key.Part)
            .Select(g => new InUseMark(InUseKind.Open, g.Key.Creation, g.Key.Part, [.. g.Select(o => o.Peer).Distinct().Order()])));
        marks.AddRange(seats
            .Where(s => s.Peer != localPeer)
            .OrderBy(s => s.Creation).ThenBy(s => s.Part)
            .Select(s => new InUseMark(InUseKind.Seat, s.Creation, s.Part, [s.Peer])));
        marks.AddRange(liftOwners
            .Where(p => p != localPeer)
            .Distinct()
            .Order()
            .Select(p => new InUseMark(InUseKind.Lift, 0, p, [p])));
        return marks;
    }
}
