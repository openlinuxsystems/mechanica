using System.Numerics;

namespace Mechanica.Core.Ui;

/// <summary>A place the compass points at (UI spec §13).</summary>
/// <param name="Id">Stable id, so the player's pick of primary survives a refresh.</param>
/// <param name="Name">What it is, for the tooltip.</param>
/// <param name="Position">Where it is in the world.</param>
public sealed record Waypoint(string Id, string Name, Vector3 Position);

/// <summary>Where a waypoint sits on the compass bar.</summary>
/// <param name="X">Across the bar, -1 (left edge) to 1 (right edge).</param>
/// <param name="Behind">True when it is outside the bar's span, pinned to the edge it is nearer turning to.</param>
/// <param name="Distance">Metres away (x/z).</param>
public readonly record struct CompassMark(float X, bool Behind, float Distance);

/// <summary>
/// The compass bar's geometry (UI spec §13): the player's heading is the centre, the bar spans
/// <see cref="HalfSpan"/> degrees each way, north is -z and east +x.
/// </summary>
public static class Compass
{
    /// <summary>Degrees either side of the heading the bar shows.</summary>
    public const float HalfSpan = 90f;

    /// <summary>Waypoints closer than this show their distance.</summary>
    public const float DistanceShownWithin = 500f;

    /// <summary>The compass points and their names every 45°.</summary>
    public static readonly IReadOnlyList<string> Points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    /// <summary>The heading, 0 to 360° clockwise from north, of a player turned <paramref name="yaw"/> radians (Godot's Y rotation; 0 faces -z).</summary>
    public static float Heading(float yaw) => Wrap(-yaw * 180f / MathF.PI);

    /// <summary>The bearing, 0 to 360° clockwise from north, from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static float Bearing(Vector3 from, Vector3 to) => Wrap(MathF.Atan2(to.X - from.X, -(to.Z - from.Z)) * 180f / MathF.PI);

    /// <summary>Where <paramref name="target"/> sits on the bar for a player at <paramref name="from"/> facing <paramref name="heading"/>.</summary>
    public static CompassMark Mark(Vector3 from, float heading, Vector3 target)
    {
        var offset = Offset(heading, Bearing(from, target));
        var distance = new Vector2(target.X - from.X, target.Z - from.Z).Length();
        var behind = MathF.Abs(offset) > HalfSpan;
        return new CompassMark(behind ? MathF.Sign(offset) : offset / HalfSpan, behind, distance);
    }

    /// <summary>Where a bearing sits on the bar, -1 to 1, or null when it is off the bar.</summary>
    public static float? X(float heading, float bearing)
    {
        var offset = Offset(heading, bearing);
        return MathF.Abs(offset) > HalfSpan ? null : offset / HalfSpan;
    }

    /// <summary>The signed turn from <paramref name="heading"/> to <paramref name="bearing"/>, -180 to 180° (positive is right).</summary>
    public static float Offset(float heading, float bearing)
    {
        var d = Wrap(bearing - heading);
        return d > 180f ? d - 360f : d;
    }

    /// <summary>The distance label under a waypoint, or null past <see cref="DistanceShownWithin"/>.</summary>
    public static string? DistanceLabel(float metres) => metres <= DistanceShownWithin ? $"{metres:0} m" : null;

    private static float Wrap(float degrees) => (degrees % 360f + 360f) % 360f;
}
