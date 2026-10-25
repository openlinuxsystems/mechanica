using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// The parts audit's fixes (PARTS_AUDIT.md in the project files): stats and recipes it corrected that no batch test
/// already covers.
/// </summary>
public class PartsAuditTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    [Fact]
    public void ARefinerMakesCircuitBoardsFromAMetalRodAndGlass()
    {
        // Audit #18 (Chase chose the Refiner recipe): 54 recipes need Circuit Boards, so loot and bot drops alone
        // are a choke point under the abundance rule.
        var recipe = RecipeBook.Load(Vanilla.Value).Find("circuit_board")!;

        Assert.Equal(["vanilla:refiner"], recipe.Stations);
        Assert.Equal(["vanilla:metal_rod:1", "vanilla:glass_block:2"], recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(("vanilla:circuit_board", 2), (recipe.Outputs.Single().Item, recipe.Outputs.Single().Count));
        Assert.Equal(10f, recipe.Time);
        Assert.Equal(2650, recipe.Definition.Order);
    }
}
