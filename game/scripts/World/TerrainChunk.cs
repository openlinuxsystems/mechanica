using Godot;
using Mechanica.Core.World;

namespace Mechanica.Game.World;

/// <summary>
/// One streamed chunk of generated ground (chunk 19, D11), built from a Core <see cref="TerrainChunkData"/>:
/// <list type="bullet">
/// <item>the ground: a vertex-coloured mesh, and a <see cref="HeightMapShape3D"/> (<see cref="GroundShape"/>)
/// added to the streamer's one shared <see cref="Ground"/> body. Every chunk's height map is a shape of
/// that one body, so Jolt's internal edge removal sees across chunk borders and wheels roll over them
/// smoothly; with a body per chunk they bumped on the edge of the chunk they were leaving.</item>
/// <item>a sea surface where the ground dips below sea level (visual only until water exists);</item>
/// <item><see cref="Objects"/>: one static body holding a collider per scattered object that is not
/// destroyed, drawn with one <see cref="MultiMesh"/> per object piece;</item>
/// <item><see cref="Structures"/>: the static pieces of POIs whose centre lies in this chunk, and
/// <see cref="Markers"/> for later systems.</item>
/// </list>
/// Meshes, materials and the sea are shared between chunks.
/// </summary>
public partial class TerrainChunk : Node3D
{
    private static readonly StandardMaterial3D GroundMaterial = new() { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.95f };
    private static readonly StandardMaterial3D WaterMaterial = new()
    {
        AlbedoColor = new Color(0.17f, 0.36f, 0.62f, 0.75f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.1f,
    };
    private static readonly Dictionary<float, PlaneMesh> WaterMeshes = [];
    private static readonly Dictionary<ShapeDefinition, Mesh> PieceMeshes = [];
    private static readonly Dictionary<string, StandardMaterial3D> Materials = [];

    private readonly Dictionary<ulong, WorldObjectId> _objectShapes = [];

    /// <summary>The generated chunk. Set before adding to the tree.</summary>
    public TerrainChunkData Data { get; init; } = null!;

    /// <summary>Which objects are destroyed. Set before adding to the tree; call <see cref="RefreshObjects"/> after changing it.</summary>
    public WorldState State { get; set; } = null!;

    /// <summary>The static body every loaded chunk's ground shape belongs to, a sibling of this node. Set before adding to the tree.</summary>
    public StaticBody3D Ground { get; init; } = null!;

    /// <summary>This chunk's height map collider, a child of <see cref="Ground"/> while the chunk is in the tree.</summary>
    public CollisionShape3D GroundShape { get; private set; } = null!;

    /// <summary>Which chunk this is.</summary>
    public ChunkCoord Coord => Data.Coord;

    /// <summary>The body holding scattered objects' colliders.</summary>
    public StaticBody3D Objects { get; private set; } = null!;

    /// <summary>The body holding POI structure pieces.</summary>
    public StaticBody3D Structures { get; private set; } = null!;

    /// <summary>POI markers, one <see cref="Marker3D"/> each, with <c>type</c>, <c>ref</c> and <c>poi</c> metadata.</summary>
    public Node3D Markers { get; private set; } = null!;

    /// <summary>Objects in this chunk that are not destroyed.</summary>
    public IEnumerable<WorldObjectPlacement> LiveObjects => Data.Objects.Where(o => !State.IsDestroyed(o.Id));

    public override void _Ready()
    {
        Name = $"Chunk_{Coord.X}_{Coord.Z}";
        var (x, z) = Coord.Origin(Data.Size);
        Position = new Vector3(x, 0, z);
        BuildGround();
        Objects = new StaticBody3D { Name = "Objects" };
        AddChild(Objects);
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

    /// <summary>The object a collision with <see cref="Objects"/> hit, from the hit's shape index.</summary>
    public WorldObjectId? ObjectAt(int shapeIndex)
    {
        if (shapeIndex < 0 || shapeIndex >= Objects.GetShapeOwners().Length)
            return null;
        var owner = Objects.ShapeFindOwner(shapeIndex);
        return Objects.ShapeOwnerGetOwner(owner) is CollisionShape3D shape && _objectShapes.TryGetValue(shape.GetInstanceId(), out var id) ? id : null;
    }

    /// <summary>Rebuilds the objects, after one was destroyed or <see cref="State"/> changed.</summary>
    public void RefreshObjects()
    {
        foreach (var child in Objects.GetChildren())
        {
            Objects.RemoveChild(child);
            child.QueueFree();
        }
        _objectShapes.Clear();
        BuildObjects();
    }

    private void BuildGround()
    {
        var n = Data.Samples;
        var res = Data.Resolution;
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

        var vertices = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var colors = new Color[n * n];
        for (var j = 0; j < n; j++)
        {
            for (var i = 0; i < n; i++)
            {
                var k = j * n + i;
                vertices[k] = new Vector3(i * res, Data.Heights[k], j * res);
                normals[k] = Data.Normals[k].ToGodot();
                var c = Data.Colors[k];
                colors[k] = new Color(c.X, c.Y, c.Z);
            }
        }
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
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        AddChild(new MeshInstance3D { Name = "Mesh", Mesh = mesh, MaterialOverride = GroundMaterial });

        if (Data.HasWater)
        {
            if (!WaterMeshes.TryGetValue(Data.Size, out var water))
                WaterMeshes[Data.Size] = water = new PlaneMesh { Size = new Vector2(Data.Size, Data.Size) };
            AddChild(new MeshInstance3D
            {
                Name = "Water",
                Mesh = water,
                MaterialOverride = WaterMaterial,
                Position = new Vector3(Data.Size / 2f, Data.SeaLevel, Data.Size / 2f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    private void BuildObjects()
    {
        var origin = Position;
        var transforms = new Dictionary<(WorldObjectDefinition, int), List<Transform3D>>();
        foreach (var o in LiveObjects)
        {
            var placed = new Transform3D(new Basis(Vector3.Up, o.Yaw).Scaled(new Vector3(o.Scale, o.Scale, o.Scale)), o.Position.ToGodot() - origin);
            var collider = o.Definition.Collider!;
            // Colliders are sized rather than scaled: physics engines handle unscaled shapes best.
            var shape = new CollisionShape3D
            {
                Shape = MakeShape(collider, o.Scale),
                Transform = new Transform3D(new Basis(Vector3.Up, o.Yaw + Mathf.DegToRad(collider.Yaw)), placed * collider.At.ToGodot()),
            };
            Objects.AddChild(shape);
            _objectShapes[shape.GetInstanceId()] = o.Id;
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
    }

    private void BuildStructures()
    {
        var origin = Position;
        Structures = new StaticBody3D { Name = "Structures" };
        AddChild(Structures);
        foreach (var piece in Data.Pieces)
        {
            var transform = new Transform3D(new Basis(Vector3.Up, piece.Yaw), piece.Position.ToGodot() - origin);
            Structures.AddChild(new CollisionShape3D { Shape = MakeShape(piece.Shape, 1f), Transform = transform });
            Structures.AddChild(new MeshInstance3D { Mesh = MeshOf(piece.Shape), MaterialOverride = MaterialOf(piece.Shape.Color), Transform = transform });
        }
        Markers = new Node3D { Name = "Markers" };
        AddChild(Markers);
        foreach (var marker in Data.Markers)
        {
            var node = new Marker3D { Name = marker.Marker.Type!, Position = marker.Position.ToGodot() - origin, Rotation = new Vector3(0, marker.Yaw, 0) };
            node.SetMeta("type", marker.Marker.Type!);
            node.SetMeta("ref", marker.Marker.Ref ?? "");
            node.SetMeta("poi", marker.Poi.Poi.QualifiedId);
            Markers.AddChild(node, forceReadableName: true);
        }
    }

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
