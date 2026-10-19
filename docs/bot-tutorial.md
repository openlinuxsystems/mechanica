# Bot Tutorial

Bots are the creatures of a world: the Scuttlers, Rakehands, Spoolers and the Thresher that raid farms,
and the passive Grazer. A bot is data plus an optional Lua script, like a part:

- `bots/<id>.json` gives its numbers (health, speed, attacks, drops) and its **behaviour tree**, the
  plan it follows every think.
- `scripts/bots/<id>.lua` (optional) adds hooks (`on_spawn`, `on_damaged`, …) and custom tree nodes.

Bots run on the host. Every move and attack goes through the same Action API as a player's, so the host
checks reach, line of sight and cooldowns the same way, and every client sees the same bots.

Make bots with the **Bot Behavior Editor** (main menu, "Bot Behavior Editor"). The worked example in
this tutorial is the Sentry Drone in [`examples/mods/example_bots`](../examples/mods/example_bots).

## 1. Your first bot

1. Open the Bot Behavior Editor and pick your mod (or make one in the Mod Manager first).
2. Click **New**, choose a template (`melee`, `ranged` or `passive`) and type an id such as `sentry_drone`.
3. On the **Bot** tab, set its name, health, speed and attacks.
4. On the **Behavior** tab, build its tree (section 3).
5. On the **Script** tab, write any hooks or custom nodes (section 4) and click **Save script**.
6. Click **Export**. The editor checks the bot against the loaded mods and writes `bots/<id>.json`.

7. Click **Test in Sandbox**. The editor exports the bot, opens the Test Sandbox with the mods reloaded
   and spawns the bot a few metres in front of you. The sandbox's bot spawner (**Spawn bot**) spawns any
   loaded bot type, and **Add local client** checks it with a client too. **Back** returns to the editor.

To see it in a real world, start a Creative game with your mod enabled and send `SpawnBot` (section 6)
through the [External AI Endpoint](ai-endpoint.md), or add the bot to your raid table and send
`StartRaid`.

## 2. Bot data

`bots/<id>.json`. Keys are snake_case, units are SI, and unknown keys are kept and warned about.

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `id` | string | required | Lowercase snake_case, unique within the mod. Other mods write `my_mod:id`. |
| `name` | string | required | Display name. |
| `description` | string | none | A sentence for the editor. |
| `family` | string | `id` | Sound family: its sounds are `<family>_idle`, `_alert`, `_attack`, `_death`. |
| `health` | number | required | Full health. |
| `speed` | number, m/s | required | Top speed on land. |
| `water_speed` | number, m/s | `speed` | Top speed in water. |
| `swim_depth` | number, m | none (walks on the bottom) | Makes it a swimmer: in water it floats with its middle this far below the surface, never below the bottom, and its model plays `swim` and `swim_idle`. |
| `sight` | number, m | 30 | How far it notices players and crops. |
| `size` | `{radius, height}`, m | 0.4, 1.2 | Its capsule, standing on its feet. |
| `color` | `"#rrggbb"` | `#808080` | Colour of the placeholder body (a bot with no `model`). |
| `model` | object | none | Its animated model (below). Omitted shows a capsule in `color`. |
| `melee_immune` | bool | false | Melee hits (the Sledgehammer) do nothing. |
| `passive` | bool | false | Never joins raids and never attacks first. |
| `raid_cost` | integer | none | What one bot costs from a raid's budget. Omitted means it never raids. |
| `attacks` | list | `[]` | Its attacks (below). |
| `drops` | list | `[]` | `{item, chance, count, count_max}`; each is rolled on its own when it dies (`chance` 0 to 1, default 1), and drops a random count from `count` to `count_max` (default `count`). |
| `behavior` | node | none | Its behaviour tree (section 3). |
| `script` | path | none | Its Lua script, relative to the mod root. |
| `properties` | object | `{}` | Free-form values its script reads as `properties`. |

