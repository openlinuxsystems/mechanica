using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mechanica.Core.Data;

namespace Mechanica.Core.World;

/// <summary>A biome ready for generation, with its index in <see cref="WorldGenData.Biomes"/>.</summary>
public sealed record Biome(int Index, string Id, string Name, float Weight, float Height, float Relief, Vector3 Color)
{
    /// <summary>Share of the relief that is ridged noise (<see cref="BiomeDefinition.Ridge"/>).</summary>
    public float Ridge { get; init; }

    /// <summary>Height of the terrace steps, or 0 (<see cref="BiomeDefinition.Terrace"/>).</summary>
    public float Terrace { get; init; }

    /// <summary>Share of the hills and cliffs it keeps (<see cref="BiomeDefinition.Hills"/>).</summary>
    public float Hills { get; init; } = 1f;
}

/// <summary>A site (<see cref="SiteDefinition"/>) ready for placement, with its prefab or pool resolved.</summary>
/// <param name="Index">Place in <see cref="WorldGenData.Sites"/>.</param>
/// <param name="Definition">The data.</param>
/// <param name="Poi">The prefab placed here, or null when it comes from <see cref="Pool"/>.</param>
/// <param name="Pool">The pool the prefab is drawn from, or null.</param>
/// <param name="Biome">Index of the biome the site is meant to stand in, or -1.</param>
public sealed record LoadedSite(int Index, SiteDefinition Definition, LoadedPoi? Poi, LoadedPool? Pool, int Biome)
{
    /// <summary>The site's id.</summary>
    public string Id => Definition.Id!;
}

/// <summary>A pool (<see cref="PoolDefinition"/>) ready for drawing: one prefab per site that uses it, in data order.</summary>
public sealed record LoadedPool(string Id, IReadOnlyList<LoadedPoi> Entries);

/// <summary>A scatter rule ready for generation. <see cref="Index"/> is its place among every biome's rules, and part of each object's id.</summary>
public sealed record Scatter(int Index, int Biome, WorldObjectDefinition Object, float Spacing, float Chance, PerchDefinition? Perch = null,
    ClusterDefinition? Cluster = null, ScatterWater? Water = null, RoadsideDefinition? Roadside = null, NearDefinition? Near = null)
{
    /// <summary>Qualified ids (<c>mod_id:poi_id</c>) of the POIs <see cref="NearDefinition.Pois"/> names, or null.</summary>
    public IReadOnlySet<string>? NearPois { get; init; }

    /// <summary>The rule's <see cref="ScatterRule.Away"/>, or null.</summary>
    public AwayDefinition? Away { get; init; }

    /// <summary>Qualified ids of the POIs <see cref="Away"/> keeps clear of, or null.</summary>
    public IReadOnlySet<string>? AwayPois { get; init; }
}

/// <summary>A biome's flora rule ready for growing. <see cref="Index"/> is its place among every biome's flora rules.</summary>
/// <param name="Index">Place among every biome's flora rules; salts its random numbers.</param>
/// <param name="Biome">Index of the biome it grows in.</param>
/// <param name="Kind">Which kind of flora, and so which Foliage density setting controls it.</param>
/// <param name="Density">Plants per square metre in a full patch at the High foliage level.</param>
/// <param name="Coverage">Share of the ground its patches cover.</param>
/// <param name="PatchSize">Wavelength of the patch noise in metres.</param>
/// <param name="MinScale">Smallest random scale.</param>
/// <param name="MaxScale">Largest random scale.</param>
/// <param name="Models">Models to pick from.</param>
/// <param name="Colors">Tints to pick from (0..1 RGB).</param>
/// <param name="MaxSlope">Steepest ground it grows on (rise over run).</param>
public sealed record Flora(int Index, int Biome, FloraKind Kind, float Density, float Coverage, float PatchSize, float MinScale, float MaxScale,
    IReadOnlyList<FloraModel> Models, IReadOnlyList<Vector3> Colors, float MaxSlope);

