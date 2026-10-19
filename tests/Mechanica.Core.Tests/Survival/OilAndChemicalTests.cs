using System.Numerics;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Survival;

/// <summary>World POI plan: oil geysers gathered by hand, and chemical pools an empty bucket collects from and that hurt.</summary>
public class OilAndChemicalTests
{
    private const string Oil = "test:crude_oil";
    private const string Chemical = "test:chemical";

    private readonly SurvivalFixture _f = new(new SurvivalSettings { BucketItem = EmptyBucket, WaterItem = Bucket, Reach = 5, WaterSeconds = 1 });

    private readonly WorldObjectId _geyser = WorldObjectId.ForPoi(3, 1);

    public OilAndChemicalTests()
    {
        _f.World.Feet[Me.Peer] = Vector3.Zero;
        _f.Catalog.Add(Oil, new PartDefinition { Id = "crude_oil", Name = "Crude Oil", Category = PartCategory.Resource, StackSize = 20,
            Sounds = new Dictionary<string, string> { ["pickup"] = "pickup_fuel" } });
        _f.Catalog.Add(Chemical, new PartDefinition { Id = "chemical", Name = "Chemical", Category = PartCategory.Resource, StackSize = 20 });
        _f.World.Objects[_geyser] = new WorldObjectPlacement(_geyser, new WorldObjectDefinition
        {
            Id = "oil_geyser", Harvest = true, Regrow = 3600, Drops = [new ObjectDrop { Item = Oil, Count = 1, CountMax = 4 }],
        }, new Vector3(0, 0, -2), 0, 1);
    }

    private object Geyser => new { @object = _geyser.ToString() };

    [Fact]
    public void PressingUseOnAGeyserGathersOneToFourCrudeOilAndUsesItUp()
    {
        var result = _f.Ok<HarvestResult>(Me, SurvivalActions.Harvest, Geyser);
        Assert.InRange(result.Taken, 1, 4);
        Assert.Equal(0, result.Left);
        Assert.Equal(result.Taken, _f.Pockets(Me).Count(Oil));
        Assert.True(_f.World.State.IsDestroyed(_geyser));
        Assert.Equal(("pickup_fuel", "test"), (_f.World.Sounds.Single().Sound, _f.World.Sounds.Single().Mod));
        Assert.Contains("nothing to harvest", _f.Refused(Me, SurvivalActions.Harvest, Geyser));
    }

    [Fact]
    public void OverManyGeysersEveryCountFromOneToFourComesUp()
    {
        var counts = new HashSet<int>();
        for (var i = 0; i < 60; i++)
        {
            var id = WorldObjectId.ForPoi(10 + i, 0);
            _f.World.Objects[id] = _f.World.Objects[_geyser] with { Id = id };
            counts.Add(_f.Ok<HarvestResult>(Me, SurvivalActions.Harvest, new { @object = id.ToString() }).Taken);
            _f.Pockets(Me).Remove(Oil, _f.Pockets(Me).Count(Oil));
        }
        Assert.Equal([1, 2, 3, 4], counts.Order());
    }

    [Fact]
    public void OnlyHarvestableObjectsInReachAreGathered()
    {
        var rock = new WorldObjectId(5, 0, 0);
        _f.World.Objects[rock] = new WorldObjectPlacement(rock, new WorldObjectDefinition { Id = "boulder" }, new Vector3(0, 0, -1), 0, 1);
        Assert.Equal("The boulder cannot be harvested by hand.", _f.Refused(Me, SurvivalActions.Harvest, new { @object = rock.ToString() }));
        _f.World.Feet[Me.Peer] = new Vector3(0, 0, 8);
        Assert.StartsWith("That is ", _f.Refused(Me, SurvivalActions.Harvest, Geyser));
        Assert.Contains("nothing to harvest", _f.Refused(Me, SurvivalActions.Harvest, new { @object = "nonsense" }));
    }

