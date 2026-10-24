# Art Direction: Structures

Every building, ruin and wreck in the world follows one design language, so a player can tell who built a structure,
and when, from its shape and colour alone. This guide is for anyone modelling a structure for vanilla or a mod: the
four families, their palettes, the rules every structure follows, the sizes, and how the vanilla structures are built
with `tools/structures/`.

The look is the part models' look (the Fabricator, the Gas Engine, the switch and the wheels) scaled up. It is
stylised, not realistic and not cartoony.

## The story behind the families

The colony AI built this world, it fell, the bots split into factions, and survivors patched together what was left.
Before the colony, someone else lived here and left stone. The player is a salvage engineer whose ship, the SV-04
Salvager, came down at the crash site.

## The four families

| Family | Who built it | Shapes | Palette | Light | Used for |
|--------|--------------|--------|---------|-------|----------|
| **Colony Works** | The colony AI, before the fall | Framed box modules (steel-blue rails round bone panels) with one rounded signature volume per building (an office drum or a capsule tank), roll-up doors, portholes, pipe runs and square lampposts | Bone `#D9D1BC`, steel blue `#3E6A8C`, mustard `#E0B530`, graphite trim `#2B2F35`, signal red `#C8423A` | Warm white light bars; cyan on anything still powered | Workshops, warehouses, relay beacons, the AI Core Facility, roadside kiosks |
| **Settler Patchwork** | Survivors and farmers after the fall | Colony parts rebuilt from salvage: half domes and lean-tos of mismatched plates on rusty ribs, plank walls, trade counters with awnings, fans, lamps and pipes bolted on | Faded colony colours (bone, sea teal `#4F9A93`, brick red `#A8473B`, mustard), rust `#7A4A30`, scrap wood `#9C7650`, zinc `#9AA3AB` | Bare bulbs on racks, warm yellow `#FFF1CC` | The farmer's shack, the farmers' hideout, trader stalls, camps, farming patches, scrapyards, the haymaze, the Scrap City garage |
| **Old Stone** | Whoever lived here before the colony | Dressed stone blocks, pillars, arches, steps and flagstone floors, cut by time | The rock material: stone `#8F9289`, dark stone `#5F645C`, dark cracks, moss on top edges | None, or cyan probe crystals | The stone court and arches at the crash site, forest ruins, ruin clusters |
| **Salvager Tech** | The player, and the player's ship | Exactly the part models: framed cases with inset panels, rails and corner blocks, vents, screens with labels, copper coils and pipes, hazard bands | Hull `#24282E`, gunmetal `#5A6068`, cyan `#38C8FF`, copper `#B8733A` | Cyan running lights and screens | The SV-04 Salvager wreck, the Fabricator, every craftable part |

Cyan belongs to the player and to anything that still has power. It is the UI accent and the Bloom Pod glow, so
players learn that a glow means "interact with this". Copper stays on machinery. Big structures never use orange as a
base colour: it is Scrap Mechanic's signature, and the UI bans it too.

## Rules every structure follows

1. **Silhouette first.** A structure is two or three primary volumes, and a landmark has to read at 200 m as a
   silhouette alone. Detail sits on those volumes.
2. **Framed construction.** Walls are panels held in rails, with posts every 2 to 3 m and corner blocks, like the
   Fabricator and engine cases. Edges get small bevels: 2 to 5 cm on props and 5 to 10 cm on buildings, never big
   toy radii.
3. **Round where it's real.** Tanks, pipes, portholes, lamps, fans, coils and dome shells are round. Each Colony
   building may have one rounded signature volume. Everything else is boxy.
4. **Show the hardware.** Screws, bolts, vents, grilles, hinges, conduits and copper pipe are visible, at believable
   sizes, slightly oversized on buildings.
5. **Worn, not filthy.** Painted metal shows scratches, chipped edges, dirt in seams and rust streaks under bolts, as
   on the Scuttlers and the Gas Engine. Only ruins and wrecks get broken geometry.
6. **Light means come here.** Emissive strips, screens and lamps mark entrances, traders, stations and loot. Cyan
   means still powered.
