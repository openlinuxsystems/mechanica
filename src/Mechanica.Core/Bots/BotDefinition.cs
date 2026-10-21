using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Bots;

/// <summary>
/// One bot type, loaded from <c>bots/**/*.json</c> in a mod (one bot per file, like parts). The numbers
/// (health, speed, attacks, drops) are data; what the bot does with them is Lua (D2): the bot's
/// <see cref="Behavior"/> tree runs in the built-in behaviour library, and an optional <see cref="Script"/>
/// adds hooks and custom tree leaves. Keys are snake_case.
/// <code>
/// {
///   "id": "scuttler_green",
///   "name": "Green Scuttler",
///   "family": "scuttler",
///   "health": 60,
///   "speed": 5,
///   "attacks": [ { "id": "whip", "kind": "melee", "damage": 15, "range": 1.6, "interval": 1.2 } ],
///   "drops": [ { "item": "circuit_board", "chance": 0.75 } ],
///   "behavior": { "type": "selector", "children": [ ... ] }
/// }
/// </code>
/// </summary>
public sealed record BotDefinition
{
    /// <summary>Id, unique within the mod (lowercase snake_case).</summary>
    public string? Id { get; init; }

    /// <summary>Display name.</summary>
    public string? Name { get; init; }

    /// <summary>A sentence about it, for the editor and wikis.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Family the bot belongs to (<c>scuttler</c> for every coloured Scuttler). Its sound events are
    /// <c>&lt;family&gt;_idle</c>, <c>_alert</c>, <c>_attack</c> and <c>_death</c>. Omitted means the id.
    /// </summary>
    public string? Family { get; init; }

    /// <summary>Full health.</summary>
    public float? Health { get; init; }

    /// <summary>Top walking speed on land, in m/s.</summary>
    public float? Speed { get; init; }

    /// <summary>Top speed in water, in m/s. Omitted means <see cref="Speed"/>.</summary>
    public float? WaterSpeed { get; init; }

    /// <summary>
    /// Swimmers: how far below the water's surface the middle of the bot swims, in m. A swimmer floats at that depth
    /// instead of walking on the bottom, and its model plays <c>swim</c>. Omitted means it walks along the bottom.
    /// </summary>
    public float? SwimDepth { get; init; }

    /// <summary>How far the bot notices players and crops, in m.</summary>
    public float? Sight { get; init; }

    /// <summary>Body size: a capsule standing on the bot's feet.</summary>
    public BotSize? Size { get; init; }

    /// <summary>Base colour of the placeholder body, <c>"#rrggbb"</c>.</summary>
    public string? Color { get; init; }

    /// <summary>Where it lives in the wild: the host spawns it near players there. Omitted means it only comes in raids and as a guard.</summary>
    public BotWildDefinition? Wild { get; init; }

    /// <summary>How players tame it by feeding it. Omitted means it cannot be tamed.</summary>
    public BotTameDefinition? Tame { get; init; }

    /// <summary>Its animated model. Omitted means the placeholder capsule in <see cref="Color"/>.</summary>
    public BotModelDefinition? Model { get; init; }

    /// <summary>Whether melee hits (the Sledgehammer) do nothing to it.</summary>
    public bool? MeleeImmune { get; init; }

    /// <summary>A passive creature (Grazer): never joins raids and never attacks first.</summary>
    public bool? Passive { get; init; }

    /// <summary>What one bot of this type costs from a raid's budget. Omitted means it never joins raids.</summary>
    public int? RaidCost { get; init; }

    /// <summary>Its attacks, which its behaviour picks from by id.</summary>
    public List<BotAttackDefinition>? Attacks { get; init; }

    /// <summary>Items it may leave when it dies, each rolled on its own.</summary>
    public List<BotDropDefinition>? Drops { get; init; }

    /// <summary>
    /// Behaviour tree: nested nodes with a <c>type</c> and, for composites, <c>children</c>. The built-in
    /// behaviour library (docs/bot-tutorial.md) runs it every think.
    /// </summary>
    public JsonObject? Behavior { get; init; }

    /// <summary>Optional Lua script (relative to the mod root, or <c>other_mod:path</c>) with hooks and custom leaves.</summary>
    public string? Script { get; init; }

    /// <summary>Free-form numbers and flags the bot's Lua reads as <c>properties</c>.</summary>
    public JsonObject? Properties { get; init; }

    /// <summary>Keys this build does not know. Kept so a round-trip never loses data.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>How players tame a bot: they feed it one of its foods, and enough feeds keep it (a domesticated animal).</summary>
public sealed record BotTameDefinition
{
    /// <summary>Items it eats from a player's hand (item ids or <c>mod_id:item_id</c>).</summary>
    public List<string>? Foods { get; init; }

    /// <summary>How many feeds tame it (default 3).</summary>
    public int? Feeds { get; init; }

