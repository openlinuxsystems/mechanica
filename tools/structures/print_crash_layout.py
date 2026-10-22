"""Prints the crash site's wreck numbers from sv04_layout.py, in the crash site POI's frame (x east, y up, z south),
for mods/vanilla/pois/crash_site.json and game/data/opening.json: python3 tools/structures/print_crash_layout.py

Run it after changing sv04_layout.py (or the rooms in cargo_ship.py the markers point at) and copy what changed.
"""

import json
import math

import sv04_layout as L

DECK = 0.04   # the deck's top in the ship's frame

# Fires lit on a burst container (its frame: doors towards +y), on every third one in SCATTER's order: just outside
# the doors, clear of the first heap (a spill's collider is 1.8 m square) so the fire can be reached and put out.
CONTAINER_FIRE = (-1.9, 3.4)
# Where each container's cargo lies, in its frame: two heaps in front of the doors.
SPILLS = ((-1.3, 5.2), (1.4, 6.8))
KIND = {"red": "metal", "blue": "electronics", "yellow": "fuel", "green": "farming", "tan": "building"}

# Spots the quests and loot point at: (what, piece, ship-frame point on the deck, facing in the ship's frame in degrees
# counter-clockwise from the bow, or None).
SPOTS = [
    ("master_battery", "aft", (L.BAY_AT[0], L.BAY_AT[1] + 0.72, L.BAY_AT[2] + 0.64), None),   # the Power Core's slot
    ("logbook", "bow", (-4.9, 20.98, 1.16), None),                                              # on the bridge lectern
    ("locker", "aft", (7.0, -21.0, DECK), 90.0),             # crew quarters, between the bunks, facing the table
    ("broken_microwave", "aft", (2.1, -12.75, 1.06), -90.0),  # mess: on the galley counter
    ("file_cabinet", "bow", (5.3, 23.4, DECK), 90.0),         # bridge: past the starboard cabinet
    ("locker", "bow", (-6.05, 16.75, DECK), -90.0),           # cargo control, beside the EVA suit lockers
    ("loot_crate", "bow", (-1.6, 15.1, DECK), 0.0),           # cargo control, by the door to the spine
]
# Starting scrap on the ground round the hull (wreck frame): plates torn off on impact, planks from a burst crate.
SCRAP = [("scrap_pile_metal", 16.5, 24.0), ("scrap_pile_metal", 13.5, 6.0), ("scrap_pile_metal", 4.0, -40.0),
         ("scrap_pile_metal", -15.0, -12.0), ("scrap_pile_wood", 13.0, -6.0), ("scrap_pile_wood", -12.0, 12.0)]


def r2(p):
    return [round(v, 2) for v in p]


def poi(p):
    return r2(L.to_poi(p))


def deck_point(piece, p):
    return poi(L.piece_point(piece, p))


def heading(piece, facing):
    """A POI yaw for something facing `facing` degrees (counter-clockwise from the bow) on piece."""
    m = L.rot_xyz(*L.PIECES[piece][1])
    d = (-math.sin(math.radians(facing)), math.cos(math.radians(facing)), 0.0)
    w = L.apply(m, d)
    return round(L.WRECK_YAW + math.degrees(math.atan2(-w[0], w[1])), 1)


def container_point(x, y, yaw, p):
    s, c = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
    return (x + p[0] * c - p[1] * s, y + p[0] * s + p[1] * c, 0.0)


def main():
    out = {"models": [], "markers": [], "opening": {}}
    for key in ("aft", "spine_a", "spine_b", "bow", "housing"):
        out["models"].append({"file": f"meshes/structures/sv04_{key}.glb", "at": [0.0, 0.0, 0.0], "yaw": L.WRECK_YAW})
    slots = L.slots()
    for code, x, y, yaw in L.SCATTER:
        out["models"].append({"file": f"meshes/structures/sv04_container_{slots[code][0]}.glb", "at": poi((x, y, 0.0)),
                              "yaw": round(L.WRECK_YAW + yaw, 1), "code": code})
    for key, piece, p, _ in L.FIRES:
        at = list(L.piece_point(piece, p))
        if key in ("airlock", "mess_door"):
            at[2] = 0.0   # outside, on the ground
        else:
            at = list(L.piece_point(piece, (p[0], p[1], DECK)))
        out["markers"].append({"type": "fire", "at": poi(at), "note": key})
    for i, (code, x, y, yaw) in enumerate(L.SCATTER):
        if i % 3 == 0:
            out["markers"].append({"type": "fire", "at": poi(container_point(x, y, yaw, CONTAINER_FIRE)), "note": code})
    for what, piece, p, facing in SPOTS:
        m = {"ref": what, "at": deck_point(piece, p)}
        if facing is not None:
            m["yaw"] = heading(piece, facing)
        out["markers"].append(m)
    for ref, x, y in SCRAP:
        out["markers"].append({"type": "object", "ref": ref, "at": poi((x, y, 0.0))})
    hx, hy, _ = L.HOUSING_LYING[0]
    out["markers"].append({"type": "object", "ref": "master_battery_fallen", "at": poi((hx - 6.0, hy - 3.5, 0.0)),
                           "yaw": round(L.WRECK_YAW + 35.0, 1)})
    for code, x, y, yaw in L.SCATTER:
        for p in SPILLS:
            out["markers"].append({"type": "object", "ref": f"cargo_spill_{KIND[slots[code][0]]}", "at": poi(container_point(x, y, yaw, p)),
                                   "yaw": round(L.WRECK_YAW + yaw + 37.0 * (p[0] > 0), 1), "note": code})
    # opening.json: the pieces' bakes, the housing and the slots the scattered containers ride in (POI model order)
    out["opening"]["pieces"] = [{"model": f"meshes/structures/sv04_{k}.glb", "pivot": list(L.PIVOTS[k]),
                                 "wreck": {"at": list(L.PIECES[k][0]), "rotation": list(L.PIECES[k][1])}} for k in L.PIECES]
    out["opening"]["housing_on_ship"] = list(L.HOUSING_CENTRE)
    out["opening"]["housing_lying"] = {"at": list(L.HOUSING_LYING[0]), "rotation": list(L.HOUSING_LYING[1])}
    out["opening"]["container_slots"] = [r2(slots[code][1]) for code, _, _, _ in L.SCATTER]
    for section, rows in out.items():
        print(f"== {section}")
        for row in rows if isinstance(rows, list) else [rows]:
            print(json.dumps(row))


if __name__ == "__main__":
    main()
