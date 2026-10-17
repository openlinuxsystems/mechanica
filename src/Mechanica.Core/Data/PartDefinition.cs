using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Data;

/// <summary>What kind of thing a <see cref="PartDefinition"/> describes.</summary>
public enum PartCategory
{
    /// <summary>A structural block or wedge on the build grid.</summary>
    Block,

    /// <summary>A placeable part with behaviour: bearings, engines, seats, logic, thrusters.</summary>
    Interactive,

    /// <summary>A placeable part that holds items, fuel or charge.</summary>
    Container,

    /// <summary>A placeable crafting or utility station.</summary>
    Station,

    /// <summary>A placeable light.</summary>
    Light,

    /// <summary>A placeable decorative prop.</summary>
    Decor,

    /// <summary>An inventory-only crafting material.</summary>
    Resource,

    /// <summary>An inventory item that is used up: food, seeds, fuel.</summary>
    Consumable,

    /// <summary>A handheld tool (Lift, Hammer, Connect Tool...).</summary>
    Tool,

    /// <summary>A handheld weapon.</summary>
    Weapon,
}

/// <summary>What a socket joins to. Real joints exist only at rotational, linear and spring sockets (D7).</summary>
public enum SocketType
{
    /// <summary>Rigid attachment; parts joined here merge into one Body.</summary>
    Structural,

    /// <summary>Hinge joint side of a bearing.</summary>
    Rotational,

    /// <summary>Slider joint side of a piston.</summary>
    Linear,

    /// <summary>Spring joint side of a suspension.</summary>
    Spring,
}

/// <summary>Collision primitive for a part.</summary>
public enum CollisionShape
{
    /// <summary>Axis-aligned box; uses <see cref="CollisionDefinition.Size"/>.</summary>
    Box,

    /// <summary>Sphere; uses <see cref="CollisionDefinition.Radius"/>.</summary>
    Sphere,

    /// <summary>Cylinder along local Y; uses radius and height.</summary>
    Cylinder,

    /// <summary>Capsule along local Y; uses radius and height.</summary>
    Capsule,

    /// <summary>Convex hull of a mesh file.</summary>
    Convex,
}

/// <summary>
/// One item or part definition, loaded from <c>parts/*.json</c> in a mod. The same schema covers
/// placeable parts and inventory-only items; placeable categories also need a size and collision.
/// Every balance number lives here, never in code. Part-specific numbers (torque, thrust, range)
/// go in <see cref="Properties"/>, which only the part's Lua script interprets (D2).
/// Keys are snake_case; units are SI.
/// </summary>
public sealed record PartDefinition
{
    /// <summary>Id, unique within the mod (lowercase snake_case, from CATALOG_IDS.md for vanilla).</summary>
    public string? Id { get; init; }

    /// <summary>Display name.</summary>
    public string? Name { get; init; }

    /// <summary>Optional tooltip text.</summary>
    public string? Description { get; init; }

    /// <summary>What kind of thing this is.</summary>
    public PartCategory? Category { get; init; }

    /// <summary>Mass in kg.</summary>
    public float? Mass { get; init; }

    /// <summary>
    /// Centre of mass relative to the part centre, in metres in the part's frame. Omitted means the
    /// part centre. It must lie within the footprint.
    /// </summary>
    public Vec3? CentreOfMass { get; init; }

    /// <summary>Durability rating; part HP derives from it (D13).</summary>
    public int? Durability { get; init; }

    /// <summary>Surface friction coefficient (0 or more).</summary>
    public float? Friction { get; init; }

    /// <summary>Buoyancy factor; buoyant force is this times displaced volume (D13).</summary>
    public float? Buoyancy { get; init; }

    /// <summary>Whether fire can ignite and spread through this part (D13).</summary>
    public bool? Flammable { get; init; }

    /// <summary>Maximum count per inventory slot. Omitted means 1.</summary>
    public int? StackSize { get; init; }

