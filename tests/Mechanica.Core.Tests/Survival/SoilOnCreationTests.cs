using System.Numerics;
using System.Text.Json.Nodes;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Survival;

/// <summary>
/// Soil on a Creation (parts map E9, the Growbed): a part with a <c>soil</c> section that is not ground-only is built
/// into a Creation with other parts. Its crop is planted, watered and kept on the part, follows it when the Creation
/// splits, saves and loads with the Creation, and only takes the seeds its data lists. Picking the bed up gives an
/// unripe crop's seed back and is refused while the crop is ripe; breaking it drops the seed.
/// </summary>
public sealed class SoilOnCreationTests
{
    private const string Growbed = "test:growbed";
    private const string Other = "test:other_seed";

    private readonly SurvivalFixture _f = new();

    public SoilOnCreationTests()
    {
        _f.Catalog
            .Add(Growbed, new PartDefinition
            {
                Id = "growbed", Name = "Growbed", Category = PartCategory.Interactive, Mass = 4.5f, Durability = 3, Flammable = false,
                Size = new GridSize(1, 1, 1), StackSize = 5, Script = "scripts/soil.lua",
                Soil = new SoilDefinition { Capacity = 600, Growth = 1, Yield = 5, Seeds = ["seed"] },
                Properties = new JsonObject { ["interval"] = 0.5, ["charge_item"] = "battery", ["charge_time"] = 360 },
            })
            .Add(Other, new PartDefinition { Id = "other_seed", Name = "Other Seed", Category = PartCategory.Consumable, StackSize = 20,
                Seed = new SeedDefinition { Crop = Crop, GrowTime = 40 } });
    }

    // A cart: three blocks in a row with the growbed on the middle one (part 4), near the player.
    private Creation Cart()
    {
        var creation = new Creation(_f.World.NextCreationId(), _f.Catalog);
        for (var x = 0; x < 3; x++)
            Assert.True(creation.TryPlacePart(Block, new GridCell(x, 0, 0), default, out _, out var error), error);
        Assert.True(creation.TryPlacePart(Growbed, new GridCell(1, 1, 0), default, out var bed, out var why), why);
        Assert.Equal(4, bed!.Id);
        creation.SetAllBodyPoses(new Pose(new Vector3(0, 0, 1), Quaternion.Identity));
        _f.World.Added(creation);
        return creation;
    }

    [Fact]
    public void ACreationsSoilPartGrowsACropThatStaysOnThePart()
    {
        var cart = Cart();
        Assert.Single(cart.Bodies);
        Assert.Null(_f.Survival.CheckPlant(Me, cart.Id, 4, Seed));
        Assert.Equal(new SoilView(cart.Id, 4, Seed, Crop, 0, 0, 600), _f.Survival.PlantSeed(Me, cart.Id, 4, Seed));
        Assert.Equal(150f, _f.Survival.WaterSoil(Me, cart.Id, 4, 150).Water);
        Assert.Equal(new[] { (cart.Id, 4) }, _f.Survival.SoilNear(new Vector3(0.25f, 0.25f, 1), 1f));

        // Moving the Creation moves nothing in the soil: it lives on the part, not at a place in the world.
        cart.SetAllBodyPoses(new Pose(new Vector3(500, 2, -300), Quaternion.CreateFromYawPitchRoll(1, 0, 0)));
        Assert.Equal(Seed, cart.SoilOf(4)!.Seed);
        Assert.Equal(150f, cart.SoilOf(4)!.Water);
        Assert.Equal(3f, _f.Survival.CropValue());

        // Knocking the end block off splits nothing away from the bed: it keeps its crop.
        var removed = cart.RemovePart(1, _f.World.NextCreationId);
        Assert.Empty(removed.SplitOff);
        Assert.Equal(Seed, cart.SoilOf(4)!.Seed);
    }

    [Fact]
    public void TheCropFollowsItsPartIntoAPieceThatSplitsOff()
    {
        var cart = Cart();
        _f.Survival.PlantSeed(Me, cart.Id, 4, Seed);
        cart.SoilOf(4)!.Set(grown: 20, water: 80, stage: 2, updated: 1234.5);
        // The middle block holds the bed: taking it off leaves the bed's piece apart from the end blocks.
        var removed = cart.RemovePart(2, _f.World.NextCreationId);
        var holder = new[] { cart }.Concat(removed.SplitOff).Single(c => c.FindPart(4) is not null);
        var soil = holder.SoilOf(4)!;
        Assert.Equal((Seed, 20f, 80f, 2, 1234.5), (soil.Seed, soil.Grown, soil.Water, soil.Stage, soil.Updated!.Value));
    }

