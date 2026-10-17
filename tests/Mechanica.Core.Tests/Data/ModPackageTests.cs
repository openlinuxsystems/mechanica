using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Data;

public class ModProfileLoadTests
{
    [Fact]
    public void DisabledModIsNotLoaded()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("extra");

        var result = mods.Load(TestMods.Profile("vanilla", "-extra"));

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["vanilla"], result.Mods.Select(m => m.Id));
    }

    [Fact]
    public void EnabledModWithDisabledDependencyIsReported()
    {
        using var mods = new TestMods().Mod("base").Mod("addon", dependencies: """[{ "id": "base" }]""");

        var result = mods.Load(TestMods.Profile("-base", "addon"));

        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.DependencyDisabled, error.Code);
        Assert.Contains("'base', which is disabled", error.Message);
        Assert.Empty(result.Mods);
    }

    [Fact]
    public void ProfileOrderIsKeptWhereDependenciesAllow()
    {
        using var mods = new TestMods()
            .Mod("vanilla")
            .Mod("aaa", dependencies: """[{ "id": "vanilla" }]""")
            .Mod("bbb", dependencies: """[{ "id": "vanilla" }]""")
            .Mod("ccc", dependencies: """[{ "id": "aaa" }]""");

        var result = mods.Load(TestMods.Profile("ccc", "bbb", "vanilla"));

        Assert.Empty(result.Diagnostics);
        // ccc wants to be first but needs aaa (unlisted, so it sorts after the listed mods); bbb needs vanilla.
        Assert.Equal(["vanilla", "bbb", "aaa", "ccc"], result.Mods.Select(m => m.Id));
    }

    [Fact]
    public void VanillaCannotBeDisabled()
    {
        using var mods = new TestMods().Mod("vanilla");

        var result = mods.Load(TestMods.Profile("-vanilla"));

        Assert.Equal(["vanilla"], result.Mods.Select(m => m.Id));
    }

    [Fact]
    public void UnlistedModsAreEnabledAndProfileEntriesForMissingModsAreIgnored()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("new_mod");

        var result = mods.Load(TestMods.Profile("gone", "vanilla"));

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["vanilla", "new_mod"], result.Mods.Select(m => m.Id));
    }

    [Fact]
    public void FingerprintChangesWithTheEnabledSet()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("extra");

        Assert.NotEqual(mods.Load(TestMods.Profile("-extra")).Fingerprint, mods.Load(TestMods.Profile("extra")).Fingerprint);
    }
}

public class ModConflictTests
{
    [Fact]
    public void DeclaredConflictKeepsTheDeclaringModOut()
    {
        using var mods = new TestMods().Mod("old_physics").Mod("new_physics", conflicts: """[{ "id": "old_physics" }]""");

        var result = mods.Load();

        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.ModIncompatible, error.Code);
        Assert.Equal("$.conflicts[0]", error.JsonPath);
        Assert.Equal(["old_physics"], result.Mods.Select(m => m.Id));
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(new ModConflict(ModConflictKind.Incompatible, "new_physics", "old_physics", null, error.Message), conflict);
    }

    [Fact]
    public void DisablingTheOtherModResolvesTheConflict()
    {
        using var mods = new TestMods().Mod("old_physics").Mod("new_physics", conflicts: """[{ "id": "old_physics" }]""");

        var result = mods.Load(TestMods.Profile("-old_physics"));

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Conflicts);
        Assert.Equal(["new_physics"], result.Mods.Select(m => m.Id));
    }

    [Fact]
    public void ConflictVersionRangeLimitsWhichVersionsConflict()
    {
        using var mods = new TestMods()
            .Mod("lib", version: "2.1.0")
            .Mod("user", conflicts: """[{ "id": "lib", "version": "<2.0.0" }]""");

        var result = mods.Load();

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Mods.Count);
    }

    [Fact]
    public void DependentsOfAnIncompatibleModFail()
    {
        using var mods = new TestMods()
            .Mod("other")
            .Mod("picky", conflicts: """[{ "id": "other" }]""")
            .Mod("needs_picky", dependencies: """[{ "id": "picky" }]""");

        var result = mods.Load();

        Assert.Equal([DiagnosticCodes.ModIncompatible, DiagnosticCodes.DependencyFailed], result.Diagnostics.Select(d => d.Code));
        Assert.Equal(["other"], result.Mods.Select(m => m.Id));
    }

    [Theory]
    [InlineData("""[{ "id": "self" }]""", "[]", "cannot conflict with itself")]
    [InlineData("""[{ "id": "lib" }]""", """[{ "id": "lib" }]""", "listed twice in dependencies and conflicts")]
    [InlineData("""[{ "id": "lib", "version": "one" }]""", "[]", "version range 'one' is invalid")]
    public void BadConflictEntriesAreManifestErrors(string conflicts, string dependencies, string message)
    {
        using var mods = new TestMods().Mod("lib").Mod("self", dependencies: dependencies, conflicts: conflicts);

        var result = mods.Load();

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.ManifestInvalid && d.Message.Contains(message));
        Assert.DoesNotContain(result.Mods, m => m.Id == "self");
    }
}

