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
        Assert.Equal(CharacterProp.None, CharacterRig.Sanitize((CharacterProp)(CharacterProp.SoilBag + 1)));
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
    public void TheSoilBagIsCarriedInBothHandsLiftedUpAndTippedOut()
    {
        Assert.Equal(CharacterProp.SoilBag, CharacterRig.Sanitize(CharacterProp.SoilBag));
        Assert.Equal(CharacterGesture.BagSpill, CharacterRig.Sanitize(CharacterGesture.BagSpill));
        Assert.Equal(CharacterGesture.None, CharacterRig.Sanitize((CharacterGesture)(CharacterGesture.BagSpill + 1)));
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
}
