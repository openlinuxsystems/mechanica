using Godot;
using Mechanica.Core.World;
using Mechanica.Game.Creations;
using Mechanica.Game.Mods;

namespace Mechanica.Game.World;

/// <summary>
/// A model a POI is built from (<see cref="PoiModel"/>, the crash site's wreck), or the collider of a world object that collides by
/// its model (<see cref="MeshDefinition.Collide"/>), read once per file and shared by every chunk that places it: the meshes it is drawn with, the triangles it collides with and its lights. A mesh named
/// <c>collider</c> is its collision shape and is not drawn; without one the drawn meshes collide.
/// </summary>
public sealed class StructureModel
{
    private const string ColliderName = "collider";
    private const float LampEnergy = 2f;
    private const float LampRange = 7f;

    // Each file's model, loaded once; null when it failed to load.
    private static readonly Dictionary<string, StructureModel?> Models = [];

    private StructureModel(IReadOnlyList<PartMesh> meshes, IReadOnlyList<(Shape3D Shape, Transform3D Transform)> colliders,
        IReadOnlyList<(OmniLight3D Light, Transform3D Transform)> lights, bool hasColliderMesh)
    {
        Meshes = meshes;
        Colliders = colliders;
        Lights = lights;
        HasColliderMesh = hasColliderMesh;
    }

    /// <summary>Whether the file has a <c>collider</c> mesh (otherwise <see cref="Colliders"/> are its drawn meshes).</summary>
    public bool HasColliderMesh { get; }

    /// <summary>The meshes it is drawn with, relative to its origin.</summary>
    public IReadOnlyList<PartMesh> Meshes { get; }

    /// <summary>Its collision shapes (both faces of every triangle collide, so walls hold from inside and out).</summary>
    public IReadOnlyList<(Shape3D Shape, Transform3D Transform)> Colliders { get; }

    /// <summary>Its point lights (a wreck's ceiling lights), templates to copy.</summary>
    public IReadOnlyList<(OmniLight3D Light, Transform3D Transform)> Lights { get; }

    /// <summary>The model in <paramref name="file"/> (<paramref name="key"/> names it in warnings), or null when it does not load.</summary>
    public static StructureModel? Of(string file, string key)
    {
        if (!Models.TryGetValue(file, out var model))
            Models[file] = model = Read(file, key);
        return model;
    }

    /// <summary>Forgets every model, so a new world reads its mods' files afresh.</summary>
    public static void Clear()
    {
        foreach (var model in Models.Values)
            foreach (var (light, _) in model?.Lights ?? [])
                light.Free();
        Models.Clear();
    }

    /// <summary>A node drawing the model and its lights (not its collider).</summary>
    public Node3D Instantiate(string name, Transform3D transform)
    {
        var root = new Node3D { Name = name, Transform = transform };
        foreach (var mesh in Meshes)
            root.AddChild(mesh.Instantiate());
        foreach (var (light, at) in Lights)
        {
            var copy = (OmniLight3D)light.Duplicate();
            copy.Transform = at;
            root.AddChild(copy);
        }
        return root;
    }

    private static StructureModel? Read(string file, string key)
    {
        if (ModGltf.Load(file) is not { } scene)
        {
            GD.PushWarning($"POI model '{key}' did not load as glTF.");
            return null;
        }
        var meshes = new List<PartMesh>();
        var colliders = new List<(Shape3D, Transform3D)>();
        var lights = new List<(OmniLight3D, Transform3D)>();
        Collect(scene, Transform3D.Identity, meshes, colliders, lights);
        scene.Free();
        var hasColliderMesh = colliders.Count > 0;
        if (!hasColliderMesh)
            colliders.AddRange(meshes.Select(m => ((Shape3D)Trimesh(m.Mesh), m.Transform)));
        return new StructureModel(meshes, colliders, lights, hasColliderMesh);
    }

    private static void Collect(Node node, Transform3D parent, List<PartMesh> meshes, List<(Shape3D, Transform3D)> colliders,
        List<(OmniLight3D, Transform3D)> lights)
    {
        // The scene root's own transform is the file's placement, not part of the model.
        var transform = node is Node3D n3 && node.GetParent() is not null ? parent * n3.Transform : parent;
        if (node is MeshInstance3D { Mesh: { } mesh })
        {
            if (node.Name.ToString().StartsWith(ColliderName, StringComparison.Ordinal))
                colliders.Add((Trimesh(mesh), transform));
            else
                meshes.Add(new PartMesh(mesh, transform, Name: node.Name));
        }
        else if (node is OmniLight3D light)
        {
            var template = (OmniLight3D)light.Duplicate();
            // A structure's lights are room lamps: the file gives their colour; Blender's photometric watts arrive
            // thousands of times too bright and with no range, so every lamp gets one room-sized brightness.
            template.LightEnergy = LampEnergy;
            template.OmniRange = LampRange;
            // Many lamps in one structure: no shadows, and they fade out with distance.
            template.ShadowEnabled = false;
            template.DistanceFadeEnabled = true;
            template.DistanceFadeBegin = 40f;
            template.DistanceFadeLength = 20f;
            lights.Add((template, transform));
        }
        foreach (var child in node.GetChildren())
            Collect(child, transform, meshes, colliders, lights);
    }

    private static ConcavePolygonShape3D Trimesh(Mesh mesh) => new() { Data = mesh.GetFaces(), BackfaceCollision = true };
}
