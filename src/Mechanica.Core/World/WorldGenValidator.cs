using System.Numerics;
using Mechanica.Core.Data;

namespace Mechanica.Core.World;

/// <summary>Checks world generation rules and POI prefabs, reporting every problem with its JSON path.</summary>
internal static class WorldGenValidator
{
    private const string Code = DiagnosticCodes.WorldGenInvalid;

    // Steepest ground a player can walk up (the controller's 45° floor angle), as rise over run.
    private const float MaxBasinSlope = 1f;

    public static void Validate(WorldGenDefinition definition, DiagnosticSink sink)
    {
        KnownKeys.WarnUnknown(definition.Extra, "$", sink, typeof(WorldGenDefinition));
        var t = definition.Terrain;
        KnownKeys.WarnUnknown(t.Extra, "$.terrain", sink, typeof(TerrainDefinition));
        Positive(t.Resolution, "$.terrain.resolution", sink);
        if (!(t.SeaFloor <= t.SeaLevel))
            sink.Error(Code, "$.terrain.sea_floor", $"sea_floor ({t.SeaFloor}) must not be above sea_level ({t.SeaLevel}).");
        NonNegative(t.CoastWidth, "$.terrain.coast_width", sink);
        NonNegative(t.CoastBiomeWidth, "$.terrain.coast_biome_width", sink);
        NonNegative(t.CoastWobble, "$.terrain.coast_wobble", sink);
        NonNegative(t.BiomeWarp, "$.terrain.biome_warp", sink);
        Positive(t.NoiseScale, "$.terrain.noise_scale", sink);
        if (t.Octaves is < 1 or > 8)
            sink.Error(Code, "$.terrain.octaves", $"octaves must be 1 to 8 (got {t.Octaves}).");
        if (!(t.Lacunarity > 1f) || !float.IsFinite(t.Lacunarity))
            sink.Error(Code, "$.terrain.lacunarity", $"lacunarity must be greater than 1 (got {t.Lacunarity}).");
        if (!(t.Gain is > 0f and <= 1f))
            sink.Error(Code, "$.terrain.gain", $"gain must be greater than 0 and at most 1 (got {t.Gain}).");
        Positive(t.BiomeCellSize, "$.terrain.biome_cell_size", sink);
        NonNegative(t.BiomeBlend, "$.terrain.biome_blend", sink);
        // Blending only looks at the 3×3 neighbouring cells; a wider blend would reach past them.
        if (t.BiomeBlend > 0.18f * t.BiomeCellSize)
            sink.Error(Code, "$.terrain.biome_blend", $"biome_blend ({t.BiomeBlend}) must be at most 0.18 × biome_cell_size ({0.18f * t.BiomeCellSize}).");
        NonNegative(t.MaxScatterSlope, "$.terrain.max_scatter_slope", sink);

        var objectIds = new HashSet<string>();
        for (var i = 0; i < (definition.Objects?.Count ?? 0); i++)
        {
            var o = definition.Objects![i];
            var path = $"$.objects[{i}]";
            KnownKeys.WarnUnknown(o.Extra, path, sink, typeof(WorldObjectDefinition));
            if (!Identifiers.IsValid(o.Id))
                sink.Error(Code, $"{path}.id", $"object id '{o.Id}' must be lowercase snake_case.");
            else if (!objectIds.Add(o.Id))
                sink.Error(Code, $"{path}.id", $"object '{o.Id}' is defined twice.");
            if (o.Collider is null)
                sink.Error(Code, $"{path}.collider", "an object needs a collider.");
            else
                Shape(o.Collider, $"{path}.collider", sink);
            for (var p = 0; p < (o.Pieces?.Count ?? 0); p++)
                Shape(o.Pieces![p], $"{path}.pieces[{p}]", sink);
            NonNegative(o.Footprint, $"{path}.footprint", sink);
            NonNegative(o.Sink, $"{path}.sink", sink);
            if (o.Scale is { } scale && (scale.Count != 2 || !(scale[0] > 0f) || !(scale[0] <= scale[1]) || !float.IsFinite(scale[1])))
                sink.Error(Code, $"{path}.scale", "scale must be [min, max] with 0 < min <= max.");
            if (o.Health is { } health && !(health > 0 && float.IsFinite(health)))
                sink.Error(Code, $"{path}.health", "health must be greater than 0.");
            if (o.SledgeDamage is { } sledge && !(sledge > 0 && float.IsFinite(sledge)))
                sink.Error(Code, $"{path}.sledge_damage", "sledge_damage must be greater than 0.");
            if (o.SledgeDamage is not null && o.Health is null)
                sink.Warning(Code, $"{path}.sledge_damage", "sledge_damage does nothing: the object has no health, so it cannot break.");
            if (o.Model is { } model && FloraModels.Find(model) is null)
                sink.Error(Code, $"{path}.model", $"'{model}' is not a built-in flora model ({string.Join(", ", FloraModels.All.Select(m => m.Id))}).");
            if (o.Drops is { Count: > 0 } && o.Health is null)
                sink.Warning(Code, $"{path}.drops", "drops are never left: the object has no health, so it cannot break.");
            for (var k = 0; k < (o.DamageFrom?.Count ?? 0); k++)
            {
                if (!DamageKinds.All.Contains(o.DamageFrom![k]))
                    sink.Error(Code, $"{path}.damage_from[{k}]", $"'{o.DamageFrom[k]}' is not a damage kind ({string.Join(", ", DamageKinds.All)}).");
            }
            if (o.BreaksInto is { } breaks)
            {
                Break(breaks, $"{path}.breaks_into", sink, Code);
                if (o.Health is null)
                    sink.Warning(Code, $"{path}.breaks_into", "the object never breaks into anything: it has no health.");
            }
            if (o.Mesh is { } mesh)
            {
                KnownKeys.WarnUnknown(mesh.Extra, $"{path}.mesh", sink, typeof(MeshDefinition));
                if (!AssetPath.TryParse(mesh.File, "mod", AssetKind.Mesh, out _, out var error))
                    sink.Error(Code, $"{path}.mesh.file", error);
                if (mesh.Lod is not null && !AssetPath.TryParse(mesh.Lod, "mod", AssetKind.Mesh, out _, out var lodError))
                    sink.Error(Code, $"{path}.mesh.lod", lodError);
                Positive(mesh.LodDistance, $"{path}.mesh.lod_distance", sink);
            }
            if (o.Fell is { } fell)
            {
                KnownKeys.WarnUnknown(fell.Extra, $"{path}.fell", sink, typeof(FellDefinition));
                Positive(fell.Height, $"{path}.fell.height", sink);
                NonNegative(fell.Spin, $"{path}.fell.spin", sink);
                if (o.BreaksInto is null)
                    sink.Warning(Code, $"{path}.fell", "nothing falls: the object has no breaks_into.");
            }
            foreach (var (key, sound) in o.Sounds ?? [])
            {
                if (!ObjectSounds.All.Contains(key))
                    sink.Error(Code, $"{path}.sounds.{key}", $"'{key}' is not an object sound ({string.Join(", ", ObjectSounds.All)}).");
                if (!PartValidator.IsItemRef(sound))
                    sink.Error(Code, $"{path}.sounds.{key}", "a sound must be an event id or 'mod_id:event_id'.");
            }
            if (o.Debris is { } debris)
            {
                KnownKeys.WarnUnknown(debris.Extra, $"{path}.debris", sink, typeof(DebrisDefinition));
                for (var c = 0; c < (debris.Chips?.Count ?? 0); c++)
                    Color(debris.Chips![c], $"{path}.debris.chips[{c}]", sink);
                for (var c = 0; c < (debris.Leaves?.Count ?? 0); c++)
                    Color(debris.Leaves![c], $"{path}.debris.leaves[{c}]", sink);
                NonNegative(debris.CanopyRadius, $"{path}.debris.canopy_radius", sink);
            }
            if (o.MaxSlope is { } maxSlope && !(maxSlope >= 0 && float.IsFinite(maxSlope)))
                sink.Error(Code, $"{path}.max_slope", "max_slope must be 0 or more.");
            if (o.Outcrop is { } outcrop)
            {
                KnownKeys.WarnUnknown(outcrop.Extra, $"{path}.outcrop", sink, typeof(OutcropDefinition));
                if (outcrop.Cubes is not { Count: 2 } cubes || cubes[0] < 1 || cubes[1] < cubes[0] || cubes[1] > OutcropLayout.MaxCubes)
                    sink.Error(Code, $"{path}.outcrop.cubes", $"cubes must be [min, max] with 1 <= min <= max <= {OutcropLayout.MaxCubes}.");
                if (outcrop.Height is not { Count: 2 } height || height[0] < 1 || height[1] < height[0] || height[1] > OutcropLayout.MaxHeight)
                    sink.Error(Code, $"{path}.outcrop.height", $"height must be [min, max] with 1 <= min <= max <= {OutcropLayout.MaxHeight}.");
            }
            for (var d = 0; d < (o.Drops?.Count ?? 0); d++)
            {
                var drop = o.Drops![d];
                KnownKeys.WarnUnknown(drop.Extra, $"{path}.drops[{d}]", sink, typeof(ObjectDrop));
                if (!PartValidator.IsItemRef(drop.Item))
                    sink.Error(Code, $"{path}.drops[{d}].item", "item must be an item id or 'mod_id:item_id'.");
                if (drop.Count < 1)
                    sink.Error(Code, $"{path}.drops[{d}].count", "count must be 1 or more.");
            }
        }

        var groundIds = new HashSet<string>();
        var grounds = definition.Grounds ?? [];
        if (grounds.Count > GroundDefinition.MaxGrounds)
            sink.Error(Code, "$.grounds", $"at most {GroundDefinition.MaxGrounds} grounds (got {grounds.Count}).");
        for (var i = 0; i < grounds.Count; i++)
        {
            var g = grounds[i];
            var path = $"$.grounds[{i}]";
            KnownKeys.WarnUnknown(g.Extra, path, sink, typeof(GroundDefinition));
            if (!Identifiers.IsValid(g.Id))
                sink.Error(Code, $"{path}.id", $"ground id '{g.Id}' must be lowercase snake_case.");
            else if (!groundIds.Add(g.Id))
                sink.Error(Code, $"{path}.id", $"ground '{g.Id}' is defined twice.");
            if (!AssetPath.TryParse(g.Texture, "mod", AssetKind.Texture, out _, out var error))
                sink.Error(Code, $"{path}.texture", error);
            if (!(g.Tile is >= 0.25f and <= 256f))
                sink.Error(Code, $"{path}.tile", $"tile must be 0.25 to 256 metres (got {g.Tile}).");
            if (g.FloraTint is not null)
                Color(g.FloraTint, $"{path}.flora_tint", sink);
        }
        if (definition.SlopeGround is { } slope)
        {
            KnownKeys.WarnUnknown(slope.Extra, "$.slope_ground", sink, typeof(SlopeGroundDefinition));
            GroundRef(slope.Ground, groundIds, "$.slope_ground.ground", sink, Code);
            if (!(slope.From >= 0f && slope.To > slope.From && float.IsFinite(slope.To)))
                sink.Error(Code, "$.slope_ground", $"slopes must satisfy 0 <= from < to (got {slope.From}, {slope.To}).");
        }

        var biomeIds = new HashSet<string>();
        var biomes = definition.Biomes ?? [];
        if (biomes.Count == 0)
            sink.Error(Code, "$.biomes", "at least one biome is needed.");
        for (var i = 0; i < biomes.Count; i++)
        {
            var b = biomes[i];
            var path = $"$.biomes[{i}]";
            KnownKeys.WarnUnknown(b.Extra, path, sink, typeof(BiomeDefinition));
            if (!Identifiers.IsValid(b.Id))
                sink.Error(Code, $"{path}.id", $"biome id '{b.Id}' must be lowercase snake_case.");
            else if (!biomeIds.Add(b.Id))
                sink.Error(Code, $"{path}.id", $"biome '{b.Id}' is defined twice.");
            NonNegative(b.Weight, $"{path}.weight", sink);
            NonNegative(b.Relief, $"{path}.relief", sink);
            if (!float.IsFinite(b.Height))
                sink.Error(Code, $"{path}.height", "height must be a number.");
            Color(b.Color, $"{path}.color", sink);
            if (b.Ground is not null)
                GroundRef(b.Ground, groundIds, $"{path}.ground", sink, Code);
            for (var r = 0; r < (b.Scatter?.Count ?? 0); r++)
            {
                var rule = b.Scatter![r];
                var rulePath = $"{path}.scatter[{r}]";
                KnownKeys.WarnUnknown(rule.Extra, rulePath, sink, typeof(ScatterRule));
                if (rule.Object is null || !objectIds.Contains(rule.Object))
                    sink.Error(Code, $"{rulePath}.object", $"'{rule.Object}' is not one of the objects defined in this file.");
                Positive(rule.Spacing, $"{rulePath}.spacing", sink);
                if (!(rule.Chance is >= 0f and <= 1f))
                    sink.Error(Code, $"{rulePath}.chance", $"chance must be 0 to 1 (got {rule.Chance}).");
            }
            for (var r = 0; r < (b.Flora?.Count ?? 0); r++)
                Flora(b.Flora![r], $"{path}.flora[{r}]", sink);
        }
        if (biomes.Count > 0 && !biomes.Any(b => b.Weight > 0f && b.Id != definition.CoastBiome))
            sink.Error(Code, "$.biomes", "at least one inland biome needs a weight above 0.");
        if (definition.CoastBiome is { } coast && !biomeIds.Contains(coast))
            sink.Error(Code, "$.coast_biome", $"'{coast}' is not one of the biomes.");
        for (var i = 0; i < biomes.Count; i++)
        {
            if (!(biomes[i].Ridge is >= 0f and <= 1f))
                sink.Error(Code, $"$.biomes[{i}].ridge", $"ridge must be 0 to 1 (got {biomes[i].Ridge}).");
            NonNegative(biomes[i].Terrace, $"$.biomes[{i}].terrace", sink);
        }
        NonNegative(t.RegionWarp, "$.terrain.region_warp", sink);
        Layout(definition, biomeIds, groundIds, sink);
    }