/// <summary>A ground texture ready for drawing, with its layer index in <see cref="WorldGenData.Grounds"/>.</summary>
/// <param name="Index">Texture layer.</param>
/// <param name="Id">Id from the data.</param>
/// <param name="TextureFile">Full path of the <c>.png</c>; null when the world was built without mods (tests).</param>
/// <param name="Tile">Metres one repeat of the texture covers.</param>
/// <param name="Flora">Whether flora grows on it.</param>
/// <param name="FloraTint">Tint plants on or next to it take, if any (0..1 RGB).</param>
public sealed record Ground(int Index, string Id, string? TextureFile, float Tile, bool Flora, Vector3? FloraTint);

/// <summary>A POI prefab that loaded cleanly.</summary>
/// <param name="QualifiedId">Game-wide id, <c>mod_id:poi_id</c>.</param>
/// <param name="Prefab">The validated data.</param>
/// <param name="Biomes">Indices of the biomes a spaced POI may stand in.</param>
public sealed record LoadedPoi(string QualifiedId, PoiPrefab Prefab, IReadOnlySet<int> Biomes);

/// <summary>
/// Everything world generation reads from the enabled mods (chunk 19): the <see cref="WorldGenDefinition"/>
/// of the last mod in load order that ships <c>world/worldgen.json</c>, and every mod's <c>pois/*.json</c>.
/// A file with errors is left out; <see cref="Diagnostics"/> says why. Host and clients compare
/// <see cref="Fingerprint"/> on join, because the world is regenerated from the seed on every peer.
/// </summary>
public sealed class WorldGenData
{
    /// <summary>Where a mod keeps its world generation rules.</summary>
    public const string DefinitionFile = "world/worldgen.json";

    /// <summary>Where a mod keeps its POI prefabs.</summary>
    public const string PoiFolder = "pois";

    private WorldGenData(WorldGenDefinition definition, IReadOnlyList<LoadedPoi> pois, IReadOnlyList<DataDiagnostic> diagnostics, string fingerprint,
        Func<string, string, AssetKind, string?>? assetFile = null, IReadOnlyList<LoadedSite>? sites = null, string definitionMod = "")
    {
        _assetFile = assetFile;
        _definitionMod = definitionMod;
        Definition = definition;
        Terrain = definition.Terrain;
        Pois = pois;
        Diagnostics = diagnostics;
        Fingerprint = fingerprint;
        Biomes = [.. (definition.Biomes ?? []).Select((b, i) => new Biome(i, b.Id!, b.Name ?? b.Id!, b.Weight, b.Height, b.Relief, ParseColor(b.Color))
        {
            Ridge = b.Ridge,
            Terrace = b.Terrace,
            Hills = b.Hills,
        })];
        Objects = (definition.Objects ?? []).ToDictionary(o => o.Id!);
        Liquids = (definition.Liquids ?? []).ToDictionary(l => l.Id!);
        var scatter = new List<Scatter>();
        foreach (var biome in definition.Biomes ?? [])
            foreach (var rule in biome.Scatter ?? [])
                scatter.Add(new Scatter(scatter.Count, Biomes.First(b => b.Id == biome.Id).Index, Objects[rule.Object!], rule.Spacing, rule.Chance, rule.Perch, rule.Cluster, rule.Water, rule.Roadside, rule.Near)
                {
                    NearPois = rule.Near?.Pois is { } near ? near.Select(QualifyPoi).ToHashSet(StringComparer.Ordinal) : null,
                    Away = rule.Away,
                    AwayPois = rule.Away?.Pois is { } away ? away.Select(QualifyPoi).ToHashSet(StringComparer.Ordinal) : null,
                });
        ScatterRules = scatter;
        var flora = new List<Flora>();
        foreach (var biome in definition.Biomes ?? [])
        {
            foreach (var rule in biome.Flora ?? [])
            {
                var kind = rule.Kind!.Value;
                var models = rule.Models is { Count: > 0 } ids ? ids.Select(id => FloraModels.Find(id)!).ToList() : FloraModels.OfKind(kind);
                var colors = rule.Colors is { Count: > 0 } tints ? tints.Select(ParseColor).ToList() : [Vector3.One];
                var (min, max) = rule.Scale is [var lo, var hi] ? (lo, hi) : (1f, 1f);
                flora.Add(new Flora(flora.Count, Biomes.First(b => b.Id == biome.Id).Index, kind, rule.Density, rule.Coverage, rule.PatchSize, min, max, models, colors, rule.MaxSlope));
            }
        }
        FloraRules = flora;
        FloraByBiome = [.. Biomes.Select(b => (IReadOnlyList<Flora>)[.. flora.Where(f => f.Biome == b.Index)])];
        CoastBiome = definition.CoastBiome is { } coast ? Biomes.First(b => b.Id == coast) : null;
        Grounds = [.. (definition.Grounds ?? []).Select((g, i) => new Ground(i, g.Id!, assetFile?.Invoke(g.Texture!, definitionMod, AssetKind.Texture), g.Tile, g.Flora,
            g.FloraTint is { } tint ? ParseColor(tint) : null))];
        BiomeGrounds = [.. (definition.Biomes ?? []).Select(b => b.Ground is { } id ? GroundIndex(id) : -1)];
        SlopeGround = definition.SlopeGround is { Ground: { } slope } ? GroundIndex(slope) : -1;
        ShoulderGround = definition.Roads?.ShoulderGround is { } shoulder ? GroundIndex(shoulder) : -1;
        TrackGround = definition.Roads?.TrackGround is { } track ? GroundIndex(track) : -1;
        Sites = sites ?? [];
    }

