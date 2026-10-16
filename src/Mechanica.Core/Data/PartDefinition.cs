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

    /// <summary>Collision shape. Omitted on a placeable part means a box filling <see cref="Size"/>.</summary>
    public CollisionDefinition? Collision { get; init; }

    /// <summary>Attachment points.</summary>
    public List<SocketDefinition>? Sockets { get; init; }

    /// <summary>Lua behaviour script (<c>.lua</c>).</summary>
    public string? Script { get; init; }

    /// <summary>Sound events this part plays, keyed by local event name (<c>"start": "engine_start"</c>).</summary>
    public Dictionary<string, string>? Sounds { get; init; }

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

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
