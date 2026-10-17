using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Creations;

/// <summary>
/// Line drawings of a joint, shared by the Part Editor preview and the in-game joint debug overlay: the
/// axis through the anchor, the range it may move over (an arc for a limited hinge, a ring for a free
/// one, a bar with end ticks for a slider or spring) and, when known, a marker where it is now.
/// Everything is in the frame the <see cref="JointFrame"/> is given in.
/// </summary>
public static class JointGizmo
{
    private const float AxisLength = 0.18f;
    private const float Radius = 0.09f;
    private const float Tick = 0.03f;
    private const int ArcSegments = 24;

    /// <summary>The colour of each joint type, matching the socket colours.</summary>
    public static Color ColorOf(ConnectionType type) => type switch
    {
        ConnectionType.Rotational => UiTokens.JointHinge,
        ConnectionType.Linear => UiTokens.JointSlider,
        ConnectionType.Spring => UiTokens.JointSpring,
        _ => UiTokens.JointRigid,
    };

    /// <summary>Colour of the "where it is now" marker.</summary>
    public static readonly Color PositionColor = UiTokens.GizmoSelected;

    /// <summary>A joint's frame from a socket alone, in the part's own frame (for the editor).</summary>
    public static JointFrame? FrameOf(SocketDefinition socket)
    {
        var type = socket.Type switch
        {
            SocketType.Rotational => ConnectionType.Rotational,
            SocketType.Linear => ConnectionType.Linear,
            SocketType.Spring => ConnectionType.Spring,
            _ => (ConnectionType?)null,
        };
        if (type is not { } t)
            return null;
        var joint = socket.Joint;
        return new JointFrame(t, socket.Position ?? Vec3.Zero, joint?.Axis ?? socket.Normal ?? new Vec3(0, 1, 0), socket.Normal ?? new Vec3(0, 1, 0),
            t == ConnectionType.Linear ? joint?.Min ?? 0f : joint?.Min, joint?.Max, joint?.Stiffness, joint?.Damping);
    }

    /// <summary>The range lines of a joint: its axis and how far it may move.</summary>
    public static List<(Vector3 A, Vector3 B)> RangeLines(JointFrame frame)
    {
        var anchor = frame.Anchor.ToGodot();
        var axis = Axis(frame);
        var lines = new List<(Vector3, Vector3)>();
        if (frame.Type == ConnectionType.Rotational)
        {
            lines.Add((anchor - axis * AxisLength * 0.5f, anchor + axis * AxisLength));
            var (from, to) = frame.SpinsFreely ? (-Mathf.Pi, Mathf.Pi) : (frame.Min!.Value, frame.Max!.Value);
            var points = Enumerable.Range(0, ArcSegments + 1).Select(i => ArcPoint(frame, Mathf.Lerp(from, to, i / (float)ArcSegments))).ToList();
            for (var i = 0; i < ArcSegments; i++)
                lines.Add((points[i], points[i + 1]));
            if (!frame.SpinsFreely)
            {
                lines.Add((anchor, points[0]));
                lines.Add((anchor, points[^1]));
            }
            return lines;
        }
        var side = Side(axis) * Tick;
        var (lo, hi) = (anchor + axis * (frame.Min ?? 0f), anchor + axis * (frame.Max ?? 0f));
        lines.Add((lo, hi));
        lines.Add((lo - side, lo + side));
        lines.Add((hi - side, hi + side));
        // The built position (the spring's rest length) gets a short tick across the other way.
        var across = axis.Cross(side).Normalized() * Tick * 0.6f;
        lines.Add((anchor - across, anchor + across));
        if (frame.Type == ConnectionType.Spring)
        {
            // A zig-zag along the travel says "spring".
            const int zigs = 8;
            for (var i = 0; i < zigs; i++)
            {
                var a = lo.Lerp(hi, i / (float)zigs) + side * (i % 2 == 0 ? 0.5f : -0.5f);
                var b = lo.Lerp(hi, (i + 1) / (float)zigs) + side * (i % 2 == 0 ? -0.5f : 0.5f);
                lines.Add((a, b));
            }
        }
        return lines;
    }

    /// <summary>A marker where the joint is now: a spoke at its angle, or a cross bar at its extension.</summary>
    public static List<(Vector3 A, Vector3 B)> PositionLines(JointFrame frame, float position)
    {
        var anchor = frame.Anchor.ToGodot();
        if (frame.Type == ConnectionType.Rotational)
            return [(anchor, anchor + (ArcPoint(frame, position) - anchor) * 1.25f)];
        var axis = Axis(frame);
        var at = anchor + axis * position;
        var side = Side(axis) * Tick * 1.6f;
        return [(at - side, at + side), (anchor, at)];
    }

    /// <summary>A small 3D cross at a centre of mass.</summary>
    public static List<(Vector3 A, Vector3 B)> CrossLines(Vector3 centre, float size = 0.04f) =>
        [(centre - Vector3.Right * size, centre + Vector3.Right * size),
         (centre - Vector3.Up * size, centre + Vector3.Up * size),
         (centre - Vector3.Back * size, centre + Vector3.Back * size)];

    /// <summary>An unshaded line mesh drawn over everything.</summary>
    public static MeshInstance3D Mesh(string name, IEnumerable<(Vector3 A, Vector3 B)> lines, Color color)
    {
        var mesh = new ImmediateMesh();
        var any = false;
        foreach (var (a, b) in lines)
        {
            if (!any)
                mesh.SurfaceBegin(Godot.Mesh.PrimitiveType.Lines);
            any = true;
            mesh.SurfaceAddVertex(a);
            mesh.SurfaceAddVertex(b);
        }
        if (any)
            mesh.SurfaceEnd();
        return new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true,
            },
        };
    }

    private static Vector3 Axis(JointFrame frame) => frame.Axis.ToGodot().Normalized();

    // The point at an angle on the hinge's circle, starting from a reference direction across the axis.
    private static Vector3 ArcPoint(JointFrame frame, float angle)
    {
        var axis = Axis(frame);
        return frame.Anchor.ToGodot() + Side(axis).Rotated(axis, angle) * Radius;
    }

    // A fixed direction at right angles to the axis.
    private static Vector3 Side(Vector3 axis) =>
        (Mathf.Abs(axis.Y) > 0.9f ? axis.Cross(Vector3.Right) : axis.Cross(Vector3.Up)).Normalized();
}
