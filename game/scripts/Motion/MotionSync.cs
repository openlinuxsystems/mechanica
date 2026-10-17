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
/// <c>GetCreation</c> and <c>ListCreations</c>. Pushes and bearing motors are kept here and applied to
/// the live Jolt bodies and hinges every physics step, so they survive a Creation being rebuilt
/// (a bearing keeps its motor; a push ends when the graph changes, since Body indices may shift).
/// Clients see the motion through <see cref="CreationSync"/> snapshots.
/// </summary>
public partial class MotionSync : Node, IMotionWorld
{
    /// <summary>Where the motion limits are read from.</summary>
    public const string SettingsPath = "res://data/motion.json";

    private readonly Dictionary<(int Creation, int Body), Push> _pushes = [];
    private readonly Dictionary<(int Creation, int Part), Motor> _motors = [];

    private sealed record Push(int Revision, Vector3 Force, Vector3 Torque)
    {
        public int StepsLeft { get; set; }
    }

    private sealed record Motor(Connection Joint, float Velocity, float MaxTorque)
    {
        public HingeJoint3D? Applied { get; set; }
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
        Creations.NodeOf(id)?.SyncToCreation();
        return Creations.Find(id);
    }

    /// <inheritdoc/>
    public IEnumerable<Creation> AllCreations => Creations.All.Select(c => FindCreation(c.Id)!).ToList();

    /// <inheritdoc/>
    public bool IsLive(int id) => Creations.NodeOf(id) is not null;

    /// <inheritdoc/>
    public void ApplyForce(Creation creation, int body, System.Numerics.Vector3 force, System.Numerics.Vector3 torque, float? duration)
    {
        var steps = duration is { } d ? Math.Max(1, (int)MathF.Round(d * Engine.PhysicsTicksPerSecond)) : 1;
        _pushes[(creation.Id, body)] = new Push(creation.Revision, force.ToGodot(), torque.ToGodot()) { StepsLeft = steps };
    }

    /// <inheritdoc/>
    public void SetBearingTarget(Creation creation, JointLink joint, float? velocity, float maxTorque)
    {
        var key = (creation.Id, joint.Connection.A);
        if (_motors.Remove(key, out var old) && old.Applied is { } hinge && IsInstanceValid(hinge))
            hinge.SetFlag(HingeJoint3D.Flag.EnableMotor, false);
        if (velocity is { } v)
            _motors[key] = new Motor(joint.Connection, v, maxTorque);
        // A freed bearing should start turning (or stop) now, not when something else wakes it.
        if (Creations.NodeOf(creation.Id) is { } node)
            Wake(node, joint.BodyA, joint.BodyB);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Session.IsHost)
        {
            _pushes.Clear();
            _motors.Clear();
            return;
        }
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
            if (Creations.NodeOf(key.Creation) is not { } node || node.JointOf(motor.Joint) is not HingeJoint3D hinge)
                continue;
            if (motor.Applied != hinge)
            {
                hinge.SetFlag(HingeJoint3D.Flag.EnableMotor, true);
                hinge.SetParam(HingeJoint3D.Param.MotorTargetVelocity, motor.Velocity);
                // Godot's hinge takes an impulse per step; Jolt turns it back into a torque limit.
                hinge.SetParam(HingeJoint3D.Param.MotorMaxImpulse, motor.MaxTorque * (float)delta);
                motor.Applied = hinge;
            }
            if (motor.Velocity != 0f)
            {
                var link = creation.Joints.FirstOrDefault(j => j.Connection == motor.Joint);
                if (link is not null)
                    Wake(node, link.BodyA, link.BodyB);
            }
        }
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
        foreach (var key in _pushes.Keys.Where(k => k.Creation == creation).ToList())
            _pushes.Remove(key);
        foreach (var key in _motors.Keys.Where(k => k.Creation == creation).ToList())
            _motors.Remove(key);
    }
}
