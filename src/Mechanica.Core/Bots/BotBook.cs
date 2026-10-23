using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Mechanica.Core.Data;

namespace Mechanica.Core.Bots;

/// <summary>A bot's attack with every default filled in.</summary>
/// <param name="Id">Id within the bot.</param>
/// <param name="Kind">How it reaches its target.</param>
/// <param name="Damage">Damage to players.</param>
/// <param name="PartDamage">Damage to parts.</param>
/// <param name="Range">Reach from the edge of the bot's body, in m.</param>
/// <param name="Radius">Blast radius of an area attack, in m (0 for the others).</param>
/// <param name="Interval">Seconds between uses.</param>
/// <param name="DamagePerSecond">Lasting damage per second on players hit (0 for none).</param>
/// <param name="DamageSeconds">How long the lasting damage lasts, in s.</param>
/// <param name="KillsSelf">Using it kills the bot.</param>
/// <param name="HitsParts">It can hurt crops and parts.</param>
/// <param name="Sound">Sound event it plays.</param>
/// <param name="Windup">Seconds from its start to when it lands (0: at once).</param>
/// <param name="Reaction">How a player it hits reacts, or null for the one its damage earns.</param>
public sealed record BotAttack(
    string Id, BotAttackKind Kind, float Damage, float PartDamage, float Range, float Radius, float Interval,
    float DamagePerSecond, float DamageSeconds, bool KillsSelf, bool HitsParts, string Sound,
    float SlowFactor = 1f, float SlowSeconds = 0f, float ObjectDamage = 0f, float Windup = 0f, Characters.HitReaction? Reaction = null)
{
    /// <summary>The longest wind-up an attack may have, in s.</summary>
    public const float MaxWindup = 3f;

    /// <summary>True when it slows players it hits.</summary>
    public bool Slows => SlowFactor < 1f && SlowSeconds > 0f;
}

/// <summary>An item a bot may drop, qualified.</summary>
/// <param name="Item">Qualified item id.</param>
/// <param name="Chance">Chance from 0 to 1.</param>
/// <param name="Count">How many, the least when <paramref name="CountMax"/> is more.</param>
/// <param name="CountMax">The most: each drop is a random count from <paramref name="Count"/> to this.</param>
public sealed record BotDrop(string Item, float Chance, int Count, int CountMax);

/// <summary>Where a bot lives in the wild, with every default filled in (<see cref="BotWildDefinition"/>).</summary>
/// <param name="Water">It lives in water (else on land).</param>
/// <param name="Count">How many live near each player.</param>
/// <param name="MinDistance">The nearest it appears to a player, m.</param>
/// <param name="MaxDistance">The farthest it appears from a player, m.</param>
/// <param name="DespawnDistance">It goes when every player is farther than this, m.</param>
/// <param name="MinDepth">Water: how deep the water must be where it appears, m.</param>
/// <param name="ClearRadius">How far round the spot must be its habitat too, m.</param>
/// <param name="Interval">Seconds between tries to spawn more.</param>
public sealed record BotWild(bool Water, int Count, float MinDistance, float MaxDistance, float DespawnDistance, float MinDepth, float ClearRadius,
    float Interval)
{
    /// <summary>Biome ids it appears in; empty means any.</summary>
    public IReadOnlySet<string> Biomes { get; init; } = new HashSet<string>();

    /// <summary>The fewest that appear together.</summary>
    public int HerdMin { get; init; } = 1;

    /// <summary>The most that appear together.</summary>
    public int HerdMax { get; init; } = 1;

    /// <summary>World objects it is often found near, or null.</summary>
    public BotWildNear? Near { get; init; }

    /// <summary>Chance (0 to 1) that a try to spawn more finds any: under 1 makes it rare.</summary>
    public float Chance { get; init; } = 1f;
}

/// <summary>World objects a wild bot is often found near, checked (<see cref="BotWildNearDefinition"/>).</summary>
/// <param name="Objects">World object or scene ids.</param>
/// <param name="Radius">How far from one it appears, m.</param>
/// <param name="Share">Share of spawns (0 to 1) that try near one first.</param>
public sealed record BotWildNear(IReadOnlySet<string> Objects, float Radius, float Share);

/// <summary>How players tame a bot by feeding it (<see cref="BotActions.FeedBot"/>).</summary>
/// <param name="Foods">Qualified item ids it eats from a player's hand.</param>
/// <param name="Feeds">How many feeds keep it.</param>
/// <param name="Lead">Qualified item that leads it once it is tame, or null when it cannot be led.</param>
/// <param name="Homes">Qualified part ids it makes its home once it is tame (empty for none).</param>
/// <param name="HomeRadius">How near a home part must be for it to move in, m.</param>
public sealed record BotTame(IReadOnlySet<string> Foods, int Feeds, string? Lead = null, IReadOnlySet<string>? Homes = null, float HomeRadius = 20f)
{
    /// <summary>Qualified part ids it makes its home (empty for none).</summary>
    public IReadOnlySet<string> Homes { get; init; } = Homes ?? new HashSet<string>(StringComparer.Ordinal);

    /// <summary>How two tame ones have young, or null when they never do.</summary>
    public BotBreed? Breed { get; init; }

    /// <summary>What a tame one gives once it has eaten enough from its home, or null when it gives nothing.</summary>
    public BotProduce? Produce { get; init; }
}

/// <summary>What a tame bot gives (<see cref="BotProduceDefinition"/>), with defaults filled in.</summary>
/// <param name="Item">Qualified item it gives.</param>
/// <param name="Feeds">Meals from its home before one is ready.</param>
/// <param name="Cooldown">Seconds after one is collected before the next can be ready.</param>
/// <param name="EatEvery">Seconds between two meals from its home.</param>
public sealed record BotProduce(string Item, int Feeds, float Cooldown, float EatEvery);

/// <summary>How two tame bots of a type have young (<see cref="BotBreedDefinition"/>), with defaults filled in.</summary>
/// <param name="Treat">Qualified item that puts one in the mood.</param>
/// <param name="Calf">Qualified bot type of the young.</param>
/// <param name="Seconds">Seconds from pairing to the birth.</param>
/// <param name="Cooldown">Seconds after pairing before either can pair again.</param>
/// <param name="Herd">Most tame ones round one home before they stop breeding.</param>
/// <param name="Radius">How near the two must be to pair, m.</param>
public sealed record BotBreed(string Treat, string Calf, float Seconds, float Cooldown, int Herd, float Radius);

/// <summary>What a young bot grows up into (<see cref="BotGrowDefinition"/>).</summary>
/// <param name="Into">Qualified bot type it becomes.</param>
/// <param name="Seconds">Seconds from birth until it grows up.</param>
public sealed record BotGrow(string Into, float Seconds);

