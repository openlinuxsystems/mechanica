using Mechanica.Core.Editor;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Editor;

public class RecipeCatalogTests : IDisposable
{
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private static string Recipe(string id, string output, int count = 1) =>
        $$"""{ "id": "{{id}}", "stations": ["fabricator"], "inputs": [{ "item": "scrap_metal", "count": 2 }], "outputs": [{ "item": "{{output}}", "count": {{count}} }], "time": 1 }""";

    private IReadOnlyList<RecipeEntry> Setup()
    {
        _mods.Mod("vanilla").Mod("cars").Mod("tweaks")
            .File("vanilla", "recipes/wheel.json", Recipe("wheel", "wheel"))
            .File("vanilla", "recipes/seat.json", Recipe("seat", "seat"))
            .File("cars", "recipes/car_wheel.json", Recipe("car_wheel", "cars:car_wheel"))
            .File("cars", "overrides/vanilla/recipes/wheel.json", Recipe("wheel", "wheel", 2))
            .File("tweaks", "overrides/vanilla/recipes/wheel.json", Recipe("wheel", "wheel", 4))
            .File("tweaks", "overrides/vanilla/recipes/not_there.json", Recipe("not_there", "wheel"));
        return RecipeCatalog.ReadAll(_mods.Root, ["vanilla", "cars", "tweaks", "missing"], []);
    }

    [Fact]
    public void EveryModsRecipesAndOverridesAreListedInLoadOrder()
    {
        var entries = Setup();

        Assert.Equal(["vanilla:seat", "vanilla:wheel", "cars:car_wheel", "vanilla:wheel", "vanilla:not_there", "vanilla:wheel"],
            entries.Select(e => e.QualifiedId));
        Assert.Equal(["vanilla", "vanilla", "cars", "cars", "tweaks", "tweaks"], entries.Select(e => e.Mod));
        Assert.Equal("overrides/vanilla/recipes/wheel.json", entries[3].File.RelativeFile);
        Assert.Equal("vanilla", entries[3].OverrideOf);
    }

    [Fact]
    public void TheLastOverrideWins_AndAnOverrideOfNothingWinsNothing()
    {
        var effective = RecipeCatalog.Effective(Setup());

        Assert.Equal(["cars:car_wheel", "vanilla:seat", "vanilla:wheel"], effective.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("tweaks", effective["vanilla:wheel"].Mod);
        Assert.Equal(4, effective["vanilla:wheel"].File.Recipe.Outputs![0].Count);
        Assert.Equal("vanilla", effective["vanilla:seat"].Mod);
    }

    [Fact]
    public void NewAndCopiedRecipesGetAFreeId()
    {
        var entries = Setup();
        var cars = Path.Combine(_mods.Root, "cars");

        var fresh = RecipeCatalog.NewRecipe(cars, "cars", "car_wheel");
        Assert.Equal("car_wheel_2", fresh.Id);
        Assert.Equal("cars:car_wheel", fresh.Outputs![0].Item);
        Assert.Equal(["fabricator"], fresh.Stations);
        Assert.Equal("new_recipe", RecipeCatalog.NewRecipe(cars, "cars", null).Id);

        var copy = RecipeCatalog.CopyAsNew(entries.First(e => e.QualifiedId == "vanilla:seat"), cars);
        Assert.Equal("seat", copy.Id);
        Assert.Equal("seat", copy.Outputs![0].Item);
        Assert.Equal("car_wheel_2", RecipeCatalog.CopyAsNew(entries.First(e => e.QualifiedId == "cars:car_wheel"), cars).Id);
    }
}
