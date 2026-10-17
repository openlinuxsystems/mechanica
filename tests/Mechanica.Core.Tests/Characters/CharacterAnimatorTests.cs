using System.Numerics;
using Mechanica.Core.Characters;

namespace Mechanica.Core.Tests.Characters;

public class CharacterAnimatorTests
{
    private const float Dt = 1f / 60f;

    // The contract's clip lengths (tools/characters/build_characters.py).
    private static readonly Dictionary<string, float> Lengths = new()
    {
        ["idle"] = 3f, ["walk"] = 1f, ["jog"] = 0.72f, ["run"] = 0.6f, ["crouch_idle"] = 2f, ["crouch_walk"] = 1.2f,
        ["jump"] = 0.3f, ["fall"] = 1f, ["land"] = 0.35f, ["swim_idle"] = 2f, ["swim"] = 1.2f, ["sit"] = 2f,
        ["hold_tool"] = 2f, ["hold_heavy"] = 2f, ["hold_gun"] = 2f, ["hold_item"] = 2f,
        ["hammer_swing"] = 0.35f, ["sledge_swing"] = 0.45f, ["tool_use"] = 0.3f, ["weld"] = 0.5f, ["paint_spray"] = 0.4f,
        ["fire_gun"] = 0.25f, ["eat"] = 1f, ["throw"] = 0.5f, ["plant"] = 0.8f, ["pour"] = 0.8f, ["interact"] = 0.4f, ["hurt"] = 0.3f,
    };

    private static CharacterAnimator Make() => new(new CharacterSettings(), Lengths);

    private static CharacterPose Standing(float vx = 0, float vz = 0, float vy = 0, float yaw = 0) => new(new Vector3(vx, vy, vz), yaw, 0, OnFloor: true);

    private static List<CharacterEvent> Run(CharacterAnimator animator, CharacterPose pose, float seconds)
    {
        var events = new List<CharacterEvent>();
        for (var t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            animator.Update(pose, Dt);
            events.AddRange(animator.Events);
        }
        return events;
    }

    [Fact]
    public void StandingStillPlaysIdleAlone()
    {
        var animator = Make();
        Run(animator, Standing(), 1f);
        var only = Assert.Single(animator.Body);
        Assert.Equal("idle", only.Clip);
        Assert.Equal(1f, only.Weight, 3);
        Assert.Equal(0f, animator.UpperWeight);
    }

    [Theory]
    [InlineData(1.5f, "walk")]
    [InlineData(4f, "jog")]
    [InlineData(7f, "run")]
    public void EachClipPlaysAloneAtItsOwnSpeed(float speed, string clip)
    {
        var animator = Make();
        Run(animator, Standing(vz: -speed), 1f);
        Assert.Equal(clip, Assert.Single(animator.Body).Clip);
    }

    [Fact]
    public void FootstepsFallOnTheClipBeatAtAnySpeed()
    {
        // Jogging at 4 m/s: a 0.72 s cycle with two steps, so 10 s holds about 27.8 steps.
        var animator = Make();
        var steps = Run(animator, Standing(vz: -4f), 10f).Count(e => e is CharacterEvent.StepLeft or CharacterEvent.StepRight);
        Assert.InRange(steps, 26, 29);
        // Between walk and jog speeds the cadence lies between theirs.
        animator = Make();
        steps = Run(animator, Standing(vz: -2.75f), 10f).Count(e => e is CharacterEvent.StepLeft or CharacterEvent.StepRight);
        Assert.InRange(steps, 20, 28);
    }

    [Fact]
    public void StepsAlternateFeet()
    {
        var steps = Run(Make(), Standing(vz: -4f), 3f).Where(e => e is CharacterEvent.StepLeft or CharacterEvent.StepRight).ToList();
        for (var i = 1; i < steps.Count; i++)
            Assert.NotEqual(steps[i - 1], steps[i]);
    }

    [Fact]
    public void NoFootstepsInTheAirOrStandingStill()
    {
        Assert.DoesNotContain(Run(Make(), Standing(), 2f), e => e is CharacterEvent.StepLeft or CharacterEvent.StepRight);
        var animator = Make();
        Run(animator, Standing(), 0.1f);
        Assert.DoesNotContain(Run(animator, new CharacterPose(new Vector3(0, -2, -4), 0, 0, OnFloor: false), 2f),
            e => e is CharacterEvent.StepLeft or CharacterEvent.StepRight);
    }

