using System.Text.Json;

namespace Mechanica.Core.Data;

/// <summary>Checks a parsed <see cref="ModManifest"/> and reports every problem it finds.</summary>
internal static class ManifestValidator
{
    public static void Validate(ModManifest manifest, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.ManifestInvalid;
        if (manifest.FormatVersion is { } format && (format < 1 || format > ModManifest.CurrentFormatVersion))
            sink.Error(DiagnosticCodes.UnsupportedFormat, "$.format_version",
                $"format_version {format} is not supported; this build reads 1 to {ModManifest.CurrentFormatVersion}.");
        if (manifest.Id is null)
            sink.Error(code, "$.id", "id is required.");
        else if (!Identifiers.IsValid(manifest.Id))
            sink.Error(code, "$.id", $"id '{manifest.Id}' must be lowercase snake_case (a letter, then letters, digits or '_').");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            sink.Error(code, "$.name", "name is required.");
        if (manifest.Version is null)
            sink.Error(code, "$.version", "version is required, for example \"1.0.0\".");
        else if (manifest.ParsedVersion is null)
            sink.Error(code, "$.version", $"version '{manifest.Version}' is not a semantic version (major.minor.patch).");

        var seen = new HashSet<string>();
        ValidateReferences(manifest, manifest.Dependencies, "dependencies", "dependency", seen, sink);
        ValidateReferences(manifest, manifest.Conflicts, "conflicts", "conflict", seen, sink);
        KnownKeys.WarnUnknown(manifest.Extra, "$", sink, typeof(ModManifest));
    }

    // A mod id may appear once across dependencies and conflicts together: needing and refusing a mod is a contradiction.
    private static void ValidateReferences(ModManifest manifest, List<ModDependency>? list, string key, string what,
        HashSet<string> seen, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.ManifestInvalid;
        for (var i = 0; i < (list?.Count ?? 0); i++)
        {
            var dep = list![i];
            var path = $"$.{key}[{i}]";
            if (!Identifiers.IsValid(dep.Id))
                sink.Error(code, $"{path}.id", dep.Id is null ? $"{what} id is required." : $"{what} id '{dep.Id}' is not a valid mod id.");
            else if (dep.Id == manifest.Id)
                sink.Error(code, $"{path}.id", what == "dependency" ? "a mod cannot depend on itself." : "a mod cannot conflict with itself.");
            else if (!seen.Add(dep.Id))
                sink.Error(code, $"{path}.id", $"mod '{dep.Id}' is listed twice in dependencies and conflicts.");
            if (dep.ParsedVersion is null)
                sink.Error(code, $"{path}.version",
                    $"version range '{dep.Version}' is invalid; use forms like \"1.2.0\", \">=1.2.0 <2.0.0\", \"^1.2.0\" or \"*\".");
            KnownKeys.WarnUnknown(dep.Extra, path, sink, typeof(ModDependency));
        }
    }
}

/// <summary>Checks a parsed <see cref="PartDefinition"/> and reports every problem it finds.</summary>
internal sealed class PartValidator(string modId, IReadOnlySet<string> declaredDependencies, Func<string, string?> modDirectory)
{
    private const string Code = DiagnosticCodes.PartInvalid;
    private const float Epsilon = 1e-4f;

