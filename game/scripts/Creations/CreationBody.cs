using Godot;

namespace Mechanica.Game.Creations;

/// <summary>
/// The Jolt rigid body of one Core <see cref="Mechanica.Core.Creations.Body"/>: a compound of every
/// member part's collision shape, with the Body's summed mass and centre of mass (D7). Its local frame
/// is the Creation's grid frame, so its transform is the Body's <see cref="Mechanica.Core.Creations.Pose"/>.
/// </summary>
public partial class CreationBody : RigidBody3D
{
    /// <summary>Index into <see cref="Mechanica.Core.Creations.Creation.Bodies"/> when this node was built.</summary>
    public int BodyIndex { get; init; }

    /// <summary>Member part ids, ascending.</summary>
    public IReadOnlyList<int> PartIds { get; init; } = [];
}
