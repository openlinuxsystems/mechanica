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
        // A loose part with no durability (a rod) cannot break; anything built needs one.
        else if (placeable && part.Loose != true && part.Durability is null)
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

        if (part.CentreOfMass is { } com)
        {
            var extent = (part.Size ?? GridSize.One).Metres;
            if (!float.IsFinite(com.X) || !float.IsFinite(com.Y) || !float.IsFinite(com.Z))
                sink.Error(Code, "$.centre_of_mass", "centre_of_mass must be finite.");
            else if (MathF.Abs(com.X) > extent.X / 2 + Epsilon || MathF.Abs(com.Y) > extent.Y / 2 + Epsilon || MathF.Abs(com.Z) > extent.Z / 2 + Epsilon)
                sink.Error(Code, "$.centre_of_mass", $"centre_of_mass {com} is outside the part's {part.Size ?? GridSize.One} footprint.");
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
        ValidateLogic(part, placeable, sink);
        ValidateLight(part, placeable, sink);
        ValidateAnimation(part, placeable, sink);
        ValidateMenu(part, placeable, sink);
        ValidateDisplay(part, placeable, sink);
        ValidateUpgrades(part, placeable, sink);
        for (var i = 0; i < (part.Settings?.Count ?? 0); i++)
        {
            foreach (var (option, icon) in part.Settings![i].Icons ?? [])
                CheckAsset(icon, $"$.settings[{i}].icons.{option}", AssetKind.Texture, sink);
        }

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
        CheckAsset(v.EmissionTexture, $"{p}.emission_texture", AssetKind.Texture, sink);
        if (v.EmissionTexture is not null && v.Emission is null)
            sink.Error(Code, $"{p}.emission_texture", "emission_texture needs an emission colour to glow with.");
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
            if (s.Joint is { } joint)
                ValidateJoint(joint, s.Type, $"{path}.joint", sink);
            else if (s.Type == SocketType.Spring)
                sink.Error(Code, $"{path}.joint", "a spring socket needs a joint with min, max and stiffness.");
            else if (s.Type == SocketType.Linear)
                sink.Error(Code, $"{path}.joint", "a linear socket needs a joint with max (its travel in m).");
            KnownKeys.WarnUnknown(s.Extra, path, sink, typeof(SocketDefinition));
        }
    }

    private static void ValidateJoint(JointDefinition j, SocketType? type, string path, DiagnosticSink sink)
    {
        KnownKeys.WarnUnknown(j.Extra, path, sink, typeof(JointDefinition));
        if (type is null)
            return;
        if (type == SocketType.Structural)
        {
            sink.Error(Code, path, "only rotational, linear and spring sockets have a joint.");
            return;
        }
        if (j.Axis is { } axis && !IsGridDirection(axis))
            sink.Error(Code, $"{path}.axis", $"axis {axis} must be a grid direction like [0, 1, 0].");
        if (j.ShaftRadius is { } shaft && (!float.IsFinite(shaft) || shaft <= 0 || shaft > 1))
            sink.Error(Code, $"{path}.shaft_radius", "shaft_radius must be more than 0 and at most 1 m.");
        else if (j.ShaftRadius is not null && type == SocketType.Rotational)
            sink.Error(Code, $"{path}.shaft_radius", "only linear and spring joints draw a shaft.");
        foreach (var (value, key) in new[] { (j.Min, "min"), (j.Max, "max"), (j.Stiffness, "stiffness"), (j.Damping, "damping") })
        {
            if (value is { } v && !float.IsFinite(v))
                sink.Error(Code, $"{path}.{key}", $"{key} must be a finite number.");
        }
        if (type != SocketType.Spring)
        {
            if (j.Stiffness is not null)
                sink.Error(Code, $"{path}.stiffness", "only spring joints have a stiffness.");
            if (j.Damping is not null)
                sink.Error(Code, $"{path}.damping", "only spring joints have damping.");
        }
        switch (type)
        {
            case SocketType.Rotational:
                if ((j.Min is null) != (j.Max is null))
                    sink.Error(Code, j.Min is null ? $"{path}.min" : $"{path}.max", "a rotational joint has both min and max (a limited hinge) or neither (it spins freely).");
                else if (j.Min is { } lo && j.Max is { } hi)
                {
                    if (lo < -MathF.PI - Epsilon || hi > MathF.PI + Epsilon)
                        sink.Error(Code, $"{path}.min", $"hinge limits must be within ±π rad (±180°); got {JsonNumbers.Format(lo)} to {JsonNumbers.Format(hi)}.");
                    else if (lo >= hi)
                        sink.Error(Code, $"{path}.max", "max must be more than min.");
                    else if (lo > Epsilon || hi < -Epsilon)
                        sink.Error(Code, $"{path}.min", "a hinge's range must include 0, the angle it is built at.");
                }
                break;
            case SocketType.Linear:
                if (j.Max is not { } max)
                    sink.Error(Code, $"{path}.max", "max (how far the joint extends, in m) is required for a linear joint.");
                else
                    CheckTravel(j.Min ?? 0f, max, path, sink);
                break;
            case SocketType.Spring:
                if (j.Min is not { } min)
                    sink.Error(Code, $"{path}.min", "min (how far the spring compresses, in m, 0 or less) is required for a spring joint.");
                if (j.Max is not { } top)
                    sink.Error(Code, $"{path}.max", "max (how far the spring extends, in m, 0 or more) is required for a spring joint.");
                if (j.Min is { } a && j.Max is { } b)
                    CheckTravel(a, b, path, sink);
                if (j.Stiffness is not { } k)
                    sink.Error(Code, $"{path}.stiffness", "stiffness (N/m) is required for a spring joint.");
                else if (k <= 0)
                    sink.Error(Code, $"{path}.stiffness", "stiffness must be more than 0.");
                if (j.Damping is < 0)
                    sink.Error(Code, $"{path}.damping", "damping must be 0 or more.");
                break;
        }
    }

    private static void CheckTravel(float min, float max, string path, DiagnosticSink sink)
    {
        if (min >= max)
            sink.Error(Code, $"{path}.max", "max must be more than min.");
        else if (min > Epsilon || max < -Epsilon)
            sink.Error(Code, $"{path}.min", "the travel must include 0, the position the joint is built at.");
        else if (max - min > JointDefinition.MaxTravel)
            sink.Error(Code, $"{path}.max", $"travel is {JsonNumbers.Format(max - min)} m; the most allowed is {JsonNumbers.Format(JointDefinition.MaxTravel)} m.");
    }

    private static bool IsGridDirection(Vec3 v) =>
        new[] { MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z) } is var a
        && a.Count(c => MathF.Abs(c - 1f) < 1e-3f) == 1 && a.Count(c => c < 1e-3f) == 2;

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
            if (crafting.LinkRange is { } range && !(range >= 0 && range <= MaxLinkRange))
                sink.Error(Code, $"{p}.link_range", $"link_range must be 0 to {MaxLinkRange} m.");
            KnownKeys.WarnUnknown(crafting.Extra, p, sink, typeof(CraftingDefinition));
        }
    }

    // Upgrade levels (UI spec §4): each level's cost and the part it makes, which must still be a valid part.
    private static void ValidateUpgrades(PartDefinition part, bool placeable, DiagnosticSink sink)
    {
        if (part.Upgrades is not { } upgrades)
            return;
        if (!placeable)
            sink.Error(Code, "$.upgrades", "only placeable parts can be upgraded.");
        if (upgrades.Count > PartUpgrades.MaxLevels)
            sink.Error(Code, "$.upgrades", $"a part has at most {PartUpgrades.MaxLevels} upgrade levels (got {upgrades.Count}).");
        var before = part;
        for (var i = 0; i < upgrades.Count && i < PartUpgrades.MaxLevels; i++)
        {
            var p = $"$.upgrades[{i}]";
            var level = upgrades[i];
            for (var c = 0; c < (level.Cost?.Count ?? 0); c++)
            {
                var cost = level.Cost![c];
                if (!IsItemRef(cost.Item))
                    sink.Error(Code, $"{p}.cost[{c}].item", $"'{cost.Item}' must be an item id or 'mod_id:item_id'.");
                if (cost.Count is not >= 1)
                    sink.Error(Code, $"{p}.cost[{c}].count", "count must be 1 or more.");
                KnownKeys.WarnUnknown(cost.Extra, $"{p}.cost[{c}]", sink, typeof(Items.RecipeItem));
            }
            KnownKeys.WarnUnknown(level.Extra, p, sink, typeof(UpgradeDefinition));
            if (level.Set is not { Count: > 0 })
            {
                sink.Error(Code, $"{p}.set", "set is required: the part keys this level changes.");
                break;
            }
            if (PartUpgrades.Apply(part, i + 1, out var error) is not { } after)
            {
                sink.Error(Code, $"{p}.set", error!);
                break;
            }
            var found = new List<DataDiagnostic>();
            var inner = new DiagnosticSink(sink.File, found);
            ValidateStorage(after, placeable, inner);
            foreach (var d in found.Where(d => d.Severity == DiagnosticSeverity.Error))
                sink.Error(Code, d.JsonPath is { } path ? $"{p}.set{path[1..]}" : $"{p}.set", d.Message);
            if (after.Inventory?.Slots < before.Inventory?.Slots || (before.Inventory is not null && after.Inventory is null))
                sink.Error(Code, $"{p}.set.inventory", "an upgrade cannot take inventory slots away.");
            if (before.Crafting is not null && after.Crafting is null)
                sink.Error(Code, $"{p}.set.crafting", "an upgrade cannot stop a station crafting.");
            before = after;
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
        ItemOnly(part.Aim, "aim");
        if (part.Aim is { } aim)
        {
            if (aim.Zoom is { } zoom && !(zoom is >= 0.2f and <= 1f))
                sink.Error(Code, "$.aim.zoom", $"zoom must be 0.2 to 1 (got {zoom}).");
            KnownKeys.WarnUnknown(aim.Extra, "$.aim", sink, typeof(AimDefinition));
        }
        PartOnly(part.Soil, "soil");
        PartOnly(part.RespawnPoint == true ? true : null, "respawn_point");
        PartOnly(part.GroundOnly == true ? true : null, "ground_only");
        PartOnly(part.BreaksInto, "breaks_into");
        PartOnly(part.SledgeDamage, "sledge_damage");
        if (part.BreaksInto is { } breaks)
            World.WorldGenValidator.Break(breaks, "$.breaks_into", sink, Code);
        if (part.SledgeDamage is { } sledge && !(sledge >= 0 && float.IsFinite(sledge)))
            sink.Error(Code, "$.sledge_damage", "sledge_damage must be 0 or more.");
        if (part.Loose == true && part.GroundOnly == true)
            sink.Error(Code, "$.loose", "a loose part cannot also be ground_only.");
        PartOnly(part.GroundPart, "ground_part");
        PartOnly(part.PlacedFrom, "placed_from");
        if (part.MaxSlope is { } slope)
        {
            if (part.GroundOnly != true)
                sink.Error(Code, "$.max_slope", "only ground_only parts can have max_slope.");
            if (!(slope is >= 0 and <= 90))
                sink.Error(Code, "$.max_slope", $"max_slope must be 0 to 90 degrees (got {slope}).");
        }
        if (part.GroundPart is { } groundPart && !IsItemRef(groundPart))
            sink.Error(Code, "$.ground_part", "ground_part must be a part id or 'mod_id:part_id'.");
        if (part.PlacedFrom is { } from && !IsItemRef(from))
            sink.Error(Code, "$.placed_from", "placed_from must be an item id or 'mod_id:item_id'.");
        if (part.GroundPart is not null && part.PlacedFrom is not null)
            sink.Error(Code, "$.ground_part", "a part placed from another item cannot set down a ground_part of its own.");
        if (part.Refine is { } refine)
        {
            if (part.Loose != true)
                sink.Error(Code, "$.refine", "only loose parts can be refined by hand.");
            if (refine.Count == 0)
                sink.Error(Code, "$.refine", "refine needs at least one item.");
            for (var r = 0; r < refine.Count; r++)
            {
                if (!IsItemRef(refine[r].Item))
                    sink.Error(Code, $"$.refine[{r}].item", "item must be an item id or 'mod_id:item_id'.");
                if (refine[r].Count is not >= 1)
                    sink.Error(Code, $"$.refine[{r}].count", "count must be 1 or more.");
            }
        }
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
            RequirePositive(soil.DryTime, "$.soil.dry_time", "dry_time (s)", false, "soil", sink);
            if (soil.WetLevel is { } wet && !(wet > 0 && wet <= 1))
                sink.Error(Code, "$.soil.wet_level", $"wet_level must be more than 0 and at most 1 (got {wet}).");
            KnownKeys.WarnUnknown(soil.Extra, "$.soil", sink, typeof(SoilDefinition));
        }
    }

    private static void ValidateAnimation(PartDefinition part, bool placeable, DiagnosticSink sink)
    {
        if (part.Animation is not { } animation)
            return;
        const string p = "$.animation";
        if (!placeable)
            sink.Error(Code, p, "only placeable parts can have animation.");
        if (animation.Axis is { } a && !(float.IsFinite(a.X) && float.IsFinite(a.Y) && float.IsFinite(a.Z) && a.X * a.X + a.Y * a.Y + a.Z * a.Z > 1e-6f))
            sink.Error(Code, $"{p}.axis", "axis must be a non-zero vector.");
        var extent = (part.Size ?? GridSize.One).Metres;
        if (animation.Pivot is { } o && (!float.IsFinite(o.X) || !float.IsFinite(o.Y) || !float.IsFinite(o.Z)
            || MathF.Abs(o.X) > extent.X / 2 + Epsilon || MathF.Abs(o.Y) > extent.Y / 2 + Epsilon || MathF.Abs(o.Z) > extent.Z / 2 + Epsilon))
            sink.Error(Code, $"{p}.pivot", $"animation pivot {o} is outside the part's {part.Size ?? GridSize.One} footprint.");
        KnownKeys.WarnUnknown(animation.Extra, p, sink, typeof(AnimationDefinition));
    }

    private static void ValidateMenu(PartDefinition part, bool placeable, DiagnosticSink sink)
    {
        if (part.Menu is not { } menu)
            return;
        const string p = "$.menu";
        if (!placeable)
            sink.Error(Code, p, "only placeable parts can have a menu.");
        if (menu.Use == MenuUse.Cycle && part.Settings?.FirstOrDefault(s => s.Id == menu.Cycle) is not { Type: SettingType.Choice })
            sink.Error(Code, $"{p}.cycle", $"a cycling menu needs cycle to name one of the part's choice settings, not '{menu.Cycle}'.");
        if (menu.Cycle is not null && menu.Use != MenuUse.Cycle)
            sink.Error(Code, $"{p}.cycle", "cycle needs \"use\": \"cycle\".");
        if ((menu.Readouts?.Count > 0 || menu.Preview is not null) && part.Script is null)
            sink.Error(Code, p, "readouts and previews come from the part's script; it has none.");
        ValidateReadouts(menu.Readouts, $"{p}.readouts", sink);
        if (menu.Preview is { } preview)
        {
            var path = $"{p}.preview";
            if (part.Settings?.FirstOrDefault(s => s.Id == preview.Range) is not { Type: SettingType.Number })
                sink.Error(Code, $"{path}.range", $"range must name one of the part's number settings, not '{preview.Range}'.");
            if (preview.Axis is { } a && !NonZero(a))
                sink.Error(Code, $"{path}.axis", "axis must be a non-zero vector.");
            if (preview.Shape == PreviewShape.Cone && preview.Angle is not (> 0 and < 90))
                sink.Error(Code, $"{path}.angle", "a cone needs an angle more than 0 and under 90 degrees.");
            KnownKeys.WarnUnknown(preview.Extra, path, sink, typeof(PreviewDefinition));
        }
        KnownKeys.WarnUnknown(menu.Extra, p, sink, typeof(MenuDefinition));
    }

    private static void ValidateReadouts(List<ReadoutDefinition>? readouts, string p, DiagnosticSink sink)
    {
        var ids = new HashSet<string>();
        for (var i = 0; i < (readouts?.Count ?? 0); i++)
        {
            var readout = readouts![i];
            var path = $"{p}[{i}]";
            if (!Identifiers.IsValid(readout.Id))
                sink.Error(Code, $"{path}.id", readout.Id is null ? "readout id is required." : $"readout id '{readout.Id}' must be lowercase snake_case.");
            else if (!ids.Add(readout.Id))
                sink.Error(Code, $"{path}.id", $"readout id '{readout.Id}' is listed twice.");
            RequirePositive(readout.Max, $"{path}.max", "max", false, "a readout", sink);
            if (readout.Decimals is < 0 or > 4)
                sink.Error(Code, $"{path}.decimals", "decimals must be 0 to 4.");
            if (readout.Unit is { Length: > 12 })
                sink.Error(Code, $"{path}.unit", "unit must be at most 12 characters.");
            KnownKeys.WarnUnknown(readout.Extra, path, sink, typeof(ReadoutDefinition));
        }
        if (readouts?.Count > Logic.PartReadouts.MaxPerPart)
            sink.Error(Code, p, $"a part shows at most {Logic.PartReadouts.MaxPerPart} readouts.");
    }

    private static void ValidateDisplay(PartDefinition part, bool placeable, DiagnosticSink sink)
    {
        if (part.Display is not { } display)
            return;
        const string p = "$.display";
        if (!placeable)
            sink.Error(Code, p, "only placeable parts can have a display.");
        if (display.Status is { } status && !Identifiers.IsValid(status))
            sink.Error(Code, $"{p}.status", "status names a snake_case readout id.");
        var extent = (part.Size ?? GridSize.One).Metres;
        bool Inside(Vec3? o) => o is not { } v || (float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z)
            && MathF.Abs(v.X) <= extent.X / 2 + Epsilon && MathF.Abs(v.Y) <= extent.Y / 2 + Epsilon && MathF.Abs(v.Z) <= extent.Z / 2 + Epsilon);
        void Readout(string? id, string path)
        {
            if (!Identifiers.IsValid(id))
                sink.Error(Code, $"{path}.readout", "readout must name a snake_case readout id.");
        }
        if (display.Glow is { } glow)
        {
            Readout(glow.Readout, $"{p}.glow");
            if (!Inside(glow.Offset))
                sink.Error(Code, $"{p}.glow.offset", "the glow's offset is outside the part's footprint.");
            if (glow.Direction is { } d && !NonZero(d))
                sink.Error(Code, $"{p}.glow.direction", "direction must be a non-zero vector.");
            RequirePositive(glow.Length, $"{p}.glow.length", "length (m)", false, "a glow", sink);
            KnownKeys.WarnUnknown(glow.Extra, $"{p}.glow", sink, typeof(GlowDefinition));
        }
        if (display.Bar is { } bar)
        {
            Readout(bar.Readout, $"{p}.bar");
            if (!Inside(bar.Offset))
                sink.Error(Code, $"{p}.bar.offset", "the bar's offset is outside the part's footprint.");
            if (bar.Axis is { } a && !NonZero(a))
                sink.Error(Code, $"{p}.bar.axis", "axis must be a non-zero vector.");
            RequirePositive(bar.Length, $"{p}.bar.length", "length (m)", true, "a bar", sink);
            KnownKeys.WarnUnknown(bar.Extra, $"{p}.bar", sink, typeof(BarDefinition));
        }
        if (display.Highlight is { } highlight)
        {
            Readout(highlight.Readout, $"{p}.highlight");
            if (!Inside(highlight.Offset))
                sink.Error(Code, $"{p}.highlight.offset", "the highlight's offset is outside the part's footprint.");
            if (highlight.Axis is { } a && !NonZero(a))
                sink.Error(Code, $"{p}.highlight.axis", "axis must be a non-zero vector.");
            RequirePositive(highlight.Radius, $"{p}.highlight.radius", "radius (m)", true, "a highlight", sink);
            KnownKeys.WarnUnknown(highlight.Extra, $"{p}.highlight", sink, typeof(HighlightDefinition));
        }
        if (display.Press is { } press)
        {
            Readout(press.Readout, $"{p}.press");
            if (press.Axis is { } a && !NonZero(a))
                sink.Error(Code, $"{p}.press.axis", "axis must be a non-zero vector.");
            if (press.Depth is { } depth && !(depth > 0 && depth <= 0.1f))
                sink.Error(Code, $"{p}.press.depth", "depth must be more than 0 and at most 0.1 m.");
            KnownKeys.WarnUnknown(press.Extra, $"{p}.press", sink, typeof(PressDefinition));
        }
        if (display.Clips is { } clips)
        {
            if (part.Mesh is null)
                sink.Error(Code, $"{p}.clips", "clips play the part's own model, so the part needs a mesh.");
            Readout(clips.Readout, $"{p}.clips");
            if (clips.Scrub?.Count > ClipsDefinition.MaxScrubs)
                sink.Error(Code, $"{p}.clips.scrub", $"a part scrubs at most {ClipsDefinition.MaxScrubs} clips.");
            for (var i = 0; i < (clips.Scrub?.Count ?? 0); i++)
            {
                var scrub = clips.Scrub![i];
                Readout(scrub.Readout, $"{p}.clips.scrub[{i}]");
                if (string.IsNullOrWhiteSpace(scrub.Clip) || scrub.Clip.Length > 64)
                    sink.Error(Code, $"{p}.clips.scrub[{i}].clip", "clip must name one of the model's clips (1 to 64 characters).");
                KnownKeys.WarnUnknown(scrub.Extra, $"{p}.clips.scrub[{i}]", sink, typeof(ScrubDefinition));
            }
            KnownKeys.WarnUnknown(clips.Extra, $"{p}.clips", sink, typeof(ClipsDefinition));
        }
        if (display.Effects?.Count > EffectDefinition.MaxPerPart)
            sink.Error(Code, $"{p}.effects", $"a part has at most {EffectDefinition.MaxPerPart} effects.");
        for (var i = 0; i < (display.Effects?.Count ?? 0); i++)
        {
            var effect = display.Effects![i];
            var path = $"{p}.effects[{i}]";
            Readout(effect.Readout, path);
            if (effect.Kind is null)
                sink.Error(Code, $"{path}.kind", $"kind is required: one of {EnumList<EffectKind>()}.");
            if (!Inside(effect.Offset))
                sink.Error(Code, $"{path}.offset", "the effect's offset is outside the part's footprint.");
            if (effect.Direction is { } d && !NonZero(d))
                sink.Error(Code, $"{path}.direction", "direction must be a non-zero vector.");
            CheckColor(effect.Color, $"{path}.color", sink);
            KnownKeys.WarnUnknown(effect.Extra, path, sink, typeof(EffectDefinition));
        }
        KnownKeys.WarnUnknown(display.Extra, p, sink, typeof(DisplayDefinition));
    }

    private static bool NonZero(Vec3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && v.X * v.X + v.Y * v.Y + v.Z * v.Z > 1e-6f;

    private static void ValidateLight(PartDefinition part, bool placeable, DiagnosticSink sink)
    {
        if (part.Light is not { } light)
            return;
        const string p = "$.light";
        if (!placeable)
            sink.Error(Code, p, "only placeable parts can have light.");
        CheckColor(light.Color, $"{p}.color", sink);
        RequireNonNegative(light.Energy, $"{p}.energy", "energy", false, "a light", sink);
        RequirePositive(light.Range, $"{p}.range", "range (m)", true, "a light", sink);
        if (light.Angle is { } angle && !(angle > 0 && angle < 90))
            sink.Error(Code, $"{p}.angle", "angle must be more than 0 and under 90 degrees.");
        var extent = (part.Size ?? GridSize.One).Metres;
        if (light.Offset is { } o && (!float.IsFinite(o.X) || !float.IsFinite(o.Y) || !float.IsFinite(o.Z)
            || MathF.Abs(o.X) > extent.X / 2 + Epsilon || MathF.Abs(o.Y) > extent.Y / 2 + Epsilon || MathF.Abs(o.Z) > extent.Z / 2 + Epsilon))
            sink.Error(Code, $"{p}.offset", $"light offset {o} is outside the part's {part.Size ?? GridSize.One} footprint.");
        if (light.Direction is { } d && !(float.IsFinite(d.X) && float.IsFinite(d.Y) && float.IsFinite(d.Z) && d.X * d.X + d.Y * d.Y + d.Z * d.Z > 1e-6f))
            sink.Error(Code, $"{p}.direction", "direction must be a non-zero vector.");
        KnownKeys.WarnUnknown(light.Extra, p, sink, typeof(LightDefinition));
    }

    private static void ValidateLogic(PartDefinition part, bool placeable, DiagnosticSink sink)
    {
        foreach (var (section, key) in new (object? Section, string Key)[] { (part.Logic, "logic"), (part.Settings, "settings"), (part.Seat, "seat") })
        {
            if (section is not null && !placeable)
                sink.Error(Code, $"$.{key}", $"only placeable parts can have {key}.");
        }
        if (part.Logic is { } logic)
        {
            if (logic.Inputs is < 0 or > LogicDefinition.MaxLinks)
                sink.Error(Code, "$.logic.inputs", $"inputs must be 0 to {LogicDefinition.MaxLinks}.");
            if (logic.Outputs is < 0 or > LogicDefinition.MaxLinks)
                sink.Error(Code, "$.logic.outputs", $"outputs must be 0 to {LogicDefinition.MaxLinks}.");
            if (logic.Use is { } use && (string.IsNullOrWhiteSpace(use) || use.Length > 40))
                sink.Error(Code, "$.logic.use", "use must be a short label (1 to 40 characters).");
            if (logic.Use is not null && part.Script is null)
                sink.Error(Code, "$.logic.use", "a part players can use needs a script with on_activate.");
            if (logic.Accepts is { } accepts && accepts.Distinct().Count() != accepts.Count)
                sink.Error(Code, "$.logic.accepts", "accepts lists a kind twice.");
            if (logic.Reverse is { } reverse && part.Settings?.FirstOrDefault(s => s.Id == reverse) is not { Type: SettingType.Toggle })
                sink.Error(Code, "$.logic.reverse", $"reverse must name one of the part's toggle settings, not '{reverse}'.");
            KnownKeys.WarnUnknown(logic.Extra, "$.logic", sink, typeof(LogicDefinition));
        }
        if (part.Seat is { } seat)
        {
            var extent = (part.Size ?? GridSize.One).Metres;
            if (seat.Offset is { } o && (MathF.Abs(o.X) > extent.X / 2 + Epsilon || MathF.Abs(o.Y) > extent.Y / 2 + Epsilon || MathF.Abs(o.Z) > extent.Z / 2 + Epsilon))
                sink.Error(Code, "$.seat.offset", $"seat offset {o} is outside the part's {part.Size ?? GridSize.One} footprint.");
            KnownKeys.WarnUnknown(seat.Extra, "$.seat", sink, typeof(SeatDefinition));
        }
        var ids = new HashSet<string>();
        for (var i = 0; i < (part.Settings?.Count ?? 0); i++)
        {
            var setting = part.Settings![i];
            var path = $"$.settings[{i}]";
            var errors = sink.ErrorCount;
            if (!Identifiers.IsValid(setting.Id))
                sink.Error(Code, $"{path}.id", setting.Id is null ? "setting id is required." : $"setting id '{setting.Id}' must be lowercase snake_case.");
            else if (!ids.Add(setting.Id))
                sink.Error(Code, $"{path}.id", $"setting id '{setting.Id}' is used twice in this part.");
            switch (setting.Type)
            {
                case null:
                    sink.Error(Code, $"{path}.type", $"type is required: one of {EnumList<SettingType>()}.");
                    break;
                case SettingType.Number or SettingType.NumberList or SettingType.Sequence:
                    if (setting.Min is not { } min || setting.Max is not { } max || !float.IsFinite(min) || !float.IsFinite(max) || min > max)
                        sink.Error(Code, $"{path}.min", "a number setting needs finite min and max, with min at most max.");
                    if (setting.Step is { } step && !(step > 0 && float.IsFinite(step)))
                        sink.Error(Code, $"{path}.step", "step must be greater than 0.");
                    if (setting.Type != SettingType.Number && setting.MaxCount is not (>= 1 and <= LogicDefinition.MaxLinks))
                        sink.Error(Code, $"{path}.max_count", $"max_count (1 to {LogicDefinition.MaxLinks}) is required for a number list or sequence.");
                    if (setting.Type == SettingType.Sequence)
                        RequirePositive(setting.MaxSpeed, $"{path}.max_speed", "max_speed", true, "a sequence", sink);
                    break;
                case SettingType.Choice:
                    if (setting.Options is not { Count: > 0 } options)
                        sink.Error(Code, $"{path}.options", "a choice setting needs at least one option.");
                    else if (options.Any(o => !Identifiers.IsValid(o)) || options.Distinct().Count() != options.Count)
                        sink.Error(Code, $"{path}.options", "options must be distinct lowercase snake_case ids.");
                    break;
            }
            if (setting.MaxSpeed is not null && setting.Type != SettingType.Sequence)
                sink.Error(Code, $"{path}.max_speed", "only a sequence setting has max_speed.");
            if (setting.Preview is { } preview && (setting.Type != SettingType.Sequence || !Identifiers.IsValid(preview)))
                sink.Error(Code, $"{path}.preview", "preview names a snake_case on_command name, on a sequence setting only.");
            for (var j = 0; j < (setting.Presets?.Count ?? 0); j++)
            {
                var preset = setting.Presets![j];
                if (setting.Type != SettingType.Number)
                    sink.Error(Code, $"{path}.presets", "only a number setting has presets.");
                else if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 24)
                    sink.Error(Code, $"{path}.presets[{j}].name", "a preset needs a short name (1 to 24 characters).");
                else if (preset.Value is not { } v || !float.IsFinite(v) || v < (setting.Min ?? float.NegativeInfinity) || v > (setting.Max ?? float.PositiveInfinity))
                    sink.Error(Code, $"{path}.presets[{j}].value", "a preset's value must be between the setting's min and max.");
                KnownKeys.WarnUnknown(preset.Extra, $"{path}.presets[{j}]", sink, typeof(SettingPreset));
            }
            if (setting.Group is { } group && (setting.Type != SettingType.Toggle || string.IsNullOrWhiteSpace(group) || group.Length > 24))
                sink.Error(Code, $"{path}.group", "group is a short heading (1 to 24 characters) on toggle settings only.");
            if (setting.Icons is { } icons && (setting.Type != SettingType.Choice || icons.Keys.Any(k => setting.Options?.Contains(k) != true)))
                sink.Error(Code, $"{path}.icons", "icons are for a choice setting's options, keyed by option id.");
            if (setting.Default is { } value && setting.Type is not null && sink.ErrorCount == errors
                && Logic.PartSettings.Check(setting, value) is { } bad)
                sink.Error(Code, $"{path}.default", $"default: {bad}");
            KnownKeys.WarnUnknown(setting.Extra, path, sink, typeof(SettingDefinition));
        }
    }

    /// <summary>Most slots one inventory may have.</summary>
    internal const int MaxSlots = 256;

    /// <summary>Most jobs one station may queue.</summary>
    internal const int MaxQueue = 64;

    /// <summary>Longest a station's <c>crafting.link_range</c> may be, in metres.</summary>
    internal const float MaxLinkRange = 8f;

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
