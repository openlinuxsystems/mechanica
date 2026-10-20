using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Fire;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Fire;

/// <summary>
/// Shows one fire (<see cref="FireView"/>): flames and embers rising, smoke above them and a flickering
/// warm light, all bigger and brighter the stronger it burns. A site fire stands where its marker is; a
/// burning part's fire follows the part's Body.
/// </summary>
public partial class FireNode : Node3D
{
    private static readonly Gradient FlameColors = new()
    {
        Offsets = [0f, 0.35f, 1f],
        Colors = [new Color(1f, 0.92f, 0.55f), new Color(1f, 0.5f, 0.12f, 0.9f), new Color(0.55f, 0.1f, 0.04f, 0f)],
    };

    private static readonly Gradient SmokeColors = new()
    {
        Offsets = [0f, 0.3f, 1f],
        Colors = [new Color(0.2f, 0.2f, 0.2f, 0f), new Color(0.22f, 0.21f, 0.2f, 0.45f), new Color(0.3f, 0.3f, 0.3f, 0f)],
    };

    private OmniLight3D _light = null!;
    private float _clock;

    /// <summary>What it shows.</summary>
    public FireView? View { get; private set; }

    /// <summary>The flames (tests look at them).</summary>
    public CpuParticles3D Flames { get; private set; } = null!;

    /// <summary>The smoke.</summary>
    public CpuParticles3D Smoke { get; private set; } = null!;

    public override void _Ready()
    {
        Flames = new CpuParticles3D
        {
            Name = "Flames",
            Amount = 28,
            Lifetime = 0.7,
            Mesh = new QuadMesh { Size = new Vector2(0.35f, 0.45f), Material = Additive() },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.3f,
            Direction = Vector3.Up,
            Spread = 12,
            Gravity = new Vector3(0, 2.2f, 0),
            InitialVelocityMin = 0.3f,
            InitialVelocityMax = 0.8f,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.2f,
            ScaleAmountCurve = Shrinking(),
            ColorRamp = FlameColors,
        };
        AddChild(Flames);
        Smoke = new CpuParticles3D
        {
            Name = "Smoke",
            Amount = 12,
            Lifetime = 2.4,
            Position = new Vector3(0, 0.6f, 0),
            Mesh = new QuadMesh { Size = new Vector2(0.6f, 0.6f), Material = Blended() },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.25f,
            Direction = Vector3.Up,
            Spread = 20,
            Gravity = new Vector3(0.2f, 0.6f, 0),
            InitialVelocityMin = 0.4f,
            InitialVelocityMax = 0.7f,
            ScaleAmountMin = 0.8f,
            ScaleAmountMax = 1.6f,
            ColorRamp = SmokeColors,
        };
        AddChild(Smoke);
        _light = new OmniLight3D
        {
            Name = "Light",
            Position = new Vector3(0, 0.5f, 0),
            LightColor = new Color(1f, 0.62f, 0.3f),
            OmniRange = 6,
            ShadowEnabled = false,
        };
        AddChild(_light);
    }

    public override void _Process(double delta)
    {
        // A flicker made of two slow waves: no randomness, so it costs nothing and looks the same everywhere.
        _clock += (float)delta;
        var strength = View?.Strength ?? 0;
        _light.LightEnergy = strength * (1.6f + 0.25f * Mathf.Sin(_clock * 13f) + 0.15f * Mathf.Sin(_clock * 31f + 1f));
    }

    /// <summary>Shows <paramref name="view"/>: the stronger it burns, the bigger it is.</summary>
    public void Show(FireView view)
    {
        View = view;
        var size = 0.45f + 0.75f * Math.Clamp(view.Strength, 0, 1);
        // A burning part's flames are a block's size; a site fire is a campfire's.
        Scale = Vector3.One * (view.Part is null ? size * 1.3f : size);
        Flames.Emitting = Smoke.Emitting = view.Strength > 0;
        if (view.Part is null)
            Position = view.Position.ToGodot();
    }

    /// <summary>Burns on the part in <paramref name="node"/>'s Body; hidden when the part is gone.</summary>
    public void Follow(CreationNode? node, int partId)
    {
        var creation = node?.Creation;
        if (creation?.FindPart(partId) is not { } part || creation.BodyIndexOf(partId) is var index && node!.Bodies.FirstOrDefault(b => b.BodyIndex == index) is not { } body)
        {
            Visible = false;
            return;
        }
        var centre = PartGeometry.CentreMetres(creation.DefinitionOf(partId), part.Cell, part.Rotation).ToGodot();
        // Flames stand up whichever way the part is turned.
        GlobalTransform = new Transform3D(Basis.Identity.Scaled(Scale), body.GlobalTransform * centre);
        Visible = true;
    }

    // Glowing particles (flames); the opening cinematic's fire uses them too.
    internal static StandardMaterial3D Additive() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        VertexColorUseAsAlbedo = true,
        AlbedoTexture = Blob(),
    };

    // Soft particles (smoke).
    internal static StandardMaterial3D Blended() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        VertexColorUseAsAlbedo = true,
        AlbedoTexture = Blob(),
    };

    // A soft round spot, so each particle is a glow rather than a square.
    private static GradientTexture2D Blob() => new()
    {
        Width = 64,
        Height = 64,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(0.5f, 0f),
        Gradient = new Gradient { Offsets = [0f, 1f], Colors = [Colors.White, new Color(1, 1, 1, 0)] },
    };

    internal static Curve Shrinking()
    {
        var curve = new Curve();
        curve.AddPoint(new Vector2(0, 1));
        curve.AddPoint(new Vector2(1, 0.1f));
        return curve;
    }
}
