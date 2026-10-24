"""Build the crash site's structure models (docs/art-direction.md) as .glb files under mods/vanilla/meshes/.

What it builds (procedurally, from scratch: no .blend inputs, no downloads), each with its origin on the ground at the
structure's centre (glTF +Y up, front towards +Z):

* ``meshes/structures/sv04_{aft,spine_a,spine_b,bow}.glb`` and ``sv04_housing.glb`` - the SV-04 Salvager, a salvage
  freighter (Salvager Tech; cargo_ship.py), as it lies after the crash (crash_site.py, laid out by sv04_layout.py):
  the aft block (engine room, engineering, medbay, crew quarters, mess), the cargo spine broken in two with four
  containers still crushed in its racks, the bow (bridge, cargo control, the starboard airlock), and the port engine
  housing the explosion tore off. All share the wreck's origin, so the crash site places them at the same point.
  ``sv04_container_<colour>.glb`` are the containers lost in the sky, burst open where they landed, and
  ``meshes/world/cargo_spill_<kind>.glb`` the heaps of cargo they spilled (world objects).
* ``meshes/structures/relay_beacon_dead.glb`` - a dead Colony relay beacon (Colony Works).
* ``meshes/structures/settler_shack.glb`` - the farmer's half-dome shack with its trade counter and fenced garden
  (Settler Patchwork).
* ``meshes/structures/stone_arch.glb``, ``stone_arch_broken.glb``, ``stone_pillar_{tall,mid,short}.glb`` and
  ``stone_court.glb`` - Old Stone ruins: arches, pillars, and a flagstone court with steps.
* ``meshes/structures/workshop.glb`` - the Workshop (Colony Works): a vehicle repair shop with two always-open bays,
  an office and a worn forecourt. Every Workshop POI uses it; its Fabricator is a part the POI places (a ``part``
  marker), not part of the model.
* ``meshes/structures/warehouse_small.glb``, ``warehouse.glb`` and ``warehouse_large.glb`` - the three Colony Works
  warehouses (2, 3 and 4 floors; warehouses.py, from the kit in colony_kit.py): exteriors with their floors, the stair
  inside, a fire escape to the roof, and each one's silhouette piece (a water tower, a flue stack, a tower crane). Their
  colliders are the kit's plain boxes and ramps (walls round the openings, slabs, stairs walked as ramps, railings).
* ``meshes/structures/small_ruin.glb``, ``ruin.glb``, ``forest_ruin.glb``, ``mountain_lookout.glb`` and
  ``ruin_city_{centre,keep,west,east,northeast,southwest,southeast}.glb`` - the Old Stone ruins (ruins.py and
  ruin_city.py, from the kit in stone_kit.py): a chapel on its terrace, a hall with its dais and gallery, a forest
  shrine, a broken watchtower climbed to its top, and a walled stone town in seven models that share its origin. Their
  colliders are the kit's plain boxes and ramps (each course of a wall, floors, stairs walked as ramps, rubble banks).
* ``meshes/structures/farmer_shack.glb``, ``farmers_hideout.glb``, ``campsite.glb``, ``large_camp.glb``, ``camp.glb``,
  ``farmbot_scrapyard.glb``, ``capsule_scrapyard.glb``, ``haymaze.glb`` and ``scrap_city_garage.glb`` - the Settler
  Patchwork buildings (settlers.py, from the kit in settler_kit.py and the settler props), each on its POI's footprint
  with its markers' ground kept clear. Their colliders are the kit's plain boxes and ramps; walk_probe.py walks them.
* ``meshes/structures/packing_station_vegetable.glb``, ``packing_station_fruit.glb``, ``silo_cluster.glb``,
  ``silo_district.glb``, ``growlab.glb``, ``chemical_lake_facility.glb`` and ``roadside_kiosk.glb`` - the last Colony
  Works sites (colony_sites.py, from colony_kit.py and the colony and industry props), each on its POI's footprint with
  its markers' ground kept clear; the silos are climbed by their stair towers and galleries. Their colliders are the
  kit's plain boxes and ramps; walk_settlers.py walks them.
* ``meshes/structures/cargo_crate_colony.glb`` - a framed Colony cargo crate (decoration in the cargo spill).
* ``meshes/structures/landmark_*.glb`` - the eight landmarks (landmarks.py): set pieces that stand alone in open
  country, placed by the ``pois/landmark_*.json`` prefabs. Their colliders keep what a player climbs walkable: each
  flight of steps no steeper than 40 degrees collides as a smooth ramp, and cables, leaves and crystals do not collide.
* ``meshes/world/scrap_pile_metal.glb`` and ``scrap_pile_wood.glb`` - the starting scrap piles (world objects).
* ``meshes/world/props/*.glb`` - the props the world's scenes lay out between places (world objects): props.py,
  props_settler_a.py, props_settler_b.py, props_farm.py, props_camp.py and props_salvage.py (Settler Patchwork),
  props_colony_a.py, props_colony_b.py, props_street.py, props_industry.py, props_utility.py and props_vehicles.py
  (Colony Works), props_salvager.py (Salvager Tech wreckage), props_stone.py, props_ruins_a.py and props_ruins_b.py
  (Old Stone ruins), props_nature.py (rocks, wood, plants and bones), props_coast.py (shore and harbour), props_fields.py (crop fields
  and orchards, Settler Patchwork) and props_roadside.py (cable poles, guard rails, road signs and roadside litter).
* ``meshes/world/formations/*.glb`` - natural rock formations (rock_formations.py): arches, a window, hoodoos (POI
  models, with a collider so their openings stay open), a sea stack and a balanced rock (world objects).
  factions_bots_a.py (Scuttlers and Rakehands) and factions_bots_b.py (Spoolers and Threshers) are the bot factions'
  marks: their lairs and the smaller pieces of their faction sites.
* ``meshes/world/cliffs/*.glb`` - sculpted rock that dresses the cliff terraces (cliff_rocks.py): bedded walls for an
  8 m step, a 60 degree corner, low outcrop bands and scree fans, each with its origin at the foot of its face and a
  low-poly collider.

A structure model is one mesh of every part with a few shared detail textures (textures.py), a ``collider`` mesh of
its larger parts (the game collides with that and does not draw it), and its point lights. World object models have
no collider (the object's own collider is used), except the pieces you walk into or onto (a faction's lair, a bunker, a
fort): their kit.collider_box shapes make a ``collider`` mesh, which the object uses when its data says
``"mesh": { "collide": true }``.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/structures/build_structures.py          # everything
    python tools/structures/build_structures.py props    # only the scene props
    python tools/structures/build_structures.py props props_fields    # only the props of the modules named
    python tools/structures/build_structures.py landmarks [landmark_ziggurat ...]   # only the landmarks
    python tools/structures/build_structures.py formations            # only the rock formations (or the ones named)
    python tools/structures/build_structures.py cliffs [cliff_wall_a ...]   # only the cliff rocks
    python tools/structures/build_structures.py sv04                  # only the SV-04 wreck, its containers and spills
    python tools/structures/build_structures.py warehouses [warehouse_small ...]   # only the warehouses
    python tools/structures/build_structures.py ruins [ruin ruin_city ...]        # only the Old Stone ruins
    python tools/structures/build_structures.py settlers [farmer_shack ...]        # only the settler buildings
    python tools/structures/build_structures.py colony [silo_cluster ...]          # only the colony sites

The outputs are committed; re-run this script after changing it (or kit.py, frame.py, families.py, ship.py, cargo_ship.py, crash_site.py, sv04_layout.py,
workshop.py, colony_kit.py, warehouses.py, colony_sites.py, stone_kit.py, ruins.py, ruin_city.py, settler_kit.py, settlers.py, landmarks.py, props*.py, rock_formations.py, cliff_rocks.py, textures.py) and commit the result. The build is deterministic (seeded noise only).
"""

