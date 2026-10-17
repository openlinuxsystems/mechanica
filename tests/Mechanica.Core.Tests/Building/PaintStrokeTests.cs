using Mechanica.Core.Building;

namespace Mechanica.Core.Tests.Building;

public class PaintStrokeTests
{
    [Fact]
    public void EachPartIsCoveredOnceAndSentByCreation()
    {
        var stroke = new PaintStroke(10);

        Assert.True(stroke.Add(1, 4));
        Assert.True(stroke.Add(2, 1));
        Assert.False(stroke.Add(1, 4));
        Assert.True(stroke.Add(1, 5));

        var batches = stroke.TakePending();
        Assert.Equal([1, 2], batches.Select(b => b.Creation));
        Assert.Equal([4, 5], batches[0].Parts);
        Assert.Equal([1], batches[1].Parts);
        Assert.Empty(stroke.TakePending());
        Assert.False(stroke.Add(2, 1));
        Assert.Equal(3, stroke.Count);
    }

    [Fact]
    public void AStrokeStopsAtItsLimit()
    {
        var stroke = new PaintStroke(2);
        stroke.Add(1, 1);
        stroke.Add(1, 2);

        Assert.True(stroke.Full);
        Assert.False(stroke.Add(1, 3));
        Assert.Equal([1, 2], Assert.Single(stroke.TakePending()).Parts);
    }
}
