using Godot;
using Mechanica.Core.Data;

namespace Mechanica.Game.Creations;

/// <summary>
/// One mesh of a part's model, placed relative to the part centre. <see cref="SurfaceMaterials"/>, when
/// set, overrides each surface's material (from the part's <c>visual</c> data).
/// </summary>
public sealed record PartMesh(Mesh Mesh, Transform3D Transform, IReadOnlyList<Material?>? SurfaceMaterials = null)
{
    /// <summary>A mesh instance showing this mesh with its overrides.</summary>
    public MeshInstance3D Instantiate()
    {
        var instance = new MeshInstance3D { Mesh = Mesh, Transform = Transform };
        for (var i = 0; i < (SurfaceMaterials?.Count ?? 0); i++)
            instance.SetSurfaceOverrideMaterial(i, SurfaceMaterials![i]);
        return instance;
    }
}

/// <summary>
/// What the runtime needs to show and collide one part definition, in part-local metres with the
/// origin at the footprint centre. <see cref="IsPlaceholder"/> is true when the mod gave no mesh or the
/// mesh failed to load, so a primitive matching the collision shape stands in.
/// </summary>
public sealed record PartModel(IReadOnlyList<PartMesh> Meshes, Shape3D Shape, Transform3D ShapeTransform, bool IsPlaceholder);

/// <summary>
/// Loads part meshes at runtime from mod folders with <see cref="GltfDocument"/> (D6) and builds
/// collision shapes from part data. Results are cached per file and per part, so every placed copy of
/// a part shares the same <see cref="Mesh"/> and <see cref="Shape3D"/> resources.
/// </summary>
public sealed class PartAssets
{
    private static readonly StandardMaterial3D PlaceholderMaterial = new() { AlbedoColor = new Color(0.62f, 0.62f, 0.66f) };

    private ModLoadResult? _content;
    private Func<string, string?> _modDirectory;
    private readonly Dictionary<string, IReadOnlyList<PartMesh>?> _files = [];
    private readonly Dictionary<string, PartModel> _models = [];

    /// <summary>Uses <paramref name="content"/> to find mod folders; without it every part is a placeholder.</summary>
    public PartAssets(ModLoadResult? content)
    {
        _content = content;
        _modDirectory = id => content?.Mods.FirstOrDefault(m => m.Id == id)?.Directory;
    }

    /// <summary>
    /// Finds mod folders with <paramref name="modDirectory"/> (mod id → folder, or null) instead of loaded
    /// content. The Part Editor uses it for mods that are being written and may not load yet.
    /// </summary>
    public PartAssets(Func<string, string?> modDirectory) => _modDirectory = modDirectory;

    /// <summary>The model for a placed part, by qualified id.</summary>
    public PartModel ModelFor(string qualifiedId, PartDefinition definition)
    {
        if (_models.TryGetValue(qualifiedId, out var cached))
            return cached;
        var modId = _content?.FindPart(qualifiedId)?.ModId;
        var (shape, shapeTransform) = BuildShape(definition, modId);
        var meshes = definition.Mesh is { } mesh && modId is not null ? LoadMeshes(mesh, modId) : null;
        var model = meshes is { Count: > 0 }
            ? new PartModel(meshes, shape, shapeTransform, IsPlaceholder: false)
            : new PartModel([new PartMesh(PlaceholderMesh(definition, shape), shapeTransform)], shape, shapeTransform, IsPlaceholder: true);
        if (definition.Visual is { } visual)
            model = model with { Meshes = [.. model.Meshes.Select(m => ApplyVisual(m, visual, modId))] };
        return _models[qualifiedId] = model;
    }

    /// <summary>
    /// Every mesh in a glTF file of <paramref name="modId"/> (or the mod the path names), with its
    /// transform relative to the file's root. Null when the path is invalid, outside loaded mods, or fails to load.
    /// </summary>
    public IReadOnlyList<PartMesh>? LoadMeshes(string path, string modId)
    {
        if (!AssetPath.TryParse(path, modId, AssetKind.Mesh, out var asset, out var error))
        {
            GD.PushWarning($"[creations] {error}");
            return null;
        }
        var key = asset.ToString();
        if (_files.TryGetValue(key, out var cached))
            return cached;
        var directory = _modDirectory(asset.ModId);
        return _files[key] = directory is null ? Warn($"mod '{asset.ModId}' is not loaded, so '{key}' cannot load.") : ReadGltf(asset.Resolve(directory), key);
    }

    /// <summary>Uses newly loaded <paramref name="content"/> from now on and forgets everything cached.</summary>
    public void SetContent(ModLoadResult content)
    {
        _content = content;
        _modDirectory = id => content.Mods.FirstOrDefault(m => m.Id == id)?.Directory;
        ClearCache();
    }

    /// <summary>Forgets cached files, so meshes changed on disk load again.</summary>
    public void ClearCache()
    {
        _files.Clear();
        _models.Clear();
    }

    /// <summary>Every mesh in a glTF file on disk with its transform relative to the file's root, or null when it fails to load.</summary>
    public static IReadOnlyList<PartMesh>? ReadGltf(string file, string key)
    {
        var document = new GltfDocument();
        var state = new GltfState();
        var result = document.AppendFromFile(file, state);
        if (result != Error.Ok)
            return Warn($"'{key}' did not load as glTF ({result}).");
        var root = document.GenerateScene(state);
        if (root is null)
            return Warn($"'{key}' has no scene.");
        var meshes = new List<PartMesh>();
        Collect(root, Transform3D.Identity, meshes);
        root.Free();
        return meshes.Count > 0 ? meshes : Warn($"'{key}' contains no meshes.");
    }

