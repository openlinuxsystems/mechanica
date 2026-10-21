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
| `tab` | enum | no | The inventory tab the item shows under (`tools`, `blocks`, `interactive`, `parts`, `consumable`) when its category's is wrong for it. Omitted uses the category's tab. See [Categories](#categories). |
| `mass` | number > 0 | placeable | Mass in kg. A Body's mass is the sum of its parts. |
| `centre_of_mass` | `[x, y, z]` m | no | Where the part's mass sits, from the part centre in the part's frame. Omitted means the part centre. Must lie within the footprint. A Body's centre of mass is its parts' mass-weighted mean. |
| `durability` | integer ≥ 1 | placeable | Toughness rating; the part's HP derives from it. A `loose` part may leave it out, and then nothing breaks it (a Stone Rod). |
| `friction` | number ≥ 0 | placeable | Surface friction coefficient. A Body uses its parts' mass-weighted mean. |
| `buoyancy` | number ≥ 0 | placeable | Buoyant force factor: force = buoyancy × displaced volume. |
| `flammable` | bool | placeable | Whether fire can ignite and spread through it ([Fire](survival.md#fire)). |
| `stack_size` | integer ≥ 1 | no | Most items per inventory slot. Omitted means 1. |
| `sound_material` | string | no | Material for place, break and impact sounds (`wood`, `metal`, `concrete`, `glass`, ...): an unbound `place` plays `place_<material>`. See [Sound Events](sound-events.md#part-sounds). |
| `size` | `[x, y, z]` cells | placeable | Footprint in 0.25 m grid cells, at least 1 on every axis. |
| `mesh` | path | no | `.glb` or `.gltf` model, centred on the footprint centre, +Y up, in metres. Its `COLOR_0` vertex colours, when it has them, multiply each material's base colour (as the glTF spec says). Without one, a grey shape matching the collision stands in. |
| `icon` | path | no | `.png` inventory icon. |
| `visual` | object | no | Colour, surface and colour variants. See [visual](#visual). |
| `collision` | object | no | Collision shape. Omitted means a box filling `size`. See [collision](#collision). |
| `sockets` | list | no | Attachment points. Placeable parts only. See [sockets](#sockets). |
| `ground_only` | bool | no | The part goes only flat on the ground, like soil: upright, resting on the world itself, never tipped onto a side, set on a Creation or welded to one. The tip key does nothing for it. Placeable parts only. Default false. |
| `max_slope` | number 0–90 (degrees) | no | With `ground_only`: the steepest ground the part may be placed on, measured across its footprint (soil: 17). Steeper ground is refused with "The ground is too steep". Default: any slope. |
| `ground_part` | id | no | An item that sets another part down when aimed at the terrain: a Soil Bag makes a soil mound. Aimed anywhere else, or with Force build held, the item places itself. A bare id is in this part's mod. Not with `placed_from`. |
| `placed_from` | id | no | The item this part is placed from and picked up as (soil's `soil_bag`): placing it uses one up, removing it gives one back, and the part itself is never an item (it is hidden from the hotbar and Creative). A bare id is in this part's mod. Not with `ground_part`. |
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
| `aim` | object | no | A weapon aims while right click is held. Items only. Without it, right click with the item in hand picks parts up, like an empty hand (the default for every item). See [survival](#survival). |
| `held` | object | no | The model a player holds in the right hand while the item is selected, seen by every player. Items only. See [held](#held). |
| `logic`, `settings`, `seat` | object, list, object | no | Logic bus links, player-set settings and seats. Placeable parts only. See [logic, settings and seats](#logic-settings-and-seats). |
| `light`, `animation` | object, object | no | A light the script switches, and a model the script turns. Placeable parts only. See [light and animation](#light-and-animation). |
| `menu`, `display` | object, object | no | What the part's menu shows and does, and what the part shows on itself. Placeable parts only. See [menu and display](#menu-and-display). |
| `properties` | object | no | Free-form numbers and settings that only the part's Lua script reads (torque, thrust, range). Key order is kept. |
| `upgrades` | list | no | Upgrade levels, in order. Placeable parts only. See [upgrades](#upgrades). |

"Placeable" means the categories that exist in the world on the build grid: `block`, `interactive`,
`container`, `station`, `light` and `decor`.

### Categories

| Value | What it is | Inventory tab |
|-------|------------|---------------|
| `block` | A structural block or wedge. Attaches on every face. | Blocks |
| `interactive` | A part with behaviour: bearings, engines, seats, logic, thrusters. | Interactive |
| `container` | Holds items, fuel or charge. | Interactive |
| `station` | A crafting or utility station. | Interactive |
| `light` | A light. | Interactive |
| `decor` | A decorative prop. | Parts |
| `resource` | An inventory-only crafting material. | Parts |
| `consumable` | An inventory item that is used up: food, seeds, fuel. | Consumable |
| `tool` | A handheld tool. | Tools |
| `weapon` | A handheld weapon. | Tools |

The inventory and the stations sort items into Scrap Mechanic's tabs: Tools, Blocks, Interactive, Parts and
Consumable. An item goes under its category's tab unless its `tab` key names another: Gasoline is a `resource`
(you can set a can down) that you use up as fuel, so it has `"tab": "consumable"`. Every vanilla item has a tab, and a
test fails if one ends up under none.

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
| `texture_span` | 1–16 | Grid cells one repeat of `texture` covers. Omitted means 1 (each cell face shows the whole texture). Above 1 the texture is laid over the creation's grid instead of each part's faces, so a wall of these blocks shows one larger pattern that runs on from block to block with no seam (Scrap Metal, Scrap Wood and Scrap Stone use 8: one repeat per 2 m). The texture must tile. Parts in the hand, the build ghost and icons show one repeat per cell. Needs `texture`. |
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

A `sphere` or `cylinder` can also take `climb`: the height in metres, above the shape's lowest point, of a step it
drives up (vanilla wheels: `0.25`, one block). A round wheel meets a block's top edge almost side on, so without it a
wheel the engine does not turn stalls against a one-block step. With it, a rolling wheel that sees a step ahead no
higher than its climb, with a level top, on something that stays put (terrain, structures, builds anchored to the
ground), lays an invisible 15° ramp against the step's face and rolls up it; the ramp goes once the wheel is up. A
bevelled or rounded top edge (a structure's floor) counts as long as its top is level a little way in. A wheel already
close to the step gets a steeper ramp (up to 35°), and one closer still (parked against it) gets a ramp sunk under it
that rises while the Creation's motors drive it at the step, so a car parked against a step stays put and drives up it
from there. The ramps are physics only: players, bots, tools and Lua raycasts and overlaps never meet them. A cylinder climbs only
while its axis lies roughly level (a wheel, not a coin lying flat), and `climb` must be below the radius.

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

A mesh node in the part's model named after one of its joint sockets (the bearing's `rotor`) is drawn on the
Body joined at that socket, so it turns or slides with what is attached there. Without a joined part it stays put.
A mesh node named after the socket plus `Flex` (the sport suspension's `mountFlex`: its coil and damper) stays on the
part but is skewed each frame along the joint's axis, so its end at the socket follows the joint and its far end stays.
A mesh node named after a `linear` socket plus `Rod` (the piston's `headRod`: one short rod segment, modelled
retracted inside the part with its outer end just under the head) is repeated along the joint's axis as it slides out:
copies stacked end to end hang from the moving head back into the part, so the rod reveals segment by segment rather than
stretching. Like a shaft, the rod has no collision.

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
| `inventory.carry_contents` | boolean | `true` lets a player pick the part up while it holds items (a gas container full of fuel): they go into the player's inventory with it, and what doesn't fit drops where it stood. A Lift may also carry a Creation whose only stored items are in such parts. Default `false`. |
| `crafting.speed` | number > 0 | Multiplier on recipe times: 2 crafts twice as fast. Default 1. |
| `crafting.queue_size` | integer 1–64 | Most orders waiting at once. Default 1. |
| `crafting.link_range` | number 0–8 (m) | Containers on the same Creation within this distance of the station (surface to surface) are its linked storage. Omitted or 0 links none. |
| `crafting.clips` | `{start, work, complete}` | Model clips that follow the station's work. The host writes them to the text readout `station`, so the part needs `display.clips` with `"readout": "station"`: `start` then `work` (name it `…_loop`) when it begins working, `complete` when its queue is done or its output is full. Each is optional. |

Each slot holds one stack of one item, up to that item's `stack_size`. A station runs every recipe that
lists its part id in `stations`: when an order is queued its inputs come from the station's linked
storage first, then the crafting player's inventory, and finished crafts land in linked storage first,
then the station's own inventory, where any player in reach can take them. A linked container can be
opened from the station's screen while the station is in reach. A container or station that holds items, or has orders queued, can't be picked up
(right click), unless it has `carry_contents`. Its contents are saved with the Creation; if it is broken (Sledgehammer, weapons, fire) they
fall out as a pile of items on the ground.

```json
"inventory": { "slots": 12 },
"crafting": { "speed": 1, "queue_size": 8, "link_range": 1 }
```

### upgrades

Each entry is one level above the part as written (level 0). A player raises a part one level at a
time with Upgrade (**U**) or from the station's screen; it is free in Creative and costs the entry's
items in Survival. A placed part's level is saved with the Creation; blueprints keep only level 0.
Picking up an upgraded part gives its own item, `mod_id:part_id@level` (`vanilla:gas_engine@2` is the Gas Engine
two upgrades up): it shows that level's name and icon, stacks only with the same level, and places the part at that
level again. Only the part as written (level 0) has a recipe. A part's script keeps running through an upgrade, its
`state` intact, and reads the new level's `properties` from the next step.

| Key | Type | Meaning |
|-----|------|---------|
| `cost` | list of `{ "item", "count" }` | What reaching this level costs in Survival. |
| `set` | object | Keys merged over the part at this level, on top of the levels below. Only `name`, `description`, `durability`, `mesh`, `icon`, `sockets`, `settings`, `display`, `crafting`, `inventory`, `logic` and `properties` can change; objects merge key by key and lists are replaced. A level can't shrink `inventory.slots` or remove `crafting`, and its `sockets` must list the same sockets (ids, types, positions and normals) with only their `joint` changed, such as a suspension's stiffer spring. |

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
| `seed.seeds` | integer ≥ 0 | Seeds a harvest gives back (the least, with `seeds_max`). Default 0. |
| `seed.seeds_max` | integer ≥ `seeds` | Most seeds a harvest gives back: each harvest rolls from `seeds` to `seeds_max` (vanilla tomato: 1 to 2). Default: exactly `seeds`. |
| `seed.value` | number ≥ 0 | How much a planted crop draws raids (chunk 21). Default 1. |
| `seed.plant` | mesh path | The growing plant's model on the soil (see [Crop plant models](#crop-plant-models)). Default: a plain green shoot with the crop's colour on top once ripe. |
| `soil.capacity` | number > 0 (s) | Most seconds of water the soil holds. Required. |
| `soil.growth` | number > 0 | Growth speed multiplier for crops on it. Default 1. |
| `soil.dry_time` | number > 0 (s) | Seconds full soil with nothing planted takes to dry out completely (vanilla 1440, about a game day). Read by `soil.lua`; omitted, empty soil keeps its water. |
| `soil.wet_level` | number in (0, 1] | Share of `capacity` from which the soil looks fully wet. Below it, watered soil looks lightly watered, or drying once it has been fully wet. Default 0.5. |
| `respawn_point` | bool | Players can sleep in it (E) to respawn there. |
| `aim.zoom` | number 0.2 to 1 | While right click is held the field of view is multiplied by this (a slight zoom) and the crosshair tightens. Default 0.85. The script sees `target.aiming`. |
| `aim.fire_on_release` | bool | Fire when right click is let go, as well as on left click while aiming. Default false. |

```json
"seed": { "crop": "tomato", "grow_time": 360, "yield": 2, "seeds": 1, "seeds_max": 2, "plant": "meshes/crops/tomato_plant.glb" }
```

The soil's script (vanilla `scripts/soil.lua`) reads these through `world.item(seed, "seed")`, so another
soil script may treat them differently. A bare `crop` id is in the seed's own mod.

### Crop plant models

A `seed.plant` model is one glTF scene that shows the crop at every stage. The game plays three of its clips, each on its own
player, so they must move different properties (a node may take its position from one and its rotation from another):

| Clip | How it plays |
|---|---|
| `grow` | Scrubbed, never played: second *n* of the clip is soil stage *n* (0 just planted to 4 ripe), so it is 4 s long. Between stages the plant eases through the clip over about 2 s, so it grows rather than pops. Show the ripe fruit by scale keys on constant interpolation. |
| `wilt` | Scrubbed from 0 (fresh) to its end (fully wilted). The plant wilts while its soil is dry and picks up again once watered. |
| `sway` | Looped, each plant starting at a different point so a field does not move in step. |

A harvest sinks the model into the soil in a puff of dirt. Vanilla's tomato (`tools/food/plant_rig.py`, built by
`tools/food/build_tomato.py`) is the reference: the scrap-wood stake and the crop's tag stand in the soil from planting, and
the tag carries the same label as the seed packet. Every crop's seed packet is the same kraft pouch; only its label (the crop's
icon over a band in the crop's colour) changes. The carrot and the potato (`tools/food/carrot.py`, `potato.py` on the shared
`tools/food/crop_rig.py`, built by `tools/food/build_roots.py`) follow it: the carrot's shoulders push up out of the soil as it
ripens, and the potato flowers and then shows its tubers at the soil as its lower leaves yellow.

## held

Without `held`, an item in hand shows nothing (vanilla's tools have their own rig props). With it, the player holding the
item carries a model in the right hand, and every other player sees it too.

| Key | Type | Meaning |
|---|---|---|
| `held.mesh` | mesh path | The model (`.glb`/`.gltf`, relative to the mod; `other_mod:path` for another mod's). Default: the item's `mesh`. One of the two is required. |
| `held.offset` | [x, y, z] m, each -0.5 to 0.5 | Moves the model from the palm, in the hand's frame. Default [0, 0, 0]. |
| `held.rotation` | [x, y, z] degrees | Turns the model in the hand (X, then Y, then Z). Default [0, 0, 0]. |
| `held.scale` | number, more than 0 to 4 | Scales the model. Default 1. |
| `held.juice` | `#rrggbb` | Food: the colour of the juice each bite sprays. Default: the item's `visual.color`. |

```json
"held": { "mesh": "meshes/torch_held.glb", "offset": [0, 0.05, 0], "rotation": [0, 90, 0], "scale": 0.8 }
```

### Food in hand

Food (the `consumable` category, unless `item_poses` in `game/data/character.json` says otherwise) rests on the open palm
(`hold_food`) and is eaten with the shared `eat` clip: it is raised to the mouth and bitten twice, then it is gone, and the hand
comes back down. The clip plays once the host has taken the food (not at full health), and no more is eaten until it ends.
A food's held model shows its bites when it has children named `eat_0` (whole), `eat_1` (one bite taken) and so on: the
hand shows the one the clip has reached (`eat.bites` in `character.json`), and nothing once the food is gone (`eat.gone`).
Without them the whole model shows until it is gone. Each bite sprays juice (`held.juice`) and plays the item's `eat` sound.
In first person the arms use `hold_food_fp` and `eat_fp`, posed for the first-person camera: the palm in the lower right of
the view, brought up under the camera for each bite. A long food lies across the palm with the end that is bitten toward the
little finger (vanilla's carrot); its bitten versions take that end off.

```json
"held": { "mesh": "meshes/items/tomato_held.glb", "offset": [-0.0373, 0.0413, 0.0015], "rotation": [-3.37, -180, -45], "juice": "#c83a2a" }
```

## logic, settings and seats

The logic and power bus (chunk 10). The game moves the signals; what a part does with them is its
script's job (D2). See the [Logic and Power Tutorial](logic-power-tutorial.md).

| Key | Type | Meaning |
|-----|------|---------|
| `logic.inputs` | integer 0 to 64 | Most links that may end at the part. Default 0: it takes none. |
| `logic.outputs` | integer 0 to 64 | Most links that may start at the part. Default 0: it sends none. |
| `logic.use` | string | What Use (E) says on the part (`"toggle"`, `"press"`); E calls the script's `on_activate`. Needs a `script`. Omitted means players cannot use it by hand. |
| `logic.hold` | bool | `true` makes a use a hold (the button): the part stays used while the player holds E on it, or its key on their seat's bar, and the script's `on_release(peer)` runs when they let go (`ReleasePart`), stand up from that seat, die or leave. Needs `logic.use`. Omitted means a use is one press (a switch). |
| `logic.kind` | `logic`, `power`, `bearing` or `structural` | The part's connection node with the Connect Tool (UI spec §12), which also names the links that start at it: blue for `logic` (switches, gates, seats, lights), cyan for `power` (engines, thrusters, rotors), white with a blue ring for `bearing` (bearings, pistons), light gray for `structural`. Default `logic`. |
| `logic.accepts` | list of kinds | The kinds of part whose links may end here, such as `["power"]` for a wheel bearing only engines turn. The Connect Tool grays out other targets and the host refuses them. Default: any kind but `structural`. |
| `logic.reverse` | setting id | One of the part's `toggle` settings that reverses which way it turns. Right click on its node with the Connect Tool flips it, a curved arrow by the node shows it, and scripts that drive the part read it as `joint.reversed`. Vanilla bearings use `"reverse"`. |
| `seat` | object | Makes the part a seat a player sits in with E. Its script reads them with `part.occupant()` and `part.occupant_input()`. |
| `seat.offset` | [x, y, z] (m) | Where the sitter's feet go, from the part's centre, in the part's frame. Inside the part's footprint. Default [0, 0, 0]. With a `seat.cushion` only its height counts: the floor the feet rest on. |
| `seat.cushion` | [x, y, z] (m) | The point on the cushion the sitter's bottom rests on, under the hips, from the part's centre in the part's frame. Inside the footprint and above the feet's floor (the offset's height, or the part's bottom). The player sits there and its legs fit the height: a chair-height cushion (0.39 m above the floor) bends the knees under it, a lower one stretches the legs out in front. Without it the hips go 0.53 m above the offset. |
| `seat.hands` | `wheel` or `free` | What the sitter's hands do: hold a wheel in front of the chest, or rest on the thighs as a passenger. Default `wheel` on a seat with logic outputs, else `free`. |
| `seat.pose` | `chair` or `saddle` | How the sitter sits: in a chair (the `sit` clips, fitted to the cushion's height), or astride a saddle (the `sit_saddle` clip with the hands on handlebars, `sit_saddle_ride` with `seat.hands` `free`; the hips land on the cushion, and a saddle higher or lower than the clip's lifts or drops the body). Default `chair`. Vanilla's Driver's Saddle and Saddle use `saddle`. |
| `seat.bounce` | number, 0 to 4 | How much the sitter bounces and sways with the ride: the hips bob and the upper body sways with each change in the seat's own motion (bumps, braking, turns), so a smooth road barely shows. 0 is rigid, 1 a padded seat. Sitting down also sinks the body in and lets it settle. Default 1. |
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
| `presets` | list of `{name, value}` | Buttons by a number's slider that set it to `value` (Low, Medium and High). Each value fits the setting. |
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
| `scripts/engine.lua` | `speed` (rad/s) | Wheel bearing speed at full throttle at full power. `gears` (default 5) is how many steps the `gear` setting (shown as Power) has, each giving that share. With no control linked in (a seat, switch, button or gate) it does not run; the `on` setting stops it whatever its controls do. |
| | `torque`, `brake` (N·m) | Most torque while driving, and while holding the wheels with no throttle or while it is not running (nobody seated, switched off, out of fuel), so a parked car stays put. |
| | `fuel`, `fuel_time` (s) | Item burned from the creation's containers, and seconds of full throttle per item. Creative needs none. |
| | `loop_parameter` | Parameter of the `loop` sound that follows the throttle (`load`, `speed`). |
| | `ignition` | When true the engine has to be switched `on` even when linked, and shows `clip`, `lever`, `heat`, `smoke`, `sparks` and `oil` for its model. |
| | `start_time`, `stop_time`, `gear_time` (s) | How long it cranks, winds down, and rattles after a power change. |
| | `heat_time` (s) | How long its `heat` display (how hard it works, 0 to 1, for glowing parts of its model) takes to rise or fall all the way. Default 0.4. |
| | `false_start`, `false_start_time` (s) | The chance a start fails first, and how long the failed try lasts. It always fails with no fuel. |
| | `uneven`, `backfire`, `hard_gear`, `smoke` | How much its speed wobbles (share of itself), backfires a second, the power from which it runs hard, and its smoke at full power (0 to 1). Keep `speed` × (1 + `uneven`) at or under the 60 rad/s bearing limit. |
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
| `scripts/light.lua` | `blink` (s), `duty` (and a `brightness` setting) | Blink period (0 or missing: steady) and the share of it spent lit. With no inputs linked it shines until E turns it off; linked, it shines while its input is above 0. A part with a `brightness` number setting (1 to 10) shines at that many tenths of full. It shows `on` or `off` on its `pose` readout, so a model with those clips (`display.clips`) lights its lamp with it. |
| `scripts/radio.lua` | (the `station` setting) | Plays the `station_N` sound loop; E turns it on and off. |
| `scripts/beacon.lua` | `ping`, `flash` (s) | Seconds between pings, and how long each ping lights it fully. |
| `scripts/horn.lua` | `honk` (s) | How long E sounds it; a linked input sounds it while above 0. |
| `scripts/drill.lua` | `min_spin` (rad/s), `interval` (s), `damage`, `damage_to` ({type = damage}), `tip` ({from, reach}), `flank` ({at, from, reach, rays, lean}), `shake` (N) | Drill: no input; while a bearing spins it at `min_spin` or more it bites what its tip and the flanks of its bit touch (the only tool that breaks rock deposits), and each bite shakes its Body sideways with `shake` N (more the faster it turns). Its `spin` readout (0 to 1 of `min_spin`) drives the base ring glow. |
| `scripts/saw_blade.lua` | `min_spin` (rad/s), `interval` (s), `damage`, `damage_to` ({type = damage}), `rim` and `faces` ({from, reach, rays}) | Saw Blade: no input; while a bearing spins it at `min_spin` or more it bites what its rim and faces touch, `damage_to` per part type or world object kind (logs cut faster than standing trees). Its `spin` readout (0 to 1 of `min_spin`) drives the hub glow. |
| `scripts/water_cannon.lua` | `muzzle`, `range` (m), `water` (s), `impulse` (N·s), `interval` (s) | Fires on a rising input or E: waters soil it hits, else pushes what it hits. |
| `scripts/mounted_gun.lua` | `ammo`, `muzzle`, `range` (m), `damage`, `impulse` (N·s), `interval` (s) | Hitscan shots taking `ammo` from the creation's containers (none in Creative). |

## light and animation

`light` gives a part a light its script switches with `part.set_light`. Every peer draws it from the
host's level; it is dark until set.

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `kind` | `omni` or `spot` | no | Default `omni`. |
| `color` | `"#rrggbb"` | no | Default white. A painted part's light shines in its paint colour instead, and its model's glowing surfaces glow in it. |
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
| `lock_when_sequenced` | bool | While a part with a `sequence` setting (a Controller) is linked into the part, its menu shows only that the Controller runs it and the host refuses setting changes (a piston). |
| `lever` | object | Lays the menu out round a power lever, as the engines' menus are: see [lever](#lever). |

Each readout:

| Key | Type | Meaning |
|-----|------|---------|
| `id` | snake_case | The id the script passes to `part.display`. Required, unique. |
| `name` | string | Label. Default: the id. |
| `kind` | `bar`, `number` or `text` | A cyan bar from 0 to `max`, a number, or the script's text. Default `number`. |
| `max` | number > 0 | A bar's full value. Default 1. |
| `unit` | string | Shown after a number (`rpm`, `N·m`). |
| `decimals` | integer 0 to 6 | Digits after the point. Default 0. |

### lever

A menu with `lever` shows the part's name and level pill, its picture with a fuel slot under it, a tall lever
from Off at the bottom to MAX at the top, its other readouts as chips (`bar` readouts other than `fuel_bar`
are left out), a hint line, and, while the part has a next level, a band with that level's picture, what it
gains, its cost (have/need, free in Creative) and Upgrade (U). The lever's steps run to the top of `setting` at
the part's last level; steps above the current level's top are hatched under a LIMIT mark until the part is
upgraded. Picking a step sends the setting (and turns `off` on); the bottom notch turns `off` off.

| Key | Type | Meaning |
|-----|------|---------|
| `setting` | setting id | The `number` setting the lever sets (an engine's `gear`). Required. |
| `off` | setting id | A `toggle` setting the bottom notch turns off. Without it the lever starts at the setting's `min`. |
| `fuel` | item id | The item shown in the fuel slot (`gasoline`). |
| `fuel_count` | readout id | A `number` readout counting `fuel` items left, shown on the slot (red and faded at 0). Needs `fuel`. |
| `fuel_bar` | readout id | A `bar` readout of what is left of the item burning now, shown under the slot. Needs `fuel`. |
| `hint` | string | The line under the lever. Default "Drag to adjust the power". |
| `upgrade_stats` | list of `{name, value, show}` | What the next level gains, in order. `value` is `outputs`, `inputs`, `durability`, `settings.<id>` (the top of a number setting) or `properties.<key>` (a number property); `show` is `difference` (default, `+2`) or `percent` (`+50%`). A line whose value does not change is left out. Without the list the band shows every change, from → to. |

```json
"lever": {
  "setting": "gear", "off": "on", "fuel": "gasoline", "fuel_count": "fuel_items", "fuel_bar": "fuel",
  "upgrade_stats": [
    { "name": "Bearings", "value": "outputs" },
    { "name": "Torque", "value": "properties.torque", "show": "percent" }
  ]
}
```

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
| `press` | `{readout, axis, depth}` | Pushes the model in by `depth` m (default 0.02, at most 0.1) along `axis` (default `[0, -1, 0]`) while the value is not 0 (a button). |
| `clips` | `{readout, scrub, idle}` | Plays the model's own glTF animations (the model must be a `mesh`). The `readout` is a text value naming the clip to play (text after a `#` is ignored, so `stop#3` restarts `stop`; `start>run_loop` plays `start` and then `run_loop`); clips whose names end in `_loop` loop. `idle` names a clip played while the readout has no value and after a clip that does not loop has finished (a station's idle glow). `scrub` (at most 4) is a list of `{readout, clip}`: that clip is held at the value (0 to 1) of its length, for a lever or a needle. |
| `effects` | list of `{readout, kind, offset, direction, color}` | At most 8 particle effects whose strength follows the value from 0 to 1. `kind` is `smoke`, `sparks` or `drops`; `offset` lies inside the footprint, `direction` (default up) is where they go, `color` is optional. |

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
  "stations": ["fabricator", "portable_fabricator"],
  "inputs": [
    { "item": "scrap_wood_block", "count": 16 },
    { "item": "scrap_metal_block", "count": 8 }
  ],
  "outputs": [
    { "item": "chest", "count": 1 }
  ],
  "time": 4,
  "order": 1710
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
| `order` | whole number | no | Where it sits in a station's list (see Recipe order). Omitted puts it after every recipe that has one. |

Item and station ids are `id` (vanilla) or `mod_id:id`, and must name parts that are loaded. A recipe
with errors is left out and the rest still load. Host and clients compare their recipes when a client
joins.

### Recipe order

A station lists its recipes by `order`, lowest first, then by name, so related items sit together
(Scrap Wheel, Wheel, Off Road Wheel). The hundreds pick the group and the tens the place in it, cheapest
or scrap tier first. A new recipe joins its group: take the next free ten after the item it belongs
behind (or a number in between), and start a new group with the next free hundred. Mods use the same
numbers to slot their recipes among vanilla's, for example `"order": 635` for a wheel that goes after the
Off Road Wheel. Only level 1 of an upgradable part has a recipe, so a part's levels never take more than
one place. A part made at both the Portable Fabricator and the Fabricator (Scrap Mechanic's Mini Craftbot and Craftbot)
has two recipes with the same output: the scrap one, `<id>_scrap`, takes the number just before the Fabricator's
(`bearing_scrap` 1005, `bearing` 1010), since every recipe needs its own number. Vanilla's groups:

| Order | Group | Vanilla recipes, in order |
|-------|-------|---------------------------|
| 100 | Blocks | Each block then its wedge, scrap first: scrap wood, scrap metal, scrap stone, sand, Wood Blocks 1-3, cardboard, barrier, insulation, carpet, Metal Blocks 1-3, extruded metal, punched steel, net, solid net, striped net, square mesh, diamond plate, aluminum, rusted metal, worn metal, path light, hull plate, hull floor, brick, tile, worn tile, Concrete Blocks 1-3, cracked concrete, concrete slab, plaster, painted wall, glass, glass tile, armored glass, ice, plastic, bubble plastic |
| 200 | Ramps, stairs and railings | `ramp_block`, `stairs_block`, `ladder`, `metal_railing` |
| 300 | Beams and frames | `ibeam_stub`, `ibeam_short`, `ibeam_long`, `ibeam_corner`, `ibeam_end`, `ibeam_holder`, `u_beam`, `metal_column_short`, `metal_column`, `metal_support`, `support_bracket`, `stand_support`, `support_pillar`, `lattice_pillar`, `support_pillar_stand`, `support_truss_small`, `support_truss`, `support_truss_large`, `tower_pole`, `tower_pole_top`, `door_frame`, `window_frame` |
| 400 | Pipes | `small_pipe_short`, `small_pipe_long`, `small_pipe_bend`, `small_pipe_corner`, `small_pipe_tee`, `small_pipe_cross`, `small_pipe_four_way_tee`, `small_pipe_five_way`, `small_pipe_six_way`, `valve`, `large_pipe_short`, `large_pipe_long`, `large_pipe_corner`, `large_pipe_tee`, `main_pipe_short`, `main_pipe_long`, `main_pipe_corner`, `main_pipe_tee`, `main_pipe_extension`, `main_pipe_mount`, `main_pipe_cap`, `main_pipe_compressor` |
| 500 | Seats and controls | `scrap_seat`, `scrap_drivers_seat`, `passenger_seat`, `saddle`, `drivers_seat`, `drivers_saddle`, `controller` |
| 600 | Wheels | `scrap_wheel`, `wheel`, `big_wheel` |
| 700 | Suspension | `sport_suspension`, `offroad_suspension` |
| 800 | Engines | `scrap_gas_engine`, `gas_engine`, `electric_engine` |
| 900 | Fuel and power | `gasoline`, `ember`, `gas_container`, `battery`, `battery_pack` |
| 1000 | Bearings and pistons | `bearing_scrap`, `bearing`, `piston` |
| 1100 | Flight | `thruster`, `hover_jet`, `rotor`, `wing`, `control_surface` |
| 1200 | Logic | `switch_scrap`, `switch`, `button`, `logic_gate`, `timer`, `sensor` |
| 1300 | Working parts | `drill`, `saw_blade`, `water_cannon`, `potato_blaster_mounted` |
| 1400 | Sound | `horn`, `radio` |
| 1500 | Vehicle body | `bumper_front`, `bumper_rear`, `mud_flap`, `exhaust_pipe`, `license_plate`, `roof_rack`, `antenna`, `windshield`, `side_window` |
| 1600 | Lights | `shack_light`, `glowstick`, `fluorescent_light`, `packing_lamp`, `interior_light`, `headlight`, `spotlight`, `construction_light`, `warning_light`, `beacon_scrap`, `beacon`, `warehouse_spotlight`, `frame_beam_light`, `neon_sign`, `protector_sign` |
| 1700 | Storage | `chest` (Scrap Chest), `small_chest`, `large_chest`, `cargo_chest`, `barrel`, `locker`, `tool_cabinet`, `file_cabinet`, `fridge`, `chemical_container`, `water_container`, `potato_ammo_container`, `dispenser`, `broken_microwave`, `ruin_chest` |
| 1800 | Stations | `portable_fabricator`, `refiner`, `respawn_bed` |
| 1900 | Farming | `soil_bag`, `water_bucket`, `feeding_trough`, `lead` |
| 2000 | Furniture | `stool`, `chair`, `toilet`, `bathtub`, `wooden_table`, `metal_table`, `office_table`, `table_support`, `office_chair_base`, `cubicle_wall`, `shelf_unit`, `shelf_pillar`, `shelf_support`, `shelf_support_tall`, `pallet`, `stall_door`, `sink`, `mattress`, `pillow`, `mug`, `calendar`, `toilet_paper` |
| 2100 | Road signs | `stop_sign`, `caution_sign`, `danger_sign`, `no_entry_sign`, `speed_limit_sign`, `direction_arrow_left`, `direction_arrow_straight`, `direction_arrow_right` |
| 2200 | Road and site | `traffic_cone`, `barrier_tape`, `outpost_sign`, `warehouse_key_sign` |
| 2300 | Nature | `fallen_log`, `tree_trunk` |
| 2400 | Tools | `connect_tool`, `weld_tool`, `paint_tool`, `paint_ammo` |
| 2500 | Weapons | `potato_blaster`, `potato_scattergun`, `potato_launcher`, `popper` |
| 2600 | Refined materials | `wood_rod`, `pine_rod`, `stone_rod`, `metal_rod` |
| 2700 | Crafting materials | `glue` |
| 2800 | Ducts, conduits and cables | `duct_short`, `duct_long`, `duct_corner`, `duct_tee`, `duct_end`, `duct_holder`, `metal_tube`, `tubes_short` |

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

## Ids and namespacing

Every entry (part, item, recipe, bot, quest, POI, character, sound event) has a game-wide id,
`mod_id:id`: `vanilla:wheel`, `my_mod:sentry_drone`. Inside a file you usually write the short form, and
where you write it decides which mod it means:

| Where | Bare id (`wheel`) means | Another mod's |
|-------|-------------------------|---------------|
| Mod data files (`parts/`, `recipes/`, `bots/`, `quests/`, `pois/`, `characters/`, `raids/`) | The mod the file is in | `other_mod:id` |
| A part's `sounds` and other sound event ids | The file's own mod's event if it has one, otherwise vanilla's | `other_mod:sound_id` |
| Asset paths (`mesh`, `icon`, `script`, sound files) | A file in the file's own mod, relative to its root (`meshes/wheel.glb`) | `other_mod:meshes/wheel.glb` (that mod must be a dependency, `MD0303`) |
| Lua functions that take item or part ids | The script's own mod | `other_mod:id` |
| Action API arguments (from players, agents and `actions.call`) | Vanilla (`wheel` is `vanilla:wheel`) | `other_mod:id` |
| `overrides/<other_mod>/` files and `remove.json` | The target mod's entry: `overrides/vanilla/wheel.json` keeps `"id": "wheel"` and replaces `vanilla:wheel` | — |
| Ids inside an override or patch of another mod's bot, quest, POI or character | The owner's: the entry is checked as if it were in the target mod (asset paths still resolve in yours, below) | `other_mod:id` |
| Bot ids in `raids/raids.patch.json` | Your mod | `other_mod:id` |

When in doubt, and always in part and recipe overrides, write ids that point outside your own mod in full
(`vanilla:component_kit`): a qualified id means the same thing everywhere.

Asset paths never use `res://`, a URL, an absolute path, backslashes or `..`; any of those is an error (`MD0301`).
In an override, relative asset paths resolve inside **your** mod, so write `vanilla:meshes/wheel.glb` to keep
the original's mesh.

The folders where a mod changes what another mod owns:

| Path in your mod | What it does |
|------------------|--------------|
| `overrides/<other_mod>/<any name>.json` | Replaces that mod's part with the same `id`. |
| `overrides/<other_mod>/recipes/**/*.json` | Replaces its recipe with the same `id`. |
| `overrides/<other_mod>/bots/`, `quests/`, `pois/`, `characters/` | Replaces its bot, quest (by file name), POI or character. |
| `overrides/<other_mod>/**/<id>.patch.json` | A merge patch on one entry, in any of the folders above. |
| `overrides/<other_mod>/remove.json` | Takes entries out, by id under `parts`, `recipes`, `bots`, `quests`, `pois` and `characters`. |
| `world/worldgen.patch.json`, `raids/raids.patch.json` | Adds to and changes the world generation rules and the raid table, stacking across mods. |
| `rules/survival.patch.json`, `rules/fire.patch.json`, `rules/building.patch.json` | Changes the game rules (not the locked keys). |

The other mod must be a dependency for overrides, patches and removals. The details are in the
[Packaging Guide](packaging-guide.md#6-overriding-another-mods-parts).

## Overrides

A mod can replace another mod's part by shipping a full part file at
`overrides/<other_mod>/<any name>.json` that keeps the original `id`. The other mod must be a
dependency. A recipe is replaced the same way, with a full recipe file at
`overrides/<other_mod>/recipes/<any name>.json` that keeps the original `id`. See the
[Packaging Guide](packaging-guide.md).

## Error codes

Every problem the loader or editor reports has a stable code, a file, a JSON path and a message, for
example `error MD0201: mods/my_mod/parts/a.json(4) $.mass: mass must be greater than 0.`

Codes are grouped by area in ranges of 100. Every code the game uses is listed here; the topic docs explain
the rules behind them. Problems are errors unless the table says "warning". A file with errors is left out
(and a mod with errors is not loaded, `MD0110`); a warning is reported and the content still loads.

| Code | Meaning |
|------|---------|
| **00xx** | **Files and JSON** |
| MD0001 | A file can't be read. |
| MD0002 | A file is not valid JSON (comments and trailing commas are allowed). |
| MD0003 | A key this build does not know; it is kept and ignored (warning). |
| **01xx** | **`mod.json`, dependencies and load order** (see the [Packaging Guide](packaging-guide.md#10-packaging-diagnostics)) |
| MD0101 | A folder has no `mod.json`; it is skipped (warning). |
| MD0102 | `mod.json` is invalid (missing id, name or version, a bad dependency or conflict entry). |
| MD0103 | The mod id does not match the folder name. |
| MD0104 | Two folders use the same mod id; the second is skipped. |
| MD0105 | A dependency is not installed. |
| MD0106 | A dependency is installed, but its version is outside the range. |
| MD0107 | Dependency cycle. |
| MD0108 | A dependency failed to load. |
| MD0109 | `format_version` is newer than this build reads. |
| MD0110 | The mod has errors in its files and was not loaded. |
| MD0111 | A dependency is disabled in the Mod Manager. |
| MD0112 | An enabled mod is listed under this mod's `conflicts`. |
| MD0113 | `mods.json` (the Mod Manager's load order) is invalid. |
| **02xx** | **Parts** |
| MD0201 | A part value is missing or invalid. |
| MD0202 | Two files in a mod define the same part id. |
| MD0203 | Overrides target a mod that is not a dependency. |
| MD0204 | An override names a part the target mod does not have. |
| MD0205 | Two mods override the same part; the later one wins (warning). |
| **03xx** | **Asset paths** |
| MD0301 | An asset path is invalid (absolute, `..`, `res://`, or the wrong file type). |
| MD0302 | The file an asset path names is missing. |
| MD0303 | An `other_mod:path` names a mod that is not a dependency. |
| **04xx** | **Saved Creations** (blueprints and saves) |
| MD0401 | A Creation value is missing or invalid. |
| MD0402 | A Creation uses a part that no loaded mod defines. |
| MD0403 | Two parts of a Creation overlap. |
| MD0404 | A connection is listed twice or joins sockets that don't fit. |
| MD0405 | The parts form more than one separate piece; a Creation must be connected. |
| **05xx** | **Part Editor and batches** |
| MD0501 | An editor source problem (a model piece). |
| MD0502 | Export could not write to the mod folder. |
| MD0503 | A batch file is invalid (Part Editor, Bot Behavior Editor or [story batch](quests.md#story-batches)): a bad `file` name, a missing template, an unknown `format_version`. Nothing is written. |
| **06xx** | **World generation and POIs** (see [World Generation](world-generation.md)) |
| MD0601 | `world/worldgen.json` or a world patch is invalid, or names a POI that did not load. |
| MD0602 | No enabled mod ships valid world generation rules; the world is flat. |
| MD0603 | A POI prefab is invalid, or two files define the same POI id. |
| **07xx** | **Recipes** (see [Recipes](#recipes)) |
| MD0701 | A recipe value is missing or invalid. |
| MD0702 | Two recipe files in a mod use the same id. |
| MD0703 | A recipe override names a recipe the other mod does not have. |
| MD0704 | Two mods override the same recipe (warning). |
| **08xx** | **Sound events** (see [Sound Events](sound-events.md#error-codes)) |
| MD0801 | A sound event value is missing or invalid, or a file was left out because of its errors. |
| MD0802 | Two events in a mod share an id. |
| MD0803 | Two mods replace the same event (warning). |
| MD0804 | A part names a sound event nobody registered (warning). |
| **09xx** | **Bots and raids** (see the [Bot Tutorial](bot-tutorial.md)) |
| MD0901 | A bot value is missing or invalid. |
| MD0902 | Two files define the same bot id (or an export would overwrite another bot). |
| MD0903 | The raid table (`raids/raids.json` or a raid patch) is invalid, or names an unknown bot or one without a `raid_cost`. |
| MD0904 | A behaviour tree node is neither built in nor a function of the bot's script (warning). |
| **10xx** | **Saves** (see [Saves](saves.md)) |
| MD1001 | A save file is missing, unreadable or invalid. |
| **11xx** | **Quests** (see [Quests and Dialogue](quests.md)) |
| MD1101 | A quest value is missing or invalid (an unknown POI, item or required quest, a `requires` loop). |
| **12xx** | **Player characters** (see [Characters](characters.md)) |
| MD1201 | A character is invalid: its mesh is missing, not skinned or lacks a rig bone. |
| MD1202 | Two files share a character id (warning). |
| **13xx** | **Mod packages** (see the [Packaging Guide](packaging-guide.md#installing)) |
| MD1301 | Not a mod package: unreadable zip, no `package.json`, invalid manifest, or vanilla. |
| MD1302 | Unsafe path or symbolic link in the archive (or the mod, on export). |
| MD1303 | The archive or a file is over the size limits, or looks like a zip bomb. |
| MD1304 | A file's SHA-256 or size, or the package hash, does not match `package.json`. |
| MD1305 | The archive's files and `package.json`'s `files` list differ. |
| MD1306 | Needs a newer game, package format or AI protocol. |
| MD1307 | A dependency is missing or the wrong version (error), or an installed mod needs another version (warning). |
| MD1308 | Conflicts with an installed mod (warning). |
| MD1309 | Export refused: the mod has errors. |
| MD1310 | An installed mod's file was changed, removed or added since it was installed (warning). |
| **14xx** | **Lua scripts** (see the [Lua API](lua-api.md)) |
| MD1401 | A script does not compile (a Lua syntax error), found by the mod tools' check of every enabled mod. |
| **15xx** | **Overrides, patches and removals** of bots, quests, POIs and characters, and `remove.json` (see the [Packaging Guide](packaging-guide.md#overriding-bots-quests-pois-and-characters)) |
| MD1501 | An override, patch or removal names an entry that doesn't exist (an error; a warning for removals). |
| MD1502 | Two mods change the same bot, quest, POI or character; the later one wins (warning). |
| **16xx** | **Game rules** (`rules/*.patch.json`, see the [Packaging Guide](packaging-guide.md#game-rules)) |
| MD1601 | A file in `rules/` is not a patch of a rules file; it is ignored (warning). |
| MD1602 | A rules patch changes a locked key, or is not one JSON object; the mod is not loaded. |
| MD1603 | A rules patch would make the settings invalid; it is left out when the world starts, and the rules before it still apply. |
