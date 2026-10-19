using Mechanica.Core.Data;

namespace Mechanica.Core.Characters;

/// <summary>
/// How a seat holds its sitter, from its part data (<see cref="SeatDefinition"/>): every peer works it out for itself,
/// so it needs no network traffic.
/// </summary>
/// <param name="Cushion">The cushion's height above the floor the feet rest on, in m; NaN when the seat names no cushion.</param>
/// <param name="HandsFree">The hands rest on the thighs instead of holding a wheel.</param>
/// <param name="Bounce">How much the sitter bounces and sways with the ride (0 to <see cref="SeatDefinition.MaxBounce"/>).</param>
public readonly record struct SeatStyle(float Cushion, bool HandsFree, float Bounce)
{
    /// <summary>A seat with no cushion or hands given: the plain sitting pose, hands on a wheel.</summary>
    public static SeatStyle Default { get; } = new(float.NaN, false, 1f);

    /// <summary>The style of the seat <paramref name="part"/>, or <see cref="Default"/> when it is no seat.</summary>
    public static SeatStyle For(PartDefinition part)
    {
        if (part.Seat is not { } seat)
            return Default;
        var cushion = seat.Cushion is { } c ? c.Y - FloorHeight(part, seat) : float.NaN;
        var hands = seat.Hands ?? (part.Logic?.Outputs > 0 ? SeatHands.Wheel : SeatHands.Free);
        return new SeatStyle(cushion, hands == SeatHands.Free, Math.Clamp(seat.Bounce ?? 1f, 0f, SeatDefinition.MaxBounce));
    }

    /// <summary>
    /// Where the sitter's feet are anchored, relative to the part centre in m in the part's frame: on the floor under
    /// the cushion when there is one (the legs reach forward from there), else the seat's offset.
    /// </summary>
    public static Vec3 Anchor(PartDefinition part)
    {
        if (part.Seat is not { } seat)
            return Vec3.Zero;
        return seat.Cushion is { } c ? new Vec3(c.X, FloorHeight(part, seat), c.Z) : seat.Offset ?? Vec3.Zero;
    }

    // The floor the feet rest on: the offset's height, or the part's bottom when a cushion is given without one.
    private static float FloorHeight(PartDefinition part, SeatDefinition seat) =>
        seat.Offset?.Y ?? -(part.Size ?? GridSize.One).Metres.Y / 2f;
}
