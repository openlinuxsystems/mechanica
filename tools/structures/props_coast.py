"""Props of the coasts and lake shores (docs/world-generation.md, Scenes), in the families of docs/art-direction.md.

Settler Patchwork, the shore's fishing life (weathered wood, rope, nets and the faded colony colours): a dune with
marram grass, a pier section, wrecked boats, lobster traps and crab pots, a net drying rack, a boathouse, a beach hut, a
deck chair, a raft of pallets, oars, net floats and a mooring rope. Colony Works, the old harbour hardware (bone, steel
blue, mustard): a navigation buoy, a bollard, a jetty light, a small harbour crane and the lighthouse the settlers
keep patched. Nature: a rock pool and a small sea stack.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import kit as K
import frame as FR
from families import M, glow, stone, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA, BRICK, RUST, ZINC, CONCRETE, WARM
from props import _root, wood, plank, post, bolt
from props_settler_a import rod, beam, prism, lump, tin, rope_mat
from props_settler_b import corrugated
from props_stone import rock, limb, lumpy_cone
from props_colony_b import label, lathe, _grp

SAND, SAND_D = "#d4c194", "#b9a476"
KELP = "#56602f"
DRIFT = "#a0978a"       # sun- and salt-bleached plank
NET = "#4d5d52"         # green-grey nylon netting
WATER = "#3d6f78"
GLASS = "#2a3a40"
CORK = "#a8865a"
SHORE = "#7d7f78"       # shore rock, darker and less mossy than the Old Stone


# ---------------------------------------------------------------- materials
def sand():
    return K.mat("sand", SAND, rough=1.0, grime=0.1, noise_scale=9.0)


def kelp():
    return K.mat("kelp", KELP, rough=0.55, grime=0.2, noise_scale=6.0)


def drift(shade=0, wear=0.3):
    return K.mat(f"drift{shade}", [DRIFT, "#8f8678", "#b0a898"][shade % 3], rough=0.9, wear=wear, grime=0.35, noise_scale=2.0,
                 scratches=0.3, cracks=0.2)


def netm():
    return K.mat("net", NET, rough=0.8, grime=0.3, noise_scale=10.0)


def shore_rock():
    return K.mat("shore_rock", SHORE, rough=0.85, grime=0.3, moss=0.25, cracks=0.6, noise_scale=3.0)


def glass():
    return K.mat("glass", GLASS, rough=0.15, metal=0.2, grime=0.3)


def _steel(wear=0.3):
    return M(STEEL, metal=0.4, wear=wear, grime=0.4)


def _dark():
    return M(GRAPHITE, metal=0.5, wear=0.3, grime=0.4)


# ---------------------------------------------------------------- shape helpers
def _settle(G, sink=0.0):
    """Drops G (a direct child of the root) until its lowest vertex is sink below the ground."""
    bpy.context.view_layer.update()
    lo = min((ob.matrix_world @ v.co).z for ob in [G, *G.children_recursive] if ob.type == "MESH" for v in ob.data.vertices)
    G.location.z -= lo + sink
    bpy.context.view_layer.update()
    return G


def _sweep(bm, pts, r, sides=6, cap=True, ang0=0.0):
    """A tube of radius r along the polyline pts into bm, its frame carried along so it does not twist."""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    rings, nrm = [], None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        if nrm is None:
            ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            nrm = t.cross(ref).normalized()
        else:
            nrm = (nrm - t * nrm.dot(t)).normalized()
        bi = t.cross(nrm)
        rings.append([bm.verts.new(p + (nrm * math.cos(a) + bi * math.sin(a)) * r) for a in (ang0 + 2 * math.pi * j / sides for j in range(sides))])
    for i in range(n - 1):
        for j in range(sides):
            k = (j + 1) % sides
            bm.faces.new((rings[i][j], rings[i][k], rings[i + 1][k], rings[i + 1][j]))
    if cap:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])


def _spline(pts, sub):
    """Catmull-Rom through pts, sub points per span, so a rope lies in curves rather than kinks."""
    pts = [Vector(p) for p in pts]
    out = []
    for i in range(len(pts) - 1):
        p0, p1, p2 = pts[max(i - 1, 0)], pts[i], pts[i + 1]
        p3 = pts[min(i + 2, len(pts) - 1)]
        for k in range(sub):
            t = k / sub
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(pts[-1])
    return out


def tube(P, name, pts, r, mat, sides=6, smooth=True, sub=0):
    """A rope, cable, strand or bent bar along pts (sub: points per span of a smooth curve through them)."""
    if sub:
        pts = _spline(pts, sub)
    bm = bmesh.new()
    _sweep(bm, pts, r, sides)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K.place(K.from_bm(name, bm, mat, smooth=smooth), parent=P)


def strands(P, name, lines, r, mat, sides=3):
    """Many thin strands (netting, grass, bunched weed) in one mesh."""
    bm = bmesh.new()
    for pts in lines:
        _sweep(bm, pts, r, sides, cap=False)
    return K.place(K.from_bm(name, bm, mat, smooth=True), parent=P)


def surface(P, name, rows, mat, thick=0.006, smooth=True):
    """A sheet through a grid of points (canvas, a sail), given a little thickness."""
    bm = bmesh.new()
    V = [[bm.verts.new(p) for p in row] for row in rows]
    for i in range(len(rows) - 1):
        for j in range(len(rows[0]) - 1):
            bm.faces.new((V[i][j], V[i][j + 1], V[i + 1][j + 1], V[i + 1][j]))
    ob = K.from_bm(name, bm, mat, smooth=smooth)
    if thick:
        ob.modifiers.new("thick", "SOLIDIFY").thickness = thick
    return K.place(ob, parent=P)


def _netting(f, nu, nv, du=None):
    """Strand polylines of a net over the surface f(u, v) in [0, 1]^2: nu strands along v, nv along u."""
    lines = []
    for i in range(nu + 1):
        u = i / nu
        lines.append([f(u, j / nv) for j in range(nv + 1)])
    for j in range(1, nv + 1):
        v = j / nv
        lines.append([f(i / nu, v) for i in range(nu + 1)])
    return lines


def chain(P, name, pts, mat, link=0.09, r=0.016):
    """Chain links along a polyline, each turned a quarter to the last."""
    path = [Vector(p) for p in pts]
    seg = [(path[i], path[i + 1]) for i in range(len(path) - 1)]
    k = 0
    for a, b in seg:
        d = b - a
        n = max(1, int(d.length / (link * 1.3)))
        for i in range(n):
            c = a + d * ((i + 0.5) / n)
            ob = K.torus(f"{name}{k}", link * 0.5, r, mat, loc=c, segs=10, parent=P)
            ob.scale = (1.0, 0.62, 1.0)
            q = d.to_track_quat("X", "Z")
            ob.rotation_euler = (q @ Matrix.Rotation(math.radians(90 * (k % 2)), 4, "X").to_quaternion()).to_euler()
            k += 1


def kelp_clump(P, name, loc, rnd, n=6, spread=0.35):
    """Wrack the tide left: a flat mat of weed with a few wavy strands lying along the tide line."""
    x0, y0 = loc
    yaw = rnd.uniform(-0.4, 0.4)
    ca, sa = math.cos(yaw), math.sin(yaw)
    L = spread * 2.6
    mat = lump(P, f"{name}_mat", 1.0, (x0, y0, 0.0), (L * 0.42, spread * 0.5, 0.035), kelp(), rnd, jitter=0.4, u=9, v=4, flat=0.0)
    mat.rotation_euler[2] = yaw
    lines = []
    for k in range(max(2, n // 2)):
        off = rnd.uniform(-0.5, 0.5) * spread
        ph = rnd.uniform(0, 6.28)
        pts = []
        for i in range(5):
            t = i / 4 - 0.5
            u, v = t * L * rnd.uniform(0.85, 1.1), off + 0.06 * math.sin(t * 9 + ph)
            pts.append((x0 + u * ca - v * sa, y0 + u * sa + v * ca, 0.035 - 0.015 * abs(t) * 2))
        lines.append(pts)
    strands(P, name, lines, 0.014, kelp(), sides=3)


def mound(P, name, rx, ry, h, mat, rnd, loc=(0, 0), segs=16, rings=6, lump_=0.08):
    """A low heap of sand or earth that runs out into the ground at a shallow angle (not a cushion)."""
    bm = bmesh.new()
    ph = [rnd.uniform(0, 6.28) for _ in range(4)]
    top = bm.verts.new((0, 0, h))
    prev = None
    for k in range(1, rings + 1):
        f = k / rings
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            w = 1 + lump_ * (math.sin(2 * a + ph[0]) + 0.6 * math.sin(3 * a + ph[1]) + 0.4 * math.sin(5 * a + ph[2]))
            z = h * (1 - f * f) ** 1.6 * (1 + 0.1 * math.sin(3 * a + 4 * f + ph[3])) - (0.02 if k == rings else 0)
            ring.append(bm.verts.new((math.cos(a) * rx * f * w, math.sin(a) * ry * f * w, z)))
        if prev is None:
            for i in range(segs):
                bm.faces.new((top, ring[i], ring[(i + 1) % segs]))
        else:
            for i in range(segs):
                j = (i + 1) % segs
                bm.faces.new((prev[i], ring[i], ring[j], prev[j]))
        prev = ring
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    return K.place(ob, (loc[0], loc[1], 0), parent=P)


def shell_scatter(P, name, c, r, n, rnd):
    """Pebbles and pale shells round a prop's foot."""
    for k in range(n):
        a = rnd.uniform(0, 6.28)
        d = rnd.uniform(0.3, 1.0) * r
        if rnd.random() < 0.4:
            lump(P, f"{name}{k}", rnd.uniform(0.03, 0.05), (c[0] + math.cos(a) * d, c[1] + math.sin(a) * d, 0.0), (1.3, 1.0, 0.4),
                 M(BONE, rough=0.7, wear=0.0, scratches=0.0), rnd, jitter=0.1, u=6, v=4)
        else:
            s = rnd.uniform(0.04, 0.09)
            lump(P, f"{name}{k}", s, (c[0] + math.cos(a) * d, c[1] + math.sin(a) * d, s * 0.2), (1.3, 1.0, 0.6), shore_rock(), rnd, jitter=0.15, u=6, v=4)


# ---------------------------------------------------------------- hulls
class Hull:
    """A boat hull from stern (t=0) to bow (t=1); u runs from the port gunwale (-1) through the keel (0) to starboard (1)."""

    def __init__(self, L, B, D, sheer=0.2, transom=0.5, bow_rise=0.6, stern_rise=0.2, full=0.7):
        self.L, self.B, self.D = L, B, D
        self.sheer, self.transom, self.bow_rise, self.stern_rise, self.full = sheer, transom, bow_rise, stern_rise, full

    def half_beam(self, t):
        if t >= 0.45:
            q = (t - 0.45) / 0.55
            return self.B / 2 * max(1 - q * q, 0.0) ** 0.6 + 0.015
        q = (0.45 - t) / 0.45
        return self.B / 2 * (1 - (1 - self.transom) * q * q)

    def top(self, t):
        return self.sheer * (2 * t - 1) ** 2 * (1.0 if t > 0.5 else 0.6)

    def keel(self, t):
        return -self.D * (1 - self.bow_rise * max(0.0, (t - 0.7) / 0.3) ** 2 - self.stern_rise * max(0.0, (0.2 - t) / 0.2) ** 2)

    def pt(self, t, u, scale=1.0):
        b = self.half_beam(t) * scale
        th = u * math.pi / 2
        zt, zk = self.top(t), self.keel(t)
        zk = zt + (zk - zt) * scale
        return Vector(((t - 0.5) * self.L, b * math.sin(th), zt - (zt - zk) * abs(math.cos(th)) ** self.full))

    def half_width_at(self, t, z):
        """The inside half-width of the hull at height z (for deck planks)."""
        zt, zk = self.top(t), self.keel(t)
        if z <= zk:
            return 0.0
        c = min(max((zt - z) / (zt - zk), 0.0), 1.0) ** (1 / self.full)
        return self.half_beam(t) * math.sin(math.acos(c))


def hull_band(P, name, H, u0, u1, mat, n=18, m=3, thick=0.03, gaps=(), off=0.0, t0=0.0, t1=1.0, inner=None):
    """One strake of a hull between u0 and u1, missing the station spans in gaps (holes), off proud of the last."""
    bm = bmesh.new()
    rows = []
    for i in range(n + 1):
        t = t0 + (t1 - t0) * i / n
        row = []
        for j in range(m + 1):
            p = H.pt(t, u0 + (u1 - u0) * j / m)
            if off:
                d = Vector((0, p.y, p.z - H.top(t)))
                if d.length > 1e-6:
                    p = p + d.normalized() * off
            row.append(bm.verts.new(p))
        rows.append(row)
    for i in range(n):
        if any(a <= i < b for a, b in gaps):
            continue
        for j in range(m):
            bm.faces.new((rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j]))
    for v in [v for v in bm.verts if not v.link_faces]:
        bm.verts.remove(v)
    ob = K.from_bm(name, bm, mat, smooth=True)
    # the inside of the planking is bare wood, whatever the paint outside
    ob.data.materials.append(inner or wood(1, wear=0.3))
    sol = ob.modifiers.new("thick", "SOLIDIFY")
    sol.thickness = thick
    sol.material_offset = 1
    return K.place(ob, parent=P)


def transom(P, name, H, mat, thick=0.04, n=10):
    """The flat stern board, its outline the hull's section at t=0."""
    bm = bmesh.new()
    vs = [bm.verts.new(H.pt(0.0, -1 + 2 * j / n)) for j in range(n + 1)]
    bm.faces.new(vs)
    ob = K.from_bm(name, bm, mat)
    ob.modifiers.new("thick", "SOLIDIFY").thickness = thick
    return K.place(ob, parent=P)


def _oar(P, name, length, loc, rot, tip=None, shade=2):
    """An oar along its local +x from the grip at its origin: round loom, a flat blade with its tip painted."""
    G = _grp(P, name, loc, rot)
    shaft = length * 0.68
    K.cyl(f"{name}_loom", 0.024, shaft, wood(shade, wear=0.3), loc=(shaft / 2, 0, 0), rot=(0, 90, 0), segs=8, bev=0, parent=G)
    K.cyl(f"{name}_grip", 0.02, 0.14, wood(3, wear=0.2), loc=(-0.05, 0, 0), rot=(0, 90, 0), segs=8, bev=0.005, parent=G)
    bl = length - shaft
    pts = [(shaft - 0.05, -0.03), (shaft + bl * 0.3, -0.07), (length - 0.02, -0.075), (length, -0.05), (length, 0.05),
           (length - 0.02, 0.075), (shaft + bl * 0.3, 0.07), (shaft - 0.05, 0.03)]
    prism(G, f"{name}_blade", pts, 0.02, wood(shade, wear=0.3), plane="xz", bev=0.004).rotation_euler = (math.radians(90), 0, 0)
    if tip:
        tp = [(length - 0.22, -0.074), (length - 0.02, -0.076), (length, -0.05), (length, 0.05), (length - 0.02, 0.076), (length - 0.22, 0.074)]
        prism(G, f"{name}_tip", tp, 0.024, M(tip, wear=0.5, grime=0.3), plane="xz", bev=0.003).rotation_euler = (math.radians(90), 0, 0)
    K.cyl(f"{name}_leather", 0.03, 0.2, M(GRAPHITE, rough=0.8, wear=0.2), loc=(shaft * 0.45, 0, 0), rot=(0, 90, 0), segs=8, bev=0, parent=G)
    return G


