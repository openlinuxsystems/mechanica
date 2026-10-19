using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Data;

public class ModLoaderTests
{
    [Fact]
    public void SampleModsLoadCleanly()
    {
        var result = ModLoader.LoadAll(TestMods.FixtureRoot);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["sample", "sample_addon"], result.Mods.Select(m => m.Id));
        Assert.Equal(
            ["sample:bearing", "sample:scrap_metal", "sample:wood_block", "sample_addon:big_bearing"],
            result.Parts.Keys.Order(StringComparer.Ordinal));

        var bearing = result.Parts["sample:bearing"].Definition;
        Assert.Equal(PartCategory.Interactive, bearing.Category);
        Assert.Equal(3f, bearing.Mass);
        Assert.Equal(SocketType.Rotational, bearing.Sockets![1].Type);
        Assert.Equal(new Vec3(0, 0.125f, 0), bearing.Sockets[1].Position);
        Assert.Equal(1000, (int)bearing.Properties!["max_torque"]!);
        Assert.True(bearing.IsPlaceable);
        Assert.False(result.Parts["sample:scrap_metal"].Definition.IsPlaceable);
    }

    [Fact]
    public void VanillaModLoadsCleanly()
    {
        var result = ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"));

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Mods, m => m.Id == Identifiers.VanillaModId);
    }

    [Fact]
    public void LoadOrderPutsDependenciesFirstAndIsDeterministic()
    {
        using var mods = new TestMods()
            .Mod("aaa", dependencies: """[{ "id": "zzz" }]""")
            .Mod("mmm", dependencies: """[{ "id": "aaa" }, { "id": "bbb" }]""")
            .Mod("bbb")
            .Mod("zzz");

        var result = mods.Load();

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["bbb", "zzz", "aaa", "mmm"], result.Mods.Select(m => m.Id));
    }

    [Fact]
    public void ModScriptsMustExistAndBeListedOnce()
    {
        using var mods = new TestMods()
            .File("rules", "mod.json", """{ "id": "rules", "name": "Rules", "version": "1.0.0", "scripts": ["scripts/main.lua"] }""")
            .File("rules", "scripts/main.lua", "-- rules")
            .File("broken", "mod.json", """{ "id": "broken", "name": "Broken", "version": "1.0.0", "scripts": ["scripts/gone.lua", "../x.lua", "scripts/a.lua", "scripts/a.lua"] }""")
            .File("broken", "scripts/a.lua", "-- a");

        var result = mods.Load();

        Assert.Equal(["rules"], result.Mods.Select(m => m.Id));
        Assert.Equal(["scripts/main.lua"], result.Mods[0].Manifest.Scripts);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.AssetMissing && d.JsonPath == "$.scripts[0]");
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.AssetPathInvalid && d.JsonPath == "$.scripts[1]");
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.ManifestInvalid && d.JsonPath == "$.scripts[3]");
    }

    [Fact]
    public void FindPartTreatsBareIdsAsVanilla()
    {
        using var mods = new TestMods().Mod("vanilla").Part("vanilla", "b.json", TestMods.Block("wood_block"));

        var result = mods.Load();

        Assert.Same(result.Parts["vanilla:wood_block"], result.FindPart("wood_block"));
        Assert.Same(result.Parts["vanilla:wood_block"], result.FindPart("vanilla:wood_block"));
        Assert.Null(result.FindPart("other:wood_block"));
    }

    [Fact]
    public void MissingDependencyFailsOnlyThatModAndItsDependents()
    {
        using var mods = new TestMods()
            .Mod("base")
            .Mod("needs_ghost", dependencies: """[{ "id": "ghost" }]""")
            .Mod("needs_needs_ghost", dependencies: """[{ "id": "needs_ghost" }]""");

        var result = mods.Load();

        Assert.Equal(["base"], result.Mods.Select(m => m.Id));
        var missing = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCodes.MissingDependency);
        Assert.Contains("'ghost', which is not installed", missing.Message);
        Assert.Equal("mods/needs_ghost/mod.json", missing.File);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.DependencyFailed && d.File == "mods/needs_needs_ghost/mod.json");
    }

    [Fact]
    public void DependencyVersionMismatchIsReported()
    {
        using var mods = new TestMods()
            .Mod("base", version: "2.1.0")
            .Mod("addon", dependencies: """[{ "id": "base", "version": "^1.0.0" }]""");

        var result = mods.Load();

        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.DependencyVersion, error.Code);
        Assert.Equal("$.dependencies[0]", error.JsonPath);
        Assert.Contains("needs 'base' version ^1.0.0, but 2.1.0 is installed", error.Message);
        Assert.Equal(["base"], result.Mods.Select(m => m.Id));
    }

    [Fact]
    public void DependencyCycleIsReported()
    {
        using var mods = new TestMods()
            .Mod("one", dependencies: """[{ "id": "two" }]""")
            .Mod("two", dependencies: """[{ "id": "one" }]""")
            .Mod("free");

        var result = mods.Load();

        Assert.Equal(["free"], result.Mods.Select(m => m.Id));
        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == DiagnosticCodes.DependencyCycle));
    }

    [Fact]
    public void ModIdMustMatchFolder()
    {
        using var mods = new TestMods().File("folder", "mod.json", """{ "id": "other", "name": "x", "version": "1.0.0" }""");

        var result = mods.Load();

        Assert.Empty(result.Mods);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.ModFolderMismatch);
    }

    [Fact]
    public void FolderWithoutManifestIsSkippedWithWarning()
    {
        using var mods = new TestMods().Mod("real").File("notes", "readme.txt", "hi");

        var result = mods.Load();

        Assert.False(result.HasErrors);
        Assert.Equal(["real"], result.Mods.Select(m => m.Id));
        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(DiagnosticCodes.ManifestMissing, warning.Code);
    }

    [Fact]
    public void ManifestErrorsAreAllReported()
    {
        using var mods = new TestMods().File("bad", "mod.json",
            """{ "format_version": 9, "id": "bad", "version": "1.0", "dependencies": [{ "id": "Base", "version": "=>1" }] }""");

        var result = mods.Load();

        Assert.Empty(result.Mods);
        Assert.Equal(
            ["$.format_version", "$.name", "$.version", "$.dependencies[0].id", "$.dependencies[0].version"],
            result.Diagnostics.Select(d => d.JsonPath));
    }

    [Fact]
    public void DependentOfBrokenManifestSaysWhy()
    {
        using var mods = new TestMods()
            .File("base", "mod.json", """{ "id": "base", "version": "1.0.0" }""")
            .Mod("addon", dependencies: """[{ "id": "base" }]""");

        var result = mods.Load();

        Assert.Contains(result.Diagnostics, d => d.File == "mods/addon/mod.json" && d.Message.Contains("whose mod.json has errors"));
    }

    [Fact]
    public void BadPartFailsItsModAndDependents()
    {
        using var mods = new TestMods()
            .Mod("base").Part("base", "bad.json", TestMods.Block(overrides: """{ "mass": -1 }"""))
            .Mod("addon", dependencies: """[{ "id": "base" }]""");

        var result = mods.Load();

        Assert.Empty(result.Mods);
        Assert.Empty(result.Parts);
        var error = result.Diagnostics.First();
        Assert.Equal("error MD0201: mods/base/parts/bad.json $.mass: mass (kg) must be greater than 0.", error.ToString());
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.DependencyFailed && d.File == "mods/addon/mod.json");
    }

    [Fact]
    public void DuplicatePartIdIsReported()
    {
        using var mods = new TestMods().Mod("m")
            .Part("m", "a.json", TestMods.Block("dup"))
            .Part("m", "sub/b.json", TestMods.Block("dup"));

        var error = Assert.Single(mods.Load().Diagnostics, d => d.Code == DiagnosticCodes.DuplicatePart);

        Assert.Equal("mods/m/parts/sub/b.json", error.File);
        Assert.Contains("already defined in mods/m/parts/a.json", error.Message);
    }

    [Fact]
    public void MalformedJsonReportsLineAndPath()
    {
        using var mods = new TestMods().Mod("m").Part("m", "a.json", "{\n  \"id\": \"a\",\n  \"mass\": \"heavy\"\n}");

        var error = mods.Load().Diagnostics.First();

        Assert.Equal(DiagnosticCodes.InvalidJson, error.Code);
        Assert.Equal("$.mass", error.JsonPath);
        Assert.Equal(3, error.Line);
        Assert.DoesNotContain("LineNumber", error.Message);
    }

    [Fact]
    public void DuplicateJsonKeysAreRejected()
    {
        using var mods = new TestMods().Mod("m").Part("m", "a.json", """{ "id": "a", "mass": 4, "mass": 5 }""");

        Assert.Contains(mods.Load().Diagnostics, d => d.Code == DiagnosticCodes.InvalidJson);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        using var mods = new TestMods().Mod("m").Part("m", "a.json", """
            // A block
            {
              "id": "a", "name": "A", "category": "block", /* SI units */
              "mass": 4, "durability": 3, "friction": 0.5, "buoyancy": 0.6, "flammable": true,
              "size": [1, 1, 1,],
            }
            """);

        var result = mods.Load();

        Assert.Empty(result.Diagnostics);
        Assert.Equal(4f, result.Parts["m:a"].Definition.Mass);
    }

    [Fact]
    public void CrossModAssetsMustBeDeclaredDependencies()
    {
        using var mods = new TestMods()
            .Mod("art").File("art", "meshes/x.glb", "glb")
            .Mod("user").Part("user", "a.json", TestMods.Block(overrides: """{ "mesh": "art:meshes/x.glb" }"""));

        Assert.Contains(mods.Load().Diagnostics, d => d.Code == DiagnosticCodes.AssetUndeclaredMod);
    }

    [Fact]
    public void CrossModAssetsResolveThroughDependencies()
    {
        using var mods = new TestMods()
            .Mod("art").File("art", "meshes/x.glb", "glb")
            .Mod("user", dependencies: """[{ "id": "art" }]""")
            .Part("user", "a.json", TestMods.Block(overrides: """{ "mesh": "art:meshes/x.glb" }"""))
            .Part("user", "b.json", TestMods.Block("b", """{ "mesh": "art:meshes/missing.glb" }"""));

        var diagnostics = mods.Load().Diagnostics;

        Assert.Equal([DiagnosticCodes.AssetMissing, DiagnosticCodes.ModNotLoaded], diagnostics.Select(d => d.Code));
        Assert.Equal("mods/user/parts/b.json", diagnostics[0].File);
    }

    [Fact]
    public void FingerprintIsStableAndChangesWithContent()
    {
        var first = ModLoader.LoadAll(TestMods.FixtureRoot).Fingerprint;
        Assert.Equal(first, ModLoader.LoadAll(TestMods.FixtureRoot).Fingerprint);
        Assert.Matches("^[0-9a-f]{64}$", first);

        using var mods = new TestMods().Mod("m").Part("m", "a.json", TestMods.Block());
        var before = mods.Load().Fingerprint;
        mods.Part("m", "a.json", TestMods.Block(overrides: """{ "durability": 4 }"""));
        Assert.NotEqual(before, mods.Load().Fingerprint);
    }
}
