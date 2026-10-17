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

    /// <summary>How far the bot notices players and crops, in m.</summary>
    public float? Sight { get; init; }

    /// <summary>Body size: a capsule standing on the bot's feet.</summary>
    public BotSize? Size { get; init; }

    /// <summary>Base colour of the placeholder body, <c>"#rrggbb"</c>.</summary>
    public string? Color { get; init; }

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

/// <summary>An item a bot may drop when it dies.</summary>
public sealed record BotDropDefinition
{
    /// <summary>Item id, <c>id</c> or <c>mod_id:id</c>.</summary>
    public string? Item { get; init; }

    /// <summary>Chance from 0 to 1 that it drops. Omitted means always.</summary>
    public float? Chance { get; init; }

    /// <summary>How many drop when it does. Omitted means 1.</summary>
    public int? Count { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
