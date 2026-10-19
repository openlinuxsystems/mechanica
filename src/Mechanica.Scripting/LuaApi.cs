namespace Mechanica.Scripting;

/// <summary>One function, hook or global of the Lua API.</summary>
/// <param name="Name">What a script writes: <c>part.apply_force</c>, <c>on_update</c>, <c>state</c>.</param>
/// <param name="Signature">How it is called, e.g. <c>part.apply_force(force, at?)</c>.</param>
/// <param name="Summary">One line on what it does.</param>
/// <param name="Group">Section: Hooks, Globals, part, creation, world, sound, soil, player, item, holder, inventory, combat, farm, fire or vec3.</param>
public sealed record LuaApiEntry(string Name, string Signature, string Summary, string Group);

/// <summary>
/// The Lua API in one list, for the editor's API panel and completion. The full reference is
/// <c>docs/lua-api.md</c>; a test keeps the two in step.
/// </summary>
public static class LuaApi
{
    /// <summary>Every entry, in reference order.</summary>
    public static IReadOnlyList<LuaApiEntry> Entries { get; } =
    [
        new("on_create", "function on_create()", "Runs once when the part's script first starts.", "Hooks"),
        new("on_update", "function on_update(dt)", "Runs every physics step (dt seconds) while the Creation is simulated.", "Hooks"),
        new("on_reload", "function on_reload()", "Runs after a hot reload instead of on_create; state is kept.", "Hooks"),
        new("on_destroy", "function on_destroy()", "Runs when the part is removed or its Creation goes away.", "Hooks"),
        new("on_activate", "function on_activate(peer)", "A player used the part (ActivatePart: E on a button, a switch, ripe soil); the return value is the action's result.", "Hooks"),
        new("can_activate", "function can_activate(peer)", "Return nil to let the player use the part, or the reason ActivatePart is refused. May not change the world.", "Hooks"),
        new("on_command", "function on_command(name, args, peer)", "The player sent the part a command from its menu (CommandPart), such as \"preview\" from a sequence's Play button; args is the command's table.", "Hooks"),
        new("on_power", "function on_power(amount)", "The sum of the part's logic inputs changed; amount is the new sum.", "Hooks"),
        new("on_collision", "function on_collision(other, impulse)", "The part's Body began touching something; other has creation and part when it is a Creation.", "Hooks"),
        new("get_outputs", "function get_outputs()", "Called once per step; returns the part's logic output (a number or boolean), like part.set_output.", "Hooks"),
        new("on_enter", "function on_enter(peer)", "A player sat down in the part (a seat).", "Hooks"),
        new("on_leave", "function on_leave(peer)", "The player sitting in the part stood up.", "Hooks"),
        new("on_equip", "function on_equip()", "Item scripts: a player took the item in hand.", "Hooks"),
        new("on_unequip", "function on_unequip()", "Item scripts: the item left the player's hand (another item or tool, or death).", "Hooks"),
        new("can_use", "function can_use(target)", "Item scripts: return nil to allow a use, or the reason it is refused. May not change the world.", "Hooks"),
        new("on_use", "function on_use(target)", "Item scripts: the player used the item (target has from and direction, plus creation and part when aimed at a part in reach); the return value is the action's result.", "Hooks"),
        new("on_hit", "function on_hit(landing)", "Item scripts: a projectile the item launched landed: {id, position, hit?, data}.", "Hooks"),
        new("can_reload_item", "function can_reload_item()", "Item scripts: return nil to allow a reload, or the reason it is refused. May not change the world.", "Hooks"),
        new("on_reload_item", "function on_reload_item()", "Item scripts: the player pressed Reload (R) with the item in hand (ReloadItem). An item without it cannot be reloaded.", "Hooks"),

        new("state", "state", "Table that survives hot reloads. Keep plain data in it.", "Globals"),
        new("properties", "properties", "The part's free-form properties from its JSON.", "Globals"),
        new("log", "log(...)", "Writes a line to the editor console and the game log (print does the same).", "Globals"),

        new("part.id", "part.id()", "The part's id within its Creation.", "part"),
        new("part.type", "part.type()", "The part's qualified type id, e.g. vanilla:thruster.", "part"),
        new("part.creation", "part.creation()", "The id of the part's Creation.", "part"),
        new("part.position", "part.position()", "World position of the part's centre, in m.", "part"),
        new("part.rotation", "part.rotation()", "World rotation of the part as a quaternion {x, y, z, w}.", "part"),
        new("part.velocity", "part.velocity()", "World velocity of the part's centre, in m/s.", "part"),
        new("part.angular_velocity", "part.angular_velocity()", "Spin of the part's Body, in rad/s.", "part"),
        new("part.to_world", "part.to_world(direction)", "Turns a direction in the part's frame into world space.", "part"),
        new("part.to_local", "part.to_local(direction)", "Turns a world direction into the part's frame.", "part"),
        new("part.point_to_world", "part.point_to_world(offset)", "World position of a point given relative to the part's centre, in the part's frame.", "part"),
        new("part.mass", "part.mass()", "The part's own mass from its JSON, in kg.", "part"),
        new("part.body_mass", "part.body_mass()", "Mass of the rigid Body the part belongs to, in kg.", "part"),
        new("part.socket", "part.socket(id)", "World position and outward normal of one of the part's sockets.", "part"),
        new("part.apply_force", "part.apply_force(force, at?)", "Pushes the part's Body with a world force (N), through the centre of mass or at a world point; returns true or false, reason.", "part"),
        new("part.apply_torque", "part.apply_torque(torque)", "Twists the part's Body with a world torque (N·m); returns true or false, reason.", "part"),
        new("part.set_bearing", "part.set_bearing(velocity, max_torque?)", "Drives this bearing's motor at velocity rad/s, or frees it with nil; returns true or false, reason.", "part"),
        new("part.set_bearing_angle", "part.set_bearing_angle(angle, speed?, max_torque?)", "Turns this bearing to angle rad (from where it was built) at up to speed rad/s and holds it; returns true or false, reason.", "part"),
        new("part.set_piston", "part.set_piston(length, speed?, max_force?)", "Moves this piston to length m at up to speed m/s and holds it, or frees it with nil; returns true or false, reason.", "part"),
        new("part.joint", "part.joint()", "This part's own joint as {type, other, position, min, max, locked, axis, reversed}, or nil; axis is a world vec3, and reversed is true when the player reversed the part with the Connect Tool.", "part"),
        new("part.set_output", "part.set_output(value)", "Sets the part's output on the logic bus (a number, or a boolean as 1 or 0); linked parts get it next step.", "part"),
        new("part.output", "part.output()", "The part's own output on the logic bus.", "part"),
        new("part.input", "part.input()", "The sum of the outputs of every part linked to this one.", "part"),
        new("part.inputs", "part.inputs()", "Each part linked to this one as {part, value, type, storage}, by part id; storage is true for a container (a gas container linked to an engine).", "part"),
        new("part.links", "part.links()", "Ids of the parts this part's output is linked to.", "part"),
        new("part.setting", "part.setting(id)", "The value of one of the part's settings (number, string, boolean or list), or nil.", "part"),
        new("part.display", "part.display(id, value)", "Shows a value on the part: a readout in its menu or what its display follows (a number, a boolean as 1 or 0, a string, or nil to clear); returns true or false, reason.", "part"),
        new("part.displayed", "part.displayed(id)", "The value the part shows under id (a number or a string), or nil.", "part"),
        new("part.sound", "part.sound(key)", "The sound id the part binds to key in its sounds, for sound.play or sound.start; nil when unbound.", "part"),
        new("part.occupant", "part.occupant()", "The peer of the player sitting in this seat, or nil.", "part"),
        new("part.occupant_input", "part.occupant_input()", "The seated player's input as {x, z, jump, sprint, yaw, pitch} (x right, z forward, -1 to 1), or nil.", "part"),

        new("creation.id", "creation.id()", "The id of the script's Creation.", "creation"),
        new("creation.mass", "creation.mass()", "Total mass of the Creation, in kg.", "creation"),
        new("creation.parts", "creation.parts()", "List of the Creation's parts as {id, type}.", "creation"),
        new("creation.set_bearing", "creation.set_bearing(part_id, velocity, max_torque?)", "Drives another bearing of the same Creation; returns true or false, reason.", "creation"),
        new("creation.set_bearing_angle", "creation.set_bearing_angle(part_id, angle, speed?, max_torque?)", "Turns another bearing of the same Creation to an angle; returns true or false, reason.", "creation"),
        new("creation.set_piston", "creation.set_piston(part_id, length, speed?, max_force?)", "Moves another piston of the same Creation to a length, or frees it with nil; returns true or false, reason.", "creation"),
        new("creation.joint", "creation.joint(part_id)", "The joint another part of the Creation owns, like part.joint(), or nil.", "creation"),
        new("creation.display", "creation.display(part_id, id, value)", "Like part.display(), for a part this one's output is linked to (an engine turning its wheel bearings' highlight).", "creation"),
        new("creation.displayed", "creation.displayed(part_id, id)", "Like part.displayed(), for any part of the same Creation (a seat checking whether an engine is running), or nil.", "creation"),
        new("creation.occupant_input", "creation.occupant_input(part_id)", "Like part.occupant_input(), for another seat of the same Creation (the pilot of a seat linked to this part), or nil.", "creation"),
        new("creation.count", "creation.count(item, parts?)", "How many of an item the Creation's containers hold (fuel in gas containers, charge in batteries), or only those in the list of part ids parts.", "creation"),
        new("creation.space", "creation.space(item, parts?)", "How many more of an item fit in the Creation's containers, or only in those in the list of part ids parts.", "creation"),
        new("creation.take", "creation.take(item, count?, parts?)", "Takes up to count (default 1) of an item from the Creation's containers, or only from those in the list of part ids parts; returns how many it took.", "creation"),

        new("world.gravity", "world.gravity()", "Gravity acceleration, in m/s².", "world"),
        new("world.time", "world.time()", "Seconds the world has been simulated; carries on across saves.", "world"),
        new("world.tick", "world.tick()", "Physics steps since the scripts started.", "world"),
        new("world.raycast", "world.raycast(from, to)", "First thing on the segment, skipping the script's own Creation: {position, normal, distance, creation?, part?, player?, bot?} or nil; player is the peer and bot the bot id.", "world"),
        new("world.creative", "world.creative()", "True in Creative, where engines need no fuel.", "world"),
        new("world.overlap_sphere", "world.overlap_sphere(centre, radius)", "Everything a sphere touches, skipping the script's own Creation: a list of {creation?, part?, player?, bot?}.", "world"),
        new("world.item", "world.item(id, key?)", "An item's or part's data as in its JSON (or just its key section, e.g. \"seed\"), or nil for an unknown id.", "world"),

        new("sound.play", "sound.play(id, at?)", "Plays a one-shot sound event at the part (or a world point) for everyone in earshot; returns true or false, reason.", "sound"),
        new("sound.start", "sound.start(id, parameters?)", "Starts a looping sound event that follows the part; returns its handle, or nil, reason.", "sound"),
        new("sound.set_parameter", "sound.set_parameter(handle, name, value)", "Bends one of the part's loops (pitch, volume or the event's own parameters); returns true or false, reason.", "sound"),
        new("sound.stop", "sound.stop(handle)", "Stops one of the part's loops; returns true or false, reason.", "sound"),

        new("soil.get", "soil.get()", "This soil part's stored state: {seed?, grown, water, stage, stages, updated?}.", "soil"),
        new("soil.set", "soil.set(values)", "Stores any of grown, water, stage and updated ({grown = 12, stage = 1}); the soil script decides growth.", "soil"),
        new("soil.clear", "soil.clear()", "Takes the crop out of this soil, leaving its water.", "soil"),

        new("player.can_give", "player.can_give(peer, items)", "True when every {item, count} in items fits in the player's inventory, or false and why.", "player"),
        new("player.give", "player.give(peer, items)", "Puts every {item, count} in the player's inventory, or nothing when they don't all fit; true, or false and why.", "player"),

        new("item.id", "item.id()", "The held item's qualified id, e.g. vanilla:potato_blaster.", "item"),
        new("item.name", "item.name(id?)", "The held item's display name, or another item's.", "item"),
        new("item.slot", "item.slot()", "The inventory slot the item is used from.", "item"),
        new("item.count", "item.count()", "How many of the item that slot holds.", "item"),
        new("item.consume", "item.consume(count?)", "Uses up count (default 1) of the item from its slot; true, or false when there aren't enough.", "item"),
        new("item.replace", "item.replace(id)", "Turns one of the item into another item, in the same slot when it was the last one (a bucket filled or emptied); true, or false when there is no room.", "item"),
        new("item.counter", "item.counter({value?, max?, ammo?, busy?})", "What the holder's hotbar slot and HUD counter show for the item: value (rounds loaded) out of max, the ammo item carried, and busy seconds (a reload).", "item"),
        new("item.sound", "item.sound(key)", "The sound id the item binds to key in its sounds (fire, reload), for sound.play; nil when unbound.", "item"),

        new("holder.peer", "holder.peer()", "The peer of the player holding the item.", "holder"),
        new("holder.eye", "holder.eye()", "World position of the holder's eyes, in m.", "holder"),
        new("holder.feet", "holder.feet()", "World position of the holder's feet, in m.", "holder"),
        new("holder.look", "holder.look()", "Unit direction the holder looks in.", "holder"),
        new("holder.health", "holder.health()", "The holder's health.", "holder"),
        new("holder.max_health", "holder.max_health()", "The most health a player can have.", "holder"),
        new("holder.heal", "holder.heal(amount)", "Gives the holder health, up to the maximum; returns their health.", "holder"),
        new("holder.aiming", "holder.aiming()", "True while the holder holds right click to aim the item (AimItem).", "holder"),

        new("inventory.count", "inventory.count(item)", "How many of an item the holder carries (bare ids are in the item's mod).", "inventory"),
        new("inventory.take", "inventory.take(item, count?)", "Takes count (default 1) of an item from the holder; true, or false when they carry too few.", "inventory"),

        new("combat.cast", "combat.cast(from, direction, range)", "The first thing a ray hits, skipping the holder: {kind, position, distance, player?, creation?, part?, object?, bot?, type?} or nil; type is a part's qualified type or a world object's kind (birch_tree).", "combat"),
        new("combat.hit", "combat.hit(hit, damage, push?, kind?)", "Deals damage to what a cast hit, pushing a part's Body with push N·s; kind (drill, saw, weapon...) says what world objects take it (a rock outcrop takes only drill). Returns the hit with damage dealt.", "combat"),
        new("combat.hitscan", "combat.hitscan(from, direction, range, damage, impulse?)", "A cast plus a hit along the ray; returns the hit with damage dealt, or nil.", "combat"),
        new("combat.splash", "combat.splash(at, radius, damage, impulse?)", "Damage falling off to 0 at radius m around at, for players, parts and world objects; returns a list of hits.", "combat"),
        new("combat.pellets", "combat.pellets(direction, count, spread)", "count directions: the first along direction, the rest evenly round a cone of spread degrees.", "combat"),
        new("combat.projectile", "combat.projectile(from, velocity, options?)", "Launches a projectile ({gravity?, lifetime?, data?, look?}; look \"water\" draws thrown water); on_hit runs where it lands. Returns its id.", "combat"),

        new("farm.soil", "farm.soil(creation, part)", "A soil part in the holder's reach: {seed?, crop?, stage, ripe, water, capacity}, or nil and why.", "farm"),
        new("farm.plant", "farm.plant(creation, part, seed?)", "Plants seed (default: this item) in empty soil in reach; returns the soil, or nil and why. Takes no items.", "farm"),
        new("farm.water", "farm.water(creation, part, seconds)", "Adds seconds of water to soil in reach, up to its capacity; returns the soil, or nil and why. Takes no items.", "farm"),
        new("farm.near", "farm.near(position, radius)", "Soil parts within radius m of position, nearest first: a list of {creation, part}.", "farm"),

        new("fire.near", "fire.near(position, radius)", "Fires burning within radius m of position, nearest first: a list of {id, position, strength, creation?, part?}.", "fire"),
        new("fire.douse", "fire.douse(position, radius, amount)", "Water: every fire within radius m loses amount strength (out at 0); returns the fires it reached, or nil and why.", "fire"),
        new("fire.ignite", "fire.ignite(creation, part, strength?)", "Sets a flammable part alight; returns true, or false and why.", "fire"),
        new("fire.strength", "fire.strength(creation, part)", "How strongly a part burns, 0 (not on fire) to 1.", "fire"),

        new("vec3", "vec3(x, y, z)", "A vector with x, y, z. Supports + - * / unary minus, == and tostring.", "vec3"),
        new("vec3.dot", "a:dot(b)", "Dot product.", "vec3"),
        new("vec3.cross", "a:cross(b)", "Cross product.", "vec3"),
        new("vec3.length", "a:length()", "Length.", "vec3"),
        new("vec3.length_squared", "a:length_squared()", "Squared length (cheaper).", "vec3"),
        new("vec3.normalized", "a:normalized()", "Unit vector in the same direction (zero stays zero).", "vec3"),
        new("vec3.lerp", "a:lerp(b, t)", "Linear blend from a to b.", "vec3"),
        new("vec3.is", "vec3.is(value)", "True when value is a vec3.", "vec3"),
    ];

    /// <summary>Lua keywords, for syntax highlighting.</summary>
    public static IReadOnlyList<string> Keywords { get; } =
    [
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
    ];

    /// <summary>Standard library names scripts may use, for syntax highlighting.</summary>
    public static IReadOnlyList<string> Libraries { get; } =
    [
        "math", "string", "table", "coroutine", "bit32", "os", "json", "print", "pairs", "ipairs", "next",
        "select", "tostring", "tonumber", "type", "assert", "error", "pcall", "xpcall", "setmetatable",
        "getmetatable", "rawget", "rawset", "rawequal", "rawlen", "unpack",
    ];
}
