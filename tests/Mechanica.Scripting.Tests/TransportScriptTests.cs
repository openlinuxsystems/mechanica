using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Motion;
using Mechanica.Core.Tests.Items;
using Mechanica.Core.Tests.Survival;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Scripting.Tests;

/// <summary>
/// Item transport from Lua (parts map E1): the generic <c>part.network</c>, <c>part.pickups</c>, <c>part.collect</c> and
/// <c>part.eject</c>, the soil's <c>on_collect</c> harvest, and the vanilla Vacuum Pump and Resource Collector scripts on
/// their real data (a pump sucking a pile through Vacuum Pipes into a Small Chest and throwing it back out; a collector
/// pulling in a loose Wood Rod).
/// </summary>
public sealed class TransportScriptTests : IDisposable
{
    private static readonly ModLoadResult Vanilla = ModLoader.Load([ItemRig.VanillaRoot]);
    private static readonly ActionCaller Host = new(Creation.HostPeer, ActionSource.Player);

    private readonly ItemRig _rig = new();
    private readonly TransportActions _transport;

    public TransportScriptTests()
    {
        Assert.False(Vanilla.HasErrors, string.Join("; ", Vanilla.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        TransportParts.Catalog(F.Catalog);
        foreach (var id in new[] { "vacuum_pump", "vacuum_pipe", "vacuum_pipe_corner", "resource_collector", "small_chest", "switch",
                     "metal_block", "wood_rod", "scrap_wood_block" })
            F.Catalog.Add($"vanilla:{id}", Vanilla.FindDefinition($"vanilla:{id}")!);
        _transport = new TransportActions(F.Catalog, F.Survival);
        _transport.Register(F.Dispatcher);
        _rig.Runtime.EnableTransport(_transport);
    }

    private SurvivalFixture F => _rig.F;

    public void Dispose() => _rig.Dispose();

    // ---- The primitives ----

    [Fact]
    public void AScriptSeesItsNetworkAndCollectsAPileIntoThePipedChest()
    {
        var (c, chest, pump) = PumpLine("""
            function on_update(dt)
              part.display("links", tostring(#part.network()))
              local link = part.network()[1]
              part.display("reaches", link and (link.type .. " " .. link.port .. ">" .. link.remote_port .. " " .. #link.pipes) or "nothing")
              for _, pickup in ipairs(part.pickups()) do
                local ok, items = part.collect(pickup)
                part.display("kind", pickup.kind)
                part.display("got", ok and (items[1].item .. " x" .. items[1].count) or items)
              end
            end
            """);
        var drop = F.Survival.AddDrop(new Vector3(0, 0.5f, 1.4f), [new ItemStack(Scrap, 7)], placed: true)!;
        _rig.Runtime.Update(0.1f);

        Assert.Equal(7, c.InventoryOf(chest)!.Count(Scrap));
        Assert.False(F.Survival.Drops.ContainsKey(drop.Id));
        Assert.Equal("1", State(c, pump, "links"));
        Assert.Equal($"{TransportParts.Chest} port>port 1", State(c, pump, "reaches"));
        Assert.Equal(("drop", $"{Scrap} x7"), (State(c, pump, "kind"), State(c, pump, "got")));
        NoErrors();
    }

    [Fact]
    public void AScriptEjectsFromThePipedChestAndIsToldWhenThereIsNothing()
    {
        var (c, chest, pump) = PumpLine("""
            function on_update(dt)
              local ok, what = part.eject(nil, 3)
              part.display("result", ok and (what.item .. " x" .. what.count) or what)
            end
            """);
        c.InventoryOf(chest)!.Add(Scrap, 3);
        _rig.Runtime.Update(0.1f);
        Assert.Equal($"{Scrap} x3", State(c, pump, "result"));
        var pile = Assert.Single(F.Survival.Drops.Values);
        Assert.Equal(3, pile.Items.Single().Count);
        _rig.Runtime.Update(0.1f);
        Assert.Equal("The containers piped to it are empty.", State(c, pump, "result"));
        Assert.Equal(0, c.InventoryOf(chest)!.Count(Scrap));
    }

    [Fact]
    public void APumpHarvestsARipeCropThroughTheSoilsOnCollect()
    {
        var (c, chest, _) = PumpLine("""
            function on_update(dt)
              for _, pickup in ipairs(part.pickups()) do
                if pickup.kind == "crop" then
                  part.display("got", (part.collect(pickup)))
                end
              end
            end
            """);
        var soil = F.Add(Soil, new Vector3(0, 0.5f, 1.5f));
        _rig.Runtime.Update(1);
        F.Survival.PlantSeed(Me, soil, 1, Seed);
        F.Survival.WaterSoil(Me, soil, 1, 60);
        _rig.Runtime.Update(100); // ripe once the soil settles
        _rig.Runtime.Update(1); // and the pump sees it

        Assert.Equal(2, c.InventoryOf(chest)!.Count(Crop));
        Assert.Equal(1, c.InventoryOf(chest)!.Count(Seed));
        Assert.Null(F.World.FindCreation(soil)!.SoilOf(1)!.Seed);
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    // ---- The vanilla parts ----

    [Fact]
    public void TheVacuumPumpSucksAPileIntoTheChestWhilePoweredAndEjectsItOnceSwitched()
    {
        var c = Deck();
        var chest = Place(c, "vanilla:small_chest", new GridCell(0, 1, 0));
        // The chest only stands on its base, so the pipes rest on blocks.
        Place(c, "vanilla:metal_block", new GridCell(3, 1, 1));
        Place(c, "vanilla:metal_block", new GridCell(4, 1, 1));
        Place(c, "vanilla:vacuum_pipe", new GridCell(3, 2, 1), TransportParts.AlongX);
        Place(c, "vanilla:vacuum_pipe", new GridCell(4, 2, 1), TransportParts.AlongX);
        // Turned along x its 3 blocks of width run from its origin toward -z: z 0 to 2, its back port at z 1.
        var pump = Place(c, "vanilla:vacuum_pump", new GridCell(5, 1, 2), TransportParts.AlongX);
        var power = Place(c, "vanilla:switch", new GridCell(-3, 1, -2));
        Assert.True(c.TryLink(power, pump, out var linkError), linkError);
        Assert.Equal([chest], TransportNetwork.Into(c, pump));

        // Its mouth (the +x end, at 2.125 m) faces a pile half a metre on.
        var drop = F.Survival.AddDrop(new Vector3(2.6f, 0.5f, 0.25f), [new ItemStack("vanilla:metal_block", 12)], placed: true)!;
        Steps(60);
        Assert.True(F.Survival.Drops.ContainsKey(drop.Id)); // unpowered: nothing
        Assert.Equal("off", PoseOf(c, pump));

        Assert.Null(_rig.Runtime.CheckActivate(Host, c, power));
        _rig.Runtime.Activate(Host, c, power);
        Steps(60);
        Assert.Equal("on", PoseOf(c, pump));
        Assert.False(F.Survival.Drops.ContainsKey(drop.Id));
        Assert.Equal(12, c.InventoryOf(chest)!.Count("vanilla:metal_block"));

        Configure(c, pump, "mode", "eject");
        Steps(60); // one a half-second
        var thrown = F.Survival.Drops.Values.Sum(d => d.Items.Sum(i => i.Count));
        Assert.InRange(thrown, 1, 3);
        Assert.Equal(12 - thrown, c.InventoryOf(chest)!.Count("vanilla:metal_block"));
        Assert.All(F.Survival.Drops.Values, d => Assert.InRange(d.Position.X, 2.5f, 3.8f));
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    [Fact]
    public void TheResourceCollectorPullsInALooseRodNearItsTop()
    {
        var c = Deck();
        var collector = Place(c, "vanilla:resource_collector", new GridCell(0, 1, 0));
        // The collector's top is 1.25 m up; its intake reaches 3 m (12 blocks).
        var rod = new Creation(F.World.NextCreationId(), F.Catalog);
        Assert.True(rod.TryPlacePart("vanilla:wood_rod", GridCell.Zero, default, out _, out var error), error);
        rod.SetAllBodyPoses(new Pose(new Vector3(0.4f, 2.5f, 0.4f), Quaternion.Identity));
        F.World.Added(rod);
        var far = new Creation(F.World.NextCreationId(), F.Catalog);
        Assert.True(far.TryPlacePart("vanilla:wood_rod", GridCell.Zero, default, out _, out error), error);
        far.SetAllBodyPoses(new Pose(new Vector3(0.4f, 6f, 0.4f), Quaternion.Identity));
        F.World.Added(far);

        Steps(150);
        Assert.Equal(1, c.InventoryOf(collector)!.Count("vanilla:wood_rod"));
        Assert.Null(F.World.FindCreation(rod.Id));
        Assert.NotNull(F.World.FindCreation(far.Id));
        Assert.DoesNotContain(_rig.Messages, m => m.Kind == ScriptMessageKind.Error);
    }

    // ---- Helpers ----

    // A deck of blocks with a chest (port +Z) at z 0, a pipe at z 1 and a "test:vac" pump running `source` at z 2, its
    // intake facing +Z; the Creation sits half a metre up.
    private (Creation Creation, int Chest, int Pump) PumpLine(string source)
    {
        _rig.Write("scripts/vac.lua", source);
        F.Catalog.Add("test:vac", F.Catalog.FindDefinition(TransportParts.Pump)! with { Script = "scripts/vac.lua" });
        var c = new Creation(F.World.NextCreationId(), F.Catalog);
        for (var z = 0; z < 3; z++)
            Place(c, TransportParts.Block, new GridCell(0, -1, z));
        var chest = Place(c, TransportParts.Chest, new GridCell(0, 0, 0));
        Place(c, TransportParts.Pipe, new GridCell(0, 0, 1));
        var pump = Place(c, "test:vac", new GridCell(0, 0, 2));
        c.SetAllBodyPoses(new Pose(new Vector3(0, 0.5f, 0), Quaternion.Identity));
        F.World.Added(c);
        return (c, chest, pump);
    }

    // A metal deck at y 0 from x -4 to 10 and z -2 to 4, at the origin.
    private Creation Deck()
    {
        var c = new Creation(F.World.NextCreationId(), F.Catalog);
        for (var x = -4; x <= 10; x++)
        {
            for (var z = -2; z <= 4; z++)
                Place(c, "vanilla:metal_block", new GridCell(x, 0, z));
        }
        c.SetAllBodyPoses(Pose.Identity);
        F.World.Added(c);
        return c;
    }

    private static int Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        Assert.True(creation.TryPlacePart(part, cell, rotation, out var placed, out var error), $"{part} at {cell}: {error}");
        return placed!.Id;
    }

    private static void Configure(Creation creation, int part, string id, object value)
    {
        var changes = new Dictionary<string, System.Text.Json.JsonElement?> { [id] = System.Text.Json.JsonSerializer.SerializeToElement(value) };
        creation.SetPartSettings(part, Core.Logic.PartSettings.Apply(creation.DefinitionOf(part), creation.FindPart(part)!.Settings, changes));
    }

    private string? PoseOf(Creation creation, int part) => _rig.Runtime.Readouts.Text(creation.Id, part, "pose");

    private string? State(Creation creation, int part, string key) => _rig.Runtime.Readouts.Text(creation.Id, part, key);

    private void Steps(int count)
    {
        for (var i = 0; i < count; i++)
            _rig.Runtime.Update(1f / 60);
    }

    private void NoErrors() =>
        Assert.True(_rig.Messages.All(m => m.Kind != ScriptMessageKind.Error && m.Kind != ScriptMessageKind.Warning),
            string.Join("; ", _rig.Messages.Select(m => m.Text)));
}