    [Fact]
    public void JumpThenLandPlaysBothAndTheirSounds()
    {
        var animator = Make();
        Run(animator, Standing(), 0.2f);
        var events = Run(animator, new CharacterPose(new Vector3(0, 4.5f, 0), 0, 0, OnFloor: false), 0.1f);
        Assert.Contains(CharacterEvent.Jump, events);
        Assert.Equal(CharacterMode.Air, animator.Mode);
        Run(animator, new CharacterPose(new Vector3(0, -4.5f, 0), 0, 0, OnFloor: false), 0.5f);
        Assert.Contains(animator.Body, c => c.Clip == "fall");
        events = Run(animator, Standing(), 0.05f);
        Assert.Contains(CharacterEvent.Land, events);
        Assert.Contains(animator.Body, c => c.Clip == "land");
    }

    [Fact]
    public void ALongFallLandsHard()
    {
        var animator = Make();
        Run(animator, Standing(), 0.2f);
        Run(animator, new CharacterPose(new Vector3(0, -12f, 0), 0, 0, OnFloor: false), 1f);
        Assert.Contains(CharacterEvent.LandHard, Run(animator, Standing(), 0.05f));
    }

    [Fact]
    public void SteppingOffAKerbIsNotAFall()
    {
        var animator = Make();
        Run(animator, Standing(vz: -4f), 0.5f);
        var events = Run(animator, new CharacterPose(new Vector3(0, -1f, -4f), 0, 0, OnFloor: false), 0.1f);
        Assert.Equal(CharacterMode.Ground, animator.Mode);
        events.AddRange(Run(animator, Standing(vz: -4f), 0.1f));
        Assert.DoesNotContain(CharacterEvent.Land, events);
        Assert.DoesNotContain(CharacterEvent.Jump, events);
    }

    [Fact]
    public void SwimmingSwitchesClipsAndSplashes()
    {
        var animator = Make();
        Run(animator, Standing(), 0.2f);
        var events = Run(animator, new CharacterPose(new Vector3(0, 0, -2.5f), 0, 0, OnFloor: false, Swimming: true), 2f);
        Assert.Equal(1, events.Count(e => e == CharacterEvent.WaterEnter));
        Assert.Contains(CharacterEvent.SwimStroke, events);
        Assert.Equal("swim", Assert.Single(animator.Body).Clip);
        Assert.Contains(CharacterEvent.WaterExit, Run(animator, Standing(), 0.1f));
    }

    [Fact]
    public void ModesCrossfadeRatherThanSnap()
    {
        var animator = Make();
        Run(animator, Standing(), 0.5f);
        animator.Update(new CharacterPose(Vector3.Zero, 0, 0, OnFloor: true, Seated: true), Dt);
        Assert.Contains(animator.Body, c => c.Clip == "idle");
        Assert.Contains(animator.Body, c => c.Clip == "sit");
        Assert.Equal(1f, animator.Body.Sum(c => c.Weight), 3);
        Run(animator, new CharacterPose(Vector3.Zero, 0, 0, OnFloor: true, Seated: true), 0.5f);
        Assert.Equal("sit", Assert.Single(animator.Body).Clip);
    }

    [Theory]
    [InlineData(CharacterProp.Hammer, "hold_tool")]
    [InlineData(CharacterProp.Sledgehammer, "hold_heavy")]
    [InlineData(CharacterProp.PotatoBlaster, "hold_gun")]
    [InlineData(CharacterProp.Item, "hold_item")]
    public void HoldingSomethingPosesTheArms(CharacterProp prop, string clip)
    {
        var animator = Make();
        Run(animator, Standing() with { Prop = prop }, 1f);
        Assert.Equal(clip, Assert.Single(animator.Upper).Clip);
        Assert.Equal(1f, animator.UpperWeight, 3);
        Assert.True(animator.ShowProp);
    }

    [Fact]
    public void TakingSomethingInHandSoundsOnceAndFadesIn()
    {
        var animator = Make();
        Run(animator, Standing(), 0.2f);
        var events = Run(animator, Standing() with { Prop = CharacterProp.Hammer }, Dt * 2);
        Assert.Equal(1, events.Count(e => e == CharacterEvent.Equip));
        Assert.InRange(animator.UpperWeight, 0.01f, 0.5f);
    }