# ================================================================== nature
def sand_dune_grass(seed=41):
    """A low mound of blown sand held by marram grass: stiff tufts leaning with the wind, a bleached stick half buried."""
    rnd = random.Random(seed)
    P = _root("dune")
    sx, sy, sz = 2.0, 1.4, 0.6
    mound(P, "mound", sx, sy, sz, sand(), rnd, segs=18, rings=6, lump_=0.06)
    mound(P, "mound2", 1.1, 0.9, 0.35, sand(), rnd, loc=(1.0, 0.6), segs=12, rings=4)

    def h(x, y):
        q = (x / sx) ** 2 + (y / sy) ** 2
        return sz * max(1 - q, 0.0) ** 1.6 * 0.9

    green, dry = [], []
    tufts = 22
    for k in range(tufts):
        a = 2 * math.pi * k / tufts * 2.3 + rnd.uniform(-0.4, 0.4)
        d = math.sqrt(rnd.uniform(0.0, 0.8))
        tx, ty = math.cos(a) * d * sx * 0.85, math.sin(a) * d * sy * 0.85
        tz = h(tx, ty) - 0.02
        for b in range(rnd.randint(7, 10)):
            yaw = rnd.uniform(0, 6.28)
            lean = rnd.uniform(5, 30)
            L = rnd.uniform(0.45, 0.9) * (1.0 - 0.3 * d)
            bend = rnd.uniform(0.05, 0.2)
            base = Vector((tx + rnd.uniform(-0.06, 0.06), ty + rnd.uniform(-0.06, 0.06), tz))
            dirv = Vector((math.sin(math.radians(lean)) * math.cos(yaw) + 0.15, math.sin(math.radians(lean)) * math.sin(yaw), math.cos(math.radians(lean))))
            pts = [base + dirv * (L * i / 2) + Vector((bend * (i / 2) ** 2, 0, 0)) for i in range(3)]
            (dry if rnd.random() < 0.3 else green).append(pts)
    # blades taper: thin strands read as grass at prop size
    strands(P, "marram", green, 0.009, K.mat("marram", "#8f9a58", rough=0.8, grime=0.15, noise_scale=12.0), sides=3)
    strands(P, "marram_dry", dry, 0.009, K.mat("marram_dry", "#c2b47a", rough=0.85, noise_scale=12.0), sides=3)
    limb(P, "stick", (-1.6, -0.5, 0.06), 15, 8, 1.3, 0.045, 0.025, drift(0), segs=7)
    limb(P, "stick_b", (-0.95, -0.35, 0.18), 120, 20, 0.35, 0.025, 0.012, drift(0), segs=6)
    shell_scatter(P, "shell", (0.3, -0.6), 1.6, 8, rnd)
    return P


def rock_pool(seed=42):
    """A ring of weed-hung shore rocks round a shallow pool: still water, a starfish and mussels at the waterline."""
    rnd = random.Random(seed)
    P = _root("rockpool")
    n = 11
    for k in range(n):
        a = 2 * math.pi * k / n + rnd.uniform(-0.12, 0.12)
        r = rnd.uniform(0.32, 0.52)
        t = rnd.uniform(0.3, 0.55)
        x, y = math.cos(a) * 1.25, math.sin(a) * 1.0
        rock(P, f"rock{k}", r, t, (x, y, t / 2 - 0.06), rnd, mat=shore_rock(), rot=(rnd.uniform(-10, 10), rnd.uniform(-10, 10), math.degrees(a) + rnd.uniform(-30, 30)))
    for k in range(3):
        a = rnd.uniform(0, 6.28)
        rock(P, f"outer{k}", rnd.uniform(0.18, 0.28), 0.2, (math.cos(a) * 1.85, math.sin(a) * 1.5, 0.05), rnd, mat=shore_rock())
    pool = K.disc("water", 1.0, K.mat("pool_water", WATER, rough=0.04, metal=0.0), (0, 0), z=0.14, segs=28, wobble=0.1, seed=seed)
    pool.parent = P
    pool.scale = (1.2, 0.95, 1)
    bed = K.disc("pool_bed", 1.05, K.mat("pool_bed", SAND_D, rough=1.0, grime=0.3), (0, 0), z=0.01, segs=20, wobble=0.1, seed=seed + 1)
    bed.parent = P
    bed.scale = (1.2, 0.95, 1)
    # weed hanging over the rims into the water
    lines = []
    for k in range(9):
        a = rnd.uniform(0, 6.28)
        x, y = math.cos(a) * 1.15, math.sin(a) * 0.92
        lines.append([(x * f, y * f, z) for f, z in ((1.12, 0.36), (1.0, 0.32), (0.9, 0.2), (0.82, 0.145))])
    strands(P, "weed", lines, 0.02, kelp(), sides=4)
    star = [(math.cos(math.radians(90 + 36 * i)) * (0.11 if i % 2 == 0 else 0.045), math.sin(math.radians(90 + 36 * i)) * (0.11 if i % 2 == 0 else 0.045)) for i in range(10)]
    prism(P, "starfish", star, 0.025, M(BRICK, wear=0.2, rough=0.8, scratches=0), loc=(0.35, -0.3, 0.155), rot=(0, 0, 20), plane="xy", bev=0.008)
    mus = K.mat("mussel", "#2a3140", rough=0.35, grime=0.2)
    for k in range(7):
        a = math.radians(200 + k * 9)
        K.sphere(f"mussel{k}", 0.035, mus, loc=(math.cos(a) * 1.0, math.sin(a) * 0.8, 0.16), scale=(1.6, 0.8, 0.7), parent=P)
    shell_scatter(P, "pebble", (0, 0), 2.1, 10, rnd)
    return P


def sea_stack_small(seed=43):
    """A 6 m stack of layered rock the sea has cut loose: ledges where harder beds stick out, turf on top, boulders round its foot."""
    rnd = random.Random(seed)
    P = _root("seastack")
    rk = K.mat("stack_rock", SHORE, rough=0.85, grime=0.35, moss=0.15, cracks=0.25, noise_scale=1.6)
    lumpy_cone("column", 1.6, 5.7, rk, rnd, parent=P, rings=9, segs=12, lump=0.1, tip=0.9)
    # harder beds: slabs that stand proud of the column
    for k, (z, r, t) in enumerate(((0.15, 2.15, 0.6), (1.3, 1.85, 0.5), (2.6, 1.6, 0.55), (3.9, 1.38, 0.45), (5.3, 1.15, 0.55))):
        rock(P, f"bed{k}", r, t, (rnd.uniform(-0.12, 0.12), rnd.uniform(-0.12, 0.12), z + t / 2), rnd, mat=rk,
             rot=(rnd.uniform(-4, 4), rnd.uniform(-4, 4), rnd.uniform(0, 180)))
    K.sphere("turf", 0.95, K.mat("turf", "#6d8a3e", rough=1.0, noise_scale=8.0, grime=0.1), loc=(0.05, 0.0, 5.85), scale=(1.05, 0.9, 0.22), parent=P)
    lines = []
    for k in range(40):
        a = rnd.uniform(0, 6.28)
        d = rnd.uniform(0.2, 0.9)
        b = Vector((math.cos(a) * d, math.sin(a) * d * 0.85, 5.9 + 0.12 * (1 - d)))
        lines.append([b, b + Vector((math.cos(a) * 0.08, math.sin(a) * 0.08, rnd.uniform(0.2, 0.35)))])
    strands(P, "tussock", lines, 0.012, K.mat("marram", "#8f9a58", rough=0.8, grime=0.15, noise_scale=12.0), sides=3)
    for k in range(6):
        a = rnd.uniform(0, 6.28)
        r = rnd.uniform(0.3, 0.6)
        rock(P, f"boulder{k}", r, r * 0.9, (math.cos(a) * 2.4, math.sin(a) * 2.2, r * 0.35), rnd, mat=rk)
    return P


# ================================================================== Settler Patchwork: the shore
def dock_pier_section(seed=44):
    """A 6 m pier section: plank deck on bearers and stringers, round piles with weed at their feet, a missing plank,
    a cleat with a rope over the side and a ladder down."""
    rnd = random.Random(seed)
    P = _root("pier")
    L, W, top = 6.0, 2.4, 1.3
    pile = drift(1)
    weed = kelp()
    for y in (-2.6, 0.0, 2.6):
        for s in (-1, 1):
            tall = (y == -2.6)
            h = (top + 0.7) if tall else (top - 0.23)
            K.cyl(f"pile{y}{s}", 0.13, h + 0.1, pile, loc=(s * 1.12, y, h / 2 - 0.05), segs=12, bev=0.02, parent=P)
            K.cyl(f"weed{y}{s}", 0.145, 0.32, weed, loc=(s * 1.12, y, 0.12), segs=12, bev=0, parent=P)
            if tall:
                K.cyl(f"cap{y}{s}", 0.14, 0.03, _dark(), loc=(s * 1.12, y, h - 0.03), segs=12, bev=0.005, parent=P)
        plank(P, f"bearer{y}", (2.6, 0.16, 0.22), (0, y, top - 0.05 - 0.18 - 0.11), shade=3)
        for s in (-1, 1):
            K.cyl("nut", 0.03, 0.04, M(ZINC, metal=0.6, wear=0.4), loc=(s * 1.12, y - 0.09, top - 0.34), rot=(90, 0, 0), segs=6, bev=0, parent=P)
    for x in (-0.95, 0.0, 0.95):
        plank(P, f"stringer{x}", (0.1, L, 0.18), (x, 0, top - 0.05 - 0.09), shade=2)
    for s in (-1, 1):
        beam(P, f"brace_a{s}", (s * 1.2, -2.5, 0.25), (s * 1.2, -0.1, top - 0.4), 0.14, 0.05, drift(2))
        beam(P, f"brace_b{s}", (s * 1.2, 0.1, top - 0.4), (s * 1.2, 2.5, 0.25), 0.14, 0.05, drift(2))
    n = 27
    for k in range(n):
        y = -L / 2 + 0.11 + k * (L - 0.22) / (n - 1)
        if k in (8, 19):
            continue
        if k == 9:
            # a split plank: one half dropped onto the stringer
            plank(P, "broken", (1.1, 0.2, 0.05), (-0.6, y, top - 0.06), rot=(0, 9, 3), shade=1)
            plank(P, "broken_b", (1.1, 0.2, 0.05), (0.62, y, top - 0.025), rot=(0, 0, -2), shade=1)
            continue
        Lp = W + rnd.uniform(-0.06, 0.06)
        mat = drift(k % 3) if rnd.random() < 0.6 else wood(k % 4, wear=0.4)
        K.box(f"plank{k}", (Lp, 0.2, 0.05), mat, loc=(rnd.uniform(-0.03, 0.03), y, top - 0.025), rot=(0, 0, rnd.uniform(-0.6, 0.6)), bev=0.01, parent=P)
    # cleat and a rope over the edge
    K.box("cleat_base", (0.1, 0.3, 0.04), _dark(), loc=(1.05, -1.4, top + 0.02), bev=0.008, parent=P)
    K.box("cleat_horn", (0.07, 0.42, 0.05), _dark(), loc=(1.05, -1.4, top + 0.09), bev=0.015, parent=P)
    for dy in (-0.08, 0.08):
        K.box("cleat_leg", (0.06, 0.05, 0.06), _dark(), loc=(1.05, -1.4 + dy, top + 0.05), bev=0.006, parent=P)
    tube(P, "rope", [(1.05, -1.55, top + 0.09), (1.05, -1.4, top + 0.13), (1.05, -1.25, top + 0.09), (1.18, -1.2, top + 0.02), (1.28, -1.15, top - 0.2),
                     (1.32, -1.0, 0.4), (1.35, -0.8, 0.04), (1.7, -0.5, 0.03), (1.9, -0.9, 0.03)], 0.028, rope_mat(), sides=6, sub=3)
    # ladder down the far side
    for dy in (-0.22, 0.22):
        K.box(f"ladder_rail{dy}", (0.06, 0.06, top + 0.5), M(ZINC, metal=0.5, wear=0.3), loc=(-1.27, 1.6 + dy, (top + 0.5) / 2 - 0.05), bev=0.008, parent=P)
    for k in range(4):
        K.cyl(f"rung{k}", 0.018, 0.44, M(ZINC, metal=0.5, wear=0.3), loc=(-1.27, 1.6, 0.25 + k * 0.3), rot=(90, 0, 0), segs=8, bev=0, parent=P)
    for k in range(3):
        kelp_clump(P, f"wrack{k}", (rnd.uniform(-1.6, 1.6), rnd.uniform(-2.8, 2.8)), rnd, n=4, spread=0.25)
    return P


def rowboat_wreck(seed=45):
    """A clinker rowboat left upside down on the sand: faded paint, a stove-in strake, keel up, one gunwale on a log."""
    rnd = random.Random(seed)
    P = _root("rowboat")
    H = Hull(3.6, 1.35, 0.55, sheer=0.2, transom=0.55, bow_rise=0.55, stern_rise=0.15, full=0.6)
    G = _grp(P, "boat", (0, 0, 0), (186, 3, 12))
    cuts = [-1, -0.72, -0.42, -0.14, 0.14, 0.42, 0.72, 1]
    paint = [M(BONE, wear=0.3, grime=0.35, rough=0.85), M(SEA, wear=0.3, grime=0.35, rough=0.85), M(BRICK, wear=0.35, grime=0.4, rough=0.85),
             M(BRICK, wear=0.3, grime=0.45, rough=0.85)]
    for k in range(7):
        col = paint[min(k, 6 - k)]
        hull_band(G, f"strake{k}", H, cuts[k] - 0.02, cuts[k + 1] + 0.02, col, n=16, m=2, thick=0.025, off=0.006 * (k % 2),
                  gaps={4: [(8, 10)], 5: [(7, 10)], 6: [(8, 9)]}.get(k, ()))
    transom(G, "transom", H, wood(1, wear=0.4))
    tube(G, "keel", [H.pt(i / 12 * 0.97, 0, 1.0) + Vector((0, 0, -0.03)) for i in range(13)], 0.04, wood(3), sides=4, smooth=False)
    for s in (-1, 1):
        tube(G, f"gunwale{s}", [H.pt(i / 12, s, 1.0) + Vector((0, s * 0.02, 0)) for i in range(13)], 0.03, wood(2), sides=4, smooth=False)
    for t in (0.35, 0.62):
        b = H.half_beam(t)
        K.box(f"thwart{t}", (0.22, 2 * b * 0.92, 0.03), wood(1), loc=((t - 0.5) * H.L, 0, H.top(t) - 0.2), bev=0.005, parent=G)
    # ribs seen through the hole
    tube(G, "rib", [H.pt(0.53, 0.15 + 0.85 * i / 5, 0.95) for i in range(6)], 0.022, wood(3), sides=4, smooth=False)
    plank(G, "sprung", (0.5, 0.02, 0.1), tuple(H.pt(0.44, 0.48, 1.0) + Vector((0, 0.05, 0.02))), rot=(-50, 14, 8), shade=1)
    _settle(G, sink=0.03)
    limb(P, "log", (-1.5, 0.7, 0.1), 5, 2, 3.2, 0.11, 0.08, drift(0), segs=8)
    _oar(P, "oar", 2.3, (-0.9, -1.15, 0.03), (90, 0, 8), tip=SEA)
    for k in range(3):
        kelp_clump(P, f"wrack{k}", (rnd.uniform(-1.8, 1.8), rnd.uniform(-1.2, 1.2)), rnd, n=4, spread=0.25)
    return P


