using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;

namespace Mechanica.Core.Survival;

/// <summary>How a world plays (D13). The host picks it; every client is told.</summary>
public enum GameMode
{
    /// <summary>Unlimited items, free building, and players take no damage.</summary>
    Creative,

    /// <summary>Health, food, farming, paid building and death.</summary>
    Survival,
}

/// <summary>How hard a Survival world is (UI spec §14 New Game). The host picks it when the world is made.</summary>
public enum Difficulty
{
    /// <summary>Gentler bots and smaller, rarer raids.</summary>
    Easy,

    /// <summary>The game as balanced.</summary>
    Normal,
}

/// <summary>What a <see cref="Difficulty"/> changes, as multipliers on the normal numbers.</summary>
public sealed record DifficultySettings
{
    /// <summary>Damage bots do to players.</summary>
    public float BotDamage { get; init; } = 1f;

    /// <summary>A raid's budget (how many bots it sends).</summary>
    public float RaidBudget { get; init; } = 1f;

    /// <summary>The wait after a raid before the next can start.</summary>
    public float RaidCooldown { get; init; } = 1f;

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate(string name)
    {
        SurvivalSettings.Positive(BotDamage, $"difficulties.{name}.bot_damage");
        SurvivalSettings.Positive(RaidBudget, $"difficulties.{name}.raid_budget");
        SurvivalSettings.Positive(RaidCooldown, $"difficulties.{name}.raid_cooldown");
    }
}

/// <summary>An item and how many, in survival data.</summary>
/// <param name="Item">Item id (<c>carrot_seed</c>, or <c>mod_id:item</c>).</param>
/// <param name="Count">How many (1 or more).</param>
public sealed record ItemCount(string Item, int Count);

/// <summary>
/// The survival loop's numbers (chunk 20), in SI units. The game loads them from
/// <c>game/data/survival.json</c> (balance numbers live in data); the defaults only fill fields the file
/// leaves out. Item numbers (how much a carrot heals, how hard a blaster hits) live in each item's own
/// part data instead.
/// </summary>
public sealed record SurvivalSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>A player's full health. Health is the only survival meter (D12).</summary>
    public float MaxHealth { get; init; } = 100f;

    /// <summary>Seconds a dead player waits before they may respawn.</summary>
    public float RespawnDelay { get; init; } = 5f;

    /// <summary>Share of full health below which the HUD warns (and <c>heartbeat_low</c> plays, with chunk 16).</summary>
    public float LowHealth { get; init; } = 0.25f;

    /// <summary>How far from a player's eyes soil, beds and water can be used, in metres.</summary>
    public float Reach { get; init; } = 7.5f;

    /// <summary>How far from a player's eyes items on the ground can be picked up, in metres.</summary>
    public float PickupReach { get; init; } = 3f;

    /// <summary>Seconds items lie on the ground before they are gone.</summary>
    public float DropLifetime { get; init; } = 900f;

    /// <summary>Most piles of items on the ground at once; the oldest goes first.</summary>
    public int MaxDrops { get; init; } = 256;

    /// <summary>Whether a dead player's inventory falls where they died.</summary>
    public bool DropInventoryOnDeath { get; init; } = true;

    /// <summary>
    /// The model a knocked-out player's items lie in (<see cref="ItemDrop.KnockedOut"/>): a mod path
    /// (<c>mod:path</c>, relative to the mod root).
    /// </summary>
    public string KnockoutBag { get; init; } = "vanilla:meshes/knockout_bag.glb";

    /// <summary>Most degrees a shot may point away from where the host sees the shooter looking (network lag).</summary>
    public float AimTolerance { get; init; } = 25f;

    /// <summary>The empty bucket <c>CollectWater</c> fills (every one the player carries, in one dip).</summary>
    public string BucketItem { get; init; } = "water_bucket";

    /// <summary>The full bucket filling one at water turns it into (in the same slot).</summary>
    public string WaterItem { get; init; } = "water_bucket_full";

    /// <summary>Shortest time between one player's fills at water, in seconds.</summary>
    public float WaterSeconds { get; init; } = 1f;

    /// <summary>Seconds Use (E) is held on a resource node (a tree, a rock node) to refine it (UI spec §9; Scrap Mechanic's hand-refine time).</summary>
    public float RefineSeconds { get; init; } = 5.2f;

    /// <summary>How far, in metres, a player may move while refining before it is cancelled (UI spec §10).</summary>
    public float RefineCancelDistance { get; init; } = 1.5f;

    /// <summary>Items each player starts a Survival world with.</summary>
    public IReadOnlyList<ItemCount> StartingItems { get; init; } = [];

    /// <summary>How hard a hit throws a player about, by the health it takes (<see cref="HitSettings"/>).</summary>
    public HitSettings Hits { get; init; } = new();

    /// <summary>Fall damage.</summary>
    public FallSettings Fall { get; init; } = new();

    /// <summary>What each difficulty changes, by name (<c>easy</c>, <c>normal</c>); a missing one changes nothing.</summary>
    public IReadOnlyDictionary<string, DifficultySettings> Difficulties { get; init; } = new Dictionary<string, DifficultySettings>();

    /// <summary>The multipliers for <paramref name="difficulty"/>.</summary>
    public DifficultySettings For(Difficulty difficulty) =>
        Difficulties.TryGetValue(difficulty.ToString().ToLowerInvariant(), out var d) ? d : new DifficultySettings();

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static SurvivalSettings FromJson(string json)
    {
        SurvivalSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<SurvivalSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid survival settings JSON: {e.Message}", e);
        }
        if (settings is null)
            throw new FormatException("Survival settings JSON is null.");
        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        Positive(MaxHealth, "max_health");
        NonNegative(RespawnDelay, "respawn_delay");
        if (!(LowHealth is >= 0f and < 1f))
            throw new FormatException($"low_health must be 0 or more and under 1 (got {LowHealth}).");
        Positive(Reach, "reach");
        Positive(PickupReach, "pickup_reach");
        Positive(DropLifetime, "drop_lifetime");
        if (MaxDrops < 1)
            throw new FormatException($"max_drops must be 1 or more (got {MaxDrops}).");
        if (!AssetPath.TryParse(KnockoutBag, Identifiers.VanillaModId, AssetKind.Mesh, out _, out var bagError))
            throw new FormatException($"knockout_bag: {bagError}");
        if (!(AimTolerance is > 0f and <= 180f))
            throw new FormatException($"aim_tolerance must be more than 0 and at most 180 degrees (got {AimTolerance}).");
        if (!Data.Identifiers.IsValid(WaterItem.Split(':')[^1]))
            throw new FormatException($"water_item '{WaterItem}' is not an item id.");
        if (!Data.Identifiers.IsValid(BucketItem.Split(':')[^1]) || BucketItem == WaterItem)
            throw new FormatException($"bucket_item '{BucketItem}' must be an item id other than water_item.");
        NonNegative(WaterSeconds, "water_seconds");
        Positive(RefineSeconds, "refine_seconds");
        Positive(RefineCancelDistance, "refine_cancel_distance");
        foreach (var start in StartingItems)
        {
            if (start.Count < 1)
                throw new FormatException($"starting_items: count of '{start.Item}' must be 1 or more.");
        }
        Fall.Validate();
        Hits.Validate();
        foreach (var (name, d) in Difficulties)
        {
            if (!Enum.TryParse<Difficulty>(name, ignoreCase: true, out _) || name != name.ToLowerInvariant())
                throw new FormatException($"difficulties: '{name}' is not a difficulty (easy or normal).");
            d.Validate(name);
        }
    }

    internal static void Positive(float value, string name)
    {
        if (!(value > 0f) || !float.IsFinite(value))
            throw new FormatException($"{name} must be greater than 0 (got {value}).");
    }

    internal static void NonNegative(float value, string name)
    {
        if (!(value >= 0f) || !float.IsFinite(value))
            throw new FormatException($"{name} must be 0 or more (got {value}).");
    }
}

