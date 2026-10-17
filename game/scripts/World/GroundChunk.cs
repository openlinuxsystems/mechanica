using Godot;
using Mechanica.Core.World;

namespace Mechanica.Game.World;

/// <summary>
/// The ground of one streamed chunk: a flat static slab whose top is at y = 0, with a plane mesh on it.
/// Chunk 19 replaces the slab with generated heightmap terrain (D11). Shapes, meshes and materials are
/// shared by every chunk of the same size.
/// </summary>
public partial class GroundChunk : StaticBody3D
{
    private const float Thickness = 1f;
    private static readonly Dictionary<float, (BoxShape3D Shape, PlaneMesh Mesh)> Shared = [];
    private static readonly StandardMaterial3D[] Materials =
    [
        new() { AlbedoColor = new Color(0.36f, 0.45f, 0.32f) },
        new() { AlbedoColor = new Color(0.33f, 0.42f, 0.30f) },
    ];

    /// <summary>Which chunk this is. Set before adding to the tree.</summary>
    public ChunkCoord Coord { get; init; }

    /// <summary>Side in metres. Set before adding to the tree.</summary>
    public float Size { get; init; }

    public override void _Ready()
    {
        Name = $"Chunk_{Coord.X}_{Coord.Z}";
        var (x, z) = Coord.Origin(Size);
        Position = new Vector3(x, 0, z);
        if (!Shared.TryGetValue(Size, out var shared))
        {
            shared = (new BoxShape3D { Size = new Vector3(Size, Thickness, Size) }, new PlaneMesh { Size = new Vector2(Size, Size) });
            Shared[Size] = shared;
        }
        var centre = new Vector3(Size / 2f, 0, Size / 2f);
        AddChild(new CollisionShape3D { Name = "Shape", Shape = shared.Shape, Position = centre + new Vector3(0, -Thickness / 2f, 0) });
        // Neighbouring chunks alternate shades so borders are visible while testing.
        AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = shared.Mesh,
            Position = centre,
            MaterialOverride = Materials[(Coord.X + Coord.Z) & 1],
        });
    }
}
