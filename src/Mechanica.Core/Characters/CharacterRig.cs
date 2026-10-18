namespace Mechanica.Core.Characters;

/// <summary>
/// What a player holds, as far as how their character looks: the prop in the right hand and the arm
/// pose that goes with it. Sent in every input and snapshot, so it is one byte.
/// </summary>
public enum CharacterProp : byte
{
    /// <summary>Empty hands.</summary>
    None,

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

    /// <summary>An empty water bucket, held by the rim with its handle lowered.</summary>
    WaterBucket,

    /// <summary>Any other item (food, seeds, a part to place): held up, with no prop drawn.</summary>
    Item,

    /// <summary>
    /// The Refine Tool: never in the inventory or on the hotbar, it is drawn from the hip while Use is held on a
    /// resource node, worked against it and put away again.
    /// </summary>
    RefineTool,

    /// <summary>A full water bucket, carried by its raised handle.</summary>
    WaterBucketFull,

    /// <summary>A Soil Bag, carried flat in front in both hands, a fist round each end.</summary>
    SoilBag,
}

/// <summary>
/// A one-shot arm movement: what a player is doing with what they hold. Sent with a counter, so the same
/// gesture twice in a row still plays twice.
/// </summary>
public enum CharacterGesture : byte
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Two-handed Sledgehammer swing.</summary>
    SledgeSwing,

    /// <summary>Pointing a tool (the Lift).</summary>
    ToolUse,

    /// <summary>The Weld Tool welding: pushed in and held while its clamping arms close and its tip spins and glows.</summary>
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

    /// <summary>Dipping an empty bucket into water and lifting it out full.</summary>
    BucketFill,

    /// <summary>Throwing the water out of a full bucket.</summary>
    BucketThrow,

    /// <summary>Setting an empty bucket down (Force Build).</summary>
    BucketPlace,

    /// <summary>Setting a full bucket down (Force Build).</summary>
    BucketPlaceFull,

    /// <summary>The Connect Tool's click as a link is made or taken away: a flick of the wrist and a pulse of its lights.</summary>
    ConnectClick,

    /// <summary>The Connect Tool reversing a bearing: a twist of the wrist and its lights pulsing the other way.</summary>
    ConnectReverse,

    /// <summary>Tipping a Soil Bag forward and shaking the soil out of it.</summary>
    BagSpill,
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
    /// a player can walk while swinging the Sledgehammer. Extra bones under these (a braid) count as upper body too.
    /// </summary>
    public static IReadOnlySet<string> UpperBodyBones { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "spine", "chest", "neck", "head",
        "shoulder_l", "upper_arm_l", "forearm_l", "hand_l", "fingers_l", "thumb_l",
        "shoulder_r", "upper_arm_r", "forearm_r", "hand_r", "fingers_r", "thumb_r",
    };

    /// <summary>
    /// The bones first-person arms are made of: forearms and hands, what a player sees of their own body when looking
    /// ahead. Upper arms are left out; that close to the eye they would fill the sides of the view.
    /// </summary>
    public static IReadOnlySet<string> ArmBones { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "forearm_l", "hand_l", "fingers_l", "thumb_l",
        "forearm_r", "hand_r", "fingers_r", "thumb_r",
    };

    /// <summary>
    /// The triangles of a skinned mesh that belong to the arms: those whose three corners each follow an arm bone
    /// more than any other. <paramref name="indices"/> lists the corners of each triangle (null: the vertices in
    /// order), <paramref name="bones"/> and <paramref name="weights"/> give <paramref name="influences"/> bones per
    /// vertex, and <paramref name="isArm"/> says whether a bone index is an arm bone. Returns the kept corners.
    /// </summary>
    public static int[] ArmTriangles(int[]? indices, int vertexCount, int[] bones, float[] weights, int influences, Func<int, bool> isArm)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(weights);
        if (influences <= 0 || bones.Length < vertexCount * influences || weights.Length < vertexCount * influences)
            throw new ArgumentException("bones and weights need influences entries for every vertex.");
        var arm = new bool[vertexCount];
        for (var v = 0; v < vertexCount; v++)
        {
            var best = -1;
            var bestWeight = 0f;
            for (var i = 0; i < influences; i++)
            {
                if (weights[v * influences + i] > bestWeight)
                {
                    bestWeight = weights[v * influences + i];
                    best = bones[v * influences + i];
                }
            }
            arm[v] = best >= 0 && isArm(best);
        }
        var corners = indices ?? Enumerable.Range(0, vertexCount).ToArray();
        var kept = new List<int>();
        for (var t = 0; t + 2 < corners.Length; t += 3)
        {
            if (arm[corners[t]] && arm[corners[t + 1]] && arm[corners[t + 2]])
                kept.AddRange([corners[t], corners[t + 1], corners[t + 2]]);
        }
        return [.. kept];
    }

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
        "hold_tool", "hold_heavy", "hold_heavy_block", "hold_gun", "hold_item",
        "sledge_swing", "tool_use", "weld", "paint_spray", "fire_gun", "eat", "throw", "plant", "pour",
        "interact", "hurt",
        "hold_bucket", "hold_bucket_full", "bucket_fill", "bucket_throw", "bucket_place", "bucket_place_full",
        "sledge_swing_side", "sledge_swing_back",
        "hold_heavy_fp", "hold_heavy_block_fp", "sledge_swing_fp", "sledge_swing_side_fp", "sledge_swing_back_fp",
        "hold_refine", "refine_draw", "refine_stow", "hold_refine_fp", "refine_draw_fp", "refine_stow_fp",
        "hold_bucket_fp", "hold_bucket_full_fp", "bucket_fill_fp", "bucket_throw_fp", "bucket_place_fp", "bucket_place_full_fp",
        "hold_connect", "hold_connect_aim", "connect_draw", "connect_stow", "connect_click", "connect_reverse",
        "hold_connect_fp", "hold_connect_aim_fp", "connect_draw_fp", "connect_stow_fp", "connect_click_fp", "connect_reverse_fp",
        "hold_weld", "hold_weld_aim", "weld_draw", "weld_stow", "hold_weld_fp", "hold_weld_aim_fp", "weld_draw_fp",
        "weld_stow_fp", "weld_fp",
        "hold_bag", "bag_spill", "bag_lift", "bag_lower", "hold_bag_fp", "bag_spill_fp", "bag_lift_fp", "bag_lower_fp",
    ];

    /// <summary>
    /// Gestures with more than one clip, the first being the one <see cref="GestureClip(CharacterGesture)"/> names: the
    /// Sledgehammer swings overhead, as a forehand from the right or as a backhand from the left.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> GestureVariants { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["sledge_swing"] = ["sledge_swing", "sledge_swing_side", "sledge_swing_back"],
    };

    /// <summary>
    /// Ending of a clip's first-person version: the local player's arms play <c>name_fp</c> instead of <c>name</c>
    /// when the set has one, posed for the camera rather than for someone watching.
    /// </summary>
    public const string FirstPersonSuffix = "_fp";

    /// <summary>Every clip the animation set must have.</summary>
    public static IEnumerable<string> Clips => FullBodyClips.Concat(UpperBodyClips);

    /// <summary>
    /// The arm pose for holding <paramref name="prop"/>, or null for empty hands. <paramref name="guard"/> raises the
    /// tool: the Sledgehammer to block (right click held), the Connect Tool to aim at a node or drag a link, the Weld Tool
    /// to aim while a part is selected.
    /// </summary>
    public static string? HoldClip(CharacterProp prop, bool guard = false) => prop switch
    {
        CharacterProp.None => null,
        CharacterProp.Sledgehammer => guard ? "hold_heavy_block" : "hold_heavy",
        CharacterProp.ConnectTool => guard ? "hold_connect_aim" : "hold_connect",
        CharacterProp.WeldTool => guard ? "hold_weld_aim" : "hold_weld",
        CharacterProp.PotatoBlaster => "hold_gun",
        CharacterProp.WaterBucket => "hold_bucket",
        CharacterProp.WaterBucketFull => "hold_bucket_full",
        CharacterProp.Item => "hold_item",
        CharacterProp.RefineTool => "hold_refine",
        CharacterProp.SoilBag => "hold_bag",
        _ => "hold_tool",
    };

    /// <summary>
    /// The clip that brings <paramref name="prop"/> into the hand when it is taken, or null when it just appears: the
    /// Refine Tool, the Connect Tool and the Weld Tool are drawn from the hip, and a Soil Bag is lifted up from low down. Every peer plays it when it sees the prop change, so it
    /// costs no traffic.
    /// </summary>
    public static string? DrawClip(CharacterProp prop) => prop switch
    {
        CharacterProp.RefineTool => "refine_draw",
        CharacterProp.ConnectTool => "connect_draw",
        CharacterProp.WeldTool => "weld_draw",
        CharacterProp.SoilBag => "bag_lift",
        _ => null,
    };

    /// <summary>
    /// The clip that puts <paramref name="prop"/> away when the hand takes something else, or null: the Refine Tool, the
    /// Connect Tool and the Weld Tool go back to the hip and a Soil Bag is lowered, still drawn until the clip ends.
    /// </summary>
    public static string? StowClip(CharacterProp prop) => prop switch
    {
        CharacterProp.RefineTool => "refine_stow",
        CharacterProp.ConnectTool => "connect_stow",
        CharacterProp.WeldTool => "weld_stow",
        CharacterProp.SoilBag => "bag_lower",
        _ => null,
    };

    /// <summary>The clip a gesture plays, or null for <see cref="CharacterGesture.None"/> and unknown values.</summary>
    public static string? GestureClip(CharacterGesture gesture) => gesture switch
    {
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
        CharacterGesture.BucketFill => "bucket_fill",
        CharacterGesture.BucketThrow => "bucket_throw",
        CharacterGesture.BucketPlace => "bucket_place",
        CharacterGesture.BucketPlaceFull => "bucket_place_full",
        CharacterGesture.ConnectClick => "connect_click",
        CharacterGesture.ConnectReverse => "connect_reverse",
        CharacterGesture.BagSpill => "bag_spill",
        _ => null,
    };

    /// <summary>
    /// The clip the <paramref name="count"/>th gesture plays: for a gesture with <see cref="GestureVariants"/>, one of
    /// them at random, never the same twice running. The pick follows from the replicated gesture count alone, so every
    /// peer (and the player's own first-person arms) plays the same swing with no traffic of its own.
    /// </summary>
    public static string? GestureClip(CharacterGesture gesture, byte count)
    {
        if (GestureClip(gesture) is not { } clip || !GestureVariants.TryGetValue(clip, out var variants))
            return GestureClip(gesture);
        // Each step moves on by 1 to n-1 places, chosen by a hash of the step: a shuffle with no repeats.
        var pick = 0;
        for (var c = 1; c <= count; c++)
            pick = (pick + 1 + (int)(Mix((uint)c) % (uint)(variants.Length - 1))) % variants.Length;
        return variants[pick];
    }

    /// <summary>The gesture clip <paramref name="clip"/> stands in for: itself, or the first of its variants.</summary>
    public static string GestureOf(string clip)
    {
        foreach (var (name, variants) in GestureVariants)
        {
            if (Array.IndexOf(variants, clip) >= 0)
                return name;
        }
        return clip;
    }

    private static uint Mix(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352dU;
        x ^= x >> 15;
        x *= 0x846ca68bU;
        return x ^ (x >> 16);
    }

    /// <summary>
    /// The node in the hand-tools model drawn for <paramref name="prop"/>, or null when nothing is drawn
    /// (empty hands, or an item without a prop of its own).
    /// </summary>
    public static string? PropNode(CharacterProp prop) => prop switch
    {
        CharacterProp.LiftRemote => "lift_remote",
        CharacterProp.WeldTool => "weld_tool",
        CharacterProp.PaintTool => "paint_tool",
        CharacterProp.Sledgehammer => "sledgehammer",
        CharacterProp.ConnectTool => "connect_tool",
        CharacterProp.PotatoBlaster => "potato_blaster",
        CharacterProp.WaterBucket or CharacterProp.WaterBucketFull => "water_bucket",
        CharacterProp.RefineTool => "refine_tool",
        CharacterProp.SoilBag => "soil_bag",
        _ => null,
    };

    /// <summary><paramref name="prop"/> when it is a known value, otherwise <see cref="CharacterProp.None"/>.</summary>
    public static CharacterProp Sanitize(CharacterProp prop) => prop <= CharacterProp.SoilBag ? prop : CharacterProp.None;

    /// <summary><paramref name="gesture"/> when it is a known value, otherwise <see cref="CharacterGesture.None"/>.</summary>
    public static CharacterGesture Sanitize(CharacterGesture gesture) => gesture <= CharacterGesture.BagSpill ? gesture : CharacterGesture.None;
}
