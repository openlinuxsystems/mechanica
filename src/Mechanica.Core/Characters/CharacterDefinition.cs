using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Characters;

/// <summary>
/// A player character's look as written in a mod's <c>characters/*.json</c>: a mesh skinned to the shared
/// <see cref="CharacterRig"/> and the sounds it makes. Everything a character does (its size, movement and
/// animations) is the same for every character, so a new look, such as a skin, is only a new file.
/// </summary>
public sealed record CharacterDefinition
{
    /// <summary>Lowercase <c>snake_case</c> id, unique in the mod.</summary>
    public string? Id { get; init; }

    /// <summary>Display name in the character picker.</summary>
    public string? Name { get; init; }

    /// <summary>One line for the picker.</summary>
    public string? Description { get; init; }

    /// <summary>The skinned model (<c>.glb</c>/<c>.gltf</c>), relative to the mod root or <c>other_mod:path</c>.</summary>
    public string? Mesh { get; init; }

    /// <summary>Where the picker lists it: lower first, then by id. The first one is the default.</summary>
    public int? Order { get; init; }

    /// <summary>
    /// True for a character that only stands in the world at a POI's <c>npc</c> marker (the farmer): it is never on the
    /// picker and no player can play as it.
    /// </summary>
    public bool? Npc { get; init; }

    /// <summary>
    /// Sound events for the names in <see cref="CharacterSounds.Names"/>, replacing their defaults. A bare
    /// id means this mod's event when it has one, otherwise vanilla's.
    /// </summary>
    public Dictionary<string, string>? Sounds { get; init; }

    /// <summary>Keys this build does not know, kept so tools can round-trip the file.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>The sounds a character makes, by local name, and the vanilla event each plays unless the character binds another.</summary>
public static class CharacterSounds
{
    /// <summary>A step on ground, or anything without its own step sound.</summary>
    public const string Footstep = "footstep";

    /// <summary>A step on a metal part.</summary>
    public const string FootstepMetal = "footstep_metal";

    /// <summary>A step on a wooden part.</summary>
    public const string FootstepWood = "footstep_wood";

    /// <summary>A step in shallow water.</summary>
    public const string FootstepWater = "footstep_water";

    /// <summary>Leaving the ground.</summary>
    public const string Jump = "jump";

    /// <summary>Landing.</summary>
    public const string Land = "land";

    /// <summary>Landing from a long fall.</summary>
    public const string LandHard = "land_hard";

    /// <summary>A swimming stroke.</summary>
    public const string SwimStroke = "swim_stroke";

    /// <summary>Going into deep water.</summary>
    public const string WaterEnter = "water_enter";

    /// <summary>Coming out of deep water.</summary>
    public const string WaterExit = "water_exit";

    /// <summary>Taking a different tool or item in hand.</summary>
    public const string ToolEquip = "tool_equip";

    /// <summary>Water pouring into a bucket being filled.</summary>
    public const string BucketFill = "bucket_fill";

    /// <summary>Every name and the event it plays by default.</summary>
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Footstep] = "footstep_default",
        [FootstepMetal] = "footstep_metal",
        [FootstepWood] = "footstep_wood",
        [FootstepWater] = "footstep_water",
        [Jump] = "player_jump",
        [Land] = "player_land",
        [LandHard] = "player_land_hard",
        [SwimStroke] = "swim_stroke",
        [WaterEnter] = "water_enter",
        [WaterExit] = "water_exit",
        [ToolEquip] = "tool_equip",
        [BucketFill] = "bucket_fill",
    };

    /// <summary>Every local sound name.</summary>
    public static IEnumerable<string> Names => Defaults.Keys;

    /// <summary>The footstep sound for a surface's sound material (<c>metal</c>, <c>wood</c>, …), or the plain footstep.</summary>
    public static string FootstepFor(string? soundMaterial) => soundMaterial switch
    {
        "metal" or "scrap" => FootstepMetal,
        "wood" => FootstepWood,
        _ => Footstep,
    };
}
