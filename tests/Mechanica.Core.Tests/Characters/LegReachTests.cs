using System.Numerics;
using Mechanica.Core.Characters;

namespace Mechanica.Core.Tests.Characters;

public class LegReachTests
{
    private static readonly Vector3 Hip = new(0f, 0.9f, 0f);
    private static readonly Vector3 Knee = new(0f, 0.48f, 0.04f);
    private static readonly Vector3 Ankle = new(0f, 0.08f, 0f);
    private static readonly Vector3 Ahead = new(0f, 0f, 1f);

    private static (Vector3 Knee, Vector3 Ankle) Apply((Quaternion Root, Quaternion Mid) turn)
    {
        var ankle = Knee + Vector3.Transform(Ankle - Knee, turn.Mid);
        return (Hip + Vector3.Transform(Knee - Hip, turn.Root), Hip + Vector3.Transform(ankle - Hip, turn.Root));
    }

    [Theory]
    [InlineData(0f, 0.2f, 0f)]       // the ground higher under the foot: the knee bends more
    [InlineData(0f, 0.12f, 0.15f)]   // up and ahead
    [InlineData(0.05f, 0.12f, -0.1f)]
    public void TheAnkleReachesTheTargetAndTheBonesKeepTheirLength(float x, float y, float z)
    {
        var target = new Vector3(x, y, z);
        var (knee, ankle) = Apply(LegReach.Solve(Hip, Knee, Ankle, target, Ahead));
        Assert.True(Vector3.Distance(ankle, target) < 1e-3f, $"ankle {ankle} target {target}");
        Assert.Equal((Knee - Hip).Length(), (knee - Hip).Length(), 4);
        Assert.Equal((Ankle - Knee).Length(), (ankle - knee).Length(), 4);
        // The knee still bends forward.
        Assert.True(knee.Z > MathF.Min(target.Z, 0f) - 1e-3f);
    }

    [Fact]
    public void AReachTooFarGoesAsFarAsTheLegDoesAndAStraightLegBendsTowardThePole()
    {
        var far = new Vector3(0f, -0.5f, 0f);
        var (_, ankle) = Apply(LegReach.Solve(Hip, Knee, Ankle, far, Ahead));
        var length = (Knee - Hip).Length() + (Ankle - Knee).Length();
        Assert.True(Vector3.Distance(Hip, ankle) > length * 0.99f);
        Assert.True(ankle.Y < Ankle.Y);

        // A leg lying dead straight still bends, toward the pole.
        var straightKnee = new Vector3(0f, 0.5f, 0f);
        var straightAnkle = new Vector3(0f, 0.1f, 0f);
        var target = new Vector3(0f, 0.3f, 0f);
        var turn = LegReach.Solve(Hip, straightKnee, straightAnkle, target, Ahead);
        var knee = Hip + Vector3.Transform(straightKnee - Hip, turn.Root);
        Assert.True(knee.Z > 0.05f, $"knee {knee}");
    }

    [Fact]
    public void BetweenTurnsOneDirectionOntoAnother()
    {
        var turn = LegReach.Between(Vector3.UnitX, Vector3.UnitY);
        Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitX, turn), Vector3.UnitY) < 1e-5f);
        var back = LegReach.Between(Vector3.UnitX, -Vector3.UnitX);
        Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitX, back), -Vector3.UnitX) < 1e-5f);
        Assert.Equal(Quaternion.Identity, LegReach.Between(Vector3.Zero, Vector3.UnitY));
    }

    [Fact]
    public void FeetFollowTheGroundEasedAndTheHipsDropForTheLowerOne()
    {
        var planting = new FootPlanting();
        // On a slope: the ground is 0.1 m up under the left foot and 0.12 m down under the right.
        for (var i = 0; i < 60; i++)
            planting.Update(0.1f, -0.12f, planted: true, 1f / 60f);
        Assert.Equal(0.1f, planting.Left, 2);
        Assert.Equal(-0.12f, planting.Right, 2);
        Assert.Equal(-0.12f, planting.Hips, 2);
        // One frame on, a step does not pop the foot all the way.
        planting.Update(0.3f, -0.12f, planted: true, 1f / 60f);
        Assert.InRange(planting.Left, 0.11f, 0.29f);
        // Far beyond reach, it stops at the most it goes; nothing under a foot counts as level.
        for (var i = 0; i < 60; i++)
            planting.Update(2f, null, planted: true, 1f / 60f);
        Assert.Equal(planting.MaxOffset, planting.Left, 2);
        Assert.Equal(0f, planting.Right, 2);
        // In the air it lets go.
        for (var i = 0; i < 60; i++)
            planting.Update(0.2f, 0.2f, planted: false, 1f / 60f);
        Assert.True(planting.Weight < 0.05f);
        Assert.True(MathF.Abs(planting.Left) < 0.02f);
        // A bad reading is ignored.
        planting.Update(float.NaN, float.PositiveInfinity, planted: true, 1f / 60f);
        Assert.True(float.IsFinite(planting.Left) && float.IsFinite(planting.Hips));
    }
}
