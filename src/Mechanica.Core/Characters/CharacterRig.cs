namespace Mechanica.Core.Characters;

/// <summary>
/// What a player holds, as far as how their character looks: the prop in the right hand and the arm
/// pose that goes with it. Sent in every input and snapshot, so it is one byte.
/// </summary>
public enum CharacterProp : byte
{
    /// <summary>Empty hands.</summary>
    None,

    /// <summary>The Hammer.</summary>
    Hammer,

    /// <summary>The Lift's remote.</summary>
    LiftRemote,

    /// <summary>The Weld Tool.</summary>
    WeldTool,

    /// <summary>The Paint Tool.</summary>
    PaintTool,

    /// <summary>The Sledgehammer, held in both hands.</summary>
    Sledgehammer,

    /// <summary>The Connect Tool.</summary>
    ConnectTool,

    /// <summary>A potato weapon, aimed with both hands.</summary>
    PotatoBlaster,

    /// <summary>A water bucket.</summary>
    WaterBucket,

    /// <summary>Any other item (food, seeds, a part to place): held up, with no prop drawn.</summary>
    Item,
}

/// <summary>
/// A one-shot arm movement: what a player is doing with what they hold. Sent with a counter, so the same
/// gesture twice in a row still plays twice.
/// </summary>
public enum CharacterGesture : byte
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Hammer strike (placing and removing parts).</summary>
    HammerSwing,

    /// <summary>Two-handed Sledgehammer swing.</summary>
    SledgeSwing,

    /// <summary>Pointing a tool (Lift, Connect Tool).</summary>
    ToolUse,

    /// <summary>Welding.</summary>
    Weld,

    /// <summary>Spraying paint.</summary>
    PaintSpray,

    /// <summary>Firing a potato weapon.</summary>
    FireGun,

    /// <summary>Eating.</summary>
    Eat,

    /// <summary>Throwing.</summary>
    Throw,

    /// <summary>Planting a seed.</summary>
    Plant,

    /// <summary>Pouring water.</summary>
    Pour,

    /// <summary>Pressing or opening something.</summary>
    Interact,

    /// <summary>Flinching from a hit.</summary>
    Hurt,
}

/// <summary>
/// The humanoid skeleton every player character shares, and the animation clips that drive it. A character
/// (and later a skin) is only a mesh skinned to these bones, so every one moves the same way and they are
/// functionally identical. The C# here is the contract; the art follows it (<c>tools/characters/</c>).
/// </summary>
public static class CharacterRig
{
    /// <summary>Every bone a character's skeleton must have, parents before children.</summary>
    public static IReadOnlyList<string> Bones { get; } =
    [
        "root", "hips", "spine", "chest", "neck", "head",
        "shoulder_l", "upper_arm_l", "forearm_l", "hand_l", "fingers_l", "thumb_l",
        "shoulder_r", "upper_arm_r", "forearm_r", "hand_r", "fingers_r", "thumb_r",
        "thigh_l", "shin_l", "foot_l", "toe_l",
        "thigh_r", "shin_r", "foot_r", "toe_r",
    ];

    /// <summary>
    /// Bones the upper-body layer (hold poses and gestures) moves. Everything else follows the locomotion, so
    /// a player can walk while swinging a hammer. Extra bones under these (a braid) count as upper body too.
    /// </summary>
    public static IReadOnlySet<string> UpperBodyBones { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "spine", "chest", "neck", "head",
        "shoulder_l", "upper_arm_l", "forearm_l", "hand_l", "fingers_l", "thumb_l",
        "shoulder_r", "upper_arm_r", "forearm_r", "hand_r", "fingers_r", "thumb_r",
    };

    /// <summary>The bone props are held by.</summary>
    public const string PropBone = "hand_r";

    /// <summary>Clips that play on the whole body.</summary>
    public static IReadOnlyList<string> FullBodyClips { get; } =
    [
        "idle", "walk", "jog", "run", "crouch_idle", "crouch_walk", "jump", "fall", "land", "swim_idle", "swim", "sit",
    ];

    /// <summary>Clips that move only <see cref="UpperBodyBones"/>.</summary>
    public static IReadOnlyList<string> UpperBodyClips { get; } =
    [
        "hold_tool", "hold_heavy", "hold_gun", "hold_item",
        "hammer_swing", "sledge_swing", "tool_use", "weld", "paint_spray", "fire_gun", "eat", "throw", "plant", "pour",
        "interact", "hurt",
    ];

    /// <summary>Every clip the animation set must have.</summary>
    public static IEnumerable<string> Clips => FullBodyClips.Concat(UpperBodyClips);

    /// <summary>The arm pose for holding <paramref name="prop"/>, or null for empty hands.</summary>
    public static string? HoldClip(CharacterProp prop) => prop switch
    {
        CharacterProp.None => null,
        CharacterProp.Sledgehammer => "hold_heavy",
        CharacterProp.PotatoBlaster => "hold_gun",
        CharacterProp.WaterBucket or CharacterProp.Item => "hold_item",
        _ => "hold_tool",
    };

    /// <summary>The clip a gesture plays, or null for <see cref="CharacterGesture.None"/> and unknown values.</summary>
    public static string? GestureClip(CharacterGesture gesture) => gesture switch
    {
        CharacterGesture.HammerSwing => "hammer_swing",
        CharacterGesture.SledgeSwing => "sledge_swing",
        CharacterGesture.ToolUse => "tool_use",
        CharacterGesture.Weld => "weld",
        CharacterGesture.PaintSpray => "paint_spray",
        CharacterGesture.FireGun => "fire_gun",
        CharacterGesture.Eat => "eat",
        CharacterGesture.Throw => "throw",
        CharacterGesture.Plant => "plant",
        CharacterGesture.Pour => "pour",
        CharacterGesture.Interact => "interact",
        CharacterGesture.Hurt => "hurt",
        _ => null,
    };

    /// <summary>
    /// The node in the hand-tools model drawn for <paramref name="prop"/>, or null when nothing is drawn
    /// (empty hands, or an item without a prop of its own).
    /// </summary>
    public static string? PropNode(CharacterProp prop) => prop switch
    {
        CharacterProp.Hammer => "hammer",
        CharacterProp.LiftRemote => "lift_remote",
        CharacterProp.WeldTool => "weld_tool",
        CharacterProp.PaintTool => "paint_tool",
        CharacterProp.Sledgehammer => "sledgehammer",
        CharacterProp.ConnectTool => "connect_tool",
        CharacterProp.PotatoBlaster => "potato_blaster",
        CharacterProp.WaterBucket => "water_bucket",
        _ => null,
    };

    /// <summary><paramref name="prop"/> when it is a known value, otherwise <see cref="CharacterProp.None"/>.</summary>
    public static CharacterProp Sanitize(CharacterProp prop) => prop <= CharacterProp.Item ? prop : CharacterProp.None;

    /// <summary><paramref name="gesture"/> when it is a known value, otherwise <see cref="CharacterGesture.None"/>.</summary>
    public static CharacterGesture Sanitize(CharacterGesture gesture) => gesture <= CharacterGesture.Hurt ? gesture : CharacterGesture.None;
}