/// <summary>How a bot model's head follows a player nearby (<see cref="BotTrackDefinition"/>), defaults filled in.</summary>
/// <param name="Nodes">Nodes that turn, with their shares of the turn, in data order (parents first).</param>
/// <param name="Yaw">Most it turns left or right, degrees.</param>
/// <param name="Pitch">Most it looks up or down, degrees.</param>
/// <param name="Range">How near a player must be while it is calm, m.</param>
public sealed record BotTrack(IReadOnlyList<KeyValuePair<string, float>> Nodes, float Yaw, float Pitch, float Range)
{
    /// <summary>The largest yaw a track may have, degrees.</summary>
    public const float MaxYaw = 170f;

    /// <summary>The largest pitch a track may have, degrees.</summary>
    public const float MaxPitch = 80f;

    /// <summary>The farthest a calm bot looks for a player, m.</summary>
    public const float MaxRange = 30f;
}

/// <summary>A bot's model with its path qualified and every default filled in.</summary>
/// <param name="Mesh">Qualified glTF path (<c>mod:meshes/bots/x.glb</c>).</param>
/// <param name="Attacks">Clips each attack plays, by attack id (every attack has at least one).</param>
/// <param name="ClipSpeeds">Ground speed (m/s) each walking clip plays at normal speed, by clip name.</param>
/// <param name="EyeCalm">Eye colour while calm, <c>"#rrggbb"</c>.</param>
/// <param name="EyeEngaged">Eye colour while engaged, <c>"#rrggbb"</c>.</param>
/// <param name="Effects">Effect kind (<see cref="BotEffects"/>) by attack id or <c>death</c>.</param>
public sealed record BotModel(string Mesh, IReadOnlyDictionary<string, IReadOnlyList<string>> Attacks, IReadOnlyDictionary<string, float> ClipSpeeds,
    string EyeCalm, string EyeEngaged, IReadOnlyDictionary<string, string> Effects)
{
    /// <summary>Qualified paths of its other looks (<see cref="BotModelDefinition.Variants"/>).</summary>
    public IReadOnlyList<string> Variants { get; init; } = [];

    /// <summary>Clips it plays now and then while it stands calm.</summary>
    public IReadOnlyList<BotIdle> Idles { get; init; } = [];

    /// <summary>Nodes shown only while the bot is kept (<see cref="BotModelDefinition.KeptNodes"/>).</summary>
    public IReadOnlyList<string> KeptNodes { get; init; } = [];

    /// <summary>The node a Lead's rope clips on to, or null (<see cref="BotModelDefinition.LeadNode"/>).</summary>
    public string? LeadNode { get; init; }

    /// <summary>How its head follows a player nearby, or null (<see cref="BotModelDefinition.Track"/>).</summary>
    public BotTrack? Track { get; init; }

    /// <summary>Nodes that swing as it moves, by how loose each is (<see cref="BotModelDefinition.Sway"/>).</summary>
    public IReadOnlyDictionary<string, float> Sway { get; init; } = new Dictionary<string, float>();

    /// <summary>The loosest a swaying node may be.</summary>
    public const float MaxSway = 3f;

    /// <summary>The shortest wait between idles, in s.</summary>
    public float IdleMin { get; init; } = BotBook.DefaultIdleMin;

    /// <summary>The longest wait between idles, in s.</summary>
    public float IdleMax { get; init; } = BotBook.DefaultIdleMax;

    /// <summary>
    /// Which look bot <paramref name="botId"/> has: 0 for <see cref="Mesh"/>, n for <see cref="Variants"/>[n - 1].
    /// <paramref name="look"/> (one it was born with) wins when the model has it; otherwise its id picks, the same on every peer.
    /// </summary>
    public int LookOf(int botId, int? look = null) =>
        look is { } l && l >= 0 && l <= Variants.Count ? l : (int)((uint)botId % (uint)(Variants.Count + 1));

    /// <summary>The mesh bot <paramref name="botId"/> shows (<see cref="LookOf"/>).</summary>
    public string MeshFor(int botId, int? look = null)
    {
        var pick = LookOf(botId, look);
        return pick == 0 ? Mesh : Variants[pick - 1];
    }
}

/// <summary>A checked idle clip (<see cref="BotIdleDefinition"/>).</summary>
/// <param name="Clip">The clip.</param>
/// <param name="Sound">Sound event it plays, or null.</param>
/// <param name="Weight">How often it is picked against the others (above 0).</param>
public sealed record BotIdle(string Clip, string? Sound, float Weight);

/// <summary>The effects a bot model can show (<see cref="BotModelDefinition.Effects"/>).</summary>
public static class BotEffects
{
    /// <summary>Fire, smoke, sparks and debris thrown out to the radius.</summary>
    public const string Explosion = "explosion";

    /// <summary>A ring that ripples over the ground out to the radius.</summary>
    public const string Shockwave = "shockwave";

    /// <summary>
    /// A copy of the model's <c>Shot</c> node (hidden on the bot) flies from where it sits to what the attack hit, or
    /// straight ahead out to the attack's range when it hit nothing.
    /// </summary>
    public const string Shot = "shot";

    /// <summary>The model node a <see cref="Shot"/> effect flies.</summary>
    public const string ShotNode = "Shot";

    /// <summary>The key for the effect shown when the bot dies.</summary>
    public const string Death = "death";

    /// <summary>Every effect kind.</summary>
    public static readonly IReadOnlyList<string> Kinds = [Explosion, Shockwave, Shot];
}

/// <summary>A bot type that loaded cleanly, with every id qualified and every default filled in.</summary>
/// <param name="QualifiedId">Game-wide id, <c>mod_id:bot_id</c>.</param>
/// <param name="ModId">The mod that defines it (its script runs in that mod's Lua state).</param>
/// <param name="Name">Display name.</param>
/// <param name="Family">Sound family.</param>
/// <param name="Health">Full health.</param>
/// <param name="Speed">Top speed on land, m/s.</param>
/// <param name="WaterSpeed">Top speed in water, m/s.</param>
/// <param name="Sight">How far it notices things, m.</param>
/// <param name="Radius">Capsule radius, m.</param>
/// <param name="Height">Capsule height, m.</param>
/// <param name="Color">Placeholder body colour, <c>"#rrggbb"</c>.</param>
/// <param name="MeleeImmune">Melee hits do nothing.</param>
/// <param name="Passive">Never raids, never attacks first.</param>
/// <param name="RaidCost">Cost in a raid's budget, or null when it never raids.</param>
/// <param name="Attacks">Attacks by id, in data order.</param>
/// <param name="Drops">Possible drops.</param>
/// <param name="Script">Qualified script path (<c>mod:scripts/x.lua</c>), or null.</param>
/// <param name="Definition">The data as written.</param>
public sealed record Bot(
    string QualifiedId, string ModId, string Name, string Family, float Health, float Speed, float WaterSpeed, float Sight,
    float Radius, float Height, string Color, bool MeleeImmune, bool Passive, int? RaidCost,
    IReadOnlyList<BotAttack> Attacks, IReadOnlyList<BotDrop> Drops, string? Script, BotDefinition Definition)
{
    /// <summary>The attack with this id, or null.</summary>
    public BotAttack? Attack(string id) => Attacks.FirstOrDefault(a => a.Id == id);

    /// <summary>Sound event for <paramref name="what"/> (<c>idle</c>, <c>alert</c>, <c>attack</c>, <c>death</c>).</summary>
    public string Sound(string what) => $"{Family}_{what}";

    /// <summary>The behaviour tree, or null when the bot has none.</summary>
    public JsonObject? Behavior => Definition.Behavior;

    /// <summary>Where it lives in the wild, or null when it only comes in raids and as a guard.</summary>
    public BotWild? Wild { get; init; }

    /// <summary>How players tame it, or null when it cannot be tamed.</summary>
    public BotTame? Tame { get; init; }

    /// <summary>What it grows up into, or null when it never changes.</summary>
    public BotGrow? Grow { get; init; }

    /// <summary>How far below the water's surface its middle swims (m), or null when it walks on the bottom.</summary>
    public float? SwimDepth { get; init; }

    /// <summary>Its animated model, or null for the placeholder capsule.</summary>
    public BotModel? Model { get; init; }
}

