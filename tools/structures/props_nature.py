"""Nature props for the wild between places (docs/world-generation.md, Scenes), in the look of docs/art-direction.md:
rock in the Old Stone material, chiselled like the rock deposit blocks (tools/rocks), and wood in the bark of the stump
and the fallen log.

Rock: a boulder split in two, a small natural arch, an 8 m spire, an overhanging shelf, rocks draped in seaweed and a
big nest of sticks on a rock. Wood: a hollow log, a pile of fallen branches, three stumps, bleached driftwood, a 12 m
fallen tree with its root plate and an arch of exposed roots. Plants: brambles, a berry bush, a giant fern, reeds, lily
pads, cattails, a 3 m mushroom, three cacti and a dry bush with a tumbleweed caught in it. Bones and mounds: a shell
midden, a huge skull, a giant rib cage and a low spread of termite spires.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh
import bpy
from mathutils import Quaternion, Vector

import kit as K
from families import M, stone, BONE, GRAPHITE, MUSTARD, STONE_D
from props import _root
from props_stone import (dstone, moss, bark, heart, earth, rock, limb, lumpy_cone, empty, root_tangle,
                         MOSS, WATER, EARTH, EARTH_D, SNAG, ROT, CAP, STEM, GILL, TERMITE)
from props_settler_a import lump

LEAF, LEAF_D, LEAF_L = "#56803a", "#3f6630", "#6c8f44"
GRASS = "#5d8f3a"
FERN, FERN_D, FERN_DRY = "#5a8a3c", "#4a7532", "#8a7650"
BERRY = "#2f2a44"
CANE = "#5e3a36"
DRYLEAF = "#7d6c48"
DRIFT = "#c2bbae"
WRACK, KELP = "#4f5426", "#646a30"
SAND = "#c9b88e"
SHELL, SHELL_D, SHELL_P, SHELL_G = "#e6dccb", "#a99c88", "#cfb6a6", "#8d8a84"
REED, REED_D, PLUME = "#7f9444", "#a49a62", "#9a8676"
TYPHA = "#4e3424"
LILY, LILY_D, PETAL = "#4c8236", "#3b6a2c", "#eee6da"
CACTUS = "#5d7f47"
TWIG, TUMBLE, DRYBUSH = "#7b6a55", "#a8946c", "#8a8a62"
SCALE, STEM_SCALE = "#7c5f45", "#b09a7c"
NEST = "#7a6650"
HOLE = "#1c1a17"

Z = Vector((0, 0, 1))


# ---------------------------------------------------------------- helpers
def leaf(shade=0):
    return K.mat(f"leaf{shade}", (LEAF, LEAF_D, LEAF_L)[shade % 3], rough=0.8, grime=0.15, noise_scale=5.0)


def mat_of(name, color, **kw):
    kw.setdefault("rough", 0.85)
    return K.mat(name, color, **kw)


def bez(p0, c, p1, n):
    """n points along a quadratic curve from p0 to p1 pulled towards c."""
    return [tuple((1 - t) ** 2 * p0[k] + 2 * (1 - t) * t * c[k] + t * t * p1[k] for k in range(3)) for t in (i / (n - 1) for i in range(n))]


def bez3(p0, c0, c1, p1, n):
    """n points along a cubic curve."""
    out = []
    for i in range(n):
        t = i / (n - 1)
        u = 1 - t
        out.append(tuple(u ** 3 * p0[k] + 3 * u * u * t * c0[k] + 3 * u * t * t * c1[k] + t ** 3 * p1[k] for k in range(3)))
    return out


def tube(P, name, pts, radii, mat, sides=6, flat=1.0, ribs=0, rib=0.0, smooth=True):
    """A bent round piece (branch, root, rib, horn, cactus arm) through pts; radii is one value, (start, end) or one per
    point; flat squashes the section, ribs fluting adds rib ridges."""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * n
    elif len(radii) == 2 and n != 2:
        radii = [radii[0] + (radii[1] - radii[0]) * i / (n - 1) for i in range(n)]
    bm = bmesh.new()
    T = [(pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized() for i in range(n)]
    ref = Z if abs(T[0].z) < 0.95 else Vector((1, 0, 0))
    N = ref.cross(T[0]).normalized()
    rings = []
    for i in range(n):
        N = (N - T[i] * N.dot(T[i])).normalized()
        B = T[i].cross(N)
        r = max(radii[i], 0.002)
        ring = []
        for j in range(sides):
            a = 2 * math.pi * j / sides
            rr = r * (1 + rib * math.cos(ribs * a))
            ring.append(bm.verts.new(pts[i] + (N * math.cos(a) + B * math.sin(a) * flat) * rr))
        rings.append(ring)
    for i in range(n - 1):
        for j in range(sides):
            k = (j + 1) % sides
            bm.faces.new((rings[i][j], rings[i][k], rings[i + 1][k], rings[i + 1][j]))
    bm.faces.new(rings[0])
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K.place(K.from_bm(name, bm, mat, smooth=smooth), parent=P)


def _tri(bm, vs, want):
    """A triangle facing along want."""
    a, b, c = (v.co for v in vs)
    if (b - a).cross(c - a).dot(want) < 0:
        vs = vs[::-1]
    return bm.faces.new(vs)


def blade(P, name, pts, w, mat, t=0.004):
    """A strap leaf or ribbon along pts, w wide at its base and narrowing to its tip, faced on both sides."""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    bm = bmesh.new()
    rows = []
    for i in range(n):
        T = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        S = T.cross(Z)
        S = S.normalized() if S.length > 1e-4 else Vector((1, 0, 0))
        Nn = S.cross(T)
        ww = max(w * (1 - (i / (n - 1)) ** 1.6), 0.002) / 2
        rows.append([bm.verts.new(pts[i] + S * s * ww + Nn * o * t / 2) for o in (1, -1) for s in (1, -1)] + [Nn])
    for i in range(n - 1):
        a, b = rows[i], rows[i + 1]
        for side, (l, r) in ((1, (0, 1)), (-1, (2, 3))):
            for q in ((a[l], a[r], b[r]), (a[l], b[r], b[l])):
                _tri(bm, list(q), a[4] * side)
    return K.place(K.from_bm(name, bm, mat, smooth=True), parent=P)


def tuft(P, name, loc, rnd, n=7, h=0.35, mat=None, w=0.03, spread=0.1):
    """A tuft of grass blades leaning out from loc."""
    mat = mat or mat_of("grass", GRASS, noise_scale=6.0)
    out = []
    for k in range(n):
        yaw = rnd.uniform(0, 2 * math.pi)
        lean = rnd.uniform(0.15, 0.6)
        L = h * rnd.uniform(0.6, 1.1)
        b = (loc[0] + math.cos(yaw) * spread * rnd.random(), loc[1] + math.sin(yaw) * spread * rnd.random(), loc[2] - 0.02)
        d = (math.cos(yaw), math.sin(yaw))
        tip = (b[0] + d[0] * L * lean * 1.3, b[1] + d[1] * L * lean * 1.3, b[2] + L * (1 - lean * 0.5))
        c = (b[0] + d[0] * L * lean * 0.2, b[1] + d[1] * L * lean * 0.2, b[2] + L * 0.75)
        out.append(blade(P, f"{name}{k}", bez(b, c, tip, 4), w, mat))
    return out


def ball(P, name, r, loc, mat, scale=(1, 1, 1), subd=1):
    """A small round thing (berry, egg, pebble) that costs few triangles."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subd, radius=r)
    ob = K.from_bm(name, bm, mat, smooth=True)
    ob.scale = scale
    return K.place(ob, loc, parent=P)


def crag_points(size, rnd, chip=(0.2, 0.5), taper=0.8, bulge=0.07, lean=(0.0, 0.0), facets=2):
    """The corners of a chiselled block size (x, y, height) standing on z = 0, knocked off unevenly like the rock
    deposit blocks, narrower at the top (lean shifts the top face)."""
    hx, hy, h = size[0] / 2, size[1] / 2, size[2]
    m = min(hx, hy, h / 2)
    pts = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            for top in (0, 1):
                k = taper if top else 1.0
                cx = sx * hx * k * rnd.uniform(0.92, 1.05) + lean[0] * top
                cy = sy * hy * k * rnd.uniform(0.92, 1.05) + lean[1] * top
                cz = h * rnd.uniform(0.9, 1.03) if top else 0.0
                d = [m * rnd.uniform(*chip) * (1.0 if top else 0.5) for _ in range(3)]
                dz = -d[2] if top else d[2]
                pts += [(cx - sx * d[0], cy, cz), (cx, cy - sy * d[1], cz), (cx, cy, cz + dz)]
    for sx in (-1, 1):
        for _ in range(facets):
            v = rnd.uniform(0.2, 0.75)
            f = (1 + (taper - 1) * v) * (1 + rnd.uniform(0, bulge))
            pts.append((sx * hx * f + lean[0] * v, rnd.uniform(-0.6, 0.6) * hy * f + lean[1] * v, v * h))
    for sy in (-1, 1):
        for _ in range(facets):
            v = rnd.uniform(0.2, 0.75)
            f = (1 + (taper - 1) * v) * (1 + rnd.uniform(0, bulge))
            pts.append((rnd.uniform(-0.6, 0.6) * hx * f + lean[0] * v, sy * hy * f + lean[1] * v, v * h))
    pts.append((rnd.uniform(-0.25, 0.25) * hx * taper + lean[0], rnd.uniform(-0.25, 0.25) * hy * taper + lean[1], h * rnd.uniform(1.0, 1.06)))
    return pts


def boulder_points(size, rnd, n=40, power=2.6, jitter=0.07, sink=0.12):
    """Points on a rounded-off box size (x, y, height) standing on z = 0: a boulder worn by weather, flat underneath."""
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / (2 - sink * 2)
    pts = []
    for i in range(n):
        # a spread of directions (golden spiral), each pushed out to the rounded box and jittered
        z = 1 - 2 * (i + 0.5) / n
        a = i * 2.39996 + rnd.uniform(-0.2, 0.2)
        r = math.sqrt(1 - z * z)
        d = (r * math.cos(a), r * math.sin(a), z)
        k = (abs(d[0]) ** power + abs(d[1]) ** power + abs(d[2]) ** power) ** (-1 / power)
        j = 1 + rnd.uniform(-jitter, jitter)
        pts.append((d[0] * k * hx * j, d[1] * k * hy * j, max(0.0, hz * (1 + d[2] * k * j) - hz * sink * 2)))
    return pts


def boulder(P, name, size, loc, rnd, rot=None, mat=None, **kw):
    """A weathered boulder size (x, y, height), its base at loc."""
    if rot is None:
        rot = (0, 0, rnd.uniform(0, 360))
    return hull(P, name, boulder_points(size, rnd, **kw), mat or stone(), loc, rot, bev=0.04)


def leaf_cluster(items, c, r, n, rnd, size=0.16):
    """Adds n leaves to items, scattered over a ball of radius r round c and facing out of it."""
    for _ in range(n):
        a, el = rnd.uniform(0, 2 * math.pi), rnd.uniform(-0.4, 1.3)
        d = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el)))
        p = Vector(c) + d * r * rnd.uniform(0.5, 1.0)
        L = size * rnd.uniform(0.8, 1.2)
        items.append((tuple(p), math.degrees(a) + rnd.uniform(-50, 50), rnd.uniform(-35, 25), L, L * 0.55))
    return items


def hull(P, name, pts, mat, loc=(0, 0, 0), rot=(0, 0, 0), bev=0.05):
    """The convex hull of pts as a bevelled faceted rock."""
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in pts]
    res = bmesh.ops.convex_hull(bm, input=vs)
    junk = list({g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)})
    bmesh.ops.delete(bm, geom=junk, context="VERTS")
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.01)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(3), verts=bm.verts, edges=bm.edges)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat)
    if bev:
        K.bevel(ob, bev, segs=2, angle=30)
    return K.place(ob, loc, rot, P)


