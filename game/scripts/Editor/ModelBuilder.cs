using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Editor;

/// <summary>
/// Turns a part's <see cref="PartSource"/> model pieces into Godot nodes for the editor preview, and
/// bakes them into glTF bytes for export. A model that only adds pieces is built from plain meshes, so
/// imported glTF files keep their own geometry and materials. A model with any subtract or intersect
/// piece is built as a CSG tree, which Godot evaluates on the next frames; bake it only after that.
/// </summary>
public static class ModelBuilder
{
    private const int CurveSegments = 32;

    /// <summary>True when any piece cuts or intersects, so the model needs CSG.</summary>
    public static bool UsesCsg(PartSource source) =>
        source.Pieces.Any(p => (p.Operation ?? CsgOperation.Union) != CsgOperation.Union);

    /// <summary>
    /// Builds the model. <paramref name="meshes"/> loads a mesh piece's glTF file (mod-relative path) or
    /// returns null when it cannot, in which case the piece is skipped. Invalid pieces are skipped too.
    /// </summary>
    public static Node3D Build(PartSource source, Func<string, IReadOnlyList<PartMesh>?> meshes)
    {
        if (UsesCsg(source))
        {
            var combiner = new CsgCombiner3D { Name = "Model" };
            foreach (var piece in source.Pieces)
            {
                if (CsgPiece(piece, meshes) is { } node)
                    combiner.AddChild(node);
            }
            return combiner;
        }

        var root = new Node3D { Name = "Model" };
        foreach (var piece in source.Pieces)
        {
            var transform = PieceTransform(piece);
            foreach (var mesh in PieceMeshes(piece, meshes))
                root.AddChild(new MeshInstance3D { Mesh = mesh.Mesh, Transform = transform * mesh.Transform });
        }
        return root;
    }

    /// <summary>
    /// The glTF binary (<c>.glb</c>) of a model made by <see cref="Build"/>, or null when it has no
    /// geometry. A CSG model must be in the tree and evaluated (two frames after its last change).
    /// </summary>
    public static byte[]? Bake(Node3D model)
    {
        var scene = new Node3D { Name = "Part" };
        try
        {
            if (model is CsgCombiner3D combiner)
            {
                if (combiner.BakeStaticMesh() is not { } baked || baked.GetSurfaceCount() == 0)
                    return null;
                AddOwned(scene, new MeshInstance3D { Name = "Mesh", Mesh = baked });
            }
            else
            {
                var index = 0;
                foreach (var instance in model.GetChildren().OfType<MeshInstance3D>())
                    AddOwned(scene, new MeshInstance3D { Name = $"Mesh{index++}", Mesh = instance.Mesh, Transform = instance.Transform });
                if (index == 0)
                    return null;
            }
            var document = new GltfDocument();
            var state = new GltfState();
            if (document.AppendFromScene(scene, state) != Error.Ok)
                return null;
            var bytes = document.GenerateBuffer(state);
            return bytes.Length > 0 ? bytes : null;
        }
        finally
        {
            scene.Free();
        }
    }

    /// <summary>The piece's placement relative to the part centre.</summary>
    public static Transform3D PieceTransform(ModelPiece piece)
    {
        var rotation = (piece.Rotation ?? Vec3.Zero).ToGodot() * (Mathf.Pi / 180f);
        var basis = Basis.FromEuler(rotation, EulerOrder.Yxz).Scaled(Vector3.One * (piece.Scale ?? 1f));
        return new Transform3D(basis, (piece.Position ?? Vec3.Zero).ToGodot());
    }

    // The piece's geometry as meshes in piece-local space.
    private static IEnumerable<PartMesh> PieceMeshes(ModelPiece piece, Func<string, IReadOnlyList<PartMesh>?> meshes)
    {
        if (piece.Kind == ModelPieceKind.Mesh)
            return piece.Mesh is { } path ? meshes(path) ?? [] : [];
        return Primitive(piece) is { } mesh ? [new PartMesh(mesh, PrimitiveTransform(piece))] : [];
    }

    private static Node3D? CsgPiece(ModelPiece piece, Func<string, IReadOnlyList<PartMesh>?> meshes)
    {
        var parts = PieceMeshes(piece, meshes).ToList();
        if (parts.Count == 0)
            return null;
        CsgShape3D node;
        if (parts.Count == 1)
        {
            node = new CsgMesh3D { Mesh = parts[0].Mesh, Transform = PieceTransform(piece) * parts[0].Transform };
        }
        else
        {
            node = new CsgCombiner3D { Transform = PieceTransform(piece) };
            foreach (var part in parts)
                node.AddChild(new CsgMesh3D { Mesh = part.Mesh, Transform = part.Transform });
        }
        node.Operation = (piece.Operation ?? CsgOperation.Union) switch
        {
            CsgOperation.Subtract => CsgShape3D.OperationEnum.Subtraction,
            CsgOperation.Intersect => CsgShape3D.OperationEnum.Intersection,
            _ => CsgShape3D.OperationEnum.Union,
        };
        return node;
    }

    // Primitives become array meshes so glTF export and CSG both see real vertex data.
    private static ArrayMesh? Primitive(ModelPiece piece)
    {
        PrimitiveMesh? primitive = piece.Kind switch
        {
            ModelPieceKind.Box when piece.Size is { } s && s.X > 0 && s.Y > 0 && s.Z > 0 => new BoxMesh { Size = s.ToGodot() },
            // PrismMesh puts the upright side at -X when LeftToRight is 0; PrimitiveTransform turns it to -Z.
            ModelPieceKind.Wedge when piece.Size is { } s && s.X > 0 && s.Y > 0 && s.Z > 0 =>
                new PrismMesh { Size = new Vector3(s.Z, s.Y, s.X), LeftToRight = 0 },
            ModelPieceKind.Cylinder when piece.Radius > 0 && piece.Height > 0 =>
                new CylinderMesh { TopRadius = piece.Radius.Value, BottomRadius = piece.Radius.Value, Height = piece.Height.Value, RadialSegments = CurveSegments },
            ModelPieceKind.Sphere when piece.Radius > 0 =>
                new SphereMesh { Radius = piece.Radius.Value, Height = piece.Radius.Value * 2, RadialSegments = CurveSegments, Rings = CurveSegments / 2 },
            _ => null,
        };
        if (primitive is null)
            return null;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, primitive.GetMeshArrays());
        return mesh;
    }

    private static Transform3D PrimitiveTransform(ModelPiece piece) => piece.Kind == ModelPieceKind.Wedge
        ? new Transform3D(new Basis(Vector3.Up, -Mathf.Pi / 2), Vector3.Zero)
        : Transform3D.Identity;

    private static void AddOwned(Node3D scene, Node3D child)
    {
        scene.AddChild(child);
        child.Owner = scene;
    }
}
