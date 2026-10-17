using Godot;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// The seam a weld leaves (UI spec §8): a subtle blue metallic square on each face the weld joined, fading out over
/// <see cref="Seconds"/>, then gone.
/// </summary>
public partial class WeldSeamGlow : Node3D
{
    private readonly StandardMaterial3D _material = new()
    {
        AlbedoColor = UiTokens.Blue with { A = 0.7f },
        Metallic = 0.8f,
        Roughness = 0.25f,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        EmissionEnabled = true,
        Emission = UiTokens.Blue,
        EmissionEnergyMultiplier = 0.6f,
    };

    private double _age;

    /// <summary>The faces. Set before adding to the tree.</summary>
    public WeldTool.Seam Seam { get; init; } = null!;

    /// <summary>How long it takes to fade, in seconds. Set before adding to the tree.</summary>
    public double Seconds { get; init; } = 1.5;

    /// <summary>How visible it still is, 1 to 0.</summary>
    public float Strength => Seconds <= 0 ? 0f : (float)Math.Clamp(1 - _age / Seconds, 0, 1);

    public override void _Ready()
    {
        TopLevel = true;
        var size = Core.MechanicaInfo.GridSize;
        var mesh = new PlaneMesh { Size = new Vector2(size, size) * 0.96f, Orientation = PlaneMesh.OrientationEnum.Z, Material = _material };
        foreach (var face in Seam.Faces)
        {
            var square = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(square);
            WeldTool.PlaceSquare(square, Seam.Frame, face);
        }
    }

    public override void _Process(double delta)
    {
        _age += delta;
        if (Strength <= 0)
        {
            QueueFree();
            return;
        }
        _material.AlbedoColor = UiTokens.Blue with { A = 0.7f * Strength };
        _material.EmissionEnergyMultiplier = 0.6f * Strength;
    }
}