    public void Validate(PartDefinition part, DiagnosticSink sink)
    {
        if (part.Id is null)
            sink.Error(Code, "$.id", "id is required.");
        else if (!Identifiers.IsValid(part.Id))
            sink.Error(Code, "$.id", $"id '{part.Id}' must be lowercase snake_case (a letter, then letters, digits or '_').");
        if (string.IsNullOrWhiteSpace(part.Name))
            sink.Error(Code, "$.name", "name is required.");
        if (part.Category is null)
            sink.Error(Code, "$.category", $"category is required: one of {EnumList<PartCategory>()}.");

        var placeable = part.IsPlaceable;
        var what = $"a {part.Category.ToString()?.ToLowerInvariant()} part";
        RequirePositive(part.Mass, "$.mass", "mass (kg)", placeable, what, sink);
        if (part.Durability is { } durability && durability < 1)
            sink.Error(Code, "$.durability", "durability must be 1 or more.");
        else if (placeable && part.Durability is null)
            sink.Error(Code, "$.durability", $"durability is required for {what}.");
        RequireNonNegative(part.Friction, "$.friction", "friction", placeable, what, sink);
        RequireNonNegative(part.Buoyancy, "$.buoyancy", "buoyancy", placeable, what, sink);
        if (placeable && part.Flammable is null)
            sink.Error(Code, "$.flammable", $"flammable (true or false) is required for {what}.");
        if (part.StackSize is < 1)
            sink.Error(Code, "$.stack_size", "stack_size must be 1 or more.");
        if (part.SoundMaterial is not null && !Identifiers.IsValid(part.SoundMaterial))
            sink.Error(Code, "$.sound_material", $"sound_material '{part.SoundMaterial}' must be lowercase snake_case.");

        if (part.Size is { } size)
        {
            if (size.X < 1 || size.Y < 1 || size.Z < 1)
                sink.Error(Code, "$.size", "size must be at least 1 grid cell on every axis.");
        }
        else if (placeable)
        {
            sink.Error(Code, "$.size", $"size (in 0.25 m grid cells, like [1, 1, 1]) is required for {what}.");
        }

        CheckAsset(part.Mesh, "$.mesh", AssetKind.Mesh, sink);
        CheckAsset(part.Icon, "$.icon", AssetKind.Texture, sink);
        CheckAsset(part.Script, "$.script", AssetKind.Script, sink);
        if (part.Visual is { } visual)
            ValidateVisual(visual, sink);
        if (part.Collision is { } collision)
            ValidateCollision(collision, sink);
        ValidateSockets(part, placeable, sink);
        ValidateStorage(part, placeable, sink);
        ValidateSurvival(part, placeable, sink);

        foreach (var (key, sound) in part.Sounds ?? [])
        {
            if (!Identifiers.IsValid(key))
                sink.Error(Code, "$.sounds", $"sound event key '{key}' must be lowercase snake_case.");
            if (!IsSoundId(sound))
                sink.Error(Code, $"$.sounds.{key}", $"sound id '{sound}' must be a snake_case id or 'mod_id:sound_id'.");
        }

        KnownKeys.WarnUnknown(part.Extra, "$", sink, typeof(PartDefinition));
    }

    private void ValidateVisual(VisualDefinition v, DiagnosticSink sink)
    {
        const string p = "$.visual";
        CheckColor(v.Color, $"{p}.color", sink);
        if (v.Roughness is { } r && !(r >= 0 && r <= 1))
            sink.Error(Code, $"{p}.roughness", "roughness must be between 0 and 1.");
        if (v.Metallic is { } m && !(m >= 0 && m <= 1))
            sink.Error(Code, $"{p}.metallic", "metallic must be between 0 and 1.");
        if (v.Opacity is { } o && !(o >= 0 && o <= 1))
            sink.Error(Code, $"{p}.opacity", "opacity must be between 0 and 1.");
        CheckColor(v.Emission, $"{p}.emission", sink);
        CheckAsset(v.Texture, $"{p}.texture", AssetKind.Texture, sink);
        var ids = new HashSet<string>();
        for (var i = 0; i < (v.Variants?.Count ?? 0); i++)
        {
            var variant = v.Variants![i];
            var path = $"{p}.variants[{i}]";
            if (!Identifiers.IsValid(variant.Id))
                sink.Error(Code, $"{path}.id", variant.Id is null ? "variant id is required." : $"variant id '{variant.Id}' must be lowercase snake_case.");
            else if (!ids.Add(variant.Id))
                sink.Error(Code, $"{path}.id", $"variant id '{variant.Id}' is used twice in this part.");
            if (string.IsNullOrWhiteSpace(variant.Name))
                sink.Error(Code, $"{path}.name", "variant name is required.");
            if (variant.Color is null)
                sink.Error(Code, $"{path}.color", "variant color (\"#rrggbb\") is required.");
            CheckColor(variant.Color, $"{path}.color", sink);
            KnownKeys.WarnUnknown(variant.Extra, path, sink, typeof(ColorVariant));
        }
        KnownKeys.WarnUnknown(v.Extra, p, sink, typeof(VisualDefinition));
    }

