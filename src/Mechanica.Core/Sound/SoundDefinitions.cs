using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Sound;

/// <summary>Mixer bus a sound plays on, so players can turn music, ambience and effects up or down separately.</summary>
public enum SoundBus
{
    /// <summary>Effects in the world: building, parts, bots, items, the player.</summary>
    Sfx,

    /// <summary>Menu and inventory feedback. Not positional.</summary>
    Ui,

    /// <summary>Background ambience: wind, birds, water.</summary>
    Ambient,

    /// <summary>Music and radio. Not positional.</summary>
    Music,
}

/// <summary>How a positional sound gets quieter with distance beyond <see cref="SoundEventDefinition.MinDistance"/>.</summary>
public enum SoundRolloff
{
    /// <summary>Gain falls as <c>min / distance</c>: about 6 dB per doubling. Natural for most sounds.</summary>
    Inverse,

    /// <summary>Gain falls as <c>(min / distance)²</c>: about 12 dB per doubling. For small, close sounds.</summary>
    InverseSquare,

    /// <summary>Gain falls linearly from 1 at the minimum distance to 0 at the maximum. For large, steady sources.</summary>
    Linear,
}

/// <summary>
/// One file of sound events in a mod: <c>sounds/*.json</c> (chunk 16). A mod may split its events over
/// several files; ids are unique across all of them.
/// </summary>
public sealed record SoundBankFile
{
    /// <summary>The events, in the order the file lists them.</summary>
    public List<SoundEventDefinition>? Events { get; init; }

    /// <summary>Keys this build does not know. Kept so a round-trip never loses data.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A sound event: what plays when the game, a tool or a part's Lua script asks for an id such as
/// <c>place_wood</c> or <c>engine_idle</c>. Each play picks one of the <see cref="Clips"/> at random and
/// varies its pitch and volume a little, so repeats don't sound mechanical. A loop keeps playing until
/// stopped, and its <see cref="Parameters"/> (engine speed, thrust) bend its pitch and volume while it
/// runs. Positional sounds fade with distance (<see cref="MinDistance"/>, <see cref="MaxDistance"/>,
/// <see cref="Rolloff"/>). Omitted fields take the defaults in <see cref="SoundEvent"/>. Keys are snake_case.
/// </summary>
public sealed record SoundEventDefinition
{
    /// <summary>
    /// Id, lowercase snake_case (from CATALOG_IDS.md for vanilla). <c>other_mod:id</c> replaces that mod's
    /// event instead, which needs <c>other_mod</c> in this mod's dependencies.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>Optional note for modders: what the sound is for.</summary>
    public string? Description { get; init; }

    /// <summary>Audio files (<c>.ogg</c>/<c>.wav</c>), relative to the mod root or <c>other_mod:path</c>; one is picked per play.</summary>
    public List<string>? Clips { get; init; }

    /// <summary>Mixer bus. Omitted means <see cref="SoundBus.Sfx"/>.</summary>
    public SoundBus? Bus { get; init; }

    /// <summary>True for a sound that repeats until stopped. Omitted means false.</summary>
    public bool? Loop { get; init; }

    /// <summary>Base volume in dB (0 is the clip as recorded). Omitted means 0.</summary>
    public float? VolumeDb { get; init; }

    /// <summary>Base pitch scale (1 is as recorded, 2 is an octave up). Omitted means 1.</summary>
    public float? Pitch { get; init; }

    /// <summary>Each play shifts the volume by up to this many dB either way. Omitted means 0.</summary>
    public float? VolumeVariationDb { get; init; }

    /// <summary>Each play scales the pitch by up to this fraction either way (0.05 is ±5 %). Omitted means 0.</summary>
    public float? PitchVariation { get; init; }

    /// <summary>
    /// True for a sound heard from a place in the world, false for one heard the same everywhere.
    /// Omitted means true, except on the <see cref="SoundBus.Ui"/> and <see cref="SoundBus.Music"/> buses.
    /// </summary>
    public bool? Spatial { get; init; }

    /// <summary>Distance in metres within which a positional sound plays at full volume. Omitted means 1.</summary>
    public float? MinDistance { get; init; }

    /// <summary>Distance in metres beyond which a positional sound is not heard at all. Omitted means 40.</summary>
    public float? MaxDistance { get; init; }

    /// <summary>How a positional sound fades between the two distances. Omitted means <see cref="SoundRolloff.Inverse"/>.</summary>
    public SoundRolloff? Rolloff { get; init; }

    /// <summary>Live controls of a loop (engine speed, thrust), each bending its pitch and volume.</summary>
    public List<SoundParameter>? Parameters { get; init; }

    /// <summary>Most copies of this event playing at once; a new one-shot replaces the oldest. Omitted means 8.</summary>
    public int? MaxInstances { get; init; }

    /// <summary>Seconds after one play before the same event can start again; plays sooner are dropped. Omitted means 0.</summary>
    public float? Cooldown { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A live control of a looping sound, such as an engine's <c>speed</c>. Its value runs from
/// <see cref="Min"/> to <see cref="Max"/>; along that range the loop's pitch scale goes linearly from
/// <c>Pitch[0]</c> to <c>Pitch[1]</c> and its volume from <c>VolumeDb[0]</c> to <c>VolumeDb[1]</c>. Several
/// parameters multiply their pitch scales and add their volumes.
/// </summary>
public sealed record SoundParameter
{
    /// <summary>Name, lowercase snake_case, unique within the event. Scripts set it by this name.</summary>
    public string? Name { get; init; }

    /// <summary>Lowest value. Omitted means 0.</summary>
    public float? Min { get; init; }

    /// <summary>Highest value. Omitted means 1.</summary>
    public float? Max { get; init; }

    /// <summary>Value when the loop starts. Omitted means <see cref="Min"/>.</summary>
    public float? Default { get; init; }

    /// <summary>Pitch scale at the lowest and highest value, <c>[low, high]</c>. Omitted means no pitch change.</summary>
    public List<float>? Pitch { get; init; }

    /// <summary>Volume change in dB at the lowest and highest value, <c>[low, high]</c>. Omitted means no volume change.</summary>
    public List<float>? VolumeDb { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
