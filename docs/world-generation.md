# World Generation and POI Prefabs

The world is generated from a seed plus data that mods ship. Every peer regenerates the same world
from the same seed and data, so only the seed and the list of destroyed objects ever cross the
network or go into a save. Like all mod data, keys are `snake_case`, units are SI (metres), and
comments and trailing commas are allowed.

- `world/worldgen.json`: terrain, biomes, the map's layout (biome regions, rivers, the mandatory
  structures' sites), the road network and scattered objects. The last enabled mod in load order
  that ships one replaces the others' completely (a total conversion).
- `world/worldgen.patch.json`: changes on top of it, so several mods can each add to the same world.
  See [Patching the world](#patching-the-world).
- `pois/*.json`: one POI prefab per file. Every enabled mod's POIs are used, under the id
  `mod_id:poi_id`.

Host and clients must have identical world data: it is part of the content check when a client joins.

## How a world is built

The vanilla world follows the World Map design spec: about 12 × 9 km, a pine forest with lakes in the
north-west, mountains along the north, autumn forest in the north-east, burned forest in the east, desert
in the south-west, golden fields in the south, a big lake in the middle with rivers joining the lakes,
and paved roads between the 50 mandatory structures. The layout is the same in every world; the seed
shapes the ground, the borders, the meanders, the roads' exact course and the scattered POIs.

1. **Biomes.** Inland ground is cut into jittered cells of `biome_cell_size`. With `regions`, each cell
   takes the biome of the region nearest it for its size (borders bent by `region_warp`), or a patch of
   one of the region's `mix` biomes; the cells round a site take the site's `biome`. Without regions each
   cell is given a biome at random by `weight`. Borders are bent by `biome_warp` and blended over
   `biome_blend`: height eases evenly from one biome to the next, while the ground textures, flora and
   scattered objects of both biomes reach into each other in tongues and patches (`biome_edge_noise`,
   `biome_edge_scale`), thinning out the further they reach, so no border is a line. The `coast_biome`
   takes over within `coast_biome_width` of the world edge.
2. **Height.** Each biome sets an average `height` and a `relief` that scales fractal noise; `ridge`
   turns it into sharp ridged noise (mountains) and `terrace` steps it into plateaus (desert). Over that
   come two layers of relief after Scrap Mechanic's overworld: broad `hills` a kilometre or so across, and
   `cliffs`, plateaus in 8 m steps that climb towards the middle of the island. Most steps are slopes you
   walk or drive up; only a `sheer` share of the island keeps short rock faces, tall cliffs where steps stack. Both
   lie flat by the coast, rivers and biomes whose `hills` is 0 (lakes), rising to full height over
   `shore_ramp`, and round sites with a fixed `pad_height` over `pad_ramp`. Near the
   edge the land falls to `sea_floor` over `coast_width`, so the world is an island; invisible walls just past
   the edge keep players and Creations from leaving it. `rivers` cut their
   channels and valleys. Each POI then applies its `terrain` ops, and the roads cut and fill the ground
   under them.
3. **POIs.** Fixed POIs go exactly where they say. Then every site places its POI at its fixed
   fraction of the world (moved at most its `jitter`, onto dry, level ground in its biome), drawing
   from its pool when it has one. The road network is laid between the sites (see
   [Roads](#roads)). Then each spaced kind, highest `priority` first, gets one candidate per cell of a
   grid whose step is the middle of its `spacing` range, jittered so that neighbours stay within the
   range. A candidate is used when it stands in one of its `biomes`, on dry ground that is not too
   steep, `clearance` metres clear of every POI and road placed before it (and, for a `shore` kind, near
   water). Roadside kinds are laid along the roads instead. Last, dirt tracks run to the POIs that ask
   for one. No road or track bank cuts into a POI's level pad.
4. **Objects.** Each biome's `scatter` rules place trees, rocks and so on (never on a road), spread evenly or gathered into groves, rock fields and clearings (`cluster`), and the small scenes between places (see [Scenes](#scenes)).
5. **Ground.** Each biome's `ground` texture covers it, blending across biome borders; steep slopes take the
   `slope_ground`, POIs paint their own `ground` on top (see [Ground textures](#ground-textures)), and road
   shoulders and dirt tracks take the roads' grounds.
6. **Flora.** Each biome's `flora` rules grow decorative grass, ground cover and bushes (see [Flora](#flora)).

The world is `world_size` (east–west) by `world_depth` (north–south) metres (from
`game/data/streaming.json`: 12,032 × 9,024 m, 188 × 141 cells of 64 m) centred on the origin, north
towards −z, streamed in 64 m chunks. Height samples are `resolution` metres apart and must divide the
chunk size.

To see what a seed lays out, run the game with `--world-export DIR [SEED]`: it writes `manifest.json`
(every POI with its coordinates, type, size, biome and road connection; every road and track as a
graded centre line with its bridges; the rivers; the biome and water shares), `heightmap.png` (and the
raw heights in `heightmap.f32`, little-endian floats row by row from the north-west), `biomes.png` and
`overview.png` at 8 m a pixel, then quits.

## Patching the world

Most world mods add something (a tree, an ore, a biome) rather than replace everything. Ship
`world/worldgen.patch.json` instead of `world/worldgen.json` and depend on the mod whose world you
change (usually `vanilla`). Every patch applies in load order on top of the last whole
`worldgen.json`:

```json
{
  "objects": [
    { "id": "blue_rock", "model": "meshes/blue_rock.glb", "collider": { "shape": "sphere", "radius": 0.45, "at": [0, 0.25, 0] },
      "health": 40, "drops": [{ "item": "my_mod:blue_stone", "count": 1, "count_max": 3 }] }
  ],
  "biomes": [
    { "id": "meadow", "scatter": [
      { "object": "blue_rock", "spacing": 20, "chance": 0.5 },
      { "object": "bush", "$remove": true }
    ] },
    { "id": "crystal_flats", "name": "Crystal Flats", "weight": 0.5, "color": "#7fb0d0", "ground": "grass" }
  ],
  "terrain": { "sea_level": -6 }
}
```

- Objects merge key by key, as in a [merge patch](packaging-guide.md#patching-instead-of-replacing);
  `null` removes a key.
- A list of objects merges entry by entry, matched by `id` (or `object`, `level` or `bot` when the
  entry has no `id`): a match is patched, `"$remove": true` takes it out, and anything new is added.
  So the example adds a rock, scatters it in the meadow, takes bushes out of the meadow, adds a biome
  and lowers the sea, and leaves everything else as it was.
- Lists of plain values (and of objects with none of those keys) replace the original list.
- Relative asset paths in a patch are your mod's.
- The result is checked like a whole file. A patch that breaks it is left out with its errors, and the
  world loads without it.

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
    "biome_edge_noise": 30.0,
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
      "scatter": [{ "object": "spruce_tree", "spacing": 12.0, "chance": 0.4 }],
      "flora": [
        { "kind": "grass", "density": 1.2, "coverage": 0.6, "patch_size": 10.0, "scale": [0.8, 1.4], "colors": ["#5fae36", "#6fba3c"] },
        { "kind": "bush", "density": 0.03, "coverage": 0.7, "patch_size": 30.0, "models": ["bush_round", "bush_low"] }
      ]
    }
  ],
  "objects": [
    {
      "id": "spruce_tree",
      "mesh": { "file": "meshes/world/spruce_tree.glb", "lod": "meshes/world/spruce_tree_lod.glb", "lod_distance": 100 },
      "collider": { "shape": "cylinder", "radius": 0.3, "height": 11.0, "at": [0.0, 5.5, 0.0] },
      "footprint": 2.6,
      "scale": [0.85, 1.2],
      "sink": 0.3,
      "health": 80,
      "damage_from": ["sledge", "saw", "explosion"],
      "breaks_into": { "count": 4, "count_max": 6, "parts": [{ "part": "wood_log" }] },
      "fell": { "height": 8.0, "spin": 0.5 },
      "sounds": { "hit": "spruce_hit", "break": "spruce_fall" },
      "debris": { "chips": ["#7a4a28", "#d9a466"], "leaves": ["#79a646", "#678f3b", "#577d32"], "canopy": [0.0, 5.5, 0.0], "canopy_radius": 1.8 }
    }
  ]
}
```

| Key | Meaning |
|-----|---------|
| `terrain.coast_wobble` | How far inland noise may pull the coastline, so the island is not a square. |
| `terrain.noise_scale` | Wavelength of the largest hills. `octaves`, `lacunarity` and `gain` add finer detail. |
| `terrain.biome_blend` | Metres over which neighbouring biomes blend. At most 1.1 × `biome_cell_size`; so is `terrain.hills.shore_ramp`. |
| `terrain.biome_edge_noise` | Metres by which noise pulls each biome's edge back across the blend, so the biomes' grounds, flora and objects mix in tongues and patches. 0 fades evenly; at most 0.6 × `biome_blend`. |
| `terrain.biome_edge_scale` | Size in metres of those tongues (default 24). |
| `terrain.region_warp` | How far, in metres, slow noise bends the borders between regions. |
| `biomes[].ridge` | 0 to 1: how much of the relief is ridged noise, for sharp mountain crests. |
| `biomes[].terrace` | Height in metres of the plateaus the ground is stepped into (the desert). 0: none. |
| `terrain.max_scatter_slope` | Steepest ground (rise over run) objects stand on. |
| `terrain.hills` | Broad hills under every inland biome: `height` (most they lift the ground), `scale` (wavelength in metres), `octaves`, `gain`, `contrast` (higher gives broader flat valleys and tops), `shore_ramp` (metres over which they rise from water and flat biomes) and `pad_ramp` (the same from a fixed pad's rim). Omitted: none. |
| `terrain.cliffs` | Cliff terraces: up to `levels` plateaus `step` metres apart, from noise of wavelength `scale` stretched by `contrast`; a sheer face takes about `face` metres of ground. Only the `sheer` share (0 to 1, default 1) of the island, picked by noise of wavelength `sheer_scale` (default 600), has sheer faces; elsewhere a face is a slope `gentle_face` metres wide (default 24) that players walk and drive up. The plateaus rise to their full number over `inland` metres from the coast. They fade where the hills do. Omitted: none. |
| `biomes[].hills` | 0 to 1: share of the hills and cliffs the biome keeps (0 for lakes, which lie flat). Default 1. |
| `biomes[].weight` | Chance an inland cell is this biome. 0 means only where placed explicitly (the coast). |
| `biomes[].ground` | Id of the ground texture (`grounds[]`) the biome is drawn with. Omitted: its plain `color`. |
| `biomes[].scatter[]` | `spacing` is the side of the rule's grid cells; each cell holds at most one object, kept with probability `chance`. |
| `biomes[].scatter[].perch` | Keeps the rule to hard-to-reach high ground, cliff tops and ledges: a spot is used only when it stands at least `rise` metres (default 6) above the lowest ground `within` metres (default 10) round it. The purple and gold Bloom Pods use it. Omitted: any ground. |
| `biomes[].scatter[].cluster` | Gathers the rule's objects into patches with open ground between, as in groves, rock fields and forest clearings: `scale` (patch size in metres, default 80), `cover` (share of the ground the patches cover, default 0.3) and `outside` (share of the rule's `chance` its spots keep outside the patches, default 0). Rules that name the same `layer` share their patches, so a meadow grove's birches, leafy trees and bushes stand together; give them the same `scale` and `cover`. Since only patch spots are kept, raise `chance` or lower `spacing` to keep the count: on average a rule places about `chance × (cover + (1 − cover) × outside)` per cell. Omitted: spread evenly. |
| `loot_tables` | Weighted item lists by id that objects' `loot` picks from: each has `items[]` of `item`, `count` (default 1), `count_max` (at least `count`) and `weight` (relative odds, default 1). Vanilla has `common`, `epic` and `legendary`, for the green, purple and gold Bloom Pods. |
| `objects[]` | Destructible world objects. Use catalog ids (`birch_tree`, `rock_node`, `boulder`, ...). `footprint` is the radius other scattered objects keep clear of; `sink` lowers the object into the ground. |
| `objects[].health` | Damage the object takes before it breaks (Sledgehammer, weapons, splash). Omitted means it cannot be broken. Damage is not saved: a part-felled tree is whole again after a restart. |
| `objects[].sledge_damage` | Damage one Sledgehammer hit does to it: its gathering damage (a rock node, stone, takes 25). Omitted means `sledge.object_damage` from `game/data/building.json`. |
| `objects[].drops[]` | Items (`item`, `count`, default 1) left on the ground as a pile when it breaks, or taken when it is refined or harvested. `count_max` (at least `count`) makes each one a random number from `count` to `count_max`. `chance` (0 to 1, default 1) makes it loot: each drop is rolled on its own, so a POI crate (an `object` marker) can hold a common find and a rare one. |
| `objects[].loot` | More items it leaves when it breaks, in the same pile as its `drops`: `picks` (default 1) to `picks_max` (default `picks`) different entries of the loot table `table`, each picked with odds in proportion to its weight among those left, with a count from its range. Never more picks than the table has entries; entries that are not items are skipped. Needs `health`. |
| `objects[].glow` | A coloured light the object gives off so it catches the eye from afar: `color` (`#rrggbb`), `energy` (default 1), `range` (metres, default 4), `at` (relative to the ground point) and `pulse` (seconds of one slow swell, 0 for steady). It fades out past 60 m and flares when the object is hit. Only drawn on objects with `mesh.clips`. |
| `objects[].harvest` | `true` for something gathered by hand, like the oil geyser: pressing Use (E) on it (the `Harvest` action) puts its `drops` straight into the inventory, leaves what does not fit on the ground and uses the object up. Not with `refine`. Default `false`. |
| `objects[].regrow` | Seconds of world time (the scripts' clock, which saves carry on) after the object is destroyed, by any means, until it stands again as new. The host restores it for every peer, and saves keep the times. Omitted: it stays gone. The oil geyser takes 3600 s, about Scrap Mechanic's 2.5 game days. |
| `objects[].model` | A built-in flora model (see [Flora](#flora)) drawn instead of `pieces`, such as `bush_berry`. |
| `objects[].solid` | `false` for an object players, bots and Creations walk and drive through, like the seed bush: its collider only catches the Sledgehammer's swing, so it still breaks for its drops. Default `true`. |
| `objects[].refine` | `true` for a raw resource node (tree, rock node): holding Use (E) on it for `refine_seconds` (`game/data/survival.json`, 5.2 s, as in Scrap Mechanic) refines it, and its `drops` go straight into the inventory. Moving more than `refine_cancel_distance` (1.5 m) or letting go cancels. Default `false`. |
| `objects[].damage_from` | Kinds of damage that hurt it: `sledge`, `drill`, `saw`, `weapon`, `explosion`, `bot`. Omitted: all of them. Rock deposit blocks take only `drill` (a Drill spun on a bearing at drilling speed). |
| `objects[].breaks_into` | Loose parts (`count` and weighted `parts[]`, as a part's `breaks_into` in the [Part Data Schema](part-data-schema.md)) it shatters into when it breaks, as well as any `drops`. `count_max` (at least `count`) makes the number vary per object, from `count` to `count_max`, picked from the seed. Vanilla deposit blocks break into one chunk (small), two chunks (medium) or three loose boulders (large). |
| `objects[].fell` | Makes a broken object fall like a tree instead of shattering: its `breaks_into` pieces stand end to end up the trunk from the ground to `height` metres (scaled with the object), the lowest `breaking.fell_gap` (`game/data/building.json`) clear of the ground, and topple away from the blow, the side it was hit from (a seeded direction when there is none), turning at `spin` rad/s about the foot as one trunk would. Needs `breaks_into`. |
| `objects[].sounds` | Sound events the object plays: `hit` for a blow that does not break it, `break` when it breaks (for a tree, its fall). Omitted: the tool's own sounds. |
| `objects[].debris` | What flies off when it is hit (cosmetic, drawn by every peer): `chips` (`#rrggbb` colours) from the point hit, and `leaves` from a sphere of `canopy_radius` metres round `canopy`, a few per hit and a shower when it falls. A tree drawn close up also shakes when it is hit. |
| `objects[].mesh` | A `.glb` or `.gltf` model (`file`, centred at `at`) drawn instead of `pieces`, scaled with the object. `variants` lists other models of the same size that rock deposit blocks pick from, so neighbours differ. `lod` is a simpler model drawn instead beyond `lod_distance` metres (default 60) and in the distant scenery. |
| `objects[].mesh.clips` | Makes the model animated: each object is drawn as its own copy of it (not batched with its neighbours, so keep it for rare objects) playing its glTF clips by name. `appear` plays once when its chunk comes in (a Bloom Pod settling shut), then `idle` loops; `hit` plays when damage reaches it and it holds; when it breaks, a copy left in its place plays `break`, stays `break_seconds` (default 4) and shrinks away over the last `fade_seconds` (default 0.6). Any clip may be left out. `variants` and `lod` are not used. |
| `objects[].max_slope` | Steepest ground it is scattered on (rise over run), if flatter than the world's own limit. |
| `objects[].outcrop` | Makes the object a rock deposit: a cluster of mounds heaped from blocks, as in Scrap Mechanic's square rocks. `deposits[]` are the sizes (`weight`; `mounds`, `[min, max]`, at most 12; `spread`, metres from the middle the other mounds may stand; `sizes[]`, the weighted `mound` names to pick from). `mounds` maps each name to `blocks` (`[min, max]`, at most 64) and `kinds[]`, the weighted world `object`s it is heaped from; each needs a box `collider`, whose size is the block's (the deposit's own object may be one). Blocks are heaped largest first, each on the ground or with at least 45% of its underside on blocks below, turned up to 0.25 rad, and no two share space; mounds stand apart. A mound other than the first turns metal-rich with chance `rich_chance` (0 to 1): each block becomes `rich[its object]`. Each block is an object of its own (`rule.cell_x.cell_z.block`, the first `rule.cell_x.cell_z`) with its own object's look, health and `breaks_into`, so breaking one leaves the rest. Each mound sits on the lowest ground under its bottom blocks, less each block's `sink`. `footprint` is the deposit's radius: no block stands further out, and a mound that does not fit is left out. `scale` is shared by the whole deposit. |
| `objects[].scene` | Makes the object a scene: a little set piece laid out round each spot the rule scatters it to (see [Scenes](#scenes)). `layouts[]` are the ways it can look, each with a `weight` (default 1) and `pieces[]`: the world `object` to stand (not another scene or a rock deposit), `at` (`[x, z]` metres from the spot, within `footprint`), `yaw` (degrees, added to the scene's own turn) and `chance` (0 to 1, default 1) that it stands at all. A scene needs no `collider` and never stands itself; each piece is an object of its own (`rule.cell_x.cell_z.piece`, the first `rule.cell_x.cell_z`) with its own object's look, health and drops, sunk and scaled by its own `sink` and `scale`. A piece that would land off the world, on a POI or a road, in water or on ground too steep for it is left out. |
| `liquids[]` | What POI `pools[]` hold: `id` (a catalog id), `color` (`#rrggbb`), `glow` (how brightly the surface shines in its colour, 0 for none), `roughness` (0 to 1, default 0.1), the surface's look (`ripple_size`, metres, default 4; `ripples`, how deep they look, 0 still to 1, default 0.6; `flow`, how fast ripples and swirls drift in m/s, default 0.05; `sheen`, 0 to 1, an oil slick's drifting rainbow film; `bubbles`, 0 to 1; `edge_color`, `#rrggbb` scum or crust along the shore, omitted for none), `damage` (health a second a player standing in it loses, hurt every half second; the cause is the liquid's id) and `collect` (`item` and `count`: what an empty bucket dipped in it collects, the bucket staying empty; omitted, a bucket cannot hold it). Vanilla has `chemical` (glowing, bubbling green with pale scum, 10 a second, collects a Chemical) and `oil` (glossy black with a rainbow sheen, the ponds round oil geysers). |

Shapes (`collider`, `pieces`) are `box` (`size`), `sphere` (`radius`) or `cylinder` (`radius`,
`height`, upright), centred at `at`, turned `yaw` degrees about +Y, coloured `#rrggbb`. They are
placeholders until meshes are authored.

Scattered objects keep stable ids (`rule.cell_x.cell_z`), which is how a destroyed tree stays destroyed (trees never grow back).
Objects a POI's `object` markers place have ids of their own, `poi.poi_index.marker_index`.

### Bloom Pods

The loot pods scattered over the land are three objects on one model built by `tools/bloom_pod/build_bloom_pod.py`:
`bloom_pod_green` (Common loot, 1 to 3 picks, 30 health: three Sledgehammer blows), `bloom_pod_purple` (Epic, 1 to 3
picks, 50 health) and `bloom_pod_gold` (Legendary, always 3 picks, 80 health). Each glows in its colour and pulses
slowly. Green pods stand in the open; purple and gold ones are `perch`ed on cliff tops and ledges, and gold is the
rarest (about 60, 18 and 6 in the vanilla world). They settle shut when they come into view, shudder when hit and burst
apart when broken, leaving their loot in one pile.

### Scenes

Scrap Mechanic's land never goes long without something to look at: a fence line, a hay field with a scarecrow, a
wrecked harvester. Scenes do that between Mechanica's POIs. Each is an object with a `scene` (see the table above) that
the biomes scatter like any other, placed before their trees and rocks so those keep clear of it. The props are
modelled by `tools/structures/props*.py` (`meshes/world/props/`, built with `build_structures.py props`) in the
families of [art-direction.md](art-direction.md): Settler wood breaks into Scrap Wood Blocks and loose metal into Scrap
Metal Blocks, while the old colony's pipes, barriers and wrecks are too big to break.

| Scene | Biomes | What it lays out |
|-------|--------|------------------|
| `scene_settler_camp` | meadow, field, forest, coastal | a tarp shelter with a log pile and barrels, or a trough and fenced pen; a broken cart, a signpost |
| `scene_fence_line` | meadow, field, autumn forest | a run of rail fence with broken and missing sections, or a fenced corner |
| `scene_hay_field` | field | round bales and stacks round a scarecrow, a trough, a cart |
| `scene_colony_pipeline` | meadow, desert, mountain | a run of the colony's big pipe ending in a valve, barriers and a cable drum |
| `scene_harvester_wreck` | meadow, field, desert | a Colony harvester torn in half, barrels and spilled parts |
| `scene_logging_camp` | forest, autumn forest, burned forest | log piles among stumps, a cart, a shelter |
| `scene_roadblock` | desert | a line of concrete barriers, barrels, sometimes the harvester wreck behind |
| `scene_burned_camp` | burned forest | a scorched trailer, a stump, a broken fence |
| `scene_farmstead` | meadow, field | a pump well, outhouse, coop, washing line and woodshed behind a picket fence and gate; or a yard with a table, hives, a drying rack and compost |
| `scene_wind_pump` | field, desert | a farm wind pump over troughs in a pole-fenced paddock, sometimes a water tower |
| `scene_campsite` | meadow, forest, autumn forest, coastal | a fire ring with a log seat and tarp, or a cooking tripod between two tarps |
| `scene_breakdown` | meadow, field, desert, burned forest, coastal | a car or truck wreck with traffic cones, a sign and barrels |
| `scene_pylon` | meadow, field, desert | a 22 m power pylon (it reads from the air) with a transformer, a pole or a broken lamp |
| `scene_bus_stop` | meadow, field | a bus shelter, a bent street lamp, signs and sometimes a torn billboard |
| `scene_container_yard` | field, desert | shipping containers, one open, a forklift wreck, pallets and a generator |
| `scene_fuel_depot` | desert | fuel tanks, gas cylinders and a generator behind concrete barriers |
| `scene_radio_station` | mountain | a guyed radio mast with its hut, a dish and solar panels |
| `scene_salvager_crash` | meadow, forest, desert, burned forest, mountain | a crashed drop pod or cargo pods among hull plates, a wing and a thruster, with a supply cache and beacon |
| `scene_stone_ruin` | meadow, forest, autumn forest, mountain | Old Stone walls round a doorway, a fallen column, a standing stone, steps |
| `scene_stone_shrine` | forest, autumn forest, mountain, coastal | a giant stone head between standing stones, a bench, a cairn |
| `scene_old_well` | meadow, field | an old stone well with a trough and low walls |
| `scene_forest_floor` | forest, autumn forest | a fallen log, an upturned root plate, a hollow stump, mushrooms, a dead snag |
| `scene_burned_snags` | burned forest | dead snags, a fallen log and roots, sometimes a car wreck |
| `scene_desert_mounds` | desert | termite mounds, a dead snag, a cairn |
| `scene_farm_wreck` | field | a tractor wreck by hay, planks and sacks and a pole fence |
| `scene_workyard` | meadow | a truck wreck, a conveyor, pallets, an I-beam, a ladder and a table |
| `scene_temple_ruin` | meadow, autumn forest | an Old Stone dais with an altar, columns, braziers and lions, or a platform with a broken statue |
| `scene_stone_gate` | meadow, autumn forest | stone gate pillars or a small arch between walls, lanterns, bollards and a milestone |
| `scene_stone_tomb` | forest, autumn forest | a tomb with sarcophagi, urns, a tablet, a gargoyle and a carved relief wall |
| `scene_stone_plaza` | meadow | a ruined fountain ringed by curved benches, planters and a small stone head |
| `scene_aqueduct` | desert, mountain | an aqueduct section with broken pillars, fallen slabs and a few probe crystals |
| `scene_stone_circle` | forest, mountain | a ring of standing stones with rune stones, or a glyph ring round an orb pedestal |
| `scene_ring_gate` | mountain | a stone ring gate between glyph pillars, with a face mask and a stone hand |
| `scene_old_tower` | forest, autumn forest, mountain | a tower stump with a broken spiral stair, rubble and a bridge fragment |
| `scene_wayside` | meadow, forest, field, autumn forest | a milestone, carved boulder or pedestal with a lantern, urn or tablet |
| `scene_street_corner` | meadow | Colony street lamps, a bench, bins, a phone booth, hydrant, vending machine, kiosk and parking meters |
| `scene_town_square` | meadow | a dry fountain under the colony founder's statue, with benches, planters, flag poles and lamps |
| `scene_checkpoint` | field, desert | a toll booth with a barrier arm, barrier stacks, sandbags and cones |
| `scene_bus_crash` | meadow, burned forest | a bus wreck by a fallen traffic light, bins, a bench and cones |
| `scene_escape_pod` | meadow, forest, burned forest | a Salvager escape pod or cockpit section with ejected seats, a parachute, flares and med kits |
| `scene_salvager_wreck` | desert, burned forest | a Salvager hull rib with nacelle, thrusters, a cracked power core and debris, or a cargo container with crates |
| `scene_beacon_site` | burned forest, mountain | a Salvager navigation beacon with a sensor buoy, flare posts and crates |
| `scene_substation` | meadow, field | a fenced substation with transformer poles, a switch cabinet and a battery bank |
| `scene_wind_farm` | meadow, field, mountain | a Colony wind turbine with a switch cabinet, solar rows and a charging post |
| `scene_solar_field` | field, desert | rows of solar panels with a battery bank, data cabinet and weather station |
| `scene_survey_post` | meadow, mountain | a weather station, sensor mast, antenna array and a crashed survey drone |
| `scene_data_wreck` | meadow, burned forest | a wrecked server rack, data cabinet, fallen robot arm, charging post and a hatch |
| `scene_oil_field` | field, desert | pump jacks or a small derrick, a gas meter station, tanks, valves and drums |
| `scene_water_works` | meadow, field | a Colony water tower with a pump station, culvert, drain and retaining wall |
| `scene_roadworks` | meadow, field | a cement mixer wreck, barriers, rebar, cones, sandbags, a manhole and scaffold |
| `scene_quarry_works` | desert, mountain | a crusher under a conveyor tower with a hopper, excavator and dump truck, or a bulldozer by beams |
| `scene_plant` | meadow, field | a cooling tower, chimney, pipe rack, compressor and tanks, or silos and a smokestack under a pipe bridge |
| `scene_crane_site` | meadow, mountain | a mobile crane wreck among beam stacks, cable drums, scaffold and rebar |
| `scene_farmyard` | meadow, field | a silo, hay loft and corn crib round troughs, churns and a haystack, or a pig pen and goat shed |
| `scene_kitchen_garden` | meadow, field | a small greenhouse, cold frames, a potting bench, crates, pumpkins and a bee skep |
| `scene_field_edge` | field | a harrow, plough and scythe rack by a pole fence, or an irrigation pump and sprinklers |
| `scene_harvest_wreck` | field | a combine harvester wreck by hay, vegetable crates and a loose tyre |
| `scene_fishing_camp` | lake, coastal | a boathouse, rowboat, net rack and traps, or a beach hut with deck chairs and a kayak |
| `scene_harbour` | coastal | a pier section with a small harbour crane, mooring posts, a jetty light and an anchor |
| `scene_lighthouse` | coastal | a small lighthouse with a beach hut, anchor, beached buoy and a rock pool |
| `scene_shipwreck` | coastal | a fishing boat wreck with a beached buoy, floats, washed-up crates and a pallet raft |
| `scene_shore` | lake, coastal | dune grass and floats, a rock pool and sea stack, or a kayak and oar rack |
| `scene_traffic_jam` | meadow, field | a van, pickup and sports car wrecks, or a delivery truck with a motorbike and bicycle |
| `scene_tanker_spill` | desert | a tanker trailer wreck with chemical drums, a waste bin and warning signs |
| `scene_yard_trailers` | field | a flatbed and boat trailer with a quad, golf cart, engine block and seat |
| `scene_railway` | meadow, field | rail track with a wagon wreck and signal, or track to a buffer with mine carts |
| `scene_air_crash` | meadow, desert | a small plane or helicopter wreck with its tail, seats and debris |
| `scene_rover_wreck` | desert, burned forest, coastal | a Colony rover wreck with a survey drone and crates, or a hovercraft wreck |
| `scene_scrap_outpost` | meadow, desert | a Settler scrap hut behind a palisade with a watchtower, generator shed and bell, or a lean-to behind barricades |
| `scene_squatter_camp` | burned forest, coastal | lean-tos round a fire with a shower, latrine, tyre swing and sheet metal fence |
| `scene_scrap_power` | field | a scrap windmill, solar rig, battery rack, antenna and generator shed |
| `scene_junk_pile` | meadow, desert | a mixed scrap heap with tyres, car doors and an engine block, or sheet metal fence and barricades |
| `scene_river_crossing` | forest | rope bridge posts, a painted sign and a bell post |
| `scene_family_camp` | meadow, forest, autumn forest | tents round a fire with a camp kitchen, benches, chairs and firewood, or a stove, smoker and drying rack |
| `scene_wood_yard` | meadow, forest | a chopping block, firewood rack, sawhorses, logs, planks and a tool wall |
| `scene_smithy` | forest, autumn forest | an anvil under an awning with a workbench, grindstone, stove and tool wall |
| `scene_trader` | meadow, field | a market stall and trade cart with crates, pumpkins, sacks and a sign |
| `scene_boulder_field` | meadow, mountain | a split boulder, rock spire, ferns and brambles, or a rock arch with a nest |
| `scene_old_wood` | forest, autumn forest | a giant fallen tree with giant mushrooms and ferns, or a root arch over hollow logs |
| `scene_bone_yard` | desert | a rib cage and big skull among cacti and tumbleweed |
| `scene_beach_drift` | coastal | driftwood, seaweed rocks, a shell mound and dune grass |
| `scene_marsh_edge` | lake | a reed bed with cattails and a hollow log |

The big nature props (`props_nature.py`: split boulders, rock spires and shelves, giant mushrooms, hollow logs,
ferns, brambles, cacti, tumbleweed, driftwood, reeds) are also scattered one at a time in their biomes.

In the vanilla world about one stands every 6,000 m² of open land (about one per 64 m chunk), some 15,000 across
the island, so there is nearly always one in sight.

### Wild cotton, beehives and embers

Three crafting resources the block recipes need come from the vanilla world:

- **Cotton:** `wild_cotton` clumps (`tools/food/build_cotton.py`) stand in the Autumn Forest. Press Use on one to pick
  1 or 2 Cotton and, now and then, a Cotton Seed; the bolls grow back after `regrow` seconds. Cotton Seed grows into a
  cotton plant in soil like the other crops.
- **Beeswax:** the `scene_rock_hive` scene sets a `rock_beehive` (`tools/beeswax/build_beeswax.py`) against the side of
  a `hive_rock`, a large rock at one fixed size, in the Forest and the Autumn Forest. Breaking the hive drops 1 or 2
  Beeswax and leaves the rock.
- **Ember:** a `burnt_tree` in the Burned Forest drops 1 or 2 Embers when it falls, as well as its logs.

### Trees

The vanilla trees (Tree Harvesting spec) are four objects, plus the Autumn Forest's `autumn_leafy_tree` and the
Burned Forest's `burnt_tree`, with models from `tools/trees/build_trees.py` (numpy and Pillow only), which also builds
the logs and the Wood Rod. Each model has painted bark (colour and normal maps) and a canopy of alpha-masked
leaf-cluster cards round a solid core; the textures come from `tools/trees/tree_textures.py` and are embedded in each
file. `tools/capture-trees.sh OUT_DIR` shows every tree in a running world. Felled, each falls into loose log parts
(`mods/vanilla/editor/batches/forestry.json`); a log breaks into 2 Wood Rods with the Sledgehammer or a Saw Blade,
and a rod refines into 20 Scrap Wood Blocks.

| Tree | Grows in | Health | Hurt by | Logs | Rods |
|------|----------|--------|---------|------|------|
| Birch (`birch_tree`) | meadows, forest edges | 40 | sledge, saw, explosion | 2 to 3 Birch Logs | 4 to 6 |
| Leafy (`leafy_tree`) | forests, meadow copses | 80 | sledge, saw, explosion | 4 to 6 Logs | 8 to 12 |
| Spruce (`spruce_tree`) | forests | 80 | sledge, saw, explosion | 4 to 6 Logs | 8 to 12 |
| Pine (`pine_tree`) | forests, hills | 220 | saw only | 8 to 12 Pine Logs | 16 to 24 |

A Saw Blade cuts only once its input has spun it up (`spin_up`, `min_spin` in its part), so a saw that has just
been switched on does not bite.
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
plants of higher ones, and Low turns the wind off. At High, grass is drawn to 220 m, ground cover to 120 m and
bushes to 460 m (never past the view distance); past `thin_share` of the distance grass and ground cover keep
`thin_keep` of their plants, `thin_scale` times larger, so the far field stays full but cheap, and the ground texture
carries on beyond. Nothing pops in: plants sink away over the last `fade_share` of the distance, a chunk's near, far
and thinned meshes cross-fade over `blend_share` of it where they hand over, and a chunk's plants rise out of the
ground over `appear_time` seconds when first drawn.

## The map's layout

```json
{
  "regions": [
    { "biome": "forest", "at": [-0.40, -0.25], "size": 1.3, "mix": [{ "biome": "meadow", "chance": 0.08 }] }
  ],
  "rivers": [
    { "points": [[0.01, 0.07], [-0.03, 0.20], [-0.18, 0.58]], "width": [22.0, 34.0], "depth": 4.0 }
  ],
  "sites": [
    { "id": "poi_36", "label": "POI-36", "poi": "crash_site", "at": [0.19, 0.38], "biome": "field", "pad_height": 6.0 },
    { "id": "large_1", "pool": "large_slots", "at": [0.32, 0.18], "jitter": 140.0, "biome": "burned_forest" }
  ],
  "pools": [
    { "id": "large_slots", "fill": [{ "poi": "warehouse", "count": 1 }, { "poi": "farmbot_scrapyard", "count": 4 }] }
  ]
}
```

Positions are `[x, z]` fractions of the world, −0.5 to 0.5 (x east, z south), so the layout keeps its
shape at any world size.

| Key | Meaning |
|-----|---------|
| `regions[]` | A point (`at`) that the biome cells nearest it, for its `size` (bigger reaches further), take the `biome` of. `mix` swaps a cell for another biome with that `chance`, for patches (woods in a meadow). |
| `rivers[]` | A river's centre line through `points` (they may reach ±0.6, past the edge, so it runs out to sea), meandering up to `meander` metres by the seed. `width` is `[source, mouth]` in metres; the bed is `depth` metres under `sea_level`, so rivers and lakes fill with water at the sea's level. Its valley sides rise from the banks at `valley_slope` and steepen until they meet the land. |
| `sites[]` | A mandatory structure (World Map spec §4): a POI prefab (`poi`) or one drawn from a `pool`, centred at `at`. `id` is permanent (design work refers to it) and `label` is the spec's name for it. `jitter` (metres) lets the seed move it onto dry, level ground in its `biome`; the cells round it keep that biome in every world. `pad_height` fixes its pad (the crash site's pond needs its pad 4 m over the sea); `yaw` turns it (a multiple of 90); `road` overrides the prefab's road link. |
| `pools[]` | Prefabs (`fill`, each `count` times) shared out over the sites that name the pool, shuffled by the seed: the totals stay the same in every world. The counts must add up to the number of those sites. |

The vanilla layout gives each of the spec's 50 structures its own prefab (`pois/poi_01.json` …
`pois/poi_50.json`; POI-36 is `crash_site` and POI-37 the quests' `outpost`), so each can be designed on
its own. The story needs the first Workshop within 350 m of the crash site, so POI-37 sits about 270 m north
of it with a 50 m jitter (the seed moves a site at most `jitter` along each axis, so up to `jitter` × 1.41 in all), and
shares its `field` biome: a site's biome reaches about 0.7 × `biome_cell_size` round it and would repaint the start. Until then they are placeholders of the right footprint and height, written by
`tools/author-poi-placeholders.py` (edit its table and re-run). Six extra large slots draw a warehouse or a
Farmbot Scrapyard from the `large_slots` pool.

## Roads

```json
{
  "roads": {
    "width": 10.0, "curb": 0.0, "shoulder": 1.5, "max_grade": 0.24, "cell": 32.0,
    "bridge_clearance": 3.0, "bridge_cost": 6.0, "loop_factor": 1.7, "loop_reach": 2600.0, "max_loops": 14,
    "links": [["poi_36", "poi_37"], ["poi_37", "poi_38"]],
    "shoulder_ground": "dirt", "track_width": 4.0, "track_max_grade": 0.32, "track_reach": 650.0, "track_ground": "dirt"
  }
}
```

Every site whose link is `road` (the prefab's `road`, or the site's) is joined into one network: first
the `links`, in order, then a minimum spanning tree over the rest, then up to `max_loops` loops between
sites within `loop_reach` metres that the network makes go more than `loop_factor` times round. Routes
are found over a grid of `cell` metres that prefers gentle ground, avoids other POIs and crosses water
at `bridge_cost` times the price; deep lakes and the sea are never crossed. Each route is straightened,
curved (its corners rounded to a 15 m radius where the straights leave room, so a switchback swings round
wide instead of folding the road over itself), and graded so it never climbs steeper than `max_grade` (0.24 is about 13.5°, spec §3: 12-15°);
where it crosses water it becomes a bridge at least `bridge_clearance` metres over the surface. Where
three or more routes meet there is a junction: a T or a crossroads, its arms at right angles, turned to
suit the roads that meet there. Each road leaves its junction straight down its own arm and then bends
evenly into its route, and the junction is a level plate with rounded corners between its arms; the
roads ease from level into their grade past it. A road running straight through a T keeps its centre
line and has no stop lines. (A junction where more than four roads meet keeps a round plate.)

Roads are `width` metres of asphalt with white edge lines, a double yellow centre line and stop lines
at junctions, between concrete curbs `curb` metres wide (0, as in vanilla: no curbs), with `shoulder` metres of `shoulder_ground`
either side. Without curbs the asphalt's sides, its ends at POIs and the junction plates' rims slope down under the
shoulder, so wheels roll on and off anywhere. Bridges have a deck, parapets, piers, hazard posts and a caution sign at each end. The
ground is cut and filled level with the road (a track running in beside a road never pulls the road's own bed down), and nothing is scattered or grows on it (no grass, flowers or bushes,
even where the road crosses a POI that grows its own flora).

POIs whose link is `track` get a dirt track (`track_width` metres of `track_ground`, at most
`track_max_grade`) to the nearest road or track, meeting it at a right angle: a site always gets one, a scattered POI only when a
road is within `track_reach` metres.

## `pois/<id>.json`

Another mod's POI is changed or removed through `overrides/<mod>/pois/`, a `.patch.json` or `remove.json`; see the [Packaging Guide](packaging-guide.md#overriding-bots-quests-pois-and-characters).

```json
{
  "id": "outpost",
  "name": "Workshop",
  "radius": 40.0,
  "size": [16.0, 4.3, 16.0],
  "road": "road",
  "placement": {},
  "terrain": [{ "op": "flatten", "radius": 18.0, "blend": 20.0 }],
  "pieces": [{ "shape": "box", "size": [16.0, 0.3, 16.0], "at": [0.0, 0.15, 0.0], "color": "#a3a093" }],
  "markers": [{ "type": "part", "ref": "fabricator", "at": [0.0, 0.3, -5.0], "yaw": 180.0 }]
}
```

| Key | Meaning |
|-----|---------|
| `radius` | Footprint, including terrain blending. POIs never overlap, and nothing is scattered inside. |
| `placement.fixed` | `[x, z]` of a single POI, with optional `pad_height` and `yaw` (a multiple of 90). Give this, `spacing`, or neither (`"placement": {}`): then only a site places it. |
| `placement.spacing` | `[min, max]` metres between neighbours of this kind. `min` must be at least twice the radius. Needs `biomes`. A spaced POI is turned a random quarter turn. |
| `placement.priority` | Higher kinds are placed first and win contested ground. |
| `placement.clearance` | Metres a spaced POI's footprint keeps clear of every other POI's footprint and of the roads. |
| `placement.roadside` | For a spaced POI that stands beside a road (a roadside kiosk): metres between the road's bed and its footprint (at least `clearance`). Such POIs are laid along the roads, not the tracks, every `spacing` metres on a side the seed picks, turned so their −z side faces the road, and only where the ground is within 1.5 m of the road's height. |
| `placement.shore` | For a spaced POI on a shore: open water (a lake or the sea) must lie within this many metres of its footprint. |
| `size` | The footprint as built, `[width, height, depth]` in metres, for the placement manifest and design work. |
| `road` | What joins it to the roads: `road` (only where a site places it), `track` (a dirt track) or `none` (the default). |
| `bot_free_radius` | Metres from the POI centre inside which no bot appears: no wild bots (on land or in water), no guards at `bot` markers and no raiders (a raid on a farm inside comes in from just past the edge). Bots may still walk in, and one spawned by hand in Creative may stand there. Omitted: bots appear right up to the POI. The crash site keeps 250 m clear so a new player starts in peace. |
| `terrain[]` | `flatten` levels a disc to the pad height; `basin` sinks it `depth` metres below the pad (below `sea_level` it fills with water). A basin's `blend` must be at least 1.5 × `depth`, so its bank is no steeper than the 45° a player can walk up and a pond is never a trap. Full strength inside `radius`, fading over `blend`, centred at `at` (`[x, z]`). `wobble` (0 to 0.5, default 0) makes the edge wander in and out by that fraction of the radius, in lobes of the POI's own, for a natural pond; it narrows the blend where the edge pulls in, so the basin rule then needs `blend` × (1 − `wobble`) ≥ 1.5 × `depth`. Must stay inside the POI radius, `wobble` included. |
| `pieces[]` | Static structure, as shapes. Positions are relative to the POI centre on its pad. |
| `models[]` | Structures as models: `file` (a `.glb` or `.gltf`, relative to the POI's mod or `other_mod:path`, usually under `meshes/structures/`), `at` (`[x, y, z]` of the model's origin, relative to the POI centre on its pad) and `yaw` (degrees, counter-clockwise from above, added to the POI's turn). A mesh named `collider` is its collision shape and is not drawn; without one the drawn meshes collide, both faces of every triangle, so walls hold from inside too. `"solid": false` makes it scenery only. Point lights in the file become room lamps. A model is placed by the terrain chunk that holds its origin and stays loaded with that chunk, so it is never cut at a chunk border. How to make one is in the [Art Direction](art-direction.md#models-in-the-game) guide. |
| `models[].story` | Makes the model animated and follow a quest, like the SV-04's battery bay: `quest` (a quest id, `other_mod:quest` for another mod's) and the glTF clips `waiting` (looped until the quest is done), `finish` (played once when the quest is done while the model is in view) and `done` (looped from then on). At least one clip. Each placement is its own copy of the model, so keep it for a few special pieces; its `collider` mesh (or, without one, its meshes in the file's rest pose) is what collides, and it does not move with the clips. Every peer plays the clips from the quest state the host sends. |
| `ground[]` | Ground painted on the footprint, in order, each over the ones before: `ground` (an id from `grounds[]`), full inside `radius` of `at` (`[x, z]`, default the centre) and fading over `blend`, with an edge pushed in and out by up to `noise` metres. Must stay inside the POI radius. |
| `flora` | Id of a biome whose flora grows on the footprint (outside bare ground). Omitted: nothing grows inside the POI. The crash site burns its middle and grows a meadow round it. |
| `markers[]` | Points for other systems: `type` (`spawn`, `item`, `part`, `bot`, `fire`, `door`, `npc`, `core`, ...), `at` (`[x, y, z]` relative to the POI centre on its pad), `yaw` (degrees about +Y, counter-clockwise from above, added to the POI's turn; 0 faces −Z) and an optional `ref` (a catalog id, or a character id for `npc`). The first `spawn` marker of a fixed POI is where players start. A `bot` marker is a guard post: in Survival its bot (`ref`, a bot of this mod unless qualified) appears when a player comes near, leaves when everyone has gone, and is replaced some time after it is killed (distances and the wait are in the game's `data/bots.json`). [Quests](quests.md) point at markers. An `npc` marker draws the [character](characters.md#non-player-characters) its `ref` names standing there, facing the marker's `yaw`. An `object` marker places the world object `ref` names (`objects[]`, such as `oil_geyser`) on the ground under it, raised by its `at` height. A `part` marker puts a working part there that belongs to the world (see [World-owned parts](#world-owned-parts-part-markers)). An `item` marker is only a note for design work for now: nothing appears there. |
| `pools[]` | Round pools of a liquid (`liquid`, an id from `liquids[]`), usually laid in a `basin`: the surface is a disc of `radius` metres round `at` (`[x, z]`), `level` metres above the pad (below 0 in a basin). The liquid fills only the part of the disc where the ground is lower than the surface, so the shore follows the basin's (wobbling) bank: make the disc reach past it. Nothing grows in it. Must stay inside the POI radius. |

The pad height of a spaced POI is the natural ground height at its centre.

### World-owned parts (`part` markers)

A `part` marker puts one working part in the world: every Workshop has a Fabricator this way.

```json
{ "type": "part", "ref": "fabricator", "at": [7.25, 0.02, -10.6], "yaw": 180.0 }
```

- `ref` is a part id: a bare id is a part of the POI's own mod, `other_mod:id` another mod's. It must be a part the
  loaded mods have and that can be placed; otherwise the POI fails to load with an error at `$.markers[i].ref`.
- `at` is the point on the floor the part stands on: the middle of its bottom face. `yaw` turns it about +Y like every
  marker: at 0 the part's front (its local −Z) faces −Z, at 180 it faces +Z. The POI's own quarter turn is added.
- The host puts the part there as a normal one-part Creation when the world starts, so its script, station, storage
  and menus work exactly as a player's do, for the host and every client. It is anchored (nothing knocks it over or
  pushes it), simulated by the host, and streamed like any still Creation: it comes in with the ground under it when
  a player comes near and is parked when everyone has left, never cut at a chunk border.
- It is **world-owned**. Anyone can use it: open it, craft, take what it made, put things in and take them out, change
  its settings. Where it stands, nobody, host or client, in Creative or Survival, can build onto it, weld it or weld
  to it, lift or capture it, paint it, upgrade it or wire it; each is refused with "The Fabricator belongs to the
  world: it can't be …".
- A player **may pick it up** (empty first, like any part that holds items). It goes into their pockets as a normal
  Fabricator to put down wherever they like, and the marker is taken for good: it is saved with the world's destroyed
  objects as `poi.<POI index>.<marker index>`, and that marker never places another. Hammers, weapons, blasts, bots and fire do it no damage, and a bot never
  attacks it. Clearing the Test Sandbox leaves it, and a blueprint of it is the player's own.
- It is saved with the world (its Creation carries `world_part`, the marker's key `mod:poi#marker@index`: the POI's
  id, the marker's index in `markers` and the POI's index in the world), so what it holds and is crafting stays. When
  a world loads, each marker not taken gets exactly one: a saved one is kept (and put back on its marker if the marker moved), a
  missing one is made (an older save, or a POI a mod added), and one whose marker is gone is removed. It is never
  made twice, however often the world is loaded or the area entered.