def fishing_boat_wreck(seed=46):
    """An 8 m wooden fishing boat beached on its side: painted strakes, a plank wheelhouse, a broken mast, a net drum
    on the stern and a hole stove into the bilge that shows the frames."""
    rnd = random.Random(seed)
    P = _root("fishingboat")
    H = Hull(8.0, 2.8, 1.55, sheer=0.45, transom=0.62, bow_rise=0.5, stern_rise=0.2, full=0.55)
    G = _grp(P, "boat", (0, 0, 0), (42, -2, 0))
    cuts = [-1, -0.78, -0.55, -0.3, 0, 0.3, 0.55, 0.78, 1]
    for k in range(8):
        lvl = min(k, 7 - k)
        col = [M(SEA, wear=0.3, grime=0.35, rough=0.85), M(BONE, wear=0.3, grime=0.4, rough=0.85), M(BONE, wear=0.35, grime=0.4, rough=0.85),
               M(BRICK, wear=0.3, grime=0.5, rough=0.85)][lvl]
        hull_band(G, f"strake{k}", H, cuts[k] - 0.015, cuts[k + 1] + 0.015, col, n=15, m=2, thick=0.05, off=0.012 * (k % 2),
                  gaps={4: [(8, 9)], 5: [(7, 10)], 6: [(8, 9)]}.get(k, ()))
    transom(G, "transom", H, M(SEA, wear=0.5, grime=0.35), thick=0.06)
    tube(G, "keel", [H.pt(i / 16 * 0.99, 0, 1.0) + Vector((0, 0, -0.05)) for i in range(17)], 0.08, wood(3), sides=4, smooth=False)
    tube(G, "stem", [H.pt(0.995, u, 1.0) for u in (0.0, 0.3, 0.6, 1.0)] + [H.pt(0.995, 1.0) + Vector((0.05, 0, 0.12))], 0.07, wood(3), sides=4, smooth=False)
    for s in (-1, 1):
        tube(G, f"rail{s}", [H.pt(i / 16, s, 1.0) + Vector((0, s * 0.03, 0.03)) for i in range(17)], 0.06, M(GRAPHITE, wear=0.4, grime=0.3), sides=4, smooth=False)
        tube(G, f"rub{s}", [H.pt(i / 16, s * 0.8, 1.0) + Vector((0, s * 0.03, 0)) for i in range(2, 16)], 0.04, wood(3), sides=4, smooth=False)
    # frames showing through the hole
    for i in range(7, 11):
        t = i / 16
        tube(G, f"frame{i}", [H.pt(t, 0.05 + 0.9 * j / 6, 0.97) for j in range(7)], 0.05, wood(3), sides=4, smooth=False)
    for k, (x, y, z, r) in enumerate(((-0.1, 1.0, -0.7, (10, 0, 25)), (0.5, 1.1, -0.55, (-15, 5, -20)))):
        plank(G, f"split{k}", (0.9, 0.04, 0.16), (x, y, z), rot=r, shade=2)
    # deck: fore-and-aft planks cut to the hull
    zd = H.top(0.5) - 0.18
    for k in range(-5, 6):
        y = k * 0.24
        xs = [i / 80 for i in range(81) if H.half_width_at(i / 80, zd) - 0.08 > abs(y) + 0.12]
        if len(xs) < 2:
            continue
        x0, x1 = (xs[0] - 0.5) * H.L, (xs[-1] - 0.5) * H.L
        plank(G, f"deck{k}", (x1 - x0, 0.22, 0.05), ((x0 + x1) / 2, y, zd), shade=k)
    # wheelhouse
    wx, wz = -1.5, zd + 0.025
    WH = _grp(G, "wheelhouse", (wx, 0, wz))
    hw, hd, hh = 0.85, 0.75, 1.75
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("wh_post", (0.1, 0.1, hh), wood(3), loc=(sx * hw, sy * hd, hh / 2), bev=0.012, parent=WH)
    for k in range(3):
        z = 0.3 + k * 0.32
        for sy in (-1, 1):
            K.box(f"wh_side{k}{sy}", (2 * hw, 0.04, 0.3), M(BONE, wear=0.55, grime=0.35), loc=(0, sy * (hd + 0.02), z), bev=0, parent=WH)
        K.box(f"wh_back{k}", (0.04, 2 * hd, 0.3), M(BONE, wear=0.55, grime=0.35), loc=(-hw - 0.02, 0, z), bev=0.008, parent=WH)
        K.box(f"wh_front{k}", (0.04, 2 * hd, 0.3), M(BONE, wear=0.55, grime=0.35), loc=(hw + 0.02, 0, z), bev=0.008, parent=WH)
    for sy in (-1, 1):
        K.box(f"wh_glass{sy}", (1.2, 0.03, 0.42), glass(), loc=(0.1, sy * (hd + 0.02), 1.32), bev=0.004, parent=WH)
        K.box(f"wh_sill{sy}", (1.4, 0.07, 0.06), wood(3), loc=(0.1, sy * (hd + 0.03), 1.08), bev=0.008, parent=WH)
    K.box("wh_glass_f", (0.03, 1.2, 0.42), glass(), loc=(hw + 0.02, 0, 1.32), bev=0.004, parent=WH)
    K.box("wh_sill_f", (0.07, 1.6, 0.06), wood(3), loc=(hw + 0.03, 0, 1.08), bev=0.008, parent=WH)
    K.box("wh_roof", (2 * hw + 0.35, 2 * hd + 0.3, 0.07), wood(1), loc=(0, 0, hh + 0.03), rot=(0, -3, 0), bev=0.012, parent=WH)
    corrugated(WH, "wh_patch", 0.9, 1.1, tin(ZINC, wear=0.25), (0.25, 0.2, hh + 0.09), rot=(0, -3, 90), pitch=0.15)
    K.cyl("wh_lamp", 0.07, 0.14, M(GRAPHITE, metal=0.5), loc=(-0.4, 0, hh + 0.14), segs=10, bev=0.01, parent=WH)
    # mast, snapped above the deck, the boom fallen across the hatch
    K.cyl("mast", 0.12, 2.0, wood(2), loc=(1.4, 0, zd + 1.0), segs=10, bev=0.02, parent=G)
    K.cyl("mast_snap", 0.12, 0.25, wood(2), loc=(1.42, 0.02, zd + 2.1), rot=(14, 8, 0), segs=10, r2=0.03, bev=0.01, parent=G)
    K.cyl("mast_band", 0.13, 0.08, _dark(), loc=(1.4, 0, zd + 1.5), segs=10, bev=0.005, parent=G)
    K.cyl("boom", 0.08, 2.6, wood(2), loc=(2.3, 0.15, zd + 0.15), rot=(0, 86, 8), segs=10, bev=0.015, parent=G)
    # the net drum on the stern
    for s in (-1, 1):
        K.box(f"drum_cheek{s}", (0.08, 0.05, 0.75), _steel(), loc=(-3.0, s * 0.55, zd + 0.38), bev=0.01, parent=G)
    K.cyl("drum", 0.3, 1.05, netm(), loc=(-3.0, 0, zd + 0.55), rot=(90, 0, 0), segs=14, bev=0.06, parent=G)
    for s in (-1, 1):
        K.cyl(f"drum_flange{s}", 0.42, 0.03, _steel(), loc=(-3.0, s * 0.53, zd + 0.55), rot=(90, 0, 0), segs=14, bev=0, parent=G)
    label(G, "SP-21", (0.25 * H.L, H.half_beam(0.75) + 0.06, H.top(0.75) - 0.32), 0.3, M(GRAPHITE, wear=0.4), rot=(90 - 6, 0, 180), extrude=0.008)
    _settle(G, sink=0.3)
    # sand banked against the buried side, a net spilt onto the beach
    mound(P, "bank", 4.4, 1.5, 0.45, sand(), rnd, loc=(0.2, 0.5), segs=14, rings=4)
    lump(P, "net_heap", 0.5, (-2.6, -1.9, 0), (1.5, 1.0, 0.35), netm(), rnd, jitter=0.25, u=10, v=6, flat=0.0)
    for k in range(2):
        kelp_clump(P, f"wrack{k}", (rnd.uniform(-3.5, 3.5), rnd.uniform(-2.0, -1.0)), rnd, n=5, spread=0.3)
    return P


def buoy_beached(seed=47):
    """A colony navigation buoy washed up on its side: mustard float, steel-blue band, a cage to a dead lamp and an X
    topmark, its ballast tail and chain in the sand."""
    rnd = random.Random(seed)
    P = _root("buoy")
    B = _grp(P, "buoy", (0, 0, 0), (0, 74, 18))
    paint = M(MUSTARD, wear=0.6, grime=0.45, scratches=0.5, moss=0.2)
    prof = [(0.04, -0.62), (0.45, -0.55), (0.78, -0.3), (0.88, 0.0), (0.88, 0.32), (0.78, 0.52), (0.5, 0.6), (0.04, 0.62)]
    lathe("float", prof, paint, segs=24, parent=B)
    K.cyl("band", 0.9, 0.26, _steel(0.4), loc=(0, 0, 0.14), segs=24, bev=0.01, parent=B)
    K.torus("fender", 0.9, 0.06, K.mat("rubber", "#26282a", rough=0.9, grime=0.3), loc=(0, 0, -0.04), segs=28, parent=B)
    K.cyl("tail", 0.14, 1.1, _dark(), loc=(0, 0, -1.1), segs=12, bev=0.01, parent=B)
    K.cyl("ballast", 0.3, 0.18, M(RUST, metal=0.4, wear=0.5), loc=(0, 0, -1.6), segs=16, bev=0.02, parent=B)
    K.torus("eye", 0.1, 0.025, _dark(), loc=(0, 0, -1.78), rot=(90, 0, 0), segs=12, parent=B)
    steel = _steel(0.25)
    legs = []
    for k in range(4):
        a = math.radians(45 + 90 * k)
        p0 = (math.cos(a) * 0.5, math.sin(a) * 0.5, 0.55)
        p1 = (math.cos(a) * 0.2, math.sin(a) * 0.2, 2.0)
        rod(B, f"leg{k}", p0, p1, 0.035, steel, segs=8)
        legs.append((p0, p1))
    K.torus("hoop", 0.36, 0.025, steel, loc=(0, 0, 1.25), segs=20, parent=B)
    K.cyl("lamp_deck", 0.3, 0.05, steel, loc=(0, 0, 2.02), segs=16, bev=0.005, parent=B)
    K.cyl("lamp_base", 0.11, 0.12, _dark(), loc=(0, 0, 2.1), segs=12, bev=0.01, parent=B)
    K.cyl("lamp_lens", 0.1, 0.18, K.mat("deadlens", "#7d8f96", rough=0.2, metal=0.2), loc=(0, 0, 2.25), segs=12, bev=0.01, parent=B)
    K.cyl("lamp_cap", 0.12, 0.05, _dark(), loc=(0, 0, 2.36), segs=12, bev=0.01, parent=B)
    K.cyl("mark_pole", 0.025, 0.4, _dark(), loc=(0, 0, 2.55), segs=8, bev=0, parent=B)
    for a in (45, -45):
        K.box(f"topmark{a}", (0.5, 0.04, 0.1), M(MUSTARD, wear=0.4), loc=(0, 0, 2.75), rot=(0, a, 0), bev=0.008, parent=B)
    # a stencilled plate on the band
    K.box("plate", (0.44, 0.02, 0.2), M(BONE, wear=0.4, grime=0.3), loc=(0, -0.91, 0.14), bev=0.004, parent=B)
    label(B, "NV-7", (0, -0.925, 0.14), 0.15, M(GRAPHITE, wear=0.2), extrude=0.004)
    for k in range(4):
        a = math.radians(30 + 90 * k)
        FR.screw(B, (math.cos(a) * 0.905, math.sin(a) * 0.905, 0.2), (90, 0, math.degrees(a) + 90), r=0.022, mat=M(ZINC, metal=0.6))
    # weed caught in the cage
    strands(B, "weed", [[(0.45, -0.1, 0.6), (0.38, -0.18, 1.0), (0.3, -0.12, 1.25), (0.33, 0.05, 1.3)],
                        [(-0.4, 0.3, 0.6), (-0.3, 0.32, 1.1), (-0.36, 0.15, 1.27)]], 0.03, kelp(), sides=4)
    _settle(B, sink=0.12)
    eye = B.matrix_world @ Vector((0, 0, -1.78))
    chain(P, "chain", [(eye.x, eye.y, eye.z), (eye.x - 0.3, eye.y - 0.4, 0.02), (eye.x - 0.2, eye.y - 1.2, 0.02), (eye.x + 0.4, eye.y - 1.7, 0.02)],
          M(RUST, metal=0.5, wear=0.4), link=0.11, r=0.02)
    lump(P, "drift", 0.6, (eye.x * 0.6, eye.y * 0.6 + 0.3, 0), (1.6, 1.0, 0.35), sand(), rnd, jitter=0.08, u=12, v=6, flat=0.0)
    kelp_clump(P, "wrack", (1.0, -0.9), rnd, n=5)
    return P


def _trap(P, name, loc, rot, rnd, w=0.95, d=0.58, h=0.42):
    """A slatted wooden lobster trap: corner posts, gappy slats, netted ends with a funnel, a brick for ballast."""
    G = _grp(P, name, loc, rot)
    sh = rnd.randint(0, 2)
    pm = drift(sh)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box(f"{name}_post", (0.045, 0.045, h), pm, loc=(sx * (w / 2 - 0.022), sy * (d / 2 - 0.022), h / 2), bev=0.008, parent=G)
    for k in range(6):
        x = -w / 2 + 0.06 + k * (w - 0.12) / 5
        K.box(f"{name}_top", (0.06, d, 0.016), drift(sh + k), loc=(x, 0, h - 0.008), bev=0, parent=G)
        K.box(f"{name}_bot", (0.06, d, 0.016), drift(sh + k + 1), loc=(x, 0, 0.008), bev=0, parent=G)
    for sy in (-1, 1):
        for k in range(3):
            K.box(f"{name}_side", (w, 0.016, 0.055), drift(sh + k), loc=(0, sy * (d / 2 - 0.008), 0.07 + k * (h - 0.14) / 2), bev=0, parent=G)
    for sx in (-1, 1):
        K.box(f"{name}_net", (0.008, d - 0.06, h - 0.06), netm(), loc=(sx * (w / 2 - 0.02), 0, h / 2), bev=0, parent=G)
        K.torus(f"{name}_funnel", 0.075, 0.012, M(GRAPHITE, rough=0.7), loc=(sx * (w / 2 - 0.03), 0, h / 2), rot=(0, 90, 0), segs=12, parent=G)
    K.box(f"{name}_brick", (0.2, 0.1, 0.06), K.mat("concrete", CONCRETE, rough=0.9, grime=0.5), loc=(0.15, 0.05, 0.05), bev=0.01, parent=G)
    return G


