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

    [Fact]
    public void AFeedSendsOnlyWhatChangedAndOnlyForCreationsInView()
    {
        var host = new PartReadouts();
        var client = new PartReadouts();
        var feed = new ReadoutFeed();
        HashSet<int> view = [1];
        host.Set(1, 1, "fuel", 0.5f);
        host.Set(1, 2, "rpm", 900f);
        host.Set(2, 1, "fuel", 1f);

        // The first send carries the Creation in view, whole; the one out of view stays at the host.
        var first = feed.Next(host, view)!;
        Assert.Equal(2, first.Values.Count);
        client.Apply(first);
        Assert.Equal(0.5f, client.Number(1, 1, "fuel"));
        Assert.False(client.Has(2, 1, "fuel"));
        Assert.Null(feed.Next(host, view));
        // Repeating a value changes nothing; another Creation's values cost the client nothing.
        host.Set(1, 1, "fuel", 0.5f);
        host.Set(2, 1, "fuel", 0.2f);
        Assert.Null(feed.Next(host, view));

        // One part changes: only that part goes.
        host.Set(1, 2, "rpm", 1200f);
        var change = feed.Next(host, view)!;
        Assert.Equal([new ReadoutPart(1, 2)], change.Parts);
        Assert.Equal([new PartReadout(1, 2, "rpm", 1200f)], change.Values);
        client.Apply(change);
        Assert.Equal(1200f, client.Number(1, 2, "rpm"));
        Assert.Equal(0.5f, client.Number(1, 1, "fuel"));

        // A value and a part going away reach the client too.
        host.Clear(1, 2, "rpm");
        client.Apply(feed.Next(host, view)!);
        Assert.False(client.Has(1, 2, "rpm"));
        host.Forget(1, 1);
        client.Apply(feed.Next(host, view)!);
        Assert.False(client.Has(1, 1, "fuel"));
        Assert.Null(feed.Next(host, view));

        // Coming into view sends a Creation whole; leaving it drops it from the client.
        host.Set(1, 3, "on", 1f);
        view = [1, 2];
        client.Apply(feed.Next(host, view)!);
        Assert.Equal(0.2f, client.Number(2, 1, "fuel"));
        Assert.Equal(1f, client.Number(1, 3, "on"));
        view = [1];
        var left = feed.Next(host, view)!;
        Assert.Equal([2], left.Creations);
        client.Apply(left);
        Assert.False(client.Has(2, 1, "fuel"));
        Assert.Equal(1f, client.Number(1, 3, "on"));

        // A pruned Creation empties out on the client.
        host.Prune(1, _ => false);
        Assert.Equal(0, host.StampOf(1));
        client.Apply(feed.Next(host, view)!);
        Assert.Empty(client.All);
        Assert.Null(feed.Next(host, view));
    }

    [Fact]
    public void StampsMarkTheCreationAndPartThatChanged()
    {
        var readouts = new PartReadouts();
        Assert.Equal(0, readouts.StampOf(1));
        readouts.Set(1, 1, "a", 1f);
        readouts.Set(1, 2, "a", 1f);
        var before = readouts.PartsOf(1).ToDictionary(p => p.Part, p => p.Stamp);
        readouts.Set(1, 2, "a", 2f);
        var after = readouts.PartsOf(1).ToDictionary(p => p.Part, p => p.Stamp);
        Assert.Equal(before[1], after[1]);
        Assert.True(after[2] > before[2]);
        Assert.Equal(after[2], readouts.StampOf(1));
    }
}
