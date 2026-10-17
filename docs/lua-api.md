# Lua API Reference

Every part's behaviour is a Lua script (decision D2). A part names its script in its JSON
(`"script": "scripts/thruster.lua"`, relative to the mod root), and every placed part of that type
runs its own copy. Scripts run only on the host, which simulates the world; clients see what the
scripts do through state sync. Part-specific numbers such as thrust or lift belong in the part's
`properties`, never in the script.

Mechanica runs Lua 5.2 through MoonSharp. Units are SI everywhere: m, s, kg, N, N·m, rad.

## A first script

```lua
-- A hover thruster: holds its Creation at properties.height.
function on_create()
  state.target = properties.height
end

function on_update(dt)
  local p, v = part.position(), part.velocity()
  local accel = -world.gravity().y + properties.stiffness * (state.target - p.y) - properties.damping * v.y
  part.apply_force(vec3(0, creation.mass() * accel, 0))
end
```

## How scripts run

- **Each part has its own globals.** Two thrusters running the same script don't share variables.
  Scripts of one mod share the standard libraries; different mods never share anything.
- **Hooks** are global functions the host calls (below). A script defines only the ones it needs.
  The top level of the file runs once when the part starts and again on every hot reload, so put
  one-time setup in `on_create`.
- **`state`** is a table that survives hot reloads. Everything else in the script starts fresh when
  it reloads. Keep plain data in `state` (numbers, strings, booleans and tables of them): that is
  what saves will store.
- **Errors stop the part.** A Lua error, or a hook that runs more than `instruction_budget` VM
  instructions (an endless loop), stops that part's script and reports the error with its file and
  line. The rest of the world keeps running. Fix the script and it starts again on the next reload.
- **Actions can be refused.** Functions that change the world go through the same validated Action
  API as players, bots and external agents, and return `true`, or `false` and the reason. The first
  time each reason comes up it is also logged as a warning.
- **Sandbox.** There is no `io`, no `os` beyond the clock (`os.time`, `os.clock`, `os.date`), no
  `load`, `loadstring`, `dofile` or `require`, and `string.rep` can't build huge strings. A script can
  only move its own Creation.
- **Hooks may not yield.** `coroutine.wrap` and `coroutine.create` work inside a hook, but
  `coroutine.yield` straight out of a hook is an error.

Limits live in `game/data/scripting.json`: `instruction_budget` (per hook call),
`reload_poll_seconds`, `max_string_length`, `max_ray_length`, `max_overlap_radius` and
`max_messages`. The motion limits (largest force, torque and bearing speed) are in
`game/data/motion.json`.

## Hot reload

Save the script and the host picks the change up within `reload_poll_seconds`. Every part running
the script gets a fresh environment with its old `state`, runs the file's top level and then
`on_reload` (or `on_create` if it never started). A part that was stopped by an error starts again.
If the new version doesn't compile, the error is reported and the old version keeps running.

## In the Part Editor

The Part Editor's **Script** tab edits the open part's script with highlighting. **Save & reload**
checks the script the way the host compiles it and marks the line of a compile error. If the Test
Sandbox runs the script, the save reloads it there at once. The **API** list holds every entry of
this reference (double-click inserts one), and the **Console** shows the sandbox's script output.

### Templates

**New from template** starts from one of the files in `game/data/lua_templates/`. `{{name}}` in a
template becomes the part's name.

| Template | What it does |
|----------|--------------|
| empty | Every hook, empty. |
| thruster | Pushes along the part's +Y with `properties.thrust` N. |
| bearing_motor | Drives a bearing at `properties.speed` rad/s with at most `properties.torque` N·m. |
| wing | Lift and drag from the air speed across the part, from its `properties`. |
| distance_sensor | Raycasts along the part's +Y up to `properties.range` m and keeps the distance in `state`. |

## Hooks

| Hook | When |
|------|------|
| `on_create()` | Once, when the part's script first starts. |
| `on_update(dt)` | Every physics step (`dt` seconds, 1/60 by default) while the Creation is simulated. A parked Creation (no player near) is not updated. |
| `on_reload()` | After a hot reload, instead of `on_create`. `state` is kept. |
| `on_destroy()` | When the part is removed or its Creation goes away. Part functions return `nil` here once the part is gone. |
| `on_activate(peer)` | A player used the part. Seats and buttons wire this up in chunk 10. |
| `on_power(amount)` | The part's power or logic input changed. The signal bus wires this up in chunk 10. |
| `on_collision(other, impulse)` | The part's Body began touching something. `other` is a table with `creation` and `part` when it is a Creation, empty for the ground; `impulse` is in N·s. |
| `get_outputs()` | Returns the part's signal outputs, for the signal bus (chunk 10). |

## Globals