def lobster_traps(seed=48):
    """A stack of slatted lobster traps, one leaning on the pile, a buoy and its line on top."""
    rnd = random.Random(seed)
    P = _root("traps")
    _trap(P, "trap_a", (-0.5, 0.0, 0), (0, 0, 3), rnd)
    _trap(P, "trap_b", (0.52, 0.04, 0), (0, 0, -4), rnd)
    _trap(P, "trap_c", (0.05, 0.02, 0.43), (0, 0, 12), rnd)
    _trap(P, "trap_d", (-0.25, -0.65, 0.0), (-62, 0, 4), rnd)
    _trap(P, "trap_e", (1.45, -0.3, 0.0), (0, 0, 38), rnd)
    fl = _grp(P, "float", (1.25, -1.0, 0.11), (0, 80, 25))
    K.sphere("float_ball", 0.11, M(SIGNAL, wear=0.4, grime=0.3, rough=0.6), scale=(1, 1, 1.2), parent=fl)
    K.cyl("float_stem", 0.015, 0.3, _dark(), loc=(0, 0, 0.2), segs=6, bev=0, parent=fl)
    tube(P, "line", [(0.15, 0.05, 0.87), (0.4, 0.1, 0.86), (0.62, 0.3, 0.86), (0.5, 0.28, 0.86), (0.3, 0.32, 0.86), (0.45, 0.1, 0.86),
                     (0.7, 0.0, 0.86), (0.88, -0.05, 0.6), (0.98, -0.08, 0.05), (1.15, -0.85, 0.03), (1.15, -1.0, 0.1)], 0.016, rope_mat(), sides=5, sub=3)
    kelp_clump(P, "wrack", (-1.1, 0.5), rnd, n=4, spread=0.25)
    return P


def fishing_net_rack(seed=49):
    """A net hung to dry over a pole between two lashed A-frames, its floats along the foot, a heap of net below."""
    rnd = random.Random(seed)
    P = _root("netrack")
    zt = 1.95
    for s in (-1, 1):
        # crossed poles, the bar resting in the crotch and lashed there
        for sy in (-1, 1):
            rod(P, f"leg{s}{sy}", (s * 1.75 + sy * 0.05, sy * 0.6, -0.05), (s * 1.75 - sy * 0.02, sy * -0.2, zt + 0.32), 0.05, drift(1), segs=8)
        for k in range(3):
            K.torus(f"lash{s}{k}", 0.075, 0.012, rope_mat(), loc=(s * 1.75 + (k - 1) * 0.035, 0, zt - 0.03), rot=(0, 90, 0), segs=12, parent=P)
    rod(P, "bar", (-2.1, 0, zt), (2.1, 0, zt), 0.045, drift(0), segs=10)
    ph = rnd.uniform(0, 6.28)

    def folds(u):
        # the net hangs bunched in soft vertical folds, gathered where it was thrown over
        return 0.09 * math.sin(u * 2 * math.pi * 4 + ph) + 0.05 * math.sin(u * 2 * math.pi * 2.5)

    def front(u, v):
        x = -1.5 + 3.0 * u
        L = 1.45 + 0.18 * math.sin(u * 7 + 1) + 0.12 * math.sin(u * 17)
        return (x + 0.05 * v * math.sin(u * 9), -0.05 - (0.04 + folds(u)) * min(v * 3, 1) - 0.08 * math.sin(math.pi * v), zt - 0.04 - L * v)

    def back(u, v):
        x = -1.5 + 3.0 * u
        L = 0.8 + 0.25 * math.sin(u * 5 + 2)
        return (x, 0.05 + (0.04 - folds(u) * 0.7) * min(v * 3, 1), zt - 0.04 - L * v)

    lines = _netting(front, 16, 7) + _netting(back, 16, 4)
    lines.append([(-1.5 + 3.0 * i / 16, 0, zt + 0.045) for i in range(17)])
    strands(P, "net", lines, 0.006, netm(), sides=3)
    tube(P, "footrope", [front(i / 24, 1.0) for i in range(25)], 0.014, rope_mat(), sides=4)
    for k in range(9):
        u = (k + 0.5) / 9
        x, y, z = front(u, 1.0)
        K.cyl(f"cork{k}", 0.045, 0.11, K.mat("cork", CORK, rough=0.9, grime=0.3, noise_scale=12.0), loc=(x, y - 0.01, z + 0.03), rot=(0, 90, 0), segs=8, bev=0.01, parent=P)
    lump(P, "heap", 0.4, (0.95, -0.85, 0), (1.3, 0.9, 0.55), netm(), rnd, jitter=0.35, u=10, v=6, flat=0.0)
    for k, col in enumerate((MUSTARD, BONE, SIGNAL)):
        K.sphere(f"float{k}", 0.1, M(col, wear=0.3, rough=0.6), loc=(0.55 + k * 0.3, -1.25 + k * 0.1, 0.09), parent=P)
    return P


def lighthouse_small(seed=50):
    """A 10 m colony lighthouse kept going by settlers: a framed octagonal tower in bone and steel blue, a gallery and
    lantern room with a warm lamp lit again, patched plates, a rewired cable and a lean-to store at its foot."""
    rnd = random.Random(seed)
    P = _root("lighthouse")
    base, H = 0.35, 7.3
    r0, r1 = 1.55, 1.05
    ap = math.cos(math.radians(22.5))
    alpha = math.degrees(math.atan((r0 - r1) * ap / H))

    def rad(z):
        return r0 + (r1 - r0) * (z - base) / H

    def onface(name, k, z, w, hh, mat, out=0.0, t=0.03, spin=0.0, bev=0.006):
        phi = math.radians(45 * k)
        a = rad(z) * ap + t / 2 + out
        return K.box(name, (w, t, hh), mat, loc=(a * math.cos(phi), a * math.sin(phi), z), rot=(-alpha, spin, 45 * k + 90), bev=bev, parent=P)

    K.cyl("plinth", 2.1, base, K.mat("concrete", CONCRETE, rough=0.9, wear=0.4, grime=0.6, cracks=0.2, moss=0.3), loc=(0, 0, base / 2 - 0.05),
          rot=(0, 0, 22.5), segs=8, bev=0.04, parent=P)
    shell = M(BONE, wear=0.45, grime=0.5, moss=0.1)
    steel = _steel(0.3)
    K.cyl("tower", r0, H, shell, loc=(0, 0, base + H / 2), rot=(0, 0, 22.5), segs=8, r2=r1, bev=0.0, parent=P)
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        beam(P, f"rail{k}", (math.cos(a) * (r0 + 0.02), math.sin(a) * (r0 + 0.02), base), (math.cos(a) * (r1 + 0.02), math.sin(a) * (r1 + 0.02), base + H),
             0.16, 0.16, steel, bev=0.02)
    for z in (base + 0.1, base + 2.5, base + 4.9, base + H - 0.1):
        K.cyl(f"band{z:.1f}", rad(z) + 0.07, 0.2, steel, loc=(0, 0, z), rot=(0, 0, 22.5), segs=8, bev=0.015, parent=P)
    K.cyl("mustard", rad(base + 6.3) + 0.03, 0.5, M(MUSTARD, wear=0.45, grime=0.3), loc=(0, 0, base + 6.3), rot=(0, 0, 22.5), segs=8, bev=0.0, parent=P)
    # the door and two portholes up the front
    onface("door_frame", 6, base + 1.2, 1.3, 2.4, _dark(), t=0.08)
    onface("door", 6, base + 1.15, 1.0, 2.15, M(MUSTARD, wear=0.5, grime=0.4), out=0.04, t=0.05)
    onface("door_patch", 6, base + 0.55, 0.6, 0.45, tin(ZINC, wear=0.4), out=0.08, t=0.02, spin=4)
    K.box("step", (1.6, 0.6, 0.18), K.mat("concrete", CONCRETE, rough=0.9, wear=0.4, grime=0.6, cracks=0.2, moss=0.3), loc=(0, -2.1, 0.06), bev=0.03, parent=P)
    label(P, "LH-3", (0, -(rad(base + 3.0) * ap + 0.035), base + 3.0), 0.42, M(GRAPHITE, wear=0.2), rot=(90 - alpha, 0, 0), extrude=0.006).data.resolution_u = 2
    for k, z in ((6, base + 4.0), (2, base + 3.4), (0, base + 5.5)):
        phi = math.radians(45 * k)
        a = rad(z) * ap + 0.02
        ob = K.cyl(f"port{k}", 0.22, 0.06, glass(), loc=(a * math.cos(phi), a * math.sin(phi), z), rot=(90 - alpha, 0, 45 * k + 90), segs=16, bev=0.0, parent=P)
        K.torus(f"port_rim{k}", 0.24, 0.035, steel, loc=ob.location, rot=(90 - alpha, 0, 45 * k + 90), segs=12, parent=P)
    # settler patches: mismatched plates bolted over the bone
    for k, (f, z, w, hh, col) in enumerate(((7, 2.2, 0.8, 0.9, SEA), (5, 3.6, 0.7, 0.6, ZINC), (7, 5.6, 0.6, 0.7, BRICK), (1, 1.6, 0.9, 0.7, RUST), (5, 1.4, 0.6, 0.8, SEA))):
        ob = onface(f"patch{k}", f, z, w, hh, tin(col, wear=0.5), out=0.005, t=0.02, spin=rnd.uniform(-6, 6))
        for sx in (-1, 1):
            for sz in (-1, 1):
                p = ob.matrix_basis @ Vector((sx * (w / 2 - 0.06), -0.015, sz * (hh / 2 - 0.06)))
                K.cyl("rivet", 0.022, 0.025, M(ZINC, metal=0.6, wear=0.3), loc=p, rot=(90 - alpha, 0, 45 * f + 90), segs=6, bev=0, parent=P)
    # gallery and lantern room
    zg = base + H
    K.cyl("gallery", 1.85, 0.16, steel, loc=(0, 0, zg + 0.08), segs=16, bev=0.02, parent=P)
    for k in range(8):
        a = math.radians(45 * k)
        beam(P, f"bracket{k}", (math.cos(a) * (r1 + 0.1), math.sin(a) * (r1 + 0.1), zg - 0.9), (math.cos(a) * 1.7, math.sin(a) * 1.7, zg), 0.1, 0.1, steel, bev=0)
    for k in range(16):
        a = 2 * math.pi * (k + 0.5) / 16
        K.cyl(f"stanchion{k}", 0.022, 1.0, steel, loc=(math.cos(a) * 1.78, math.sin(a) * 1.78, zg + 0.66), segs=6, bev=0, parent=P)
    K.torus("handrail", 1.78, 0.035, steel, loc=(0, 0, zg + 1.16), segs=26, parent=P)
    K.torus("midrail", 1.78, 0.02, steel, loc=(0, 0, zg + 0.66), segs=26, parent=P)
    K.cyl("lantern_base", 0.98, 0.7, _steel(0.35), loc=(0, 0, zg + 0.51), segs=16, bev=0.02, parent=P)
    K.cyl("lantern_glass", 0.9, 1.05, K.mat("lantern_glass", "#5f7880", rough=0.1, metal=0.1, grime=0.3), loc=(0, 0, zg + 1.38), rot=(0, 0, 22.5), segs=8, bev=0, parent=P)
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        K.box(f"mullion{k}", (0.07, 0.07, 1.05), _dark(), loc=(math.cos(a) * 0.9, math.sin(a) * 0.9, zg + 1.38), rot=(0, 0, math.degrees(a)), bev=0, parent=P)
    K.cyl("lamp", 0.28, 0.5, glow(WARM, 6), loc=(0, 0, zg + 1.35), segs=12, bev=0.02, parent=P)
    K.cyl("roof", 1.08, 0.6, M(SIGNAL, wear=0.45, grime=0.35), loc=(0, 0, zg + 2.2), segs=16, r2=0.15, bev=0.02, parent=P)
    K.sphere("vent", 0.17, _dark(), loc=(0, 0, zg + 2.58), parent=P)
    K.cyl("rod", 0.02, 0.6, _dark(), loc=(0, 0, zg + 2.95), segs=6, bev=0, parent=P)
    lamp = K.point("lamp_light", (0, 0, zg + 1.35), WARM, 120, radius=0.3)
    lamp.parent = P
    # a settler wind charger clamped to the rail, its cable run down the tower to the store
    rod(P, "wc_pole", (1.4, 0.9, zg + 0.1), (1.4, 0.9, zg + 2.2), 0.04, M(ZINC, metal=0.5, wear=0.3))
    K.cyl("wc_body", 0.09, 0.32, tin(SEA), loc=(1.4, 0.9, zg + 2.25), rot=(0, 90, 0), segs=10, bev=0.01, parent=P)
    for k in range(3):
        a = 120 * k
        K.box(f"wc_blade{k}", (0.02, 0.1, 0.55), wood(2, wear=0.3), loc=(1.22, 0.9 + math.sin(math.radians(a)) * 0.29, zg + 2.25 + math.cos(math.radians(a)) * 0.29),
              rot=(a, 0, 0), bev=0.004, parent=P)
    K.box("wc_tail", (0.5, 0.02, 0.25), tin(ZINC), loc=(1.75, 0.9, zg + 2.3), bev=0.004, parent=P)
    cab = []
    for k in range(9):
        z = zg - k * (H - 0.6) / 8
        a = rad(z) * ap + 0.06
        cab.append((a * math.cos(math.radians(-8)), a * math.sin(math.radians(-8)) + 0.0, z))
    tube(P, "cable", [(1.4, 0.9, zg + 0.1), (1.55, 0.2, zg - 0.05)] + cab[1:] + [(2.3, -0.35, 0.65)], 0.025, M(GRAPHITE, rough=0.6), sides=5)
    # lean-to store on the right
    for y in (-0.9, 0.9):
        post(P, f"store_post{y}", (2.6, y), 1.6, r=0.06)
    corrugated(P, "store_roof", 2.3, 1.4, tin(ZINC, wear=0.25), (2.1, 0, 1.68), rot=(22, 0, 90), pitch=0.15)
    for k in range(4):
        z = 0.3 + k * 0.32
        K.box(f"store_board{k}", (0.03, 1.9, 0.3), wood(k, wear=0.4), loc=(2.63, 0, z), rot=(0, 0, rnd.uniform(-1, 1)), bev=0.008, parent=P)
    K.cyl("store_drum", 0.28, 0.85, M(BRICK, metal=0.4, wear=0.6, grime=0.5), loc=(2.2, -1.1, 0.43), segs=16, bev=0.02, parent=P)
    K.box("store_crate", (0.6, 0.5, 0.45), wood(2), loc=(2.0, 1.0, 0.23), rot=(0, 0, 8), bev=0.015, parent=P)
    for k in range(6):
        a = math.radians(150 + k * 50 + rnd.uniform(-15, 15))
        rock(P, f"foot{k}", rnd.uniform(0.2, 0.35), 0.25, (math.cos(a) * 2.25, math.sin(a) * 2.25, 0.05), rnd, mat=shore_rock())
    return P


