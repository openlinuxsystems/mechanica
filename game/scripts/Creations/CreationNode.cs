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
/// Lift (<see cref="Creation.OnLift"/>) is frozen everywhere, and so is each Body holding a part anchored to the
/// ground (<see cref="Creation.IsBodyAnchored"/>), so players can't kick a build about.
/// </para>
/// </summary>
public partial class CreationNode : Node3D
{
    private readonly List<CreationBody> _bodies = [];
    private readonly List<Joint3D> _joints = [];
    private readonly Dictionary<Joint3D, JointFrame> _frames = [];
    private readonly List<(MeshInstance3D Mesh, JointLink Link, JointFrame Frame)> _shafts = [];
    private readonly List<(int Part, Light3D Light, float Energy)> _lights = [];
    private readonly List<(int Part, Node3D Model, Vector3 Axis, Vector3 Pivot)> _animated = [];
    private readonly List<PartDisplayVisual> _displays = [];
    private readonly List<int> _soils = [];
    private readonly Dictionary<int, Node3D> _partNodes = [];
    private readonly List<(CreationBody Body, Node3D Visual, Transform3D Placed)> _visuals = [];
    private Vector3 _visualOffset;
    private readonly List<(int Station, int Container)> _pipes = [];

    // The pipe from a station to each container linked to it (UI spec §4): visual only, no collision.
    private static readonly StandardMaterial3D PipeMaterial = new()
    {
        AlbedoColor = Ui.UiTokens.Blue.Darkened(0.35f),
        Metallic = 0.7f,
        Roughness = 0.35f,
    };

    private static readonly CylinderMesh PipeMesh = new() { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 1f, RadialSegments = 10, Rings = 1, Material = PipeMaterial };

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

    /// <summary>The storage pipes drawn, station then linked container (<see cref="Core.Items.StorageLinks"/>).</summary>
    public IReadOnlyList<(int Station, int Container)> Pipes => _pipes;

    /// <summary>True when this peer runs the physics for the Creation.</summary>
    public bool IsSimulated => Multiplayer.GetUniqueId() == Creation.AuthorityPeer;

    /// <summary>True when the bodies are held still: another peer simulates them, or a Lift holds them.</summary>
    public bool IsFrozen => !IsSimulated || Creation.OnLift;

    /// <summary>True when the Body at <paramref name="bodyIndex"/> is held still: the whole Creation is, or it is anchored to the ground.</summary>
    public bool IsBodyFrozen(int bodyIndex) => IsFrozen || Creation.IsBodyAnchored(bodyIndex);

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

    /// <summary>
    /// Shifts how every part is drawn by a world-space offset without moving its bodies, so a Creation held by a Lift
    /// rides the platform as it eases between levels while its collision stays where the host put it. Zero when not held.
    /// </summary>
    public Vector3 VisualOffset
    {
        get => _visualOffset;
        set
        {
            if (value == _visualOffset)
                return;
            _visualOffset = value;
            ApplyVisualOffset();
        }
    }

    private void ApplyVisualOffset()
    {
        foreach (var (body, visual, placed) in _visuals)
            visual.Transform = new Transform3D(placed.Basis, placed.Origin + body.Transform.Basis.Inverse() * _visualOffset);
    }

    /// <summary>The light of each part with a <c>light</c> section, by part id (chunk 23).</summary>
    public IEnumerable<(int Part, Light3D Light)> Lights => _lights.Select(l => (l.Part, l.Light));

    /// <summary>Shows every part light at its level: dark at 0, its full energy at 1.</summary>
    public void ApplyLights(Core.Logic.LightLevels levels)
    {
        foreach (var (part, light, energy) in _lights)
        {
            var level = levels.Level(Creation.Id, part);
            light.Visible = level > 0f;
            light.LightEnergy = energy * level;
        }
    }

    /// <summary>The model node of each part with an <c>animation</c> section, by part id (chunk 23).</summary>
    public IEnumerable<(int Part, Node3D Model)> AnimatedModels => _animated.Select(a => (a.Part, a.Model));

    /// <summary>Turns every animated part's model to its angle at <paramref name="now"/> on the animations' clock.</summary>
    public void ApplyAnimations(Core.Logic.PartAnimations animations, double now)
    {
        foreach (var (part, model, axis, pivot) in _animated)
        {
            var turn = new Basis(axis, animations.AngleAt(Creation.Id, part, now));
            // Turning about the pivot: the model's origin swings round it.
            model.Transform = new Transform3D(turn, pivot - turn * pivot);
        }
    }

    /// <summary>What each part with a <c>display</c> section shows on itself (UI spec §12).</summary>
    public IReadOnlyList<PartDisplayVisual> Displays => _displays;

    /// <summary>Draws every part's display from <paramref name="readouts"/> (after <see cref="ApplyAnimations"/>).</summary>
    public void ApplyDisplays(Core.Logic.PartReadouts readouts, bool connecting, double delta)
    {
        foreach (var display in _displays)
        {
            var linked = Creation.LinksFrom(display.Part).Count > 0 || Creation.LinksTo(display.Part).Count > 0;
            display.Apply(readouts, linked, connecting, _animated.Any(a => a.Part == display.Part), delta);
        }
    }

