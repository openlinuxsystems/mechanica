using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Motion;

/// <summary>Arguments of <c>ApplyForce</c>.</summary>
/// <param name="Creation">Creation to push.</param>
/// <param name="Force">World-space force in N, through the Body's centre of mass.</param>
/// <param name="Body">Index of the Body in <see cref="Creations.Creation.Bodies"/>.</param>
/// <param name="Torque">World-space torque in N·m.</param>
/// <param name="Duration">How long to keep pushing, in s. Null means one physics step. A later call on the same Body replaces it.</param>
public sealed record ApplyForceArgs(int Creation, Vec3 Force, int Body = 0, Vec3? Torque = null, float? Duration = null);

/// <summary>Arguments of <c>SetBearingTarget</c>.</summary>
/// <param name="Creation">Creation holding the bearing.</param>
/// <param name="Part">The bearing's part id (the part that owns the rotational joint).</param>
/// <param name="Velocity">Target speed in rad/s about the bearing's rotor normal. Null turns the motor off so the bearing spins freely.</param>
/// <param name="MaxTorque">Motor torque limit in N·m; null uses the default from data.</param>
public sealed record SetBearingTargetArgs(int Creation, int Part, float? Velocity, float? MaxTorque = null);

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
public sealed record JointState(int Part, int Other, ConnectionType Type, int BodyA, int BodyB, bool Locked);

/// <summary>Value of <c>GetCreation</c>.</summary>
/// <param name="Id">Creation id.</param>
/// <param name="AuthorityPeer">Peer that simulates it (D5).</param>
/// <param name="Simulated">False while the Creation is parked (no player near it), so motion actions are refused.</param>
/// <param name="Parts">Part ids, ascending.</param>
/// <param name="Bodies">Bodies, by index.</param>
/// <param name="Joints">Joint links.</param>
public sealed record CreationState(
    int Id, int AuthorityPeer, bool Simulated, IReadOnlyList<int> Parts, IReadOnlyList<BodyState> Bodies, IReadOnlyList<JointState> Joints);

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

    /// <summary>Pushes Body <paramref name="body"/> for <paramref name="duration"/> seconds (null: one step), replacing any earlier push on it.</summary>
    void ApplyForce(Creation creation, int body, Vector3 force, Vector3 torque, float? duration);

    /// <summary>Sets or (with a null velocity) clears the motor of the bearing joint <paramref name="joint"/>.</summary>
    void SetBearingTarget(Creation creation, JointLink joint, float? velocity, float maxTorque);
}

/// <summary>
/// The generic motion primitives of the Action API (D2: C# exposes forces, torques, body state and
/// joint motor targets; part behaviour such as engines and seats builds on them in Lua):
/// <list type="bullet">
/// <item><c>ApplyForce</c>: pushes a Body with a force and torque for a while.</item>
/// <item><c>SetBearingTarget</c>: drives a bearing's motor at a target speed, or frees it.</item>
/// <item><c>GetCreation</c> and <c>ListCreations</c>: read-only queries of Creation state.</item>
/// </list>
/// Players don't call the motion actions directly: they drive through seats and controllers (chunk 10),
/// whose scripts, bots and external agents do. Only the Creation's authority peer may move it (D5);
/// in v1 that is always the host.
/// </summary>
public sealed class MotionActions(MotionSettings settings, IMotionWorld world)
{
    /// <summary>Action name for pushing a Body.</summary>
    public const string ApplyForce = "ApplyForce";

    /// <summary>Action name for driving a bearing.</summary>
    public const string SetBearingTarget = "SetBearingTarget";

    /// <summary>Action name for reading one Creation.</summary>
    public const string GetCreation = "GetCreation";

    /// <summary>Action name for listing Creations.</summary>
    public const string ListCreations = "ListCreations";