def boathouse_shack(seed=51):
    """A plank boathouse open to the water: clapboard walls on posts, a patched tin roof, a slip of rails and sleepers
    running out of the door, a winch on the back wall and walkways down the sides."""
    rnd = random.Random(seed)
    P = _root("boathouse")
    W, D, Hw = 3.8, 5.0, 2.5
    pitch = 28
    rise = W / 2 * math.tan(math.radians(pitch))
    for x in (-W / 2, W / 2):
        for y in (-D / 2, 0, D / 2):
            K.box(f"post{x}{y}", (0.14, 0.14, Hw), wood(3), loc=(x, y, Hw / 2), bev=0.015, parent=P)
        K.box(f"plate{x}", (0.14, D + 0.14, 0.14), wood(3), loc=(x, 0, Hw - 0.07), bev=0.015, parent=P)
    K.box("lintel", (W, 0.14, 0.18), wood(3), loc=(0, -D / 2, Hw - 0.09), bev=0.015, parent=P)
    # clapboard sides and back
    rows = 9
    for k in range(rows):
        z = 0.15 + k * (Hw - 0.3) / (rows - 1)
        sh = k + rnd.randint(0, 3)
        for s in (-1, 1):
            if s == 1 and k in (4, 5) or (s == 1 and k == 6):
                # the window gap on the right wall
                for y0, y1 in ((-D / 2, -0.85), (0.15, D / 2)):
                    K.box(f"board{s}{k}{y0}", (0.03, y1 - y0, 0.28), wood(sh), loc=(s * (W / 2 + 0.085), (y0 + y1) / 2, z), rot=(0 if s > 0 else 0, -s * 6, 0), bev=0.008, parent=P)
                continue
            K.box(f"board{s}{k}", (0.03, D + 0.1, 0.28), wood(sh), loc=(s * (W / 2 + 0.085), 0, z), rot=(0, -s * 6, 0), bev=0.008, parent=P)
        K.box(f"back{k}", (W + 0.2, 0.03, 0.28), wood(sh + 1), loc=(0, D / 2 + 0.085, z), rot=(6, 0, 0), bev=0.008, parent=P)
    # window: frame, dark glass, one shutter swung open
    K.box("win_frame", (0.08, 1.1, 0.95), wood(3), loc=(W / 2 + 0.09, -0.35, 1.48), bev=0.01, parent=P)
    K.box("win_glass", (0.03, 0.9, 0.75), glass(), loc=(W / 2 + 0.1, -0.35, 1.48), bev=0.0, parent=P)
    K.box("win_bar", (0.04, 0.05, 0.75), wood(3), loc=(W / 2 + 0.12, -0.35, 1.48), bev=0.0, parent=P)
    K.box("shutter", (0.5, 0.04, 0.85), M(SEA, wear=0.6, grime=0.4), loc=(W / 2 + 0.33, -0.95, 1.48), rot=(0, 0, -65), bev=0.008, parent=P)
    # gables
    gable = [(-W / 2 - 0.1, 0), (W / 2 + 0.1, 0), (0, rise + 0.05)]
    for y in (-D / 2 - 0.05, D / 2 + 0.05):
        prism(P, f"gable{y}", gable, 0.04, wood(1, wear=0.5), loc=(0, y, Hw), plane="xz")
    K.box("ridge", (0.14, D + 0.5, 0.16), wood(3), loc=(0, 0, Hw + rise - 0.03), bev=0.015, parent=P)
    slant = (W / 2 + 0.35) / math.cos(math.radians(pitch))
    for s in (-1, 1):
        cx = s * (W / 2 + 0.35) / 2
        cz = Hw + rise - (W / 2 + 0.35) / 2 * math.tan(math.radians(pitch)) + 0.07
        corrugated(P, f"roof{s}", slant, D + 0.5, tin(ZINC, wear=0.25), (cx, 0, cz), rot=(0, s * pitch, 0), pitch=0.15)
    corrugated(P, "roof_patch", 1.1, 1.3, tin(SEA, wear=0.3), (-0.95, 0.6, Hw + rise - 0.95 * math.tan(math.radians(pitch)) + 0.11), rot=(0, -pitch, 0), pitch=0.15)
    corrugated(P, "roof_patch2", 0.9, 1.0, tin(RUST, wear=0.2), (1.0, -1.5, Hw + rise - 1.0 * math.tan(math.radians(pitch)) + 0.11), rot=(0, pitch, 0), pitch=0.15)
    # walkways down both sides inside
    for s in (-1, 1):
        for k in range(10):
            y = -D / 2 + 0.25 + k * (D - 0.5) / 9
            K.box(f"walk{s}{k}", (0.8, 0.22, 0.04), wood(k), loc=(s * (W / 2 - 0.45), y, 0.42), bev=0, parent=P)
        K.box(f"walk_beam{s}", (0.1, D, 0.18), wood(3), loc=(s * (W / 2 - 0.85), 0, 0.31), bev=0.01, parent=P)
    # the slip: two rails on sleepers from the back wall out past the door
    for s in (-1, 1):
        beam(P, f"slip_rail{s}", (s * 0.5, D / 2 - 0.2, 0.32), (s * 0.5, -D / 2 - 2.0, 0.04), 0.12, 0.14, wood(3))
    for k in range(9):
        y = D / 2 - 0.4 - k * 0.8
        z = 0.32 - (D / 2 - 0.2 - y) / (D + 1.8) * 0.28 - 0.12
        K.box(f"sleeper{k}", (1.5, 0.18, 0.1), drift(k), loc=(0, y, max(z, 0.04)), bev=0.01, parent=P)
    # winch on the back wall with its cable down the slip
    for s in (-1, 1):
        K.box(f"winch_cheek{s}", (0.06, 0.3, 0.5), M(GRAPHITE, metal=0.5, wear=0.4), loc=(s * 0.35, D / 2 - 0.2, 0.75), bev=0.01, parent=P)
    K.cyl("winch_drum", 0.13, 0.64, M(RUST, metal=0.5, wear=0.4), loc=(0, D / 2 - 0.2, 0.85), rot=(0, 90, 0), segs=14, bev=0.01, parent=P)
    K.box("winch_crank", (0.05, 0.04, 0.3), M(GRAPHITE, metal=0.5), loc=(0.42, D / 2 - 0.2, 0.75), rot=(0, 0, 0), bev=0.005, parent=P)
    rod(P, "winch_cable", (0, D / 2 - 0.33, 0.8), (0, -0.8, 0.22), 0.012, M(GRAPHITE, metal=0.4), segs=6)
    K.box("hook", (0.05, 0.12, 0.08), M(GRAPHITE, metal=0.5), loc=(0, -0.85, 0.22), bev=0.008, parent=P)
    # a lantern over the door and a buoy hung on the post
    K.box("lantern_arm", (0.04, 0.4, 0.04), M(GRAPHITE, metal=0.5), loc=(1.55, -D / 2 - 0.2, Hw - 0.25), bev=0.004, parent=P)
    K.cyl("lantern", 0.08, 0.2, K.mat("lantern_glass", "#5f7880", rough=0.1, metal=0.1, grime=0.3), loc=(1.55, -D / 2 - 0.38, Hw - 0.42), segs=8, bev=0.01, parent=P)
    K.cyl("lantern_cap", 0.1, 0.06, M(GRAPHITE, metal=0.5), loc=(1.55, -D / 2 - 0.38, Hw - 0.29), segs=8, r2=0.03, bev=0.0, parent=P)
    K.sphere("buoy", 0.16, M(MUSTARD, wear=0.4), loc=(-W / 2 - 0.05, -D / 2 - 0.2, 1.3), parent=P)
    K.sphere("buoy2", 0.14, M(SIGNAL, wear=0.4), loc=(-W / 2 - 0.08, -D / 2 - 0.2, 1.0), parent=P)
    K.torus("buoy_rope", 0.06, 0.012, rope_mat(), loc=(-W / 2 - 0.05, -D / 2 - 0.12, 1.55), rot=(90, 0, 0), segs=10, parent=P)
    return P


def life_ring_post(seed=52):
    """A post with a backboard and a little roof, a red and bone life ring on its hook, a throwing line coiled on a peg."""
    rnd = random.Random(seed)
    P = _root("lifering")
    post(P, "post", (0, 0), 1.95, r=0.07)
    plank(P, "board", (0.62, 0.04, 0.72), (0, -0.1, 1.35), shade=1)
    K.box("board_paint", (0.56, 0.01, 0.66), M(BONE, wear=0.6, grime=0.35), loc=(0, -0.125, 1.35), bev=0.0, parent=P)
    for s in (-1, 1):
        K.box(f"roof{s}", (0.42, 0.24, 0.025), tin(ZINC, wear=0.25), loc=(s * 0.18, -0.06, 1.86), rot=(0, s * 22, 0), bev=0.004, parent=P)
    K.cyl("hook", 0.02, 0.12, M(GRAPHITE, metal=0.6), loc=(0, -0.18, 1.62), rot=(90, 0, 0), segs=8, bev=0, parent=P)
    R, r = 0.32, 0.065
    for k in range(4):
        col = SIGNAL if k % 2 == 0 else BONE
        K.torus(f"ring{k}", R, r, M(col, wear=0.3, grime=0.3, rough=0.6), loc=(0, -0.21, 1.62 - R + 0.02), rot=(90, 0, 0), arc=90, segs=32, parent=P).rotation_euler[1] = math.radians(45 + 90 * k)
    # grab line round the ring, clipped at the colour joins
    gl = []
    for i in range(33):
        a = 2 * math.pi * i / 32
        dip = 0.025 * abs(math.sin(2 * a))
        gl.append((math.cos(a) * (R + r + 0.012 + dip), -0.21, 1.62 - R + 0.02 + math.sin(a) * (R + r + 0.012 + dip)))
    tube(P, "grabline", gl, 0.011, rope_mat(), sides=5)
    for k in range(4):
        a = math.radians(90 * k)
        K.box(f"clip{k}", (0.05, 0.16, 0.05), rope_mat(), loc=(math.cos(a) * R, -0.21, 1.62 - R + 0.02 + math.sin(a) * R), rot=(0, -90 * k, 0), bev=0.01, parent=P)
    K.cyl("peg", 0.018, 0.14, wood(3), loc=(0, -0.12, 0.85), rot=(90, 0, 0), segs=8, bev=0, parent=P)
    for k in range(4):
        K.torus(f"line{k}", 0.13 + k * 0.006, 0.012, rope_mat(), loc=(0.005 * k, -0.18 - 0.012 * k, 0.74), rot=(90 + rnd.uniform(-6, 6), rnd.uniform(-6, 6), 0), segs=18, parent=P)
    for k in range(5):
        a = rnd.uniform(0, 6.28)
        K.sphere(f"stone{k}", rnd.uniform(0.07, 0.11), shore_rock(), loc=(math.cos(a) * 0.17, math.sin(a) * 0.17, 0.02), scale=(1.2, 1.0, 0.6), parent=P)
    return P


def anchor_rusty(seed=53):
    """A big fisherman's anchor driven into the sand at a slant: shank, curved arms and flukes, an iron stock, its ring
    and a run of chain."""
    rnd = random.Random(seed)
    P = _root("anchor")
    A = _grp(P, "anchor", (0, 0, 0), (8, -38, 12))
    iron = K.mat("anchor_rust", RUST, rough=0.85, metal=0.35, wear=0.35, grime=0.4, noise_scale=5.0, cracks=0.2)
    K.box("shank", (0.16, 0.12, 2.5), iron, loc=(0, 0, 1.25), bev=0.03, parent=A)
    K.box("crown", (0.3, 0.18, 0.26), iron, loc=(0, 0, 0.05), bev=0.04, parent=A)
    for s in (-1, 1):
        pts = []
        for i in range(9):
            a = math.radians(-90 + s * (25 + 115 * i / 8))
            pts.append((math.cos(a) * 1.0 * (1 if s > 0 else 1), 0, 0.95 + math.sin(a) * 1.0))
        tube(A, f"arm{s}", pts, 0.075, iron, sides=8)
        tip = Vector(pts[-1])
        fl = [(-0.22, -0.05), (0.0, 0.42), (0.22, -0.05), (0.0, -0.18)]
        f = prism(A, f"fluke{s}", fl, 0.05, iron, loc=tip, plane="xz", bev=0.01)
        f.rotation_euler = (0, math.radians(s * -28), 0)
        f.location = tip + Vector((s * -0.08, 0, -0.12))
    K.cyl("stock", 0.06, 2.0, iron, loc=(0, 0, 2.25), rot=(90, 0, 0), segs=10, bev=0.01, parent=A)
    for s in (-1, 1):
        K.sphere(f"stock_ball{s}", 0.1, iron, loc=(0, s * 1.0, 2.25), parent=A)
    K.box("stock_collar", (0.24, 0.2, 0.16), iron, loc=(0, 0, 2.25), bev=0.02, parent=A)
    K.torus("ring", 0.2, 0.04, iron, loc=(0, 0, 2.62), rot=(90, 0, 0), segs=20, parent=A)
    _settle(A, sink=0.55)
    ring = A.matrix_world @ Vector((0, 0, 2.78))
    crown = A.matrix_world @ Vector((0, 0, 0.0))
    chain(P, "chain", [(ring.x, ring.y, ring.z), (ring.x + 0.25, ring.y - 0.3, 0.35), (ring.x + 0.5, ring.y - 0.6, 0.03), (ring.x + 1.2, ring.y - 0.7, 0.03),
                       (ring.x + 1.6, ring.y - 0.2, 0.03)], M(RUST, metal=0.5, wear=0.3), link=0.16, r=0.028)
    mound(P, "sand", 1.3, 1.0, 0.3, sand(), rnd, loc=(crown.x, crown.y), segs=14, rings=4)
    kelp_clump(P, "wrack", (crown.x + 0.6, crown.y + 0.8), rnd, n=4, spread=0.3)
    return P


def mooring_post(seed=54):
    """A cast colony bollard on its concrete block, painted steel blue with a mustard cap, a mooring rope looped on it."""
    rnd = random.Random(seed)
    P = _root("bollard")
    K.box("block", (0.85, 0.85, 0.35), K.mat("concrete", CONCRETE, rough=0.9, wear=0.4, grime=0.6, cracks=0.2, moss=0.3), loc=(0, 0, 0.13), bev=0.04, parent=P)
    prof = [(0.001, 0.3), (0.3, 0.3), (0.3, 0.36), (0.2, 0.4), (0.17, 0.55), (0.16, 0.65), (0.18, 0.82), (0.26, 0.86), (0.27, 0.92), (0.22, 0.97), (0.001, 0.99)]
    lathe("bollard", prof, M(STEEL, metal=0.45, wear=0.35, grime=0.4, scratches=0.5), segs=20, parent=P)
    K.cyl("cap", 0.275, 0.08, M(MUSTARD, metal=0.3, wear=0.3), loc=(0, 0, 0.89), segs=20, bev=0.01, parent=P)
    for k in range(6):
        a = 2 * math.pi * (k + 0.5) / 6
        K.cyl(f"nut{k}", 0.03, 0.05, M(ZINC, metal=0.6, wear=0.4), loc=(math.cos(a) * 0.25, math.sin(a) * 0.25, 0.38), segs=6, bev=0, parent=P)
    # the eye of the rope round the waist, spliced and run off across the ground
    eye = []
    for i in range(25):
        a = 2 * math.pi * i / 24 + math.pi / 2
        eye.append((math.cos(a) * 0.205, math.sin(a) * 0.205, 0.62 + 0.04 * math.sin(a)))
    tube(P, "eye", eye, 0.04, rope_mat(), sides=8)
    tube(P, "rope", [(0.0, -0.2, 0.58), (0.05, -0.45, 0.48), (0.1, -0.7, 0.3), (0.12, -0.95, 0.06), (0.4, -1.3, 0.04), (0.9, -1.35, 0.04), (1.2, -1.0, 0.04),
                     (1.0, -0.75, 0.04), (0.7, -0.95, 0.045), (0.85, -1.2, 0.05), (1.35, -1.3, 0.04)], 0.04, rope_mat(), sides=8, sub=3)
    K.cyl("splice", 0.05, 0.22, rope_mat(), loc=(0.02, -0.35, 0.53), rot=(66, 0, 0), segs=8, bev=0.01, parent=P)
    kelp_clump(P, "wrack", (-0.6, 0.6), rnd, n=4, spread=0.25)
    return P


