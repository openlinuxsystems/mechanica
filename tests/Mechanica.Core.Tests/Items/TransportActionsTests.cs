using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Tests.Survival;
using static Mechanica.Core.Tests.Items.TransportParts;

namespace Mechanica.Core.Tests.Items;

/// <summary>
/// The item transport actions (Parts map E1) a Vacuum Pump's and a Resource Collector's scripts call: what an intake
/// reaches (<c>GetPickups</c>), taking a pile, a loose part, a ripe crop or water into the containers piped to it
/// (<c>CollectItems</c>), and throwing items from them out of the intake (<c>EjectItems</c>). Only part scripts collect
/// and eject; every check is the host's.
/// </summary>
public sealed class TransportActionsTests
{
    private const string Log = "test:log";
    private const string Tank = "test:tank";
    private const string Collector = "test:collector";
    private const string Water = "vanilla:water_bucket_full";

    private static readonly ActionCaller Script = new(1, ActionSource.Script);

    private readonly SurvivalFixture _f = new();
    private readonly TransportActions _transport;

    public TransportActionsTests()
    {
        Catalog(_f.Catalog)
            .Add(Log, new PartDefinition { Id = "log", Name = "Log", Category = PartCategory.Resource, Mass = 10, Size = GridSize.One, Loose = true,
                Flammable = true, StackSize = 10 })
            .Add(Water, new PartDefinition { Id = "water_bucket_full", Name = "Water", Category = PartCategory.Consumable, StackSize = 5 })
            .Add(Tank, Placeable("tank") with
            {
                Inventory = new InventoryDefinition { Slots = 2, Accepts = [Water] },
                IoPorts = [new ItemPortDefinition { Id = "port", Position = new Vec3(0, 0, 0.125f), Normal = new Vec3(0, 0, 1) }],
            })
            .Add(Collector, Placeable("collector") with
            {
                Inventory = new InventoryDefinition { Slots = 4 },
                IoPorts = [new ItemPortDefinition { Id = "port", Position = new Vec3(0, 0, 0.125f), Normal = new Vec3(0, 0, 1), Mode = ItemPortMode.Out }],
                Intake = new IntakeDefinition { Position = new Vec3(0, 0.125f, 0), Normal = new Vec3(0, 1, 0), Reach = 3 },
            });
        _transport = new TransportActions(_f.Catalog, _f.Survival);
        _transport.Register(_f.Dispatcher);
    }

    // A deck of blocks along z 0..3 with a chest (or `container`) at z 0, pipes at z 1 and 2 and the pump at z 3, its intake
    // facing +Z at about (0.125, 0.125, 1). Returns the Creation, the container and the pump.
    private (Creation Creation, int Chest, int Pump) PumpLine(string container = Chest)
    {
        var c = new Creation(_f.World.NextCreationId(), _f.Catalog);
        for (var z = 0; z < 4; z++)
            Assert.True(c.TryPlacePart(Block, new GridCell(0, -1, z), default, out _, out var e), e);
        Assert.True(c.TryPlacePart(container, new GridCell(0, 0, 0), default, out var chest, out var error), error);
        Assert.True(c.TryPlacePart(Pipe, new GridCell(0, 0, 1), default, out _, out error), error);
        Assert.True(c.TryPlacePart(Pipe, new GridCell(0, 0, 2), default, out _, out error), error);
        Assert.True(c.TryPlacePart(Pump, new GridCell(0, 0, 3), default, out var pump, out error), error);
        c.SetAllBodyPoses(new Pose(new Vector3(0, 0.5f, 0), Quaternion.Identity));
        _f.World.Added(c);
        return (c, chest!.Id, pump!.Id);
    }

    private ActionResult Collect(ActionCaller caller, int creation, int part, PickupKind kind, int? drop = null, int? target = null, int? targetPart = null) =>
        _f.Dispatch(caller, TransportActions.CollectItems, new CollectItemsArgs(creation, part, kind, drop, target, targetPart));

