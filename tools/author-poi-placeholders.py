#!/usr/bin/env python3
"""Writes the placeholder POI prefabs of the World Map spec (mods/vanilla/pois/*.json).

The spec's 50 mandatory structures get simple volumes of the right footprint and height until each is designed on its
own: a concrete pad, the building's bounding volume, and a few shapes that make the type readable from a distance
(silos, a smokestack, a tower). Warehouses are hollow shells with their floors, stairs and the warehouse markers, so
their gameplay works while they wait for their design. Edit the table here and re-run; never edit the written files.

Usage: tools/author-poi-placeholders.py [OUT_DIR]   (default: mods/vanilla/pois)
"""
import json
import math
import os
import sys

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mods", "vanilla", "pois")

PAD = "#a3a093"
METAL = "#7d858c"
ROOF = "#4a4f55"
YELLOW = "#e0b52c"
CONCRETE = "#b4b0a4"
RUIN = "#9c988c"
HAY = "#d8b85a"
GLASS = "#9fd8c8"
WOOD = "#8c5a3c"
BURNT = "#3a3430"

LAND = ["meadow", "forest", "field", "autumn_forest"]


def box(size, at, color, yaw=0.0):
    piece = {"shape": "box", "size": [float(v) for v in size], "at": [float(v) for v in at]}
    if yaw:
        piece["yaw"] = float(yaw)
    piece["color"] = color
    return piece


def cylinder(radius, height, at, color):
    return {"shape": "cylinder", "radius": float(radius), "height": float(height), "at": [float(v) for v in at], "color": color}


def volume(w, d, h, color, x=0.0, z=0.0, y=0.0):
    """A building's bounding volume standing on the pad."""
    return box([w, h, d], [x, y + h / 2, z], color)


def ring(count, radius, fn):
    return [fn(radius * math.cos(2 * math.pi * k / count), radius * math.sin(2 * math.pi * k / count)) for k in range(count)]


def warehouse(floors, w, d):
    """A hollow shell: floors every 4 m, a door gap in the front (+z) wall, stairs up the side, the warehouse markers."""
    h = 4.0 * floors
    hw, hd = w / 2, d / 2
    pieces = [box([w, 0.3, d], [0, 0.15, 0], PAD)]
    for f in range(1, floors):
        # Each upper floor leaves an 8 m stairwell open at the front.
        pieces.append(box([w, 0.3, d - 8], [0, 4 * f + 0.15, -4], PAD))
    pieces.append(box([w + 0.8, 0.3, d + 0.8], [0, h + 0.15, 0], ROOF))
    pieces += [
        box([w, h, 0.4], [0, h / 2, -hd], METAL),
        box([0.4, h, d], [-hw, h / 2, 0], METAL),
        box([0.4, h, d], [hw, h / 2, 0], METAL),
        box([hw - 3, h, 0.4], [-(hw + 3) / 2, h / 2, hd], METAL),
        box([hw - 3, h, 0.4], [(hw + 3) / 2, h / 2, hd], METAL),
        box([6, h - 4, 0.4], [0, 4 + (h - 4) / 2, hd], METAL),
    ]
    # Stairs: a flight of 0.5 m steps per floor, alternating sides of the stairwell.
    for f in range(floors - 1):
        side = -1 if f % 2 == 0 else 1
        for s in range(8):
            rise = 0.5 * (s + 1)
            pieces.append(box([1, rise, 3], [side * (hw - 1.5 - s), 4 * f + rise / 2, hd - 3], WOOD))
    # Roof tanks and pipes make the type readable from the road (spec §6).
    pieces += [cylinder(3, 4, [-hw / 2, h + 2.3, -hd / 2], YELLOW), cylinder(3, 4, [hw / 2, h + 2.3, -hd / 2], YELLOW),
               box([hw, 0.8, 0.8], [0, h + 0.8, -hd / 2], "#3f6fa8")]
    bots = ["spooler_blue", "spooler_green", "spooler_yellow", "spooler_blue"]
    markers = [{"type": "door", "ref": "warehouse_key", "at": [0.0, 0.0, hd]}]
    for f in range(floors):
        markers.append({"type": "bot", "ref": bots[f], "at": [(-4.0 if f % 2 else 0.0), 4 * f + 0.3, -hd / 2]})
    markers.append({"type": "item", "ref": "warehouse_key", "at": [hw - 4, 4 * (floors - 1) + 0.3, -hd + 4]})
    for f in range(floors):
        markers.append({"type": "item", "ref": "metal_crate" if f < floors - 1 else "wooden_crate",
                        "at": [(-hw + 4) if f % 2 == 0 else (hw - 4), 4 * f + 0.3, -hd + 4]})
    markers.append({"type": "item", "ref": "warehouse_key_sign", "at": [0.0, 0.0, hd + 4]})
    return pieces, markers, h


