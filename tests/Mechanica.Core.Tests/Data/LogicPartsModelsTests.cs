using System.Text.Json;
using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// The parts audit's batch D2 (tools/logic/): the Horn, Radio, Timer and Logic Gate have built models and icons in
/// place of their Part Editor boxes, keep their sizes and base sockets, keep the recipes and orders the audit kept, and
/// their models carry the clips their part data plays.
/// </summary>
public class LogicPartsModelsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static string VanillaDir => Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("horn", 1, 1, 1)]
    [InlineData("radio", 2, 2, 1)]
    [InlineData("timer", 1, 1, 1)]
    [InlineData("logic_gate", 1, 1, 1)]
    public void EachPartHasItsModelAndIcon(string id, int x, int y, int z)
    {
        var part = Part(id);

        Assert.Equal($"meshes/parts/{id}.glb", part.Mesh);
        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        Assert.True(File.Exists(Path.Combine(VanillaDir, part.Mesh!)), $"{id} has its model");
        Assert.True(File.Exists(Path.Combine(VanillaDir, part.Icon!)), $"{id} has its icon");
        Assert.False(File.Exists(Path.Combine(VanillaDir, "meshes", $"{id}.glb")), $"{id}'s old placeholder mesh is gone");
        Assert.Equal(new GridSize(x, y, z), part.Size);
        var socket = Assert.Single(part.Sockets!);
        Assert.Equal("base", socket.Id);
        Assert.Equal(new Vec3(0, -0.125f * y, 0), socket.Position);
    }

    [Theory]
    [InlineData("horn", "on", "off")]
    [InlineData("radio", "on", "off", "tune")]
    [InlineData("timer", "count_loop", "rest", "lit")]
    [InlineData("logic_gate", "and", "or", "xor", "nand", "nor", "xnor", "lit")]
    public void EachModelHasTheClipsItsPartPlays(string id, params string[] clips)
    {
        var part = Part(id);
        var display = part.Display?.Clips ?? throw new Xunit.Sdk.XunitException($"{id} plays no clips");
        var inModel = ClipNames(Path.Combine(VanillaDir, part.Mesh!));

        Assert.Equal(clips.Order(StringComparer.Ordinal), inModel.Order(StringComparer.Ordinal));
        Assert.All(display.Scrub ?? [], s => Assert.Contains(s.Clip!, inModel));
    }

    [Theory]
    [InlineData("horn", 10, 1410, "metal_block:1", "circuit_board:1", "glue:1")]
    [InlineData("radio", 10, 1420, "metal_block:5", "circuit_board:1", "glue:1")]
    [InlineData("timer", 45, 1240, "metal_block:2", "circuit_board:2", "glue:1")]
    [InlineData("logic_gate", 20, 1230, "metal_block:1", "circuit_board:1", "glue:1")]
    public void TheRecipesAreUnchanged(string id, float time, int order, params string[] inputs)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find(id)!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(inputs.Select(i => $"vanilla:{i}"), recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(1, recipe.Outputs.Single().Count);
        Assert.Equal(time, recipe.Time);
        Assert.Equal(order, recipe.Definition.Order);
        Assert.Single(book.All, r => r.Outputs.Any(o => o.Item == $"vanilla:{id}"));
    }

    // The animation names in a .glb's JSON chunk.
    private static List<string> ClipNames(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var length = BitConverter.ToInt32(bytes, 12);
        using var doc = JsonDocument.Parse(bytes.AsMemory(20, length));
        return doc.RootElement.TryGetProperty("animations", out var animations)
            ? [.. animations.EnumerateArray().Select(a => a.GetProperty("name").GetString()!)]
            : [];
    }
}
