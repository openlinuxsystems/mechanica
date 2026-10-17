namespace Mechanica.Scripting;

/// <summary>One function, hook or global of the Lua API.</summary>
/// <param name="Name">What a script writes: <c>part.apply_force</c>, <c>on_update</c>, <c>state</c>.</param>
/// <param name="Signature">How it is called, e.g. <c>part.apply_force(force, at?)</c>.</param>
/// <param name="Summary">One line on what it does.</param>
/// <param name="Group">Section: Hooks, Globals, part, creation, world or vec3.</param>
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
        new("on_activate", "function on_activate(peer)", "A player used the part (wired by seats and buttons, chunk 10).", "Hooks"),
        new("on_power", "function on_power(amount)", "The part's power or logic input changed (signal bus, chunk 10).", "Hooks"),
        new("on_collision", "function on_collision(other, impulse)", "The part's Body began touching something; other has creation and part when it is a Creation.", "Hooks"),
        new("get_outputs", "function get_outputs()", "Returns the part's signal outputs (signal bus, chunk 10).", "Hooks"),

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
        new("part.joint", "part.joint()", "This part's own joint as {type, other, position, min, max, locked}, or nil.", "part"),

        new("creation.id", "creation.id()", "The id of the script's Creation.", "creation"),
        new("creation.mass", "creation.mass()", "Total mass of the Creation, in kg.", "creation"),
        new("creation.parts", "creation.parts()", "List of the Creation's parts as {id, type}.", "creation"),
        new("creation.set_bearing", "creation.set_bearing(part_id, velocity, max_torque?)", "Drives another bearing of the same Creation; returns true or false, reason.", "creation"),
        new("creation.set_bearing_angle", "creation.set_bearing_angle(part_id, angle, speed?, max_torque?)", "Turns another bearing of the same Creation to an angle; returns true or false, reason.", "creation"),
        new("creation.set_piston", "creation.set_piston(part_id, length, speed?, max_force?)", "Moves another piston of the same Creation to a length, or frees it with nil; returns true or false, reason.", "creation"),
        new("creation.joint", "creation.joint(part_id)", "The joint another part of the Creation owns, like part.joint(), or nil.", "creation"),

        new("world.gravity", "world.gravity()", "Gravity acceleration, in m/s².", "world"),
        new("world.time", "world.time()", "Seconds simulated since the scripts started.", "world"),
        new("world.tick", "world.tick()", "Physics steps since the scripts started.", "world"),
        new("world.raycast", "world.raycast(from, to)", "First thing on the segment, skipping the script's own Creation: {position, normal, distance, creation?, part?} or nil.", "world"),
        new("world.overlap_sphere", "world.overlap_sphere(centre, radius)", "Everything a sphere touches, skipping the script's own Creation: a list of {creation?, part?}.", "world"),

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
