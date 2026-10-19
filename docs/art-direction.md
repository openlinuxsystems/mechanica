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
| **Colony Works** | The colony AI, before the fall | Framed box modules (steel-blue rails round bone panels) with one rounded signature volume per building (an office drum or a capsule tank), roll-up doors, portholes, pipe runs and square lampposts | Bone `#D9D1BC`, steel blue `#3E6A8C`, mustard `#E0B530`, graphite trim `#2B2F35`, signal red `#C8423A` | Warm white light bars; cyan on anything still powered | Workshop Outposts, warehouses, relay beacons, the AI Core Facility, roadside kiosks |
| **Settler Patchwork** | Survivors and farmers after the fall | Colony parts rebuilt from salvage: half domes and lean-tos of mismatched plates on rusty ribs, plank walls, trade counters with awnings, fans, lamps and pipes bolted on | Faded colony colours (bone, sea teal `#4F9A93`, brick red `#A8473B`, mustard), rust `#7A4A30`, scrap wood `#9C7650`, zinc `#9AA3AB` | Bare bulbs on racks, warm yellow `#FFF1CC` | The farmer's shack, trader stalls, camps, farming patches |
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
  player never snags on detail. Without a `collider` mesh the drawn meshes collide.
- **Lights.** Point lights in the file become room lamps: their colour is kept, and every lamp gets one room-sized
  brightness and reach. They cast no shadows and fade out beyond 40 m, so a structure can have several.
- **Materials.** A flat base colour times a grey detail texture, with an optional normal map and `COLOR_0` vertex
  colours (grime at the base, moss on top faces), which multiply the base colour. Emissive materials glow.
- **Budget.** About 30,000 triangles and 4 MB for the largest model (the ship's fore section); props a few thousand.

## Building the vanilla structures

The vanilla structures are made procedurally in Blender (as a Python module, no Blender UI), from scratch:

```sh
pip install bpy==5.2.2 numpy pillow
python tools/structures/build_structures.py
```

| File | What it holds |
|------|---------------|
| `kit.py` | Materials (paint with chipped edges, grime, moss, cracks), shape helpers (bevelled boxes, cylinders, bolts, pipes, lamps) and `export`, which writes a model with its `collider` mesh and lights |
| `textures.py` | The shared detail textures and normal maps (paint, metal, stone, wood, concrete), from seeded noise |
| `frame.py` | Framed construction: framed boxes, vents, labels, screens and hazard bands |
| `families.py` | The palette and the Colony Works, Settler Patchwork and Old Stone structures |
| `ship.py` | The SV-04 Salvager (Salvager Tech) |
| `build_structures.py` | Exports every vanilla structure and the starting scrap piles |

The outputs are committed. Re-run the build after changing any of these files and commit the result; it is
deterministic. To see a structure in the world, run `tools/capture-crash-site.sh OUT_DIR`, which renders the crash
site from the spawn, from above and inside the ship.

A new structure uses the palette constants and the builders in `families.py` and `frame.py`, so it follows the rules
without anyone having to remember them. Show renders for approval before a new structure merges.
