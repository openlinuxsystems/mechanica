using Godot;

namespace Mechanica.Game.Survival;

/// <summary>
/// Thrown water as clients draw it (<see cref="Core.Survival.ProjectileView.Water"/>): a wobbling blob trailing drops
/// along its arc, and a splash where it lands. Looks only; the host decides what the water does.
/// </summary>
public static class WaterEffects
{
    private static StandardMaterial3D? _material;

    private static StandardMaterial3D Material => _material ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.42f, 0.66f, 0.78f, 0.75f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.08f,
        Metallic = 0.1f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>The flying water: a blob about the bucket's width with drops streaming off behind it.</summary>
    public static MeshInstance3D Blob()
    {
        var blob = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.13f, Height = 0.2f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = Material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        blob.AddChild(Drops("Trail", amount: 40, lifetime: 0.45, burst: false, speed: (0.2f, 0.9f), spread: 180f, size: 0.03f));
        return blob;
    }

    /// <summary>Bursts a splash at <paramref name="at"/> under <paramref name="parent"/>; it frees itself when done.</summary>
    public static void Splash(Node parent, Vector3 at)
    {
        var splash = Drops("Splash", amount: 70, lifetime: 0.8, burst: true, speed: (1.5f, 4f), spread: 70f, size: 0.035f);
        splash.Position = at;
        splash.OneShot = true;
        splash.Emitting = true;
        parent.AddChild(splash);
        splash.Finished += splash.QueueFree;
    }

    private static CpuParticles3D Drops(string name, int amount, double lifetime, bool burst, (float Min, float Max) speed, float spread, float size) => new()
    {
        Name = name,
        Amount = amount,
        Lifetime = lifetime,
        Explosiveness = burst ? 0.95f : 0f,
        Direction = Vector3.Up,
        Spread = spread,
        InitialVelocityMin = speed.Min,
        InitialVelocityMax = speed.Max,
        Gravity = new Vector3(0, -9.8f, 0),
        ScaleAmountMin = 0.5f,
        ScaleAmountMax = 1.2f,
        Mesh = new SphereMesh { Radius = size, Height = size * 2f, RadialSegments = 6, Rings = 3 },
        MaterialOverride = Material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