def crag(P, name, size, loc, rnd, rot=None, mat=None, bev=None, **kw):
    """A chiselled rock size (x, y, height), its base at loc."""
    if rot is None:
        rot = (rnd.uniform(-3, 3), rnd.uniform(-3, 3), rnd.uniform(0, 360))
    if bev is None:
        bev = min(0.06, min(size) * 0.04)
    return hull(P, name, crag_points(size, rnd, **kw), mat or stone(), loc, rot, bev)


def surface_z(x, y, top=30.0):
    """The height of whatever is under (x, y), or 0 on open ground."""
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    hit, loc, _n, _i, _o, _m = bpy.context.scene.ray_cast(dg, (x, y, top), (0, 0, -1))
    return max(loc.z, 0.0) if hit else 0.0


def orient(ob, normal, spin=0.0):
    """Turns ob so its local Z lies along normal, spun spin degrees about it."""
    q = Vector(normal).normalized().to_track_quat("Z", "Y") @ Quaternion((0, 0, 1), math.radians(spin))
    ob.rotation_euler = q.to_euler()
    return ob


def leaves(P, name, items, mat, t=0.004):
    """Loose single leaves in one mesh: items of (base, yaw, pitch, length, width) in degrees and metres, each leaf a
    pointed diamond faced on both sides."""
    bm = bmesh.new()
    for base, yaw, pitch, L, W in items:
        b = Vector(base)
        y, p = math.radians(yaw), math.radians(pitch)
        d = Vector((math.cos(y) * math.cos(p), math.sin(y) * math.cos(p), math.sin(p)))
        s = Vector((-math.sin(y), math.cos(y), 0))
        n = s.cross(d) if s.cross(d).z >= 0 else d.cross(s)
        ring = [b, b + d * L * 0.45 + s * W * 0.5, b + d * L, b + d * L * 0.45 - s * W * 0.5]
        for o in (1, -1):
            vs = [bm.verts.new(v + n * o * t / 2) for v in ring]
            for i in range(1, 3):
                _tri(bm, [vs[0], vs[i], vs[i + 1]], n * o)
    return K.place(K.from_bm(name, bm, mat, smooth=True), parent=P)


def foliage(P, name, r, loc, rnd, shade=0, scale=(1, 1, 0.8), mat=None, u=12, v=8):
    """A mass of leaves: a lumpy ball, flattened underneath."""
    return lump(P, name, r, loc, scale, mat or leaf(shade), rnd, jitter=0.16, u=u, v=v, flat=0.55)


def bracket(P, name, loc, yaw, r, mat=None):
    """A shelf fungus on bark, facing out along yaw."""
    mat = mat or K.mat("bracket", CAP, rough=0.8, grime=0.3, noise_scale=6.0)
    return K.cyl(name, r, 0.05, mat, loc=loc, rot=(0, 0, yaw), segs=12, r2=r * 0.85, bev=0.01, parent=P)


# ================================================================== rock
def boulder_split(seed=41):
    """A 3 m boulder split clean in two by frost, the halves leaning apart, a sapling growing in the crack."""
    rnd = random.Random(seed)
    P = _root("splitboulder")
    s = stone()
    pts = boulder_points((3.3, 2.8, 2.5), rnd, n=44)
    for k, sx in enumerate((-1, 1)):
        G = empty(P, f"half{k}", (sx * 0.16, 0, -0.15), (0, sx * 7, sx * 3))
        half = []
        for x, y, z in pts:
            c = 0.06 * z - 0.05
            half.append((c if sx * (x - c) < 0 else x, y, z))
        hull(G, f"half{k}", half, s, bev=0.05)
    K.box("crack_soil", (0.34, 2.0, 0.1), earth(), loc=(0.02, 0, 0.0), bev=0.03, parent=P)
    # a rowan sapling come up in the crack
    stem = bark(0.0)
    tube(P, "sapling", bez((0.02, -0.6, 0.0), (0.06, -0.62, 1.4), (0.0, -0.5, 2.9), 6), (0.045, 0.012), stem, sides=6)
    limb(P, "twig0", (0.04, -0.6, 2.2), 20, 35, 0.6, 0.016, 0.006, stem, segs=5, bev=0)
    limb(P, "twig1", (0.02, -0.56, 2.5), 200, 30, 0.55, 0.014, 0.005, stem, segs=5, bev=0)
    crown = []
    for c, r in (((0.0, -0.5, 2.95), 0.32), ((0.5, -0.42, 2.55), 0.26), ((-0.5, -0.62, 2.72), 0.26), ((0.05, -0.6, 2.4), 0.2)):
        leaf_cluster(crown, c, r, 40, rnd, size=0.17)
    leaves(P, "crown", crown[::2], leaf(2))
    leaves(P, "crown_d", crown[1::2], leaf(0))
    for k in range(4):
        rock(P, f"chip{k}", rnd.uniform(0.1, 0.22), rnd.uniform(0.08, 0.15), (rnd.uniform(-0.3, 0.3), rnd.uniform(-1.6, -1.3), 0.04), rnd)
    rock(P, "chip_back", 0.25, 0.18, (1.7, 0.9, 0.06), rnd)
    tuft(P, "tuft0", (0.0, -1.2, 0.0), rnd, h=0.4)
    tuft(P, "tuft1", (-1.7, 0.4, 0.0), rnd, h=0.35)
    tuft(P, "tuft2", (1.6, -0.8, 0.0), rnd, h=0.3)
    moss(P, "moss0", (-1.5, -0.9, 0.0), 0.3, flat=0.4)
    moss(P, "moss1", (1.3, 1.1, 0.0), 0.35, flat=0.4)
    return P


def rock_arch_small(seed=42):
    """A natural arch about 4 m high: two weathered legs of piled rock and a span of fused blocks across them."""
    rnd = random.Random(seed)
    P = _root("rockarch")
    s, sd = stone(), dstone()
    for sx in (-1, 1):
        crag(P, f"foot{sx}", (1.9, 2.0, 1.4), (sx * 1.75, rnd.uniform(-0.1, 0.1), -0.15), rnd, rot=(0, 0, rnd.uniform(-10, 10)), taper=0.85)
        crag(P, f"leg{sx}", (1.4, 1.6, 1.4), (sx * 1.6, 0.0, 1.05), rnd, rot=(0, sx * -6, rnd.uniform(-8, 8)), taper=0.85,
             mat=sd if sx > 0 else s)
    R, zc = 1.5, 2.3
    n = 7
    for k in range(n):
        a = math.radians(170 - 160 * k / (n - 1))
        G = empty(P, f"span{k}", (R * math.cos(a), 0, zc + R * math.sin(a) * 0.85), (0, math.degrees(a) + 90, 0))
        t = 0.95 - 0.25 * math.sin(a)
        crag(G, f"block{k}", (1.35, rnd.uniform(1.3, 1.5), t), (0, 0, -t / 2), rnd, rot=(rnd.uniform(-4, 4), 0, rnd.uniform(-6, 6)),
             taper=0.9, chip=(0.15, 0.4), mat=sd if k == 4 else s)
    crag(P, "cap", (1.6, 1.3, 0.55), (0.25, 0.05, zc + R * 0.85 + 0.1), rnd, rot=(0, 4, 12), taper=0.75)
    for k in range(6):
        sx = rnd.choice((-1, 1))
        rock(P, f"rubble{k}", rnd.uniform(0.12, 0.3), rnd.uniform(0.1, 0.22), (sx * rnd.uniform(0.5, 2.8), rnd.uniform(-1.6, 1.4), 0.05), rnd)
    moss(P, "moss0", (-2.3, -0.9, 0.0), 0.35, flat=0.4)
    moss(P, "moss1", (2.4, 0.9, 0.0), 0.3, flat=0.4)
    tuft(P, "tuft0", (-1.0, -1.0, 0.0), rnd, h=0.4)
    tuft(P, "tuft1", (2.6, -0.8, 0.0), rnd, h=0.35)
    tuft(P, "tuft_top", (0.1, 0.0, surface_z(0.1, 0.0)), rnd, h=0.3, n=5)
    return P


def rock_spire(seed=43):
    """An 8 m spire of stacked weathered blocks on a broad foot, darker bands between, a bush clinging to its top."""
    rnd = random.Random(seed)
    P = _root("spire")
    s, sd = stone(), dstone()
    crag(P, "foot", (3.2, 2.9, 1.5), (0, 0, -0.15), rnd, taper=0.75)
    crag(P, "foot_l", (1.7, 1.5, 1.0), (-1.6, 0.6, -0.1), rnd, taper=0.7)
    crag(P, "foot_r", (1.4, 1.6, 0.8), (1.45, -0.7, -0.1), rnd, taper=0.7)
    z, x, y = 1.15, 0.0, 0.0
    sizes = [(2.3, 2.1, 1.6), (2.0, 1.9, 1.3), (1.75, 1.6, 0.5), (1.8, 1.7, 1.4), (1.5, 1.4, 1.2), (1.35, 1.25, 0.45), (1.3, 1.2, 1.1), (1.0, 0.95, 0.75)]
    for k, (w, d, h) in enumerate(sizes):
        band = h < 0.6
        crag(P, f"tier{k}", (w, d, h), (x, y, z), rnd, rot=(rnd.uniform(-3, 3), rnd.uniform(-3, 3), rnd.uniform(0, 360)), taper=0.88 if band else 0.82,
             mat=sd if band else s, chip=(0.15, 0.4))
        z += h * 0.9
        x += rnd.uniform(-0.08, 0.1)
        y += rnd.uniform(-0.06, 0.06)
    top = surface_z(x, y)
    moss(P, "moss_top", (x, y, top - 0.05), 0.4, flat=0.3)
    foliage(P, "bush0", 0.35, (x + 0.15, y - 0.05, top), rnd, shade=1, scale=(1.2, 1.0, 0.8))
    foliage(P, "bush1", 0.25, (x - 0.2, y + 0.15, top - 0.05), rnd, shade=0)
    tuft(P, "tuft_top", (x - 0.1, y - 0.25, top - 0.05), rnd, h=0.35, n=6)
    for k in range(8):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(2.0, 2.9)
        rock(P, f"scree{k}", rnd.uniform(0.15, 0.35), rnd.uniform(0.12, 0.25), (math.cos(a) * r, math.sin(a) * r, 0.05), rnd)
    tuft(P, "tuft0", (-1.2, -1.5, 0.0), rnd, h=0.4)
    tuft(P, "tuft1", (2.0, 0.6, 0.0), rnd, h=0.4)
    moss(P, "moss0", (1.5, 1.4, 0.0), 0.4, flat=0.4)
    return P


def rock_shelf(seed=44):
    """A flat slab of rock 4 m across jutting out over the grass from a rock mass, room to shelter under its lip."""
    rnd = random.Random(seed)
    P = _root("shelf")
    s, sd = stone(), dstone()
    crag(P, "mass", (4.8, 2.4, 2.5), (0, 1.2, -0.1), rnd, rot=(0, 0, 3), taper=0.9)
    crag(P, "face", (4.6, 1.6, 1.3), (0.2, 1.8, 2.5), rnd, rot=(0, 0, -4), taper=0.7, mat=sd)
    crag(P, "mass_r", (1.6, 1.6, 1.7), (1.9, 1.3, -0.1), rnd, rot=(0, 0, 20), taper=0.8, mat=sd)
    crag(P, "mass_l", (1.3, 1.4, 1.2), (-2.0, 1.4, -0.1), rnd, rot=(0, 0, -12), taper=0.75)
    crag(P, "slab", (4.6, 3.6, 0.6), (0.1, 0.0, 2.2), rnd, rot=(-4, 2, 4), taper=0.95, chip=(0.15, 0.45))
    crag(P, "slab_top", (2.2, 1.4, 0.5), (-1.2, 0.6, 2.7), rnd, rot=(-3, 0, -8), taper=0.85)
    for k in range(5):
        rock(P, f"fallen{k}", rnd.uniform(0.15, 0.35), rnd.uniform(0.12, 0.25), (rnd.uniform(-1.8, 1.8), rnd.uniform(-1.4, -0.2), 0.05), rnd)
    tuft(P, "tuft_top", (0.9, -0.6, surface_z(0.9, -0.6)), rnd, h=0.3, n=5)
    moss(P, "moss0", (-1.2, -0.5, 0.0), 0.35, flat=0.4)
    tuft(P, "tuft0", (1.8, -1.3, 0.0), rnd, h=0.4)
    tuft(P, "tuft1", (-2.2, -0.2, 0.0), rnd, h=0.35)
    return P


