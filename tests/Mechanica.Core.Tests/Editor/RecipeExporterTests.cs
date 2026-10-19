using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Items;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Editor;

public class RecipeExporterTests : IDisposable
{
    private readonly TestMods _mods = new();
    private readonly PartCatalog _catalog = new PartCatalog()
        .Add("my_mod:bench", new PartDefinition { Id = "bench", Name = "Bench", Category = PartCategory.Station, Inventory = new() { Slots = 2 }, Crafting = new() })
        .Add("vanilla:scrap_metal", new PartDefinition { Id = "scrap_metal", Name = "Scrap Metal", Category = PartCategory.Resource });

    private string ModDir => Path.Combine(_mods.Root, "my_mod");

    public void Dispose() => _mods.Dispose();

    private static RecipeDefinition Recipe(string id = "gizmo", string output = "gizmo") => new()
    {
        Id = id,
        Stations = ["my_mod:bench"],
        Inputs = [new RecipeItem { Item = "scrap_metal", Count = 3 }],
        Outputs = [new RecipeItem { Item = $"my_mod:{output}", Count = 1 }],
        Time = 2.5f,
    };

    private static Dictionary<string, PartDefinition> Gizmo => new()
    {
        ["my_mod:gizmo"] = new PartDefinition { Id = "gizmo", Name = "Gizmo", Category = PartCategory.Resource },
    };

    [Fact]
    public void ARecipeForThePartBeingEditedIsWrittenInCanonicalForm()
    {
        var result = RecipeExporter.Export(ModDir, Recipe(), _catalog, Gizmo);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.Equal("my_mod:gizmo", result.QualifiedId);
        var json = File.ReadAllText(Path.Combine(ModDir, "recipes", "gizmo.json"));
        Assert.Equal(DataJson.Write(Recipe()), json);
        Assert.StartsWith("{\n  \"id\": \"gizmo\",\n  \"stations\": [\n    \"my_mod:bench\"\n  ],", json);
        Assert.True(File.Exists(Path.Combine(ModDir, "mod.json")));

        var files = RecipeExporter.ReadAll(ModDir, []);
        Assert.Equal("recipes/gizmo.json", Assert.Single(files).RelativeFile);
        Assert.Single(RecipeExporter.Making(files, "my_mod", "gizmo"));
        Assert.Empty(RecipeExporter.Making(files, "my_mod", "bench"));
    }

    [Fact]
    public void AnInvalidRecipeIsNotWritten()
    {
        var result = RecipeExporter.Export(ModDir, Recipe(), _catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Message == "there is no item 'my_mod:gizmo'.");
        Assert.False(Directory.Exists(Path.Combine(ModDir, "recipes")));
    }

    [Fact]
    public void ChangingTheIdRenamesTheFileButNeverOverwritesAnother()
    {
        Assert.True(RecipeExporter.Export(ModDir, Recipe(), _catalog, Gizmo).Succeeded);
        Assert.True(RecipeExporter.Export(ModDir, Recipe("other"), _catalog, Gizmo).Succeeded);

        var clash = RecipeExporter.Export(ModDir, Recipe("other"), _catalog, Gizmo, previousFile: "recipes/gizmo.json");
        Assert.False(clash.Succeeded);
        Assert.Contains(clash.Diagnostics, d => d.Code == DiagnosticCodes.DuplicateRecipe);

        var renamed = RecipeExporter.Export(ModDir, Recipe("gizmo_two"), _catalog, Gizmo, previousFile: "recipes/gizmo.json");
        Assert.True(renamed.Succeeded);
        Assert.Equal(["recipes/gizmo_two.json", "recipes/other.json"], RecipeExporter.ReadAll(ModDir, []).Select(f => f.RelativeFile));
    }

    [Fact]
    public void AnOverrideIsWrittenUnderTheOwnersFolderWithItsId()
    {
        var recipe = Recipe() with { Outputs = [new RecipeItem { Item = "scrap_metal", Count = 2 }] };

        var result = RecipeExporter.Export(ModDir, recipe, _catalog, overrideOf: "vanilla");

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.Equal("vanilla:gizmo", result.QualifiedId);
        Assert.Equal("overrides/vanilla/recipes/gizmo.json", RecipeExporter.TargetFile("gizmo", "vanilla"));
        Assert.True(File.Exists(Path.Combine(ModDir, "overrides", "vanilla", "recipes", "gizmo.json")));
        Assert.Empty(RecipeExporter.ReadAll(ModDir, []));
        var file = Assert.Single(RecipeExporter.ReadAll(ModDir, [], "vanilla"));
        Assert.Single(RecipeExporter.Making([file], "vanilla", "scrap_metal"));
        Assert.True(RecipeExporter.Delete(ModDir, file.RelativeFile));
        Assert.Empty(RecipeExporter.ReadAll(ModDir, [], "vanilla"));
    }

    [Fact]
    public void DeleteOnlyTouchesRecipeFiles()
    {
        Assert.True(RecipeExporter.Export(ModDir, Recipe(), _catalog, Gizmo).Succeeded);

        Assert.False(RecipeExporter.Delete(ModDir, "mod.json"));
        Assert.False(RecipeExporter.Delete(ModDir, "recipes/../mod.json"));
        Assert.True(RecipeExporter.Delete(ModDir, "recipes/gizmo.json"));
        Assert.Empty(RecipeExporter.ReadAll(ModDir, []));
    }

    [Fact]
    public void WrittenRecipesLoadInTheGame()
    {
        _mods.Mod("my_mod")
            .Part("my_mod", "bench.json", TestMods.Block("bench", """{ "category": "station", "inventory": { "slots": 2 }, "crafting": {} }"""))
            .Part("my_mod", "gizmo.json", """{ "id": "gizmo", "name": "Gizmo", "category": "resource" }""")
            .Mod("vanilla")
            .Part("vanilla", "scrap_metal.json", """{ "id": "scrap_metal", "name": "Scrap Metal", "category": "resource" }""");
        var content = _mods.Load();
        Assert.True(RecipeExporter.Export(ModDir, Recipe(), content).Succeeded);

        var book = RecipeBook.Load(_mods.Load());

        Assert.False(book.HasErrors, string.Join("\n", book.Diagnostics));
        Assert.Equal("Gizmo", book.Find("my_mod:gizmo")!.Name);
    }
}