An attack:

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `id` | string | required | Unique within the bot; the tree and script name attacks by id. |
| `kind` | `melee`, `ranged`, `area` | required | Melee reaches one target next to it, ranged one target it can see, area everything around it. |
| `damage` | number | required | Damage to a player, and to a crop or part unless `part_damage` is given. |
| `part_damage` | number | `damage` | Damage to a crop or part. |
| `range` | number, m | required for melee and ranged | Reach. |
| `radius` | number, m | required for area | Area attacks only: blast radius; damage falls to 0 at the edge. |
| `interval` | number, s | required | Seconds between two uses. |
| `damage_over_time` | `{per_second, seconds}` | none | A lasting effect on players hit. |
| `slow` | `{factor, seconds}` | none | Players hit move at `factor` (more than 0, less than 1) of their speed for `seconds`. The strongest slow on a player wins. Survival only. |
| `object_damage` | number | 0 | Damage to world objects (trees, rocks, bushes): the one it is aimed at (`{object = id}`), or for an area attack every one in its radius. 0 means it cannot hurt them. Broken objects drop their items as if a player broke them. |
| `kills_self` | bool | false | Using it kills the bot (a kamikaze charge). |
| `hits_parts` | bool | true | Whether it can hurt crops and parts. |
| `sound` | sound id | `<family>_attack` | Sound event it plays. |

Damage goes through the same host primitives as weapons (`SurvivalActions`), so Creative applies to bots
too: in Creative nothing is hurt. Friendly fire doesn't stop bots; they hurt players either way.

### Using the editor's Bot tab

The **Bot** tab edits every key above. The top of the grid has a field for each simple key: **Id**, **Name**,
**Description**, **Family (sounds)**, **Health**, **Speed**, **Water speed**, **Swim depth**, **Sight**, **Radius** and **Height**
(the `size`), **Colour**, **Melee immune**, **Passive**, **Raid cost** and **Script**. Optional fields left empty
take the default in the table.

Under the heading **Attacks and drops** are three JSON boxes: **Attacks** (the `attacks` list), **Drops** (the
`drops` list) and **Properties** (the `properties` object). Type the JSON, then click **Apply** under the box: the
editor parses it and uses it, or shows the parse error beside the button and keeps what was there. An empty box
removes the key. For example, a melee bite and a drop:

```json
[ { "id": "bite", "kind": "melee", "damage": 12, "range": 1.2, "interval": 1.5 } ]
```

```json
[ { "item": "vanilla:component_kit", "chance": 0.25, "count": 1, "count_max": 2 } ]
```

Under **Model and animation**, the **Model** box takes the `model` object (below) the same way.

**Export** checks the attacks and drops (a missing required key, a number out of range, an attack listed
twice, a malformed item or sound id) and writes the file only when they pass.

### Models and animation

A `model` is a glTF (`.glb` or `.gltf`) in your mod, with its feet at the origin and facing -Z. It only changes
how the bot looks: the host still moves and hits with the bot's capsule (`size`).

