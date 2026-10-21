using System.Numerics;
using Mechanica.Core.Data;

namespace Mechanica.Core.World;

/// <summary>Checks world generation rules and POI prefabs, reporting every problem with its JSON path.</summary>
internal static class WorldGenValidator
{
    private const string Code = DiagnosticCodes.WorldGenInvalid;

    // Steepest ground a player can walk up (the controller's 45° floor angle), as rise over run.
    private const float MaxBasinSlope = 1f;

    // How far, in cell sizes, the biome blend and shore ramp may reach: the 5×5 cells round a point hold every cell that close.
    private const float MaxBlendCells = 1.1f;

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
        // Blending looks at the 5×5 neighbouring cells at most; a wider blend would reach past them.
        if (t.BiomeBlend > MaxBlendCells * t.BiomeCellSize)
            sink.Error(Code, "$.terrain.biome_blend", $"biome_blend ({t.BiomeBlend}) must be at most {MaxBlendCells} × biome_cell_size ({MaxBlendCells * t.BiomeCellSize}).");
        NonNegative(t.BiomeEdgeNoise, "$.terrain.biome_edge_noise", sink);
        Positive(t.BiomeEdgeScale, "$.terrain.biome_edge_scale", sink);
        // The noise pulls a biome's edge back by up to its amount; more than this could leave a sample with no biome at all.
        if (t.BiomeEdgeNoise > 0.6f * t.BiomeBlend)
            sink.Error(Code, "$.terrain.biome_edge_noise", $"biome_edge_noise ({t.BiomeEdgeNoise}) must be at most 0.6 × biome_blend ({0.6f * t.BiomeBlend}).");
        NonNegative(t.MaxScatterSlope, "$.terrain.max_scatter_slope", sink);

