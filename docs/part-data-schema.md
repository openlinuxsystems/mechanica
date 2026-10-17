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
| `sound_material` | string | no | Material for place, break and impact sounds (`wood`, `metal`, `concrete`, `glass`, ...). |
| `size` | `[x, y, z]` cells | placeable | Footprint in 0.25 m grid cells, at least 1 on every axis. |
| `mesh` | path | no | `.glb` or `.gltf` model, centred on the footprint centre, +Y up, in metres. Without one, a grey shape matching the collision stands in. |
| `icon` | path | no | `.png` inventory icon. |
| `visual` | object | no | Colour, surface and colour variants. See [visual](#visual). |
| `collision` | object | no | Collision shape. Omitted means a box filling `size`. See [collision](#collision). |
| `sockets` | list | no | Attachment points. Placeable parts only. See [sockets](#sockets). |
| `script` | path | no | `.lua` behaviour script. |
| `sounds` | object | no | Sound events: local event name → sound id (`"start": "engine_start"`). Ids are snake_case, or `mod_id:sound_id` for another mod's sound. |
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