```json
"model": {
  "mesh": "meshes/bots/scuttler_green.glb",
  "attacks": { "whip": ["tail_slam", "tail_sweep", "claw_combo"] },
  "clip_speeds": { "patrol": 0.93, "chase": 3.0 },
  "eye_calm": "#2bff3a",
  "eye_engaged": "#ff1a0a",
  "effects": { "whip": "shockwave" }
}
```

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `mesh` | path | required | The glTF, relative to the mod root (`other_mod:path` for another mod's). |
| `attacks` | `{attack id: [clip, …]}` | each attack plays the clip named after it | Clips an attack plays, one per use, in turn. |
| `clip_speeds` | `{clip: m/s}` | none | Ground speed at which a walking clip plays at normal speed. The clip speeds up and slows down with the bot, so its feet do not slide. |
| `eye_calm` | `"#rrggbb"` | `#2bff3a` (green) | Colour of its glowing (emissive) materials while calm. |
| `eye_engaged` | `"#rrggbb"` | `#ff1a0a` (red) | The same while engaged (`bot.engage()`, section 4). |
| `effects` | `{attack id or "death": effect}` | none | An effect every peer sees when it uses the attack or dies: `explosion` (a fireball, flames, smoke, sparks and flying scrap) or `shockwave` (a ring rippling over the ground with dust and stones). Each is as big as the attack's `radius` (its `range` for a melee or ranged attack; a death uses its largest area attack). One effect shows at a time, so a blast that kills its bot shows once. |

Name the clips after what the bot does. Every peer picks the clip from what the host already sends, so everyone
sees the same one:

| Clip | Plays |
|------|-------|
| `idle` | Standing still (loops). |
| `patrol` | Walking while calm (loops). |
| `chase` | Walking while engaged, or faster than 1.6 times the `patrol` clip speed (loops). |
| `alert`, `lose_interest` | Once, when it engages and when it calms down. |
| `hit` | Once, when it is hurt. |
| `death` | Once, when it dies; it holds the last frame until the body is gone. |
| `jump`, `fall`, `land` | Going up, falling (loops), and touching down. |
| `swim`, `swim_idle` | Swimming and treading water, for a swimmer (`swim_depth`) in water (loop). Without them it walks. |
| one per attack | Once, when it uses the attack. The host deals the damage as the attack starts, so keep the wind-up short (about 0.2 s). |

A missing clip is skipped (a missing walk clip plays `idle`). Clips play in place: they never move the bot.
Glowing materials fade from `eye_calm` to `eye_engaged` and back, and go dark when it dies.

**Feet.** A node named `<leg>_Hip` with a descendant `<leg>_Foot` (an empty at the tip of the foot) is a leg. The
game tilts the body to the ground under the feet, and turns each hip so a foot that is down in the clip stands on
the ground, on slopes and on creations alike.

Vanilla's five Scuttlers are made by [`tools/scuttler/build_scuttler.py`](../tools/scuttler/build_scuttler.py), a
Blender script you can copy for a rig of the same shape. They share one rig and differ in paint, size and one
signature piece (`--variant blue` builds just one). In a batch, a bot whose `base` has a model inherits its
`attacks` map too, so set the base's attack ids to `null` in its own map (`"whip": null`), as the Blue, Yellow,
Red and Seed Scuttlers do.

## 3. The behaviour tree

A behaviour tree is a plan made of nodes. Each node is a JSON object with a `type`; composites and
decorators have `children`. Every think (`think_interval` in `game/data/bots.json`, 0.1 s) the tree runs
from the root and each node returns one of three results:

- `success`: it did what it does;
- `failure`: it could not;
- `running`: it is still at it (walking somewhere, waiting) and wants to be run again next think.

The Sentry Drone's tree, from `bots/sentry_drone.json`:

```json
{ "type": "selector", "children": [
  { "type": "sequence", "children": [
    { "type": "health_below", "fraction": 0.25 },
    { "type": "find_player" },
    { "type": "flee", "range": 20 } ] },
  { "type": "sequence", "children": [
    { "type": "find_player", "range": 15 },
    { "type": "target_in_range", "attack": "zap" },
    { "type": "stop" },
    { "type": "attack", "attack": "zap" } ] },
  { "type": "patrol", "speed": 0.5 } ] }
```

The selector tries each plan in turn: run away when badly hurt, else stop and shoot a player within 15 m, else walk
the patrol route. `patrol` is not a built-in node; the drone's script defines it (section 4).

### Built-in nodes

Composites and decorators:

| Node | Parameters | What it does |
|------|------------|--------------|
| `sequence` | children | Runs its children in order. Fails at the first child that fails and is running while a child runs; succeeds when all succeed. |
| `selector` | children | Tries its children in order and returns the first result that is not failure; fails when every child fails. |
| `invert` | one child | Turns its child's success into failure and failure into success. |
| `succeed` | one child | Runs its child and succeeds unless it is still running. |
| `cooldown` | one child, `seconds` (5) | Fails for a while after its child succeeds. |

Leaves:

| Node | Parameters | What it does |
|------|------------|--------------|
| `find_player` | `range` (sight) | Targets the nearest living player in range. Fails when there is none. |
| `find_crop` | `range` (sight) | Targets the nearest planted crop in range (near the raid's farm during a raid). Fails when there is none. |
| `has_target` | | Succeeds while the bot has a target that still exists. |
| `clear_target` | | Forgets the target. Succeeds. |
| `target_in_range` | `attack` | Succeeds when the target is within reach of the attack. |
| `chase` | `attack`, `speed` (1) | Walks toward the target. Running until within reach of the attack, then success; failure without a target. |
| `attack` | `attack` | Uses an attack on the target. Succeeds when the host accepts it; fails when out of range or not ready. |
| `flee` | `range` (15) | Runs from the target until that far away, then success. |
| `go_to_raid` | `radius` (6) | Walks to the farm its raid targets. Running on the way, success on arrival, failure outside a raid. |
| `in_raid` | | Succeeds when the bot is part of a raid. |
| `health_below` | `fraction` (0.3) | Succeeds when health is below that share of full health. |
| `in_water` | | Succeeds while the bot stands in water. |
| `wander` | `radius` (8), `speed` (0.4) | Strolls between random spots near where it spawned. Always running. |
| `wait` | `seconds` (1) | Stands still. Running for the time, then success. |
| `stop` | | Stops walking. Succeeds. |
| `hesitate` | `range` (4), `min_speed` (4), `seconds` (2), `part` ("") | Balks at a part spinning on a joint within `range` m (a bearing's rotor, a wheel, a saw; only parts of the types in `part`, such as `vanilla:saw_blade,vanilla:drill`, when it is set) turning faster than `min_speed` rad/s: backs away, running, for `seconds`, then fails so the tree goes on. Fails at once when nothing spins near. Put it under `invert` before a `chase`. |
| `break_objects` | `attack` | Uses the attack on the nearest world object within its reach. Succeeds when it hit one; fails when none is in reach or the attack is not ready. The attack needs `object_damage`. |
| `call` | `function` | Calls a function of the bot's script with `(node, dt)`; its return value is the result. |

`speed` is a share of top speed, 0 to 1. The editor's palette shows the same list with each node's help
text, and the data checker refuses unknown parameters and attack ids that the bot does not have.

### Typical shapes

- **Raider:** `selector[ sequence[find_player, chase, attack], sequence[find_crop, chase, attack], go_to_raid, wander ]`.
  Vanilla's Green Scuttler is this tree; open it in the editor to see it.
- **A once-in-a-while move:** wrap it in `succeed(cooldown(...))` so the rest of the sequence runs on
  either way. The Rakehand's battle cry is `succeed(cooldown(call "cry"))`.
- **Shy creature:** `selector[ sequence[find_player, flee], wander ]` (the Grazer, with `passive: true`).
- **Wary of blades:** put `invert(hesitate)` before each `chase`, so the bot balks at a spinning saw
  or wheel before it comes on anyway (the Rakehand). With `part: "vanilla:saw_blade"` it fears only a saw blade at
  cutting speed (every Scuttler).
- **Clearing its way:** `sequence[ succeed(break_objects), selector[...] ]` smashes trees and rocks in
  reach whatever else it is doing (the Thresher, whose kick has `object_damage`).

### Using the editor's Behavior tab

Select a node in the tree, pick a node type in the palette and click **Add**: a composite takes any number
of children, a decorator one, a leaf none. **Up** and **Down** reorder a node among its siblings and
**Remove** deletes it with its children. The grid below the tree edits the selected node's parameters.
Undo and redo cover every change.

## 4. The bot's script

The script runs in the same sandbox as part scripts (see the [Lua API Reference](lua-api.md): no `io`,
`os` or file loading, `vec3` and `log` available). Each bot has its own copy of the script's globals.

### Hooks

All optional:

| Hook | When |
|------|------|
| `on_spawn()` | Once, when the bot appears. |
| `on_reload()` | After its script changed on disk and was reloaded (instead of `on_spawn`). |
| `on_think(dt)` | Every think, instead of the tree. Call `behavior.run(tree, dt)` to run the tree as well. |
| `on_damaged(amount)` | After a hit it survives, with the health lost. |
| `on_death()` | When it dies (the killing hit calls only this). |

Two tables last for the bot's life: `state` (yours, kept across reloads) and `properties` (the bot file's
`properties`, read-only by convention).

### Custom nodes

A node whose `type` is not built in calls the script's global function of that name with `(node, dt)`,
where `node` is the node's table, parameters included. Return `"success"`, `"failure"` or `"running"`
(or `true`/`false`). The Sentry Drone's patrol:

```lua
function patrol(node, dt)
  local points = properties.patrol
  if points == nil or #points == 0 then
    bot.stop()
    return "success"
  end
  local offset = points[state.next_point]
  local home = bot.home()
  local goal = vec3(home.x + offset[1], home.y, home.z + offset[2])
  local away = goal - bot.position()
  away.y = 0
  if away:length() < 0.75 then
    state.next_point = state.next_point % #points + 1
  end
  bot.move_to(goal, bot.speed() * (node.speed or 0.5))
  return "running"
end
```

`{ "type": "call", "function": "patrol" }` does the same thing; the editor offers `call` because its
palette lists only built-in nodes. A mod can also add a node for all its bots by assigning
`behavior.leaves.my_node = function(node, dt) ... end` in a script its bots share.

The built-in library is plain Lua over the same primitives (`src/Mechanica.Scripting/Bots/behavior.lua`),
so any built-in node can be read as an example. `behavior.target()` and `behavior.set_target(t)` read and
set the target the built-in nodes share.

`behavior.node(node, dt)` is internal: it is how a composite or decorator runs one child (a built-in leaf, or
`bot.custom(node.type)` for a custom one), and it returns `"success"`, `"failure"` or `"running"`. You only need it
when you write your own composite in `behavior.leaves` that runs the children in `node.children`; a plain leaf
never calls it. Use `behavior.run(tree, dt)` to run a whole tree.

### bot.*

The bot whose script is running:

| Function | Returns |
|----------|---------|
| `bot.id()`, `bot.type()`, `bot.name()` | Its id (a number), its type (`mod:id`) and name. |
| `bot.position()`, `bot.centre()`, `bot.home()` | Its feet, the middle of its body and where it spawned, as `vec3`. |
| `bot.yaw()` | Facing, in radians. |
| `bot.health()`, `bot.max_health()` | Health now and full. |
| `bot.speed()`, `bot.sight()`, `bot.radius()` | Top speed here (water counts), sight and body radius. |
| `bot.in_water()` | Whether it stands in water. |
| `bot.random()` | A number in [0, 1) from the bot's own seeded stream (replays the same). |
| `bot.memory()` | A table for the tree's state (the built-in nodes keep the target here). |
| `bot.attacks()`, `bot.attack_info(id)` | Its attacks: `{id, kind, damage, range, radius, interval, cooldown, ready}`. |
| `bot.raid()` | `{id, level, target}` while it raids, else nil. |
| `bot.move(direction, speed?)` | Walk that way at up to `speed` m/s (top speed when omitted). |
| `bot.move_to(point, speed?)` | Walk toward a point. |
| `bot.face(point)` | Stand and turn toward a point. |
| `bot.stop()` | Stand still. |
| `bot.attack(id, target?)` | Use an attack. Returns `true`, or `false, reason` when the host refuses. |
| `bot.reach(target)` | Distance from its body to the target's edge, or nil when the target is gone. |
| `bot.alert()`, `bot.sound(id)` | Play its `_alert` sound, or any sound event, at the bot. `bot.alert()` also engages it. |
| `bot.engage()` | Keep it engaged (fighting: its eye turns red) for `engaged_seconds` (`game/data/bots.json`). `find_player` calls it while it has a target; attacking and being hurt engage it too. |
| `bot.custom(name)` | The script's global function `name`, the one a custom node of that `type` runs. An error when the script defines no such function. |

A **target** is a table naming one thing: `{player = peer}`, `{creation = id, part = id}` (a part or a
crop's soil), `{bot = id}`, or `{object = id}` (a world object, for attacks with `object_damage`).

### world.*

| Function | Returns |
|----------|---------|
| `world.time()` | Host time, in s. |
| `world.players(range?)` | Living players in range (default: sight), nearest first: `{peer, position, distance}`. |
| `world.crops(range?)`, `world.crops_near(point, range)` | Planted crops: `{creation, part, position, value, ripe, distance}`. |
| `world.bots(range?)` | Other bots: `{bot, type, position, distance}`. |
| `world.objects(range?)` | World objects a bot can break (trees, rocks, bushes) within `range` of its body's edge (default: sight), nearest first: `{object, position, distance}`. |
| `world.spinning(range?, min_speed?, part?)` | Parts spinning on a joint within `range` m (default: sight) and faster than `min_speed` rad/s (default 4) against what they turn on: `{creation, part, position, speed, distance}`, nearest first. With `part` (part types separated by commas, such as `vanilla:saw_blade,vanilla:drill`) only parts of those types on the turning side count. |
| `world.locate(target)` | Where a target is (`vec3`), or nil. |
| `world.raycast(from, to)` | The first thing hit: `{position, player?, creation?, part?, bot?}`, or nil. |

When the host refuses an action (out of range, not ready, the target is gone), the call returns
`false, reason`, and the game log shows each kind of refusal once. A Lua error stops that bot until the script is
fixed and saved; the bot stands still meanwhile, and the error is in the log.

## 5. Raids

When the crops planted in a world are worth enough, a raid comes: the raid horn sounds, and after a short
warning bots arrive in a ring around the most valuable farm and go for its crops. Raids happen in Survival.
The table lives in `raids/raids.json`; like world generation, the last mod in load order that ships one
wins. To add to a table instead of replacing it, ship `raids/raids.patch.json`: levels merge by `level`
and their bots by `bot`, as in a [world patch](world-generation.md#patching-the-world). Bare bot ids in
a patch are your mod's:

```json
{ "levels": [{ "level": 1, "bots": [{ "bot": "rust_bug", "count": 2 }] }] }
```

```json
{
  "check_interval": 60,
  "cooldown": 900,
  "warning": 10,
  "spawn_distance": 35,
  "duration": 300,
  "player_multipliers": [1, 1.3, 1.6, 2],
  "levels": [
    { "level": 1, "min_value": 10, "budget": 4, "bots": [ { "bot": "scuttler_green", "count": 4 } ] }
  ]
}
```

| Key | Meaning |
|-----|---------|
| `check_interval` | Seconds between checks of the crop value. |
| `cooldown` | Seconds after a raid before the next can start. |
| `warning` | Seconds between the horn and the raiders' arrival. |
| `spawn_distance` | How far from the farm raiders arrive, in m. |
| `duration` | Longest raid, in s; raiders still standing then leave. |
| `player_multipliers` | Budget multiplier for 1, 2, 3, 4… players; more players use the last. |
| `levels[].min_value` | Total planted crop value (each seed's `value`) at which the level starts. |
| `levels[].budget` | Bot budget for one player, spent at each bot's `raid_cost`. |
| `levels[].budget_per_value` | Extra budget per point of value above `min_value` (for the top level). |
| `levels[].bots` | The mix sent at the level's budget: `{bot, count}`. A bigger budget sends more of the same mix. |
| `levels[].notes` | A note for the editor. |

A bare bot id means the table's own mod; write `vanilla:rakehand` for another mod's. Every bot in a raid
needs a `raid_cost`.

Edit the table on the editor's **Raids** tab. The heading names the file (`raids/raids.json`) and reminds you
that the last mod in load order that has one wins. The **Table** box holds the whole table as JSON; click
**Apply** to use your edits (a parse error shows beside the button). Below it, the preview shows what a raid
would send without starting one: set **Preview crop value** (the planted crop value, 0 to 100,000) and
**Preview players** (1 to 16), and the line under them reads, for example, "Level 2, budget 9 for 2 player(s):
3x vanilla:scuttler_green, 1x vanilla:rakehand", or "Crops worth 5 set off no raid.", or lists what to fix
first when the table names an unknown bot or a bot without a `raid_cost`. **Save raid table** checks the table
against the mods' bots and writes `raids/raids.json`; the status line says whether it was saved and why not.

## 6. Action API

Bots use these actions, and so can the [External AI Endpoint](ai-endpoint.md). Spawning, removing and
starting raids are open to players only in Creative.

| Action | Arguments | Notes |
|--------|-----------|-------|
| `SpawnBot` | `bot`, `position`, `yaw?` | Returns `{bot}`. |
| `DespawnBot` | `bot` | |
| `MoveBot` | `bot`, `direction?`, `speed?` | Walk that way; no direction stops it. |
| `BotAttack` | `bot`, `attack`, `player?` / `creation?`, `part?` / `target?` / `object?` | Checked for reach, line of sight and cooldown. Returns `{hits}`. |
| `GetBots`, `GetBot` | none; `bot` | Queries. |
| `StartRaid` | `level?` | Starts a raid now at the given (or the current) level. |
| `GetRaid` | none | The raid in progress. |

## 7. Packaging

To change or remove another mod's bot (a tougher `vanilla:scuttler_red`), override it, patch it or list it in
`remove.json` instead of copying it under a new id; see the [Packaging Guide](packaging-guide.md#overriding-bots-quests-pois-and-characters).

A bot mod is an ordinary mod (see the [Packaging Guide](packaging-guide.md)):

```
mods/example_bots/
  mod.json                     depends on vanilla when it uses vanilla's items or sounds
  bots/sentry_drone.json       written by the editor's Export
  scripts/bots/sentry_drone.lua
  meshes/bots/sentry_drone.glb optional: its animated model
  raids/raids.json             optional: replaces the raid table
  editor/batches/bots.json     optional: the editor batch that makes bots/ (below)
```

Bots and the raid table are part of the content the host and clients compare when a client joins, so
everyone needs the same mods.

### Batches

A batch makes many bots at once from a template plus a few changed keys, which keeps families of bots in
step. Vanilla's twelve bots and raid table come from `mods/vanilla/editor/batches/bots.json`, and the
example mod's from its own `editor/batches/bots.json`:

```json
{
  "format_version": 1,
  "bots": [
    { "file": "bots/sentry_drone.json", "template": "ranged", "set": { "name": "Sentry Drone", "health": 120 } },
    { "file": "bots/sentry_drone_mk2.json", "base": "sentry_drone", "set": { "health": 200 } }
  ],
  "raids": { "levels": [ ] }
}
```

`template` starts from one of the editor's templates, `base` from an earlier bot of the batch (by id); `set`
replaces keys (a `behavior` replaces the whole tree). Run a batch with
`Godot --headless --path game -- --part-batch <batch.json>`, or `tools/author-vanilla.sh` for vanilla and
the examples.
