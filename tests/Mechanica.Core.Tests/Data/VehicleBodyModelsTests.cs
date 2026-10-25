using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// Parts audit D3: the vehicle body parts (bumpers, mud flap, exhaust pipe, license plate, roof rack) have built models
/// and icons (tools/vehicle_body/build_vehicle_body.py) in place of the chunk-23 placeholder shapes, keep their grid
/// sizes, and keep the cheap scrap-tier Fabricator recipes in the Vehicle body group (1500) as the audit says.
/// </summary>
public class VehicleBodyModelsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("bumper_front", 8, 1, 1)]
    [InlineData("bumper_rear", 8, 1, 1)]
    [InlineData("mud_flap", 2, 2, 1)]
    [InlineData("exhaust_pipe", 1, 3, 1)]
    [InlineData("license_plate", 2, 1, 1)]
    [InlineData("roof_rack", 6, 1, 8)]
    public void VehicleBodyPartsHaveTheirModelAndIcon(string id, int x, int y, int z)
    {
        var part = Part(id);
        var vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

        Assert.Equal(new GridSize(x, y, z), part.Size);
        Assert.Equal($"meshes/parts/{id}.glb", part.Mesh);
        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        Assert.True(File.Exists(Path.Combine(vanilla, part.Mesh!)), $"{id} has its model");
        Assert.True(File.Exists(Path.Combine(vanilla, part.Icon!)), $"{id} has its icon");
        Assert.False(File.Exists(Path.Combine(vanilla, "meshes", "decor", $"{id}.glb")), $"{id}'s placeholder mesh is gone");
    }

    [Theory]
    [InlineData("bumper_front", 1510, 12)]
    [InlineData("bumper_rear", 1520, 12)]
    [InlineData("mud_flap", 1530, 4)]
    [InlineData("exhaust_pipe", 1540, 4)]
    [InlineData("license_plate", 1550, 4)]
    [InlineData("roof_rack", 1560, 8)]
    public void VehicleBodyPartsKeepTheirScrapRecipes(string id, int order, int scrapMetal)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find(id)!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal([$"vanilla:scrap_metal_block:{scrapMetal}"], recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal($"vanilla:{id}:1", $"{recipe.Outputs.Single().Item}:{recipe.Outputs.Single().Count}");
        Assert.Equal(2, recipe.Time);
        Assert.Equal(order, recipe.Definition.Order);
        Assert.Single(book.All, r => r.Outputs.Any(o => o.Item == $"vanilla:{id}"));
    }
}