    private static void CheckColor(string? color, string path, DiagnosticSink sink)
    {
        if (color is not null && !HexColor.TryParse(color, out _))
            sink.Error(Code, path, $"color '{color}' must be hex text like \"#a07040\".");
    }

    private void ValidateCollision(CollisionDefinition c, DiagnosticSink sink)
    {
        const string p = "$.collision";
        switch (c.Shape)
        {
            case null:
                sink.Error(Code, $"{p}.shape", $"shape is required: one of {EnumList<CollisionShape>()}.");
                break;
            case CollisionShape.Box:
                if (c.Size is not { } s)
                    sink.Error(Code, $"{p}.size", "a box needs size [x, y, z] in metres.");
                else if (s.X <= 0 || s.Y <= 0 || s.Z <= 0)
                    sink.Error(Code, $"{p}.size", "box size must be greater than 0 on every axis.");
                break;
            case CollisionShape.Sphere:
                RequirePositive(c.Radius, $"{p}.radius", "radius (m)", true, "a sphere", sink);
                break;
            case CollisionShape.Cylinder or CollisionShape.Capsule:
                var shape = c.Shape.Value.ToString().ToLowerInvariant();
                RequirePositive(c.Radius, $"{p}.radius", "radius (m)", true, $"a {shape}", sink);
                RequirePositive(c.Height, $"{p}.height", "height (m)", true, $"a {shape}", sink);
                if (c.Shape == CollisionShape.Capsule && c.Radius > 0 && c.Height > 0 && c.Height < 2 * c.Radius)
                    sink.Error(Code, $"{p}.height", "a capsule's height includes its caps, so it must be at least twice the radius.");
                break;
            case CollisionShape.Convex:
                if (c.Mesh is null)
                    sink.Error(Code, $"{p}.mesh", "a convex shape needs a mesh.");
                break;
        }
        CheckAsset(c.Mesh, $"{p}.mesh", AssetKind.Mesh, sink);
        KnownKeys.WarnUnknown(c.Extra, p, sink, typeof(CollisionDefinition));
    }

    private static void ValidateSockets(PartDefinition part, bool placeable, DiagnosticSink sink)
    {
        if (part.Sockets is not { Count: > 0 } sockets)
            return;
        if (!placeable)
        {
            sink.Error(Code, "$.sockets", "only placeable parts can have sockets.");
            return;
        }
        var half = part.Size is { } size ? size.Metres : (Vec3?)null;
        var ids = new HashSet<string>();
        for (var i = 0; i < sockets.Count; i++)
        {
            var s = sockets[i];
            var path = $"$.sockets[{i}]";
            if (!Identifiers.IsValid(s.Id))
                sink.Error(Code, $"{path}.id", s.Id is null ? "socket id is required." : $"socket id '{s.Id}' must be lowercase snake_case.");
            else if (!ids.Add(s.Id))
                sink.Error(Code, $"{path}.id", $"socket id '{s.Id}' is used twice in this part.");
            if (s.Type is null)
                sink.Error(Code, $"{path}.type", $"socket type is required: one of {EnumList<SocketType>()}.");
            if (s.Position is not { } pos)
                sink.Error(Code, $"{path}.position", "socket position [x, y, z] in metres is required.");
            else if (half is { } h && (MathF.Abs(pos.X) > h.X / 2 + Epsilon || MathF.Abs(pos.Y) > h.Y / 2 + Epsilon || MathF.Abs(pos.Z) > h.Z / 2 + Epsilon))
                sink.Error(Code, $"{path}.position",
                    $"socket position {pos} is outside the part's {part.Size} footprint (±{JsonNumbers.Format(h.X / 2)}, ±{JsonNumbers.Format(h.Y / 2)}, ±{JsonNumbers.Format(h.Z / 2)} m).");
            if (s.Normal is not { } n)
                sink.Error(Code, $"{path}.normal", "socket normal (a unit direction like [0, 1, 0]) is required.");
            else if (MathF.Abs(n.Length - 1) > 1e-3f)
                sink.Error(Code, $"{path}.normal", $"socket normal {n} must have length 1.");
            KnownKeys.WarnUnknown(s.Extra, path, sink, typeof(SocketDefinition));
        }
    }