7. **Labels and pictograms.** Short stencilled labels and model codes (FAB-1, OUTPOST 7, SV-04) go on machines and
   buildings, and pictograms on signs. There are no long text signs, so signs work in any language.
8. **Nature reclaims.** Grass, flowers and moss gather at bases. Every structure meets the ground with a pad, kerb,
   rubble or plants.
9. **Build to the player.** Sizes are multiples of 0.25 m (the build grid). The player is 1.8 m tall, steps up 0.35 m
   and jumps about 1 m.

## Sizes

| Thing | Size |
|-------|------|
| Door | 1.25 m wide, 2.5 m tall |
| Vehicle door | 6 m wide |
| Storey | 3.5 m |
| Counter | 1.0 m tall |
| Step or doorway sill | at most 0.3 m, so the player walks over it |
| Ramp | at most 45° |
| Interior a player runs through | at least 2.5 m clear |

## Models in the game

A structure is a `.glb` under `meshes/structures/`, placed by a POI prefab's `models[]` (see
[World Generation](world-generation.md#poisidjson)). It is drawn and collided with as it is, so:

- **Origin** on the ground at the structure's centre, +Y up, front towards +Z.
- **Collider.** Add a mesh named `collider` (or starting with it) holding only the parts a player should bump into:
  walls, floors, big machines. It is not drawn. Leave out screws, labels and anything under about 0.5 m, so the
  player never snags on detail. Without a `collider` mesh the drawn meshes collide. In `kit.py`, `no_collide` keeps a
  part out of it (a cable, leaves) and `collide_only` puts in a part that is not drawn (a smooth ramp under steps).
- **Lights.** Point lights in the file become room lamps: their colour is kept, and every lamp gets one room-sized
  brightness and reach. They cast no shadows and fade out beyond 40 m, so a structure can have several.
- **Materials.** A flat base colour times a grey detail texture, with an optional normal map and `COLOR_0` vertex
  colours (grime at the base, moss on top faces), which multiply the base colour. Emissive materials glow.
- **Budget.** About 60,000 triangles and 6 MB for the largest model (the Workshop, furnished inside); a plain building
  half that; props a few thousand.

## The Workshop

Every Workshop (the story's first one, POI-37, and the others on the map) is the same Colony Works vehicle repair
shop, `meshes/structures/workshop.glb`, built by `tools/structures/workshop.py`:

- **Hall.** 22 x 16 m, framed bone and steel-blue walls with clerestory windows under a pitched seamed roof, and a
  mustard WORKSHOP sign on the front gable.
- **Two bays** 8.5 m wide and 5.8 m tall, their roll-up doors always up, so a creation drives straight in. Bay 1 is a
  drive-through, with a second roll-up door in the back wall, and the tool wall and bench along its side; bay 2 is
  clear for the player's creation, with the Fabricator against its back wall on a hazard-edged plinth.
- **Office** on the left, walked into from the forecourt or through a doorway from the hall: a desk with a
  terminal, a couch, lockers, filing cabinets and a notice board.
- **Forecourt.** A 34 x 30 m slab, flush with the ground (the prefab sinks the model 0.28 m, leaving a 2 cm edge)
  so creations drive on from any side. It has seen use but is kept: saw-cut joints, hairline cracks, patched slabs,
  tyre scuffs into the bays, oil drips, worn bay lines, dirt drifted against the walls and a few weeds in the cracks.
  Not a dump.

The Fabricator is not part of the model. Each Workshop prefab has a `part` marker where it stands, and the host
puts a working Fabricator there that nobody can break; a player may pick it up and take it elsewhere (see
[World Generation](world-generation.md#poisidjson)).

## The crash site

The SV-04 Salvager is a Salvager Tech container freighter, 62 m long with a dark hull: a bridge and cargo control in
the bow, a spine carrying racks of coloured cargo containers on both sides, and an aft block with the crew quarters,
mess, engineering and two engine housings. `tools/structures/cargo_ship.py` builds it whole (the review renders) and
`tools/structures/crash_site.py` breaks it up.

The story: the ship came in from the south-east, blew open over the forest and lost its containers, then ploughed into
the clearing and broke in four. What lies there:

- **The wreck**, four walk-in pieces in a burnt scar with the bow to the north-west: `sv04_aft.glb`,
  `sv04_spine_a.glb`, `sv04_spine_b.glb` and `sv04_bow.glb`. Their bellies are dug in so every hatch sill is low
  enough to step or jump over. The Power Core is in engineering, port side of the aft block, and is drawn by
  `battery_bay.glb` with its story clips (see the Getting Started quest).
- **The port engine housing** (`sv04_housing.glb`), torn off by the explosion, lying on its own west of the aft block.
  The Master Battery was blown out with it and lies in the grass nearby.
- **Twelve burst containers** (`sv04_container_<colour>.glb`) trailing back along the flight path, each on a crater
  with its cargo spilled in front of its doors. A container's colour says what it carried, and each spill is a world
  object (`cargo_spill_<kind>`) the player empties with Use: red metal, blue electronics, yellow fuel, green farming,
  tan building materials. The drops are in `world/worldgen.json`.
- **Ten fires**: one in each of the wreck's hatches and openings, and four at burst containers. They burn until water
  puts them out; the first quest asks for three. Three Fire Extinguishers (`dropped_extinguisher`, two each) lie by the
  spawn, enough foam for all ten.
- The crew's things inside (lockers, a microwave in the mess, a file cabinet on the bridge, a loot crate in cargo
  control), scrap piles round the hull, and the older site round it: the Old Stone court, the farm and pond, the dead
  relay beacon and the burnt woods.

Every number (where the pieces lie, the container slots and where each container landed, the fires) lives in
`tools/structures/sv04_layout.py`, which the models, the Power Core build and the opening share.
`python3 tools/structures/print_crash_layout.py` prints them in the POI's frame, for
`mods/vanilla/pois/crash_site.json` and `game/data/opening.json`; copy what changed after editing the layout.

## The opening cinematic

The first time a world started from the menu loads, the host watches the crash happen. It is 31 seconds and in-engine.
The intact SV-04 flies in low over the forest. Its port engine housing catches fire, then the ship blows open: the
housing tears away and falls on its own, and the containers break off one by one as the ship passes over the spot
each one lands on, tumbling down with a cloud of dust. The ship ploughs into the clearing and breaks in four. The
picture then goes black and comes back on the player getting up at the spawn, facing the wreck. Jump or Esc skips it.
Players who join later spawn straight in, and a loaded save never plays it. The world itself never changes, so nothing
is sent to other peers.

What flies are copies of the wreck's own models: the four pieces, the housing and the twelve containers. Each comes
to rest exactly where the crash site places it. While they fly, the following stay out of sight until they land:

- the real models;
- the loot crates, scrap piles and cargo spills near the wreck (`hidden_objects`, within `hide_radius`);
- the site's fires.

The HUD is hidden, quest dialogue waits and the player stands still.

`game/data/opening.json` holds the cinematic:

- the ship's flight, as timed keys;
- when the housing catches fire, when the ship explodes (and where on it), when the housing comes down;
- the containers' fall time and tumble;
- when the ship touches down, breaks and settles;
- the camera shots, shakes, sounds and fades.

The ship rises over hills in its way, so the flight clears the forest in every world, and the cameras never go under
the ground. It is an engine setting: it ships with the build and is not moddable. Its `pieces` (each piece's pivot
and where it lies), `housing_on_ship`, `housing_lying` and `containers.slots` repeat `sv04_layout.py`. Change them
together: `OpeningSceneTests` checks that the pieces put back together make the intact ship, that every container
rides in its rack slot and that each one is down before the ship lands.

It only plays at a crash site that places those models and holds the spawn. A mod that moves the spawn or
replaces the crash site without them simply starts without it. To see it, run `tools/capture-opening.sh OUT_DIR`,
which saves stills through it. With `MOVIE=1` it also records the whole of it, sound included, to `opening.avi`.

## Building the vanilla structures

The vanilla structures are made procedurally in Blender (as a Python module, no Blender UI), from scratch:

```sh
pip install bpy==5.2.2 numpy pillow
python tools/structures/build_structures.py
```

| File | What it holds |
|------|---------------|
| `kit.py` | Materials (paint with chipped edges, grime, moss, cracks), shape helpers (bevelled boxes, cylinders, bolts, pipes, lamps) and `export`, which writes a model with its `collider` mesh and lights |
| `textures.py` | The shared detail textures and normal maps (paint, metal, stone, wood, concrete, worn concrete), from seeded noise |
| `frame.py` | Framed construction: framed boxes, vents, labels, screens and hazard bands |
| `families.py` | The palette and the Colony Works, Settler Patchwork and Old Stone structures |
| `cargo_ship.py`, `crash_site.py`, `sv04_layout.py` | The SV-04 Salvager whole, its wreck, and the layout they share (Salvager Tech; see [The crash site](#the-crash-site)) |
| `ship.py` | The starting scrap piles' plates and the older Salvager pieces |
| `workshop.py` | The Workshop (Colony Works) |
| `landmarks.py` | The eight landmarks (all four families; see [World Generation](world-generation.md#landmarks)), whose stairs, ramps and catwalks collide as walkable ramps (`build_structures.py landmarks` builds only them) |
| `stone_kit.py` | The Old Stone kit: coursed walls (whole, broken, collapsed) with doors, windows and arches, pillars, columns, arches, stairs, flagstone floors and slabs, rubble, fallen blocks, parapets, a tower with its stair inside, and houses; each piece adds its plain collider boxes and ramps |
| `ruins.py`, `ruin_city.py` | The Old Stone ruins built from it: Small Ruin, Ruin, Forest Ruin, Mountain Lookout, and Ruin City in seven models (`build_structures.py ruins` builds only them); `walk_probe.py` checks a model's collider can be walked to its markers, `render_ruins.py` renders them for review |
| `settler_kit.py` | The Settler Patchwork kit: plate, plank and sheet walls, half domes and lean-tos on ribs, salvage doors, awnings, the trade counter, decks, stairs, the scaffold lookout, fences of scrap, bolted-on fans, lamps and pipes, junk piles, hay bales, colony capsules, vaults and cranes |
| `settlers.py` | The settler sites built from that kit: the farmer's shack, the farmers' hideout, the camps, both scrapyards, the haymaze and the Scrap City garage (`build_structures.py settlers [names]` builds only them) |
| `walk_settlers.py` | Walks a settler site's collider as a player (0.35 m steps, 1.9 m headroom): every marker stands on ground in the clear and in reach, and each spot `settlers.WALK` names can be reached (`python3 tools/structures/walk_settlers.py NAME...`) |
| `render_settlers.py` | Review renders of the settler kit sheet and sites, each with a 1.8 m player |
| `cliff_rocks.py` | Sculpted rock that dresses the 8 m cliff terraces, in the stone and with the voxel-remeshed, worn, bedded and faceted sculpt of the rock formations (`rock_formations.py`): bedded walls 13 to 16 m long (`cliff_wall_a` to `_c`), a 60° convex corner, 4 to 5 m outcrop bands and scree fans, each with its origin on the ground at the foot of its face, the face towards +Z, the back running into the hillside, and a low-poly `collider` (`build_structures.py cliffs` builds only them); `render_cliffs.py` renders the kit and a dressed step for review |
| `build_structures.py` | Exports every vanilla structure and the starting scrap piles |

The outputs are committed. Re-run the build after changing any of these files and commit the result; it is
deterministic. To see a structure in the world, run `tools/capture-crash-site.sh OUT_DIR`, which renders the crash
site from the spawn, from above and inside the ship, `tools/capture-workshop.sh OUT_DIR` for the first Workshop, or
`tools/capture-landmarks.sh OUT_DIR` for the landmarks.

A new structure uses the palette constants and the builders in `families.py` and `frame.py`, so it follows the rules
without anyone having to remember them. Show renders for approval before a new structure merges.