def seaweed_rocks(seed=45):
    """A knot of wet shore rocks, their lower half draped in wrack, barnacles above it and kelp washed up round them."""
    rnd = random.Random(seed)
    P = _root("seaweedrocks")
    wet = K.mat("wetstone", STONE_D, rough=0.45, grime=0.4, cracks=0.7, noise_scale=3.0)
    weed = K.mat("wrack", WRACK, rough=0.55, grime=0.2, noise_scale=7.0)
    kelp = K.mat("kelp", KELP, rough=0.5, noise_scale=5.0)
    for k, (size, loc, yaw) in enumerate((((2.2, 1.8, 1.5), (0.0, 0.2, -0.1), 15), ((1.4, 1.3, 1.0), (1.5, -0.4, -0.1), -20),
                                           ((1.1, 1.2, 0.7), (-1.4, -0.5, -0.1), 40))):
        pts = boulder_points(size, rnd, n=34, power=2.3)
        hull(P, f"rock{k}", pts, wet, loc, (rnd.uniform(-3, 3), rnd.uniform(-3, 3), yaw), bev=0.05)
        cut = size[2] * rnd.uniform(0.45, 0.6)
        tilt = rnd.uniform(-0.12, 0.12)
        skirt = []
        for x, y, z in pts:
            c = cut + tilt * x
            skirt.append((x * 1.04, y * 1.04, min(z, c) if z > c else z))
        hull(P, f"weed{k}", skirt, weed, loc, (rnd.uniform(-3, 3), rnd.uniform(-3, 3), yaw), bev=0.04)
    # strands hanging off the wrack line and lying round the foot
    for k in range(14):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(1.0, 2.3)
        b = (math.cos(a) * r * 1.1, math.sin(a) * r * 0.85, 0.02)
        e = (b[0] + math.cos(a + 1.3) * 0.7, b[1] + math.sin(a + 1.3) * 0.7, 0.02)
        c = ((b[0] + e[0]) / 2 + rnd.uniform(-0.2, 0.2), (b[1] + e[1]) / 2 + rnd.uniform(-0.2, 0.2), 0.03)
        blade(P, f"strand{k}", bez(b, c, e, 5), rnd.uniform(0.07, 0.12), kelp if k % 3 else weed)
    for k in range(4):
        a = rnd.uniform(0, 2 * math.pi)
        lump(P, f"heap{k}", 0.25, (math.cos(a) * 2.2, math.sin(a) * 1.6, 0.0), (1.4, 1.0, 0.35), kelp, rnd, jitter=0.2, u=8, v=5)
    bar = K.mat("barnacle", SHELL_G, rough=0.9, grime=0.3, noise_scale=12.0)
    for k in range(28):
        x, y = rnd.uniform(-0.7, 0.8), rnd.uniform(-0.4, 0.8)
        z = surface_z(x, y)
        if z < 1.0:
            continue
        K.cyl(f"barnacle{k}", rnd.uniform(0.025, 0.04), 0.035, bar, loc=(x, y, z), segs=6, r2=0.012, bev=0, parent=P)
    return P


# ================================================================== wood
def hollow_log(seed=46):
    """A 5 m trunk 1.8 m thick lying in the grass, rotted hollow so you could crawl through it, mossy along the top."""
    rnd = random.Random(seed)
    P = _root("hollowlog")
    b = bark(0.5)
    rot_m = K.mat("rot", ROT, rough=1.0, noise_scale=12.0, cracks=0.5)
    old = K.mat("heart_old", SNAG, rough=0.95, cracks=0.6)
    G = empty(P, "log", (0, 0, 0.84), (0, 0, 6))
    bm = bmesh.new()
    na, nl, L, Ro, Ri = 16, 8, 5.0, 0.9, 0.66
    ph = [rnd.uniform(0, 6.28) for _ in range(3)]
    ends = [[rnd.uniform(-0.18, 0.18) for _ in range(na)] for _ in range(2)]
    rings = []
    for i in range(nl + 1):
        f = i / nl
        x = -L / 2 + L * f
        taper = 1 - 0.1 * f
        ro_, ri_ = [], []
        for j in range(na):
            a = 2 * math.pi * j / na
            w = 1 + 0.05 * math.sin(3 * a + ph[0] + f * 2) + 0.03 * math.sin(5 * a + ph[1])
            xx = x + (ends[0][j] if i == 0 else ends[1][j] if i == nl else 0)
            ro_.append(bm.verts.new((xx, Ro * taper * w * math.cos(a), Ro * taper * w * math.sin(a) * 0.95)))
            wi = 1 + 0.08 * math.sin(4 * a + ph[2])
            ri_.append(bm.verts.new((xx, Ri * taper * wi * math.cos(a), Ri * taper * wi * math.sin(a) * 0.95)))
        rings.append((ro_, ri_))
    for i in range(nl):
        (o0, i0), (o1, i1) = rings[i], rings[i + 1]
        for j in range(na):
            k = (j + 1) % na
            bm.faces.new((o0[j], o0[k], o1[k], o1[j])).smooth = True
            f = bm.faces.new((i0[j], i1[j], i1[k], i0[k]))
            f.smooth = True
            f.material_index = 1
    for e, (o, i_) in enumerate((rings[0], rings[-1])):
        for j in range(na):
            k = (j + 1) % na
            q = (o[j], i_[j], i_[k], o[k]) if e == 0 else (o[j], o[k], i_[k], i_[j])
            bm.faces.new(q).material_index = 2
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm("shell", bm, b)
    ob.data.materials.append(rot_m)
    ob.data.materials.append(old)
    K.place(ob, parent=G)
    K.box("litter", (L - 0.5, 0.9, 0.06), rot_m, loc=(0, 0, -0.56), bev=0.03, parent=G)
    for k in range(5):
        rock(G, f"debris{k}", rnd.uniform(0.06, 0.12), 0.05, (rnd.uniform(-2.0, 2.0), rnd.uniform(-0.3, 0.3), -0.52), rnd, mat=rot_m)
    # a snapped branch, bracket fungus down one flank, moss along the top
    limb(G, "stub", (0.8, 0.4, 0.62), 60, 35, 0.7, 0.14, 0.1, b, segs=8)
    cp, sp = math.cos(math.radians(35)) * 0.7, math.sin(math.radians(35)) * 0.7
    K.cyl("stub_end", 0.1, 0.02, heart(), loc=(0.8 + cp * math.cos(math.radians(60)), 0.4 + cp * math.sin(math.radians(60)), 0.62 + sp), rot=(0, 55, 60),
          segs=8, bev=0, parent=G)
    for k, (x, a, r) in enumerate(((-1.2, -40, 0.16), (-0.9, -45, 0.12), (-1.0, -30, 0.1), (1.6, -50, 0.14))):
        ar = math.radians(a)
        bracket(G, f"fungus{k}", (x, math.cos(ar) * 0.9, math.sin(ar) * 0.9 + 0.05 * k), -90, r)
    moss(P, "moss_foot", (-2.0, 1.0, 0.0), 0.35, flat=0.4)
    for k, (x, y) in enumerate(((2.4, -0.9), (-2.5, 1.0), (0.5, -1.1))):
        tuft(P, f"tuft{k}", (x, y, 0.0), rnd, h=0.45)
    return P


def tree_fallen_branches(seed=47):
    """A heap of fallen branches 3.5 m across, the bigger limbs underneath, twigs and dead leaves on top."""
    rnd = random.Random(seed)
    P = _root("branches")
    b = bark(0.2)
    dry = mat_of("dryleaf", DRYLEAF, noise_scale=8.0, grime=0.2)
    # (start, end, r0, height of the middle above the straight line)
    limbs = (((-1.7, -0.4, 0.12), (1.6, 0.5, 0.1), 0.11, 0.05), ((-1.2, 0.9, 0.1), (1.4, -0.7, 0.2), 0.09, 0.12),
             ((-0.4, -1.3, 0.08), (0.6, 1.4, 0.32), 0.08, 0.12), ((1.7, -1.0, 0.07), (-0.9, 0.3, 0.38), 0.07, 0.15),
             ((-1.6, 0.2, 0.3), (0.9, 1.1, 0.12), 0.06, 0.1), ((0.2, -1.4, 0.05), (1.5, 0.9, 0.36), 0.06, 0.12),
             ((-1.0, -1.0, 0.45), (1.0, 0.4, 0.45), 0.05, 0.1))
    for k, (p0, p1, r0, lift) in enumerate(limbs):
        c = tuple((p0[i] + p1[i]) / 2 + (rnd.uniform(-0.25, 0.25) if i < 2 else lift) for i in range(3))
        pts = bez(p0, c, p1, 6)
        tube(P, f"limb{k}", pts, (r0, r0 * 0.3), b, sides=7)
        K.cyl(f"limb_end{k}", r0 * 0.95, 0.02, heart(), loc=pts[0], rot=(0, 90 - math.degrees(math.atan2(pts[1][2] - pts[0][2],
              math.dist(pts[1][:2], pts[0][:2]))), math.degrees(math.atan2(pts[1][1] - pts[0][1], pts[1][0] - pts[0][0]))), segs=7, bev=0, parent=P)
        for q in range(2):
            i = rnd.randint(2, 4)
            yaw = math.degrees(math.atan2(pts[i + 1][1] - pts[i][1], pts[i + 1][0] - pts[i][0])) + rnd.choice((-1, 1)) * rnd.uniform(25, 50)
            L = rnd.uniform(0.5, 0.9)
            limb(P, f"twig{k}_{q}", pts[i], yaw, rnd.uniform(5, 30), L, r0 * 0.35, 0.008, b, segs=5, bev=0)
    for k in range(9):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(0.2, 1.6)
        x, y = math.cos(a) * r, math.sin(a) * r * 0.8
        lump(P, f"leaves{k}", rnd.uniform(0.2, 0.32), (x, y, rnd.uniform(0.0, 0.12)), (1.7, 1.3, 0.22), dry, rnd, jitter=0.3, u=8, v=5)
    for k, (x, y) in enumerate(((-1.9, -0.6), (1.8, 0.9), (0.3, -1.6))):
        tuft(P, f"tuft{k}", (x, y, 0.0), rnd, h=0.4)
    return P


def stump_cluster(seed=48):
    """Three small stumps where a coppice was cut: two sawn at a slant, one snapped off, moss and a fungus on them."""
    rnd = random.Random(seed)
    P = _root("stumps")
    b = bark(0.4)
    for k, (x, y, r, h, snap) in enumerate(((-0.55, -0.2, 0.3, 0.55, False), (0.5, -0.3, 0.22, 0.4, False), (0.0, 0.55, 0.19, 0.7, True))):
        K.cyl(f"trunk{k}", r, h, b, loc=(x, y, h / 2 - 0.04), segs=12, r2=r * 0.88, bev=0.02, parent=P)
        if snap:
            for q in range(6):
                a = 2 * math.pi * q / 6 + rnd.uniform(-0.3, 0.3)
                rr = r * rnd.uniform(0.3, 0.7)
                limb(P, f"splinter{k}{q}", (x + math.cos(a) * rr, y + math.sin(a) * rr, h - 0.06), rnd.uniform(0, 360), rnd.uniform(72, 88),
                     rnd.uniform(0.12, 0.35), rnd.uniform(0.05, 0.08), 0.005, heart(), segs=4, bev=0)
            K.cyl(f"break{k}", r * 0.85, 0.03, heart(), loc=(x, y, h - 0.05), segs=12, bev=0, parent=P)
        else:
            K.cyl(f"top{k}", r * 0.82, 0.03, heart(), loc=(x, y, h - 0.04), rot=(rnd.uniform(-9, 9), rnd.uniform(-9, 9), 0), segs=12, bev=0, parent=P)
        for q in range(4):
            a = 2 * math.pi * q / 4 + rnd.uniform(-0.4, 0.4)
            K.cyl(f"root{k}{q}", r * 0.4, r * 2.0, b, loc=(x + math.cos(a) * r * 1.1, y + math.sin(a) * r * 1.1, 0.03), rot=(0, 79, math.degrees(a)),
                  segs=7, r2=r * 0.1, bev=0.01, parent=P)
    moss(P, "moss_top", (-0.55, -0.18, 0.53), 0.14, flat=0.3)
    bracket(P, "fungus0", (0.71, -0.25, 0.25), 15, 0.09)
    bracket(P, "fungus1", (0.7, -0.33, 0.17), 0, 0.07)
    moss(P, "moss0", (-0.2, 0.75, 0.0), 0.3, flat=0.4)
    tuft(P, "tuft0", (0.6, -0.5, 0.0), rnd, h=0.35)
    tuft(P, "tuft1", (-0.95, -0.4, 0.0), rnd, h=0.3)
    rock(P, "stone", 0.12, 0.1, (0.95, -0.1, 0.03), rnd)
    return P


