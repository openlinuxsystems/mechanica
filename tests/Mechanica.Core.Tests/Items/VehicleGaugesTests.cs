using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Settings;
using Mechanica.Core.Ui;
using static Mechanica.Core.Tests.Items.ItemParts;

namespace Mechanica.Core.Tests.Items;

public class VehicleGaugesTests
{
    private const string Charge = "test:charge";
    private const string GasTank = "test:gas_tank";
    private const string Battery = "test:battery";
    private static readonly ActionCaller Player = new(2, ActionSource.Player);

    private readonly PartCatalog _catalog = Catalog()
        .Add(Charge, new PartDefinition { Id = "charge", Name = "Charge", Category = PartCategory.Resource, StackSize = 10 })
        .Add(GasTank, Container("gas_tank", "Gas Tank", new InventoryDefinition { Slots = 2, Accepts = ["test:fuel"], Gauge = GaugeKind.Gas }))
        .Add(Battery, Container("battery", "Battery", new InventoryDefinition { Slots = 3, Accepts = ["test:charge"], Gauge = GaugeKind.Electric }));

    private readonly World _world = new();

    [Fact]
    public void RowsStackByKindThenPlacementWithHeldAndCapacity()
    {
        // battery, tank, chest (no gauge), tank, so the second tank has the highest id
        var creation = Build(Battery, GasTank, Chest, GasTank);
        creation.InventoryOf(1)!.Add(Charge, 12);
        creation.InventoryOf(2)!.Add(Fuel, 25);
        creation.InventoryOf(4)!.Add(Fuel, 3);
        creation.SetPartName(4, "Main tank");

        Assert.Equal(
            [
                new GaugeRow(GaugeKind.Gas, 2, 1, 25, 40),
                new GaugeRow(GaugeKind.Gas, 4, 2, 3, 40, "Main tank"),
                new GaugeRow(GaugeKind.Electric, 1, 1, 12, 30),
            ],
            VehicleGauges.Read(creation));
    }

    [Fact]
    public void NumberOfMatchesTheRowNumbers()
    {
        var creation = Build(Battery, GasTank, Chest, GasTank);

        Assert.Equal((GaugeKind.Gas, 2), VehicleGauges.NumberOf(creation, 4));
        Assert.Equal((GaugeKind.Electric, 1), VehicleGauges.NumberOf(creation, 1));
        Assert.Null(VehicleGauges.NumberOf(creation, 3));
        Assert.Null(VehicleGauges.NumberOf(creation, 9));
    }

    [Fact]
    public void PerKindSumsEachKindWithoutNames()
    {
        var creation = Build(GasTank, Battery, GasTank);
        creation.InventoryOf(1)!.Add(Fuel, 40);
        creation.InventoryOf(3)!.Add(Fuel, 10);

        Assert.Equal(
            [new GaugeRow(GaugeKind.Gas, 0, 0, 50, 80), new GaugeRow(GaugeKind.Electric, 0, 0, 0, 30)],
            VehicleGauges.PerKind(VehicleGauges.Read(creation)));
    }

    [Fact]
    public void ACreationWithoutGaugeContainersHasNoRows() =>
        Assert.Empty(VehicleGauges.Read(Build(Chest, Tank)));

    [Theory]
    [InlineData("  Main tank  ", "Main tank")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("A\tB", "AB")]
    [InlineData("Twenty one characters", "Twenty one character")]
    public void NamesAreCleaned(string? name, string? cleaned) => Assert.Equal(cleaned, VehicleGauges.CleanName(name));

    [Fact]
    public void NameContainerNamesAGaugeContainerInReachAndBlankResetsIt()
    {
        var (dispatcher, creation) = Rig(GasTank);

        Assert.True(Dispatch(dispatcher, new NameContainerArgs(creation.Id, 1, " Main tank ")).Ok);
        Assert.Equal("Main tank", creation.FindPart(1)!.Name);
        Assert.Equal(1, _world.Changes);

        Assert.True(Dispatch(dispatcher, new NameContainerArgs(creation.Id, 1, "")).Ok);
        Assert.Null(creation.FindPart(1)!.Name);
    }

    [Fact]
    public void NameContainerRefusesLongNamesPartsWithoutGaugesAndContainersOutOfReach()
    {
        var (dispatcher, creation) = Rig(GasTank, Chest);

        Assert.Contains("at most 20", Dispatch(dispatcher, new NameContainerArgs(creation.Id, 1, new string('x', 21))).Message);
        Assert.Contains("no gauge", Dispatch(dispatcher, new NameContainerArgs(creation.Id, 2, "Box")).Message);
        _world.Eyes[2] = new Vector3(0, 1.6f, 40);
        Assert.False(Dispatch(dispatcher, new NameContainerArgs(creation.Id, 1, "Far")).Ok);
        Assert.Null(creation.FindPart(1)!.Name);
    }