def beach_hut(seed=55):
    """A small beach hut of vertical boards in faded stripes, on short legs with a step, a pitched felt roof, a padlocked
    door and a little window."""
    rnd = random.Random(seed)
    P = _root("beachhut")
    W, D, Hw, fl = 2.0, 1.8, 2.1, 0.3
    pitch = 35
    rise = (W / 2) * math.tan(math.radians(pitch))
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box(f"leg{sx}{sy}", (0.12, 0.12, fl + 0.1), wood(3), loc=(sx * (W / 2 - 0.1), sy * (D / 2 - 0.1), (fl + 0.1) / 2 - 0.1), bev=0.012, parent=P)
    K.box("floor", (W + 0.04, D + 0.04, 0.06), wood(2), loc=(0, 0, fl), bev=0.01, parent=P)
    stripes = [M(BONE, wear=0.55, grime=0.3), M(SEA, wear=0.6, grime=0.3), M(BONE, wear=0.55, grime=0.3), M(BRICK, wear=0.6, grime=0.3)]
    bw = 0.2
    n = int(round(W / bw))
    for k in range(n):
        x = -W / 2 + bw / 2 + k * bw
        h = Hw + rise * (1 - abs(x) / (W / 2)) - 0.02
        for y in (-D / 2, D / 2):
            if y < 0 and -0.4 < x < 0.4:
                # the door gap: board over it only above the door
                K.box(f"over{k}", (bw - 0.008, 0.03, h - 2.0), stripes[k % 4], loc=(x, y - 0.015, fl + 2.0 + (h - 2.0) / 2), bev=0.006, parent=P)
                continue
            K.box(f"board{k}{y}", (bw - 0.008, 0.03, h), stripes[k % 4], loc=(x, y + (0.015 if y > 0 else -0.015), fl + h / 2), bev=0.006, parent=P)
    m = int(round(D / bw))
    for k in range(m):
        y = -D / 2 + bw / 2 + k * bw
        for s in (-1, 1):
            if s == 1 and k in (3, 4) :
                K.box(f"side_lo{k}", (0.03, bw - 0.008, 0.95), stripes[k % 4], loc=(s * (W / 2 + 0.015), y, fl + 0.475), bev=0.006, parent=P)
                K.box(f"side_hi{k}", (0.03, bw - 0.008, 0.55), stripes[k % 4], loc=(s * (W / 2 + 0.015), y, fl + Hw - 0.275), bev=0.006, parent=P)
                continue
            K.box(f"side{k}{s}", (0.03, bw - 0.008, Hw), stripes[k % 4], loc=(s * (W / 2 + 0.015), y, fl + Hw / 2), bev=0.006, parent=P)
    # window in the right wall
    K.box("win_glass", (0.02, 0.38, 0.55), glass(), loc=(W / 2 + 0.01, -D / 2 + bw * 4, fl + 1.23), bev=0.0, parent=P)
    for dz in (-0.3, 0.3):
        K.box("win_sill", (0.06, 0.48, 0.05), wood(3), loc=(W / 2 + 0.04, -D / 2 + bw * 4, fl + 1.23 + dz), bev=0.006, parent=P)
    # door: boards, a Z brace, hinges, hasp and padlock
    door = _grp(P, "door", (-0.4, -D / 2 - 0.04, fl), (0, 0, -6))
    for k in range(4):
        K.box(f"door_board{k}", (0.19, 0.03, 1.95), M(SEA, wear=0.6, grime=0.35), loc=(0.1 + k * 0.2, 0, 0.98), bev=0.006, parent=door)
    for z in (0.35, 1.6):
        K.box(f"door_ledge{z}", (0.76, 0.03, 0.1), wood(1), loc=(0.4, -0.03, z), bev=0.006, parent=door)
        K.box(f"hinge{z}", (0.3, 0.012, 0.04), M(GRAPHITE, metal=0.6), loc=(0.12, -0.05, z), bev=0.002, parent=door)
    beam(door, "door_brace", (0.12, -0.03, 0.4), (0.68, -0.03, 1.55), 0.1, 0.03, wood(1))
    K.box("hasp", (0.14, 0.012, 0.04), M(ZINC, metal=0.6), loc=(0.75, -0.05, 1.05), bev=0.002, parent=door)
    K.box("padlock", (0.06, 0.03, 0.07), M(MUSTARD, metal=0.5, wear=0.3), loc=(0.8, -0.07, 1.0), bev=0.008, parent=door)
    # roof: two boarded slopes under felt, barge boards along the gables
    slant = (W / 2 + 0.25) / math.cos(math.radians(pitch))
    felt = K.mat("felt", "#3a3d3a", rough=0.95, grime=0.2, noise_scale=8.0, moss=0.3)
    for s in (-1, 1):
        cx = s * (W / 2 + 0.25) / 2 * 0.98
        cz = fl + Hw + rise - (W / 2 + 0.25) / 2 * math.tan(math.radians(pitch)) + 0.07
        K.box(f"roof{s}", (slant, D + 0.4, 0.06), felt, loc=(cx, 0, cz), rot=(0, s * pitch, 0), bev=0.01, parent=P)
        for y in (-D / 2 - 0.2, D / 2 + 0.2):
            K.box(f"barge{s}{y}", (slant, 0.04, 0.14), wood(3), loc=(cx, y, cz - 0.04), rot=(0, s * pitch, 0), bev=0.006, parent=P)
    K.box("ridge", (0.1, D + 0.45, 0.08), wood(3), loc=(0, 0, fl + Hw + rise + 0.1), bev=0.01, parent=P)
    # step
    K.box("step", (0.8, 0.35, 0.08), wood(2), loc=(0, -D / 2 - 0.3, 0.18), bev=0.01, parent=P)
    for s in (-1, 1):
        K.box(f"step_leg{s}", (0.08, 0.3, 0.2), wood(3), loc=(s * 0.33, -D / 2 - 0.3, 0.07), bev=0.008, parent=P)
    return P


def deck_chair(seed=56):
    """A folding deck chair: a beech frame in three parts and a striped canvas sling sagging between its bars."""
    P = _root("deckchair")
    fr = wood(2, wear=0.25)
    hw = 0.29
    top, foot = (0.42, 1.0), (-0.38, 0.0)              # the long frame (y, z)
    front0, front1 = (-0.62, 0.0), (-0.42, 0.43)        # the front frame that carries the lower bar
    back0, back1 = (0.62, 0.0), (0.08, 0.55)            # the rear legs
    for s in (-1, 1):
        beam(P, f"long{s}", (s * hw, foot[0], foot[1]), (s * hw, top[0], top[1]), 0.045, 0.022, fr)
        beam(P, f"front{s}", (s * (hw + 0.03), front0[0], front0[1]), (s * (hw + 0.03), front1[0], front1[1]), 0.045, 0.022, fr)
        beam(P, f"back{s}", (s * (hw - 0.03), back0[0], back0[1]), (s * (hw - 0.03), back1[0], back1[1]), 0.045, 0.022, fr)
        bolt(P, (s * (hw + 0.045), -0.2, 0.25), rot=(0, 90, 0))
    for y, z, w in ((top[0], top[1], 2 * hw + 0.04), (front1[0], front1[1], 2 * hw + 0.1), (back1[0], back1[1], 2 * hw - 0.02), (back0[0] - 0.08, 0.1, 2 * hw - 0.02),
                    (foot[0] + 0.1, 0.12, 2 * hw + 0.04)):
        K.cyl(f"bar{y}", 0.018, w, fr, loc=(0, y, z), rot=(0, 90, 0), segs=8, bev=0, parent=P)
    # the sling, in stripes
    cols = [SEA, BONE, SEA, BONE, SEA, BONE, SEA]
    ns = len(cols)
    a, b = Vector((0, top[0] - 0.02, top[1] - 0.02)), Vector((0, front1[0] + 0.02, front1[1] + 0.02))
    for k in range(ns):
        x0, x1 = -hw + 0.02 + (2 * hw - 0.04) * k / ns, -hw + 0.02 + (2 * hw - 0.04) * (k + 1) / ns
        rows = []
        for i in range(9):
            t = i / 8
            p = a.lerp(b, t)
            sag = 0.3 * math.sin(math.pi * t) ** 1.2
            rows.append([(x0, p.y + sag * 0.35, p.z - sag), (x1, p.y + sag * 0.35, p.z - sag)])
        surface(P, f"sling{k}", rows, M(cols[k], wear=0.4, grime=0.3, rough=0.9, scratches=0.0), thick=0.006)
    return P


def _drum(P, name, loc, rot, col, rnd):
    """An oil drum lying or standing: painted shell, rolling hoops, end lids."""
    G = _grp(P, name, loc, rot)
    K.cyl(f"{name}_shell", 0.3, 0.88, M(col, metal=0.4, wear=0.6, grime=0.5, scratches=0.5), segs=18, bev=0.02, parent=G)
    for z in (-0.25, 0.25):
        K.cyl(f"{name}_hoop", 0.31, 0.03, M(RUST, metal=0.5, wear=0.3), loc=(0, 0, z), segs=18, bev=0, parent=G)
    for z in (-0.44, 0.44):
        K.cyl(f"{name}_lid", 0.27, 0.01, M(RUST, metal=0.5), loc=(0, 0, z), segs=18, bev=0, parent=G)
    return G


def _pallet(P, name, loc, rot, rnd, L=1.2, W=1.0):
    G = _grp(P, name, loc, rot)
    for k, x in enumerate((-L / 2 + 0.07, 0.0, L / 2 - 0.07)):
        K.box(f"{name}_stringer{k}", (0.1, W, 0.1), drift(k), loc=(x, 0, 0.05), bev=0.008, parent=G)
    for k in range(6):
        K.box(f"{name}_deck{k}", (L, 0.12, 0.025), drift(k + 1) if rnd.random() < 0.7 else wood(k), loc=(0, -W / 2 + 0.06 + k * (W - 0.12) / 5, 0.112),
              rot=(0, 0, rnd.uniform(-0.8, 0.8)), bev=0, parent=G)
    return G


def pallet_raft(seed=57):
    """A raft of four pallets lashed onto oil drums, a pole mast with a patched tarp, a paddle and a water jug aboard."""
    rnd = random.Random(seed)
    P = _root("raft")
    cols = [SEA, BRICK, ZINC, STEEL]
    for k, (x, y) in enumerate(((-0.62, -0.55), (0.62, -0.55), (-0.62, 0.55), (0.62, 0.55))):
        _drum(P, f"drum{k}", (x, y, 0.29), (0, 90, rnd.uniform(-3, 3)), cols[k], rnd)
    zt = 0.58
    for k, (x, y) in enumerate(((-0.61, -0.51), (0.61, -0.51), (-0.61, 0.51), (0.61, 0.51))):
        _pallet(P, f"pallet{k}", (x, y, zt), (rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-2, 2)), rnd)
    # lashings: rope round each drum up over a pallet stringer
    for k, (x, y) in enumerate(((-0.62, -0.55), (0.62, -0.55), (-0.62, 0.55), (0.62, 0.55))):
        K.torus(f"lash{k}", 0.33, 0.016, rope_mat(), loc=(x, y, 0.32), rot=(0, 90, 0), segs=16, parent=P).scale = (1.0, 0.95, 1.12)
    # mast and tarp
    mx, my = -0.25, 0.15
    K.cyl("mast", 0.04, 1.9, drift(1), loc=(mx, my, zt + 0.12 + 0.95), segs=8, bev=0.01, parent=P)
    K.cyl("yard", 0.03, 1.1, drift(0), loc=(mx, my - 0.04, zt + 1.8), rot=(0, 90, 0), segs=8, bev=0.0, parent=P)
    K.cyl("mast_lash", 0.055, 0.12, rope_mat(), loc=(mx, my, zt + 0.24), segs=8, bev=0.01, parent=P)
    rows = []
    for i in range(7):
        t = i / 6
        row = []
        for j in range(5):
            u = j / 4
            row.append((mx - 0.52 + 1.04 * u + 0.06 * t * (u - 0.5), my - 0.06 - 0.18 * math.sin(math.pi * t) * math.sin(math.pi * u) - 0.05 * t,
                        zt + 1.78 - (0.95 + 0.12 * math.sin(u * 7)) * t))
        rows.append(row)
    surface(P, "sail", rows, M(SEA, wear=0.5, grime=0.4, rough=0.9, scratches=0.0), thick=0.006)
    patch = [[(mx - 0.1 + 0.3 * u, my - 0.06 - 0.18 * math.sin(math.pi * t) * math.sin(math.pi * (0.4 + 0.29 * u)) - 0.05 * t - 0.012, zt + 1.78 - 0.95 * t)
              for u in (0, 1)] for t in (0.35, 0.5, 0.65)]
    surface(P, "sail_patch", patch, M(MUSTARD, wear=0.4, grime=0.3, rough=0.9, scratches=0.0), thick=0.004)
    for s in (-1, 1):
        rod(P, f"sheet{s}", (mx + s * 0.55, my - 0.1, zt + 0.86), (mx + s * 0.95, my - 0.6, zt + 0.14), 0.008, rope_mat(), segs=5)
    _oar(P, "paddle", 1.5, (-0.2, -0.75, zt + 0.15), (0, 0, 8), tip=MUSTARD)
    K.cyl("jug", 0.14, 0.4, M(BONE, wear=0.3, rough=0.5), loc=(0.75, 0.6, zt + 0.34), segs=12, bev=0.04, parent=P)
    K.cyl("jug_cap", 0.05, 0.06, M(SIGNAL, rough=0.5), loc=(0.75, 0.6, zt + 0.57), segs=10, bev=0.01, parent=P)
    return P


