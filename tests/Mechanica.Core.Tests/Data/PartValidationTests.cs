using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Data;

public class PartValidationTests
{
    private static IReadOnlyList<DataDiagnostic> Check(string partJson, Action<TestMods>? setup = null)
    {
        using var mods = new TestMods().Mod("m");
        setup?.Invoke(mods);
        mods.Part("m", "p.json", partJson);
        return [.. mods.Load().Diagnostics.Where(d => d.File == "mods/m/parts/p.json")];
    }

    private static DataDiagnostic Single(string partJson, string jsonPath, Action<TestMods>? setup = null)
    {
        var diagnostics = Check(partJson, setup);
        return Assert.Single(diagnostics, d => d.JsonPath == jsonPath);
    }

    [Fact]
    public void ValidBlockHasNoDiagnostics() => Assert.Empty(Check(TestMods.Block()));

    [Fact]
    public void EmptyObjectReportsEveryRequiredField()
    {
        var paths = Check("{}").Select(d => d.JsonPath).ToList();

        Assert.Equal(["$.id", "$.name", "$.category"], paths);
    }

    [Fact]
    public void PlaceablePartsNeedPhysicalProperties()
    {
        var paths = Check("""{ "id": "x", "name": "X", "category": "interactive" }""").Select(d => d.JsonPath);

        Assert.Equal(["$.mass", "$.durability", "$.friction", "$.buoyancy", "$.flammable", "$.size"], paths);
    }

    [Fact]
    public void InventoryItemsNeedOnlyIdentity() =>
        Assert.Empty(Check("""{ "id": "gasoline", "name": "Gasoline", "category": "consumable", "stack_size": 20 }"""));

    [Theory]
    [InlineData("""{ "id": "Wood-Block" }""", "$.id", "lowercase snake_case")]
    [InlineData("""{ "category": "gadget" }""", "$.category", "")]
    [InlineData("""{ "mass": 0 }""", "$.mass", "greater than 0")]
    [InlineData("""{ "durability": 0 }""", "$.durability", "1 or more")]
    [InlineData("""{ "friction": -0.1 }""", "$.friction", "0 or more")]
    [InlineData("""{ "stack_size": 0 }""", "$.stack_size", "1 or more")]
    [InlineData("""{ "size": [1, 0, 1] }""", "$.size", "at least 1 grid cell")]
    [InlineData("""{ "size": [1, 1] }""", "$.size", "array of 3 numbers")]
    [InlineData("""{ "sound_material": "Wood" }""", "$.sound_material", "snake_case")]
    [InlineData("""{ "sounds": { "place": "Place Wood" } }""", "$.sounds.place", "snake_case id")]
    [InlineData("""{ "collision": { "shape": "box" } }""", "$.collision.size", "needs size")]
    [InlineData("""{ "collision": { "shape": "sphere", "radius": 0 } }""", "$.collision.radius", "greater than 0")]
    [InlineData("""{ "collision": { "shape": "capsule", "radius": 0.1, "height": 0.1 } }""", "$.collision.height", "twice the radius")]
    [InlineData("""{ "collision": { "shape": "convex" } }""", "$.collision.mesh", "needs a mesh")]
    [InlineData("""{ "collision": {} }""", "$.collision.shape", "box, sphere, cylinder, capsule, convex")]
    [InlineData("""{ "visual": { "color": "red" } }""", "$.visual.color", "hex text")]
    [InlineData("""{ "visual": { "roughness": 1.5 } }""", "$.visual.roughness", "between 0 and 1")]
    [InlineData("""{ "visual": { "metallic": -1 } }""", "$.visual.metallic", "between 0 and 1")]
    [InlineData("""{ "visual": { "opacity": 1.2 } }""", "$.visual.opacity", "between 0 and 1")]
    [InlineData("""{ "visual": { "emission": "#ffd8" } }""", "$.visual.emission", "hex text")]
    [InlineData("""{ "visual": { "texture": "textures/a.jpg" } }""", "$.visual.texture", ".png")]
    [InlineData("""{ "visual": { "variants": [{ "id": "red", "name": "Red", "color": "#ff00zz" }] } }""", "$.visual.variants[0].color", "hex text")]
    [InlineData("""{ "visual": { "variants": [{ "id": "red", "color": "#ff0000" }] } }""", "$.visual.variants[0].name", "name is required")]
    [InlineData("""{ "light": { "energy": 1 } }""", "$.light.range", "range (m) is required")]
    [InlineData("""{ "light": { "range": 0 } }""", "$.light.range", "greater than 0")]
    [InlineData("""{ "light": { "range": 5, "energy": -1 } }""", "$.light.energy", "0 or more")]
    [InlineData("""{ "light": { "range": 5, "color": "white" } }""", "$.light.color", "hex text")]
    [InlineData("""{ "light": { "range": 5, "kind": "spot", "angle": 90 } }""", "$.light.angle", "under 90 degrees")]
    [InlineData("""{ "light": { "range": 5, "offset": [0, 0.2, 0] } }""", "$.light.offset", "outside the part's [1, 1, 1] footprint")]
    [InlineData("""{ "light": { "range": 5, "direction": [0, 0, 0] } }""", "$.light.direction", "non-zero")]
    public void BadValuesGiveClearErrors(string overrides, string path, string messagePart)
    {
        var error = Single(TestMods.Block(overrides: overrides), path);

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains(messagePart, error.Message);
    }

