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
public sealed record BotAttack(
    string Id, BotAttackKind Kind, float Damage, float PartDamage, float Range, float Radius, float Interval,
    float DamagePerSecond, float DamageSeconds, bool KillsSelf, bool HitsParts, string Sound);

/// <summary>An item a bot may drop, qualified.</summary>
/// <param name="Item">Qualified item id.</param>
/// <param name="Chance">Chance from 0 to 1.</param>
/// <param name="Count">How many.</param>
public sealed record BotDrop(string Item, float Chance, int Count);

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

    /// <summary>Loads the bots of the mods in <paramref name="content"/>.</summary>
    public static BotBook Load(ModLoadResult content)
    {
        var diagnostics = new List<DataDiagnostic>();
        var bots = new Dictionary<string, Bot>(StringComparer.Ordinal);
        foreach (var mod in content.Mods)
        {
            var folder = Path.Combine(mod.Directory, Folder);
            if (!Directory.Exists(folder))
                continue;
            var files = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories)
                .Select(f => (Path: f, Relative: Path.GetRelativePath(mod.Directory, f).Replace('\\', '/')))
                .OrderBy(f => f.Relative, StringComparer.Ordinal);
            foreach (var (path, relative) in files)
            {
                var sink = new DiagnosticSink($"{mod.Id}/{relative}", diagnostics);
                if (Read(path, sink) is not { } definition)
                    continue;
                var bot = Check(definition, mod.Id, mod.Directory, content, sink);
                if (bot is not null && bots.ContainsKey(bot.QualifiedId))
                    sink.Error(DiagnosticCodes.DuplicateBot, "$.id", $"bot '{bot.QualifiedId}' is defined twice.");
                else if (bot is not null && sink.ErrorCount == 0)
                    bots.Add(bot.QualifiedId, bot);
            }
        }
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
            if (sink.ErrorCount == before)
                drops.Add(new BotDrop(item, drop.Chance ?? 1f, drop.Count ?? 1));
        }

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
        KnownKeys.WarnUnknown(b.Extra, "$", sink, typeof(BotDefinition));

        if (sink.ErrorCount > errors)
            return null;
        return new Bot($"{modId}:{b.Id}", modId, b.Name!, family, b.Health!.Value, b.Speed!.Value, b.WaterSpeed ?? b.Speed!.Value,
            b.Sight ?? DefaultSight, radius, height, b.Color?.ToLowerInvariant() ?? DefaultColor, b.MeleeImmune ?? false, b.Passive ?? false,
            b.RaidCost, attacks, drops, script, b);
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
        if (a.Sound is not null && !PartValidator.IsItemRef(a.Sound))
            sink.Error(code, $"{path}.sound", $"sound '{a.Sound}' must be a sound event id.");
        if (sink.ErrorCount > errors)
            return null;
        var radius = a.Radius ?? 0f;
        return new BotAttack(a.Id!, a.Kind!.Value, a.Damage!.Value, a.PartDamage ?? a.Damage!.Value, a.Range ?? radius, radius, a.Interval!.Value,
            a.DamageOverTime?.PerSecond ?? 0f, a.DamageOverTime?.Seconds ?? 0f, a.KillsSelf ?? false, a.HitsParts ?? true,
            a.Sound ?? $"{family}_attack");
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

    private static BotDefinition? Read(string path, DiagnosticSink sink)
    {
        try
        {
            return ModLoader.ParseJson<BotDefinition>(File.ReadAllText(path), sink);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            sink.Error(DiagnosticCodes.FileUnreadable, null, $"could not read file: {e.Message}");
            return null;
        }
    }
}
