using Godot;

namespace Mechanica.Game.Survival;

/// <summary>
/// Thrown water as clients draw it (<see cref="Core.Survival.ProjectileView.Water"/>): a wobbling slosh of water trailing
/// drops along its arc, and a splash where it lands that covers the ground it reaches (the projectile's
/// <see cref="Core.Survival.ProjectileView.Size"/>, the bucket's splash radius): a crown of water thrown up, a sheet
/// sprayed out low across the ground, a drifting mist and a dark wet patch that dries off. Looks only; the host decides
/// what the water does.
/// </summary>
public static class WaterEffects
{
    /// <summary>The splash radius drawn when the projectile gives none, in metres.</summary>
    public const float DefaultRadius = 1.2f;

    private static readonly Color WaterColour = new(0.42f, 0.66f, 0.78f, 0.75f);
    // Thrown chemical (ProjectileView.Chemical, a Chemical Bucket): the same splash in the pools' glowing green.
    private static readonly Color ChemicalColour = new(0.38f, 0.82f, 0.18f, 0.85f);
    private static StandardMaterial3D? _material;
    private static StandardMaterial3D? _chemicalMaterial;
    private static StandardMaterial3D? _mistMaterial;
    private static GradientTexture2D? _wetTexture;

    private static StandardMaterial3D Material => _material ??= new StandardMaterial3D
    {
        AlbedoColor = WaterColour,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.08f,
        Metallic = 0.1f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        VertexColorUseAsAlbedo = true,
    };

    private static StandardMaterial3D ChemicalMaterial => _chemicalMaterial ??= new StandardMaterial3D
    {
        AlbedoColor = ChemicalColour,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.12f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        VertexColorUseAsAlbedo = true,
        EmissionEnabled = true,
        Emission = ChemicalColour,
        EmissionEnergyMultiplier = 0.4f,
    };

    // Soft round puffs, facing the camera, their colour (and fade) from the particles.
    private static StandardMaterial3D MistMaterial => _mistMaterial ??= new StandardMaterial3D
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        VertexColorUseAsAlbedo = true,
        AlbedoTexture = new GradientTexture2D
        {
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(0.5f, 0f),
            Gradient = new Gradient { Colors = [new Color(1, 1, 1, 0.6f), new Color(1, 1, 1, 0f)], Offsets = [0f, 1f] },
            Width = 64,
            Height = 64,
        },
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    // The wet patch: dark in the middle, fading out to a ragged edge.
    private static GradientTexture2D WetTexture => _wetTexture ??= new GradientTexture2D
    {
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(0.5f, 0f),
        Gradient = new Gradient
        {
            Colors = [new Color(0.16f, 0.2f, 0.24f, 0.62f), new Color(0.16f, 0.2f, 0.24f, 0.5f), new Color(0.16f, 0.2f, 0.24f, 0f)],
            Offsets = [0f, 0.7f, 1f],
        },
        Width = 128,
        Height = 128,
    };

    /// <summary>
    /// The flying water: a slosh about as wide as the splash is big, with drops streaming off behind it; green for
    /// <paramref name="chemical"/>.
    /// </summary>
    public static Node3D Blob(float radius = DefaultRadius, bool chemical = false)
    {
        var material = Look(chemical).Material;
        var scale = Mathf.Clamp(radius / DefaultRadius, 0.6f, 3f);
        var blob = new Node3D { Name = "Water" };
        // A few overlapping lumps that wobble round each other read as loose water rather than a ball.
        for (var i = 0; i < 3; i++)
        {
            var lump = new MeshInstance3D
            {
                Name = $"Lump{i}",
                Mesh = new SphereMesh { Radius = 0.12f * scale, Height = 0.18f * scale, RadialSegments = 12, Rings = 6 },
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(Mathf.Cos(i * 2.1f), Mathf.Sin(i * 1.7f) * 0.5f, Mathf.Sin(i * 2.1f)) * 0.07f * scale,
            };
            blob.AddChild(lump);
        }
        blob.AddChild(Drops("Trail", amount: (int)(50 * scale), lifetime: 0.5, burst: false, speed: (0.2f, 1.2f), spread: 180f, size: 0.035f, chemical));
        blob.AddChild(new WaterWobble { Name = "Wobble" });
        return blob;
    }