    [Fact]
    public void TheCropSavesAndLoadsWithTheCreation()
    {
        var cart = Cart();
        _f.Survival.PlantSeed(Me, cart.Id, 4, Seed);
        cart.SoilOf(4)!.Set(grown: 30, water: 400, stage: 3, updated: 4321.25, wetAt: SoilState.WetAt(_f.Catalog.FindDefinition(Growbed)!.Soil));
        cart.SetAllBodyPoses(new Pose(new Vector3(130, 1, 64), Quaternion.Identity));

        var json = CreationJson.Write(cart);
        var diagnostics = new List<DataDiagnostic>();
        var read = CreationJson.Read(json, _f.Catalog, "test", diagnostics)!;
        Assert.Empty(diagnostics);
        var soil = read.SoilOf(4)!;
        Assert.Equal((Seed, 30f, 400f, 3, 4321.25, true), (soil.Seed, soil.Grown, soil.Water, soil.Stage, soil.Updated!.Value, soil.Soaked));
        Assert.Equal(4, read.Parts.Count);
        Assert.Equal(json, CreationJson.Write(read));
    }

    [Fact]
    public void FertilizerBoostsAGrowingCropAndTheBoostSavesWithTheCreation()
    {
        var cart = Cart();
        Assert.Equal("Nothing is growing there.", _f.Survival.CheckBoost(Me, cart.Id, 4, 15));
        _f.Survival.PlantSeed(Me, cart.Id, 4, Seed);
        Assert.Equal("A boost must be more than 1 and at most 100.", _f.Survival.CheckBoost(Me, cart.Id, 4, 1));
        Assert.Equal("A boost must be more than 1 and at most 100.", _f.Survival.CheckBoost(Me, cart.Id, 4, float.NaN));
        Assert.Equal(15f, _f.Survival.BoostSoil(Me, cart.Id, 4, 15).Boost);
        Assert.Equal("That crop already grows that fast.", _f.Survival.CheckBoost(Me, cart.Id, 4, 15));
        Assert.Throws<InvalidOperationException>(() => _f.Survival.BoostSoil(Me, cart.Id, 4, 10));

        var json = CreationJson.Write(cart);
        Assert.Contains("\"boost\"", json);
        var diagnostics = new List<DataDiagnostic>();
        var read = CreationJson.Read(json, _f.Catalog, "test", diagnostics)!;
        Assert.Empty(diagnostics);
        Assert.Equal(15f, read.SoilOf(4)!.Boost);
        Assert.Equal(15f, _f.Survival.SoilViewOf(read, 4).Boost);

        cart.SoilOf(4)!.Set(grown: 40, stage: SoilState.Stages);
        Assert.Equal("That crop is already ripe.", _f.Survival.CheckBoost(Me, cart.Id, 4, 20));
        cart.SoilOf(4)!.Clear();
        Assert.Equal(1f, cart.SoilOf(4)!.Boost);
        Assert.DoesNotContain("\"boost\"", CreationJson.Write(cart));
    }

    [Fact]
    public void SoilTakesOnlyTheSeedsItsDataLists()
    {
        var cart = Cart();
        Assert.Equal("The Growbed cannot grow Other Seed.", _f.Survival.CheckPlant(Me, cart.Id, 4, Other));
        Assert.Throws<InvalidOperationException>(() => _f.Survival.PlantSeed(Me, cart.Id, 4, Other));
        // Plain soil takes any seed.
        var mound = _f.Add(Soil, new Vector3(0, 0, 1));
        Assert.Null(_f.Survival.CheckPlant(Me, mound, 1, Other));
    }

    [Fact]
    public void PickingUpTheBedGivesAnUnripeCropsSeedBackAndWaitsForARipeHarvest()
    {
        var cart = Cart();
        _f.Survival.PlantSeed(Me, cart.Id, 4, Seed);
        _f.Pockets(Me).Remove(Seed, _f.Pockets(Me).Count(Seed));

        cart.SoilOf(4)!.Set(grown: 40, stage: SoilState.Stages);
        Assert.Equal("Harvest the Crop before picking up the Growbed.", _f.Refused(Me, BuildActions.RemovePart, new { creation = cart.Id, part = 4 }));

        cart.SoilOf(4)!.Set(grown: 10, stage: 1);
        _f.Ok(Me, BuildActions.RemovePart, new { creation = cart.Id, part = 4 });
        Assert.Null(cart.FindPart(4));
        Assert.Equal(1, _f.Pockets(Me).Count(Seed));
        Assert.Equal(1, _f.Pockets(Me).Count(Growbed));
    }

    [Fact]
    public void BreakingTheBedDropsItsSeed()
    {
        var cart = Cart();
        _f.Survival.PlantSeed(Me, cart.Id, 4, Seed);
        _f.Survival.Spill(cart, 4);
        var drop = Assert.Single(_f.Survival.Drops.Values);
        Assert.Equal(new ItemStack(Seed, 1), Assert.Single(drop.Items));
        Assert.Null(cart.SoilOf(4)!.Seed);
    }
}
