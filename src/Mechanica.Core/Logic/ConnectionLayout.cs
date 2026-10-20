using System.Numerics;
using Mechanica.Core.Creations;

namespace Mechanica.Core.Logic;

/// <summary>
/// Screen and aim geometry for the Connect Tool's lines and nodes (UI spec §10, §12): chevrons every so many pixels
/// along a line, lanes that keep two lines between the same parts apart, how near the cursor is to a line, and
/// which node a drag snaps to.
/// </summary>
public static class ConnectionLayout
{
    /// <summary>
    /// Where the chevrons go along the screen line <paramref name="from"/> to <paramref name="to"/>: one every
    /// <paramref name="spacing"/> px, centred so both ends have the same margin, and always at least one.
    /// </summary>
    public static IReadOnlyList<Vector2> Chevrons(Vector2 from, Vector2 to, float spacing)
    {
        var length = Vector2.Distance(from, to);
        if (length < 1e-3f || spacing <= 0f)
            return [];
        var count = Math.Max(1, (int)(length / spacing));
        var margin = (length - (count - 1) * spacing) / 2f;
        if (margin < 0f)
            margin = length / 2f;
        var direction = (to - from) / length;
        return [.. Enumerable.Range(0, count).Select(i => from + direction * (margin + i * spacing))];
    }

    /// <summary>
    /// The chevrons of <see cref="Chevrons"/> moved <paramref name="travelled"/> px along the line toward
    /// <paramref name="to"/>, for a hovered line whose arrows run the way it points. Each one wraps back to the start
    /// a spacing after it passes the last, and fades in and out over the quarter spacing at either end of its run, so
    /// the flow never jumps. At 0 they are where <see cref="Chevrons"/> puts them.
    /// </summary>
    public static IReadOnlyList<(Vector2 At, float Alpha)> MovingChevrons(Vector2 from, Vector2 to, float spacing, float travelled)
    {
        var still = Chevrons(from, to, spacing);
        if (still.Count == 0)
            return [];
        var length = Vector2.Distance(from, to);
        var direction = (to - from) / length;
        var run = still.Count * spacing;
        // The run is centred on the still chevrons and so never leaves the line.
        var start = Math.Max(0f, Vector2.Distance(from, still[0]) - spacing / 2f);
        var shift = (travelled % run + run) % run;
        var edge = spacing / 4f;
        return [.. still.Select((_, i) =>
        {
            var along = (i * spacing + spacing / 2f + shift) % run;
            var alpha = Math.Clamp(Math.Min(along, run - along) / edge, 0f, 1f);
            return (from + direction * (start + Math.Min(along, length - start)), alpha);
        })];
    }

    /// <summary>
    /// Each link's lane, in order: the links joining the same two parts (either way round) share a group, and
    /// each gets its own index in it, so drawing them <see cref="LaneOffset"/> apart shows them all.
    /// </summary>
    public static IReadOnlyList<(int Index, int Count)> Lanes(IReadOnlyList<LogicLink> links)
    {
        var groups = links.Select((l, i) => (Key: (Math.Min(l.From, l.To), Math.Max(l.From, l.To)), i))
            .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Select(x => x.i).ToList());
        return [.. links.Select((l, i) =>
        {
            var group = groups[(Math.Min(l.From, l.To), Math.Max(l.From, l.To))];
            return (group.IndexOf(i), group.Count);
        })];
    }

    /// <summary>
    /// How far, in px, lane <paramref name="index"/> of <paramref name="count"/> moves the line from
    /// <paramref name="from"/> to <paramref name="to"/> sideways. Lanes sit <paramref name="gap"/> apart around the
    /// middle, measured from the lower part id to the higher, so a link back the other way takes the other side.
    /// </summary>
    public static Vector2 LaneOffset(Vector2 from, Vector2 to, int index, int count, float gap, bool fromLower = true)
    {
        if (count <= 1 || Vector2.DistanceSquared(from, to) < 1e-6f)
            return Vector2.Zero;
        var along = Vector2.Normalize(fromLower ? to - from : from - to);
        var side = new Vector2(-along.Y, along.X);
        return side * ((index - (count - 1) / 2f) * gap);
    }

    /// <summary>Distance, in px, from <paramref name="point"/> to the segment <paramref name="a"/>–<paramref name="b"/>.</summary>
    public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + ab * t);
    }

    /// <summary>
    /// The index of the point nearest the aim ray from <paramref name="origin"/> along <paramref name="direction"/>
    /// (a unit vector) that is at most <paramref name="radius"/> m from it and in front, no further than
    /// <paramref name="reach"/> m along it; -1 when there is none. Ties go to the nearer one along the ray.
    /// </summary>
    public static int Snap(Vector3 origin, Vector3 direction, IReadOnlyList<Vector3> points, float radius, float reach)
    {
        var best = -1;
        var bestDistance = float.MaxValue;
        var bestAlong = float.MaxValue;
        for (var i = 0; i < points.Count; i++)
        {
            var along = Vector3.Dot(points[i] - origin, direction);
            if (along < 0f || along > reach + radius)
                continue;
            var distance = Vector3.Distance(points[i], origin + direction * along);
            if (distance > radius)
                continue;
            if (distance < bestDistance - 1e-4f || (MathF.Abs(distance - bestDistance) <= 1e-4f && along < bestAlong))
                (best, bestDistance, bestAlong) = (i, distance, along);
        }
        return best;
    }

    /// <summary>
    /// The control point of the preview line's curve while it is dragged (§10: the line's tension), for
    /// <see cref="Curve"/>: <paramref name="sag"/> of its length below the middle of the straight line (screen y
    /// grows down), or the middle itself, a straight line, once it has snapped to a node.
    /// </summary>
    public static Vector2 Tension(Vector2 from, Vector2 to, bool snapped, float sag = 0.12f)
    {
        var middle = (from + to) / 2f;
        return snapped ? middle : middle + new Vector2(0f, Vector2.Distance(from, to) * sag);
    }

    /// <summary>A point of the quadratic curve from <paramref name="a"/> through control <paramref name="c"/> to <paramref name="b"/>.</summary>
    public static Vector2 Curve(Vector2 a, Vector2 c, Vector2 b, float t) =>
        (1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b;
}
