using System.Text.Json;
using Mechanica.Core.World;

namespace Mechanica.Tools.FloraDump;

/// <summary>
/// Writes the full meshes of the named built-in flora models (<see cref="FloraModels"/>) to a JSON file: per model its
/// flat-shaded triangles' positions, normals and vertex colours (shade, accent, tint mask) as flat float lists.
/// Usage: <c>dotnet run --project tools/nature_parts/flora_dump -- OUT.json model [model ...]</c>.
/// </summary>
public static class Program
{
    /// <summary>Entry point.</summary>
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: FloraDump OUT.json model [model ...]");
            return 2;
        }
        var models = new Dictionary<string, object>();
        foreach (var id in args.Skip(1))
        {
            if (FloraModels.Find(id) is not { } model)
            {
                Console.Error.WriteLine($"no flora model '{id}'");
                return 1;
            }
            var mesh = model.Mesh;
            models[id] = new
            {
                kind = model.Kind.ToString(),
                positions = mesh.Positions.SelectMany(p => new[] { p.X, p.Y, p.Z }),
                normals = mesh.Normals.SelectMany(n => new[] { n.X, n.Y, n.Z }),
                colors = mesh.Colors.SelectMany(c => new[] { c.X, c.Y, c.Z, c.W }),
            };
        }
        File.WriteAllText(args[0], JsonSerializer.Serialize(models));
        return 0;
    }
}
