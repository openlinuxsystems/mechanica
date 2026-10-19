using Godot;
using Mechanica.Core.Bots;

namespace Mechanica.Game.Bots;

/// <summary>
/// A bot model's effect (<see cref="BotEffects"/>), shown on every peer where the bot used an attack or died, as big as
/// the attack's radius. It frees itself when it has played out.
/// </summary>
public partial class BotEffect : Node3D
{
    // Seconds before the node frees itself.
    private const float ExplosionSeconds = 4.6f;
    private const float ShockwaveSeconds = 1.4f;

    // Seconds the fireball and the shockwave rings take to reach full size.
    private const float FireballGrow = 0.22f;
    private const float RingGrow = 0.45f;

    private static readonly Color FireCore = new(1f, 0.95f, 0.6f);
    private static readonly Color Fire = new(1f, 0.55f, 0.12f);
    private static readonly Color Ember = new(0.75f, 0.16f, 0.04f);
    private static readonly Color Smoke = new(0.16f, 0.15f, 0.14f, 0.7f);
    private static readonly Color Spark = new(1f, 0.85f, 0.45f);
    private static readonly Color Scrap = new(0.22f, 0.21f, 0.2f);
    private static readonly Color Dust = new(0.6f, 0.53f, 0.42f, 0.6f);
    private static readonly Color Wave = new(1f, 0.95f, 0.8f, 0.95f);

    // A soft round puff, so flames, smoke and dust are not squares.
    private static readonly GradientTexture2D Puff = new()
    {
        Width = 64,
        Height = 64,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(1f, 0.5f),
        Gradient = new Gradient { Offsets = [0f, 0.5f, 1f], Colors = [Colors.White, new Color(1, 1, 1, 0.55f), new Color(1, 1, 1, 0f)] },
    };

    private float _age;
    private float _seconds;
    private MeshInstance3D? _fireball;
    private StandardMaterial3D? _fireballMaterial;
    private OmniLight3D? _flash;
    private readonly List<(MeshInstance3D Ring, StandardMaterial3D Material, float Delay)> _rings = [];

    /// <summary>The effect kind (<see cref="BotEffects.Kinds"/>).</summary>
    public string Kind { get; init; } = BotEffects.Explosion;

    /// <summary>How far it reaches, in m.</summary>
    public float Radius { get; init; } = 2f;

    /// <summary>Makes an effect at <paramref name="at"/> (the bot's feet) under <paramref name="parent"/>.</summary>
    public static BotEffect Show(Node parent, string kind, Vector3 at, float radius)
    {
        var effect = new BotEffect { Name = "BotEffect", Kind = kind, Radius = Mathf.Max(radius, 0.5f) };
        parent.AddChild(effect);
        effect.GlobalPosition = at;
        return effect;
    }