    /// <summary>Material for place/break/impact sounds (wood, metal, concrete, glass, ...).</summary>
    public string? SoundMaterial { get; init; }

    /// <summary>Footprint in 0.25 m grid cells. Required for placeable categories.</summary>
    public GridSize? Size { get; init; }

    /// <summary>Mesh asset (<c>.glb</c>/<c>.gltf</c>), relative to the mod root or <c>other_mod:path</c>.</summary>
    public string? Mesh { get; init; }

    /// <summary>Inventory icon (<c>.png</c>).</summary>
    public string? Icon { get; init; }

    /// <summary>Colour, surface material and colour variants. Omitted means the mesh's own materials.</summary>
    public VisualDefinition? Visual { get; init; }

    /// <summary>Collision shape. Omitted on a placeable part means a box filling <see cref="Size"/>.</summary>
    public CollisionDefinition? Collision { get; init; }

    /// <summary>Attachment points.</summary>
    public List<SocketDefinition>? Sockets { get; init; }

    /// <summary>Lua behaviour script (<c>.lua</c>).</summary>
    public string? Script { get; init; }

    /// <summary>Sound events this part plays, keyed by local event name (<c>"start": "engine_start"</c>).</summary>
    public Dictionary<string, string>? Sounds { get; init; }

    /// <summary>Item storage this part holds (chests, fuel tanks, stations' output). Placeable parts only.</summary>
    public InventoryDefinition? Inventory { get; init; }

    /// <summary>Makes the part a crafting station that runs recipes naming it. Needs <see cref="Inventory"/> for its output.</summary>
    public CraftingDefinition? Crafting { get; init; }

    /// <summary>
    /// Makes an item a seed that grows into a crop on soil (chunk 20). Items only. Planting it is the item's
    /// Lua script (<c>farm.plant</c>); growing and harvesting read this.
    /// </summary>
    public SeedDefinition? Seed { get; init; }

    /// <summary>Makes a placeable part farm soil that seeds are planted in (chunk 20).</summary>
    public SoilDefinition? Soil { get; init; }

    /// <summary>Makes a placeable part a respawn point a player can set (a bed, chunk 20).</summary>
    public bool? RespawnPoint { get; init; }

    /// <summary>
    /// Makes the part a node of the logic and power bus (chunk 10): how many Connect Tool links may come in
    /// and go out. Placeable parts only.
    /// </summary>
    public LogicDefinition? Logic { get; init; }

    /// <summary>
    /// Settings a player can change on each placed part (a logic gate's mode, a timer's delay), in display
    /// order. The part's script reads them with <c>part.setting(id)</c>. Placeable parts only.
    /// </summary>
    public List<SettingDefinition>? Settings { get; init; }

    /// <summary>Makes the part a seat a player can sit in (chunk 10). Placeable parts only.</summary>
    public SeatDefinition? Seat { get; init; }

    /// <summary>
    /// Makes the part give off light (chunk 23). It starts dark; the part's script turns it up with
    /// <c>part.set_light(level)</c>. Placeable parts only.
    /// </summary>
    public LightDefinition? Light { get; init; }

    /// <summary>Free-form, part-specific data for the Lua script. Key order is kept as written.</summary>
    public JsonObject? Properties { get; init; }

    /// <summary>Keys this build does not know. Kept so a round-trip never loses data.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>True for categories that exist in the world on the build grid.</summary>
    [JsonIgnore]
    public bool IsPlaceable => Category is PartCategory.Block or PartCategory.Interactive or PartCategory.Container
        or PartCategory.Station or PartCategory.Light or PartCategory.Decor;
}

/// <summary>The item storage of a placeable part. Inventory is a C# primitive (D2), configured here.</summary>
public sealed record InventoryDefinition
{
    /// <summary>Number of item slots (1 or more).</summary>
    public int? Slots { get; init; }

