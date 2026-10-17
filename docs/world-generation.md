# World Generation and POI Prefabs

The world is generated from a seed plus data that mods ship. Every peer regenerates the same world
from the same seed and data, so only the seed and the list of destroyed objects ever cross the
network or go into a save. Like all mod data, keys are `snake_case`, units are SI (metres), and
comments and trailing commas are allowed.

- `world/worldgen.json`: terrain, biomes and scattered objects. The last enabled mod in load order
  that ships one replaces the others' completely.
- `pois/*.json`: one POI prefab per file. Every enabled mod's POIs are used, under the id
  `mod_id:poi_id`.

Host and clients must have identical world data: it is part of the content check when a client joins.

## How a world is built

1. **POIs** are laid out first. Fixed POIs go exactly where they say. Then each spaced kind, highest
   `priority` first, gets one candidate per cell of a grid whose step is the middle of its `spacing`
   range, jittered so that neighbours stay within the range. A candidate is used when it stands in one
   of its `biomes`, on dry ground that is not too steep, and clear of every POI placed before it.
2. **Biomes.** Inland ground is cut into jittered cells of `biome_cell_size`, each given a biome at
   random by `weight`. Borders are bent by `biome_warp` and blended over `biome_blend`. The
   `coast_biome` takes over within `coast_biome_width` of the world edge.
3. **Height.** Each biome sets an average `height` and a `relief` that scales fractal noise. Near the
   edge the land falls to `sea_floor` over `coast_width`, so the world is an island. Each POI then
   applies its `terrain` ops.
4. **Objects.** Each biome's `scatter` rules place trees, rocks and so on.

The world is a square of `world_size` (from `game/data/streaming.json`, 4096 m) centred on the origin,
streamed in 64 m chunks. Height samples are `resolution` metres apart and must divide the chunk size.

## `world/worldgen.json`

```json
{
  "terrain": {
    "resolution": 1.0,
    "sea_level": -4.0,
    "sea_floor": -24.0,
    "coast_width": 160.0,
    "coast_biome_width": 240.0,
    "coast_wobble": 140.0,
    "noise_scale": 420.0,
    "octaves": 5,
    "lacunarity": 2.0,
    "gain": 0.5,
    "biome_cell_size": 760.0,
    "biome_blend": 80.0,
    "biome_warp": 160.0,
    "max_scatter_slope": 0.7
  },
  "coast_biome": "coastal",
  "biomes": [
    {
      "id": "forest",
      "name": "Forest",
      "weight": 3.0,
      "height": 12.0,
      "relief": 20.0,
      "color": "#3f6d3a",
      "scatter": [{ "object": "tree", "spacing": 7.0, "chance": 0.6 }]
    }
  ],
  "objects": [
    {
      "id": "tree",
      "collider": { "shape": "cylinder", "radius": 0.35, "height": 9.0, "at": [0.0, 4.5, 0.0] },
      "pieces": [{ "shape": "sphere", "radius": 2.6, "at": [0.0, 8.0, 0.0], "color": "#2f5e2a" }],
      "footprint": 1.5,
      "scale": [0.8, 1.25],
      "sink": 0.3
    }
  ]
}
```

| Key | Meaning |
|-----|---------|
| `terrain.coast_wobble` | How far inland noise may pull the coastline, so the island is not a square. |
| `terrain.noise_scale` | Wavelength of the largest hills. `octaves`, `lacunarity` and `gain` add finer detail. |
| `terrain.biome_blend` | At most 0.18 × `biome_cell_size`. |
| `terrain.max_scatter_slope` | Steepest ground (rise over run) objects stand on. |
| `biomes[].weight` | Chance an inland cell is this biome. 0 means only where placed explicitly (the coast). |
| `biomes[].scatter[]` | `spacing` is the side of the rule's grid cells; each cell holds at most one object, kept with probability `chance`. |
| `objects[]` | Destructible world objects. Use catalog ids (`tree`, `rock_node`, `boulder`, ...). `footprint` is the radius other scattered objects keep clear of; `sink` lowers the object into the ground. |

Shapes (`collider`, `pieces`) are `box` (`size`), `sphere` (`radius`) or `cylinder` (`radius`,
`height`, upright), centred at `at`, turned `yaw` degrees about +Y, coloured `#rrggbb`. They are
placeholders until meshes are authored.

Scattered objects keep stable ids (`rule.cell_x.cell_z`), which is how a destroyed tree stays destroyed.
Adding, removing or reordering scatter rules changes those ids, so do it before a world is saved.

## `pois/<id>.json`

```json
{
  "id": "outpost",
  "name": "Workshop Outpost",
  "radius": 40.0,
  "placement": { "spacing": [800.0, 1200.0], "biomes": ["meadow", "forest"], "priority": 50 },
  "terrain": [{ "op": "flatten", "radius": 18.0, "blend": 20.0 }],
  "pieces": [{ "shape": "box", "size": [16.0, 0.3, 16.0], "at": [0.0, 0.15, 0.0], "color": "#a3a093" }],
  "markers": [{ "type": "item", "ref": "fabricator", "at": [0.0, 0.3, -5.0] }]
}
```

| Key | Meaning |
|-----|---------|
| `radius` | Footprint, including terrain blending. POIs never overlap, and nothing is scattered inside. |
| `placement.fixed` | `[x, z]` of a single POI, with optional `pad_height` and `yaw` (a multiple of 90). Give this or `spacing`. |
| `placement.spacing` | `[min, max]` metres between neighbours of this kind. `min` must be at least twice the radius. Needs `biomes`. A spaced POI is turned a random quarter turn. |
| `placement.priority` | Higher kinds are placed first and win contested ground. |
| `terrain[]` | `flatten` levels a disc to the pad height; `basin` sinks it `depth` metres below the pad (below `sea_level` it fills with water). Full strength inside `radius`, fading over `blend`, centred at `at` (`[x, z]`). Must stay inside the POI radius. |
| `pieces[]` | Static structure, as shapes. Positions are relative to the POI centre on its pad. |
| `markers[]` | Points for other systems: `type` (`spawn`, `item`, `bot`, `fire`, `door`, ...) and an optional catalog `ref`. The first `spawn` marker of a fixed POI is where players start. |

The pad height of a spaced POI is the natural ground height at its centre.