def driftwood_pile(seed=49):
    """Sea-bleached driftwood thrown up in a heap: smooth grey trunks, a root wad and some twisted branches on a sand patch."""
    rnd = random.Random(seed)
    P = _root("driftwood")
    dw = K.mat("drift", DRIFT, rough=0.9, grime=0.15, cracks=0.45, noise_scale=5.0)
    K.disc("sand", 2.3, mat_of("sand", SAND, noise_scale=8.0, grime=0.1), (0, 0), z=0.01, segs=28, wobble=0.15, seed=seed).parent = P
    logs = (((-2.0, -0.3, 0.25), (1.9, 0.4, 0.22), 0.25), ((-1.4, 0.9, 0.2), (1.5, -0.6, 0.55), 0.2), ((-0.6, -1.3, 0.14), (1.2, 1.2, 0.45), 0.15),
            ((1.6, -1.2, 0.12), (-0.6, -0.2, 0.62), 0.12))
    for k, (p0, p1, r) in enumerate(logs):
        c = tuple((p0[i] + p1[i]) / 2 + (rnd.uniform(-0.3, 0.3) if i < 2 else 0.05) for i in range(3))
        pts = bez(p0, c, p1, 7)
        rad = [r * (0.55 if i in (0, 6) else 0.9 if i in (1, 5) else 1.0) * (1 - 0.25 * i / 6) for i in range(7)]
        tube(P, f"log{k}", pts, rad, dw, sides=9)
    # the root wad at the end of the big log
    for q in range(6):
        a = 2 * math.pi * q / 6 + rnd.uniform(-0.3, 0.3)
        base = (-1.95, -0.3, 0.25)
        end = (base[0] - rnd.uniform(0.3, 0.7), base[1] + math.cos(a) * 0.6, max(0.05, base[2] + math.sin(a) * 0.55))
        mid = ((base[0] + end[0]) / 2 - 0.15, (base[1] + end[1]) / 2, (base[2] + end[2]) / 2 + 0.1)
        tube(P, f"root{q}", bez(base, mid, end, 4), (0.12, 0.03), dw, sides=6)
    for k in range(5):
        a = rnd.uniform(0, 2 * math.pi)
        b0 = (rnd.uniform(-1.4, 1.4), rnd.uniform(-1.0, 1.0), rnd.uniform(0.05, 0.4))
        pts = [b0]
        for i in range(4):
            a += rnd.uniform(-0.8, 0.8)
            p = pts[-1]
            pts.append((p[0] + math.cos(a) * 0.3, p[1] + math.sin(a) * 0.3, max(0.04, p[2] + rnd.uniform(-0.12, 0.08))))
        tube(P, f"branch{k}", pts, (0.06, 0.02), dw, sides=6)
    for k in range(6):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(1.4, 2.1)
        ball(P, f"pebble{k}", rnd.uniform(0.06, 0.12), (math.cos(a) * r, math.sin(a) * r, 0.03), K.mat("pebble", "#9c9a90", rough=0.8, noise_scale=8.0),
             scale=(1.3, 1.0, 0.6))
    return P


def fallen_tree_big(seed=50):
    """A 12 m tree blown over with its root plate torn up on edge, the crown's limbs propped on the grass."""
    rnd = random.Random(seed)
    P = _root("fallentree")
    b = bark(0.5)
    plate = root_tangle(seed=37)
    plate.parent = P
    plate.location = (-4.8, 0, 0)
    plate.rotation_euler = (0, 0, math.radians(-90))
    plate.scale = (1.25, 1.25, 1.25)
    # the trunk runs on from the plate's stub along the ground
    pts = [(-4.0, 0.0, 0.66), (-2.5, 0.0, 0.56), (0.0, 0.1, 0.48), (2.5, 0.25, 0.4), (5.0, 0.35, 0.3), (7.0, 0.4, 0.24)]
    rad = [0.54, 0.5, 0.44, 0.36, 0.28, 0.2]
    tube(P, "trunk", pts, rad, b, sides=14)
    K.cyl("trunk_end", 0.18, 0.02, heart(), loc=(7.01, 0.4, 0.24), rot=(0, 90, 0), segs=14, bev=0, parent=P)
    # crown: limbs reaching up from the top side, two crushed flat on the lower side
    crown = [[], []]
    for k, (x, yaw, pitch, L, r0) in enumerate(((2.0, 115, 22, 3.2, 0.15), (3.3, 70, 32, 2.9, 0.13), (4.2, -80, 8, 2.8, 0.12),
                                                (5.2, 110, 28, 2.6, 0.1), (6.0, -40, 15, 2.2, 0.09), (4.8, 30, 18, 2.6, 0.1))):
        base = (x, 0.25 + 0.1 * x / 6, 0.42 - 0.03 * x)
        cp = math.cos(math.radians(pitch))
        d = (cp * math.cos(math.radians(yaw)), cp * math.sin(math.radians(yaw)), math.sin(math.radians(pitch)))
        end = (base[0] + d[0] * L, base[1] + d[1] * L, max(0.15, base[2] + d[2] * L))
        mid = ((base[0] + end[0]) / 2 + 0.3, (base[1] + end[1]) / 2, (base[2] + end[2]) / 2 + 0.25)
        bp = bez(base, mid, end, 5)
        tube(P, f"limb{k}", bp, (r0, r0 * 0.3), b, sides=7)
        for q in range(2):
            yaw2 = yaw + rnd.choice((-1, 1)) * rnd.uniform(30, 60)
            limb(P, f"twig{k}{q}", bp[2 + q], yaw2, pitch + rnd.uniform(-10, 20), L * 0.4, r0 * 0.4, 0.01, b, segs=5, bev=0)
        if end[2] > 0.6:
            r = rnd.uniform(0.8, 0.95)
            foliage(P, f"mass{k}", r * 0.9, end, rnd, shade=1, scale=(1.5, 1.3, 0.75), u=8, v=6)
            foliage(P, f"massb{k}", 0.65, bp[3], rnd, shade=1, scale=(1.5, 1.3, 0.75), u=7, v=5)
            leaf_cluster(crown[k % 2], end, r, 20, rnd, size=0.22)
            leaf_cluster(crown[(k + 1) % 2], bp[3], 0.45, 12, rnd, size=0.2)
        else:
            K.cyl(f"limb_end{k}", r0 * 0.3, 0.02, heart(), loc=end, rot=(0, 90 - pitch, yaw), segs=7, bev=0, parent=P)
    for k, (x, y) in enumerate(((6.8, -0.6), (5.5, 1.3), (3.0, -0.9), (7.3, 1.0))):
        foliage(P, f"ground_mass{k}", 0.4, (x, y, 0.0), rnd, shade=1, scale=(1.3, 1.1, 0.6), u=7, v=5)
        leaf_cluster(crown[k % 2], (x, y, 0.2), 0.45, 12, rnd, size=0.2)
    leaves(P, "crown", crown[0], leaf(0))
    leaves(P, "crown_d", crown[1], leaf(2))
    for k in range(3):
        moss(P, f"moss{k}", (-2.5 + k * 2.0, 0.05, 0.92 - 0.05 * k), 0.3, flat=0.25, stretch=1.6)
    return P


def tree_root_arch(seed=51):
    """A tree that grew over a long-rotted nurse stump: its trunk stands on an arch of roots high enough to walk under."""
    rnd = random.Random(seed)
    P = _root("rootarch")
    b = bark(0.5)
    zb = 2.6
    K.sphere("crown_root", 0.55, b, loc=(0, 0, zb), scale=(1.1, 1.0, 0.6), parent=P)
    # roots fan out from the base and come down to the ground either side of the passage (which runs along x)
    angs = (35, 70, 110, 150, 210, 250, 290, 325)
    for k, a in enumerate(angs):
        ar = math.radians(a + rnd.uniform(-8, 8))
        R = rnd.uniform(2.0, 2.6)
        p0 = (math.cos(ar) * 0.35, math.sin(ar) * 0.35, zb - 0.1)
        c0 = (math.cos(ar) * 1.3, math.sin(ar) * 1.2, zb + 0.15)
        c1 = (math.cos(ar) * R * 0.95, math.sin(ar) * R * 0.95, 1.4)
        p1 = (math.cos(ar) * R, math.sin(ar) * R, -0.15)
        tube(P, f"root{k}", bez3(p0, c0, c1, p1, 9), (0.3, 0.14), b, sides=8)
        flare = (math.cos(ar) * (R + 0.4), math.sin(ar) * (R + 0.4), -0.05)
        limb(P, f"flare{k}", (p1[0], p1[1], 0.1), math.degrees(ar) + rnd.uniform(-20, 20), -4, 0.7, 0.1, 0.03, b, segs=6, bev=0)
        if k % 2:
            m = bez3(p0, c0, c1, p1, 9)[4]
            limb(P, f"rootlet{k}", m, math.degrees(ar) + rnd.choice((-40, 40)), -60, m[2] / math.sin(math.radians(60)) + 0.1, 0.05, 0.02, b, segs=5, bev=0)
    # the trunk above, and a modest crown
    crowns = []
    tube(P, "trunk", [(0, 0, zb - 0.1), (0.05, 0.0, zb + 1.5), (0.15, 0.05, zb + 3.2), (0.2, 0.1, zb + 4.4)], (0.36, 0.18), b, sides=12)
    for k, (z, yaw, pitch, L) in enumerate(((zb + 2.6, 30, 35, 1.8), (zb + 3.1, 200, 40, 1.6), (zb + 3.6, 300, 50, 1.2))):
        limb(P, f"branch{k}", (0.15, 0.05, z), yaw, pitch, L, 0.1, 0.03, b, segs=7)
        cp = math.cos(math.radians(pitch))
        end = (0.15 + cp * math.cos(math.radians(yaw)) * L, 0.05 + cp * math.sin(math.radians(yaw)) * L, z + math.sin(math.radians(pitch)) * L)
        crowns.append((end, rnd.uniform(0.8, 0.95)))
    # one broad crown of overlapping leaf masses over the branch ends, not a ball on each
    crowns += [((0.25, 0.1, zb + 4.7), 1.0)]
    for k in range(5):
        a = 2 * math.pi * k / 5 + rnd.uniform(-0.3, 0.3)
        crowns.append(((0.2 + math.cos(a) * 1.1, 0.1 + math.sin(a) * 1.0, zb + 4.1 + rnd.uniform(-0.2, 0.3)), rnd.uniform(0.7, 0.85)))
    items = [[], []]
    for k, (c, r) in enumerate(crowns):
        foliage(P, f"leaves{k}", r * 0.85, c, rnd, shade=1, scale=(1.25, 1.15, 0.75), u=8, v=6)
        leaf_cluster(items[k % 2], c, r, 26, rnd, size=0.24)
    leaves(P, "crown", items[0], leaf(0))
    leaves(P, "crown_d", items[1], leaf(2))
    moss(P, "moss_base", (0.0, 0.0, zb + 0.25), 0.3, flat=0.3)
    K.disc("hollow", 1.3, K.mat("earth_d", EARTH_D, rough=1.0, noise_scale=6.0), (0, 0), z=0.012, segs=20, wobble=0.15, seed=seed).parent = P
    for k in range(4):
        rock(P, f"rot{k}", rnd.uniform(0.1, 0.18), 0.08, (rnd.uniform(-0.8, 0.8), rnd.uniform(-0.6, 0.6), 0.04), rnd,
             mat=K.mat("rot", ROT, rough=1.0, noise_scale=12.0, cracks=0.5))
    for k, (x, y) in enumerate(((2.3, 1.3), (-2.1, -1.5), (1.5, -2.4))):
        tuft(P, f"tuft{k}", (x, y, 0.0), rnd, h=0.4)
    return P