    /// <summary>Registers all four actions.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<ApplyForceArgs>(ApplyForce, ValidateForce, ApplyForceNow);
        dispatcher.Register<SetBearingTargetArgs>(SetBearingTarget, ValidateBearing, ApplyBearing);
        dispatcher.Register<GetCreationArgs>(GetCreation,
            (_, args) => world.FindCreation(args.Creation) is null ? $"There is no creation {args.Creation}." : null,
            (_, args) => Value(Describe(world.FindCreation(args.Creation)!)),
            readOnly: true);
        dispatcher.Register<NoArgs>(ListCreations, (_, _) => null,
            (_, _) => Value(world.AllCreations.OrderBy(c => c.Id).Select(Summarize).ToList()),
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
            new JointState(j.Connection.A, j.Connection.B, j.Connection.Type, j.BodyA, j.BodyB, j.IsLocked)).ToList();
        return new CreationState(creation.Id, creation.AuthorityPeer, world.IsLive(creation.Id),
            [.. creation.Parts.Select(p => p.Id).Order()], bodies, joints);
    }

    private string? ValidateForce(ActionCaller caller, ApplyForceArgs args)
    {
        if (CheckMover(caller, args.Creation, out var creation) is { } refusal)
            return refusal;
        if (args.Body < 0 || args.Body >= creation!.Bodies.Count)
            return $"Creation {args.Creation} has no body {args.Body} (it has {creation!.Bodies.Count}).";
        return CheckVector(args.Force, "force", settings.MaxForce, "N")
            ?? (args.Torque is { } torque ? CheckVector(torque, "torque", settings.MaxTorque, "N·m") : null)
            ?? (args.Duration is { } d && (!float.IsFinite(d) || d <= 0f || d > settings.MaxDuration)
                ? $"duration must be more than 0 and at most {settings.MaxDuration:0.###} s."
                : null);
    }

    private JsonElement? ApplyForceNow(ActionCaller caller, ApplyForceArgs args)
    {
        world.ApplyForce(world.FindCreation(args.Creation)!, args.Body, args.Force.ToNumerics(),
            (args.Torque ?? Vec3.Zero).ToNumerics(), args.Duration);
        return null;
    }

    private string? ValidateBearing(ActionCaller caller, SetBearingTargetArgs args)
    {
        if (CheckMover(caller, args.Creation, out var creation) is { } refusal)
            return refusal;
        if (creation!.FindPart(args.Part) is null)
            return $"Creation {args.Creation} has no part {args.Part}.";
        if (FindBearing(creation, args.Part) is not { } joint)
            return $"Part {args.Part} is not a bearing with something on its rotor.";
        if (joint.IsLocked)
            return $"Bearing {args.Part} is locked: both its sides are in one body.";
        if (args.Velocity is { } v && (!float.IsFinite(v) || MathF.Abs(v) > settings.MaxBearingSpeed))
            return $"velocity must be at most {settings.MaxBearingSpeed:0.###} rad/s either way.";
        if (args.MaxTorque is { } t && (!float.IsFinite(t) || t <= 0f || t > settings.MaxBearingTorque))
            return $"max_torque must be more than 0 and at most {settings.MaxBearingTorque:0.###} N·m.";
        return null;
    }

    private JsonElement? ApplyBearing(ActionCaller caller, SetBearingTargetArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        world.SetBearingTarget(creation, FindBearing(creation, args.Part)!, args.Velocity,
            args.MaxTorque ?? settings.DefaultBearingTorque);
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

    private static JointLink? FindBearing(Creation creation, int part) =>
        creation.Joints.FirstOrDefault(j => j.Connection.Type == ConnectionType.Rotational && j.Connection.A == part);

    private static string? CheckVector(Vec3 v, string name, float max, string unit) =>
        !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)
            ? $"{name} must be finite."
            : v.Length > max ? $"{name} is {v.Length:0.#} {unit}; the most allowed is {max:0.#} {unit}." : null;

    private static CreationSummary Summarize(Creation creation) =>
        new(creation.Id, creation.Parts.Count, creation.Bodies.Count > 0 ? ToVec(creation.BodyPose(0).Position) : Vec3.Zero);

    private static Vec3 ToVec(Vector3 v) => new(v.X, v.Y, v.Z);

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}