    [Fact]
    public void AGestureOverridesTheHoldAndEnds()
    {
        var animator = Make();
        var holding = Standing() with { Prop = CharacterProp.Hammer };
        Run(animator, holding, 0.5f);
        Run(animator, holding with { Gesture = CharacterGesture.HammerSwing, GestureCount = 1 }, 0.15f);
        Assert.Equal("hammer_swing", animator.Gesture);
        Assert.True(animator.Upper.Single(c => c.Clip == "hammer_swing").Weight > 0.9f);
        Run(animator, holding with { Gesture = CharacterGesture.HammerSwing, GestureCount = 1 }, 0.4f);
        Assert.Null(animator.Gesture);
        Assert.Equal("hold_tool", Assert.Single(animator.Upper).Clip);
    }

    [Fact]
    public void TheSameGestureTwicePlaysTwice()
    {
        var animator = Make();
        var pose = Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = 1 };
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer }, 0.2f);
        Run(animator, pose, 0.6f);
        Assert.Null(animator.Gesture);
        Run(animator, pose with { GestureCount = 2 }, Dt);
        Assert.Equal("sledge_swing", animator.Gesture);
    }

    [Fact]
    public void AGestureCanBeTimedToTheTool()
    {
        // The sledge clip is 0.45 s long; the swing takes 0.4 s (UI spec §10), so the clip plays faster.
        var animator = new CharacterAnimator(new CharacterSettings { GestureSeconds = new() { ["sledge_swing"] = 0.4f } }, Lengths);
        var pose = Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = 1 };
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer }, 0.2f);
        Run(animator, pose, 0.2f);
        // Started on the first of the frames, so 0.2 s less one frame in.
        Assert.Equal((0.2f - Dt) * 0.45f / 0.4f, animator.Upper.Single(c => c.Clip == "sledge_swing").Time, 3);
        Run(animator, pose, 0.21f);
        Assert.Null(animator.Gesture);
    }

    [Fact]
    public void HandsAreFreeWhileSwimming()
    {
        var animator = Make();
        Run(animator, new CharacterPose(Vector3.Zero, 0, 0, OnFloor: false, Swimming: true, Prop: CharacterProp.Hammer), 1f);
        Assert.Equal(0f, animator.UpperWeight);
        Assert.False(animator.ShowProp);
    }

    [Fact]
    public void StrafingTurnsTheBodyAndBackingUpPlaysInReverse()
    {
        var animator = Make();
        // Moving right (+X) at yaw 0 turns the body right (negative yaw), up to the limit.
        Run(animator, Standing(vx: 4f), 1f);
        Assert.Equal(-60f * MathF.PI / 180f, animator.BodyTurn, 3);

        animator = Make();
        var forward = Run(animator, Standing(vz: 4f), 3f).Where(e => e is CharacterEvent.StepLeft or CharacterEvent.StepRight).ToList();
        Assert.Equal(0f, animator.BodyTurn, 3);
        Assert.NotEmpty(forward);
    }

    [Fact]
    public void HeadFollowsPartOfTheLookPitch()
    {
        var animator = Make();
        animator.Update(new CharacterPose(Vector3.Zero, 0, 1f, OnFloor: true), Dt);
        Assert.Equal(0.6f, animator.HeadPitch, 3);
    }

    [Fact]
    public void BadInputDoesNotBreakIt()
    {
        var animator = Make();
        animator.Update(new CharacterPose(new Vector3(float.NaN, float.PositiveInfinity, 1), float.NaN, float.NaN, true,
            Prop: (CharacterProp)200, Gesture: (CharacterGesture)200, GestureCount: 3), float.NaN);
        Assert.Equal(1f, animator.Body.Sum(c => c.Weight), 3);
        Assert.Null(animator.Gesture);
    }

    [Fact]
    public void MissingClipsAreSkipped()
    {
        var animator = new CharacterAnimator(new CharacterSettings(), new Dictionary<string, float> { ["idle"] = 2f });
        for (var i = 0; i < 60; i++)
            animator.Update(new CharacterPose(new Vector3(0, 0, -4), 0, 0, true, Prop: CharacterProp.Hammer, Gesture: CharacterGesture.HammerSwing, GestureCount: 1), Dt);
        Assert.Equal("idle", Assert.Single(animator.Body).Clip);
        Assert.Empty(animator.Upper);
    }

    [Fact]
    public void FlinchPlaysWithoutAGestureCount()
    {
        var animator = Make();
        Run(animator, Standing(), 0.1f);
        animator.Play(CharacterGesture.Hurt);
        Run(animator, Standing(), Dt);
        Assert.Equal("hurt", animator.Gesture);
    }
}