    [Fact]
    public void APumpSucksAPileIntoTheChestPipedToIt()
    {
        var (c, chest, pump) = PumpLine();
        var drop = _f.Survival.AddDrop(new Vector3(0.1f, 0.5f, 1.8f), [new ItemStack(SurvivalFixture.Scrap, 30), new ItemStack(SurvivalFixture.Crop, 2)])!;
        var far = _f.Survival.AddDrop(new Vector3(0.1f, 0.5f, 6f), [new ItemStack(SurvivalFixture.Scrap, 1)])!;

        var pickups = _transport.Pickups(c, pump);
        var seen = Assert.Single(pickups);
        Assert.Equal((PickupKind.Drop, drop.Id), (seen.Kind, seen.Drop));

        Assert.Contains("script", Collect(SurvivalFixture.Me, c.Id, pump, PickupKind.Drop, drop.Id).Message);
        Assert.Contains("reach", Collect(Script, c.Id, pump, PickupKind.Drop, far.Id).Message);
        var result = Collect(Script, c.Id, pump, PickupKind.Drop, drop.Id);
        Assert.True(result.Ok, result.ToString());
        Assert.Equal(30, c.InventoryOf(chest)!.Count(SurvivalFixture.Scrap));
        Assert.Equal(2, c.InventoryOf(chest)!.Count(SurvivalFixture.Crop));
        Assert.False(_f.Survival.Drops.ContainsKey(drop.Id));
    }

    [Fact]
    public void WhatDoesNotFitStaysInThePile()
    {
        var (c, chest, pump) = PumpLine();
        // Four slots of scrap (50 a stack): 250 leaves 50 behind.
        var drop = _f.Survival.AddDrop(new Vector3(0, 0.5f, 1.5f), [new ItemStack(SurvivalFixture.Scrap, 250)])!;
        Assert.True(Collect(Script, c.Id, pump, PickupKind.Drop, drop.Id).Ok);
        Assert.Equal(200, c.InventoryOf(chest)!.Count(SurvivalFixture.Scrap));
        Assert.Equal(50, _f.Survival.Drops[drop.Id].Items.Single().Count);
        Assert.Equal("There is no room for it.", Collect(Script, c.Id, pump, PickupKind.Drop, drop.Id).Message);
    }

    [Fact]
    public void NothingPipedMeansNothingCollected()
    {
        var (c, _, pump) = PumpLine();
        c.RemovePart(c.Parts.First(p => p.Part == Pipe).Id, _f.World.NextCreationId);
        var drop = _f.Survival.AddDrop(new Vector3(0, 0.5f, 1.5f), [new ItemStack(SurvivalFixture.Scrap, 1)])!;
        Assert.Equal("Nothing is piped to it to take things in.", Collect(Script, c.Id, pump, PickupKind.Drop, drop.Id).Message);
    }

    [Fact]
    public void ALooseLogIsTakenInWhole()
    {
        var (c, chest, pump) = PumpLine();
        var log = _f.Add(Log, new Vector3(0, 0.3f, 2.2f));
        var seen = Assert.Single(_transport.Pickups(c, pump));
        Assert.Equal((PickupKind.Loose, log), (seen.Kind, seen.Creation));
        Assert.True(Collect(Script, c.Id, pump, PickupKind.Loose, target: log).Ok);
        Assert.Equal(1, c.InventoryOf(chest)!.Count(Log));
        Assert.Null(_f.World.FindCreation(log));
        // A built Creation is never sucked in, however close.
        Assert.Empty(_transport.Pickups(c, pump));
    }

    [Fact]
    public void ARipeCropIsHarvestedThroughTheSoilsScript()
    {
        var (c, chest, pump) = PumpLine();
        var soil = _f.Add(SurvivalFixture.Soil, new Vector3(0, 0.3f, 2.5f));
        var state = _f.World.FindCreation(soil)!.SoilOf(1)!;
        state.Plant(SurvivalFixture.Seed);
        Assert.Empty(_transport.Pickups(c, pump));
        state.Set(grown: 40, water: 0, stage: 4, updated: 1);
        var seen = Assert.Single(_transport.Pickups(c, pump));
        Assert.Equal((PickupKind.Crop, soil, 1, SurvivalFixture.Crop), (seen.Kind, seen.Creation, seen.Part, seen.Items[0].Item));

        Assert.Equal("Crops cannot be collected here.", Collect(Script, c.Id, pump, PickupKind.Crop, target: soil, targetPart: 1).Message);
        _transport.HarvestCrop = (creation, part) =>
        {
            creation.SoilOf(part)!.Clear();
            return [new ItemStack(SurvivalFixture.Crop, 3), new ItemStack(SurvivalFixture.Seed, 1)];
        };
        Assert.True(Collect(Script, c.Id, pump, PickupKind.Crop, target: soil, targetPart: 1).Ok);
        Assert.Equal(3, c.InventoryOf(chest)!.Count(SurvivalFixture.Crop));
        Assert.Equal(1, c.InventoryOf(chest)!.Count(SurvivalFixture.Seed));
        Assert.Null(state.Seed);
    }

