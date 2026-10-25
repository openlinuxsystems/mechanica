using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// The parts audit's batches A1 and D1 (flight parts): the Hover Jet and Rotor burn batteries like the Electric Engine,
/// all four flight parts are made of blocks at the Fabricator in the flight group (1100, docs/part-data-schema.md), the
/// Hover Jet no longer beats the Thruster on mileage, and each has a built model and icon at its old size.
/// </summary>
public class FlightPartsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    private static float Number(PartDefinition part, string key) => part.Properties![key]!.GetValue<float>();

    [Theory]
    [InlineData("hover_jet", 30, 1120, "metal_block_2:20", "component_kit:3", "circuit_board:2", "glue:1")]
    [InlineData("rotor", 30, 1130, "metal_block_2:10", "aluminum_block:10", "component_kit:2", "circuit_board:1")]
    [InlineData("wing", 10, 1140, "aluminum_block:10", "glue:1")]
    [InlineData("control_surface", 10, 1150, "aluminum_block:5", "bearing:1", "circuit_board:1")]
    public void FlightPartsAreMadeOfBlocksAtTheFabricator(string id, float time, int order, params string[] inputs)
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find(id)!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(inputs.Select(i => $"vanilla:{i}"), recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(1, recipe.Outputs.Single().Count);
        Assert.Equal(time, recipe.Time);
        Assert.Equal(order, recipe.Definition.Order);
        Assert.DoesNotContain(recipe.Inputs, i => i.Item == "vanilla:scrap_metal_block");
        // The order is its own in the flight group.
        Assert.Single(book.All, r => r.Definition.Order == order);
    }

    [Theory]
    [InlineData("hover_jet", 60)]
    [InlineData("rotor", 120)]
    public void TheHoverJetAndRotorBurnBatteriesLikeTheElectricEngine(string id, float fuelTime)
    {
        var part = Part(id);

        Assert.Equal(Part("electric_engine").Properties!["fuel"]!.GetValue<string>(), part.Properties!["fuel"]!.GetValue<string>());
        Assert.Equal("battery_pack", part.Properties!["fuel"]!.GetValue<string>());
        Assert.Equal(fuelTime, Number(part, "fuel_time"));
        Assert.Equal(["fuel", "fuel_items"], part.Menu!.Readouts!.Select(r => r.Id));
        Assert.Contains("Battery Containers", part.Description);
        Assert.Contains("Rotors and Hover Jets", Part("battery").Description);
    }

    [Fact]
    public void TheHoverJetGoesLessFarOnItsFuelThanTheThruster()
    {
        // Push times seconds of full power per item: the Thruster 1 on step 1 (a higher step burns as much more as it
        // pushes), against the Hover Jet on a battery. Batteries also cost more to make than gasoline.
        var thruster = Part("thruster");
        var jet = Part("hover_jet");
        var perGasoline = Number(thruster, "thrust") * Number(thruster, "fuel_time");
        var perBattery = Number(jet, "thrust") * Number(jet, "fuel_time");
        Assert.True(perBattery < perGasoline, $"the jet gives {perBattery} N·s a battery, the thruster {perGasoline} N·s a gasoline");
        // And it has no power steps or upgrades: one Thruster 1 on step 3 pushes harder than a Hover Jet.
        Assert.Null(jet.Upgrades);
        Assert.True(Number(thruster, "thrust") * MathF.Pow(Number(thruster, "step_gain"), 2) > Number(jet, "thrust"));
    }

    [Theory]
    [InlineData("wing", 4, 1, 4, null)]
    [InlineData("control_surface", 4, 1, 2, null)]
    [InlineData("rotor", 3, 1, 3, "blades")]
    [InlineData("hover_jet", 2, 1, 2, "pose")]
    public void FlightPartsHaveBuiltModelsAndIconsAtTheirSize(string id, int x, int y, int z, string? clips)
    {
        var part = Part(id);
        var vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

        Assert.Equal(new GridSize(x, y, z), part.Size);
        Assert.Equal($"meshes/parts/{id}.glb", part.Mesh);
        Assert.Equal($"textures/items/{id}_icon.png", part.Icon);
        Assert.True(File.Exists(Path.Combine(vanilla, part.Mesh!)), $"{id} has its model");
        Assert.True(File.Exists(Path.Combine(vanilla, part.Icon!)), $"{id} has its icon");
        Assert.False(File.Exists(Path.Combine(vanilla, "meshes", $"{id}.glb")), "the placeholder model is gone");
        Assert.Equal("#ffffff", part.Visual?.Color);
        Assert.Equal(clips, part.Display?.Clips?.Readout);
    }

    [Fact]
    public void ThePartFilesAreTheBatchOutput() =>
        new VanillaBlocksTests().PartFilesAreThePartEditorBatchOutput("aero.json", 5);
}