/// <summary>
/// How a blow throws the player it hurts about (<c>hits</c> in <c>survival.json</c>): by the health it takes. Lasting
/// harm (fire, a fall, toxic water) makes no reaction.
/// </summary>
public sealed record HitSettings
{
    /// <summary>Health a blow must take to make the player flinch from it.</summary>
    public float Light { get; init; } = 1f;

    /// <summary>Health from which it knocks them back a couple of steps.</summary>
    public float Heavy { get; init; } = 25f;

    /// <summary>Health from which it throws them off their feet.</summary>
    public float Knockdown { get; init; } = 50f;

    /// <summary>The causes of harm that are blows (<see cref="SurvivalActions.Damage"/>'s cause); anything else makes no reaction.</summary>
    public IReadOnlyList<string> Causes { get; init; } = ["bot", "sledge", "weapon", "explosion"];

    /// <summary>The reaction to a blow of <paramref name="cause"/> taking <paramref name="damage"/> health.</summary>
    public HitReaction ReactionTo(string cause, float damage) =>
        !Causes.Contains(cause) || !(damage >= Light) ? HitReaction.None
        : damage >= Knockdown ? HitReaction.Knockdown
        : damage >= Heavy ? HitReaction.Heavy
        : HitReaction.Light;

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        SurvivalSettings.Positive(Light, "hits.light");
        if (!(Heavy >= Light) || !(Knockdown >= Heavy) || !float.IsFinite(Knockdown))
            throw new FormatException($"hits must rise from light to heavy to knockdown (got {Light}, {Heavy}, {Knockdown}).");
        if (Causes is null || Causes.Any(string.IsNullOrWhiteSpace))
            throw new FormatException("hits.causes must be cause names.");
    }
}

/// <summary>Fall damage: landing faster than <see cref="MinSpeed"/> hurts, more the faster.</summary>
public sealed record FallSettings
{
    /// <summary>Landing speed (m/s, straight down) that starts to hurt.</summary>
    public float MinSpeed { get; init; } = 11f;

    /// <summary>Health lost per m/s above <see cref="MinSpeed"/>.</summary>
    public float DamagePerSpeed { get; init; } = 8f;

    /// <summary>Health a landing at <paramref name="speed"/> m/s takes.</summary>
    public float DamageAt(float speed) => speed > MinSpeed ? (speed - MinSpeed) * DamagePerSpeed : 0f;

    /// <summary>Throws <see cref="FormatException"/> naming the first out-of-range value.</summary>
    public void Validate()
    {
        SurvivalSettings.NonNegative(MinSpeed, "fall.min_speed");
        SurvivalSettings.NonNegative(DamagePerSpeed, "fall.damage_per_speed");
    }
}
