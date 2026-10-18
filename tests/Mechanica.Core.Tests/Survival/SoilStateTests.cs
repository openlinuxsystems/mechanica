using Mechanica.Core.Data;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Tests.Survival;

/// <summary>How wet soil looks (Soil Design Spec): dry, watered on the way up, wet, and drying on the way down.</summary>
public sealed class SoilStateTests
{
    [Fact]
    public void SoilLooksWateredOnTheWayUpAndDryingOnTheWayDown()
    {
        var soil = new SoilState();
        Assert.Equal(SoilWetness.Dry, soil.Wetness(30));

        soil.AddWater(20, 60, wetAt: 30);
        Assert.Equal((SoilWetness.Watered, false), (soil.Wetness(30), soil.Soaked));
        soil.AddWater(20, 60, wetAt: 30);
        Assert.Equal((SoilWetness.Wet, true), (soil.Wetness(30), soil.Soaked));

        var revision = soil.Revision;
        soil.Set(water: 35, wetAt: 30);
        Assert.Equal(revision, soil.Revision); // still wet: nothing new to show
        soil.Set(water: 25, wetAt: 30);
        Assert.Equal(SoilWetness.Drying, soil.Wetness(30));
        Assert.NotEqual(revision, soil.Revision);

        soil.Set(water: 0, wetAt: 30);
        Assert.Equal((SoilWetness.Dry, false), (soil.Wetness(30), soil.Soaked));
        soil.AddWater(5, 60, wetAt: 30);
        Assert.Equal(SoilWetness.Watered, soil.Wetness(30)); // a fresh start, not drying
    }

    [Fact]
    public void TheWetLevelIsAShareOfTheCapacity()
    {
        Assert.Equal(150f, SoilState.WetAt(new SoilDefinition { Capacity = 300 }));
        Assert.Equal(240f, SoilState.WetAt(new SoilDefinition { Capacity = 300, WetLevel = 0.8f }));
        Assert.True(SoilState.WetAt(null) > 0);
    }

    [Fact]
    public void LoadingKeepsSoakedOnlyWithWater()
    {
        var soil = new SoilState();
        soil.Load(null, 0, 10, soaked: true);
        Assert.Equal(SoilWetness.Drying, soil.Wetness(30));
        soil.Load(null, 0, 0, soaked: true);
        Assert.False(soil.Soaked);
    }
}
