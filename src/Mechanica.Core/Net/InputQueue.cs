using Mechanica.Core.Player;

namespace Mechanica.Core.Net;

/// <summary>
/// Host-side buffer of one client's inputs. The host simulates a remote player only with that
/// client's own inputs, in order, so its result matches the client's prediction exactly:
/// <list type="bullet">
/// <item>Normally one input per physics tick, so a client that sends faster cannot move faster; past
/// <see cref="MaxBuffered"/> the oldest inputs are dropped.</item>
/// <item>When nothing has arrived the player waits for its input instead of guessing (network jitter).</item>
/// <item>When a backlog builds up after such a wait, two inputs run in one tick until it is gone.</item>
/// <item>After <see cref="MaxStallTicks"/> ticks with nothing, the player stands still (input with no
/// movement) so a stalled client doesn't hang in mid-air.</item>
/// </list>
/// </summary>
public sealed class InputQueue
{
    /// <summary>Inputs kept waiting; beyond this the oldest are dropped.</summary>
    public const int MaxBuffered = 8;

    /// <summary>Ticks a player waits for input before it is simulated standing still.</summary>
    public const int MaxStallTicks = 10;

    /// <summary>Largest forward jump in sequence accepted (10 s of ticks); bigger jumps are bogus and ignored.</summary>
    public const uint MaxSequenceJump = 600;

    /// <summary>Inputs left waiting after a tick that trigger catch-up (two inputs in one tick).</summary>
    public const int CatchUpBacklog = 2;

    private readonly Queue<SequencedInput> _queue = new();
    private PlayerInput _last;
    private int _stalledTicks;

    /// <summary>Newest sequence accepted into the buffer (0 before any).</summary>
    public uint NewestReceived { get; private set; }

    /// <summary>Sequence of the last input handed to the simulation, sent back as the client's ack (0 before any).</summary>
    public uint LastApplied { get; private set; }

    /// <summary>Inputs dropped because the buffer was full (diagnostics).</summary>
    public int Dropped { get; private set; }

    /// <summary>Ticks that had no input to run (diagnostics).</summary>
    public int Stalled { get; private set; }

    /// <summary>Inputs waiting.</summary>
    public int Count => _queue.Count;

    /// <summary>Adds the inputs from one packet. Old, duplicate and implausibly far-ahead sequences are ignored.</summary>
    public void Receive(IEnumerable<SequencedInput> inputs)
    {
        foreach (var input in inputs.OrderBy(i => i.Sequence))
        {
            if (input.Sequence <= NewestReceived || input.Sequence - NewestReceived > MaxSequenceJump)
                continue;
            NewestReceived = input.Sequence;
            _queue.Enqueue(input);
        }
        while (_queue.Count > MaxBuffered)
        {
            _queue.Dequeue();
            Dropped++;
        }
    }

    /// <summary>
    /// The inputs to simulate this tick, in order: usually one, none while waiting for input, two
    /// while catching up, or a standing-still input after a long stall.
    /// </summary>
    public IReadOnlyList<PlayerInput> NextTick()
    {
        if (_queue.Count == 0)
        {
            Stalled++;
            return ++_stalledTicks > MaxStallTicks ? [PlayerInput.None with { Yaw = _last.Yaw, Pitch = _last.Pitch }] : [];
        }

        _stalledTicks = 0;
        var result = new List<PlayerInput>(2) { Take() };
        if (_queue.Count >= CatchUpBacklog)
            result.Add(Take());
        return result;
    }

    private PlayerInput Take()
    {
        var next = _queue.Dequeue();
        LastApplied = next.Sequence;
        _last = next.Input;
        return next.Input;
    }
}