/// <summary>Finds bot types by id.</summary>
public interface IBotCatalog
{
    /// <summary>The bot type with this id (a bare id means vanilla), or null.</summary>
    Bot? FindBot(string id);
}

/// <summary>
/// Every bot type of the enabled mods (chunk 21), read from each mod's <c>bots/**/*.json</c> in load order
/// and checked against the loaded items. A file with errors is left out and <see cref="Diagnostics"/>
/// says why. Host and clients compare <see cref="Fingerprint"/> on join.
/// </summary>
public sealed class BotBook : IBotCatalog
{
    /// <summary>Where a mod keeps its bots.</summary>
    public const string Folder = "bots";

    /// <summary>Default capsule radius, m.</summary>
    public const float DefaultRadius = 0.4f;

    /// <summary>Default capsule height, m.</summary>
    public const float DefaultHeight = 1.2f;

    /// <summary>Default sight, m.</summary>
    public const float DefaultSight = 30f;

    /// <summary>Default body colour.</summary>
    public const string DefaultColor = "#808080";

    /// <summary>Default eye colour of a calm bot.</summary>
    public const string DefaultEyeCalm = "#2bff3a";

    /// <summary>Default shortest wait between a model's idle clips, in s.</summary>
    public const float DefaultIdleMin = 6f;

    /// <summary>Default longest wait between a model's idle clips, in s.</summary>
    public const float DefaultIdleMax = 14f;

    /// <summary>Default eye colour of an engaged bot.</summary>
    public const string DefaultEyeEngaged = "#ff1a0a";

    /// <summary>Wild bots: the nearest they appear to a player when <c>min_distance</c> is left out, m.</summary>
    public const float DefaultWildMinDistance = 30f;

    /// <summary>Wild bots: the farthest they appear from a player when <c>max_distance</c> is left out, m.</summary>
    public const float DefaultWildMaxDistance = 70f;

    /// <summary>Wild bots: how far every player must be for one to go, when <c>despawn_distance</c> is left out, m.</summary>
    public const float DefaultWildDespawnDistance = 120f;

    /// <summary>Wild water bots: the least water depth where one appears, when <c>min_depth</c> is left out, m.</summary>
    public const float DefaultWildMinDepth = 1.5f;

    /// <summary>Wild bots: how far round the spot must be its habitat, when <c>clear_radius</c> is left out, m.</summary>
    public const float DefaultWildClearRadius = 8f;

    /// <summary>Wild bots: seconds between tries to spawn more, when <c>interval</c> is left out.</summary>
    public const float DefaultWildInterval = 8f;

    /// <summary>Default share of a wild bot's spawns that try near its <see cref="BotWild.Near"/> objects first.</summary>
    public const float DefaultWildNearShare = 0.5f;

    /// <summary>Feeds that tame a bot when its <c>tame</c> leaves <c>feeds</c> out.</summary>
    public const int DefaultTameFeeds = 3;

    /// <summary>How near a home part must be for a tame bot to move in when its data leaves <c>home_radius</c> out, m.</summary>
    public const float DefaultHomeRadius = 20f;

    /// <summary>Seconds from pairing to the birth when a breed does not say.</summary>
    public const float DefaultBreedSeconds = 60f;

    /// <summary>Seconds before a parent can pair again when a breed does not say.</summary>
    public const float DefaultBreedCooldown = 600f;

    /// <summary>Most tame ones round a home before they stop breeding, when a breed does not say.</summary>
    public const int DefaultBreedHerd = 8;

    /// <summary>How near two must be to pair, m, when a breed does not say.</summary>
    public const float DefaultBreedRadius = 8f;

    /// <summary>Meals before a tame bot's produce is ready, when its data does not say.</summary>
    public const int DefaultProduceFeeds = 5;

    /// <summary>Seconds after a tame bot's produce is collected before the next can be ready, when its data does not say.</summary>
    public const float DefaultProduceCooldown = 300f;

    /// <summary>Seconds between two of a tame bot's meals from its home, when its data does not say.</summary>
    public const float DefaultProduceEatEvery = 30f;

    /// <summary>Deepest behaviour tree accepted.</summary>
    public const int MaxBehaviorDepth = 24;

    /// <summary>Most nodes in one behaviour tree.</summary>
    public const int MaxBehaviorNodes = 256;

    private readonly Dictionary<string, Bot> _bots;

