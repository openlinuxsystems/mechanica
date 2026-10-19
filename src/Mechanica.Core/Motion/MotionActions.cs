using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Motion;

/// <summary>Arguments of <c>ApplyForce</c>.</summary>
/// <param name="Creation">Creation to push.</param>
/// <param name="Force">World-space force in N, through the Body's centre of mass.</param>
/// <param name="Body">Index of the Body in <see cref="Creations.Creation.Bodies"/>.</param>
/// <param name="Torque">World-space torque in N·m.</param>
/// <param name="Duration">How long to keep pushing, in s. Null means one physics step. A later call on the same Body replaces it.</param>
/// <param name="At">World point the force acts at, in m. Null pushes through the centre of mass; any other point also twists the Body.</param>
/// <param name="Part">
/// The part doing the pushing (a thruster, a wing). Pushes from different parts of one Body add up, and a
/// later call from the same part replaces only that part's push. The part must be in <paramref name="Body"/>.
/// Null is one shared push per Body, as above.
/// </param>
public sealed record ApplyForceArgs(int Creation, Vec3 Force, int Body = 0, Vec3? Torque = null, float? Duration = null,
    Vec3? At = null, int? Part = null);

/// <summary>Arguments of <c>SetBearingTarget</c>.</summary>
/// <param name="Creation">Creation holding the bearing.</param>
/// <param name="Part">The bearing's part id (the part that owns the rotational joint).</param>
/// <param name="Velocity">
/// Target speed in rad/s about the bearing's axis. Without <paramref name="Angle"/>, null turns the motor
/// off so the bearing spins freely. With an angle it is the most speed the bearing turns there at (either
/// sign); null uses the default from data.
/// </param>
/// <param name="MaxTorque">Motor torque limit in N·m; null uses the default from data.</param>
/// <param name="Angle">
/// Angle in rad to turn to and hold, measured from the pose the bearing was built in (steering, doors).
/// Within ±π, and within the joint's limits when it has them. Null drives at <paramref name="Velocity"/>.
/// </param>
public sealed record SetBearingTargetArgs(int Creation, int Part, float? Velocity = null, float? MaxTorque = null, float? Angle = null);

/// <summary>Arguments of <c>SetPistonTarget</c>.</summary>
/// <param name="Creation">Creation holding the piston.</param>
/// <param name="Part">The piston's part id (the part that owns the linear joint).</param>
/// <param name="Length">
/// Extension in m to move to and hold, within the joint's <c>min</c> and <c>max</c>. Null turns the motor off
/// so the piston slides freely.
/// </param>
/// <param name="Speed">Most speed in m/s; null uses the default from data.</param>
/// <param name="MaxForce">Motor force limit in N; null uses the default from data.</param>
public sealed record SetPistonTargetArgs(int Creation, int Part, float? Length, float? Speed = null, float? MaxForce = null);

/// <summary>
/// What a joint's motor does (<see cref="IMotionWorld.SetJointMotor"/>). With no <see cref="Target"/> it
/// drives at <see cref="Velocity"/> (rad/s or m/s), or frees the joint when that is null too. With a
/// target position (rad or m) it moves there at up to |<see cref="Velocity"/>| and holds it.
/// </summary>
/// <param name="Velocity">Speed, or the most speed toward <paramref name="Target"/>.</param>
/// <param name="MaxEffort">Torque limit in N·m (hinges) or force limit in N (sliders).</param>
/// <param name="Target">Position to hold, or null.</param>
public sealed record JointMotor(float? Velocity, float MaxEffort, float? Target = null)
{
    /// <summary>True when the motor is off and the joint moves freely.</summary>
    public bool IsFree => Velocity is null && Target is null;

    /// <summary>
    /// The speed to drive at now, given where the joint is: the set speed, or for a target a speed toward
    /// it that slows down near it (<paramref name="gain"/> per s) and never exceeds the top speed.
    /// <paramref name="period"/> wraps the error for a free-spinning hinge (2π), so it turns the short way.
    /// </summary>
    public float DriveVelocity(float position, float gain, float? period = null)
    {
        if (Target is not { } target)
            return Velocity ?? 0f;
        var error = target - position;
        if (period is { } p)
            error -= p * MathF.Round(error / p);
        var top = MathF.Abs(Velocity ?? 0f);
        return Math.Clamp(error * gain, -top, top);
    }
}

