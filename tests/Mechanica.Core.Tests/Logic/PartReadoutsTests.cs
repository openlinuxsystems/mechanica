using Mechanica.Core.Logic;

namespace Mechanica.Core.Tests.Logic;

/// <summary>What parts show in their menus and on themselves (<see cref="PartReadouts"/>).</summary>
public class PartReadoutsTests
{
    [Fact]
    public void ValuesAreSetReadAndClearedWithoutCountingRepeats()
    {
        var readouts = new PartReadouts();
        Assert.Null(readouts.Set(1, 2, "fuel", 0.5f));
        Assert.Null(readouts.Set(1, 2, "mode", "and"));
        var revision = readouts.Revision;
        Assert.Null(readouts.Set(1, 2, "fuel", 0.5f));
        Assert.Equal(revision, readouts.Revision);
        Assert.Equal(0.5f, readouts.Number(1, 2, "fuel"));
        Assert.Equal("and", readouts.Text(1, 2, "mode"));
        Assert.Null(readouts.Number(1, 2, "mode"));
        Assert.True(readouts.Has(1, 2, "fuel"));
        readouts.Clear(1, 2, "fuel");
        Assert.False(readouts.Has(1, 2, "fuel"));
        Assert.True(readouts.Revision > revision);
        Assert.Null(readouts.Set(1, 2, "rpm", float.NaN));
        Assert.Equal(0f, readouts.Number(1, 2, "rpm"));
    }

    [Fact]
    public void BadIdsTooManyValuesAndLongTextAreHeldBack()
    {
        var readouts = new PartReadouts();
        Assert.Contains("snake_case", readouts.Set(1, 1, "Fuel", 1f));
        for (var i = 0; i < PartReadouts.MaxPerPart; i++)
            Assert.Null(readouts.Set(1, 1, $"v{i}", i));
        Assert.Contains($"at most {PartReadouts.MaxPerPart}", readouts.Set(1, 1, "more", 1f));
        // Changing a value it already has is fine.
        Assert.Null(readouts.Set(1, 1, "v0", 5f));
        readouts.Set(1, 2, "text", new string('a', 100));
        Assert.Equal(PartReadouts.MaxText, readouts.Text(1, 2, "text")!.Length);
    }

    [Fact]
    public void PartsAndCreationsThatGoAwayTakeTheirValuesAndLoadRoundTrips()
    {
        var readouts = new PartReadouts();
        readouts.Set(1, 1, "a", 1f);
        readouts.Set(1, 2, "a", 2f);
        readouts.Set(2, 1, "a", "x");
        readouts.Forget(1, 1);
        Assert.False(readouts.Has(1, 1, "a"));
        readouts.Prune(1, part => part != 2);
        Assert.False(readouts.Has(1, 2, "a"));
        Assert.True(readouts.Has(2, 1, "a"));

        var copy = new PartReadouts();
        copy.Load(readouts.All);
        Assert.Equal("x", copy.Text(2, 1, "a"));
        readouts.Clear();
        Assert.Empty(readouts.All);
    }
}