public class ModOverrideTests
{
    private static TestMods WithBase() => new TestMods()
        .Mod("vanilla")
        .Part("vanilla", "wood.json", TestMods.Block("wood_block"))
        .Part("vanilla", "metal.json", TestMods.Block("metal_block"));

    [Fact]
    public void OverrideReplacesThePartAndResolvesAssetsInTheOverridingMod()
    {
        using var mods = WithBase()
            .Mod("heavy_wood", dependencies: """[{ "id": "vanilla" }]""")
            .File("heavy_wood", "meshes/wood.glb", "glTF")
            .File("heavy_wood", "overrides/vanilla/wood.json",
                TestMods.Block("wood_block", """{ "mass": 40, "mesh": "meshes/wood.glb" }"""));

        var result = mods.Load();

        Assert.Empty(result.Diagnostics);
        var wood = result.Parts["vanilla:wood_block"];
        Assert.Equal(40f, wood.Definition.Mass);
        Assert.Equal("heavy_wood", wood.ModId);
        Assert.True(wood.IsOverride);
        Assert.False(result.Parts["vanilla:metal_block"].IsOverride);
        Assert.Single(result.Mods.Single(m => m.Id == "heavy_wood").Overrides);
        Assert.DoesNotContain(result.Parts.Keys, k => k.StartsWith("heavy_wood:"));
    }

    [Fact]
    public void OverrideNeedsTheTargetAsADependency()
    {
        using var mods = WithBase()
            .Mod("sneaky")
            .File("sneaky", "overrides/vanilla/wood.json", TestMods.Block("wood_block"));

        var result = mods.Load();

        Assert.Equal([DiagnosticCodes.OverrideUndeclaredMod, DiagnosticCodes.ModNotLoaded], result.Diagnostics.Select(d => d.Code));
        Assert.Equal("mods/sneaky/overrides/vanilla", result.Diagnostics[0].File);
        Assert.Equal("vanilla", result.Parts["vanilla:wood_block"].ModId);
    }

    [Fact]
    public void OverrideOfAnUnknownPartIsAnError()
    {
        using var mods = WithBase()
            .Mod("typo", dependencies: """[{ "id": "vanilla" }]""")
            .File("typo", "overrides/vanilla/wod.json", TestMods.Block("wod_block"));

        var result = mods.Load();

        Assert.Equal(DiagnosticCodes.OverrideUnknownPart, result.Diagnostics[0].Code);
        Assert.Contains("no part 'wod_block'", result.Diagnostics[0].Message);
        Assert.DoesNotContain(result.Mods, m => m.Id == "typo");
    }

    [Fact]
    public void TwoUnrelatedOverridesClashAndTheLaterOneWins()
    {
        using var mods = WithBase()
            .Mod("aaa", dependencies: """[{ "id": "vanilla" }]""")
            .File("aaa", "overrides/vanilla/wood.json", TestMods.Block("wood_block", """{ "mass": 10 }"""))
            .Mod("bbb", dependencies: """[{ "id": "vanilla" }]""")
            .File("bbb", "overrides/vanilla/wood.json", TestMods.Block("wood_block", """{ "mass": 20 }"""));

        var byId = mods.Load();
        var swapped = mods.Load(TestMods.Profile("vanilla", "bbb", "aaa"));

        var warning = Assert.Single(byId.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(DiagnosticCodes.OverrideConflict, warning.Code);
        Assert.Equal(20f, byId.Parts["vanilla:wood_block"].Definition.Mass);
        Assert.Equal(new ModConflict(ModConflictKind.OverrideClash, "aaa", "bbb", "vanilla:wood_block", warning.Message),
            Assert.Single(byId.Conflicts));
        Assert.Equal(10f, swapped.Parts["vanilla:wood_block"].Definition.Mass);
        Assert.Equal("bbb", Assert.Single(swapped.Conflicts).ModId);
        Assert.NotEqual(byId.Fingerprint, swapped.Fingerprint);
    }

    [Fact]
    public void OverrideOfAnOverrideByADependentIsNotAClash()
    {
        using var mods = WithBase()
            .Mod("zzz", dependencies: """[{ "id": "vanilla" }]""")
            .File("zzz", "overrides/vanilla/wood.json", TestMods.Block("wood_block", """{ "mass": 10 }"""))
            .Mod("patch", dependencies: """[{ "id": "vanilla" }, { "id": "zzz" }]""")
            .File("patch", "overrides/vanilla/wood.json", TestMods.Block("wood_block", """{ "mass": 30 }"""));

        var result = mods.Load(TestMods.Profile("patch"));

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Conflicts);
        Assert.Equal(30f, result.Parts["vanilla:wood_block"].Definition.Mass);
    }

