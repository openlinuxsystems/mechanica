using Godot;
using Mechanica.Core.Building;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// A player's Lift in the world: a solid column from the ground up to the platform top, which players
/// can stand on and which a held Creation rests on. <see cref="Show"/> moves it to a <see cref="LiftState"/>.
/// </summary>
public partial class LiftBody : StaticBody3D
{
    private static readonly StandardMaterial3D Material = new() { AlbedoColor = UiTokens.Blue };

    private readonly BoxShape3D _shape = new();
    private readonly BoxMesh _mesh = new() { Material = Material };

    /// <summary>The Lift's state as last shown.</summary>
    public LiftState State { get; private set; } = null!;

    public override void _Ready()
    {
        AddChild(new CollisionShape3D { Name = "Shape", Shape = _shape });
        AddChild(new MeshInstance3D { Name = "Mesh", Mesh = _mesh });
    }

    /// <summary>Sizes and places the column for <paramref name="state"/>.</summary>
    public void Show(LiftState state, LiftSettings settings)
    {
        State = state;
        var height = settings.TopAt(state.Level);
        var size = new Vector3(settings.PlatformSize, height, settings.PlatformSize);
        _shape.Size = size;
        _mesh.Size = size;
        Position = state.Position.ToGodot() + new Vector3(0, height / 2, 0);
    }
}
