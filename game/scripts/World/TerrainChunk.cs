using Godot;
using Mechanica.Core.World;

namespace Mechanica.Game.World;

/// <summary>
/// One streamed chunk of generated ground (chunk 19, D11), built from a Core <see cref="TerrainChunkData"/>:
/// <list type="bullet">
/// <item>the ground: a mesh painted with the world's ground textures (or plain biome colours), and a <see cref="HeightMapShape3D"/> (<see cref="GroundShape"/>)
/// added to the streamer's one shared <see cref="Ground"/> body. Every chunk's height map is a shape of
/// that one body, so Jolt's internal edge removal sees across chunk borders and wheels roll over them
/// smoothly; with a body per chunk they bumped on the edge of the chunk they were leaving.</item>
/// <item>a water surface where the ground dips below sea level, drawn with <see cref="WaterSurface.Material"/> (cosmetic: it drives no physics);</item>
/// <item><see cref="Objects"/>: one static body holding a collider per scattered object that is not
/// destroyed, drawn with one <see cref="MultiMesh"/> per object piece or flora model. Objects that are not
/// <see cref="WorldObjectDefinition.Solid"/> (bushes) put their collider in <see cref="Brush"/> instead, an area
/// nothing collides with and only rays that ask for areas (the sledge) hit;</item>
/// <item><see cref="Structures"/>: the static pieces of POIs whose centre lies in this chunk, and
/// <see cref="Markers"/> for later systems.</item>
/// </list>
/// Meshes, materials and the sea are shared between chunks.
/// </summary>
public partial class TerrainChunk : Node3D
{
    private static readonly StandardMaterial3D GroundMaterial = new() { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.95f };
    private static readonly Dictionary<ShapeDefinition, Mesh> PieceMeshes = [];
    private static readonly Dictionary<string, StandardMaterial3D> Materials = [];
    // Models world objects are drawn with (rock outcrop cubes), by file; null when one failed to load.
    private static readonly Dictionary<string, Creations.PartMesh?> ObjectMeshes = [];

    private readonly Dictionary<ulong, WorldObjectId> _objectShapes = [];

    /// <summary>The generated chunk. Set before adding to the tree.</summary>
    public TerrainChunkData Data { get; init; } = null!;

    /// <summary>Which objects are destroyed. Set before adding to the tree; call <see cref="RefreshObjects"/> after changing it.</summary>
    public WorldState State { get; set; } = null!;

    /// <summary>The world's textured ground (<see cref="World.GroundMaterial"/>); null draws the plain biome colours.</summary>
    public Material? Textured { get; init; }

    /// <summary>The world's generation data, which finds objects' model files; null draws every object from its pieces.</summary>
    public WorldGenData? WorldGen { get; init; }

    /// <summary>The static body every loaded chunk's ground shape belongs to, a sibling of this node. Set before adding to the tree.</summary>
    public StaticBody3D Ground { get; init; } = null!;

    /// <summary>This chunk's height map collider, a child of <see cref="Ground"/> while the chunk is in the tree.</summary>
    public CollisionShape3D GroundShape { get; private set; } = null!;

    /// <summary>
    /// True for scenery past the simulated ground (<see cref="FarChunks"/>): drawn only, with no colliders,
    /// POI markers or object lookups, and the ground mesh at half its resolution.
    /// </summary>
    public bool Scenery { get; init; }

    /// <summary>Which chunk this is.</summary>
    public ChunkCoord Coord => Data.Coord;

    /// <summary>The body holding scattered objects' colliders.</summary>
    public StaticBody3D Objects { get; private set; } = null!;

    /// <summary>
    /// Colliders of the objects players, bots and Creations pass through (not <see cref="WorldObjectDefinition.Solid"/>):
    /// an area, so physics bodies ignore it and so do rays unless they collide with areas.
    /// </summary>
    public Area3D Brush { get; private set; } = null!;

    /// <summary>The body holding POI structure pieces.</summary>
    public StaticBody3D Structures { get; private set; } = null!;

    /// <summary>POI markers, one <see cref="Marker3D"/> each, with <c>type</c>, <c>ref</c> and <c>poi</c> metadata.</summary>
    public Node3D Markers { get; private set; } = null!;

    /// <summary>Objects in this chunk that are not destroyed.</summary>
    public IEnumerable<WorldObjectPlacement> LiveObjects => Data.Objects.Where(o => !State.IsDestroyed(o.Id));

    public override void _Ready()
    {
        Name = $"{(Scenery ? "Scenery" : "Chunk")}_{Coord.X}_{Coord.Z}";
        var (x, z) = Coord.Origin(Data.Size);
        Position = new Vector3(x, 0, z);
        BuildGround();
        Objects = new StaticBody3D { Name = "Objects" };
        AddChild(Objects);
        Brush = new Area3D { Name = "Brush", Monitoring = false };
        AddChild(Brush);
        BuildObjects();
        BuildStructures();
    }

