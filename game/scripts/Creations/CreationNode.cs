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
    private readonly Dictionary<(int A, int B), Joint3D> _jointsByParts = [];
    private readonly List<(MeshInstance3D Mesh, JointLink Link, JointFrame Frame)> _shafts = [];
    private readonly List<Flex> _flexes = [];

    // A joint part's mesh named after its joint socket plus "Flex" (a suspension's coil), skewed each frame from its
    // rest transform so its far end (along the socket normal) follows the joint by Distance along Axis.
    private sealed record Flex(MeshInstance3D Mesh, JointLink Link, Transform3D Rest, Vector3 Axis, Vector3 Normal, float Start, float Length);

    // A linear joint part's mesh named after its joint socket plus "Rod" (a piston's rod segment) and its copies, stacked
    // end to end each frame from the moving head back into the part, so the rod reveals segment by segment.
    private sealed record Rod(List<MeshInstance3D> Copies, JointLink Link, Transform3D Rest, Vector3 Axis, float Segment);
    private readonly List<Rod> _rods = [];

    // An aimed joint (a Turret Seat's turret, parts map E8): two Jolt hinges stacked through a yoke body that belongs to
    // no part, yaw from the joint part's Body to the yoke and pitch from the yoke to the Body built on it.
    private sealed record AimJoint(JointLink Link, JointFrame Frame, RigidBody3D Yoke, HingeJoint3D Yaw, HingeJoint3D Pitch);
    private readonly Dictionary<(int A, int B), AimJoint> _aimJoints = [];

    // A turret part's model nodes named after its aimed socket plus "Yaw" and "Pitch" (the pitch node under the yaw node,
    // both at the pivot), turned each frame to the turret's aim.
    private sealed record AimVisual(int Part, Node3D? Yaw, Node3D? Pitch, Transform3D YawRest, Transform3D PitchRest, Vector3 YawAxis, Vector3 PitchAxis);
    private readonly List<AimVisual> _aimVisuals = [];
    private readonly Dictionary<int, (float Yaw, float Pitch)> _aimShown = [];

    // How heavy a yoke is: light next to what it carries, heavy enough that the hinge pair stays stiff.
    private const float YokeMassShare = 0.25f;
    private const float YokeMinMass = 2f;
    // The yoke's moment of inertia (kg·m²) per kg of the Body it carries, about each axis.
    private const float YokeInertiaPerKg = 0.5f;

    // How fast a turret drawn from its script's readouts (nothing built on it) closes on the latest one, per second: the
    // readouts arrive 20 times a second.
    private const float AimVisualRate = 18f;
    private readonly List<(int Part, Light3D Light, float Energy)> _lights = [];
    private readonly List<(int Part, Node3D Model, Vector3 Axis, Vector3 Pivot)> _animated = [];
    private readonly HashSet<int> _animatedParts = [];
    private readonly List<PartDisplayVisual> _displays = [];
    // Parts' words (a sign's text), redrawn when the Creation's revision moves (a setting changed).
    private readonly List<(int Part, Label3D Label, TextDisplayDefinition Text)> _texts = [];
    private int _textsRevision = -1;
    private readonly List<int> _soils = [];
    private readonly Dictionary<int, Node3D> _partNodes = [];
    private readonly List<(CreationBody Body, Node3D Visual, Transform3D Placed)> _visuals = [];
    private Vector3 _visualOffset;
    private readonly List<(int Station, int Container)> _pipes = [];
    private StepRamps _ramps = null!;

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
        _jointsByParts.GetValueOrDefault((connection.A, connection.B));

    /// <summary>The yoke and the yaw and pitch hinges built for an aimed joint edge, or null when it is locked or has none.</summary>
    public (RigidBody3D Yoke, HingeJoint3D Yaw, HingeJoint3D Pitch)? AimJointsOf(Connection connection) =>
        _aimJoints.TryGetValue((connection.A, connection.B), out var aim) ? (aim.Yoke, aim.Yaw, aim.Pitch) : null;

    /// <summary>
    /// An aimed joint's yaw and pitch now (rad from its built pose), from its two live bodies; null when the link is
    /// locked or has no joint.
    /// </summary>
    public (float Yaw, float Pitch)? AimPosition(JointLink link)
    {
        if (link.IsLocked || !_aimJoints.TryGetValue((link.Connection.A, link.Connection.B), out var aim)
            || link.BodyA >= _bodies.Count || link.BodyB >= _bodies.Count)
            return null;
        return JointMath.Aim(aim.Frame, _bodies[link.BodyA].GlobalTransform.ToPose(), _bodies[link.BodyB].GlobalTransform.ToPose());
    }

    /// <summary>
    /// How a turret part (one with an aimed socket) is drawn turned now: its yaw and pitch in rad, from its joint when
    /// something is built on it, else from its script's <c>aim_yaw</c> and <c>aim_pitch</c> readouts. Null for other parts
    /// and before the first <see cref="ApplyDisplays"/>.
    /// </summary>
    public (float Yaw, float Pitch)? AimShown(int part) => _aimShown.TryGetValue(part, out var aim) ? aim : null;

    /// <summary>The yokes of the aimed joints (for tests).</summary>
    public IEnumerable<RigidBody3D> Yokes => _aimJoints.Values.Select(a => a.Yoke);

    /// <summary>Gives each turret's yoke the motion of the Body it carries (after a parked Creation resumes moving).</summary>
    public void MatchYokes()
    {
        foreach (var aim in _aimJoints.Values)
        {
            if (aim.Link.BodyB < _bodies.Count && !aim.Yoke.Freeze)
                (aim.Yoke.LinearVelocity, aim.Yoke.AngularVelocity) = (_bodies[aim.Link.BodyB].LinearVelocity, _bodies[aim.Link.BodyB].AngularVelocity);
        }
    }

    /// <summary>The joint data a built joint was made from (anchor, axis, limits, spring).</summary>
    public JointFrame FrameOf(Joint3D joint) => _frames[joint];

    /// <summary>
    /// Where a joint is now, from its two live bodies: rad or m from its built pose (<see cref="JointMath"/>).
    /// Null when the link is locked or has no joint.
    /// </summary>
    public float? JointPosition(JointLink link)
    {
        if (link.Connection.Type == ConnectionType.Aimed)
            return AimPosition(link)?.Yaw;
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

    /// <summary>The ramps this Creation's wheels have laid at steps (<see cref="StepRamps"/>).</summary>
    public StepRamps Ramps => _ramps;

    public override void _Ready()
    {
        Name = $"Creation{Creation.Id}";
        _ramps = new StepRamps(this, body => IsBodyFrozen(body.BodyIndex), StaysPut, Drive);
        Build();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_ramps.Any)
            _ramps.Update(GetWorld3D().DirectSpaceState);
    }

    // Terrain and structures never move, nor does a Body anchored to the ground; anything else simulated can.
    private static bool StaysPut(CollisionObject3D obstacle) => obstacle switch
    {
        CreationBody body => body.GetParent() is CreationNode node && node.Creation.IsBodyAnchored(body.BodyIndex) && !node.Creation.OnLift,
        RigidBody3D or CharacterBody3D => false,
        _ => true,
    };

    // The level way this Creation's turning hinge motors drive it: each one's wheel rolls across its axis.
    private Vector3 Drive()
    {
        var drive = Vector3.Zero;
        foreach (var joint in _joints)
        {
            if (joint is HingeJoint3D hinge && hinge.GetFlag(HingeJoint3D.Flag.EnableMotor))
                drive += Vector3.Up.Cross(hinge.GlobalBasis.Z) * hinge.GetParam(HingeJoint3D.Param.MotorTargetVelocity);
        }
        return drive with { Y = 0f };
    }

    public override void _Process(double delta)
    {
        if (_texts.Count > 0 && _textsRevision != Creation.Revision)
            ShowTexts();
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
        foreach (var flex in _flexes)
        {
            // v' = v + Axis * k * (Normal·v - Start): the near end stays, the far end moves the whole joint travel.
            var k = (JointPosition(flex.Link) ?? 0f) / flex.Length;
            var (a, n) = (flex.Axis * k, flex.Normal);
            var skew = new Basis(Vector3.Right + a * n.X, Vector3.Up + a * n.Y, Vector3.Back + a * n.Z);
            flex.Mesh.Transform = new Transform3D(skew, -a * flex.Start) * flex.Rest;
        }
        foreach (var rod in _rods)
        {
            // Segment i hangs i segments back from the head; the ones that would sit wholly inside the part hide.
            var length = Mathf.Max(0f, JointPosition(rod.Link) ?? 0f);
            var shown = Mathf.Min(rod.Copies.Count, Mathf.CeilToInt(length / rod.Segment - 1e-3f));
            for (var i = 0; i < rod.Copies.Count; i++)
            {
                var copy = rod.Copies[i];
                copy.Visible = i == 0 || i < shown;
                copy.Transform = i < shown ? new Transform3D(Basis.Identity, rod.Axis * (length - i * rod.Segment)) * rod.Rest : rod.Rest;
            }
        }
    }

    /// <summary>Each part's words as drawn now, by part (for tests).</summary>
    public IEnumerable<(int Part, Label3D Label)> Texts => _texts.Select(t => (t.Part, t.Label));

    private void ShowTexts()
    {
        _textsRevision = Creation.Revision;
        foreach (var (part, label, text) in _texts)
        {
            if (Creation.FindPart(part) is not { } placed)
                continue;
            var definition = Creation.DefinitionOf(part);
            PartText.Show(label, text, Core.Logic.PartSettings.Find(definition, text.Setting ?? ""), PartText.WordsOf(definition, placed.Settings, text));
        }
    }

    /// <summary>How many rod segments of each sliding joint mesh (a piston's rod) show now, by part.</summary>
    public IEnumerable<(int Part, int Segments)> RodSegments =>
        _rods.Select(r => (r.Link.Connection.A, r.Copies.Count(c => c.Visible && c.Transform != r.Rest)));

    /// <summary>How far each flexing joint mesh (a suspension's coil) is skewed now, by part: its far end's offset in metres.</summary>
    public IEnumerable<(int Part, float Offset)> Flexes =>
        _flexes.Select(f => (f.Link.Connection.A, (f.Mesh.Transform.Basis * f.Normal - f.Rest.Basis * f.Normal).Dot(f.Axis) * f.Length));

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
            // Every frame for every light: only a change reaches the renderer.
            var level = levels.Level(Creation.Id, part);
            if (light.Visible != level > 0f)
                light.Visible = level > 0f;
            if (light.LightEnergy != energy * level)
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
            display.Apply(readouts, linked, connecting, _animatedParts.Contains(display.Part), delta);
        }
        ApplyAims(readouts, delta);
    }

    // A turret part's yaw and pitch nodes, found anywhere in its model (a seat's model is its whole glTF scene).
    private void AddAimVisual(int part, SocketDefinition socket, Node3D model)
    {
        var yaw = model.FindChild($"{socket.Id}Yaw", recursive: true, owned: false) as Node3D;
        var pitch = model.FindChild($"{socket.Id}Pitch", recursive: true, owned: false) as Node3D;
        if (yaw is null && pitch is null)
            return;
        var yawAxis = (socket.Joint?.Axis ?? socket.Normal ?? new Vec3(0, 1, 0)).ToGodot().Normalized();
        var pitchAxis = (socket.Joint?.PitchAxis ?? new Vec3(1, 0, 0)).ToGodot().Normalized();
        _aimVisuals.Add(new AimVisual(part, yaw, pitch, yaw?.Transform ?? Transform3D.Identity, pitch?.Transform ?? Transform3D.Identity, yawAxis, pitchAxis));
    }

    // Turns each turret's model nodes to its aim: from its joint while something is built on it (so the shell stays
    // with what it carries), else from its script's readouts, eased between their updates.
    private void ApplyAims(Core.Logic.PartReadouts readouts, double delta)
    {
        foreach (var visual in _aimVisuals)
        {
            (float Yaw, float Pitch) aim;
            if (Creation.JointOf(visual.Part) is { Connection.Type: ConnectionType.Aimed } link && AimPosition(link) is { } measured)
                aim = measured;
            else
            {
                var target = (Yaw: readouts.Number(Creation.Id, visual.Part, "aim_yaw") ?? 0f, Pitch: readouts.Number(Creation.Id, visual.Part, "aim_pitch") ?? 0f);
                var was = _aimShown.TryGetValue(visual.Part, out var shown) ? shown : target;
                var k = 1f - Mathf.Exp(-AimVisualRate * (float)delta);
                aim = (JointMath.Wrap(was.Yaw + JointMath.Wrap(target.Yaw - was.Yaw) * k), was.Pitch + (target.Pitch - was.Pitch) * k);
            }
            _aimShown[visual.Part] = aim;
            // Each node turns about its own origin (the pivot), in its parent's frame.
            if (visual.Yaw is { } yaw)
                yaw.Transform = new Transform3D(new Basis(visual.YawAxis, aim.Yaw) * visual.YawRest.Basis, visual.YawRest.Origin);
            if (visual.Pitch is { } pitch)
                pitch.Transform = new Transform3D(new Basis(visual.PitchAxis, aim.Pitch) * visual.PitchRest.Basis, visual.PitchRest.Origin);
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
        Creation.SettleInsets();
        Clear();
        Build();
        foreach (var body in _bodies)
        {
            if (states.TryGetValue(body.PartIds[0], out var state))
                (body.LinearVelocity, body.AngularVelocity) = (state.Linear, state.Angular);
        }
        MatchYokes();
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
            if (link.Connection.Type == ConnectionType.Aimed)
            {
                if (!link.IsLocked)
                    BuildAim(link);
                continue;
            }
            if (!link.IsLocked && BuildJoint(link) is { } joint)
            {
                _joints.Add(joint);
                _jointsByParts.TryAdd((link.Connection.A, link.Connection.B), joint);
            }
            if (!link.IsLocked)
                MoveJointMeshes(link);
        }
        if (_visualOffset != Vector3.Zero)
            ApplyVisualOffset();
    }

    private void Clear()
    {
        var aims = _aimJoints.Values.SelectMany(a => new Node[] { a.Yaw, a.Pitch, a.Yoke });
        foreach (var node in _joints.Cast<Node>().Concat(aims).Concat(_bodies))
        {
            RemoveChild(node);
            node.QueueFree();
        }
        _aimJoints.Clear();
        _aimVisuals.Clear();
        _joints.Clear();
        _jointsByParts.Clear();
        _frames.Clear();
        _shafts.Clear();
        _flexes.Clear();
        _rods.Clear();
        _lights.Clear();
        _animated.Clear();
        _animatedParts.Clear();
        _displays.Clear();
        _texts.Clear();
        _textsRevision = -1;
        _soils.Clear();
        _partNodes.Clear();
        _visuals.Clear();
        _pipes.Clear();
        _bodies.Clear();
        _ramps.Clear();
    }

    private CreationBody BuildBody(Body body, bool frozen)
    {
        var node = new CreationBody
        {
            Name = $"Body{body.PartIds[0]}",
            CreationId = Creation.Id,
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
            if (definition.Collision is { Climb: > 0f, Radius: { } radius } climbing && climbing.Shape is CollisionShape.Sphere or CollisionShape.Cylinder)
            {
                var shape = placed * model.ShapeTransform;
                var cylinder = climbing.Shape == CollisionShape.Cylinder;
                _ramps.Add(node, shape.Origin, cylinder ? shape.Basis.Y : null, radius, climbing.Climb.Value, cylinder ? climbing.Height ?? 0f : 0f);
            }
            var visual = new Node3D { Name = $"Part{id}", Transform = placed };
            // The meshes and light hang off a model node, which an animated part's script turns.
            var shown = new Node3D { Name = "Model" };
            visual.AddChild(shown);
            // A part that plays its own clips shows its whole glTF scene; any other, its meshes.
            if (definition.Display?.Clips is not null && Assets.SceneFor(part.Part, definition, part.Color) is { } scene)
                shown.AddChild(scene);
            else
            {
                foreach (var mesh in model.Meshes)
                    shown.AddChild(mesh.Instantiate(placed));
            }
            if (definition.Light is { } light)
            {
                var lamp = BuildLight(light, part.Color);
                shown.AddChild(lamp);
                _lights.Add((id, lamp, light.Energy ?? 1f));
            }
            if (definition.Animation is { } animation)
            {
                _animated.Add((id, shown, (animation.Axis ?? new Vec3(0, 1, 0)).ToGodot().Normalized(), (animation.Pivot ?? Vec3.Zero).ToGodot()));
                _animatedParts.Add(id);
            }
            if (definition.Soil is not null)
                _soils.Add(id);
            if (definition.Sockets?.FirstOrDefault(s => s.Type == SocketType.Aimed) is { } aimed)
                AddAimVisual(id, aimed, shown);
            if (definition.Display is not null)
                _displays.Add(new PartDisplayVisual(Creation.Id, id, definition, visual, shown));
            if (definition.Display?.Text is { } text)
            {
                var label = PartText.Create(text);
                shown.AddChild(label);
                _texts.Add((id, label, text));
            }
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
            foreach (var container in Core.Items.StorageLinks.NearOf(Creation, station).Where(body.PartIds.Contains))
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
    // A painted light shines in its paint colour (parts map E5), as Scrap Mechanic's do.
    private static Light3D BuildLight(LightDefinition light, string? paint)
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
        if (HexColor.TryParse(paint ?? light.Color, out var color))
            lamp.LightColor = new Color(color.R, color.G, color.B);
        var at = (light.Offset ?? Vec3.Zero).ToGodot();
        var forward = (light.Direction ?? new Vec3(0, 0, -1)).ToGodot().Normalized();
        var up = MathF.Abs(forward.Dot(Vector3.Up)) > 0.99f ? Vector3.Back : Vector3.Up;
        lamp.Transform = new Transform3D(Basis.LookingAt(forward, up), at);
        return lamp;
    }

    // The joint part's meshes named after its joint socket (a bearing's turning half) go onto the body on the socket's
    // far side, where they stay as they are now, so they turn with what is attached there. Meshes named after the socket
    // plus "Flex" (a suspension's coil) stay and are skewed each frame to reach the moving side (see _Process).
    private void MoveJointMeshes(JointLink link)
    {
        var c = link.Connection;
        if (c.ASocket is not { } socket || !_partNodes.TryGetValue(c.A, out var visual) || visual.GetNodeOrNull<Node3D>("Model") is not { } model)
            return;
        var a = _bodies[link.BodyA];
        var b = _bodies[link.BodyB];
        if (Creation.DefinitionOf(c.A).Sockets?.FirstOrDefault(s => s.Id == socket) is { } definition && definition.Normal is { } normal)
        {
            var n = normal.ToGodot().Normalized();
            var axis = (definition.Joint?.Axis ?? normal).ToGodot().Normalized();
            foreach (var mesh in model.GetChildren().OfType<MeshInstance3D>().Where(m => m.Name == $"{socket}Flex"))
            {
                var box = mesh.Transform * mesh.GetAabb();
                var (start, end) = (float.MaxValue, float.MinValue);
                for (var i = 0; i < 8; i++)
                {
                    var along = box.GetEndpoint(i).Dot(n);
                    (start, end) = (Mathf.Min(start, along), Mathf.Max(end, along));
                }
                if (end - start > 0.001f)
                    _flexes.Add(new Flex(mesh, link, mesh.Transform, axis, n, start, end - start));
            }
            if (c.Type == ConnectionType.Linear && definition.Joint?.Max is { } max)
            {
                foreach (var mesh in model.GetChildren().OfType<MeshInstance3D>().Where(m => m.Name == $"{socket}Rod"))
                {
                    var box = mesh.Transform * mesh.GetAabb();
                    var (start, end) = (float.MaxValue, float.MinValue);
                    for (var i = 0; i < 8; i++)
                    {
                        var along = box.GetEndpoint(i).Dot(n);
                        (start, end) = (Mathf.Min(start, along), Mathf.Max(end, along));
                    }
                    var segment = end - start;
                    if (segment < 0.01f)
                        continue;
                    var copies = new List<MeshInstance3D> { mesh };
                    for (var i = 1; i < Mathf.CeilToInt(max / segment) + 1; i++)
                    {
                        var copy = (MeshInstance3D)mesh.Duplicate();
                        copy.Name = $"{socket}Rod{i}";
                        copy.Visible = false;
                        model.AddChild(copy);
                        copies.Add(copy);
                    }
                    _rods.Add(new Rod(copies, link, mesh.Transform, axis, segment));
                }
            }
        }
        foreach (var mesh in model.GetChildren().OfType<MeshInstance3D>().Where(m => m.Name == socket).ToList())
        {
            var placed = b.Transform.AffineInverse() * a.Transform * visual.Transform * model.Transform * mesh.Transform;
            model.RemoveChild(mesh);
            var holder = new Node3D { Name = $"Part{c.A}{socket}", Transform = placed };
            mesh.Transform = Transform3D.Identity;
            holder.AddChild(mesh);
            b.AddChild(holder);
            _visuals.Add((b, holder, placed));
        }
    }

    private static string JointName(Connection c) => $"Joint{c.A}_{c.B}";

    // Builds an aimed joint (parts map E8) as a bearing-style hinge pair: a yaw hinge from the turret part's Body to a
    // yoke, and a pitch hinge from the yoke to the Body built on the turret, both about the joint's pivot. As with
    // BuildJoint, every body is put back in A's grid frame (the joint's zero) while the hinges enter the tree; the yoke
    // then takes the yaw the two Bodies stand at, so a saved, parked or rebuilt turret keeps its aim. The two Bodies
    // never collide with each other (the turret's top sits on its own part).
    private void BuildAim(JointLink link)
    {
        var c = link.Connection;
        if (!Creation.TryGetJointFrame(c, out var frame) || frame.Aim is not { } aim)
            return;
        var a = _bodies[link.BodyA];
        var b = _bodies[link.BodyB];
        var (yaw, _) = JointMath.Aim(frame, a.Transform.ToPose(), b.Transform.ToPose());
        var pivot = aim.Pivot.ToGodot();
        var yoke = new RigidBody3D
        {
            Name = $"Yoke{c.A}_{c.B}",
            TopLevel = true,
            Transform = a.Transform,
            Mass = Mathf.Max(YokeMinMass, b.Mass * YokeMassShare),
            CollisionLayer = 0,
            CollisionMask = 0,
            Freeze = IsFrozen,
            FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic,
            // A small sphere's own inertia is tiny next to what it carries: the pitch motor would shake it.
            Inertia = Vector3.One * Mathf.Max(YokeMinMass, b.Mass) * YokeInertiaPerKg,
        };
        yoke.AddChild(new CollisionShape3D { Name = "Shape", Shape = new SphereShape3D { Radius = 0.2f }, Position = pivot });
        AddChild(yoke);
        var yawHinge = AimHinge(frame.SpinsFreely ? null : (frame.Min!.Value, frame.Max!.Value));
        yawHinge.Name = $"Joint{c.A}_{c.B}Yaw";
        yawHinge.TopLevel = true;
        yawHinge.Transform = a.Transform * LocalJointTransform(frame with { Type = ConnectionType.Rotational, Anchor = aim.Pivot });
        yawHinge.NodeA = $"../{a.Name}";
        yawHinge.NodeB = $"../{yoke.Name}";
        var pitchHinge = AimHinge((aim.PitchMin, aim.PitchMax));
        pitchHinge.Name = $"Joint{c.A}_{c.B}Pitch";
        pitchHinge.TopLevel = true;
        pitchHinge.Transform = a.Transform * LocalJointTransform(frame with { Type = ConnectionType.Rotational, Anchor = aim.Pivot, Axis = aim.PitchAxis });
        pitchHinge.NodeA = $"../{yoke.Name}";
        pitchHinge.NodeB = $"../{b.Name}";
        var actual = b.Transform;
        b.Transform = a.Transform;
        AddChild(yawHinge);
        AddChild(pitchHinge);
        b.Transform = actual;
        yoke.Transform = a.Transform * JointMath.AimPose(frame, yaw, 0f).ToTransform();
        (yoke.LinearVelocity, yoke.AngularVelocity) = (b.LinearVelocity, b.AngularVelocity);
        a.AddCollisionExceptionWith(b);
        b.AddCollisionExceptionWith(a);
        _aimJoints[(c.A, c.B)] = new AimJoint(link, frame, yoke, yawHinge, pitchHinge);
    }

    /// <summary>
    /// Which way Godot's hinge angle runs against a right-handed turn about the hinge's Z: its motor velocity and its
    /// limits both count the other way (see <see cref="Motion.MotionSync"/>).
    /// </summary>
    public const float HingeAngleSign = -1f;

    // A hinge of an aimed joint, limited to `limits` (rad, right-handed about its axis) or free.
    private static HingeJoint3D AimHinge((float Min, float Max)? limits)
    {
        var hinge = new HingeJoint3D();
        if (limits is { } l)
        {
            hinge.SetFlag(HingeJoint3D.Flag.UseLimit, true);
            hinge.SetParam(HingeJoint3D.Param.LimitLower, HingeAngleSign > 0 ? l.Min : -l.Max);
            hinge.SetParam(HingeJoint3D.Param.LimitUpper, HingeAngleSign > 0 ? l.Max : -l.Min);
        }
        return hinge;
    }

    // Builds the Jolt joint of one link: a hinge for a bearing, a 6DOF joint free along one axis for a
    // piston (with a motor) or a suspension (with a spring). Godot takes each body's joint frame from
    // where the bodies are when the joint enters the tree, so body B is put back in A's grid frame (the
    // joint's zero, where both sides were built; an inset hinge's B the inset back) for that moment. A joint
    // rebuilt while extended or turned keeps its zero, its limits and its spring's rest length.
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
        b.Transform = a.Transform.TranslatedLocal(frame.InsetOffset.ToGodot());
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
