using Mechanica.Core.Characters;

namespace Mechanica.Core.Tests.Characters;

public class CharacterRigTests
{
    // Bone 0 is the chest, bone 1 the right forearm.
    private static bool IsArm(int bone) => bone == 1;

    [Fact]
    public void ArmTrianglesKeepOnlyTrianglesWhollyOnTheArms()
    {
        // Four vertices, two influences each: 0 chest, 1 mostly chest, 2 and 3 mostly forearm.
        int[] bones = [0, 1, 0, 1, 1, 0, 1, 0];
        float[] weights = [1, 0, 0.6f, 0.4f, 0.7f, 0.3f, 1, 0];
        int[] indices = [0, 1, 2, 2, 3, 3, 1, 2, 3];
        Assert.Equal([2, 3, 3], CharacterRig.ArmTriangles(indices, 4, bones, weights, 2, IsArm));
        Assert.Equal([0, 1, 2], CharacterRig.ArmTriangles(indices, 4, bones, weights, 2, b => true)[..3]);
        Assert.Empty(CharacterRig.ArmTriangles(indices, 4, bones, weights, 2, b => false));
    }

    [Fact]
    public void ArmTrianglesReadUnindexedMeshesInOrder()
    {
        int[] bones = [1, 1, 1, 0, 1, 1];
        float[] weights = [1, 1, 1, 1, 1, 1];
        Assert.Equal([0, 1, 2], CharacterRig.ArmTriangles(null, 6, bones, weights, 1, IsArm));
    }

    [Fact]
    public void ArmTrianglesRefuseShortBoneData() =>
        Assert.Throws<ArgumentException>(() => CharacterRig.ArmTriangles(null, 3, [1, 1], [1, 1], 1, IsArm));

    [Fact]
    public void FingerBonesAreOptionalArmAndUpperBodyBones()
    {
        Assert.Equal(26, CharacterRig.FingerBones.Count);
        Assert.Empty(CharacterRig.FingerBones.Intersect(CharacterRig.Bones));
        Assert.All(CharacterRig.FingerBones, b => Assert.Contains(b, CharacterRig.ArmBones));
        Assert.All(CharacterRig.FingerBones, b => Assert.Contains(b, CharacterRig.UpperBodyBones));
        Assert.All(CharacterRig.FingerFallback, f => Assert.Contains(f.Key, CharacterRig.Bones));
    }

    [Fact]
    public void FirstPersonArmsAreForearmsAndHandsOnTheUpperBody()
    {
        Assert.Subset(CharacterRig.UpperBodyBones.ToHashSet(), CharacterRig.ArmBones.ToHashSet());
        Assert.Contains(CharacterRig.PropBone, CharacterRig.ArmBones);
        Assert.DoesNotContain("upper_arm_r", CharacterRig.ArmBones);
    }

    [Fact]
    public void TheRefineToolIsAPropWithItsOwnDrawWorkAndStowClips()
    {
        Assert.Equal(CharacterProp.RefineTool, CharacterRig.Sanitize(CharacterProp.RefineTool));
        Assert.Equal(CharacterProp.None, CharacterRig.Sanitize((CharacterProp)(CharacterProp.Drink + 1)));
        Assert.Equal("refine_tool", CharacterRig.PropNode(CharacterProp.RefineTool));
        Assert.Equal("hold_refine", CharacterRig.HoldClip(CharacterProp.RefineTool));
        Assert.Equal("refine_draw", CharacterRig.DrawClip(CharacterProp.RefineTool));
        Assert.Equal("refine_stow", CharacterRig.StowClip(CharacterProp.RefineTool));
        Assert.Null(CharacterRig.DrawClip(CharacterProp.LiftRemote));
        Assert.Null(CharacterRig.StowClip(CharacterProp.Sledgehammer));
        foreach (var clip in new[] { "hold_refine", "refine_draw", "refine_stow" })
        {
            Assert.Contains(clip, CharacterRig.UpperBodyClips);
            Assert.Contains(clip + CharacterRig.FirstPersonSuffix, CharacterRig.UpperBodyClips);
        }
        Assert.False(CharacterAnimator.IsOneShot("hold_refine"));
        Assert.True(CharacterAnimator.IsOneShot("refine_draw"));
    }

