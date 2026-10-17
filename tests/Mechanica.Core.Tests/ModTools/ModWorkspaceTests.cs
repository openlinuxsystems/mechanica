using Mechanica.Core.Data;
using Mechanica.Core.ModTools;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.ModTools;

public class ModWorkspaceTests
{
    private static ModWorkspace Workspace(TestMods mods, ScriptChecker? checker = null) =>
        new(mods.Root, Path.Combine(Path.GetDirectoryName(mods.Root)!, "user", ModProfile.FileName), checker);

    [Fact]
    public void ListsModsInLoadOrderWithTheirState()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("b", dependencies: """[{ "id": "a" }]""").Mod("a")
            .File("broken", "mod.json", "{ \"id\": \"broken\" }");
        var workspace = Workspace(mods);

        var list = workspace.List();

        Assert.Equal(["a", "b", "vanilla"], list.Mods.Select(m => m.Id));
        Assert.All(list.Mods, m => Assert.True(m.Enabled && m.Loaded));
        Assert.False(list.Mods.Single(m => m.Id == "vanilla").CanDisable);
        Assert.Equal(["a"], list.Mods.Single(m => m.Id == "b").Dependencies);
        Assert.Equal(["broken"], list.Broken);
    }

    [Fact]
    public void EnableAndMoveSaveTheProfile()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("a").Mod("b", dependencies: """[{ "id": "a" }]""");
        var workspace = Workspace(mods);

        Assert.Equal(["a", "b"], workspace.SetEnabled("a", false));
        Assert.Equal(2, workspace.Move("vanilla", 5));

        var list = workspace.List();
        Assert.Equal(["a", "b", "vanilla"], list.Mods.Select(m => m.Id));
        Assert.False(list.Mods.Single(m => m.Id == "b").Enabled);
        Assert.True(File.Exists(workspace.ProfilePath));
        Assert.Equal(["b", "a"], workspace.SetEnabled("b", true));
        Assert.Throws<ModToolException>(() => workspace.SetEnabled("vanilla", false));
        Assert.Throws<ModToolException>(() => workspace.SetEnabled("nope", true));
        Assert.Throws<ModToolException>(() => workspace.Move("nope", 0));
    }

    [Fact]
    public void ReportsConflicts()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("a").Mod("b", conflicts: """[{ "id": "a" }]""");

        var conflict = Assert.Single(Workspace(mods).Conflicts());

        Assert.Equal(new ConflictInfo("incompatible", "b", "a", null, conflict.Message), conflict);
    }

    [Fact]
    public void CreatesAModThatLoads()
    {
        using var mods = new TestMods().Mod("vanilla");
        var workspace = Workspace(mods);

        Assert.Equal("my_mod/mod.json", workspace.Create("my_mod", "My Mod", null, "Wheels.", ["Ada"], null));

        var manifest = ModLoader.Scan(mods.Root).Mods.Single(m => m.Id == "my_mod").Manifest;
        Assert.Equal("My Mod", manifest.Name);
        Assert.Equal("0.1.0", manifest.Version);
        Assert.Equal(["vanilla"], manifest.Dependencies!.Select(d => d.Id));
        Assert.True(Directory.Exists(Path.Combine(mods.Root, "my_mod", "parts")));
        Assert.True(workspace.List().Mods.Single(m => m.Id == "my_mod").Loaded);
        Assert.Throws<ModToolException>(() => workspace.Create("my_mod", null, null, null, null, null));
        Assert.Throws<ModToolException>(() => workspace.Create("Bad", null, null, null, null, null));
        Assert.Throws<ModToolException>(() => workspace.Create("other", null, "not-a-version", null, null, null));
        Assert.Throws<ModToolException>(() => workspace.Create("self", null, null, null, null, ["self"]));
    }

    [Fact]
    public void ImportsAssetsSentAsBytes()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("m");
        var workspace = Workspace(mods);
        var gltf = """{ "asset": { "version": "2.0" }, "buffers": [{ "uri": "Data.bin", "byteLength": 3 }], "images": [{ "uri": "tex/Wood.png" }] }"""u8.ToArray();

        var path = workspace.Import("m", AssetKind.Mesh, "Crate.gltf", gltf,
            new Dictionary<string, byte[]> { ["Data.bin"] = [1, 2, 3], ["tex/Wood.png"] = [4] });

        Assert.Equal("meshes/crate.gltf", path);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(mods.Root, "m", "meshes", "data.bin")));
        Assert.Equal("sounds/hit.ogg", workspace.Import("m", AssetKind.Sound, "Hit.OGG", [9]));
        Assert.Throws<ModToolException>(() => workspace.Import("m", AssetKind.Sound, "hit.mp3", [9]));
        Assert.Throws<ModToolException>(() => workspace.Import("m", AssetKind.Mesh, "../x.glb", [9]));
        Assert.Throws<ModToolException>(() => workspace.Import("m", AssetKind.Mesh, "a.gltf", gltf, new Dictionary<string, byte[]> { ["../Data.bin"] = [1] }));
        Assert.Throws<ModToolException>(() => workspace.Import("vanilla", AssetKind.Sound, "hit.ogg", [9]));
    }

    [Fact]
    public void ValidateReportsEveryDomainAndScripts()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("m")
            .Part("m", "bad.json", TestMods.Block("bad", """{ "mass": -1 }"""))
            .Mod("r").File("r", "recipes/r.json", "{ not json")
            .File("m", "scripts/broken.lua", "function on_update(dt)\n  if then\nend");
        var workspace = Workspace(mods, source => source.Contains("if then") ? new ScriptProblem(2, 5, "unexpected symbol near 'then'") : null);

        var report = workspace.Validate();

        Assert.False(report.Ok);
        Assert.Contains(report.Diagnostics, d => d is { Domain: "mods", Code: DiagnosticCodes.PartInvalid, Mod: "m", JsonPath: "$.mass" });
        Assert.Contains(report.Diagnostics, d => d is { Domain: "recipes", Code: DiagnosticCodes.InvalidJson, Mod: "r" });
        var script = Assert.Single(report.Diagnostics, d => d.Domain == "scripts");
        Assert.Equal(new DiagnosticInfo("scripts", "error", DiagnosticCodes.ScriptSyntax, "m", $"{Path.GetFileName(mods.Root)}/m/scripts/broken.lua", null, 2,
            "unexpected symbol near 'then'"), script);
        Assert.Equal(report.Diagnostics.Count(d => d.Severity == "error"), report.Errors);
        Assert.DoesNotContain("m", report.LoadedMods);
    }

    [Fact]
    public void ValidatingOneModEnablesItForTheCheckOnly()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("m").Part("m", "a.json", TestMods.Block("a"))
            .Mod("other").Part("other", "bad.json", TestMods.Block("bad", """{ "mass": -1 }"""));
        var workspace = Workspace(mods);
        workspace.SetEnabled("m", false);

        var report = workspace.Validate("m");

        Assert.True(report.Ok);
        Assert.Contains("m", report.LoadedMods);
        Assert.Empty(report.Diagnostics);
        Assert.False(workspace.List().Mods.Single(x => x.Id == "m").Enabled);
        Assert.Throws<ModToolException>(() => workspace.Validate("nope"));
    }

    [Fact]
    public void VanillaContentValidatesClean()
    {
        var root = Path.Combine(TestMods.RepoRoot, "mods");
        var workspace = new ModWorkspace(root, Path.Combine(Path.GetTempPath(), "mechanica-tests", Guid.NewGuid().ToString("N"), ModProfile.FileName));

        var report = workspace.Validate();

        Assert.True(report.Ok, string.Join("\n", report.Diagnostics.Select(d => $"{d.File}: {d.Message}")));
        Assert.Equal(GameContent.Load(root, null).Manifest.Fingerprint, report.Fingerprint);
    }
}