    /// <summary>
    /// Item ids the slots take (<c>gasoline</c>, <c>other_mod:fuel</c>; a bare id means vanilla). Omitted
    /// means any item.
    /// </summary>
    public List<string>? Accepts { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A crafting station: recipes that list this part in <c>stations</c> run here.</summary>
public sealed record CraftingDefinition
{
    /// <summary>Speed multiplier on recipe times (1 runs a recipe in its <c>time</c>; 2 in half). Omitted means 1.</summary>
    public float? Speed { get; init; }

    /// <summary>Most jobs waiting at once. Omitted means 1.</summary>
    public int? QueueSize { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A seed: planted in soil, it grows over time while the soil is watered, then is harvested.</summary>
public sealed record SeedDefinition
{
    /// <summary>Item a ripe crop yields (<c>carrot</c>, or <c>mod_id:item</c>).</summary>
    public string? Crop { get; init; }

    /// <summary>Seconds of watered growth until the crop is ripe (more than 0).</summary>
    public float? GrowTime { get; init; }

    /// <summary>How many crop items a harvest gives. Omitted means 1.</summary>
    public int? Yield { get; init; }

    /// <summary>How many seeds of this kind a harvest gives back. Omitted means 0.</summary>
    public int? Seeds { get; init; }

    /// <summary>What one ripe crop is worth to raids (chunk 21 scales raids by planted crop value). Omitted means 1.</summary>
    public float? Value { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Farm soil: one crop grows on top of the part.</summary>
public sealed record SoilDefinition
{
    /// <summary>Most seconds of water the soil holds (more than 0).</summary>
    public float? Capacity { get; init; }

    /// <summary>Speed multiplier on growth (1 grows a crop in its <c>grow_time</c>). Omitted means 1.</summary>
    public float? Growth { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// A part's place on the logic and power bus. Links go from a part with outputs to a part with inputs, inside
/// one Creation; what flows along them is each source's output value (D2: the scripts decide what it means).
/// </summary>
public sealed record LogicDefinition
{
    /// <summary>Most links that may end at the part (0 to <see cref="MaxLinks"/>). Omitted means 0, so it takes none.</summary>
    public int? Inputs { get; init; }

    /// <summary>Most links that may start at the part (0 to <see cref="MaxLinks"/>). Omitted means 0, so it sends none.</summary>
    public int? Outputs { get; init; }

    /// <summary>
    /// What the interact key (F) says on a placed part, such as "toggle" or "press"; it calls the script's
    /// <c>on_activate</c>. Omitted means players cannot use the part by hand.
    /// </summary>
    public string? Use { get; init; }

    /// <summary>Most links one side of a part may have.</summary>
    public const int MaxLinks = 64;

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>What kind of value a <see cref="SettingDefinition"/> holds.</summary>
public enum SettingType
{
    /// <summary>A number between <see cref="SettingDefinition.Min"/> and <see cref="SettingDefinition.Max"/>.</summary>
    Number,

    /// <summary>One of <see cref="SettingDefinition.Options"/> (snake_case ids).</summary>
    Choice,

    /// <summary>True or false.</summary>
    Toggle,

    /// <summary>Up to <see cref="SettingDefinition.MaxCount"/> numbers, each between min and max (a controller's frames).</summary>
    NumberList,
}

/// <summary>One setting a player can change on a placed part, with the Connect Tool's settings panel.</summary>
public sealed record SettingDefinition
{
    /// <summary>Id, unique within the part (lowercase snake_case); the script reads the setting by it.</summary>
    public string? Id { get; init; }

    /// <summary>Display name. Omitted shows the id.</summary>
    public string? Name { get; init; }

    /// <summary>What kind of value it holds.</summary>
    public SettingType? Type { get; init; }

    /// <summary>Lowest value of a number or of each list entry (required for those).</summary>
    public float? Min { get; init; }

    /// <summary>Highest value of a number or of each list entry (required for those).</summary>
    public float? Max { get; init; }

    /// <summary>Step the settings panel moves a number by. Omitted means any value.</summary>
    public float? Step { get; init; }

    /// <summary>The choices of a choice setting, as snake_case ids, in display order.</summary>
    public List<string>? Options { get; init; }

    /// <summary>Most entries of a number list (1 to 64; required for number lists).</summary>
    public int? MaxCount { get; init; }

    /// <summary>
    /// Value a newly placed part starts with. Omitted means min (number), the first option (choice), false
    /// (toggle) or an empty list.
    /// </summary>
    public JsonElement? Default { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A seat: a player sits on the part, and its script reads their steering (<c>part.occupant_input()</c>).</summary>
public sealed record SeatDefinition
{
    /// <summary>Where the sitting player's feet go, relative to the part centre in metres in the part's frame. Omitted means the centre.</summary>
    public Vec3? Offset { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>How a <see cref="LightDefinition"/> spreads its light.</summary>
public enum LightKind
{
    /// <summary>Every direction from one point (a lamp or a glowstick).</summary>
    Omni,

    /// <summary>A cone along <see cref="LightDefinition.Direction"/> (a headlight or a spotlight).</summary>
    Spot,
}

/// <summary>
/// The light a part gives off at full level. A light is a C# primitive (like sound), so its numbers live
/// here and its behaviour (when it is on, how bright, blinking) lives in the part's Lua script.
/// </summary>
public sealed record LightDefinition
{
    /// <summary>Omni or spot. Omitted means omni.</summary>
    public LightKind? Kind { get; init; }

    /// <summary>Light colour as hex text (<c>"#ffe8c0"</c>). Omitted means white.</summary>
    public string? Color { get; init; }

    /// <summary>Brightness at full level (Godot light energy, 0 or more). Omitted means 1.</summary>
    public float? Energy { get; init; }

    /// <summary>How far the light reaches, in m (more than 0).</summary>
    public float? Range { get; init; }

    /// <summary>A spot's half-angle, in degrees (more than 0, under 90). Omitted means 30.</summary>
    public float? Angle { get; init; }

    /// <summary>Where the light sits relative to the part centre, in m in the part's frame. Omitted means the centre.</summary>
    public Vec3? Offset { get; init; }

    /// <summary>A spot's direction in the part's frame. Omitted means -Z (the part's front).</summary>
    public Vec3? Direction { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A collision shape, in metres, relative to the part centre.</summary>
public sealed record CollisionDefinition
{
    /// <summary>Primitive kind.</summary>
    public CollisionShape? Shape { get; init; }

    /// <summary>Box extents in metres.</summary>
    public Vec3? Size { get; init; }

    /// <summary>Sphere, cylinder or capsule radius in metres.</summary>
    public float? Radius { get; init; }

    /// <summary>Cylinder or capsule height in metres.</summary>
    public float? Height { get; init; }

    /// <summary>Shape centre relative to the part centre, in metres.</summary>
    public Vec3? Offset { get; init; }

    /// <summary>Mesh for <see cref="CollisionShape.Convex"/>.</summary>
    public string? Mesh { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// How a part looks beyond its mesh: a base colour that tints it, a simple PBR surface and the
/// colour variants a player can pick. Colours are <c>"#rrggbb"</c> hex text.
/// </summary>
public sealed record VisualDefinition
{
    /// <summary>Base colour, <c>"#rrggbb"</c>. Multiplies the mesh's own colour; white leaves it as modelled.</summary>
    public string? Color { get; init; }

    /// <summary>Surface roughness, 0 (mirror) to 1 (matte).</summary>
    public float? Roughness { get; init; }

    /// <summary>Metalness, 0 to 1.</summary>
    public float? Metallic { get; init; }

    /// <summary>How opaque the surface is, 0 (invisible) to 1 (solid). Omitted means 1. Below 1 the part is drawn see-through.</summary>
    public float? Opacity { get; init; }

    /// <summary>Glow colour, <c>"#rrggbb"</c>, added on top of the lit surface. Omitted means none. It lights nothing else.</summary>
    public string? Emission { get; init; }

    /// <summary>Albedo texture (<c>.png</c>), relative to the mod root or <c>other_mod:path</c>.</summary>
    public string? Texture { get; init; }

    /// <summary>Whether the Paint Tool can recolour the part. Omitted means true.</summary>
    public bool? Paintable { get; init; }

    /// <summary>Named colour choices besides the base colour, in display order.</summary>
    public List<ColorVariant>? Variants { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A named colour choice for a part.</summary>
public sealed record ColorVariant
{
    /// <summary>Id, unique within the part (lowercase snake_case).</summary>
    public string? Id { get; init; }

    /// <summary>Display name.</summary>
    public string? Name { get; init; }

    /// <summary>The colour, <c>"#rrggbb"</c>.</summary>
    public string? Color { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Parses and formats <c>"#rrggbb"</c> colours.</summary>
public static class HexColor
{
    /// <summary>Reads <c>"#rrggbb"</c> into 0–1 channels. Case-insensitive.</summary>
    public static bool TryParse(string? text, out (float R, float G, float B) color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#' || !text[1..].All(char.IsAsciiHexDigit))
            return false;
        var value = Convert.ToInt32(text[1..], 16);
        color = (((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f);
        return true;
    }

    /// <summary>Formats 0–1 channels as lowercase <c>"#rrggbb"</c>, clamping out-of-range values.</summary>
    public static string Format(float r, float g, float b) =>
        $"#{Channel(r):x2}{Channel(g):x2}{Channel(b):x2}";

    private static int Channel(float v) => (int)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f);
}

/// <summary>An attachment point on a part.</summary>
public sealed record SocketDefinition
{
    /// <summary>Id, unique within the part.</summary>
    public string? Id { get; init; }

    /// <summary>What this socket joins to.</summary>
    public SocketType? Type { get; init; }

    /// <summary>Position relative to the part centre, in metres. Must lie within the part's footprint.</summary>
    public Vec3? Position { get; init; }

    /// <summary>Outward unit direction the socket faces.</summary>
    public Vec3? Normal { get; init; }

    /// <summary>
    /// How the joint at a rotational, linear or spring socket moves: its axis, travel and spring. Omitted
    /// means the defaults (see <see cref="JointDefinition"/>); structural sockets have none.
    /// </summary>
    public JointDefinition? Joint { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// The joint a rotational, linear or spring socket makes (D7). All values are in the part's own frame
/// and in SI units. The joint's zero is the pose the two sides were built in on the grid.
/// </summary>
public sealed record JointDefinition
{
    /// <summary>Longest travel (max − min) a linear or spring joint may have, in m.</summary>
    public const float MaxTravel = 16f;

    /// <summary>
    /// Unit grid direction the joint turns about (rotational) or slides along (linear, spring), in the
    /// part's frame. Omitted means the socket's normal: a bearing turns about its rotor face, a piston
    /// pushes out of its face. A suspension that carries a wheel on its side slides at right angles to it.
    /// </summary>
    public Vec3? Axis { get; init; }

    /// <summary>
    /// Lowest joint position: an angle in rad (rotational) or a distance along the axis in m (linear,
    /// spring). A rotational joint with neither limit spins freely. Omitted on a linear joint means 0
    /// (fully retracted); a spring needs both limits.
    /// </summary>
    public float? Min { get; init; }

    /// <summary>Highest joint position, in rad or m. Required for linear and spring joints.</summary>
    public float? Max { get; init; }

    /// <summary>Spring rate in N/m (spring joints only, required).</summary>
    public float? Stiffness { get; init; }

    /// <summary>Damping in N·s/m (spring joints only). Omitted means 0, an undamped spring.</summary>
    public float? Damping { get; init; }

    /// <summary>
    /// Linear and spring joints only: draws a rod of this radius (m), in the part's colour, from the
    /// anchor to where the far side is now, so an extended piston shows its shaft. Omitted draws none.
    /// </summary>
    public float? ShaftRadius { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
