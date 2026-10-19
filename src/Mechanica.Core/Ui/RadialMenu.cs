using System.Numerics;

namespace Mechanica.Core.Ui;

/// <summary>
/// The geometry of a radial menu centred on the cursor (the Paint Tool's colour picker, UI spec §9): items sit round a
/// circle starting at the top and going clockwise, and the mouse picks the one in its direction once it is past the
/// dead zone in the middle.
/// </summary>
public static class RadialMenu
{
    /// <summary>
    /// Which of <paramref name="count"/> items the mouse at <paramref name="offset"/> from the centre points at (screen
    /// y grows down), or -1 inside <paramref name="deadZone"/> pixels of the centre.
    /// </summary>
    public static int Pick(Vector2 offset, int count, float deadZone)
    {
        if (count <= 0 || offset.Length() < deadZone)
            return -1;
        // Clockwise from straight up.
        var angle = MathF.Atan2(offset.X, -offset.Y);
        if (angle < 0)
            angle += MathF.Tau;
        var step = MathF.Tau / count;
        return (int)MathF.Floor((angle + step / 2) / step) % count;
    }

    /// <summary>
    /// Which cell of a menu of <paramref name="spokes"/> directions by <paramref name="rings"/> rings the mouse at
    /// <paramref name="offset"/> points at, as <c>spoke * rings + ring</c> (the Paint Tool's base colours by their shades):
    /// the spoke in its direction (as <see cref="Pick"/>), and the ring its distance falls in, rings
    /// <paramref name="ringWidth"/> pixels wide from <paramref name="inner"/> outwards (beyond the last ring is the last).
    /// -1 inside <paramref name="inner"/>.
    /// </summary>
    public static int PickCell(Vector2 offset, int spokes, int rings, float inner, float ringWidth)
    {
        var spoke = Pick(offset, spokes, inner);
        if (rings <= 0 || ringWidth <= 0f || spoke < 0)
            return -1;
        var ring = Math.Clamp((int)MathF.Floor((offset.Length() - inner) / ringWidth), 0, rings - 1);
        return spoke * rings + ring;
    }

    /// <summary>Where item <paramref name="index"/> of <paramref name="count"/> sits, as an offset from the centre at <paramref name="radius"/>.</summary>
    public static Vector2 Position(int index, int count, float radius)
    {
        var angle = MathF.Tau * index / Math.Max(1, count);
        return new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * radius;
    }
}