    // The ground shape lives on the shared body, so it goes when the chunk does.
    public override void _ExitTree()
    {
        if (!IsInstanceValid(GroundShape))
            return;
        GroundShape.GetParent()?.RemoveChild(GroundShape);
        GroundShape.QueueFree();
    }

    /// <summary>True when this chunk generated the object (destroyed or not).</summary>
    public bool Holds(WorldObjectId id) => Data.Objects.Any(o => o.Id == id);

    /// <summary>The object a ray or collision hit, from what it hit and the hit's shape index; null when it was no object.</summary>
    public static WorldObjectId? ObjectHit(GodotObject? collider, int shapeIndex) =>
        collider is CollisionObject3D body && body.GetParent() is TerrainChunk chunk && (body == chunk.Objects || body == chunk.Brush)
            ? chunk.ObjectAt(body, shapeIndex)
            : null;

    /// <summary>The object a collision with <see cref="Objects"/> hit, from the hit's shape index.</summary>
    public WorldObjectId? ObjectAt(int shapeIndex) => ObjectAt(Objects, shapeIndex);

    private WorldObjectId? ObjectAt(CollisionObject3D body, int shapeIndex)
    {
        if (shapeIndex < 0 || shapeIndex >= body.GetShapeOwners().Length)
            return null;
        var owner = body.ShapeFindOwner(shapeIndex);
        return body.ShapeOwnerGetOwner(owner) is CollisionShape3D shape && _objectShapes.TryGetValue(shape.GetInstanceId(), out var id) ? id : null;
    }

    /// <summary>Rebuilds the objects, after one was destroyed or <see cref="State"/> changed.</summary>
    public void RefreshObjects()
    {
        foreach (var child in Objects.GetChildren().Concat(Brush.GetChildren()))
        {
            child.GetParent().RemoveChild(child);
            child.QueueFree();
        }
        _objectShapes.Clear();
        BuildObjects();
    }

