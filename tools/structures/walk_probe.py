"""Walk probe: where can a player go on a structure model's collider? python3 walk_probe.py MODEL.glb [options]

Loads the model's ``collider`` mesh (several models in one frame may be given, as for a split model) and stands a
player on it: every 0.25 m it finds each floor (a face no steeper than 45 degrees) with 1.9 m of headroom and nothing
within 0.3 m above step height, links floors to their neighbours when the step up is at most 0.35 m (falls of up to
--drop metres are allowed), and floods out from the start. The ground is the plane z = 0 (the flattened pad) unless
--no-ground. Blender frame: X east, Y north (the POI's -Z), Z up; a POI marker (x, y, z) is (x, -z, y) here.

Options:
  --start x,y           where the player walks in from (default: the bounding box's south-west corner, outside)
  --mark name:x,y,z     a marker (POI coordinates) that must stand on a reachable floor at its height with room
                        above it (repeat)
  --goal name:x,y,z     a place (Blender coordinates) that must be reachable (repeat)
  --drop m              the highest fall a walker takes (default 2.5)
  --cell m              the grid (default 0.25)

Prints each marker and goal with OK or FAIL, the reachable area and the highest floor reached. Exit status 1 on a
FAIL.
"""
import math
import sys
from collections import deque

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

STEP, HEAD, RADIUS, SLOPE = 0.35, 1.9, 0.3, math.cos(math.radians(46))


def load(paths, ground=True):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    verts, polys = [], []
    for p in paths:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=p)
        for ob in set(bpy.data.objects) - before:
            if ob.type == "MESH" and ob.name.startswith("collider"):
                me = ob.data
                m = ob.matrix_world
                base = len(verts)
                verts += [m @ v.co for v in me.vertices]
                polys += [[base + i for i in poly.vertices] for poly in me.polygons]
    if not polys:
        sys.exit("no collider mesh")
    xs = [v.x for v in verts]
    ys = [v.y for v in verts]
    box = (min(xs), max(xs), min(ys), max(ys), max(v.z for v in verts))
    if ground:
        x0, x1, y0, y1 = box[0] - 20, box[1] + 20, box[2] - 20, box[3] + 20
        b = len(verts)
        verts += [Vector((x0, y0, 0)), Vector((x1, y0, 0)), Vector((x1, y1, 0)), Vector((x0, y1, 0))]
        polys.append([b, b + 1, b + 2, b + 3])
    return BVHTree.FromPolygons(verts, polys), box


def floors(bvh, x, y, top):
    """Every walkable surface in the column at x, y, from the top down: (z, normal z)."""
    out = []
    o = Vector((x, y, top))
    d = Vector((0, 0, -1))
    for _ in range(40):
        hit, n, _i, dist = bvh.ray_cast(o, d)
        if hit is None:
            break
        if abs(n.z) >= SLOPE:
            out.append(hit.z)
        o = hit + d * 0.005
    return sorted(set(round(z, 3) for z in out))


def clear_up(bvh, x, y, z, h):
    hit = bvh.ray_cast(Vector((x, y, z)), Vector((0, 0, 1)), h)
    return hit[0] is None


def standable(bvh, x, y, z):
    if not clear_up(bvh, x, y, z + 0.05, HEAD - 0.05):
        return False
    for dx, dy in ((RADIUS, 0), (-RADIUS, 0), (0, RADIUS), (0, -RADIUS)):
        if not clear_up(bvh, x + dx, y + dy, z + STEP + 0.01, HEAD - STEP - 0.01):
            return False
    return True


def main(argv):
    paths, marks, goals = [], [], []
    start, drop, cell, ground = None, 2.5, 0.25, True
    it = iter(argv)
    for a in it:
        if a == "--start":
            start = tuple(float(v) for v in next(it).split(","))
        elif a == "--mark":
            n, v = next(it).split(":")
            x, y, z = (float(c) for c in v.split(","))
            marks.append((n, (x, -z, y)))
        elif a == "--goal":
            n, v = next(it).split(":")
            goals.append((n, tuple(float(c) for c in v.split(","))))
        elif a == "--drop":
            drop = float(next(it))
        elif a == "--cell":
            cell = float(next(it))
        elif a == "--no-ground":
            ground = False
        else:
            paths.append(a)
    bvh, (x0, x1, y0, y1, top) = load(paths, ground)
    x0, x1, y0, y1 = x0 - 2, x1 + 2, y0 - 2, y1 + 2
    nx, ny = int((x1 - x0) / cell) + 1, int((y1 - y0) / cell) + 1
    top += 5
    nodes = {}
    for i in range(nx):
        for j in range(ny):
            x, y = x0 + i * cell, y0 + j * cell
            zs = [z for z in floors(bvh, x, y, top) if standable(bvh, x, y, z)]
            if zs:
                nodes[(i, j)] = zs
    si, sj = (0, 0) if start is None else (round((start[0] - x0) / cell), round((start[1] - y0) / cell))
    si, sj = min(max(si, 0), nx - 1), min(max(sj, 0), ny - 1)
    seen = set()
    q = deque()
    for z in nodes.get((si, sj), []):
        seen.add((si, sj, z))
        q.append((si, sj, z))
    while q:
        i, j, z = q.popleft()
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            for z2 in nodes.get((i + di, j + dj), ()):
                if -drop <= z2 - z <= STEP and (i + di, j + dj, z2) not in seen:
                    seen.add((i + di, j + dj, z2))
                    q.append((i + di, j + dj, z2))
    ok = True

    def reach(x, y, z, tol=0.12):
        i, j = round((x - x0) / cell), round((y - y0) / cell)
        best = None
        for di in (-1, 0, 1):
            for dj in (-1, 0, 1):
                for zz in nodes.get((i + di, j + dj), ()):
                    if abs(zz - z) <= tol and (i + di, j + dj, zz) in seen:
                        best = zz
        return best

    for n, (x, y, z) in marks:
        fl = floors(bvh, x, y, z + 3.0)
        under = max([f for f in fl if f <= z + 0.02], default=None)
        room = clear_up(bvh, x, y, z + 0.02, 1.0)
        r = reach(x, y, z)
        good = under is not None and abs(under - z) <= 0.03 and room and r is not None
        ok &= good
        print(f"mark {n} at {x:.2f},{y:.2f},{z:.2f}: floor {under}, room above {'yes' if room else 'NO'}, reachable {'yes' if r is not None else 'NO'} -> {'OK' if good else 'FAIL'}")
    for n, (x, y, z) in goals:
        r = reach(x, y, z, tol=0.3)
        ok &= r is not None
        print(f"goal {n} at {x:.2f},{y:.2f},{z:.2f}: {'reached at %.2f' % r if r is not None else 'NOT reached'} -> {'OK' if r is not None else 'FAIL'}")
    high = max((z for _i, _j, z in seen), default=0)
    print(f"reachable: {len(seen) * cell * cell:.0f} m2 of floor, highest floor {high:.2f} m; nodes {len(nodes)} columns")
    return 0 if ok else 1


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    sys.exit(main(args))