    /// <summary>Unknown keys (warned about).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// Where a bot lives in the wild (docs/bot-tutorial.md, "Living in the wild"). In Survival the host keeps up to
/// <see cref="Count"/> of it near each player, in its habitat, and takes away the ones every player has left behind.
/// </summary>
public sealed record BotWildDefinition
{
    /// <summary><c>water</c> (lakes and the sea) or <c>land</c>.</summary>
    public string? Habitat { get; init; }

    /// <summary>How many live near each player.</summary>
    public int? Count { get; init; }

    /// <summary>The nearest it appears to a player, in m.</summary>
    public float? MinDistance { get; init; }

    /// <summary>The farthest it appears from a player, in m.</summary>
    public float? MaxDistance { get; init; }

    /// <summary>It goes away when every player is farther than this, in m.</summary>
    public float? DespawnDistance { get; init; }

    /// <summary>Water: how deep the water must be where it appears, in m.</summary>
    public float? MinDepth { get; init; }

    /// <summary>How far round the spot must be its habitat too (so its wander stays there), in m.</summary>
    public float? ClearRadius { get; init; }

    /// <summary>Seconds between tries to spawn more.</summary>
    public float? Interval { get; init; }

    /// <summary>Biome ids (world generation) it appears in. Omitted or empty means any.</summary>
    public List<string>? Biomes { get; init; }

    /// <summary><c>[min, max]</c>: how many appear together, a few metres apart. Omitted means one at a time.</summary>
    public List<int>? Herd { get; init; }

    /// <summary>World objects (or scenes) it is often found near.</summary>
    public BotWildNearDefinition? Near { get; init; }