    /// <summary>The soil parts, whose look follows how wet they are (<see cref="Survival.SoilLooks"/>).</summary>
    public IReadOnlyList<int> SoilParts => _soils;

    /// <summary>A placed part's node (its frame in the world), or null when this Creation has no such part.</summary>
    public Node3D? PartNode(int part) => _partNodes.GetValueOrDefault(part);

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
            var node = BuildBody(body, frozen || Creation.IsBodyAnchored(body.Index));
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
        if (_visualOffset != Vector3.Zero)
            ApplyVisualOffset();
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
        _lights.Clear();
        _animated.Clear();
        _displays.Clear();
        _soils.Clear();
        _partNodes.Clear();
        _visuals.Clear();
        _pipes.Clear();
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
            // The meshes and light hang off a model node, which an animated part's script turns.
            var shown = new Node3D { Name = "Model" };
            visual.AddChild(shown);
            // A part whose display plays clips shows its whole mesh scene, so its clips can move its pieces.
            var scene = definition.Display?.Clips is { Count: > 0 } ? Assets.AnimatedModelFor(part.Part, definition, part.Color) : null;
            if (scene is not null)
                shown.AddChild(scene);
            else
            {
                foreach (var mesh in model.Meshes)
                    shown.AddChild(mesh.Instantiate());
            }
            if (definition.Light is { } light)
            {
                var lamp = BuildLight(light);
                shown.AddChild(lamp);
                _lights.Add((id, lamp, light.Energy ?? 1f));
            }
            if (definition.Animation is { } animation)
                _animated.Add((id, shown, (animation.Axis ?? new Vec3(0, 1, 0)).ToGodot().Normalized(), (animation.Pivot ?? Vec3.Zero).ToGodot()));
            if (definition.Soil is not null)
                _soils.Add(id);
            if (definition.Display is not null)
                _displays.Add(new PartDisplayVisual(Creation.Id, id, definition, visual, shown, scene));
            _partNodes[id] = visual;
            node.AddChild(visual);
            _visuals.Add((node, visual, placed));
            if (definition.Friction is { } f)
            {
                var m = definition.Mass ?? 0f;
                friction += f * m;
                frictionMass += m;
            }
        }
        AddPipes(node, body);
        // Jolt has one material per body, so a Body's friction is its parts' mass-weighted mean.
        if (frictionMass > 0)
            node.PhysicsMaterialOverride = new PhysicsMaterial { Friction = friction / frictionMass };
        AddChild(node);
        return node;
    }

    // A pipe from each station in the body to every linked container in the same body, centre to centre.
    private void AddPipes(CreationBody node, Body body)
    {
        foreach (var station in body.PartIds.Where(id => Creation.DefinitionOf(id).Crafting is not null))
        {
            foreach (var container in Core.Items.StorageLinks.Of(Creation, station).Where(body.PartIds.Contains))
            {
                var from = Centre(station);
                var to = Centre(container);
                var length = from.DistanceTo(to);
                if (length < 0.01f)
                    continue;
                var up = (to - from) / length;
                var side = Mathf.Abs(up.Dot(Vector3.Forward)) > 0.9f ? Vector3.Right : Vector3.Forward;
                var x = up.Cross(side).Normalized();
                var basis = new Basis(x, up, x.Cross(up)).Scaled(new Vector3(1, length, 1));
                node.AddChild(new MeshInstance3D
                {
                    Name = $"Pipe{station}to{container}",
                    Mesh = PipeMesh,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Transform = new Transform3D(basis, (from + to) / 2),
                });
                _pipes.Add((station, container));
            }
        }
    }

    private Vector3 Centre(int part)
    {
        var instance = Creation.FindPart(part)!;
        return PartGeometry.CentreMetres(Creation.DefinitionOf(part), instance.Cell, instance.Rotation).ToGodot();
    }

    // A part's light, dark until its script turns it up. Spots shine along local -Z, so they are turned to the light's direction.
    private static Light3D BuildLight(LightDefinition light)
    {
        Light3D lamp;
        var range = light.Range ?? 1f;
        if (light.Kind == LightKind.Spot)
            lamp = new SpotLight3D { SpotRange = range, SpotAngle = light.Angle ?? 30f };
        else
            lamp = new OmniLight3D { OmniRange = range };
        lamp.Name = "Light";
        lamp.Visible = false;
        lamp.LightEnergy = 0f;
        if (HexColor.TryParse(light.Color, out var color))
            lamp.LightColor = new Color(color.R, color.G, color.B);
        var at = (light.Offset ?? Vec3.Zero).ToGodot();
        var forward = (light.Direction ?? new Vec3(0, 0, -1)).ToGodot().Normalized();
        var up = MathF.Abs(forward.Dot(Vector3.Up)) > 0.99f ? Vector3.Back : Vector3.Up;
        lamp.Transform = new Transform3D(Basis.LookingAt(forward, up), at);
        return lamp;
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
