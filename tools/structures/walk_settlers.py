"""Walks a structure's collider the way the player would: python3 walk_settlers.py NAME [NAME ...]

For each POI named (its model meshes/structures/NAME.glb, its prefab pois/NAME.json), it loads the model's ``collider``
mesh over flat ground and lays a 0.25 m grid of standing spots on every floor, deck, ramp and yard, each kept only
where the player fits (radius 0.35 m from the player data, 1.9 m headroom). Spots join where the floor between them
steps no more than 0.35 m. It floods out from the POI's front edge and reports:

* every marker of the prefab: the surface under it (at the marker's height, on the model or the ground), that nothing
  stands in the space it needs (a part: 0.6 m square, 1.2 m up; a derelict: 3 x 6 m, 2.5 m up, turned by its yaw) and
  that a player can walk up to it (a door marker, which fills its opening in the wall, only that);
* every target settlers.WALK, colony_sites.WALK or warehouses.WALK lists for the POI (inside rooms, decks, silo roofs,
  the maze's heart, a warehouse's yard): reachable or not; a table entry's ``half`` widens the walked area (a yard);
* the share of the footprint a player can walk.

Exits 1 if any check fails. Blender frame: the POI's (x, y, z) is Blender (x, -z, y).
"""
import json
import math
import sys
from collections import deque
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
REPO = Path(__file__).resolve().parents[2]
CELL = 0.25
RADIUS = 0.35
HEADROOM = 1.9
STEP = 0.35


def load(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(REPO / f"mods/vanilla/meshes/structures/{name}.glb"))
    col = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("collider")]
    verts, polys = [], []
    for o in col:
        base = len(verts)
        verts += [o.matrix_world @ v.co for v in o.data.vertices]
        polys += [[base + i for i in p.vertices] for p in o.data.polygons]
    return verts, polys


# Types without a pooled prefab of their own are probed through their first numbered site.
SITE = {"farmer_shack": "poi_04", "campsite": "poi_10", "farmers_hideout": "poi_20", "capsule_scrapyard": "poi_41",
        "haymaze": "poi_42", "large_camp": "poi_43", "scrap_city_garage": "poi_50",
        "packing_station_vegetable": "poi_05", "packing_station_fruit": "poi_18", "silo_cluster": "poi_06",
        "silo_district": "poi_26", "growlab": "poi_14", "chemical_lake_facility": "poi_23"}


