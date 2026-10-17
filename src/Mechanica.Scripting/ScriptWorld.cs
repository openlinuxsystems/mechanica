using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Scripting;

/// <summary>What a ray from <c>world.raycast</c> hit.</summary>
/// <param name="Position">World hit point, in m.</param>
/// <param name="Normal">World surface normal at the hit.</param>
/// <param name="Distance">Distance from the ray's start, in m.</param>
/// <param name="Creation">Creation hit, or null for terrain and other objects.</param>
/// <param name="Part">Part hit, when <paramref name="Creation"/> is set and the part is known.</param>
/// <param name="Player">The player hit (their peer), when it is one.</param>
/// <param name="Bot">The bot hit (its id), when it is one.</param>
public readonly record struct ScriptRayHit(Vector3 Position, Vector3 Normal, float Distance, int? Creation, int? Part, int? Player = null, int? Bot = null);

/// <summary>One thing inside a <c>world.overlap_sphere</c> test.</summary>
/// <param name="Creation">Creation, or null for terrain and other objects.</param>
/// <param name="Part">Part, when <paramref name="Creation"/> is set and the part is known.</param>
/// <param name="Player">The player (their peer), when it is one.</param>
/// <param name="Bot">The bot (its id), when it is one.</param>
public readonly record struct ScriptOverlap(int? Creation, int? Part, int? Player = null, int? Bot = null);

/// <summary>
/// The physics queries scripts can make that the Creation graph cannot answer. The game implements it
/// over Jolt; tests use <see cref="NoPhysics"/> or a fake.
/// </summary>
public interface IScriptPhysics
{
    /// <summary>Gravity acceleration, in m/s².</summary>
    Vector3 Gravity { get; }

    /// <summary>Casts a ray, skipping the bodies of <paramref name="ignoreCreation"/>. Null when nothing is hit.</summary>
    ScriptRayHit? Raycast(Vector3 from, Vector3 to, int? ignoreCreation);

    /// <summary>Everything a sphere touches, skipping <paramref name="ignoreCreation"/>.</summary>
    IReadOnlyList<ScriptOverlap> OverlapSphere(Vector3 centre, float radius, int? ignoreCreation);
}

/// <summary>An empty world: standard gravity, and rays and spheres never hit anything.</summary>
public sealed class NoPhysics : IScriptPhysics
{
    /// <inheritdoc/>
    public Vector3 Gravity { get; init; } = new(0f, -9.81f, 0f);

    /// <inheritdoc/>
    public ScriptRayHit? Raycast(Vector3 from, Vector3 to, int? ignoreCreation) => null;

    /// <inheritdoc/>
    public IReadOnlyList<ScriptOverlap> OverlapSphere(Vector3 centre, float radius, int? ignoreCreation) => [];
}

/// <summary>Where a part is and how it moves, in world space (SI units).</summary>
/// <param name="Position">Centre of the part's footprint, in m.</param>
/// <param name="Rotation">Rotation from the part's own frame (as authored) to the world.</param>
/// <param name="Velocity">Velocity of the part's centre, in m/s (the Body's velocity plus its spin about the centre of mass).</param>
/// <param name="AngularVelocity">The Body's spin, in rad/s.</param>
/// <param name="Body">Index of the part's Body.</param>
/// <param name="BodyMass">Mass of the part's Body, in kg.</param>
public readonly record struct PartKinematics(
    Vector3 Position, Quaternion Rotation, Vector3 Velocity, Vector3 AngularVelocity, int Body, float BodyMass)
{
    /// <summary>Reads a part's state from its Creation's stored Body poses and velocities.</summary>
    public static PartKinematics Of(Creation creation, int partId)
    {
        var part = creation.FindPart(partId) ?? throw new ArgumentException($"Creation {creation.Id} has no part {partId}.", nameof(partId));
        var definition = creation.DefinitionOf(partId);
        var bodyIndex = creation.BodyIndexOf(partId);
        var body = creation.Bodies[bodyIndex];
        var pose = creation.BodyPose(bodyIndex);
        var velocity = creation.GetBodyVelocity(bodyIndex);
        var position = pose.Apply(PartGeometry.CentreMetres(definition, part.Cell, part.Rotation).ToNumerics());
        var centreOfMass = pose.Apply(body.CentreOfMass.ToNumerics());
        return new PartKinematics(
            position,
            Quaternion.Normalize(pose.Rotation * Pose.ToQuaternion(part.Rotation)),
            velocity.Linear + Vector3.Cross(velocity.Angular, position - centreOfMass),
            velocity.Angular,
            bodyIndex,
            body.Mass);
    }
}
