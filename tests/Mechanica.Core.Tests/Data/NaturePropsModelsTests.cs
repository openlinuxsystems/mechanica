using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// Parts audit D6: the Creative nature props (Boulder, Large Rock, Small Rock, Bush, Grass Tuft, Flower Patch, Fallen Log,
/// Tree Trunk) have models built from the world's own meshes (tools/nature_parts/build_nature_parts.py) and icons in
/// place of the chunk-23 placeholder shapes, each model fits its grid box, and the props keep their stats and recipes:
/// none for the rocks and flora (Creative only), Scrap Wood Blocks for the log and the trunk.
/// </summary>
public class NaturePropsModelsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));
    private static readonly string VanillaDir = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("boulder", 6, 5, 6)]
    [InlineData("large_rock", 3, 3, 3)]
    [InlineData("small_rock", 1, 1, 1)]
    [InlineData("bush", 5, 4, 5)]
    [InlineData("grass_tuft", 2, 3, 2)]
    [InlineData("flower_patch", 2, 1, 2)]
    [InlineData("fallen_log", 3, 3, 8)]
    [InlineData("tree_trunk", 5, 8, 5)]
    public void NaturePropsHaveTheirModelAndIcon(string id, int x, int y, int z)
    {
        var part = Part(id);

        Assert.Equal(new GridSize(x, y, z), part.Size);
        Assert.Equal($"meshes/parts/{id}.glb", part.Mesh);
        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        Assert.True(File.Exists(Path.Combine(VanillaDir, part.Mesh!)), $"{id} has its model");
        Assert.True(File.Exists(Path.Combine(VanillaDir, part.Icon!)), $"{id} has its icon");
        Assert.False(File.Exists(Path.Combine(VanillaDir, "meshes", "decor", $"{id}.glb")), $"{id}'s placeholder mesh is gone");
    }

    [Theory]
    [InlineData("boulder")]
    [InlineData("large_rock")]
    [InlineData("small_rock")]
    [InlineData("bush")]
    [InlineData("grass_tuft")]
    [InlineData("flower_patch")]
    [InlineData("fallen_log")]
    [InlineData("tree_trunk")]
    public void EachModelFitsItsGridBoxAndRestsOnItsFloor(string id)
    {
        var part = Part(id);
        var (min, max) = GlbBounds(Path.Combine(VanillaDir, part.Mesh!));
        var half = new Vector3(part.Size!.Value.X, part.Size.Value.Y, part.Size.Value.Z) * (MechanicaInfo.GridSize / 2f);
        const float slack = 0.001f;

        Assert.True(Vector3.Max(max, half + new Vector3(slack)) == half + new Vector3(slack), $"{id} reaches {max}, past +{half}");
        Assert.True(Vector3.Min(min, -half - new Vector3(slack)) == -half - new Vector3(slack), $"{id} reaches {min}, past -{half}");
        // It fills its box: no more than one block of air across, and it stands on the box's floor.
        var size = max - min;
        var box = half * 2f;
        Assert.True(box.X - size.X < MechanicaInfo.GridSize && box.Y - size.Y < MechanicaInfo.GridSize && box.Z - size.Z < MechanicaInfo.GridSize,
            $"{id}'s box {box} is a block bigger than its model {size}");
        Assert.InRange(min.Y + half.Y, 0f, 0.01f);
    }

    [Theory]
    [InlineData("boulder")]
    [InlineData("large_rock")]
    [InlineData("fallen_log")]
    [InlineData("tree_trunk")]
    public void TheHeavyPropsCollideAsTheirModel(string id)
    {
        var part = Part(id);

        Assert.Equal(CollisionShape.Convex, part.Collision!.Shape);
        Assert.Equal(part.Mesh, part.Collision.Mesh);
    }

    [Theory]
    [InlineData("boulder", 900, 30)]
    [InlineData("large_rock", 150, 15)]
    [InlineData("small_rock", 6, 6)]
    [InlineData("bush", 4, 1)]
    [InlineData("grass_tuft", 0.2, 1)]
    [InlineData("flower_patch", 0.3, 1)]
    [InlineData("fallen_log", 160, 10)]
    [InlineData("tree_trunk", 200, 12)]
    public void NaturePropsKeepTheirStats(string id, double mass, int durability)
    {
        var part = Part(id);

        Assert.Equal(mass, part.Mass!.Value, 3);
        Assert.Equal(durability, part.Durability);
    }

    [Theory]
    [InlineData("boulder")]
    [InlineData("large_rock")]
    [InlineData("small_rock")]
    [InlineData("bush")]
    [InlineData("grass_tuft")]
    [InlineData("flower_patch")]
    public void RocksAndFloraStayCreativeOnly(string id)
    {
        var book = RecipeBook.Load(Vanilla.Value);

        Assert.DoesNotContain(book.All, r => r.Outputs.Any(o => o.Item == $"vanilla:{id}"));
    }

    [Theory]
    [InlineData("fallen_log", 2310, 32)]
    [InlineData("tree_trunk", 2320, 40)]
    public void TheLogAndTrunkKeepTheirScrapWoodRecipes(string id, int order, int scrapWood)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find(id)!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal([$"vanilla:scrap_wood_block:{scrapWood}"], recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal($"vanilla:{id}:1", $"{recipe.Outputs.Single().Item}:{recipe.Outputs.Single().Count}");
        Assert.Equal(order, recipe.Definition.Order);
    }

    // The bounds of every mesh in a .glb, through its nodes' transforms, in glTF axes (y up).
    private static (Vector3 Min, Vector3 Max) GlbBounds(string file)
    {
        var bytes = File.ReadAllBytes(file);
        using var doc = JsonDocument.Parse(bytes.AsMemory(20, BitConverter.ToInt32(bytes, 12)));
        var root = doc.RootElement;
        var nodes = root.GetProperty("nodes");
        var accessors = root.GetProperty("accessors");
        var meshes = root.GetProperty("meshes");
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        static Vector3 Vec(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

        void Visit(int index, Matrix4x4 parent)
        {
            var node = nodes[index];
            var scale = node.TryGetProperty("scale", out var s) ? Vec(s) : Vector3.One;
            var rotation = node.TryGetProperty("rotation", out var r)
                ? new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle())
                : Quaternion.Identity;
            var translation = node.TryGetProperty("translation", out var t) ? Vec(t) : Vector3.Zero;
            var world = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation) * parent;
            if (node.TryGetProperty("mesh", out var m))
            {
                foreach (var primitive in meshes[m.GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    var accessor = accessors[primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                    Vector3 lo = Vec(accessor.GetProperty("min")), hi = Vec(accessor.GetProperty("max"));
                    for (var c = 0; c < 8; c++)
                    {
                        var corner = Vector3.Transform(new Vector3((c & 1) == 0 ? lo.X : hi.X, (c & 2) == 0 ? lo.Y : hi.Y, (c & 4) == 0 ? lo.Z : hi.Z), world);
                        min = Vector3.Min(min, corner);
                        max = Vector3.Max(max, corner);
                    }
                }
            }
            if (node.TryGetProperty("children", out var children))
                foreach (var child in children.EnumerateArray())
                    Visit(child.GetInt32(), world);
        }

        foreach (var index in root.GetProperty("scenes")[root.TryGetProperty("scene", out var sc) ? sc.GetInt32() : 0].GetProperty("nodes").EnumerateArray())
            Visit(index.GetInt32(), Matrix4x4.Identity);
        return (min, max);
    }
}