def probe(name, walk=None):
    poi = json.loads("\n".join(line for line in (REPO / f"mods/vanilla/pois/{SITE.get(name, name)}.json").read_text().splitlines()
                               if not line.strip().startswith("//")))
    w, _, d = poi["size"]
    verts, polys = load(name)
    X, Y = (walk or {}).get("half", (w / 2 + 9.0, d / 2 + 9.0))
    base = len(verts)
    verts += [Vector((-X - 5, -Y - 5, 0)), Vector((X + 5, -Y - 5, 0)), Vector((X + 5, Y + 5, 0)), Vector((-X - 5, Y + 5, 0))]
    polys.append([base, base + 1, base + 2, base + 3])
    bvh = BVHTree.FromPolygons(verts, polys)
    top = max(v.z for v in verts) + 1.0
    nx, ny = int(2 * X / CELL), int(2 * Y / CELL)
    dirs = [Vector((math.cos(a), math.sin(a), 0)) for a in (k * math.pi / 4 for k in range(8))]
    nodes = {}          # (i, j) -> [z, ...] of standing spots
    for i in range(nx + 1):
        x = -X + i * CELL
        for j in range(ny + 1):
            y = -Y + j * CELL
            z = top
            spots = []
            for _ in range(64):     # every layer down to the ground (a stair tower has many)
                hit = bvh.ray_cast(Vector((x, y, z)), Vector((0, 0, -1)))
                if hit[0] is None:
                    break
                loc, nrm = hit[0], hit[1]
                if abs(nrm.z) > 0.64:
                    up = bvh.ray_cast(loc + Vector((0, 0, 0.02)), Vector((0, 0, 1)), HEADROOM - 0.02)
                    if up[0] is None and all(bvh.ray_cast(loc + Vector((0, 0, hz)), dv, RADIUS)[0] is None for hz in (0.45, 1.1, 1.75) for dv in dirs):
                        spots.append(loc.z)
                z = loc.z - 0.03
            if spots:
                nodes[(i, j)] = spots

    def near(x, y, z, r=0.6, dz=0.3):
        out = []
        for i in range(int((x - r + X) / CELL), int((x + r + X) / CELL) + 2):
            for j in range(int((y - r + Y) / CELL), int((y + r + Y) / CELL) + 2):
                for zz in nodes.get((i, j), ()):
                    if math.hypot(-X + i * CELL - x, -Y + j * CELL - y) <= r and abs(zz - z) <= dz:
                        out.append((i, j, zz))
        return out

    # flood from the front edge
    start = near(0.0, -Y + 1.0, 0.0, r=1.5)
    seen = set(start)
    q = deque(start)
    while q:
        i, j, z = q.popleft()
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            for zz in nodes.get((i + di, j + dj), ()):
                n = (i + di, j + dj, zz)
                if n in seen or abs(zz - z) > STEP:
                    continue
                hz = max(z, zz) + 0.4
                if bvh.ray_cast(Vector((-X + i * CELL, -Y + j * CELL, hz)), Vector((di, dj, 0)), CELL)[0] is not None:
                    continue
                seen.add(n)
                q.append(n)
    fails = []

    def reach(x, y, z, r=0.9):
        return any(n in seen for n in near(x, y, z, r=r))
    print(f"== {name}: {len(seen)} spots walkable of {sum(len(v) for v in nodes.values())} "
          f"({len([n for n in seen if abs(n[0] * CELL - X) < w / 2 and abs(n[1] * CELL - Y) < d / 2])} on the footprint)")
    for k, m in enumerate(poi.get("markers", [])):
        mx, my, mz = m["at"][0], -m["at"][2], m["at"][1]
        hit = bvh.ray_cast(Vector((mx, my, mz + 0.3)), Vector((0, 0, -1)), 0.6)
        support = hit[0] is not None and abs(hit[0].z - mz) < 0.06
        yaw = math.radians(m.get("yaw", 0.0))
        if m["type"] == "derelict":
            hx, hy, hh = 1.5, 3.0, 2.5
        else:
            hx, hy, hh = 0.3, 0.3, 1.2
        clear = True
        for u in range(-2, 3):
            for v in range(-2, 3):
                ox, oy = hx * u / 2, hy * v / 2
                px, py = mx + ox * math.cos(yaw) - oy * math.sin(yaw), my + ox * math.sin(yaw) + oy * math.cos(yaw)
                h = bvh.ray_cast(Vector((px, py, mz + hh)), Vector((0, 0, -1)), hh + 0.5)
                if h[0] is not None and h[0].z > mz + 0.1:
                    clear = False
        ok_reach = reach(mx, my, mz, r=1.6 if m["type"] == "derelict" else 1.0)
        if m["type"] == "door":     # a door fills its opening in the wall: only that you can walk up to it
            support = clear = True
        status = "ok" if support and clear and ok_reach else "FAIL"
        if status != "ok":
            fails.append(f"marker {k} {m.get('ref')}")
        print(f"  marker {k:2d} {m['type']:8s} {m.get('ref', ''):22s} at ({mx:6.2f}, {my:6.2f}, {mz:4.2f}): "
              f"support {'yes' if support else 'NO'}, clear {'yes' if clear else 'NO'}, reach {'yes' if ok_reach else 'NO'}  {status}")
    for label, (tx, ty, tz) in (walk or {}).get("targets", {}).items():
        ok = reach(tx, ty, tz)
        if not ok:
            fails.append(label)
        print(f"  target {label:34s} ({tx:6.2f}, {ty:6.2f}, {tz:5.2f}): {'reached' if ok else 'NOT REACHED'}")
    return fails


def main():
    walks = {}
    for mod in ("settlers", "colony_sites", "warehouses"):
        try:
            walks.update(__import__(mod).WALK)
        except ImportError:
            pass
    bad = {}
    for name in sys.argv[1:]:
        f = probe(name, walks.get(name))
        if f:
            bad[name] = f
    for name, f in bad.items():
        print(f"{name}: FAILED {', '.join(f)}")
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
