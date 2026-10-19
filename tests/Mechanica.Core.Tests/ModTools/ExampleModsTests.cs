using Mechanica.Core.Data;
using Mechanica.Core.ModTools;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.ModTools;

/// <summary>
/// Every example mod in <c>examples/mods/</c> (the tutorials' mods) loads beside vanilla with no errors in any kind of
/// content, the way a world starts and the Mod Manager's Check all content checks it.
/// </summary>
public class ExampleModsTests
{
    private static readonly string Examples = Path.Combine(TestMods.RepoRoot, "examples", "mods");
    private static readonly string Vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

    /// <summary>Every example mod folder, by id.</summary>
    public static TheoryData<string> ExampleMods()
    {
        var data = new TheoryData<string>();
        foreach (var dir in Directory.GetDirectories(Examples).Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(dir));
        return data;
    }

    [Theory]
    [MemberData(nameof(ExampleMods))]
    public void EachExampleModLoadsBesideVanillaWithoutErrors(string mod)
    {
        var content = GameContent.From(ModLoader.Load([Vanilla, Path.Combine(Examples, mod)]));

        var errors = content.Diagnostics.Where(d => d.Diagnostic.Severity == DiagnosticSeverity.Error).Select(d => $"[{d.Domain}] {d.Diagnostic}");
        Assert.Empty(errors);
        Assert.Contains(content.Mods.Mods, m => m.Id == mod);
        // Warnings too: an example teaches by being clean.
        Assert.Empty(content.Diagnostics.Where(d => d.Diagnostic.File.StartsWith(mod, StringComparison.Ordinal)).Select(d => $"[{d.Domain}] {d.Diagnostic}"));
    }

    [Fact]
    public void EveryExampleModLoadsTogether()
    {
        var content = GameContent.From(ModLoader.Load([Vanilla, .. Directory.GetDirectories(Examples).Order(StringComparer.Ordinal)]));

        Assert.Empty(content.Diagnostics.Where(d => d.Diagnostic.Severity == DiagnosticSeverity.Error).Select(d => $"[{d.Domain}] {d.Diagnostic}"));
    }

    [Fact]
    public void TheItemsExampleHasItsItemsAndRecipesInOrder()
    {
        var content = GameContent.From(ModLoader.Load([Vanilla, Path.Combine(Examples, "example_items")]));

        foreach (var id in new[] { "ration_bar", "dried_pea", "pea_shooter", "fizz_bomb" })
        {
            Assert.NotNull(content.Mods.FindDefinition($"example_items:{id}"));
            var recipe = content.Recipes.Find($"example_items:{id}");
            Assert.NotNull(recipe);
            Assert.NotNull(recipe.Definition.Order);
        }
        Assert.Equal("vanilla:meshes/decor/small_pipe_short.glb", content.Mods.FindDefinition("example_items:pea_shooter")!.Held!.Mesh);
    }

    [Fact]
    public void TheModScriptExampleHasItsScriptLanguageAndRules()
    {
        var directory = Path.Combine(Examples, "example_modscript");
        var mods = ModLoader.Load([Vanilla, directory]);

        Assert.Equal(["scripts/welcome.lua"], mods.Mods.Single(m => m.Id == "example_modscript").Manifest.Scripts);

        var problems = new List<DataDiagnostic>();
        var french = Assert.Single(ModLanguages.Read([directory], problems));
        Assert.Empty(problems);
        Assert.Equal("Mélange montagnard", ModLanguages.For([french], "fr")["Trail Mix"]);

        var engine = File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "survival.json"));
        var rules = ModRules.Apply("survival", engine, mods, json =>
        {
            try
            {
                SurvivalSettings.FromJson(json);
                return null;
            }
            catch (FormatException e)
            {
                return e.Message;
            }
        }, problems);
        Assert.Empty(problems);
        Assert.Equal(8f, SurvivalSettings.FromJson(rules).RespawnDelay);
    }
}
