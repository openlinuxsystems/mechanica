using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Player;

/// <summary>
/// Tuning for the on-foot player, in SI units. The game loads the values from JSON
/// (balance numbers live in data); the defaults here only fill fields the file leaves out.
/// </summary>
public sealed record PlayerMovementSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Top walking speed in m/s.</summary>
    public float WalkSpeed { get; init; } = 4f;

    /// <summary>Top sprinting speed in m/s.</summary>
    public float SprintSpeed { get; init; } = 7f;

    /// <summary>Horizontal acceleration on the ground in m/s².</summary>
    public float GroundAcceleration { get; init; } = 40f;

    /// <summary>
    /// On the ground, how fast speed builds above <see cref="WalkSpeed"/> toward a sprint, in m/s²: the player visibly
    /// picks up pace over a few strides instead of snapping to a run. Turning keeps <see cref="GroundAcceleration"/>.
    /// </summary>
    public float SprintAcceleration { get; init; } = 9f;

    /// <summary>On the ground, how fast speed drops while above <see cref="WalkSpeed"/>, in m/s²: a sprint eases off before it walks or stops.</summary>
    public float SprintDeceleration { get; init; } = 14f;

    /// <summary>Horizontal acceleration in the air in m/s² (air control).</summary>
    public float AirAcceleration { get; init; } = 8f;

    /// <summary>Upward speed given by a jump in m/s.</summary>
    public float JumpSpeed { get; init; } = 4.5f;

    /// <summary>Downward acceleration in m/s².</summary>
    public float Gravity { get; init; } = 9.81f;

    /// <summary>Fastest fall speed in m/s.</summary>
    public float MaxFallSpeed { get; init; } = 50f;

    /// <summary>Look limit above and below the horizon in degrees.</summary>
    public float MaxPitchDegrees { get; init; } = 89f;

    /// <summary>Capsule height in metres.</summary>
    public float Height { get; init; } = 1.8f;

    /// <summary>Capsule radius in metres.</summary>
    public float Radius { get; init; } = 0.35f;

    /// <summary>Camera height above the feet in metres.</summary>
    public float EyeHeight { get; init; } = 1.6f;

    /// <summary>Force in N the player applies to a rigid body it walks into.</summary>
    public float PushForce { get; init; } = 600f;

    /// <summary>Top crouching speed in m/s.</summary>
    public float CrawlSpeed { get; init; } = 1.5f;

    /// <summary>Capsule height while crouching in metres, so a crouching player fits under low gaps.</summary>
    public float CrouchHeight { get; init; } = 1.2f;

    /// <summary>Camera height above the feet while crouching in metres.</summary>
    public float CrouchEyeHeight { get; init; } = 1.05f;

    /// <summary>How fast the camera moves between standing and crouching eye height in m/s.</summary>
    public float StanceSpeed { get; init; } = 4f;

    /// <summary>Whether the local player's camera starts first-person (zoom out with C for third person).</summary>
    public bool CameraStartsFirstPerson { get; init; } = true;

    /// <summary>
    /// How far behind the player the third-person camera sits when it pulls back (death, spectating, starting third-person), in metres.
    /// 0 means first-person, and pulling back then uses <see cref="CameraMinDistance"/>.
    /// </summary>
    public float CameraDistance { get; init; } = 3.5f;

    /// <summary>The closest third-person camera distance in metres; zooming in past it goes first-person.</summary>
    public float CameraMinDistance { get; init; } = 1.5f;

    /// <summary>The furthest third-person camera distance in metres.</summary>
    public float CameraMaxDistance { get; init; } = 6f;

    /// <summary>Metres one zoom step moves the camera.</summary>
    public float CameraZoomStep { get; init; } = 1f;

    /// <summary>Metres the third-person camera sits right of the head, over the shoulder, so the player doesn't hide the crosshair.</summary>
    public float CameraShoulder { get; init; } = 0.5f;

    /// <summary>Metres the third-person camera sits above the head.</summary>
    public float CameraRaise { get; init; } = 0.25f;

    /// <summary>Seconds a player takes to sit down in a seat or stand up beside it (UI spec §9, §10).</summary>
    public float SeatTransitionSeconds { get; init; } = 0.3f;

    /// <summary>Eye height above a seated player's feet in metres, for the seat's interior camera when the player has no body (it looks from the body's eyes otherwise).</summary>
    public float SeatedEyeHeight { get; init; } = 1.1f;

    /// <summary>How far behind the seat the external chase camera sits, in metres (Ctrl toggles it).</summary>
    public float ChaseDistance { get; init; } = 6f;

    /// <summary>How high above the seat the chase camera sits, in metres.</summary>
    public float ChaseHeight { get; init; } = 2.2f;

    /// <summary>How quickly the chase camera catches up with the seat (1/s); higher is stiffer.</summary>
    public float ChaseStiffness { get; init; } = 8f;

    /// <summary>How deep the feet are below the water surface when the player starts swimming, in metres.</summary>
    public float SwimDepth { get; init; } = 1.2f;

    /// <summary>How deep the feet float below the surface when the player swims without rising or diving, in metres.</summary>
    public float FloatDepth { get; init; } = 1.4f;

    /// <summary>Top swimming speed in m/s.</summary>
    public float SwimSpeed { get; init; } = 2.5f;

    /// <summary>Top swimming speed with sprint held in m/s.</summary>
    public float SwimSprintSpeed { get; init; } = 4f;

    /// <summary>Speed rising (jump held) or diving (crouch held) in water, m/s.</summary>
    public float SwimVerticalSpeed { get; init; } = 2f;

    /// <summary>Acceleration in water in m/s², horizontal and vertical.</summary>
    public float SwimAcceleration { get; init; } = 6f;

    /// <summary>Upward speed per metre the player is below their float depth, 1/s.</summary>
    public float Buoyancy { get; init; } = 3f;

    /// <summary>
    /// How high the feet rise above the water surface when a swimmer jumps at the surface, in metres. It is the
    /// tallest edge a player can climb out onto from deep water, so no pond with a bank that low is a trap.
    /// </summary>
    public float SwimLeapHeight { get; init; } = 0.6f;

    /// <summary>
    /// The tallest edge the player walks or runs straight up without jumping, in metres: one block (0.25 m) with
    /// room for uneven ground under it, so stairs and soil plots need no jump. It is also how far a player walking off
    /// an edge is kept on the floor, so going down stairs does not fall a step at a time.
    /// </summary>
    public float StepHeight { get; init; } = 0.35f;

    /// <summary>Speed climbing a ladder up or down, in m/s (Parts map E2, <see cref="LadderClimb"/>).</summary>
    public float ClimbSpeed { get; init; } = 2f;

    /// <summary>Speed moving sideways along a ladder's rungs, in m/s.</summary>
    public float ClimbSideSpeed { get; init; } = 1f;

    /// <summary>
    /// How far from a ladder's face the player's capsule may be and still grab it by walking into it, in metres. A player
    /// on it is let go once it is twice this far off.
    /// </summary>
    public float ClimbReach { get; init; } = 0.25f;

    /// <summary>Speed a jump off a ladder pushes the player away from its face, in m/s; they also rise at <see cref="ClimbJumpUp"/>.</summary>
    public float ClimbJumpSpeed { get; init; } = 3.5f;

    /// <summary>Upward speed of a jump off a ladder, in m/s.</summary>
    public float ClimbJumpUp { get; init; } = 3f;

    /// <summary>Seconds after letting go of a ladder (a jump, the top, the bottom) before it can be grabbed again.</summary>
    public float ClimbRegrabSeconds { get; init; } = 0.4f;

    /// <summary>Speed the player steps off the top of a ladder onto the floor behind it, in m/s.</summary>
    public float ClimbTopOutSpeed { get; init; } = 2.5f;

    /// <summary>What walking speed is multiplied by when wading in water shallower than <see cref="SwimDepth"/>.</summary>
    public float WadeSpeedScale { get; init; } = 0.7f;

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static PlayerMovementSettings FromJson(string json)
    {
        PlayerMovementSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<PlayerMovementSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid player settings JSON: {e.Message}", e);
        }

        if (settings is null)
            throw new FormatException("Player settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        Positive(WalkSpeed, "walk_speed");
        Positive(SprintSpeed, "sprint_speed");
        Positive(GroundAcceleration, "ground_acceleration");
        Positive(SprintAcceleration, "sprint_acceleration");
        Positive(SprintDeceleration, "sprint_deceleration");
        NonNegative(AirAcceleration, "air_acceleration");
        NonNegative(JumpSpeed, "jump_speed");
        NonNegative(Gravity, "gravity");
        Positive(MaxFallSpeed, "max_fall_speed");
        Positive(Height, "height");
        Positive(Radius, "radius");
        NonNegative(PushForce, "push_force");
        Positive(CrawlSpeed, "crawl_speed");
        Positive(SwimDepth, "swim_depth");
        Positive(SwimSpeed, "swim_speed");
        Positive(SwimSprintSpeed, "swim_sprint_speed");
        Positive(SwimVerticalSpeed, "swim_vertical_speed");
        Positive(SwimAcceleration, "swim_acceleration");
        Positive(Buoyancy, "buoyancy");
        NonNegative(SwimLeapHeight, "swim_leap_height");
        Positive(ClimbSpeed, "climb_speed");
        NonNegative(ClimbSideSpeed, "climb_side_speed");
        Positive(ClimbReach, "climb_reach");
        NonNegative(ClimbJumpSpeed, "climb_jump_speed");
        NonNegative(ClimbJumpUp, "climb_jump_up");
        NonNegative(ClimbRegrabSeconds, "climb_regrab_seconds");
        Positive(ClimbTopOutSpeed, "climb_top_out_speed");
        if (!float.IsFinite(StepHeight) || StepHeight < 0f || StepHeight >= CrouchHeight / 2f)
            throw new FormatException($"step_height must be from 0 up to below half of crouch_height (got {StepHeight}).");
        if (!float.IsFinite(FloatDepth) || FloatDepth < SwimDepth || FloatDepth >= EyeHeight)
            throw new FormatException($"float_depth must be from swim_depth up to below eye_height, so a floating player swims with their head out (got {FloatDepth}).");
        if (!float.IsFinite(WadeSpeedScale) || WadeSpeedScale is <= 0f or > 1f)
            throw new FormatException($"wade_speed_scale must be above 0 and at most 1 (got {WadeSpeedScale}).");
        if (!float.IsFinite(MaxPitchDegrees) || MaxPitchDegrees is <= 0f or >= 90f)
            throw new FormatException($"max_pitch_degrees must be between 0 and 90 (got {MaxPitchDegrees}).");
        if (Height < 2f * Radius)
            throw new FormatException($"height ({Height}) must be at least twice radius ({Radius}).");
        if (!float.IsFinite(EyeHeight) || EyeHeight <= 0f || EyeHeight > Height)
            throw new FormatException($"eye_height must be in (0, height] (got {EyeHeight}).");
        if (!float.IsFinite(CrouchHeight) || CrouchHeight < 2f * Radius || CrouchHeight > Height)
            throw new FormatException($"crouch_height must be from twice radius up to height (got {CrouchHeight}).");
        if (!float.IsFinite(CrouchEyeHeight) || CrouchEyeHeight <= 0f || CrouchEyeHeight > CrouchHeight || CrouchEyeHeight > EyeHeight)
            throw new FormatException($"crouch_eye_height must be in (0, crouch_height] and at most eye_height (got {CrouchEyeHeight}).");
        Positive(StanceSpeed, "stance_speed");
        Positive(SeatTransitionSeconds, "seat_transition_seconds");
        Positive(SeatedEyeHeight, "seated_eye_height");
        Positive(ChaseDistance, "chase_distance");
        Positive(ChaseStiffness, "chase_stiffness");
        if (!float.IsFinite(ChaseHeight) || Math.Abs(ChaseHeight) > 20f)
            throw new FormatException($"chase_height must be within 20 m (got {ChaseHeight}).");
        Positive(CameraMinDistance, "camera_min_distance");
        Positive(CameraZoomStep, "camera_zoom_step");
        if (!float.IsFinite(CameraShoulder) || Math.Abs(CameraShoulder) > 2f)
            throw new FormatException($"camera_shoulder must be within 2 m either side (got {CameraShoulder}).");
        if (!float.IsFinite(CameraRaise) || Math.Abs(CameraRaise) > 2f)
            throw new FormatException($"camera_raise must be within 2 m (got {CameraRaise}).");
        if (!float.IsFinite(CameraMaxDistance) || CameraMaxDistance < CameraMinDistance)
            throw new FormatException($"camera_max_distance must be at least camera_min_distance (got {CameraMaxDistance}).");
        if (!float.IsFinite(CameraDistance) || CameraDistance != 0f && (CameraDistance < CameraMinDistance || CameraDistance > CameraMaxDistance))
            throw new FormatException($"camera_distance must be 0 (first-person) or from camera_min_distance to camera_max_distance (got {CameraDistance}).");
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