# ================================================================== plants
def bramble_patch(seed=52):
    """A 3 m tangle of bramble: thorny canes arching over and rooting at their tips, trefoil leaves along them, a dense low
    mass in the middle and a few blackberries."""
    rnd = random.Random(seed)
    P = _root("brambles")
    cane = K.mat("cane", CANE, rough=0.7, grime=0.2, noise_scale=8.0)
    thorn = K.mat("thorn", "#c9b49a", rough=0.6)
    berry = K.mat("blackberry", "#221d2a", rough=0.35, noise_scale=20.0)
    unripe = K.mat("redberry", "#7a2a2e", rough=0.4)
    items = [[], []]
    nb = 0
    for k in range(16):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(0.0, 0.5)
        p0 = (math.cos(a) * r * 1.3, math.sin(a) * r, -0.02)
        out = rnd.uniform(1.0, 1.7)
        a2 = a + rnd.uniform(-0.5, 0.5)
        p1 = (p0[0] + math.cos(a2) * out * 1.15, p0[1] + math.sin(a2) * out, rnd.uniform(-0.03, 0.15))
        h = rnd.uniform(0.6, 1.05)
        c0 = (p0[0] + math.cos(a2) * out * 0.1, p0[1] + math.sin(a2) * out * 0.1, h * 1.2)
        c1 = (p0[0] + math.cos(a2) * out * 0.6, p0[1] + math.sin(a2) * out * 0.6, h * 1.1)
        pts = bez3(p0, c0, c1, p1, 9)
        tube(P, f"cane{k}", pts, (0.017, 0.007), cane, sides=5)
        for q in range(1, 8):
            p, nxt = Vector(pts[q]), Vector(pts[q + 1])
            T = (nxt - p).normalized()
            side = T.cross(Z).normalized() * (1 if q % 2 else -1)
            hook = (side - T * 0.4 + Z * 0.2).normalized()
            if q % 2:
                orient(K.cyl(f"thorn{k}{q}", 0.007, 0.028, thorn, loc=p + hook * 0.024, segs=3, r2=0.0, bev=0, parent=P), hook)
            if q >= 3:
                yaw = math.degrees(math.atan2(side.y, side.x))
                for f, dy in ((0.0, 0), (1.0, 40), (1.0, -40)):
                    L = rnd.uniform(0.13, 0.18) * (1.2 if dy == 0 else 1.0)
                    items[rnd.randint(0, 1)].append((tuple(p + Z * 0.01), yaw + dy + rnd.uniform(-15, 15), rnd.uniform(-25, 5), L, L * 0.6))
            if q >= 5 and rnd.random() < 0.25:
                for i in range(3):
                    ball(P, f"berry{nb}_{i}", 0.022, p + Vector((rnd.uniform(-0.03, 0.03), rnd.uniform(-0.03, 0.03), -0.04 - i * 0.012)),
                         unripe if nb % 3 == 0 else berry, scale=(1, 1, 1.25))
                nb += 1
    leaves(P, "leaves", items[0], leaf(0))
    leaves(P, "leaves_d", items[1], leaf(1))
    for k in range(9):
        a = rnd.uniform(0, 2 * math.pi)
        c = (math.cos(a) * rnd.uniform(0.1, 1.1), math.sin(a) * rnd.uniform(0.1, 0.8), 0.0)
        r = rnd.uniform(0.35, 0.5)
        foliage(P, f"low{k}", r, c, rnd, shade=1, scale=(1.4, 1.2, 0.85), u=9, v=6)
        leaf_cluster(items[k % 2], (c[0], c[1], r * 0.4), r * 1.1, 8, rnd, size=0.15)
    return P


def berry_bush_big(seed=53):
    """A big shrub 2.4 m across and 1.7 m high, dense dark foliage hung with clusters of blue-black berries."""
    rnd = random.Random(seed)
    P = _root("berrybush")
    b = bark(0.1)
    for k in range(6):
        a = 2 * math.pi * k / 6 + rnd.uniform(-0.3, 0.3)
        limb(P, f"stem{k}", (math.cos(a) * 0.1, math.sin(a) * 0.1, -0.05), math.degrees(a), rnd.uniform(45, 65), 0.7, 0.05, 0.025, b, segs=6, bev=0)
    # a dome of small leaf masses rather than a few big ones, so the outline breaks up like a real shrub
    blobs = []
    for k in range(16):
        el = math.radians(rnd.uniform(0, 75) if k else 90)
        a = 2 * math.pi * k / 15 + rnd.uniform(-0.2, 0.2)
        r = rnd.uniform(0.38, 0.5)
        blobs.append((math.cos(a) * math.cos(el) * 0.85, math.sin(a) * math.cos(el) * 0.75, 0.3 + math.sin(el) * 1.0, r))
    blobs += [(0.1, -0.1, 0.6, 0.55), (-0.15, 0.15, 0.9, 0.55)]
    items = [[], []]
    for k, (x, y, z, r) in enumerate(blobs):
        foliage(P, f"leaves{k}", r * 0.9, (x, y, z), rnd, shade=1, scale=(1.1, 1.0, 0.85), u=8, v=6)
        leaf_cluster(items[k % 2], (x, y, z), r * 1.05, int(30 * r), rnd, size=0.14)
    leaves(P, "leafy", items[0], leaf(0))
    leaves(P, "leafy_d", items[1], leaf(1))
    berry = K.mat("berry", BERRY, rough=0.3, noise_scale=20.0)
    stalk = K.mat("stalk", "#5a3a3a", rough=0.7)
    n = 0
    for x, y, z, r in blobs[:12]:
        for q in range(2):
            a = rnd.uniform(-2.6, -0.5) if q == 0 else rnd.uniform(0, 2 * math.pi)
            el = rnd.uniform(-0.1, 0.5)
            d = Vector((math.cos(a) * math.cos(el) * 1.15, math.sin(a) * math.cos(el) * 1.05, math.sin(el) * 0.85))
            c = Vector((x, y, z)) + d * r * 0.98
            K.cyl(f"stalk{n}", 0.006, 0.08, stalk, loc=c + Vector((0, 0, 0.02)), segs=3, bev=0, parent=P)
            for i in range(3):
                o = Vector((rnd.uniform(-0.04, 0.04), rnd.uniform(-0.04, 0.04), -0.02 - i * 0.025))
                ball(P, f"berry{n}_{i}", rnd.uniform(0.03, 0.04), c + o + d * 0.06, berry)
            n += 1
    return P


def frond(P, name, base, yaw, L, rise, mat, rnd, n=9, w=0.2, t=0.004):
    """A fern frond from base: a stalk arching out and over along yaw, leaflets either side that taper to its tip."""
    bm = bmesh.new()
    p = Vector(base)
    dh = Vector((math.cos(math.radians(yaw)), math.sin(math.radians(yaw)), 0))
    pts = [p.copy()]
    for i in range(n):
        f = i / n
        pitch = math.radians(rise - (rise + 40) * f ** 1.3)
        p = p + (dh * math.cos(pitch) + Z * math.sin(pitch)) * (L / n)
        pts.append(p.copy())
    for i in range(1, n):
        a, b = pts[i], pts[i + 1]
        T = (b - a).normalized()
        S = T.cross(Z).normalized()
        Nn = S.cross(T)
        f = (i + 0.5) / n
        lw = w * max(0.15, math.sin(math.pi * min(1.0, f * 1.05)) ** 0.7)
        for side in (1, -1):
            tip = a.lerp(b, 0.4) + S * side * lw + T * lw * 0.45 - Nn * lw * 0.3
            for o in (1, -1):
                off = Nn * o * t / 2
                _tri(bm, [bm.verts.new(a + off), bm.verts.new(b + off), bm.verts.new(tip + off)], Nn * o)
    K.place(K.from_bm(name, bm, mat, smooth=True), parent=P)
    tube(P, f"{name}_stalk", pts, (0.014, 0.003), mat, sides=4)
    return pts


def fern_clump_big(seed=54):
    """A giant fern 3 m across: a ring of fronds arching out of a rootstock, young ones uncurling in the middle."""
    rnd = random.Random(seed)
    P = _root("fern")
    fm = [K.mat("fern", FERN, rough=0.75, grime=0.1, noise_scale=6.0), K.mat("fern_d", FERN_D, rough=0.75, grime=0.1, noise_scale=6.0)]
    dry = K.mat("fern_dry", FERN_DRY, rough=0.9, noise_scale=6.0)
    K.sphere("stock", 0.3, bark(0.6), loc=(0, 0, 0.0), scale=(1.2, 1.1, 0.6), parent=P)
    nf = 16
    for k in range(nf):
        yaw = 360 * k / nf + rnd.uniform(-8, 8)
        frond(P, f"frond{k}", (math.cos(math.radians(yaw)) * 0.12, math.sin(math.radians(yaw)) * 0.12, 0.12), yaw, rnd.uniform(1.8, 2.2),
              rnd.uniform(58, 74), fm[k % 2], rnd, w=rnd.uniform(0.28, 0.33))
    for k in range(5):
        yaw = rnd.uniform(0, 360)
        frond(P, f"inner{k}", (0, 0, 0.15), yaw, rnd.uniform(1.4, 1.6), rnd.uniform(76, 84), fm[0], rnd, n=8, w=0.22)
    for k in range(3):
        yaw = rnd.uniform(0, 360)
        frond(P, f"dead{k}", (0, 0, 0.05), yaw, 1.6, 8, dry, rnd, n=7, w=0.24)
    for k in range(3):
        a = rnd.uniform(0, 2 * math.pi)
        x, y = math.cos(a) * 0.08, math.sin(a) * 0.08
        limb(P, f"fiddle_stalk{k}", (x, y, 0.1), math.degrees(a), 82, 0.45, 0.015, 0.012, fm[0], segs=5, bev=0)
        K.torus(f"fiddle{k}", 0.05, 0.016, fm[0], loc=(x, y, 0.58), rot=(90, 0, math.degrees(a)), arc=300, segs=12, parent=P)
    K.disc("litter", 0.7, earth(), (0, 0), z=0.01, segs=16, wobble=0.15, seed=seed).parent = P
    return P


def reed_bed(seed=55):
    """A bed of common reed for a pond's edge, 3.5 m long: tall stems with feathery heads, strap leaves, mud at the foot."""
    rnd = random.Random(seed)
    P = _root("reeds")
    stem = K.mat("reed", REED, rough=0.7, noise_scale=6.0)
    old = K.mat("reed_d", REED_D, rough=0.8, noise_scale=6.0)
    plume = K.mat("plume", PLUME, rough=0.95, noise_scale=14.0)
    K.disc("mud", 1.9, K.mat("mud", EARTH, rough=0.75, noise_scale=6.0, grime=0.3), (0, 0), z=0.01, segs=24, wobble=0.12, seed=seed).parent = P
    P.children[-1].scale = (1.0, 0.55, 1.0)
    for k in range(64):
        x, y = rnd.gauss(0, 0.65), rnd.gauss(0, 0.28)
        h = rnd.uniform(1.4, 2.3) * (1 - 0.25 * abs(x) / 1.8)
        lean = (rnd.uniform(-0.12, 0.12), rnd.uniform(-0.15, 0.05))
        top = (x + lean[0] * h, y + lean[1] * h, h)
        m = old if rnd.random() < 0.3 else stem
        tube(P, f"stem{k}", [(x, y, -0.02), (x + lean[0] * h * 0.4, y + lean[1] * h * 0.4, h * 0.55), top], (0.011, 0.004), m, sides=4)
        if k % 3 == 0:
            # the plume nods over to one side
            d = Vector((lean[0] + 0.5, lean[1] + 0.2, 0.6)).normalized()
            for q in range(3):
                pd = (d + Vector((rnd.uniform(-0.25, 0.25), rnd.uniform(-0.25, 0.25), -0.15 * q))).normalized()
                blade(P, f"plume{k}{q}", bez(top, Vector(top) + pd * 0.15 + Vector((0, 0, 0.05)), Vector(top) + pd * 0.32, 4), 0.06, plume)
        if k % 2 == 0:
            yaw = rnd.uniform(0, 2 * math.pi)
            z0 = rnd.uniform(0.3, 1.0)
            L = rnd.uniform(0.5, 0.75)
            b0 = (x + lean[0] * z0, y + lean[1] * z0, z0)
            tip = (b0[0] + math.cos(yaw) * L * 0.8, b0[1] + math.sin(yaw) * L * 0.8, z0 + L * 0.2)
            c = (b0[0] + math.cos(yaw) * L * 0.3, b0[1] + math.sin(yaw) * L * 0.3, z0 + L * 0.4)
            blade(P, f"leaf{k}", bez(b0, c, tip, 4), 0.035, m)
    for k in range(5):
        x = rnd.uniform(-1.6, 1.6)
        tuft(P, f"tuft{k}", (x, rnd.choice((-0.6, 0.6)), 0.0), rnd, h=0.5, mat=stem)
    return P


