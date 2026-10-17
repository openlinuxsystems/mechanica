using Mechanica.Core.Logic;

namespace Mechanica.Core.Tests.Logic;

public class PartAnimationsTests
{
    [Fact]
    public void ASpinCarriesOnFromWhereItWasSet()
    {
        var animations = new PartAnimations();

        Assert.True(animations.Set(1, 2, 0.5f, 2f, now: 10));

        Assert.Equal(0.5f, animations.AngleAt(1, 2, 10), 4);
        Assert.Equal(1.5f, animations.AngleAt(1, 2, 10.5), 4);
        Assert.Equal(2f, animations.Speed(1, 2));
        Assert.Equal(0f, animations.AngleAt(1, 3, 10.5));
    }

    [Fact]
    public void AnglesWrapWithinHalfATurn()
    {
        var animations = new PartAnimations();
        animations.Set(1, 1, 3f, 1f, now: 0);

        Assert.InRange(animations.AngleAt(1, 1, 1), -MathF.PI, MathF.PI);
        Assert.Equal(4f - 2 * MathF.PI, animations.AngleAt(1, 1, 1), 4);
    }

    [Fact]
    public void ANullAngleKeepsTheAngleReachedAndOnlyRealChangesCount()
    {
        var animations = new PartAnimations();
        animations.Set(1, 1, 0f, 1f, now: 0);
        var revision = animations.Revision;

        Assert.False(animations.Set(1, 1, null, 1f, now: 2));
        Assert.True(animations.Set(1, 1, null, 0f, now: 2));
        Assert.Equal(2f, animations.AngleAt(1, 1, 5), 4);
        Assert.Equal(revision + 1, animations.Revision);
    }

    [Fact]
    public void AnUnturnedStillPartIsLeftOut()
    {
        var animations = new PartAnimations();
        animations.Set(1, 1, 0.3f, 0f, now: 0);
        animations.Set(1, 1, 0f, 0f, now: 1);
        animations.Set(1, 2, float.NaN, float.PositiveInfinity, now: 1);

        Assert.Empty(animations.All(1));
    }

    [Fact]
    public void AClientLoadsTheHostsAnimationsOnItsOwnClock()
    {
        var host = new PartAnimations();
        host.Set(3, 1, 0f, 4f, now: 100);
        host.Set(3, 2, 0.25f, 0f, now: 100);
        host.Forget(3, 9);
        var client = new PartAnimations();

        client.Load(host.All(now: 100.25), now: 7);

        Assert.Equal([new PartAnimation(3, 1, 1f, 4f), new PartAnimation(3, 2, 0.25f, 0f)], client.All(7));
        Assert.Equal(2f, client.AngleAt(3, 1, 7.25), 4);
        client.Clear();
        Assert.Empty(client.All(7));
    }
}
