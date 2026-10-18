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