    [Fact]
    public void SocketsAreChecked()
    {
        var diagnostics = Check(TestMods.Block(overrides: """
            {
              "sockets": [
                { "id": "top", "type": "structural", "position": [0, 0.125, 0], "normal": [0, 1, 0] },
                { "id": "top", "type": "rotational", "position": [0, 0.5, 0], "normal": [0, 2, 0] },
                { "id": "x" }
              ]
            }
            """));

        Assert.Equal(
            ["$.sockets[1].id", "$.sockets[1].position", "$.sockets[1].normal", "$.sockets[2].type", "$.sockets[2].position", "$.sockets[2].normal"],
            diagnostics.Select(d => d.JsonPath));
        Assert.Contains("outside the part's [1, 1, 1] footprint", diagnostics[1].Message);
    }

    [Fact]
    public void VariantIdsAreUniqueAndColourRequired()
    {
        var diagnostics = Check(TestMods.Block(overrides: """
            { "visual": { "color": "#A07040", "variants": [{ "id": "a", "name": "A", "color": "#000000" }, { "id": "a", "name": "B" }] } }
            """));

        Assert.Equal(["$.visual.variants[1].id", "$.visual.variants[1].color"], diagnostics.Select(d => d.JsonPath));
    }

    [Fact]
    public void ALightIsValidOnAPlaceablePartOnly()
    {
        Assert.Empty(Check(TestMods.Block(overrides: """
            { "light": { "kind": "spot", "color": "#fff0c0", "energy": 2, "range": 20, "angle": 30, "offset": [0, 0, -0.1], "direction": [0, -1, -1] } }
            """)));
        var error = Single("""{ "id": "a", "name": "A", "category": "resource", "light": { "range": 4 } }""", "$.light");

        Assert.Contains("only placeable parts", error.Message);
    }

    [Fact]
    public void ItemsCannotHaveSockets()
    {
        var error = Single("""
            { "id": "a", "name": "A", "category": "resource",
              "sockets": [{ "id": "s", "type": "structural", "position": [0, 0, 0], "normal": [0, 1, 0] }] }
            """, "$.sockets");

        Assert.Contains("only placeable parts", error.Message);
    }

    [Fact]
    public void UnknownKeysWarnWithSuggestionAndArePreserved()
    {
        using var mods = new TestMods().Mod("m").Part("m", "p.json", TestMods.Block(overrides: """{ "flamable": true, "editor_note": "x" }"""));

        var result = mods.Load();

        Assert.False(result.HasErrors);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.Contains("Did you mean 'flammable'?", result.Diagnostics[0].Message);
        Assert.DoesNotContain("Did you mean", result.Diagnostics[1].Message);
        var extra = result.Parts["m:test_block"].Definition.Extra!;
        Assert.Equal(["flamable", "editor_note"], extra.Keys);
    }

    [Theory]
    [InlineData("res://parts/bearing.glb", "URL or engine path")]
    [InlineData("../other/meshes/a.glb", "'..'")]
    [InlineData("meshes/../../a.glb", "'..'")]
    [InlineData("/home/me/a.glb", "absolute")]
    [InlineData("C:/models/a.glb", "absolute paths are not allowed")]
    [InlineData("meshes\\a.glb", "backslashes")]
    [InlineData("meshes/a.mesh", ".glb or .gltf")]
    [InlineData("meshes/missing.glb", "was not found")]
    public void BadMeshPathsAreRejected(string mesh, string messagePart)
    {
        var error = Single(TestMods.Block(overrides: $$"""{ "mesh": "{{mesh.Replace("\\", "\\\\")}}" }"""), "$.mesh");

        Assert.Contains(messagePart, error.Message);
    }

    [Fact]
    public void ScriptAndIconExtensionsAreChecked()
    {
        var diagnostics = Check(TestMods.Block(overrides: """{ "script": "scripts/a.js", "icon": "icons/a.jpg" }"""));

        Assert.Equal(["$.icon", "$.script"], diagnostics.Select(d => d.JsonPath));
    }

    [Fact]
    public void ExistingAssetsPass() =>
        Assert.Empty(Check(
            TestMods.Block(overrides: """{ "mesh": "meshes/a.glb", "script": "scripts/a.lua", "icon": "textures/a.png" }"""),
            mods => mods.File("m", "meshes/a.glb", "x").File("m", "scripts/a.lua", "").File("m", "textures/a.png", "x")));
}