    private BotBook(IEnumerable<Bot> bots, IReadOnlyList<DataDiagnostic> diagnostics)
    {
        _bots = bots.ToDictionary(b => b.QualifiedId, StringComparer.Ordinal);
        All = [.. _bots.Values.OrderBy(b => b.QualifiedId, StringComparer.Ordinal)];
        Diagnostics = diagnostics;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var bot in All)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(bot.QualifiedId));
            hash.AppendData(Encoding.UTF8.GetBytes(DataJson.Write(bot.Definition)));
        }
        Fingerprint = Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>A book with no bots.</summary>
    public static BotBook Empty { get; } = new([], []);

    /// <summary>Every bot type, ordered by qualified id.</summary>
    public IReadOnlyList<Bot> All { get; }

    /// <summary>Every problem found while loading.</summary>
    public IReadOnlyList<DataDiagnostic> Diagnostics { get; }

    /// <summary>True when any diagnostic is an error.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>SHA-256 (hex) of every bot's id and canonical form.</summary>
    public string Fingerprint { get; }

    /// <inheritdoc/>
    public Bot? FindBot(string id) => _bots.GetValueOrDefault(Identifiers.Qualify(id));

    /// <summary>
    /// Loads the bots of the mods in <paramref name="content"/>, with other mods' overrides, patches and removals
    /// (<c>overrides/&lt;mod&gt;/bots/</c>, see <see cref="ContentLayers"/>) applied in load order.
    /// </summary>
    public static BotBook Load(ModLoadResult content)
    {
        var diagnostics = new List<DataDiagnostic>();
        var bots = new Dictionary<string, Bot>(StringComparer.Ordinal);
        var directories = content.Mods.ToDictionary(m => m.Id, m => m.Directory, StringComparer.Ordinal);
        foreach (var entry in ContentLayers.Collect(content, Folder, ContentLayers.IdMember, diagnostics,
            new LayerCodes(DiagnosticCodes.BotInvalid, DiagnosticCodes.DuplicateBot)))
        {
            var sink = new DiagnosticSink(entry.DisplayFile, diagnostics);
            if (ModLoader.ParseJson<BotDefinition>(entry.Json, sink) is not { } definition)
                continue;
            var bot = Check(definition, entry.OwnerModId, directories[entry.OwnerModId], content, sink);
            if (bot is not null && sink.ErrorCount == 0)
                bots.Add(bot.QualifiedId, bot);
        }
        CheckLinks(bots, diagnostics);
        return new BotBook(bots.Values, diagnostics);
    }

    /// <summary>Builds a book from bots in code (tests, tools). Throws on any error.</summary>
    /// <exception cref="FormatException">A bot has errors.</exception>
    public static BotBook From(IPartCatalog catalog, params (string ModId, BotDefinition Bot)[] bots)
    {
        var diagnostics = new List<DataDiagnostic>();
        var list = new List<Bot>();
        foreach (var (modId, definition) in bots)
        {
            var sink = new DiagnosticSink($"{modId}:{definition.Id}", diagnostics);
            if (Check(definition, modId, null, catalog, sink) is { } bot && sink.ErrorCount == 0)
                list.Add(bot);
        }
        var byId = list.ToDictionary(b => b.QualifiedId, StringComparer.Ordinal);
        CheckLinks(byId, diagnostics);
        list = [.. list.Where(b => byId.ContainsKey(b.QualifiedId))];
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            throw new FormatException(string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return new BotBook(list, diagnostics);
    }

    /// <summary>
    /// Checks one bot as the loader would, as if it were in mod <paramref name="modId"/> (whose folder,
    /// when given, is where its script must exist). The Bot Behavior Editor checks with this before it saves.
    /// </summary>
    public static IReadOnlyList<DataDiagnostic> Validate(BotDefinition bot, string modId, string? modDirectory, IPartCatalog catalog, string displayFile)
    {
        var diagnostics = new List<DataDiagnostic>();
        Check(bot, modId, modDirectory, catalog, new DiagnosticSink(displayFile, diagnostics));
        return diagnostics;
    }

    private static Bot? Check(BotDefinition b, string modId, string? modDirectory, IPartCatalog catalog, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.BotInvalid;
        var errors = sink.ErrorCount;
        if (b.Id is null)
            sink.Error(code, "$.id", "id is required.");
        else if (!Identifiers.IsValid(b.Id))
            sink.Error(code, "$.id", $"id '{b.Id}' must be lowercase snake_case (a letter, then letters, digits or '_').");
        if (string.IsNullOrWhiteSpace(b.Name))
            sink.Error(code, "$.name", "name is required.");
        if (b.Family is not null && !Identifiers.IsValid(b.Family))
            sink.Error(code, "$.family", $"family '{b.Family}' must be lowercase snake_case.");
        Positive(b.Health, "$.health", "health", required: true, sink);
        Positive(b.Speed, "$.speed", "speed (m/s)", required: true, sink);
        Positive(b.WaterSpeed, "$.water_speed", "water_speed (m/s)", required: false, sink);
        Positive(b.SwimDepth, "$.swim_depth", "swim_depth (m)", required: false, sink);
        Positive(b.Sight, "$.sight", "sight (m)", required: false, sink);
        var radius = b.Size?.Radius ?? DefaultRadius;
        var height = b.Size?.Height ?? DefaultHeight;
        if (b.Size is { } size)
        {
            KnownKeys.WarnUnknown(size.Extra, "$.size", sink, typeof(BotSize));
            if (!(radius is > 0.05f and <= 4f))
                sink.Error(code, "$.size.radius", "radius must be more than 0.05 and at most 4 m.");
            if (!(height <= 16f && height >= 2 * radius))
                sink.Error(code, "$.size.height", "height must be at least twice the radius and at most 16 m.");
        }
        if (b.Color is not null && !HexColor.TryParse(b.Color, out _))
            sink.Error(code, "$.color", $"color '{b.Color}' must be \"#rrggbb\".");
        if (b.RaidCost is { } cost && cost < 1)
            sink.Error(code, "$.raid_cost", "raid_cost must be 1 or more; leave it out for a bot that never raids.");
        if (b.Passive == true && b.RaidCost is not null)
            sink.Error(code, "$.raid_cost", "a passive bot never raids; remove raid_cost.");

        var attacks = new List<BotAttack>();
        var family = b.Family ?? b.Id ?? "bot";
        for (var i = 0; i < (b.Attacks?.Count ?? 0); i++)
        {
            if (CheckAttack(b.Attacks![i], $"$.attacks[{i}]", family, sink) is { } attack)
            {
                if (attacks.Any(a => a.Id == attack.Id))
                    sink.Error(code, $"$.attacks[{i}].id", $"attack '{attack.Id}' is listed twice.");
                else
                    attacks.Add(attack);
            }
        }

        var drops = new List<BotDrop>();
        for (var i = 0; i < (b.Drops?.Count ?? 0); i++)
        {
            var drop = b.Drops![i];
            var path = $"$.drops[{i}]";
            KnownKeys.WarnUnknown(drop.Extra, path, sink, typeof(BotDropDefinition));
            if (!PartValidator.IsItemRef(drop.Item))
            {
                sink.Error(code, $"{path}.item", drop.Item is null ? "item is required." : $"'{drop.Item}' must be an item id or 'mod_id:item_id'.");
                continue;
            }
            var item = Identifiers.Qualify(drop.Item);
            var before = sink.ErrorCount;
            if (catalog.FindDefinition(item) is null)
                sink.Error(code, $"{path}.item", $"there is no item '{item}'.");
            if (drop.Chance is { } chance && !(chance is >= 0f and <= 1f))
                sink.Error(code, $"{path}.chance", "chance must be from 0 to 1.");
            if (drop.Count is { } count && count < 1)
                sink.Error(code, $"{path}.count", "count must be 1 or more.");
            if (drop.CountMax is { } most && most < (drop.Count ?? 1))
                sink.Error(code, $"{path}.count_max", $"count_max must be at least count ({drop.Count ?? 1}).");
            if (sink.ErrorCount == before)
                drops.Add(new BotDrop(item, drop.Chance ?? 1f, drop.Count ?? 1, drop.CountMax ?? drop.Count ?? 1));
        }

        var wild = b.Wild is { } w ? CheckWild(w, sink) : null;
        var tame = b.Tame is { } t ? CheckTame(t, catalog, sink) : null;
        var grow = b.Grow is { } g ? CheckGrow(g, sink) : null;

        if (b.Behavior is { } tree)
            CheckBehavior(tree, "$.behavior", 1, new int[1], b.Script is not null, sink);
        string? script = null;
        if (b.Script is not null)
        {
            if (!AssetPath.TryParse(b.Script, modId, AssetKind.Script, out var asset, out var error))
                sink.Error(DiagnosticCodes.AssetPathInvalid, "$.script", error);
            else
            {
                script = asset.ToString();
                if (modDirectory is not null && asset.ModId == modId && !File.Exists(asset.Resolve(modDirectory)))
                    sink.Error(DiagnosticCodes.AssetMissing, "$.script", $"script '{b.Script}' does not exist in the mod.");
            }
        }
        var model = b.Model is { } m ? CheckModel(m, modId, modDirectory, attacks, sink) : null;
        KnownKeys.WarnUnknown(b.Extra, "$", sink, typeof(BotDefinition));

        if (sink.ErrorCount > errors)
            return null;
        return new Bot($"{modId}:{b.Id}", modId, b.Name!, family, b.Health!.Value, b.Speed!.Value, b.WaterSpeed ?? b.Speed!.Value,
            b.Sight ?? DefaultSight, radius, height, b.Color?.ToLowerInvariant() ?? DefaultColor, b.MeleeImmune ?? false, b.Passive ?? false,
            b.RaidCost, attacks, drops, script, b) { Model = model, SwimDepth = b.SwimDepth, Wild = wild, Tame = tame, Grow = grow };
    }

    private static BotModel? CheckModel(BotModelDefinition m, string modId, string? modDirectory, List<BotAttack> attacks, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.BotInvalid;
        var errors = sink.ErrorCount;
        KnownKeys.WarnUnknown(m.Extra, "$.model", sink, typeof(BotModelDefinition));
        string? mesh = null;
        if (m.Mesh is null)
            sink.Error(code, "$.model.mesh", "mesh (a .glb or .gltf in the mod) is required.");
        else if (!AssetPath.TryParse(m.Mesh, modId, AssetKind.Mesh, out var asset, out var error))
            sink.Error(DiagnosticCodes.AssetPathInvalid, "$.model.mesh", error);
        else
        {
            mesh = asset.ToString();
            if (modDirectory is not null && asset.ModId == modId && !File.Exists(asset.Resolve(modDirectory)))
                sink.Error(DiagnosticCodes.AssetMissing, "$.model.mesh", $"mesh '{m.Mesh}' does not exist in the mod.");
        }
        foreach (var (key, value) in new[] { ("eye_calm", m.EyeCalm), ("eye_engaged", m.EyeEngaged) })
        {
            if (value is not null && !HexColor.TryParse(value, out _))
                sink.Error(code, $"$.model.{key}", $"{key} '{value}' must be \"#rrggbb\".");
        }
        var clips = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var attack in attacks)
            clips[attack.Id] = [attack.Id];
        foreach (var (id, list) in m.Attacks ?? [])
        {
            if (!attacks.Any(a => a.Id == id))
                sink.Error(code, $"$.model.attacks.{id}", $"the bot has no attack '{id}'.");
            else if (list is not { Count: > 0 } || list.Any(string.IsNullOrWhiteSpace))
                sink.Error(code, $"$.model.attacks.{id}", "list one or more clip names.");
            else
                clips[id] = [.. list];
        }
        var effects = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, kind) in m.Effects ?? [])
        {
            if (key != BotEffects.Death && !attacks.Any(a => a.Id == key))
                sink.Error(code, $"$.model.effects.{key}", $"the bot has no attack '{key}' (use an attack id or \"death\").");
            else if (kind is null || !BotEffects.Kinds.Contains(kind))
                sink.Error(code, $"$.model.effects.{key}", $"effect '{kind}' must be one of: {string.Join(", ", BotEffects.Kinds)}.");
            else if (kind == BotEffects.Shot && key == BotEffects.Death)
                sink.Error(code, $"$.model.effects.{key}", "a shot follows an attack to what it hit; use it on an attack, not on death.");
            else
                effects[key] = kind;
        }
        var speeds = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (clip, speed) in m.ClipSpeeds ?? [])
        {
            if (!(speed > 0) || !float.IsFinite(speed))
                sink.Error(code, $"$.model.clip_speeds.{clip}", $"clip speed {speed} must be above 0 m/s.");
            else
                speeds[clip] = speed;
        }
        var variants = new List<string>();
        for (var i = 0; i < (m.Variants?.Count ?? 0); i++)
        {
            var path = $"$.model.variants[{i}]";
            if (!AssetPath.TryParse(m.Variants![i], modId, AssetKind.Mesh, out var asset, out var error))
                sink.Error(DiagnosticCodes.AssetPathInvalid, path, error);
            else if (modDirectory is not null && asset.ModId == modId && !File.Exists(asset.Resolve(modDirectory)))
                sink.Error(DiagnosticCodes.AssetMissing, path, $"mesh '{m.Variants[i]}' does not exist in the mod.");
            else
                variants.Add(asset.ToString());
        }
        var idles = new List<BotIdle>();
        for (var i = 0; i < (m.Idles?.Count ?? 0); i++)
        {
            var idle = m.Idles![i];
            var path = $"$.model.idles[{i}]";
            KnownKeys.WarnUnknown(idle?.Extra, path, sink, typeof(BotIdleDefinition));
            if (idle is null || string.IsNullOrWhiteSpace(idle.Clip))
                sink.Error(code, $"{path}.clip", "clip is required.");
            else if (idle.Sound is not null && string.IsNullOrWhiteSpace(idle.Sound))
                sink.Error(code, $"{path}.sound", "sound must name a sound event (or be left out).");
            else if (idle.Weight is { } w && !(w > 0 && float.IsFinite(w)))
                sink.Error(code, $"{path}.weight", $"weight {w} must be above 0.");
            else
                idles.Add(new BotIdle(idle.Clip, idle.Sound, idle.Weight ?? 1f));
        }
        for (var i = 0; i < (m.KeptNodes?.Count ?? 0); i++)
        {
            if (string.IsNullOrWhiteSpace(m.KeptNodes![i]))
                sink.Error(code, $"$.model.kept_nodes[{i}]", "a node name is required.");
        }
        if (m.LeadNode is { } leadNode && string.IsNullOrWhiteSpace(leadNode))
            sink.Error(code, "$.model.lead_node", "a node name is required (or leave it out).");
        var (idleMin, idleMax) = (DefaultIdleMin, DefaultIdleMax);
        if (m.IdleEvery is { } every)
        {
            if (every.Count != 2 || !(every[0] > 0) || !(every[1] >= every[0]) || !float.IsFinite(every[1]))
                sink.Error(code, "$.model.idle_every", "idle_every must be [min, max] seconds, 0 < min <= max.");
            else
                (idleMin, idleMax) = (every[0], every[1]);
        }
        var track = m.Track is { } t ? CheckTrack(t, sink) : null;
        foreach (var (node, loose) in m.Sway ?? [])
        {
            if (string.IsNullOrWhiteSpace(node))
                sink.Error(code, "$.model.sway", "a node name is required.");
            else if (!(loose > 0 && loose <= BotModel.MaxSway))
                sink.Error(code, $"$.model.sway.{node}", $"looseness {loose} must be above 0 and at most {BotModel.MaxSway}.");
        }
        if (sink.ErrorCount > errors)
            return null;
        return new BotModel(mesh!, clips, speeds, m.EyeCalm?.ToLowerInvariant() ?? DefaultEyeCalm, m.EyeEngaged?.ToLowerInvariant() ?? DefaultEyeEngaged,
            effects) { Variants = variants, Idles = idles, IdleMin = idleMin, IdleMax = idleMax, KeptNodes = m.KeptNodes ?? [], LeadNode = m.LeadNode, Track = track,
            Sway = m.Sway ?? [] };
    }

    private static BotTrack? CheckTrack(BotTrackDefinition t, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.BotInvalid;
        var errors = sink.ErrorCount;
        KnownKeys.WarnUnknown(t.Extra, "$.model.track", sink, typeof(BotTrackDefinition));
        if (t.Nodes is not { Count: > 0 })
            sink.Error(code, "$.model.track.nodes", "name one or more nodes that turn, each with its share (0 to 1).");
        foreach (var (node, share) in t.Nodes ?? [])
        {
            if (string.IsNullOrWhiteSpace(node))
                sink.Error(code, "$.model.track.nodes", "a node name is required.");
            else if (!(share > 0 && share <= 1))
                sink.Error(code, $"$.model.track.nodes.{node}", $"share {share} must be above 0 and at most 1.");
        }
        if (t.Yaw is { } yaw && !(yaw > 0 && yaw <= BotTrack.MaxYaw))
            sink.Error(code, "$.model.track.yaw", $"yaw {yaw} must be above 0 and at most {BotTrack.MaxYaw} degrees.");
        if (t.Pitch is { } pitch && !(pitch >= 0 && pitch <= BotTrack.MaxPitch))
            sink.Error(code, "$.model.track.pitch", $"pitch {pitch} must be 0 to {BotTrack.MaxPitch} degrees.");
        if (t.Range is { } range && !(range > 0 && range <= BotTrack.MaxRange))
            sink.Error(code, "$.model.track.range", $"range {range} must be above 0 and at most {BotTrack.MaxRange} m.");
        return sink.ErrorCount > errors ? null : new BotTrack([.. t.Nodes!], t.Yaw ?? 70f, t.Pitch ?? 30f, t.Range ?? 6f);
    }

    private static BotAttack? CheckAttack(BotAttackDefinition a, string path, string family, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.BotInvalid;
        var errors = sink.ErrorCount;
        KnownKeys.WarnUnknown(a.Extra, path, sink, typeof(BotAttackDefinition));
        if (!Identifiers.IsValid(a.Id))
            sink.Error(code, $"{path}.id", a.Id is null ? "id is required." : $"id '{a.Id}' must be lowercase snake_case.");
        if (a.Kind is null)
            sink.Error(code, $"{path}.kind", "kind (melee, ranged or area) is required.");
        NonNegative(a.Damage, $"{path}.damage", "damage", required: true, sink);
        NonNegative(a.PartDamage, $"{path}.part_damage", "part_damage", required: false, sink);
        Positive(a.Interval, $"{path}.interval", "interval (s)", required: true, sink);
        Positive(a.Range, $"{path}.range", "range (m)", required: a.Kind is BotAttackKind.Melee or BotAttackKind.Ranged, sink);
        Positive(a.Radius, $"{path}.radius", "radius (m)", required: a.Kind is BotAttackKind.Area, sink);
        if (a.Radius is not null && a.Kind is not BotAttackKind.Area)
            sink.Error(code, $"{path}.radius", "only area attacks have a radius.");
        if (a.DamageOverTime is { } dot)
        {
            KnownKeys.WarnUnknown(dot.Extra, $"{path}.damage_over_time", sink, typeof(BotDamageOverTime));
            Positive(dot.PerSecond, $"{path}.damage_over_time.per_second", "per_second", required: true, sink);
            Positive(dot.Seconds, $"{path}.damage_over_time.seconds", "seconds", required: true, sink);
        }
        if (a.Slow is { } slow)
        {
            KnownKeys.WarnUnknown(slow.Extra, $"{path}.slow", sink, typeof(BotSlow));
            if (slow.Factor is not { } factor)
                sink.Error(code, $"{path}.slow.factor", "factor (what speed is multiplied by, between 0 and 1) is required.");
            else if (!(factor > 0 && factor < 1))
                sink.Error(code, $"{path}.slow.factor", $"factor must be more than 0 and less than 1 (got {factor}).");
            Positive(slow.Seconds, $"{path}.slow.seconds", "seconds", required: true, sink);
        }
        NonNegative(a.ObjectDamage, $"{path}.object_damage", "object_damage", required: false, sink);
        NonNegative(a.Windup, $"{path}.windup", "windup (s)", required: false, sink);
        if (a.Windup is { } windup && (windup > BotAttack.MaxWindup || (a.Interval is { } every && windup >= every)))
            sink.Error(code, $"{path}.windup", $"windup must be at most {BotAttack.MaxWindup} s and shorter than the interval.");
        if (a.Reaction is { } reaction && !Enum.IsDefined(reaction))
            sink.Error(code, $"{path}.reaction", "reaction must be none, light, heavy or knockdown.");
        if (a.Sound is not null && !PartValidator.IsItemRef(a.Sound))
            sink.Error(code, $"{path}.sound", $"sound '{a.Sound}' must be a sound event id.");
        if (sink.ErrorCount > errors)
            return null;
        var radius = a.Radius ?? 0f;
        return new BotAttack(a.Id!, a.Kind!.Value, a.Damage!.Value, a.PartDamage ?? a.Damage!.Value, a.Range ?? radius, radius, a.Interval!.Value,
            a.DamageOverTime?.PerSecond ?? 0f, a.DamageOverTime?.Seconds ?? 0f, a.KillsSelf ?? false, a.HitsParts ?? true,
            a.Sound ?? $"{family}_attack", a.Slow?.Factor ?? 1f, a.Slow?.Seconds ?? 0f, a.ObjectDamage ?? 0f, a.Windup ?? 0f, a.Reaction);
    }

    private static void CheckBehavior(JsonObject node, string path, int depth, int[] count, bool hasScript, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.BotInvalid;
        if (++count[0] > MaxBehaviorNodes)
        {
            if (count[0] == MaxBehaviorNodes + 1)
                sink.Error(code, path, $"a behaviour tree may have at most {MaxBehaviorNodes} nodes.");
            return;
        }
        if (depth > MaxBehaviorDepth)
        {
            sink.Error(code, path, $"the behaviour tree is more than {MaxBehaviorDepth} levels deep.");
            return;
        }
        if (node["type"] is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var type) || !Identifiers.IsValid(type))
        {
            sink.Error(code, $"{path}.type", "every node needs a type: a lowercase snake_case node name.");
            return;
        }
        var known = BotBehavior.Find(type);
        if (known is null)
        {
            // A custom leaf lives in the bot's script; without one it can only be another mod's.
            if (!hasScript)
                sink.Warning(DiagnosticCodes.BotBehaviorUnknownNode, $"{path}.type",
                    $"'{type}' is not a built-in node and the bot has no script to define it; it fails when it runs.");
        }
        var children = node["children"];
        if (children is null)
        {
            if (known is { Shape: BotNodeShape.Composite })
                sink.Error(code, $"{path}.children", $"'{type}' needs children.");
            else if (known is { Shape: BotNodeShape.Decorator })
                sink.Error(code, $"{path}.children", $"'{type}' needs exactly one child.");
            return;
        }
        if (children is not JsonArray list)
        {
            sink.Error(code, $"{path}.children", "children must be a list of nodes.");
            return;
        }
        if (known is { Shape: BotNodeShape.Leaf })
            sink.Error(code, $"{path}.children", $"'{type}' is a leaf and takes no children.");
        else if (known is { Shape: BotNodeShape.Decorator } && list.Count != 1)
            sink.Error(code, $"{path}.children", $"'{type}' needs exactly one child.");
        else if (known is { Shape: BotNodeShape.Composite } && list.Count == 0)
            sink.Error(code, $"{path}.children", $"'{type}' needs at least one child.");
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is JsonObject child)
                CheckBehavior(child, $"{path}.children[{i}]", depth + 1, count, hasScript, sink);
            else
                sink.Error(code, $"{path}.children[{i}]", "a child must be a node object.");
        }
    }

    private static BotWild? CheckWild(BotWildDefinition w, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.BotInvalid;
        var errors = sink.ErrorCount;
        KnownKeys.WarnUnknown(w.Extra, "$.wild", sink, typeof(BotWildDefinition));
        if (w.Habitat is not ("water" or "land"))
            sink.Error(code, "$.wild.habitat", w.Habitat is null ? "habitat (water or land) is required." : $"habitat '{w.Habitat}' must be water or land.");
        if (w.Count is not { } count || count < 1)
            sink.Error(code, "$.wild.count", w.Count is null ? "count (how many near each player) is required." : "count must be 1 or more.");
        Positive(w.MinDistance, "$.wild.min_distance", "min_distance (m)", required: false, sink);
        Positive(w.MaxDistance, "$.wild.max_distance", "max_distance (m)", required: false, sink);
        Positive(w.DespawnDistance, "$.wild.despawn_distance", "despawn_distance (m)", required: false, sink);
        Positive(w.MinDepth, "$.wild.min_depth", "min_depth (m)", required: false, sink);
        NonNegative(w.ClearRadius, "$.wild.clear_radius", "clear_radius (m)", required: false, sink);
        Positive(w.Interval, "$.wild.interval", "interval (s)", required: false, sink);
        var min = w.MinDistance ?? DefaultWildMinDistance;
        var max = w.MaxDistance ?? DefaultWildMaxDistance;
        var despawn = w.DespawnDistance ?? DefaultWildDespawnDistance;
        if (!(max > min))
            sink.Error(code, "$.wild.max_distance", $"max_distance ({max}) must be more than min_distance ({min}).");
        if (!(despawn > max))
            sink.Error(code, "$.wild.despawn_distance", $"despawn_distance ({despawn}) must be more than max_distance ({max}).");
        for (var i = 0; i < (w.Biomes?.Count ?? 0); i++)
        {
            if (string.IsNullOrWhiteSpace(w.Biomes![i]))
                sink.Error(code, $"$.wild.biomes[{i}]", "a biome id is required.");
        }
        if (w.Chance is { } chance && !(chance > 0 && chance <= 1))
            sink.Error(code, "$.wild.chance", "chance must be more than 0 and at most 1.");
        if (w.Herd is { } herd && (herd.Count != 2 || herd[0] < 1 || herd[1] < herd[0]))
            sink.Error(code, "$.wild.herd", "herd must be [min, max], 1 <= min <= max.");
        BotWildNear? near = null;
        if (w.Near is { } n)
        {
            KnownKeys.WarnUnknown(n.Extra, "$.wild.near", sink, typeof(BotWildNearDefinition));
            if (n.Objects is not { Count: > 0 } || n.Objects.Any(string.IsNullOrWhiteSpace))
                sink.Error(code, "$.wild.near.objects", "list one or more world object ids.");
            Positive(n.Radius, "$.wild.near.radius", "radius (m)", required: true, sink);
            if (n.Share is { } share && !(share >= 0 && share <= 1))
                sink.Error(code, "$.wild.near.share", "share must be from 0 to 1.");
            if (sink.ErrorCount == errors)
                near = new BotWildNear(new HashSet<string>(n.Objects!, StringComparer.Ordinal), n.Radius!.Value, n.Share ?? DefaultWildNearShare);
        }
        if (sink.ErrorCount > errors)
            return null;
        return new BotWild(w.Habitat == "water", w.Count!.Value, min, max, despawn, w.MinDepth ?? DefaultWildMinDepth, w.ClearRadius ?? DefaultWildClearRadius,
            w.Interval ?? DefaultWildInterval)
        {
            Biomes = new HashSet<string>(w.Biomes ?? [], StringComparer.Ordinal),
            HerdMin = w.Herd?[0] ?? 1,
            HerdMax = w.Herd?[1] ?? 1,
            Near = near,
            Chance = w.Chance ?? 1f,
        };
    }

    private static BotTame? CheckTame(BotTameDefinition t, IPartCatalog catalog, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.BotInvalid;
        var errors = sink.ErrorCount;
        KnownKeys.WarnUnknown(t.Extra, "$.tame", sink, typeof(BotTameDefinition));
        var foods = new HashSet<string>(StringComparer.Ordinal);
        if (t.Foods is not { Count: > 0 })
            sink.Error(code, "$.tame.foods", "list one or more foods it eats.");
        for (var i = 0; i < (t.Foods?.Count ?? 0); i++)
        {
            var food = t.Foods![i];
            if (!PartValidator.IsItemRef(food))
                sink.Error(code, $"$.tame.foods[{i}]", $"'{food}' must be an item id or 'mod_id:item_id'.");
            else if (catalog.FindDefinition(Identifiers.Qualify(food)) is null)
                sink.Error(code, $"$.tame.foods[{i}]", $"there is no item '{Identifiers.Qualify(food)}'.");
            else
                foods.Add(Identifiers.Qualify(food));
        }
        if (t.Feeds is < 1)
            sink.Error(code, "$.tame.feeds", "feeds must be 1 or more.");
        string? lead = null;
        if (t.Lead is { } l)
        {
            if (!PartValidator.IsItemRef(l))
                sink.Error(code, "$.tame.lead", $"'{l}' must be an item id or 'mod_id:item_id'.");
            else if (catalog.FindDefinition(Identifiers.Qualify(l)) is null)
                sink.Error(code, "$.tame.lead", $"there is no item '{Identifiers.Qualify(l)}'.");
            else
                lead = Identifiers.Qualify(l);
        }
        var homes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < (t.Homes?.Count ?? 0); i++)
        {
            var home = t.Homes![i];
            if (!PartValidator.IsItemRef(home))
                sink.Error(code, $"$.tame.homes[{i}]", $"'{home}' must be a part id or 'mod_id:part_id'.");
            else if (catalog.FindDefinition(Identifiers.Qualify(home)) is null)
                sink.Error(code, $"$.tame.homes[{i}]", $"there is no part '{Identifiers.Qualify(home)}'.");
            else
                homes.Add(Identifiers.Qualify(home));
        }
        Positive(t.HomeRadius, "$.tame.home_radius", "home_radius", false, sink);
        var breed = t.Breed is { } b ? CheckBreed(b, catalog, sink) : null;
        var produce = t.Produce is { } p ? CheckProduce(p, catalog, sink) : null;
        if (t.Produce is not null && homes.Count == 0)
            sink.Error(code, "$.tame.produce", "a bot that gives produce eats from its home: list its homes.");
        return sink.ErrorCount > errors ? null
            : new BotTame(foods, t.Feeds ?? DefaultTameFeeds, lead, homes, t.HomeRadius ?? DefaultHomeRadius) { Breed = breed, Produce = produce };
    }

    private static BotProduce? CheckProduce(BotProduceDefinition p, IPartCatalog catalog, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.BotInvalid;
        var errors = sink.ErrorCount;
        KnownKeys.WarnUnknown(p.Extra, "$.tame.produce", sink, typeof(BotProduceDefinition));
        if (p.Item is null)
            sink.Error(code, "$.tame.produce.item", "item is required.");
        else if (!PartValidator.IsItemRef(p.Item))
            sink.Error(code, "$.tame.produce.item", $"'{p.Item}' must be an item id or 'mod_id:item_id'.");
        else if (catalog.FindDefinition(Identifiers.Qualify(p.Item)) is null)
            sink.Error(code, "$.tame.produce.item", $"there is no item '{Identifiers.Qualify(p.Item)}'.");
        if (p.Feeds is < 1)
            sink.Error(code, "$.tame.produce.feeds", "feeds must be 1 or more.");
        NonNegative(p.Cooldown, "$.tame.produce.cooldown", "cooldown", false, sink);
        Positive(p.EatEvery, "$.tame.produce.eat_every", "eat_every", false, sink);
        return sink.ErrorCount > errors ? null
            : new BotProduce(Identifiers.Qualify(p.Item!), p.Feeds ?? DefaultProduceFeeds, p.Cooldown ?? DefaultProduceCooldown, p.EatEvery ?? DefaultProduceEatEvery);
    }

    private static BotBreed? CheckBreed(BotBreedDefinition b, IPartCatalog catalog, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.BotInvalid;
        var errors = sink.ErrorCount;
        KnownKeys.WarnUnknown(b.Extra, "$.tame.breed", sink, typeof(BotBreedDefinition));
        if (b.Treat is null)
            sink.Error(code, "$.tame.breed.treat", "treat is required.");
        else if (!PartValidator.IsItemRef(b.Treat))
            sink.Error(code, "$.tame.breed.treat", $"'{b.Treat}' must be an item id or 'mod_id:item_id'.");
        else if (catalog.FindDefinition(Identifiers.Qualify(b.Treat)) is null)
            sink.Error(code, "$.tame.breed.treat", $"there is no item '{Identifiers.Qualify(b.Treat)}'.");
        CheckBotRef(b.Calf, "$.tame.breed.calf", "calf", sink);
        Positive(b.Seconds, "$.tame.breed.seconds", "seconds", false, sink);
        Positive(b.Cooldown, "$.tame.breed.cooldown", "cooldown", false, sink);
        Positive(b.Radius, "$.tame.breed.radius", "radius", false, sink);
        if (b.Herd is < 2)
            sink.Error(code, "$.tame.breed.herd", "herd must be 2 or more.");
        return sink.ErrorCount > errors ? null : new BotBreed(Identifiers.Qualify(b.Treat!), Identifiers.Qualify(b.Calf!),
            b.Seconds ?? DefaultBreedSeconds, b.Cooldown ?? DefaultBreedCooldown, b.Herd ?? DefaultBreedHerd, b.Radius ?? DefaultBreedRadius);
    }

    private static BotGrow? CheckGrow(BotGrowDefinition g, DiagnosticSink sink)
    {
        var errors = sink.ErrorCount;
        KnownKeys.WarnUnknown(g.Extra, "$.grow", sink, typeof(BotGrowDefinition));
        CheckBotRef(g.Into, "$.grow.into", "into", sink);
        Positive(g.Seconds, "$.grow.seconds", "seconds", true, sink);
        return sink.ErrorCount > errors ? null : new BotGrow(Identifiers.Qualify(g.Into!), g.Seconds!.Value);
    }

    // A bot type named in a bot's data; whether it exists is checked once every bot is loaded (CheckLinks).
    private static void CheckBotRef(string? id, string path, string label, DiagnosticSink sink)
    {
        if (id is null)
            sink.Error(DiagnosticCodes.BotInvalid, path, $"{label} is required.");
        else if (!PartValidator.IsItemRef(id))
            sink.Error(DiagnosticCodes.BotInvalid, path, $"'{id}' must be a bot id or 'mod_id:bot_id'.");
    }

    // The bot types bots name (a breed's calf, what a calf grows into) must exist; a bot naming a missing one is left out.
    private static void CheckLinks(Dictionary<string, Bot> bots, List<DataDiagnostic> diagnostics)
    {
        foreach (var bot in bots.Values.ToList())
        {
            var sink = new DiagnosticSink(bot.QualifiedId, diagnostics);
            if (bot.Tame?.Breed is { } breed && !bots.ContainsKey(breed.Calf))
                sink.Error(DiagnosticCodes.BotInvalid, "$.tame.breed.calf", $"there is no bot '{breed.Calf}'.");
            if (bot.Grow is { } grow && !bots.ContainsKey(grow.Into))
                sink.Error(DiagnosticCodes.BotInvalid, "$.grow.into", $"there is no bot '{grow.Into}'.");
            if (sink.ErrorCount > 0)
                bots.Remove(bot.QualifiedId);
        }
    }

    private static void Positive(float? value, string path, string label, bool required, DiagnosticSink sink)
    {
        if (value is { } v && !(v > 0 && float.IsFinite(v)))
            sink.Error(DiagnosticCodes.BotInvalid, path, $"{label} must be greater than 0.");
        else if (required && value is null)
            sink.Error(DiagnosticCodes.BotInvalid, path, $"{label} is required.");
    }

    private static void NonNegative(float? value, string path, string label, bool required, DiagnosticSink sink)
    {
        if (value is { } v && !(v >= 0 && float.IsFinite(v)))
            sink.Error(DiagnosticCodes.BotInvalid, path, $"{label} must be 0 or more.");
        else if (required && value is null)
            sink.Error(DiagnosticCodes.BotInvalid, path, $"{label} is required.");
    }
}
