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

    /// <summary>A handheld tool (Lift, Sledgehammer, Connect Tool...).</summary>
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
    /// Makes a placeable part go only flat on the ground, as Scrap Mechanic's soil does: upright (its local +Y
    /// straight up), resting on the world itself, never tipped onto a side or set on a Creation.
    /// </summary>
    public bool? GroundOnly { get; init; }

    /// <summary>
    /// Steepest ground, in degrees from level, a ground-only part may be set on (soil: 17, as in Scrap Mechanic).
    /// Omitted allows any slope the part rests on. Ground-only parts only.
    /// </summary>
    public float? MaxSlope { get; init; }

    /// <summary>
    /// The part this item sets down when it is placed on the ground (the world itself, not a Creation), one for one, as
    /// a Soil Bag spills into a soil mound. Holding Force build sets down the item's own part instead, and so does
    /// placing it on a Creation. The part named must be ground-only and say this item in its <see cref="PlacedFrom"/>.
    /// Placeable parts only.
    /// </summary>
    public string? GroundPart { get; init; }

    /// <summary>
    /// The item this part is placed from and comes back as when it is picked up (a soil mound is placed from, and picked
    /// up as, a Soil Bag). Such a part is never an item of its own: it is not listed in Creative or on the hotbar.
    /// Placeable parts only.
    /// </summary>
    public string? PlacedFrom { get; init; }

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

    /// <summary>
    /// Makes a held item (a weapon) aim while right click is held (UI spec §10): the view zooms in a little and the
    /// crosshair tightens; the item's script sees <c>target.aiming</c>. Items only.
    /// </summary>
    public AimDefinition? Aim { get; init; }

    /// <summary>
    /// How the item looks in a player's hand: a model held in the right hand, seen by every player. Items only.
    /// Without it an item is held up with nothing drawn.
    /// </summary>
    public HeldDefinition? Held { get; init; }

    /// <summary>Makes the part a seat a player can sit in (chunk 10). Placeable parts only.</summary>
    public SeatDefinition? Seat { get; init; }

    /// <summary>
    /// Levels a placed part can be upgraded through with Upgrade (U), first to last (UI spec §4, §8). Each level
    /// costs items and changes some of the part's data; see <see cref="UpgradeDefinition"/>. Placeable parts only.
    /// </summary>
    public List<UpgradeDefinition>? Upgrades { get; init; }

    /// <summary>
    /// Makes the part give off light (chunk 23). It starts dark; the part's script turns it up with
    /// <c>part.set_light(level)</c>. Placeable parts only.
    /// </summary>
    public LightDefinition? Light { get; init; }

    /// <summary>
    /// Lets the part's script turn its model about an axis (chunk 23): a rotor's blades, a flap, a saw.
    /// The script sets the angle and speed with <c>part.animate(angle, speed)</c>; it moves only how the part
    /// looks, never its collision or its Body. Placeable parts only.
    /// </summary>
    public AnimationDefinition? Animation { get; init; }

    /// <summary>
    /// How the part's menu works when a player uses it with E (UI spec §12): whether E opens the menu or cycles a
    /// setting, the values its script shows there, live signals, a world preview and whether changes wait for
    /// Apply. Omitted opens a menu of <see cref="Settings"/> that applies each change at once. Placeable parts only.
    /// </summary>
    public MenuDefinition? Menu { get; init; }

    /// <summary>
    /// What the part shows on itself as its script drives it (UI spec §12): a running/idle icon, a glow, an
    /// extension bar, a turning highlight. Each follows a value the script sets with <c>part.display</c>.
    /// Placeable parts only.
    /// </summary>
    public DisplayDefinition? Display { get; init; }

    /// <summary>
    /// Makes the part a loose physics object rather than something built (rock chunks, resource rods): it always
    /// stands alone as a Creation of its own, is never anchored to the ground, and nothing is built onto it, welded to it
    /// or set into it. A loose part can be picked up into the inventory and set down again like any other part.
    /// </summary>
    public bool? Loose { get; init; }

    /// <summary>
    /// Loose parts this part falls apart into when its health runs out (a rock chunk into rods), each its own
    /// Creation thrown a little apart, instead of simply going. Placeable parts only.
    /// </summary>
    public World.BreakDefinition? BreaksInto { get; init; }

    /// <summary>
    /// Items holding Use (E) on the part with the Refine Tool turns it into, straight into the inventory, as
    /// with a tree (UI spec §9, §10): a Stone Rod into 20 Scrap Stone Blocks. Loose parts only.
    /// </summary>
    public List<Items.RecipeItem>? Refine { get; init; }

    /// <summary>
    /// Damage one Sledgehammer hit does to this part instead of the sledge's own part damage (a rock chunk
    /// breaks under fewer hits than a block). Placeable parts only.
    /// </summary>
    public float? SledgeDamage { get; init; }

    /// <summary>Free-form, part-specific data for the Lua script. Key order is kept as written.</summary>
    public JsonObject? Properties { get; init; }

    /// <summary>Keys this build does not know. Kept so a round-trip never loses data.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>
    /// False for a part that is only ever placed from another item (<see cref="PlacedFrom"/>): it has no stack of its
    /// own in an inventory, the hotbar or the Creative list.
    /// </summary>
    [JsonIgnore]
    public bool IsItem => PlacedFrom is null;

    /// <summary>True for categories that exist in the world on the build grid.</summary>
    [JsonIgnore]
    public bool IsPlaceable => Category is PartCategory.Block or PartCategory.Interactive or PartCategory.Container
        or PartCategory.Station or PartCategory.Light or PartCategory.Decor || Loose == true;
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

    /// <summary>
    /// True when a player may pick the part up while it holds items (a Gas Container full of fuel): what it holds goes
    /// into their inventory with it, and what does not fit drops where it stood. A Lift may carry a Creation whose only
    /// stored items are in such parts. Omitted means false: the part has to be emptied first.
    /// </summary>
    public bool? CarryContents { get; init; }

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

    /// <summary>
    /// Containers on the same Creation within this many metres of the station (surface to surface) are linked to it:
    /// crafting takes inputs from them and puts outputs in them. Omitted or 0 links nothing.
    /// </summary>
    public float? LinkRange { get; init; }

    /// <summary>
    /// The model's clips that follow the station's work, written to its <see cref="ClipReadout"/> readout (which its
    /// <c>display.clips</c> plays): <c>start</c> then <c>work</c> when it begins working, <c>complete</c> when it stops.
    /// </summary>
    public StationClipsDefinition? Clips { get; init; }

    /// <summary>The text readout a station with <see cref="Clips"/> names its clips in.</summary>
    public const string ClipReadout = "station";

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Clips a crafting station plays as it works (a Portable Fabricator's arm). Each is optional.</summary>
public sealed record StationClipsDefinition
{
    /// <summary>Played once when the station begins working, before <see cref="Work"/>.</summary>
    public string? Start { get; init; }

    /// <summary>Played while it works (name it <c>…_loop</c> so it repeats).</summary>
    public string? Work { get; init; }

    /// <summary>Played once when it stops: the queue is done or its output is full.</summary>
    public string? Complete { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// One upgrade level of a placed part. <see cref="Set"/> holds part keys to change, merged like a Part Editor batch's
/// <c>set</c> (objects key by key, other values replace) on top of the part and every level before it. Only
/// <c>name</c>, <c>description</c>, <c>durability</c>, <c>mesh</c>, <c>icon</c>, <c>sockets</c> (only their joints),
/// <c>settings</c>, <c>display</c>, <c>crafting</c>, <c>inventory</c>, <c>logic</c> and <c>properties</c> may change, and an
/// inventory never shrinks.
/// <code>
/// "upgrades": [
///   { "cost": [ { "item": "component_kit", "count": 2 } ], "set": { "crafting": { "speed": 1.25 }, "inventory": { "slots": 12 } } }
/// ]
/// </code>
/// </summary>
public sealed record UpgradeDefinition
{
    /// <summary>Part keys a level may change.</summary>
    public static readonly IReadOnlyList<string> Changeable =
        ["name", "description", "durability", "mesh", "icon", "sockets", "settings", "display", "crafting", "inventory", "logic", "properties"];

    /// <summary>Items the level takes from the player's inventory in Survival. Omitted means free.</summary>
    public List<Items.RecipeItem>? Cost { get; init; }

    /// <summary>Part keys the level changes.</summary>
    public JsonObject? Set { get; init; }

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

    /// <summary>
    /// Seconds full soil takes to dry out with nothing planted in it, as in Scrap Mechanic, where watered soil left
    /// empty dries in about a day. Water drains at <c>capacity / dry_time</c> per second. Omitted never dries.
    /// </summary>
    public float? DryTime { get; init; }

    /// <summary>
    /// Share of <see cref="Capacity"/> (0 to 1) at which the soil looks fully wet (<see cref="Survival.SoilWetness.Wet"/>).
    /// Omitted means 0.5.
    /// </summary>
    public float? WetLevel { get; init; }

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
    /// What the interact key (E) says on a placed part, such as "toggle" or "press"; it calls the script's
    /// <c>on_activate</c>. Omitted means players cannot use the part by hand.
    /// </summary>
    public string? Use { get; init; }

    /// <summary>
    /// True makes <see cref="Use"/> a hold (a button): the part stays used while the player holds the interact key, or
    /// the part's key on their seat's bar, and its script's <c>on_release(peer)</c> runs when they let go, stand up or
    /// leave. Needs <see cref="Use"/>. Omitted means a use is one press (a switch).
    /// </summary>
    public bool? Hold { get; init; }

    /// <summary>
    /// What kind of connection node the part shows with the Connect Tool (UI spec §12), which also names the kind of
    /// link that starts at it. Omitted means <see cref="PortKind.Logic"/>.
    /// </summary>
    public PortKind? Kind { get; init; }

    /// <summary>
    /// The kinds of part whose links may end at this one (an engine's power, a seat's logic). Omitted means any
    /// kind but <see cref="PortKind.Structural"/>.
    /// </summary>
    public IReadOnlyList<PortKind>? Accepts { get; init; }

    /// <summary>
    /// The id of one of the part's toggle settings that reverses which way it turns: right click on its node with
    /// the Connect Tool flips it, and a curved arrow by the node shows it. Scripts that drive the part read it as
    /// <c>joint.reversed</c>. Omitted means the part has no direction to reverse.
    /// </summary>
    public string? Reverse { get; init; }

    /// <summary>The node kind, <see cref="PortKind.Logic"/> when omitted.</summary>
    [JsonIgnore]
    public PortKind KindOrDefault => Kind ?? PortKind.Logic;

    /// <summary>True when a link from a part of kind <paramref name="source"/> may end at this part.</summary>
    public bool Takes(PortKind source) => Accepts?.Contains(source) ?? source != PortKind.Structural;

    /// <summary>Most links one side of a part may have.</summary>
    public const int MaxLinks = 64;

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>The kinds of connection node (UI spec §12): each has its own colour.</summary>
public enum PortKind
{
    /// <summary>On/off and value signals: switches, buttons, gates, timers, sensors, seats, lights (blue).</summary>
    Logic,

    /// <summary>Something that makes or uses power: engines, thrusters, rotors (cyan).</summary>
    Power,

    /// <summary>A joint something drives: bearings and pistons (white with a blue outline).</summary>
    Bearing,

    /// <summary>Structural or weld-related (light gray); its links go nowhere by default.</summary>
    Structural,
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

    /// <summary>Up to <see cref="SettingDefinition.MaxCount"/> numbers, each between min and max.</summary>
    NumberList,

    /// <summary>
    /// Up to <see cref="SettingDefinition.MaxCount"/> frames (a controller's sequence). Each frame lists joints the part
    /// is linked to, as <c>{"part": id, "target": value, "speed": value}</c>: a bearing's angle in degrees or a piston's
    /// extension in m, between min and max, and a speed above 0 and at most <see cref="SettingDefinition.MaxSpeed"/>.
    /// A frame that is a plain number sends every linked bearing to that angle.
    /// </summary>
    Sequence,
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

    /// <summary>Buttons in the menu that set a number to a named value (an engine's Low, Medium and High), in display order.</summary>
    public List<SettingPreset>? Presets { get; init; }

    /// <summary>
    /// A short heading shared by toggles shown side by side in one row (a sensor's "Detects": Players, Bots, …).
    /// Toggles next to each other with the same group share the row.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// True hides the setting while anything is linked into the part (an engine's On switch, when a seat drives it).
    /// </summary>
    public bool? HideWhenLinked { get; init; }

    /// <summary>A picture per option of a choice setting (<c>.png</c>, relative to the mod root), shown large when it is picked.</summary>
    public Dictionary<string, string>? Icons { get; init; }

    /// <summary>Highest speed of a sequence frame's joint (rad/s for bearings, m/s for pistons). Required for sequences.</summary>
    public float? MaxSpeed { get; init; }

    /// <summary>
    /// For a sequence: the part's <c>on_command</c> name that previews a sequence before it is applied. The menu sends
    /// the frames with <c>{"playing": true}</c> to start and <c>{"playing": false}</c> to stop. Omitted shows no preview.
    /// </summary>
    public string? Preview { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A named value a setting's preset button sets.</summary>
public sealed record SettingPreset
{
    /// <summary>Button label.</summary>
    public string? Name { get; init; }

    /// <summary>The value it sets.</summary>
    public float? Value { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>What Use (E) on a part does.</summary>
public enum MenuUse
{
    /// <summary>Opens the part's menu.</summary>
    Open,

    /// <summary>Steps <see cref="MenuDefinition.Cycle"/> to its next option and shows it large for a moment.</summary>
    Cycle,
}

/// <summary>How a <see cref="ReadoutDefinition"/> shows its value.</summary>
public enum ReadoutKind
{
    /// <summary>A cyan bar from 0 to <see cref="ReadoutDefinition.Max"/> (fuel, charge).</summary>
    Bar,

    /// <summary>A number with <see cref="ReadoutDefinition.Unit"/> (RPM, torque).</summary>
    Number,

    /// <summary>Text the script sets (a status line).</summary>
    Text,
}

/// <summary>The shape a part's <see cref="PreviewDefinition"/> draws in the world.</summary>
public enum PreviewShape
{
    /// <summary>A line along the axis.</summary>
    Ray,

    /// <summary>A cone along the axis, <see cref="PreviewDefinition.Angle"/> degrees from its middle to its edge.</summary>
    Cone,

    /// <summary>A sphere around the part.</summary>
    Sphere,
}

/// <summary>The menu a player opens on a part with E (UI spec §12). Behaviour stays in the part's script (D2).</summary>
public sealed record MenuDefinition
{
    /// <summary>What E does. Omitted means <see cref="MenuUse.Open"/>.</summary>
    public MenuUse? Use { get; init; }

    /// <summary>The choice setting E steps through when <see cref="Use"/> is <see cref="MenuUse.Cycle"/>.</summary>
    public string? Cycle { get; init; }

    /// <summary>Values the part's script shows in the menu with <c>part.display(id, value)</c>, in display order.</summary>
    public List<ReadoutDefinition>? Readouts { get; init; }

    /// <summary>True shows the part's inputs and its output live (a logic gate).</summary>
    public bool? Signals { get; init; }

    /// <summary>What the part reaches, drawn in the world while the menu is open (a sensor's range).</summary>
    public PreviewDefinition? Preview { get; init; }

    /// <summary>True keeps changes in the menu until the player presses Apply or closes it (a controller's sequence).</summary>
    public bool? Apply { get; init; }

    /// <summary>
    /// True locks the part's settings while a part with a sequence setting (a Controller) is linked into it: the menu
    /// shows only that the Controller runs the part, and the host refuses <c>ConfigurePart</c> (a piston).
    /// </summary>
    public bool? LockWhenSequenced { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>One value a part's script shows in its menu.</summary>
public sealed record ReadoutDefinition
{
    /// <summary>Id the script sets with <c>part.display</c> (lowercase snake_case).</summary>
    public string? Id { get; init; }

    /// <summary>Label. Omitted shows the id.</summary>
    public string? Name { get; init; }

    /// <summary>How it shows. Omitted means <see cref="ReadoutKind.Number"/>.</summary>
    public ReadoutKind? Kind { get; init; }

    /// <summary>A full bar's value. Omitted means 1.</summary>
    public float? Max { get; init; }

    /// <summary>Unit after a number (<c>rpm</c>, <c>N·m</c>).</summary>
    public string? Unit { get; init; }

    /// <summary>Digits after the point. Omitted means 0.</summary>
    public int? Decimals { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>What a part reaches, drawn in the world while its menu is open.</summary>
public sealed record PreviewDefinition
{
    /// <summary>The shape. Omitted means <see cref="PreviewShape.Ray"/>.</summary>
    public PreviewShape? Shape { get; init; }

    /// <summary>The number setting holding its reach in m.</summary>
    public string? Range { get; init; }

    /// <summary>The direction it looks, in the part's frame. Omitted means +Y.</summary>
    public Vec3? Axis { get; init; }

    /// <summary>A cone's half angle in degrees (more than 0, under 90).</summary>
    public float? Angle { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>What a part shows on itself as its script drives it (UI spec §12).</summary>
public sealed record DisplayDefinition
{
    /// <summary>
    /// A readout drawn as an icon above the part while it has links and the Connect Tool is put away: green while the
    /// value is on (not 0), gray while it is off (an engine running or idle).
    /// </summary>
    public string? Status { get; init; }

    /// <summary>A faint blue glow whose strength follows a readout from 0 to 1 (a thruster's or rotor's power).</summary>
    public GlowDefinition? Glow { get; init; }

    /// <summary>A thin cyan bar showing a readout from 0 to 1 while the Connect Tool is out (a piston's extension).</summary>
    public BarDefinition? Bar { get; init; }

    /// <summary>A highlight that turns about an axis while a readout is not 0 (a powered bearing).</summary>
    public HighlightDefinition? Highlight { get; init; }

    /// <summary>The model pushed in while a readout is not 0 (a switch that is on, a button held down).</summary>
    public PressDefinition? Press { get; init; }

    /// <summary>The model's own glTF clips, played by a text readout and scrubbed by number readouts (an engine starting).</summary>
    public ClipsDefinition? Clips { get; init; }

    /// <summary>Particle effects whose strength follows a readout from 0 to 1 (exhaust smoke, sparks, oil drips).</summary>
    public IReadOnlyList<EffectDefinition>? Effects { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Which of a part model's glTF clips play: the model is shown as its whole glTF scene, with its animations.</summary>
public sealed record ClipsDefinition
{
    /// <summary>Most clips a part may scrub.</summary>
    public const int MaxScrubs = 4;

    /// <summary>
    /// The text readout naming the clip to play. A new value starts that clip from the beginning; anything after a
    /// <c>#</c> is ignored, so <c>gear#2</c> after <c>gear#1</c> plays <c>gear</c> again. <c>start&gt;run_loop</c> plays
    /// <c>start</c> and then <c>run_loop</c>. A clip whose name ends in <c>_loop</c> repeats; any other holds its last
    /// frame (or goes on to <see cref="Idle"/>).
    /// </summary>
    public string? Readout { get; init; }

    /// <summary>Clips held at a point set by a number readout instead of played (a lever's position).</summary>
    public IReadOnlyList<ScrubDefinition>? Scrub { get; init; }

    /// <summary>
    /// The clip played while the readout has no value, and after a played clip that does not loop has finished (a
    /// station's idle glow). Omitted holds the model's own pose, or a finished clip's last frame.
    /// </summary>
    public string? Idle { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A clip held at the point a readout sets.</summary>
public sealed record ScrubDefinition
{
    /// <summary>The number readout: 0 is the clip's start, 1 its end.</summary>
    public string? Readout { get; init; }

    /// <summary>The clip's name in the model. It should move nodes the played clips leave alone.</summary>
    public string? Clip { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>What a particle effect looks like.</summary>
public enum EffectKind
{
    /// <summary>Soft puffs that rise and spread (exhaust).</summary>
    Smoke,

    /// <summary>Small bright sparks thrown out that fall.</summary>
    Sparks,

    /// <summary>Drops that fall (oil, water).</summary>
    Drops,
}

/// <summary>A particle effect on a part.</summary>
public sealed record EffectDefinition
{
    /// <summary>Most effects one part may have.</summary>
    public const int MaxPerPart = 8;

    /// <summary>The readout (0 to 1) that sets its strength; 0 stops it.</summary>
    public string? Readout { get; init; }

    /// <summary>What it looks like.</summary>
    public EffectKind? Kind { get; init; }

    /// <summary>Where it comes from, relative to the part centre in m in the part's frame. Omitted means the centre.</summary>
    public Vec3? Offset { get; init; }

    /// <summary>Which way it leaves, in the part's frame. Omitted means +Y.</summary>
    public Vec3? Direction { get; init; }

    /// <summary>Its colour as <c>"#rrggbb"</c>. Omitted means the kind's own (dark smoke, yellow sparks, black drops).</summary>
    public string? Color { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A glow on a part.</summary>
public sealed record GlowDefinition
{
    /// <summary>The readout (0 to 1) that sets its strength.</summary>
    public string? Readout { get; init; }

    /// <summary>Where it comes from, relative to the part centre in m in the part's frame. Omitted means the centre.</summary>
    public Vec3? Offset { get; init; }

    /// <summary>Which way it streams, in the part's frame. Omitted means +Y.</summary>
    public Vec3? Direction { get; init; }

    /// <summary>How far it reaches at full strength, in m. Omitted means 0.5.</summary>
    public float? Length { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A bar along a part.</summary>
public sealed record BarDefinition
{
    /// <summary>The readout (0 to 1) it fills to.</summary>
    public string? Readout { get; init; }

    /// <summary>Where the empty end is, relative to the part centre in m in the part's frame. Omitted means the centre.</summary>
    public Vec3? Offset { get; init; }

    /// <summary>The direction it fills, in the part's frame. Omitted means +Y.</summary>
    public Vec3? Axis { get; init; }

    /// <summary>A full bar's length in m (more than 0).</summary>
    public float? Length { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A turning highlight on a part.</summary>
public sealed record HighlightDefinition
{
    /// <summary>The readout that turns it on while it is not 0.</summary>
    public string? Readout { get; init; }

    /// <summary>The axis it turns about, in the part's frame. Omitted means +Y.</summary>
    public Vec3? Axis { get; init; }

    /// <summary>Its centre, relative to the part centre in m in the part's frame. Omitted means the centre.</summary>
    public Vec3? Offset { get; init; }

    /// <summary>Its radius in m (more than 0).</summary>
    public float? Radius { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A part's model pushed in, as a button is pressed.</summary>
public sealed record PressDefinition
{
    /// <summary>The readout that pushes it in while it is not 0.</summary>
    public string? Readout { get; init; }

    /// <summary>The direction it moves when pushed, in the part's frame. Omitted means -Y.</summary>
    public Vec3? Axis { get; init; }

    /// <summary>How far it moves, in m (more than 0, at most 0.1). Omitted means 0.02.</summary>
    public float? Depth { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>How a held item aims with right click (UI spec §10). What aiming changes about a shot is up to its script.</summary>
public sealed record AimDefinition
{
    /// <summary>Default <see cref="Zoom"/>.</summary>
    public const float DefaultZoom = 0.85f;

    /// <summary>What the field of view is multiplied by while aiming: under 1 zooms in. Default <see cref="DefaultZoom"/>.</summary>
    public float? Zoom { get; init; }

    /// <summary>True fires when right click is let go, as well as on left click while aiming.</summary>
    public bool? FireOnRelease { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// The model a held item shows in the player's right hand (<c>held</c>). Lengths are metres and angles degrees in the
/// hand's frame: origin in the palm, +X along the fingers' width (thumb side), +Y back up the arm, -Z out of the palm.
/// </summary>
public sealed record HeldDefinition
{
    /// <summary>Furthest <see cref="Offset"/> may move the model from the palm, per axis in m.</summary>
    public const float MaxOffset = 0.5f;

    /// <summary>Largest <see cref="Scale"/>.</summary>
    public const float MaxScale = 4f;

    /// <summary>The model, a mod path like <c>meshes/items/torch.glb</c>. Omitted means the item's own <c>mesh</c>.</summary>
    public string? Mesh { get; init; }

    /// <summary>Where the model's origin sits from the palm, in m. Default (0, 0, 0).</summary>
    public Vec3? Offset { get; init; }

    /// <summary>The model's rotation in the hand, in degrees about X, Y then Z. Default (0, 0, 0).</summary>
    public Vec3? Rotation { get; init; }

    /// <summary>Scale of the model (more than 0, at most <see cref="MaxScale"/>). Default 1.</summary>
    public float? Scale { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>A seat: a player sits on the part, and its script reads their steering (<c>part.occupant_input()</c>).</summary>
public sealed record SeatDefinition
{
    /// <summary>Most a seat's <see cref="Bounce"/> may be.</summary>
    public const float MaxBounce = 4f;

    /// <summary>
    /// Where the sitting player's feet go, relative to the part centre in metres in the part's frame. Omitted means the
    /// centre. With a <see cref="Cushion"/> only its height counts: the floor the feet rest on.
    /// </summary>
    public Vec3? Offset { get; init; }

    /// <summary>
    /// The point on the seat's cushion the sitter's bottom rests on (under the hips), relative to the part centre in
    /// metres in the part's frame. The player sits there and its legs fit the cushion's height above the
    /// <see cref="Offset"/> floor (or the part's bottom): a chair, or a low seat with the legs out in front. Omitted
    /// means the player stands its hips 0.53 m above the offset.
    /// </summary>
    public Vec3? Cushion { get; init; }

    /// <summary>What the sitter's hands do. Omitted means <see cref="SeatHands.Wheel"/> on a seat with logic outputs (one that
    /// drives), else <see cref="SeatHands.Free"/>.</summary>
    public SeatHands? Hands { get; init; }

    /// <summary>
    /// How much the sitter bounces and sways with the ride, 0 (not at all) to <see cref="MaxBounce"/>; 1 is a padded seat.
    /// The bumps come from how the seat itself moves, so a smooth road barely shows. Omitted means 1.
    /// </summary>
    public float? Bounce { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>What a seated player's hands do (<see cref="SeatDefinition.Hands"/>).</summary>
public enum SeatHands
{
    /// <summary>Hold a wheel or controls in front of the chest.</summary>
    Wheel,

    /// <summary>Rest on the thighs, as a passenger.</summary>
    Free,
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

/// <summary>How a part's model turns when its script animates it (<see cref="PartDefinition.Animation"/>).</summary>
public sealed record AnimationDefinition
{
    /// <summary>The axis the model turns about, in the part's frame (right-handed). Omitted means +Y.</summary>
    public Vec3? Axis { get; init; }

    /// <summary>A point on the axis relative to the part centre, in m in the part's frame. Omitted means the centre.</summary>
    public Vec3? Pivot { get; init; }

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

    /// <summary>
    /// Glow map (<c>.png</c>), relative to the mod root or <c>other_mod:path</c>: the glow colour shines only where it is
    /// bright, so a light can glow in its strips and not its frame. Needs <see cref="Emission"/>.
    /// </summary>
    public string? EmissionTexture { get; init; }

    /// <summary>
    /// Grid cells one repeat of <see cref="Texture"/> covers, 1 to 16. Omitted means 1: each cell face shows the whole
    /// texture. Above 1 the texture is laid over the creation's grid instead, so neighbouring parts carry on one larger
    /// pattern (a wall of blocks reads as one surface, with no seam at each block).
    /// </summary>
    public int? TextureSpan { get; init; }

    /// <summary>The largest <see cref="TextureSpan"/>.</summary>
    public const int MaxTextureSpan = 16;

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