def _crab_pot(P, name, loc, rot, rnd, R=0.42, h=0.32):
    """A round crab pot: two steel hoops on short bars, a rubber-bound base, netting all round and a funnel in the top."""
    G = _grp(P, name, loc, rot)
    steel = M(RUST, metal=0.5, wear=0.3, grime=0.4)
    K.torus(f"{name}_base", R, 0.03, K.mat("rubber", "#26282a", rough=0.9, grime=0.3), loc=(0, 0, 0.03), segs=18, parent=G)
    K.torus(f"{name}_top", R * 0.92, 0.014, steel, loc=(0, 0, h), segs=16, parent=G)
    for k in range(8):
        a = 2 * math.pi * k / 8
        rod(G, f"{name}_bar{k}", (math.cos(a) * R, math.sin(a) * R, 0.03), (math.cos(a) * R * 0.92, math.sin(a) * R * 0.92, h), 0.01, steel, segs=4)
    lines = []
    nh = 6
    for d in (-1, 1):
        for k in range(nh):
            a0 = 2 * math.pi * k / nh
            lines.append([(math.cos(a0 + d * 0.9 * f) * R * (1 - 0.08 * f), math.sin(a0 + d * 0.9 * f) * R * (1 - 0.08 * f), 0.04 + (h - 0.05) * f) for f in (0, 0.25, 0.5, 0.75, 1.0)])
    for k in range(7):
        a0 = 2 * math.pi * k / 7
        lines.append([(math.cos(a0 + math.pi * 2 / 7 * f) * R * 0.9 * (1 - 0.75 * g), math.sin(a0 + math.pi * 2 / 7 * f) * R * 0.9 * (1 - 0.75 * g), h)
                      for f, g in ((0, 0), (0.5, 0.5), (1, 1))])
    strands(G, f"{name}_net", lines, 0.007, netm(), sides=3)
    K.cyl(f"{name}_funnel", 0.12, 0.12, netm(), loc=(0, 0, h - 0.04), segs=10, r2=0.08, bev=0, parent=G)
    K.torus(f"{name}_mouth", 0.08, 0.012, steel, loc=(0, 0, h - 0.1), segs=12, parent=G)
    K.cyl(f"{name}_floor", R * 0.95, 0.01, netm(), loc=(0, 0, 0.02), segs=16, bev=0, parent=G)
    return G


def crab_pots(seed=58):
    """Three round crab pots, one stacked askew on the others, their bridles tied to a line and a float."""
    rnd = random.Random(seed)
    P = _root("crabpots")
    _crab_pot(P, "pot_a", (-0.45, 0.05, 0), (0, 0, 10), rnd)
    _crab_pot(P, "pot_b", (0.45, -0.05, 0), (0, 0, -20), rnd)
    _crab_pot(P, "pot_c", (0.02, 0.05, 0.36), (6, -9, 30), rnd)
    _crab_pot(P, "pot_d", (0.95, 0.85, 0.3), (0, 75, 30), rnd)
    top = (0.06, 0.05, 0.88)
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.5
        tube(P, f"bridle{k}", [(0.02 + math.cos(a) * 0.36, 0.05 + math.sin(a) * 0.36, 0.67), top], 0.009, rope_mat(), sides=4)
    tube(P, "line", [top, (0.25, -0.15, 0.82), (0.5, -0.45, 0.3), (0.7, -0.65, 0.03), (1.1, -0.9, 0.03), (1.45, -0.6, 0.03)], 0.014, rope_mat(), sides=5, sub=3)
    K.sphere("float", 0.12, M(MUSTARD, wear=0.4, grime=0.3, rough=0.6), loc=(1.55, -0.55, 0.12), parent=P)
    K.torus("float_eye", 0.04, 0.01, _dark(), loc=(1.45, -0.58, 0.12), rot=(90, 0, 70), segs=10, parent=P)
    return P


