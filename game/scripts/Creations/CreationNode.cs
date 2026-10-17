using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.World;

namespace Mechanica.Game.Creations;

/// <summary>
/// Puts a Core <see cref="Creation"/> into the world: one <see cref="CreationBody"/> per Body, placed at
/// the Body's stored pose, and one Jolt joint per unlocked joint link (D7). The node itself stays at the
/// origin; bodies are top-level so physics moves them freely.
/// <para>
/// The graph stays the source of truth. After a graph change call <see cref="Rebuild"/>; before saving
/// call <see cref="SyncToCreation"/> so the simulated poses and velocities reach the save. Only the
/// Creation's authority peer simulates (D5): on other peers the bodies are frozen kinematic and
/// <see cref="Net.CreationSync"/> poses them from snapshots. A Creation on a
/// Lift (<see cref="Creation.OnLift"/>) is frozen everywhere.
/// </para>
/// </summary>
public partial class CreationNode : Node3D
{
    private readonly List<CreationBody> _bodies = [];
    private readonly List<Joint3D> _joints = [];
    private readonly Dictionary<Joint3D, JointFrame> _frames = [];
    private readonly List<(MeshInstance3D Mesh, JointLink Link, JointFrame Frame)> _shafts = [];

    /// <summary>The graph this node shows. Set before adding the node to the tree.</summary>
    public Creation Creation { get; init; } = null!;

    /// <summary>Meshes and shapes for the parts. Set before adding the node to the tree.</summary>
    public PartAssets Assets { get; init; } = null!;

    /// <summary>Rigid bodies, in <see cref="Creation.Bodies"/> order.</summary>
    public IReadOnlyList<CreationBody> Bodies => _bodies;

    /// <summary>Jolt joints, in <see cref="Creation.Joints"/> order with locked links skipped.</summary>
    public IReadOnlyList<Joint3D> Joints => _joints;

    /// <summary>The Jolt joint built for a joint edge, or null when it is locked or has none.</summary>
    public Joint3D? JointOf(Connection connection) =>
        _joints.FirstOrDefault(j => j.Name == JointName(connection));

    /// <summary>The joint data a built joint was made from (anchor, axis, limits, spring).</summary>
    public JointFrame FrameOf(Joint3D joint) => _frames[joint];

    /// <summary>
    /// Where a joint is now, from its two live bodies: rad or m from its built pose (<see cref="JointMath"/>).
    /// Null when the link is locked or has no joint.
    /// </summary>
    public float? JointPosition(JointLink link)
    {
        if (link.IsLocked || JointOf(link.Connection) is not { } joint || link.BodyA >= _bodies.Count || link.BodyB >= _bodies.Count)
            return null;
        return JointMath.Position(_frames[joint], _bodies[link.BodyA].GlobalTransform.ToPose(), _bodies[link.BodyB].GlobalTransform.ToPose());
    }

    /// <summary>Grid-frame box of each body, in <see cref="Bodies"/> order (<see cref="CreationBounds"/>).</summary>
    public IReadOnlyList<WorldBounds> LocalBounds { get; private set; } = [];

    /// <summary>True when this peer runs the physics for the Creation.</summary>
    public bool IsSimulated => Multiplayer.GetUniqueId() == Creation.AuthorityPeer;

    /// <summary>True when the bodies are held still: another peer simulates them, or a Lift holds them.</summary>
    public bool IsFrozen => !IsSimulated || Creation.OnLift;

    public override void _Ready()
    {
        Name = $"Creation{Creation.Id}";
        Build();
    }

    public override void _Process(double delta)
    {
        foreach (var (mesh, link, frame) in _shafts)
        {
            // The rod runs from the anchor to where the far side is now, along the axis.
            var length = JointPosition(link) ?? 0f;
            mesh.Visible = length > 0.002f;
            if (!mesh.Visible)
                continue;
            var axis = frame.Axis.ToGodot();
            // Cylinders stand along +Y; Godot's arc quaternion can't turn +Y onto -Y, so that case is a half turn about X.
            var turn = axis.Y < -0.99f ? new Basis(Vector3.Right, Mathf.Pi) : new Basis(new Quaternion(Vector3.Up, axis));
            mesh.Transform = new Transform3D(turn * Basis.FromScale(new Vector3(1, length, 1)), frame.Anchor.ToGodot() + axis * (length / 2));
        }
    }

    /// <summary>Writes every body's current world pose and velocity back to <see cref="Creation"/>.</summary>
    public void SyncToCreation()
    {
        foreach (var body in _bodies)
        {
            var index = Creation.BodyIndexOf(body.PartIds[0]);
            Creation.SetBodyPose(index, body.GlobalTransform.ToPose());
            Creation.SetBodyVelocity(index, new BodyVelocity(body.LinearVelocity.ToNumerics(), body.AngularVelocity.ToNumerics()));
        }
    }