    // Regions, rivers, roads, sites and pools: the world map's fixed layout (World Map spec).
    private static void Layout(WorldGenDefinition definition, HashSet<string> biomeIds, HashSet<string> groundIds, DiagnosticSink sink)
    {
        for (var i = 0; i < (definition.Regions?.Count ?? 0); i++)
        {
            var r = definition.Regions![i];
            var path = $"$.regions[{i}]";
            KnownKeys.WarnUnknown(r.Extra, path, sink, typeof(RegionDefinition));
            BiomeRef(r.Biome, biomeIds, $"{path}.biome", sink);
            Fraction(r.At, $"{path}.at", sink);
            Positive(r.Size, $"{path}.size", sink);
            var total = 0f;
            for (var m = 0; m < (r.Mix?.Count ?? 0); m++)
            {
                var mix = r.Mix![m];
                KnownKeys.WarnUnknown(mix.Extra, $"{path}.mix[{m}]", sink, typeof(RegionMix));
                BiomeRef(mix.Biome, biomeIds, $"{path}.mix[{m}].biome", sink);
                if (!(mix.Chance is >= 0f and <= 1f))
                    sink.Error(Code, $"{path}.mix[{m}].chance", $"chance must be 0 to 1 (got {mix.Chance}).");
                total += mix.Chance;
            }
            if (total > 1f + 1e-4f)
                sink.Error(Code, $"{path}.mix", $"the chances add up to {total}, over 1.");
        }
        for (var i = 0; i < (definition.Rivers?.Count ?? 0); i++)
        {
            var r = definition.Rivers![i];
            var path = $"$.rivers[{i}]";
            KnownKeys.WarnUnknown(r.Extra, path, sink, typeof(RiverDefinition));
            if (r.Points is not { Count: >= 2 } points)
                sink.Error(Code, $"{path}.points", "a river needs at least two points.");
            else
            {
                for (var k = 0; k < points.Count; k++)
                    Fraction(points[k], $"{path}.points[{k}]", sink, 0.6f);
            }
            if (r.Width is not { Count: 2 } width || !(width[0] > 0f) || !(width[1] > 0f) || !float.IsFinite(width[0] + width[1]))
                sink.Error(Code, $"{path}.width", "width must be [source, mouth], both above 0.");
            Positive(r.Depth, $"{path}.depth", sink);
            NonNegative(r.Meander, $"{path}.meander", sink);
            if (!(r.ValleySlope is > 0f and <= 1f))
                sink.Error(Code, $"{path}.valley_slope", $"valley_slope must be above 0 and at most 1 (got {r.ValleySlope}).");
        }

        var siteIds = new HashSet<string>();
        var pools = definition.Pools ?? [];
        var poolIds = new HashSet<string>();
        for (var i = 0; i < pools.Count; i++)
        {
            var pool = pools[i];
            var path = $"$.pools[{i}]";
            KnownKeys.WarnUnknown(pool.Extra, path, sink, typeof(PoolDefinition));
            if (!Identifiers.IsValid(pool.Id))
                sink.Error(Code, $"{path}.id", $"pool id '{pool.Id}' must be lowercase snake_case.");
            else if (!poolIds.Add(pool.Id))
                sink.Error(Code, $"{path}.id", $"pool '{pool.Id}' is defined twice.");
            if (pool.Fill is not { Count: > 0 })
                sink.Error(Code, $"{path}.fill", "a pool needs at least one entry.");
            for (var e = 0; e < (pool.Fill?.Count ?? 0); e++)
            {
                KnownKeys.WarnUnknown(pool.Fill![e].Extra, $"{path}.fill[{e}]", sink, typeof(PoolEntry));
                if (!PoiRef(pool.Fill[e].Poi))
                    sink.Error(Code, $"{path}.fill[{e}].poi", $"'{pool.Fill[e].Poi}' is not a POI id (id or mod_id:id).");
                if (pool.Fill[e].Count < 1)
                    sink.Error(Code, $"{path}.fill[{e}].count", "count must be 1 or more.");
            }
            var used = definition.Sites?.Count(s => s.Pool == pool.Id) ?? 0;
            var filled = pool.Fill?.Sum(f => Math.Max(0, f.Count)) ?? 0;
            if (pool.Id is not null && used != filled)
                sink.Error(Code, $"{path}.fill", $"the pool holds {filled} prefabs but {used} sites draw from it; they must match.");
        }
        for (var i = 0; i < (definition.Sites?.Count ?? 0); i++)
        {
            var site = definition.Sites![i];
            var path = $"$.sites[{i}]";
            KnownKeys.WarnUnknown(site.Extra, path, sink, typeof(SiteDefinition));
            if (!Identifiers.IsValid(site.Id))
                sink.Error(Code, $"{path}.id", $"site id '{site.Id}' must be lowercase snake_case.");
            else if (!siteIds.Add(site.Id))
                sink.Error(Code, $"{path}.id", $"site '{site.Id}' is defined twice.");
            if ((site.Poi is null) == (site.Pool is null))
                sink.Error(Code, path, "give exactly one of poi or pool.");
            if (site.Poi is not null && !PoiRef(site.Poi))
                sink.Error(Code, $"{path}.poi", $"'{site.Poi}' is not a POI id (id or mod_id:id).");
            if (site.Pool is { } pool && !poolIds.Contains(pool))
                sink.Error(Code, $"{path}.pool", $"'{pool}' is not one of the pools.");
            Fraction(site.At, $"{path}.at", sink);
            NonNegative(site.Jitter, $"{path}.jitter", sink);
            if (site.Biome is not null)
                BiomeRef(site.Biome, biomeIds, $"{path}.biome", sink);
            if (site.Yaw % 90f != 0f)
                sink.Error(Code, $"{path}.yaw", $"yaw must be a multiple of 90 (got {site.Yaw}).");
            if (site.PadHeight is { } pad && !float.IsFinite(pad))
                sink.Error(Code, $"{path}.pad_height", "pad_height must be a number.");
            if (site.Road is { } road && !RoadLinks.All.Contains(road))
                sink.Error(Code, $"{path}.road", $"'{road}' is not a road link ({string.Join(", ", RoadLinks.All)}).");
        }

        if (definition.Roads is { } roads)
        {
            const string path = "$.roads";
            KnownKeys.WarnUnknown(roads.Extra, path, sink, typeof(RoadsDefinition));
            Positive(roads.Width, $"{path}.width", sink);
            NonNegative(roads.Curb, $"{path}.curb", sink);
            NonNegative(roads.Shoulder, $"{path}.shoulder", sink);
            if (!(roads.MaxGrade is > 0f and <= 1f))
                sink.Error(Code, $"{path}.max_grade", $"max_grade must be above 0 and at most 1 (got {roads.MaxGrade}).");
            if (!(roads.TrackMaxGrade is > 0f and <= 1f))
                sink.Error(Code, $"{path}.track_max_grade", $"track_max_grade must be above 0 and at most 1 (got {roads.TrackMaxGrade}).");
            // Finer than this and the routing grid outgrows a 12 km world; coarser and roads cut corners off hills.
            if (!(roads.Cell is >= 8f and <= 128f))
                sink.Error(Code, $"{path}.cell", $"cell must be 8 to 128 metres (got {roads.Cell}).");
            NonNegative(roads.BridgeClearance, $"{path}.bridge_clearance", sink);
            if (!(roads.BridgeCost >= 1f) || !float.IsFinite(roads.BridgeCost))
                sink.Error(Code, $"{path}.bridge_cost", $"bridge_cost must be 1 or more (got {roads.BridgeCost}).");
            if (!(roads.LoopFactor >= 1f) || !float.IsFinite(roads.LoopFactor))
                sink.Error(Code, $"{path}.loop_factor", $"loop_factor must be 1 or more (got {roads.LoopFactor}).");
            NonNegative(roads.LoopReach, $"{path}.loop_reach", sink);
            if (roads.MaxLoops < 0)
                sink.Error(Code, $"{path}.max_loops", "max_loops must be 0 or more.");
            Positive(roads.TrackWidth, $"{path}.track_width", sink);
            NonNegative(roads.TrackReach, $"{path}.track_reach", sink);
            if (roads.ShoulderGround is not null)
                GroundRef(roads.ShoulderGround, groundIds, $"{path}.shoulder_ground", sink, Code);
            if (roads.TrackGround is not null)
                GroundRef(roads.TrackGround, groundIds, $"{path}.track_ground", sink, Code);
            for (var i = 0; i < (roads.Links?.Count ?? 0); i++)
            {
                var link = roads.Links![i];
                if (link is not { Count: 2 } || !link.All(siteIds.Contains))
                    sink.Error(Code, $"{path}.links[{i}]", "a link is a pair of site ids.");
            }
        }
    }

