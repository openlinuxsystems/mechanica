using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

/// <summary>
/// Signs with words: the text setting's limits, the parts that draw it, POI markers that write on the signs they place
/// (with tokens pointing the way to other places), and the vanilla signs standing in the world.
/// </summary>
public sealed class SignTests
{
    private static readonly Lazy<ModLoadResult> Mods = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static readonly Lazy<WorldGenData> Data = new(() => WorldGenData.Load(Mods.Value));

    private static readonly SettingDefinition FourLines = new() { Id = "text", Name = "Text", Type = SettingType.Text, MaxLength = 20, Lines = 4 };

    private static JsonElement Text(string text) => JsonSerializer.SerializeToElement(text);

    [Theory]
    [InlineData("", null)]
    [InlineData("KEEP OUT\nBots", null)]
    [InlineData("é ü ß 日本", null)]
    [InlineData("twenty-one characters", "at most 20 characters")]
    [InlineData("1\n2\n3\n4\n5", "at most 4 lines")]
    [InlineData("tab\there", "control characters")]
    [InlineData("bell\u0007", "control characters")]
    [InlineData("right‮left", "control characters")]
    public void TextFitsItsSetting(string text, string? problem)
    {
        var error = PartSettings.Check(FourLines, Text(text));
        if (problem is null)
            Assert.Null(error);
        else
            Assert.Contains(problem, error);
        Assert.Contains("must be text", PartSettings.Check(FourLines, JsonSerializer.SerializeToElement(4)));
        Assert.Equal("", PartSettings.DefaultOf(FourLines).GetString());
    }

    [Fact]
    public void ATextSettingAndItsDisplayAreChecked()
    {
        using var mods = new TestMods().Mod("m")
            .Part("m", "a.json", TestMods.Block("a", """
                { "settings": [{ "id": "text", "type": "text" }], "display": { "text": { "setting": "text", "size": [0.2, 0.2] } } }
                """))
            .Part("m", "b.json", TestMods.Block("b", """
                { "settings": [{ "id": "n", "type": "number", "min": 0, "max": 1, "max_length": 4 }],
                  "display": { "text": { "setting": "n", "at": [0, 0, 1], "size": [0.2], "facing": [0, 1, 0] } } }
                """))
            .Part("m", "c.json", TestMods.Block("c", """
                { "settings": [{ "id": "text", "type": "text", "max_length": 40, "lines": 2 }],
                  "display": { "text": { "setting": "text", "size": [0.2, 0.2], "facing": [-1, 0, 0], "color": "#102030" } } }
                """));
        var errors = mods.Load().Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        string[] Paths(string file) => [.. errors.Where(d => d.File.EndsWith(file, StringComparison.Ordinal)).Select(d => d.JsonPath!).Order()];

        Assert.Equal(["$.settings[0].max_length"], Paths("a.json"));
        Assert.Equal(["$.display.text.at", "$.display.text.facing", "$.display.text.setting", "$.display.text.size", "$.settings[0].max_length"], Paths("b.json"));
        Assert.Empty(Paths("c.json"));
    }

    [Theory]
    [InlineData(0f, -800f, "800 m", "N")]
    [InlineData(1400f, 0f, "1.4 km", "E")]
    [InlineData(-20f, 20f, "50 m", "SW")]
    [InlineData(700f, -700f, "1.0 km", "NE")]
    public void TokensPointTheWayToTheNearestPlace(float x, float z, string distance, string direction)
    {
        var poi = Data.Value.Pois.Single(p => p.QualifiedId == "vanilla:camp");
        var far = Data.Value.Pois.Single(p => p.QualifiedId == "vanilla:outpost");
        PoiInstance At(LoadedPoi which, float px, float pz) => new(0, which, new Vector2(px, pz), 0f, 0);
        var pois = new[] { At(far, x, z), At(far, x * 3 + 5000, z * 3), At(poi, 9, 9) };

        Assert.Equal($"Workshop {distance} {direction}",
            SignText.Resolve("Workshop {distance:outpost|poi_02} {direction:outpost|poi_02}", Vector3.Zero, "vanilla:camp", pois));
        Assert.Equal("? ?", SignText.Resolve("{distance:pond} {direction:pond}", Vector3.Zero, "vanilla:camp", pois));
        Assert.Equal("plain words", SignText.Resolve("plain words", Vector3.Zero, "vanilla:camp", pois));
    }

