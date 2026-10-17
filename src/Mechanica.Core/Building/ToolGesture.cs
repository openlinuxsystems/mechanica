using Mechanica.Core.Creations;

namespace Mechanica.Core.Building;

/// <summary>Where a held mouse button is in its gesture (UI spec §8, §9).</summary>
public enum GesturePhase
{
    /// <summary>Not pressed.</summary>
    Idle,

    /// <summary>Just pressed: a release now is a click.</summary>
    Pressed,

    /// <summary>Held past the hold time without moving to another cell: the Hammer's alignment mode.</summary>
    Holding,

    /// <summary>Moved to other cells for long enough while held: a drag over an area.</summary>
    Dragging,
}

/// <summary>How a gesture ended.</summary>
public enum GestureEnd
{
    /// <summary>Released before the hold time and without dragging.</summary>
    Click,

    /// <summary>Released after holding still.</summary>
    Hold,

    /// <summary>Released after a drag.</summary>
    Drag,
}

/// <summary>
/// The press, hold, drag and release of one mouse button over grid cells, timed by the caller's clock (UI spec §9):
/// held still for <see cref="HoldSeconds"/> it is holding, moved off its first cell for
/// <see cref="DragSeconds"/> it is dragging, and a release says which it was. The Hammer uses it for alignment and
/// drag placement with the left button and for timed and drag removal with the right.
/// </summary>
public sealed class ToolGesture(double holdSeconds, double dragSeconds)
{
    private double _pressedAt;
    private double? _movedAt;

    /// <summary>Seconds held still before <see cref="GesturePhase.Holding"/>.</summary>
    public double HoldSeconds { get; } = holdSeconds;

    /// <summary>Seconds of being off the first cell before <see cref="GesturePhase.Dragging"/>.</summary>
    public double DragSeconds { get; } = dragSeconds;

    /// <summary>Where the gesture is now.</summary>
    public GesturePhase Phase { get; private set; }

    /// <summary>The cell under the aim when pressed.</summary>
    public GridCell Start { get; private set; }

    /// <summary>The cell under the aim now.</summary>
    public GridCell Current { get; private set; }

    /// <summary>Seconds since the press, as of the last call.</summary>
    public double Held { get; private set; }

    /// <summary>True from the press until the release or cancel.</summary>
    public bool Down => Phase != GesturePhase.Idle;

    /// <summary>Starts a gesture at <paramref name="now"/> over <paramref name="cell"/>.</summary>
    public void Press(double now, GridCell cell)
    {
        Phase = GesturePhase.Pressed;
        Start = Current = cell;
        _pressedAt = now;
        _movedAt = null;
        Held = 0;
    }

    /// <summary>Moves the gesture on to <paramref name="now"/> with the aim over <paramref name="cell"/> (null keeps the last cell). Returns the phase.</summary>
    public GesturePhase Update(double now, GridCell? cell)
    {
        if (Phase == GesturePhase.Idle)
            return Phase;
        Held = now - _pressedAt;
        if (cell is { } at)
            Current = at;
        if (Phase == GesturePhase.Dragging)
            return Phase;
        if (Current != Start)
            _movedAt ??= now;
        else
            _movedAt = null;
        if (_movedAt is { } moved && now - moved >= DragSeconds)
            Phase = GesturePhase.Dragging;
        else if (Phase == GesturePhase.Pressed && _movedAt is null && Held >= HoldSeconds)
            Phase = GesturePhase.Holding;
        return Phase;
    }

    /// <summary>Ends the gesture at <paramref name="now"/>; null when nothing was pressed.</summary>
    public GestureEnd? Release(double now)
    {
        if (Phase == GesturePhase.Idle)
            return null;
        Update(now, null);
        var end = Phase switch
        {
            GesturePhase.Dragging => GestureEnd.Drag,
            GesturePhase.Holding => GestureEnd.Hold,
            _ => GestureEnd.Click,
        };
        Phase = GesturePhase.Idle;
        return end;
    }

    /// <summary>Drops the gesture without ending it.</summary>
    public void Cancel() => Phase = GesturePhase.Idle;
}