    private static void Collect(Node node, Transform3D parent, List<PartMesh> meshes)
    {
        // The root's own transform is the file's placement, not part of the model.
        var transform = node is Node3D n3 && node.GetParent() is not null ? parent * n3.Transform : parent;
        if (node is MeshInstance3D { Mesh: { } mesh })
            meshes.Add(new PartMesh(mesh, transform));
        foreach (var child in node.GetChildren())
            Collect(child, transform, meshes);
    }

    /// <summary>The collision shape of a part and its transform from the part centre.</summary>
    public (Shape3D Shape, Transform3D Transform) BuildShape(PartDefinition definition, string? modId)
    {
        var collision = definition.Collision;
        var offset = new Transform3D(Basis.Identity, (collision?.Offset ?? Vec3.Zero).ToGodot());
        var footprint = (definition.Size ?? GridSize.One).Metres.ToGodot();
        Shape3D shape = collision?.Shape switch
        {
            CollisionShape.Box => new BoxShape3D { Size = collision.Size?.ToGodot() ?? footprint },
            CollisionShape.Sphere => new SphereShape3D { Radius = collision.Radius!.Value },
            CollisionShape.Cylinder => new CylinderShape3D { Radius = collision.Radius!.Value, Height = collision.Height!.Value },
            CollisionShape.Capsule => new CapsuleShape3D { Radius = collision.Radius!.Value, Height = collision.Height!.Value },
            CollisionShape.Convex => (Shape3D?)ConvexShape(collision.Mesh!, modId) ?? new BoxShape3D { Size = footprint },
            _ => new BoxShape3D { Size = footprint },
        };
        return (shape, offset);
    }

    private ConvexPolygonShape3D? ConvexShape(string path, string? modId)
    {
        if (modId is null || LoadMeshes(path, modId) is not { } meshes)
            return null;
        var points = meshes.SelectMany(m => m.Mesh.GetFaces().Select(v => m.Transform * v)).Distinct().ToArray();
        return points.Length >= 4 ? new ConvexPolygonShape3D { Points = points } : null;
    }

    /// <summary>A grey primitive mesh matching <paramref name="shape"/>, for parts without a model.</summary>
    public static Mesh PlaceholderMesh(PartDefinition definition, Shape3D shape)
    {
        PrimitiveMesh mesh = shape switch
        {
            BoxShape3D box => new BoxMesh { Size = box.Size },
            SphereShape3D sphere => new SphereMesh { Radius = sphere.Radius, Height = sphere.Radius * 2 },
            CylinderShape3D cylinder => new CylinderMesh { TopRadius = cylinder.Radius, BottomRadius = cylinder.Radius, Height = cylinder.Height },
            CapsuleShape3D capsule => new CapsuleMesh { Radius = capsule.Radius, Height = capsule.Height },
            _ => new BoxMesh { Size = (definition.Size ?? GridSize.One).Metres.ToGodot() },
        };
        mesh.Material = PlaceholderMaterial;
        return mesh;
    }

    /// <summary>
    /// The mesh with each surface's material copied and adjusted by <paramref name="visual"/>: albedo
    /// multiplied by its colour, roughness, metalness and texture replaced where given.
    /// </summary>
    public PartMesh ApplyVisual(PartMesh mesh, VisualDefinition visual, string? modId)
    {
        var materials = new List<Material?>();
        for (var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
            materials.Add(VisualMaterial(visual, modId, mesh.SurfaceMaterials?.ElementAtOrDefault(i) ?? mesh.Mesh.SurfaceGetMaterial(i)));
        return mesh with { SurfaceMaterials = materials };
    }

    /// <summary>
    /// A copy of <paramref name="original"/> (or a plain white material) adjusted by <paramref name="visual"/>:
    /// albedo multiplied by its colour, roughness, metalness and texture replaced where given.
    /// </summary>
    public BaseMaterial3D VisualMaterial(VisualDefinition visual, string? modId, Material? original = null)
    {
        var material = original is BaseMaterial3D b ? (BaseMaterial3D)b.Duplicate() : new StandardMaterial3D();
        if (HexColor.TryParse(visual.Color, out var c))
            material.AlbedoColor *= new Color(c.R, c.G, c.B);
        if (visual.Roughness is { } roughness)
            material.Roughness = roughness;
        if (visual.Metallic is { } metallic)
            material.Metallic = metallic;
        if (visual.Texture is { } path && modId is not null && LoadTexture(path, modId) is { } texture)
            material.AlbedoTexture = texture;
        return material;
    }

    private Texture2D? LoadTexture(string path, string modId)
    {
        if (!AssetPath.TryParse(path, modId, AssetKind.Texture, out var asset, out var error))
        {
            GD.PushWarning($"[creations] {error}");
            return null;
        }
        var directory = _modDirectory(asset.ModId);
        var image = directory is null || !File.Exists(asset.Resolve(directory)) ? null : Image.LoadFromFile(asset.Resolve(directory));
        if (image is null)
        {
            GD.PushWarning($"[creations] texture '{asset}' did not load.");
            return null;
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static IReadOnlyList<PartMesh>? Warn(string message)
    {
        GD.PushWarning($"[creations] {message}");
        return null;
    }
}