    [Fact]
    public void NamesSaveWithTheCreation()
    {
        var creation = Build(GasTank);
        creation.SetPartName(1, "Main tank");

        var json = CreationJson.Write(creation);
        var read = CreationJson.Read(json, _catalog, "test.json", []);

        Assert.Contains("\"name\": \"Main tank\"", json);
        Assert.Equal("Main tank", read!.FindPart(1)!.Name);
    }

    [Fact]
    public void ASavedNameThatIsTooLongDoesNotLoad()
    {
        var creation = Build(GasTank);
        creation.SetPartName(1, "Main tank");
        var diagnostics = new List<DataDiagnostic>();

        Assert.Null(CreationJson.Read(CreationJson.Write(creation).Replace("Main tank", new string('x', 30)), _catalog, "test.json", diagnostics));
        Assert.Contains(diagnostics, d => d.JsonPath?.EndsWith(".name", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void TheSettingDefaultsToOneRowPerContainerAndRoundTrips()
    {
        Assert.Equal(VehicleGaugeMode.PerContainer, new UserSettings().Interface.VehicleGauges);
        var settings = new UserSettings { Interface = new InterfaceSettings { VehicleGauges = VehicleGaugeMode.PerType } };

        var json = settings.ToJson();

        Assert.Contains("\"vehicle_gauges\": \"per_type\"", json);
        Assert.Equal(VehicleGaugeMode.PerType, UserSettings.Parse(json)!.Interface.VehicleGauges);
    }

    [Fact]
    public void AGaugeUnderTheLowShareIsLow()
    {
        var hud = HudSettings.Parse("""{ "gauge_low": 0.1 }""");

        Assert.True(hud.IsLow(new GaugeRow(GaugeKind.Gas, 1, 1, 9, 100)));
        Assert.False(hud.IsLow(new GaugeRow(GaugeKind.Gas, 1, 1, 10, 100)));
        Assert.False(hud.IsLow(new GaugeRow(GaugeKind.Gas, 1, 1, 0, 0)));
        Assert.Throws<FormatException>(() => HudSettings.Parse("""{ "gauge_low": 2 }"""));
        Assert.Throws<FormatException>(() => HudSettings.Parse("""{ "gauge_pulse_hz": 0 }"""));
    }

    private (ActionDispatcher Dispatcher, Creation Creation) Rig(params string[] parts)
    {
        var items = new InventoryActions(_catalog, RecipeBook.Empty, new InventorySettings { Reach = 5, PlayerSlots = 4 }, _world);
        var dispatcher = new ActionDispatcher();
        items.Register(dispatcher);
        _world.Eyes[2] = new Vector3(0, 1.6f, 3);
        return (dispatcher, Build(parts));
    }

    private static ActionResult Dispatch(ActionDispatcher dispatcher, NameContainerArgs args) =>
        dispatcher.Dispatch(Player, InventoryActions.NameContainer, JsonSerializer.Serialize(args, ActionJson.Options));

    private Creation Build(params string[] parts)
    {
        var creation = new Creation(_world.Creations.Count + 1, _catalog);
        for (var x = 0; x < parts.Length; x++)
            Assert.True(creation.TryPlacePart(parts[x], new GridCell(x, 0, 0), default, out _, out var error), error);
        creation.SetAllBodyPoses(Pose.Identity);
        _world.Creations[creation.Id] = creation;
        return creation;
    }

    private static PartDefinition Container(string id, string name, InventoryDefinition inventory) => new()
    {
        Id = id,
        Name = name,
        Category = PartCategory.Container,
        Mass = 5,
        Durability = 3,
        Flammable = false,
        Size = GridSize.One,
        StackSize = 10,
        Inventory = inventory,
    };

    private sealed class World : IInventoryWorld
    {
        public Dictionary<int, Creation> Creations { get; } = [];

        public Dictionary<int, Vector3> Eyes { get; } = [];

        public int Changes { get; private set; }

        public IEnumerable<Creation> AllCreations => Creations.Values;

        public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

        public Vector3? EyeOf(int peer) => Eyes.TryGetValue(peer, out var eye) ? eye : null;

        public void Changed(Creation creation) => Changes++;
    }
}