    [Fact]
    public void AMarkerWritesOnTheSignItPlaces()
    {
        var spot = new WorldPartSpot("vanilla:camp#9@1", "vanilla:camp", "vanilla:signboard", new Vector3(1, 2, 3), 0f,
            Settings: new Dictionary<string, JsonElement> { ["text"] = Text("Back soon") });
        var creation = WorldParts.Build(spot, 7, Mods.Value, out var problem);
        Assert.Null(problem);
        Assert.Equal("Back soon", creation!.Parts.Single().Settings!["text"].GetString());

        var tooLong = spot with { Settings = new Dictionary<string, JsonElement> { ["text"] = Text(new string('x', 81)) } };
        Assert.Null(WorldParts.Build(tooLong, 8, Mods.Value, out problem));
        Assert.Contains("at most 80 characters", problem);
    }

    [Fact]
    public void MarkerSettingsMustFitTheirPart()
    {
        using var mods = new TestMods().Mod("vanilla")
            .File("vanilla", WorldGenData.DefinitionFile, """{ "biomes": [{ "id": "a" }] }""")
            .Part("vanilla", "board.json", TestMods.Block("board", """{ "settings": [{ "id": "text", "type": "text", "max_length": 12 }] }"""))
            .File("vanilla", "pois/yard.json", """
                { "id": "yard", "radius": 20, "placement": { "fixed": [100, 100] },
                  "markers": [
                    { "type": "part", "ref": "board", "settings": { "text": "a sign that says far too much" } },
                    { "type": "part", "ref": "board", "settings": { "colour": "red" } },
                    { "type": "spawn", "settings": { "text": "hi" } },
                    { "type": "part", "ref": "board", "settings": { "text": "{distance:yard}" } },
                    { "type": "part", "ref": "board", "settings": { "text": "{direction:yard} {distance:yard}" } }
                  ] }
                """);
        var errors = WorldGenData.Load(mods.Load()).Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.JsonPath).ToList();

        Assert.Contains("$.markers[0].settings", errors);
        Assert.Contains("$.markers[1].settings", errors);
        Assert.Contains("$.markers[2].settings", errors);
        Assert.DoesNotContain("$.markers[3].settings", errors);
        // At its longest ("NW 99.9 km", 10 characters) it still fits.
        Assert.DoesNotContain("$.markers[4].settings", errors);
    }

    [Fact]
    public void TheVanillaSignsTakeWords()
    {
        foreach (var id in new[] { "signboard", "colony_sign", "paper_note" })
        {
            var definition = Mods.Value.Parts[$"vanilla:{id}"].Definition;
            Assert.Equal(SettingType.Text, PartSettings.Find(definition, "text")?.Type);
            Assert.Equal("text", definition.Display?.Text?.Setting);
        }
    }

    [Fact]
    public void TheWorldsSignsPointTheWay()
    {
        var world = new WorldGenerator(Data.Value, 11, 12032f, 9024f);
        var signs = WorldParts.SpotsOf(world).Where(s => s.Settings?.ContainsKey("text") == true).ToList();
        Assert.True(signs.Count >= 20, $"{signs.Count} signs with words");
        var words = signs.Select(s => s.Settings!["text"].GetString()!).ToList();
        Assert.DoesNotContain(words, w => w.Contains('{') || w.Contains('?'));
        Assert.Contains(words, w => w.StartsWith("COLONY STORES", StringComparison.Ordinal));
        Assert.Contains(words, w => System.Text.RegularExpressions.Regex.IsMatch(w, @"Workshop [0-9.]+ k?m (N|NE|E|SE|S|SW|W|NW)"));
        var next = 1;
        Assert.Empty(WorldParts.Reconcile(signs, [], Mods.Value, () => next++).Problems);
    }
}