def silos(count, radius, height, spread):
    return ring(count, spread, lambda x, z: cylinder(radius, height, [x, height / 2, z], YELLOW)) if count > 1 else \
        [cylinder(radius, height, [0, height / 2, 0], YELLOW)]


def placeholder(w, d, h, color, extra=()):
    return [box([w + 8, 0.3, d + 8], [0, 0.15, 0], PAD), volume(w, d, h, color, y=0.3), *extra]


# Structure types by id: name, [width, height, depth], road link, pieces, markers and the rest. Each of the spec's 50
# structures gets its own prefab (pois/poi_NN.json) made from its type, so each can be designed on its own later.
TYPES = {}


def add(poi_id, name, size, road, pieces, markers=None, flora=None, ground="lot", basin=None, spacing=None, pools=None, margin=28):
    """margin: metres of ground blending round the pad (small spaced POIs blend in over less)."""
    TYPES[poi_id] = dict(id=poi_id, name=name, size=size, road=road, pieces=pieces, markers=markers or [], flora=flora,
                         ground=ground, basin=basin, spacing=spacing, pools=pools or [], margin=margin)


def pool(liquid, radius, at=(0, 0), level=-1.0):
    """A pool of liquid (worldgen.json liquids) laid in a basin: its surface sits level metres below the pad. The liquid
    fills the disc only where the ground is lower, so radius reaches past the basin's wobbling edge and the shore
    follows the ground."""
    return {"liquid": liquid, "at": [float(v) for v in at], "radius": float(radius), "level": float(level)}


def geysers(count, radius, at=(0, 0), turn=0.0):
    """Oil geysers (world object markers) in a ring round a point."""
    return [{"type": "object", "ref": "oil_geyser",
             "at": [round(at[0] + radius * math.cos(2 * math.pi * k / count + turn), 2), 0.0,
                    round(at[1] + radius * math.sin(2 * math.pi * k / count + turn), 2)]} for k in range(count)]


for floors, poi_id, name, (w, d) in [(2, "warehouse_small", "Warehouse (2 floors)", (40, 30)),
                                     (3, "warehouse", "Warehouse (3 floors)", (55, 40)),
                                     (4, "warehouse_large", "Large Warehouse (4 floors)", (70, 50))]:
    pieces, markers, h = warehouse(floors, w, d)
    add(poi_id, name, [w, h + 4.3, d], "road", pieces, markers)

add("small_ruin", "Small Ruin", [14, 5, 12], "track", placeholder(14, 12, 5, RUIN), flora="meadow", ground=None)
add("farmer_shack", "Farmer Shack", [12, 5, 10], "track",
    placeholder(12, 10, 5, WOOD, [box([5, 2.5, 4], [10, 1.25, 4], HAY)]), flora="meadow", ground=None)
add("packing_station_vegetable", "Packing Station (Vegetable)", [36, 8, 24], "road",
    placeholder(36, 24, 8, METAL, [box([3, 3, 0.4], [0, 10, 12.2], "#5fa83a"), box([20, 1.2, 3], [0, 1.0, 15], ROOF)]))
add("packing_station_fruit", "Packing Station (Fruit)", [36, 8, 24], "road",
    placeholder(36, 24, 8, METAL, [box([3, 3, 0.4], [0, 10, 12.2], "#c8402f"), box([20, 1.2, 3], [0, 1.0, 15], ROOF)]))
