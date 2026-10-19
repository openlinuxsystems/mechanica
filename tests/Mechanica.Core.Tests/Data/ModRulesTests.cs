using Mechanica.Core.Data;
using System.Text.Json.Nodes;
using Mechanica.Core.Fire;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Tests.Data;

// Mods changing the game rules (rules/<name>.patch.json) over the engine's settings files.
public class ModRulesTests
{
    private static readonly string EngineSurvival = File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "survival.json"));
    private static readonly string EngineFire = File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "fire.json"));

    private static string? Check(string json)
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
    }

    [Fact]
    public void RulesStackInLoadOrderAndTheLastModWins()
    {
        using var mods = new TestMods()
            .Mod("hardcore")
            .File("hardcore", "rules/survival.patch.json", """{ "max_health": 50, "starting_items": [{ "item": "carrot", "count": 1 }], "fall": { "min_speed": 6 } }""")
            .Mod("kind", dependencies: """[{ "id": "hardcore" }]""")
            .File("kind", "rules/survival.patch.json", """{ "max_health": 80 }""");
        var diagnostics = new List<DataDiagnostic>();

        var settings = SurvivalSettings.FromJson(ModRules.Apply("survival", EngineSurvival, mods.Load(), Check, diagnostics));

        Assert.Empty(diagnostics);
        Assert.Equal(80, settings.MaxHealth);
        Assert.Equal(new ItemCount("carrot", 1), Assert.Single(settings.StartingItems));
        Assert.Equal(6, settings.Fall.MinSpeed);
        Assert.Equal(SurvivalSettings.FromJson(EngineSurvival).Fall.DamagePerSpeed, settings.Fall.DamagePerSpeed);
    }

    [Fact]
    public void ARuleThatBreaksTheSettingsIsLeftOut()
    {
        using var mods = new TestMods()
            .Mod("good").File("good", "rules/survival.patch.json", """{ "respawn_delay": 2 }""")
            .Mod("zbad").File("zbad", "rules/survival.patch.json", """{ "max_health": -5, "respawn_delay": 1 }""");
        var diagnostics = new List<DataDiagnostic>();

        var settings = SurvivalSettings.FromJson(ModRules.Apply("survival", EngineSurvival, mods.Load(), Check, diagnostics));

        Assert.Equal(2, settings.RespawnDelay);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.RuleInvalid && d.File == "zbad/rules/survival.patch.json");
    }

    [Fact]
    public void EngineSettingsAreLockedAndUnknownFilesWarn()
    {
        using var mods = new TestMods()
            .Mod("cheat").File("cheat", "rules/fire.patch.json", """{ "max_fires": 99999 }""")
            .Mod("odd").File("odd", "rules/movement.patch.json", "{}");

        var loaded = mods.Load();

        Assert.DoesNotContain(loaded.Mods, m => m.Id == "cheat");
        Assert.Contains(loaded.Diagnostics, d => d.Code == DiagnosticCodes.RuleLocked && d.JsonPath == "$.max_fires");
        Assert.Contains(loaded.Diagnostics, d => d.Code == DiagnosticCodes.RuleUnknown && d.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(loaded.Mods, m => m.Id == "odd");
        Assert.Equal(EngineFire, ModRules.Apply("fire", EngineFire, loaded, _ => null, []));
    }

    [Fact]
    public void EveryRulesFileNamesARealSettingsFileAndItsLockedKeys()
    {
        foreach (var (name, locked) in ModRules.Files)
        {
            var engine = JsonNode.Parse(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", name + ".json")),
                documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!.AsObject();
            Assert.All(locked, key => Assert.True(engine.ContainsKey(key), $"{name}.json has no '{key}'"));
        }
        Assert.NotNull(FireSettings.FromJson(EngineFire));
    }
}