    private static bool PoiRef(string? id) => id is not null && id.Split(':') is { Length: 1 or 2 } parts && parts.All(Identifiers.IsValid);

    private static void BiomeRef(string? id, HashSet<string> biomeIds, string path, DiagnosticSink sink)
    {
        if (id is null || !biomeIds.Contains(id))
            sink.Error(Code, path, $"'{id}' is not one of the biomes.");
    }

    // [x, z] as fractions of the world, -0.5 (west, north) to 0.5 (east, south); rivers may reach a little past the edge.
    private static void Fraction(List<float>? at, string path, DiagnosticSink sink, float reach = 0.5f)
    {
        if (at is not { Count: 2 } || !at.All(v => v >= -reach && v <= reach))
            sink.Error(Code, path, $"must be [x, z], each from -{reach} to {reach} (fractions of the world).");
    }

    public static void Validate(PoiPrefab prefab, IReadOnlyList<string> biomeIds, DiagnosticSink sink, IReadOnlyCollection<string> groundIds)
    {
        const string code = DiagnosticCodes.PoiInvalid;
        KnownKeys.WarnUnknown(prefab.Extra, "$", sink, typeof(PoiPrefab));
        if (!Identifiers.IsValid(prefab.Id))
            sink.Error(code, "$.id", $"POI id '{prefab.Id}' must be lowercase snake_case.");
        if (!(prefab.Radius > 0f) || !float.IsFinite(prefab.Radius))
            sink.Error(code, "$.radius", $"radius must be greater than 0 (got {prefab.Radius}).");

        if (prefab.Placement is not { } placement)
            sink.Error(code, "$.placement", "a POI needs placement rules.");
        else
        {
            KnownKeys.WarnUnknown(placement.Extra, "$.placement", sink, typeof(PoiPlacement));
            if (placement.Fixed is not null && placement.Spacing is not null)
                sink.Error(code, "$.placement", "give at most one of fixed or spacing (neither: only sites place it).");
            if (placement.Fixed is { } at && (at.Count != 2 || !at.All(float.IsFinite)))
                sink.Error(code, "$.placement.fixed", "fixed must be [x, z].");
            if (placement.Yaw % 90f != 0f)
                sink.Error(code, "$.placement.yaw", $"yaw must be a multiple of 90 (got {placement.Yaw}).");
            if (placement.Spacing is { } spacing)
            {
                if (spacing.Count != 2 || !(spacing[0] > 0f) || !(spacing[0] <= spacing[1]) || !float.IsFinite(spacing[1]))
                    sink.Error(code, "$.placement.spacing", "spacing must be [min, max] with 0 < min <= max.");
                else if (spacing[0] < 2f * prefab.Radius)
                    sink.Error(code, "$.placement.spacing", $"the minimum spacing ({spacing[0]}) must be at least twice the radius ({prefab.Radius}).");
                if (placement.Biomes is not { Count: > 0 })
                    sink.Error(code, "$.placement.biomes", "a spaced POI needs at least one biome.");
            }
            NonNegative(placement.Clearance, "$.placement.clearance", sink, code);
            for (var i = 0; i < (placement.Biomes?.Count ?? 0); i++)
            {
                if (!biomeIds.Contains(placement.Biomes![i]))
                    sink.Error(code, $"$.placement.biomes[{i}]", $"'{placement.Biomes[i]}' is not a biome in the world generation rules.");
            }
        }

        for (var i = 0; i < (prefab.Terrain?.Count ?? 0); i++)
        {
            var op = prefab.Terrain![i];
            var path = $"$.terrain[{i}]";
            KnownKeys.WarnUnknown(op.Extra, path, sink, typeof(TerrainOp));
            if (op.Op is null)
                sink.Error(code, $"{path}.op", "op is required (flatten or basin).");
            NonNegative(op.Radius, $"{path}.radius", sink, code);
            NonNegative(op.Blend, $"{path}.blend", sink, code);
            NonNegative(op.Depth, $"{path}.depth", sink, code);
            var offset = Vector2.Zero;
            if (op.At is { } at)
            {
                if (at.Count != 2 || !at.All(float.IsFinite))
                    sink.Error(code, $"{path}.at", "at must be [x, z].");
                else
                    offset = new Vector2(at[0], at[1]);
            }
            // A basin's bank is steepest halfway through the blend (smoothstep: 1.5 × depth / blend). Steeper than
            // the 45° a player can walk up, a basin below sea level is a pond nobody can climb out of.
            if (op.Op == TerrainOpKind.Basin && op.Depth > 0f && !(1.5f * op.Depth <= MaxBasinSlope * op.Blend))
                sink.Error(code, $"{path}.blend", $"a basin {op.Depth} m deep needs a blend of at least {1.5f * op.Depth / MaxBasinSlope:0.##} m, or its bank is too steep to climb out of (got {op.Blend}).");
            // Shaping must stay inside the footprint, or it would reach under a neighbouring POI.
            if (offset.Length() + op.Radius + op.Blend > prefab.Radius + 1e-3f)
                sink.Error(code, path, $"reaches {offset.Length() + op.Radius + op.Blend:0.##} m from the centre, past the POI radius ({prefab.Radius}).");
        }
        for (var i = 0; i < (prefab.Ground?.Count ?? 0); i++)
        {
            var paint = prefab.Ground![i];
            var path = $"$.ground[{i}]";
            KnownKeys.WarnUnknown(paint.Extra, path, sink, typeof(GroundPaint));
            GroundRef(paint.Ground, groundIds, $"{path}.ground", sink, code);
            NonNegative(paint.Radius, $"{path}.radius", sink, code);
            NonNegative(paint.Blend, $"{path}.blend", sink, code);
            NonNegative(paint.Noise, $"{path}.noise", sink, code);
            var offset = Vector2.Zero;
            if (paint.At is { } at)
            {
                if (at.Count != 2 || !at.All(float.IsFinite))
                    sink.Error(code, $"{path}.at", "at must be [x, z].");
                else
                    offset = new Vector2(at[0], at[1]);
            }
            if (offset.Length() + paint.Radius + paint.Blend > prefab.Radius + 1e-3f)
                sink.Error(code, path, $"reaches {offset.Length() + paint.Radius + paint.Blend:0.##} m from the centre, past the POI radius ({prefab.Radius}).");
        }
        if (prefab.Road is { } road && !RoadLinks.All.Contains(road))
            sink.Error(code, "$.road", $"'{road}' is not a road link ({string.Join(", ", RoadLinks.All)}).");
        if (prefab.Size is { } size && !(size.X > 0f && size.Y > 0f && size.Z > 0f))
            sink.Error(code, "$.size", "size must be [width, height, depth], each above 0.");
        if (prefab.Flora is { } flora && !biomeIds.Contains(flora))
            sink.Error(code, "$.flora", $"'{flora}' is not a biome in the world generation rules.");
        for (var i = 0; i < (prefab.Pieces?.Count ?? 0); i++)
            Shape(prefab.Pieces![i], $"$.pieces[{i}]", sink, code);
        for (var i = 0; i < (prefab.Markers?.Count ?? 0); i++)
        {
            var marker = prefab.Markers![i];
            var path = $"$.markers[{i}]";
            KnownKeys.WarnUnknown(marker.Extra, path, sink, typeof(PoiMarker));
            if (!Identifiers.IsValid(marker.Type))
                sink.Error(code, $"{path}.type", $"marker type '{marker.Type}' must be lowercase snake_case.");
            if (marker.Ref is { } reference && !reference.Split(':').All(Identifiers.IsValid))
                sink.Error(code, $"{path}.ref", $"'{reference}' is not an id (id or mod_id:id).");
        }
    }