    /// <summary>Chance (more than 0, at most 1) that a try to spawn more finds any. Omitted means 1; under 1 makes it rare.</summary>
    public float? Chance { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>World objects a wild bot is often found near (<see cref="BotWildDefinition.Near"/>).</summary>
public sealed record BotWildNearDefinition
{
    /// <summary>World object or scene ids (world generation).</summary>
    public List<string>? Objects { get; init; }

    /// <summary>How far from one it appears, in m.</summary>
    public float? Radius { get; init; }

    /// <summary>The share of spawns (0 to 1) that try near one first, when one is in reach.</summary>
    public float? Share { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A bot's capsule, standing on its feet.</summary>
public sealed record BotSize
{
    /// <summary>Capsule radius, in m.</summary>
    public float? Radius { get; init; }

    /// <summary>Total height, in m (at least twice the radius).</summary>
    public float? Height { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A bot's animated model (docs/bot-tutorial.md, "Models and animation"): a glTF whose clips are named after what the bot
/// does (<c>idle</c>, <c>patrol</c>, <c>chase</c>, <c>alert</c>, <c>hit</c>, <c>death</c>, <c>jump</c>, <c>fall</c>,
/// <c>land</c>, and one per attack, plus any <see cref="Idles"/>). Every peer picks the clip from what the host sends, so
/// they all play the same one (idles aside).
/// </summary>
public sealed record BotModelDefinition
{
    /// <summary>The <c>.glb</c> or <c>.gltf</c>, relative to the mod root (or <c>other_mod:path</c>). Its feet are at the origin, facing -Z.</summary>
    public string? Mesh { get; init; }

    /// <summary>
    /// Clips each attack plays, by attack id; one is picked per use, in turn. An attack left out plays the clip named
    /// after it.
    /// </summary>
    public Dictionary<string, List<string>>? Attacks { get; init; }

    /// <summary>
    /// Ground speed (m/s) at which each walking clip (<c>patrol</c>, <c>chase</c>) plays at normal speed, by clip name; the
    /// clip speeds up and slows down with the bot so its feet do not slide. A clip left out plays at normal speed.
    /// </summary>
    public Dictionary<string, float>? ClipSpeeds { get; init; }

    /// <summary>Colour of its glowing (emissive) materials, its eye and lamps, while calm, <c>"#rrggbb"</c>.</summary>
    public string? EyeCalm { get; init; }

    /// <summary>Colour of its glowing materials while engaged, <c>"#rrggbb"</c>.</summary>
    public string? EyeEngaged { get; init; }

    /// <summary>
    /// Effects to show on every peer, by attack id (when the bot uses it) or <c>death</c> (when it dies):
    /// <c>explosion</c> (fire, smoke, sparks and debris), <c>shockwave</c> (a ring that ripples over the ground), each as
    /// big as the attack's radius (a death's uses the bot's largest area attack, or its size), or <c>shot</c> (the
    /// model's hidden <c>Shot</c> node flies to what the attack hit).
    /// </summary>
    public Dictionary<string, string>? Effects { get; init; }

    /// <summary>
    /// Other looks of the same model (coats, colours), each a <c>.glb</c> or <c>.gltf</c> with the same clips. Each bot
    /// shows <see cref="Mesh"/> or one of these, picked by its id, so every peer shows the same one.
    /// </summary>
    public List<string>? Variants { get; init; }

    /// <summary>Nodes of the model shown only while the bot is kept (a tamed animal's collar); hidden on a wild one.</summary>
    public List<string>? KeptNodes { get; init; }

    /// <summary>
    /// Clips it plays now and then while it stands calm (grazing, looking round, mooing), one picked at random every
    /// <see cref="IdleEvery"/> seconds. Cosmetic: each peer picks its own.
    /// </summary>
    public List<BotIdleDefinition>? Idles { get; init; }

    /// <summary>The shortest and longest wait between <see cref="Idles"/>, in seconds, <c>[min, max]</c>. Omitted means [6, 14].</summary>
    public List<float>? IdleEvery { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A clip a bot model plays now and then while it stands (<see cref="BotModelDefinition.Idles"/>).</summary>
public sealed record BotIdleDefinition
{
    /// <summary>The clip.</summary>
    public string? Clip { get; init; }

    /// <summary>A sound event it plays with the clip (a moo), from the bot's mod. Omitted means none.</summary>
    public string? Sound { get; init; }

    /// <summary>How often it is picked against the others. Omitted means 1.</summary>
    public float? Weight { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>How an attack reaches what it hits.</summary>
public enum BotAttackKind
{
    /// <summary>Hits one target within <see cref="BotAttackDefinition.Range"/> of the bot's body.</summary>
    Melee,

    /// <summary>Hits one target within range that the bot can see (a projectile, resolved at once).</summary>
    Ranged,

    /// <summary>Hits everything within <see cref="BotAttackDefinition.Radius"/> of the bot (a stomp or a blast).</summary>
    Area,
}

/// <summary>One of a bot's attacks.</summary>
public sealed record BotAttackDefinition
{
    /// <summary>Id, unique within the bot.</summary>
    public string? Id { get; init; }

    /// <summary>How it reaches its target.</summary>
    public BotAttackKind? Kind { get; init; }

    /// <summary>Damage to a player (and to a part, unless <see cref="PartDamage"/> is given).</summary>
    public float? Damage { get; init; }

    /// <summary>Damage to a part. Omitted means <see cref="Damage"/>.</summary>
    public float? PartDamage { get; init; }

    /// <summary>
    /// Reach from the edge of the bot's body, in m. Melee and ranged need it; an area attack may use it to
    /// say how close it must get before it goes off (omitted means its radius).
    /// </summary>
    public float? Range { get; init; }

    /// <summary>Area attacks: radius of the blast around the bot, in m. Damage falls to 0 at the edge.</summary>
    public float? Radius { get; init; }

    /// <summary>Seconds between two uses.</summary>
    public float? Interval { get; init; }

    /// <summary>A lasting effect on players hit: damage per second for some seconds (pesticide).</summary>
    public BotDamageOverTime? DamageOverTime { get; init; }

    /// <summary>A lasting effect on players hit: they move slower for some seconds (bubble wrap).</summary>
    public BotSlow? Slow { get; init; }

    /// <summary>
    /// Damage to a world object (tree, rock, bush; D11) it is aimed at or, for an area attack, that stands in
    /// its radius. Omitted or 0 means it cannot hurt world objects.
    /// </summary>
    public float? ObjectDamage { get; init; }

    /// <summary>When true, using it kills the bot (a kamikaze charge).</summary>
    public bool? KillsSelf { get; init; }

    /// <summary>Whether it can hurt crops and parts. Omitted means true.</summary>
    public bool? HitsParts { get; init; }

    /// <summary>Sound event it plays. Omitted means <c>&lt;family&gt;_attack</c>.</summary>
    public string? Sound { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Damage dealt every second for a while after a hit.</summary>
public sealed record BotDamageOverTime
{
    /// <summary>Damage per second.</summary>
    public float? PerSecond { get; init; }

    /// <summary>How long it lasts, in s.</summary>
    public float? Seconds { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Players hit move slower for a while.</summary>
public sealed record BotSlow
{
    /// <summary>What their walking and running speed is multiplied by, more than 0 and less than 1.</summary>
    public float? Factor { get; init; }

    /// <summary>How long it lasts, in s.</summary>
    public float? Seconds { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>An item a bot may drop when it dies.</summary>
public sealed record BotDropDefinition
{
    /// <summary>Item id, <c>id</c> or <c>mod_id:id</c>.</summary>
    public string? Item { get; init; }

    /// <summary>Chance from 0 to 1 that it drops. Omitted means always.</summary>
    public float? Chance { get; init; }

    /// <summary>How many drop when it does. Omitted means 1.</summary>
    public int? Count { get; init; }

    /// <summary>The most that may drop: each time, a random count from <see cref="Count"/> to this. Omitted: always <see cref="Count"/>.</summary>
    public int? CountMax { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
