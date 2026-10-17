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
| `durability` | integer ≥ 1 | placeable | Toughness rating; the part's HP derives from it. |
| `friction` | number ≥ 0 | placeable | Surface friction coefficient. A Body uses its parts' mass-weighted mean. |
| `buoyancy` | number ≥ 0 | placeable | Buoyant force factor: force = buoyancy × displaced volume. |
| `flammable` | bool | placeable | Whether fire can ignite and spread through it. |
| `stack_size` | integer ≥ 1 | no | Most items per inventory slot. Omitted means 1. |
| `sound_material` | string | no | Material for place, break and impact sounds (`wood`, `metal`, `concrete`, `glass`, ...): an unbound `place` plays `place_<material>`. See [Sound Events](sound-events.md#part-sounds). |
| `size` | `[x, y, z]` cells | placeable | Footprint in 0.25 m grid cells, at least 1 on every axis. |
| `mesh` | path | no | `.glb` or `.gltf` model, centred on the footprint centre, +Y up, in metres. Without one, a grey shape matching the collision stands in. |
| `icon` | path | no | `.png` inventory icon. |
| `visual` | object | no | Colour, surface and colour variants. See [visual](#visual). |
| `collision` | object | no | Collision shape. Omitted means a box filling `size`. See [collision](#collision). |
| `sockets` | list | no | Attachment points. Placeable parts only. See [sockets](#sockets). |
| `script` | path | no | `.lua` behaviour script. A placed part runs it while it exists; an item (food, a weapon) runs it while a player holds and uses it. See the [Lua API Reference](lua-api.md). |
| `sounds` | object | no | Sound events: local sound name → sound id (`"start": "engine_start"`). The game plays `place`, `remove`, `break` and `impact`; other names are for the script. A bare id is the part's own mod's event, else vanilla's; `mod_id:sound_id` names another mod's. See [Sound Events](sound-events.md#part-sounds). |
| `inventory` | object | no | Item storage. Placeable parts only. See [inventory and crafting](#inventory-and-crafting). |
| `crafting` | object | no | Makes the part a crafting station. Needs `inventory`. See [inventory and crafting](#inventory-and-crafting). |
| `soil`, `respawn_point` | object, bool | no | Farming soil and respawn beds. Placeable parts only. See [survival](#survival). |
| `seed`, `watering` | object | no | Farming items. Items only. See [survival](#survival). |
| `properties` | object | no | Free-form numbers and settings that only the part's Lua script reads (torque, thrust, range). Key order is kept. |

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
| `texture` | path | `.png` albedo texture, replacing the mesh's own. |
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

## inventory and crafting

Inventories are a built-in primitive (players, chests, fuel tanks, station output), configured here
rather than in Lua.

| Key | Type | Meaning |
|-----|------|---------|
| `inventory.slots` | integer 1–256 | Number of item slots. Required when `inventory` is present. |
| `inventory.accepts` | list of item ids | Only these items go in (`["gasoline"]` for a gas container). Omitted means any item. |
| `crafting.speed` | number > 0 | Multiplier on recipe times: 2 crafts twice as fast. Default 1. |
| `crafting.queue_size` | integer 1–64 | Most orders waiting at once. Default 1. |

Each slot holds one stack of one item, up to that item's `stack_size`. A station runs every recipe that
lists its part id in `stations`: the crafting player's inventory pays the inputs when the order is
queued, and finished crafts land in the station's own inventory, where any player in reach can take
them. A container or station that holds items, or has orders queued, can't be picked up with the
Hammer. Its contents are saved with the Creation; if it is broken (Sledgehammer, weapons, a crash) they
fall out as a pile of items on the ground.

```json
"inventory": { "slots": 12 },
"crafting": { "speed": 1, "queue_size": 8 }
```

## survival

Farming and respawn beds in the survival loop. These are built-in primitives, configured here rather
than in Lua. Numbers are SI: health points, seconds, metres, N·s. See also [Survival](survival.md).

Food and weapons are item scripts (chunk 20b): a `script` plus its numbers in `properties`. The
vanilla scripts read these properties:

| Script | Property | Meaning |
|--------|----------|---------|
| `scripts/food.lua` | `health` | Health one eaten item gives back. |
| `scripts/potato_gun.lua` | `damage` | Damage per hit, or at the centre of a splash. |
| | `interval` (s) | Shortest time between shots. |
| | `range` (m) | How far a shot reaches. |
| | `ammo` | Item used up per shot (it can be the weapon itself, like a Popper). Omitted means none. |
| | `pellets` | Shots per trigger pull, spread evenly round a cone (at most 64). Default 1. |
| | `spread` (°) | Half-angle of that cone, under 90. Default 0. |
| | `radius` (m) | Makes it a splash weapon: damage falls off to 0 at this distance from where it hits (or the end of its range). |
| | `impulse` (N·s) | Push given to the Body it hits (splash: at the centre). Default 0. |

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
| `seed.value` | number ≥ 0 | How much a planted crop draws raids (chunk 21). Default 1. |
| `watering.seconds` | number > 0 (s) | Seconds of water one item adds to soil. Required. |
| `soil.capacity` | number > 0 (s) | Most seconds of water the soil holds. Required. |
| `soil.growth` | number > 0 | Growth speed multiplier for crops on it. Default 1. |
| `respawn_point` | bool | Players can sleep in it (F) to respawn there. |

```json
"seed": { "crop": "carrot", "grow_time": 240, "yield": 2, "seeds": 1 }
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
dependency. See the [Packaging Guide](packaging-guide.md).

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
| MD08xx | Sound events. See [Sound Events](sound-events.md#error-codes). |
