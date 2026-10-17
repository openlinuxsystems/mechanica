using Godot;
using FileAccess = Godot.FileAccess;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Motion;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;

namespace Mechanica.Game.Motion;

/// <summary>
/// The host's <see cref="IMotionWorld"/> behind <c>ApplyForce</c>, <c>SetBearingTarget</c>,
/// <c>SetPistonTarget</c>, <c>GetCreation</c> and <c>ListCreations</c>. Pushes and joint motors are kept here and applied to
/// the live Jolt bodies and joints every physics step, so they survive a Creation being rebuilt
/// (a bearing or piston keeps its motor, and a target angle or length is steered toward each step; a push ends when the graph changes, since Body indices may shift).
/// Clients see the motion through <see cref="CreationSync"/> snapshots.
/// </summary>
public partial class MotionSync : Node, IMotionWorld
{
    /// <summary>Where the motion limits are read from.</summary>
    public const string SettingsPath = "res://data/motion.json";

    // Godot hinge motors turn B about the hinge's Z the other way from a right-handed angle.
    private const float HingeMotorSign = -1f;

    // Part -1 is a Body's shared push (calls without a part).
    private readonly Dictionary<(int Creation, int Body, int Part), Push> _pushes = [];
    private readonly Dictionary<int, (ulong Frame, int Revision)> _synced = [];
    private readonly Dictionary<(int Creation, int Part), Motor> _motors = [];

    private readonly List<(int Creation, PushState Push)> _applied = [];

    private sealed record Push(int Revision, Vector3 Force, Vector3 Torque, PushState Shown)
    {
        public int StepsLeft { get; set; }
    }

    private sealed record Motor(Connection Joint, JointMotor Drive)
    {
        public Joint3D? Applied { get; set; }
        public float AppliedVelocity { get; set; } = float.NaN;
    }

    /// <summary>Session, to act only on the host. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The Creations to move. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Motion limits; null loads <see cref="SettingsPath"/>.</summary>
    public MotionSettings? Settings { get; set; }

    /// <summary>The actions, for describing Creations outside the dispatcher.</summary>
    public MotionActions Actions { get; private set; } = null!;

    public override void _Ready() => Creations.Removed += Forget;

    public override void _ExitTree() => Creations.Removed -= Forget;

    /// <summary>Registers the motion actions.</summary>
    public void RegisterActions(ActionDispatcher dispatcher)
    {
        Settings ??= LoadSettings();
        Actions = new MotionActions(Settings, this);
        Actions.Register(dispatcher);
    }

    /// <summary>Reads <see cref="SettingsPath"/>, failing loudly on bad data.</summary>
    public static MotionSettings LoadSettings()
    {
        using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {SettingsPath}: {FileAccess.GetOpenError()}");
        return MotionSettings.FromJson(file.GetAsText());
    }

    /// <inheritdoc/>
    public Creation? FindCreation(int id)
    {
        // Bodies only move in the physics step, so one read per step serves every call in it (scripts
        // push many times a step).
        if (Creations.NodeOf(id) is { } node)
        {
            var stamp = (Engine.GetPhysicsFrames(), node.Creation.Revision);
            if (_synced.GetValueOrDefault(id) != stamp)
            {
                node.SyncToCreation();
                _synced[id] = stamp;
            }
        }
        return Creations.Find(id);
    }

    /// <inheritdoc/>
    public IEnumerable<Creation> AllCreations => Creations.All.Select(c => FindCreation(c.Id)!).ToList();

    /// <inheritdoc/>
    public bool IsLive(int id) => Creations.NodeOf(id) is not null;

    /// <inheritdoc/>
    public void ApplyForce(Creation creation, int body, System.Numerics.Vector3 force, System.Numerics.Vector3 torque, float? duration, int? part,
        System.Numerics.Vector3? at, System.Numerics.Vector3 ownTorque)
    {
        var steps = duration is { } d ? Math.Max(1, (int)MathF.Round(d * Engine.PhysicsTicksPerSecond)) : 1;
        var shown = new PushState(part, body, Vec(force), at is { } a ? Vec(a) : null, Vec(ownTorque));
        _pushes[(creation.Id, body, part ?? -1)] = new Push(creation.Revision, force.ToGodot(), torque.ToGodot(), shown) { StepsLeft = steps };
    }

    /// <inheritdoc/>
    public IReadOnlyList<PushState> PushesOn(Creation creation) =>
        [.. _applied.Where(a => a.Creation == creation.Id).Select(a => a.Push)];

