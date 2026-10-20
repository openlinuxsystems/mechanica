using Godot;

namespace Mechanica.Game.Mods;

/// <summary>
/// Loads a mod's glTF model (.glb or .gltf) into a scene the way the glTF spec reads it: a mesh's <c>COLOR_0</c> vertex
/// colours multiply its material's base colour. Godot's runtime loader keeps the colours but leaves them unused, so a
/// model whose colours live in its vertices (fruit, grime on a structure) would come out white.
/// </summary>
public static class ModGltf
{
    /// <summary>The model at <paramref name="file"/> as a new scene, or null (and <paramref name="error"/>) when it does not load.</summary>
    public static Node? Load(string file, out Error error)
    {
        var document = new GltfDocument();
        var state = new GltfState();
        error = document.AppendFromFile(file, state);
        if (error != Error.Ok)
            return null;
        var scene = document.GenerateScene(state);
        if (scene is not null)
            UseVertexColors(scene);
        return scene;
    }

    /// <summary>The model at <paramref name="file"/> as a new scene, or null when it does not load.</summary>
    public static Node? Load(string file) => Load(file, out _);

    // Every surface that carries vertex colours gets them as albedo, multiplying its base colour.
    private static void UseVertexColors(Node node)
    {
        if (node is MeshInstance3D { Mesh: ArrayMesh mesh })
        {
            for (var i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                if ((mesh.SurfaceGetFormat(i) & Mesh.ArrayFormat.FormatColor) != 0 && mesh.SurfaceGetMaterial(i) is BaseMaterial3D material)
                    material.VertexColorUseAsAlbedo = true;
            }
        }
        else if (node is ImporterMeshInstance3D { Mesh: { } importer })
        {
            for (var i = 0; i < importer.GetSurfaceCount(); i++)
            {
                if ((importer.GetSurfaceFormat(i) & (ulong)Mesh.ArrayFormat.FormatColor) != 0
                    && importer.GetSurfaceMaterial(i) is BaseMaterial3D material)
                    material.VertexColorUseAsAlbedo = true;
            }
        }
        foreach (var child in node.GetChildren())
            UseVertexColors(child);
    }
}