/// <summary>Arguments of <c>GetCreation</c>.</summary>
/// <param name="Creation">Creation to describe.</param>
public sealed record GetCreationArgs(int Creation);

/// <summary>One Body in the value of <c>GetCreation</c>.</summary>
/// <param name="Index">Body index, as <c>ApplyForce</c> takes it.</param>
/// <param name="Parts">Member part ids.</param>
/// <param name="Mass">Mass in kg.</param>
/// <param name="Position">World position of the Body's grid-frame origin, in m.</param>
/// <param name="Rotation">World rotation as a quaternion <c>[x, y, z, w]</c>.</param>
/// <param name="Forward">World direction of the grid frame's +X axis.</param>
/// <param name="Up">World direction of the grid frame's +Y axis.</param>
/// <param name="LinearVelocity">m/s.</param>
/// <param name="AngularVelocity">rad/s.</param>
public sealed record BodyState(
    int Index, IReadOnlyList<int> Parts, float Mass, Vec3 Position, float[] Rotation, Vec3 Forward, Vec3 Up,
    Vec3 LinearVelocity, Vec3 AngularVelocity);

/// <summary>One joint in the value of <c>GetCreation</c>.</summary>
/// <param name="Part">The part that owns the joint (a bearing for <c>rotational</c>).</param>
/// <param name="Other">The part on the joint's other side.</param>
/// <param name="Type">Joint kind: <c>rotational</c>, <c>linear</c> or <c>spring</c>.</param>
/// <param name="BodyA">Body of <paramref name="Part"/>.</param>
/// <param name="BodyB">Body of <paramref name="Other"/>.</param>
/// <param name="Locked">True when both sides are in one Body, so the joint can't move.</param>
/// <param name="Position">
/// Where the joint is now: angle in rad or extension in m from the pose it was built in. Null when the
/// world can't tell (the Creation is parked).
/// </param>
/// <param name="Min">Lowest position the joint allows (rad or m); null on a free-spinning bearing.</param>
/// <param name="Max">Highest position the joint allows; null on a free-spinning bearing.</param>
public sealed record JointState(int Part, int Other, ConnectionType Type, int BodyA, int BodyB, bool Locked,
    float? Position = null, float? Min = null, float? Max = null);

/// <summary>One placed part in the value of <c>GetCreation</c>: enough to see it in the world and to rebuild it.</summary>
/// <param name="Id">Part id within the Creation.</param>
/// <param name="Part">Qualified definition id.</param>
/// <param name="Cell">Grid cell of the part's local origin cell, in the Creation's grid frame (as <c>PlacePart</c> takes it).</param>
/// <param name="Rotation">Orientation on the grid (as <c>PlacePart</c> takes it).</param>
/// <param name="Body">Index of the Body it belongs to.</param>
/// <param name="Centre">World position of the part's centre now, in m.</param>
/// <param name="Level">Upgrade level (0 is the part as written).</param>
/// <param name="Damage">Health lost to hits.</param>
/// <param name="Color">Paint colour <c>"#rrggbb"</c>, or null for the part's own.</param>
/// <param name="Health">Health left, or null when the part can't be broken by hits.</param>
/// <param name="MaxHealth">Full health, or null when the part can't be broken by hits.</param>
/// <param name="Fire">Fire strength on the part (0 to 1), or null when it isn't burning.</param>
/// <param name="Settings">Settings changed from the part's defaults, or null.</param>
public sealed record PartState(
    int Id, string Part, GridCell Cell, GridRotation Rotation, int Body, Vec3 Centre, int Level, float Damage, string? Color = null,
    float? Health = null, float? MaxHealth = null, float? Fire = null, IReadOnlyDictionary<string, JsonElement>? Settings = null);

