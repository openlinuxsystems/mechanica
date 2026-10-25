using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// The parts audit's batch D5 (signs and site): the Workshop Sign, the Warehouse Key Sign, the Speed Limit Sign and the
/// Barrier Tape have their own model and icon (tools/decor/build_site_signs.py), no Part Editor placeholder, and keep
/// the recipes and orders the audit kept.
/// </summary>
public class SiteSignsModelsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));
    private static readonly string Mod = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("outpost_sign")]
    [InlineData("warehouse_key_sign")]
    [InlineData("speed_limit_sign")]
    [InlineData("barrier_tape")]
    public void HasItsOwnModelAndIcon(string id)
    {
        var part = Part(id);

        Assert.Equal($"meshes/parts/{id}.glb", part.Mesh);
        Assert.True(File.Exists(Path.Combine(Mod, part.Mesh!)), $"{id} has a model");
        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        Assert.True(File.Exists(Path.Combine(Mod, part.Icon!)), $"{id} has an icon");
        Assert.False(File.Exists(Path.Combine(Mod, "meshes", "decor", $"{id}.glb")), $"{id}'s placeholder model is gone");
    }

    [Theory]
    [InlineData("outpost_sign", 3, 6, 1)]
    [InlineData("warehouse_key_sign", 3, 6, 1)]
    [InlineData("speed_limit_sign", 3, 6, 1)]
    [InlineData("barrier_tape", 8, 4, 1)]
    public void KeepsItsSize(string id, int x, int y, int z) => Assert.Equal(new GridSize(x, y, z), Part(id).Size);

    [Theory]
    [InlineData("outpost_sign", 2230)]
    [InlineData("warehouse_key_sign", 2240)]
    [InlineData("speed_limit_sign", 2150)]
    [InlineData("barrier_tape", 2220)]
    public void KeepsItsRecipeAndOrder(string id, int order)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find(id)!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(["vanilla:scrap_metal_block:4"], recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal($"vanilla:{id}", recipe.Outputs.Single().Item);
        Assert.Equal(1, recipe.Outputs.Single().Count);
        Assert.Equal(2f, recipe.Time);
        Assert.Equal(order, recipe.Definition.Order);
        Assert.Single(book.All, r => r.Outputs.Any(o => o.Item == $"vanilla:{id}"));
    }
}