    private static void ValidateStorage(PartDefinition part, bool placeable, DiagnosticSink sink)
    {
        if (part.Inventory is { } inventory)
        {
            const string p = "$.inventory";
            if (!placeable)
                sink.Error(Code, p, "only placeable parts can have an inventory; items go in one.");
            if (inventory.Slots is not { } slots)
                sink.Error(Code, $"{p}.slots", "slots (1 or more) is required.");
            else if (slots is < 1 or > MaxSlots)
                sink.Error(Code, $"{p}.slots", $"slots must be 1 to {MaxSlots} (got {slots}).");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < (inventory.Accepts?.Count ?? 0); i++)
            {
                var item = inventory.Accepts![i];
                if (!IsItemRef(item))
                    sink.Error(Code, $"{p}.accepts[{i}]", $"'{item}' must be an item id or 'mod_id:item_id'.");
                else if (!seen.Add(Identifiers.Qualify(item)))
                    sink.Error(Code, $"{p}.accepts[{i}]", $"'{item}' is listed twice.");
            }
            KnownKeys.WarnUnknown(inventory.Extra, p, sink, typeof(InventoryDefinition));
        }
        if (part.Crafting is { } crafting)
        {
            const string p = "$.crafting";
            if (!placeable)
                sink.Error(Code, p, "only placeable parts can be crafting stations.");
            if (part.Inventory is null)
                sink.Error(Code, p, "a crafting station needs an inventory for what it makes.");
            if (crafting.Speed is { } speed && !(speed > 0 && float.IsFinite(speed)))
                sink.Error(Code, $"{p}.speed", "speed must be greater than 0.");
            if (crafting.QueueSize is < 1 or > MaxQueue)
                sink.Error(Code, $"{p}.queue_size", $"queue_size must be 1 to {MaxQueue}.");
            KnownKeys.WarnUnknown(crafting.Extra, p, sink, typeof(CraftingDefinition));
        }
    }

    // Farming and respawn points (chunk 20). Weapons and food are Lua item scripts (chunk 20b).
    private static void ValidateSurvival(PartDefinition part, bool placeable, DiagnosticSink sink)
    {
        void ItemOnly(object? section, string key)
        {
            if (section is not null && placeable)
                sink.Error(Code, $"$.{key}", $"only items (not placeable parts) can have {key}.");
        }
        void PartOnly(object? section, string key)
        {
            if (section is not null && !placeable)
                sink.Error(Code, $"$.{key}", $"only placeable parts can have {key}.");
        }
        ItemOnly(part.Seed, "seed");
        PartOnly(part.Soil, "soil");
        PartOnly(part.RespawnPoint == true ? true : null, "respawn_point");
        if (part.Seed is { } seed)
        {
            if (!IsItemRef(seed.Crop))
                sink.Error(Code, "$.seed.crop", "crop (the item a ripe crop yields) is required: an item id or 'mod_id:item_id'.");
            RequirePositive(seed.GrowTime, "$.seed.grow_time", "grow_time (s)", true, "a seed", sink);
            if (seed.Yield is < 1)
                sink.Error(Code, "$.seed.yield", "yield must be 1 or more.");
            if (seed.Seeds is < 0)
                sink.Error(Code, "$.seed.seeds", "seeds must be 0 or more.");
            RequireNonNegative(seed.Value, "$.seed.value", "value", false, "a seed", sink);
            KnownKeys.WarnUnknown(seed.Extra, "$.seed", sink, typeof(SeedDefinition));
        }
        if (part.Soil is { } soil)
        {
            RequirePositive(soil.Capacity, "$.soil.capacity", "capacity (s)", true, "soil", sink);
            RequirePositive(soil.Growth, "$.soil.growth", "growth", false, "soil", sink);
            KnownKeys.WarnUnknown(soil.Extra, "$.soil", sink, typeof(SoilDefinition));
        }
    }

    /// <summary>Most slots one inventory may have.</summary>
    internal const int MaxSlots = 256;

    /// <summary>Most jobs one station may queue.</summary>
    internal const int MaxQueue = 64;

    /// <summary>True for <c>item_id</c> or <c>mod_id:item_id</c>.</summary>
    internal static bool IsItemRef([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? id) => id is not null && IsSoundId(id);

    /// <summary>Checks an asset path: well formed, in this mod or a declared dependency, and present on disk.</summary>
    internal void CheckAsset(string? text, string jsonPath, AssetKind kind, DiagnosticSink sink)
    {
        if (text is null)
            return;
        if (!AssetPath.TryParse(text, modId, kind, out var asset, out var error))
        {
            sink.Error(DiagnosticCodes.AssetPathInvalid, jsonPath, error);
            return;
        }
        if (asset.ModId != modId && !declaredDependencies.Contains(asset.ModId))
        {
            sink.Error(DiagnosticCodes.AssetUndeclaredMod, jsonPath,
                $"'{text}' refers to mod '{asset.ModId}', which is not in this mod's dependencies.");
            return;
        }
        var directory = modDirectory(asset.ModId);
        if (directory is null || !File.Exists(asset.Resolve(directory)))
            sink.Error(DiagnosticCodes.AssetMissing, jsonPath, $"file '{asset.RelativePath}' was not found in mod '{asset.ModId}'.");
    }

    private static void RequirePositive(float? value, string path, string label, bool required, string what, DiagnosticSink sink)
    {
        if (value is { } v && !(v > 0 && float.IsFinite(v)))
            sink.Error(Code, path, $"{label} must be greater than 0.");
        else if (required && value is null)
            sink.Error(Code, path, $"{label} is required for {what}.");
    }

    private static void RequireNonNegative(float? value, string path, string label, bool required, string what, DiagnosticSink sink)
    {
        if (value is { } v && !(v >= 0 && float.IsFinite(v)))
            sink.Error(Code, path, $"{label} must be 0 or more.");
        else if (required && value is null)
            sink.Error(Code, path, $"{label} is required for {what}.");
    }

    private static bool IsSoundId(string? id)
    {
        if (id is null)
            return false;
        var colon = id.IndexOf(':');
        return colon < 0 ? Identifiers.IsValid(id) : Identifiers.IsValid(id[..colon]) && Identifiers.IsValid(id[(colon + 1)..]);
    }

    private static string EnumList<T>() where T : struct, Enum =>
        string.Join(", ", Enum.GetNames<T>().Select(JsonNamingPolicy.SnakeCaseLower.ConvertName));
}

/// <summary>Warns about keys a schema type does not define, suggesting the closest known key.</summary>
internal static class KnownKeys
{
    public static void WarnUnknown(Dictionary<string, JsonElement>? extra, string path, DiagnosticSink sink, Type schema)
    {
        if (extra is null)
            return;
        var known = DataJson.ReadOptions.GetTypeInfo(schema).Properties.Where(p => !p.IsExtensionData).Select(p => p.Name).ToList();
        foreach (var key in extra.Keys)
        {
            var guess = known.MinBy(k => Distance(k, key.ToLowerInvariant()));
            var hint = guess is not null && Distance(guess, key.ToLowerInvariant()) <= 2 ? $" Did you mean '{guess}'?" : "";
            sink.Warning(DiagnosticCodes.UnknownKey, path == "$" ? $"$.{key}" : $"{path}.{key}",
                $"unknown key '{key}' is kept but ignored.{hint}");
        }
    }

    private static int Distance(string a, string b)
    {
        var d = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) d[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = d[0];
            d[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cur = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = cur;
            }
        }
        return d[b.Length];
    }
}
