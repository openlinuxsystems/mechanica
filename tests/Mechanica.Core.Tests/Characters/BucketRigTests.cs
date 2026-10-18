using System.Numerics;
using Mechanica.Core.Characters;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Characters;

/// <summary>The held bucket follows the shipped clips the way the model builder posed them.</summary>
public class BucketRigTests
{
    private static BucketSettings Shipped() =>
        CharacterSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "character.json"))).Bucket;

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    [Fact]
    public void InGripMatchesTheModelBuilder()
    {
        // Values printed by tools/characters/build_characters.py bucket_in_grip(0.5, 30), turned to Y-up.
        var t = BucketRig.InGrip(Shipped(), new BucketState(0.5f, 30f, 1f));
        Near(new Vector3(0f, 0f, 1f), t.X);
        Near(new Vector3(-0.86603f, 0.5f, 0f), t.Y);
        Near(new Vector3(-0.5f, -0.86603f, 0f), t.Z);
        Near(new Vector3(0.33531f, -0.198f, 0f), t.Origin);
    }

    [Fact]
    public void AFullBucketHangsStraightUnderTheFist()
    {
        var settings = Shipped();
        var t = BucketRig.InGrip(settings, BucketState.Full);
        // The bucket's axis runs back up the fingers; its top (hinges plus handle) is the fist.
        var top = t.Origin + t.Y * (settings.HingeHeight + settings.HandleLength);
        Near(Vector3.Zero, top);
        Assert.Equal(0f, BucketRig.HandleDegrees(settings, BucketState.Full), 3);
        Assert.Equal(settings.HandleLoweredDegrees, BucketRig.HandleDegrees(settings, BucketState.Empty), 3);
    }

    [Fact]
    public void MotionEasesBetweenKeysAndFirstPersonSharesIt()
    {
        var settings = Shipped();
        var mid = BucketRig.Motion(settings, "bucket_throw_fp", 0.36f);
        Assert.NotNull(mid);
        Assert.Equal(1f, mid.Value.Grip, 3);
        Assert.Equal(77.5f, mid.Value.Tilt, 2);
        Assert.Equal(0.725f, mid.Value.Water, 3);
        Assert.Equal(BucketRig.Motion(settings, "bucket_throw", 0.36f), mid);
        Assert.Equal(BucketState.Empty, BucketRig.Motion(settings, "bucket_throw", 5f));
        Assert.Equal(BucketState.Full, BucketRig.Motion(settings, "bucket_fill", 5f));
        Assert.Null(BucketRig.Motion(settings, "hammer_swing", 0.1f));
    }

    [Fact]
    public void WaterFillsTheTaperedInside()
    {
        var settings = Shipped();
        var full = BucketRig.WaterHeight(settings, BucketState.Full);
        Assert.Equal(settings.WaterHeight, full, 4);
        Assert.Equal(settings.FloorHeight, BucketRig.WaterHeight(settings, BucketState.Empty), 4);
        Assert.Equal(0.13081f, BucketRig.InnerRadius(settings, full), 4);
    }

    [Theory]
    [InlineData("{ \"bucket\": { \"handle_lowered_degrees\": 200 } }")]
    [InlineData("{ \"bucket\": { \"rim_grip\": [0, 1] } }")]
    [InlineData("{ \"bucket\": { \"water_height\": 0.5, \"rim_height\": 0.3 } }")]
    [InlineData("{ \"bucket\": { \"motion\": { \"bucket_fill\": [[0, 0, 0, 0], [0, 1, 0, 1]] } } }")]
    [InlineData("{ \"bucket\": { \"motion\": { \"bucket_fill\": [[0, 2, 0, 0]] } } }")]
    public void BadBucketSettingsAreRejected(string json) =>
        Assert.Throws<FormatException>(() => CharacterSettings.FromJson(json));
}