    [Fact]
    public void OverrideFilesAreValidatedLikeParts()
    {
        using var mods = WithBase()
            .Mod("bad", dependencies: """[{ "id": "vanilla" }]""")
            .File("bad", "overrides/vanilla/wood.json", TestMods.Block("wood_block", """{ "mass": -1 }"""));

        var result = mods.Load();

        Assert.Equal([DiagnosticCodes.PartInvalid, DiagnosticCodes.ModNotLoaded], result.Diagnostics.Select(d => d.Code));
        Assert.Equal("mods/bad/overrides/vanilla/wood.json", result.Diagnostics[0].File);
    }
}

public class ModProfileFileTests
{
    [Fact]
    public void SaveThenLoadRoundTripsInCanonicalForm()
    {
        using var mods = new TestMods();
        var path = Path.Combine(Path.GetDirectoryName(mods.Root)!, "user", ModProfile.FileName);
        var profile = TestMods.Profile("vanilla", "-extra") with { FormatVersion = 1 };

        profile.Save(path);
        var diagnostics = new List<DataDiagnostic>();
        var loaded = ModProfile.Load(path, diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(DataJson.Write(profile), File.ReadAllText(path));
        Assert.Equal(DataJson.Write(profile), DataJson.Write(loaded!));
        Assert.Equal("""
            {
              "format_version": 1,
              "mods": [
                {
                  "id": "vanilla",
                  "enabled": true
                },
                {
                  "id": "extra",
                  "enabled": false
                }
              ]
            }

            """.ReplaceLineEndings("\n"), File.ReadAllText(path));
    }

    [Fact]
    public void LoadWithProfileReadsTheSavedFile()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("extra");
        var path = Path.Combine(mods.Root, ModProfile.FileName);
        TestMods.Profile("-extra").Save(path);

        var result = ModLoader.LoadWithProfile(mods.Root, path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["vanilla"], result.Mods.Select(m => m.Id));
    }

    [Fact]
    public void LoadWithBrokenProfileLoadsEverythingAndSaysWhy()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("extra");
        var path = Path.Combine(Path.GetDirectoryName(mods.Root)!, ModProfile.FileName);
        File.WriteAllText(path, "not json");

        var result = ModLoader.LoadWithProfile(mods.Root, path);

        Assert.Equal(DiagnosticCodes.InvalidJson, Assert.Single(result.Diagnostics).Code);
        Assert.Equal(["extra", "vanilla"], result.Mods.Select(m => m.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void MissingFileMeansNoProfile()
    {
        var diagnostics = new List<DataDiagnostic>();

        Assert.Null(ModProfile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "mods.json"), diagnostics));
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("""{ "mods": [ { "id": "a" }, { "id": "a" } ] }""", DiagnosticCodes.ProfileInvalid)]
    [InlineData("""{ "mods": [ { "id": "Bad Id" } ] }""", DiagnosticCodes.ProfileInvalid)]
    [InlineData("""{ "format_version": 9 }""", DiagnosticCodes.UnsupportedFormat)]
    [InlineData("""{ "mods": [ """, DiagnosticCodes.InvalidJson)]
    public void BrokenFileIsReportedAndIgnored(string json, string code)
    {
        using var mods = new TestMods();
        var path = Path.Combine(mods.Root, ModProfile.FileName);
        File.WriteAllText(path, json);
        var diagnostics = new List<DataDiagnostic>();

        Assert.Null(ModProfile.Load(path, diagnostics));
        Assert.Equal(code, diagnostics[0].Code);
        Assert.Equal(ModProfile.FileName, diagnostics[0].File);
    }

    [Fact]
    public void UnknownKeysWarnButLoad()
    {
        using var mods = new TestMods();
        var path = Path.Combine(mods.Root, ModProfile.FileName);
        File.WriteAllText(path, """{ "mods": [ { "id": "a", "enabeld": false } ] }""");
        var diagnostics = new List<DataDiagnostic>();

        var profile = ModProfile.Load(path, diagnostics);

        Assert.NotNull(profile);
        var warning = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.UnknownKey, warning.Code);
        Assert.Contains("Did you mean 'enabled'?", warning.Message);
    }
}