    private static void GroundRef(string? id, IReadOnlyCollection<string> groundIds, string path, DiagnosticSink sink, string code)
    {
        if (id is null || !groundIds.Contains(id))
            sink.Error(code, path, $"'{id}' is not one of the grounds in the world generation rules.");
    }

    private static void Flora(FloraRule rule, string path, DiagnosticSink sink)
    {
        KnownKeys.WarnUnknown(rule.Extra, path, sink, typeof(FloraRule));
        if (rule.Kind is null)
            sink.Error(Code, $"{path}.kind", "kind is required (grass, ground_cover or bush).");
        // Plants are scattered per square metre of ground; more than this many is a typo, and would stall the game.
        if (!float.IsFinite(rule.Density) || rule.Density < 0f || rule.Density > 20f)
            sink.Error(Code, $"{path}.density", $"density must be 0 to 20 plants per square metre (got {rule.Density}).");
        if (!(rule.Coverage is >= 0f and <= 1f))
            sink.Error(Code, $"{path}.coverage", $"coverage must be 0 to 1 (got {rule.Coverage}).");
        Positive(rule.PatchSize, $"{path}.patch_size", sink);
        NonNegative(rule.MaxSlope, $"{path}.max_slope", sink);
        if (rule.Scale is { } scale && (scale.Count != 2 || !(scale[0] > 0f) || !(scale[0] <= scale[1]) || !float.IsFinite(scale[1])))
            sink.Error(Code, $"{path}.scale", "scale must be [min, max] with 0 < min <= max.");
        for (var m = 0; m < (rule.Models?.Count ?? 0); m++)
        {
            var model = FloraModels.Find(rule.Models![m]);
            if (model is null)
                sink.Error(Code, $"{path}.models[{m}]", $"'{rule.Models[m]}' is not a built-in flora model ({string.Join(", ", FloraModels.All.Select(x => x.Id))}).");
            else if (rule.Kind is { } k && model.Kind != k)
                sink.Error(Code, $"{path}.models[{m}]", $"'{model.Id}' is a {Kind(model.Kind)} model, not {Kind(k)}.");
        }
        for (var c = 0; c < (rule.Colors?.Count ?? 0); c++)
        {
            if (!WorldGenData.TryParseColor(rule.Colors![c], out _))
                sink.Error(Code, $"{path}.colors[{c}]", $"'{rule.Colors[c]}' is not a colour; use #rrggbb.");
        }
    }

