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
            if (o.Model is { } model && FloraModels.Find(model) is null)
                sink.Error(Code, $"{path}.model", $"'{model}' is not a built-in flora model ({string.Join(", ", FloraModels.All.Select(m => m.Id))}).");
            if (o.Drops is { Count: > 0 } && o.Health is null)
                sink.Warning(Code, $"{path}.drops", "drops are never left: the object has no health, so it cannot break.");
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
            if ((placement.Fixed is null) == (placement.Spacing is null))
                sink.Error(code, "$.placement", "give exactly one of fixed or spacing.");
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