public class ModPackageManagerTests
{
    private static TestMods Setup() => new TestMods()
        .Mod("vanilla")
        .Mod("lib", dependencies: """[{ "id": "vanilla" }]""")
        .Mod("cars", dependencies: """[{ "id": "lib" }]""")
        .Mod("decor", dependencies: """[{ "id": "vanilla" }]""");

    private static string[] Ids(ModPackageManager manager) => [.. manager.Mods.Select(m => m.Id)];

    [Fact]
    public void WithoutAProfileEveryModIsEnabledInDefaultLoadOrder()
    {
        using var mods = Setup();

        var manager = mods.Manager();

        Assert.Equal(["vanilla", "decor", "lib", "cars"], Ids(manager));
        Assert.All(manager.Mods, m => Assert.True(manager.IsEnabled(m.Id)));
        Assert.Equal(["vanilla", "decor", "lib", "cars"], manager.Load().Mods.Select(m => m.Id));
    }

    [Fact]
    public void EnablingAModEnablesItsDependencies()
    {
        using var mods = Setup();
        var manager = mods.Manager(TestMods.Profile("-lib", "-cars"));

        var changed = manager.SetEnabled("cars", true);

        Assert.Equal(["cars", "lib"], changed);
        Assert.True(manager.IsEnabled("lib"));
    }

    [Fact]
    public void DisablingAModDisablesItsDependents()
    {
        using var mods = Setup();
        var manager = mods.Manager();

        var changed = manager.SetEnabled("lib", false);

        Assert.Equal(["lib", "cars"], changed);
        Assert.Equal(["vanilla", "decor"], manager.Load().Mods.Select(m => m.Id));
        Assert.Empty(manager.Load().Diagnostics);
    }

    [Fact]
    public void VanillaStaysEnabled()
    {
        using var mods = Setup();
        var manager = mods.Manager(TestMods.Profile("-vanilla"));

        Assert.True(manager.IsEnabled("vanilla"));
        Assert.False(ModPackageManager.CanDisable("vanilla"));
        Assert.Empty(manager.SetEnabled("vanilla", false));
        Assert.True(manager.IsEnabled("vanilla"));
    }

    [Fact]
    public void MoveStopsAtDependencies()
    {
        using var mods = Setup();
        var manager = mods.Manager();

        Assert.Equal(3, manager.Move("cars", 0));
        Assert.Equal(1, manager.Move("lib", 0));
        Assert.Equal(["vanilla", "lib", "decor", "cars"], Ids(manager));
        Assert.Equal(0, manager.Move("vanilla", 3));
    }

    [Fact]
    public void MoveReordersIndependentMods()
    {
        using var mods = Setup();
        var manager = mods.Manager();

        Assert.Equal(3, manager.Move("decor", 3));
        Assert.Equal(["vanilla", "lib", "cars", "decor"], Ids(manager));
        Assert.Equal(1, manager.Move("lib", 0));
        Assert.Equal(1, manager.Move("decor", 1));
        Assert.Equal(["vanilla", "decor", "lib", "cars"], Ids(manager));
    }

    [Fact]
    public void ProfileKeepsOrderStateAndMissingMods()
    {
        using var mods = Setup();
        var manager = mods.Manager(TestMods.Profile("vanilla", "uninstalled", "lib"));
        manager.Move("decor", 1);
        manager.SetEnabled("cars", false);

        var profile = manager.ToProfile();
        var reopened = mods.Manager(profile);

        Assert.Equal(["uninstalled"], manager.MissingMods);
        Assert.Equal(["vanilla", "uninstalled", "decor", "lib", "cars"], profile.Mods!.Select(e => e.Id));
        Assert.Equal(Ids(manager), Ids(reopened));
        Assert.False(reopened.IsEnabled("cars"));
        Assert.Equal(["vanilla", "decor", "lib"], reopened.Load().Mods.Select(m => m.Id));
    }

    [Fact]
    public void ProfileThatPutsADependentFirstIsCorrected()
    {
        using var mods = Setup();

        var manager = mods.Manager(TestMods.Profile("cars", "decor"));

        Assert.Equal(["vanilla", "decor", "lib", "cars"], Ids(manager));
    }

    [Fact]
    public void ScanReportsBrokenManifestsWithoutLoadingParts()
    {
        using var mods = Setup().File("broken", "mod.json", """{ "id": "broken" }""").Part("decor", "bad.json", "{");

        var manager = mods.Manager();

        Assert.Contains("broken", manager.Scan.Broken);
        Assert.All(manager.Scan.Diagnostics, d => Assert.Equal("mods/broken/mod.json", d.File));
        Assert.Contains(manager.Load().Diagnostics, d => d.Code == DiagnosticCodes.InvalidJson);
    }
}