    /// <summary>
    /// Bursts a splash of <paramref name="radius"/> metres at <paramref name="at"/> under <paramref name="parent"/>; every piece
    /// frees itself when done. <paramref name="chemical"/> splashes green.
    /// </summary>
    public static void Splash(Node parent, Vector3 at, float radius = DefaultRadius, bool chemical = false)
    {
        radius = Mathf.Clamp(radius, 0.3f, 10f);
        var reach = Mathf.Sqrt(radius / DefaultRadius);
        // The crown: thrown up high in the middle and falling back.
        Burst(parent, at, Drops("Crown", amount: (int)(90 + 50 * radius), lifetime: 1.2, burst: true, speed: (3f * reach, 6.5f * reach),
            spread: 35f, size: 0.06f, chemical));
        // The sheet: sprayed out low and fast across the ground to the edge of the radius.
        var sheet = Drops("Sheet", amount: (int)(140 + 80 * radius), lifetime: 0.9, burst: true, speed: (1.5f, 3f), spread: 80f, size: 0.05f, chemical);
        sheet.EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere;
        sheet.EmissionSphereRadius = 0.25f;
        sheet.RadialAccelMin = 4.5f * radius;
        sheet.RadialAccelMax = 7f * radius;
        sheet.DampingMin = sheet.DampingMax = 1.5f;
        Burst(parent, at + Vector3.Up * 0.05f, sheet);
        // Droplets landing all over the patch a beat later.
        var rain = Drops("Rain", amount: (int)(40 * radius), lifetime: 0.7, burst: true, speed: (0.5f, 1.5f), spread: 30f, size: 0.04f, chemical);
        rain.EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere;
        rain.EmissionSphereRadius = radius * 0.8f;
        rain.Explosiveness = 0.5f;
        Burst(parent, at + Vector3.Up * 0.6f, rain, delay: 0.15);
        // The mist: soft puffs drifting up and out, fading.
        var mist = new CpuParticles3D
        {
            Name = "Mist",
            Amount = (int)(14 + 10 * radius),
            Lifetime = 1.4,
            Explosiveness = 0.9f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = radius * 0.5f,
            Direction = Vector3.Up,
            Spread = 60f,
            InitialVelocityMin = 0.4f,
            InitialVelocityMax = 1.2f,
            Gravity = new Vector3(0, 0.15f, 0),
            DampingMin = 0.8f,
            DampingMax = 1.2f,
            ScaleAmountMin = 0.6f * reach,
            ScaleAmountMax = 1.3f * reach,
            ScaleAmountCurve = Grow(),
            ColorRamp = new Gradient { Colors = [new Color(0.85f, 0.92f, 1f, 0.55f), new Color(0.85f, 0.92f, 1f, 0f)], Offsets = [0f, 1f] },
            Mesh = new QuadMesh { Size = new Vector2(1.1f, 1.1f) },
            MaterialOverride = MistMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Burst(parent, at + Vector3.Up * 0.2f, mist);
        // The wet patch, soaked into whatever it landed on, drying off.
        var wet = new Decal
        {
            Name = "Wet",
            Size = new Vector3(radius * 2.2f, 1.2f, radius * 2.2f),
            TextureAlbedo = WetTexture,
            Modulate = Colors.White,
            AlbedoMix = 1f,
            UpperFade = 0.3f,
            LowerFade = 0.3f,
            Position = at,
            RotationDegrees = new Vector3(0, GD.Randf() * 360f, 0),
        };
        parent.AddChild(wet);
        var dry = wet.CreateTween();
        dry.TweenInterval(2.0);
        dry.TweenProperty(wet, Decal.PropertyName.Modulate.ToString(), new Color(1, 1, 1, 0), 3.0);
        dry.TweenCallback(Callable.From(wet.QueueFree));
    }

    // A puff swelling as it drifts, from half its size to one and a half.
    private static Curve Grow()
    {
        var curve = new Curve { MaxValue = 1.5f };
        curve.AddPoint(new Vector2(0, 0.5f));
        curve.AddPoint(new Vector2(1, 1.5f));
        return curve;
    }

    private static void Burst(Node parent, Vector3 at, CpuParticles3D particles, double delay = 0)
    {
        particles.Position = at;
        particles.OneShot = true;
        particles.Emitting = delay <= 0;
        parent.AddChild(particles);
        particles.Finished += particles.QueueFree;
        if (delay > 0)
            particles.GetTree().CreateTimer(delay).Timeout += () => { if (GodotObject.IsInstanceValid(particles)) particles.Emitting = true; };
    }

    private static (StandardMaterial3D Material, Color Colour) Look(bool chemical) =>
        chemical ? (ChemicalMaterial, ChemicalColour) : (Material, WaterColour);

    private static CpuParticles3D Drops(string name, int amount, double lifetime, bool burst, (float Min, float Max) speed, float spread, float size,
        bool chemical) => new()
    {
        Name = name,
        Amount = Mathf.Max(1, amount),
        Lifetime = lifetime,
        Explosiveness = burst ? 0.95f : 0f,
        Direction = Vector3.Up,
        Spread = spread,
        InitialVelocityMin = speed.Min,
        InitialVelocityMax = speed.Max,
        Gravity = new Vector3(0, -9.8f, 0),
        ScaleAmountMin = 0.5f,
        ScaleAmountMax = 1.3f,
        Color = Look(chemical).Colour,
        Mesh = new SphereMesh { Radius = size, Height = size * 2f, RadialSegments = 6, Rings = 3 },
        MaterialOverride = Look(chemical).Material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