add("silo_cluster", "Silo Cluster", [30, 18, 30], "road",
    [box([38, 0.3, 38], [0, 0.15, 0], PAD), *silos(4, 4, 18, 8), volume(10, 8, 6, METAL, x=-10, z=12, y=0.3)])
add("silo_district", "Silo District", [90, 26, 70], "road",
    [box([98, 0.3, 78], [0, 0.15, 0], PAD),
     *[cylinder(5, 26, [x, 13.3, z], YELLOW) for x in (-30, -18, -6, 6) for z in (-22, -10)],
     volume(30, 20, 12, METAL, x=26, z=-14, y=0.3), volume(24, 16, 10, METAL, x=-20, z=20, y=0.3),
     box([60, 1, 1], [-12, 24, -16], "#3f6fa8"), box([0.3, 2, 30], [44, 1.3, 20], METAL)])
add("ruin", "Ruin", [30, 10, 24], "track",
    [box([34, 0.3, 28], [0, 0.15, 0], RUIN), volume(30, 0.5, 10, RUIN, z=-12), volume(0.5, 24, 7, RUIN, x=-15),
     volume(12, 0.5, 4, RUIN, x=9, z=12), volume(14, 12, 3, RUIN, x=6, z=-4, y=0.3)], flora="meadow", ground=None)
add("mountain_lookout", "Mountain Lookout", [10, 14, 10], "track",
    [box([14, 0.3, 14], [0, 0.15, 0], RUIN), *[cylinder(0.4, 12, [x, 6.3, z], METAL) for x in (-4, 4) for z in (-4, 4)],
     box([10, 2.5, 10], [0, 13.5, 0], RUIN)], flora="mountain", ground=None)
add("campsite", "Campsite", [20, 3, 20], "track",
    [box([3, 2.4, 4], [-5, 1.2, -4], "#5f8a4a", 20), box([3, 2.4, 4], [5, 1.2, -3], "#5f8a4a", -15),
     box([4, 2.8, 5], [0, 1.4, 6], "#8a6a4a"), cylinder(1, 0.4, [0, 0.2, 0], BURNT)], flora="meadow", ground=None)
add("large_camp", "Large Camp", [50, 4, 40], "track",
    [box([54, 0.3, 44], [0, 0.15, 0], "#8a7a5a"),
     *[box([4, 3, 5], [x, 1.8, z], "#5f8a4a") for x in (-18, -9, 9, 18) for z in (-12, 12)],
     volume(12, 8, 4, WOOD, y=0.3), *[box([2, 2, 2], [x, 1.3, 18], HAY) for x in (-20, -16, 16, 20)]])
add("growlab", "Growlab", [30, 8, 20], "road",
    placeholder(30, 20, 8, GLASS, [volume(10, 8, 6, METAL, x=-20, y=0.3)]))
add("farmers_hideout", "Farmers Hideout", [40, 16, 30], "road",
    placeholder(40, 30, 16, METAL, [cylinder(2, 28, [14, 14.3, -10], ROOF), box([8, 0.4, 12], [0, 6, 21], METAL)]))
# Chemical pools (Chemical item: an empty bucket dipped in one; standing in one hurts) and oil geysers (Crude Oil by hand).
add("chemical_lake_facility", "Chemical Lake Facility", [40, 10, 30], "track",
    placeholder(40, 30, 10, RUIN, [box([1, 1, 18], [-10, 0.8, 26], YELLOW), box([1, 1, 18], [10, 0.8, 26], YELLOW)]),
    basin=dict(at=[0, 36], radius=16, depth=1.5, wobble=0.15), pools=[pool("chemical", 22, at=(0, 36))])
add("chemical_lake", "Chemical Lake", [52, 2, 52], "track",
    [box([1, 1, 20], [26, 0.8, 0], YELLOW), cylinder(2, 3, [30, 1.5, 12], YELLOW)],
    flora="meadow", ground="dirt", basin=dict(at=[0, 0], radius=24, depth=1.5, wobble=0.3), pools=[pool("chemical", 37)])
