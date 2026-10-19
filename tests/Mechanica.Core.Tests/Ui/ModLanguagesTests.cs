using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class ModLanguagesTests : IDisposable
{
    private readonly TestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    private static string File(string code, string strings) => $$"""{ "code": "{{code}}", "name": "N", "strings": { {{strings}} } }""";

    [Fact]
    public void LaterModsWin_AndARegionalCodeBuildsOnItsBase()
    {
        _mods.Mod("cars").Mod("tweaks")
            .File("cars", "lang/pt.json", File("pt", """ "Wheel": "Roda", "Seat": "Assento" """))
            .File("cars", "lang/pt-BR.json", File("pt-BR", """ "Seat": "Banco" """))
            .File("tweaks", "lang/pt.json", File("pt", """ "Wheel": "Roda grande", "Empty": "" """))
            .File("tweaks", "lang/broken.json", "{ not json");
        var diagnostics = new List<DataDiagnostic>();
        var languages = ModLanguages.Read([Path.Combine(_mods.Root, "cars"), Path.Combine(_mods.Root, "tweaks"), Path.Combine(_mods.Root, "none")], diagnostics);

        Assert.Equal(["pt-BR", "pt", "pt"], languages.Select(l => l.Code));
        var problem = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, problem.Severity);
        Assert.Equal("mods/tweaks/lang/broken.json", problem.File);

        var brazil = ModLanguages.For(languages, "pt-BR");
        Assert.Equal("Roda grande", brazil["Wheel"]);
        Assert.Equal("Banco", brazil["Seat"]);
        Assert.False(brazil.ContainsKey("Empty"));
        Assert.Equal("Assento", ModLanguages.For(languages, "pt")["Seat"]);
        Assert.Empty(ModLanguages.For(languages, "fr"));
    }

    [Fact]
    public void EnabledDirectoriesFollowTheProfile()
    {
        _mods.Mod("vanilla").Mod("cars").Mod("off");
        var profile = Path.Combine(_mods.Root, "..", "mods.json");
        System.IO.File.WriteAllText(profile, """{ "format_version": 1, "mods": [{ "id": "cars" }, { "id": "off", "enabled": false }, { "id": "vanilla", "enabled": false }] }""");

        Assert.Equal(["cars", "vanilla"], ModLoader.EnabledDirectories(_mods.Root, profile).Select(Path.GetFileName));
    }

    [Fact]
    public void CheckingContentListsBadLanguageFiles()
    {
        _mods.Mod("vanilla").File("vanilla", "lang/fr.json", File("fr", """ "Seat {0}": "Siège" """));

        var problem = Assert.Single(Core.ModTools.GameContent.Load(_mods.Root, null).Diagnostics, d => d.Domain == "lang");
        Assert.Contains("placeholders", problem.Diagnostic.Message);
    }
}