    [Fact]
    public void WhatDoesNotFitIsLeftWhereTheGeyserStood()
    {
        var pockets = _f.Pockets(Me);
        for (var s = 0; s < pockets.SlotCount; s++)
            pockets.SetSlot(s, new ItemStack(Crop, 20));
        var result = _f.Ok<HarvestResult>(Me, SurvivalActions.Harvest, Geyser);
        Assert.Equal(0, result.Taken);
        Assert.Equal(result.Left, Assert.Single(_f.Survival.Drops.Values).Items.Single().Count);
    }

    [Fact]
    public void AnEmptyBucketDippedInAChemicalPoolCollectsAChemicalAndStaysEmpty()
    {
        _f.World.Pools.Add(Pool(new LiquidDefinition { Id = "chemical", Damage = 10, Collect = new ObjectDrop { Item = Chemical, Count = 1 } }));
        _f.Pockets(Me).Add(EmptyBucket, 1);
        var slot = _f.SlotOf(Me, EmptyBucket);
        var dip = new { point = new Vec3(0, -0.5f, 2), slot };
        Assert.Equal(new CollectWaterResult(slot), _f.Ok<CollectWaterResult>(Me, SurvivalActions.CollectWater, dip));
        Assert.Equal((1, 1, 0), (_f.Pockets(Me).Count(EmptyBucket), _f.Pockets(Me).Count(Chemical), _f.Pockets(Me).Count(Bucket)));
        Assert.Equal("You are still filling the last one.", _f.Refused(Me, SurvivalActions.CollectWater, dip));
        _f.World.Now += 1;
        _f.Ok(Me, SurvivalActions.CollectWater, dip);
        Assert.Equal(2, _f.Pockets(Me).Count(Chemical));
        // Above the surface is not in the pool, and there is no sea there either.
        Assert.Equal("There is no water there.", _f.Refused(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0.5f, 2), slot }));
    }

    [Fact]
    public void ABucketCannotHoldALiquidThatCollectsNothing()
    {
        _f.World.Pools.Add(Pool(new LiquidDefinition { Id = "oil" }));
        _f.Pockets(Me).Add(EmptyBucket, 1);
        Assert.Equal("A bucket cannot hold that.", _f.Refused(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, -0.5f, 2) }));
    }

    [Fact]
    public void StandingInAChemicalPoolHurtsEveryHalfSecondAndOilDoesNot()
    {
        _f.World.Pools.Add(Pool(new LiquidDefinition { Id = "chemical", Damage = 10 }));
        _f.World.Pools.Add(Pool(new LiquidDefinition { Id = "oil" }, centre: new Vector3(40, 0, 0)));
        var hurts = new List<(int Peer, float Amount, string Cause)>();
        _f.Survival.Hurt += (peer, amount, cause) => hurts.Add((peer, amount, cause));
        _f.World.Feet[Me.Peer] = new Vector3(0, -0.8f, 2);
        _f.Survival.Tick(0.25f);
        Assert.Empty(hurts);
        _f.Survival.Tick(0.25f);
        Assert.Equal([(Me.Peer, 5f, "chemical")], hurts.Select(h => (h.Peer, MathF.Round(h.Amount, 3), h.Cause)));
        Assert.Equal(95f, _f.Survival.View(Me.Peer)!.Health, 3);
        // Wading out stops it; oil is only dark water.
        _f.World.Feet[Me.Peer] = new Vector3(40, -0.8f, 0);
        for (var t = 0; t < 8; t++)
            _f.Survival.Tick(0.25f);
        Assert.Single(hurts);
    }

    // A pool 10 m across round the centre, its surface at the centre's height.
    private static PoiPoolPlacement Pool(LiquidDefinition liquid, Vector3? centre = null)
    {
        var poi = new PoiInstance(0, new LoadedPoi("test:pond", new PoiPrefab { Id = "pond", Radius = 20 }, new HashSet<int>()), Vector2.Zero, 0, 0);
        return new PoiPoolPlacement(poi, new LiquidPool { Liquid = liquid.Id, Radius = 5 }, liquid, centre ?? Vector3.Zero);
    }
}
