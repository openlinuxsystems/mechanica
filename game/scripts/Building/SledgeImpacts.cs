using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Sound;
using Mechanica.Game.Sound;

namespace Mechanica.Game.Building;

/// <summary>
/// The puff of dust, flying chips and sparks where a Sledgehammer hit lands, on every peer that hears it. The host plays
/// <c>smash_hit</c> or <c>smash_break</c> at the spot for everyone in earshot (<see cref="Core.Building.ToolActions"/>), so
/// the burst rides on that sound (<see cref="SoundSync.Played"/>) and costs no traffic of its own. A break throws twice
/// as much.
/// </summary>
public partial class SledgeImpacts : Node3D
{
    /// <summary>The sound of a hit that does not break what it lands on.</summary>
    public const string HitSound = Identifiers.VanillaModId + ":smash_hit";

    /// <summary>The sound of a hit that breaks it.</summary>
    public const string BreakSound = Identifiers.VanillaModId + ":smash_break";

    private const float Lifetime = 0.9f;

    private static readonly Color Dust = new(0.62f, 0.58f, 0.52f, 0.55f);
    private static readonly Color Chip = new(0.3f, 0.3f, 0.32f);
    private static readonly Color Spark = new(1f, 0.93f, 0.7f);

    /// <summary>The world's sounds. Set before adding to the tree.</summary>
    public SoundSync Sounds { get; init; } = null!;

    /// <summary>Bursts made so far (tests count them).</summary>
    public int Bursts { get; private set; }

    public override void _Ready() => Sounds.Played += OnPlayed;

    public override void _ExitTree() => Sounds.Played -= OnPlayed;

    private void OnPlayed(SoundEvent sound, Vector3 at)
    {
        if (sound.QualifiedId is HitSound or BreakSound)
            Burst(at, sound.QualifiedId == BreakSound);
    }

    /// <summary>Throws a burst at <paramref name="at"/>; <paramref name="broke"/> for a bigger one.</summary>
    public void Burst(Vector3 at, bool broke)
    {
        Bursts++;
        var scale = broke ? 2 : 1;
        var root = new Node3D { Name = "Burst", Position = at };
        AddChild(root);
        root.AddChild(Emitter(12 * scale, 0.7f, new Vector2(0.8f, 2.0f), -1.5f, 75f, Quad(0.16f), Dust, grow: true));
        root.AddChild(Emitter(10 * scale, 0.6f, new Vector2(2.5f, 4.5f), -9.8f, 60f, Box(0.025f), Chip));
        root.AddChild(Emitter(8 * scale, 0.22f, new Vector2(4f, 7f), -4f, 80f, Quad(0.03f), Spark, glow: true));
        GetTree().CreateTimer(Lifetime + 0.2f).Timeout += root.QueueFree;
    }

    private static CpuParticles3D Emitter(int amount, float life, Vector2 speed, float gravity, float spread, Mesh mesh, Color colour,
        bool grow = false, bool glow = false)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = glow ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
            VertexColorUseAsAlbedo = true,
            Transparency = colour.A < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            BillboardMode = mesh is QuadMesh ? BaseMaterial3D.BillboardModeEnum.Particles : BaseMaterial3D.BillboardModeEnum.Disabled,
            Roughness = 0.9f,
        };
        mesh.SurfaceSetMaterial(0, material);
        var p = new CpuParticles3D
        {
            Amount = amount,
            Lifetime = life,
            OneShot = true,
            Explosiveness = 1f,
            Emitting = true,
            Mesh = mesh,
            Direction = Vector3.Up,
            Spread = spread,
            InitialVelocityMin = speed.X,
            InitialVelocityMax = speed.Y,
            Gravity = new Vector3(0, gravity, 0),
            DampingMin = 1f,
            DampingMax = 2f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.08f,
            Color = colour,
            AngularVelocityMin = -360f,
            AngularVelocityMax = 360f,
        };
        var fade = new Gradient();
        fade.SetColor(0, colour);
        fade.SetColor(1, new Color(colour, 0f));
        p.ColorRamp = fade;
        if (grow)
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0.5f));
            curve.AddPoint(new Vector2(1, 1.6f));
            p.ScaleAmountCurve = curve;
        }
        return p;
    }

    private static QuadMesh Quad(float size) => new() { Size = new Vector2(size, size) };

    private static BoxMesh Box(float size) => new() { Size = new Vector3(size, size * 0.6f, size) };
}
