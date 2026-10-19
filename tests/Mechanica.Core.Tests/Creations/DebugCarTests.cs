using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Creations;

/// <summary>The debug scrap car builds from the vanilla parts as one wired, fuelled Creation, and its settings parse.</summary>
public class DebugCarTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    [Fact]
    public void ItBuildsFromVanillaScrapParts()
    {
        var car = new Creation(1, Vanilla.Value);
        var parts = DebugCar.Build(car, new DebugCarSettings { Gasoline = 30, Gear = 3 });

        Assert.Equal("vanilla:scrap_drivers_seat", car.FindPart(parts.Seat)!.Part);
        Assert.Equal("vanilla:scrap_gas_engine", car.FindPart(parts.Engine)!.Part);
        Assert.Equal(4, car.Parts.Count(p => p.Part == "vanilla:scrap_wheel"));
        Assert.Equal(5, car.Parts.Count(p => p.Part == "vanilla:bearing"));
        Assert.Equal([parts.Engine, parts.Steering], car.LinksFrom(parts.Seat).Order());
        Assert.Equal(parts.Driven.Order(), car.LinksFrom(parts.Engine));
        Assert.Equal([parts.Engine], car.LinksFrom(parts.Container));
        Assert.Empty(parts.Free.SelectMany(car.LinksTo));
        Assert.Equal(3, car.FindPart(parts.Engine)!.Settings!["gear"].GetInt32());
        Assert.True(car.FindPart(parts.Engine)!.Settings!["on"].GetBoolean());
        Assert.Equal(30, car.InventoryOf(parts.Container)!.Count("vanilla:gasoline"));
        // The chassis, steering axle and four wheels: each bearing adds one moving body.
        Assert.Equal(5, car.Joints.Count);
    }

    [Fact]
    public void SettingsReadFromJson()
    {
        var settings = DebugCarSettings.Parse("""{ "enabled": false, "offset": [1, 0, -2], "yaw": 90, "gasoline": 5, "gear": 2, "engine_on": false, }""");
        Assert.False(settings.Enabled);
        Assert.Equal([1f, 0f, -2f], settings.Offset);
        Assert.Equal((90f, 5, 2, false), (settings.Yaw, settings.Gasoline, settings.Gear, settings.EngineOn));
        Assert.Throws<FormatException>(() => DebugCarSettings.Parse("""{ "offset": [1, 2] }"""));
        Assert.Throws<FormatException>(() => DebugCarSettings.Parse("""{ "gasoline": -1 }"""));
    }
}
