using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class LanguageTests
{
    [Fact]
    public void ALanguageFileTranslatesItsKeysAndLeavesTheRestInEnglish()
    {
        var fr = Language.Parse("""
            { "code": "fr", "name": "Français", "strings": { "New Game": "Nouvelle partie", "Hosting on port {0}.": "Hébergé sur le port {0}.", "Empty": "" } }
            """, "fr.json");
        Assert.Equal(("fr", "Français"), (fr.Code, fr.Name));
        Assert.Equal("Nouvelle partie", fr["New Game"]);
        Assert.Equal("Options", fr["Options"]);
        Assert.Equal("Empty", fr["Empty"]);
        Assert.Equal("Hébergé sur le port 27460.", Language.Format(fr["Hosting on port {0}."], [27460]));
    }

    [Theory]
    [InlineData("""{ "code": "French", "name": "x" }""", "code")]
    [InlineData("""{ "code": "fr" }""", "name")]
    [InlineData("""{ "code": "fr", "name": "x", "strings": { "Port {0}": "Port" } }""", "placeholders")]
    [InlineData("{", "JSON")]
    public void BadFilesAreRefused(string json, string named)
    {
        var e = Assert.Throws<FormatException>(() => Language.Parse(json, "x.json"));
        Assert.Contains(named, e.Message);
    }

    [Fact]
    public void FormatFillsNumberedSlotsOnly()
    {
        Assert.Equal("1 of 2 {name}", Language.Format("{0} of {1} {name}", [1, 2]));
        Assert.Equal("{0}", Language.Format("{0}", []));
        Assert.Equal("1.5 m", Language.Format("{0} m", [1.5]));
    }

    [Fact]
    public void ThePseudoLocaleMarksEveryStringAndKeepsSlotsAndTags()
    {
        var pseudo = PseudoLocale.Of("Hosting on port {0}, [color=#fff]Ada[/color]");
        Assert.True(PseudoLocale.IsPseudo(pseudo));
        Assert.Contains("{0}", pseudo);
        Assert.Contains("[color=#fff]", pseudo);
        Assert.Contains("[/color]", pseudo);
        Assert.Contains("Hóstíñg", pseudo);
        Assert.True(pseudo.Length > "Hosting on port {0}, [color=#fff]Ada[/color]".Length);
        var table = PseudoLocale.For(new Language("en", "English", new Dictionary<string, string> { ["Back"] = "Back" }));
        Assert.Equal(PseudoLocale.Of("Back"), table["Back"]);
    }

    [Fact]
    public void TheEnglishTableReadsAndRoundTrips()
    {
        var path = Path.Combine(Data.TestMods.RepoRoot, "game", "data", "lang", "en.json");
        var english = Language.Parse(File.ReadAllText(path), "en.json");
        Assert.Equal(Language.English, english.Code);
        Assert.Equal(File.ReadAllText(path), english.ToJson());
        Assert.All(english.Strings, kv => Assert.Equal(kv.Key, kv.Value));
    }
}

public class LocKeysTests
{
    [Fact]
    public void LiteralKeysAreFound()
    {
        var keys = LocKeys.In("""
            a = Loc.T("Hosting on port {0}.", port); b = Loc.T( "Say \"hi\"" ); c = Loc.T(name); d = Loc.Tx("no");
            """).ToList();
        Assert.Equal(["Hosting on port {0}.", "Say \"hi\""], keys);
    }

    [Fact]
    public void EveryKeyTheGameAsksForIsInEnglish()
    {
        var root = Data.TestMods.RepoRoot;
        var english = Language.Parse(File.ReadAllText(Path.Combine(root, "game", "data", "lang", "en.json")), "en.json");
        var missing = Directory.EnumerateFiles(Path.Combine(root, "game", "scripts"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => LocKeys.In(File.ReadAllText(f)).Select(k => $"{Path.GetFileName(f)}: {k}"))
            .Where(k => !english.Strings.ContainsKey(k[(k.IndexOf(": ", StringComparison.Ordinal) + 2)..]))
            .ToList();
        Assert.True(missing.Count == 0, "Keys missing from game/data/lang/en.json (run the language test with -- --collect):\n" + string.Join("\n", missing));
    }

    // A key no code or data spells out was text built at run time (a name, a count) that skipped Loc.T.
    [Fact]
    public void EveryEnglishKeyIsWrittenInTheCodeOrData()
    {
        var root = Data.TestMods.RepoRoot;
        var english = Language.Parse(File.ReadAllText(Path.Combine(root, "game", "data", "lang", "en.json")), "en.json");
        var sources = new[] { Path.Combine(root, "game", "scripts"), Path.Combine(root, "src") }
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "game", "data"), "*.json").Where(f => !f.Contains("lang", StringComparison.Ordinal)))
            .Select(File.ReadAllText)
            .ToList();
        // Long literals split over lines ("…" + "…") count as one.
        var all = System.Text.RegularExpressions.Regex.Replace(string.Join("\n", sources), "\"\\s*\\+\\s*\"", "");
        var stray = english.Strings.Keys
            .Where(k => !all.Contains(System.Text.Json.JsonSerializer.Serialize(k, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), StringComparison.Ordinal)
                && !(System.Text.RegularExpressions.Regex.IsMatch(k, @"^[A-Za-z]+$") && System.Text.RegularExpressions.Regex.IsMatch(all, $@"\b{k}\b")))
            .ToList();
        Assert.True(stray.Count == 0, "Keys in en.json that no code or data spells out:\n" + string.Join("\n", stray));
    }
}
