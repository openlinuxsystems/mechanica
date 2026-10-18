# Part Data Schema Reference

Every block, part and item in Mechanica is one JSON file in a mod's `parts/` folder (any subfolder),
plus the assets it points at. Vanilla uses exactly this format. The Part Editor writes these files for
you; this page is the reference for what each key means, so you can read, review or diff them.

- Keys are `snake_case`. Units are SI: kilograms, metres, seconds, newtons.
- The build grid is **0.25 m**. Sizes are given in whole grid cells.
- Files are JSON. Comments (`//`, `/* */`) and trailing commas are allowed when reading. The editor
  writes a canonical form: 2-space indents, keys in the order of this page, vectors on one line.
- Unknown keys are kept and round-trip, but the loader warns about them (with a "did you mean"
  suggestion for likely typos).
- Asset paths are relative to the mod root (`meshes/wheel.glb`). Another mod's file is
  `other_mod:meshes/wheel.glb`, and `other_mod` must be in your `mod.json` dependencies. Absolute
  paths, `..`, backslashes, URLs and `res://` paths are rejected.

## A complete example

```json
{
  "id": "bearing",
  "name": "Bearing",
  "description": "Hinge joint with an optional motor.",
  "category": "interactive",
  "mass": 3,
  "durability": 5,
  "friction": 0.4,
  "buoyancy": 0,
  "flammable": false,
  "sound_material": "metal",
  "size": [1, 1, 1],
  "mesh": "meshes/bearing.glb",
  "icon": "textures/bearing_icon.png",
  "visual": {
    "color": "#d0d0d8",
    "roughness": 0.4,
    "metallic": 0.8,
    "variants": [
      { "id": "black", "name": "Black", "color": "#202020" }
    ]
  },
  "collision": {
    "shape": "cylinder",
    "radius": 0.125,
    "height": 0.25
  },
  "sockets": [
    { "id": "base", "type": "structural", "position": [0, -0.125, 0], "normal": [0, -1, 0] },
    { "id": "rotor", "type": "rotational", "position": [0, 0.125, 0], "normal": [0, 1, 0] }
  ],
  "script": "scripts/bearing.lua",
  "sounds": {
    "start": "bearing_start",
    "loop": "bearing_spin_loop",
    "stop": "bearing_stop"
  },
  "properties": {
    "max_torque": 1000,
    "max_speed": 12.5
  }
}
```