    public override void _Ready()
    {
        if (Kind == BotEffects.Shockwave)
            BuildShockwave();
        else
            BuildExplosion();
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= _seconds)
        {
            QueueFree();
            return;
        }
        if (_fireball is not null)
        {
            var grow = Ease(_age / FireballGrow);
            _fireball.Scale = Vector3.One * Mathf.Lerp(0.15f, Mathf.Clamp(Radius / 4f, 0.4f, 2f) * 1.5f, grow);
            var fade = Mathf.Clamp((_age - FireballGrow * 0.6f) / 0.5f, 0f, 1f);
            _fireballMaterial!.AlbedoColor = FireCore.Lerp(Ember, fade) with { A = 0.75f * (1f - fade) };
        }
        if (_flash is not null)
            _flash.LightEnergy = Mathf.Max(0f, 10f * (1f - _age / 0.45f));
        foreach (var (ring, material, delay) in _rings)
        {
            var t = (_age - delay) / RingGrow;
            ring.Visible = t > 0;
            if (t <= 0)
                continue;
            ring.Scale = new Vector3(1, 1, 1) * Mathf.Lerp(0.2f, Radius, Ease(Mathf.Min(t, 1f)));
            material.AlbedoColor = Wave with { A = Wave.A * Mathf.Clamp(1.4f - t, 0f, 1f) };
        }
    }

    // Fast at first, settling at the end.
    private static float Ease(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp(t, 0f, 1f), 3f);

    // ---- Explosion ----

    private void BuildExplosion()
    {
        _seconds = ExplosionSeconds;
        // Tuned for a 4 m blast; the fire fills about the inner half of the radius, where it hurts most.
        var k = Mathf.Clamp(Radius / 4f, 0.4f, 2f);
        var centre = new Vector3(0, 0.5f * k, 0);
        _fireballMaterial = Glow(FireCore, transparent: true, add: true);
        _fireball = new MeshInstance3D
        {
            Name = "Fireball",
            Position = centre,
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8, Material = _fireballMaterial },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_fireball);
        _flash = new OmniLight3D { Name = "Flash", Position = centre + Vector3.Up * 0.5f, LightColor = Fire, OmniRange = Radius * 3f, LightEnergy = 10f };
        AddChild(_flash);
        // Flames burst out and rise, burning from yellow through orange to deep red.
        AddChild(Particles("Fire", 80, 1.1f, Quad(0.8f * k), new Vector2(2f, 5f) * k, gravity: 3f, spread: 180f,
            ramp: [FireCore, Fire, Ember, Ember with { A = 0f }], glow: true, add: true, grow: (0.5f, 1.4f), at: centre, emitRadius: 0.3f * k,
            damping: 4f));
        // Smoke rolls up after it and hangs.
        AddChild(Particles("Smoke", 50, 3.5f, Quad(1.1f * k), new Vector2(1f, 2.5f) * k, gravity: 0.8f, spread: 60f,
            ramp: [Smoke with { A = 0f }, Smoke, Smoke, Smoke with { A = 0.4f }, Smoke with { A = 0f }], grow: (0.5f, 2.4f), at: centre,
            emitRadius: 0.4f * k, delay: 0.1f, damping: 1.5f));
        // Sparks and scrap fly out and fall.
        AddChild(Particles("Sparks", 60, 0.9f, Quad(0.06f), new Vector2(6f, 12f) * Mathf.Sqrt(k), gravity: -9.8f, spread: 110f,
            ramp: [Spark, Spark, Fire with { A = 0f }], glow: true, add: true, at: centre, emitRadius: 0.2f));
        AddChild(Particles("Scrap", 16, 1.8f, new BoxMesh { Size = new Vector3(0.09f, 0.04f, 0.07f) }, new Vector2(4f, 8f) * Mathf.Sqrt(k),
            gravity: -9.8f, spread: 75f, ramp: [Scrap, Scrap, Scrap with { A = 0f }], at: centre, emitRadius: 0.2f, spin: true, damping: 0.3f));
        // Small fires burn on for a moment where it went off.
        AddChild(Particles("Embers", 30, 0.7f, Quad(0.45f * k), new Vector2(0.5f, 1.5f), gravity: 1.5f, spread: 20f,
            ramp: [Fire with { A = 0f }, Fire, Ember with { A = 0f }], glow: true, add: true, at: Vector3.Up * 0.1f, emitRadius: 1.2f * k,
            oneShot: false, stopAfter: 3f, ring: true));
    }

    // ---- Shockwave ----

    private void BuildShockwave()
    {
        _seconds = ShockwaveSeconds;
        var r = Radius;
        for (var i = 0; i < 2; i++)
        {
            var material = Glow(Wave, transparent: true);
            material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            var ring = new MeshInstance3D
            {
                Name = $"Ring{i}",
                Position = Vector3.Up * (0.06f + 0.02f * i),
                Mesh = Ring(i == 0 ? 0.7f : 0.88f, 1f),
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(ring);
            _rings.Add((ring, material, 0.12f * i));
        }
        // Dust kicks up along the wave front and a few stones jump.
        AddChild(Particles("Dust", 64, 1.1f, Quad(0.45f), new Vector2(0.5f, 1.4f), gravity: -0.5f, spread: 25f,
            ramp: [Dust, Dust, Dust with { A = 0f }], grow: (0.5f, 2.4f), at: Vector3.Up * 0.1f, emitRadius: 0.4f, ring: true,
            radial: 22f * r, damping: 0.4f));
        AddChild(Particles("Stones", 14, 1.0f, new BoxMesh { Size = new Vector3(0.07f, 0.05f, 0.06f) }, new Vector2(2f, 4f), gravity: -9.8f,
            spread: 25f, ramp: [Scrap, Scrap, Scrap with { A = 0f }], at: Vector3.Up * 0.05f, emitRadius: 0.45f * r, ring: true, spin: true));
    }

    // A flat ring in the XZ plane from inner to outer (1 = the radius), brightest at its outer edge.
    private static ArrayMesh Ring(float inner, float outer)
    {
        const int segments = 48;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (var i = 0; i < segments; i++)
        {
            var a0 = Mathf.Tau * i / segments;
            var a1 = Mathf.Tau * (i + 1) / segments;
            Vector3 P(float a, float r) => new(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            var (i0, i1, o0, o1) = (P(a0, inner), P(a1, inner), P(a0, outer), P(a1, outer));
            var faint = new Color(1, 1, 1, 0.1f);
            foreach (var (v, c) in new[] { (i0, faint), (o0, Colors.White), (o1, Colors.White), (i0, faint), (o1, Colors.White), (i1, faint) })
            {
                st.SetColor(c);
                st.SetNormal(Vector3.Up);
                st.AddVertex(v);
            }
        }
        return st.Commit();
    }

    // ---- Parts ----

    private static StandardMaterial3D Glow(Color colour, bool transparent, bool add = false) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = colour,
        VertexColorUseAsAlbedo = true,
        Transparency = transparent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        // Fire adds light (it never darkens what is behind it); a ring of dust and light mixes.
        BlendMode = add ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
    };

    private static QuadMesh Quad(float size) => new() { Size = new Vector2(size, size) };

    private CpuParticles3D Particles(string name, int amount, float life, Mesh mesh, Vector2 speed, float gravity, float spread, Color[] ramp,
        Vector3 at, float emitRadius, bool glow = false, bool add = false, (float From, float To)? grow = null, float delay = 0f, bool oneShot = true,
        float stopAfter = 0f, bool ring = false, bool spin = false, float radial = 0f, float damping = 1f)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = glow ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = add ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
            BillboardMode = mesh is QuadMesh ? BaseMaterial3D.BillboardModeEnum.Particles : BaseMaterial3D.BillboardModeEnum.Disabled,
            Roughness = 0.9f,
            AlbedoTexture = mesh is QuadMesh ? Puff : null,
        };
        mesh.SurfaceSetMaterial(0, material);
        var gradient = new Gradient();
        gradient.Offsets = [.. Enumerable.Range(0, ramp.Length).Select(i => (float)i / (ramp.Length - 1))];
        gradient.Colors = ramp;
        var p = new CpuParticles3D
        {
            Name = name,
            Position = at,
            Amount = amount,
            Lifetime = life,
            OneShot = oneShot,
            Explosiveness = oneShot ? 0.95f : 0f,
            Mesh = mesh,
            Direction = Vector3.Up,
            Spread = spread,
            InitialVelocityMin = speed.X,
            InitialVelocityMax = speed.Y,
            Gravity = new Vector3(0, gravity, 0),
            DampingMin = damping,
            DampingMax = damping * 3f,
            EmissionShape = ring ? CpuParticles3D.EmissionShapeEnum.Ring : CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = emitRadius,
            EmissionRingRadius = emitRadius,
            EmissionRingInnerRadius = emitRadius * 0.6f,
            EmissionRingHeight = 0.05f,
            EmissionRingAxis = Vector3.Up,
            ColorRamp = gradient,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Emitting = delay <= 0f,
            AngularVelocityMin = spin ? -540f : -90f,
            AngularVelocityMax = spin ? 540f : 90f,
            RadialAccelMin = radial * 0.8f,
            RadialAccelMax = radial,
        };
        if (grow is { } g)
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, g.From / g.To));
            curve.AddPoint(new Vector2(1, 1f));
            p.ScaleAmountCurve = curve;
            p.ScaleAmountMin = p.ScaleAmountMax = g.To;
        }
        if (delay > 0f)
            GetTree().CreateTimer(delay).Timeout += () => p.Emitting = true;
        if (stopAfter > 0f)
            GetTree().CreateTimer(stopAfter).Timeout += () => p.Emitting = false;
        return p;
    }
}