    private void BuildGround()
    {
        var n = Data.Samples;
        var res = Data.Resolution;
        if (!Scenery)
            BuildGroundShape(n, res);
        // Scenery skips every other sample when the grid allows it: far away, half the triangles look the same.
        var step = Scenery && (n - 1) % 2 == 0 ? 2 : 1;
        var m = (n - 1) / step + 1;
        var vertices = new Vector3[m * m];
        var normals = new Vector3[m * m];
        var colors = new Color[m * m];
        // Ground weights, four layers per custom channel. Floats: the Compatibility renderer reads 8-bit custom channels unnormalised.
        var textured = Data.Grounds.Length > 0 && Textured is not null;
        var custom0 = textured ? new float[m * m * 4] : [];
        var custom1 = textured ? new float[m * m * 4] : [];
        for (var j = 0; j < m; j++)
        {
            for (var i = 0; i < m; i++)
            {
                var k = j * step * n + i * step;
                var v = j * m + i;
                vertices[v] = new Vector3(i * step * res, Data.Heights[k], j * step * res);
                normals[v] = Data.Normals[k].ToGodot();
                var c = Data.Colors[k];
                colors[v] = new Color(c.X, c.Y, c.Z);
                if (textured)
                {
                    for (var l = 0; l < 4; l++)
                    {
                        custom0[v * 4 + l] = Data.Grounds[k * TerrainChunkData.GroundLayers + l];
                        custom1[v * 4 + l] = Data.Grounds[k * TerrainChunkData.GroundLayers + 4 + l];
                    }
                }
            }
        }
        n = m;
        var indices = new int[(n - 1) * (n - 1) * 6];
        var t = 0;
        for (var j = 0; j < n - 1; j++)
        {
            for (var i = 0; i < n - 1; i++)
            {
                // Clockwise seen from above, which Godot treats as front-facing.
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                indices[t++] = a;
                indices[t++] = b;
                indices[t++] = c;
                indices[t++] = b;
                indices[t++] = d;
                indices[t++] = c;
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var format = (Mesh.ArrayFormat)0;
        if (textured)
        {
            arrays[(int)Mesh.ArrayType.Custom0] = custom0;
            arrays[(int)Mesh.ArrayType.Custom1] = custom1;
            format = (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
                | (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);
        }
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: format);
        AddChild(new MeshInstance3D { Name = "Mesh", Mesh = mesh, MaterialOverride = textured ? Textured : GroundMaterial });

        if (Data.HasWater)
        {
            AddChild(new MeshInstance3D
            {
                Name = "Water",
                Mesh = WaterSurface.MeshFor(Data.Size, Scenery),
                MaterialOverride = WaterSurface.Material,
                Position = new Vector3(Data.Size / 2f, Data.SeaLevel, Data.Size / 2f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    private void BuildGroundShape(int n, float res)
    {
        GroundShape = new CollisionShape3D
        {
            Name = $"Shape_{Coord.X}_{Coord.Z}",
            Shape = new HeightMapShape3D { MapWidth = n, MapDepth = n, MapData = Data.Heights },
            // A height map is centred on its node and spaced 1 m; scale it to the resolution. Ground is
            // this node's sibling, so the shape sits at the chunk's position plus half a chunk.
            Position = Position + new Vector3(Data.Size / 2f, 0, Data.Size / 2f),
            Scale = new Vector3(res, 1f, res),
        };
        Ground.AddChild(GroundShape, forceReadableName: true);
    }

    private void BuildObjects()
    {
        var origin = Position;
        var transforms = new Dictionary<(WorldObjectDefinition, int), List<Transform3D>>();
        var models = new Dictionary<WorldObjectDefinition, List<Transform3D>>();
        var meshes = new Dictionary<WorldObjectDefinition, (Creations.PartMesh Mesh, List<Transform3D> List)>();
        foreach (var o in LiveObjects)
        {
            var placed = new Transform3D(new Basis(Vector3.Up, o.Yaw).Scaled(new Vector3(o.Scale, o.Scale, o.Scale)), o.Position.ToGodot() - origin);
            if (!Scenery)
            {
                var collider = o.Definition.Collider!;
                // Colliders are sized rather than scaled: physics engines handle unscaled shapes best.
                var shape = new CollisionShape3D
                {
                    Shape = MakeShape(collider, o.Scale),
                    Transform = new Transform3D(new Basis(Vector3.Up, o.Yaw + Mathf.DegToRad(collider.Yaw)), placed * collider.At.ToGodot()),
                };
                (o.Definition.Solid ? (Node)Objects : Brush).AddChild(shape);
                _objectShapes[shape.GetInstanceId()] = o.Id;
            }
            if (FloraModels.Find(o.Definition.Model) is not null)
            {
                if (!models.TryGetValue(o.Definition, out var placements))
                    models[o.Definition] = placements = [];
                placements.Add(placed);
                continue;
            }
            if (MeshOf(o.Definition) is { } mesh)
            {
                if (!meshes.TryGetValue(o.Definition, out var drawn))
                    meshes[o.Definition] = drawn = (mesh, []);
                drawn.List.Add(placed * new Transform3D(Basis.Identity, o.Definition.Mesh!.At.ToGodot()) * mesh.Transform);
                continue;
            }
            var pieces = o.Definition.Pieces ?? [];
            for (var p = 0; p < pieces.Count; p++)
            {
                if (!transforms.TryGetValue((o.Definition, p), out var list))
                    transforms[(o.Definition, p)] = list = [];
                list.Add(placed * Local(pieces[p]));
            }
        }
        foreach (var ((definition, piece), list) in transforms)
        {
            var shape = definition.Pieces![piece];
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = MeshOf(shape), InstanceCount = list.Count };
            for (var i = 0; i < list.Count; i++)
                multi.SetInstanceTransform(i, list[i]);
            Objects.AddChild(new MultiMeshInstance3D { Name = $"{definition.Id}_{piece}", Multimesh = multi, MaterialOverride = MaterialOf(shape.Color) });
        }
        foreach (var (definition, (mesh, list)) in meshes)
        {
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh.Mesh, InstanceCount = list.Count };
            for (var i = 0; i < list.Count; i++)
                multi.SetInstanceTransform(i, list[i]);
            Objects.AddChild(new MultiMeshInstance3D { Name = $"{definition.Id}_mesh", Multimesh = multi });
        }
        foreach (var (definition, list) in models)
        {
            var model = FloraModels.Find(definition.Model)!;
            var multi = FloraField.New(FloraMaterials.Meshes(model).Full, list.Count);
            var tint = FloraTint.SrgbToLinear();
            for (var i = 0; i < list.Count; i++)
            {
                multi.SetInstanceTransform(i, list[i]);
                multi.SetInstanceColor(i, Colors.White);
                multi.SetInstanceCustomData(i, new Color(tint.R, tint.G, tint.B, i * 0.37f % 1f));
            }
            Objects.AddChild(new MultiMeshInstance3D { Name = definition.Id!, Multimesh = multi, MaterialOverride = FloraMaterials.For(model.Kind) });
        }
    }

    private void BuildStructures()
    {
        var origin = Position;
        Structures = new StaticBody3D { Name = "Structures" };
        AddChild(Structures);
        foreach (var piece in Data.Pieces)
        {
            var transform = new Transform3D(new Basis(Vector3.Up, piece.Yaw), piece.Position.ToGodot() - origin);
            if (!Scenery)
                Structures.AddChild(new CollisionShape3D { Shape = MakeShape(piece.Shape, 1f), Transform = transform });
            Structures.AddChild(new MeshInstance3D { Mesh = MeshOf(piece.Shape), MaterialOverride = MaterialOf(piece.Shape.Color), Transform = transform });
        }
        Markers = new Node3D { Name = "Markers" };
        AddChild(Markers);
        foreach (var marker in Data.Markers.Where(_ => !Scenery))
        {
            var node = new Marker3D { Name = marker.Marker.Type!, Position = marker.Position.ToGodot() - origin, Rotation = new Vector3(0, marker.Yaw, 0) };
            node.SetMeta("type", marker.Marker.Type!);
            node.SetMeta("ref", marker.Marker.Ref ?? "");
            node.SetMeta("poi", marker.Poi.Poi.QualifiedId);
            Markers.AddChild(node, forceReadableName: true);
        }
    }

    /// <summary>
    /// A copy of <paramref name="o"/>'s pieces in world space drawn with <paramref name="material"/> (the blue hover
    /// outline round a resource node, UI spec §10).
    /// </summary>
    public static Node3D Shell(WorldObjectPlacement o, Material material)
    {
        var root = new Node3D { Name = "Shell", Transform = new Transform3D(new Basis(Vector3.Up, o.Yaw).Scaled(new Vector3(o.Scale, o.Scale, o.Scale)), o.Position.ToGodot()) };
        if (FloraModels.Find(o.Definition.Model) is { } model)
        {
            root.AddChild(new MeshInstance3D { Mesh = FloraMaterials.Meshes(model).Full, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            return root;
        }
        foreach (var piece in o.Definition.Pieces ?? [])
            root.AddChild(new MeshInstance3D { Mesh = MeshOf(piece), MaterialOverride = material, Transform = Local(piece), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return root;
    }

    // The model an object is drawn with, loaded once per file; null when it has none or it failed to load (pieces are drawn).
    private Creations.PartMesh? MeshOf(WorldObjectDefinition definition)
    {
        if (WorldGen?.MeshFileOf(definition) is not { } file)
            return null;
        if (!ObjectMeshes.TryGetValue(file, out var mesh))
            ObjectMeshes[file] = mesh = Creations.PartAssets.ReadGltf(file, definition.Mesh!.File!)?.FirstOrDefault();
        return mesh;
    }

    // Leaf colour of world objects drawn with a flora model (the harvestable bush).
    private static readonly Color FloraTint = new(0.36f, 0.68f, 0.24f);

    // A piece's own placement relative to its owner: turned about +Y, then moved to its centre.
    private static Transform3D Local(ShapeDefinition shape) => new(new Basis(Vector3.Up, Mathf.DegToRad(shape.Yaw)), shape.At.ToGodot());

    private static Shape3D MakeShape(ShapeDefinition shape, float scale) => shape.Shape switch
    {
        PrimitiveShape.Box => new BoxShape3D { Size = shape.Size!.Value.ToGodot() * scale },
        PrimitiveShape.Sphere => new SphereShape3D { Radius = shape.Radius!.Value * scale },
        _ => new CylinderShape3D { Radius = shape.Radius!.Value * scale, Height = shape.Height!.Value * scale },
    };

    private static Mesh MeshOf(ShapeDefinition shape)
    {
        if (PieceMeshes.TryGetValue(shape, out var mesh))
            return mesh;
        mesh = shape.Shape switch
        {
            PrimitiveShape.Box => new BoxMesh { Size = shape.Size!.Value.ToGodot() },
            PrimitiveShape.Sphere => new SphereMesh { Radius = shape.Radius!.Value, Height = 2f * shape.Radius!.Value, RadialSegments = 12, Rings = 6 },
            _ => new CylinderMesh { TopRadius = shape.Radius!.Value, BottomRadius = shape.Radius!.Value, Height = shape.Height!.Value, RadialSegments = 10, Rings = 1 },
        };
        PieceMeshes[shape] = mesh;
        return mesh;
    }

    private static StandardMaterial3D MaterialOf(string? color)
    {
        var key = color ?? "";
        if (!Materials.TryGetValue(key, out var material))
        {
            var c = WorldGenData.ParseColor(color);
            Materials[key] = material = new StandardMaterial3D { AlbedoColor = new Color(c.X, c.Y, c.Z) };
        }
        return material;
    }
}