def kayak_wreck(seed=59):
    """A colony plastic kayak cracked across its middle and folded on the sand: faded mustard hull, a cockpit rim,
    deck lines and half a paddle."""
    rnd = random.Random(seed)
    P = _root("kayak")
    L, B, Dk, Hd = 3.8, 0.6, 0.2, 0.13
    hullm = M(MUSTARD, wear=0.45, grime=0.4, rough=0.45, scratches=0.6)
    segs = 14

    def ring(t, jag=0.0):
        b = B / 2 * max(1 - (2 * t - 1) ** 2, 0.0) ** 0.75 + 0.008
        x = (t - 0.5) * L
        up = Hd * (0.4 + 0.6 * max(1 - (2 * t - 1) ** 2, 0.0)) + 0.06 * (2 * t - 1) ** 4
        lo = Dk * max(1 - (2 * t - 1) ** 2, 0.0) ** 0.6 + 0.005
        out = []
        for j in range(segs):
            a = 2 * math.pi * j / segs
            z = math.sin(a) * (up if math.sin(a) > 0 else lo)
            out.append((x + (rnd.uniform(-jag, jag) if jag else 0), math.cos(a) * b, z))
        return out

    def half(name, t0, t1, jag_end, loc, rot):
        G = _grp(P, name, loc, rot)
        bm = bmesh.new()
        n = 8
        rows = []
        for i in range(n + 1):
            t = t0 + (t1 - t0) * i / n
            jag = 0.07 if (i == 0 and jag_end == 0) or (i == n and jag_end == 1) else 0.0
            rows.append([bm.verts.new(p) for p in ring(t, jag)])
        for i in range(n):
            for j in range(segs):
                k = (j + 1) % segs
                bm.faces.new((rows[i][j], rows[i][k], rows[i + 1][k], rows[i + 1][j]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        ob = K.from_bm(f"{name}_hull", bm, hullm, smooth=True)
        ob.modifiers.new("thick", "SOLIDIFY").thickness = 0.012
        K.place(ob, parent=G)
        return G

    A = half("bow", 0.53, 0.995, 0, (0.08, 0, 0), (0, -8, 6))
    S = half("stern", 0.005, 0.47, 1, (-0.08, 0.04, 0), (0, 9, -10))
    # cockpit rim on the stern half, its opening dark
    K.torus("coaming", 1.0, 0.02, M(GRAPHITE, rough=0.6), loc=(-0.25, 0, Hd + 0.01), segs=24, parent=S).scale = (0.36, 0.22, 1)
    ck = K.disc("cockpit", 1.0, K.mat("cockpit", "#1c1e20", rough=0.9), (0, 0), z=0, segs=20)
    ck.parent = S
    ck.location = (-0.25, 0, Hd + 0.005)
    ck.scale = (0.34, 0.2, 1)
    for G, xs in ((A, (0.5, 0.9, 1.3)), (S, (-0.9, -1.3))):
        pts = [(x, s * 0.12, Hd + 0.02) for x in xs for s in (-1, 1)]
        for k in range(0, len(pts) - 2, 2):
            for s in (0, 1):
                rod(G, f"bungee{k}{s}", pts[k + s], pts[k + 3 - s], 0.008, M(GRAPHITE, rough=0.6), segs=4)
    K.box("hatch", (0.3, 0.26, 0.03), M(GRAPHITE, rough=0.6), loc=(-1.05, 0, Hd + 0.005), bev=0.01, parent=S)
    _settle(A, sink=0.04)
    _settle(S, sink=0.04)
    # half a paddle
    pg = _grp(P, "paddle", (0.4, -0.75, 0.025), (90, 0, 168))
    K.cyl("paddle_shaft", 0.017, 1.1, M(GRAPHITE, metal=0.3, wear=0.3), loc=(0.55, 0, 0), rot=(0, 90, 0), segs=8, bev=0, parent=pg)
    prism(pg, "paddle_blade", [(1.05, -0.05), (1.5, -0.09), (1.6, -0.06), (1.6, 0.06), (1.5, 0.09), (1.05, 0.05)], 0.012, M(MUSTARD, wear=0.4),
          plane="xz", bev=0.003).rotation_euler = (math.radians(90), 0, 0)
    kelp_clump(P, "wrack", (0.9, 0.5), rnd, n=4, spread=0.25)
    return P


def jetty_light(seed=60):
    """A three-pile dolphin bound with rope at the end of a jetty, a colony light post on the tallest pile: steel-blue
    column, red lens, a mustard band and a small solar plate."""
    rnd = random.Random(seed)
    P = _root("jettylight")
    piles = [((0.0, 0.18), 2.4, (0, 0, 0)), ((-0.2, -0.15), 2.1, (-5, -5, 0)), ((0.22, -0.13), 2.0, (-5, 5, 0))]
    for k, ((x, y), h, rot) in enumerate(piles):
        G = _grp(P, f"pile{k}", (x, y, 0), rot)
        K.cyl(f"pile{k}", 0.16, h + 0.1, drift(k), loc=(0, 0, h / 2 - 0.05), segs=12, bev=0.02, parent=G)
        K.cyl(f"weed{k}", 0.175, 0.4, kelp(), loc=(0, 0, 0.15), segs=12, bev=0.01, parent=G)
        K.cyl(f"top{k}", 0.13, 0.02, K.mat("heart", "#c9a06a", rough=0.9, noise_scale=10.0, cracks=0.3), loc=(0, 0, h), segs=12, bev=0, parent=G)
    for z in (1.0, 1.75):
        for k in range(2):
            K.torus(f"wrap{z}{k}", 0.36, 0.022, rope_mat(), loc=(0.0, -0.03, z + k * 0.05), segs=16, parent=P).scale = (1.0, 0.9, 1)
    zt = 2.4
    K.box("plate", (0.38, 0.38, 0.03), _dark(), loc=(0, 0.18, zt + 0.015), bev=0.005, parent=P)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.cyl("nut", 0.02, 0.03, M(ZINC, metal=0.6), loc=(sx * 0.14, 0.18 + sy * 0.14, zt + 0.04), segs=6, bev=0, parent=P)
    K.cyl("column", 0.06, 1.2, _steel(0.25), loc=(0, 0.18, zt + 0.63), segs=12, bev=0.01, parent=P)
    K.cyl("band", 0.065, 0.2, M(MUSTARD, wear=0.3), loc=(0, 0.18, zt + 0.5), segs=12, bev=0.005, parent=P)
    K.cyl("lamp_base", 0.12, 0.08, _dark(), loc=(0, 0.18, zt + 1.27), segs=12, bev=0.01, parent=P)
    K.cyl("lens", 0.1, 0.22, K.mat("red_lens", "#8a2e2a", rough=0.15, metal=0.1, grime=0.2), loc=(0, 0.18, zt + 1.42), segs=12, bev=0.015, parent=P)
    for k in range(2):
        K.cyl(f"lens_ring{k}", 0.105, 0.015, _dark(), loc=(0, 0.18, zt + 1.38 + k * 0.08), segs=12, bev=0, parent=P)
    K.cyl("lamp_cap", 0.13, 0.08, _dark(), loc=(0, 0.18, zt + 1.57), segs=12, r2=0.06, bev=0.01, parent=P)
    K.box("solar_arm", (0.04, 0.3, 0.04), _dark(), loc=(0, 0.33, zt + 0.95), bev=0.004, parent=P)
    K.box("solar", (0.35, 0.3, 0.025), K.mat("solar", "#1f2a36", rough=0.2, metal=0.3, grime=0.3), loc=(0, 0.5, zt + 1.0), rot=(-35, 0, 0), bev=0.004, parent=P)
    K.box("solar_frame", (0.38, 0.33, 0.015), _steel(), loc=(0, 0.505, zt + 0.99), rot=(-35, 0, 0), bev=0.003, parent=P)
    label(P, "J4", (0, 0.18 - 0.07, zt + 0.82), 0.1, M(BONE, wear=0.2), extrude=0.004)
    for k in range(12):
        a = rnd.uniform(0, 6.28)
        K.cyl(f"barnacle{k}", 0.025, 0.025, M(BONE, rough=0.9, wear=0.0, scratches=0.0), loc=(math.cos(a) * 0.17, 0.18 + math.sin(a) * 0.17, rnd.uniform(0.4, 0.7)),
              rot=(0, 90, math.degrees(a)), segs=6, r2=0.012, bev=0, parent=P)
    tube(P, "mooring", [(0.3, -0.15, 1.4), (0.5, -0.4, 1.2), (0.8, -0.8, 0.5), (1.0, -1.1, 0.03), (1.6, -1.2, 0.03)], 0.03, rope_mat(), sides=6, sub=3)
    shell_scatter(P, "pebble", (0, 0), 1.0, 8, rnd)
    return P


def oar_rack(seed=61):
    """A timber rack with oars stood blade up against its rail, their tips in faded colours, one oar dropped on the sand."""
    rnd = random.Random(seed)
    P = _root("oarrack")
    for x in (-1.0, 1.0):
        post(P, f"post{x}", (x, 0.15), 1.75, r=0.07)
    plank(P, "rail", (2.3, 0.08, 0.12), (0, 0.06, 1.45), shade=1)
    plank(P, "foot", (2.2, 0.3, 0.06), (0, -0.25, 0.08), shade=2)
    for x in (-1.0, 1.0):
        bolt(P, (x, 0.0, 1.45), rot=(90, 0, 0))
        K.box(f"foot_block{x}", (0.1, 0.3, 0.12), wood(3), loc=(x, -0.25, 0.04), bev=0.01, parent=P)
    tips = [SEA, BRICK, MUSTARD, BONE, SEA, BRICK]
    for k in range(6):
        x = -0.8 + k * 0.32
        K.cyl(f"peg{k}", 0.018, 0.12, wood(3), loc=(x + 0.16, 0.0, 1.45), rot=(90, 0, 0), segs=6, bev=0, parent=P)
        L = rnd.uniform(2.3, 2.6)
        lean = 12 + rnd.uniform(-2, 2)
        # stood grip down on the foot board, leaning back onto the rail
        _oar(P, f"oar{k}", L, (x, -0.3, 0.12), (rnd.uniform(-8, 8) + 90, -90 + lean, 90), tip=tips[k], shade=k)
    _oar(P, "oar_down", 2.4, (-1.2, -0.95, 0.03), (90, 0, -6), tip=MUSTARD, shade=1)
    return P


def net_float_pile(seed=62):
    """A pile of net floats in faded colony colours and cork, tangled in rope and a heap of old net."""
    rnd = random.Random(seed)
    P = _root("floats")
    lump(P, "net", 0.7, (0, 0.1, 0), (1.45, 1.05, 0.55), netm(), rnd, jitter=0.35, u=12, v=7, flat=0.0)
    lump(P, "net2", 0.4, (-0.75, -0.35, 0), (1.3, 1.0, 0.5), netm(), rnd, jitter=0.35, u=9, v=5, flat=0.0)
    cols = [MUSTARD, BONE, SIGNAL, SEA, MUSTARD, BRICK, BONE]
    k = 0
    for layer, (n, rr, z) in enumerate(((11, 0.85, 0.12), (8, 0.55, 0.32), (4, 0.28, 0.5), (1, 0.0, 0.64))):
        for i in range(n):
            a = 2 * math.pi * i / max(n, 1) + rnd.uniform(-0.25, 0.25) + layer
            d = rr * rnd.uniform(0.8, 1.1)
            x, y = math.cos(a) * d * 1.15, math.sin(a) * d * 0.85 + 0.05
            if rnd.random() < 0.3:
                K.cyl(f"cork{k}", 0.075, 0.16, K.mat("cork", CORK, rough=0.9, grime=0.3, noise_scale=12.0), loc=(x, y, z + 0.02),
                      rot=(rnd.uniform(0, 90), 90, rnd.uniform(0, 180)), segs=10, bev=0.015, parent=P)
            else:
                r = rnd.uniform(0.11, 0.17)
                K.sphere(f"float{k}", r, M(cols[k % len(cols)], wear=0.35, grime=0.35, rough=0.55), loc=(x, y, z + r * 0.75), parent=P)
                K.torus(f"eye{k}", 0.03, 0.008, _dark(), loc=(x, y, z + r * 1.75), rot=(90, 0, rnd.uniform(0, 180)), segs=8, parent=P)
            k += 1
    pts = []
    for i in range(22):
        t = i / 21
        a = t * 9.0
        pts.append((math.cos(a) * (0.95 - 0.5 * t), math.sin(a) * (0.7 - 0.35 * t) + 0.05, 0.08 + 0.55 * t + 0.05 * math.sin(a * 2)))
    tube(P, "rope", pts, 0.02, rope_mat(), sides=5)
    tube(P, "rope_tail", [(0.95, 0.05, 0.08), (1.3, -0.3, 0.03), (1.7, -0.25, 0.03), (1.9, -0.55, 0.03)], 0.02, rope_mat(), sides=5, sub=3)
    return P


def ship_rope_coil(seed=63):
    """A huge coil of mooring hawser: two layers of turns on a pallet, the end flaked in a spiral on top and run off to an
    eye splice on the ground."""
    rnd = random.Random(seed)
    P = _root("hawser")
    _pallet(P, "pallet", (0, 0, 0), (0, 0, 8), rnd, L=1.6, W=1.6)
    r = 0.07
    z0 = 0.14 + r
    rope = K.mat("hawser", "#b59b6a", rough=0.95, grime=0.3, noise_scale=20.0)
    turns, per = 5, 20
    for name, R in (("inner", 0.42), ("outer", 0.58)):
        pts = []
        for i in range(turns * per + 1):
            a = 2 * math.pi * i / per
            pts.append((math.cos(a) * R, math.sin(a) * R, z0 + 2 * r * i / per))
        tube(P, name, pts, r, rope, sides=6)
    zt = z0 + 2 * r * turns + r * 0.4
    pts = []
    for i in range(int(2.2 * per) + 1):
        a = 2 * math.pi * i / per
        R = 0.6 - 0.45 * i / (2.2 * per)
        pts.append((math.cos(a) * R, math.sin(a) * R, zt))
    tube(P, "flake", pts, r, rope, sides=6)
    tail = [(0.6, 0.0, z0 + 2 * r * turns), (0.78, -0.15, 0.6), (0.9, -0.4, 0.2), (1.0, -0.7, r), (1.5, -0.95, r), (2.0, -0.7, r)]
    tube(P, "tail", tail, r, rope, sides=6, sub=3)
    K.torus("eye", 0.16, r * 0.9, rope, loc=(2.18, -0.6, r), rot=(0, 0, 0), segs=18, parent=P).scale = (1.4, 0.8, 1)
    K.cyl("splice", r * 1.25, 0.3, rope, loc=(2.0, -0.7, r), rot=(0, 90, 40), segs=8, bev=0.01, parent=P)
    K.cyl("whipping", r * 1.15, 0.08, M(SIGNAL, rough=0.7), loc=(1.88, -0.78, r), rot=(0, 90, 40), segs=8, bev=0, parent=P)
    return P


def harbour_crane_small(seed=64):
    """A small colony harbour crane: a framed steel-blue pedestal on a concrete pad, a slewing deck with a bone machinery
    house and counterweight, and a mustard lattice jib with its hook hanging."""
    rnd = random.Random(seed)
    P = _root("crane")
    conc = K.mat("concrete", CONCRETE, rough=0.9, wear=0.4, grime=0.6, cracks=0.2, moss=0.3)
    K.box("pad", (2.6, 2.6, 0.4), conc, loc=(0, 0, 0.15), bev=0.05, parent=P)
    steel = _steel(0.3)
    FR.framed_box(P, "pedestal", (0, 0, 0.35 + 1.25), (1.1, 1.1, 2.5), M(BONE, wear=0.45, grime=0.5), steel, fw=0.14, screws=M(ZINC, metal=0.6))
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.cyl("anchor_nut", 0.04, 0.06, M(ZINC, metal=0.6), loc=(sx * 0.7, sy * 0.7, 0.38), segs=6, bev=0, parent=P)
    FR.hazard(P, (0, -0.57, 0.65), 1.0, 0.25, M(MUSTARD, wear=0.4), _dark(), n=6)
    label(P, "HC-2", (0, -0.565, 2.4), 0.22, _dark(), extrude=0.005).data.resolution_u = 2
    # ladder up the back of the pedestal
    for dx in (-0.2, 0.2):
        K.box(f"ladder{dx}", (0.04, 0.04, 2.6), M(ZINC, metal=0.5, wear=0.3), loc=(dx, 0.62, 1.6), bev=0.005, parent=P)
    for k in range(6):
        K.cyl(f"ladder_rung{k}", 0.014, 0.4, M(ZINC, metal=0.5, wear=0.3), loc=(0, 0.62, 0.6 + k * 0.4), rot=(0, 90, 0), segs=6, bev=0, parent=P)
    K.cyl("slew_ring", 0.75, 0.2, _dark(), loc=(0, 0, 2.95), segs=24, bev=0.02, parent=P)
    D = _grp(P, "deck", (0, 0, 3.05), (0, 0, -25))
    K.box("deck_plate", (2.6, 1.6, 0.14), steel, loc=(0.2, 0, 0.07), bev=0.02, parent=D)
    FR.framed_box(D, "house", (0.75, 0, 0.14 + 0.65), (1.4, 1.4, 1.3), M(BONE, wear=0.45, grime=0.45), steel, fw=0.1)
    FR.vent(D, (0.75, -0.73, 0.85), 0.7, 0.45, steel, _dark())
    K.box("counterweight", (0.5, 1.5, 0.9), _dark(), loc=(1.65, 0, 0.14 + 0.45), bev=0.03, parent=D)
    FR.hazard(D, (1.65, -0.77, 0.45), 1.3, 0.2, M(MUSTARD, wear=0.4), _dark(), n=6, axis="y")
    # cab: a small glazed box beside the jib foot
    K.box("cab", (0.8, 0.7, 1.0), M(BONE, wear=0.45, grime=0.4), loc=(-0.45, -0.4, 0.14 + 0.5), bev=0.03, parent=D)
    K.box("cab_glass", (0.6, 0.03, 0.45), glass(), loc=(-0.45, -0.76, 0.85), bev=0.004, parent=D)
    K.box("cab_glass_s", (0.03, 0.5, 0.45), glass(), loc=(-0.86, -0.4, 0.85), bev=0.004, parent=D)
    K.box("cab_roof", (0.9, 0.8, 0.06), steel, loc=(-0.45, -0.4, 1.17), bev=0.01, parent=D)
    K.box("winch", (0.5, 0.6, 0.35), _dark(), loc=(0.0, 0.35, 0.35), bev=0.02, parent=D)
    K.cyl("winch_drum", 0.15, 0.55, M(RUST, metal=0.5, wear=0.3), loc=(0.0, 0.35, 0.6), rot=(90, 0, 0), segs=12, bev=0.01, parent=D)
    # the jib: a tapering four-chord lattice from its foot pin up and out to -x
    jm = M(MUSTARD, metal=0.3, wear=0.3, grime=0.35)
    foot = Vector((-0.6, 0.25, 0.45))
    ang = math.radians(44)
    Lj = 5.6
    ax = Vector((-math.cos(ang), 0, math.sin(ang)))
    side = Vector((0, 1, 0))
    up = ax.cross(side).normalized() * -1

    def corner(t, i, j):
        w = 0.32 * (1 - t) + 0.14 * t
        return foot + ax * (Lj * t) + side * (i * w) + up * (j * w)

    cs = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for i, j in cs:
        beam(D, f"chord{i}{j}", corner(0, i, j), corner(1, i, j), 0.07, 0.07, jm, bev=0.0)
    nb = 7
    for k in range(nb):
        t0, t1 = k / nb, (k + 1) / nb
        for q in range(4):
            a, b = cs[q], cs[(q + 1) % 4]
            p0, p1 = (corner(t0, *a), corner(t1, *b)) if k % 2 == 0 else (corner(t0, *b), corner(t1, *a))
            beam(D, f"lace{k}{q}", p0, p1, 0.04, 0.04, jm, bev=0.0)
        if k:
            for q in range(4):
                a, b = cs[q], cs[(q + 1) % 4]
                beam(D, f"strut{k}{q}", corner(t0, *a), corner(t0, *b), 0.04, 0.04, jm, bev=0.0)
    tip = corner(1, 0, 0)
    K.cyl("sheave", 0.16, 0.12, _dark(), loc=tip, rot=(90, 0, 0), segs=14, bev=0.01, parent=D)
    K.cyl("foot_pin", 0.06, 0.8, _dark(), loc=foot, rot=(90, 0, 0), segs=10, bev=0.005, parent=D)
    # luffing ties from the house top to the jib head
    for s in (-1, 1):
        rod(D, f"tie{s}", (0.2, s * 0.3, 1.6), tip + Vector((0, s * 0.1, 0.1)), 0.018, _dark(), segs=6)
    K.box("a_frame", (0.12, 0.9, 0.12), steel, loc=(0.2, 0, 1.6), bev=0.01, parent=D)
    for s in (-1, 1):
        beam(D, f"a_leg{s}", (0.45, s * 0.45, 1.44), (0.2, s * 0.4, 1.6), 0.08, 0.08, steel, bev=0.0)
    # hoist rope and hook block
    hz = 0.9
    hook = Vector((tip.x, tip.y, hz))
    rod(D, "hoist", tip + Vector((0, 0, -0.16)), hook + Vector((0, 0, 0.25)), 0.012, _dark(), segs=6)
    K.box("block", (0.22, 0.12, 0.3), M(MUSTARD, metal=0.3, wear=0.4), loc=hook + Vector((0, 0, 0.12)), bev=0.02, parent=D)
    K.torus("hook", 0.09, 0.025, _dark(), loc=hook + Vector((0, 0, -0.12)), rot=(90, 0, 0), arc=270, segs=14, parent=D)
    for k in range(3):
        a = rnd.uniform(0, 6.28)
        rock(P, f"foot{k}", rnd.uniform(0.15, 0.25), 0.2, (math.cos(a) * 1.55, math.sin(a) * 1.55, 0.04), rnd, mat=shore_rock())
    return P


def shipping_crate_washed(seed=65):
    """A colony slat crate the tide brought in: half sunk in the sand at a slant, stencilled, a slat torn off its top,
    weed draped over it."""
    rnd = random.Random(seed)
    P = _root("crate")
    C = _grp(P, "crate", (0, 0, 0), (7, -10, 22))
    w, d, h = 1.2, 1.0, 0.9
    fr = drift(1)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("post", (0.08, 0.08, h), fr, loc=(sx * (w / 2 - 0.04), sy * (d / 2 - 0.04), h / 2), bev=0.01, parent=C)
            K.box("rail_x", (w, 0.08, 0.08), fr, loc=(0, sy * (d / 2 - 0.04), (h - 0.08) * (sx > 0) + 0.04), bev=0.01, parent=C)
        for sz in (0, 1):
            K.box("rail_y", (0.08, d, 0.08), fr, loc=(sx * (w / 2 - 0.04), 0, (h - 0.08) * sz + 0.04), bev=0.01, parent=C)
    K.box("inside", (w - 0.1, d - 0.1, h - 0.1), K.mat("crate_inside", "#2e2822", rough=0.95), loc=(0, 0, h / 2), bev=0, parent=C)
    sl = drift(0)
    for k in range(4):
        z = 0.14 + k * 0.205
        for sy in (-1, 1):
            K.box(f"slat_y{k}{sy}", (w - 0.16, 0.03, 0.17), drift(k), loc=(0, sy * (d / 2 - 0.01), z), bev=0.006, parent=C)
        for sx in (-1, 1):
            K.box(f"slat_x{k}{sx}", (0.03, d - 0.16, 0.17), drift(k + 1), loc=(sx * (w / 2 - 0.01), 0, z), bev=0.006, parent=C)
    for k in range(5):
        if k == 2:
            continue
        y = -d / 2 + 0.14 + k * (d - 0.28) / 4
        K.box(f"slat_top{k}", (w - 0.16, 0.16, 0.03), sl if k % 2 else drift(2), loc=(0, y, h - 0.01), bev=0.006, parent=C)
    K.box("slat_torn", (0.6, 0.16, 0.03), drift(2), loc=(-0.25, 0.0, h - 0.01), rot=(0, -12, 4), bev=0.006, parent=C)
    label(C, "COLONY", (-0.12, -d / 2 - 0.028, 0.55), 0.14, M(GRAPHITE, wear=0.3), extrude=0.004).data.resolution_u = 2
    label(C, "C-17", (-0.12, -d / 2 - 0.028, 0.34), 0.12, M(GRAPHITE, wear=0.3), extrude=0.004).data.resolution_u = 2
    for sx in (-1, 1):
        for z in (0.04, h - 0.04):
            bolt(C, (sx * (w / 2 - 0.04), -d / 2 - 0.005, z))
    # weed thrown over a corner: a mat on the lid and fronds hanging down the front and side
    lump(C, "weed_mat", 1.0, (0.3, -0.1, h + 0.015), (0.3, 0.38, 0.025), kelp(), rnd, jitter=0.45, u=8, v=4)
    lump(C, "weed_front", 1.0, (0.33, -d / 2 - 0.02, h - 0.12), (0.2, 0.02, 0.14), kelp(), rnd, jitter=0.45, u=8, v=4)
    lump(C, "weed_side", 1.0, (w / 2 + 0.02, -0.25, h - 0.09), (0.02, 0.16, 0.1), kelp(), rnd, jitter=0.45, u=8, v=4)
    _settle(C, sink=0.22)
    lump(P, "drift", 0.8, (0.35, 0.45, 0), (1.3, 1.0, 0.35), sand(), rnd, jitter=0.08, u=12, v=6, flat=0.0)
    kelp_clump(P, "wrack", (-0.9, -0.6), rnd, n=4, spread=0.25)
    return P


PROPS = {
    "sand_dune_grass": sand_dune_grass,
    "rock_pool": rock_pool,
    "sea_stack_small": sea_stack_small,
    "dock_pier_section": dock_pier_section,
    "rowboat_wreck": rowboat_wreck,
    "fishing_boat_wreck": fishing_boat_wreck,
    "buoy_beached": buoy_beached,
    "lobster_traps": lobster_traps,
    "fishing_net_rack": fishing_net_rack,
    "lighthouse_small": lighthouse_small,
    "boathouse_shack": boathouse_shack,
    "life_ring_post": life_ring_post,
    "anchor_rusty": anchor_rusty,
    "mooring_post": mooring_post,
    "beach_hut": beach_hut,
    "deck_chair": deck_chair,
    "pallet_raft": pallet_raft,
    "crab_pots": crab_pots,
    "kayak_wreck": kayak_wreck,
    "jetty_light": jetty_light,
    "oar_rack": oar_rack,
    "net_float_pile": net_float_pile,
    "ship_rope_coil": ship_rope_coil,
    "harbour_crane_small": harbour_crane_small,
    "shipping_crate_washed": shipping_crate_washed,
}
