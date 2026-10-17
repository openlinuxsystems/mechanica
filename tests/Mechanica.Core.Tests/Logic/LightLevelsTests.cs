using Mechanica.Core.Logic;

namespace Mechanica.Core.Tests.Logic;

public class LightLevelsTests
{
    [Fact]
    public void LevelsAreClampedAndOnlyRealChangesCount()
    {
        var lights = new LightLevels();

        Assert.True(lights.Set(1, 2, 0.5f));
        Assert.False(lights.Set(1, 2, 0.5f));
        Assert.True(lights.Set(1, 3, 7f));
        Assert.True(lights.Set(1, 4, float.NaN) is false);

        Assert.Equal(0.5f, lights.Level(1, 2));
        Assert.Equal(1f, lights.Level(1, 3));
        Assert.Equal(0f, lights.Level(1, 4));
        Assert.Equal(2, lights.Revision);
    }

    [Fact]
    public void DarkPartsAreLeftOutAndAllIsOrdered()
    {
        var lights = new LightLevels();
        lights.Set(2, 1, 1f);
        lights.Set(1, 5, 0.25f);
        lights.Set(1, 3, 1f);
        lights.Set(1, 3, 0f);

        Assert.Equal([new LightLevel(1, 5, 0.25f), new LightLevel(2, 1, 1f)], lights.All);
    }

    [Fact]
    public void ForgetAndClearDropLevels()
    {
        var lights = new LightLevels();
        lights.Set(1, 1, 1f);
        lights.Set(1, 2, 1f);
        var revision = lights.Revision;

        lights.Forget(1, 1);
        lights.Forget(1, 9);
        Assert.Equal(revision + 1, lights.Revision);
        lights.Clear();

        Assert.Empty(lights.All);
        Assert.Equal(revision + 2, lights.Revision);
    }

    [Fact]
    public void AClientLoadsTheHostsLevels()
    {
        var host = new LightLevels();
        host.Set(3, 1, 0.75f);
        host.Set(3, 2, 1f);
        var client = new LightLevels();
        client.Set(9, 9, 1f);

        client.Load([.. host.All, new LightLevel(4, 1, 5f), new LightLevel(4, 2, -1f)]);

        Assert.Equal([new LightLevel(3, 1, 0.75f), new LightLevel(3, 2, 1f), new LightLevel(4, 1, 1f)], client.All);
    }
}