    /// <inheritdoc/>
    public void SetJointMotor(Creation creation, JointLink joint, JointMotor motor)
    {
        var key = (creation.Id, joint.Connection.A);
        if (_motors.Remove(key, out var old) && old.Applied is { } applied && IsInstanceValid(applied))
            EnableMotor(applied, false);
        if (!motor.IsFree)
            _motors[key] = new Motor(joint.Connection, motor);
        // A freed joint should start moving (or stop) now, not when something else wakes it.
        if (Creations.NodeOf(creation.Id) is { } node)
            Wake(node, joint.BodyA, joint.BodyB);
    }

    /// <inheritdoc/>
    public float? JointPosition(Creation creation, JointLink joint) => Creations.NodeOf(creation.Id)?.JointPosition(joint);

    public override void _PhysicsProcess(double delta)
    {
        if (!Session.IsHost)
        {
            _pushes.Clear();
            _motors.Clear();
            _applied.Clear();
            return;
        }
        _applied.Clear();
        foreach (var (key, push) in _pushes.ToList())
        {
            if (Creations.NodeOf(key.Creation) is not { } node || node.Creation.Revision != push.Revision || key.Body >= node.Bodies.Count)
            {
                _pushes.Remove(key);
                continue;
            }
            var body = node.Bodies[key.Body];
            body.Sleeping = false;
            body.ApplyCentralForce(push.Force);
            body.ApplyTorque(push.Torque);
            _applied.Add((key.Creation, push.Shown));
            if (--push.StepsLeft <= 0)
                _pushes.Remove(key);
        }
        foreach (var (key, motor) in _motors.ToList())
        {
            if (Creations.Find(key.Creation) is not { } creation || creation.FindPart(key.Part) is null)
            {
                _motors.Remove(key);
                continue;
            }
            // Parked: keep the target for when it comes back.
            // The motor belongs to the part, so it carries over to whatever the part's joint holds now.
            if (Creations.NodeOf(key.Creation) is not { } node
                || creation.Joints.FirstOrDefault(j => j.Connection.A == key.Part && j.Connection.Type == motor.Joint.Type) is not { } link
                || node.JointOf(link.Connection) is not { } joint)
                continue;
            var frame = node.FrameOf(joint);
            var velocity = motor.Drive.Target is null
                ? motor.Drive.Velocity ?? 0f
                : motor.Drive.DriveVelocity(node.JointPosition(link) ?? 0f, Settings!.ServoGain, frame.SpinsFreely ? MathF.Tau : null);
            if (motor.Applied != joint)
            {
                EnableMotor(joint, true);
                // Godot's hinge takes an impulse per step; Jolt turns it back into a torque limit.
                if (joint is HingeJoint3D hinge)
                    hinge.SetParam(HingeJoint3D.Param.MotorMaxImpulse, motor.Drive.MaxEffort * (float)delta);
                else if (joint is Generic6DofJoint3D slider)
                    slider.SetParamX(Generic6DofJoint3D.Param.LinearMotorForceLimit, motor.Drive.MaxEffort);
                motor.Applied = joint;
                motor.AppliedVelocity = float.NaN;
            }
            if (velocity != motor.AppliedVelocity)
            {
                if (joint is HingeJoint3D hinge)
                    hinge.SetParam(HingeJoint3D.Param.MotorTargetVelocity, velocity * HingeMotorSign);
                else if (joint is Generic6DofJoint3D slider)
                    slider.SetParamX(Generic6DofJoint3D.Param.LinearMotorTargetVelocity, velocity);
                motor.AppliedVelocity = velocity;
            }
            // A motor holding a target keeps its bodies awake so the hold reacts to loads.
            if (velocity != 0f || motor.Drive.Target is not null)
                Wake(node, link.BodyA, link.BodyB);
        }
    }

    private static Core.Data.Vec3 Vec(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static void EnableMotor(Joint3D joint, bool on)
    {
        if (joint is HingeJoint3D hinge)
            hinge.SetFlag(HingeJoint3D.Flag.EnableMotor, on);
        else if (joint is Generic6DofJoint3D slider)
            slider.SetFlagX(Generic6DofJoint3D.Flag.EnableLinearMotor, on);
    }

    private static void Wake(CreationNode node, int a, int b)
    {
        foreach (var index in new[] { a, b })
        {
            if (index < node.Bodies.Count)
                node.Bodies[index].Sleeping = false;
        }
    }

    private void Forget(int creation)
    {
        _synced.Remove(creation);
        foreach (var key in _pushes.Keys.Where(k => k.Creation == creation).ToList())
            _pushes.Remove(key);
        foreach (var key in _motors.Keys.Where(k => k.Creation == creation).ToList())
            _motors.Remove(key);
    }
}
