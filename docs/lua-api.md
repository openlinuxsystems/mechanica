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
  `load`, `loadstring` or `dofile` (`require` loads only this mod's scripts and its dependencies'), and
  `string.rep` can't build huge strings. A script can
  only move its own Creation. `json.parse(text)` turns JSON into a table and `json.serialize(table)`
  turns a table of plain data into JSON text. [Standard libraries](#standard-libraries) lists everything
  that is there.
- **Hooks may not yield.** `coroutine.wrap` and `coroutine.create` work inside a hook, but
  `coroutine.yield` straight out of a hook is an error.

Limits live in `game/data/scripting.json`, which ships with the build (mods can't change it). These are
the values it ships with:

| Key | Default | What it limits |
|-----|---------|----------------|
| `instruction_budget` | 1,000,000 | Lua VM instructions one hook call may run before the script is stopped (a guard against endless loops) |
| `reload_poll_seconds` | 0.5 | Seconds between checks of the script files for changes (hot reload) |
| `max_string_length` | 1,000,000 | Longest string `string.rep` may build, in characters |
| `max_ray_length` | 500 | Longest ray `world.raycast` and `combat.cast` may cast, in m |
| `max_overlap_radius` | 25 | Largest radius `world.overlap_sphere`, `combat.splash` and `farm.near` may test, in m |
| `max_messages` | 500 | Prints, warnings and errors the host keeps for the editor console |
| `max_loops_per_part` | 4 | Sound loops one part may run at once (`sound.start`) |
| `max_projectiles` | 128 | Projectiles scripts may have in flight at once (`combat.projectile`) |
| `max_projectile_lifetime` | 10 | Longest a projectile may fly, in s (and how long it flies when the script doesn't say) |
| `max_queued_events` | 1024 | Events waiting for `on_event` between steps; past it the oldest are dropped |
| `max_timers_per_script` | 64 | Timers one script may run at once (`timer.after`, `timer.every`) |

The motion limits (largest force, torque, bearing speed and piston force) are in
`game/data/motion.json`.

### Standard libraries

Scripts get MoonSharp's soft sandbox (`CoreModules.Preset_SoftSandbox`) plus Mechanica's own libraries
(below). Everything else in standard Lua 5.2 is missing.

| Library | What is there |
|---------|---------------|
| Basic functions | `assert`, `error`, `print` (to the console), `select`, `tonumber`, `tostring`, `type`, `unpack`, `pack`, `collectgarbage`, `_G`, `_VERSION` |
| Iteration | `pairs`, `ipairs`, `next` |
| Metatables | `setmetatable`, `getmetatable`, `rawget`, `rawset`, `rawequal`, `rawlen` |
| Errors | `pcall`, `xpcall` |
| `string` | The Lua string functions (`byte`, `char`, `find`, `format`, `gmatch`, `gsub`, `len`, `lower`, `match`, `rep` (bounded by `max_string_length`), `reverse`, `sub`, `upper`), plus MoonSharp's `contains`, `startsWith`, `endsWith` and `unicode` |
| `table` | `concat`, `insert`, `pack`, `remove`, `sort`, `unpack` |
| `math` | All of standard `math`, including `random` and `randomseed` |
| `bit32` | All of Lua 5.2's `bit32` (`band`, `bor`, `bxor`, `bnot`, `lshift`, `rshift`, `arshift`, `lrotate`, `rrotate`, `btest`, `extract`, `replace`) |
| `coroutine` | `create`, `resume`, `running`, `status`, `wrap`, `yield` (but a hook itself may not yield) |
| `os` | Only the clock: `clock`, `date`, `difftime`, `time` |
| `json` | `json.parse(text)`, `json.serialize(table)`, `json.null` and `json.isnull(value)` |
| `require` | Mechanica's own: loads a script of this mod or of a mod it depends on ([Sharing code with `require`](#sharing-code-with-require)) |

Not there: `io`, the rest of `os`, `load`, `loadstring`, `loadfile`, `dofile`, `debug` and `package.loadlib`
(`package` holds only `loaded`).

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
| thruster | Pushes toward its base (along the part's -Y) with `properties.thrust` N, like `vanilla:thruster`. |
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
| `on_activate(peer)` | A player used the part: they pressed E on it (the `ActivatePart` action). Buttons, switches and soil (harvest) use it. What it returns (plain data) is the action's result. |
| `on_release(peer)` | A player let go of a part they held (`logic.hold`, the button): they let go of E or the part's key on their seat's bar, stood up, died or left (the `ReleasePart` action, or the host for them). Every `on_activate` of a held part is followed by one `on_release` for the same peer. |
| `can_activate(peer)` | Before every `on_activate`. Return `nil` to allow it, or a string: the reason `ActivatePart` is refused. Like an item's `can_use` it only reads: `soil.set`, `soil.clear` and `player.give` raise an error there. |
| `on_command(name, args, peer)` | A player sent the part a command from its menu (the `CommandPart` action). The only commands are the `preview` names of the part's `sequence` settings: a controller's Play button sends `{playing, frames}`, with `frames` checked like the setting before the script sees it. `peer` is who sent it. A part without this hook takes no commands. |
| `on_power(amount)` | The sum of the part's logic inputs changed; `amount` is the new sum. It also runs when the script (re)starts with a non-zero input. See the [Logic and Power Tutorial](logic-power-tutorial.md). |
| `on_collision(other, impulse)` | The part's Body began touching something. `other` is a table with `creation` and `part` when it is a Creation, empty for the ground; `impulse` is in N·s. |
| `on_damage(amount, kind, broken, at)` | The part took `amount` health of damage. `kind` is what dealt it: `weapon`, `explosion`, `sledge`, `drill`, `saw`, `bot` or `fire`. `broken` is true when the blow broke the part off; it is then gone (`part.*` reads give `nil`, `part.sound` still answers) and its script is destroyed straight after. `at` is the part's middle when it was hit. Damage is heard once no other hook is running, in the order it came, so a blast that sets off another part (an Explosive Canister next to one) is heard in the same step. A part on a piece a blow broke off its creation still hears it, from its script on the new creation (which starts afresh, as after any split). Placed parts only; needs combat on the host. |
| `get_outputs()` | Called once per step after `on_update`; what it returns (a number, or a boolean as 1 or 0) is the part's logic output, as if it called `part.set_output`. |
| `on_enter(peer)` | A player sat down in the part (a part with `seat`). |
| `on_leave(peer)` | The player sitting in the part stood up (or the seat is going away). |
| `on_event(topic, data)` | An event the script listens to happened (`events.listen`, see [Mod scripts](#mod-scripts)). `data` is the event's table. Mod scripts have it too. |

Held items have their own hooks (see [Item scripts](#item-scripts)):

| Hook | When |
|------|------|
| `on_equip()` | The player took the item in hand. |
| `on_unequip()` | The item left the player's hand: they took another item or tool, or died. |
| `on_update(dt)` | Every step while a living player holds the item, after the parts' `on_update`. It may change the world (drain a charge with `item.counter`, heal with `holder.heal`). |
| `on_aim(on)` | The player started (`true`) or stopped (`false`) aiming with right click (`AimItem`): wind up a bow, raise a shield. |
| `can_use(target)` | Before every use. Return `nil` to allow it, or a string: the reason the host refuses the use. It may read but not change the world. |
| `on_use(target)` | The player used the item. `target` is `{from, direction, aiming, creation?, part?}`: their eyes, where they aim, whether they hold right click to aim, and the Creation and part in reach under the crosshair. What it returns (plain data) is the `UseItem` action's result. |
| `on_hit(landing)` | A projectile the script launched landed: `{id, position, hit?, data}`. `hit` is missing when its lifetime ran out. Part and mod scripts have it too. |
| `can_reload_item()` | Before every reload. Return `nil` to allow it, or a string: the reason the host refuses it. It may read but not change the world. |
| `on_reload_item()` | The player pressed Reload (R) with the item in hand (`ReloadItem`). An item that does not define it cannot be reloaded. |

## Globals

| Name | What it is |
|------|------------|
| `state` | Table that survives hot reloads. |
| `properties` | The part's `properties` object from its JSON, as a table. |
| `log(...)` | Writes a line to the editor console and the game log. `print` does the same. |
| `require(path)` | Runs another Lua file of this mod (`scripts/lib/util.lua`) or of a mod it depends on (`other_mod:scripts/util.lua`) once and returns what it returns, so scripts can share code. See [Sharing code](#sharing-code-with-require). |

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
| `part.joint()` | The joint this part owns (a bearing's rotor, a piston's head, a suspension's mount) as `{type, other, position, min, max, locked, axis, reversed}`, or `nil`. `type` is `rotational`, `linear` or `spring`; `other` is the part on the far side; `position` is where it is now (rad or m from where it was built); `min` and `max` are missing on a free-spinning bearing; `axis` is the world direction (a `vec3`) it turns about or slides along; `reversed` is true when the player reversed the part with the Connect Tool (its `logic.reverse` setting), so a script driving it should turn it the other way. |
| `part.set_output(value)` | Sets the part's output on the logic bus: a number, or a boolean (1 or 0). It stays until changed, and parts linked to this one see it next step. |
| `part.output()` | The part's own output on the logic bus (0 until set). |
| `part.input()` | The sum of the outputs of every part linked to this one. |
| `part.inputs()` | Each part linked to this one as `{part, value, type, storage, seat, occupied}`, by part id (a logic gate reads them one by one). `type` is the linked part's qualified id; `storage` is true when it is a container, such as a gas container linked to an engine to feed it; `seat` is true for a seat (nil otherwise) and `occupied` while someone sits in it, so an engine runs only while its driver's seat is taken. |
| `part.links()` | The ids of the parts this part's output is linked to (an engine finds its bearings here). |
| `part.setting(id)` | The value of one of the part's settings: a number, a choice (string), a toggle (boolean), a number list (table) or a sequence (a table of frames, each a number or a list of `{part, target, speed}`); the default until a player changes it. `nil` for an unknown id. |
| `part.sound(key)` | The sound id the part binds to `key` in its `sounds` (for `sound.play` and `sound.start`), or `nil`. Scripts play the part's own bindings this way, so the ids stay in data. |
| `part.destroy(kind?)` | Breaks the part off at once, as enough damage of `kind` (`explosion` unless given) would: it spills what it holds, the pieces it held together split and it hears `on_damage` with `broken` true. True when it broke; `false` when it is already gone or cannot break (no `durability`). Needs combat on the host. |
| `part.occupant()` | For a seat: the peer of the player sitting in it, or `nil`. |
| `part.occupant_input()` | For a seat: the sitting player's input as `{x, z, jump, sprint, yaw, pitch}`. `x` is steering (-1 left to 1 right) and `z` throttle (-1 back to 1 forward). `nil` when the seat is empty. |
| `part.set_light(level)` | For a part with a `light` section: sets how bright it shines, 0 (dark) to 1 (its full `energy`), or `true`/`false` for 1/0. It stays until changed; the host sends it to every client. Returns `true`, or `false` and the reason. |
| `part.light()` | The part's light level now, 0 to 1. |
| `part.crafting()` | A station's queue (a part with `crafting`): `{working, blocked, crafts, finished}`. `working` is true while it crafts, `blocked` while a finished craft waits for room, `crafts` is how many crafts are queued and `finished` counts every craft it has finished since it was loaded, so a script sees one complete when it goes up. `nil` for other parts. |
| `part.animate(angle?, speed?)` | For a part with an `animation` section: turns its model (not its collision or Body) about the section's `axis` through its `pivot`, to `angle` rad (`nil` keeps the angle it has), then keeps turning it at `speed` rad/s (default 0). Every client draws it from the host's state, so send it when it changes, not every step. Returns `true`, or `false` and the reason. |
| `part.animation()` | The model's turn now as `angle, speed` (rad, rad/s). |
| `part.display(id, value)` | Shows a value on the part under `id` (lowercase snake_case): a number, a boolean (1 or 0), a string (cut to 64 characters) or `nil` to clear it. Its menu's `readouts` and its `display` section (status icon, bar, glow, highlight, press) read these; see the [Part Data Schema](part-data-schema.md#menu-and-display). A part shows at most 16 values. Setting the same value again sends nothing, so it is fine to call every step. The host sends them to every client. Returns `true`, or `false` and the reason. |
| `part.displayed(id)` | The value the part shows under `id` (a number or a string), or `nil`. |

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
| `creation.display(part_id, id, value)` | Like `part.display`, for a part this one's output is linked to (an engine turns its wheel bearings' `drive` highlight). Any other part is refused. |
| `creation.displayed(part_id, id)` | Like `part.displayed`, for any part of the same Creation (a seat checks whether an engine shows `running`), or `nil`. |
| `creation.occupant_input(part_id)` | Like `part.occupant_input()`, for another seat of the same Creation: a control surface or rotor linked from a seat reads its pilot's controls this way. `nil` when that seat is empty. |
| `creation.count(item, parts?)` | How many of an item the Creation's containers hold (gasoline in gas containers, batteries in battery containers). With `parts`, a list of part ids, only those containers count (an engine's linked gas containers, or a container's own `{part.id()}`). Bare ids are in the part's mod. |
| `creation.space(item, parts?)` | How many more of an item fit in the Creation's containers, or only in those in the list of part ids `parts` (a gas container works out how full it is from this and `creation.count`). |
| `creation.take(item, count?, parts?)` | Takes up to `count` (default 1) of an item from the Creation's containers, in part order, and returns how many it took; with `parts`, only from the containers in that list of part ids. Engines burn fuel this way. |

## `world`

| Function | Returns |
|----------|---------|
| `world.gravity()` | Gravity acceleration, in m/s² (a `vec3`). |
| `world.time()` | Seconds the world has been simulated. Saves keep it, so the clock carries on when a world loads and times kept in `state` or on soil stay meaningful. |
| `world.time_of_day()` | The in-game time in hours, from 0 (midnight) up to 24. A world starts at 6 and one `day_length` (the world's generation rules, 1440 s by default) of `world.time()` is a whole day, so an Alarm Clock set for 7:30 rings once a day. |
| `world.tick()` | Physics steps since the scripts started. |
| `world.creative()` | `true` in Creative, where engines run without fuel. |
| `world.raycast(from, to)` | The first thing on the segment from `from` to `to`, skipping the script's own Creation: `{position, normal, distance, creation?, part?, player?, bot?}`, or `nil`. `player` is the peer of a player it hit and `bot` the id of a bot; all four are missing for the ground and world objects. A ray may be at most `max_ray_length` m long. |
| `world.overlap_sphere(centre, radius)` | Everything a sphere touches, skipping the script's own Creation: a list of `{creation?, part?, player?, bot?}` (all missing for the ground). `radius` is at most `max_overlap_radius` m. |
| `world.item(id, key?)` | An item's or part's data as written in its JSON (snake_case keys), or only its `key` section (`world.item(seed, "seed")` is `{crop, grow_time, yield, seeds, seeds_max, value, plant}`), or `nil` for an unknown id. Bare ids are in the script's mod. Works in part and item scripts. |

## `soil`

The soil part the script runs on (chunk 20). The host only stores what the script decides; the vanilla
`soil.lua` grows crops from the world clock and harvests them in `on_activate`. A soil function on a part
without a `soil` section raises an error.

| Function | Returns |
|----------|---------|
| `soil.get()` | `{seed?, grown, water, stage, stages, updated?, soaked}`: the planted seed (qualified id), seconds of growth done, seconds of water left, the growth stage (0 to `stages`, which is 4; `stages` is ripe), the `world.time()` growth was last worked out at (missing until the script first sets it), and whether the soil has been fully wet since it was last dry (it then looks drying rather than lightly watered). `nil` once the part is gone. |
| `soil.set(values)` | Stores any of `grown`, `water`, `stage` and `updated` (`soil.set{grown = 12, stage = 1, updated = world.time()}`); what is left out stays. `grown` and `stage` only apply with something planted; water is kept to the soil's `capacity`. A new stage or the soil running dry is sent to clients. |
| `soil.clear()` | Takes the crop out (a harvest), leaving the water. |

A parked Creation runs no `on_update`, so soil works growth out from the time since `updated` whenever it
runs; the crop it gets is the same as growing step by step.

## `player`

Any player's inventory, from part, item and mod scripts. Items are lists of `{item, count}` (`count` default 1;
bare ids are in the script's mod).

| Function | Returns |
|----------|---------|
| `player.can_give(peer, items)` | `true` when every item fits in the player's inventory at once, or `false` and why. |
| `player.give(peer, items)` | Puts every item in the player's inventory, or nothing when they don't all fit: `true`, or `false` and why. |

## `sound`

Sound events come from the mods' sound banks (`sounds/*.json`, edited with the Part Editor's Sound
Event Binder). An id without a mod (`"rotor_loop"`) is one of the part's own mod; another mod's is
written `vanilla:thruster_loop`. Sounds are played by the host and heard by every player in earshot.
A loop's parameters are the ones its event declares, each with its own range (`thruster_loop` has
`thrust`, from 0 to 1).

| Function | Returns |
|----------|---------|
| `sound.play(id, at?, options?)` | Plays a one-shot at the part's centre (an item script: its holder's eyes), or at the world point `at` (pass `nil` to keep the default place and still give options). `options.pitch` scales its pitch for every listener: 1 as recorded, 2 an octave up, 0.5 an octave down, kept between 0.25 and 4 (`sound.play("vanilla:horn_honk", nil, {pitch = 1.5})`). Returns `true`, or `false` and the reason. |
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
ones are `food.lua`, `potato_gun.lua`, `seed.lua`, `bucket.lua` (the empty water bucket) and `watering.lua` (the full
one) in `mods/vanilla/scripts/`.

- **One instance per player and item type.** Each player gets their own copy of the script, with its
  own `state`, the first time they take one in hand or use one. Their `state` is saved with the world under their
  player and comes back when they return (before the script's setup runs), so a lantern's fuel or a gun's kill count
  carries on. Keep it plain data, as for part scripts.
- **Uses go through the Action API.** Using an item in hand (and bots or agents) calls
  `UseItem {slot, direction?, creation?, part?}`. The host checks the slot holds a scripted item, that
  `direction` is close to where the player looks (`aim_tolerance` in `survival.json`) and that the
  part exists, then runs `can_use` and, if that allows it, `on_use`. Both get `target`:
  `{from, direction, creation?, part?}`, where `creation` and `part` are what the crosshair
  points at within reach, and `aiming`, true while the player holds right click to aim (`AimItem {on}`).
  `EquipItem {slot?}` says what is in hand. `ReloadItem {slot}` runs `can_reload_item`, then `on_reload_item`.
  A dead player's uses are refused.
- **`can_use`, `can_reload_item` and the script's setup only read.** Functions that change the world (`item.consume`, `item.replace`, `item.counter`,
  `holder.heal`, `inventory.take`, `farm.plant`, `farm.water`, `fire.douse`, `fire.ignite` and the `combat` ones that deal damage
  or launch) raise an error there.
- **Damage follows the world's rules.** In Creative nothing hurts players; with friendly fire off
  (the default) no shot, splash or part script hurts a player, but a player's own splash still hurts them.
- `part` and `creation` functions don't work in an item script; `world` and `log` do. The `item`,
  `holder` and `inventory` libraries only work in an item script; `combat`, `farm` (except `farm.plant`) and
  `fire` work in part and mod scripts too (below).

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
| `item.replace(id)` | Turns one of the item into the item `id` (bare ids are in the item's own mod). The last one in its slot becomes `id` where it is, so it stays in the player's hand (the full water bucket emptying); one of several goes into the inventory. `true`, or `false` when there is no room. |
| `item.counter({value?, max?, ammo?, busy?})` | Sets what the holder's hotbar slot and HUD counter show for the item (UI spec §10): `value` (rounds loaded) out of `max`, the `ammo` item they carry (counted from their inventory), and `busy` seconds while it reloads. Only the holder is sent it. |
| `item.sound(key)` | The sound id the item binds to `key` in its `sounds` (`fire`, `reload`), for `sound.play`; `nil` when unbound. |

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
| `holder.aiming()` | `true` while they hold right click to aim the item. |

### `inventory`

The holder's inventory. Item ids may be bare (`potato`, in the item's own mod) or `mod_id:item`.

| Function | Returns |
|----------|---------|
| `inventory.count(item)` | How many of `item` they carry. |
| `inventory.take(item, count?)` | Takes `count` (default 1). `true`, or `false` (and nothing taken) when they carry fewer. |

Part and mod scripts can use every `combat` function, `farm.soil`, `farm.water` and `farm.near` too (a saw, a drill, a water
cannon, a mounted gun or a mortar), and both kinds use [`fire`](#fire). They act for the host, skip nothing (aim rays
and launch projectiles from just outside the part) and have no reach limit beyond the ray's range. A projectile a
part or mod script launched lands in that script's `on_hit`; a part that is gone by then hears nothing.

### `combat`

Hits are tables `{kind, position, player?, creation?, part?, object?, bot?, type?}`; `kind` is `player`, `part`,
`object` (a tree, rock or bush), `bot` or `ground`. `combat.cast` also sets `type`: the part's qualified type
(`vanilla:wood_block`) or the world object's kind (`birch_tree`), so a tool can treat each its own way. What damage did comes back with `damage` added. These
are the same primitives bots use (chunk 21).

| Function | Returns |
|----------|---------|
| `combat.cast(from, direction, range)` | The first thing a ray hits within `range` m, skipping the holder, with its `distance`; or `nil`. `range` is at most `max_ray_length`. |
| `combat.hit(hit, damage, push?, kind?)` | Deals `damage` to what a cast hit. A part's Body also gets `push` (a `vec3` impulse, N·s) unless the part broke off. `kind` is the damage kind (`drill`, `saw`, `weapon` (the default), `sledge`, `explosion`, `bot`): a world object with `damage_from` takes only the kinds it lists, so only a drill (`kind = "drill"`) breaks a rock outcrop. Returns the hit with `damage` dealt. |
| `combat.hitscan(from, direction, range, damage, impulse?)` | `combat.cast` then `combat.hit`, pushing along the ray with `impulse` N·s. The hit, or `nil` when nothing is in range. |
| `combat.splash(at, radius, damage, impulse?)` | Hurts every player, part and world object within `radius` m of `at`, falling off from `damage` at the centre to 0 at the edge, and pushes parts away with up to `impulse` N·s. A list of hits. `radius` is at most `max_overlap_radius`. |
| `combat.explode(at, radius, damage, impulse?)` | An explosion: the same damage and push as `combat.splash`, and every peer sees the blast (a fireball, a flash and smoke as big as `radius`). Play its sound with `sound.play`. A list of hits. |
| `combat.pellets(direction, count, spread)` | `count` (at most 64) unit directions: the first along `direction`, the rest evenly round a cone of `spread` degrees. No randomness, so every replay agrees. |
| `combat.projectile(from, velocity, options?)` | Launches a projectile from `from` at `velocity` m/s. `options`: `gravity` (a `vec3`, default `world.gravity()`), `lifetime` (s, default and at most `max_projectile_lifetime`), `data` (passed to `on_hit`), `look` (how clients draw it: `"water"` is a blob of water that splashes where it lands; default a plain ball), `size` (how big the look is drawn, in metres, up to 10: for `"water"` the radius its splash covers, crown, spray, mist and a wet patch; default 1.2). It flies on the host, every client draws it along its arc, and `on_hit` runs where it lands; it deals no damage by itself. Returns its id, or `false` and the reason when `max_projectiles` are already in flight. |

### `farm`

Soil parts (chunk 20). A soil table is `{seed?, crop?, stage, ripe, water, capacity}`: the planted
seed and its crop (qualified ids), the growth stage (0 to 4, 4 is `ripe`) and the seconds of water it
holds of its `capacity`. Each function checks the part is soil within the holder's `reach`, and
returns `nil` and the reason when it can't act; in `on_hit`, `farm.water` reaches wherever the item's projectile
landed. Crops grow, and are harvested (`ActivatePart`), by the
soil part's own script ([`soil`](#soil)); the seed's `seed` section sets what grows and how fast.

| Function | Returns |
|----------|---------|
| `farm.soil(creation, part)` | The soil table. |
| `farm.plant(creation, part, seed?)` | Plants `seed` (an item with a `seed` section; default this item) in empty soil and returns the soil table. It takes nothing from the inventory: use `item.consume`. |
| `farm.water(creation, part, seconds)` | Adds `seconds` of water, up to `capacity`, and returns the soil table. Takes nothing from the inventory either. |
| `farm.near(position, radius)` | Soil parts within `radius` m of `position` (at most `max_overlap_radius`), nearest first: a list of `{creation, part}`, for watering where a splash lands. |

## `fire`

Fire (D13) burns on the host: at POI `fire` markers (the crash site's three) and on flammable parts, which
catch from a fire next to them, grow, take damage until they break off and set their flammable neighbours
alight; how fast is `game/data/fire.json` (see [Survival](survival.md#fire)). Scripts decide what puts fires
out or starts them. A fire table is `{id, position, strength, creation?, part?}`: `strength` runs from 0
(out) to 1, and burning parts have `creation` and `part`. Part and item scripts can both use the library; in
an item script `douse` and `ignite` only reach as far as the holder's `reach` (plus `radius`), except that `douse`
reaches wherever the item's projectile landed in `on_hit` (thrown water), and like every function that changes
the world they raise an error in `can_use`.

| Function | Returns |
|----------|---------|
| `fire.near(position, radius)` | Fires burning within `radius` m of `position`, nearest first. `radius` is at most `max_overlap_radius`. |
| `fire.douse(position, radius, amount)` | Water: every fire within `radius` m loses `amount` strength and goes out at 0. The fires it reached, with their strength after; or `nil` and why. |
| `fire.ignite(creation, part, strength?)` | Sets a flammable part alight (`strength` defaults to `ignite` in `fire.json`). `true`, or `false` and why. |
| `fire.strength(creation, part)` | How strongly a part burns, 0 when it does not. |

```lua
-- A full water bucket: throw the water, empty the bucket, and put out what it lands on.
function on_use(target)
  combat.projectile(holder.eye(), (target.direction + vec3(0, 0.35, 0)):normalized() * 7, { look = "water" })
  item.replace("water_bucket")
end

function on_hit(landing)
  return { fires = fire.douse(landing.position, 1.2, 1) }
end
```

## Mod scripts

Behaviour that belongs to no part (game rules, a welcome message, a timer, a custom event) goes in a mod
script. List it in `mod.json` and it runs on the host for the whole world, after the mods load and in load
order:

```json
{ "id": "welcome", "name": "Welcome", "version": "1.0.0", "dependencies": [{ "id": "vanilla" }],
  "scripts": ["scripts/rules.lua"] }
```

A mod script is like a part script without a part: it has `state` (saved with the world), the hooks
`on_create`, `on_update(dt)`, `on_reload`, `on_destroy` and `on_event`, and every library that does not need a part
(`world`, `player`, `sound.play` with a position, `combat`, `farm`, `fire`, `timer`, `events`, `actions`, `require`). `part.*` and
`creation.*` raise an error there. It acts as the host, and it stops on an error like any script until it is
fixed and reloaded. Clients run no scripts; they see what the host does.

```lua
-- Greet every player who joins, and count them.
events.listen("players")

function on_event(topic, data)
  if data.kind == "joined" then
    state.visits = (state.visits or 0) + 1
    player.give(data.peer, { { item = "vanilla:component_kit", count = 1 } })
    print("Welcome, " .. data.name .. " (visitor " .. state.visits .. ")")
  end
end
```

### `events`

Events are the same topics the [AI endpoint](ai/reference.md) streams (every one except script messages).
Listening is per script: call `events.listen` at the top level or in `on_create`; a reload starts with no topics.
Events wait until the start of the next step and arrive in order, at most `max_queued_events` of them.

| Function | Returns |
|----------|---------|
| `events.listen(topic)` | Hears `topic` as `on_event(topic, data)`. Part scripts may listen too. An unknown topic is an error. |
| `events.unlisten(topic)` | Stops hearing it; true when the script listened to it. |
| `events.topics()` | A list of every topic a script can listen to (the table below). |

`data` is the event's payload as a table, with snake_case keys as the AI endpoint sends it. Positions in it are
arrays of three numbers (`data.position[1]` is x), not `vec3` values.

| Topic | When | `data` |
|-------|------|--------|
| `actions` | Every state-changing Action API call by a player, bot or agent (not by Lua scripts) | `{peer, source, action, args, ok, code, message, value}` |
| `bots` | A bot spawned, was removed, attacked or destroyed a crop | `{kind = "spawned", bot}` with the bot's `{id, type, position, yaw, health, max_health, dead, raid?}`; `{kind = "removed", bot = {id}}`; `{kind = "attacked", attack}`; `{kind = "crop_destroyed", creation, part, bot}` |
| `chat` | A chat line | `{id, peer, name, text}` |
| `dialogue` | A quest character or the Logbook spoke | `{quest, lines}`, each line `{speaker, text}` |
| `drop` | A pile of items appeared, changed or was picked up | `{kind = "changed", drop}` with `{id, position, items, placed, expires_in?}`; `{kind = "removed", drop = {id}}` |
| `fire` | A fire at a POI marker went out | `{kind = "out", fire}` with `{id, position, strength, creation?, part?, poi?, site?, marker?}` |
| `hurt` | A player took damage | `{peer, amount, cause}` |
| `mods` | Mod content was validated or reloaded (mod tools), or a package was installed or uninstalled | `{kind = "validated"/"reloaded", result}`, or `{action = "installed", id, version, change}` / `{action = "uninstalled", id}` |
| `players` | A player joined or left the world | `{kind = "joined"/"left", peer, name}` |
| `projectiles` | A projectile was launched or landed | `{kind = "launched", projectile}` with `{id, owner, from, velocity, gravity, lifetime, age, look?}`; `{kind = "landed", projectile = {id, owner, point, hit?}}`, `hit` being `{player?, creation?, part?, object?, bot?}` |
| `quest` | Quest progress changed | The `GetQuests` state: `{quests, flags, revealed}` |
| `raid` | A raid started, advanced or ended | The `GetRaid` view: `{id, level, budget, crop_value, players, phase, target, bots, composition}` |
| `reports` | An agent filed a note, recommendation or bug report | `{id, kind, created, author, title, tags, severity?, priority?, has_screenshot}` |
| `seats` | Someone sat down or stood up | `{seat = {peer, creation, part}, seated}` |
| `vitals` | A player's health changed, they died or respawned | The `GetVitals` view: `{peer, health, max_health, dead, respawn_in, has_bed, mode, slow, slow_for, difficulty, friendly_fire}` |
| `world` | The world was saved, or a load, new world or reset finished | `{kind = "saved", path, status}` or `{kind = "loaded", status}`, `status` being the `world.info` view |

Script messages (`messages`) and `events_dropped` are endpoint-only: a script can't listen to them.

### `actions`

Any state change a player or agent can make, a mod script can make too, through the same validated Action API
([AI reference](ai/reference.md) lists every action and its arguments). Arguments are a table in the action's
snake_case JSON form.

| Function | Returns |
|----------|---------|
| `actions.call(name, args?)` | Runs the action as the host: `true` and its value, or `false` and the reason (logged once). Mod scripts may call every action except the ones that move a Creation (`ApplyForce`, `SetBearingTarget`, `SetPistonTarget`); part and item scripts may call only actions that read (`GetPlayers`, `GetInventory`, `ListCreations`, …). |
| `actions.list()` | The names `actions.call` accepts. |

### Sharing code with `require`

`require(path)` runs a file once per mod and hands every script that asks the same result, usually a table of
functions. The file runs in its own environment over the mod's libraries, so it has no `state` or `properties`
of its own; pass what it needs as arguments. A mod reaches its own files and those of the mods in its
`dependencies` (`"vanilla:scripts/lib/x.lua"`). A changed file loads again the next time it is required, so
reload the script that requires it.

```lua
-- scripts/lib/maths.lua
local M = {}
function M.clamp(x, lo, hi) return math.max(lo, math.min(hi, x)) end
return M

-- scripts/thruster.lua
local maths = require("scripts/lib/maths.lua")
```

## `timer`

Mod and part scripts can ask to be called later instead of counting seconds in `on_update`. Timers count game
time: they fire at the start of a step, before `on_update`, and a part's timers wait while its Creation is not
simulated. A reload or a stopped script ends its timers (they are not saved), so start them again in `on_create`.
A script runs at most `max_timers_per_script` at once.

| Function | Returns |
|----------|---------|
| `timer.after(seconds, fn)` | Calls `fn(id)` once, `seconds` from now (0 means the next step); returns the timer's id. |
| `timer.every(seconds, fn)` | Calls `fn(id)` every `seconds` (more than 0, at most once a step) until cancelled; returns the id. |
| `timer.cancel(id)` | Stops the timer; true when it was still waiting. |
| `timer.count()` | How many of this script's timers are waiting. |

```lua
function on_create()
  state.minutes = state.minutes or 0
  timer.every(60, function(id)
    state.minutes = state.minutes + 1
    print(state.minutes .. " minutes played")
    if state.minutes >= 10 then timer.cancel(id) end
  end)
  timer.after(5, function() print("five seconds in") end)
end
```

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