    private static string Kind(FloraKind kind) => kind switch { FloraKind.GroundCover => "ground_cover", _ => kind.ToString().ToLowerInvariant() };

    private static void Shape(ShapeDefinition shape, string path, DiagnosticSink sink, string code = Code)
    {
        KnownKeys.WarnUnknown(shape.Extra, path, sink, typeof(ShapeDefinition));
        switch (shape.Shape)
        {
            case PrimitiveShape.Box:
                if (shape.Size is not { } size || !(size.X > 0f && size.Y > 0f && size.Z > 0f))
                    sink.Error(code, $"{path}.size", "a box needs a size with every side above 0.");
                break;
            case PrimitiveShape.Sphere:
                if (!(shape.Radius > 0f))
                    sink.Error(code, $"{path}.radius", "a sphere needs a radius above 0.");
                break;
            case PrimitiveShape.Cylinder:
                if (!(shape.Radius > 0f) || !(shape.Height > 0f))
                    sink.Error(code, path, "a cylinder needs a radius and a height above 0.");
                break;
            default:
                sink.Error(code, $"{path}.shape", "shape is required (box, sphere or cylinder).");
                break;
        }
        Color(shape.Color, $"{path}.color", sink, code);
    }

    private static void Color(string? color, string path, DiagnosticSink sink, string code = Code)
    {
        if (color is not null && !WorldGenData.TryParseColor(color, out _))
            sink.Error(code, path, $"'{color}' is not a colour; use #rrggbb.");
    }