        foreach (var (id, table) in definition.LootTables ?? [])
            LootTable(id, table, $"$.loot_tables.{id}", sink);

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
            if (o.Collider is null && o.Scene is null)
                sink.Error(Code, $"{path}.collider", "an object needs a collider.");
            else if (o.Collider is not null)
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
            if (o.Drops is { Count: > 0 } && o.Health is null && !o.Harvest)
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
                for (var v = 0; v < (mesh.Variants?.Count ?? 0); v++)
                {
                    if (!AssetPath.TryParse(mesh.Variants![v], "mod", AssetKind.Mesh, out _, out var variantError))
                        sink.Error(Code, $"{path}.mesh.variants[{v}]", variantError);
                }
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
            for (var d = 0; d < (o.Drops?.Count ?? 0); d++)
            {
                var drop = o.Drops![d];
                KnownKeys.WarnUnknown(drop.Extra, $"{path}.drops[{d}]", sink, typeof(ObjectDrop));
                if (!PartValidator.IsItemRef(drop.Item))
                    sink.Error(Code, $"{path}.drops[{d}].item", "item must be an item id or 'mod_id:item_id'.");
                if (drop.Count < 1)
                    sink.Error(Code, $"{path}.drops[{d}].count", "count must be 1 or more.");
                if (drop.CountMax is { } most && most < drop.Count)
                    sink.Error(Code, $"{path}.drops[{d}].count_max", $"count_max must be at least count ({drop.Count}).");
                if (drop.Chance is { } chance && !(chance is >= 0f and <= 1f))
                    sink.Error(Code, $"{path}.drops[{d}].chance", "chance must be from 0 to 1.");
            }
            if (o.Loot is { } loot)
            {
                KnownKeys.WarnUnknown(loot.Extra, $"{path}.loot", sink, typeof(LootRoll));
                if (loot.Table is null || definition.LootTables?.ContainsKey(loot.Table) != true)
                    sink.Error(Code, $"{path}.loot.table", $"'{loot.Table}' is not one of the loot_tables in this file.");
                if (loot.Picks < 1)
                    sink.Error(Code, $"{path}.loot.picks", "picks must be 1 or more.");
                if (loot.PicksMax is { } most && most < loot.Picks)
                    sink.Error(Code, $"{path}.loot.picks_max", $"picks_max must be at least picks ({loot.Picks}).");
                else if (loot.Table is { } tableId && definition.LootTables?.GetValueOrDefault(tableId)?.Items is { } entries
                    && (loot.PicksMax ?? loot.Picks) > entries.Count)
                    sink.Warning(Code, $"{path}.loot", $"the table '{tableId}' has only {entries.Count} entries, so at most that many are picked.");
                if (o.Health is null)
                    sink.Warning(Code, $"{path}.loot", "loot is never left: the object has no health, so it cannot break.");
            }
            if (o.Glow is { } glow)
            {
                KnownKeys.WarnUnknown(glow.Extra, $"{path}.glow", sink, typeof(ObjectGlow));
                Color(glow.Color, $"{path}.glow.color", sink);
                NonNegative(glow.Energy, $"{path}.glow.energy", sink);
                Positive(glow.Range, $"{path}.glow.range", sink);
                NonNegative(glow.Pulse, $"{path}.glow.pulse", sink);
            }
            if (o.Mesh?.Clips is { } clips)
            {
                KnownKeys.WarnUnknown(clips.Extra, $"{path}.mesh.clips", sink, typeof(ObjectClips));
                Positive(clips.BreakSeconds, $"{path}.mesh.clips.break_seconds", sink);
                NonNegative(clips.FadeSeconds, $"{path}.mesh.clips.fade_seconds", sink);
                if (clips.FadeSeconds > clips.BreakSeconds)
                    sink.Error(Code, $"{path}.mesh.clips.fade_seconds", $"fade_seconds must be at most break_seconds ({clips.BreakSeconds}).");
                if (o.Mesh.Variants is { Count: > 0 } || o.Mesh.Lod is not null)
                    sink.Warning(Code, $"{path}.mesh.clips", "an animated model draws only its file: variants and lod are not used.");
            }
            if (o.Harvest && o.Drops is not { Count: > 0 })
                sink.Warning(Code, $"{path}.harvest", "harvesting gives nothing: the object has no drops.");
            if (o.Harvest && o.Refine)
                sink.Error(Code, $"{path}.harvest", "an object is either refined (held Use) or harvested (pressed Use), not both.");
            if (o.Regrow is { } regrow && !(regrow > 0 && float.IsFinite(regrow)))
                sink.Error(Code, $"{path}.regrow", "regrow must be greater than 0 seconds.");
        }

        var liquidIds = new HashSet<string>();
        for (var i = 0; i < (definition.Liquids?.Count ?? 0); i++)
        {
            var liquid = definition.Liquids![i];
            var path = $"$.liquids[{i}]";
            KnownKeys.WarnUnknown(liquid.Extra, path, sink, typeof(LiquidDefinition));
            if (!Identifiers.IsValid(liquid.Id))
                sink.Error(Code, $"{path}.id", $"liquid id '{liquid.Id}' must be lowercase snake_case.");
            else if (!liquidIds.Add(liquid.Id))
                sink.Error(Code, $"{path}.id", $"liquid '{liquid.Id}' is defined twice.");
            Color(liquid.Color, $"{path}.color", sink);
            NonNegative(liquid.Glow, $"{path}.glow", sink);
            if (!(liquid.Roughness is >= 0f and <= 1f))
                sink.Error(Code, $"{path}.roughness", $"roughness must be 0 to 1 (got {liquid.Roughness}).");
            if (!(liquid.RippleSize > 0f) || !float.IsFinite(liquid.RippleSize))
                sink.Error(Code, $"{path}.ripple_size", $"ripple_size must be greater than 0 (got {liquid.RippleSize}).");
            if (!(liquid.Ripples is >= 0f and <= 1f))
                sink.Error(Code, $"{path}.ripples", $"ripples must be 0 to 1 (got {liquid.Ripples}).");
            NonNegative(liquid.Flow, $"{path}.flow", sink);
            if (!(liquid.Sheen is >= 0f and <= 1f))
                sink.Error(Code, $"{path}.sheen", $"sheen must be 0 to 1 (got {liquid.Sheen}).");
            if (!(liquid.Bubbles is >= 0f and <= 1f))
                sink.Error(Code, $"{path}.bubbles", $"bubbles must be 0 to 1 (got {liquid.Bubbles}).");
            if (liquid.EdgeColor is not null)
                Color(liquid.EdgeColor, $"{path}.edge_color", sink);
            NonNegative(liquid.Damage, $"{path}.damage", sink);
            if (liquid.Collect is { } collect)
            {
                KnownKeys.WarnUnknown(collect.Extra, $"{path}.collect", sink, typeof(ObjectDrop));
                if (!PartValidator.IsItemRef(collect.Item))
                    sink.Error(Code, $"{path}.collect.item", "item must be an item id or 'mod_id:item_id'.");
                if (collect.Count < 1)
                    sink.Error(Code, $"{path}.collect.count", "count must be 1 or more.");
                if (collect.CountMax is not null)
                    sink.Error(Code, $"{path}.collect.count_max", "a bucket always collects count.");
            }
        }

        var objects = (definition.Objects ?? []).Where(o => o.Id is not null).GroupBy(o => o.Id!).ToDictionary(g => g.Key, g => g.First());
        for (var i = 0; i < (definition.Objects?.Count ?? 0); i++)
        {
            if (definition.Objects![i].Outcrop is { } outcrop)
                Outcrop(outcrop, definition.Objects[i], objects, $"$.objects[{i}]", sink);
            if (definition.Objects[i].Scene is { } scene)
                Scene(scene, definition.Objects[i], objects, $"$.objects[{i}]", sink);
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
                if (rule.Perch is { } perch)
                {
                    KnownKeys.WarnUnknown(perch.Extra, $"{rulePath}.perch", sink, typeof(PerchDefinition));
                    Positive(perch.Rise, $"{rulePath}.perch.rise", sink);
                    Positive(perch.Within, $"{rulePath}.perch.within", sink);
                }
                if (rule.Cluster is { } cluster)
                {
                    KnownKeys.WarnUnknown(cluster.Extra, $"{rulePath}.cluster", sink, typeof(ClusterDefinition));
                    Positive(cluster.Scale, $"{rulePath}.cluster.scale", sink);
                    if (!(cluster.Cover is > 0f and <= 1f))
                        sink.Error(Code, $"{rulePath}.cluster.cover", $"cover must be above 0 and at most 1 (got {cluster.Cover}).");
                    if (!(cluster.Outside is >= 0f and <= 1f))
                        sink.Error(Code, $"{rulePath}.cluster.outside", $"outside must be 0 to 1 (got {cluster.Outside}).");
                }
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
            if (!(biomes[i].Hills is >= 0f and <= 1f))
                sink.Error(Code, $"$.biomes[{i}].hills", $"hills must be 0 to 1 (got {biomes[i].Hills}).");
        }
        NonNegative(t.RegionWarp, "$.terrain.region_warp", sink);
        if (t.Hills is { } hills)
        {
            KnownKeys.WarnUnknown(hills.Extra, "$.terrain.hills", sink, typeof(HillsDefinition));
            NonNegative(hills.Height, "$.terrain.hills.height", sink);
            Positive(hills.Scale, "$.terrain.hills.scale", sink);
            if (hills.Octaves is < 1 or > 8)
                sink.Error(Code, "$.terrain.hills.octaves", $"octaves must be 1 to 8 (got {hills.Octaves}).");
            if (!(hills.Gain is > 0f and <= 1f))
                sink.Error(Code, "$.terrain.hills.gain", $"gain must be greater than 0 and at most 1 (got {hills.Gain}).");
            Positive(hills.Contrast, "$.terrain.hills.contrast", sink);
            NonNegative(hills.ShoreRamp, "$.terrain.hills.shore_ramp", sink);
            // Flat biomes hold their neighbours' hills down over the ramp, which looks at the same cells as the blend.
            if (hills.ShoreRamp > MaxBlendCells * t.BiomeCellSize)
                sink.Error(Code, "$.terrain.hills.shore_ramp", $"shore_ramp ({hills.ShoreRamp}) must be at most {MaxBlendCells} × biome_cell_size ({MaxBlendCells * t.BiomeCellSize}).");
            NonNegative(hills.PadRamp, "$.terrain.hills.pad_ramp", sink);
        }
        if (t.Cliffs is { } cliffs)
        {
            KnownKeys.WarnUnknown(cliffs.Extra, "$.terrain.cliffs", sink, typeof(CliffsDefinition));
            NonNegative(cliffs.Step, "$.terrain.cliffs.step", sink);
            if (cliffs.Levels is < 0 or > 16)
                sink.Error(Code, "$.terrain.cliffs.levels", $"levels must be 0 to 16 (got {cliffs.Levels}).");
            Positive(cliffs.Scale, "$.terrain.cliffs.scale", sink);
            Positive(cliffs.Contrast, "$.terrain.cliffs.contrast", sink);
            Positive(cliffs.Face, "$.terrain.cliffs.face", sink);
            if (cliffs.Sheer is < 0f or > 1f)
                sink.Error(Code, "$.terrain.cliffs.sheer", $"sheer must be 0 to 1 (got {cliffs.Sheer}).");
            Positive(cliffs.GentleFace, "$.terrain.cliffs.gentle_face", sink);
            Positive(cliffs.SheerScale, "$.terrain.cliffs.sheer_scale", sink);
            NonNegative(cliffs.Inland, "$.terrain.cliffs.inland", sink);
        }
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

    // A part marker names a part the host can put down: one in the catalog that is placeable (a bare ref is the POI's mod's).
    private static void CheckPartMarker(PoiMarker marker, string path, DiagnosticSink sink, IPartCatalog? parts, string modId)
    {
        if (string.IsNullOrEmpty(marker.Ref))
        {
            sink.Error(DiagnosticCodes.PoiInvalid, $"{path}.ref", "a part marker needs the id of the part it puts there.");
            return;
        }
        if (parts is null)
            return;
        var id = marker.Ref.Contains(':') ? marker.Ref : $"{modId}:{marker.Ref}";
        if (parts.FindDefinition(id) is not { } definition)
            sink.Error(DiagnosticCodes.PoiInvalid, $"{path}.ref", $"a part marker needs the id of a part in the loaded mods (no part '{id}').");
        else if (!definition.IsPlaceable)
            sink.Error(DiagnosticCodes.PoiInvalid, $"{path}.ref", $"a part marker needs a part that can be placed ('{id}' is an inventory item).");
    }

    // [x, z] as fractions of the world, -0.5 (west, north) to 0.5 (east, south); rivers may reach a little past the edge.
    private static void Fraction(List<float>? at, string path, DiagnosticSink sink, float reach = 0.5f)
    {
        if (at is not { Count: 2 } || !at.All(v => v >= -reach && v <= reach))
            sink.Error(Code, path, $"must be [x, z], each from -{reach} to {reach} (fractions of the world).");
    }

    /// <param name="objectIds">World object ids <c>object</c> markers may name; null skips the check.</param>
    /// <param name="liquidIds">Liquid ids pools may hold; null skips the check.</param>
    /// <param name="parts">The parts <c>part</c> markers may name; null skips the check.</param>
    /// <param name="modId">The mod the prefab belongs to, which a bare <c>part</c> marker ref names a part of.</param>
    public static void Validate(PoiPrefab prefab, IReadOnlyList<string> biomeIds, DiagnosticSink sink, IReadOnlyCollection<string> groundIds,
        IReadOnlyCollection<string>? objectIds = null, IReadOnlyCollection<string>? liquidIds = null, IPartCatalog? parts = null,
        string? modId = null)
    {
        const string code = DiagnosticCodes.PoiInvalid;
        KnownKeys.WarnUnknown(prefab.Extra, "$", sink, typeof(PoiPrefab));
        if (!Identifiers.IsValid(prefab.Id))
            sink.Error(code, "$.id", $"POI id '{prefab.Id}' must be lowercase snake_case.");
        if (!(prefab.Radius > 0f) || !float.IsFinite(prefab.Radius))
            sink.Error(code, "$.radius", $"radius must be greater than 0 (got {prefab.Radius}).");
        if (prefab.BotFreeRadius is { } botFree && !(botFree > 0f && float.IsFinite(botFree)))
            sink.Error(code, "$.bot_free_radius", $"bot_free_radius must be greater than 0 (got {botFree}).");

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
            if (placement.Roadside is { } roadside)
            {
                if (placement.Spacing is null)
                    sink.Error(code, "$.placement.roadside", "roadside POIs are spaced along the roads: give spacing too.");
                if (!(roadside >= placement.Clearance) || !float.IsFinite(roadside))
                    sink.Error(code, "$.placement.roadside", $"roadside must be at least the clearance ({placement.Clearance}), or the road keeps the POI away (got {roadside}).");
            }
            if (placement.Shore is { } shore)
            {
                if (placement.Spacing is null)
                    sink.Error(code, "$.placement.shore", "shore POIs are spaced: give spacing too.");
                if (!(shore > 0f) || !float.IsFinite(shore))
                    sink.Error(code, "$.placement.shore", $"shore must be greater than 0 (got {shore}).");
            }
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
            if (op.Wobble is { } w && !(w is >= 0f and <= 0.5f))
                sink.Error(code, $"{path}.wobble", $"wobble must be 0 to 0.5 (got {w}).");
            var wobble = Math.Clamp(op.Wobble ?? 0f, 0f, 0.5f);
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
            // A wobbling edge narrows the blend by up to (1 - wobble) where it pulls in.
            if (op.Op == TerrainOpKind.Basin && op.Depth > 0f && !(1.5f * op.Depth <= MaxBasinSlope * op.Blend * (1f - wobble)))
                sink.Error(code, $"{path}.blend", $"a basin {op.Depth} m deep needs a blend of at least {1.5f * op.Depth / MaxBasinSlope / (1f - wobble):0.##} m, or its bank is too steep to climb out of (got {op.Blend}).");
            // Shaping must stay inside the footprint, or it would reach under a neighbouring POI.
            var reach = offset.Length() + (op.Radius + op.Blend) * (1f + wobble);
            if (reach > prefab.Radius + 1e-3f)
                sink.Error(code, path, $"reaches {reach:0.##} m from the centre, past the POI radius ({prefab.Radius}).");
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
        for (var i = 0; i < (prefab.Models?.Count ?? 0); i++)
        {
            var model = prefab.Models![i];
            var path = $"$.models[{i}]";
            KnownKeys.WarnUnknown(model.Extra, path, sink, typeof(PoiModel));
            if (!AssetPath.TryParse(model.File, "mod", AssetKind.Mesh, out _, out var error))
                sink.Error(code, $"{path}.file", error);
            if (!(float.IsFinite(model.At.X) && float.IsFinite(model.At.Y) && float.IsFinite(model.At.Z)) || !float.IsFinite(model.Yaw))
                sink.Error(code, path, "at and yaw must be numbers.");
            else if (new Vector2(model.At.X, model.At.Z).Length() > prefab.Radius + 1e-3f)
                sink.Error(code, $"{path}.at", $"is {new Vector2(model.At.X, model.At.Z).Length():0.##} m from the centre, past the POI radius ({prefab.Radius}).");
        }
        for (var i = 0; i < (prefab.Markers?.Count ?? 0); i++)
        {
            var marker = prefab.Markers![i];
            var path = $"$.markers[{i}]";
            KnownKeys.WarnUnknown(marker.Extra, path, sink, typeof(PoiMarker));
            if (!Identifiers.IsValid(marker.Type))
                sink.Error(code, $"{path}.type", $"marker type '{marker.Type}' must be lowercase snake_case.");
            if (marker.Ref is { } reference && !reference.Split(':').All(Identifiers.IsValid))
                sink.Error(code, $"{path}.ref", $"'{reference}' is not an id (id or mod_id:id).");
            if (marker.Type == PoiMarker.ObjectType && (marker.Ref is null || objectIds?.Contains(marker.Ref) == false))
                sink.Error(code, $"{path}.ref", $"an object marker needs the id of a world object in the world generation rules (got '{marker.Ref}').");
            if (marker.Type == PoiMarker.PartType)
                CheckPartMarker(marker, path, sink, parts, modId ?? Identifiers.VanillaModId);
        }
        for (var i = 0; i < (prefab.Pools?.Count ?? 0); i++)
        {
            var pool = prefab.Pools![i];
            var path = $"$.pools[{i}]";
            KnownKeys.WarnUnknown(pool.Extra, path, sink, typeof(LiquidPool));
            if (pool.Liquid is null || liquidIds?.Contains(pool.Liquid) == false)
                sink.Error(code, $"{path}.liquid", $"'{pool.Liquid}' is not one of the liquids in the world generation rules.");
            if (!(pool.Radius > 0f) || !float.IsFinite(pool.Radius))
                sink.Error(code, $"{path}.radius", $"radius must be greater than 0 (got {pool.Radius}).");
            if (!float.IsFinite(pool.Level))
                sink.Error(code, $"{path}.level", "level must be a number.");
            var offset = Vector2.Zero;
            if (pool.At is { } at)
            {
                if (at.Count != 2 || !at.All(float.IsFinite))
                    sink.Error(code, $"{path}.at", "at must be [x, z].");
                else
                    offset = new Vector2(at[0], at[1]);
            }
            if (offset.Length() + pool.Radius > prefab.Radius + 1e-3f)
                sink.Error(code, path, $"reaches {offset.Length() + pool.Radius:0.##} m from the centre, past the POI radius ({prefab.Radius}).");
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

    // A rock deposit: its sizes, its mounds and the block objects they are heaped from.
    private static void Scene(SceneDefinition scene, WorldObjectDefinition owner, Dictionary<string, WorldObjectDefinition> objects, string path,
        DiagnosticSink sink)
    {
        path += ".scene";
        KnownKeys.WarnUnknown(scene.Extra, path, sink, typeof(SceneDefinition));
        if (owner.Outcrop is not null)
            sink.Error(Code, path, "an object is a scene or a rock deposit, not both.");
        if (!(owner.Footprint > 0f))
            sink.Error(Code, path, "a scene needs a footprint: no piece stands further out than it.");
        if (scene.Layouts is not { Count: > 0 })
            sink.Error(Code, $"{path}.layouts", "a scene needs at least one layout.");
        for (var l = 0; l < (scene.Layouts?.Count ?? 0); l++)
        {
            var layout = scene.Layouts![l];
            var at = $"{path}.layouts[{l}]";
            KnownKeys.WarnUnknown(layout.Extra, at, sink, typeof(SceneLayout));
            Positive(layout.Weight, $"{at}.weight", sink);
            if (layout.Pieces is not { Count: > 0 })
                sink.Error(Code, $"{at}.pieces", "a layout needs at least one piece.");
            for (var p = 0; p < (layout.Pieces?.Count ?? 0); p++)
            {
                var piece = layout.Pieces![p];
                var pp = $"{at}.pieces[{p}]";
                KnownKeys.WarnUnknown(piece.Extra, pp, sink, typeof(ScenePiece));
                if (piece.Object is null || !objects.TryGetValue(piece.Object, out var o))
                    sink.Error(Code, $"{pp}.object", $"'{piece.Object}' is not one of the objects defined in this file.");
                else if (o.Scene is not null || o.Outcrop is not null)
                    sink.Error(Code, $"{pp}.object", $"'{piece.Object}' is a scene or rock deposit itself; a piece is a single object.");
                if (piece.At is not { Count: 2 } spot || !float.IsFinite(spot[0]) || !float.IsFinite(spot[1]))
                    sink.Error(Code, $"{pp}.at", "at must be [x, z] in metres.");
                else if (MathF.Sqrt(spot[0] * spot[0] + spot[1] * spot[1]) > owner.Footprint)
                    sink.Error(Code, $"{pp}.at", $"the piece stands outside the scene's footprint ({owner.Footprint} m).");
                if (!(piece.Chance is > 0f and <= 1f))
                    sink.Error(Code, $"{pp}.chance", $"chance must be above 0 and at most 1 (got {piece.Chance}).");
            }
        }
    }

    private static void Outcrop(OutcropDefinition outcrop, WorldObjectDefinition owner, Dictionary<string, WorldObjectDefinition> objects, string path,
        DiagnosticSink sink)
    {
        path += ".outcrop";
        KnownKeys.WarnUnknown(outcrop.Extra, path, sink, typeof(OutcropDefinition));
        if (!(owner.Footprint > 0f))
            sink.Error(Code, path, "a rock deposit needs a footprint: no block stands further out than it.");
        if (outcrop.Deposits is not { Count: > 0 })
            sink.Error(Code, $"{path}.deposits", "a rock deposit needs at least one deposit size.");
        for (var d = 0; d < (outcrop.Deposits?.Count ?? 0); d++)
        {
            var deposit = outcrop.Deposits![d];
            var at = $"{path}.deposits[{d}]";
            KnownKeys.WarnUnknown(deposit.Extra, at, sink, typeof(DepositSize));
            Positive(deposit.Weight, $"{at}.weight", sink);
            NonNegative(deposit.Spread, $"{at}.spread", sink);
            if (deposit.Mounds is not { Count: 2 } mounds || mounds[0] < 1 || mounds[1] < mounds[0] || mounds[1] > OutcropLayout.MaxMounds)
                sink.Error(Code, $"{at}.mounds", $"mounds must be [min, max] with 1 <= min <= max <= {OutcropLayout.MaxMounds}.");
            if (deposit.Sizes is not { Count: > 0 })
                sink.Error(Code, $"{at}.sizes", "a deposit size needs at least one mound size.");
            for (var k = 0; k < (deposit.Sizes?.Count ?? 0); k++)
            {
                var pick = deposit.Sizes![k];
                KnownKeys.WarnUnknown(pick.Extra, $"{at}.sizes[{k}]", sink, typeof(MoundPick));
                Positive(pick.Weight, $"{at}.sizes[{k}].weight", sink);
                if (pick.Mound is null || outcrop.Mounds?.ContainsKey(pick.Mound) != true)
                    sink.Error(Code, $"{at}.sizes[{k}].mound", $"'{pick.Mound}' is not one of this outcrop's mounds.");
            }
        }
        foreach (var (name, mound) in outcrop.Mounds ?? [])
        {
            var at = $"{path}.mounds.{name}";
            KnownKeys.WarnUnknown(mound.Extra, at, sink, typeof(MoundDefinition));
            if (mound.Blocks is not { Count: 2 } blocks || blocks[0] < 1 || blocks[1] < blocks[0] || blocks[1] > OutcropLayout.MaxBlocks)
                sink.Error(Code, $"{at}.blocks", $"blocks must be [min, max] with 1 <= min <= max <= {OutcropLayout.MaxBlocks}.");
            if (mound.Kinds is not { Count: > 0 })
                sink.Error(Code, $"{at}.kinds", "a mound needs at least one kind of block.");
            for (var k = 0; k < (mound.Kinds?.Count ?? 0); k++)
            {
                var kind = mound.Kinds![k];
                KnownKeys.WarnUnknown(kind.Extra, $"{at}.kinds[{k}]", sink, typeof(MoundBlock));
                Positive(kind.Weight, $"{at}.kinds[{k}].weight", sink);
                Block(kind.Object, objects, $"{at}.kinds[{k}].object", sink);
            }
        }
        if (!(outcrop.RichChance is >= 0f and <= 1f))
            sink.Error(Code, $"{path}.rich_chance", "rich_chance must be from 0 to 1.");
        if (outcrop.RichChance > 0f && outcrop.Rich is not { Count: > 0 })
            sink.Warning(Code, $"{path}.rich_chance", "no mound turns metal-rich: rich is empty.");
        foreach (var (from, to) in outcrop.Rich ?? [])
        {
            Block(from, objects, $"{path}.rich.{from}", sink);
            Block(to, objects, $"{path}.rich.{from}", sink);
        }
    }

    // A block a mound is heaped from: a world object with a box collider (a deposit's own object may be one).
    private static void Block(string? id, Dictionary<string, WorldObjectDefinition> objects, string path, DiagnosticSink sink)
    {
        if (id is null || !objects.TryGetValue(id, out var block))
            sink.Error(Code, path, $"'{id}' is not a world object.");
        else if (block.Collider is not { Shape: PrimitiveShape.Box, Size: { } })
            sink.Error(Code, path, $"block '{id}' needs a box collider: its box is the block's size.");
    }

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

    private static void LootTable(string id, LootTableDefinition table, string path, DiagnosticSink sink)
    {
        KnownKeys.WarnUnknown(table.Extra, path, sink, typeof(LootTableDefinition));
        if (!Identifiers.IsValid(id))
            sink.Error(Code, path, $"loot table id '{id}' must be lowercase snake_case.");
        if (table.Items is not { Count: > 0 })
            sink.Error(Code, $"{path}.items", "a loot table needs at least one item.");
        for (var e = 0; e < (table.Items?.Count ?? 0); e++)
        {
            var entry = table.Items![e];
            var at = $"{path}.items[{e}]";
            KnownKeys.WarnUnknown(entry.Extra, at, sink, typeof(LootEntry));
            if (!PartValidator.IsItemRef(entry.Item))
                sink.Error(Code, $"{at}.item", "item must be an item id or 'mod_id:item_id'.");
            if (entry.Count < 1)
                sink.Error(Code, $"{at}.count", "count must be 1 or more.");
            if (entry.CountMax is { } most && most < entry.Count)
                sink.Error(Code, $"{at}.count_max", $"count_max must be at least count ({entry.Count}).");
            Positive(entry.Weight, $"{at}.weight", sink);
        }
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