def pad(P, name, r, loc, yaw, mat, t=0.012):
    """A lily pad: a disc with its notch, t thick, lying flat."""
    bm = bmesh.new()
    n = 14
    notch = 0.22
    outline = [(0.0, 0.0)] + [(r * math.cos(notch + (2 * math.pi - 2 * notch) * i / (n - 1)), r * math.sin(notch + (2 * math.pi - 2 * notch) * i / (n - 1)))
                              for i in range(n)]
    lo = [bm.verts.new((x, y, 0)) for x, y in outline]
    hi = [bm.verts.new((x, y, t)) for x, y in outline]
    bm.faces.new(hi)
    bm.faces.new(list(reversed(lo)))
    for i in range(len(outline)):
        j = (i + 1) % len(outline)
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K.place(K.from_bm(name, bm, mat), loc, (0, 0, yaw), P)


def lily_pad_cluster(seed=56):
    """A still pool 4.4 m across covered in lily pads, two flowers open among them, a muddy rim."""
    rnd = random.Random(seed)
    P = _root("lilies")
    K.disc("rim", 2.35, K.mat("earth_d", EARTH_D, rough=0.7, noise_scale=6.0), (0, 0), z=0.008, segs=32, wobble=0.08, seed=seed).parent = P
    K.disc("water", 2.2, K.mat("pond", WATER, rough=0.05), (0, 0), z=0.03, segs=32, wobble=0.08, seed=seed).parent = P
    pm = [K.mat("lily", LILY, rough=0.45, grime=0.15, noise_scale=6.0), K.mat("lily_d", LILY_D, rough=0.45, grime=0.15, noise_scale=6.0)]
    placed = []
    tries = 0
    while len(placed) < 22 and tries < 400:
        tries += 1
        a = rnd.uniform(0, 2 * math.pi)
        rr = math.sqrt(rnd.random()) * 1.8
        x, y = math.cos(a) * rr + 0.2, math.sin(a) * rr * 0.9
        r = rnd.uniform(0.16, 0.36)
        if math.hypot(x, y) + r > 2.1 or any(math.hypot(x - px, y - py) < (r + pr) * 0.92 for px, py, pr in placed):
            continue
        placed.append((x, y, r))
        pad(P, f"pad{len(placed)}", r, (x, y, 0.032), rnd.uniform(0, 360), pm[len(placed) % 2])
    petal = K.mat("petal", PETAL, rough=0.5, noise_scale=10.0)
    heart_m = K.mat("lily_heart", MUSTARD, rough=0.6)
    for k, (x, y, r) in enumerate(placed[:2]):
        fx, fy = x - r * 0.3, y + r * 0.2
        for ring, (n, ln, tilt) in enumerate(((8, 0.11, 55), (6, 0.08, 30))):
            for q in range(n):
                a = 360 * q / n + ring * 22
                ob = ball(P, f"petal{k}{ring}{q}", ln / 2, (fx + math.cos(math.radians(a)) * ln * 0.45, fy + math.sin(math.radians(a)) * ln * 0.45,
                          0.07 + ring * 0.02), petal, scale=(1.0, 0.45, 0.2), subd=2)
                ob.rotation_mode = "ZYX"
                ob.rotation_euler = (0, math.radians(-tilt), math.radians(a))
        K.cyl(f"stamen{k}", 0.03, 0.03, heart_m, loc=(fx, fy, 0.09), segs=8, bev=0, parent=P)
    for k in range(3):
        a = rnd.uniform(0, 2 * math.pi)
        tuft(P, f"tuft{k}", (math.cos(a) * 2.35, math.sin(a) * 2.35, 0.0), rnd, h=0.45)
    return P


def cattail_clump(seed=57):
    """A stand of cattails 2 m tall: brown velvet heads on stiff stems, long strap leaves arching out round them."""
    rnd = random.Random(seed)
    P = _root("cattails")
    stem = K.mat("typha_stem", REED, rough=0.7, noise_scale=6.0)
    lv = [K.mat("typha_leaf", LEAF_L, rough=0.7, noise_scale=6.0), K.mat("reed_d", REED_D, rough=0.8, noise_scale=6.0)]
    head = K.mat("typha", TYPHA, rough=0.95, noise_scale=16.0)
    K.disc("mud", 1.1, K.mat("mud", EARTH, rough=0.75, noise_scale=6.0, grime=0.3), (0, 0), z=0.01, segs=20, wobble=0.15, seed=seed).parent = P
    for k in range(18):
        x, y = rnd.gauss(0, 0.35), rnd.gauss(0, 0.3)
        h = rnd.uniform(1.5, 2.1)
        lx, ly = rnd.uniform(-0.06, 0.06), rnd.uniform(-0.06, 0.06)
        top = Vector((x + lx * h, y + ly * h, h))
        tube(P, f"stem{k}", [(x, y, -0.02), (x + lx * h * 0.5, y + ly * h * 0.5, h * 0.5), tuple(top)], (0.009, 0.006), stem, sides=4)
        d = Vector((lx, ly, 1)).normalized()
        hl = rnd.uniform(0.2, 0.28)
        c = top - d * (0.12 + hl / 2)
        ob = K.cyl(f"head{k}", 0.032, hl, head, loc=c, segs=8, bev=0.012, parent=P)
        orient(ob, d)
    for k in range(34):
        a = rnd.uniform(0, 2 * math.pi)
        x, y = math.cos(a) * rnd.uniform(0.0, 0.45), math.sin(a) * rnd.uniform(0.0, 0.4)
        L = rnd.uniform(1.0, 1.7)
        lean = rnd.uniform(0.2, 0.5)
        b0 = (x, y, -0.02)
        tip = (x + math.cos(a) * L * lean * 1.4, y + math.sin(a) * L * lean * 1.4, L * (1 - lean * 0.6))
        c = (x + math.cos(a) * L * lean * 0.2, y + math.sin(a) * L * lean * 0.2, L * 0.85)
        blade(P, f"leaf{k}", bez(b0, c, tip, 6), rnd.uniform(0.03, 0.045), lv[1] if k % 5 == 0 else lv[0])
    return P


def giant_mushroom(seed=58):
    """A single parasol mushroom 3 m tall: a scaly domed cap with a dark boss, a ringed stem on a bulb, two young ones."""
    rnd = random.Random(seed)
    P = _root("giantmushroom")
    cap_m = K.mat("cap", CAP, rough=0.75, grime=0.35, noise_scale=4.0, cracks=0.15)
    stem_m = K.mat("stem", STEM, rough=0.85, grime=0.35, noise_scale=9.0)
    gill_m = K.mat("gill", GILL, rough=0.9, noise_scale=20.0)
    scale_m = K.mat("scale", SCALE, rough=0.9, noise_scale=10.0)
    K.sphere("bulb", 0.3, stem_m, loc=(0, 0, 0.08), scale=(1.0, 1.0, 0.7), parent=P)
    top = Vector((0.12, -0.05, 2.65))
    sp = bez3((0, 0, 0.1), (0.2, 0.05, 0.9), (-0.05, -0.08, 1.9), tuple(top), 7)
    tube(P, "stem", sp, [0.24, 0.2, 0.18, 0.165, 0.155, 0.145, 0.14], stem_m, sides=14)
    for k in range(14):
        i = rnd.randint(1, 4)
        p0, p1 = Vector(sp[i]), Vector(sp[i + 1])
        a = rnd.uniform(0, 2 * math.pi)
        c = p0.lerp(p1, rnd.random()) + Vector((math.cos(a), math.sin(a), 0)) * (0.2 - i * 0.012)
        ob = K.box(f"stem_scale{k}", (0.12, 0.012, 0.05), K.mat("stem_scale", STEM_SCALE, rough=0.9, noise_scale=10.0), loc=c, rot=(0, 0, math.degrees(a) + 90), bev=0, parent=P)
        ob.rotation_euler.x = math.radians(rnd.uniform(-15, 15))
    K.cyl("ring", 0.2, 0.16, stem_m, loc=(0.1, -0.04, 2.15), segs=14, r2=0.3, bev=0.01, parent=P)
    R, H = 1.25, 0.55
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=12, radius=1.0)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -0.12], context="VERTS")
    ph = rnd.uniform(0, 6.28)
    for v in bm.verts:
        w = 1 + 0.04 * math.sin(5 * math.atan2(v.co.y, v.co.x) + ph)
        v.co.x *= R * w
        v.co.y *= R * w
        v.co.z = (v.co.z + 0.12) * H
    cz = top.z - 0.08
    K.place(K.from_bm("cap", bm, cap_m, smooth=True), (top.x, top.y, cz), (0, 0, 0), P)
    K.cyl("gills", R * 0.98, 0.04, gill_m, loc=(top.x, top.y, cz + 0.03), segs=20, r2=0.2, bev=0, parent=P)
    K.sphere("boss", 0.32, scale_m, loc=(top.x, top.y, cz + H * 1.12 - 0.05), scale=(1.0, 1.0, 0.45), parent=P)
    for k in range(34):
        rr = R * math.sqrt(rnd.uniform(0.06, 0.8))
        a = rnd.uniform(0, 2 * math.pi)
        x, y = math.cos(a) * rr, math.sin(a) * rr
        f = rr / R
        z = H * (math.sqrt(max(0.0, 1 - f * f)) + 0.12)
        n = Vector((x / (R * R), y / (R * R), (z - 0.12 * H) / (H * H))).normalized()
        ob = K.cyl(f"scale{k}", rnd.uniform(0.05, 0.09) * (1.2 - f * 0.6), 0.02, scale_m, loc=(top.x + x, top.y + y, cz + z - 0.004), segs=5, r2=0.02, bev=0, parent=P)
        orient(ob, n, rnd.uniform(0, 72))
    for k, (x, y, h, cr) in enumerate(((0.8, 0.45, 0.45, 0.14), (-0.6, -0.55, 0.3, 0.09))):
        K.cyl(f"young_stem{k}", cr * 0.45, h, stem_m, loc=(x, y, h / 2 - 0.03), segs=8, r2=cr * 0.35, bev=0.01, parent=P)
        K.sphere(f"young_cap{k}", cr, cap_m, loc=(x, y, h - 0.02), scale=(1.0, 1.0, 0.9), parent=P)
        K.sphere(f"young_boss{k}", cr * 0.5, scale_m, loc=(x, y, h + cr * 0.75), scale=(1.0, 1.0, 0.5), parent=P)
    tuft(P, "tuft0", (-0.5, 0.4, 0.0), rnd, h=0.35)
    tuft(P, "tuft1", (0.5, -0.5, 0.0), rnd, h=0.3)
    return P