from __future__ import annotations

import random
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
sys.path.insert(0, str(Path(__file__).resolve().parent))
import kit as K  # noqa: E402

K.EXPORT = True
import families as F  # noqa: E402
import frame as FR  # noqa: E402
import props as PR  # noqa: E402
import props_colony_a  # noqa: E402
import props_colony_b  # noqa: E402
import props_settler_a  # noqa: E402
import props_settler_b  # noqa: E402
import props_stone  # noqa: E402
import props_farm  # noqa: E402
import props_camp  # noqa: E402
import props_salvage  # noqa: E402
import props_street  # noqa: E402
import props_industry  # noqa: E402
import props_utility  # noqa: E402
import props_vehicles  # noqa: E402
import props_ruins_a  # noqa: E402
import props_ruins_b  # noqa: E402
import props_salvager  # noqa: E402
import props_nature  # noqa: E402
import props_coast  # noqa: E402
import props_fields  # noqa: E402
import props_roadside  # noqa: E402
import rock_formations  # noqa: E402
import cliff_rocks  # noqa: E402
import factions_bots_a  # noqa: E402
import factions_bots_b  # noqa: E402
import ship as S  # noqa: E402
import crash_site as CS  # noqa: E402
import workshop as W  # noqa: E402
import landmarks as LM  # noqa: E402
import warehouses as WH  # noqa: E402
import ruins as RU  # noqa: E402
import ruin_city as RC  # noqa: E402
import settlers as ST  # noqa: E402
import colony_sites as CO  # noqa: E402

STRUCTURES = K.MESH / "structures"
WORLD = K.MESH / "world"


def fresh():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    K._mats.clear()
    K._finish_images.clear()


def report(name, counts):
    print(f"{name}: {counts[0]} triangles, collider {counts[1]}")


def ship():
    """The SV-04 wreck (crash_site.py, laid out by sv04_layout.py): its four pieces and the lost engine housing, all
    sharing the wreck's origin, then a burst container of each colour and a heap of each colour's cargo."""
    fresh()
    root, pieces = CS.wreck(scene=False, power_core=False)
    for key, piece in pieces.items():
        report(f"sv04_{key}", K.export(piece, STRUCTURES / f"sv04_{key}.glb", frame=root))
    lost = next(c for c in root.children if c.name.startswith("lost_housing"))
    report("sv04_housing", K.export(lost, STRUCTURES / "sv04_housing.glb", frame=root))
    for colour, kind in CS.SPILLS.items():
        single(f"sv04_container_{colour}", lambda colour=colour: CS.burst_model(colour))
        single(f"cargo_spill_{kind}", lambda colour=colour: CS.spill_pile(colour), WORLD, collider=False)