| Name | What it is |
|------|------------|
| `state` | Table that survives hot reloads. |
| `properties` | The part's `properties` object from its JSON, as a table. |
| `log(...)` | Writes a line to the editor console and the game log. `print` does the same. |

## `part`

The part whose script is running. Positions and directions are in world space unless the name says
otherwise. "The part's frame" is the part as authored in the Part Editor, turned the way it was
placed.

| Function | Returns |
|----------|---------|
| `part.id()` | The part's id within its Creation. |
| `part.type()` | The part's qualified type id, e.g. `vanilla:thruster`. |
| `part.creation()` | The id of the part's Creation. |
| `part.position()` | World position of the part's centre, in m (a `vec3`). |
| `part.rotation()` | World rotation of the part as a quaternion `{x, y, z, w}`. |
| `part.velocity()` | World velocity of the part's centre, in m/s: the Body's velocity plus its spin. |
| `part.angular_velocity()` | Spin of the part's Body, in rad/s. |
| `part.to_world(direction)` | A direction in the part's frame turned into world space. |
| `part.to_local(direction)` | A world direction turned into the part's frame. |
| `part.point_to_world(offset)` | World position of a point given relative to the part's centre, in the part's frame. |
| `part.mass()` | The part's own mass from its JSON, in kg. |
| `part.body_mass()` | Mass of the rigid Body the part belongs to, in kg. |
| `part.socket(id)` | World position and outward normal of one of the part's sockets (two `vec3`s), or `nil`. |
| `part.apply_force(force, at?)` | Pushes the part's Body with a world force in N for this step. Without `at` it pushes through the centre of mass; with a world point `at` (for example `part.position()`) it also twists the Body. Calls in one step add up. Returns `true`, or `false` and the reason. |
| `part.apply_torque(torque)` | Twists the part's Body with a world torque in N·m for this step. Adds up with the part's other pushes. Returns `true`, or `false` and the reason. |
| `part.set_bearing(velocity, max_torque?)` | For a bearing: drives its motor at `velocity` rad/s about the rotor normal, with at most `max_torque` N·m (default from `motion.json`). `nil` frees the bearing. The target stays until changed. Returns `true`, or `false` and the reason. |

Pushes last one physics step, so a script that keeps pushing calls `apply_force` every
`on_update`. They go through the `ApplyForce` action with the part's id, so pushes from different
parts of one Body add up. Bearings go through `SetBearingTarget`.

## `creation`

The Creation the part belongs to.

| Function | Returns |
|----------|---------|
| `creation.id()` | The Creation's id. |
| `creation.mass()` | Total mass of the Creation, in kg. |
| `creation.parts()` | A list of the Creation's parts, each `{id, type}`. |
| `creation.set_bearing(part_id, velocity, max_torque?)` | Like `part.set_bearing`, for another bearing of the same Creation (an engine driving wheels). Returns `true`, or `false` and the reason. |

## `world`

| Function | Returns |
|----------|---------|
| `world.gravity()` | Gravity acceleration, in m/s² (a `vec3`). |
| `world.time()` | Seconds simulated since the scripts started. |
| `world.tick()` | Physics steps since the scripts started. |
| `world.raycast(from, to)` | The first thing on the segment from `from` to `to`, skipping the script's own Creation: `{position, normal, distance, creation?, part?}`, or `nil`. A ray may be at most `max_ray_length` m long. |
| `world.overlap_sphere(centre, radius)` | Everything a sphere touches, skipping the script's own Creation: a list of `{creation?, part?}` (both missing for the ground). `radius` is at most `max_overlap_radius` m. |

## `vec3`

Vectors are tables with `x`, `y` and `z`. Functions that take a vector also accept `{1, 2, 3}`.

| Function | Returns |
|----------|---------|
| `vec3(x, y, z)` | A new vector. `+`, `-`, unary `-`, `*` (by a number, or component-wise), `/` (by a number), `==` and `tostring` work on vectors. |
| `a:dot(b)` | Dot product. |
| `a:cross(b)` | Cross product. |
| `a:length()` | Length. |
| `a:length_squared()` | Squared length; cheaper than `length`. |
| `a:normalized()` | A unit vector in the same direction; a zero vector stays zero. |
| `a:lerp(b, t)` | Linear blend from `a` to `b`. |
| `vec3.is(value)` | True when `value` is a `vec3`. |

## Performance

Every `vec3` operation makes a new table, and MoonSharp is an interpreter, so arithmetic on plain
numbers is several times cheaper than on vectors. In a hot `on_update`, read the components once
(`local vx, vy, vz = v.x, v.y, v.z`) and do the maths on numbers. `tools/lua-bench.sh` runs the D2
benchmark: 200 scripted wings on one Creation, with and without Godot.