add("oil_pool", "Oil Pool", [36, 1, 36], "none",
    [cylinder(1.5, 4, [18, 2, 0], ROOF)], ground="sand",
    basin=dict(at=[0, 0], radius=16, depth=1.5, wobble=0.3), pools=[pool("oil", 26)], markers=geysers(8, 27))
# Spaced (World POI plan): small pools and ponds between the sites, so every part of the map has chemicals and oil in reach.
add("chemical_pool", "Chemical Pool", [30, 3, 30], "none",
    [cylinder(0.6, 1.2, [14, 0.6, 3], YELLOW), cylinder(0.6, 1.2, [15, 0.6, 4.5], YELLOW), box([1.2, 0.6, 0.8], [-14, 0.3, -2], "#5a5e62", 20)],
    flora="meadow", ground="dirt", basin=dict(at=[0, 0], radius=12, depth=1.5, wobble=0.3), pools=[pool("chemical", 21)],
    spacing={"spacing": [1750.0, 2400.0], "biomes": ["meadow", "field", "forest", "autumn_forest"], "priority": 33, "clearance": 96.0})
add("oil_pond", "Oil Pond", [20, 2, 20], "none",
    [box([4, 0.5, 0.6], [11, 0.25, -3], "#3a3430", 35)],
    ground="burnt", basin=dict(at=[0, 0], radius=6, depth=1.0, wobble=0.3), pools=[pool("oil", 13, level=-0.6)], markers=geysers(4, 12.5, turn=0.4),
    spacing={"spacing": [700.0, 1000.0], "biomes": ["desert", "burned_forest"], "priority": 33, "clearance": 64.0})
# Small spaced POIs (World POI plan, approved 2026-10-19): camps, forest ruins, roadside kiosks, farming patches, build
# areas and shore dressing fill the land between the sites so there is something to find every few hundred metres.
TENT = "#5f8a4a"
CANVAS = "#b8a47a"
CROP = "#6aa84f"
add("camp", "Camp", [14, 3, 14], "track",
    [box([3, 2.2, 4], [-4, 1.1, -3], TENT, 20), box([3, 2.0, 3.5], [4, 1.0, -2], CANVAS, -25), cylinder(0.8, 0.3, [0, 0.15, 2], BURNT),
     box([2.2, 0.4, 0.5], [-1.8, 0.2, 3.6], WOOD, 70), box([1.2, 0.9, 0.8], [5, 0.45, 4], WOOD)],
    [{"type": "item", "ref": "wooden_crate", "at": [5.0, 0.0, 4.0]},
     {"type": "item", "ref": "wooden_crate", "at": [-4.0, 0.0, -1.0]}],
    ground="dirt", margin=12,
    spacing={"spacing": [570.0, 820.0], "biomes": ["forest", "autumn_forest", "burned_forest"], "priority": 20, "clearance": 48.0})
add("forest_ruin", "Forest Ruin", [16, 5, 16], "track",
    [box([7, 4, 0.6], [-4, 2, -7.5], RUIN), box([3, 2, 0.6], [5, 1, -7.5], RUIN), box([0.6, 4.5, 9], [-7.5, 2.25, -2], RUIN),
     box([0.6, 1.5, 5], [7.5, 0.75, 3], RUIN), box([2, 0.8, 1.5], [2, 0.4, 2], RUIN, 30), cylinder(0.5, 5, [-7.5, 2.5, 7.5], RUIN)],
    [{"type": "item", "ref": "wooden_crate", "at": [-5.0, 0.0, -5.0]}, {"type": "item", "ref": "wooden_crate", "at": [4.0, 0.0, 5.0]}],
    flora="forest", ground=None, margin=12,
    spacing={"spacing": [710.0, 1020.0], "biomes": ["forest", "autumn_forest"], "priority": 21, "clearance": 48.0})