def cactus(P, name, loc, h, r, arms, mat, rnd):
    """A ribbed column cactus h tall with arms (height up the trunk, yaw, reach, rise)."""
    x, y = loc
    pts = [(x, y, -0.05), (x, y, h * 0.5), (x, y, h - r * 0.6), (x, y, h - r * 0.2), (x, y, h)]
    tube(P, name, pts, [r, r * 0.98, r * 0.95, r * 0.7, r * 0.2], mat, sides=20, ribs=10, rib=0.07)
    for k, (z, yaw, reach, rise) in enumerate(arms):
        ar = math.radians(yaw)
        ra = r * 0.62
        d = (math.cos(ar), math.sin(ar))
        p = [(x + d[0] * r * 0.4, y + d[1] * r * 0.4, z), (x + d[0] * (r + reach * 0.6), y + d[1] * (r + reach * 0.6), z + 0.02),
             (x + d[0] * (r + reach), y + d[1] * (r + reach), z + 0.25), (x + d[0] * (r + reach), y + d[1] * (r + reach), z + rise - ra * 0.5),
             (x + d[0] * (r + reach), y + d[1] * (r + reach), z + rise)]
        tube(P, f"{name}_arm{k}", p, [ra, ra, ra, ra * 0.75, ra * 0.2], mat, sides=16, ribs=8, rib=0.07)


def cactus_cluster(seed=59):
    """Three ribbed column cacti, 2.6, 1.7 and 0.9 m, the tallest with two arms, on a patch of dry ground."""
    rnd = random.Random(seed)
    P = _root("cacti")
    cm = K.mat("cactus", CACTUS, rough=0.6, grime=0.25, noise_scale=10.0, cracks=0.1)
    K.disc("dirt", 1.6, mat_of("dirt", TERMITE, noise_scale=6.0, grime=0.2), (0, 0), z=0.01, segs=24, wobble=0.15, seed=seed).parent = P
    cactus(P, "tall", (-0.2, 0.25), 2.6, 0.2, ((1.1, 10, 0.32, 0.85), (1.5, 190, 0.28, 0.6)), cm, rnd)
    cactus(P, "mid", (0.65, -0.35), 1.7, 0.16, ((0.8, -60, 0.25, 0.5),), cm, rnd)
    cactus(P, "short", (-0.6, -0.55), 0.9, 0.17, (), cm, rnd)
    bloom = K.mat("bloom", PETAL, rough=0.6)
    for k, (x, y, z) in enumerate(((-0.6, -0.55, 0.92), (-0.55, -0.48, 0.88), (-0.68, -0.6, 0.87))):
        K.cyl(f"flower{k}", 0.04, 0.05, bloom, loc=(x, y, z), segs=7, r2=0.055, bev=0, parent=P)
    for k in range(5):
        a = rnd.uniform(0, 2 * math.pi)
        rock(P, f"stone{k}", rnd.uniform(0.08, 0.18), rnd.uniform(0.06, 0.12), (math.cos(a) * rnd.uniform(0.8, 1.5), math.sin(a) * rnd.uniform(0.8, 1.4), 0.03), rnd)
    dryg = mat_of("drygrass", DRYBUSH, noise_scale=6.0)
    for k, (x, y) in enumerate(((1.1, 0.5), (-1.2, 0.3), (0.3, 0.9))):
        tuft(P, f"tuft{k}", (x, y, 0.0), rnd, h=0.35, mat=dryg)
    return P


def dry_bush_tumbleweed(seed=60):
    """A dry desert shrub of grey twigs with a tumbleweed blown into it and caught."""
    rnd = random.Random(seed)
    P = _root("drybush")
    tw = K.mat("twig", TWIG, rough=0.85, grime=0.2, noise_scale=8.0)
    tm = K.mat("tumble", TUMBLE, rough=0.9, noise_scale=10.0)
    dl = K.mat("drybush", DRYBUSH, rough=0.9, noise_scale=8.0)
    dry_items = []
    for k in range(7):
        yaw = 360 * k / 7 + rnd.uniform(-20, 20)
        pitch = rnd.uniform(40, 70)
        L = rnd.uniform(0.8, 1.2)
        base = (rnd.uniform(-0.05, 0.05), rnd.uniform(-0.05, 0.05), -0.03)
        limb(P, f"stem{k}", base, yaw, pitch, L, 0.035, 0.012, tw, segs=5, bev=0)
        cp = math.cos(math.radians(pitch))
        for q in range(3):
            f = rnd.uniform(0.35, 0.95)
            p = (base[0] + cp * math.cos(math.radians(yaw)) * L * f, base[1] + cp * math.sin(math.radians(yaw)) * L * f, base[2] + math.sin(math.radians(pitch)) * L * f)
            L2 = rnd.uniform(0.25, 0.5)
            y2 = yaw + rnd.choice((-1, 1)) * rnd.uniform(25, 60)
            p2 = rnd.uniform(20, 70)
            limb(P, f"twig{k}{q}", p, y2, p2, L2, 0.012, 0.004, tw, segs=4, bev=0)
            c2 = math.cos(math.radians(p2))
            tip = (p[0] + c2 * math.cos(math.radians(y2)) * L2, p[1] + c2 * math.sin(math.radians(y2)) * L2, p[2] + math.sin(math.radians(p2)) * L2)
            for i in range(2):
                limb(P, f"twig{k}{q}_{i}", tip, y2 + rnd.uniform(-50, 50), p2 + rnd.uniform(-20, 20), rnd.uniform(0.12, 0.25), 0.006, 0.003, tw, segs=3, bev=0)
            leaf_cluster(dry_items, tip, 0.12, 5, rnd, size=0.05)
    leaves(P, "dry_leaves", dry_items, dl)
    # the tumbleweed: a ball of interlaced twigs pushed against the bush by the wind
    c = Vector((0.75, -0.45, 0.48))
    for k in range(24):
        ax = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))).normalized()
        u = ax.orthogonal().normalized()
        v = ax.cross(u)
        rr = rnd.uniform(0.3, 0.46)
        a0 = rnd.uniform(0, 2 * math.pi)
        span = rnd.uniform(2.0, 3.5)
        pts = []
        for i in range(7):
            a = a0 + span * i / 6
            w = rr * (1 + rnd.uniform(-0.08, 0.08))
            pts.append(tuple(c + (u * math.cos(a) + v * math.sin(a)) * w))
        tube(P, f"tumble{k}", pts, (0.01, 0.005), tm, sides=3)
    for k in range(3):
        rock(P, f"stone{k}", rnd.uniform(0.08, 0.14), 0.08, (rnd.uniform(-0.9, 0.9), rnd.uniform(0.5, 0.9), 0.03), rnd)
    return P


# ================================================================== bones, shells and mounds
def shell_mound(seed=61):
    """An old shell midden 3 m across: a low heap of crushed shell and sand, whole shells of a few kinds all over it."""
    rnd = random.Random(seed)
    P = _root("shellmound")
    crush = K.mat("crushed_shell", SHELL_D, rough=0.95, noise_scale=14.0, grime=0.2)
    heaps = ((0.0, 0.0, 1.5, 1.2, 0.7), (0.9, 0.5, 0.8, 0.7, 0.4), (-0.9, -0.35, 0.8, 0.7, 0.35))
    for k, (x, y, a, b, h) in enumerate(heaps):
        K.sphere(f"heap{k}", 1.0, crush, loc=(x, y, -0.05), scale=(a, b, h), parent=P)
    mats = [K.mat("shell", SHELL, rough=0.6, noise_scale=10.0, grime=0.15), K.mat("shell_p", SHELL_P, rough=0.6, noise_scale=10.0),
            K.mat("shell_g", SHELL_G, rough=0.7, noise_scale=10.0)]

    def surf(x, y):
        best, nrm = -1.0, Z
        for hx, hy, a, b, h in heaps:
            q = 1 - ((x - hx) / a) ** 2 - ((y - hy) / b) ** 2
            if q > 0:
                z = h * math.sqrt(q) - 0.05
                if z > best:
                    best, nrm = z, Vector(((x - hx) / (a * a), (y - hy) / (b * b), (z + 0.05) / (h * h))).normalized()
        return best, nrm

    n = 0
    while n < 110:
        x, y = rnd.uniform(-1.8, 1.8), rnd.uniform(-1.3, 1.3)
        z, nrm = surf(x, y)
        if z < 0:
            continue
        kind = rnd.random()
        m = mats[rnd.randrange(3)]
        s = rnd.uniform(0.7, 1.3)
        if kind < 0.45:      # a clam half, cupped
            ob = ball(P, f"clam{n}", 0.07 * s, (x, y, z), m)
        elif kind < 0.75:    # a limpet cone
            ob = K.cyl(f"limpet{n}", 0.06 * s, 0.05 * s, m, loc=(x, y, z + 0.01), segs=7, r2=0.008, bev=0, parent=P)
        else:                # a whelk, lying on its side
            ob = K.cyl(f"whelk{n}", 0.045 * s, 0.16 * s, m, loc=(x, y, z + 0.03), segs=6, r2=0.005, bev=0, parent=P)
        orient(ob, nrm.lerp(Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)).normalized(), 0.85 if kind >= 0.75 else 0.2), rnd.uniform(0, 360))
        if kind < 0.45:
            ob.scale = (1.0, 0.85, 0.35)
        n += 1
    for k in range(10):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(1.6, 2.2)
        K.cyl(f"loose{k}", 0.05, 0.04, mats[k % 3], loc=(math.cos(a) * r, math.sin(a) * r * 0.8, 0.01), rot=(rnd.uniform(-20, 20), 0, 0), segs=7, r2=0.01, bev=0, parent=P)
    tuft(P, "tuft0", (1.5, -0.9, 0.0), rnd, h=0.4, mat=mat_of("drygrass", DRYBUSH, noise_scale=6.0))
    tuft(P, "tuft1", (-1.4, 0.9, 0.0), rnd, h=0.35, mat=mat_of("drygrass", DRYBUSH, noise_scale=6.0))
    return P


def bird_nest_big(seed=62):
    """A nest of sticks 1.7 m across on top of a lone rock, three big pale eggs in its lining."""
    rnd = random.Random(seed)
    P = _root("nest")
    boulder(P, "rock", (2.6, 2.3, 1.7), (0, 0, -0.1), rnd, power=2.2)
    boulder(P, "rock_b", (1.2, 1.1, 0.8), (1.35, 0.7, -0.1), rnd, mat=dstone())
    zt = surface_z(0.0, 0.0) - 0.05
    sm = [K.mat("nest", NEST, rough=0.9, grime=0.2, noise_scale=8.0), bark(0.0), K.mat("snag", SNAG, rough=0.9, grime=0.2, noise_scale=6.0)]
    n = 0
    for layer in range(5):
        R = 0.55 + layer * 0.07
        z = zt + 0.04 + layer * 0.075
        cnt = 16 + layer * 2
        for i in range(cnt):
            a = 2 * math.pi * (i + rnd.random() * 0.5) / cnt
            L = rnd.uniform(0.4, 0.65)
            yaw = math.degrees(a) + 90 + rnd.uniform(-14, 14)
            c = (math.cos(a) * R, math.sin(a) * R, z)
            p0 = (c[0] - math.cos(math.radians(yaw)) * L / 2, c[1] - math.sin(math.radians(yaw)) * L / 2, c[2] + rnd.uniform(-0.05, 0.05))
            limb(P, f"stick{n}", p0, yaw, rnd.uniform(-8, 8), L, rnd.uniform(0.012, 0.022), 0.008, sm[n % 3], segs=4, bev=0)
            n += 1
    for k in range(6):
        a = rnd.uniform(0, 2 * math.pi)
        R = rnd.uniform(0.6, 0.75)
        limb(P, f"stray{k}", (math.cos(a) * R, math.sin(a) * R, zt + 0.12), math.degrees(a) + rnd.uniform(-40, 40), rnd.uniform(-35, -15),
             rnd.uniform(0.3, 0.45), 0.014, 0.006, sm[k % 3], segs=4, bev=0)
    K.cyl("lining", 0.5, 0.12, K.mat("lining", DRYLEAF, rough=1.0, noise_scale=16.0), loc=(0, 0, zt + 0.08), segs=16, r2=0.62, bev=0.03, parent=P)
    egg = K.mat("egg", BONE, rough=0.5, grime=0.2, noise_scale=30.0, cracks=0.05)
    for k, (x, y, rz) in enumerate(((-0.12, 0.05, 30), (0.12, 0.1, -40), (0.0, -0.13, 80))):
        ob = ball(P, f"egg{k}", 0.1, (x, y, zt + 0.22), egg, scale=(1.0, 1.0, 1.35), subd=2)
        ob.rotation_euler = (math.radians(75), 0, math.radians(rz))
    K.box("feather", (0.03, 0.22, 0.004), K.mat("feather", "#e8e4dc", rough=0.7), loc=(0.9, -0.85, 0.01), rot=(0, 0, 30), bev=0, parent=P)
    for k, (x, y) in enumerate(((-1.4, -0.8), (1.5, -0.6))):
        tuft(P, f"tuft{k}", (x, y, 0.0), rnd, h=0.4)
    return P


