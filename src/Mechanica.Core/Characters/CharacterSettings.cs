using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Characters;

/// <summary>
/// How player characters are animated, from <c>game/data/character.json</c> (engine settings, not moddable).
/// Times are in seconds, speeds in m/s. The defaults here only fill fields the file leaves out.
/// </summary>
public sealed record CharacterSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    /// <summary>The shared animation set (<see cref="CharacterRig.Clips"/>), as a mod asset path.</summary>
    public string Animations { get; init; } = "vanilla:meshes/characters/humanoid_animations.glb";

    /// <summary>The props drawn in a player's hand (<see cref="CharacterRig.PropNode"/>), as a mod asset path.</summary>
    public string HandTools { get; init; } = "vanilla:meshes/characters/hand_tools.glb";

    /// <summary>
    /// The ground speed each moving clip is authored for: played once per cycle at that speed, so feet don't
    /// slide. Clips not listed (idle, sit) don't move.
    /// </summary>
    public Dictionary<string, float> ClipSpeeds { get; init; } = new()
    {
        ["walk"] = 1.5f, ["jog"] = 4f, ["run"] = 7f, ["crouch_walk"] = 1.5f, ["swim"] = 2.5f,
    };

    /// <summary>Where in a walking cycle (0 to 1) the left and then the right foot strikes the ground.</summary>
    public float[] StepPhases { get; init; } = [0f, 0.5f];

    /// <summary>Where in a swimming cycle (0 to 1) each arm pulls.</summary>
    public float[] StrokePhases { get; init; } = [0.25f, 0.75f];

    /// <summary>Crossfade between standing, crouching, falling, swimming and sitting.</summary>
    public float ModeFade { get; init; } = 0.15f;

    /// <summary>Crossfade between hold poses when the item in hand changes.</summary>
    public float HoldFade { get; init; } = 0.2f;

    /// <summary>Fade into a gesture.</summary>
    public float GestureFadeIn { get; init; } = 0.06f;

    /// <summary>Fade out of a gesture at its end.</summary>
    public float GestureFadeOut { get; init; } = 0.12f;

    /// <summary>Time off the ground before a player who didn't jump starts to fall (stepping off a kerb doesn't).</summary>
    public float AirGrace { get; init; } = 0.15f;

    /// <summary>Upward speed leaving the ground that counts as a jump.</summary>
    public float JumpDetectSpeed { get; init; } = 1.5f;

    /// <summary>Slowest landing that plays the landing animation and sound.</summary>
    public float LandMinSpeed { get; init; } = 2.5f;

    /// <summary>Landing speed from which the hard landing sound plays.</summary>
    public float HardLandSpeed { get; init; } = 9f;

    /// <summary>Slowest movement that makes footsteps.</summary>
    public float MinStepSpeed { get; init; } = 0.3f;

    /// <summary>How far the body turns toward a sideways or backward move, in degrees.</summary>
    public float MaxBodyTurnDegrees { get; init; } = 60f;

    /// <summary>How fast the body turns, in degrees per second.</summary>
    public float BodyTurnRate { get; init; } = 540f;

    /// <summary>How far the model moves up from the seat point while sitting, in metres (negative is down).</summary>
    public float SeatedOffset { get; init; } = 0f;

    /// <summary>
    /// How a held item looks, by bare item id: the prop drawn and the gesture its use plays. Items not listed
    /// use <see cref="CategoryPoses"/> for their category, then <see cref="CharacterProp.Item"/> with no gesture.
    /// </summary>
    public Dictionary<string, ItemPose> ItemPoses { get; init; } = [];

    /// <summary>How a held item looks by its part category (<c>consumable</c>, <c>weapon</c>, …).</summary>
    public Dictionary<string, ItemPose> CategoryPoses { get; init; } = [];

    /// <summary>How the item <paramref name="itemId"/> (qualified or bare) of <paramref name="category"/> looks in hand.</summary>
    public ItemPose PoseFor(string itemId, string? category)
    {
        var bare = itemId[(itemId.IndexOf(':') + 1)..];
        return ItemPoses.GetValueOrDefault(bare) ?? (category is null ? null : CategoryPoses.GetValueOrDefault(category)) ?? ItemPose.Default;
    }

    /// <summary>Share of the look pitch the neck and head follow (0 to 1).</summary>
    public float HeadPitchShare { get; init; } = 0.6f;

    /// <summary>
    /// Where the first-person arms' model stands relative to the camera, in metres (right, up, back). The model's
    /// feet sit here, so the hands come into the lower part of the view.
    /// </summary>
    public float[] FirstPersonOffset { get; init; } = [0f, -1.25f, 0.1f];

    /// <summary>How far the first-person arms tip forward from upright, in degrees, to bring the hands into view.</summary>
    public float FirstPersonPitchDegrees { get; init; } = 10f;

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static CharacterSettings FromJson(string json)
    {
        CharacterSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<CharacterSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid character settings JSON: {e.Message}", e);
        }
        if (settings is null)
            throw new FormatException("Character settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        foreach (var (clip, speed) in ClipSpeeds)
            Positive(speed, $"clip_speeds.{clip}");
        foreach (var phase in StepPhases.Concat(StrokePhases))
        {
            if (!float.IsFinite(phase) || phase is < 0f or >= 1f)
                throw new FormatException($"step and stroke phases must be from 0 up to 1 (got {phase}).");
        }
        Positive(ModeFade, "mode_fade");
        Positive(HoldFade, "hold_fade");
        Positive(GestureFadeIn, "gesture_fade_in");
        Positive(GestureFadeOut, "gesture_fade_out");
        NonNegative(AirGrace, "air_grace");
        Positive(JumpDetectSpeed, "jump_detect_speed");
        NonNegative(LandMinSpeed, "land_min_speed");
        Positive(HardLandSpeed, "hard_land_speed");
        NonNegative(MinStepSpeed, "min_step_speed");
        Positive(BodyTurnRate, "body_turn_rate");
        if (!float.IsFinite(MaxBodyTurnDegrees) || MaxBodyTurnDegrees is < 0f or > 90f)
            throw new FormatException($"max_body_turn_degrees must be from 0 to 90 (got {MaxBodyTurnDegrees}).");
        CheckPoses(ItemPoses, "item_poses");
        CheckPoses(CategoryPoses, "category_poses");
        if (!float.IsFinite(SeatedOffset) || MathF.Abs(SeatedOffset) > 2f)
            throw new FormatException($"seated_offset must be from -2 to 2 metres (got {SeatedOffset}).");
        if (!float.IsFinite(HeadPitchShare) || HeadPitchShare is < 0f or > 1f)
            throw new FormatException($"head_pitch_share must be from 0 to 1 (got {HeadPitchShare}).");
        if (FirstPersonOffset is not { Length: 3 } || FirstPersonOffset.Any(v => !float.IsFinite(v) || MathF.Abs(v) > 3f))
            throw new FormatException("first_person_offset must be three numbers from -3 to 3 metres.");
        if (!float.IsFinite(FirstPersonPitchDegrees) || MathF.Abs(FirstPersonPitchDegrees) > 60f)
            throw new FormatException($"first_person_pitch_degrees must be from -60 to 60 (got {FirstPersonPitchDegrees}).");
    }

    private static void CheckPoses(Dictionary<string, ItemPose> poses, string name)
    {
        foreach (var (key, pose) in poses)
        {
            if (pose is null || !Enum.IsDefined(pose.Prop) || !Enum.IsDefined(pose.Gesture))
                throw new FormatException($"{name}.{key} needs a known prop and gesture.");
        }
    }

    private static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new FormatException($"{name} must be a positive number (got {value}).");
    }

    private static void NonNegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f)
            throw new FormatException($"{name} must be zero or positive (got {value}).");
    }
}

/// <summary>How an item looks in hand: the prop drawn and the gesture using it plays.</summary>
public sealed record ItemPose
{
    /// <summary>An item with no pose of its own: held up, no gesture.</summary>
    public static ItemPose Default { get; } = new() { Prop = CharacterProp.Item };

    /// <summary>The prop drawn (and so the arm pose).</summary>
    public CharacterProp Prop { get; init; } = CharacterProp.Item;

    /// <summary>The gesture played when it is used.</summary>
    public CharacterGesture Gesture { get; init; }
}