add("roadside_kiosk", "Roadside Kiosk", [6, 3.5, 5], "none",
    [box([10, 0.2, 8], [0, 0.1, 0], PAD), box([5, 2.6, 3.5], [0, 1.5, 0.5], WOOD), box([6, 0.2, 2], [0, 2.9, -1.8], CANVAS, 0),
     box([5, 1, 0.3], [0, 1.0, -1.4], WOOD), box([0.2, 2.2, 0.2], [3.8, 1.1, -3.2], METAL), box([1.6, 1, 0.1], [3.8, 2.6, -3.2], YELLOW)],
    [{"type": "item", "ref": "wooden_crate", "at": [0.0, 1.5, -1.4]}, {"type": "item", "ref": "wooden_crate", "at": [-3.5, 0.2, 2.0]}],
    margin=6,
    spacing={"spacing": [520.0, 900.0], "biomes": ["meadow", "forest", "field", "desert", "autumn_forest", "burned_forest"],
             "priority": 40, "clearance": 0.0, "roadside": 8.0})
add("farming_patch", "Farming Patch", [24, 1, 20], "track",
    [*[box([22, 0.35, 1.6], [0, 0.17, z], "#6b4a2e") for z in (-7.5, -4.5, -1.5, 1.5, 4.5, 7.5)],
     *[box([20, 0.6, 0.8], [0, 0.6, z], CROP) for z in (-7.5, -1.5, 4.5)],
     box([0.2, 1.2, 20], [-12, 0.6, 0], WOOD), box([0.2, 1.2, 20], [12, 0.6, 0], WOOD), box([2, 2, 2], [14, 1, -8], HAY)],
    [{"type": "item", "ref": "wooden_crate", "at": [14.0, 0.0, -5.0]}],
    ground="dirt", margin=12,
    spacing={"spacing": [1300.0, 1800.0], "biomes": ["field", "meadow"], "priority": 22, "clearance": 64.0})
add("build_area", "Build Area", [40, 1, 40], "track",
    [box([40, 0.3, 40], [0, 0.15, 0], CONCRETE), *[cylinder(0.25, 6, [x, 3.3, z], METAL) for x in (-19, 19) for z in (-19, 19)],
     box([4, 2.5, 3], [-16, 1.55, -16], METAL), box([1, 1, 1], [-13, 0.8, -16], YELLOW)],
    [{"type": "item", "ref": "wooden_crate", "at": [-16.0, 0.3, -13.0]}],
    ground=None, margin=14,
    spacing={"spacing": [1750.0, 2400.0], "biomes": ["meadow", "field", "desert"], "priority": 24, "clearance": 96.0})
add("shore_wreck", "Shore Wreck", [12, 3, 8], "none",
    [box([8, 1.6, 3], [0, 0.6, 0], "#7a6450", 15), box([3, 1.2, 2.6], [-4.5, 1.2, 0.8], WOOD, 15), box([0.3, 3, 0.3], [1, 2, 0], WOOD),
     box([6, 0.3, 1.5], [6, 0.35, -3], WOOD, -20), box([1, 0.8, 1], [-5, 0.4, -3], METAL)],
    [{"type": "item", "ref": "wooden_crate", "at": [0.0, 1.4, 0.0]}],
    ground="sand", margin=10,
    spacing={"spacing": [480.0, 760.0], "biomes": ["meadow", "forest", "field", "desert", "autumn_forest", "burned_forest", "coastal"],
             "priority": 31, "clearance": 32.0, "shore": 24.0})
add("farmbot_scrapyard", "Farmbot Scrapyard", [60, 6, 50], "none",
    [box([64, 0.3, 54], [0, 0.15, 0], BURNT),
     *[box([s, s / 2, s], [x, s / 4 + 0.3, z], "#6a5a52", (x * 7) % 45) for s, x, z in
       [(10, -18, -12), (8, 6, -16), (12, 16, 6), (7, -8, 10), (9, -22, 14), (6, 24, -18)]],
     box([60, 2, 0.4], [0, 1.3, -25], METAL), box([0.4, 2, 50], [30, 1.3, 0], METAL)], ground="burnt")
add("capsule_scrapyard", "Capsule Scrapyard", [50, 6, 40], "track",
    [box([54, 0.3, 44], [0, 0.15, 0], PAD),
     *[cylinder(2.5, 5, [x, 2.8, z], "#c9d2d8") for x in (-16, -8, 0, 8, 16) for z in (-10, 10)],
     box([50, 2, 0.4], [0, 1.3, -20], METAL)])
