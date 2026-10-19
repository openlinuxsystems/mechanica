using Godot;
using Mechanica.Core.World;

namespace Mechanica.Game.World;

/// <summary>
/// What a world object does when it is hit (Tree Harvesting spec §4.2, §5), drawn by every peer from the host's
/// <see cref="WorldStreamer.ObjectHit"/>: a tree drawn close up shakes, chips of its <see cref="DebrisDefinition.Chips"/>
/// colours fly from the hit and a few <see cref="DebrisDefinition.Leaves"/> drift down from its canopy; the blow that
/// fells it sends a burst of leaves down as the canopy goes. Cosmetic only: nothing here touches the simulation.
/// </summary>
public partial class ObjectEffects : Node3D
{
    private static readonly StandardMaterial3D Material = new()
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.9f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
    private static readonly BoxMesh ChipMesh = new() { Size = new Vector3(0.07f, 0.05f, 0.06f), Material = Material };
    private static readonly BoxMesh LeafMesh = new() { Size = new Vector3(0.16f, 0.02f, 0.12f), Material = Material };

    /// <summary>Chips flying from a hit.</summary>
    public const int ChipsPerHit = 10;

    /// <summary>Leaves drifting down from the canopy of a tree that is hit.</summary>
    public const int LeavesPerHit = 6;

    /// <summary>Leaves falling when a tree is felled.</summary>
    public const int LeavesPerFall = 70;

    /// <summary>The world, which finds objects and the chunks drawing them. Set before adding to the tree.</summary>
    public WorldStreamer Streamer { get; init; } = null!;

    /// <summary>Bursts of chips or leaves started so far (tests).</summary>
    public int Bursts { get; private set; }

    /// <summary>Hits shown so far (tests).</summary>
    public int Hits { get; private set; }

    /// <summary>
    /// Shows a hit on <paramref name="id"/> at <paramref name="at"/>; <paramref name="broke"/> for the blow that broke or
    /// felled it.
    /// </summary>
    public void Struck(WorldObjectId id, Vector3 at, bool broke)
    {
        if (Streamer.Generator.Find(id) is not { } placement)
            return;
        Hits++;
        if (!broke)
            Streamer.ChunkHolding(id)?.Shake(id, at);
        if (placement.Definition.Debris is not { } debris)
            return;
        if (Colours(debris.Chips) is { } chips)
            Burst("Chips", at, ChipMesh, chips, broke ? ChipsPerHit * 2 : ChipsPerHit, 0.15f, 4.5f, 9.8f, 1.1f);
        if (Colours(debris.Leaves) is { } leaves)
        {
            var canopy = placement.Position.ToGodot() + debris.Canopy.ToGodot() * placement.Scale;
            var radius = debris.CanopyRadius * placement.Scale;
            Burst("Leaves", canopy, LeafMesh, leaves, broke ? LeavesPerFall : LeavesPerHit, radius, 0.8f, 1.6f, broke ? 3.5f : 2.6f);
        }
    }

    /// <summary>Chips or sparks from a cutting tool's bite (<see cref="WorldStreamer.Impact"/>) shown so far (tests).</summary>
    public int Bites { get; private set; }

    /// <summary>
    /// Shows a saw's or drill's bite at <paramref name="at"/>: sparks off metal (and bots), chips off anything else in
    /// the colours of its material.
    /// </summary>
    public void Bite(Vector3 at, string material)
    {
        Bites++;
        if (material is "metal" or "scrap")
            Burst("Sparks", at, SparkMesh, SparkColours, SparksPerBite, 0.04f, 5.5f, 9.8f, 0.35f);
        else
            Burst("Chips", at, ChipMesh, BiteColours.GetValueOrDefault(material) ?? BiteColours["wood"], ChipsPerHit / 2, 0.06f, 3.5f, 9.8f, 0.9f);
    }

    /// <summary>Sparks from one bite into metal.</summary>
    public const int SparksPerBite = 8;

    private static readonly StandardMaterial3D SparkMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
    private static readonly BoxMesh SparkMesh = new() { Size = new Vector3(0.012f, 0.012f, 0.05f), Material = SparkMaterial };
    private static readonly Gradient SparkColours = Colours(["#fff6c8", "#ffd86a", "#fffbe8"])!;
    private static readonly Dictionary<string, Gradient> BiteColours = new()
    {
        ["wood"] = Colours(["#c9a26b", "#a87a45", "#e0c38f"])!,
        ["concrete"] = Colours(["#9a9a96", "#7d7d7a"])!,
        ["plastic"] = Colours(["#d8d8d8", "#5a5a5a"])!,
        ["glass"] = Colours(["#cfe6ee", "#ffffff"])!,
        ["cardboard"] = Colours(["#b8925e", "#9c7a4a"])!,
    };

    // A one-shot cloud of small pieces from round `at`, thrown out at `speed` and falling under `gravity`.
    private void Burst(string name, Vector3 at, Mesh mesh, Gradient colours, int count, float radius, float speed, float gravity, float lifetime)
    {
        Bursts++;
        var particles = new CpuParticles3D
        {
            Name = name,
            Position = at,
            Mesh = mesh,
            Amount = count,
            Lifetime = lifetime,
            OneShot = true,
            Explosiveness = 0.9f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = Mathf.Max(radius, 0.01f),
            Direction = Vector3.Up,
            Spread = 180f,
            InitialVelocityMin = speed * 0.4f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, -gravity, 0),
            AngularVelocityMin = -360f,
            AngularVelocityMax = 360f,
            ParticleFlagRotateY = true,
            DampingMin = 0.2f,
            DampingMax = 0.8f,
            ColorInitialRamp = colours,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Emitting = true,
        };
        AddChild(particles);
        GetTree().CreateTimer(lifetime + 0.5f).Timeout += () =>
        {
            if (IsInstanceValid(particles))
                particles.QueueFree();
        };
    }

    // A ramp a particle picks its colour from at random: the listed colours in even bands.
    private static Gradient? Colours(List<string>? hexes)
    {
        var colours = (hexes ?? []).Where(h => Color.HtmlIsValid(h)).Select(h => Color.FromHtml(h)).ToList();
        if (colours.Count == 0)
            return null;
        var gradient = new Gradient { InterpolationMode = Gradient.InterpolationModeEnum.Constant };
        gradient.Offsets = [.. Enumerable.Range(0, colours.Count).Select(i => (float)i / colours.Count)];
        gradient.Colors = [.. colours];
        return gradient;
    }
}