def bone():
    return K.mat("oldbone", BONE, rough=0.8, grime=0.5, cracks=0.4, moss=0.3, noise_scale=4.0)


def animal_skull_big(seed=63):
    """The skull of some huge horned grazer, 3.2 m long, settled into the ground on its jaw line, the lower jaw lying beside it."""
    rnd = random.Random(seed)
    P = _root("skull")
    bm_ = bone()
    dark = K.mat("hole", HOLE, rough=1.0)
    tooth = K.mat("tooth", "#cfc4a8", rough=0.6, grime=0.4)
    G = empty(P, "skull", (0, 0.3, -0.3), (4, -3, rnd.uniform(-8, 8)))
    # a long low head: braincase at the back, a muzzle tapering forward (-Y) to the nose
    lump(G, "cranium", 0.8, (0, 0.75, 0.8), (1.0, 1.05, 0.62), bm_, rnd, jitter=0.02, u=14, v=9)
    G2 = empty(G, "muzzle", (0, 0.35, 0.72), (90 + 5, 0, 0))
    from props_stone import tapered
    tapered("muzzle", (1.2, 0.85), (0.6, 0.4), 2.3, bm_, bev=0.16, parent=G2, shift=(0, 0.06))
    K.sphere("nose_tip", 0.3, bm_, loc=(0, -1.95, 0.56), scale=(1.0, 0.75, 0.68), parent=G)
    K.box("nasal_hole", (0.34, 0.12, 0.18), dark, loc=(0, -2.14, 0.62), rot=(-20, 0, 0), bev=0.05, parent=G)
    K.box("nasal_ridge", (0.1, 1.9, 0.08), bm_, loc=(0, -0.9, 1.0), rot=(-7, 0, 0), bev=0.03, parent=G)
    K.box("poll", (1.1, 0.35, 0.3), bm_, loc=(0, 1.25, 1.12), bev=0.1, parent=G)
    for sx in (-1, 1):
        # the orbit: a ring of bone round a dark socket on the side of the head, looking out and a little forward
        o = empty(G, f"orbit{sx}", (sx * 0.6, 0.1, 0.95), (0, 0, 0))
        orient(o, (sx * 1.0, -0.45, 0.35))
        K.torus(f"orbit_rim{sx}", 0.19, 0.055, bm_, loc=(0, 0, -0.02), segs=16, parent=o)
        K.cyl(f"socket{sx}", 0.17, 0.2, dark, loc=(0, 0, -0.12), segs=14, bev=0, parent=o)
        tube(G, f"cheek{sx}", bez((sx * 0.5, -0.35, 0.6), (sx * 0.86, 0.15, 0.6), (sx * 0.74, 0.75, 0.62), 5), (0.11, 0.09), bm_, sides=8, flat=0.6)
        # horn cores: thick at the root, out sideways, then a short turn up and forward
        hp = bez3((sx * 0.4, 1.15, 1.2), (sx * 1.2, 1.2, 1.2), (sx * 1.75, 0.95, 1.35), (sx * 1.95, 0.6, 1.85), 8)
        tube(G, f"horn{sx}", hp, [0.22, 0.2, 0.17, 0.14, 0.11, 0.08, 0.055, 0.03], K.mat("horn", "#c6bca4", rough=0.7, grime=0.5, cracks=0.3,
             noise_scale=5.0), sides=10, flat=0.85)
        for i in range(6):
            K.box(f"tooth{sx}{i}", (0.12, 0.18, 0.16), tooth, loc=(sx * (0.3 + i * 0.03), -1.35 + i * 0.22, 0.2), bev=0.025, parent=G)
    K.box("crack", (0.03, 0.6, 0.02), dark, loc=(0.22, 0.6, 1.3), rot=(10, 0, 25), bev=0, parent=G)
    # the lower jaw, fallen away and lying on its flat side, half sunk
    J = empty(P, "jaw", (1.9, -0.7, 0.02), (0, 84, 28))
    tapered("jaw", (0.22, 2.4), (0.15, 2.2), 0.42, bm_, loc=(0, 0, -0.1), bev=0.06, parent=J)
    K.box("jaw_angle", (0.18, 0.5, 0.75), bm_, loc=(0, 1.1, 0.2), rot=(-20, 0, 0), bev=0.06, parent=J)
    for i in range(5):
        K.box(f"jaw_tooth{i}", (0.1, 0.17, 0.13), tooth, loc=(0, -0.6 + i * 0.2, 0.36), bev=0.02, parent=J)
    K.sphere("heap", 1.0, earth(), loc=(0, 0.1, -0.02), scale=(1.4, 2.3, 0.16), parent=P)
    for k, (x, y) in enumerate(((-1.4, -1.2), (1.2, 1.5), (-1.3, 1.3), (0.9, -2.0))):
        tuft(P, f"tuft{k}", (x, y, 0.0), rnd, h=0.4)
    return P


def rib_cage_bones(seed=64):
    """The ribs of a giant creature that died on its belly and sank: two rows of ribs arching 4.5 m out of the ground and
    curving in towards each other over a 6 m length, the last vertebrae of its tail breaking the turf at one end."""
    rnd = random.Random(seed)
    P = _root("ribcage")
    bm_ = bone()
    inner = K.mat("bone_in", "#b9ad94", rough=0.95, cracks=0.6)
    broken = {(1, -1): 0.55, (4, 1): 0.7, (5, -1): 0.45}
    for k in range(7):
        x = -2.7 + k * 0.9 + rnd.uniform(-0.08, 0.08)
        f = math.sqrt(max(0.15, 1 - (x / 3.6) ** 2))
        for sy in (-1, 1):
            H = 4.6 * f * rnd.uniform(0.92, 1.04)
            base = 1.6 + 0.8 * f
            sweep = 0.25 + 0.06 * x
            p0 = (x, sy * base, -0.3)
            c0 = (x + sweep * 0.3, sy * (base + 0.35), H * 0.7)
            c1 = (x + sweep * 0.8, sy * (base * 0.55), H * 1.15)
            p1 = (x + sweep, sy * rnd.uniform(0.25, 0.5), H * rnd.uniform(0.82, 0.9))
            pts = bez3(p0, c0, c1, p1, 11)
            rad = [0.2 - 0.15 * (i / 10) ** 1.2 for i in range(11)]
            cut = broken.get((k, sy))
            if cut:
                n = int(11 * cut)
                pts, rad = pts[:n], rad[:n]
                orient(K.cyl(f"rib_break{k}{sy}", rad[-1] * 1.02, 0.02, inner, loc=pts[-1], segs=8, bev=0, parent=P), Vector(pts[-1]) - Vector(pts[-2]))
                limb(P, f"rib_piece{k}{sy}", (x + 0.3, sy * (base - 0.6), 0.06), rnd.uniform(0, 360), 2, rnd.uniform(1.2, 1.8), rad[-1], 0.05, bm_, segs=8, bev=0.01)
            tube(P, f"rib{k}{sy}", pts, rad, bm_, sides=7, flat=0.6)
            lump(P, f"heave{k}{sy}", 0.4, (x, sy * base, -0.02), (1.1, 1.0, 0.3), earth(), rnd, jitter=0.1, u=7, v=4)
    # the tail vertebrae running out of the ground past the last ribs
    for i in range(4):
        x = 3.2 + i * 0.45
        r = 0.26 - i * 0.03
        K.cyl(f"vert{i}", r, 0.38, bm_, loc=(x, 0.0, r * 0.4), rot=(0, 90, 0), segs=10, bev=0.04, parent=P)
        K.box(f"spinous{i}", (0.12, 0.14, r * 2.2), bm_, loc=(x + 0.05, 0.0, r * 1.1), rot=(0, 20, 0), bev=0.03, parent=P)
    K.sphere("tail_mound", 1.0, earth(), loc=(3.7, 0.0, -0.05), scale=(1.3, 0.55, 0.2), parent=P)
    for k in range(4):
        rock(P, f"bonebit{k}", rnd.uniform(0.08, 0.14), 0.06, (rnd.uniform(-2.5, 2.5), rnd.uniform(-1.0, 1.0), 0.03), rnd, mat=bm_)
    for k, (x, y) in enumerate(((-3.6, -0.5), (2.8, 2.3), (0.2, -2.9), (-1.5, 2.6), (1.5, -1.0))):
        tuft(P, f"tuft{k}", (x, y, 0.0), rnd, h=0.45)
    return P


def termite_spires_low(seed=65):
    """A low spread of small termite spires, knee to chest high, fused at their feet across 3 m of baked ground."""
    rnd = random.Random(seed)
    P = _root("termitespires")
    mud = K.mat("termite", TERMITE, rough=1.0, grime=0.25, noise_scale=3.0, cracks=0.15)
    K.sphere("skirt", 1.0, mud, loc=(0, 0, 0), scale=(1.8, 1.3, 0.14), parent=P)
    spires = ((0.0, 0.0, 1.25, 0.32), (0.7, 0.35, 0.8, 0.25), (-0.75, 0.25, 0.95, 0.27), (0.35, -0.55, 0.55, 0.2), (-0.4, -0.5, 0.7, 0.22),
              (1.3, -0.2, 0.45, 0.18), (-1.35, -0.1, 0.4, 0.17), (0.1, 0.75, 0.5, 0.18), (-0.9, 0.85, 0.3, 0.14))
    for k, (x, y, h, r) in enumerate(spires):
        lumpy_cone(f"spire{k}", r, h, mud, rnd, loc=(x, y, 0.0), rot=(y * 12 + rnd.uniform(-5, 5), -x * 10 + rnd.uniform(-5, 5), 0), parent=P,
                   rings=4, segs=9, tip=0.04)
    for k in range(4):
        rock(P, f"clod{k}", rnd.uniform(0.07, 0.12), 0.07, (rnd.uniform(-1.8, 1.8), rnd.uniform(-1.5, -1.1), 0.03), rnd, mat=mud)
    dryg = mat_of("drygrass", DRYBUSH, noise_scale=6.0)
    for k, (x, y) in enumerate(((1.7, 0.8), (-1.8, -0.9))):
        tuft(P, f"tuft{k}", (x, y, 0.0), rnd, h=0.35, mat=dryg)
    return P


PROPS = {
    "boulder_split": boulder_split,
    "rock_arch_small": rock_arch_small,
    "rock_spire": rock_spire,
    "rock_shelf": rock_shelf,
    "hollow_log": hollow_log,
    "tree_fallen_branches": tree_fallen_branches,
    "stump_cluster": stump_cluster,
    "bramble_patch": bramble_patch,
    "berry_bush_big": berry_bush_big,
    "fern_clump_big": fern_clump_big,
    "reed_bed": reed_bed,
    "lily_pad_cluster": lily_pad_cluster,
    "cattail_clump": cattail_clump,
    "driftwood_pile": driftwood_pile,
    "seaweed_rocks": seaweed_rocks,
    "shell_mound": shell_mound,
    "bird_nest_big": bird_nest_big,
    "animal_skull_big": animal_skull_big,
    "rib_cage_bones": rib_cage_bones,
    "giant_mushroom": giant_mushroom,
    "fallen_tree_big": fallen_tree_big,
    "tree_root_arch": tree_root_arch,
    "cactus_cluster": cactus_cluster,
    "dry_bush_tumbleweed": dry_bush_tumbleweed,
    "termite_spires_low": termite_spires_low,
}
