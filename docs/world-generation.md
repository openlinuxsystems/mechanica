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
5. **Ground.** Each biome's `ground` texture covers it, blending across biome borders; steep slopes take the
   `slope_ground`, and POIs paint their own `ground` on top (see [Ground textures](#ground-textures)).
6. **Flora.** Each biome's `flora` rules grow decorative grass, ground cover and bushes (see [Flora](#flora)).

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
      "scatter": [{ "object": "tree", "spacing": 7.0, "chance": 0.6 }],
      "flora": [
        { "kind": "grass", "density": 1.2, "coverage": 0.6, "patch_size": 10.0, "scale": [0.8, 1.4], "colors": ["#5fae36", "#6fba3c"] },
        { "kind": "bush", "density": 0.03, "coverage": 0.7, "patch_size": 30.0, "models": ["bush_round", "bush_low"] }
      ]
    }
  ],
  "objects": [
    {
      "id": "tree",
      "collider": { "shape": "cylinder", "radius": 0.35, "height": 9.0, "at": [0.0, 4.5, 0.0] },
      "pieces": [{ "shape": "sphere", "radius": 2.6, "at": [0.0, 8.0, 0.0], "color": "#2f5e2a" }],
      "footprint": 1.5,
      "scale": [0.8, 1.25],
      "sink": 0.3,
      "health": 60,
      "drops": [{ "item": "scrap_wood", "count": 6 }],
      "refine": true
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
| `biomes[].ground` | Id of the ground texture (`grounds[]`) the biome is drawn with. Omitted: its plain `color`. |
| `biomes[].scatter[]` | `spacing` is the side of the rule's grid cells; each cell holds at most one object, kept with probability `chance`. |
| `objects[]` | Destructible world objects. Use catalog ids (`tree`, `rock_node`, `boulder`, ...). `footprint` is the radius other scattered objects keep clear of; `sink` lowers the object into the ground. |
| `objects[].health` | Damage the object takes before it breaks (Sledgehammer, weapons, splash). Omitted means it cannot be broken. Damage is not saved: a part-felled tree is whole again after a restart. |
| `objects[].sledge_damage` | Damage one Sledgehammer hit does to it: its gathering damage (a rock node, stone, takes 25). Omitted means `sledge.object_damage` from `game/data/building.json`. |
| `objects[].drops[]` | Items (`item`, `count`, default 1) left on the ground as a pile when it breaks. |
| `objects[].model` | A built-in flora model (see [Flora](#flora)) drawn instead of `pieces`, such as `bush_berry`. |
| `objects[].solid` | `false` for an object players, bots and Creations walk and drive through, like the seed bush: its collider only catches the Sledgehammer's swing, so it still breaks for its drops. Default `true`. |
| `objects[].refine` | `true` for a raw resource node (tree, rock node): holding Use (E) on it for `refine_seconds` (`game/data/survival.json`, 5.2 s, as in Scrap Mechanic) refines it, and its `drops` go straight into the inventory. Moving more than `refine_cancel_distance` (1.5 m) or letting go cancels. Default `false`. |

Shapes (`collider`, `pieces`) are `box` (`size`), `sphere` (`radius`) or `cylinder` (`radius`,
`height`, upright), centred at `at`, turned `yaw` degrees about +Y, coloured `#rrggbb`. They are
placeholders until meshes are authored.

Scattered objects keep stable ids (`rule.cell_x.cell_z`), which is how a destroyed tree stays destroyed.
Adding, removing or reordering scatter rules changes those ids, so do it before a world is saved.

### Ground textures

`grounds[]` lists up to 8 tileable ground textures; biomes, steep slopes and POIs pick them by id, and
neighbouring grounds blend. The vanilla ones are painted in the flora's style (`tools/author-ground-textures.py`
redraws them).

| Key | Meaning |
|-----|---------|
| `grounds[].id` | Id (lowercase snake_case). |
| `grounds[].texture` | Tileable square `.png` relative to the mod root (`other_mod:path` for another mod's), drawn at 512 px. |
| `grounds[].tile` | Metres of ground one repeat covers (0.25 to 256). Default 4. A copy 3.7 times larger is mixed in so the repeat doesn't show from afar. |
| `grounds[].flora` | `false` for ground nothing grows on (burnt earth): flora skips samples it covers by more than 45%. Default `true`. |
| `grounds[].flora_tint` | `#rrggbb` that plants on or next to it take, fully where it covers 40% of the ground (charred grass at the crash site). |
| `slope_ground` | `ground` (an id), `from` and `to` (slopes, rise over run, default 0.7 and 1.1): steep ground fades to it between them, whatever the biome. |

### Flora

`biomes[].flora[]` grows decorative plants: tufts of grass, ground cover (clover and small flowers) and
leafy bushes, drawn with GPU instancing round the camera. Flora is decor only. It has no collider, so
players, bots and Creations pass through it (and it bends out of their way); it is never saved or synced,
because every peer grows the same plants from the seed. Plants keep off water, ground steeper than
`max_slope`, bare ground (`grounds[].flora: false`), POI footprints (unless the POI names its own `flora`), and
(bushes) scattered objects.

| Key | Meaning |
|-----|---------|
| `kind` | `grass`, `ground_cover` or `bush`. Each kind has its own Foliage density setting (Options, Graphics). |
| `density` | Plants per square metre in a full patch, at the High foliage level (0 to 20). |
| `coverage` | Share (0 to 1) of the biome's ground covered by patches; noise decides where. Default 1, everywhere. |
| `patch_size` | Typical width of a patch in metres. Default 12. |
| `scale` | Random scale range `[min, max]`. Default 1. |
| `models` | Models to pick from (below). Default: every model of the kind. |
| `colors` | Tints to pick from, `#rrggbb`. Leaves take the tint; flowers and bolts keep their own colours. |
| `max_slope` | Steepest ground (rise over run) the plants grow on. Default 1. |

The models are built in code: grass `grass_short`, `grass_tall`, `grass_wide`, `grass_mixed`,
`grass_tipped` (yellow tips) and `grass_patch`; ground cover `clover`, `sprouts`, `flowers_yellow`,
`flowers_white` and `flowers_purple`; bushes `bush_round`, `bush_spiky`, `bush_low`, `bush_flowering`,
`bush_lumpy` (yellow-tipped lumps) and `bush_berry` (red flowers and bolts, the vanilla seed bush). Run
`tools/capture-flora.sh OUT_DIR` to render them all in a meadow.

How much is drawn, how far and how much it sways in the wind is the player's choice (Options, Graphics,
Foliage density: Off, Low, Medium, High, Very high, for grass, ground cover and bushes apart), tuned in
`game/data/flora.json`, which ships with the build and is not moddable. Lower levels draw a subset of the
plants of higher ones, and Low turns the wind off. At High, grass is drawn to 150 m, ground cover to 80 m and
bushes to 380 m; past `thin_share` of the distance grass and ground cover keep `thin_keep` of their plants,
`thin_scale` times larger, so the far field stays full but cheap, and the ground texture carries on beyond.

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
| `terrain[]` | `flatten` levels a disc to the pad height; `basin` sinks it `depth` metres below the pad (below `sea_level` it fills with water). A basin's `blend` must be at least 1.5 × `depth`, so its bank is no steeper than the 45° a player can walk up and a pond is never a trap. Full strength inside `radius`, fading over `blend`, centred at `at` (`[x, z]`). Must stay inside the POI radius. |
| `pieces[]` | Static structure, as shapes. Positions are relative to the POI centre on its pad. |
| `ground[]` | Ground painted on the footprint, in order, each over the ones before: `ground` (an id from `grounds[]`), full inside `radius` of `at` (`[x, z]`, default the centre) and fading over `blend`, with an edge pushed in and out by up to `noise` metres. Must stay inside the POI radius. |
| `flora` | Id of a biome whose flora grows on the footprint (outside bare ground). Omitted: nothing grows inside the POI. The crash site burns its middle and grows a meadow round it. |
| `markers[]` | Points for other systems: `type` (`spawn`, `item`, `bot`, `fire`, `door`, `npc`, `core`, ...) and an optional `ref` (a catalog id, or a character id for `npc`). The first `spawn` marker of a fixed POI is where players start. A `bot` marker is a guard post: in Survival its bot (`ref`, a bot of this mod unless qualified) appears when a player comes near, leaves when everyone has gone, and is replaced some time after it is killed (distances and the wait are in the game's `data/bots.json`). [Quests](quests.md) point at markers. |

The pad height of a spaced POI is the natural ground height at its centre.
