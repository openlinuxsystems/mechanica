using Mechanica.Core.Data;
using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class ItemStatsTests
{
    private static readonly StatPipScale Scale = new();

    [Fact]
    public void RowsShowPipsAndSiValues()
    {
        var engine = new PartDefinition { Mass = 12, Durability = 6, Friction = 0.5f, Buoyancy = 0.2f };
        var rows = ItemStats.Rows(engine, Scale);
        Assert.Equal(["Mass", "Durability", "Friction", "Buoyancy"], rows.Select(r => r.Name));
        Assert.Equal(["12 kg", "6", "0.5", "0.2"], rows.Select(r => r.Value));
        Assert.Equal([4, 2, 5, 2], rows.Select(r => r.Pips));
    }

    [Fact]
    public void MassPipsAreLogarithmic()
    {
        int Pips(float kg) => ItemStats.Rows(new PartDefinition { Mass = kg }, Scale)[0].Pips;
        Assert.Equal(1, Pips(0.05f));
        Assert.Equal(10, Pips(900));
        Assert.Equal(10, Pips(5000));
        Assert.True(Pips(4) < Pips(40) && Pips(40) < Pips(400));
    }

    [Fact]
    public void ZeroShowsNoPipsAndAnythingElseAtLeastOne()
    {
        var rows = ItemStats.Rows(new PartDefinition { Buoyancy = 0, Friction = 0.01f }, Scale);
        Assert.Equal(1, rows.Single(r => r.Name == "Friction").Pips);
        Assert.Equal(0, rows.Single(r => r.Name == "Buoyancy").Pips);
    }

    [Fact]
    public void ItemsWithoutStatsHaveNoRows() =>
        Assert.Empty(ItemStats.Rows(new PartDefinition { Category = PartCategory.Resource }, Scale));

    [Fact]
    public void SizeIsInMetres() =>
        Assert.Equal("0.5×0.5×0.75 m", ItemStats.Size(new PartDefinition { Size = new GridSize(2, 2, 3) }));

    [Fact]
    public void HudSettingsReadTheScale()
    {
        var settings = HudSettings.Parse("""{ "stat_pips": { "mass_kg": 100, "durability": 10, "friction": 2, "buoyancy": 1 } }""");
        Assert.Equal(100, settings.StatPips.MassKg);
        Assert.Throws<FormatException>(() => HudSettings.Parse("""{ "stat_pips": { "mass_kg": 0, "durability": 10, "friction": 2, "buoyancy": 1 } }"""));
    }
}
