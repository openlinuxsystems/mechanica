using Godot;
using Mechanica.Core.Creations;
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
/// <see cref="Net.CreationSync"/> poses them from snapshots.
/// </para>
/// </summary>
public partial class CreationNode : Node3D
{
    private readonly List<CreationBody> _bodies = [];
    private readonly List<Joint3D> _joints = [];

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

    /// <summary>Grid-frame box of each body, in <see cref="Bodies"/> order (<see cref="CreationBounds"/>).</summary>
    public IReadOnlyList<WorldBounds> LocalBounds { get; private set; } = [];

    /// <summary>True when this peer runs the physics for the Creation.</summary>
    public bool IsSimulated => Multiplayer.GetUniqueId() == Creation.AuthorityPeer;

    public override void _Ready()
    {
        Name = $"Creation{Creation.Id}";
        Build();
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

    private void Build()
    {
        var simulated = IsSimulated;
        foreach (var body in Creation.Bodies)
        {
            var node = BuildBody(body, simulated);
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
        _bodies.Clear();
    }

    private CreationBody BuildBody(Body body, bool simulated)
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
            Freeze = !simulated,
            FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic,
        };
        var friction = 0f;
        var frictionMass = 0f;
        foreach (var id in body.PartIds)
        {
            var part = Creation.FindPart(id)!;
            var definition = Creation.DefinitionOf(id);
            var model = Assets.ModelFor(part.Part, definition);
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

    private Joint3D? BuildJoint(JointLink link)
    {
        var c = link.Connection;
        if (!Creation.TryGetSocketFrame(c.A, c.ASocket!, out var position, out var normal))
            return null;
        // Godot hinges turn about local Z and sliders move along local X.
        Joint3D joint = c.Type == ConnectionType.Rotational ? new HingeJoint3D() : new SliderJoint3D();
        var axis = new GridCell((int)MathF.Round(normal.X), (int)MathF.Round(normal.Y), (int)MathF.Round(normal.Z));
        var frame = GridRotation.All.First(r => (c.Type == ConnectionType.Rotational ? r.ZAxis : r.XAxis) == axis);
        var poseA = Creation.BodyPose(link.BodyA).ToTransform();
        joint.Name = JointName(c);
        joint.TopLevel = true;
        joint.Transform = poseA * new Transform3D(frame.ToBasis(), position.ToGodot());
        // Spring links get a free slider until chunk 9 adds the spring primitive.
        joint.NodeA = $"../{_bodies[link.BodyA].Name}";
        joint.NodeB = $"../{_bodies[link.BodyB].Name}";
        AddChild(joint);
        return joint;
    }
}
