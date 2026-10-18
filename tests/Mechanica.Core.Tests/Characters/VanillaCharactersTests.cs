using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Characters;

/// <summary>The shipped characters, animation set and settings fit the rig.</summary>
public class VanillaCharactersTests
{
    private static readonly string Vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");
    private static readonly Lazy<ModLoadResult> Content = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static CharacterSettings Settings() =>
        CharacterSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "character.json")));

    [Fact]
    public void VanillaHasTheMaleAndFemaleScrapper()
    {
        var book = CharacterBook.Load(Content.Value);
        Assert.Empty(book.Diagnostics);
        Assert.Equal(["vanilla:scrapper_male", "vanilla:scrapper_female"], book.All.Select(c => c.QualifiedId));
    }

    [Fact]
    public void BothCharactersShareOneSkeleton()
    {
        var male = GltfSummary.Read(Path.Combine(Vanilla, "meshes", "characters", "scrapper_male.glb"));
        var female = GltfSummary.Read(Path.Combine(Vanilla, "meshes", "characters", "scrapper_female.glb"));
        Assert.Empty(male.MissingBones());
        Assert.Equal(male.Joints.Order(), female.Joints.Order());
    }

    [Fact]
    public void TheAnimationSetHasEveryClipAndUpperBodyClipsLeaveTheLegsAlone()
    {
        Assert.True(AssetPath.TryParse(Settings().Animations, "vanilla", AssetKind.Mesh, out var asset, out var error), error);
        var summary = GltfSummary.Read(asset.Resolve(Path.Combine(TestMods.RepoRoot, "mods", asset.ModId)));
        foreach (var clip in CharacterRig.Clips)
            Assert.True(summary.Animation(clip) is { Duration: > 0 }, $"clip '{clip}' is missing or empty");
        foreach (var clip in CharacterRig.UpperBodyClips)
        {
            var lower = summary.Animation(clip)!.Targets.Where(t => CharacterRig.Bones.Contains(t) && !CharacterRig.UpperBodyBones.Contains(t)).ToList();
            Assert.True(lower.Count == 0, $"'{clip}' moves {string.Join(", ", lower)}");
        }
    }

    [Fact]
    public void MovingClipsMatchTheirAuthoredCycle()
    {
        var settings = Settings();
        var summary = GltfSummary.Read(Path.Combine(Vanilla, "meshes", "characters", "humanoid_animations.glb"));
        Assert.Equal(0.9f, summary.Animation("walk")!.Duration, 2);
        Assert.Equal(0.72f, summary.Animation("jog")!.Duration, 1);
        Assert.Equal(0.6f, summary.Animation("run")!.Duration, 1);
        Assert.All(settings.ClipSpeeds.Keys, clip => Assert.NotNull(summary.Animation(clip)));
    }

    [Fact]
    public void SettingsLoadAndItemPosesResolve()
    {
        var settings = Settings();
        Assert.Equal(CharacterProp.WaterBucket, settings.PoseFor("vanilla:water_bucket", "consumable").Prop);
        Assert.Equal(CharacterGesture.BucketFill, settings.PoseFor("vanilla:water_bucket", "consumable").Gesture);
        Assert.Equal(CharacterProp.WaterBucketFull, settings.PoseFor("vanilla:water_bucket_full", "consumable").Prop);
        Assert.Equal(CharacterGesture.BucketThrow, settings.PoseFor("vanilla:water_bucket_full", "consumable").Gesture);
        Assert.Equal(CharacterGesture.Plant, settings.PoseFor("carrot_seed", "consumable").Gesture);
        Assert.Equal(CharacterGesture.Eat, settings.PoseFor("vanilla:carrot", "consumable").Gesture);
        Assert.Equal(CharacterGesture.FireGun, settings.PoseFor("vanilla:potato_scattergun", "weapon").Gesture);
        Assert.Equal(ItemPose.Default, settings.PoseFor("some_mod:thing", null));
    }

    [Theory]
    [InlineData("""{ "mode_fade": 0 }""")]
    [InlineData("""{ "step_phases": [1.5] }""")]
    [InlineData("""{ "head_pitch_share": 2 }""")]
    [InlineData("""{ "item_poses": { "x": { "prop": "laser" } } }""")]
    [InlineData("""{ "nonsense": 1 }""")]
    [InlineData("""{ "first_person_offset": [0, 1] }""")]
    [InlineData("""{ "first_person_offset": [0, 9, 0] }""")]
    [InlineData("""{ "first_person_pitch_degrees": 80 }""")]
    public void BadSettingsAreRefused(string json) => Assert.Throws<FormatException>(() => CharacterSettings.FromJson(json));

    [Fact]
    public void EveryCharacterSoundIsAVanillaEvent()
    {
        var sounds = Mechanica.Core.Sound.SoundRegistry.Load(Content.Value);
        foreach (var sound in CharacterSounds.Defaults.Values)
            Assert.True(sounds.Find(sound, "vanilla") is not null, $"no sound event '{sound}'");
    }
}