    /// <summary>Checks a <see cref="BreakDefinition"/> (an object's or a part's <c>breaks_into</c>).</summary>
    internal static void Break(BreakDefinition breaks, string path, DiagnosticSink sink, string code)
    {
        KnownKeys.WarnUnknown(breaks.Extra, path, sink, typeof(BreakDefinition));
        if (breaks.Count is < 1 or > BreakDefinition.MaxCount)
            sink.Error(code, $"{path}.count", $"count must be 1 to {BreakDefinition.MaxCount} (got {breaks.Count}).");
        if (breaks.CountMax is { } most && (most < breaks.Count || most > BreakDefinition.MaxCount))
            sink.Error(code, $"{path}.count_max", $"count_max must be count ({breaks.Count}) to {BreakDefinition.MaxCount} (got {most}).");
        if (breaks.Parts is not { Count: > 0 })
            sink.Error(code, $"{path}.parts", "parts needs at least one part.");
        for (var p = 0; p < (breaks.Parts?.Count ?? 0); p++)
        {
            var piece = breaks.Parts![p];
            KnownKeys.WarnUnknown(piece.Extra, $"{path}.parts[{p}]", sink, typeof(BreakPiece));
            if (!PartValidator.IsItemRef(piece.Part))
                sink.Error(code, $"{path}.parts[{p}].part", "part must be a part id or 'mod_id:part_id'.");
            if (!(piece.Weight > 0 && float.IsFinite(piece.Weight)))
                sink.Error(code, $"{path}.parts[{p}].weight", "weight must be greater than 0.");
        }
    }

    private static void Positive(float value, string path, DiagnosticSink sink)
    {
        if (!float.IsFinite(value) || value <= 0f)
            sink.Error(Code, path, $"must be greater than 0 (got {value}).");
    }

    private static void NonNegative(float value, string path, DiagnosticSink sink, string code = Code)
    {
        if (!float.IsFinite(value) || value < 0f)
            sink.Error(code, path, $"must be 0 or more (got {value}).");
    }
}