## Top-level keys

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `id` | string | always | Lowercase snake_case, unique in the mod. The game-wide id is `mod_id:id`. Vanilla ids come from the canonical catalog. Never reuse an id for a different part: saved Creations store it. |
| `name` | string | always | Display name. |
| `description` | string | no | Tooltip text. |
| `category` | enum | always | See [Categories](#categories). |
| `mass` | number > 0 | placeable | Mass in kg. A Body's mass is the sum of its parts. |
| `centre_of_mass` | `[x, y, z]` m | no | Where the part's mass sits, from the part centre in the part's frame. Omitted means the part centre. Must lie within the footprint. A Body's centre of mass is its parts' mass-weighted mean. |
| `durability` | integer ≥ 1 | placeable | Toughness rating; the part's HP derives from it. A `loose` part may leave it out, and then nothing breaks it (a Stone Rod). |
| `friction` | number ≥ 0 | placeable | Surface friction coefficient. A Body uses its parts' mass-weighted mean. |
| `buoyancy` | number ≥ 0 | placeable | Buoyant force factor: force = buoyancy × displaced volume. |
| `flammable` | bool | placeable | Whether fire can ignite and spread through it ([Fire](survival.md#fire)). |
| `stack_size` | integer ≥ 1 | no | Most items per inventory slot. Omitted means 1. |
| `sound_material` | string | no | Material for place, break and impact sounds (`wood`, `metal`, `concrete`, `glass`, ...): an unbound `place` plays `place_<material>`. See [Sound Events](sound-events.md#part-sounds). |
| `size` | `[x, y, z]` cells | placeable | Footprint in 0.25 m grid cells, at least 1 on every axis. |
| `mesh` | path | no | `.glb` or `.gltf` model, centred on the footprint centre, +Y up, in metres. Without one, a grey shape matching the collision stands in. |
| `icon` | path | no | `.png` inventory icon. |
| `visual` | object | no | Colour, surface and colour variants. See [visual](#visual). |
| `collision` | object | no | Collision shape. Omitted means a box filling `size`. See [collision](#collision). |
| `sockets` | list | no | Attachment points. Placeable parts only. See [sockets](#sockets). |
| `ground_only` | bool | no | The part goes only flat on the ground, like soil: upright, resting on the world itself, never tipped onto a side, set on a Creation or welded to one. The Hammer's tip key does nothing for it. Placeable parts only. Default false. |
| `loose` | bool | no | A loose physics item lying in the world on its own, like a Stone Chunk or a Stone Rod: set down it is its own unanchored Creation, and nothing is built onto it or welded to it. Makes a `resource` part placeable. Not with `ground_only`. Default false. |
| `breaks_into` | object | no | Loose parts the part shatters into when it breaks (a chunk into rods, a log into Wood Rods): `count` pieces (1 to 16; with `count_max`, a seeded number from `count` to `count_max`), each picked from `parts[]` (`part`, `weight`, default 1) by weight, thrown out by `breaking` in `game/data/building.json`. Placeable parts only. |
| `sledge_damage` | number ≥ 0 | no | Damage one Sledgehammer swing does to the part, instead of `sledge.damage` in `game/data/building.json`. Placeable parts only. |
| `refine` | list | no | Items (`item`, `count` ≥ 1) a `loose` part refines into when a player holds Use on it with the Refine Tool for `refine_seconds` (`game/data/survival.json`). The part is used up. Loose parts only. |
| `script` | path | no | `.lua` behaviour script. A placed part runs it while it exists; an item (food, a weapon) runs it while a player holds and uses it. See the [Lua API Reference](lua-api.md). |
| `sounds` | object | no | Sound events: local sound name → sound id (`"start": "engine_start"`). The game plays `place`, `remove`, `break` and `impact`; other names are for the script. A bare id is the part's own mod's event, else vanilla's; `mod_id:sound_id` names another mod's. See [Sound Events](sound-events.md#part-sounds). |
| `inventory` | object | no | Item storage. Placeable parts only. See [inventory and crafting](#inventory-and-crafting). |
| `crafting` | object | no | Makes the part a crafting station. Needs `inventory`. See [inventory and crafting](#inventory-and-crafting). |
| `soil`, `respawn_point` | object, bool | no | Farming soil and respawn beds. Placeable parts only. See [survival](#survival). |
| `seed` | object | no | What a seed grows into. Items only. See [survival](#survival). |
| `aim` | object | no | A weapon aims while right click is held. Items only. See [survival](#survival). |
| `logic`, `settings`, `seat` | object, list, object | no | Logic bus links, player-set settings and seats. Placeable parts only. See [logic, settings and seats](#logic-settings-and-seats). |
| `light`, `animation` | object, object | no | A light the script switches, and a model the script turns. Placeable parts only. See [light and animation](#light-and-animation). |
| `menu`, `display` | object, object | no | What the part's menu shows and does, and what the part shows on itself. Placeable parts only. See [menu and display](#menu-and-display). |
| `properties` | object | no | Free-form numbers and settings that only the part's Lua script reads (torque, thrust, range). Key order is kept. |
| `upgrades` | list | no | Upgrade levels, in order. Placeable parts only. See [upgrades](#upgrades). |

"Placeable" means the categories that exist in the world on the build grid: `block`, `interactive`,
`container`, `station`, `light` and `decor`.

### Categories

| Value | What it is |
|-------|------------|
| `block` | A structural block or wedge. Attaches on every face. |
| `interactive` | A part with behaviour: bearings, engines, seats, logic, thrusters. |
| `container` | Holds items, fuel or charge. |
| `station` | A crafting or utility station. |
| `light` | A light. |
| `decor` | A decorative prop. |
| `resource` | An inventory-only crafting material. |
| `consumable` | An inventory item that is used up: food, seeds, fuel. |
| `tool` | A handheld tool. |
| `weapon` | A handheld weapon. |

## visual

| Key | Type | Meaning |
|-----|------|---------|
| `color` | `"#rrggbb"` | Base colour. It multiplies the mesh's own colour, so white keeps the model as it is. |
| `roughness` | 0–1 | 0 is mirror-smooth, 1 is matte. |
| `metallic` | 0–1 | 0 is plastic or wood, 1 is bare metal. |
| `opacity` | 0–1 | How solid it looks. Omitted means 1; lower values are see-through, like glass. Physics is unaffected. |
| `emission` | `"#rrggbb"` | Glow colour added on top of the lit surface. It does not light other things. |
| `texture` | path | `.png` albedo texture, replacing the mesh's own. Its alpha makes the part see-through where it is low: blended with `opacity` below 1, cut out (holes) otherwise. Vanilla box and wedge models map one repeat to each 0.25 m grid cell. |
| `emission_texture` | path | `.png` glow map: `emission` shines only where it is bright (black does not glow). Needs `emission`. |
| `paintable` | bool | Whether the Paint Tool can recolour it. Omitted means true. |
| `variants` | list | Named colour choices: `{ "id": "black", "name": "Black", "color": "#202020" }`. Ids are unique in the part. |

When `visual` is omitted the mesh's own glTF materials are used unchanged.

## collision

The collision shape is centred on the part centre plus `offset`. It does not have to match the mesh,
and a simpler shape is cheaper to simulate.

| `shape` | Keys it needs |
|---------|---------------|
| `box` | `size`: `[x, y, z]` metres |
| `sphere` | `radius` |
| `cylinder` | `radius`, `height` (along local Y) |
| `capsule` | `radius`, `height` (along local Y, including the caps, so at least 2 × radius) |
| `convex` | `mesh`: a `.glb`/`.gltf` whose points are wrapped in a convex hull |

Every shape also takes an optional `offset`: `[x, y, z]` metres from the part centre.

All rigidly attached parts of a Creation are simulated as one rigid Body with a compound shape made of
their collision shapes. Real physics joints only exist at bearings, pistons and suspension.

## sockets

A socket is an attachment point on the surface of the footprint.

| Key | Type | Meaning |
|-----|------|---------|
| `id` | string | Snake_case, unique in the part. Lua and the Connect Tool refer to sockets by id. |
| `type` | enum | `structural` (rigid), `rotational` (bearing hinge side), `linear` (piston slider side) or `spring` (suspension side). |
| `position` | `[x, y, z]` m | From the part centre. Must lie within the footprint, normally on a face. |
| `normal` | `[x, y, z]` | Outward unit direction, normally an axis such as `[0, 1, 0]`. |
| `joint` | object | `rotational`, `linear` and `spring` sockets only: the joint's axis, travel and spring. Required for `linear` and `spring`. See [joint](#joint). |

How parts attach when they are placed touching each other:

- **Blocks attach on every face, always**, whatever sockets they list.
- A part with **no sockets** also attaches on every face.
- Any other part attaches only on faces where one of its sockets sits. A socket sits on a face when its
  normal points out of that face and its position is on that face square (edges count, so one socket
  centred on a 2-cell side covers both cells).
- Structural meets structural: a rigid link, both parts join the same Body.
- A `rotational`, `linear` or `spring` socket meeting a structural face makes a joint of that kind
  between two Bodies. Two joint sockets never connect to each other.

The Part Editor's connection preview shows the result on every face: green for rigid, blue for a
joint, red where nothing attaches.

### joint

How the joint at a `rotational`, `linear` or `spring` socket moves. Positions are measured from where
the two sides were built on the grid: an angle in rad, right-handed about `axis`, or a distance in m
along it. The [Socket and Joint Guide](socket-joint-guide.md) walks through each kind.

| Key | Type | Meaning |
|-----|------|---------|
| `axis` | `[x, y, z]` | Grid direction the joint turns about or slides along, in the part's frame. Omitted means the socket's `normal`. |
| `min` | number | Lowest position, rad or m. A `rotational` joint with neither `min` nor `max` spins freely; with both, it is a limited hinge within ±π whose range includes 0. A `linear` joint defaults to 0. A `spring` needs it (0 or less). |
| `max` | number | Highest position, rad or m. Required for `linear` and `spring` (0 or more). Travel (`max` − `min`) is at most 16 m. |
| `stiffness` | number > 0 | `spring` only, required: spring rate in N/m. A load of `m` kg sags it by about `m × 9.81 / stiffness` m. |
| `damping` | number ≥ 0 | `spring` only: damping in N·s/m. Omitted means 0. |
| `shaft_radius` | number, m | `linear` and `spring` only: draws a rod of this radius, in the part's colour, from the socket to where the far side is now, so an extended piston shows its shaft. |

```json
{ "id": "mount", "type": "spring", "position": [0.125, 0, 0], "normal": [1, 0, 0],
  "joint": { "axis": [0, 1, 0], "min": -0.08, "max": 0.08, "stiffness": 25000, "damping": 1500 } }
```

## inventory and crafting

Inventories are a built-in primitive (players, chests, fuel tanks, station output), configured here
rather than in Lua.

| Key | Type | Meaning |
|-----|------|---------|
| `inventory.slots` | integer 1–256 | Number of item slots. Required when `inventory` is present. |
| `inventory.accepts` | list of item ids | Only these items go in (`["gasoline"]` for a gas container). Omitted means any item. |
| `crafting.speed` | number > 0 | Multiplier on recipe times: 2 crafts twice as fast. Default 1. |
| `crafting.queue_size` | integer 1–64 | Most orders waiting at once. Default 1. |
| `crafting.link_range` | number 0–8 (m) | Containers on the same Creation within this distance of the station (surface to surface) are its linked storage. Omitted or 0 links none. |

Each slot holds one stack of one item, up to that item's `stack_size`. A station runs every recipe that
lists its part id in `stations`: when an order is queued its inputs come from the station's linked
storage first, then the crafting player's inventory, and finished crafts land in linked storage first,
then the station's own inventory, where any player in reach can take them. A linked container can be
opened from the station's screen while the station is in reach. A container or station that holds items, or has orders queued, can't be picked up with the
Hammer. Its contents are saved with the Creation; if it is broken (Sledgehammer, weapons, a crash) they
fall out as a pile of items on the ground.

```json
"inventory": { "slots": 12 },
"crafting": { "speed": 1, "queue_size": 8, "link_range": 1 }
```

### upgrades

Each entry is one level above the part as written (level 0). A player raises a part one level at a
time with Upgrade (**U**) or from the station's screen; it is free in Creative and costs the entry's
items in Survival. A placed part's level is saved with the Creation; blueprints keep only level 0.

| Key | Type | Meaning |
|-----|------|---------|
| `cost` | list of `{ "item", "count" }` | What reaching this level costs in Survival. |
| `set` | object | Keys merged over the part at this level, on top of the levels below. Only `crafting`, `inventory`, `logic` and `properties` can change; objects merge key by key. A level can't shrink `inventory.slots` or remove `crafting`. |

At most 10 levels. The Part Editor lists them under Upgrades.

```json
"upgrades": [
  { "cost": [{ "item": "component_kit", "count": 2 }], "set": { "crafting": { "speed": 1.25 }, "inventory": { "slots": 12 } } },
  { "cost": [{ "item": "component_kit", "count": 4 }], "set": { "crafting": { "speed": 1.5 } } }
]
```

## survival

Farming and respawn beds in the survival loop. These are built-in primitives, configured here rather
than in Lua. Numbers are SI: health points, seconds, metres, N·s. See also [Survival](survival.md).

Food, weapons, seeds and the water bucket are item scripts (chunk 20b): a `script` plus its numbers
in `properties`. The vanilla scripts read these properties:

| Script | Property | Meaning |
|--------|----------|---------|
| `scripts/food.lua` | `health` | Health one eaten item gives back. |
| `scripts/potato_gun.lua` | `damage` | Damage per hit, or at the centre of a splash. |
| | `interval` (s) | Shortest time between shots. |
| | `range` (m) | How far a hitscan shot reaches. |
| | `speed` (m/s) | Lobs each shot (D14): a projectile thrown at this speed that arcs under gravity and lands (or bursts, with `radius`) where it hits. Replaces `range`. |
| | `fuse` (s) | A lobbed shot bursts after this long if it has not hit anything. Default and at most `max_projectile_lifetime`. |
| | `ammo` | Item used up per shot (it can be the weapon itself, like a Popper). Omitted means none. |
| | `pellets` | Shots per trigger pull, spread evenly round a cone (at most 64). Default 1. |
| | `spread` (°) | Half-angle of that cone, under 90. Default 0. |
| | `radius` (m) | Makes it a splash weapon: damage falls off to 0 at this distance from where it hits (or the end of its range). |
| | `impulse` (N·s) | Push given to the Body it hits (splash: at the centre). Default 0. |
| | `aim_spread` (°) | `spread` while the player aims (holds right click, with an `aim` section). Default `spread`. |
| | `magazine` | Shots between reloads; Reload (R) loads it again from the `ammo` carried. The hotbar slot and HUD show the rounds loaded and the ammo carried. Omitted means no reloading (each shot just uses `ammo`). |
| | `reload` (s) | How long a reload takes. Default 1. |
| `scripts/seed.lua` | (none) | Plants the seed in the soil aimed at. Its `seed` section (below) says what grows. |
| `scripts/watering.lua` | `seconds` (s) | Seconds of water one use adds to the soil aimed at. The item is used up. |
| `scripts/soil.lua` (a part) | `interval` (s) | How often the soil works out its crop's growth while its Creation is live. Growth is exact whatever it is (it comes from the world clock); it only bounds how late a new stage shows. The soil also grows, from the clock, the time its Creation was parked. E on a ripe crop (`ActivatePart`) harvests it. |

```json
"script": "scripts/potato_gun.lua",
"properties": { "damage": 20, "interval": 0.25, "range": 60, "ammo": "potato", "impulse": 20 }
```

| Key | Type | Meaning |
|-----|------|---------|
| `seed.crop` | item id | What the seed grows into. Required. |
| `seed.grow_time` | number > 0 (s) | Seconds of watered growth until ripe. Required. |
| `seed.yield` | integer ≥ 1 | Crop items a harvest gives. Default 1. |
| `seed.seeds` | integer ≥ 0 | Seeds a harvest gives back. Default 0. |

The soil's script (vanilla `scripts/soil.lua`) reads these through `world.item(seed, "seed")`, so another
soil script may treat them differently. A bare `crop` id is in the seed's own mod.
| `seed.value` | number ≥ 0 | How much a planted crop draws raids (chunk 21). Default 1. |
| `soil.capacity` | number > 0 (s) | Most seconds of water the soil holds. Required. |
| `soil.growth` | number > 0 | Growth speed multiplier for crops on it. Default 1. |
| `respawn_point` | bool | Players can sleep in it (E) to respawn there. |
| `aim.zoom` | number 0.2 to 1 | While right click is held the field of view is multiplied by this (a slight zoom) and the crosshair tightens. Default 0.85. The script sees `target.aiming`. |
| `aim.fire_on_release` | bool | Fire when right click is let go, as well as on left click while aiming. Default false. |

```json
"seed": { "crop": "carrot", "grow_time": 240, "yield": 2, "seeds": 1 }
```

## logic, settings and seats

The logic and power bus (chunk 10). The game moves the signals; what a part does with them is its
script's job (D2). See the [Logic and Power Tutorial](logic-power-tutorial.md).

| Key | Type | Meaning |
|-----|------|---------|
| `logic.inputs` | integer 0 to 64 | Most links that may end at the part. Default 0: it takes none. |
| `logic.outputs` | integer 0 to 64 | Most links that may start at the part. Default 0: it sends none. |
| `logic.use` | string | What Use (E) says on the part (`"toggle"`, `"press"`); E calls the script's `on_activate`. Needs a `script`. Omitted means players cannot use it by hand. |
| `logic.kind` | `logic`, `power`, `bearing` or `structural` | The part's connection node with the Connect Tool (UI spec §12), which also names the links that start at it: blue for `logic` (switches, gates, seats, lights), cyan for `power` (engines, thrusters, rotors), white with a blue ring for `bearing` (bearings, pistons), light gray for `structural`. Default `logic`. |
| `logic.accepts` | list of kinds | The kinds of part whose links may end here, such as `["power"]` for a wheel bearing only engines turn. The Connect Tool grays out other targets and the host refuses them. Default: any kind but `structural`. |
| `logic.reverse` | setting id | One of the part's `toggle` settings that reverses which way it turns. Right click on its node with the Connect Tool flips it, a curved arrow by the node shows it, and scripts that drive the part read it as `joint.reversed`. Vanilla bearings use `"reverse"`. |
| `seat` | object | Makes the part a seat a player sits in with E. Its script reads them with `part.occupant()` and `part.occupant_input()`. |
| `seat.offset` | [x, y, z] (m) | Where the sitter's feet go, from the part's centre, in the part's frame. Inside the part's footprint. Default [0, 0, 0]. |
| `settings` | list | Values a player sets on each placed part in its menu (E on the part, or R with the Connect Tool), in display order. The script reads them with `part.setting(id)`. |

Each setting:

| Key | Type | Meaning |
|-----|------|---------|
| `id` | snake_case | Unique within the part. Required. |
| `name` | string | Label in the settings panel. Default: the id. |
| `type` | `number`, `choice`, `toggle`, `number_list` or `sequence` | Required. A number shows a slider with a field, a choice a drop-down, a toggle a check box, and a sequence the frame editor. |
| `min`, `max` | number | Range of a number, and of each entry of a number list or target of a sequence. Required for all three, with `min` ≤ `max`. |
| `step` | number > 0 | Step of the settings panel's field. |
| `options` | list of snake_case | The choices of a `choice`, distinct. Required for a choice. |
| `icons` | object | A choice's pictures: option → `.png`. The menu shows the picked one by the drop-down, and a cycle card shows it large. |
| `presets` | list of `{name, value}` | Buttons by a number's slider that set it to `value` (an engine's Low, Medium and High). Each value fits the setting. |
| `group` | string | Toggles with the same group sit side by side under that heading (a sensor's Detects: Players, Bots, Blocks, Creations). |
| `hide_when_linked` | bool | While anything is linked into the part the setting gives way to "Driven by what is linked to it." (an engine's On toggle). |
| `max_count` | integer 1 to 64 | Most entries of a `number_list`, or frames of a `sequence`. Required for both. |
| `max_speed` | number > 0 | A sequence's fastest joint speed, in rad/s for bearings and m/s for pistons. Required for a sequence. |
| `preview` | snake_case | A sequence's Play button sends the frames being edited to the script's `on_command` under this name, so it can run them before they are applied. |
| `default` | value | The value a placed part starts with. It must fit the setting. Default: `min` (or 0), the first option, false, or an empty list. |

```json
"script": "scripts/logic_gate.lua",
"logic": { "inputs": 10, "outputs": 10 },
"settings": [
  { "id": "mode", "name": "Mode", "type": "choice", "options": ["and", "or", "xor", "nand", "nor", "xnor"], "default": "and" }
]
```

A `sequence` value is a list of frames. Each frame lists the bearings and pistons the part is linked to,
`[{ "part": 4, "target": 45, "speed": 1.5 }]`: a bearing's target in degrees and a piston's in m of
extension, with the speed it moves there. A frame may also be a single number, an angle for every linked
bearing (how older controllers saved them).

A saved creation keeps the settings a player changed on each part (`"settings": { "mode": "or" }`,
only values that differ from the default) and its links as a top-level list:
`"links": [{ "from": 1, "to": 4 }]`. Loading a save checks both against the part data.

The vanilla scripts read these properties:

| Script | Property | Meaning |
|--------|----------|---------|
| `scripts/seat.lua` | `steer_angle` (°) | How far A and D turn the upright bearings the seat feeds. |
| | `steer_speed` (rad/s), `steer_torque` (N·m) | How fast and how hard they turn. |
| `scripts/engine.lua` | `speed` (rad/s) | Wheel bearing speed at full throttle at full power (the `gear` setting, shown as Power 1 to 5, gives a fifth each). With nothing linked in, the `on` setting runs it at full throttle. |
| | `torque`, `brake` (N·m) | Most torque while driving, and while holding the wheels with no throttle. |
| | `fuel`, `fuel_time` (s) | Item burned from the creation's containers, and seconds of full throttle per item. Creative needs none. |
| | `loop_parameter` | Parameter of the `loop` sound that follows the throttle (`load`, `speed`). |
| `scripts/sensor.lua` | `interval` | Steps between looks (its `range` and what it detects are settings). |
| | `cone` (°) | How far off its face its ring of rays looks, so it sees a narrow cone. Its menu preview uses the same angle. |
| `scripts/timer.lua` | (the `mode` setting) | `once` passes its input on after the delay; `repeat` blinks its output every delay while the input is on. |
| `scripts/button.lua` | `pulse` (s) | How long it stays on. |
| `scripts/controller.lua` | `speed` (rad/s) | How fast bearings move to single-angle frames (a frame's joints carry their own speeds). |
| | `torque` (N·m), `force` (N) | Most torque on its bearings and force on its pistons. |

### Aero parts

The aero scripts (see the [Aero and Propulsion Tutorial](aero-tutorial.md)) share the `control`
setting (`link`, `w_s`, `a_d` or `space_shift`) and the `invert` toggle.

| Script | Property | Meaning |
|--------|----------|---------|
| `scripts/thruster.lua` | `thrust` (N) | Push toward its base at full power. |
| | `lever_rate` (1/s) | How fast Space and Shift move the `space_shift` lever. |
| | `fuel`, `fuel_time` (s) | Item burned from the creation's containers, and seconds of full power per item. Creative needs none. |
| `scripts/wing.lua` | `area` (m²), `air_density` (kg/m³) | Lifting area and the air. |
| | `camber_lift`, `lift_slope` (per rad) | Lift coefficient with the air along the surface, and its growth with the angle of attack. |
| | `max_lift`, `stall_lift` | Lift coefficient where it stalls, and that of a stalled plate at 45°. |
| | `drag`, `induced_drag`, `stall_drag` | Drag coefficient at no lift, extra drag per lift coefficient squared, and drag square to the air. |
| | `whoosh_speed` (m/s) | Air speed where the `whoosh` loop is loudest. |
| | `max_deflection` (°), `lever_rate` (1/s) | Control surfaces: how far the command tilts the air, and the lever's speed. |
| `scripts/rotor.lua` | `thrust` (N), `counter_torque` (N·m) | Pull and twist at full speed. The `spin` setting picks the twist's direction. |
| | `spool_rate` (1/s), `lever_rate` (1/s) | How fast its speed follows the command, and the lever's speed. |
| | `inflow_speed` (m/s), `max_inflow_gain` | Climb speed along its axis where it pulls nothing, and the most extra pull while sinking. |
| `scripts/hover_jet.lua` | `thrust` (N), `lever_rate` (1/s) | Push toward its base at full power, and the lever's speed. |
| | `level_torque` (N·m/rad), `level_damping` (N·m·s/rad), `max_level_torque` (N·m) | Levelling while the `stabilize` setting is on. |

### Lights and utility parts

| Script | Property | Meaning |
|--------|----------|---------|
| `scripts/light.lua` | `blink` (s), `duty` | Blink period (0 or missing: steady) and the share of it spent lit. With no inputs linked it shines until E turns it off; linked, it shines while its input is above 0. |
| `scripts/radio.lua` | (the `station` setting) | Plays the `station_N` sound loop; E turns it on and off. |
| `scripts/beacon.lua` | `ping`, `flash` (s) | Seconds between pings, and how long each ping lights it fully. |
| `scripts/horn.lua` | `honk` (s) | How long E sounds it; a linked input sounds it while above 0. |
| `scripts/cutter.lua` | `edge`, `reach` (m), `interval` (s), `damage`, `spin` (rad/s) | Saw and drill: the rays it cuts along (part frame), how far, how often, how hard, and its model's spin while running. |
| `scripts/water_cannon.lua` | `muzzle`, `range` (m), `water` (s), `impulse` (N·s), `interval` (s) | Fires on a rising input or E: waters soil it hits, else pushes what it hits. |
| `scripts/mounted_gun.lua` | `ammo`, `muzzle`, `range` (m), `damage`, `impulse` (N·s), `interval` (s) | Hitscan shots taking `ammo` from the creation's containers (none in Creative). |

## light and animation

`light` gives a part a light its script switches with `part.set_light`. Every peer draws it from the
host's level; it is dark until set.

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `kind` | `omni` or `spot` | no | Default `omni`. |
| `color` | `"#rrggbb"` | no | Default white. |
| `energy` | number ≥ 0 | no | Brightness at level 1. |
| `range` | number > 0, m | yes | How far it reaches. |
| `angle` | 0 < number < 90, ° | no | A spot's cone half-angle. |
| `offset` | `[x, y, z]` m | no | Where the light sits, from the part centre in the part's frame; within the footprint. |
| `direction` | `[x, y, z]` | no | Which way a spot points in the part's frame, default `[0, 0, -1]`. Not zero. |

`animation` lets the script turn the part's model with `part.animate` (rotor blades, control surfaces,
saw blades). It turns only the drawn model: collision and the Body stay put.

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `axis` | `[x, y, z]` | no | What it turns about in the part's frame, default `[0, 1, 0]`. Not zero. |
| `pivot` | `[x, y, z]` m | no | A point on the axis, from the part centre; within the footprint. Default the centre. |

## menu and display

`menu` shapes the part's menu (UI spec §12), the window E opens on a part with settings or readouts.
Every key is optional.

| Key | Type | Meaning |
|-----|------|---------|
| `use` | `open` or `cycle` | What E on the part does. `open` (default) opens the menu. `cycle` switches the `cycle` setting to its next option and shows a card with its icon and name for two seconds (a logic gate); R with the Connect Tool still opens the menu. |
| `cycle` | setting id | The `choice` setting `cycle` steps through. Required for `cycle`. |
| `readouts` | list | Values the script shows with `part.display`, at the top of the menu. |
| `signals` | bool | Show a line with the part's input and output on the logic bus. |
| `preview` | object | A see-through shape drawn on the part while its menu is open, sized by a setting (a sensor's cone). |
| `apply` | bool | Changes wait for an Apply button (or closing the menu) instead of going to the host at once (a controller's frames). |

Each readout:

| Key | Type | Meaning |
|-----|------|---------|
| `id` | snake_case | The id the script passes to `part.display`. Required, unique. |
| `name` | string | Label. Default: the id. |
| `kind` | `bar`, `number` or `text` | A cyan bar from 0 to `max`, a number, or the script's text. Default `number`. |
| `max` | number > 0 | A bar's full value. Default 1. |
| `unit` | string | Shown after a number (`rpm`, `N·m`). |
| `decimals` | integer 0 to 6 | Digits after the point. Default 0. |

`preview` has `shape` (`ray`, `cone` or `sphere`, default `ray`), `range` (the number setting that sizes it, in
m, required), `axis` (which way a ray or cone points in the part's frame, default `[0, 1, 0]`) and
`angle` (a cone's half-angle in °, default 10).

`display` is what the part shows on itself in the world, from values its script sets with
`part.display` (or that a part linked to it sets with `creation.display`). Every peer draws it from the
host's values.

| Key | Type | Meaning |
|-----|------|---------|
| `status` | readout id | A small dot above the part while it has links and the Connect Tool is away: green while the value is not 0, gray while it is. |
| `bar` | `{readout, offset, axis, length}` | A thin cyan bar along the part that fills from 0 to 1 with the value, shown while the Connect Tool is out (a piston's extension). `axis` defaults to `[0, 1, 0]`, `length` to 0.25 m. |
| `glow` | `{readout, offset, direction, length}` | A faint blue particle glow whose strength follows the value from 0 to 1 (a thruster's exhaust). `direction` defaults to `[0, 1, 0]`, `length` to 0.5 m. |
| `highlight` | `{readout, axis, offset, radius}` | A ring of cyan dashes turning about `axis` while the value is not 0, its sign giving the way round (a bearing an engine drives). `radius` defaults to 0.12 m. |
| `press` | `{readout, axis, depth}` | Pushes the model in by `depth` m (default 0.02, at most 0.1) along `axis` (default `[0, -1, 0]`) while the value is not 0 (a switch). |

Offsets are from the part centre in the part's frame, within its footprint; axes and directions are not zero.

```json
"settings": [{ "id": "range", "name": "Range (m)", "type": "number", "min": 1, "max": 20, "step": 0.25, "default": 4 }],
"menu": {
  "signals": true,
  "readouts": [{ "id": "fuel", "name": "Fuel", "kind": "bar" }],
  "preview": { "shape": "cone", "range": "range", "angle": 8 }
},
"display": { "status": "running" }
```

## Recipes

Recipes live in their own files, `recipes/**/*.json`, one recipe per file. The editor's recipe panel
writes them.

```json
{
  "id": "chest",
  "stations": ["fabricator", "mini_fabricator"],
  "inputs": [
    { "item": "scrap_wood", "count": 4 },
    { "item": "scrap_metal", "count": 2 }
  ],
  "outputs": [
    { "item": "chest", "count": 1 }
  ],
  "time": 4
}
```

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `id` | string | always | Lowercase snake_case, unique in the mod; the game-wide id is `mod_id:id`. Usually the main output's id. |
| `name` | string | no | Display name. Omitted means the first output's name. |
| `stations` | list of part ids | always | Parts with `crafting` that can run it. |
| `inputs` | list of `{ item, count }` | always | Items used up. Each item once; add the counts together. |
| `outputs` | list of `{ item, count }` | always | Items made. |
| `time` | number ≥ 0 | always | Seconds for one craft at speed 1. |

Item and station ids are `id` (vanilla) or `mod_id:id`, and must name parts that are loaded. A recipe
with errors is left out and the rest still load. Host and clients compare their recipes when a client
joins.

## The editor source file

The Part Editor keeps what the part file cannot hold, mainly the model pieces it bakes into the mesh,
in `editor/<same path as the part file>`. For `parts/blocks/oak_block.json` that is
`editor/parts/blocks/oak_block.json`. The game never reads these files; ship them if you want others to
be able to reopen your parts in the editor.

```json
{
  "format_version": 1,
  "model": [
    { "kind": "box", "size": [0.25, 0.25, 0.25] },
    { "kind": "cylinder", "operation": "subtract", "radius": 0.05, "height": 0.3, "rotation": [90, 0, 0] }
  ]
}
```

| Key | Meaning |
|-----|---------|
| `kind` | `mesh` (an imported glTF, `mesh` key), `box` (`size`), `wedge` (`size`; full height at -Z sloping to nothing at +Z), `cylinder` (`radius`, `height`) or `sphere` (`radius`). |
| `operation` | `union` (default), `subtract` or `intersect`, applied against all the pieces above it. The first piece must be a union. |
| `position` | Metres from the part centre. |
| `rotation` | Degrees about X, Y and Z, applied in Y, X, Z order. |
| `scale` | Uniform scale, default 1. |

On export, a model with a single untransformed `mesh` piece makes the part point straight at that
file. Anything else is baked into one glTF file at `meshes/<path of the part file inside parts/>.glb`
(for `parts/blocks/oak_block.json`, `meshes/blocks/oak_block.glb`).

## Overrides

A mod can replace another mod's part by shipping a full part file at
`overrides/<other_mod>/<any name>.json` that keeps the original `id`. The other mod must be a
dependency. A recipe is replaced the same way, with a full recipe file at
`overrides/<other_mod>/recipes/<any name>.json` that keeps the original `id`. See the
[Packaging Guide](packaging-guide.md).

## Error codes

Every problem the loader or editor reports has a stable code, a file, a JSON path and a message, for
example `error MD0201: mods/my_mod/parts/a.json(4) $.mass: mass must be greater than 0.`

| Code | Meaning |
|------|---------|
| MD0001–MD0003 | File unreadable, invalid JSON, unknown key (warning). |
| MD01xx | `mod.json`, dependencies, load order and conflicts. |
| MD0201 | A part value is missing or invalid. |
| MD0202 | Two files in a mod define the same part id. |
| MD0203–MD0205 | Override problems. |
| MD0301–MD0303 | Asset path invalid, file missing, or another mod used without depending on it. |
| MD04xx | Saved Creation problems. |
| MD0501 | Editor source problem (a model piece). |
| MD0502 | Export could not write to the mod folder. |
| MD06xx | World generation and POI prefabs. See [World Generation](world-generation.md). |
| MD0701 | A recipe value is missing or invalid. |
| MD0702 | Two recipe files in a mod use the same id. |
| MD0703 | A recipe override names a recipe the other mod does not have. |
| MD0704 | Two mods override the same recipe (warning). |
| MD08xx | Sound events. See [Sound Events](sound-events.md#error-codes). |