    /// <summary>
    /// Rebuilds the bodies and joints after the graph changed (a part placed or removed). Each surviving
    /// part keeps the pose of the body it was in, a new part takes the pose of a part it connects to, and
    /// each new body keeps the velocity of the old body that held its lowest part.
    /// </summary>
    public void Rebuild()
    {
        var states = new Dictionary<int, (Pose Pose, Vector3 Linear, Vector3 Angular)>();
        foreach (var body in _bodies)
        {
            var state = (body.GlobalTransform.ToPose(), body.LinearVelocity, body.AngularVelocity);
            foreach (var id in body.PartIds.Where(id => Creation.FindPart(id) is not null))
                states[id] = state;
        }
        foreach (var part in Creation.Parts.Where(p => !states.ContainsKey(p.Id)))
        {
            var neighbour = Creation.Connections.Where(c => c.Involves(part.Id))
                .Select(c => c.A == part.Id ? c.B : c.A).FirstOrDefault(states.ContainsKey);
            if (neighbour > 0)
                states[part.Id] = states[neighbour];
        }
        foreach (var (id, state) in states)
            Creation.SetPartPose(id, state.Pose);
        Clear();
        Build();
        foreach (var body in _bodies)
        {
            if (states.TryGetValue(body.PartIds[0], out var state))
                (body.LinearVelocity, body.AngularVelocity) = (state.Linear, state.Angular);
        }
    }

    /// <summary>
    /// Puts every body back at its stored pose, at rest, after the Creation was moved without a graph
    /// change (a Lift picked it up, moved or let go of it).
    /// </summary>
    public void Teleport()
    {
        Clear();
        Build();
    }

    private void Build()
    {
        var frozen = IsFrozen;
        foreach (var body in Creation.Bodies)
        {
            var node = BuildBody(body, frozen);
            // A saved or synced Creation carries on moving as it was.
            var velocity = Creation.GetBodyVelocity(body.Index);
            (node.LinearVelocity, node.AngularVelocity) = (velocity.Linear.ToGodot(), velocity.Angular.ToGodot());
            _bodies.Add(node);
        }
        LocalBounds = CreationBounds.LocalBodyBounds(Creation);
        foreach (var link in Creation.Joints)
        {
            if (!link.IsLocked && BuildJoint(link) is { } joint)
                _joints.Add(joint);
        }
    }

    private void Clear()
    {
        foreach (var node in _joints.Cast<Node>().Concat(_bodies))
        {
            RemoveChild(node);
            node.QueueFree();
        }
        _joints.Clear();
        _frames.Clear();
        _shafts.Clear();
        _bodies.Clear();
    }

    private CreationBody BuildBody(Body body, bool frozen)
    {
        var node = new CreationBody
        {
            Name = $"Body{body.PartIds[0]}",
            BodyIndex = body.Index,
            PartIds = body.PartIds,
            TopLevel = true,
            Transform = Creation.BodyPose(body.Index).ToTransform(),
            Mass = body.Mass,
            CenterOfMassMode = RigidBody3D.CenterOfMassModeEnum.Custom,
            CenterOfMass = body.CentreOfMass.ToGodot(),
            Freeze = frozen,
            FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic,
        };
        var friction = 0f;
        var frictionMass = 0f;
        foreach (var id in body.PartIds)
        {
            var part = Creation.FindPart(id)!;
            var definition = Creation.DefinitionOf(id);
            var model = Assets.ModelFor(part.Part, definition, part.Color);
            var placed = new Transform3D(part.Rotation.ToBasis(),
                PartGeometry.CentreMetres(definition, part.Cell, part.Rotation).ToGodot());
            // Shapes must be direct children of the body; meshes hang off a node per part.
            node.AddChild(new CollisionShape3D { Name = $"Shape{id}", Shape = model.Shape, Transform = placed * model.ShapeTransform });
            var visual = new Node3D { Name = $"Part{id}", Transform = placed };
            foreach (var mesh in model.Meshes)
                visual.AddChild(mesh.Instantiate());
            node.AddChild(visual);
            if (definition.Friction is { } f)
            {
                var m = definition.Mass ?? 0f;
                friction += f * m;
                frictionMass += m;
            }
        }
        // Jolt has one material per body, so a Body's friction is its parts' mass-weighted mean.
        if (frictionMass > 0)
            node.PhysicsMaterialOverride = new PhysicsMaterial { Friction = friction / frictionMass };
        AddChild(node);
        return node;
    }

