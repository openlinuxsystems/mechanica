using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Editor;

public class PartDeriveTests
{
    private static TestMods VanillaWithWheel()
    {
        var mods = new TestMods().Mod("vanilla");
        var wheel = DataJson.Write(EditorParts.Block() with { Id = "wheel", Name = "Wheel", Mesh = "meshes/wheel.glb", Script = "scripts/wheel.lua" });
        mods.Part("vanilla", "wheels/wheel.json", wheel)
            .File("vanilla", "meshes/wheel.glb", "glTF")
            .File("vanilla", "scripts/wheel.lua", "-- wheel");
        return mods;
    }

    private static PartDocument Open(TestMods mods, string file)
    {
        var diagnostics = new List<DataDiagnostic>();
        return PartDocument.Open(Path.Combine(mods.Root, file), diagnostics) ?? throw new InvalidOperationException(string.Join("\n", diagnostics));
    }

    [Fact]
    public void AnOverrideKeepsTheIdPointsAssetsBackAndLoads()
    {
        using var mods = VanillaWithWheel();
        var wheel = Open(mods, "vanilla/parts/wheels/wheel.json");
        var target = Path.Combine(mods.Root, "better_wheels");

        var doc = PartDerive.Derive(wheel, target, PartDeriveMode.Override);
        var added = PartDerive.EnsureDependencies(wheel, target, PartDeriveMode.Override);
        doc.Apply("Lighter", d => d with { Mass = 1 });
        var result = PartExporter.Export(doc, null);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.Empty(added); // a new mod already depends on vanilla
        Assert.Equal("vanilla:wheel", result.QualifiedId);
        Assert.Equal("overrides/vanilla/wheels/wheel.json", doc.RelativeFile);
        Assert.Equal("vanilla:meshes/wheel.glb", doc.Definition.Mesh);
        Assert.Equal("vanilla:scripts/wheel.lua", doc.Definition.Script);
        Assert.Equal("vanilla", PartDerive.OwnerOf(doc));
        var loaded = mods.Load();
        Assert.False(loaded.HasErrors, string.Join("\n", loaded.Diagnostics));
        Assert.Equal(1, loaded.Parts["vanilla:wheel"].Definition.Mass);
        // The original file is untouched.
        Assert.NotEqual(1, Open(mods, "vanilla/parts/wheels/wheel.json").Definition.Mass);
    }

    [Fact]
    public void ACopyGetsItsOwnIdAndNameInTheTargetMod()
    {
        using var mods = VanillaWithWheel();
        var wheel = Open(mods, "vanilla/parts/wheels/wheel.json");
        var target = Path.Combine(mods.Root, "my_mod");

        var doc = PartDerive.Derive(wheel, target, PartDeriveMode.Copy, "sport_wheel", "Sport Wheel");
        PartDerive.EnsureDependencies(wheel, target, PartDeriveMode.Copy);
        var result = PartExporter.Export(doc, null);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.Equal("my_mod:sport_wheel", result.QualifiedId);
        Assert.Equal("parts/sport_wheel.json", doc.RelativeFile);
        var loaded = mods.Load();
        Assert.False(loaded.HasErrors, string.Join("\n", loaded.Diagnostics));
        Assert.Equal("Sport Wheel", loaded.Parts["my_mod:sport_wheel"].Definition.Name);
        Assert.True(loaded.Parts.ContainsKey("vanilla:wheel"));
    }

    [Fact]
    public void OverridingAnOverrideTargetsTheOriginalOwner()
    {
        using var mods = VanillaWithWheel();
        var wheel = Open(mods, "vanilla/parts/wheels/wheel.json");
        var first = Path.Combine(mods.Root, "first");
        var doc = PartDerive.Derive(wheel, first, PartDeriveMode.Override);
        PartDerive.EnsureDependencies(wheel, first, PartDeriveMode.Override);
        Assert.True(PartExporter.Export(doc, null).Succeeded);

        var second = Path.Combine(mods.Root, "second");
        var again = PartDerive.Derive(doc, second, PartDeriveMode.Override);
        var added = PartDerive.EnsureDependencies(doc, second, PartDeriveMode.Override);

        Assert.Equal("overrides/vanilla/wheels/wheel.json", again.RelativeFile);
        Assert.Equal(["first"], added); // vanilla is already there from the new mod.json
        Assert.Equal("vanilla:meshes/wheel.glb", again.Definition.Mesh);
    }

    [Fact]
    public void BadRequestsAreRefused()
    {
        using var mods = VanillaWithWheel();
        var wheel = Open(mods, "vanilla/parts/wheels/wheel.json");

        Assert.Throws<ArgumentException>(() => PartDerive.Derive(wheel, Path.Combine(mods.Root, "vanilla"), PartDeriveMode.Override));
        Assert.Throws<ArgumentException>(() => PartDerive.Derive(wheel, Path.Combine(mods.Root, "my_mod"), PartDeriveMode.Copy, "Bad Id"));
        Assert.Throws<ArgumentException>(() => PartDerive.Derive(wheel, Path.Combine(mods.Root, "My-Mod"), PartDeriveMode.Override));
    }

    [Theory]
    [InlineData("meshes/x.glb", true)]
    [InlineData("textures/a/b.png", true)]
    [InlineData("vanilla:meshes/x.glb", false)]
    [InlineData("wheel_spin", false)]
    [InlineData("x.glb", false)]
    public void OnlyRelativeAssetPathsAreRewritten(string text, bool rewritten) =>
        Assert.Equal(rewritten, PartDerive.IsRelativeAsset(text));

    [Fact]
    public void ANewModDependsOnVanillaWhenItIsInstalled()
    {
        using var mods = new TestMods();
        PartExporter.EnsureManifest(Path.Combine(mods.Root, "alone"));
        Assert.Empty(ModLoader.ParseJson<ModManifest>(File.ReadAllText(Path.Combine(mods.Root, "alone", "mod.json")), "mod.json", [])!.Dependencies!);
        mods.Mod("vanilla");
        var dir = Path.Combine(mods.Root, "fresh");
        PartExporter.EnsureManifest(dir);
        var manifest = ModLoader.ParseJson<ModManifest>(File.ReadAllText(Path.Combine(dir, "mod.json")), "mod.json", [])!;
        Assert.Equal("vanilla", Assert.Single(manifest.Dependencies!).Id);
    }

    [Fact]
    public void EditInfoKeepsEverythingElseInModJson()
    {
        using var mods = new TestMods()
            .File("mine", "mod.json", """{ "id": "mine", "name": "Mine", "version": "1.0.0", "dependencies": [{ "id": "vanilla", "version": "^1.0.0" }], "scripts": ["scripts/a.lua"], "homepage": "x" }""");
        var dir = Path.Combine(mods.Root, "mine");

        Assert.Empty(ModManifestFile.Update(dir, "Mine Two", "1.1.0", "Better", ["Ada"], ["vanilla", "lib"]));
        Assert.NotEmpty(ModManifestFile.Update(dir, "", "nope", null, [], []));

        var manifest = ModLoader.ParseJson<ModManifest>(File.ReadAllText(Path.Combine(dir, "mod.json")), "mod.json", [])!;
        Assert.Equal(("Mine Two", "1.1.0", "Better"), (manifest.Name, manifest.Version, manifest.Description));
        Assert.Equal("^1.0.0", manifest.Dependencies![0].Version);
        Assert.Equal("lib", manifest.Dependencies[1].Id);
        Assert.Equal(["scripts/a.lua"], manifest.Scripts);
        Assert.True(manifest.Extra!.ContainsKey("homepage"));
    }
}