    /// <summary>The ground layer painted on road shoulders, or -1.</summary>
    public int ShoulderGround { get; }

    /// <summary>The ground layer dirt tracks are painted with, or -1.</summary>
    public int TrackGround { get; }

    /// <summary>The world's mandatory structures, in file order, with their prefabs or pools resolved.</summary>
    public IReadOnlyList<LoadedSite> Sites { get; }

    // Full path of a mod path, read relative to a mod (the second argument), or null.
    private readonly Func<string, string, AssetKind, string?>? _assetFile;
    private readonly string _definitionMod;

    // A POI id as scatter rules give it, qualified with the world file's mod when it names none.
    private string QualifyPoi(string id) => id.Contains(':') ? id : $"{_definitionMod}:{id}";

    /// <summary>
    /// Full path of the model a world object is drawn with (<see cref="WorldObjectDefinition.Mesh"/>), or null when it has
    /// none or the world was built without mods (tests). <paramref name="variant"/> picks one of
    /// <see cref="MeshDefinition.Variants"/> (1 the first); 0, or one it does not have, is the main model.
    /// </summary>
    public string? MeshFileOf(WorldObjectDefinition definition, int variant = 0) =>
        ModelOf(definition, variant) is { } file ? _assetFile?.Invoke(file, _definitionMod, AssetKind.Mesh) : null;

    /// <summary>The mod path of the model <paramref name="variant"/> of an object (see <see cref="MeshFileOf"/>), or null.</summary>
    public static string? ModelOf(WorldObjectDefinition definition, int variant = 0) =>
        variant > 0 && definition.Mesh?.Variants is { } variants && variant <= variants.Count ? variants[variant - 1] : definition.Mesh?.File;

    /// <summary>Full path of the far model a world object is drawn with past its LOD distance (<see cref="MeshDefinition.Lod"/>), or null.</summary>
    public string? LodFileOf(WorldObjectDefinition definition) =>
        definition.Mesh?.Lod is { } file ? _assetFile?.Invoke(file, _definitionMod, AssetKind.Mesh) : null;

    /// <summary>
    /// Full path of a POI model's file (<see cref="PoiModel.File"/>), read relative to the mod the POI belongs to, or null
    /// when the world was built without mods (tests).
    /// </summary>
    public string? ModelFileOf(LoadedPoi poi, PoiModel model) =>
        model.File is { } file ? _assetFile?.Invoke(file, poi.QualifiedId[..Math.Max(0, poi.QualifiedId.IndexOf(':'))], AssetKind.Mesh) : null;

