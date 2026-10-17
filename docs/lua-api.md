# Lua API Reference

Every part's behaviour is a Lua script (decision D2), and so is every held item's (weapons, food;
see [Item scripts](#item-scripts)). A part names its script in its JSON
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
`reload_poll_seconds`, `max_string_length`, `max_ray_length`, `max_overlap_radius`,
`max_messages`, `max_loops_per_part`, `max_projectiles` and `max_projectile_lifetime`. The motion
limits (largest force, torque, bearing speed and piston force) are in
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
| `on_activate(peer)` | A player used the part: they pressed F on it (the `ActivatePart` action). Buttons and switches use it. |
| `on_power(amount)` | The sum of the part's logic inputs changed; `amount` is the new sum. It also runs when the script (re)starts with a non-zero input. See the [Logic and Power Tutorial](logic-power-tutorial.md). |
| `on_collision(other, impulse)` | The part's Body began touching something. `other` is a table with `creation` and `part` when it is a Creation, empty for the ground; `impulse` is in N·s. |
| `get_outputs()` | Called once per step after `on_update`; what it returns (a number, or a boolean as 1 or 0) is the part's logic output, as if it called `part.set_output`. |
| `on_enter(peer)` | A player sat down in the part (a part with `seat`). |
| `on_leave(peer)` | The player sitting in the part stood up (or the seat is going away). |

Held items have their own hooks (see [Item scripts](#item-scripts)):

| Hook | When |
|------|------|
| `on_equip()` | The player took the item in hand. |
| `on_unequip()` | The item left the player's hand: they took another item or tool, or died. |
| `can_use(target)` | Before every use. Return `nil` to allow it, or a string: the reason the host refuses the use. It may read but not change the world. |
| `on_use(target)` | The player used the item. `target` is `{from, direction}`: their eyes and where they aim. What it returns (plain data) is the `UseItem` action's result. |
| `on_hit(landing)` | A projectile the item launched landed: `{id, position, hit?, data}`. `hit` is missing when its lifetime ran out. |

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
| `part.set_bearing_angle(angle, speed?, max_torque?)` | For a bearing: turns it to `angle` rad and holds it there (steering, doors, arms). The angle is measured from where the bearing was built, right-handed about its axis, within ±π (and within the joint's `min`/`max` when it has limits). It moves at up to `speed` rad/s (default `default_bearing_speed`) and slows down as it arrives; a free bearing takes the short way round. Returns `true`, or `false` and the reason. |
| `part.set_piston(length, speed?, max_force?)` | For a piston: moves it to `length` m of extension (between its joint's `min` and `max`) at up to `speed` m/s with at most `max_force` N (defaults from `motion.json`), and holds it there against loads. `nil` frees the piston so it slides between its stops. Returns `true`, or `false` and the reason. |
| `part.joint()` | The joint this part owns (a bearing's rotor, a piston's head, a suspension's mount) as `{type, other, position, min, max, locked}`, or `nil`. `type` is `rotational`, `linear` or `spring`; `other` is the part on the far side; `position` is where it is now (rad or m from where it was built); `min` and `max` are missing on a free-spinning bearing; `axis` is the world direction (a `vec3`) it turns about or slides along. |
| `part.set_output(value)` | Sets the part's output on the logic bus: a number, or a boolean (1 or 0). It stays until changed, and parts linked to this one see it next step. |
| `part.output()` | The part's own output on the logic bus (0 until set). |
| `part.input()` | The sum of the outputs of every part linked to this one. |
| `part.inputs()` | Each part linked to this one as `{part, value}`, by part id (a logic gate reads them one by one). |
| `part.links()` | The ids of the parts this part's output is linked to (an engine finds its bearings here). |
| `part.setting(id)` | The value of one of the part's settings: a number, a choice (string), a toggle (boolean) or a number list (table); the default until a player changes it. `nil` for an unknown id. |
| `part.sound(key)` | The sound id the part binds to `key` in its `sounds` (for `sound.play` and `sound.start`), or `nil`. Scripts play the part's own bindings this way, so the ids stay in data. |
| `part.occupant()` | For a seat: the peer of the player sitting in it, or `nil`. |
| `part.occupant_input()` | For a seat: the sitting player's input as `{x, z, jump, sprint, yaw, pitch}`. `x` is steering (-1 left to 1 right) and `z` throttle (-1 back to 1 forward). `nil` when the seat is empty. |

Pushes last one physics step, so a script that keeps pushing calls `apply_force` every
`on_update`. They go through the `ApplyForce` action with the part's id, so pushes from different
parts of one Body add up. Bearings go through `SetBearingTarget` and pistons through `SetPistonTarget`.
Joint targets stay until changed, so set them once (in `on_create` or when an input changes), not
every step. See the [Socket and Joint Guide](socket-joint-guide.md) for how joints are defined.

## `creation`

The Creation the part belongs to.

| Function | Returns |
|----------|---------|
| `creation.id()` | The Creation's id. |
| `creation.mass()` | Total mass of the Creation, in kg. |
| `creation.parts()` | A list of the Creation's parts, each `{id, type}`. |
| `creation.set_bearing(part_id, velocity, max_torque?)` | Like `part.set_bearing`, for another bearing of the same Creation (an engine driving wheels). Returns `true`, or `false` and the reason. |
| `creation.set_bearing_angle(part_id, angle, speed?, max_torque?)` | Like `part.set_bearing_angle`, for another bearing of the same Creation (a seat steering the front wheels). |
| `creation.set_piston(part_id, length, speed?, max_force?)` | Like `part.set_piston`, for another piston of the same Creation (a controller moving several). |
| `creation.joint(part_id)` | Like `part.joint()`, for another part of the same Creation. |
| `creation.occupant_input(part_id)` | Like `part.occupant_input()`, for another seat of the same Creation: a control surface or rotor linked from a seat reads its pilot's controls this way. `nil` when that seat is empty. |
| `creation.count(item)` | How many of an item the Creation's containers hold (gasoline in gas containers, battery packs in batteries). Bare ids are in the part's mod. |
| `creation.take(item, count?)` | Takes up to `count` (default 1) of an item from the Creation's containers, in part order, and returns how many it took. Engines burn fuel this way. |

## `world`

| Function | Returns |
|----------|---------|
| `world.gravity()` | Gravity acceleration, in m/s² (a `vec3`). |
| `world.time()` | Seconds simulated since the scripts started. |
| `world.tick()` | Physics steps since the scripts started. |
| `world.creative()` | `true` in Creative, where engines run without fuel. |
| `world.raycast(from, to)` | The first thing on the segment from `from` to `to`, skipping the script's own Creation: `{position, normal, distance, creation?, part?}`, or `nil`. A ray may be at most `max_ray_length` m long. |
| `world.overlap_sphere(centre, radius)` | Everything a sphere touches, skipping the script's own Creation: a list of `{creation?, part?}` (both missing for the ground). `radius` is at most `max_overlap_radius` m. |

## `sound`

Sound events come from the mods' sound banks (`sounds/*.json`, edited with the Part Editor's Sound
Event Binder). An id without a mod (`"rotor_loop"`) is one of the part's own mod; another mod's is
written `vanilla:thruster_loop`. Sounds are played by the host and heard by every player in earshot.
A loop's parameters are the ones its event declares, each with its own range (`thruster_loop` has
`thrust`, from 0 to 1).

| Function | Returns |
|----------|---------|
| `sound.play(id, at?)` | Plays a one-shot at the part's centre (an item script: its holder's eyes), or at the world point `at`. Returns `true`, or `false` and the reason. |
| `sound.start(id, parameters?)` | Starts a looping event that follows the part, with optional starting parameter values (`{thrust = 0.5}`). Returns its handle, or `nil` and the reason. A part runs at most `max_loops_per_part` loops. |
| `sound.set_parameter(handle, name, value)` | Sets a parameter of one of the part's loops, which bends its pitch and volume as the event says. Changes reach clients up to 20 times a second. Returns `true`, or `false` and the reason. |
| `sound.stop(handle)` | Stops one of the part's loops. Returns `true`, or `false` and the reason. |

A part's loops keep playing across hot reloads, so keep their handles in `state`. They stop when
the part is removed or its script stops on an error. Item scripts play one-shots only.

```lua
function on_create()
  sound.play("vanilla:thruster_ignite")
  state.hum = sound.start("vanilla:thruster_loop", {thrust = 0})
end

function on_update(dt)
  -- Louder and higher the faster the part goes, full at 20 m/s.
  local speed = part.velocity():length()
  if state.hum then sound.set_parameter(state.hum, "thrust", math.min(speed / 20, 1)) end
end
```

## Item scripts

Items (anything not placeable: food, weapons, seeds, the water bucket) can name a `script` too
(chunk 20b, D2). The item's behaviour lives there, and its numbers in its `properties`. The vanilla
ones are `food.lua`, `potato_gun.lua`, `seed.lua` and `watering.lua` in `mods/vanilla/scripts/`.

- **One instance per player and item type.** Each player gets their own copy of the script, with its
  own `state`, the first time they take one in hand or use one. It is dropped when they leave.
- **Uses go through the Action API.** The Item tool (and bots or agents) call
  `UseItem {slot, direction?, creation?, part?}`. The host checks the slot holds a scripted item, that
  `direction` is close to where the player looks (`aim_tolerance` in `survival.json`) and that the
  part exists, then runs `can_use` and, if that allows it, `on_use`. Both get `target`:
  `{from, direction, creation?, part?}`, where `creation` and `part` are what the Item tool's crosshair
  points at within reach. `EquipItem {slot?}` says what is in hand. A dead player's uses are refused.
- **`can_use` and the script's setup only read.** Functions that change the world (`item.consume`,
  `holder.heal`, `inventory.take`, `farm.plant`, `farm.water` and the `combat` ones that deal damage
  or launch) raise an error there.
- **Damage follows the world's rules.** In Creative nothing hurts players; with `pvp` off a shot
  can't hurt another player, but a player's own splash still hurts them.
- `part` and `creation` functions don't work in an item script; `world` and `log` do. The `item`,
  `holder`, `inventory`, `combat` and `farm` libraries only work in an item script.

```lua
-- Food: eating one heals properties.health.
function can_use(target)
  if holder.health() >= holder.max_health() then
    return "You are already at full health."
  end
end

function on_use(target)
  item.consume(1)
  return { health = holder.heal(properties.health) }
end
```

### `item`

| Function | Returns |
|----------|---------|
| `item.id()` | The item's qualified id, e.g. `vanilla:potato_blaster`. |
| `item.name(id?)` | The item's display name, or another item's (bare ids are in the item's own mod). |
| `item.slot()` | The inventory slot the item is used from. |
| `item.count()` | How many of the item that slot holds. |
| `item.consume(count?)` | Uses up `count` (default 1) of the item from its slot. `true`, or `false` when the slot holds fewer. |

### `holder`

| Function | Returns |
|----------|---------|
| `holder.peer()` | The peer of the player holding the item. |
| `holder.eye()` | World position of their eyes, in m. |
| `holder.feet()` | World position of their feet, in m. |
| `holder.look()` | Unit direction they look in. |
| `holder.health()` | Their health. |
| `holder.max_health()` | The most health a player can have (`max_health` in `survival.json`). |
| `holder.heal(amount)` | Gives them up to `amount` health, never past the maximum, and returns their health. |

### `inventory`

The holder's inventory. Item ids may be bare (`potato`, in the item's own mod) or `mod_id:item`.

| Function | Returns |
|----------|---------|
| `inventory.count(item)` | How many of `item` they carry. |
| `inventory.take(item, count?)` | Takes `count` (default 1). `true`, or `false` (and nothing taken) when they carry fewer. |

### `combat`

Hits are tables `{kind, position, player?, creation?, part?, object?, bot?}`; `kind` is `player`, `part`,
`object` (a tree, rock or bush), `bot` or `ground`. What damage did comes back with `damage` added. These
are the same primitives bots use (chunk 21).

| Function | Returns |
|----------|---------|
| `combat.cast(from, direction, range)` | The first thing a ray hits within `range` m, skipping the holder, with its `distance`; or `nil`. `range` is at most `max_ray_length`. |
| `combat.hit(hit, damage, push?)` | Deals `damage` to what a cast hit. A part's Body also gets `push` (a `vec3` impulse, N·s) unless the part broke off. Returns the hit with `damage` dealt. |
| `combat.hitscan(from, direction, range, damage, impulse?)` | `combat.cast` then `combat.hit`, pushing along the ray with `impulse` N·s. The hit, or `nil` when nothing is in range. |
| `combat.splash(at, radius, damage, impulse?)` | Hurts every player, part and world object within `radius` m of `at`, falling off from `damage` at the centre to 0 at the edge, and pushes parts away with up to `impulse` N·s. A list of hits. `radius` is at most `max_overlap_radius`. |
| `combat.pellets(direction, count, spread)` | `count` (at most 64) unit directions: the first along `direction`, the rest evenly round a cone of `spread` degrees. No randomness, so every replay agrees. |
| `combat.projectile(from, velocity, options?)` | Launches a projectile from `from` at `velocity` m/s. `options`: `gravity` (a `vec3`, default `world.gravity()`), `lifetime` (s, default and at most `max_projectile_lifetime`), `data` (passed to `on_hit`). It flies on the host, every client draws it along its arc, and `on_hit` runs where it lands; it deals no damage by itself. Returns its id, or `false` and the reason when `max_projectiles` are already in flight. |

### `farm`

Soil parts (chunk 20). A soil table is `{seed?, crop?, stage, ripe, water, capacity}`: the planted
seed and its crop (qualified ids), the growth stage (0 to 4, 4 is `ripe`) and the seconds of water it
holds of its `capacity`. Each function checks the part is soil within the holder's `reach`, and
returns `nil` and the reason when it can't act. Crops grow, and are harvested, by the host (`Harvest`);
the seed's own `seed` section sets what grows and how fast.

| Function | Returns |
|----------|---------|
| `farm.soil(creation, part)` | The soil table. |
| `farm.plant(creation, part, seed?)` | Plants `seed` (an item with a `seed` section; default this item) in empty soil and returns the soil table. It takes nothing from the inventory: use `item.consume`. |
| `farm.water(creation, part, seconds)` | Adds `seconds` of water, up to `capacity`, and returns the soil table. Takes nothing from the inventory either. |

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