def single(name, build, folder=STRUCTURES, collider=True):
    fresh()
    root = build()
    report(name, K.export(root, folder / f"{name}.glb", collide_min=0.6 if collider else 1e9))


def court():
    root = K.link(bpy.data.objects.new("court", None))
    s = F.stone()
    K.disc("court_floor", 10.5, K.mat("flag", "#85877f", rough=0.9, cracks=0.6, noise_scale=2), (0, 0), z=0.06, segs=8).parent = root
    for k in range(3):
        K.box(f"court_step{k}", (6 - k * 1.5, 4 - k, 0.35), s, loc=(0, 2, 0.18 + k * 0.35), bev=0.08, parent=root)
    return root


def crate():
    root = K.link(bpy.data.objects.new("crate", None))
    FR.framed_box(root, "ccrate", (0, 0, 0.6), (1.2, 1.2, 1.2), F.M(F.STEEL, metal=0.3), F.M(F.GRAPHITE, metal=0.3), fw=0.1)
    return root


def pile(kind):
    def build():
        root = K.link(bpy.data.objects.new(kind, None))
        S.scrap_pile(root, (0, 0, 0), random.Random(7 if kind == "metal" else 8), kind)
        return root
    return build


def props(only=()):
    for module in (PR, props_settler_a, props_settler_b, props_colony_a, props_colony_b, props_stone,
                   props_farm, props_camp, props_salvage, props_street, props_industry, props_utility,
                   props_vehicles, props_ruins_a, props_ruins_b, props_salvager, props_nature, props_coast,
                   props_fields, props_roadside, factions_bots_a, factions_bots_b):
        if only and module.__name__ not in only:
            continue
        for name, build in module.PROPS.items():
            single(name, build, WORLD / "props", collider=False)


def landmarks(only=()):
    for name, build in LM.PROPS.items():
        if not only or name in only:
            single(name, build)


def formations(only=()):
    for name, build in rock_formations.PROPS.items():
        if not only or name in only:
            single(name, build, WORLD / "formations", collider=name in rock_formations.COLLIDE)


def cliffs(only=()):
    for name, build in cliff_rocks.PROPS.items():
        if not only or name in only:
            single(name, build, WORLD / "cliffs")


def warehouses(only=()):
    for name in WH.WAREHOUSES:
        if not only or name in only:
            single(name, lambda name=name: WH.warehouse(name))


def ruins(only=()):
    for name, build in RU.RUINS.items():
        if not only or name in only:
            single(name, build)
    if not only or "ruin_city" in only:
        fresh()
        for key, root in RC.ruin_city().items():
            report(f"ruin_city_{key}", K.export(root, STRUCTURES / f"ruin_city_{key}.glb"))


def settlers(only=()):
    for name, build in ST.SETTLERS.items():
        if not only or name in only:
            single(name, build)


def colony(only=()):
    for name, build in CO.SITES.items():
        if not only or name in only:
            single(name, build)


def main():
    if sys.argv[1:2] == ["colony"]:
        colony(sys.argv[2:])
        return
    if sys.argv[1:2] == ["ruins"]:
        ruins(sys.argv[2:])
        return
    if sys.argv[1:2] == ["settlers"]:
        settlers(sys.argv[2:])
        return
    if sys.argv[1:2] == ["warehouses"]:
        warehouses(sys.argv[2:])
        return
    if sys.argv[1:2] == ["props"]:
        props(sys.argv[2:])
        return
    if sys.argv[1:2] == ["landmarks"]:
        landmarks(sys.argv[2:])
        return
    if sys.argv[1:2] == ["formations"]:
        formations(sys.argv[2:])
        return
    if sys.argv[1:2] == ["cliffs"]:
        cliffs(sys.argv[2:])
        return
    if sys.argv[1:2] == ["sv04"]:
        ship()
        return
    ship()
    single("relay_beacon_dead", lambda: F.dead_beacon())
    single("settler_shack", lambda: F.settler_shack())
    single("stone_arch", lambda: F.old_stone_arch(seed=5))
    single("stone_arch_broken", lambda: F.old_stone_arch(seed=9, broken=True))
    for name, h, seed in (("stone_pillar_tall", 5.0, 21), ("stone_pillar_mid", 3.4, 23), ("stone_pillar_short", 2.6, 22)):
        single(name, lambda h=h, seed=seed: F.stone_pillar((0, 0, 0), h, seed))
    single("stone_court", court)
    single("workshop", lambda: W.workshop())
    warehouses()
    ruins()
    settlers()
    colony()
    single("cargo_crate_colony", crate)
    landmarks()
    single("scrap_pile_metal", pile("metal"), WORLD, collider=False)
    single("scrap_pile_wood", pile("wood"), WORLD, collider=False)
    props()
    formations()
    cliffs()


if __name__ == "__main__":
    main()
