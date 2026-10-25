using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// Parts audit batch D4: the chunk-23 Chair, Stool, Wooden Table, Pallet, Door Frame, Window Frame and Stairs Block have
/// built models and icons (tools/decor/furniture_d4.py) in place of their Part Editor boxes, the Door Frame is the
/// art-direction door (1.25 x 2.5 m) with a collision a player walks through, and their recipes and orders are the
/// ones the audit's fix batches left.
/// </summary>
public class FurnitureModelsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static string VanillaDir => Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("chair", 2, 4, 2)]
    [InlineData("stool", 2, 2, 2)]
    [InlineData("wooden_table", 6, 3, 4)]
    [InlineData("pallet", 5, 1, 5)]
    [InlineData("door_frame", 5, 10, 1)]
    [InlineData("window_frame", 4, 4, 1)]
    [InlineData("stairs_block", 4, 4, 4)]
    public void EachPartHasItsModelAndIcon(string id, int x, int y, int z)
    {
        var part = Part(id);

        Assert.Equal(new GridSize(x, y, z), part.Size);
        Assert.Equal($"meshes/parts/{id}.glb", part.Mesh);
        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        Assert.True(File.Exists(Path.Combine(VanillaDir, part.Mesh!)), $"{id} has its model");
        Assert.True(File.Exists(Path.Combine(VanillaDir, part.Icon!)), $"{id} has its icon");
        // The model carries its own colours: no tint or texture over it, and the old Part Editor box model is gone.
        Assert.Equal("#ffffff", part.Visual?.Color);
        Assert.Null(part.Visual?.Texture);
        Assert.False(File.Exists(Path.Combine(VanillaDir, "meshes", "decor", $"{id}.glb")), $"{id}'s placeholder mesh is removed");
    }

    [Fact]
    public void TheDoorFrameIsTheArtDirectionDoor()
    {
        var door = Part("door_frame");

        Assert.Equal(new GridSize(5, 10, 1), door.Size);
        Assert.Equal(new Vec3(1.25f, 2.5f, 0.25f), door.Size!.Value.Metres);
    }

    /// <summary>
    /// The frames collide as their posts and head (or rails), not as a box filling the footprint, so a player walks
    /// through the Door Frame's 1.05 x 2.3 m opening and the Window Frame's 0.84 m opening stays open.
    /// </summary>
    [Theory]
    [InlineData("door_frame", 1.05, 2.3)]
    [InlineData("window_frame", 0.84, 0.84)]
    public void TheFramesLeaveTheirOpeningClear(string id, float width, float height)
    {
        var part = Part(id);
        var size = part.Size!.Value.Metres;
        var collision = part.Collision!;

        Assert.Equal(CollisionShape.Boxes, collision.Shape);
        var boxes = collision.Boxes!;
        // The opening: centred across, from the floor (a door) or centred (a window), through the whole depth.
        var bottom = id == "door_frame" ? -size.Y / 2 : -height / 2;
        var (x0, x1, y0, y1) = (-width / 2, width / 2, bottom, bottom + height);
        foreach (var box in boxes)
        {
            var c = box.Offset ?? Vec3.Zero;
            var h = box.Size!.Value * 0.5f;
            var overlaps = c.X - h.X < x1 - 1e-4f && c.X + h.X > x0 + 1e-4f && c.Y - h.Y < y1 - 1e-4f && c.Y + h.Y > y0 + 1e-4f;
            Assert.False(overlaps, $"{id}'s collision box at {c} reaches into its opening");
            Assert.True(MathF.Abs(c.X) + h.X <= size.X / 2 + 1e-4f && MathF.Abs(c.Y) + h.Y <= size.Y / 2 + 1e-4f
                && MathF.Abs(c.Z) + h.Z <= size.Z / 2 + 1e-4f, $"{id}'s collision box at {c} stays in the footprint");
        }
        // Posts on both sides and something over the opening.
        Assert.Contains(boxes, b => (b.Offset ?? Vec3.Zero).X < x0);
        Assert.Contains(boxes, b => (b.Offset ?? Vec3.Zero).X > x1);
        Assert.Contains(boxes, b => (b.Offset ?? Vec3.Zero).Y > y1);
    }

    [Fact]
    public void TheStairsBlockCollidesAsTheHullOfItsModel() =>
        Assert.Equal(new CollisionDefinition { Shape = CollisionShape.Convex, Mesh = "meshes/parts/stairs_block.glb" }, Part("stairs_block").Collision);

    /// <summary>The models changed, the numbers did not: the stats and recipes the audit's fix batches set.</summary>
    [Theory]
    [InlineData("chair", 3, 3, 0.5, 0.6, true)]
    [InlineData("stool", 3, 3, 0.5, 0.6, true)]
    [InlineData("wooden_table", 6, 3, 0.5, 0.6, true)]
    [InlineData("pallet", 6, 3, 0.5, 0.6, true)]
    [InlineData("door_frame", 6, 3, 0.5, 0.6, true)]
    [InlineData("window_frame", 4.5, 3, 0.5, 0.6, true)]
    [InlineData("stairs_block", 6, 5, 0.5, 0.2, false)]
    public void TheStatsAreUnchanged(string id, double mass, int durability, double friction, double buoyancy, bool flammable)
    {
        var part = Part(id);

        Assert.Equal((mass, durability, friction, buoyancy, flammable, 20),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, part.StackSize!.Value));
    }

    [Theory]
    [InlineData("chair", 2020, "scrap_wood_block:8")]
    [InlineData("stool", 2010, "scrap_wood_block:4")]
    [InlineData("wooden_table", 2030, "scrap_wood_block:16")]
    [InlineData("pallet", 2060, "scrap_wood_block:12")]
    [InlineData("door_frame", 360, "scrap_wood_block:16")]
    [InlineData("window_frame", 370, "scrap_wood_block:8")]
    [InlineData("stairs_block", 211, "scrap_metal_block:8", "scrap_wood_block:8")]
    public void TheRecipesAndOrdersAreUnchanged(string id, int order, params string[] inputs)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find(id)!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(inputs.Select(i => $"vanilla:{i}"), recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(1, recipe.Outputs.Single().Count);
        Assert.Equal(2, recipe.Time);
        Assert.Equal(order, recipe.Definition.Order);
        Assert.Single(book.All, r => r.Outputs.Any(o => o.Item == $"vanilla:{id}"));
    }
}