add("ruin_city", "Ruin City", [160, 40, 140], "road",
    [box([168, 0.3, 148], [0, 0.15, 0], RUIN),
     *[volume(w, d, h, RUIN, x=x, z=z, y=0.3) for w, d, h, x, z in
       [(30, 24, 40, -50, -40), (24, 24, 28, -10, -50), (36, 20, 34, 40, -44), (20, 30, 22, -56, 10),
        (28, 28, 36, 50, 8), (40, 18, 16, 0, 0), (22, 22, 30, -30, 48), (30, 20, 24, 30, 50)]],
     box([150, 0.3, 10], [0, 0.45, 26], "#5a5e62")])
add("haymaze", "Haymaze", [60, 3, 60], "road",
    [box([64, 0.3, 64], [0, 0.15, 0], "#8a7a5a"),
     *[box([w, 3, 2], [x, 1.8, z], HAY) for w, x, z in [(60, 0, -29), (60, 0, 29), (40, -10, -15), (40, 10, 0), (40, -10, 15)]],
     *[box([2, 3, d], [x, 1.8, z], HAY) for d, x, z in [(52, -29, 0), (52, 29, 0), (14, 0, -22), (14, 15, 22)]]])
add("scrap_city_garage", "Scrap City Garage", [60, 12, 40], "road",
    placeholder(60, 40, 12, METAL, [box([16, 8, 0.4], [-12, 4.3, 20.2], ROOF), box([16, 8, 0.4], [12, 4.3, 20.2], ROOF),
                                    cylinder(3, 6, [20, 15.3, -10], YELLOW)]))


def inline(value):
    """One-line JSON in the repo's style: spaces inside object braces."""
    if isinstance(value, dict):
        return "{ " + ", ".join(f"{json.dumps(k)}: {inline(v)}" for k, v in value.items()) + " }" if value else "{}"
    if isinstance(value, list):
        return "[" + ", ".join(inline(v) for v in value) + "]"
    return json.dumps(value)


def radius_of(w, d, margin=28):
    """Footprint radius: the pad's half-diagonal plus room to blend into the ground."""
    return math.ceil(math.hypot(w + 8, d + 8) / 2 + margin)