    [Fact]
    public void ThePaintToolAimsWhileSprayingAndTwistsItsTankToReload()
    {
        Assert.Equal("paint_tool", CharacterRig.PropNode(CharacterProp.PaintTool));
        Assert.Equal("hold_paint", CharacterRig.HoldClip(CharacterProp.PaintTool));
        Assert.Equal("hold_paint_aim", CharacterRig.HoldClip(CharacterProp.PaintTool, guard: true));
        Assert.Equal("paint_draw", CharacterRig.DrawClip(CharacterProp.PaintTool));
        Assert.Equal("paint_stow", CharacterRig.StowClip(CharacterProp.PaintTool));
        Assert.Equal("paint_reload", CharacterRig.GestureClip(CharacterGesture.PaintReload));
        Assert.True(CharacterRig.CanGuard(CharacterProp.PaintTool));
        Assert.False(CharacterRig.CanGuard(CharacterProp.WaterBucket));
        foreach (var clip in new[] { "hold_paint", "hold_paint_aim", "paint_draw", "paint_stow", "paint_reload" })
        {
            Assert.Contains(clip, CharacterRig.UpperBodyClips);
            Assert.Contains(clip + CharacterRig.FirstPersonSuffix, CharacterRig.UpperBodyClips);
        }
        Assert.False(CharacterAnimator.IsOneShot("hold_paint_aim"));
        Assert.True(CharacterAnimator.IsOneShot("paint_reload"));
    }

    [Fact]
    public void TheSoilBagIsCarriedInBothHandsLiftedUpAndTippedOut()
    {
        Assert.Equal(CharacterProp.SoilBag, CharacterRig.Sanitize(CharacterProp.SoilBag));
        Assert.Equal(CharacterGesture.BagSpill, CharacterRig.Sanitize(CharacterGesture.BagSpill));
        Assert.Equal(CharacterGesture.None, CharacterRig.Sanitize((CharacterGesture)(CharacterGesture.Drink + 1)));
        Assert.Equal("soil_bag", CharacterRig.PropNode(CharacterProp.SoilBag));
        Assert.Equal("hold_bag", CharacterRig.HoldClip(CharacterProp.SoilBag));
        Assert.Equal("bag_lift", CharacterRig.DrawClip(CharacterProp.SoilBag));
        Assert.Equal("bag_lower", CharacterRig.StowClip(CharacterProp.SoilBag));
        Assert.Equal("bag_spill", CharacterRig.GestureClip(CharacterGesture.BagSpill));
        foreach (var clip in new[] { "hold_bag", "bag_spill", "bag_lift", "bag_lower" })
        {
            Assert.Contains(clip, CharacterRig.UpperBodyClips);
            Assert.Contains(clip + CharacterRig.FirstPersonSuffix, CharacterRig.UpperBodyClips);
        }
    }

    [Fact]
    public void FoodRestsOnThePalmAndIsEatenInBites()
    {
        Assert.Equal(CharacterProp.Food, CharacterRig.Sanitize(CharacterProp.Food));
        Assert.Equal("hold_food", CharacterRig.HoldClip(CharacterProp.Food));
        Assert.Contains("hold_food", CharacterRig.FreeArmHolds);
        Assert.Null(CharacterRig.PropNode(CharacterProp.Food));
        foreach (var clip in new[] { "hold_food", "eat" })
        {
            Assert.Contains(clip, CharacterRig.UpperBodyClips);
            Assert.Contains(clip + CharacterRig.FirstPersonSuffix, CharacterRig.UpperBodyClips);
        }
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.29f, 0)]
    [InlineData(0.3f, 1)]
    [InlineData(0.6f, 2)]
    [InlineData(0.86f, -1)]
    [InlineData(1.4f, -1)]
    public void BitesFollowTheEatClip(float seconds, int bites) => Assert.Equal(bites, new EatSettings().BitesAt(seconds));

    [Fact]
    public void DrinksHoldTheirOwnPropAndPlayTheDrinkClip()
    {
        Assert.Equal("hold_drink", CharacterRig.HoldClip(CharacterProp.Drink));
        Assert.Equal("drink", CharacterRig.GestureClip(CharacterGesture.Drink));
        Assert.Equal(CharacterProp.Drink, CharacterRig.Sanitize(CharacterProp.Drink));
        Assert.Equal(CharacterGesture.Drink, CharacterRig.Sanitize(CharacterGesture.Drink));
        Assert.Equal(CharacterGesture.Install, CharacterRig.Sanitize(CharacterGesture.Install));
        foreach (var clip in new[] { "hold_drink", "hold_drink_fp", "drink", "drink_fp" })
            Assert.Contains(clip, CharacterRig.UpperBodyClips);
    }
}