/// <summary>Value of <c>GetCreation</c>.</summary>
/// <param name="Id">Creation id.</param>
/// <param name="AuthorityPeer">Peer that simulates it (D5).</param>
/// <param name="Simulated">False while the Creation is parked (no player near it), so motion actions are refused.</param>
/// <param name="Parts">Part ids, ascending.</param>
/// <param name="Bodies">Bodies, by index.</param>
/// <param name="Joints">Joint links.</param>
/// <param name="PartStates">Every part's type, grid cell, rotation, colour, health and settings, by id ascending.</param>
public sealed record CreationState(
    int Id, int AuthorityPeer, bool Simulated, IReadOnlyList<int> Parts, IReadOnlyList<BodyState> Bodies, IReadOnlyList<JointState> Joints,
    IReadOnlyList<PartState> PartStates);

/// <summary>Arguments of <c>GetForces</c>.</summary>
/// <param name="Creation">Creation whose pushes to read.</param>
public sealed record GetForcesArgs(int Creation);

/// <summary>
/// One push the world applied in its last physics step, as <c>GetForces</c> returns it and the aero force
/// overlay draws it.
/// </summary>
/// <param name="Part">The part that pushed (a thruster, a wing), or null for a Body's shared push.</param>
/// <param name="Body">The Body pushed.</param>
/// <param name="Force">World force in N.</param>
/// <param name="At">World point the force acts at, in m; null when it acts through the centre of mass.</param>
/// <param name="Torque">World torque in N·m on top of the force's own twist about the centre of mass (a rotor's counter-torque).</param>
public sealed record PushState(int? Part, int Body, Vec3 Force, Vec3? At, Vec3 Torque);

/// <summary>One entry in the value of <c>ListCreations</c>.</summary>
/// <param name="Id">Creation id.</param>
/// <param name="Parts">Number of parts.</param>
/// <param name="Position">World position of its first Body, in m.</param>
public sealed record CreationSummary(int Id, int Parts, Vec3 Position);

/// <summary>What the motion actions need from the world they move. The host's game layer implements it.</summary>
public interface IMotionWorld
{
    /// <summary>The Creation with this id, with its Body poses and velocities current, or null.</summary>
    Creation? FindCreation(int id);

    /// <summary>Every Creation, poses current.</summary>
    IEnumerable<Creation> AllCreations { get; }

    /// <summary>True when the Creation is live in this peer's physics world (not parked).</summary>
    bool IsLive(int id);

    /// <summary>
    /// Pushes Body <paramref name="body"/> through its centre of mass for <paramref name="duration"/> seconds
    /// (null: one step), replacing any earlier push on it from the same <paramref name="part"/> (null: the
    /// Body's shared push). <paramref name="torque"/> already holds the twist of a force off the centre of
    /// mass; <paramref name="at"/> and <paramref name="ownTorque"/> (the torque asked for on top of it) only
    /// say where the force acts, for <see cref="PushesOn"/>.
    /// </summary>
    void ApplyForce(Creation creation, int body, Vector3 force, Vector3 torque, float? duration, int? part, Vector3? at, Vector3 ownTorque);

    /// <summary>The pushes applied to a Creation in the last physics step, for <c>GetForces</c>. Default: none.</summary>
    IReadOnlyList<PushState> PushesOn(Creation creation) => [];

    /// <summary>Sets or (with a <see cref="JointMotor.IsFree"/> motor) clears the motor of a bearing or piston joint.</summary>
    void SetJointMotor(Creation creation, JointLink joint, JointMotor motor);

    /// <summary>Where a joint is now (rad or m from its built pose), or null when it is not simulated here.</summary>
    float? JointPosition(Creation creation, JointLink joint);
}

/// <summary>
/// The generic motion primitives of the Action API (D2: C# exposes forces, torques, body state and
/// joint motor targets; part behaviour such as engines and seats builds on them in Lua):
/// <list type="bullet">
/// <item><c>ApplyForce</c>: pushes a Body with a force and torque for a while.</item>
/// <item><c>SetBearingTarget</c>: drives a bearing's motor at a target speed or to an angle, or frees it.</item>
/// <item><c>SetPistonTarget</c>: moves a piston to a length and holds it, or frees it.</item>
/// <item><c>GetCreation</c>, <c>ListCreations</c> and <c>GetForces</c>: read-only queries of Creation state and of the pushes on it.</item>
/// </list>
/// Players don't call the motion actions directly: they drive through seats and controllers (chunk 10),
/// whose scripts, bots and external agents do. Only the Creation's authority peer may move it (D5);
/// in v1 that is always the host.
/// </summary>
public sealed class MotionActions(MotionSettings settings, IMotionWorld world)
{
    /// <summary>How part health derives from durability, for <see cref="PartState.Health"/>; null leaves health out.</summary>
    public SledgeSettings? Sledge { get; set; }