def write(poi):
    w, h, d = poi["size"]
    reach = math.hypot(w + 8, d + 8) / 2
    radius = radius_of(w, d, poi["margin"])
    level = math.ceil(reach + min(8, poi["margin"] / 2))
    terrain = [{"op": "flatten", "radius": float(level), "blend": float(radius - level)}]
    if poi["basin"]:
        b = poi["basin"]
        terrain.append({"op": "basin", "at": [float(v) for v in b["at"]], "radius": float(b["radius"]), "blend": 4.0, "depth": b["depth"],
                        **({"wobble": b["wobble"]} if b.get("wobble") else {})})
    lines = ["{",
             "  // Placeholder (World Map spec §4): footprint and height only, until the structure is designed on its own.",
             "  // Written by tools/author-poi-placeholders.py; edit the table there and re-run.",
             f'  "id": {inline(poi["id"])},',
             f'  "name": {inline(poi["name"])},',
             f'  "radius": {float(radius)},',
             f'  "size": {inline([float(v) for v in poi["size"]])},',
             f'  "road": {inline(poi["road"])},',
             '  "placement": {},' if not poi["spacing"] else f'  "placement": {inline(poi["spacing"])},',
             '  "terrain": [']
    lines += [f"    {inline(op)}{',' if k < len(terrain) - 1 else ''}" for k, op in enumerate(terrain)]
    lines.append("  ],")
    if poi["ground"]:
        lines.append(f'  "ground": [{inline({"ground": poi["ground"], "radius": float(math.ceil(reach)), "blend": float(min(10, radius - math.ceil(reach))), "noise": 4.0})}],')
    if poi["flora"]:
        lines.append(f'  "flora": {inline(poi["flora"])},')
    if poi["pools"]:
        lines.append('  "pools": [')
        lines += [f"    {inline(p)}{',' if k < len(poi['pools']) - 1 else ''}" for k, p in enumerate(poi["pools"])]
        lines.append("  ],")
    lines.append('  "pieces": [')
    lines += [f"    {inline(p)}{',' if k < len(poi['pieces']) - 1 else ''}" for k, p in enumerate(poi["pieces"])]
    lines.append("  ]" + ("," if poi["markers"] else ""))
    if poi["markers"]:
        lines.append('  "markers": [')
        lines += [f"    {inline(m)}{',' if k < len(poi['markers']) - 1 else ''}" for k, m in enumerate(poi["markers"])]
        lines.append("  ]")
    lines.append("}")
    text = "\n".join(lines) + "\n"
    with open(os.path.join(OUT, poi["id"] + ".json"), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


# The spec's 50 structures (World Map spec §4): number, type, name. POI-36 (crash_site), POI-37 (outpost, the quests'
# first Workshop) and the other Workshops, POI-02 and POI-13, keep their hand-made prefabs. Warehouse floors are fixed per structure (12 warehouses with the large slots).
SITES = [
    (1, "small_ruin", "Forest Outpost"), (3, "warehouse", "Warehouse"),
    (4, "farmer_shack", "Farmer Shack"), (5, "packing_station_vegetable", "Packing Station"), (6, "silo_cluster", "Silo Cluster"),
    (7, "ruin", "Ruin"), (8, "warehouse", "Warehouse"), (9, "mountain_lookout", "Mountain Lookout"), (10, "campsite", "Campsite"),
    (11, "small_ruin", "Small Ruin"), (12, "campsite", "Autumn Forest Camp"),
    (14, "growlab", "Growlab"), (15, "warehouse_small", "Warehouse"), (16, "ruin", "Ruin"), (17, "small_ruin", "Shack"),
    (18, "packing_station_fruit", "Packing Station"), (19, "warehouse", "Warehouse"), (20, "farmers_hideout", "Farmers Hideout"),
    (21, "warehouse_large", "Large Warehouse"), (22, "ruin", "Autumn Ruin"), (23, "chemical_lake_facility", "Chemical Lake Facility"),
    (24, "warehouse_large", "Large Warehouse"), (25, "packing_station_vegetable", "Packing Station"), (26, "silo_district", "Silo District"),
    (27, "warehouse_small", "Desert Warehouse"), (28, "warehouse_small", "Field Warehouse"), (29, "ruin", "Ruin"),
    (30, "campsite", "Camp"), (31, "warehouse", "Warehouse"), (32, "farmbot_scrapyard", "Burnt Forest Scrapyard"),
    (33, "ruin", "Desert Ruin"), (34, "small_ruin", "Small Ruin"), (35, "warehouse_large", "Large Warehouse"),
    (38, "packing_station_vegetable", "Packing Station (Vegetable)"), (39, "packing_station_fruit", "Packing Station (Fruit)"),
    (40, "ruin_city", "Ruin City"), (41, "capsule_scrapyard", "Capsule Scrapyard"), (42, "haymaze", "Haymaze"),
    (43, "large_camp", "Large Camp"), (44, "growlab", "Growlab (Meadow)"), (45, "growlab", "Growlab (Desert)"),
    (46, "growlab", "Growlab (Forest)"), (47, "growlab", "Growlab (Burned Forest)"), (48, "chemical_lake", "Chemical Lake"),
    (49, "oil_pool", "Oil Pool"), (50, "scrap_city_garage", "Scrap City Garage"),
]

# Types the extra large slots draw by seed (worldgen.json pools), and the spaced ones, keep a prefab of their own.
POOLED = ["warehouse_small", "warehouse", "warehouse_large", "farmbot_scrapyard", "chemical_pool", "oil_pond", "camp", "forest_ruin",
          "roadside_kiosk", "farming_patch", "build_area", "shore_wreck"]

POIS = [TYPES[t] for t in POOLED]
for number, type_id, name in SITES:
    POIS.append(dict(TYPES[type_id], id=f"poi_{number:02d}", name=f"POI-{number:02d} {name}"))

os.makedirs(OUT, exist_ok=True)
for poi in POIS:
    write(poi)
print(f"wrote {len(POIS)} placeholder POIs to {os.path.normpath(OUT)}")