    private static string JointName(Connection c) => $"Joint{c.A}_{c.B}";

    // Builds the Jolt joint of one link: a hinge for a bearing, a 6DOF joint free along one axis for a
    // piston (with a motor) or a suspension (with a spring). Godot takes each body's joint frame from
    // where the bodies are when the joint enters the tree, so body B is put back in A's grid frame (the
    // joint's zero, where both sides were built) for that moment. A joint rebuilt while extended or
    // turned keeps its zero, its limits and its spring's rest length.
    private Joint3D? BuildJoint(JointLink link)
    {
        var c = link.Connection;
        if (!Creation.TryGetJointFrame(c, out var frame))
            return null;
        var rotational = c.Type == ConnectionType.Rotational;
        var a = _bodies[link.BodyA];
        var b = _bodies[link.BodyB];
        Joint3D joint = rotational ? Hinge(frame) : Slider(frame);
        joint.Name = JointName(c);
        joint.TopLevel = true;
        joint.Transform = a.Transform * LocalJointTransform(frame);
        joint.NodeA = $"../{a.Name}";
        joint.NodeB = $"../{b.Name}";
        var actual = b.Transform;
        b.Transform = a.Transform;
        AddChild(joint);
        b.Transform = actual;
        _frames[joint] = frame;
        if (!rotational && frame.ShaftRadius is { } radius)
            AddShaft(link, frame, radius, a);
        return joint;
    }

    /// <summary>
    /// The joint's frame in the grid frame: at the socket's anchor, with the joint axis on local Z for a
    /// hinge (Godot hinges turn about Z) and on local X for a slider or spring (the 6DOF axis left free).
    /// </summary>
    public static Transform3D LocalJointTransform(JointFrame frame)
    {
        var axis = new GridCell((int)MathF.Round(frame.Axis.X), (int)MathF.Round(frame.Axis.Y), (int)MathF.Round(frame.Axis.Z));
        var rotational = frame.Type == ConnectionType.Rotational;
        var turn = GridRotation.All.First(r => (rotational ? r.ZAxis : r.XAxis) == axis);
        return new Transform3D(turn.ToBasis(), frame.Anchor.ToGodot());
    }

    // A rod hanging off the joint part's body, stretched each frame by _Process.
    private void AddShaft(JointLink link, JointFrame frame, float radius, CreationBody body)
    {
        var part = Creation.FindPart(link.Connection.A)!;
        var definition = Creation.DefinitionOf(part.Id);
        var visual = (definition.Visual ?? new VisualDefinition()) with { Color = part.Color ?? definition.Visual?.Color };
        var mesh = new MeshInstance3D
        {
            Name = $"Shaft{link.Connection.A}_{link.Connection.B}",
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 1f, RadialSegments = 12 },
            MaterialOverride = Assets.VisualMaterial(visual, part.Part.Split(':')[0]),
            Visible = false,
        };
        body.AddChild(mesh);
        _shafts.Add((mesh, link, frame));
    }

    private static HingeJoint3D Hinge(JointFrame frame)
    {
        var hinge = new HingeJoint3D();
        if (!frame.SpinsFreely)
        {
            hinge.SetFlag(HingeJoint3D.Flag.UseLimit, true);
            hinge.SetParam(HingeJoint3D.Param.LimitLower, frame.Min!.Value);
            hinge.SetParam(HingeJoint3D.Param.LimitUpper, frame.Max!.Value);
        }
        return hinge;
    }

    private static Generic6DofJoint3D Slider(JointFrame frame)
    {
        // Every axis but linear X is locked by its default 0..0 limit.
        var joint = new Generic6DofJoint3D();
        joint.SetFlagX(Generic6DofJoint3D.Flag.EnableLinearLimit, true);
        joint.SetParamX(Generic6DofJoint3D.Param.LinearLowerLimit, frame.Min ?? 0f);
        joint.SetParamX(Generic6DofJoint3D.Param.LinearUpperLimit, frame.Max ?? 0f);
        if (frame.Type == ConnectionType.Spring)
        {
            joint.SetFlagX(Generic6DofJoint3D.Flag.EnableLinearSpring, true);
            joint.SetParamX(Generic6DofJoint3D.Param.LinearSpringStiffness, frame.Stiffness ?? 0f);
            joint.SetParamX(Generic6DofJoint3D.Param.LinearSpringDamping, frame.Damping ?? 0f);
            joint.SetParamX(Generic6DofJoint3D.Param.LinearSpringEquilibriumPoint, 0f);
        }
        return joint;
    }
}
