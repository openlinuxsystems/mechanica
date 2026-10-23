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
        ["hold_tool"] = 2f, ["hold_heavy"] = 2f, ["hold_heavy_block"] = 2f, ["hold_gun"] = 2f, ["hold_item"] = 2f,
        ["hold_heavy_fp"] = 2f, ["hold_heavy_block_fp"] = 2f, ["sledge_swing_fp"] = 1f,
        ["sledge_swing_side"] = 1f, ["sledge_swing_back"] = 1f, ["sledge_swing_side_fp"] = 1f, ["sledge_swing_back_fp"] = 1f,
        ["sledge_swing"] = 1f, ["tool_use"] = 0.3f, ["weld"] = 0.6f, ["paint_spray"] = 0.4f,
        ["fire_gun"] = 0.25f, ["eat"] = 1f, ["throw"] = 0.5f, ["plant"] = 0.8f, ["pour"] = 0.8f, ["interact"] = 0.4f, ["hurt"] = 0.3f,
        ["hold_bucket"] = 2f, ["hold_bucket_full"] = 2f, ["bucket_fill"] = 1.1f, ["bucket_throw"] = 0.9f, ["bucket_place"] = 0.8f, ["bucket_place_full"] = 0.8f,
        ["hold_refine"] = 0.75f, ["refine_draw"] = 0.4f, ["refine_stow"] = 0.35f,
        ["hold_refine_fp"] = 0.75f, ["refine_draw_fp"] = 0.4f, ["refine_stow_fp"] = 0.35f,
        ["hold_bag"] = 2f, ["bag_spill"] = 0.8f, ["bag_lift"] = 0.4f, ["bag_lower"] = 0.35f,
        ["hold_weld"] = 2f, ["hold_weld_aim"] = 2f, ["weld_draw"] = 0.5f, ["weld_stow"] = 0.45f,
        ["hold_weld_fp"] = 2f, ["hold_weld_aim_fp"] = 2f, ["weld_draw_fp"] = 0.5f, ["weld_stow_fp"] = 0.45f, ["weld_fp"] = 0.6f,
        ["turn_l"] = 0.7f, ["turn_r"] = 0.7f, ["jump_run"] = 0.5f, ["land_hard"] = 1f, ["idle_look"] = 4f, ["idle_stretch"] = 3.6f,
        ["hit_front"] = 0.5f, ["hit_back"] = 0.5f, ["hit_left"] = 0.5f, ["hit_right"] = 0.5f, ["hit_heavy"] = 1f,
        ["knockdown"] = 1.1f, ["get_up"] = 1.3f, ["death"] = 1.6f, ["downed"] = 2f,
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
    [InlineData(CharacterProp.LiftRemote, "hold_tool")]
    [InlineData(CharacterProp.Sledgehammer, "hold_heavy")]
    [InlineData(CharacterProp.PotatoBlaster, "hold_gun")]
    [InlineData(CharacterProp.PotatoScattergun, "hold_gun")]
    [InlineData(CharacterProp.PotatoLauncher, "hold_gun")]
    [InlineData(CharacterProp.Item, "hold_item")]
    [InlineData(CharacterProp.WaterBucket, "hold_bucket")]
    [InlineData(CharacterProp.WaterBucketFull, "hold_bucket_full")]
    [InlineData(CharacterProp.RefineTool, "hold_refine")]
    [InlineData(CharacterProp.SoilBag, "hold_bag")]
    [InlineData(CharacterProp.WeldTool, "hold_weld")]
    public void HoldingSomethingPosesTheArms(CharacterProp prop, string clip)
    {
        var animator = Make();
        Run(animator, Standing() with { Prop = prop }, 1f);
        Assert.Equal(clip, Assert.Single(animator.Upper).Clip);
        Assert.Equal(1f, animator.UpperWeight, 3);
        Assert.True(animator.ShowProp);
    }

    [Fact]
    public void FillingTheBucketIsNotTakingSomethingInHand()
    {
        var animator = Make();
        Run(animator, Standing() with { Prop = CharacterProp.WaterBucket }, 0.5f);
        var events = Run(animator, Standing() with { Prop = CharacterProp.WaterBucketFull }, 0.5f);
        Assert.DoesNotContain(CharacterEvent.Equip, events);
    }

    [Fact]
    public void AGestureReportsHowFarInAndHowMuchItShows()
    {
        var animator = Make();
        var pose = Standing() with { Prop = CharacterProp.WaterBucketFull };
        Run(animator, pose, 0.5f);
        Assert.Equal(0f, animator.GestureWeight);
        Run(animator, pose with { Gesture = CharacterGesture.BucketThrow, GestureCount = 1 }, 0.3f);
        Assert.Equal("bucket_throw", animator.Gesture);
        Assert.InRange(animator.GestureTime, 0.25f, 0.31f);
        Assert.Equal(1f, animator.GestureWeight, 3);
    }

    [Fact]
    public void TakingSomethingInHandSoundsOnceAndFadesIn()
    {
        var animator = Make();
        Run(animator, Standing(), 0.2f);
        var events = Run(animator, Standing() with { Prop = CharacterProp.LiftRemote }, Dt * 2);
        Assert.Equal(1, events.Count(e => e == CharacterEvent.Equip));
        Assert.InRange(animator.UpperWeight, 0.01f, 0.5f);
    }

    [Theory]
    [InlineData(false, "refine_draw", "refine_stow")]
    [InlineData(true, "refine_draw_fp", "refine_stow_fp")]
    public void TheRefineToolIsDrawnFromTheHipAndPutBack(bool firstPerson, string draw, string stow)
    {
        var animator = new CharacterAnimator(new CharacterSettings(), Lengths, firstPerson);
        var remote = Standing() with { Prop = CharacterProp.LiftRemote };
        Run(animator, remote, 0.5f);
        Run(animator, Standing() with { Prop = CharacterProp.RefineTool }, Dt);
        Assert.Equal(draw, animator.Gesture);
        Assert.Equal(CharacterProp.RefineTool, animator.Prop);
        Run(animator, Standing() with { Prop = CharacterProp.RefineTool }, 0.6f);
        Assert.Null(animator.Gesture);
        Assert.Contains(animator.Upper, c => c.Clip == (firstPerson ? "hold_refine_fp" : "hold_refine") && c.Weight > 0.99f);

        // Letting go puts it away: still drawn until the stow ends, then the remote is back.
        Run(animator, remote, Dt);
        Assert.Equal(stow, animator.Gesture);
        Assert.Equal(CharacterProp.RefineTool, animator.Prop);
        Assert.True(animator.ShowProp);
        Run(animator, remote, 0.4f);
        Assert.Null(animator.Gesture);
        Assert.Equal(CharacterProp.LiftRemote, animator.Prop);
    }

    [Fact]
    public void AGestureCutsTheStowShortAndTheToolGoes()
    {
        var animator = Make();
        Run(animator, Standing() with { Prop = CharacterProp.RefineTool }, 1f);
        Run(animator, Standing(), Dt);
        Assert.Equal(CharacterProp.RefineTool, animator.Prop);
        animator.Update(Standing() with { Gesture = CharacterGesture.Interact, GestureCount = 1 }, Dt);
        Assert.Equal("interact", animator.Gesture);
        Assert.Equal(CharacterProp.None, animator.Prop);
        Assert.False(animator.ShowProp);
    }

    [Fact]
    public void AGestureOverridesTheHoldAndEnds()
    {
        var animator = Make();
        var holding = Standing() with { Prop = CharacterProp.LiftRemote };
        Run(animator, holding, 0.5f);
        Run(animator, holding with { Gesture = CharacterGesture.ToolUse, GestureCount = 1 }, 0.15f);
        Assert.Equal("tool_use", animator.Gesture);
        Assert.True(animator.Upper.Single(c => c.Clip == "tool_use").Weight > 0.9f);
        Run(animator, holding with { Gesture = CharacterGesture.ToolUse, GestureCount = 1 }, 0.4f);
        Assert.Null(animator.Gesture);
        Assert.Equal("hold_tool", Assert.Single(animator.Upper).Clip);
    }

    [Fact]
    public void TheSameGestureTwicePlaysTwice()
    {
        var animator = Make();
        var pose = Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = 1 };
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer }, 0.2f);
        Run(animator, pose, 1.1f);
        Assert.Null(animator.Gesture);
        Run(animator, pose with { GestureCount = 2 }, Dt);
        Assert.Equal(CharacterRig.GestureClip(CharacterGesture.SledgeSwing, 2), animator.Gesture);
    }

    [Fact]
    public void AGestureCanBeTimedToTheTool()
    {
        // The sledge clip is 1 s long; a quicker swing setting plays it faster.
        var animator = new CharacterAnimator(new CharacterSettings { GestureSeconds = new() { ["sledge_swing"] = 0.8f } }, Lengths);
        var pose = Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = 1 };
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer }, 0.2f);
        Run(animator, pose, 0.4f);
        // Started on the first of the frames, so 0.4 s less one frame in.
        Assert.Equal((0.4f - Dt) * 1f / 0.8f, animator.Upper.Single(c => c.Clip == CharacterRig.GestureClip(CharacterGesture.SledgeSwing, 1)).Time, 3);
        Run(animator, pose, 0.45f);
        Assert.Null(animator.Gesture);
    }

    [Fact]
    public void SledgeSwingsMixOverheadAndSideSwingsAtRandom()
    {
        // Every peer works the swing out from the replicated gesture count: the same count, the same swing.
        var swings = Enumerable.Range(0, 256).Select(c => CharacterRig.GestureClip(CharacterGesture.SledgeSwing, (byte)c)).ToList();
        Assert.Equal(swings, Enumerable.Range(0, 256).Select(c => CharacterRig.GestureClip(CharacterGesture.SledgeSwing, (byte)c)));
        Assert.All(CharacterRig.GestureVariants["sledge_swing"], v => Assert.InRange(swings.Count(s => s == v), 60, 110));
        // Never the same swing twice running, and not a fixed rotation either.
        Assert.All(swings.Zip(swings.Skip(1)), p => Assert.NotEqual(p.First, p.Second));
        Assert.Contains(swings.Zip(swings.Skip(1), swings.Skip(2)), t => t.First == t.Third);
        Assert.Contains(swings.Zip(swings.Skip(1), swings.Skip(2)), t => t.First != t.Third);
        // Other gestures have one clip.
        Assert.Equal("tool_use", CharacterRig.GestureClip(CharacterGesture.ToolUse, 7));
        Assert.Equal("sledge_swing", CharacterRig.GestureOf("sledge_swing_back"));
        Assert.Equal("weld", CharacterRig.GestureOf("weld"));
    }

    [Fact]
    public void SteppingBackwardPlaysTheBackpedalWhenTheRigHasIt()
    {
        var lengths = new Dictionary<string, float>(Lengths) { ["walk_back"] = 1f };
        var animator = new CharacterAnimator(new CharacterSettings(), lengths);
        Run(animator, Standing(vz: 1.5f), 1f);
        Assert.Equal(1f, animator.Body.Where(c => c.Clip == "walk_back").Sum(c => c.Weight), 2);
        Assert.DoesNotContain(animator.Body, c => c.Clip == "walk");
        Run(animator, Standing(vz: -1.5f), 1f);
        Assert.Equal(1f, animator.Body.Where(c => c.Clip == "walk").Sum(c => c.Weight), 2);
        Assert.DoesNotContain(animator.Body, c => c.Clip == "walk_back");
        // A rig without it walks backward on the walk, as before.
        var plain = Make();
        Run(plain, Standing(vz: 1.5f), 1f);
        Assert.Equal(1f, plain.Body.Where(c => c.Clip == "walk").Sum(c => c.Weight), 2);
    }

    [Fact]
    public void SteppingSidewaysBlendsTheStrafeClipsAndTurnsTheLegsTheRest()
    {
        var lengths = new Dictionary<string, float>(Lengths)
        {
            ["strafe_l"] = 1f, ["strafe_r"] = 1f, ["strafe_jog_l"] = 0.72f, ["strafe_jog_r"] = 0.72f,
            ["strafe_run_l"] = 0.6f, ["strafe_run_r"] = 0.6f,
        };
        float Share(CharacterAnimator a, string clip) => a.Body.Where(c => c.Clip == clip).Sum(c => c.Weight);
        var d = 1.5f / MathF.Sqrt(2f);

        // Straight to the right: all strafe, the legs turned the strafe's heading short of the way they go.
        var animator = new CharacterAnimator(new CharacterSettings(), lengths);
        Run(animator, Standing(vx: 1.5f), 1f);
        Assert.Equal(1f, Share(animator, "strafe_r"), 2);
        Assert.Equal(45f, MathF.Abs(animator.BodyTurn) * 180f / MathF.PI, 1);
        // Ahead and to the right: half and half, and the legs turn half as far.
        Run(animator, Standing(vx: d, vz: -d), 1f);
        Assert.Equal(0.5f, Share(animator, "strafe_r"), 2);
        Assert.Equal(0.5f, Share(animator, "walk"), 2);
        Assert.Equal(22.5f, MathF.Abs(animator.BodyTurn) * 180f / MathF.PI, 1);
        // Left, and back to the right (the left clip played in reverse steps back to the right).
        Run(animator, Standing(vx: -1.5f), 1f);
        Assert.Equal(1f, Share(animator, "strafe_l"), 2);
        Run(animator, Standing(vx: d, vz: d), 1f);
        Assert.Equal(0.5f, Share(animator, "strafe_l"), 2);
        Assert.Equal(0f, Share(animator, "strafe_r"), 2);
        // Sprinting sideways uses the run's version.
        Run(animator, Standing(vx: 7f), 1f);
        Assert.Equal(1f, Share(animator, "strafe_run_r"), 2);
        // Straight ahead, none; crouching, none either.
        Run(animator, Standing(vz: -1.5f), 1f);
        Assert.DoesNotContain(animator.Body, c => c.Clip.StartsWith("strafe", StringComparison.Ordinal));

        // A rig without them turns the legs as far as they go, as before.
        var plain = Make();
        Run(plain, Standing(vx: 1.5f), 1f);
        Assert.Equal(1f, Share(plain, "walk"), 2);
        Assert.Equal(60f, MathF.Abs(plain.BodyTurn) * 180f / MathF.PI, 1);
    }

    [Fact]
    public void PullingUpPlantsTheFeetAndSettingOffDipsTheBody()
    {
        var lengths = new Dictionary<string, float>(Lengths) { ["stop_walk"] = 0.5f, ["stop_run"] = 0.7f };
        float Share(CharacterAnimator a, string clip) => a.Body.Where(c => c.Clip == clip).Sum(c => c.Weight);
        var animator = new CharacterAnimator(new CharacterSettings(), lengths);
        Run(animator, Standing(), 0.5f);

        // Setting off at a run, the body dips forward more than the run's own lean, then comes back to it.
        var peak = 0f;
        for (var t = 0f; t < 0.3f; t += Dt)
        {
            animator.Update(Standing(vz: -7f), Dt);
            peak = MathF.Max(peak, animator.Lean.X);
        }
        Run(animator, Standing(vz: -7f), 1.5f);
        var running = animator.Lean.X;
        Assert.True(peak > running + 3f * MathF.PI / 180f, $"peak {peak} running {running}");

        // Pulling up from it plays the running stop, which then gives way to idle.
        Run(animator, Standing(), 0.1f);
        Assert.True(Share(animator, "stop_run") > 0.9f, string.Join(", ", animator.Body));
        Run(animator, Standing(), 0.7f);
        Assert.Equal(0f, Share(animator, "stop_run"), 2);
        Assert.Equal(1f, Share(animator, "idle"), 2);

        // From a walk, the walking stop; from a shuffle, none.
        Run(animator, Standing(vz: -1.5f), 1f);
        Run(animator, Standing(), 0.1f);
        Assert.True(Share(animator, "stop_walk") > 0.9f);
        Run(animator, Standing(), 1f);
        Run(animator, Standing(vz: -0.5f), 1f);
        Run(animator, Standing(), 0.1f);
        Assert.DoesNotContain(animator.Body, c => c.Clip.StartsWith("stop", StringComparison.Ordinal));

        // Landing from a jump is the landing's, not a stop.
        Run(animator, Standing(vz: -7f), 1f);
        Run(animator, new CharacterPose(new Vector3(0, 4f, -7f), 0, 0, OnFloor: false), 0.5f);
        Run(animator, Standing(), 0.1f);
        Assert.DoesNotContain(animator.Body, c => c.Clip.StartsWith("stop", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLegsKeepWalkingThroughASwing()
    {
        var animator = Make();
        var walking = Standing(vz: -1.5f) with { Prop = CharacterProp.Sledgehammer };
        Run(animator, walking, 0.5f);
        Run(animator, walking with { Gesture = CharacterGesture.SledgeSwing, GestureCount = 1 }, 0.3f);
        Assert.StartsWith("sledge_swing", animator.Gesture);
        Assert.Equal(1f, animator.Body.Where(c => c.Clip == "walk").Sum(c => c.Weight), 2);
        Assert.Contains(animator.Upper, c => c.Clip.StartsWith("sledge_swing", StringComparison.Ordinal) && c.Weight > 0.9f);
    }

    [Fact]
    public void ASideSwingTakesTheSwingsTime()
    {
        var animator = new CharacterAnimator(new CharacterSettings { GestureSeconds = new() { ["sledge_swing"] = 0.5f } }, Lengths);
        var count = (byte)Enumerable.Range(1, 20).First(c => CharacterRig.GestureClip(CharacterGesture.SledgeSwing, (byte)c) == "sledge_swing_side");
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer, GestureCount = (byte)(count - 1) }, 0.2f);
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = count }, 0.3f);
        Assert.Equal("sledge_swing_side", animator.Gesture);
        Assert.Equal((0.3f - Dt) / 0.5f, animator.Upper.Single(c => c.Clip == "sledge_swing_side").Time, 3);
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = count }, 0.25f);
        Assert.Null(animator.Gesture);
    }

    [Fact]
    public void AGestureStartSkipsTheStartOfItsClip()
    {
        // The sledge swing skips the first 30% of its clip (the wind-up) and plays the rest in 0.35 s.
        var animator = new CharacterAnimator(new CharacterSettings
        {
            GestureSeconds = new() { ["sledge_swing"] = 0.35f },
            GestureStart = new() { ["sledge_swing"] = 0.3f },
        }, Lengths);
        var count = (byte)Enumerable.Range(1, 20).First(c => CharacterRig.GestureClip(CharacterGesture.SledgeSwing, (byte)c) == "sledge_swing_back");
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer, GestureCount = (byte)(count - 1) }, 0.2f);
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = count }, Dt);
        Assert.Equal(0.3f, animator.GestureTime, 3);
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = count }, 0.2f);
        Assert.Equal(0.3f + 0.7f * 0.2f / 0.35f, animator.GestureTime, 3);
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = count }, 0.16f);
        Assert.Null(animator.Gesture);
    }

    [Fact]
    public void WithoutGestureSecondsTheSkippedPartIsNotWaitedOut()
    {
        var animator = new CharacterAnimator(new CharacterSettings { GestureStart = new() { ["sledge_swing"] = 0.5f } }, Lengths);
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer }, 0.2f);
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = 1 }, 0.25f);
        Assert.Equal(0.5f + 0.25f - Dt, animator.GestureTime, 3);   // the rest plays at its own pace
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = 1 }, 0.27f);
        Assert.Null(animator.Gesture);
    }

    [Fact]
    public void FirstPersonArmsPlayTheirOwnVersionsWhereThereAreSome()
    {
        var animator = new CharacterAnimator(new CharacterSettings { GestureSeconds = new() { ["sledge_swing"] = 0.8f } }, Lengths, firstPerson: true);
        Run(animator, Standing() with { Prop = CharacterProp.Sledgehammer }, 0.5f);
        Assert.Equal("hold_heavy_fp", Assert.Single(animator.Upper).Clip);

        var pose = Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = 1 };
        Run(animator, pose, 0.4f);
        Assert.Equal(CharacterRig.GestureClip(CharacterGesture.SledgeSwing, 1) + "_fp", animator.Gesture);
        // Timed like the clip it stands in for.
        Assert.Equal((0.4f - Dt) / 0.8f, animator.Upper.Single(c => c.Clip == CharacterRig.GestureClip(CharacterGesture.SledgeSwing, 1) + "_fp").Time, 3);

        // A tool without first-person clips plays the ordinary ones.
        Run(animator, Standing() with { Prop = CharacterProp.LiftRemote }, 1f);
        Assert.Equal("hold_tool", Assert.Single(animator.Upper).Clip);
        var body = Make();
        Run(body, Standing() with { Prop = CharacterProp.Sledgehammer }, 0.5f);
        Assert.Equal("hold_heavy", Assert.Single(body.Upper).Clip);
    }

    [Fact]
    public void HoldingRightClickRaisesTheSledgehammerToBlock()
    {
        var animator = Make();
        var held = Standing() with { Prop = CharacterProp.Sledgehammer };
        Run(animator, held, 0.5f);
        Run(animator, held with { Guard = true }, 0.1f);
        // Crossfading over the hold fade (0.2 s): half way after 0.1 s.
        Assert.Equal(0.5f, animator.Upper.Single(c => c.Clip == "hold_heavy_block").Weight, 1);
        Run(animator, held with { Guard = true }, 0.2f);
        Assert.Equal("hold_heavy_block", Assert.Single(animator.Upper).Clip);
        Run(animator, held, 0.3f);
        Assert.Equal("hold_heavy", Assert.Single(animator.Upper).Clip);
        // Blocking is a Sledgehammer pose only.
        Assert.Equal("hold_tool", CharacterRig.HoldClip(CharacterProp.LiftRemote, guard: true));
    }

    [Theory]
    [InlineData(false, "weld_draw", "hold_weld_aim", "weld")]
    [InlineData(true, "weld_draw_fp", "hold_weld_aim_fp", "weld_fp")]
    public void TheWeldToolIsDrawnRaisedWhileAPartIsSelectedAndPushedInToWeld(bool firstPerson, string draw, string aim, string weld)
    {
        var animator = new CharacterAnimator(new CharacterSettings(), Lengths, firstPerson);
        var held = Standing() with { Prop = CharacterProp.WeldTool };
        Run(animator, Standing() with { Prop = CharacterProp.LiftRemote }, 0.5f);
        Run(animator, held, Dt);
        Assert.Equal(draw, animator.Gesture);
        Run(animator, held with { Guard = true }, 0.8f);
        Assert.Contains(animator.Upper, c => c.Clip == aim && c.Weight > 0.99f);
        animator.Update(held with { Guard = true, Gesture = CharacterGesture.Weld, GestureCount = 1 }, Dt);
        Assert.Equal(weld, animator.Gesture);
        Assert.Equal("weld_stow", CharacterRig.StowClip(CharacterProp.WeldTool));
    }

    [Fact]
    public void HandsAreFreeWhileSwimming()
    {
        var animator = Make();
        Run(animator, new CharacterPose(Vector3.Zero, 0, 0, OnFloor: false, Swimming: true, Prop: CharacterProp.LiftRemote), 1f);
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
            animator.Update(new CharacterPose(new Vector3(0, 0, -4), 0, 0, true, Prop: CharacterProp.LiftRemote, Gesture: CharacterGesture.ToolUse, GestureCount: 1), Dt);
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

    [Theory]
    [InlineData(0f, "hit_front")]
    [InlineData(1.5f, "hit_left")]
    [InlineData(-1.5f, "hit_right")]
    [InlineData(3f, "hit_back")]
    public void ALightHitFlinchesAwayFromWhereItCameFrom(float from, string clip)
    {
        var animator = Make();
        Run(animator, Standing(), 0.1f);
        animator.React(HitReaction.Light, from);
        Run(animator, Standing(), Dt);
        Assert.Equal(clip, animator.Gesture);
        Assert.False(animator.Grounded);
        // A flinch on its health dropping doesn't cut it short.
        animator.Play(CharacterGesture.Hurt);
        Assert.Equal(clip, animator.Gesture);
    }

    [Fact]
    public void AKnockdownLiesAMomentThenGetsUp()
    {
        var animator = Make();
        Run(animator, Standing(), 0.1f);
        animator.React(HitReaction.Knockdown, 0f);
        Run(animator, Standing(), Dt);
        Assert.Equal("knockdown", animator.Gesture);
        Assert.True(animator.Grounded);
        // Down, it takes no other hit or gesture.
        animator.React(HitReaction.Light, 0f);
        Assert.Equal("knockdown", animator.Gesture);
        Run(animator, Standing(), 1.1f + 0.6f + 0.1f);
        Assert.Equal("get_up", animator.Gesture);
        Run(animator, Standing(), 1.4f);
        Assert.Null(animator.Gesture);
        Assert.False(animator.Grounded);
    }

    [Fact]
    public void GettingUpHoldsThePlayerDownUntilTheClipEnds()
    {
        var animator = Make();
        Run(animator, Standing(), 0.1f);

        Assert.True(animator.GetUp());
        Run(animator, Standing(), Dt);
        Assert.Equal("get_up", animator.Gesture);
        Assert.True(animator.Grounded);
        Assert.False(animator.GetUp());
        Run(animator, Standing(), 1.4f);
        Assert.Null(animator.Gesture);
        Assert.False(animator.Grounded);
    }

    [Fact]
    public void ASeatedPlayerOnlyFlinches()
    {
        var animator = Make();
        var seated = Standing() with { Seated = true };
        Run(animator, seated, 0.1f);
        animator.React(HitReaction.Knockdown, 0f);
        Run(animator, seated, Dt);
        Assert.Equal("hit_front", animator.Gesture);
    }

    [Fact]
    public void DyingFallsAndStaysDownUntilRespawn()
    {
        var animator = Make();
        Run(animator, Standing(), 0.1f);
        Run(animator, Standing() with { Dead = true }, Dt);
        Assert.Equal("death", animator.Gesture);
        Run(animator, Standing() with { Dead = true }, 3f);
        Assert.Equal("downed", animator.Gesture);
        Assert.True(animator.Grounded);
        Run(animator, Standing(), Dt);
        Assert.False(animator.Grounded);
    }

    [Fact]
    public void JoiningWhileDeadStartsDown()
    {
        var animator = Make();
        Run(animator, Standing() with { Dead = true }, Dt);
        Assert.Equal("downed", animator.Gesture);
    }

    [Fact]
    public void StandingTheFeetStayPutUntilTheLookTurnsFarThenStepRound()
    {
        var animator = Make();
        Run(animator, Standing(), 0.2f);
        // A look 40° to the left: the upper body twists toward it, the feet stay.
        Run(animator, Standing(yaw: 0.7f), 0.2f);
        Assert.Null(animator.TurnClip);
        Assert.True(animator.Twist > 0.3f);
        // Further than turn_in_place_degrees: the feet step round to the left.
        Run(animator, Standing(yaw: 1.2f), Dt * 2);
        Assert.Equal("turn_l", animator.TurnClip);
        Run(animator, Standing(yaw: 1.2f), 1.5f);
        Assert.Null(animator.TurnClip);
    }

    [Fact]
    public void SwingsUseTheLegsStandingButNotRunning()
    {
        var animator = Make();
        var swing = Standing() with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing, GestureCount = 1 };
        Run(animator, swing with { GestureCount = 0 }, 0.3f);
        Run(animator, swing, 0.15f);
        Assert.True(animator.LegsWeight > 0.5f);

        var running = Make();
        var run = Standing(vz: -7f) with { Prop = CharacterProp.Sledgehammer, Gesture = CharacterGesture.SledgeSwing };
        Run(running, run with { GestureCount = 0 }, 0.5f);
        Run(running, run with { GestureCount = 1 }, 0.15f);
        Assert.True(running.LegsWeight < 0.05f);
    }

    [Fact]
    public void WalkingWithAToolTheStrideStillSwaysTheTorsoAndTheFreeArm()
    {
        var animator = Make();
        var holding = Standing() with { Prop = CharacterProp.LiftRemote };
        Run(animator, holding, 0.5f);
        Assert.Equal(0f, animator.Carry);
        Run(animator, Standing(vz: -1.5f) with { Prop = CharacterProp.LiftRemote }, 0.8f);
        Assert.True(animator.Carry > 0.7f);
        var free = CharacterRig.FreeArmHolds.Contains(CharacterRig.HoldClip(CharacterProp.LiftRemote, false)!);
        Assert.Equal(free ? 1f : 0f, animator.FreeArm, 3);
    }
}