    /// <summary>Ground textures, in layer order.</summary>
    public IReadOnlyList<Ground> Grounds { get; }

    /// <summary>Each biome's ground layer, or -1 for a biome drawn in its plain colour.</summary>
    public IReadOnlyList<int> BiomeGrounds { get; }

    /// <summary>The ground layer steep slopes take, or -1 for none.</summary>
    public int SlopeGround { get; }

    /// <summary>The layer of a ground id, or -1.</summary>
    public int GroundIndex(string id)
    {
        for (var i = 0; i < Grounds.Count; i++)
        {
            if (Grounds[i].Id == id)
                return i;
        }
        return -1;
    }

    /// <summary>The rules in use.</summary>
    public WorldGenDefinition Definition { get; }

    /// <summary>Height field settings.</summary>
    public TerrainDefinition Terrain { get; }

    /// <summary>Biomes in file order.</summary>
    public IReadOnlyList<Biome> Biomes { get; }

    /// <summary>The biome along the world edge, if any.</summary>
    public Biome? CoastBiome { get; }

    /// <summary>Scattered object kinds by id.</summary>
    public IReadOnlyDictionary<string, WorldObjectDefinition> Objects { get; }

    /// <summary>Liquids POI pools hold, by id.</summary>
    public IReadOnlyDictionary<string, LiquidDefinition> Liquids { get; }

    /// <summary>Every biome's scatter rules, in biome then file order.</summary>
    public IReadOnlyList<Scatter> ScatterRules { get; }

    /// <summary>
    /// Largest <see cref="WorldObjectDefinition.Footprint"/> any scatter rule's object has, scaled by its largest scale: how
    /// far an object (an outcrop's cube) may stand from the ground point it is scattered by.
    /// </summary>
    public float MaxFootprint => ScatterRules.Count == 0 ? 0f : ScatterRules.Max(r => r.Object.Footprint * (r.Object.Scale is [_, var hi] ? hi : 1f));

    /// <summary>Every biome's flora rules, in biome then file order.</summary>
    public IReadOnlyList<Flora> FloraRules { get; }

    /// <summary>The flora rules of each biome, by biome index.</summary>
    public IReadOnlyList<IReadOnlyList<Flora>> FloraByBiome { get; }

    /// <summary>POI prefabs, ordered by qualified id.</summary>
    public IReadOnlyList<LoadedPoi> Pois { get; }

    /// <summary>Every problem found while loading.</summary>
    public IReadOnlyList<DataDiagnostic> Diagnostics { get; }

    /// <summary>True when any diagnostic is an error.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>SHA-256 (hex) of the canonical form of the definition and every POI.</summary>
    public string Fingerprint { get; }

    /// <summary>A flat world at height 0 with one bare biome, no sea, objects or POIs. For tests and as a fallback.</summary>
    public static WorldGenData Flat { get; } = FromDefinition(new WorldGenDefinition
    {
        Terrain = new TerrainDefinition { CoastWidth = 0f, CoastBiomeWidth = 0f, SeaLevel = -100f, SeaFloor = -100f },
        Biomes = [new BiomeDefinition { Id = "flat", Name = "Flat", Weight = 1f, Color = "#5c7350" }],
    });