    [Fact]
    public void WaterGoesOnlyIntoATankThatTakesIt()
    {
        var (chestLine, _, chestPump) = PumpLine();
        _f.World.WaterLevel = 5;
        var water = Assert.Single(_transport.Pickups(chestLine, chestPump));
        Assert.Equal(PickupKind.Water, water.Kind);
        Assert.Equal("Nothing piped to it holds water.", Collect(Script, chestLine.Id, chestPump, PickupKind.Water).Message);

        var (tankLine, tank, tankPump) = PumpLine(Tank);
        Assert.True(Collect(Script, tankLine.Id, tankPump, PickupKind.Water).Ok);
        Assert.True(Collect(Script, tankLine.Id, tankPump, PickupKind.Water).Ok);
        Assert.Equal(2, tankLine.InventoryOf(tank)!.Count(Water));
    }

    [Fact]
    public void APumpEjectsItemsFromThePipedChestOutOfItsMouth()
    {
        var (c, chest, pump) = PumpLine();
        Assert.Equal("The containers piped to it are empty.", _f.Dispatch(Script, TransportActions.EjectItems, new EjectItemsArgs(c.Id, pump)).Message);
        c.InventoryOf(chest)!.Add(SurvivalFixture.Scrap, 12);
        Assert.Contains("script", _f.Dispatch(SurvivalFixture.Me, TransportActions.EjectItems, new EjectItemsArgs(c.Id, pump)).Message);
        Assert.Contains("count", _f.Dispatch(Script, TransportActions.EjectItems, new EjectItemsArgs(c.Id, pump, SurvivalFixture.Scrap, 51)).Message);

        var result = _f.Ok<EjectItemsResult>(Script, TransportActions.EjectItems, new EjectItemsArgs(c.Id, pump, SurvivalFixture.Scrap, 5));
        Assert.Equal((SurvivalFixture.Scrap, 5), (result.Item, result.Count));
        Assert.Equal(7, c.InventoryOf(chest)!.Count(SurvivalFixture.Scrap));
        // One metre (the intake's throw) out of the mouth, on the ground below.
        var drop = _f.Survival.Drops[result.Drop];
        Assert.Equal(0f, drop.Position.X, 3);
        Assert.Equal(0f, drop.Position.Y, 3);
        Assert.Equal(0.875f + 1f, drop.Position.Z, 3);
        Assert.Equal(5, drop.Items.Single().Count);
    }

    [Fact]
    public void ACollectorTakesThingsIntoItselfAndSendsThemOnThroughItsPort()
    {
        var c = new Creation(_f.World.NextCreationId(), _f.Catalog);
        Assert.True(c.TryPlacePart(Block, new GridCell(0, -1, 0), default, out _, out var error), error);
        Assert.True(c.TryPlacePart(Collector, new GridCell(0, 0, 0), default, out var collector, out error), error);
        c.SetAllBodyPoses(new Pose(Vector3.Zero, Quaternion.Identity));
        _f.World.Added(c);
        var log = _f.Add(Log, new Vector3(2, 0, 1));
        Assert.True(Collect(Script, c.Id, collector!.Id, PickupKind.Loose, target: log).Ok);
        Assert.Equal(1, c.InventoryOf(collector.Id)!.Count(Log));
    }

    [Fact]
    public void TheNetworkAndPickupsCanBeRead()
    {
        var (c, chest, pump) = PumpLine();
        var links = _f.Ok<List<PipeLinkView>>(SurvivalFixture.Me, TransportActions.GetNetwork, new TransportPartArgs(c.Id, pump));
        var link = Assert.Single(links);
        Assert.Equal((chest, Chest, true, true, 2), (link.Part, link.Type, link.TakesIn, link.Storage, link.Pipes.Count));
        Assert.True(_f.Dispatcher.IsReadOnly(TransportActions.GetNetwork));
        Assert.True(_f.Dispatcher.IsReadOnly(TransportActions.GetPickups));
        Assert.Contains("no intake", _f.Dispatch(SurvivalFixture.Me, TransportActions.GetPickups, new TransportPartArgs(c.Id, chest)).Message);
    }
}