    /// <summary>Action name for pushing a Body.</summary>
    public const string ApplyForce = "ApplyForce";

    /// <summary>Action name for driving a bearing.</summary>
    public const string SetBearingTarget = "SetBearingTarget";

    /// <summary>Action name for driving a piston.</summary>
    public const string SetPistonTarget = "SetPistonTarget";

    /// <summary>Action name for reading one Creation.</summary>
    public const string GetCreation = "GetCreation";

    /// <summary>Action name for listing Creations.</summary>
    public const string ListCreations = "ListCreations";

    /// <summary>Action name for reading the pushes on a Creation (the aero force overlay's query).</summary>
    public const string GetForces = "GetForces";

    /// <summary>Registers all six actions.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<ApplyForceArgs>(ApplyForce, ValidateForce, ApplyForceNow);
        dispatcher.Register<SetBearingTargetArgs>(SetBearingTarget, ValidateBearing, ApplyBearing);
        dispatcher.Register<SetPistonTargetArgs>(SetPistonTarget, ValidatePiston, ApplyPiston);
        dispatcher.Register<GetCreationArgs>(GetCreation,
            (_, args) => world.FindCreation(args.Creation) is null ? $"There is no creation {args.Creation}." : null,
            (_, args) => Value(Describe(world.FindCreation(args.Creation)!)),
            readOnly: true);
        dispatcher.Register<NoArgs>(ListCreations, (_, _) => null,
            (_, _) => Value(world.AllCreations.OrderBy(c => c.Id).Select(Summarize).ToList()),
            readOnly: true);
        dispatcher.Register<GetForcesArgs>(GetForces,
            (_, args) => world.FindCreation(args.Creation) is null ? $"There is no creation {args.Creation}." : null,
            (_, args) => Value(world.PushesOn(world.FindCreation(args.Creation)!)),
            readOnly: true);
    }

    /// <summary>Describes a Creation as <c>GetCreation</c> returns it.</summary>
    public CreationState Describe(Creation creation)
    {
        var bodies = creation.Bodies.Select(b =>
        {
            var pose = creation.BodyPose(b.Index);
            var velocity = creation.GetBodyVelocity(b.Index);
            var q = pose.Rotation;
            return new BodyState(b.Index, b.PartIds, b.Mass, ToVec(pose.Position), [q.X, q.Y, q.Z, q.W],
                ToVec(Vector3.Transform(Vector3.UnitX, q)), ToVec(Vector3.Transform(Vector3.UnitY, q)),
                ToVec(velocity.Linear), ToVec(velocity.Angular));
        }).ToList();
        var joints = creation.Joints.Select(j =>
        {
            creation.TryGetJointFrame(j.Connection, out var frame);
            return new JointState(j.Connection.A, j.Connection.B, j.Connection.Type, j.BodyA, j.BodyB, j.IsLocked,
                j.IsLocked ? 0f : world.JointPosition(creation, j), frame?.Min, frame?.Max);
        }).ToList();
        var parts = creation.Parts.OrderBy(p => p.Id).Select(p =>
        {
            var body = creation.BodyIndexOf(p.Id);
            var definition = creation.DefinitionOf(p.Id);
            var centre = creation.BodyPose(body).Apply(PartGeometry.CentreMetres(definition, p.Cell, p.Rotation).ToNumerics());
            var damage = creation.PartDamage(p.Id);
            var full = Sledge?.HealthOf(definition.Durability);
            var fire = creation.FireOf(p.Id);
            return new PartState(p.Id, p.Part, p.Cell, p.Rotation, body, ToVec(centre), p.Level, damage, p.Color,
                full is { } f ? MathF.Max(0f, f - damage) : null, full, fire > 0f ? fire : null, p.Settings);
        }).ToList();
        return new CreationState(creation.Id, creation.AuthorityPeer, world.IsLive(creation.Id),
            [.. creation.Parts.Select(p => p.Id).Order()], bodies, joints, parts);
    }

    private string? ValidateForce(ActionCaller caller, ApplyForceArgs args)
    {
        if (CheckMover(caller, args.Creation, out var creation) is { } refusal)
            return refusal;
        if (args.Body < 0 || args.Body >= creation!.Bodies.Count)
            return $"Creation {args.Creation} has no body {args.Body} (it has {creation!.Bodies.Count}).";
        if (args.Part is { } part)
        {
            if (creation.FindPart(part) is null)
                return $"Creation {args.Creation} has no part {part}.";
            if (creation.BodyIndexOf(part) != args.Body)
                return $"Part {part} is in body {creation.BodyIndexOf(part)}, not body {args.Body}.";
        }
        if (args.At is { } at)
        {
            if (!float.IsFinite(at.X) || !float.IsFinite(at.Y) || !float.IsFinite(at.Z))
                return "at must be finite.";
            var lever = (at.ToNumerics() - CentreOfMass(creation, args.Body)).Length();
            if (lever > settings.MaxLeverArm)
                return $"at is {lever:0.#} m from the body's centre of mass; the most allowed is {settings.MaxLeverArm:0.#} m.";
        }
        return CheckVector(args.Force, "force", settings.MaxForce, "N")
            ?? (args.Torque is { } torque ? CheckVector(torque, "torque", settings.MaxTorque, "N·m") : null)
            ?? (args.Duration is { } d && (!float.IsFinite(d) || d <= 0f || d > settings.MaxDuration)
                ? $"duration must be more than 0 and at most {settings.MaxDuration:0.###} s."
                : null);
    }

    private JsonElement? ApplyForceNow(ActionCaller caller, ApplyForceArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        var force = args.Force.ToNumerics();
        var own = (args.Torque ?? Vec3.Zero).ToNumerics();
        var torque = own;
        // A force off the centre of mass is the same force through it plus the twist of its lever arm.
        if (args.At is { } at)
            torque += Vector3.Cross(at.ToNumerics() - CentreOfMass(creation, args.Body), force);
        world.ApplyForce(creation, args.Body, force, torque, args.Duration, args.Part, args.At?.ToNumerics(), own);
        return null;
    }

    /// <summary>World position of a Body's centre of mass at its current pose.</summary>
    public static Vector3 CentreOfMass(Creation creation, int body) =>
        creation.BodyPose(body).Apply(creation.Bodies[body].CentreOfMass.ToNumerics());

    private string? ValidateBearing(ActionCaller caller, SetBearingTargetArgs args)
    {
        if (CheckMover(caller, args.Creation, out var creation) is { } refusal)
            return refusal;
        if (creation!.FindPart(args.Part) is null)
            return $"Creation {args.Creation} has no part {args.Part}.";
        if (FindJoint(creation, args.Part, ConnectionType.Rotational) is not { } joint)
            return $"Part {args.Part} is not a bearing with something on its rotor.";
        if (joint.IsLocked)
            return $"Bearing {args.Part} is locked: both its sides are in one body.";
        if (args.Velocity is { } v && (!float.IsFinite(v) || MathF.Abs(v) > settings.MaxBearingSpeed))
            return $"velocity must be at most {settings.MaxBearingSpeed:0.###} rad/s either way.";
        if (args.MaxTorque is { } t && (!float.IsFinite(t) || t <= 0f || t > settings.MaxBearingTorque))
            return $"max_torque must be more than 0 and at most {settings.MaxBearingTorque:0.###} N·m.";
        if (args.Angle is { } angle)
        {
            if (!float.IsFinite(angle) || MathF.Abs(angle) > MathF.PI + 1e-4f)
                return "angle must be within ±π rad (±180°).";
            if (args.Velocity == 0f)
                return "velocity is the most speed toward the angle, so it can't be 0.";
            creation.TryGetJointFrame(joint.Connection, out var frame);
            if (frame is { SpinsFreely: false, Min: { } lo, Max: { } hi } && (angle < lo - 1e-4f || angle > hi + 1e-4f))
                return $"angle {angle:0.###} rad is outside bearing {args.Part}'s limits ({lo:0.###} to {hi:0.###} rad).";
        }
        return null;
    }

    private JsonElement? ApplyBearing(ActionCaller caller, SetBearingTargetArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        var torque = args.MaxTorque ?? settings.DefaultBearingTorque;
        var motor = args.Angle is { } angle
            ? new JointMotor(MathF.Abs(args.Velocity ?? settings.DefaultBearingSpeed), torque, angle)
            : new JointMotor(args.Velocity, torque);
        world.SetJointMotor(creation, FindJoint(creation, args.Part, ConnectionType.Rotational)!, motor);
        return null;
    }

    private string? ValidatePiston(ActionCaller caller, SetPistonTargetArgs args)
    {
        if (CheckMover(caller, args.Creation, out var creation) is { } refusal)
            return refusal;
        if (creation!.FindPart(args.Part) is null)
            return $"Creation {args.Creation} has no part {args.Part}.";
        if (FindJoint(creation, args.Part, ConnectionType.Linear) is not { } joint)
            return $"Part {args.Part} is not a piston with something on its head.";
        if (joint.IsLocked)
            return $"Piston {args.Part} is locked: both its sides are in one body.";
        if (args.Length is { } length)
        {
            creation.TryGetJointFrame(joint.Connection, out var frame);
            var (lo, hi) = (frame!.Min ?? 0f, frame.Max ?? 0f);
            if (!float.IsFinite(length) || length < lo - 1e-4f || length > hi + 1e-4f)
                return $"length must be from {lo:0.###} to {hi:0.###} m for piston {args.Part}.";
        }
        if (args.Speed is { } v && (!float.IsFinite(v) || v <= 0f || v > settings.MaxPistonSpeed))
            return $"speed must be more than 0 and at most {settings.MaxPistonSpeed:0.###} m/s.";
        if (args.MaxForce is { } f && (!float.IsFinite(f) || f <= 0f || f > settings.MaxPistonForce))
            return $"max_force must be more than 0 and at most {settings.MaxPistonForce:0.###} N.";
        return null;
    }

    private JsonElement? ApplyPiston(ActionCaller caller, SetPistonTargetArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        var force = args.MaxForce ?? settings.DefaultPistonForce;
        var motor = args.Length is { } length
            ? new JointMotor(args.Speed ?? settings.DefaultPistonSpeed, force, length)
            : new JointMotor(null, force);
        world.SetJointMotor(creation, FindJoint(creation, args.Part, ConnectionType.Linear)!, motor);
        return null;
    }

    private string? CheckMover(ActionCaller caller, int id, out Creation? creation)
    {
        creation = null;
        if (caller.Source == ActionSource.Player)
            return "Players drive through seats and controllers, not motion actions.";
        creation = world.FindCreation(id);
        if (creation is null)
            return $"There is no creation {id}.";
        if (creation.AuthorityPeer != Creation.HostPeer)
            return $"Creation {id} is simulated by peer {creation.AuthorityPeer}, not the host.";
        if (!world.IsLive(id))
            return $"Creation {id} is parked (nobody is near it), so it can't move.";
        return null;
    }

    private static JointLink? FindJoint(Creation creation, int part, ConnectionType type) =>
        creation.JointOf(part) is { } joint && joint.Connection.Type == type
            ? joint
            : creation.Joints.FirstOrDefault(j => j.Connection.Type == type && j.Connection.A == part);

    private static string? CheckVector(Vec3 v, string name, float max, string unit) =>
        !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)
            ? $"{name} must be finite."
            : v.Length > max ? $"{name} is {v.Length:0.#} {unit}; the most allowed is {max:0.#} {unit}." : null;

    private static CreationSummary Summarize(Creation creation) =>
        new(creation.Id, creation.Parts.Count, creation.Bodies.Count > 0 ? ToVec(creation.BodyPose(0).Position) : Vec3.Zero);

    private static Vec3 ToVec(Vector3 v) => new(v.X, v.Y, v.Z);

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}