    /// <summary>Loads the world data of the mods in <paramref name="content"/>, in load order.</summary>
    public static WorldGenData Load(ModLoadResult content)
    {
        var diagnostics = new List<DataDiagnostic>();
        WorldGenDefinition? definition = null;
        var definitionMod = "";
        var prefabs = new List<(string ModId, string File, PoiPrefab Prefab)>();
        // The last whole worldgen.json in load order, with every later mod's worldgen.patch.json merged in by id
        // (a new tree in "objects", a biome's "scatter" entry), so world mods combine.
        var layered = ContentLayers.LayerFile(content, DefinitionFile, diagnostics, (patch, modId) => ContentLayers.PointAssetsAt(patch, modId),
            (json, sink) => ModLoader.ParseJson<WorldGenDefinition>(json.ToJsonString(), sink) is { } read && Valid(read, sink));
        if (layered is { } world)
        {
            definition = ModLoader.ParseJson<WorldGenDefinition>(world.Json.ToJsonString(), new DiagnosticSink(world.DisplayFile, diagnostics));
            definitionMod = world.BaseModId;
        }
        // Other mods override, patch and remove POIs in overrides/<mod>/pois/ (see ContentLayers).
        foreach (var entry in ContentLayers.Collect(content, PoiFolder, ContentLayers.IdMember, diagnostics,
            new LayerCodes(DiagnosticCodes.PoiInvalid, DiagnosticCodes.PoiInvalid)))
        {
            if (ModLoader.ParseJson<PoiPrefab>(entry.Json, new DiagnosticSink(entry.DisplayFile, diagnostics)) is { } prefab)
                prefabs.Add((entry.OwnerModId, entry.DisplayFile, prefab));
        }
        if (definition is null)
        {
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.WorldGenMissing, DefinitionFile, null, null,
                "no enabled mod ships valid world generation rules; the world is flat."));
            return FromDefinition(Flat.Definition, diagnostics);
        }

        var pois = new List<LoadedPoi>();
        var biomeIds = (definition.Biomes ?? []).Select(b => b.Id!).ToList();
        var groundIds = (definition.Grounds ?? []).Select(g => g.Id!).ToList();
        var objectIds = (definition.Objects ?? []).Select(o => o.Id!).ToList();
        var liquidIds = (definition.Liquids ?? []).Select(l => l.Id!).ToList();
        foreach (var (modId, file, prefab) in prefabs)
        {
            var sink = new DiagnosticSink(file, diagnostics);
            WorldGenValidator.Validate(prefab, biomeIds, sink, groundIds, objectIds, liquidIds, content, modId,
                definition.LootTables ?? new Dictionary<string, LootTableDefinition>());
            var id = $"{modId}:{prefab.Id}";
            if (pois.Any(p => p.QualifiedId == id))
                sink.Error(DiagnosticCodes.PoiInvalid, "$.id", $"POI '{id}' is defined twice.");
            if (sink.ErrorCount == 0)
                pois.Add(new LoadedPoi(id, prefab, (prefab.Placement!.Biomes ?? []).Select(b => biomeIds.IndexOf(b)).ToHashSet()));
        }
        var folders = content.Mods.ToDictionary(m => m.Id, m => m.Directory);
        string? AssetFile(string text, string mod, AssetKind kind) =>
            AssetPath.TryParse(text, mod, kind, out var path, out _) && folders.TryGetValue(path.ModId, out var folder)
                ? path.Resolve(folder)
                : null;
        var sites = ResolveSites(definition, definitionMod, pois, new DiagnosticSink($"{definitionMod}/{DefinitionFile}", diagnostics));
        return new WorldGenData(definition, [.. pois.OrderBy(p => p.QualifiedId, StringComparer.Ordinal)], diagnostics, ComputeFingerprint(definition, pois),
            AssetFile, sites, definitionMod);
    }

    // Sites name prefabs that live in their own files, so they are checked once every POI has loaded. A site whose prefab
    // is missing (or failed to load) is left out.
    private static List<LoadedSite> ResolveSites(WorldGenDefinition definition, string definitionMod, IReadOnlyList<LoadedPoi> pois, DiagnosticSink sink)
    {
        LoadedPoi? Find(string? id) => id is null ? null : pois.FirstOrDefault(p => p.QualifiedId == (id.Contains(':') ? id : $"{definitionMod}:{id}"));
        var pools = new Dictionary<string, LoadedPool>();
        for (var i = 0; i < (definition.Pools?.Count ?? 0); i++)
        {
            var pool = definition.Pools![i];
            var entries = new List<LoadedPoi>();
            for (var e = 0; e < (pool.Fill?.Count ?? 0); e++)
            {
                var entry = pool.Fill![e];
                if (Find(entry.Poi) is { } poi)
                    entries.AddRange(Enumerable.Repeat(poi, Math.Max(0, entry.Count)));
                else
                    sink.Error(DiagnosticCodes.WorldGenInvalid, $"$.pools[{i}].fill[{e}].poi", $"'{entry.Poi}' is not a POI that loaded.");
            }
            pools[pool.Id!] = new LoadedPool(pool.Id!, entries);
        }
        // Scatter rules that stand near POIs or keep away from them name prefabs too.
        for (var b = 0; b < (definition.Biomes?.Count ?? 0); b++)
        {
            for (var r = 0; r < (definition.Biomes![b].Scatter?.Count ?? 0); r++)
            {
                var rule = definition.Biomes[b].Scatter![r];
                foreach (var (key, ids) in new[] { ("near", rule.Near?.Pois), ("away", rule.Away?.Pois) })
                {
                    for (var k = 0; k < (ids?.Count ?? 0); k++)
                    {
                        if (Find(ids![k]) is null)
                            sink.Error(DiagnosticCodes.WorldGenInvalid, $"$.biomes[{b}].scatter[{r}].{key}.pois[{k}]", $"'{ids[k]}' is not a POI that loaded.");
                    }
                }
            }
        }
        var biomeIds = (definition.Biomes ?? []).Select(b => b.Id).ToList();
        var sites = new List<LoadedSite>();
        for (var i = 0; i < (definition.Sites?.Count ?? 0); i++)
        {
            var site = definition.Sites![i];
            var biome = site.Biome is { } b ? biomeIds.IndexOf(b) : -1;
            if (site.Pool is { } poolId)
            {
                if (pools.TryGetValue(poolId, out var pool) && pool.Entries.Count == definition.Sites.Count(s => s.Pool == poolId))
                    sites.Add(new LoadedSite(sites.Count, site, null, pool, biome));
                continue;
            }
            if (Find(site.Poi) is { } poi)
                sites.Add(new LoadedSite(sites.Count, site, poi, null, biome));
            else
                sink.Error(DiagnosticCodes.WorldGenInvalid, $"$.sites[{i}].poi", $"'{site.Poi}' is not a POI that loaded.");
        }
        return sites;
    }

    /// <summary>Builds data from a definition alone (no POIs), validating it.</summary>
    /// <exception cref="FormatException">The definition has errors.</exception>
    public static WorldGenData FromDefinition(WorldGenDefinition definition) => FromDefinition(definition, []);

    private static WorldGenData FromDefinition(WorldGenDefinition definition, List<DataDiagnostic> diagnostics)
    {
        var errors = new List<DataDiagnostic>();
        var sink = new DiagnosticSink("worldgen", errors);
        WorldGenValidator.Validate(definition, sink);
        if (sink.ErrorCount > 0)
            throw new FormatException(string.Join("\n", errors.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return new WorldGenData(definition, [], diagnostics, ComputeFingerprint(definition, []));
    }

    /// <summary>Parses <c>#rrggbb</c>; anything else gives grey.</summary>
    public static Vector3 ParseColor(string? text)
    {
        if (!TryParseColor(text, out var color))
            return new Vector3(0.5f);
        return color;
    }

    /// <summary>Parses <c>#rrggbb</c> into 0..1 channels.</summary>
    public static bool TryParseColor(string? text, out Vector3 color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#'
            || !int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return false;
        color = new Vector3((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff) / 255f;
        return true;
    }

    private static bool Valid(WorldGenDefinition definition, DiagnosticSink sink)
    {
        var errors = sink.ErrorCount;
        WorldGenValidator.Validate(definition, sink);
        return sink.ErrorCount == errors;
    }

    private static T? Read<T>(string path, DiagnosticSink sink) where T : class
    {
        try
        {
            return ModLoader.ParseJson<T>(File.ReadAllText(path), sink);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            sink.Error(DiagnosticCodes.FileUnreadable, null, $"could not read file: {e.Message}");
            return null;
        }
    }

    private static string ComputeFingerprint(WorldGenDefinition definition, IEnumerable<LoadedPoi> pois)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(definition, DataJson.WriteOptions)));
        foreach (var poi in pois.OrderBy(p => p.QualifiedId, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(poi.QualifiedId));
            hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(poi.Prefab, DataJson.WriteOptions)));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
