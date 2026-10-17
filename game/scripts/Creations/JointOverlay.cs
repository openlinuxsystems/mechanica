using Godot;
using Mechanica.Core.Creations;

namespace Mechanica.Game.Creations;

/// <summary>
/// Joint debug overlay (spec 13.1): for every live Creation it draws each joint's axis and range of
/// motion in its type's colour (<see cref="JointGizmo"/>), a white marker where the joint is now, and
/// each Body's centre of mass. Locked joints (both sides welded into one Body) are grey. It reads the
/// bodies as this peer shows them, so on a client it draws what state sync delivered. Redrawn every
/// frame while visible; hidden by default.
/// </summary>
public partial class JointOverlay : MeshInstance3D
{
    private static readonly Color LockedColor = new(0.55f, 0.55f, 0.55f);
    private static readonly Color CentreOfMassColor = new(1f, 0.3f, 0.3f);

    private readonly ImmediateMesh _lines = new();

    /// <summary>The Creations to draw. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Joints drawn on the last redraw, for tests.</summary>
    public int JointsDrawn { get; private set; }

    public override void _Ready()
    {
        Name = "JointOverlay";
        Mesh = _lines;
        TopLevel = true;
        Visible = false;
        CastShadow = ShadowCastingSetting.Off;
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
        };
    }

    public override void _Process(double delta)
    {
        if (Visible)
            Redraw();
    }

    /// <summary>Draws the current state now.</summary>
    public void Redraw()
    {
        _lines.ClearSurfaces();
        JointsDrawn = 0;
        var any = false;
        void Add(Transform3D frame, IEnumerable<(Vector3 A, Vector3 B)> lines, Color color)
        {
            foreach (var (a, b) in lines)
            {
                if (!any)
                    _lines.SurfaceBegin(Godot.Mesh.PrimitiveType.Lines);
                any = true;
                _lines.SurfaceSetColor(color);
                _lines.SurfaceAddVertex(frame * a);
                _lines.SurfaceSetColor(color);
                _lines.SurfaceAddVertex(frame * b);
            }
        }
        foreach (var node in Creations.All.Select(c => Creations.NodeOf(c.Id)).OfType<CreationNode>())
        {
            foreach (var body in node.Bodies)
                Add(body.GlobalTransform, JointGizmo.CrossLines(body.CenterOfMass, 0.06f), CentreOfMassColor);
            foreach (var link in node.Creation.Joints)
            {
                if (link.BodyA >= node.Bodies.Count || !node.Creation.TryGetJointFrame(link.Connection, out var frame))
                    continue;
                var at = node.Bodies[link.BodyA].GlobalTransform;
                Add(at, JointGizmo.RangeLines(frame), link.IsLocked ? LockedColor : JointGizmo.ColorOf(frame.Type));
                if (node.JointPosition(link) is { } position)
                    Add(at, JointGizmo.PositionLines(frame, position), JointGizmo.PositionColor);
                JointsDrawn++;
            }
        }
        if (any)
            _lines.SurfaceEnd();
    }
}
